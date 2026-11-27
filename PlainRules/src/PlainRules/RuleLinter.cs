using System.Text.RegularExpressions;

namespace PlainRules;

public enum LintSeverity { Info, Warning, Error }

/// <summary>One problem found by <see cref="RuleLinter"/>.</summary>
public sealed class LintIssue
{
    /// <summary>Index of the rule, or -1 when the issue is about the whole set.</summary>
    public int RuleIndex { get; init; }
    public LintSeverity Severity { get; init; }
    /// <summary>Stable machine-readable code, for example "EmptyValue".</summary>
    public string Code { get; init; } = "";
    /// <summary>Plain-language message to show next to the rule.</summary>
    public string Message { get; init; } = "";
    public override string ToString() => (RuleIndex >= 0 ? $"Rule {RuleIndex + 1}: " : "") + Message;
}

/// <summary>Checks a rule set for mistakes. Errors mean the set (or a rule) cannot run.</summary>
public static class RuleLinter
{
    /// <param name="set">The rule set to check.</param>
    /// <param name="sample">Optional sample text; enables "matches nothing" and "changes nothing" hints.</param>
    /// <param name="existingNames">Names already in use by other sets, to catch collisions.</param>
    public static IReadOnlyList<LintIssue> Check(RuleSet set, string? sample = null, IEnumerable<string>? existingNames = null)
    {
        var issues = new List<LintIssue>();
        void Add(int idx, LintSeverity sev, string code, string msg) =>
            issues.Add(new LintIssue { RuleIndex = idx, Severity = sev, Code = code, Message = msg });

        var name = (set.Name ?? "").Trim();
        if (name.Length == 0) Add(-1, LintSeverity.Error, "EmptyName", "Give the filter a name.");
        else if (existingNames != null && existingNames.Any(n => string.Equals(n?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            Add(-1, LintSeverity.Error, "DuplicateName", $"A filter named “{name}” already exists.");
        if (set.Rules.Count == 0) Add(-1, LintSeverity.Warning, "NoRules", "This filter has no rules yet.");

        var seen = new HashSet<string>();
        for (int i = 0; i < set.Rules.Count; i++)
        {
            var r = set.Rules[i];
            if (r.Disabled) { Add(i, LintSeverity.Info, "Disabled", "This rule is turned off."); continue; }

            var key = $"{r.Kind}|{r.Value}|{r.With}|{r.To}|{r.IgnoreCase}|{r.WholeWords}|{r.Scope}|{r.Block}|{r.BlockStart}|{r.BlockEnd}";
            if (!seen.Add(key)) Add(i, LintSeverity.Warning, "DuplicateRule", "Same as an earlier rule.");

            List<RuleCompiler.Part> parts;
            try { parts = RuleCompiler.Expand(r); }
            catch (ArgumentException ex)
            {
                var code = r.Kind == RuleKind.Preset ? "UnknownPreset"
                         : r.Kind == RuleKind.RemoveBlock ? "BadBlock"
                         : r.Kind == RuleKind.Regex && r.Value.Length > 0 ? "InvalidRegex"
                         : "EmptyValue";
                Add(i, LintSeverity.Error, code, ex.Message);
                continue;
            }

            try { _ = RuleCompiler.ExpandScope(r); }
            catch (ArgumentException ex)
            {
                Add(i, LintSeverity.Error, "BadScope", ex.Message);
                continue;
            }
            if (r.Kind == RuleKind.RemoveBlock && r.Scope != RuleScope.Everywhere)
                Add(i, LintSeverity.Info, "ScopeIgnored", "“Where” does not apply to a rule that removes blocks.");

            if (r.Kind == RuleKind.Regex)
            {
                try
                {
                    var rx = new Regex(parts[0].Pattern, parts[0].Options, TimeSpan.FromMilliseconds(250));
                    if (rx.Match("").Success)
                        Add(i, LintSeverity.Warning, "MatchesEmpty", "This regex can match empty text, which can insert text everywhere.");
                }
                catch (RegexMatchTimeoutException) { }
            }
            if (r.Kind == RuleKind.Replace && string.Equals(r.Value, r.With ?? "", r.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                Add(i, LintSeverity.Warning, "NoOpReplace", "Replacing text with itself changes nothing.");
            if (r.WholeWords && r.Kind is RuleKind.RemoveLinesStarting or RuleKind.RemoveBetween or RuleKind.Preset or RuleKind.Regex)
                Add(i, LintSeverity.Info, "WholeWordsIgnored", "“Whole words” does not apply to this kind of rule.");
        }

        if (sample != null && issues.All(x => x.Severity != LintSeverity.Error))
        {
            var preview = RuleEngine.Preview(set, sample);
            foreach (var s in preview.Steps)
            {
                if (s.Skipped) continue;
                if (s.Error != null) Add(s.RuleIndex, LintSeverity.Warning, "RuleFailed", s.Error);
                else if (s.Matches == 0) Add(s.RuleIndex, LintSeverity.Info, "NoMatchInSample", "Matches nothing in the sample text.");
            }
        }
        return issues;
    }

    /// <summary>True when no issue is an error.</summary>
    public static bool IsValid(IEnumerable<LintIssue> issues) => issues.All(i => i.Severity != LintSeverity.Error);
}
