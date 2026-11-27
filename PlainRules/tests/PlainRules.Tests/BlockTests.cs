using PlainRules;
using Xunit;

namespace PlainRules.Tests;

public class BlockTests
{
    private static string Run(Rule rule, string input) =>
        RuleEngine.Apply(new RuleSet { Name = "t", Rules = { rule } }, input);

    private static Rule Remove(string block, string? with = null) =>
        new() { Kind = RuleKind.RemoveBlock, Block = block, With = with };

    // ---- remove / replace whole blocks ----

    [Fact]
    public void RemoveBlock_CodeBlock_CanBeReplacedWithAWord()
        => Assert.Equal("Intro\n(code)\nOutro", Run(Remove("code-blocks", "(code)"), "Intro\n```js\nlet x = 1;\n```\nOutro"));

    [Fact]
    public void RemoveBlock_UnclosedCodeBlock_RunsToTheEnd()
        => Assert.Equal("a\n", Run(Remove("code-blocks"), "a\n```\nb\nc"));

    [Fact]
    public void RemoveBlock_Quotes_RemovesConsecutiveQuotedLines()
        => Assert.Equal("a\n\nb", Run(Remove("quotes"), "a\n> q1\n> q2\nb"));

    [Fact]
    public void RemoveBlock_Lists_RemovesBulletsAndNumbers()
        => Assert.Equal("intro\n\noutro", Run(Remove("lists"), "intro\n- a\n- b\noutro"));

    [Fact]
    public void RemoveBlock_Brackets()
        => Assert.Equal("see  and ", Run(Remove("brackets"), "see (fig 1) and (x)"));

    [Fact]
    public void RemoveBlock_ReplacementDollarIsLiteral()
        => Assert.Equal("a $1 b", Run(Remove("brackets", "$1"), "a (x) b"));

    [Fact]
    public void RemoveBlock_BetweenMarkers()
    {
        var r = new Rule { Kind = RuleKind.RemoveBlock, Block = "between", BlockStart = "<<", BlockEnd = ">>" };
        Assert.Equal("a  b", Run(r, "a <<hidden\ntext>> b"));
    }

    // ---- scoping other rules to blocks ----

    [Fact]
    public void Scope_Inside_OnlyTouchesTheBlocks()
    {
        var r = new Rule { Kind = RuleKind.Replace, Value = "a", With = "b", Scope = RuleScope.Inside, Block = "inline-code" };
        Assert.Equal("a `b` a", Run(r, "a `a` a"));
    }

    [Fact]
    public void Scope_Outside_LeavesTheBlocksAlone()
    {
        var r = new Rule { Kind = RuleKind.RemoveText, Value = "foo", Scope = RuleScope.Outside, Block = "code-blocks" };
        Assert.Equal("\n```\nfoo\n```\n", Run(r, "foo\n```\nfoo\n```\nfoo"));
    }

    [Fact]
    public void Scope_BetweenMarkers_Inside()
    {
        var r = new Rule { Kind = RuleKind.RemoveText, Value = "x", Scope = RuleScope.Inside, Block = "between", BlockStart = "<<", BlockEnd = ">>" };
        Assert.Equal("x << >> x", Run(r, "x <<x x>> x"));
    }

    [Fact]
    public void Scope_NoBlocksInText_InsideChangesNothing_OutsideChangesEverything()
    {
        var inside = new Rule { Kind = RuleKind.RemoveText, Value = "a", Scope = RuleScope.Inside, Block = "quotes" };
        var outside = new Rule { Kind = RuleKind.RemoveText, Value = "a", Scope = RuleScope.Outside, Block = "quotes" };
        Assert.Equal("banana", Run(inside, "banana"));
        Assert.Equal("bnn", Run(outside, "banana"));
    }

    [Fact]
    public void Scope_WorksWithLineRulesAndRegex()
    {
        var r = new Rule { Kind = RuleKind.Regex, Value = @"\d+", With = "#", Scope = RuleScope.Inside, Block = "brackets" };
        Assert.Equal("1 (#) 2", Run(r, "1 (22) 2"));
    }

    [Fact]
    public void Scope_Preview_CountsOnlyMatchesInScope()
    {
        var set = new RuleSet { Name = "t", Rules = { new Rule { Kind = RuleKind.Replace, Value = "a", With = "b", Scope = RuleScope.Inside, Block = "inline-code" } } };
        var preview = RuleEngine.Preview(set, "a `a` a `a`");
        Assert.Equal("a `b` a `b`", preview.Output);
        Assert.Equal(2, preview.Steps[0].Matches);
    }

    [Fact]
    public void Scope_BadBlock_SkipsTheRuleInsteadOfThrowing()
    {
        var set = new RuleSet { Name = "t", Rules = { new Rule { Kind = RuleKind.RemoveText, Value = "a", Scope = RuleScope.Inside } } };
        var preview = RuleEngine.Preview(set, "banana");
        Assert.Equal("banana", preview.Output);
        Assert.NotNull(preview.Steps[0].Error);
    }

    [Fact]
    public void Rules_AfterABlockRemoval_SeeTheShortenedText()
    {
        var set = new RuleSet { Name = "t", Rules = { Remove("code-blocks"), new Rule { Kind = RuleKind.RemoveText, Value = "x" } } };
        Assert.Equal("\nb", RuleEngine.Apply(set, "```\nxx\n```\nxb"));
    }

    // ---- lint ----

    private static bool Has(IEnumerable<LintIssue> issues, string code) => issues.Any(i => i.Code == code);

    [Fact]
    public void Lint_ScopeWithoutBlock_IsError()
    {
        var set = new RuleSet { Name = "t", Rules = { new Rule { Kind = RuleKind.RemoveText, Value = "a", Scope = RuleScope.Inside } } };
        Assert.True(Has(RuleLinter.Check(set), "BadScope"));
    }

    [Fact]
    public void Lint_BetweenWithoutMarkers_IsError()
    {
        var set = new RuleSet { Name = "t", Rules = { new Rule { Kind = RuleKind.RemoveText, Value = "a", Scope = RuleScope.Outside, Block = "between", BlockStart = "<<" } } };
        Assert.True(Has(RuleLinter.Check(set), "BadScope"));
    }

    [Fact]
    public void Lint_RemoveBlockWithoutBlock_IsError()
    {
        var set = new RuleSet { Name = "t", Rules = { new Rule { Kind = RuleKind.RemoveBlock } } };
        Assert.True(Has(RuleLinter.Check(set), "BadBlock"));
    }

    [Fact]
    public void Lint_GoodScopedRule_IsValid()
    {
        var set = new RuleSet { Name = "t", Rules = { new Rule { Kind = RuleKind.RemoveText, Value = "a", Scope = RuleScope.Inside, Block = "quotes" } } };
        Assert.True(RuleLinter.IsValid(RuleLinter.Check(set, "> a")));
    }

    // ---- file format ----

    [Fact]
    public void Json_PlainRulesStayFreeOfScopeFields()
    {
        var json = RuleSerializer.ToJson(new RuleSet { Name = "t", Rules = { new Rule { Kind = RuleKind.RemoveText, Value = "a" } } });
        Assert.DoesNotContain("scope", json);
        Assert.DoesNotContain("block", json);
    }

    [Fact]
    public void Json_ScopeRoundTrips()
    {
        var set = new RuleSet { Name = "t", Rules = { new Rule { Kind = RuleKind.Replace, Value = "a", With = "b", Scope = RuleScope.Outside, Block = "between", BlockStart = "<<", BlockEnd = ">>" } } };
        var json = RuleSerializer.ToJson(set);
        Assert.Contains("\"scope\": \"outside\"", json);
        var back = RuleSerializer.FromJson(json).Rules[0];
        Assert.Equal(RuleScope.Outside, back.Scope);
        Assert.Equal("between", back.Block);
        Assert.Equal("<<", back.BlockStart);
        Assert.Equal(">>", back.BlockEnd);
    }

    [Fact]
    public void Json_OldFilesWithoutScopeStillLoad()
    {
        var set = RuleSerializer.FromJson("{ \"version\": 1, \"name\": \"old\", \"rules\": [ { \"kind\": \"removeText\", \"value\": \"a\" } ] }");
        Assert.Equal(RuleScope.Everywhere, set.Rules[0].Scope);
        Assert.Null(set.Rules[0].Block);
    }

    [Fact]
    public void Json_RemoveBlockKind()
    {
        var set = RuleSerializer.FromJson("{ \"name\": \"x\", \"rules\": [ { \"kind\": \"removeBlock\", \"block\": \"code-blocks\", \"with\": \"(code)\" } ] }");
        Assert.Equal("(code)", RuleEngine.Apply(set, "```\nz\n```"));
    }

    // ---- describe, catalog, templates ----

    [Fact]
    public void Describe_MentionsTheScope()
    {
        var r = new Rule { Kind = RuleKind.Replace, Value = "a", With = "b", Scope = RuleScope.Inside, Block = "code-blocks" };
        Assert.Equal("Replace “a” with “b”, only inside code blocks", r.Describe());
        Assert.Equal("Remove quoted lines", Remove("quotes").Describe());
    }

    [Fact]
    public void Blocks_AllHaveUniqueIdsAndTheirPatternsCompile()
    {
        Assert.Equal(Blocks.All.Count, Blocks.All.Select(b => b.Id).Distinct().Count());
        foreach (var b in Blocks.All)
        {
            var r = new Rule { Kind = RuleKind.RemoveBlock, Block = b.Id, BlockStart = "<", BlockEnd = ">" };
            var set = new RuleSet { Name = "t", Rules = { r } };
            Assert.True(RuleLinter.IsValid(RuleLinter.Check(set)), b.Id);
            Assert.Null(RuleEngine.Preview(set, "sample (text) <x>").Steps[0].Error);
        }
    }

    [Fact]
    public void Templates_AreValidAndRunnable()
    {
        const string sample = "Hi 😀 see https://x.com [1]\n> old reply\n```\ncode\n```\n**bold** [um] um ok";
        foreach (var t in Templates.All)
        {
            var set = t.ToRuleSet();
            Assert.True(RuleLinter.IsValid(RuleLinter.Check(set, sample)), t.Id);
            RuleEngine.Apply(set, sample);
        }
    }

    [Fact]
    public void Template_ChatAnswer_SkipsCodeAndTidiesMarkdown()
    {
        var output = RuleEngine.Apply(Templates.Find("chat-answer")!.ToRuleSet(), "See **this** [link](http://a.b)\n```\nlet x = 1;\n```\nDone");
        Assert.Contains("(code skipped)", output);
        Assert.DoesNotContain("let x", output);
        Assert.DoesNotContain("**", output);
        Assert.DoesNotContain("http", output);
        Assert.Contains("link", output);
    }

    // ---- version, saved blocks, CRLF, overlap, literal markers ----

    [Fact]
    public void Version_OrdinaryFiltersStayVersion1_BlockFiltersBecomeVersion2()
    {
        var plain = new RuleSet { Name = "t", Rules = { new Rule { Kind = RuleKind.RemoveText, Value = "a" } } };
        var blocky = new RuleSet { Name = "t", Rules = { Remove("quotes") } };
        Assert.Contains("\"version\": 1", RuleSerializer.ToJson(plain));
        Assert.Contains("\"version\": 2", RuleSerializer.ToJson(blocky));
        Assert.Equal(1, plain.Version);   // saving never changes the caller's set
    }

    [Fact]
    public void Version_NewerThanSupported_IsRefusedWithAReadableMessage()
    {
        var ex = Assert.Throws<RuleFormatException>(() => RuleSerializer.FromJson("{ \"version\": 3, \"name\": \"x\", \"rules\": [] }"));
        Assert.Contains("newer version", ex.Message);
        Assert.False(RuleSerializer.TryFromJson("{ \"version\": 3, \"name\": \"x\", \"rules\": [] }", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void UnknownRuleKind_IsRefusedNotIgnored()
        => Assert.Throws<RuleFormatException>(() => RuleSerializer.FromJson("{ \"name\": \"x\", \"rules\": [ { \"kind\": \"mystery\", \"value\": \"a\" } ] }"));

    [Fact]
    public void SavedBlocks_AreUsedByNameAndRoundTrip()
    {
        var set = new RuleSet
        {
            Name = "t",
            Blocks = new() { new BlockDef { Id = "notes", Start = "<<", End = ">>" } },
            Rules = { new Rule { Kind = RuleKind.RemoveBlock, Block = "my:notes" }, new Rule { Kind = RuleKind.RemoveText, Value = "x", Scope = RuleScope.Outside, Block = "my:notes" } },
        };
        var back = RuleSerializer.FromJson(RuleSerializer.ToJson(set));
        Assert.Equal("a  b", RuleEngine.Apply(back, "a <<n>> b"));
        Assert.Equal("a <<xx>>", RuleEngine.Apply(new RuleSet { Name = "t", Blocks = set.Blocks, Rules = { set.Rules[1] } }, "xa <<xx>>x"));
        Assert.True(RuleLinter.IsValid(RuleLinter.Check(back)));
    }

    [Fact]
    public void SavedBlocks_UnknownName_IsLintError()
    {
        var set = new RuleSet { Name = "t", Rules = { new Rule { Kind = RuleKind.RemoveBlock, Block = "my:nope" } } };
        Assert.True(Has(RuleLinter.Check(set), "BadBlock"));
    }

    [Fact]
    public void Crlf_LineBasedBlocksWork()
    {
        Assert.Equal("a\r\n\r\nb", Run(Remove("quotes"), "a\r\n> q1\r\n> q2\r\nb"));
        Assert.Equal("a\r\n\r\nb", Run(Remove("lists"), "a\r\n- x\r\n2. y\r\nb"));
        Assert.Equal("a\r\n(code)\r\nb", Run(Remove("code-blocks", "(code)"), "a\r\n```\r\nz\r\n```\r\nb"));
        Assert.Equal("a\r\n\r\nb", Run(Remove("headings"), "a\r\n## h\r\nb"));
    }

    [Fact]
    public void Crlf_ScopeInsideQuotes()
    {
        var r = new Rule { Kind = RuleKind.RemoveText, Value = "x", Scope = RuleScope.Inside, Block = "quotes" };
        Assert.Equal("x\r\n> a\r\n> b\r\nx", Run(r, "x\r\n> xa\r\n> xb\r\nx"));
    }

    [Fact]
    public void Overlap_SameKind_LeftmostBlockWinsAndBlocksDoNotNest()
    {
        var r = new Rule { Kind = RuleKind.RemoveBlock, Block = "between", BlockStart = "<<", BlockEnd = ">>" };
        Assert.Equal(" c>>", Run(r, "<<a <<b>> c>>"));
    }

    [Fact]
    public void Markers_AreLiteralNotRegex()
    {
        var r = new Rule { Kind = RuleKind.RemoveBlock, Block = "between", BlockStart = "[.*", BlockEnd = ".*]" };
        Assert.Equal("a  b [zzz] c", Run(r, "a [.*x.*] b [zzz] c"));
    }
}
