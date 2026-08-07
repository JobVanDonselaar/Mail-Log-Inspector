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
    private const int SheetColumnCount = 12;
    private const int TableColumnCount = 8;
    private const uint ChartTopRow = 9;
    private const uint ChartBottomRow = 27;
    private const uint FirstTableRow = 29;
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
        TableBlock volume = WriteBreakdownTable(
            sheetData,
            merges,
            ref row,
            "Afzenderdomeinen op volume",
            "Domein",
            summary.SenderVolumeRows);

        TableBlock lowest = WriteBreakdownTable(
            sheetData,
            merges,
            ref row,
            "Afzenderdomeinen met het laagste afleverpercentage",
            "Domein",
            summary.SenderLowestSuccessRows);

        FinishSheet(worksheetPart, merges, volume);

        AddDomainCharts(
            worksheetPart,
            sheetName,
            volume,
            lowest,
            volumeTitle: "Verzonden volume per afzenderdomein",
            rateTitle: "Laagste afleverpercentage per afzenderdomein",
            rateSelector: row => row.SuccessRate,
            rateColumn: "H",
            rateColor: ChartGreen,
            volumeColor: ChartBlue);
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
        TableBlock volume = WriteBreakdownTable(
            sheetData,
            merges,
            ref row,
            "Ontvangerdomeinen met de meeste problemen",
            "Domein",
            summary.RecipientProblemVolumeRows);

        TableBlock highest = WriteBreakdownTable(
            sheetData,
            merges,
            ref row,
            "Ontvangerdomeinen met het hoogste probleempercentage",
            "Domein",
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

        FinishSheet(worksheetPart, merges, volume);

        AddDomainCharts(
            worksheetPart,
            sheetName,
            volume,
            highest,
            volumeTitle: "Problemen per ontvangerdomein",
            rateTitle: "Hoogste probleempercentage per ontvangerdomein",
            rateSelector: row => row.ProblemRate,
            rateColumn: "G",
            rateColor: ChartRed,
            volumeColor: ChartOrange,
            volumeSelector: row => row.ProblemCount,
            volumeColumn: "F");
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

    private static void FinishSheet(WorksheetPart worksheetPart, List<string> merges, TableBlock firstTable)
    {
        worksheetPart.Worksheet.Append(
            new S.AutoFilter { Reference = $"A{firstTable.HeaderRow}:{ColumnName(TableColumnCount)}{firstTable.LastDataRow}" },
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

        foreach (string column in new[] { "A", "C", "E", "G", "I", "K" })
        {
            merges.Add($"{column}6:{NextColumn(column)}6");
            merges.Add($"{column}7:{NextColumn(column)}7");
        }

        sheetData.Append(KpiRow(6, StyleKpiLabel,
            ("A", "Geaccepteerd"), ("C", "Afgeleverd"), ("E", "Afleverratio"),
            ("G", "Onderweg"), ("I", "Bounced"), ("K", "Probleemratio")));

        int problems = summary.UnderwayCount + summary.BounceCount;
        sheetData.Append(CreateSparseRow(7, 32,
            NumberCell("A7", summary.TotalCount, StyleKpiBlue),
            NumberCell("C7", summary.DeliveredCount, StyleKpiGreen),
            NumberCell("E7", Ratio(summary.DeliveredCount, summary.TotalCount), StyleKpiPercent),
            NumberCell("G7", summary.UnderwayCount, StyleKpiOrange),
            NumberCell("I7", summary.BounceCount, StyleKpiRed),
            NumberCell("K7", Ratio(problems, summary.TotalCount), StyleKpiPercent)));

        sheetData.Append(CreateSparseRow(8));
    }

    /// <summary>
    /// Schrijft één ranglijst en schuift <paramref name="row"/> door naar de eerste vrije rij
    /// eronder, zodat opeenvolgende tabellen elkaar nooit overschrijven.
    /// </summary>
    private static TableBlock WriteBreakdownTable(
        S.SheetData sheetData,
        List<string> merges,
        ref uint row,
        string title,
        string keyHeader,
        IReadOnlyList<MailLogInspectorBreakdownRow> rows)
    {
        merges.Add($"A{row}:{ColumnName(SheetColumnCount)}{row}");
        sheetData.Append(StyledSpanRow(row, 1, SheetColumnCount, title, StyleSection, 22));
        row++;

        uint headerRow = row;
        sheetData.Append(CreateStyledStringRow(row, StyleTableHeader,
            keyHeader, "Totaal", "Afgeleverd", "Onderweg", "Bounce", "Problemen", "% probleem", "% afgeleverd"));
        row++;

        uint firstDataRow = row;
        foreach (MailLogInspectorBreakdownRow entry in rows)
        {
            uint bodyStyle = row % 2 == 1 ? StyleBodyAlternate : StyleBody;
            uint numberStyle = row % 2 == 1 ? StyleNumberAlternate : StyleNumber;
            sheetData.Append(CreateSparseRow(row,
                StringCell($"A{row}", entry.Key, bodyStyle),
                NumberCell($"B{row}", entry.Total, numberStyle),
                NumberCell($"C{row}", entry.Delivered, numberStyle),
                NumberCell($"D{row}", entry.Underway, numberStyle),
                NumberCell($"E{row}", entry.Bounce, numberStyle),
                NumberCell($"F{row}", entry.ProblemCount, numberStyle),
                NumberCell($"G{row}", entry.ProblemRate, StylePercent),
                NumberCell($"H{row}", entry.SuccessRate, StylePercent)));
            row++;
        }

        if (rows.Count == 0)
        {
            sheetData.Append(CreateSparseRow(row,
                StringCell($"A{row}", "Geen resultaten in deze selectie.", StyleBody)));
            row++;
        }

        uint lastDataRow = row - 1;
        row++;
        return new TableBlock(headerRow, firstDataRow, lastDataRow, rows);
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

        sheetData.Append(CreateStyledStringRow(row, StyleTableHeader, valueHeader, "Aantal", "% van totaal", "Omschrijving"));
        row++;

        int total = rows.Sum(entry => entry.Count);
        foreach (MailLogInspectorValueMeaningCount entry in rows)
        {
            uint bodyStyle = row % 2 == 1 ? StyleBodyAlternate : StyleBody;
            uint numberStyle = row % 2 == 1 ? StyleNumberAlternate : StyleNumber;
            sheetData.Append(CreateSparseRow(row,
                StringCell($"A{row}", entry.Value, bodyStyle),
                NumberCell($"B{row}", entry.Count, numberStyle),
                NumberCell($"C{row}", Ratio(entry.Count, total), StylePercent),
                StringCell($"D{row}", entry.Meaning, bodyStyle)));
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
        string rateColumn,
        string rateColor,
        string volumeColor,
        Func<MailLogInspectorBreakdownRow, double>? volumeSelector = null,
        string volumeColumn = "B")
    {
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
                $"'{sheetName}'!$A${volumeTable.FirstDataRow}:$A${lastRow}",
                $"'{sheetName}'!${volumeColumn}${volumeTable.FirstDataRow}:${volumeColumn}${lastRow}",
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
                toColumn: 6,
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
                $"'{sheetName}'!$A${rateTable.FirstDataRow}:$A${lastRow}",
                $"'{sheetName}'!${rateColumn}${rateTable.FirstDataRow}:${rateColumn}${lastRow}",
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
                fromColumn: 6,
                fromRow: (int)ChartTopRow,
                toColumn: SheetColumnCount,
                toRow: (int)ChartBottomRow));
        }
    }

    private static S.Columns ReportColumns() =>
        new(
            Column(1, 34), Column(2, 12), Column(3, 13), Column(4, 12), Column(5, 12),
            Column(6, 13), Column(7, 13), Column(8, 14), Column(9, 12), Column(10, 12),
            Column(11, 13), Column(12, 13));

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
