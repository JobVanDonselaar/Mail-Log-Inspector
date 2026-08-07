using System.Globalization;
using System.Net;
using System.Text;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

/// <summary>
/// Bouwt onderwerp, HTML-body en platte tekst voor een bouncemelding. Welke blokken meegaan
/// bepaalt <see cref="BounceNotificationContentOptions"/>, zodat de gebruiker kiest tussen een
/// korte samenvatting en een volledig rapport.
/// </summary>
public static class BounceNotificationContentBuilder
{
    /// <summary>Standaard aantal detailregels in de mail zelf; de rest staat in de Excel-bijlage.</summary>
    public const int MaxInlineRows = BounceNotificationContentOptions.DefaultMaxDetailRows;

    public static string BuildSubject(
        string subjectTemplate,
        MailLogInspectorSenderBounceReport report,
        DateTime reportDate)
    {
        string template = string.IsNullOrWhiteSpace(subjectTemplate)
            ? BounceNotificationSettings.DefaultSubjectTemplate
            : subjectTemplate;

        return ApplyPlaceholders(template, report, reportDate).Trim();
    }

    public static string BuildHtmlBody(
        MailLogInspectorSenderBounceReport report,
        DateTime reportDate,
        string? sourceFileName,
        bool hasAttachment)
    {
        return BuildHtmlBody(
            report,
            reportDate,
            sourceFileName,
            hasAttachment,
            BounceNotificationContentOptions.Default);
    }

    public static string BuildHtmlBody(
        MailLogInspectorSenderBounceReport report,
        DateTime reportDate,
        string? sourceFileName,
        bool hasAttachment,
        BounceNotificationContentOptions options)
    {
        BounceNotificationContentOptions content =
            (options ?? BounceNotificationContentOptions.Default).EnsureNotEmpty();
        int maxRows = content.ResolveMaxDetailRows();

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\" /></head>");
        html.Append("<body style=\"margin:0;padding:20px;background:#f3f5f8;font-family:Segoe UI,Arial,sans-serif;color:#1f2937;\">");
        html.Append("<div style=\"max-width:820px;margin:0 auto;\">");

        html.Append("<div style=\"background:#1f5d8c;color:#ffffff;padding:18px 22px;border-radius:6px 6px 0 0;\">");
        html.Append("<div style=\"font-size:19px;font-weight:600;\">Bounce-overzicht</div>");
        html.Append($"<div style=\"font-size:13px;opacity:0.9;margin-top:4px;\">{Encode(report.SenderAddress)} &middot; {Encode(reportDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture))}</div>");
        html.Append("</div>");

        html.Append("<div style=\"background:#ffffff;border:1px solid #d8e0ea;border-top:none;padding:22px;border-radius:0 0 6px 6px;\">");

        if (!string.IsNullOrWhiteSpace(content.IntroText))
        {
            html.Append("<div style=\"font-size:13px;line-height:1.55;margin:0 0 18px 0;\">");
            html.Append(RenderParagraphs(ApplyPlaceholders(content.IntroText!, report, reportDate)));
            html.Append("</div>");
        }

        if (content.IncludeKpiSummary)
        {
            AppendKpiRow(html, report);
        }

        if (content.IncludeReasonBreakdown)
        {
            AppendBreakdownSection(
                html,
                "Bounce-oorzaken",
                report.ReasonBreakdown.Select(entry => (entry.Reason, entry.Count)).ToList(),
                report.BounceCount);
        }

        if (content.IncludeRecipientDomainBreakdown)
        {
            AppendBreakdownSection(
                html,
                "Ontvangende domeinen",
                report.RecipientDomainBreakdown.Select(entry => (entry.Domain, entry.Count)).ToList(),
                report.BounceCount);
        }

        if (content.IncludeDetailTable)
        {
            AppendDetailTable(html, report, maxRows);
        }

        bool attachmentIncluded = hasAttachment && content.IncludeExcelAttachment;
        string? note = BuildRowNote(report, content, maxRows, attachmentIncluded);
        if (note is not null)
        {
            html.Append($"<p style=\"font-size:12px;color:#637386;margin:14px 0 0 0;\">{Encode(note)}</p>");
        }

        if (content.IncludeSourceFileName && !string.IsNullOrWhiteSpace(sourceFileName))
        {
            html.Append($"<p style=\"font-size:11px;color:#8a99ab;margin:10px 0 0 0;\">Bron: {Encode(sourceFileName!)}</p>");
        }

        if (!string.IsNullOrWhiteSpace(content.FooterText))
        {
            html.Append("<div style=\"font-size:11px;color:#8a99ab;margin:16px 0 0 0;border-top:1px solid #edf1f5;padding-top:12px;\">");
            html.Append(RenderParagraphs(ApplyPlaceholders(content.FooterText!, report, reportDate)));
            html.Append("</div>");
        }

        html.Append("</div></div></body></html>");
        return html.ToString();
    }

    public static string BuildPlainTextBody(
        MailLogInspectorSenderBounceReport report,
        DateTime reportDate,
        string? sourceFileName)
    {
        return BuildPlainTextBody(
            report,
            reportDate,
            sourceFileName,
            hasAttachment: true,
            BounceNotificationContentOptions.Default);
    }

    public static string BuildPlainTextBody(
        MailLogInspectorSenderBounceReport report,
        DateTime reportDate,
        string? sourceFileName,
        bool hasAttachment,
        BounceNotificationContentOptions options)
    {
        BounceNotificationContentOptions content =
            (options ?? BounceNotificationContentOptions.Default).EnsureNotEmpty();
        int maxRows = content.ResolveMaxDetailRows();

        var text = new StringBuilder();
        text.AppendLine("BOUNCE-OVERZICHT");
        text.AppendLine($"Afzender: {report.SenderAddress}");
        text.AppendLine($"Datum: {reportDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}");
        text.AppendLine();

        if (!string.IsNullOrWhiteSpace(content.IntroText))
        {
            text.AppendLine(ApplyPlaceholders(content.IntroText!, report, reportDate).Trim());
            text.AppendLine();
        }

        if (content.IncludeKpiSummary)
        {
            text.AppendLine($"Totaal verzonden : {report.TotalCount:N0}");
            text.AppendLine($"Afgeleverd       : {report.DeliveredCount:N0} ({report.DeliveredPercent:N1}%)");
            text.AppendLine($"Onderweg         : {report.UnderwayCount:N0}");
            text.AppendLine($"Bounces          : {report.BounceCount:N0} ({report.BouncePercent:N1}%)");
            text.AppendLine();
        }

        if (content.IncludeReasonBreakdown && report.ReasonBreakdown.Count > 0)
        {
            text.AppendLine("BOUNCE-OORZAKEN");
            foreach ((string reason, int count) in report.ReasonBreakdown)
            {
                text.AppendLine($"  {reason}: {count:N0}");
            }

            text.AppendLine();
        }

        if (content.IncludeRecipientDomainBreakdown && report.RecipientDomainBreakdown.Count > 0)
        {
            text.AppendLine("ONTVANGENDE DOMEINEN");
            foreach ((string domain, int count) in report.RecipientDomainBreakdown.Take(10))
            {
                text.AppendLine($"  {domain}: {count:N0}");
            }

            text.AppendLine();
        }

        if (content.IncludeDetailTable)
        {
            text.AppendLine("DETAILS");
            foreach (MailLogInspectorBounceRow row in report.Bounces.Take(maxRows))
            {
                text.AppendLine($"  {row.AcceptedAtDisplay} | {row.Recipient} | {row.ReasonDisplay} | {row.ResponseDisplay}");
            }

            text.AppendLine();
        }

        bool attachmentIncluded = hasAttachment && content.IncludeExcelAttachment;
        string? note = BuildRowNote(report, content, maxRows, attachmentIncluded);
        if (note is not null)
        {
            text.AppendLine(note);
            text.AppendLine();
        }

        if (content.IncludeSourceFileName && !string.IsNullOrWhiteSpace(sourceFileName))
        {
            text.AppendLine($"Bron: {sourceFileName}");
            text.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(content.FooterText))
        {
            text.AppendLine(ApplyPlaceholders(content.FooterText!, report, reportDate).Trim());
        }

        return text.ToString();
    }

    /// <summary>Bestandsnaam voor de Excel-bijlage; veilig voor het bestandssysteem en voor mailclients.</summary>
    public static string BuildAttachmentFileName(MailLogInspectorSenderBounceReport report, DateTime reportDate)
    {
        var safeSender = new string(report.SenderAddress
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '.' or '_' ? character : '-')
            .ToArray());

        return $"Bounces-{safeSender}-{reportDate:yyyy-MM-dd}.xlsx";
    }

    /// <summary>
    /// Vervangt de plaatshouders die zowel in het onderwerp als in de vrije teksten bruikbaar zijn.
    /// </summary>
    public static string ApplyPlaceholders(
        string value,
        MailLogInspectorSenderBounceReport report,
        DateTime reportDate)
    {
        return value
            .Replace("{sender}", report.SenderAddress, StringComparison.OrdinalIgnoreCase)
            .Replace("{domain}", report.SenderDomain, StringComparison.OrdinalIgnoreCase)
            .Replace("{count}", report.BounceCount.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", reportDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Toelichting over ingekorte detailregels en de eventuele bijlage.</summary>
    private static string? BuildRowNote(
        MailLogInspectorSenderBounceReport report,
        BounceNotificationContentOptions content,
        int maxRows,
        bool attachmentIncluded)
    {
        bool truncated = content.IncludeDetailTable && report.Bounces.Count > maxRows;

        if (truncated && attachmentIncluded)
        {
            return $"Deze mail toont de eerste {maxRows:N0} regels. De volledige lijst van {report.Bounces.Count:N0} bounces staat in de Excel-bijlage.";
        }

        if (truncated)
        {
            return $"Deze mail toont de eerste {maxRows:N0} van {report.Bounces.Count:N0} bounces.";
        }

        if (attachmentIncluded)
        {
            return "De volledige lijst staat ook in de Excel-bijlage.";
        }

        return null;
    }

    /// <summary>Zet vrije tekst met regeleindes om naar veilige HTML-alinea's.</summary>
    private static string RenderParagraphs(string value)
    {
        string[] paragraphs = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        var html = new StringBuilder();
        foreach (string paragraph in paragraphs)
        {
            string encoded = Encode(paragraph.Trim()).Replace("\n", "<br />", StringComparison.Ordinal);
            html.Append($"<p style=\"margin:0 0 10px 0;\">{encoded}</p>");
        }

        return html.ToString();
    }

    private static void AppendKpiRow(StringBuilder html, MailLogInspectorSenderBounceReport report)
    {
        html.Append("<table role=\"presentation\" style=\"width:100%;border-collapse:separate;border-spacing:8px 0;margin:0 0 18px 0;\"><tr>");
        AppendKpiCell(html, "Verzonden", report.TotalCount.ToString("N0", CultureInfo.InvariantCulture), "#eef5fc", "#1f5d8c");
        AppendKpiCell(html, "Afgeleverd", report.DeliveredCount.ToString("N0", CultureInfo.InvariantCulture), "#eaf5ef", "#2f855a");
        AppendKpiCell(html, "% Afgeleverd", $"{report.DeliveredPercent.ToString("N1", CultureInfo.InvariantCulture)}%", "#eaf5ef", "#2f855a");
        AppendKpiCell(html, "Bounces", report.BounceCount.ToString("N0", CultureInfo.InvariantCulture), "#fdece9", "#c83b2b");
        AppendKpiCell(html, "% Bounce", $"{report.BouncePercent.ToString("N1", CultureInfo.InvariantCulture)}%", "#fdece9", "#c83b2b");
        html.Append("</tr></table>");
    }

    private static void AppendKpiCell(StringBuilder html, string label, string value, string background, string color)
    {
        html.Append($"<td style=\"background:{background};padding:12px 10px;border-radius:6px;text-align:center;width:20%;\">");
        html.Append($"<div style=\"font-size:11px;color:#637386;text-transform:uppercase;letter-spacing:0.4px;\">{Encode(label)}</div>");
        html.Append($"<div style=\"font-size:20px;font-weight:600;color:{color};margin-top:4px;\">{Encode(value)}</div>");
        html.Append("</td>");
    }

    private static void AppendBreakdownSection(
        StringBuilder html,
        string title,
        IReadOnlyList<(string Label, int Count)> entries,
        int total)
    {
        if (entries.Count == 0)
        {
            return;
        }

        html.Append($"<div style=\"font-size:14px;font-weight:600;margin:18px 0 8px 0;\">{Encode(title)}</div>");
        html.Append("<table style=\"width:100%;border-collapse:collapse;font-size:12px;\">");

        foreach ((string label, int count) in entries.Take(10))
        {
            double percent = total > 0 ? count * 100.0 / total : 0.0;
            int barWidth = (int)Math.Round(Math.Clamp(percent, 0, 100));

            html.Append("<tr>");
            html.Append($"<td style=\"padding:4px 8px 4px 0;width:38%;\">{Encode(label)}</td>");
            html.Append("<td style=\"padding:4px 8px 4px 0;\">");
            html.Append($"<div style=\"background:#e7edf3;height:12px;border-radius:2px;\"><div style=\"background:#c83b2b;height:12px;width:{barWidth}%;border-radius:2px;\"></div></div>");
            html.Append("</td>");
            html.Append($"<td style=\"padding:4px 0;text-align:right;width:110px;white-space:nowrap;\">{count.ToString("N0", CultureInfo.InvariantCulture)} ({percent.ToString("N1", CultureInfo.InvariantCulture)}%)</td>");
            html.Append("</tr>");
        }

        html.Append("</table>");
    }

    private static void AppendDetailTable(
        StringBuilder html,
        MailLogInspectorSenderBounceReport report,
        int maxRows)
    {
        html.Append("<div style=\"font-size:14px;font-weight:600;margin:20px 0 8px 0;\">Gebouncede berichten</div>");
        html.Append("<table style=\"width:100%;border-collapse:collapse;font-size:12px;\">");
        html.Append("<thead><tr style=\"background:#f8fafc;\">");
        html.Append("<th style=\"text-align:left;padding:7px 8px;border-bottom:1px solid #d8e0ea;font-weight:600;\">Tijdstip</th>");
        html.Append("<th style=\"text-align:left;padding:7px 8px;border-bottom:1px solid #d8e0ea;font-weight:600;\">Ontvanger</th>");
        html.Append("<th style=\"text-align:left;padding:7px 8px;border-bottom:1px solid #d8e0ea;font-weight:600;\">Oorzaak</th>");
        html.Append("<th style=\"text-align:right;padding:7px 8px;border-bottom:1px solid #d8e0ea;font-weight:600;\">Code</th>");
        html.Append("</tr></thead><tbody>");

        int index = 0;
        foreach (MailLogInspectorBounceRow row in report.Bounces.Take(maxRows))
        {
            string background = index % 2 == 0 ? "#ffffff" : "#f8fafc";
            html.Append($"<tr style=\"background:{background};\">");
            html.Append($"<td style=\"padding:6px 8px;border-bottom:1px solid #edf1f5;white-space:nowrap;\">{Encode(row.AcceptedAtDisplay)}</td>");
            html.Append($"<td style=\"padding:6px 8px;border-bottom:1px solid #edf1f5;\">{Encode(row.Recipient)}</td>");
            html.Append($"<td style=\"padding:6px 8px;border-bottom:1px solid #edf1f5;\">{Encode(row.ReasonDisplay)}</td>");
            html.Append($"<td style=\"padding:6px 8px;border-bottom:1px solid #edf1f5;text-align:right;\">{Encode(row.ResponseDisplay)}</td>");
            html.Append("</tr>");
            index++;
        }

        html.Append("</tbody></table>");
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
