using System.Text;
using TextToSpeech.Core.IO;

namespace TextToSpeech.Core.Tests;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "tts-atomic-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Creates_the_file_and_missing_folder()
    {
        string path = Path.Combine(folder, "sub", "a.json");
        AtomicFile.WriteAllText(path, "hello");
        Assert.Equal("hello", File.ReadAllText(path));
    }

    [Fact]
    public void Overwrites_an_existing_file_and_leaves_no_temp_files()
    {
        string path = Path.Combine(folder, "a.json");
        AtomicFile.WriteAllText(path, "one");
        AtomicFile.WriteAllText(path, "two");
        Assert.Equal("two", File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(folder));
    }

    [Fact]
    public void Writes_UTF8_without_a_byte_order_mark_by_default()
    {
        string path = Path.Combine(folder, "a.json");
        AtomicFile.WriteAllText(path, "x");
        Assert.Equal(new byte[] { (byte)'x' }, File.ReadAllBytes(path));
    }

    [Fact]
    public void Honours_an_explicit_encoding()
    {
        string path = Path.Combine(folder, "a.json");
        AtomicFile.WriteAllText(path, "x", new UTF8Encoding(true));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'x' }, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_failed_write_keeps_the_old_file_and_removes_the_temp_file()
    {
        string path = Path.Combine(folder, "a.json");
        AtomicFile.WriteAllText(path, "keep me");
        // The target is a non-empty directory, so the final move fails after the temp file was written.
        string blocked = Path.Combine(folder, "blocked.json");
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "inner.txt"), "x");

        Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(blocked, "new"));

        Assert.Equal("keep me", File.ReadAllText(path));
        Assert.DoesNotContain(Directory.GetFiles(folder), f => f.EndsWith(".tmp"));
    }
}
