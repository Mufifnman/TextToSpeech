using Xunit;

namespace TextToSpeech.Tests;

public class WikipediaCitationTests
{
    [Fact]
    public void RemovesNumberedCitations()
    {
        string text = "The sky is blue.[1] Grass is green.[23]";
        Filters.WikipediaCitation(ref text);
        Assert.Equal("The sky is blue. Grass is green.", text);
    }

    [Fact]
    public void RemovesEditLinks()
    {
        string text = "History[edit]\nIt began.";
        Filters.WikipediaCitation(ref text);
        Assert.Equal("History\nIt began.", text);
    }

    [Fact]
    public void LeavesNonNumericBracketsAlone()
    {
        string text = "An array [a, b] and a note [citation needed].";
        Filters.WikipediaCitation(ref text);
        Assert.Equal("An array [a, b] and a note [citation needed].", text);
    }
}

public class PsychologyFilterTests
{
    [Fact]
    public void RemovesSingleAuthorYearCitation()
    {
        string text = "Memory decays quickly (Ebbinghaus, 1885). Then it levels off.";
        Filters.PsychologyFilter(ref text);
        Assert.Equal("Memory decays quickly . Then it levels off.", text);
    }

    [Fact]
    public void RemovesMultipleCitationsSeparatedBySemicolons()
    {
        string text = "This is well known (Smith & Jones, 2004; Lee et al., 2010a; Brown, 1999).";
        Filters.PsychologyFilter(ref text);
        Assert.Equal("This is well known .", text);
    }

    [Fact]
    public void LeavesParenthesesWithoutYearsAlone()
    {
        string text = "Use the short form (see below) when possible.";
        Filters.PsychologyFilter(ref text);
        Assert.Equal("Use the short form (see below) when possible.", text);
    }
}

public class GovernmentFilterTests
{
    [Fact]
    public void RemovesPageCitations()
    {
        string text = "The policy changed (Hamilton, p. 12). It worked.";
        Filters.GovernmentFilter(ref text);
        Assert.Equal("The policy changed . It worked.", text);
    }

    [Fact]
    public void RemovesPageRangeCitationsWithYear()
    {
        string text = "As argued (Madison, 1788, pp. 45-47) the plan held.";
        Filters.GovernmentFilter(ref text);
        Assert.Equal("As argued  the plan held.", text);
    }

    [Fact]
    public void RemovesLifetimes()
    {
        string text = "John Locke (1632-1704) wrote it.";
        Filters.GovernmentFilter(ref text);
        Assert.Equal("John Locke  wrote it.", text);
    }

    [Fact]
    public void RemovesBookTitle()
    {
        string text = "CLASSIC MODELS\nChapter one.";
        Filters.GovernmentFilter(ref text);
        Assert.Equal("\nChapter one.", text);
    }

    [Fact]
    public void NoLongerRunsPsychologyFilterItself()
    {
        string text = "Known since (Smith, 2004).";
        Filters.GovernmentFilter(ref text);
        Assert.Equal("Known since (Smith, 2004).", text);
    }
}

public class AhitStarredQuestionTests
{
    [Fact]
    public void RemovesStarButtonTextAndSpeaksBlanks()
    {
        string text = "Unset starred question\nThe capital of France is _____.";
        Filters.AHITStarredQuestion(ref text);
        Assert.Equal("\nThe capital of France is  blank .", text);
    }
}

public class CombineLinesTests
{
    [Fact]
    public void JoinsWrappedLinesWithinASentence()
    {
        string text = "This sentence was\nwrapped by the PDF.";
        Filters.CombineLines(ref text);
        Assert.Equal("This sentence was wrapped by the PDF.", text);
    }

    [Fact]
    public void RejoinsHyphenatedWords()
    {
        string text = "An extra-\nordinary result.";
        Filters.CombineLines(ref text);
        Assert.Equal("An extraordinary result.", text);
    }

    [Fact]
    public void EndsHeadingsWithAPeriodSoTheyAreReadSeparately()
    {
        string text = "Introduction\nThe study began.";
        Filters.CombineLines(ref text);
        Assert.Equal("Introduction. The study began.", text);
    }

    [Fact]
    public void ReadsDecimalNumbersAsPoint()
    {
        string text = "See section 3.3 for details.";
        Filters.CombineLines(ref text);
        Assert.Equal("See section 3 point 3 for details.", text);
    }

    [Fact]
    public void DropsPeriodsThatAreNotSentenceEnds()
    {
        string text = "Smith et al. found more.";
        Filters.CombineLines(ref text);
        Assert.Equal("Smith et al  found more.", text);
    }
}

public class FilterToggleTests
{
    private const string Sample =
        "Unset starred question\nLocke (1632-1704) said so.[1] Others agreed (Smith, 2004). Fill in _____.";

    private static FilterSettings Off(params string[] ids)
    {
        var s = new FilterSettings();
        foreach (var id in ids) s.SetEnabled(id, false);
        return s;
    }

    [Fact]
    public void AllFiltersAreOnByDefault()
    {
        var settings = new FilterSettings();
        Assert.All(FilterRegistry.All, f => Assert.True(settings.IsEnabled(f.Id)));
    }

    [Fact]
    public void UnknownFilterIdsDefaultToOn()
    {
        Assert.True(new FilterSettings().IsEnabled("some-filter-added-later"));
    }

    [Fact]
    public void RegistryIdsAreUniqueAndGroupedForSubmenus()
    {
        Assert.Equal(FilterRegistry.All.Count, FilterRegistry.All.Select(f => f.Id).Distinct().Count());
        Assert.Equal(new[] { "Citations", "Quiz material" }, FilterRegistry.Groups.ToArray());
        Assert.All(FilterRegistry.All, f => Assert.False(string.IsNullOrWhiteSpace(f.Label)));
    }

    [Fact]
    public void DefaultSettingsMatchTheOriginalClipboardPipeline()
    {
        // Original MainWindow order: Wikipedia, Government (which ran Psychology first), AHIT.
        string expected = Sample;
        Filters.WikipediaCitation(ref expected);
        Filters.PsychologyFilter(ref expected);
        Filters.GovernmentFilter(ref expected);
        Filters.AHITStarredQuestion(ref expected);

        string actual = Sample;
        Filters.ApplyClipboardFilters(ref actual, new FilterSettings());

        Assert.Equal(expected, actual);
        Assert.Equal("\nLocke  said so. Others agreed . Fill in  blank .", actual);
    }

    [Fact]
    public void AllFiltersOffLeavesTextUnchanged()
    {
        string text = Sample;
        Filters.ApplyClipboardFilters(ref text, Off(FilterRegistry.All.Select(f => f.Id).ToArray()));
        Assert.Equal(Sample, text);
    }

    [Fact]
    public void WikipediaToggleOnlyAffectsWikipediaCitations()
    {
        string text = Sample;
        Filters.ApplyClipboardFilters(ref text, Off("wikipedia"));
        Assert.Contains("[1]", text);
        Assert.DoesNotContain("(Smith, 2004)", text);
    }

    [Fact]
    public void AuthorYearToggleKeepsAuthorYearCitations()
    {
        string text = Sample;
        Filters.ApplyClipboardFilters(ref text, Off("author-year"));
        Assert.Contains("(Smith, 2004)", text);
        Assert.DoesNotContain("(1632-1704)", text);
    }

    [Fact]
    public void PageCitationToggleKeepsLifetimes()
    {
        string text = Sample;
        Filters.ApplyClipboardFilters(ref text, Off("page-citations"));
        Assert.Contains("(1632-1704)", text);
        Assert.DoesNotContain("(Smith, 2004)", text);
    }

    [Fact]
    public void AhitToggleKeepsBlanksAndStarText()
    {
        string text = Sample;
        Filters.ApplyClipboardFilters(ref text, Off("ahit"));
        Assert.Contains("Unset starred question", text);
        Assert.Contains("_____", text);
    }

    [Fact]
    public void OcrFiltersRespectTheAuthorYearToggle()
    {
        string on = "Known (Smith, 2004).";
        Filters.ApplyOcrFilters(ref on, new FilterSettings());
        Assert.Equal("Known .", on);

        string off = "Known (Smith, 2004).";
        Filters.ApplyOcrFilters(ref off, Off("author-year"));
        Assert.Equal("Known (Smith, 2004).", off);
    }

    [Fact]
    public void OcrStageSkipsClipboardOnlyFilters()
    {
        string text = "Fill in _____.[1]";
        Filters.ApplyOcrFilters(ref text, new FilterSettings());
        Assert.Equal("Fill in _____.[1]", text);
    }

    [Fact]
    public void SettingsSurviveJsonRoundTrip()
    {
        var s = Off("ahit", "wikipedia");
        var back = System.Text.Json.JsonSerializer.Deserialize<FilterSettings>(System.Text.Json.JsonSerializer.Serialize(s))!;
        Assert.False(back.IsEnabled("ahit"));
        Assert.False(back.IsEnabled("wikipedia"));
        Assert.True(back.IsEnabled("page-citations"));
    }
}
