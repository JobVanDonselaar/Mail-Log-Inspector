using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MailLogInspector.Core;

public static class MailLogInspectorLog
{
    private const long MaxLogBytes = 2L * 1024 * 1024;
    private const int RetainedFiles = 4;
    private static readonly object SyncRoot = new();
    private static readonly Regex SensitiveValuePattern = new(
        @"(?i)(access_token|refresh_token|client_secret|password|token|code)=([^&\s]+)",
        RegexOptions.CultureInvariant);
    private static string? _logPath;

    public static void Configure(string workspaceRoot)
    {
        string logDirectory = Path.Combine(workspaceRoot, "Logs");
        Directory.CreateDirectory(logDirectory);
        lock (SyncRoot)
        {
            _logPath = Path.Combine(logDirectory, "mail-log-inspector.log");
        }
    }

    public static void Info(string area, string message) => Write("INFO", area, message, null);

    public static void Error(string area, string message, Exception exception) => Write("ERROR", area, message, exception);

    public static IDisposable Measure(string area, string operation)
    {
        return new Measurement(area, operation);
    }

    private static void Write(string level, string area, string message, Exception? exception)
    {
        lock (SyncRoot)
        {
            if (string.IsNullOrWhiteSpace(_logPath))
            {
                return;
            }

            try
            {
                RotateIfRequired(_logPath);
                var line = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("O"))
                    .Append(' ').Append(level)
                    .Append(" [").Append(area).Append("] ")
                    .Append(RedactSensitiveData(message));
                if (exception != null)
                {
                    line.Append(" | ")
                        .Append(exception.GetType().Name)
                        .Append(": ")
                        .Append(RedactSensitiveData(exception.Message));
                }
                File.AppendAllText(_logPath, line.AppendLine().ToString(), Encoding.UTF8);
            }
            catch
            {
                // Logging must never interrupt the application workflow.
            }
        }
    }

    private static string RedactSensitiveData(string value) =>
        SensitiveValuePattern.Replace(value, "$1=[REDACTED]");

    private static void RotateIfRequired(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxLogBytes)
        {
            return;
        }

        string oldest = path + "." + RetainedFiles;
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }
        for (int index = RetainedFiles - 1; index >= 1; index--)
        {
            string source = path + "." + index;
            if (File.Exists(source))
            {
                File.Move(source, path + "." + (index + 1), true);
            }
        }
        File.Move(path, path + ".1", true);
    }

    private sealed class Measurement : IDisposable
    {
        private readonly string _area;
        private readonly string _operation;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public Measurement(string area, string operation)
        {
            _area = area;
            _operation = operation;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            Info(_area, $"{_operation} gereed in {_stopwatch.Elapsed.TotalMilliseconds:0} ms");
        }
    }
}