using System.Globalization;
using Microsoft.VisualBasic.FileIO;

namespace MailLogInspector.Core;

public static class SmtpCsvReader
{
    private static readonly string[] DateFormats =
    [
        "M/d/yyyy h:mmtt",
        "M/d/yyyy hh:mmtt",
        "MM/dd/yyyy h:mmtt",
        "MM/dd/yyyy hh:mmtt",
        "M/d/yyyy h:mm:sstt",
        "M/d/yyyy hh:mm:sstt",
        "MM/dd/yyyy h:mm:sstt",
        "MM/dd/yyyy hh:mm:sstt"
    ];

    public static IEnumerable<SmtpLogEntry> Enumerate(
        string path,
        Action<SmtpParseError>? onError = null,
        CancellationToken cancellationToken = default)
    {
        using var parser = CreateParser(path);
        var headers = ReadHeaders(parser);
        var index = BuildHeaderIndex(headers);
        var rowNumber = 1;

        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;

            string[]? fields;
            try
            {
                fields = parser.ReadFields();
            }
            catch (MalformedLineException ex)
            {
                onError?.Invoke(new SmtpParseError(rowNumber, ex.Message, parser.ErrorLine));
                continue;
            }

            if (fields is null || fields.Length == 0)
            {
                continue;
            }

            if (fields.Length != headers.Length)
            {
                onError?.Invoke(new SmtpParseError(rowNumber, $"Expected {headers.Length} columns but found {fields.Length}."));
                continue;
            }

            if (!TryBuildEntry(rowNumber, fields, index, out var entry, out var error))
            {
                onError?.Invoke(new SmtpParseError(rowNumber, error));
                continue;
            }

            yield return entry;
        }
    }

    private static TextFieldParser CreateParser(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var reader = new StreamReader(stream);
        var parser = new TextFieldParser(reader);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;
        return parser;
    }

    private static string[] ReadHeaders(TextFieldParser parser)
    {
        if (parser.EndOfData)
        {
            throw new InvalidDataException("CSV file is empty.");
        }

        var headers = parser.ReadFields();
        if (headers is null || headers.Length == 0)
        {
            throw new InvalidDataException("CSV file does not contain a header row.");
        }

        return headers;
    }

    private static Dictionary<string, int> BuildHeaderIndex(string[] headers)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
        {
            index[NormalizeHeader(headers[i])] = i;
        }

        return index;
    }

    private static bool TryBuildEntry(int rowNumber, string[] fields, IReadOnlyDictionary<string, int> index, out SmtpLogEntry entry, out string error)
    {
        string GetRequired(string name)
        {
            if (!index.TryGetValue(NormalizeHeader(name), out var i))
            {
                throw new KeyNotFoundException($"Missing required column '{name}'.");
            }

            return fields[i] ?? string.Empty;
        }

        string GetOptional(string name)
        {
            return index.TryGetValue(NormalizeHeader(name), out var i)
                ? fields[i] ?? string.Empty
                : string.Empty;
        }

        try
        {
            var acceptedRaw = GetRequired("Date accepted");
            var deliveredRaw = GetRequired("Date delivered");
            var mailFrom = GetRequired("Mail from");
            var recipient = GetRequired("Recipient");
            var status = GetRequired("Status");
            var responseCode = GetRequired("Response code");
            var responseMessage = GetOptional("Response message");
            var bounceClass = GetOptional("Bounce class");
            var triesRaw = GetRequired("Tries");
            var senderId = GetOptional("Sender id");
            var trackingId = GetRequired("Tracking id");
            var campaignId = GetOptional("Campaign id");

            entry = new SmtpLogEntry(
                RowNumber: rowNumber,
                AcceptedAt: ParseDate(acceptedRaw),
                DeliveredAt: ParseDate(deliveredRaw),
                MailFrom: mailFrom,
                MailFromDomain: ExtractDomain(mailFrom),
                Recipient: recipient,
                RecipientDomain: ExtractDomain(recipient),
                Status: status,
                ResponseCode: responseCode,
                ResponseMessage: responseMessage,
                BounceClass: bounceClass,
                Tries: ParseNullableInt(triesRaw),
                SenderId: senderId,
                TrackingId: trackingId,
                CampaignId: campaignId);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            entry = null!;
            error = ex.Message;
            return false;
        }
    }

    private static string NormalizeHeader(string header) =>
        new string(header.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

    private static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        throw new FormatException($"Could not parse date value '{value}'.");
    }

    private static int? ParseNullableInt(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new FormatException($"Could not parse integer value '{value}'.");
    }

    private static string ExtractDomain(string email)
    {
        var trimmed = email.Trim();
        var at = trimmed.LastIndexOf('@');
        return at >= 0 && at < trimmed.Length - 1 ? trimmed[(at + 1)..].ToLowerInvariant() : string.Empty;
    }
}
