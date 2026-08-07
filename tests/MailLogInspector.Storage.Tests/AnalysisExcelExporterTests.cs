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
            Assert.Equal("Afgeleverd", CellText(sheet, "D6"));
            Assert.Equal("Afleverratio", CellText(sheet, "G6"));
            AssertNumericCell(sheet, "A7", 200);
            AssertNumericCell(sheet, "D7", 150);
            AssertNumericCell(sheet, "G7", 0.75);

            Assert.Equal("Onderweg", CellText(sheet, "A8"));
            Assert.Equal("Bounced", CellText(sheet, "D8"));
            Assert.Equal("Probleemratio", CellText(sheet, "G8"));
            AssertNumericCell(sheet, "A9", 20);
            AssertNumericCell(sheet, "D9", 30);
            AssertNumericCell(sheet, "G9", 0.25);

            Assert.True(FindCell(sheet, "A1")?.StyleIndex?.Value > 0);
            Assert.True(FindCell(sheet, "A7")?.StyleIndex?.Value > 0);
            Assert.Contains(sheet.Worksheet.Descendants<MergeCell>(), merge => merge.Reference?.Value == "A1:I1");
            Assert.Contains(sheet.Worksheet.Descendants<MergeCell>(), merge => merge.Reference?.Value == "A7:C7");
            Assert.Equal(9, sheet.Worksheet.GetFirstChild<Columns>()?.Elements<Column>().Count());
            Assert.Equal(OrientationValues.Landscape, sheet.Worksheet.GetFirstChild<PageSetup>()?.Orientation?.Value);
        }

        AssertValid(export.Document);
    }

    [Fact]
    public void Export_WritesSenderRankingsBelowTheChartArea()
    {
        using var export = ExportWorkbook(Summary(), Context());
        WorksheetPart senders = WorksheetPart(export.Document, "Afzenders");

        Assert.Equal("Beeld van de verzendende domeinen", CellText(senders, "A11"));

        // Beide ranglijsten staan naast elkaar op dezelfde rijen, net als in de app.
        Assert.Equal("Afzenderdomeinen met de meeste problemen", CellText(senders, "A31"));
        Assert.Equal("Afzenderdomeinen met het hoogste probleempercentage", CellText(senders, "F31"));

        foreach (string column in new[] { "A", "F" })
        {
            Assert.Equal("Domein", CellText(senders, $"{column}32"));
        }

        Assert.Equal("Totaal", CellText(senders, "B32"));
        Assert.Equal("Problemen", CellText(senders, "C32"));
        Assert.Equal("% probleem", CellText(senders, "D32"));
        Assert.Equal("Totaal", CellText(senders, "G32"));
        Assert.Equal("Problemen", CellText(senders, "H32"));
        Assert.Equal("% probleem", CellText(senders, "I32"));

        Assert.Equal("praktijk-a.nl", CellText(senders, "A33"));
        AssertNumericCell(senders, "B33", 120);
        AssertNumericCell(senders, "C33", 20);
        AssertNumericCell(senders, "D33", 20d / 120d);
        Assert.Equal("praktijk-b.nl", CellText(senders, "A34"));

        Assert.Equal("praktijk-b.nl", CellText(senders, "F33"));
        AssertNumericCell(senders, "G33", 80);
        AssertNumericCell(senders, "H33", 30);
        AssertNumericCell(senders, "I33", 30d / 80d);

        // De detailkolommen en % afgeleverd zijn bewust weggelaten.
        Assert.Null(FindCell(senders, "J32"));
        Assert.DoesNotContain("% afgeleverd", AllText(senders), StringComparison.Ordinal);
        Assert.Null(senders.Worksheet.GetFirstChild<AutoFilter>());
    }

    [Fact]
    public void Export_WritesRecipientResponsesAndBounceCausesWithShare()
    {
        using var export = ExportWorkbook(Summary(), Context());
        WorksheetPart recipients = WorksheetPart(export.Document, "Ontvangers");

        Assert.Equal("Ontvangerdomeinen met de meeste problemen", CellText(recipients, "A31"));
        Assert.Equal("Ontvangerdomeinen met het hoogste probleempercentage", CellText(recipients, "F31"));

        Assert.Equal("SMTP-responsen", CellText(recipients, "A36"));
        Assert.Equal("Code", CellText(recipients, "A37"));
        Assert.Equal("% van totaal", CellText(recipients, "C37"));
        Assert.Equal("Omschrijving", CellText(recipients, "D37"));
        Assert.Equal("550", CellText(recipients, "A38"));
        AssertNumericCell(recipients, "B38", 18);
        AssertNumericCell(recipients, "C38", 0.75);
        Assert.Equal("Onbekende ontvanger", CellText(recipients, "D38"));
        AssertNumericCell(recipients, "C39", 0.25);

        // De omschrijving loopt door over de resterende kolommen zodat ze leesbaar blijft.
        Assert.Contains(recipients.Worksheet.Descendants<MergeCell>(), merge => merge.Reference?.Value == "D38:I38");

        Assert.Equal("Belangrijkste bounce-oorzaken", CellText(recipients, "A41"));
        Assert.Equal("Oorzaak", CellText(recipients, "A42"));
        Assert.Equal("Ongeldige ontvanger", CellText(recipients, "A43"));
        AssertNumericCell(recipients, "B43", 21);
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
            (0, 11, 4, 29),
            (5, 11, 9, 29)
        ],
        anchors.Select(anchor =>
        (
            int.Parse(anchor.FromMarker!.ColumnId!.Text),
            int.Parse(anchor.FromMarker.RowId!.Text),
            int.Parse(anchor.ToMarker!.ColumnId!.Text),
            int.Parse(anchor.ToMarker.RowId!.Text)
        )).ToArray());

        // De linkergrafiek leest de linkertabel, de rechtergrafiek de rechtertabel.
        string[] senderFormulas = ChartFormulas(senderDrawings);
        Assert.Contains("'Afzenders'!$A$33:$A$34", senderFormulas);
        Assert.Contains("'Afzenders'!$C$33:$C$34", senderFormulas);
        Assert.Contains("'Afzenders'!$F$33:$F$34", senderFormulas);
        Assert.Contains("'Afzenders'!$I$33:$I$34", senderFormulas);

        WorksheetPart recipients = WorksheetPart(export.Document, "Ontvangers");
        DrawingsPart recipientDrawings = Assert.IsType<DrawingsPart>(recipients.DrawingsPart);
        Assert.Equal(2, recipientDrawings.ChartParts.Count());
        string[] recipientFormulas = ChartFormulas(recipientDrawings);
        Assert.Contains("'Ontvangers'!$C$33:$C$34", recipientFormulas);
        Assert.Contains("'Ontvangers'!$I$33:$I$34", recipientFormulas);
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
        Assert.Equal("Geen resultaten in deze selectie.", CellText(senders, "A33"));
        Assert.Equal("Geen resultaten in deze selectie.", CellText(senders, "F33"));
        AssertNumericCell(senders, "G7", 0);
        AssertNumericCell(senders, "G9", 0);
        Assert.Null(senders.DrawingsPart);

        WorksheetPart recipients = WorksheetPart(export.Document, "Ontvangers");
        Assert.Equal("SMTP-responsen", CellText(recipients, "A35"));
        Assert.Equal("Geen meldingen in deze selectie.", CellText(recipients, "A37"));
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

    [Fact]
    public void Export_NeverOverlapsMergedRanges()
    {
        // Overlappende merges laat Excel het bestand "repareren"; de schemavalidatie ziet dat niet.
        foreach (MailLogInspectorAnalysisSummary summary in new[] { Summary(), EmptySummary() })
        {
            using var export = ExportWorkbook(summary, Context());
            foreach (string name in SheetNames(export.Document))
            {
                (int Left, int Top, int Right, int Bottom)[] ranges =
                    WorksheetPart(export.Document, name).Worksheet.Descendants<MergeCell>()
                        .Select(merge => ParseRange(merge.Reference!.Value!))
                        .ToArray();

                for (int first = 0; first < ranges.Length; first++)
                {
                    for (int second = first + 1; second < ranges.Length; second++)
                    {
                        bool overlaps =
                            ranges[first].Left <= ranges[second].Right &&
                            ranges[second].Left <= ranges[first].Right &&
                            ranges[first].Top <= ranges[second].Bottom &&
                            ranges[second].Top <= ranges[first].Bottom;

                        Assert.False(overlaps, $"Blad {name}: {ranges[first]} overlapt {ranges[second]}.");
                    }
                }
            }
        }
    }

    private static (int Left, int Top, int Right, int Bottom) ParseRange(string reference)
    {
        string[] corners = reference.Split(':');
        (int Column, int Row) start = ParseReference(corners[0]);
        (int Column, int Row) end = ParseReference(corners[^1]);
        return (start.Column, start.Row, end.Column, end.Row);
    }

    private static (int Column, int Row) ParseReference(string reference)
    {
        string letters = new(reference.TakeWhile(char.IsLetter).ToArray());
        int column = letters.Aggregate(0, (value, letter) => (value * 26) + (letter - 'A' + 1));
        return (column, int.Parse(reference[letters.Length..]));
    }

    private static string CellText(WorksheetPart worksheetPart, string reference) =>
        FindCell(worksheetPart, reference)?.CellValue?.Text ?? string.Empty;

    private static string AllText(WorksheetPart worksheetPart) =>
        string.Join('|', worksheetPart.Worksheet.Descendants<Cell>()
            .Select(cell => cell.CellValue?.Text)
            .Where(text => !string.IsNullOrEmpty(text)));

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
