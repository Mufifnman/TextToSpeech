# Local API: compatibility rules and error codes

The local API (`http://127.0.0.1:<port>/v1/...`, found through `%LocalAppData%\TextToSpeech\api.json`) is used by the Claude Code
plugin, the `tts` command line tool, the MCP server and anyone's scripts. These rules say what may change without breaking them.
`GET /v1/status` returns `apiVersion` (currently **1**, `ApiRouter.ApiVersion`).

## Compatibility rules

Without bumping `apiVersion` (these are always allowed):
- Add a new route, a new optional request field, or a new field in a response.
- Add a new error code, or a new value to a documented set of choices *only where the docs say the set may grow*.
- Make the app accept more than before (a larger limit, a previously rejected value).
- Change the text of an error `message`.

Clients must therefore: ignore response fields they do not know; never parse `message`; treat an unknown error `code`
as its HTTP status class; and send only fields the version they target defines.

Requires a new `apiVersion` (and, where possible, keeping the old behaviour for one release):
- Remove or rename a route, field or error code, or change the type or meaning of a field.
- Make a previously optional request field required, or reject a request that was accepted.
- Change the HTTP status of an existing error code.
- Change the shape of `api.json` in a way that breaks reading `port` and `token`.

Other rules:
- `api.json` keeps `port` and `token` (and `pid`) under those names; the Node clients also accept `Port`/`Token`.
- The token changes every time the app starts. Clients read `api.json` on every call and never cache the token.
- The Node client in `integrations/` is the source; `plugins/tts/` carries copies that a test keeps identical
  (`bundled scripts match integrations/`). Edit `integrations/`, then copy the three files.
- A client checks `apiVersion` only if it needs a feature newer than 1.

## Error shape

Every failure is `{ "error": { "code": "...", "message": "..." } }` with the HTTP status below.

## Error codes (server)

| Code | Status | Meaning |
|---|---|---|
| `forbidden_host` | 403 | Host header is not 127.0.0.1, localhost or [::1] with the API port. |
| `forbidden_origin` | 403 | A browser request (has an Origin header); not allowed. |
| `unauthorized` | 401 | Token missing or wrong. |
| `bad_json` | 400 | The body is not valid JSON. |
| `bad_request` | 400 | A field is missing or out of range (default code; see the message). |
| `too_large` | 413 | Request body is too large. |
| `not_found` | 404 | Unknown route (default code). |
| `method_not_allowed` | 405 | Wrong HTTP method for the route. |
| `internal` | 500 | Unexpected error in the app; see the log file. |
| `too_many_streams` | 429 | Too many `/v1/events` streams are open. |
| `unsupported_override` | 400 | Per-request voice, rate or volume is not supported; use the PUT routes. |
| `unsupported` | 404 | Pause points are not supported by this host. |
| `no_speakable_text` | 400 | The text has nothing to say after cleaning. |
| `pauses_with_filters` | 400 | `pauses` cannot be combined with `applyFilters`. |
| `too_many_pauses` | 400 | More than 50 pauses. |
| `bad_pause` | 400 | A pause has a bad id, label or offset. |
| `bad_questions` | 400 | More than 50 questions, or one is too long. |
| `not_paused_at_pause` | 409 | Reading is not waiting at a pause point. |
| `no_such_pause` | 404 | No unanswered pause with that id. |
| `no_such_voice` | 404 | Unknown voice id. |
| `bad_position` | 400 | `jump` to a sentence that does not exist. |
| `no_such_session` | 404 | Unknown session id. |
| `no_current_session` | 404 | The sentence being read is not an API session. |
| `session_gone` | 404 | The session is no longer in the reading list. |
| `already_taken` | 409 | The remainder of the session is already taken. |
| `nothing_to_take` | 409 | Everything in the session has been read. |
| `not_taken` | 409 | The session is not handed off. |
| `bad_handoff` | 409 | `handoffId` does not match the open handoff. |
| `handoff_closed` | 409 | The handoff expired or was closed; send the text as a new `speak`. |
| `revision_mismatch` | 409 | `ifRevision` is not the session's current revision. |
| `bad_start_at` | 400 | `startAt` is outside the new text. |
| `bad_offset` | 400 | `fromOffset` is past the end of the text. |
| `too_late` | 409 | That part has already been read (or is being read). |

## Error codes (added by the Node client, status 0)

| Code | Meaning |
|---|---|
| `not_running` | No usable `api.json`: the app is closed, the API is off, the file is incomplete, or it is left over from an app that exited (its `pid` is not running). |
| `unreachable` | `api.json` exists but nothing answered on that port. |
| `http_error` / `error` | The server answered with a failure that had no error body. |

A test (`error codes in the C# code are documented`) fails when a code is added in `TextToSpeech.Api` without a row here.
