namespace PlainRules;

/// <summary>A ready-made starting point for a whole filter: a name and a sensible list of rules.</summary>
public sealed class RuleTemplate
{
    internal RuleTemplate(string id, string name, string description, Func<List<Rule>> rules)
    {
        Id = id; Name = name; Description = description; _rules = rules;
    }

    private readonly Func<List<Rule>> _rules;

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }

    /// <summary>A fresh copy of the template's rules, safe to edit.</summary>
    public List<Rule> CreateRules() => _rules();

    public RuleSet ToRuleSet(string? name = null) => new() { Name = name ?? Name, Description = Description, Rules = CreateRules() };
}

/// <summary>Built-in starting points. Each is only a list of ordinary rules; edit them freely after adding.</summary>
public static class Templates
{
    private static Rule Preset(string id) => new() { Kind = RuleKind.Preset, Value = id };

    public static IReadOnlyList<RuleTemplate> All { get; } = new[]
    {
        new RuleTemplate("web-page", "Web page text",
            "Drop links, citation numbers, emoji and extra blank space from copied web text.",
            () => new List<Rule> { Preset("urls"), Preset("citations"), Preset("emoji"), Preset("repeated-punctuation"), Preset("whitespace") }),

        new RuleTemplate("chat-answer", "Chat or markdown answer",
            "Skip code blocks, tidy markdown marks and links, drop emoji.",
            () => new List<Rule>
            {
                new() { Kind = RuleKind.RemoveBlock, Block = "code-blocks", With = "(code skipped)" },
                new() { Kind = RuleKind.RemoveBlock, Block = "inline-code", With = "code" },
                Preset("markdown-links"), Preset("markdown"), Preset("urls"), Preset("emoji"), Preset("whitespace"),
            }),

        new RuleTemplate("academic-paper", "Academic paper",
            "Join broken lines and hyphenated words, drop citations, footnote marks and page numbers.",
            () => new List<Rule> { Preset("hyphen-breaks"), Preset("unwrap-lines"), Preset("citations"), Preset("footnotes"), Preset("page-numbers"), Preset("whitespace") }),

        new RuleTemplate("email-thread", "Email or forum thread",
            "Skip quoted replies, links and email addresses.",
            () => new List<Rule>
            {
                new() { Kind = RuleKind.RemoveBlock, Block = "quotes" },
                Preset("urls"), Preset("emails"), Preset("whitespace"),
            }),

        new RuleTemplate("transcript", "Spoken transcript",
            "Remove filler words and text in [square brackets] such as [music].",
            () => new List<Rule>
            {
                new() { Kind = RuleKind.RemoveWords, Value = "um, uh, er, you know", IgnoreCase = true },
                new() { Kind = RuleKind.RemoveBlock, Block = "square-brackets" },
                Preset("repeated-punctuation"), Preset("whitespace"),
            }),
    };

    public static RuleTemplate? Find(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}
