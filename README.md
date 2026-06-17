# Discord Presence for Codex

Discord Rich Presence for making Codex / a model agent appear as the active worker, rather than showing the user's current tab or editor state.

The displayed text is intentionally configurable through `appsettings.json`.

## Setup

1. Create an application in the Discord Developer Portal.
2. The default `Discord:ClientId` is already set to `1516774220636360784`.
3. Optionally add Rich Presence assets matching `LargeImageKey` and `SmallImageKey`.
4. Build with `build.cmd`.
5. Start it with `start.cmd`.
6. Stop it with `stop.cmd`.

You can also set `Project:Path` directly in `appsettings.json`.
The app automatically detects the current Codex model name when possible. Use `Presence:ModelName` or `--model <name>` as the fallback name.
`Project:IgnoredFilePatterns` excludes noisy runtime files such as logs and PID files from the recent editing file detector.

## Current Display Style

The default template uses English text with a clear model-driven agent feel:

- `Details`: `{ModelName} working on {ProjectName}`
- `State`: `{ActivityLine}`
- `LargeImageText`: `{CodexStatus} ・ session {SessionElapsed}`
- `SmallImageText`: `{Tokens} ・ est. {EstimatedCost}`
- Button: `GitHub`

Token and cost values are enabled in the template, but automatic Codex usage extraction is still a future integration point. Until then, `TokenUsage:TotalTokens` and `TokenUsage:EstimatedCostUsd` can be filled manually.

## Easy Start And Stop

- `build.cmd` builds the Windows `exe`
- `start.cmd` launches the app in the background
- `stop.cmd` shuts down the running instance
- Only one instance can run at a time

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
- `{EditingFileLabel}`
- `{EditingFilePath}`
- `{ChangedFileCount}`
- `{ChangedFilesText}`
- `{ActivityLine}`
- `{SessionElapsed}`
- `{SessionStartedAt}`
- `{Tokens}`
- `{EstimatedCost}`
