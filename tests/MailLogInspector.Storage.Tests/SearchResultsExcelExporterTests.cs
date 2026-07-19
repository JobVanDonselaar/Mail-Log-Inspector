using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using MailLogInspector.App;
using MailLogInspector.Core;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace MailLogInspector.Storage.Tests;

public sealed class SearchResultsExcelExporterTests
{
    [Fact]
    public void Export_WithoutDashboard_KeepsSingleFilterableDetailsSheet()
    {
        using var export = ExportWorkbook(
            [SearchRow(MailLogInspectorReasonCode.PolicyBlock)],
            dashboard: null);

        Assert.Equal(["Zoekresultaten"], SheetNames(export.Document));
        WorksheetPart details = WorksheetPart(export.Document, "Zoekresultaten");
        Assert.NotNull(export.Document.WorkbookPart!.WorkbookStylesPart);
        Assert.Equal("Mail Log Inspector - Zoekresultaten", CellText(details, "A1"));
        Assert.Contains("werkelijk geladen en zichtbare", CellText(details, "A2"), StringComparison.Ordinal);
        Assert.Equal("A5:H6", details.Worksheet.GetFirstChild<AutoFilter>()?.Reference?.Value);
        Assert.Equal("Policy block", CellText(details, "D6"));
        Assert.DoesNotContain("Importbestand", details.Worksheet.Descendants<Cell>().Select(cell => cell.CellValue?.Text));
        Assert.Contains(details.Worksheet.Descendants<MergeCell>(), merge => merge.Reference?.Value == "A1:H1");
        Assert.Equal(5d, details.Worksheet.Descendants<Pane>().Single().VerticalSplit?.Value);
        Assert.Equal("A6", details.Worksheet.Descendants<Pane>().Single().TopLeftCell?.Value);
        Assert.Equal(8, details.Worksheet.GetFirstChild<Columns>()?.Elements<Column>().Count());
        Assert.Equal(OrientationValues.Landscape, details.Worksheet.GetFirstChild<PageSetup>()?.Orientation?.Value);
        Assert.True(FindCell(details, "A1")?.StyleIndex?.Value > 0);
        Assert.True(FindCell(details, "A5")?.StyleIndex?.Value > 0);
        AssertValid(export.Document);
    }

    [Fact]
    public void Export_WithDashboard_WritesLimitedTypedDataAndNativeCharts()
    {
        MailLogInspectorSenderDomainDashboard dashboard = Dashboard(
            trend: Enumerable.Range(0, 35)
                .Select(index => new MailLogInspectorSenderDomainTrendDay(
                    new DateTime(2026, 1, 1).AddDays(index),
                    10 + index,
                    8 + index,
                    1,
                    1,
                    9,
                    1,
                    90 + index,
                    MailLogInspectorDurationBucket.WithinFiveMinutes))
                .ToArray(),
            causes:
            [
                new(MailLogInspectorReasonCode.PolicyBlock, "Geblokkeerd policy/spam", 12),
                new(MailLogInspectorReasonCode.MailboxFull, "452 Mailbox vol", 9),
                new(MailLogInspectorReasonCode.InvalidRecipient, "550 Ongeldig", 6),
                new(MailLogInspectorReasonCode.Timeout, "Timeout", 3),
                new(MailLogInspectorReasonCode.DnsProblem, "DNS probleem", 2)
            ]);

        using var export = ExportWorkbook([], dashboard);

        Assert.Equal(["Domeinanalyse", "Zoekresultaten"], SheetNames(export.Document));
        WorksheetPart analysis = WorksheetPart(export.Document, "Domeinanalyse");
        Assert.Equal("Exquise Next Generation - E-mailafleverrapport", CellText(analysis, "A1"));
        Assert.Contains("example.com", CellText(analysis, "A2"), StringComparison.Ordinal);
        Assert.Contains("Verzending via SMTP.com", CellText(analysis, "A2"), StringComparison.Ordinal);
        Assert.Contains("01-01-2026", CellText(analysis, "A2"), StringComparison.Ordinal);
        Assert.Contains("operationeel inzicht", CellText(analysis, "A3"), StringComparison.Ordinal);
        Assert.Equal("Kerncijfers geselecteerde periode", CellText(analysis, "A5"));
        AssertNumericCell(analysis, "A7", 100);
        AssertNumericCell(analysis, "C7", 75);
        AssertNumericCell(analysis, "E7", 0.75);
        AssertNumericCell(analysis, "G7", 15);
        AssertNumericCell(analysis, "I7", 10);
        AssertNumericCell(analysis, "K7", 0.8);
        AssertNumericCell(analysis, "A11", 125.5);
        Assert.Equal("15 min", CellText(analysis, "C11"));

        Assert.Contains(analysis.Worksheet.Descendants<MergeCell>(), merge => merge.Reference?.Value == "A1:M1");
        Assert.Empty(analysis.Worksheet.Descendants<Pane>());
        Assert.Equal(80U, analysis.Worksheet.GetFirstChild<SheetViews>()?.GetFirstChild<SheetView>()?.ZoomScale?.Value);
        WorksheetPart details = WorksheetPart(export.Document, "Zoekresultaten");
        Assert.Equal(5d, details.Worksheet.Descendants<Pane>().Single().VerticalSplit?.Value);
        Assert.Equal(OrientationValues.Landscape, analysis.Worksheet.GetFirstChild<PageSetup>()?.Orientation?.Value);
        Assert.True(FindCell(analysis, "A1")?.StyleIndex?.Value > 0);
        Assert.True(FindCell(analysis, "A7")?.StyleIndex?.Value > 0);

        Assert.Equal("A42:E72", analysis.Worksheet.GetFirstChild<AutoFilter>()?.Reference?.Value);
        Assert.Equal("06-01-2026", CellText(analysis, "A43"));
        Assert.Equal("04-02-2026", CellText(analysis, "A72"));
        Assert.Null(FindCell(analysis, "A73"));
        Assert.Equal("Geblokkeerd policy/spam", CellText(analysis, "L43"));
        AssertNumericCell(analysis, "M46", 3);
        Assert.Equal("1–5 min", CellText(analysis, "G43"));
        AssertNumericCell(analysis, "H43", 0.5);
        Assert.Equal("> 1 uur", CellText(analysis, "G46"));
        AssertNumericCell(analysis, "H46", 2d / 30d);

        DrawingsPart drawings = Assert.IsType<DrawingsPart>(analysis.DrawingsPart);
        ChartPart[] charts = drawings.ChartParts.ToArray();
        Assert.Equal(3, charts.Length);
        Xdr.TwoCellAnchor[] anchors = drawings.WorksheetDrawing!.Elements<Xdr.TwoCellAnchor>().ToArray();
        Assert.Equal(3, anchors.Length);
        Assert.Equal(
        [
            (0, 11, 8, 25),
            (8, 11, 13, 25),
            (0, 25, 13, 39)
        ],
        anchors.Select(anchor =>
        (
            int.Parse(anchor.FromMarker!.ColumnId!.Text),
            int.Parse(anchor.FromMarker.RowId!.Text),
            int.Parse(anchor.ToMarker!.ColumnId!.Text),
            int.Parse(anchor.ToMarker.RowId!.Text)
        )).ToArray());
        Assert.Contains(charts, part => part.ChartSpace.Descendants<C.BarChart>()
            .Any(chart => chart.BarDirection?.Val?.Value == C.BarDirectionValues.Column));
        Assert.Contains(charts, part => part.ChartSpace.Descendants<C.BarChart>()
            .Any(chart => chart.BarDirection?.Val?.Value == C.BarDirectionValues.Bar));

        string[] formulas = charts
            .SelectMany(part => part.ChartSpace.Descendants<C.Formula>())
            .Select(formula => formula.Text ?? string.Empty)
            .ToArray();
        Assert.Contains("'Domeinanalyse'!$A$43:$A$72", formulas);
        Assert.Contains("'Domeinanalyse'!$C$43:$C$72", formulas);
        Assert.Contains("'Domeinanalyse'!$L$43:$L$46", formulas);
        Assert.Contains("'Domeinanalyse'!$M$43:$M$46", formulas);
        Assert.Contains("'Domeinanalyse'!$G$43:$G$46", formulas);
        Assert.Contains("'Domeinanalyse'!$H$43:$H$46", formulas);
        Assert.True(charts.Count(part => part.ChartSpace.Descendants<C.DataLabels>().Any()) >= 2);
        Assert.Contains(charts, part => part.ChartSpace.Descendants<C.MaxAxisValue>()
            .Any(maximum => maximum.Val?.Value == 60d));
        Assert.All(charts, part => Assert.Empty(part.ChartSpace.Elements<C.Style>()));
        Assert.All(charts, part => Assert.Contains(part.ChartSpace.Descendants<A.DefaultRunProperties>(), properties => properties.FontSize?.Value == 1100));
        Assert.All(charts, part => Assert.Contains(part.ChartSpace.Descendants<C.MajorGridlines>(), gridlines => gridlines.Descendants<A.RgbColorModelHex>().Any(color => color.Val?.Value == "D9E2F3")));
        Assert.Contains(charts, part => part.ChartSpace.Descendants<C.NumberingCache>()
            .Any(cache => cache.Descendants<C.NumericPoint>().Count() == 30));
        Assert.Contains(charts, part => part.ChartSpace.Descendants<C.StringCache>()
            .Any(cache => cache.Descendants<C.StringPoint>().Count() == 4));
        AssertValid(export.Document);
    }

    [Fact]
    public void Export_WithEmptyTrendAndCauses_OmitsChartsAndRemainsValid()
    {
        using var export = ExportWorkbook([], Dashboard([], []) with
        {
            DurationDistribution = MailLogInspectorDurationDistribution.Empty
        });

        WorksheetPart analysis = WorksheetPart(export.Document, "Domeinanalyse");
        Assert.Null(analysis.DrawingsPart);
        Assert.Empty(export.Document.WorkbookPart!.GetPartsOfType<ChartPart>());
        AssertValid(export.Document);
    }

    private static TemporaryExport ExportWorkbook(
        IReadOnlyList<MailLogInspectorSearchRow> rows,
        MailLogInspectorSenderDomainDashboard? dashboard)
    {
        string path = Path.Combine(Path.GetTempPath(), $"mail-log-export-{Guid.NewGuid():N}.xlsx");
        SearchResultsExcelExporter.Export(path, rows, dashboard);
        return new TemporaryExport(path, SpreadsheetDocument.Open(path, false));
    }

    private static MailLogInspectorSearchRow SearchRow(MailLogInspectorReasonCode reasonCode) =>
        new(
            new DateTime(2026, 1, 2, 10, 30, 0),
            "sender@example.com",
            "recipient@example.net",
            "tracking-id",
            "bounce",
            125,
            reasonCode,
            "Blocked by policy",
            new DateTime(2026, 1, 2, 10, 30, 0),
            new DateTime(2026, 1, 2, 10, 32, 5),
            "mail.csv");

    private static MailLogInspectorSenderDomainDashboard Dashboard(
        IReadOnlyList<MailLogInspectorSenderDomainTrendDay> trend,
        IReadOnlyList<MailLogInspectorSenderDomainCause> causes) =>
        new(
            "example.com",
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 31, 23, 59, 59),
            100,
            75,
            10,
            15,
            60,
            15,
            125.5,
            MailLogInspectorDurationBucket.WithinFifteenMinutes,
            trend,
            causes)
        {
            DurationDistribution = new MailLogInspectorDurationDistribution(
                60, 15, 30, 15, 8, 5, 2)
        };
    private static string[] SheetNames(SpreadsheetDocument document) =>
        document.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>()
            .Select(sheet => sheet.Name!.Value!)
            .ToArray();

    private static WorksheetPart WorksheetPart(SpreadsheetDocument document, string name)
    {
        Sheet sheet = document.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>()
            .Single(candidate => candidate.Name?.Value == name);
        return (WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id!.Value!);
    }

    private static Cell? FindCell(WorksheetPart worksheetPart, string reference) =>
        worksheetPart.Worksheet.Descendants<Cell>()
            .SingleOrDefault(cell => cell.CellReference?.Value == reference);

    private static string CellText(WorksheetPart worksheetPart, string reference) =>
        FindCell(worksheetPart, reference)?.CellValue?.Text ?? string.Empty;

    private static void AssertNumericCell(WorksheetPart worksheetPart, string reference, double expected)
    {
        Cell cell = Assert.IsType<Cell>(FindCell(worksheetPart, reference));
        Assert.Equal(CellValues.Number, cell.DataType?.Value);
        Assert.Equal(expected, double.Parse(cell.CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture), 8);
    }

    private static void AssertValid(SpreadsheetDocument document)
    {
        ValidationErrorInfo[] errors = new OpenXmlValidator(FileFormatVersions.Office2013)
            .Validate(document)
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(error => error.Description)));
    }

    private sealed class TemporaryExport(string path, SpreadsheetDocument document) : IDisposable
    {
        public SpreadsheetDocument Document { get; } = document;

        public void Dispose()
        {
            Document.Dispose();
            File.Delete(path);
        }
    }
}
