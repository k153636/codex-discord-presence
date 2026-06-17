# Discord Presence for Codex

Discord Rich Presence for making Codex / a model agent appear as the active worker, rather than showing the user's current tab or editor state.

The displayed text is intentionally configurable through `appsettings.json`.

## Setup

1. Create an application in the Discord Developer Portal.
2. Set the application ID in `Discord:ClientId`.
3. Optionally add Rich Presence assets matching `LargeImageKey` and `SmallImageKey`.
4. Run the app.

```powershell
dotnet run -- --project "E:\path\to\your\project"
```

You can also set `Project:Path` directly in `appsettings.json`.
The app automatically detects the current Codex model name when possible. Use `Presence:ModelName` or `--model <name>` as the fallback name.

## Current Display Style

The default template uses English text with a clear model-driven agent feel:

- `Details`: `{ModelName} is building {ProjectName}`
- `State`: `Editing {EditingFileName} | {ChangedFileCount} files changed`
- `LargeImageText`: `{CodexStatus} | session {SessionElapsed}`
- `SmallImageText`: `{Tokens} | est. {EstimatedCost}`
- Button: `GitHub`

Token and cost values are enabled in the template, but automatic Codex usage extraction is still a future integration point. Until then, `TokenUsage:TotalTokens` and `TokenUsage:EstimatedCostUsd` can be filled manually.

## Model Detection

When `Presence.AutoDetectModelName` is enabled, the app resolves `{ModelName}` from:

- `CODEX_MODEL`, `OPENAI_MODEL`, or `MODEL_NAME`
- Recent Codex session JSONL files under `CODEX_HOME` or `%USERPROFILE%\.codex`
- `%USERPROFILE%\.codex\config.toml`
- `Presence.ModelName` as the fallback

## Template Values

`Presence.Details`, `Presence.State`, `Presence.LargeImageText`, `Presence.SmallImageText`, and button labels/URLs can use these placeholders:

- `{ModelName}`
- `{CodexStatus}`
- `{CodexProcessName}`
- `{ProjectName}`
- `{ProjectPath}`
- `{EditingFileName}`
- `{EditingFilePath}`
- `{ChangedFileCount}`
- `{SessionElapsed}`
- `{SessionStartedAt}`
- `{Tokens}`
- `{EstimatedCost}`

## GitHub Button

The default button points to:

https://github.com/k153636/codex-discord-presence
