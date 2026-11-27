# PlainRules

Describe text clean-up rules in plain language instead of regex. A small, dependency-free .NET 8 library with:

- **Rules** in plain words: remove text, replace, remove lines, remove between two markers, remove a list of words.
- **Ready-made clean-ups** (presets): web links, citation numbers, markdown symbols, emoji, repeated punctuation, page numbers, hard line wraps and more (`Presets.All`).
- **Blocks**: remove code blocks, quotes, lists, text in brackets or text between your own markers, or limit any rule to the inside or outside of such blocks.
- **Templates**: ready-made starting filters (`Templates.All`).
- **Lint** that explains mistakes in plain language (`RuleLinter.Check`).
- **Live preview** with a match count per rule (`RuleEngine.Preview`).
- **Hand-editable JSON** files (comments and trailing commas allowed).
- An **advanced raw regex** rule for people who want it. Every rule has a timeout so a bad pattern cannot freeze your app.

It knows nothing about any particular app. It has no UI and no dependencies.

## Quick start

```csharp
using PlainRules;

var set = new RuleSet
{
    Name = "Clean web text",
    Rules =
    {
        new Rule { Kind = RuleKind.Preset, Value = "urls" },
        new Rule { Kind = RuleKind.Preset, Value = "citations" },
        new Rule { Kind = RuleKind.Replace, Value = "&", With = "and" },
        new Rule { Kind = RuleKind.RemoveLinesStarting, Value = "Advertisement" },
    },
};

var issues = RuleLinter.Check(set, sample: text);        // mistakes and hints
var compiled = RuleEngine.Compile(set);                  // compile once ...
string clean = compiled.Apply(text);                     // ... apply many times
var preview = RuleEngine.Preview(set, text);             // output + matches per rule
File.WriteAllText("clean-web-text.json", RuleSerializer.ToJson(set));
```

## File format

One rule set per file. Rules run from top to bottom.

```jsonc
{
  "version": 1,
  "name": "Clean web text",
  "group": "Mine",
  "rules": [
    { "kind": "preset", "value": "urls" },
    { "kind": "replace", "value": "&", "with": "and", "ignoreCase": false, "wholeWords": false },
    { "kind": "removeBetween", "value": "[[", "to": "]]" },
    { "kind": "removeWords", "value": "um, uh, you know" },
    { "kind": "regex", "value": "\\s+$", "with": "" }   // advanced
  ]
}
```

Kinds: `removeText`, `replace`, `removeLinesStarting`, `removeLinesContaining`, `removeBetween`, `removeWords`, `preset`, `regex`, `removeBlock`.
Options on any rule: `ignoreCase`, `wholeWords` (text rules only), `disabled`.

## Blocks

A block is a part of the text with a recognizable shape. `removeBlock` removes every block of one kind (or replaces it with `with`):

```jsonc
{ "kind": "removeBlock", "block": "code-blocks", "with": "(code skipped)" }
{ "kind": "removeBlock", "block": "between", "blockStart": "<<", "blockEnd": ">>" }
```

Any other rule can be limited to blocks with `scope` (`inside` or `outside`, default is everywhere) and `block`:

```jsonc
{ "kind": "replace", "value": "&", "with": "and", "scope": "outside", "block": "code-blocks" }
```

Block ids (`Blocks.All`): `code-blocks`, `inline-code`, `quotes`, `lists`, `headings`, `tables`, `brackets`, `square-brackets`, `quoted-text`, and `between` (with `blockStart` and `blockEnd`).
A scoped rule runs on each block (or each stretch of text between blocks) on its own, so a match never crosses a block border. Rules run top to bottom, so a later rule sees the text after earlier removals.
Files without these fields keep working unchanged.

**Detection rules**
- Each rule uses one block kind. Within a kind, blocks are found left to right and never overlap: the leftmost block wins and blocks do not nest (for markers, the first end marker closes the block).
- Line-based kinds (`quotes`, `lists`, `headings`, `tables`, `code-blocks`) handle both `\n` and `\r\n` (clipboard text is usually `\r\n`).
- Markers (`blockStart`, `blockEnd`) are literal text, never regex.
- Detection works on plain text only. OCR output has no fences or tables, so those find nothing there, but line-based kinds can match OCR lines that happen to start with `>`, `-` or `1.`. Custom filters run on both clipboard text and OCR.
- Rules run top to bottom; a rule sees the text after earlier rules, and its match count in `Preview` counts only matches inside its scope.
- Every rule, and the block search of a scoped rule, runs under the match timeout (`RuleEngine.DefaultTimeout`, 300 ms); a rule that times out is skipped.

**Saved blocks.** Define a block once in the file and use it by name, so markers are not retyped:

```jsonc
{ "version": 2, "name": "Notes", "blocks": [ { "id": "notes", "start": "<<", "end": ">>" } ],
  "rules": [ { "kind": "removeBlock", "block": "my:notes" } ] }
```
Built-in ids have no prefix; ids you define are written `my:<id>`.

**File version.** A file is written as version 2 only when it uses blocks, scopes or `removeBlock`; other files stay version 1. This version refuses a file newer than it understands with a readable message. Older builds do not check the version and would run a scoped rule everywhere, so keep scoped filters on builds that include this change.

## Presets

| id | what it does |
|---|---|
| `whitespace` | squeeze repeated spaces and blank lines |
| `urls` | remove web links |
| `citations` | remove `[12]`, `[3, 4]`, `[5-7]` |
| `markdown` | remove `#`, `**`, `_`, `` ` ``, `>` marks |
| `markdown-links` | `[text](url)` becomes `text` |
| `footnotes` | remove superscript digits and `[a]` marks |
| `emoji` | remove emoji and pictographs |
| `repeated-punctuation` | `!!!` becomes `!` |
| `emails` | remove email addresses |
| `page-numbers` | remove lines that are only a page number |
| `parentheses` | remove `(text in brackets)` |
| `hyphen-breaks` | join `inter-` / `national` |
| `unwrap-lines` | join hard-wrapped lines in a paragraph |
| `odd-characters` | zero-width characters, soft hyphens, non-breaking spaces |
| `html` | remove tags and comments |

## Build and test

```
dotnet build PlainRules.sln
dotnet test PlainRules.sln
```

## License

MIT (see `LICENSE`; the copyright line is a placeholder to fill in before publishing).
