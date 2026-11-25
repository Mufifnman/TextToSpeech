using TextToSpeech;
using TextToSpeech.Core.Reading;
using Xunit;

namespace TextToSpeech.Windows.Tests;

public class ClipboardIntakeTests
{
    private static ReadingDocument DocWith(string text)
    {
        var doc = new ReadingDocument();
        doc.Append(text);
        return doc;
    }

    [Fact]
    public void NewText_IsCleanedByTheFilters_AndReturnedWithATrailingNewline()
    {
        var result = ClipboardIntake.Prepare("Some fact.[1] More.", "", new ReadingDocument(), new FilterSettings());

        Assert.NotNull(result);
        Assert.DoesNotContain("[1]", result);
        Assert.EndsWith("\n", result);
    }

    [Fact]
    public void TextAlreadyInTheEditBox_IsSkipped()
    {
        Assert.Null(ClipboardIntake.Prepare("Hello there.", "first line\nHello there.", new ReadingDocument(), new FilterSettings()));
    }

    [Fact]
    public void TextStillWaitingToBeRead_IsSkipped_ButTextAlreadyReadIsTakenAgain()
    {
        var doc = DocWith("One two. Three four.");
        Assert.Null(ClipboardIntake.Prepare("Three four.", "", doc, new FilterSettings()));   // unread: duplicate

        doc.CompleteSentence();
        doc.CompleteSentence();                                                               // everything read
        Assert.NotNull(ClipboardIntake.Prepare("Three four.", "", doc, new FilterSettings()));
    }

    [Fact]
    public void LongestSentence_PicksTheLongestPiece()
    {
        Assert.Equal("This one is clearly much longer.", ClipboardIntake.LongestSentence("Short. This one is clearly much longer. Mid size."));
        Assert.Equal("", ClipboardIntake.LongestSentence(""));
    }
}

public class OcrTextProcessorTests
{
    [Fact]
    public void WrappedLinesAreJoined_AndInvertedExclamationBecomesI()
    {
        string result = OcrTextProcessor.Process("The cat sat on\nthe mat. \u00A1t was fun.", null, new FilterSettings());

        Assert.Contains("The cat sat on the mat.", result);
        Assert.Contains("it was fun.", result);
    }

    [Fact]
    public void ACutOffSentenceFromTheLastCapture_IsJoinedOnTop()
    {
        string result = OcrTextProcessor.Process("the bottom of the page.", "Text cut off at", new FilterSettings());

        Assert.StartsWith("Text cut off at the bottom of the page", result);
    }

    [Fact]
    public void AuthorYearCitationsAreRemovedFromOcrText_WhenTheFilterIsOn()
    {
        Assert.DoesNotContain("(Smith, 2004)", OcrTextProcessor.Process("Known fact (Smith, 2004).", null, new FilterSettings()));

        var off = new FilterSettings();
        off.SetEnabled("author-year", false);
        Assert.Contains("(Smith, 2004)", OcrTextProcessor.Process("Known fact (Smith, 2004).", null, off));
    }
}
