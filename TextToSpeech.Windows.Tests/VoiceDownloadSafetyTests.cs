using System.IO;
using TextToSpeech.Speech;
using Xunit;

namespace TextToSpeech.Windows.Tests;

public class VoiceDownloadSafetyTests
{
    private static readonly DownloadableVoice Voice = new("Test", "test-voice", "1 MB",
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", 100);

    [Fact]
    public void EveryCatalogVoicePinsAChecksumAndSize()
    {
        foreach (var v in VoiceDownloader.Catalog)
        {
            Assert.Matches("^[0-9a-f]{64}$", v.Sha256);
            Assert.InRange(v.ArchiveBytes, 1_000_000, 1L << 30);
        }
    }

    [Fact]
    public void MatchingSizeAndHashPass()
    {
        VoiceDownloader.VerifyDownload(Voice, 100, Voice.Sha256.ToUpperInvariant());
    }

    [Fact]
    public void WrongHashIsRefused()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            VoiceDownloader.VerifyDownload(Voice, 100, new string('0', 64)));
        Assert.Contains("checksum", ex.Message);
    }

    [Fact]
    public void WrongSizeIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => VoiceDownloader.VerifyDownload(Voice, 99, Voice.Sha256));
    }

    [Fact]
    public void CappedStreamReadsUpToTheLimit()
    {
        using var capped = new CappedReadStream(new MemoryStream(new byte[100]), 100);
        var buffer = new byte[64];
        int total = 0, n;
        while ((n = capped.Read(buffer, 0, buffer.Length)) > 0) total += n;
        Assert.Equal(100, total);
    }

    [Fact]
    public void CappedStreamThrowsPastTheLimit()
    {
        using var capped = new CappedReadStream(new MemoryStream(new byte[101]), 100);
        var buffer = new byte[64];
        Assert.Throws<InvalidDataException>(() => { while (capped.Read(buffer, 0, buffer.Length) > 0) { } });
    }
}
