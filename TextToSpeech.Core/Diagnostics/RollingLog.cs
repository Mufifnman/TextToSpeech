using System.Text;

namespace TextToSpeech.Core.Diagnostics;

/// <summary>
/// A small thread-safe text log that rolls over by size: <c>app.log</c> is the current file, and when it would pass
/// <c>maxBytes</c> it becomes <c>app.1.log</c> (older ones shift up, the oldest beyond <c>keep</c> is deleted).
/// Logging must never break the app, so every failure here is swallowed.
/// PRIVACY: callers must not log the text being read or any API token; log events, sizes and error messages only.
/// </summary>
public sealed class RollingLog
{
    private readonly object gate = new();
    private readonly string folder;
    private readonly string baseName;
    private readonly long maxBytes;
    private readonly int keep;
    private long currentBytes = -1;

    public RollingLog(string folder, string baseName = "app", long maxBytes = 1_000_000, int keep = 3)
    {
        this.folder = folder;
        this.baseName = baseName;
        this.maxBytes = Math.Max(1_000, maxBytes);
        this.keep = Math.Max(1, keep);
    }

    public string Folder => folder;
    public string FilePath => Path.Combine(folder, baseName + ".log");
    private string Rolled(int n) => Path.Combine(folder, baseName + "." + n + ".log");

    public void Write(string level, string message, Exception? error = null)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(' ').Append(level.PadRight(5)).Append(' ')
                .Append(message);
            if (error != null) line.Append(" | ").Append(error);
            line.AppendLine();
            string text = line.ToString();

            lock (gate)
            {
                Directory.CreateDirectory(folder);
                if (currentBytes < 0) currentBytes = File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0;
                if (currentBytes > 0 && currentBytes + text.Length > maxBytes) Roll();
                File.AppendAllText(FilePath, text, Encoding.UTF8);
                currentBytes += Encoding.UTF8.GetByteCount(text);
            }
        }
        catch (Exception)
        {
            // Never let logging throw.
        }
    }

    private void Roll()
    {
        try
        {
            File.Delete(Rolled(keep));
            for (int n = keep - 1; n >= 1; n--)
            {
                if (File.Exists(Rolled(n))) File.Move(Rolled(n), Rolled(n + 1), overwrite: true);
            }
            File.Move(FilePath, Rolled(1), overwrite: true);
        }
        catch (Exception)
        {
            // If it cannot roll (a viewer has it open), keep appending rather than lose lines.
        }
        currentBytes = File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0;
    }
}

/// <summary>The app-wide log at <c>%LocalAppData%\TextToSpeech\logs\app.log</c>.</summary>
public static class AppLog
{
    private static RollingLog log = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TextToSpeech", "logs"));

    public static string Folder => log.Folder;
    public static string FilePath => log.FilePath;

    /// <summary>Points the log somewhere else (tests).</summary>
    public static void UseFolder(string folder) => log = new RollingLog(folder);

    public static void Info(string message) => log.Write("INFO", message);
    public static void Warn(string message, Exception? error = null) => log.Write("WARN", message, error);
    public static void Error(string message, Exception? error = null) => log.Write("ERROR", message, error);
}
