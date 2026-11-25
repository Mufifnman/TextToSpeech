using PlainRules;
using Xunit;

namespace PlainRules.Tests;

public class RuleEngineTests
{
    private static string Run(Rule rule, string input) =>
        RuleEngine.Apply(new RuleSet { Name = "t", Rules = { rule } }, input);

    private static string Preset(string id, string input) =>
        Run(new Rule { Kind = RuleKind.Preset, Value = id }, input);

    [Fact]
    public void RemoveText_RemovesAll_AndEscapesSpecialCharacters()
        => Assert.Equal("a  b", Run(new Rule { Kind = RuleKind.RemoveText, Value = "(x)" }, "a (x) b"));

    [Fact]
    public void RemoveText_IgnoreCase()
        => Assert.Equal("a  b", Run(new Rule { Kind = RuleKind.RemoveText, Value = "FOO", IgnoreCase = true }, "a foo b"));

    [Fact]
    public void RemoveText_WholeWords_LeavesPartsOfWords()
        => Assert.Equal("cat  concat dogs", Run(new Rule { Kind = RuleKind.RemoveText, Value = "dog", WholeWords = true }, "cat dog concat dogs"));

    [Fact]
    public void Replace_UsesLiteralDollarSigns()
        => Assert.Equal("cost USD$1", Run(new Rule { Kind = RuleKind.Replace, Value = "$", With = "USD$1" }, "cost $"));

    [Fact]
    public void RemoveLinesStarting_RemovesWholeLines()
        => Assert.Equal("keep\nkeep2", Run(new Rule { Kind = RuleKind.RemoveLinesStarting, Value = "//" }, "keep\n  // gone\nkeep2"));

    [Fact]
    public void RemoveLinesContaining_HandlesCrLfAndLastLine()
    {
        var r = new Rule { Kind = RuleKind.RemoveLinesContaining, Value = "ad" };
        Assert.Equal("a\r\nc", Run(r, "a\r\nbad\r\nc"));
        Assert.Equal("a\n", Run(r, "a\nlast ad"));
    }

    [Fact]
    public void RemoveBetween_RemovesMarkersAndAcrossLines()
        => Assert.Equal("a  b", Run(new Rule { Kind = RuleKind.RemoveBetween, Value = "[[", To = "]]" }, "a [[x\ny]] b"));

    [Fact]
    public void RemoveWords_ListBySeparators()
        => Assert.Equal("  brown ", Run(new Rule { Kind = RuleKind.RemoveWords, Value = "the, quick\nfox" }, "the quick brown fox"));

    [Fact]
    public void Regex_UsesReplacementGroups()
        => Assert.Equal("b-a", Run(new Rule { Kind = RuleKind.Regex, Value = @"(a)-(b)", With = "$2-$1" }, "a-b"));

    [Fact]
    public void DisabledRule_IsSkipped()
        => Assert.Equal("abc", Run(new Rule { Kind = RuleKind.RemoveText, Value = "b", Disabled = true }, "abc"));

    [Fact]
    public void BrokenRule_IsSkipped_NotThrown()
        => Assert.Equal("abc", Run(new Rule { Kind = RuleKind.Regex, Value = "(" }, "abc"));

    [Fact]
    public void Rules_RunInOrder()
    {
        var set = new RuleSet { Name = "t", Rules = {
            new Rule { Kind = RuleKind.Replace, Value = "a", With = "b" },
            new Rule { Kind = RuleKind.Replace, Value = "b", With = "c" } } };
        Assert.Equal("cc", RuleEngine.Apply(set, "ab"));
    }

    [Fact]
    public void Preview_ReportsMatchesPerRule()
    {
        var set = new RuleSet { Name = "t", Rules = {
            new Rule { Kind = RuleKind.RemoveText, Value = "x" },
            new Rule { Kind = RuleKind.RemoveText, Value = "zzz" } } };
        var p = RuleEngine.Preview(set, "x x y");
        Assert.Equal("  y", p.Output);
        Assert.Equal(2, p.Steps[0].Matches);
        Assert.Equal(0, p.Steps[1].Matches);
    }

    [Fact]
    public void DefaultTimeout_IsShort_SoARunawayRuleCannotFreezeAWindow()
    {
        // Rules run on the UI thread in the app; a bad pattern must give up in a fraction of a second.
        Assert.True(RuleEngine.DefaultTimeout <= TimeSpan.FromMilliseconds(500), "default timeout is " + RuleEngine.DefaultTimeout);
    }

    [Fact]
    public void Timeout_SkipsRunawayRegex()
    {
        var old = RuleEngine.DefaultTimeout;
        RuleEngine.DefaultTimeout = TimeSpan.FromMilliseconds(50);
        try
        {
            var input = new string('a', 40) + "!";
            Assert.Equal(input, Run(new Rule { Kind = RuleKind.Regex, Value = @"^(a+)+$" }, input));
        }
        finally { RuleEngine.DefaultTimeout = old; }
    }

    // ---- presets ----

    [Fact] public void Whitespace() => Assert.Equal("a b\n\nc", Preset("whitespace", "a   b\n\n\n\n\nc"));
    [Fact] public void Urls() => Assert.Equal("see  now.", Preset("urls", "see https://x.com/a?b=1 now."));
    [Fact] public void Urls_KeepTrailingPunctuation() => Assert.Equal("go . ok", Preset("urls", "go http://a.b/c. ok"));
    [Fact] public void Citations() => Assert.Equal("as shown and.", Preset("citations", "as shown [12] and [3, 4]."));
    [Fact] public void MarkdownSymbols() => Assert.Equal("Title\nbold and italic and code", Preset("markdown", "# Title\n**bold** and *italic* and `code`"));
    [Fact] public void MarkdownLinks() => Assert.Equal("see docs now", Preset("markdown-links", "see [docs](http://a.b) now"));
    [Fact] public void Footnotes() => Assert.Equal("word and.", Preset("footnotes", "word¹² and[a]."));
    [Fact] public void Emoji_KeepsTrademark() => Assert.Equal("great™", Preset("emoji", "great \U0001F389™"));
    [Fact] public void RepeatedPunctuation() => Assert.Equal("what!? wait...", Preset("repeated-punctuation", "what!!!??? wait......"));
    [Fact] public void Emails() => Assert.Equal("mail  now", Preset("emails", "mail me.x+y@site.co.uk now"));
    [Fact] public void PageNumbers() => Assert.Equal("text\nmore", Preset("page-numbers", "text\nPage 4 of 20\n- 12 -\nmore"));
    [Fact] public void Parentheses() => Assert.Equal("done", Preset("parentheses", "(see Fig. 2) done").TrimStart());
    [Fact] public void HyphenBreaks() => Assert.Equal("an international day", Preset("hyphen-breaks", "an inter-\nnational day"));
    [Fact] public void UnwrapLines_KeepsParagraphs() => Assert.Equal("one two three.\n\nNext para", Preset("unwrap-lines", "one two\nthree.\n\nNext para"));
    [Fact] public void OddCharacters() => Assert.Equal("zerowidth a b", Preset("odd-characters", "zero​width a b"));
    [Fact] public void Html() => Assert.Equal("x y", Preset("html", "<b>x</b><!-- c --> y"));

    [Fact]
    public void EveryPreset_CompilesAndRuns()
    {
        foreach (var p in PlainRules.Presets.All)
            Assert.Equal("", Preset(p.Id, ""));
    }
}
