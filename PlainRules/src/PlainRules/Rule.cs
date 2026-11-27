using System.Text.Json.Serialization;

namespace PlainRules;

/// <summary>The kind of clean-up a rule performs. Written as a plain string in JSON.</summary>
public enum RuleKind
{
    /// <summary>Remove every occurrence of <see cref="Rule.Value"/>.</summary>
    RemoveText,
    /// <summary>Replace <see cref="Rule.Value"/> with <see cref="Rule.With"/>.</summary>
    Replace,
    /// <summary>Remove whole lines that start with <see cref="Rule.Value"/> (leading spaces ignored).</summary>
    RemoveLinesStarting,
    /// <summary>Remove whole lines that contain <see cref="Rule.Value"/>.</summary>
    RemoveLinesContaining,
    /// <summary>Remove everything from <see cref="Rule.Value"/> up to and including <see cref="Rule.To"/>.</summary>
    RemoveBetween,
    /// <summary>Remove each word in <see cref="Rule.Value"/> (separated by commas or new lines).</summary>
    RemoveWords,
    /// <summary>A ready-made common clean-up. <see cref="Rule.Value"/> is the preset id, see <see cref="Presets"/>.</summary>
    Preset,
    /// <summary>Advanced: <see cref="Rule.Value"/> is a raw .NET regex, <see cref="Rule.With"/> its replacement.</summary>
    Regex,
    /// <summary>Remove (or replace with <see cref="Rule.With"/>) every block of the kind named in <see cref="Rule.Block"/>, for example code blocks.</summary>
    RemoveBlock,
}

/// <summary>Where in the text a rule applies.</summary>
public enum RuleScope
{
    /// <summary>The whole text (the default).</summary>
    Everywhere,
    /// <summary>Only inside the blocks named in <see cref="Rule.Block"/>.</summary>
    Inside,
    /// <summary>Everywhere except inside the blocks named in <see cref="Rule.Block"/>.</summary>
    Outside,
}

/// <summary>One plain-language rule. Which properties matter depends on <see cref="Kind"/>.</summary>
public sealed class Rule
{
    public RuleKind Kind { get; set; }

    /// <summary>The text to look for (or the preset id, word list or regex, depending on the kind).</summary>
    public string Value { get; set; } = "";

    /// <summary>Replacement text for <see cref="RuleKind.Replace"/> and <see cref="RuleKind.Regex"/>. Empty means remove.</summary>
    public string? With { get; set; }

    /// <summary>End marker for <see cref="RuleKind.RemoveBetween"/>.</summary>
    public string? To { get; set; }

    /// <summary>Ignore upper/lower case when matching.</summary>
    public bool IgnoreCase { get; set; }

    /// <summary>Only match whole words, not parts of longer words.</summary>
    public bool WholeWords { get; set; }

    /// <summary>Limit the rule to the inside or the outside of blocks. Needs <see cref="Block"/>. Default: everywhere.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public RuleScope Scope { get; set; }

    /// <summary>Block type id (see <see cref="Blocks"/>), for example "code-blocks". Used by <see cref="Scope"/> and <see cref="RuleKind.RemoveBlock"/>.</summary>
    public string? Block { get; set; }

    /// <summary>Start marker when <see cref="Block"/> is "between".</summary>
    public string? BlockStart { get; set; }

    /// <summary>End marker when <see cref="Block"/> is "between".</summary>
    public string? BlockEnd { get; set; }

    /// <summary>Turn the rule off without deleting it.</summary>
    public bool Disabled { get; set; }

    /// <summary>A short human-readable summary, for lists in an editor.</summary>
    public string Describe()
    {
        string what = DescribeKind();
        if (Kind == RuleKind.RemoveBlock || Scope == RuleScope.Everywhere) return what;
        return what + (Scope == RuleScope.Inside ? ", only inside " : ", everywhere except ") + BlockName();
    }

    private string BlockName()
    {
        if (Block == Blocks.Between) return "text between " + (BlockStart ?? "") + " and " + (BlockEnd ?? "");
        return Blocks.Find(Block)?.Name ?? "blocks";
    }

    private string DescribeKind()
    {
        string q(string? s) => "“" + (s ?? "") + "”";
        return Kind switch
        {
            RuleKind.RemoveText => "Remove " + q(Value),
            RuleKind.Replace => "Replace " + q(Value) + " with " + q(With),
            RuleKind.RemoveLinesStarting => "Remove lines starting with " + q(Value),
            RuleKind.RemoveLinesContaining => "Remove lines containing " + q(Value),
            RuleKind.RemoveBetween => "Remove from " + q(Value) + " to " + q(To),
            RuleKind.RemoveWords => "Remove words: " + Value.Replace("\r", "").Replace("\n", ", "),
            RuleKind.Preset => Presets.Find(Value)?.Name ?? ("Unknown preset " + q(Value)),
            RuleKind.Regex => "Regex " + q(Value),
            RuleKind.RemoveBlock => string.IsNullOrEmpty(With) ? "Remove " + BlockName() : "Replace " + BlockName() + " with " + q(With),
            _ => Kind.ToString(),
        };
    }
}

/// <summary>A named, ordered list of rules. This is what is saved to a file.</summary>
public sealed class RuleSet
{
    /// <summary>File format version, for future changes.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Display name, for example the menu entry.</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional group the set belongs to, for example a submenu.</summary>
    public string? Group { get; set; }

    public string? Description { get; set; }

    /// <summary>Rules, applied from first to last.</summary>
    public List<Rule> Rules { get; set; } = new();
}
