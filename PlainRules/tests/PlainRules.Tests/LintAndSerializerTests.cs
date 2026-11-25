using PlainRules;
using Xunit;

namespace PlainRules.Tests;

public class LintAndSerializerTests
{
    private static RuleSet Set(params Rule[] rules) => new() { Name = "Mine", Rules = rules.ToList() };

    private static bool Has(IEnumerable<LintIssue> issues, string code) => issues.Any(i => i.Code == code);

    [Fact] public void NullRulesAndNullEntries_AreDropped()
    {
        Assert.Empty(RuleSerializer.FromJson("{ \"rules\": null }").Rules);
        var set = RuleSerializer.FromJson("{ \"rules\": [ null, { \"kind\": \"removeText\", \"value\": \"x\" } ] }");
        Assert.Single(set.Rules);
    }

    [Fact] public void EmptyName_IsError() =>
        Assert.True(Has(RuleLinter.Check(new RuleSet { Rules = { new Rule { Kind = RuleKind.RemoveText, Value = "x" } } }), "EmptyName"));

    [Fact] public void DuplicateName_IsError() =>
        Assert.True(Has(RuleLinter.Check(Set(new Rule { Kind = RuleKind.RemoveText, Value = "x" }), null, new[] { "mine" }), "DuplicateName"));

    [Fact] public void EmptyValue_IsError()
    {
        var issues = RuleLinter.Check(Set(new Rule { Kind = RuleKind.RemoveText, Value = "" }));
        Assert.True(Has(issues, "EmptyValue"));
        Assert.False(RuleLinter.IsValid(issues));
    }

    [Fact] public void InvalidRegex_IsError() =>
        Assert.True(Has(RuleLinter.Check(Set(new Rule { Kind = RuleKind.Regex, Value = "(" })), "InvalidRegex"));

    [Fact] public void RegexMatchingEmpty_IsWarning() =>
        Assert.True(Has(RuleLinter.Check(Set(new Rule { Kind = RuleKind.Regex, Value = "a*", With = "-" })), "MatchesEmpty"));

    [Fact] public void UnknownPreset_IsError() =>
        Assert.True(Has(RuleLinter.Check(Set(new Rule { Kind = RuleKind.Preset, Value = "nope" })), "UnknownPreset"));

    [Fact] public void DuplicateRule_IsWarning()
    {
        var r = new Rule { Kind = RuleKind.RemoveText, Value = "x" };
        var r2 = new Rule { Kind = RuleKind.RemoveText, Value = "x" };
        Assert.True(Has(RuleLinter.Check(Set(r, r2)), "DuplicateRule"));
    }

    [Fact] public void NoOpReplace_IsWarning() =>
        Assert.True(Has(RuleLinter.Check(Set(new Rule { Kind = RuleKind.Replace, Value = "a", With = "a" })), "NoOpReplace"));

    [Fact] public void NoMatchInSample_IsInfo() =>
        Assert.True(Has(RuleLinter.Check(Set(new Rule { Kind = RuleKind.RemoveText, Value = "zzz" }), "hello"), "NoMatchInSample"));

    [Fact] public void GoodSet_HasNoErrors() =>
        Assert.True(RuleLinter.IsValid(RuleLinter.Check(Set(new Rule { Kind = RuleKind.Preset, Value = "urls" }), "see http://a.b")));

    [Fact]
    public void Json_RoundTrips()
    {
        var set = Set(
            new Rule { Kind = RuleKind.Replace, Value = "a", With = "b", IgnoreCase = true },
            new Rule { Kind = RuleKind.Preset, Value = "urls" });
        set.Group = "Mine";
        var back = RuleSerializer.FromJson(RuleSerializer.ToJson(set));
        Assert.Equal("Mine", back.Name);
        Assert.Equal("Mine", back.Group);
        Assert.Equal(2, back.Rules.Count);
        Assert.Equal(RuleKind.Replace, back.Rules[0].Kind);
        Assert.True(back.Rules[0].IgnoreCase);
        Assert.Equal("urls", back.Rules[1].Value);
    }

    [Fact]
    public void Json_AcceptsCommentsTrailingCommasAndLooseCase()
    {
        const string json = """
        {
          // my filter
          "name": "Hand made",
          "rules": [
            { "kind": "removeText", "value": "foo", },
          ],
        }
        """;
        var set = RuleSerializer.FromJson(json);
        Assert.Equal("Hand made", set.Name);
        Assert.Equal(RuleKind.RemoveText, set.Rules[0].Kind);
    }

    [Fact]
    public void Json_BadInput_GivesReadableError()
    {
        Assert.False(RuleSerializer.TryFromJson("{ not json", out var set, out var error));
        Assert.Null(set);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.False(RuleSerializer.TryFromJson("{ \"rules\": [ { \"kind\": \"bogus\" } ] }", out _, out _));
    }

    [Fact]
    public void SafeFileName_ReplacesBadCharacters() =>
        Assert.Equal("a-b", RuleSerializer.SafeFileName(" a/b "));
}
