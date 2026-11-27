using System.Text;
using System.Text.RegularExpressions;

namespace PlainRules;

/// <summary>Result of running a rule set on sample text.</summary>
public sealed class PreviewResult
{
    public string Output { get; init; } = "";
    /// <summary>One entry per rule in the set, in order.</summary>
    public IReadOnlyList<RuleStepResult> Steps { get; init; } = Array.Empty<RuleStepResult>();
}

/// <summary>What one rule did during a preview.</summary>
public sealed class RuleStepResult
{
    public int RuleIndex { get; init; }
    /// <summary>How many places the rule matched.</summary>
    public int Matches { get; init; }
    /// <summary>Set when the rule could not run (bad regex, timeout) or was disabled.</summary>
    public string? Error { get; init; }
    public bool Skipped { get; init; }
}

/// <summary>A rule set turned into ready-to-run regexes. Build once, apply many times.</summary>
public sealed class CompiledRuleSet
{
    private sealed record Step(int RuleIndex, Regex? Regex, string Replacement, string? Error, bool Skipped, Regex? Block = null, bool Inside = false);

    private readonly List<Step> _steps = new();

    internal CompiledRuleSet(RuleSet set, TimeSpan timeout)
    {
        for (int i = 0; i < set.Rules.Count; i++)
        {
            var rule = set.Rules[i];
            if (rule.Disabled) { _steps.Add(new Step(i, null, "", null, true)); continue; }
            try
            {
                var parts = RuleCompiler.Expand(rule);
                var scope = RuleCompiler.ExpandScope(rule);
                Regex? block = scope is { } sc ? new Regex(sc.Pattern, sc.Options, timeout) : null;
                if (parts.Count == 0) { _steps.Add(new Step(i, null, "", "Nothing to do.", true)); continue; }
                foreach (var p in parts)
                    _steps.Add(new Step(i, new Regex(p.Pattern, p.Options | RegexOptions.CultureInvariant, timeout), p.Replacement, null, false, block, scope?.Inside ?? false));
            }
            catch (Exception ex) when (ex is ArgumentException or RegexParseException or InvalidOperationException)
            {
                _steps.Add(new Step(i, null, "", ex.Message, true));
            }
        }
    }

    /// <summary>Apply every rule, in order. Rules that fail or time out are skipped.</summary>
    public string Apply(string input) => Run(input, null);

    /// <summary>Apply every rule and report what each one matched.</summary>
    public PreviewResult Preview(string sample)
    {
        var matches = new int[Math.Max(1, _steps.Count == 0 ? 0 : _steps.Max(s => s.RuleIndex) + 1)];
        var errors = new string?[matches.Length];
        var skipped = new bool[matches.Length];
        foreach (var s in _steps) { errors[s.RuleIndex] ??= s.Error; skipped[s.RuleIndex] |= s.Skipped; }
        var output = Run(sample, (idx, count, err) => { matches[idx] += count; if (err != null) errors[idx] = err; });
        var results = new List<RuleStepResult>();
        for (int i = 0; i < matches.Length && _steps.Count > 0; i++)
            results.Add(new RuleStepResult { RuleIndex = i, Matches = matches[i], Error = errors[i], Skipped = skipped[i] });
        return new PreviewResult { Output = output, Steps = results };
    }

    private string Run(string input, Action<int, int, string?>? report)
    {
        var text = input ?? "";
        foreach (var s in _steps)
        {
            if (s.Regex == null) continue;
            try
            {
                int count = 0;
                string Replace(string segment) => s.Regex.Replace(segment, m =>
                {
                    count++;
                    return m.Result(s.Replacement);
                });
                text = s.Block == null ? Replace(text) : ReplaceInBlocks(text, s.Block, s.Inside, Replace);
                report?.Invoke(s.RuleIndex, count, null);
            }
            catch (RegexMatchTimeoutException)
            {
                report?.Invoke(s.RuleIndex, 0, "Took too long and was skipped.");
            }
        }
        return text;
    }

    /// <summary>
    /// Run <paramref name="replace"/> only on the parts of the text inside the blocks, or only on the parts outside them.
    /// Each part is handled on its own, so a match never crosses a block border.
    /// </summary>
    private static string ReplaceInBlocks(string text, Regex block, bool inside, Func<string, string> replace)
    {
        var sb = new StringBuilder(text.Length);
        int pos = 0;
        foreach (Match b in block.Matches(text))
        {
            if (b.Length == 0) continue;
            string gap = text.Substring(pos, b.Index - pos);
            sb.Append(inside ? gap : replace(gap));
            sb.Append(inside ? replace(b.Value) : b.Value);
            pos = b.Index + b.Length;
        }
        string tail = text.Substring(pos);
        sb.Append(inside ? tail : replace(tail));
        return sb.ToString();
    }
}

/// <summary>Entry points for running rule sets.</summary>
public static class RuleEngine
{
    /// <summary>Longest a single rule may run on one text before it is skipped.</summary>
    public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMilliseconds(300);

    public static CompiledRuleSet Compile(RuleSet set) => new(set, DefaultTimeout);

    public static string Apply(RuleSet set, string input) => Compile(set).Apply(input);

    public static PreviewResult Preview(RuleSet set, string sample) => Compile(set).Preview(sample);
}
