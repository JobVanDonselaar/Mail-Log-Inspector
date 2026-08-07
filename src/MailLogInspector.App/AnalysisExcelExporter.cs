using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using MailLogInspector.Core;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using static MailLogInspector.App.ExcelReportKit;

namespace MailLogInspector.App;

/// <summary>De filters waarmee de analyse is uitgevoerd. Ze staan in de kop van elk werkblad.</summary>
public sealed record AnalysisReportContext(
    DateTime FromDate,
    DateTime ThroughDate,
    string? SenderFilter,
    string? RecipientFilter,
    int TopDomainLimit)
{
    public string DescribePeriod() =>
        $"{FromDate:dd-MM-yyyy} t/m {ThroughDate:dd-MM-yyyy}";

    /// <summary>Beschrijft de actieve filters, zodat het rapport zonder de app te lezen is.</summary>
    public string DescribeFilters()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(SenderFilter))
        {
            parts.Add($"afzender bevat '{SenderFilter.Trim()}'");
        }
        if (!string.IsNullOrWhiteSpace(RecipientFilter))
        {
            parts.Add($"ontvanger bevat '{RecipientFilter.Trim()}'");
        }
        return parts.Count == 0 ? "geen extra filters" : string.Join(" en ", parts);
    }
}

/// <summary>
/// Schrijft het analyserapport als werkmap met twee werkbladen: één vanuit het perspectief van de
/// verzendende domeinen en één vanuit de ontvangende domeinen. Beide bladen staan op zichzelf,
/// met dezelfde kerncijfers, een grafiek en de onderliggende tabellen.
/// </summary>
public static class AnalysisExcelExporter
{
    private const int SheetColumnCount = 9;
    private const int TableColumnCount = 4;
    private const int RightTableColumn = 6;
    private const uint ChartTopRow = 11;
    private const uint ChartBottomRow = 29;
    private const uint FirstTableRow = 31;
    private const int MaxChartCategories = 12;

    public static void Export(
        string path,
        MailLogInspectorAnalysisSummary summary,
        AnalysisReportContext context)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using SpreadsheetDocument document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        document.PackageProperties.Title = "Mail Log Inspector - Analyserapport";
        document.PackageProperties.Subject =
            $"Aflevering en probleemoorzaken per domein, periode {context.DescribePeriod()}";
        document.PackageProperties.Creator = "Mail Log Inspector";
        document.PackageProperties.Created = DateTime.Now;

        WorkbookPart workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new S.Workbook();
        AddWorkbookStyles(workbookPart);
        S.Sheets sheets = workbookPart.Workbook.AppendChild(new S.Sheets());

        AddSenderSheet(workbookPart, sheets, summary, context, sheetId: 1);
        AddRecipientSheet(workbookPart, sheets, summary, context, sheetId: 2);

        workbookPart.Workbook.Save();
    }

    // ------------------------------------------------------------------ afzenders

    private static void AddSenderSheet(
        WorkbookPart workbookPart,
        S.Sheets sheets,
        MailLogInspectorAnalysisSummary summary,
        AnalysisReportContext context,
        uint sheetId)
    {
        const string sheetName = "Afzenders";
        var sheetData = new S.SheetData();
        WorksheetPart worksheetPart = CreateSheet(workbookPart, sheets, sheetData, sheetName, sheetId);
        var merges = new List<string>();

        WriteHeader(
            sheetData,
            merges,
            "Mail Log Inspector - Analyse verzendende domeinen",
            context,
            "Dit werkblad toont welke afzenderdomeinen het meeste verkeer veroorzaken en waar de aflevering achterblijft.");
        WriteKpiBlock(sheetData, merges, summary);

        merges.Add($"A{ChartTopRow}:{ColumnName(SheetColumnCount)}{ChartTopRow}");
        sheetData.Append(StyledSpanRow(ChartTopRow, 1, SheetColumnCount,
            "Beeld van de verzendende domeinen", StyleSection, 24));

        uint row = FirstTableRow;
        (TableBlock volume, TableBlock rate) = WriteBreakdownPair(
            sheetData,
            merges,
            ref row,
            "Afzenderdomeinen met de meeste problemen",
            "Afzenderdomeinen met het hoogste probleempercentage",
            "Domein",
            summary.SenderProblemVolumeRows,
            summary.SenderHighestProblemRateRows);

        FinishSheet(worksheetPart, merges);

        AddDomainCharts(
            worksheetPart,
            sheetName,
            volume,
            rate,
            volumeTitle: "Problemen per afzenderdomein",
            rateTitle: "Hoogste probleempercentage per afzenderdomein",
            rateSelector: row => row.ProblemRate,
            rateColor: ChartRed,
            volumeColor: ChartOrange,
            volumeSelector: row => row.ProblemCount);
    }

    // ----------------------------------------------------------------- ontvangers

    private static void AddRecipientSheet(
        WorkbookPart workbookPart,
        S.Sheets sheets,
        MailLogInspectorAnalysisSummary summary,
        AnalysisReportContext context,
        uint sheetId)
    {
        const string sheetName = "Ontvangers";
        var sheetData = new S.SheetData();
        WorksheetPart worksheetPart = CreateSheet(workbookPart, sheets, sheetData, sheetName, sheetId);
        var merges = new List<string>();

        WriteHeader(
            sheetData,
            merges,
            "Mail Log Inspector - Analyse ontvangende domeinen",
            context,
            "Dit werkblad toont bij welke ontvangende domeinen berichten blijven steken en welke meldingen de mailservers teruggeven.");
        WriteKpiBlock(sheetData, merges, summary);

        merges.Add($"A{ChartTopRow}:{ColumnName(SheetColumnCount)}{ChartTopRow}");
        sheetData.Append(StyledSpanRow(ChartTopRow, 1, SheetColumnCount,
            "Beeld van de ontvangende domeinen", StyleSection, 24));

        uint row = FirstTableRow;
        (TableBlock volume, TableBlock highest) = WriteBreakdownPair(
            sheetData,
            merges,
            ref row,
            "Ontvangerdomeinen met de meeste problemen",
            "Ontvangerdomeinen met het hoogste probleempercentage",
            "Domein",
            summary.RecipientProblemVolumeRows,
            summary.RecipientHighestProblemRateRows);

        WriteValueMeaningTable(
            sheetData,
            merges,
            ref row,
            "SMTP-responsen",
            "Code",
            summary.TopResponseCodes);

        WriteValueMeaningTable(
            sheetData,
            merges,
            ref row,
            "Belangrijkste bounce-oorzaken",
            "Oorzaak",
            summary.TopBounceCauses);

        FinishSheet(worksheetPart, merges);

        AddDomainCharts(
            worksheetPart,
            sheetName,
            volume,
            highest,
            volumeTitle: "Problemen per ontvangerdomein",
            rateTitle: "Hoogste probleempercentage per ontvangerdomein",
            rateSelector: row => row.ProblemRate,
            rateColor: ChartRed,
            volumeColor: ChartOrange,
            volumeSelector: row => row.ProblemCount);
    }

    // ------------------------------------------------------------ gedeelde opbouw

    private static WorksheetPart CreateSheet(
        WorkbookPart workbookPart,
        S.Sheets sheets,
        S.SheetData sheetData,
        string name,
        uint sheetId)
    {
        WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new S.Worksheet(
            FitToPageProperties(),
            DashboardView(),
            new S.SheetFormatProperties { DefaultRowHeight = 18 },
            ReportColumns(),
            sheetData);
        sheets.Append(new S.Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = name
        });
        return worksheetPart;
    }

    private static void FinishSheet(WorksheetPart worksheetPart, List<string> merges)
    {
        worksheetPart.Worksheet.Append(
            MergeRanges(merges.ToArray()),
            ReportPageMargins(),
            LandscapePageSetup());
    }

    private static void WriteHeader(
        S.SheetData sheetData,
        List<string> merges,
        string title,
        AnalysisReportContext context,
        string explanation)
    {
        string span = ColumnName(SheetColumnCount);
        merges.Add($"A1:{span}1");
        merges.Add($"A2:{span}2");
        merges.Add($"A3:{span}3");

        sheetData.Append(StyledSpanRow(1, 1, SheetColumnCount, title, StyleTitle, 30));
        sheetData.Append(StyledSpanRow(2, 1, SheetColumnCount,
            $"Periode: {context.DescribePeriod()} | Selectie: {context.DescribeFilters()} | Ranglijsten tonen de top {context.TopDomainLimit}",
            StyleSubtitle, 28));
        sheetData.Append(StyledSpanRow(3, 1, SheetColumnCount,
            $"{explanation} Gegenereerd: {DateTime.Now:dd-MM-yyyy HH:mm}.",
            StyleNote, 30));
        sheetData.Append(CreateSparseRow(4));
    }

    private static void WriteKpiBlock(
        S.SheetData sheetData,
        List<string> merges,
        MailLogInspectorAnalysisSummary summary)
    {
        merges.Add($"A5:{ColumnName(SheetColumnCount)}5");
        sheetData.Append(StyledSpanRow(5, 1, SheetColumnCount, "Kerncijfers geselecteerde periode", StyleSection, 24));

        foreach (uint kpiRow in new uint[] { 6, 7, 8, 9 })
        {
            foreach (string column in new[] { "A", "D", "G" })
            {
                merges.Add($"{column}{kpiRow}:{ColumnName(column[0] - 'A' + 3)}{kpiRow}");
            }
        }

        int problems = summary.UnderwayCount + summary.BounceCount;

        sheetData.Append(KpiRow(6, StyleKpiLabel,
            ("A", "Geaccepteerd"), ("D", "Afgeleverd"), ("G", "Afleverratio")));
        sheetData.Append(CreateSparseRow(7, 32,
            NumberCell("A7", summary.TotalCount, StyleKpiBlue),
            NumberCell("D7", summary.DeliveredCount, StyleKpiGreen),
            NumberCell("G7", Ratio(summary.DeliveredCount, summary.TotalCount), StyleKpiPercent)));

        sheetData.Append(KpiRow(8, StyleKpiLabel,
            ("A", "Onderweg"), ("D", "Bounced"), ("G", "Probleemratio")));
        sheetData.Append(CreateSparseRow(9, 32,
            NumberCell("A9", summary.UnderwayCount, StyleKpiOrange),
            NumberCell("D9", summary.BounceCount, StyleKpiRed),
            NumberCell("G9", Ratio(problems, summary.TotalCount), StyleKpiPercent)));

        sheetData.Append(CreateSparseRow(10));
    }

    /// <summary>
    /// Schrijft twee ranglijsten naast elkaar, met dezelfde kolommen als de Analyse-tab in de app.
    /// Beide tabellen delen hun rijen, zodat ze op één scherm naast elkaar te lezen zijn.
    /// </summary>
    private static (TableBlock Left, TableBlock Right) WriteBreakdownPair(
        S.SheetData sheetData,
        List<string> merges,
        ref uint row,
        string leftTitle,
        string rightTitle,
        string keyHeader,
        IReadOnlyList<MailLogInspectorBreakdownRow> leftRows,
        IReadOnlyList<MailLogInspectorBreakdownRow> rightRows)
    {
        merges.Add($"A{row}:{ColumnName(TableColumnCount)}{row}");
        merges.Add($"{ColumnName(RightTableColumn)}{row}:{ColumnName(SheetColumnCount)}{row}");
        sheetData.Append(CreateSparseRow(row, 22,
            SpanCells(row, 1, TableColumnCount, leftTitle, StyleSection)
                .Concat(SpanCells(row, RightTableColumn, SheetColumnCount, rightTitle, StyleSection))
                .ToArray()));
        row++;
        uint headerRow = row;
        string[] headers = [keyHeader, "Totaal", "Problemen", "% probleem"];
        sheetData.Append(CreateSparseRow(headerRow,
            headers.Select((header, index) => StringCell($"{ColumnName(index + 1)}{headerRow}", header, StyleTableHeader))
                .Concat(headers.Select((header, index) =>
                    StringCell($"{ColumnName(RightTableColumn + index)}{headerRow}", header, StyleTableHeader)))
                .ToArray()));
        row++;

        uint firstDataRow = row;
        int dataRowCount = Math.Max(leftRows.Count, rightRows.Count);
        for (int index = 0; index < dataRowCount; index++)
        {
            uint bodyStyle = row % 2 == 1 ? StyleBodyAlternate : StyleBody;
            uint numberStyle = row % 2 == 1 ? StyleNumberAlternate : StyleNumber;
            sheetData.Append(CreateSparseRow(row,
                BreakdownCells(row, 1, index < leftRows.Count ? leftRows[index] : null, bodyStyle, numberStyle)
                    .Concat(BreakdownCells(row, RightTableColumn, index < rightRows.Count ? rightRows[index] : null, bodyStyle, numberStyle))
                    .ToArray()));
            row++;
        }

        if (dataRowCount == 0)
        {
            sheetData.Append(CreateSparseRow(row,
                StringCell($"A{row}", "Geen resultaten in deze selectie.", StyleBody),
                StringCell($"{ColumnName(RightTableColumn)}{row}", "Geen resultaten in deze selectie.", StyleBody)));
            row++;
        }

        uint lastDataRow = row - 1;
        row++;
        return (
            new TableBlock(headerRow, firstDataRow, lastDataRow, leftRows),
            new TableBlock(headerRow, firstDataRow, lastDataRow, rightRows));
    }

    /// <summary>Vier cellen van één ranglijstrij, of lege cellen als de andere lijst langer is.</summary>
    private static IEnumerable<S.Cell> BreakdownCells(
        uint row,
        int firstColumn,
        MailLogInspectorBreakdownRow? entry,
        uint bodyStyle,
        uint numberStyle)
    {
        string key = ColumnName(firstColumn);
        string total = ColumnName(firstColumn + 1);
        string problems = ColumnName(firstColumn + 2);
        string rate = ColumnName(firstColumn + 3);

        if (entry is null)
        {
            yield return StyledBlank($"{key}{row}", bodyStyle);
            yield return StyledBlank($"{total}{row}", numberStyle);
            yield return StyledBlank($"{problems}{row}", numberStyle);
            yield return StyledBlank($"{rate}{row}", StylePercent);
            yield break;
        }

        yield return StringCell($"{key}{row}", entry.Key, bodyStyle);
        yield return NumberCell($"{total}{row}", entry.Total, numberStyle);
        yield return NumberCell($"{problems}{row}", entry.ProblemCount, numberStyle);
        yield return NumberCell($"{rate}{row}", entry.ProblemRate, StylePercent);
    }

    /// <summary>Een gestileerde titelcel met opvulcellen, zodat de samengevoegde band doorloopt.</summary>
    private static IEnumerable<S.Cell> SpanCells(uint row, int firstColumn, int lastColumn, string text, uint style)
    {
        yield return StringCell($"{ColumnName(firstColumn)}{row}", text, style);
        for (int column = firstColumn + 1; column <= lastColumn; column++)
        {
            yield return StyledBlank($"{ColumnName(column)}{row}", style);
        }
    }

    private static void WriteValueMeaningTable(
        S.SheetData sheetData,
        List<string> merges,
        ref uint row,
        string title,
        string valueHeader,
        IReadOnlyList<MailLogInspectorValueMeaningCount> rows)
    {
        merges.Add($"A{row}:{ColumnName(SheetColumnCount)}{row}");
        sheetData.Append(StyledSpanRow(row, 1, SheetColumnCount, title, StyleSection, 22));
        row++;

        merges.Add($"D{row}:{ColumnName(SheetColumnCount)}{row}");
        sheetData.Append(CreateSparseRow(row,
            new[]
            {
                StringCell($"A{row}", valueHeader, StyleTableHeader),
                StringCell($"B{row}", "Aantal", StyleTableHeader),
                StringCell($"C{row}", "% van totaal", StyleTableHeader)
            }.Concat(SpanCells(row, 4, SheetColumnCount, "Omschrijving", StyleTableHeader)).ToArray()));
        row++;

        int total = rows.Sum(entry => entry.Count);
        foreach (MailLogInspectorValueMeaningCount entry in rows)
        {
            uint bodyStyle = row % 2 == 1 ? StyleBodyAlternate : StyleBody;
            uint numberStyle = row % 2 == 1 ? StyleNumberAlternate : StyleNumber;
            merges.Add($"D{row}:{ColumnName(SheetColumnCount)}{row}");
            sheetData.Append(CreateSparseRow(row,
                new[]
                {
                    StringCell($"A{row}", entry.Value, bodyStyle),
                    NumberCell($"B{row}", entry.Count, numberStyle),
                    NumberCell($"C{row}", Ratio(entry.Count, total), StylePercent)
                }.Concat(SpanCells(row, 4, SheetColumnCount, entry.Meaning, bodyStyle)).ToArray()));
            row++;
        }

        if (rows.Count == 0)
        {
            sheetData.Append(CreateSparseRow(row,
                StringCell($"A{row}", "Geen meldingen in deze selectie.", StyleBody)));
            row++;
        }

        row++;
    }

    private static void AddDomainCharts(
        WorksheetPart worksheetPart,
        string sheetName,
        TableBlock volumeTable,
        TableBlock rateTable,
        string volumeTitle,
        string rateTitle,
        Func<MailLogInspectorBreakdownRow, double> rateSelector,
        string rateColor,
        string volumeColor,
        Func<MailLogInspectorBreakdownRow, double>? volumeSelector = null)
    {
        string volumeKeyColumn = ColumnName(1);
        string volumeValueColumn = ColumnName(3);
        string rateKeyColumn = ColumnName(RightTableColumn);
        string rateValueColumn = ColumnName(RightTableColumn + 3);
        IReadOnlyList<MailLogInspectorBreakdownRow> volumeRows = volumeTable.Rows.Take(MaxChartCategories).ToArray();
        IReadOnlyList<MailLogInspectorBreakdownRow> rateRows = rateTable.Rows.Take(MaxChartCategories).ToArray();
        if (volumeRows.Count == 0 && rateRows.Count == 0)
        {
            return;
        }

        DrawingsPart drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
        var drawing = new Xdr.WorksheetDrawing();
        drawingsPart.WorksheetDrawing = drawing;
        worksheetPart.Worksheet.Append(new S.Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) });

        uint drawingId = 1;
        if (volumeRows.Count > 0)
        {
            double[] values = volumeRows
                .Select(volumeSelector ?? (row => row.Total))
                .ToArray();
            uint lastRow = volumeTable.FirstDataRow + (uint)volumeRows.Count - 1;
            ChartPart chartPart = drawingsPart.AddNewPart<ChartPart>();
            chartPart.ChartSpace = CreateBarChart(
                C.BarDirectionValues.Bar,
                volumeTitle,
                volumeColor,
                $"'{sheetName}'!${volumeKeyColumn}${volumeTable.FirstDataRow}:${volumeKeyColumn}${lastRow}",
                $"'{sheetName}'!${volumeValueColumn}${volumeTable.FirstDataRow}:${volumeValueColumn}${lastRow}",
                volumeRows.Select(row => row.Key).ToArray(),
                values,
                categoryAxisId: 111111111,
                valueAxisId: 222222222,
                showValues: true,
                maximumValue: RoundChartMaximum(values.Length == 0 ? 0 : values.Max()));
            drawing.Append(CreateAnchor(
                drawingsPart.GetIdOfPart(chartPart),
                drawingId++,
                volumeTitle,
                fromColumn: 0,
                fromRow: (int)ChartTopRow,
                toColumn: TableColumnCount,
                toRow: (int)ChartBottomRow));
        }

        if (rateRows.Count > 0)
        {
            double[] values = rateRows.Select(rateSelector).ToArray();
            uint lastRow = rateTable.FirstDataRow + (uint)rateRows.Count - 1;
            ChartPart chartPart = drawingsPart.AddNewPart<ChartPart>();
            chartPart.ChartSpace = CreateBarChart(
                C.BarDirectionValues.Bar,
                rateTitle,
                rateColor,
                $"'{sheetName}'!${rateKeyColumn}${rateTable.FirstDataRow}:${rateKeyColumn}${lastRow}",
                $"'{sheetName}'!${rateValueColumn}${rateTable.FirstDataRow}:${rateValueColumn}${lastRow}",
                rateRows.Select(row => row.Key).ToArray(),
                values,
                categoryAxisId: 333333333,
                valueAxisId: 444444444,
                numberFormat: "0.0%",
                showValues: true,
                maximumValue: values.Length == 0 ? 0 : Math.Min(1.0, RoundChartMaximum(values.Max())));
            drawing.Append(CreateAnchor(
                drawingsPart.GetIdOfPart(chartPart),
                drawingId,
                rateTitle,
                fromColumn: RightTableColumn - 1,
                fromRow: (int)ChartTopRow,
                toColumn: SheetColumnCount,
                toRow: (int)ChartBottomRow));
        }
    }

    private static S.Columns ReportColumns() =>
        new(
            Column(1, 34), Column(2, 12), Column(3, 13), Column(4, 13), Column(5, 4),
            Column(6, 34), Column(7, 12), Column(8, 13), Column(9, 13));

    private static double Ratio(int part, int whole) => whole <= 0 ? 0.0 : part / (double)whole;

    private static string NextColumn(string column) =>
        ColumnName(column[0] - 'A' + 2);

    /// <summary>Waar een geschreven tabel staat, zodat een grafiek naar de juiste cellen kan wijzen.</summary>
    private sealed record TableBlock(
        uint HeaderRow,
        uint FirstDataRow,
        uint LastDataRow,
        IReadOnlyList<MailLogInspectorBreakdownRow> Rows);
}
