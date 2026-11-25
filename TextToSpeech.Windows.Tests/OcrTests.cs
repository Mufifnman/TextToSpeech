using System.Drawing;
using TextToSpeech.Ocr;
using Xunit;

namespace TextToSpeech.Windows.Tests;

public class OcrTests
{
    [Fact]
    public void Outcome_OkAndFail()
    {
        var ok = OcrOutcome.Ok("hi");
        Assert.True(ok.Succeeded);
        Assert.Equal("hi", ok.Text);
        Assert.Null(ok.ErrorMessage);

        var fail = OcrOutcome.Fail("nope");
        Assert.False(fail.Succeeded);
        Assert.Null(fail.Text);
        Assert.Equal("nope", fail.ErrorMessage);
    }

    [Fact]
    public void Engines_WindowsIsDefaultAndCached()
    {
        Assert.Equal(OcrOptions.Windows, Enum.GetValues<OcrOptions>()[0]);
        Assert.Same(OcrEngines.For(OcrOptions.Windows), OcrEngines.For(OcrOptions.Windows));
    }

    [Fact]
    public void Engines_UnknownOptionThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OcrEngines.For((OcrOptions)99));
    }

    // Needs an OCR language pack installed (any normal English Windows has one).
    [Fact]
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
