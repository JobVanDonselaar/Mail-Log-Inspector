using System.IO;
using DocumentFormat.OpenXml.Packaging;
using MailLogInspector.Core;
using S = DocumentFormat.OpenXml.Spreadsheet;
using static MailLogInspector.App.ExcelReportKit;

namespace MailLogInspector.App;

public sealed record LongestDeliveredExportContext(
	DateTime FromDate,
	DateTime ThroughDate,
	string? SenderFilter,
	string? RecipientFilter,
	int TopCount);

public sealed record LongestDeliveredExportEntry(
	int Rank,
	MailLogInspectorLongestDeliveredMail Mail,
	MailLogInspectorMailHistory History,
	string HistoryNote);

public static class LongestDeliveredExcelExporter
{
	public static void Export(
		string path,
		LongestDeliveredExportContext context,
		IReadOnlyList<LongestDeliveredExportEntry> entries)
	{
		string? directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		using SpreadsheetDocument document = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
		document.PackageProperties.Title = "Mail Log Inspector - Langste aflevertijden";
		document.PackageProperties.Subject = $"Top {context.TopCount} langste aflevertijden met archiefhistorie";
		document.PackageProperties.Creator = "Mail Log Inspector";

		WorkbookPart workbookPart = document.AddWorkbookPart();
		workbookPart.Workbook = new S.Workbook();
		AddWorkbookStyles(workbookPart);
		S.Sheets sheets = workbookPart.Workbook.AppendChild(new S.Sheets());

		AddSummarySheet(workbookPart, sheets, context, entries, 1);
		AddHistorySheet(workbookPart, sheets, entries, 2);
		AddContextSheet(workbookPart, sheets, context, entries.Count, 3);

		workbookPart.Workbook.Save();
	}

	private static void AddSummarySheet(
		WorkbookPart workbookPart,
		S.Sheets sheets,
		LongestDeliveredExportContext context,
		IReadOnlyList<LongestDeliveredExportEntry> entries,
		uint sheetId)
	{
		WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
		var sheetData = new S.SheetData();
		worksheetPart.Worksheet = new S.Worksheet(
			FitToPageProperties(),
			FrozenView(5, "A6"),
			new S.SheetFormatProperties { DefaultRowHeight = 18 },
			new S.Columns(
				Column(1, 7), Column(2, 17), Column(3, 17), Column(4, 14), Column(5, 34), Column(6, 34),
				Column(7, 40), Column(8, 12), Column(9, 13), Column(10, 14), Column(11, 46)),
			sheetData);
		sheets.Append(new S.Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = "Top langste mails" });

		sheetData.Append(StyledSpanRow(1, 1, 11, "Mail Log Inspector - Top langste aflevertijden", StyleTitle, 30));
		sheetData.Append(StyledSpanRow(2, 1, 11,
			$"Periode: {context.FromDate:dd-MM-yyyy} t/m {context.ThroughDate:dd-MM-yyyy} | Top: {context.TopCount}",
			StyleSubtitle, 24));
		sheetData.Append(StyledSpanRow(3, 1, 11,
			$"Filters: afzender={CleanFilter(context.SenderFilter)}, ontvanger={CleanFilter(context.RecipientFilter)} | Gegenereerd: {DateTime.Now:dd-MM-yyyy HH:mm}",
			StyleNote, 24));
		sheetData.Append(CreateSparseRow(4));
		sheetData.Append(CreateStyledStringRow(5, StyleTableHeader,
			"#", "Accepted", "Afgeleverd", "Duur (sec)", "Duur", "Afzender", "Ontvanger",
			"Tracking", "SMTP code", "Bron", "Waarom traag?"));

		uint rowIndex = 6;
		foreach (LongestDeliveredExportEntry entry in entries)
		{
			uint bodyStyle = rowIndex % 2 == 1 ? StyleBodyAlternate : StyleBody;
			uint numberStyle = rowIndex % 2 == 1 ? StyleNumberAlternate : StyleNumber;
			sheetData.Append(CreateSparseRow(rowIndex,
				NumberCell($"A{rowIndex}", entry.Rank, numberStyle),
				DateCell($"B{rowIndex}", entry.Mail.AcceptedAt, StyleDateTime),
				DateCell($"C{rowIndex}", entry.Mail.DeliveredAt, StyleDateTime),
				NumberCell($"D{rowIndex}", entry.Mail.DurationSeconds, numberStyle),
				StringCell($"E{rowIndex}", TimeSpan.FromSeconds(Math.Max(0, entry.Mail.DurationSeconds)).ToString(), bodyStyle),
				StringCell($"F{rowIndex}", entry.Mail.Sender, bodyStyle),
				StringCell($"G{rowIndex}", entry.Mail.Recipient, bodyStyle),
				StringCell($"H{rowIndex}", entry.Mail.TrackingId, bodyStyle),
				StringCell($"I{rowIndex}", entry.Mail.ResponseCode?.ToString() ?? "-", bodyStyle),
				StringCell($"J{rowIndex}", entry.Mail.SourceFileName, bodyStyle),
				StringCell($"K{rowIndex}", entry.HistoryNote, bodyStyle)));
			rowIndex++;
		}

		worksheetPart.Worksheet.Append(
			new S.AutoFilter { Reference = $"A5:K{Math.Max(5, entries.Count + 5)}" },
			MergeRanges("A1:K1", "A2:K2", "A3:K3"),
			ReportPageMargins(),
			LandscapePageSetup());
	}

	private static void AddHistorySheet(
		WorkbookPart workbookPart,
		S.Sheets sheets,
		IReadOnlyList<LongestDeliveredExportEntry> entries,
		uint sheetId)
	{
		WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
		var sheetData = new S.SheetData();
		worksheetPart.Worksheet = new S.Worksheet(
			FitToPageProperties(),
			FrozenView(5, "A6"),
			new S.SheetFormatProperties { DefaultRowHeight = 18 },
			new S.Columns(
				Column(1, 7), Column(2, 14), Column(3, 34), Column(4, 34), Column(5, 17), Column(6, 17),
				Column(7, 12), Column(8, 9), Column(9, 25), Column(10, 64), Column(11, 13), Column(12, 36), Column(13, 40)),
			sheetData);
		sheets.Append(new S.Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = "Historie archief" });

		sheetData.Append(StyledSpanRow(1, 1, 13, "Volledige historie uit archief per geselecteerde mail", StyleTitle, 30));
		sheetData.Append(StyledSpanRow(2, 1, 13, "Elke regel hieronder is een logregel uit een gearchiveerd bronrapport.", StyleNote, 24));
		sheetData.Append(CreateSparseRow(3));
		sheetData.Append(CreateSparseRow(4));
		sheetData.Append(CreateStyledStringRow(5, StyleTableHeader,
			"#", "Tracking", "Afzender", "Ontvanger", "Accepted", "Afgerond",
			"Status", "Code", "Code uitleg", "Servermelding", "Pogingen", "Bounceklasse", "Archiefbron"));

		uint rowIndex = 6;
		foreach (LongestDeliveredExportEntry entry in entries)
		{
			if (!entry.History.HasAttempts)
			{
				uint bodyStyle = rowIndex % 2 == 1 ? StyleBodyAlternate : StyleBody;
				sheetData.Append(CreateSparseRow(rowIndex,
					NumberCell($"A{rowIndex}", entry.Rank, StyleNumber),
					StringCell($"B{rowIndex}", entry.Mail.TrackingId, bodyStyle),
					StringCell($"C{rowIndex}", entry.Mail.Sender, bodyStyle),
					StringCell($"D{rowIndex}", entry.Mail.Recipient, bodyStyle),
					StringCell($"E{rowIndex}", "-", bodyStyle),
					StringCell($"F{rowIndex}", "-", bodyStyle),
					StringCell($"G{rowIndex}", "Geen archiefregels gevonden", bodyStyle),
					StringCell($"H{rowIndex}", "-", bodyStyle),
					StringCell($"I{rowIndex}", "-", bodyStyle),
					StringCell($"J{rowIndex}", "-", bodyStyle),
					StringCell($"K{rowIndex}", "-", bodyStyle),
					StringCell($"L{rowIndex}", "-", bodyStyle),
					StringCell($"M{rowIndex}", "-", bodyStyle)));
				rowIndex++;
				continue;
			}

			foreach (MailLogInspectorMailHistoryAttempt attempt in entry.History.Attempts)
			{
				uint bodyStyle = rowIndex % 2 == 1 ? StyleBodyAlternate : StyleBody;
				uint numberStyle = rowIndex % 2 == 1 ? StyleNumberAlternate : StyleNumber;
				sheetData.Append(CreateSparseRow(rowIndex,
					NumberCell($"A{rowIndex}", entry.Rank, numberStyle),
					StringCell($"B{rowIndex}", entry.Mail.TrackingId, bodyStyle),
					StringCell($"C{rowIndex}", attempt.Sender, bodyStyle),
					StringCell($"D{rowIndex}", attempt.Recipient, bodyStyle),
					DateCell($"E{rowIndex}", attempt.AcceptedAt, StyleDateTime),
					DateCell($"F{rowIndex}", attempt.DeliveredAt, StyleDateTime),
					StringCell($"G{rowIndex}", attempt.StatusDisplay, bodyStyle),
					StringCell($"H{rowIndex}", attempt.ResponseCodeDisplay, bodyStyle),
					StringCell($"I{rowIndex}", attempt.ResponseCodeMeaning, bodyStyle),
					StringCell($"J{rowIndex}", attempt.ResponseMessage, bodyStyle),
					StringCell($"K{rowIndex}", attempt.TriesDisplay, bodyStyle),
					StringCell($"L{rowIndex}", string.IsNullOrWhiteSpace(attempt.BounceClass) ? "-" : attempt.BounceClass, bodyStyle),
					StringCell($"M{rowIndex}", attempt.SourceFileName, bodyStyle)));
				rowIndex++;
			}
		}

		worksheetPart.Worksheet.Append(
			new S.AutoFilter { Reference = $"A5:M{Math.Max(5, rowIndex - 1)}" },
			MergeRanges("A1:M1", "A2:M2"),
			ReportPageMargins(),
			LandscapePageSetup());
	}

	private static void AddContextSheet(
		WorkbookPart workbookPart,
		S.Sheets sheets,
		LongestDeliveredExportContext context,
		int actualCount,
		uint sheetId)
	{
		WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
		var sheetData = new S.SheetData();
		worksheetPart.Worksheet = new S.Worksheet(
			FitToPageProperties(),
			new S.SheetFormatProperties { DefaultRowHeight = 18 },
			new S.Columns(Column(1, 30), Column(2, 80)),
			sheetData);
		sheets.Append(new S.Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = "Context" });

		sheetData.Append(StyledSpanRow(1, 1, 2, "Rapportcontext", StyleTitle, 28));
		sheetData.Append(CreateSparseRow(3,
			StringCell("A3", "Van", StyleTableHeader),
			StringCell("B3", context.FromDate.ToString("dd-MM-yyyy"), StyleBody)));
		sheetData.Append(CreateSparseRow(4,
			StringCell("A4", "Tot en met", StyleTableHeader),
			StringCell("B4", context.ThroughDate.ToString("dd-MM-yyyy"), StyleBody)));
		sheetData.Append(CreateSparseRow(5,
			StringCell("A5", "Afzenderfilter", StyleTableHeader),
			StringCell("B5", CleanFilter(context.SenderFilter), StyleBody)));
		sheetData.Append(CreateSparseRow(6,
			StringCell("A6", "Ontvangerfilter", StyleTableHeader),
			StringCell("B6", CleanFilter(context.RecipientFilter), StyleBody)));
		sheetData.Append(CreateSparseRow(7,
			StringCell("A7", "Top limiet", StyleTableHeader),
			NumberCell("B7", context.TopCount, StyleNumber)));
		sheetData.Append(CreateSparseRow(8,
			StringCell("A8", "Aantal records", StyleTableHeader),
			NumberCell("B8", actualCount, StyleNumber)));
		sheetData.Append(CreateSparseRow(9,
			StringCell("A9", "Gegenereerd op", StyleTableHeader),
			StringCell("B9", DateTime.Now.ToString("dd-MM-yyyy HH:mm"), StyleBody)));

		worksheetPart.Worksheet.Append(MergeRanges("A1:B1"), ReportPageMargins(), LandscapePageSetup());
	}

	private static string CleanFilter(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
