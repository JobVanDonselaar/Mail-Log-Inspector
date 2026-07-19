namespace MailLogInspector.Core;

public sealed record SmtpParseError(int RowNumber, string Message, string? RawRow = null);
