using System.Text.RegularExpressions;

namespace PlainRules;

/// <summary>A ready-made common clean-up, made of one or more ordinary regex steps.</summary>
public sealed class Preset
{
    internal Preset(string id, string name, string description, string example, params PresetStep[] steps)
    {
        Id = id; Name = name; Description = description; Example = example; Steps = steps;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    /// <summary>Short before/after illustration, for tooltips.</summary>
    public string Example { get; }
    internal IReadOnlyList<PresetStep> Steps { get; }

    /// <summary>The regex steps behind the preset, for display or documentation.</summary>
    public IEnumerable<(string Pattern, string Replacement)> Patterns => Steps.Select(s => (s.Pattern, s.Replacement));
}

internal readonly record struct PresetStep(string Pattern, string Replacement, RegexOptions Options = RegexOptions.None);

/// <summary>The built-in list of common clean-ups, most useful first.</summary>
public static class Presets
{
    private const RegexOptions M = RegexOptions.Multiline;

    public static IReadOnlyList<Preset> All { get; } = new[]
    {
        new Preset("whitespace", "Extra spaces and blank lines",
            "Squeeze repeated spaces and more than one blank line.", "a   b  becomes  a b",
            new PresetStep(@"[ \t]{2,}", " "),
            new PresetStep(@"(?:[ \t]*\r?\n){3,}", "\n\n")),

        new Preset("urls", "Web links",
            "Remove http(s):// and www. links.", "see https://x.com/a?b=1.  becomes  see .",
            new PresetStep(@"(?:https?://|www\.)[^\s<>""]*[^\s<>"".,;:!?)\]]", "")),

        new Preset("citations", "Citation numbers",
            "Remove numbered citations such as [12] or [3, 4] or [5-7].", "as shown [12] [3, 4]  becomes  as shown",
            new PresetStep(@"[ \t]?\[\d+(?:\s*[,–\-]\s*\d+)*\]", "")),

        new Preset("markdown", "Markdown symbols",
            "Remove #, **, __, *, _, ` and > marks but keep the words.", "# Title **bold**  becomes  Title bold",
            new PresetStep(@"^#{1,6}[ \t]+", "", M),
            new PresetStep(@"^>[ \t]?", "", M),
            new PresetStep(@"(\*\*|__)(.+?)\1", "$2"),
            new PresetStep(@"(?<!\w)([*_])(\S(?:[^\r\n]*?\S)?)\1(?!\w)", "$2"),
            new PresetStep(@"`([^`\r\n]+)`", "$1")),

        new Preset("markdown-links", "Markdown links (keep the text)",
            "Turn [text](url) into text.", "[docs](http://a.b)  becomes  docs",
            new PresetStep(@"!?\[([^\]]*)\]\([^)\s]*\)", "$1")),

        new Preset("footnotes", "Footnote marks",
            "Remove superscript digits and [a]-style marks.", "word¹²  becomes  word",
            new PresetStep(@"[¹²³⁰-⁹]+|\[[a-z]\]", "")),

        new Preset("emoji", "Emoji and pictographs",
            "Remove emoji and similar symbols (keeps © ® ™).", "great \U0001F389  becomes  great",
            new PresetStep(@"[ \t]?(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[☀-➿⭐⭕️‍])+", "")),

        new Preset("repeated-punctuation", "Repeated punctuation",
            "Shorten !!! ??? ,,, and long runs of dots.", "what!!!???  becomes  what!?",
            new PresetStep(@"([!?,;:])\1+", "$1"),
            new PresetStep(@"\.{4,}", "...")),

        new Preset("emails", "Email addresses",
            "Remove email addresses.", "mail me@site.com now  becomes  mail  now",
            new PresetStep(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+", "")),

        new Preset("page-numbers", "Page numbers and headers",
            "Remove lines that are only a page number, \"Page 4 of 20\" or \"- 12 -\".", "Page 4 of 20  (whole line removed)",
            new PresetStep(@"^[ \t]*(?:Page[ \t]+)?\d+(?:[ \t]+of[ \t]+\d+)?[ \t]*(?:\r?\n|\z)", "", M | RegexOptions.IgnoreCase),
            new PresetStep(@"^[ \t]*-[ \t]*\d+[ \t]*-[ \t]*(?:\r?\n|\z)", "", M)),

        new Preset("parentheses", "Text in parentheses",
            "Remove (anything in round brackets).", "(see Fig. 2) done  becomes  done",
            new PresetStep(@"[ \t]*\([^()\r\n]*\)", "")),

        new Preset("hyphen-breaks", "Line-break hyphens",
            "Join words split across lines with a hyphen.", "inter-\\nnational  becomes  international",
            new PresetStep(@"(\w)-[ \t]*\r?\n[ \t]*(\w)", "$1$2")),

        new Preset("unwrap-lines", "Hard line wraps inside paragraphs",
            "Join lines of one paragraph (for text copied from PDFs). Blank lines stay.", "one\\ntwo  becomes  one two",
            new PresetStep(@"(?<![\r\n.!?:])[ \t]*\r?\n(?![ \t]*\r?\n)", " ")),

        new Preset("odd-characters", "Invisible and odd characters",
            "Remove zero-width spaces and soft hyphens; turn non-breaking spaces into spaces.", "zero​width  becomes  zerowidth",
            new PresetStep(@"[​-‍­﻿]", ""),
            new PresetStep(@" ", " ")),

        new Preset("html", "HTML tags",
            "Remove <tags> and <!-- comments -->.", "<b>x</b>  becomes  x",
            new PresetStep(@"<!--[\s\S]*?-->", ""),
            new PresetStep(@"</?[A-Za-z][^>]*>", "")),
    };

    /// <summary>Find a preset by id (case-insensitive), or null.</summary>
    public static Preset? Find(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
