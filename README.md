# TextToSpeech

A Windows tool that reads text aloud, aimed at people who find reading hard (for example dyslexia or low literacy).
It reads text from the clipboard, from whatever is selected in another app, or from a region of the screen (OCR),
and shows the sentence being spoken as it goes.

## What it does

- **Voices:** local neural voices (Piper and Kokoro, run through sherpa-onnx, nothing leaves the machine) that you download
  from the app, plus the Windows voices as a fallback.
- **Reading:** play, pause, skip, rewind, click a word to jump, adjustable speed, volume and pause between sentences.
- **Hotkeys** (configurable): Alt+R reads the selection quietly, Ctrl+Alt+R reads it and shows the window, Alt+O and
  Ctrl+Alt+O read a screen region, Alt+Space plays or pauses, Alt+Delete clears.
- **Text filters:** built-in clean-ups (citations and similar) and your own plain-language filters, edited in the app.
- **Always on:** lives in the tray, optional reading popup, optional start with Windows (`--background` starts hidden).
- **Local API (off by default):** other programs can speak text through a token-protected loopback HTTP API.
  See [integrations/README.md](integrations/README.md).

## Build and test

Needs the .NET 8 SDK and Windows 10 (build 19041) or newer for the app itself.

```
dotnet build TextToSpeech.sln -c Release
dotnet test TextToSpeech.Core.Tests          # no UI, runs on any OS
dotnet test TextToSpeech.Api.Tests           # no UI, runs on any OS
dotnet test PlainRules/PlainRules.sln        # the standalone rule engine
dotnet test TextToSpeech.Windows.Tests       # needs Windows (audio, OCR, WPF)
```

`*.traineddata` files are stored with Git LFS; install Git LFS before cloning if you want the Tesseract OCR option.

## Layout

| Folder | What is in it |
| --- | --- |
| `TextToSpeech/` | The WPF app: windows, tray, hotkeys, OCR and speech engines (Windows only). |
| `TextToSpeech.Core/` | UI-free reading logic: reading list, sentence splitting, the reading controller, filter file library. |
| `TextToSpeech.Api/` | The local HTTP API (router, security, sessions). No WPF. |
| `PlainRules/` | Standalone plain-language text rule engine (own solution, tests and README). |
| `integrations/` | Node command line tool and MCP server that talk to the local API. |
| `docs/design/` | Architecture review and roadmap. |
