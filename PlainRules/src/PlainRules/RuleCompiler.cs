using System.Text.RegularExpressions;

namespace PlainRules;

/// <summary>Turns a plain-language rule into ordinary regex steps. Internal: callers use <see cref="RuleEngine"/>.</summary>
internal static class RuleCompiler
{
    internal readonly record struct Part(string Pattern, string Replacement, RegexOptions Options);

    /// <summary>Throws ArgumentException (or a regex parse exception) for rules that cannot run.</summary>
    internal static List<Part> Expand(Rule rule)
    {
        var opts = rule.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        string value = rule.Value ?? "";
        var parts = new List<Part>();

        switch (rule.Kind)
        {
            case RuleKind.RemoveText:
                Need(value, "text to remove");
                parts.Add(new Part(Literal(value, rule.WholeWords), "", opts));
                break;

            case RuleKind.Replace:
                Need(value, "text to replace");
                parts.Add(new Part(Literal(value, rule.WholeWords), (rule.With ?? "").Replace("$", "$$"), opts));
                break;

            case RuleKind.RemoveLinesStarting:
                Need(value, "line start");
                parts.Add(new Part(@"^[ \t]*" + Regex.Escape(value) + @"[^\r\n]*(?:\r?\n|\z)", "", opts | RegexOptions.Multiline));
                break;

            case RuleKind.RemoveLinesContaining:
                Need(value, "text to look for");
                parts.Add(new Part(@"^[^\r\n]*" + Literal(value, rule.WholeWords) + @"[^\r\n]*(?:\r?\n|\z)", "", opts | RegexOptions.Multiline));
                break;

            case RuleKind.RemoveBetween:
                Need(value, "start marker");
                Need(rule.To ?? "", "end marker");
                parts.Add(new Part(Regex.Escape(value) + @"[\s\S]*?" + Regex.Escape(rule.To!), "", opts));
                break;

            case RuleKind.RemoveWords:
            {
                var words = SplitWords(value);
                if (words.Count == 0) throw new ArgumentException("No words given.");
                parts.Add(new Part(@"(?<!\w)(?:" + string.Join("|", words.Select(Regex.Escape)) + @")(?!\w)", "", opts));
                break;
            }

            case RuleKind.Preset:
            {
                var preset = Presets.Find(value) ?? throw new ArgumentException($"Unknown preset \"{value}\".");
                foreach (var s in preset.Steps)
                    parts.Add(new Part(s.Pattern, s.Replacement, s.Options | opts));
                break;
            }

            case RuleKind.Regex:
                Need(value, "regex");
                parts.Add(new Part(value, rule.With ?? "", opts));
                // Validate eagerly so the caller gets the parse error.
                _ = new Regex(value, opts);
                break;

            case RuleKind.RemoveBlock:
            {
                var (pattern, blockOptions) = Blocks.PatternFor(rule.Block, rule.BlockStart, rule.BlockEnd);
                parts.Add(new Part(pattern, (rule.With ?? "").Replace("$", "$$"), blockOptions));
                break;
            }

            default:
                throw new ArgumentException("Unknown rule kind.");
        }
        return parts;
    }

    /// <summary>
    /// The blocks a rule is limited to, or null when it applies everywhere. Throws ArgumentException when the
    /// scope is set but the block is missing or unknown. A <see cref="RuleKind.RemoveBlock"/> rule has no scope.
    /// </summary>
    internal static (string Pattern, RegexOptions Options, bool Inside)? ExpandScope(Rule rule)
    {
        if (rule.Kind == RuleKind.RemoveBlock || rule.Scope == RuleScope.Everywhere) return null;
        var (pattern, options) = Blocks.PatternFor(rule.Block, rule.BlockStart, rule.BlockEnd);
        return (pattern, options | RegexOptions.CultureInvariant, rule.Scope == RuleScope.Inside);
    }

    internal static List<string> SplitWords(string value) =>
        value.Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
             .Select(w => w.Trim()).Where(w => w.Length > 0).Distinct().ToList();

    private static string Literal(string value, bool wholeWords)
    {
        var esc = Regex.Escape(value);
        if (!wholeWords) return esc;
        // Only anchor on a side that starts/ends with a word character.
        var pre = char.IsLetterOrDigit(value[0]) || value[0] == '_' ? @"(?<!\w)" : "";
        var post = char.IsLetterOrDigit(value[^1]) || value[^1] == '_' ? @"(?!\w)" : "";
        return pre + esc + post;
    }

    private static void Need(string s, string what)
    {
        if (string.IsNullOrEmpty(s)) throw new ArgumentException($"Missing {what}.");
    }
}
