namespace MailLogInspector.Core;

public sealed record MailLogInspectorImportProgress(MailLogInspectorImportStage Stage, string Message, double PercentComplete, long BytesRead, long TotalBytes, int RowsRead);
