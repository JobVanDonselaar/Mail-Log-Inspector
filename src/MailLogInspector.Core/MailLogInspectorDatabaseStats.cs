using System;

namespace MailLogInspector.Core;

public sealed record MailLogInspectorDatabaseStats(long MailItemCount, long ImportCount, long DatabaseSizeBytes, DateTime? ImportFrom, DateTime? ImportThrough, DateTime? LastUpdateAt);
