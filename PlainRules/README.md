# PlainRules

Describe text clean-up rules in plain language instead of regex. A small, dependency-free .NET 8 library with:

- **Rules** in plain words: remove text, replace, remove lines, remove between two markers, remove a list of words.
- **Ready-made clean-ups** (presets): web links, citation numbers, markdown symbols, emoji, repeated punctuation, page numbers, hard line wraps and more (`Presets.All`).
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

Kinds: `removeText`, `replace`, `removeLinesStarting`, `removeLinesContaining`, `removeBetween`, `removeWords`, `preset`, `regex`.
Options on any rule: `ignoreCase`, `wholeWords` (text rules only), `disabled`.

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
