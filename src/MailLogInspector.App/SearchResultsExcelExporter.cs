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

public static class SearchResultsExcelExporter
{
    public static void Export(
        string path,
        IReadOnlyList<MailLogInspectorSearchRow> visibleRows,
        MailLogInspectorSenderDomainDashboard? domainDashboard)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using SpreadsheetDocument document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        document.PackageProperties.Title = domainDashboard is null
            ? "Mail Log Inspector - Zoekresultaten"
            : "Mail Log Inspector - Domeinanalyse";
        document.PackageProperties.Subject = "Zakelijk rapport over mailaflevering en bounce-oorzaken";
        document.PackageProperties.Creator = "Mail Log Inspector";

        WorkbookPart workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new S.Workbook();
        AddWorkbookStyles(workbookPart);
        S.Sheets sheets = workbookPart.Workbook.AppendChild(new S.Sheets());

        if (domainDashboard is not null)
        {
            AddDomainDashboardSheet(workbookPart, sheets, domainDashboard, sheetId: 1);
            AddSearchResultsSheet(workbookPart, sheets, visibleRows, sheetId: 2);
        }
        else
        {
            AddSearchResultsSheet(workbookPart, sheets, visibleRows, sheetId: 1);
        }

        workbookPart.Workbook.Save();
    }

    private static void AddSearchResultsSheet(
        WorkbookPart workbookPart,
        S.Sheets sheets,
        IReadOnlyList<MailLogInspectorSearchRow> rows,
        uint sheetId)
    {
        WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new S.SheetData();
        var worksheet = new S.Worksheet(
            FitToPageProperties(),
            FrozenView(5, "A6"),
            new S.SheetFormatProperties { DefaultRowHeight = 18 },
            SearchColumns(),
            sheetData);
        worksheetPart.Worksheet = worksheet;
        sheets.Append(new S.Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = "Zoekresultaten"
        });

        sheetData.Append(StyledSpanRow(1, 1, 8, "Mail Log Inspector - Zoekresultaten", StyleTitle, 30));
        sheetData.Append(StyledSpanRow(2, 1, 8,
            "Dit werkblad bevat de werkelijk geladen en zichtbare zoekresultaten. Gebruik de filters in rij 5 voor verdere selectie.",
            StyleNote,
            30));
        sheetData.Append(StyledSpanRow(3, 1, 8,
            $"Gegenereerd: {DateTime.Now:dd-MM-yyyy HH:mm} | Aantal zichtbare regels: {rows.Count:#,##0}",
            StyleNote,
            22));
        sheetData.Append(CreateSparseRow(4));
        sheetData.Append(CreateStyledStringRow(5, StyleTableHeader,
            "Accepted at", "Afzender", "Ontvanger", "Status", "Doorlooptijd",
            "Laatste melding", "First seen", "Last seen"));

        uint rowIndex = 6;
        foreach (MailLogInspectorSearchRow row in rows)
        {
            bool alternate = rowIndex % 2 == 1;
            uint bodyStyle = alternate ? StyleBodyAlternate : StyleBody;
            sheetData.Append(CreateSparseRow(rowIndex,
                DateCell($"A{rowIndex}", row.AcceptedAt, StyleDateTime),
                StringCell($"B{rowIndex}", row.Sender, bodyStyle),
                StringCell($"C{rowIndex}", row.Recipient, bodyStyle),
                StringCell($"D{rowIndex}", row.StatusDisplay, bodyStyle),
                StringCell($"E{rowIndex}", row.DurationDisplay, bodyStyle),
                StringCell($"F{rowIndex}", row.LastMessage, bodyStyle),
                DateCell($"G{rowIndex}", row.FirstSeenAt, StyleDateTime),
                DateCell($"H{rowIndex}", row.LastSeenAt, StyleDateTime)));
            rowIndex++;
        }

        worksheet.Append(
            new S.AutoFilter { Reference = $"A5:H{Math.Max(5, rows.Count + 5)}" },
            MergeRanges("A1:H1", "A2:H2", "A3:H3"),
            ReportPageMargins(),
            LandscapePageSetup());
    }
    private static void AddDomainDashboardSheet(
        WorkbookPart workbookPart,
        S.Sheets sheets,
        MailLogInspectorSenderDomainDashboard dashboard,
        uint sheetId)
    {
        const uint sourceHeaderRow = 42;
        const uint sourceStartRow = 43;

        WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new S.SheetData();
        var worksheet = new S.Worksheet(
            FitToPageProperties(),
            DashboardView(),
            new S.SheetFormatProperties { DefaultRowHeight = 18 },
            DashboardColumns(),
            sheetData);
        worksheetPart.Worksheet = worksheet;
        sheets.Append(new S.Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = "Domeinanalyse"
        });

        sheetData.Append(StyledSpanRow(1, 1, 13, "Exquise Next Generation - E-mailafleverrapport", StyleTitle, 30));
        sheetData.Append(StyledSpanRow(2, 1, 13,
            $"Tandartspraktijk / afzenderdomein: {dashboard.Domain} | Verzending via SMTP.com | " +
            $"Periode: {dashboard.FromDate:dd-MM-yyyy} t/m {dashboard.ThroughDate:dd-MM-yyyy}",
            StyleSubtitle, 28));
        sheetData.Append(StyledSpanRow(3, 1, 13,
            "Dit rapport geeft operationeel inzicht in aflevering, snelheid en de belangrijkste oorzaken van niet-afgeleverde berichten.",
            StyleNote, 30));
        sheetData.Append(CreateSparseRow(4));
        sheetData.Append(StyledSpanRow(5, 1, 13, "Kerncijfers geselecteerde periode", StyleSection, 24));

        sheetData.Append(KpiRow(6, StyleKpiLabel,
            ("A", "Geaccepteerd"), ("C", "Afgeleverd"), ("E", "Afleverratio"),
            ("G", "Bounced"), ("I", "Onderweg"), ("K", "Duurdekking")));
        sheetData.Append(CreateSparseRow(7, 32,
            NumberCell("A7", dashboard.TotalCount, StyleKpiBlue),
            NumberCell("C7", dashboard.DeliveredCount, StyleKpiGreen),
            NumberCell("E7", dashboard.TotalCount <= 0 ? 0 : dashboard.DeliveredCount / (double)dashboard.TotalCount, StyleKpiPercent),
            NumberCell("G7", dashboard.BounceCount, StyleKpiRed),
            NumberCell("I7", dashboard.UnderwayCount, StyleKpiOrange),
            NumberCell("K7", dashboard.DeliveredCount <= 0 ? 0 : dashboard.DurationCount / (double)dashboard.DeliveredCount, StyleKpiPercent)));

        sheetData.Append(CreateSparseRow(8));
        sheetData.Append(StyledSpanRow(9, 1, 13, "Afleversnelheid laatste 30 dagen", StyleSection, 24));
        sheetData.Append(KpiRow(10, StyleKpiLabel,
            ("A", "Gemiddelde aflevertijd"), ("C", "95% afgeleverd binnen"), ("E", "Bruikbare duren")));
        sheetData.Append(CreateSparseRow(11, 28,
            NumberCell("A11", dashboard.AverageDurationSeconds ?? 0, StyleDuration),
            StringCell("C11", FormatDurationBucket(dashboard.P95DurationBucket), StyleKpiText),
            NumberCell("E11", dashboard.DurationCount, StyleKpiBlue)));

        IReadOnlyList<MailLogInspectorSenderDomainTrendDay> trend = dashboard.Trend.TakeLast(30).ToArray();
        IReadOnlyList<MailLogInspectorSenderDomainCause> causes = dashboard.TopCauses.Take(4).ToArray();
        MailLogInspectorDurationDistribution duration = dashboard.DurationDistribution;
        int delayedCount = duration.LongerThanOneMinute;
        (string Label, int Count)[] delayedBuckets =
        [
            ("1–5 min", duration.OneToFiveMinutes),
            ("5–15 min", duration.FiveToFifteenMinutes),
            ("15–60 min", duration.FifteenToSixtyMinutes),
            ("> 1 uur", duration.OverOneHour)
        ];

        sheetData.Append(StyledSpanRow(41, 1, 5, "Brondata dagelijkse ontwikkeling", StyleSection, 22));
        sheetData.Append(StyledSpanRow(41, 7, 10, "Brondata afleververtraging", StyleSection, 22));
        sheetData.Append(StyledSpanRow(41, 12, 13, "Brondata bounce-oorzaken", StyleSection, 22));
        sheetData.Append(CreateSparseRow(sourceHeaderRow,
            StringCell("A42", "Dag", StyleTableHeader),
            StringCell("B42", "Geaccepteerd", StyleTableHeader),
            StringCell("C42", "Afgeleverd", StyleTableHeader),
            StringCell("D42", "Bounced", StyleTableHeader),
            StringCell("E42", "Onderweg", StyleTableHeader),
            StringCell("G42", "Vertraging", StyleTableHeader),
            StringCell("H42", "% vertraagd", StyleTableHeader),
            StringCell("I42", "Aantal", StyleTableHeader),
            StringCell("J42", "% totaal", StyleTableHeader),
            StringCell("L42", "Bounce-oorzaak", StyleTableHeader),
            StringCell("M42", "Aantal", StyleTableHeader)));

        int dataRowCount = Math.Max(Math.Max(trend.Count, causes.Count), delayedBuckets.Length);
        for (int index = 0; index < dataRowCount; index++)
        {
            uint rowIndex = checked(sourceStartRow + (uint)index);
            uint bodyStyle = rowIndex % 2 == 1 ? StyleBodyAlternate : StyleBody;
            uint numberStyle = rowIndex % 2 == 1 ? StyleNumberAlternate : StyleNumber;
            var cells = new List<S.Cell>();
            if (index < trend.Count)
            {
                MailLogInspectorSenderDomainTrendDay day = trend[index];
                cells.Add(StringCell($"A{rowIndex}", day.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture), bodyStyle));
                cells.Add(NumberCell($"B{rowIndex}", day.TotalCount, numberStyle));
                cells.Add(NumberCell($"C{rowIndex}", day.DeliveredCount, numberStyle));
                cells.Add(NumberCell($"D{rowIndex}", day.BounceCount, numberStyle));
                cells.Add(NumberCell($"E{rowIndex}", day.UnderwayCount, numberStyle));
            }
            if (index < delayedBuckets.Length)
            {
                cells.Add(StringCell($"G{rowIndex}", delayedBuckets[index].Label, bodyStyle));
                cells.Add(NumberCell(
                    $"H{rowIndex}",
                    delayedCount <= 0 ? 0 : delayedBuckets[index].Count / (double)delayedCount,
                    StylePercent));
                cells.Add(NumberCell($"I{rowIndex}", delayedBuckets[index].Count, numberStyle));
                cells.Add(NumberCell(
                    $"J{rowIndex}",
                    duration.DurationCount <= 0 ? 0 : delayedBuckets[index].Count / (double)duration.DurationCount,
                    StylePercent));
            }
            if (index < causes.Count)
            {
                cells.Add(StringCell($"L{rowIndex}", causes[index].Description, bodyStyle));
                cells.Add(NumberCell($"M{rowIndex}", causes[index].Count, numberStyle));
            }
            sheetData.Append(CreateSparseRow(rowIndex, cells.ToArray()));
        }

        worksheet.Append(
            new S.AutoFilter { Reference = $"A42:E{Math.Max(42, 42 + trend.Count)}" },
            MergeRanges(
                "A1:M1", "A2:M2", "A3:M3", "A5:M5",
                "A6:B6", "C6:D6", "E6:F6", "G6:H6", "I6:J6", "K6:L6",
                "A7:B7", "C7:D7", "E7:F7", "G7:H7", "I7:J7", "K7:L7",
                "A9:M9", "A10:B10", "C10:D10", "E10:F10",
                "A11:B11", "C11:D11", "E11:F11",
                "A41:E41", "G41:J41", "L41:M41"),
            ReportPageMargins(),
            LandscapePageSetup());

        if (trend.Count > 0 || causes.Count > 0 || duration.DurationCount > 0)
        {
            AddCharts(worksheetPart, trend, causes, duration, sourceStartRow);
        }
    }
    private static void AddCharts(
        WorksheetPart worksheetPart,
        IReadOnlyList<MailLogInspectorSenderDomainTrendDay> trend,
        IReadOnlyList<MailLogInspectorSenderDomainCause> causes,
        MailLogInspectorDurationDistribution duration,
        uint sourceStartRow)
    {
        DrawingsPart drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
        var drawing = new Xdr.WorksheetDrawing();
        drawingsPart.WorksheetDrawing = drawing;
        worksheetPart.Worksheet.Append(new S.Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) });

        uint drawingId = 1;
        if (trend.Count > 0)
        {
            double[] delivered = trend.Select(day => (double)day.DeliveredCount).ToArray();
            uint sourceEndRow = checked(sourceStartRow + (uint)trend.Count - 1);
            ChartPart chartPart = drawingsPart.AddNewPart<ChartPart>();
            chartPart.ChartSpace = CreateBarChart(
                C.BarDirectionValues.Column,
                "Dagelijks afgeleverd volume",
                "2F855A",
                "'Domeinanalyse'!$A$" + sourceStartRow + ":$A$" + sourceEndRow,
                "'Domeinanalyse'!$C$" + sourceStartRow + ":$C$" + sourceEndRow,
                trend.Select(day => day.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)).ToArray(),
                delivered,
                1001,
                1002,
                "#,##0",
                showValues: false,
                maximumValue: RoundChartMaximum(delivered.Max()));
            drawing.Append(CreateAnchor(
                drawingsPart.GetIdOfPart(chartPart),
                drawingId++,
                "Afleversnelheid laatste 30 dagen",
                0, 11, 8, 25));
        }

        int delayedCount = duration.LongerThanOneMinute;
        if (delayedCount > 0)
        {
            string[] labels = ["1–5 min", "5–15 min", "15–60 min", "> 1 uur"];
            double[] percentages =
            [
                duration.OneToFiveMinutes / (double)delayedCount,
                duration.FiveToFifteenMinutes / (double)delayedCount,
                duration.FifteenToSixtyMinutes / (double)delayedCount,
                duration.OverOneHour / (double)delayedCount
            ];
            uint sourceEndRow = checked(sourceStartRow + 3);
            ChartPart chartPart = drawingsPart.AddNewPart<ChartPart>();
            chartPart.ChartSpace = CreateBarChart(
                C.BarDirectionValues.Bar,
                "Afleververtraging",
                "D97706",
                "'Domeinanalyse'!$G$" + sourceStartRow + ":$G$" + sourceEndRow,
                "'Domeinanalyse'!$H$" + sourceStartRow + ":$H$" + sourceEndRow,
                labels,
                percentages,
                3001,
                3002,
                "0.0%",
                showValues: true,
                maximumValue: 1);
            drawing.Append(CreateAnchor(
                drawingsPart.GetIdOfPart(chartPart),
                drawingId++,
                "Afleververtraging",
                8, 11, 13, 25));
        }

        if (causes.Count > 0)
        {
            uint sourceEndRow = checked(sourceStartRow + (uint)causes.Count - 1);
            ChartPart chartPart = drawingsPart.AddNewPart<ChartPart>();
            chartPart.ChartSpace = CreateBarChart(
                C.BarDirectionValues.Bar,
                "Bounce-oorzaken",
                "C83B2B",
                "'Domeinanalyse'!$L$" + sourceStartRow + ":$L$" + sourceEndRow,
                "'Domeinanalyse'!$M$" + sourceStartRow + ":$M$" + sourceEndRow,
                causes.Select(cause => cause.Description).ToArray(),
                causes.Select(cause => (double)cause.Count).ToArray(),
                2001,
                2002,
                "#,##0",
                showValues: true);
            drawing.Append(CreateAnchor(
                drawingsPart.GetIdOfPart(chartPart),
                drawingId,
                "Bounce-oorzaken",
                0, 25, 13, 39));
        }

        drawing.Save();
    }
    private static S.Columns SearchColumns() =>
        new(
            Column(1, 19), Column(2, 31), Column(3, 31), Column(4, 18), Column(5, 16),
            Column(6, 54), Column(7, 19), Column(8, 19));

    private static S.Columns DashboardColumns() =>
        new(
            Column(1, 16), Column(2, 16), Column(3, 16), Column(4, 16), Column(5, 16),
            Column(6, 3), Column(7, 16), Column(8, 16), Column(9, 16), Column(10, 16),
            Column(11, 3), Column(12, 28), Column(13, 14));

    private static string FormatDurationBucket(MailLogInspectorDurationBucket? bucket) => bucket switch
    {
        MailLogInspectorDurationBucket.WithinOneMinute => "1 min",
        MailLogInspectorDurationBucket.WithinFiveMinutes => "5 min",
        MailLogInspectorDurationBucket.WithinFifteenMinutes => "15 min",
        MailLogInspectorDurationBucket.WithinOneHour => "1 uur",
        MailLogInspectorDurationBucket.OverOneHour => "> 1 uur",
        _ => "-"
    };
}
