using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using MailLogInspector.Core;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace MailLogInspector.App;

public static class SearchResultsExcelExporter
{
    private const uint StyleTitle = 1;
    private const uint StyleSubtitle = 2;
    private const uint StyleNote = 3;
    private const uint StyleSection = 4;
    private const uint StyleTableHeader = 5;
    private const uint StyleBody = 6;
    private const uint StyleBodyAlternate = 7;
    private const uint StyleNumber = 8;
    private const uint StyleNumberAlternate = 9;
    private const uint StylePercent = 10;
    private const uint StyleDateTime = 11;
    private const uint StyleKpiLabel = 12;
    private const uint StyleKpiBlue = 13;
    private const uint StyleKpiGreen = 14;
    private const uint StyleKpiPercent = 15;
    private const uint StyleKpiRed = 16;
    private const uint StyleKpiOrange = 17;
    private const uint StyleDuration = 18;
    private const uint StyleKpiText = 19;
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

    private static void AddWorkbookStyles(WorkbookPart workbookPart)
    {
        WorkbookStylesPart stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = CreateStylesheet();
        stylesPart.Stylesheet.Save();
    }

    private static S.Stylesheet CreateStylesheet()
    {
        var numberingFormats = new S.NumberingFormats(
            new S.NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0" },
            new S.NumberingFormat { NumberFormatId = 165, FormatCode = "0.0%" },
            new S.NumberingFormat { NumberFormatId = 166, FormatCode = "0.0 \"sec\"" },
            new S.NumberingFormat { NumberFormatId = 167, FormatCode = "dd-mm-yyyy hh:mm" })
        { Count = 4 };

        var fonts = new S.Fonts(
            Font("FF1F2937", 11),
            Font("FFFFFFFF", 18, bold: true),
            Font("FFFFFFFF", 11, bold: true),
            Font("FF637386", 10),
            Font("FF1F4E78", 14, bold: true),
            Font("FF1F4E78", 16, bold: true),
            Font("FF2F855A", 16, bold: true),
            Font("FFC83B2B", 16, bold: true),
            Font("FFC77912", 16, bold: true))
        { Count = 9 };

        var fills = new S.Fills(
            new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
            new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 }),
            SolidFill("FF1F4E78"),
            SolidFill("FFEAF2F8"),
            SolidFill("FF2F75B5"),
            SolidFill("FFF4F7FA"),
            SolidFill("FFE2F0D9"),
            SolidFill("FFFCE4D6"),
            SolidFill("FFFFF2CC"))
        { Count = 9 };

        var borders = new S.Borders(new S.Border(), ThinBorder("FFD8E0EA")) { Count = 2 };
        var formats = new S.CellFormats(
            new S.CellFormat(),
            Format(fontId: 1, fillId: 2, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(fontId: 2, fillId: 2, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(fontId: 3, fillId: 3, alignment: new S.Alignment { Vertical = S.VerticalAlignmentValues.Center, WrapText = true }),
            Format(fontId: 4, fillId: 3, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(fontId: 2, fillId: 4, borderId: 1, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(borderId: 1, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(fillId: 5, borderId: 1, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(borderId: 1, numberFormatId: 164, alignment: Align(S.HorizontalAlignmentValues.Right)),
            Format(fillId: 5, borderId: 1, numberFormatId: 164, alignment: Align(S.HorizontalAlignmentValues.Right)),
            Format(borderId: 1, numberFormatId: 165, alignment: Align(S.HorizontalAlignmentValues.Right)),
            Format(borderId: 1, numberFormatId: 167, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(fontId: 3, fillId: 3, borderId: 1, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 5, fillId: 3, borderId: 1, numberFormatId: 164, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 6, fillId: 6, borderId: 1, numberFormatId: 164, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 6, fillId: 6, borderId: 1, numberFormatId: 165, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 7, fillId: 7, borderId: 1, numberFormatId: 164, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 8, fillId: 8, borderId: 1, numberFormatId: 164, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 5, fillId: 3, borderId: 1, numberFormatId: 166, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 5, fillId: 3, borderId: 1, alignment: Align(S.HorizontalAlignmentValues.Center)))
        { Count = 20 };

        return new S.Stylesheet(
            numberingFormats,
            fonts,
            fills,
            borders,
            new S.CellStyleFormats(new S.CellFormat()) { Count = 1 },
            formats,
            new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 },
            new S.DifferentialFormats { Count = 0 },
            new S.TableStyles { Count = 0, DefaultTableStyle = "TableStyleMedium2", DefaultPivotStyle = "PivotStyleLight16" });
    }

    private static S.Alignment Align(S.HorizontalAlignmentValues horizontal) =>
        new() { Horizontal = horizontal, Vertical = S.VerticalAlignmentValues.Center };

    private static S.Font Font(string color, double size, bool bold = false)
    {
        var font = new S.Font();
        if (bold)
        {
            font.Append(new S.Bold());
        }
        font.Append(
            new S.FontSize { Val = size },
            new S.Color { Rgb = color },
            new S.FontName { Val = "Aptos" },
            new S.FontFamilyNumbering { Val = 2 });
        return font;
    }

    private static S.Fill SolidFill(string color) =>
        new(new S.PatternFill(new S.ForegroundColor { Rgb = color }, new S.BackgroundColor { Indexed = 64 })
        {
            PatternType = S.PatternValues.Solid
        });

    private static S.Border ThinBorder(string color) =>
        new(
            new S.LeftBorder(new S.Color { Rgb = color }) { Style = S.BorderStyleValues.Thin },
            new S.RightBorder(new S.Color { Rgb = color }) { Style = S.BorderStyleValues.Thin },
            new S.TopBorder(new S.Color { Rgb = color }) { Style = S.BorderStyleValues.Thin },
            new S.BottomBorder(new S.Color { Rgb = color }) { Style = S.BorderStyleValues.Thin },
            new S.DiagonalBorder());

    private static S.CellFormat Format(
        uint fontId = 0,
        uint fillId = 0,
        uint borderId = 0,
        uint numberFormatId = 0,
        S.Alignment? alignment = null)
    {
        var format = new S.CellFormat
        {
            FontId = fontId,
            FillId = fillId,
            BorderId = borderId,
            NumberFormatId = numberFormatId,
            ApplyFont = fontId > 0,
            ApplyFill = fillId > 0,
            ApplyBorder = borderId > 0,
            ApplyNumberFormat = numberFormatId > 0,
            ApplyAlignment = alignment is not null
        };
        if (alignment is not null)
        {
            format.Append(alignment);
        }
        return format;
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
    private static double RoundChartMaximum(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        double targetStep = value / 4.0;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(targetStep)));
        double normalized = targetStep / magnitude;
        double factor = normalized <= 1
            ? 1
            : normalized <= 2
                ? 2
                : normalized <= 2.5
                    ? 2.5
                    : normalized <= 5
                        ? 5
                        : 10;
        return Math.Ceiling(value / (factor * magnitude)) * factor * magnitude;
    }
    private static C.ChartSpace CreateBarChart(
        C.BarDirectionValues direction,
        string title,
        string color,
        string categoryFormula,
        string valueFormula,
        IReadOnlyList<string> categories,
        IReadOnlyList<double> values,
        uint categoryAxisId,
        uint valueAxisId,
        string numberFormat = "#,##0",
        bool showValues = false,
        double? maximumValue = null)
    {
        var chartSpace = new C.ChartSpace();
        chartSpace.Append(new C.EditingLanguage { Val = "nl-NL" });
        var chart = new C.Chart();
        chart.Append(CreateChartTitle(title));
        var plotArea = new C.PlotArea(new C.Layout());
        var barChart = new C.BarChart(
            new C.BarDirection { Val = direction },
            new C.BarGrouping { Val = C.BarGroupingValues.Clustered },
            new C.VaryColors { Val = false });
        var series = new C.BarChartSeries(
            new C.Index { Val = 0 },
            new C.Order { Val = 0 },
            new C.ChartShapeProperties(
                new A.SolidFill(new A.RgbColorModelHex { Val = color }),
                new A.Outline(new A.NoFill())),
            new C.CategoryAxisData(CreateStringReference(categoryFormula, categories)),
            new C.Values(CreateNumberReference(valueFormula, values, numberFormat)));
        barChart.Append(series);
        if (showValues)
        {
            barChart.Append(new C.DataLabels(
                new C.NumberingFormat { FormatCode = numberFormat, SourceLinked = false },
                new C.ShowLegendKey { Val = false },
                new C.ShowValue { Val = true },
                new C.ShowCategoryName { Val = false },
                new C.ShowSeriesName { Val = false }));
        }
        barChart.Append(new C.GapWidth { Val = (ushort)(direction == C.BarDirectionValues.Column ? 55 : 65) });
        barChart.Append(new C.AxisId { Val = categoryAxisId }, new C.AxisId { Val = valueAxisId });
        plotArea.Append(barChart);
        plotArea.Append(CreateCategoryAxis(categoryAxisId, valueAxisId, direction));
        plotArea.Append(CreateValueAxis(valueAxisId, categoryAxisId, direction, numberFormat, maximumValue));
        chart.Append(plotArea, new C.PlotVisibleOnly { Val = true }, new C.DisplayBlanksAs { Val = C.DisplayBlanksAsValues.Zero });
        chartSpace.Append(chart);
        return chartSpace;
    }

    private static C.StringReference CreateStringReference(string formula, IReadOnlyList<string> values)
    {
        var cache = new C.StringCache();
        cache.Append(new C.PointCount { Val = checked((uint)values.Count) });
        for (uint index = 0; index < values.Count; index++)
        {
            cache.Append(new C.StringPoint { Index = index, NumericValue = new C.NumericValue(values[(int)index]) });
        }
        return new C.StringReference(new C.Formula(formula), cache);
    }

    private static C.NumberReference CreateNumberReference(string formula, IReadOnlyList<double> values, string numberFormat)
    {
        var cache = new C.NumberingCache(new C.FormatCode(numberFormat));
        cache.Append(new C.PointCount { Val = checked((uint)values.Count) });
        for (uint index = 0; index < values.Count; index++)
        {
            cache.Append(new C.NumericPoint
            {
                Index = index,
                NumericValue = new C.NumericValue(values[(int)index].ToString(CultureInfo.InvariantCulture))
            });
        }
        return new C.NumberReference(new C.Formula(formula), cache);
    }

    private static C.CategoryAxis CreateCategoryAxis(
        uint axisId,
        uint crossingAxisId,
        C.BarDirectionValues direction)
    {
        return new C.CategoryAxis(
            new C.AxisId { Val = axisId },
            new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
            new C.Delete { Val = false },
            new C.AxisPosition { Val = direction == C.BarDirectionValues.Bar ? C.AxisPositionValues.Left : C.AxisPositionValues.Bottom },
            new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
            new C.CrossingAxis { Val = crossingAxisId },
            new C.Crosses { Val = C.CrossesValues.AutoZero },
            new C.AutoLabeled { Val = true },
            new C.LabelAlignment { Val = C.LabelAlignmentValues.Center },
            new C.LabelOffset { Val = 100 });
    }

    private static C.ValueAxis CreateValueAxis(
        uint axisId,
        uint crossingAxisId,
        C.BarDirectionValues direction,
        string numberFormat,
        double? maximumValue)
    {
        var scaling = new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax });
        if (maximumValue.HasValue)
        {
            scaling.Append(new C.MaxAxisValue { Val = maximumValue.Value });
        }
        scaling.Append(new C.MinAxisValue { Val = 0 });

        return new C.ValueAxis(
            new C.AxisId { Val = axisId },
            scaling,
            new C.Delete { Val = false },
            new C.AxisPosition { Val = direction == C.BarDirectionValues.Bar ? C.AxisPositionValues.Bottom : C.AxisPositionValues.Left },
            new C.MajorGridlines(
                new C.ChartShapeProperties(
                    new A.Outline(
                        new A.SolidFill(new A.RgbColorModelHex { Val = "D9E2F3" }))
                    {
                        Width = 6350
                    })),
            new C.NumberingFormat { FormatCode = numberFormat, SourceLinked = false },
            new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
            new C.CrossingAxis { Val = crossingAxisId },
            new C.Crosses { Val = C.CrossesValues.AutoZero },
            new C.CrossBetween { Val = C.CrossBetweenValues.Between });
    }

    private static C.Title CreateChartTitle(string title) =>
        new(
            new C.ChartText(new C.RichText(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(
                    new A.ParagraphProperties(
                        new A.DefaultRunProperties
                        {
                            FontSize = 1100,
                            Bold = true
                        }),
                    new A.Run(new A.Text(title))))),
            new C.Overlay { Val = false });

    private static Xdr.TwoCellAnchor CreateAnchor(
        string relationshipId,
        uint drawingId,
        string name,
        int fromColumn,
        int fromRow,
        int toColumn,
        int toRow)
    {
        var graphicFrame = new Xdr.GraphicFrame(
            new Xdr.NonVisualGraphicFrameProperties(
                new Xdr.NonVisualDrawingProperties { Id = drawingId, Name = name },
                new Xdr.NonVisualGraphicFrameDrawingProperties()),
            new Xdr.Transform(
                new A.Offset { X = 0, Y = 0 },
                new A.Extents { Cx = 0, Cy = 0 }),
            new A.Graphic(new A.GraphicData(
                new C.ChartReference { Id = relationshipId })
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart" }))
        { Macro = string.Empty };

        return new Xdr.TwoCellAnchor(
            Marker<Xdr.FromMarker>(fromColumn, fromRow),
            Marker<Xdr.ToMarker>(toColumn, toRow),
            graphicFrame,
            new Xdr.ClientData());
    }

    private static T Marker<T>(int column, int row) where T : OpenXmlCompositeElement, new()
    {
        var marker = new T();
        marker.Append(
            new Xdr.ColumnId(column.ToString(CultureInfo.InvariantCulture)),
            new Xdr.ColumnOffset("0"),
            new Xdr.RowId(row.ToString(CultureInfo.InvariantCulture)),
            new Xdr.RowOffset("0"));
        return marker;
    }

    private static S.SheetProperties FitToPageProperties() =>
        new(new S.PageSetupProperties { FitToPage = true, AutoPageBreaks = false });

    private static S.SheetViews DashboardView() =>
        new(new S.SheetView
        {
            WorkbookViewId = 0,
            ZoomScale = 80U,
            ZoomScaleNormal = 100U
        });

    private static S.SheetViews FrozenView(double rows, string topLeftCell) =>
        new(new S.SheetView(
            new S.Pane
            {
                VerticalSplit = rows,
                TopLeftCell = topLeftCell,
                ActivePane = S.PaneValues.BottomLeft,
                State = S.PaneStateValues.Frozen
            })
        { WorkbookViewId = 0 });

    private static S.Columns SearchColumns() =>
        new(
            Column(1, 19), Column(2, 31), Column(3, 31), Column(4, 18), Column(5, 16),
            Column(6, 54), Column(7, 19), Column(8, 19));

    private static S.Columns DashboardColumns() =>
        new(
            Column(1, 16), Column(2, 16), Column(3, 16), Column(4, 16), Column(5, 16),
            Column(6, 3), Column(7, 16), Column(8, 16), Column(9, 16), Column(10, 16),
            Column(11, 3), Column(12, 28), Column(13, 14));

    private static S.Column Column(uint index, double width) =>
        new() { Min = index, Max = index, Width = width, CustomWidth = true };

    private static S.PageMargins ReportPageMargins() =>
        new() { Left = 0.3, Right = 0.3, Top = 0.5, Bottom = 0.5, Header = 0.2, Footer = 0.2 };

    private static S.PageSetup LandscapePageSetup() =>
        new()
        {
            PaperSize = 9,
            Orientation = S.OrientationValues.Landscape,
            FitToWidth = 1,
            FitToHeight = 0
        };

    private static S.MergeCells MergeRanges(params string[] ranges)
    {
        var merges = new S.MergeCells();
        foreach (string range in ranges)
        {
            merges.Append(new S.MergeCell { Reference = range });
        }
        return merges;
    }

    private static S.Row KpiRow(uint rowIndex, uint style, params (string Column, string Label)[] values)
    {
        return CreateSparseRow(
            rowIndex,
            values.Select(value => StringCell($"{value.Column}{rowIndex}", value.Label, style)).ToArray());
    }

    private static S.Row StyledSpanRow(
        uint rowIndex,
        int firstColumn,
        int lastColumn,
        string value,
        uint style,
        double height)
    {
        var cells = new List<S.Cell> { StringCell($"{ColumnName(firstColumn)}{rowIndex}", value, style) };
        for (int column = firstColumn + 1; column <= lastColumn; column++)
        {
            cells.Add(StyledBlank($"{ColumnName(column)}{rowIndex}", style));
        }
        return CreateSparseRow(rowIndex, height, cells.ToArray());
    }

    private static S.Row CreateStyledStringRow(uint rowIndex, uint style, params string[] values)
    {
        var cells = values.Select((value, index) =>
            StringCell($"{ColumnName(index + 1)}{rowIndex}", value, style)).ToArray();
        return CreateSparseRow(rowIndex, cells);
    }

    private static S.Row CreateSparseRow(uint rowIndex, params S.Cell[] cells) =>
        CreateSparseRow(rowIndex, null, cells);

    private static S.Row CreateSparseRow(uint rowIndex, double? height, params S.Cell[] cells)
    {
        var row = new S.Row { RowIndex = rowIndex };
        if (height.HasValue)
        {
            row.Height = height.Value;
            row.CustomHeight = true;
        }
        row.Append(cells);
        return row;
    }

    private static S.Cell StringCell(string reference, string? value, uint style = 0) => new()
    {
        CellReference = reference,
        DataType = S.CellValues.String,
        CellValue = new S.CellValue(value ?? string.Empty),
        StyleIndex = style
    };

    private static S.Cell NumberCell(string reference, double value, uint style = 0) => new()
    {
        CellReference = reference,
        DataType = S.CellValues.Number,
        CellValue = new S.CellValue(value.ToString(CultureInfo.InvariantCulture)),
        StyleIndex = style
    };

    private static S.Cell DateCell(string reference, DateTime? value, uint style) =>
        value.HasValue
            ? NumberCell(reference, value.Value.ToOADate(), style)
            : StyledBlank(reference, style);

    private static S.Cell StyledBlank(string reference, uint style) => new()
    {
        CellReference = reference,
        StyleIndex = style
    };

    private static string ColumnName(int column)
    {
        string result = string.Empty;
        while (column > 0)
        {
            column--;
            result = (char)('A' + column % 26) + result;
            column /= 26;
        }
        return result;
    }
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
