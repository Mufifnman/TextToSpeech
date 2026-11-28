using TextToSpeech.Core.Diagnostics;
using Xunit;

namespace TextToSpeech.Core.Tests;

public class RollingLogTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "tts-log-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(folder, true); } catch (IOException) { } }

    [Fact]
    public void WritesLevelMessageAndException()
    {
        var log = new RollingLog(folder);
        log.Write("WARN", "something odd", new InvalidOperationException("boom"));
        string text = File.ReadAllText(log.FilePath);
        Assert.Contains("WARN", text);
        Assert.Contains("something odd", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void RollsOverBySizeAndKeepsOnlyTheNewestFiles()
    {
        var log = new RollingLog(folder, maxBytes: 1_000, keep: 2);
        string line = new string('x', 300);
        for (int i = 0; i < 40; i++) log.Write("INFO", i + " " + line);

        var names = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "app.1.log", "app.2.log", "app.log" }, names);
        foreach (var f in Directory.GetFiles(folder)) Assert.True(new FileInfo(f).Length < 2_000);
        Assert.Contains("39 ", File.ReadAllText(log.FilePath));
    }

    [Fact]
    public void NeverThrowsWhenTheFolderCannotBeUsed()
    {
        string file = Path.Combine(Path.GetTempPath(), "tts-log-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "a file, not a folder");
        try
        {
            new RollingLog(Path.Combine(file, "sub")).Write("INFO", "ignored");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ContinuesAnExistingFileAcrossRestarts()
    {
        new RollingLog(folder).Write("INFO", "first run");
        var second = new RollingLog(folder);
        second.Write("INFO", "second run");
        string text = File.ReadAllText(second.FilePath);
        Assert.Contains("first run", text);
        Assert.Contains("second run", text);
    }
}
