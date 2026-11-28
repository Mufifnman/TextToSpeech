using System.Text;
using System.Text.RegularExpressions;

namespace TextToSpeech.Core.Reading
{
    /// <summary>What a block of text is, for display and pacing.</summary>
    public enum BlockKind { Paragraph, Heading, ListItem, Code }

    /// <summary>
    /// One block of a markdown text with its markup already removed. <see cref="Level"/> is the heading level (1 to 6)
    /// or the list depth (0 is the top level). <see cref="Marker"/> is what is drawn in front of a list item
    /// ("•", "1.", a checkbox). <see cref="Ordinal"/> is set for numbered items and is spoken ("one"). The source
    /// offsets are character positions in the text that was parsed (end is exclusive); they let a caller anchor
    /// a pause point to a place in its own text.
    /// </summary>
    public sealed record MarkdownBlock(BlockKind Kind, int Level, string? Marker, int? Ordinal, string Text, int SourceStart, int SourceEnd);

    /// <summary>
    /// A small markdown reader: headings, bullet and numbered lists (nested, with checkboxes), fenced code blocks,
    /// block quotes, simple tables and paragraphs, with bold, italic, code, link and image markup removed.
    /// Not a full CommonMark implementation on purpose: it only has to give the reader clean text and enough
    /// structure to lay out the popup. No UI and no dependencies.
    /// </summary>
    public static class MarkdownReader
    {
        private static readonly Regex HeadingRe = new(@"^ {0,3}(#{1,6})[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$", RegexOptions.Compiled);
        private static readonly Regex ListRe = new(@"^([ \t]*)([-*+]|\d{1,9}[.)])[ \t]+(.*)$", RegexOptions.Compiled);
        private static readonly Regex FenceRe = new(@"^ {0,3}(`{3,}|~{3,})(.*)$", RegexOptions.Compiled);
        private static readonly Regex RuleRe = new(@"^ {0,3}([-*_])([ \t]*\1){2,}[ \t]*$", RegexOptions.Compiled);
        private static readonly Regex TableSepRe = new(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$", RegexOptions.Compiled);
        private static readonly Regex CheckboxRe = new(@"^\[([ xX])\][ \t]+(.*)$", RegexOptions.Compiled);

        private readonly record struct Line(string Content, int Start, int End);

        /// <summary>
        /// Quick check whether text has markdown structure worth laying out (a heading, list, code fence, quote or
        /// table). Plain prose, and text with only inline markup, returns false.
        /// </summary>
        public static bool LooksLikeMarkdown(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            foreach (var line in SplitLines(text))
            {
                string c = line.Content;
                if (HeadingRe.IsMatch(c) || FenceRe.IsMatch(c) || c.TrimStart().StartsWith("> ", StringComparison.Ordinal))
                {
                    return true;
                }
                if (ListRe.IsMatch(c) && !RuleRe.IsMatch(c))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Splits <paramref name="text"/> into blocks. With <paramref name="markdown"/> false nothing is interpreted:
        /// the blocks are just the paragraphs (separated by blank lines) with their text as written.
        /// </summary>
        public static IReadOnlyList<MarkdownBlock> Parse(string? text, bool markdown = true)
        {
            var blocks = new List<MarkdownBlock>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return blocks;
            }
            var lines = SplitLines(text);
            return markdown ? ParseMarkdown(lines) : ParsePlain(lines);
        }

        private static IReadOnlyList<MarkdownBlock> ParsePlain(List<Line> lines)
        {
            var blocks = new List<MarkdownBlock>();
            StringBuilder? para = null;
            int start = 0, end = 0;
            void Flush()
            {
                if (para != null)
                {
                    blocks.Add(new MarkdownBlock(BlockKind.Paragraph, 0, null, null, para.ToString(), start, end));
                    para = null;
                }
            }
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.Content))
                {
                    Flush();
                    continue;
                }
                if (para == null)
                {
                    para = new StringBuilder(line.Content);
                    start = line.Start;
                }
                else
                {
                    para.Append('\n').Append(line.Content);
                }
                end = line.End;
            }
            Flush();
            return blocks;
        }

        private static IReadOnlyList<MarkdownBlock> ParseMarkdown(List<Line> lines)
        {
            var blocks = new List<MarkdownBlock>();
            var indents = new List<int>();           // indent widths of the open list levels
            StringBuilder? para = null;
            int paraStart = 0, paraEnd = 0;
            StringBuilder? item = null;
            int itemDepth = 0, itemStart = 0, itemEnd = 0;
            string? itemMarker = null;
            int? itemOrdinal = null;

            void FlushPara()
            {
                if (para != null)
                {
                    string t = StripInline(para.ToString());
                    if (t.Length > 0)
                    {
                        blocks.Add(new MarkdownBlock(BlockKind.Paragraph, 0, null, null, t, paraStart, paraEnd));
                    }
                    para = null;
                }
            }
            void FlushItem()
            {
                if (item != null)
                {
                    string t = StripInline(item.ToString());
                    if (t.Length > 0)
                    {
                        blocks.Add(new MarkdownBlock(BlockKind.ListItem, itemDepth, itemMarker, itemOrdinal, t, itemStart, itemEnd));
                    }
                    item = null;
                }
            }
            void FlushAll()
            {
                FlushPara();
                FlushItem();
            }

            int i = 0;
            while (i < lines.Count)
            {
                var line = lines[i];
                string content = line.Content;

                if (string.IsNullOrWhiteSpace(content))
                {
                    FlushAll();
                    i++;
                    continue;
                }

                var fence = FenceRe.Match(content);
                if (fence.Success)
                {
                    FlushAll();
                    indents.Clear();
                    char fenceChar = fence.Groups[1].Value[0];
                    int fenceLength = fence.Groups[1].Value.Length;
                    i++;
                    while (i < lines.Count)
                    {
                        string l = lines[i].Content;
                        string trimmed = l.Trim();
                        if (trimmed.Length >= fenceLength && trimmed.All(ch => ch == fenceChar))
                        {
                            i++;
                            break;
                        }
                        if (trimmed.Length > 0)
                        {
                            blocks.Add(new MarkdownBlock(BlockKind.Code, 0, null, null, trimmed, lines[i].Start, lines[i].End));
                        }
                        i++;
                    }
                    continue;
                }

                var heading = HeadingRe.Match(content);
                if (heading.Success)
                {
                    FlushAll();
                    indents.Clear();
                    string t = StripInline(heading.Groups[2].Value);
                    if (t.Length > 0)
                    {
                        blocks.Add(new MarkdownBlock(BlockKind.Heading, heading.Groups[1].Value.Length, null, null, t, line.Start, line.End));
                    }
                    i++;
                    continue;
                }

                if (RuleRe.IsMatch(content))
                {
                    FlushAll();
                    indents.Clear();
                    i++;
                    continue;
                }

                var list = ListRe.Match(content);
                if (list.Success)
                {
                    FlushAll();
                    int indent = IndentWidth(list.Groups[1].Value);
                    while (indents.Count > 0 && indents[^1] > indent)
                    {
                        indents.RemoveAt(indents.Count - 1);
                    }
                    if (indents.Count == 0 || indents[^1] < indent)
                    {
                        indents.Add(indent);
                    }
                    itemDepth = Math.Min(indents.Count - 1, 8);
                    string bullet = list.Groups[2].Value;
                    string body = list.Groups[3].Value;
                    itemOrdinal = null;
                    if (char.IsDigit(bullet[0]) && int.TryParse(bullet.AsSpan(0, bullet.Length - 1), out int n))
                    {
                        itemOrdinal = n;
                        itemMarker = n + ".";
                    }
                    else
                    {
                        itemMarker = "•";
                    }
                    var box = CheckboxRe.Match(body);
                    if (box.Success)
                    {
                        itemMarker = box.Groups[1].Value == " " ? "☐" : "☑";
                        itemOrdinal = null;
                        body = box.Groups[2].Value;
                    }
                    item = new StringBuilder(body);
                    itemStart = line.Start;
                    itemEnd = line.End;
                    i++;
                    continue;
                }

                string trimmedLine = content.Trim();
                if (trimmedLine.StartsWith('|') || (trimmedLine.Contains('|') && i + 1 < lines.Count && TableSepRe.IsMatch(lines[i + 1].Content) && lines[i + 1].Content.Contains('|')))
                {
                    FlushAll();
                    indents.Clear();
                    if (!(trimmedLine.Contains('|') && TableSepRe.IsMatch(trimmedLine)))
                    {
                        var cells = trimmedLine.Trim('|').Split('|').Select(c => StripInline(c)).Where(c => c.Length > 0);
                        string row = string.Join(", ", cells);
                        if (row.Length > 0)
                        {
                            blocks.Add(new MarkdownBlock(BlockKind.Paragraph, 0, null, null, row, line.Start, line.End));
                        }
                    }
                    i++;
                    continue;
                }

                // Continuation of the list item above (indented), otherwise paragraph text.
                if (item != null && char.IsWhiteSpace(content[0]))
                {
                    item.Append(' ').Append(trimmedLine);
                    itemEnd = line.End;
                    i++;
                    continue;
                }

                string paragraphLine = trimmedLine;
                if (paragraphLine.StartsWith('>'))
                {
                    paragraphLine = paragraphLine.TrimStart('>', ' ', '\t');
                }
                FlushItem();
                indents.Clear();
                if (para == null)
                {
                    para = new StringBuilder(paragraphLine);
                    paraStart = line.Start;
                }
                else
                {
                    para.Append(' ').Append(paragraphLine);
                }
                paraEnd = line.End;
                i++;
            }
            FlushAll();
            return blocks;
        }

        private static int IndentWidth(string whitespace)
        {
            int width = 0;
            foreach (char c in whitespace)
            {
                width += c == '\t' ? 4 : 1;
            }
            return width;
        }

        private static List<Line> SplitLines(string text)
        {
            var lines = new List<Line>();
            int start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i == text.Length || text[i] == '\n')
                {
                    int end = i;
                    int contentEnd = end > start && text[end - 1] == '\r' ? end - 1 : end;
                    lines.Add(new Line(text.Substring(start, contentEnd - start), start, contentEnd));
                    start = i + 1;
                }
            }
            return lines;
        }

        // ---- inline markup ----

        private static readonly Regex ImageRe = new(@"!\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex LinkRe = new(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex AutoLinkRe = new(@"<(https?://[^>\s]+)>", RegexOptions.Compiled);
        private static readonly Regex CodeSpanRe = new(@"`+([^`]+)`+", RegexOptions.Compiled);
        private static readonly Regex BoldRe = new(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1", RegexOptions.Compiled);
        private static readonly Regex StarItalicRe = new(@"(?<![\w*])\*(?=[^\s*])(.+?)(?<=[^\s*])\*(?![\w*])", RegexOptions.Compiled);
        private static readonly Regex UnderscoreItalicRe = new(@"(?<![\w_])_(?=[^\s_])(.+?)(?<=[^\s_])_(?![\w_])", RegexOptions.Compiled);
        private static readonly Regex StrikeRe = new(@"~~(?=\S)(.+?)(?<=\S)~~", RegexOptions.Compiled);
        private static readonly Regex HtmlTagRe = new(@"</?[a-zA-Z][^>\n]*>", RegexOptions.Compiled);
        private static readonly Regex EscapeRe = new(@"\\([\\`*_{}\[\]()#+\-.!|>~])", RegexOptions.Compiled);

        /// <summary>Removes bold, italic, strike, code, link, image and HTML tag markup, keeping the words.</summary>
        public static string StripInline(string text)
        {
            string t = text;
            t = ImageRe.Replace(t, "$1");
            t = LinkRe.Replace(t, "$1");
            t = AutoLinkRe.Replace(t, "$1");
            t = CodeSpanRe.Replace(t, "$1");
            t = BoldRe.Replace(t, "$2");
            t = StarItalicRe.Replace(t, "$1");
            t = UnderscoreItalicRe.Replace(t, "$1");
            t = StrikeRe.Replace(t, "$1");
            t = HtmlTagRe.Replace(t, "");
            t = EscapeRe.Replace(t, "$1");
            return t.Trim();
        }
    }
}
