using System.Drawing;
using TextToSpeech.Ocr;
using Xunit;

namespace TextToSpeech.Windows.Tests;

public class OcrTests
{
    // Needs an OCR language pack installed (any normal English Windows has one), so it is tagged
    // LocalOnly: run on a dev PC, skip elsewhere with --filter "Category!=LocalOnly".
    [Fact]
    [Trait("Category", "LocalOnly")]
    public async Task WindowsOcr_ReadsRenderedText()
    {
        using var bmp = new Bitmap(500, 120);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var font = new Font("Arial", 28);
            g.DrawString("Hello world", font, Brushes.Black, 10, 10);
            g.DrawString("Second line", font, Brushes.Black, 10, 60);
        }

        var outcome = await OcrEngines.For(OcrOptions.Windows).RecognizeAsync(bmp);

        Assert.True(outcome.Succeeded, outcome.ErrorMessage);
        Assert.Contains("Hello", outcome.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Second", outcome.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WindowsOcr_BlankImageDoesNotThrow()
    {
        using var bmp = new Bitmap(200, 80);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        var outcome = await OcrEngines.For(OcrOptions.Windows).RecognizeAsync(bmp);
        Assert.True(!outcome.Succeeded || string.IsNullOrWhiteSpace(outcome.Text));
    }
}
