using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using MailLogInspector.App;
using MailLogInspector.Core;
using Xunit;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace MailLogInspector.Storage.Tests;

public sealed class AnalysisExcelExporterTests
{
    [Fact]
    public void Export_WritesSenderAndRecipientSheetsWithSharedHeaderAndKpis()
    {
        using var export = ExportWorkbook(Summary(), Context());

        Assert.Equal(["Afzenders", "Ontvangers"], SheetNames(export.Document));
        Assert.NotNull(export.Document.WorkbookPart!.WorkbookStylesPart);

        WorksheetPart senders = WorksheetPart(export.Document, "Afzenders");
        Assert.Equal("Mail Log Inspector - Analyse verzendende domeinen", CellText(senders, "A1"));
        Assert.Contains("01-03-2026 t/m 31-03-2026", CellText(senders, "A2"), StringComparison.Ordinal);
        Assert.Contains("afzender bevat 'praktijk'", CellText(senders, "A2"), StringComparison.Ordinal);
        Assert.Contains("ontvanger bevat 'gmail'", CellText(senders, "A2"), StringComparison.Ordinal);
        Assert.Contains("top 25", CellText(senders, "A2"), StringComparison.Ordinal);
        Assert.Contains("afzenderdomeinen", CellText(senders, "A3"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Kerncijfers geselecteerde periode", CellText(senders, "A5"));

        WorksheetPart recipients = WorksheetPart(export.Document, "Ontvangers");
        Assert.Equal("Mail Log Inspector - Analyse ontvangende domeinen", CellText(recipients, "A1"));
        Assert.Equal("Kerncijfers geselecteerde periode", CellText(recipients, "A5"));

        foreach (WorksheetPart sheet in new[] { senders, recipients })
        {
            Assert.Equal("Geaccepteerd", CellText(sheet, "A6"));
            Assert.Equal("Probleemratio", CellText(sheet, "K6"));
            AssertNumericCell(sheet, "A7", 200);
            AssertNumericCell(sheet, "C7", 150);
            AssertNumericCell(sheet, "E7", 0.75);
            AssertNumericCell(sheet, "G7", 20);
            AssertNumericCell(sheet, "I7", 30);
            AssertNumericCell(sheet, "K7", 0.25);
            Assert.True(FindCell(sheet, "A1")?.StyleIndex?.Value > 0);
            Assert.True(FindCell(sheet, "A7")?.StyleIndex?.Value > 0);
            Assert.Contains(sheet.Worksheet.Descendants<MergeCell>(), merge => merge.Reference?.Value == "A1:L1");
            Assert.Contains(sheet.Worksheet.Descendants<MergeCell>(), merge => merge.Reference?.Value == "A7:B7");
            Assert.Equal(12, sheet.Worksheet.GetFirstChild<Columns>()?.Elements<Column>().Count());
            Assert.Equal(OrientationValues.Landscape, sheet.Worksheet.GetFirstChild<PageSetup>()?.Orientation?.Value);
        }

        AssertValid(export.Document);
    }

    [Fact]
    public void Export_WritesSenderRankingsBelowTheChartArea()
    {
        using var export = ExportWorkbook(Summary(), Context());
        WorksheetPart senders = WorksheetPart(export.Document, "Afzenders");

        Assert.Equal("Beeld van de verzendende domeinen", CellText(senders, "A9"));
        Assert.Equal("Afzenderdomeinen op volume", CellText(senders, "A29"));
        Assert.Equal("Domein", CellText(senders, "A30"));
        Assert.Equal("% afgeleverd", CellText(senders, "H30"));

        Assert.Equal("praktijk-a.nl", CellText(senders, "A31"));
        AssertNumericCell(senders, "B31", 120);
        AssertNumericCell(senders, "C31", 100);
        AssertNumericCell(senders, "D31", 8);
        AssertNumericCell(senders, "E31", 12);
        AssertNumericCell(senders, "F31", 20);
        AssertNumericCell(senders, "G31", 20d / 120d);
        AssertNumericCell(senders, "H31", 100d / 120d);
        Assert.Equal("praktijk-b.nl", CellText(senders, "A32"));

        // Twee datarijen, dan een lege rij, dan de tweede ranglijst.
        Assert.Equal("Afzenderdomeinen met het laagste afleverpercentage", CellText(senders, "A34"));
        Assert.Equal("Domein", CellText(senders, "A35"));
        Assert.Equal("praktijk-b.nl", CellText(senders, "A36"));

        Assert.Equal("A30:H32", senders.Worksheet.GetFirstChild<AutoFilter>()?.Reference?.Value);
    }

    [Fact]
    public void Export_WritesRecipientResponsesAndBounceCausesWithShare()
    {
        using var export = ExportWorkbook(Summary(), Context());
        WorksheetPart recipients = WorksheetPart(export.Document, "Ontvangers");

        Assert.Equal("Ontvangerdomeinen met de meeste problemen", CellText(recipients, "A29"));
        Assert.Equal("Ontvangerdomeinen met het hoogste probleempercentage", CellText(recipients, "A34"));

        Assert.Equal("SMTP-responsen", CellText(recipients, "A39"));
        Assert.Equal("Code", CellText(recipients, "A40"));
        Assert.Equal("% van totaal", CellText(recipients, "C40"));
        Assert.Equal("Omschrijving", CellText(recipients, "D40"));
        Assert.Equal("550", CellText(recipients, "A41"));
        AssertNumericCell(recipients, "B41", 18);
        AssertNumericCell(recipients, "C41", 0.75);
        Assert.Equal("Onbekende ontvanger", CellText(recipients, "D41"));
        AssertNumericCell(recipients, "C42", 0.25);

        Assert.Equal("Belangrijkste bounce-oorzaken", CellText(recipients, "A44"));
        Assert.Equal("Oorzaak", CellText(recipients, "A45"));
        Assert.Equal("Ongeldige ontvanger", CellText(recipients, "A46"));
        AssertNumericCell(recipients, "B46", 21);
    }

    [Fact]
    public void Export_AddsTwoChartsPerSheetPointingAtTheWrittenRows()
    {
        using var export = ExportWorkbook(Summary(), Context());

        WorksheetPart senders = WorksheetPart(export.Document, "Afzenders");
        DrawingsPart senderDrawings = Assert.IsType<DrawingsPart>(senders.DrawingsPart);
        Assert.Equal(2, senderDrawings.ChartParts.Count());
        Xdr.TwoCellAnchor[] anchors = senderDrawings.WorksheetDrawing!.Elements<Xdr.TwoCellAnchor>().ToArray();
        Assert.Equal(
        [
            (0, 9, 6, 27),
            (6, 9, 12, 27)
        ],
        anchors.Select(anchor =>
        (
            int.Parse(anchor.FromMarker!.ColumnId!.Text),
            int.Parse(anchor.FromMarker.RowId!.Text),
            int.Parse(anchor.ToMarker!.ColumnId!.Text),
            int.Parse(anchor.ToMarker.RowId!.Text)
        )).ToArray());

        string[] senderFormulas = ChartFormulas(senderDrawings);
        Assert.Contains("'Afzenders'!$A$31:$A$32", senderFormulas);
        Assert.Contains("'Afzenders'!$B$31:$B$32", senderFormulas);
        Assert.Contains("'Afzenders'!$A$36:$A$37", senderFormulas);
        Assert.Contains("'Afzenders'!$H$36:$H$37", senderFormulas);

        WorksheetPart recipients = WorksheetPart(export.Document, "Ontvangers");
        DrawingsPart recipientDrawings = Assert.IsType<DrawingsPart>(recipients.DrawingsPart);
        Assert.Equal(2, recipientDrawings.ChartParts.Count());
        string[] recipientFormulas = ChartFormulas(recipientDrawings);
        Assert.Contains("'Ontvangers'!$F$31:$F$32", recipientFormulas);
        Assert.Contains("'Ontvangers'!$G$36:$G$37", recipientFormulas);
    }

    [Fact]
    public void Export_WithoutData_WritesPlaceholdersAndStaysValid()
    {
        using var export = ExportWorkbook(EmptySummary(), Context() with
        {
            SenderFilter = null,
            RecipientFilter = "   "
        });

        WorksheetPart senders = WorksheetPart(export.Document, "Afzenders");
        Assert.Contains("geen extra filters", CellText(senders, "A2"), StringComparison.Ordinal);
        Assert.Equal("Geen resultaten in deze selectie.", CellText(senders, "A31"));
        AssertNumericCell(senders, "E7", 0);
        AssertNumericCell(senders, "K7", 0);
        Assert.Null(senders.DrawingsPart);

        WorksheetPart recipients = WorksheetPart(export.Document, "Ontvangers");
        Assert.Equal("SMTP-responsen", CellText(recipients, "A37"));
        Assert.Equal("Geen meldingen in deze selectie.", CellText(recipients, "A39"));
        Assert.Null(recipients.DrawingsPart);
        Assert.Empty(export.Document.WorkbookPart!.GetPartsOfType<ChartPart>());

        AssertValid(export.Document);
    }

    [Theory]
    [InlineData(null, null, "geen extra filters")]
    [InlineData(" praktijk ", null, "afzender bevat 'praktijk'")]
    [InlineData(null, "gmail.com", "ontvanger bevat 'gmail.com'")]
    [InlineData("a", "b", "afzender bevat 'a' en ontvanger bevat 'b'")]
    public void DescribeFilters_SummarisesTheActiveSelection(string? sender, string? recipient, string expected)
    {
        AnalysisReportContext context = Context() with
        {
            SenderFilter = sender,
            RecipientFilter = recipient
        };

        Assert.Equal(expected, context.DescribeFilters());
    }

    [Fact]
    public void DescribePeriod_UsesDutchDateOrder()
    {
        Assert.Equal("01-03-2026 t/m 31-03-2026", Context().DescribePeriod());
    }

    private static AnalysisReportContext Context() =>
        new(
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 31, 23, 59, 59),
            "praktijk",
            "gmail",
            25);

    private static MailLogInspectorAnalysisSummary Summary() =>
        new(
            200,
            150,
            20,
            30,
            [Breakdown("praktijk-a.nl", 120, 100, 8, 12), Breakdown("praktijk-b.nl", 80, 50, 12, 18)],
            [Breakdown("praktijk-b.nl", 80, 50, 12, 18), Breakdown("praktijk-a.nl", 120, 100, 8, 12)],
            [Breakdown("gmail.com", 90, 60, 10, 20), Breakdown("hotmail.com", 60, 40, 10, 10)],
            [Breakdown("hotmail.com", 60, 40, 10, 10), Breakdown("gmail.com", 90, 60, 10, 20)],
            [new MailLogInspectorValueMeaningCount("Ongeldige ontvanger", 21, "Adres bestaat niet"),
             new MailLogInspectorValueMeaningCount("Mailbox vol", 9, "Quota overschreden")],
            [new MailLogInspectorValueMeaningCount("550", 18, "Onbekende ontvanger"),
             new MailLogInspectorValueMeaningCount("452", 6, "Mailbox vol")]);

    private static MailLogInspectorAnalysisSummary EmptySummary() =>
        new(0, 0, 0, 0, [], [], [], [], [], []);

    private static MailLogInspectorBreakdownRow Breakdown(string key, int total, int delivered, int underway, int bounce) =>
        new(key, total, delivered, underway, bounce);

    private static TemporaryExport ExportWorkbook(
        MailLogInspectorAnalysisSummary summary,
        AnalysisReportContext context)
    {
        string path = Path.Combine(Path.GetTempPath(), $"mail-log-analysis-{Guid.NewGuid():N}.xlsx");
        AnalysisExcelExporter.Export(path, summary, context);
        return new TemporaryExport(path, SpreadsheetDocument.Open(path, false));
    }

    private static string[] ChartFormulas(DrawingsPart drawingsPart) =>
        drawingsPart.ChartParts
            .SelectMany(part => part.ChartSpace.Descendants<C.Formula>())
            .Select(formula => formula.Text ?? string.Empty)
            .ToArray();

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
