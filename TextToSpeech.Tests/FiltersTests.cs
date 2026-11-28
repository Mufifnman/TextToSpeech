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
    public void AllFiltersAreOnByDefault_IncludingOnesAddedLater()
    {
        var settings = new FilterSettings();
        Assert.All(FilterRegistry.All, f => Assert.True(settings.IsEnabled(f.Id)));
        Assert.True(settings.IsEnabled("some-filter-added-later"));
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

    // Turning one filter off keeps exactly its own marks in the text; the other filters still run.
    [Theory]
    [InlineData("wikipedia", "[1]", "(Smith, 2004)")]
    [InlineData("author-year", "(Smith, 2004)", "(1632-1704)")]
    [InlineData("page-citations", "(1632-1704)", "(Smith, 2004)")]
    [InlineData("ahit", "_____", "[1]")]
    public void TurningOneFilterOffKeepsOnlyItsOwnMarks(string offId, string stillThere, string stillRemoved)
    {
        string text = Sample;
        Filters.ApplyClipboardFilters(ref text, Off(offId));
        Assert.Contains(stillThere, text);
        Assert.DoesNotContain(stillRemoved, text);
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

    // Custom filters (made in the filter editor) join the registry after the built-in ones.
    // These live in this class so they never run in parallel with the other registry tests.
    [Fact]
    public void CustomFiltersJoinTheRegistryAndRunThroughTheSamePipeline()
    {
        try
        {
            FilterRegistry.SetCustom(new[]
            {
                new FilterDefinition("custom:ads", "My filters", "Strip ads", FilterStage.Clipboard | FilterStage.Ocr,
                    (ref string t) => { t = t.Replace("ADVERT", ""); }),
            });

            Assert.Equal("custom:ads", FilterRegistry.All[^1].Id);
            Assert.Contains("My filters", FilterRegistry.Groups);

            string text = "keep ADVERT this";
            Filters.ApplyClipboardFilters(ref text, new FilterSettings());
            Assert.Equal("keep  this", text);

            string off = "keep ADVERT this";
            Filters.ApplyClipboardFilters(ref off, Off("custom:ads"));
            Assert.Equal("keep ADVERT this", off);

            string ocr = "keep ADVERT this";
            Filters.ApplyOcrFilters(ref ocr, new FilterSettings());
            Assert.Equal("keep  this", ocr);
        }
        finally
        {
            FilterRegistry.SetCustom(Array.Empty<FilterDefinition>());
        }
        Assert.Equal(new[] { "Citations", "Quiz material" }, FilterRegistry.Groups.ToArray());
    }
}

/// <summary>
/// The filters used to remove one match at a time and rescan from the start (quadratic). They now
/// make linear passes. These tests keep the old algorithm as a reference and check that the results match.
/// </summary>
public class FilterLoopEquivalenceTests
{
    private static string OldRemove(string text, string target)
    {
        while (System.Text.RegularExpressions.Regex.IsMatch(text, target))
        {
            var m = System.Text.RegularExpressions.Regex.Match(text, target);
            text = text.Remove(m.Index, m.Length);
        }
        return text;
    }

    private static string OldCombineLines(string text)
    {
        Func<string, string, System.Text.RegularExpressions.Match> rx = System.Text.RegularExpressions.Regex.Match;
        string target = @"[^.]\n[A-Z]";
        while (System.Text.RegularExpressions.Regex.IsMatch(text, target))
            text = text.Insert(rx(text, target).Index + 1, ".");
        text = text.Replace("-\n\r", "").Replace("-\n", "").Replace('\n', ' ');
        target = @"[0-9]\.[0-9]";
        while (System.Text.RegularExpressions.Regex.IsMatch(text, target))
        {
            var m = rx(text, target);
            text = text.Remove(m.Index + 1, 1).Insert(m.Index + 1, " point ");
        }
        target = @"\.( )*[a-z]";
        while (System.Text.RegularExpressions.Regex.IsMatch(text, target))
        {
            var m = rx(text, target);
            text = text.Remove(m.Index, 1).Insert(m.Index, " ");
        }
        return text;
    }

    private static IEnumerable<string> Samples()
    {
        var alphabet = new[] { "a", "B", "1", "2", ".", "\n", " ", "-", "\r", "x", "Z", "[", "]", "(", ")", ";", ",", "2004", "p. ", "Smith" };
        var rng = new Random(12345);
        for (int i = 0; i < 3000; i++)
        {
            int len = rng.Next(0, 14);
            var sb = new System.Text.StringBuilder();
            for (int j = 0; j < len; j++) sb.Append(alphabet[rng.Next(alphabet.Length)]);
            yield return sb.ToString();
        }
        yield return "a\nB\nC\nD";
        yield return "Section 1.2.3 and 4.5. then more. And\n\nMore";
        yield return "(Smith & Jones, 2004; Lee et al., 2010a; Brown, 1999) and [[1]2]";
    }

    [Fact]
    public void CombineLinesMatchesTheOldAlgorithm()
    {
        foreach (var s in Samples())
        {
            string actual = s;
            Filters.CombineLines(ref actual);
            Assert.True(OldCombineLines(s) == actual, $"input {s.Replace("\n", "\\n").Replace("\r", "\\r")}");
        }
    }

    [Fact]
    public void CitationFiltersMatchTheOldAlgorithm()
    {
        foreach (var s in Samples())
        {
            string wiki = s;
            Filters.WikipediaCitation(ref wiki);
            Assert.True(OldRemove(OldRemove(s, @"\[edit\]"), @"\[[0-9]+\]") == wiki, $"wiki input {s}");

            string life = s;
            Filters.GovernmentFilter(ref life);
            string expected = s;
            string citation = @"([a-z,A-Z\. ]+, )*([0-9]{4}b?, )?(pp?\. )([0-9, \-xiv]+(ff)?)";
            string buildup = citation;
            for (int i = 0; i < 6; i++) { expected = OldRemove(expected, @"\(" + buildup + @"\)"); buildup += "; " + citation; }
            buildup = citation;
            for (int i = 0; i < 5; i++) { expected = OldRemove(expected, @"\{" + buildup + @"\)"); buildup += "; " + citation; }
            expected = OldRemove(expected, "CLASSIC MODELS");
            expected = OldRemove(expected, @"\([0-9]{4}-[0-9]{4}\)");
            Assert.True(expected == life, $"government input {s}");
        }
    }

    [Fact]
    public void LongTextIsProcessedInReasonableTime()
    {
        // 30,000 citations in about 200 KB; the old one-at-a-time loop rescanned the whole text per match.
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 30000; i++) sb.Append("word[").Append(i).Append("] ");
        string text = sb.ToString();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Filters.WikipediaCitation(ref text);
        watch.Stop();
        Assert.DoesNotContain("[", text);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public void ABackTrackingPatternGivesUpInsteadOfFreezing()
    {
        string evil = new string('a', 40) + "!";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string result = Filters.ReplaceUntilStable(evil, @"^(a+)+$", _ => "");
        watch.Stop();
        Assert.Equal(evil, result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }
}
