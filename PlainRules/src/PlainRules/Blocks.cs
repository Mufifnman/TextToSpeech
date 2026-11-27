using System.Text.RegularExpressions;

namespace PlainRules;

/// <summary>
/// A kind of block in a text: a code block, a quote, a list, text in brackets, text between two markers...
/// Blocks let a rule work on just part of the text (see <see cref="Rule.Scope"/>) or remove whole blocks.
/// </summary>
public sealed class BlockType
{
    internal BlockType(string id, string name, string description, string example, string pattern, RegexOptions options = RegexOptions.None)
    {
        Id = id; Name = name; Description = description; Example = example; Pattern = pattern; Options = options;
    }

    /// <summary>Stable id written to files, for example "code-blocks".</summary>
    public string Id { get; }
    /// <summary>Short name for menus, for example "code blocks".</summary>
    public string Name { get; }
    public string Description { get; }
    /// <summary>Short illustration, for tooltips.</summary>
    public string Example { get; }
    internal string Pattern { get; }
    internal RegexOptions Options { get; }
}

/// <summary>The built-in block types, plus the "between two markers" block you define yourself.</summary>
public static class Blocks
{
    /// <summary>Id of the block you define yourself with <see cref="Rule.BlockStart"/> and <see cref="Rule.BlockEnd"/>.</summary>
    public const string Between = "between";

    private const RegexOptions M = RegexOptions.Multiline;
    private const string Bullet = @"(?:[-*+•]|\d+[.)])[ \t]+";

    public static IReadOnlyList<BlockType> All { get; } = new[]
    {
        new BlockType("code-blocks", "code blocks",
            "Fenced code: from a line starting with ``` to the next ``` line (or the end of the text).",
            "```\\ncode\\n```",
            "^[ \\t]*```[^\\r\\n]*(?:\\r?\\n[\\s\\S]*?^[ \\t]*```[^\\r\\n]*|[\\s\\S]*)", M),

        new BlockType("inline-code", "inline code",
            "Short code in single backticks.", "run `npm test` now",
            "(?<!`)`[^`\\r\\n]+`(?!`)"),

        new BlockType("quotes", "quoted lines",
            "Lines starting with > (quoted replies, block quotes).", "> someone said",
            @"^[ \t]*>[^\r\n]*(?:\r?\n[ \t]*>[^\r\n]*)*", M),

        new BlockType("lists", "lists",
            "Bullet or numbered lists.", "- one\\n- two\\n1. three",
            @"^[ \t]*" + Bullet + @"[^\r\n]*(?:\r?\n[ \t]*" + Bullet + @"[^\r\n]*)*", M),

        new BlockType("headings", "headings",
            "Lines starting with #.", "## Section",
            @"^[ \t]*#{1,6}[ \t]+[^\r\n]*", M),

        new BlockType("tables", "tables",
            "Lines starting with | (markdown tables).", "| a | b |",
            @"^[ \t]*\|[^\r\n]*(?:\r?\n[ \t]*\|[^\r\n]*)*", M),

        new BlockType("brackets", "text in (brackets)",
            "Text in round brackets, on one line.", "(see below)",
            @"\([^()\r\n]*\)"),

        new BlockType("square-brackets", "text in [square brackets]",
            "Text in square brackets, on one line.", "[note]",
            @"\[[^\[\]\r\n]*\]"),

        new BlockType("quoted-text", "\"quoted text\"",
            "Text in double quotation marks, on one line.", "he said “hi”",
            "\"[^\"\\r\\n]*\"|“[^”\\r\\n]*”"),

        new BlockType(Between, "between two markers",
            "Everything from a start marker to an end marker that you choose.", "<<note … >>",
            ""),
    };

    public static BlockType? Find(string? id) =>
        All.FirstOrDefault(b => string.Equals(b.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The regex that finds the blocks a rule refers to. Throws <see cref="ArgumentException"/> for a missing or unknown block.</summary>
    internal static (string Pattern, RegexOptions Options) PatternFor(string? id, string? start, string? end)
    {
        var block = Find(id) ?? throw new ArgumentException(string.IsNullOrWhiteSpace(id) ? "Choose which kind of block." : $"Unknown block \"{id}\".");
        if (block.Id != Between) return (block.Pattern, block.Options);
        if (string.IsNullOrEmpty(start)) throw new ArgumentException("Missing start marker.");
        if (string.IsNullOrEmpty(end)) throw new ArgumentException("Missing end marker.");
        return (Regex.Escape(start) + @"[\s\S]*?" + Regex.Escape(end), RegexOptions.None);
    }
}
