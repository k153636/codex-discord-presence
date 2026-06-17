# Discord Presence for Codex

Discord Rich Presence for showing Codex as the active worker instead of the user's current tab or editor state.

Presence text is template-driven through `appsettings.json`, so you can adjust the copy without changing code.

## Quick Start

1. Run `build.cmd`.
2. Run `start.cmd`.
3. Use `stop.cmd` to shut it down.

The app is configured for a self-contained `win-x64` single-file publish.

## What It Shows

- Current Codex model when available
- Project name and project size
- Recent edited file name
- Git changed-file count
- Session elapsed time
- Token count and estimated cost placeholders
- Discord buttons

## Activity Labels

The presence engine prefers high-confidence labels:

- `Running command`
- `Coordinating changes across {n} files`
- `Applying edits`
- `Analyzing project`
- `Ready`
- `Offline`

`Planning` and `Refactoring` are still supported, but they are treated as low-confidence labels and only appear when the local evidence is explicit enough.

## Default Presence

- `Details`: `{ModelName} working on {ProjectName}`
- `State`: `{ActivityLine}`
- `LargeImageText`: `{ProjectName} ・ {ProjectSizeText}`
- `SmallImageText`: `session {SessionElapsed}`
- Button: `GitHub`

## Model Detection

When `Presence.AutoDetectModelName` is enabled, the app resolves `{ModelName}` from:

- `CODEX_MODEL`, `OPENAI_MODEL`, or `MODEL_NAME`
- Recent Codex session JSONL files under `CODEX_HOME` or `%USERPROFILE%\.codex`
- `%USERPROFILE%\.codex\config.toml`
- `Presence.ModelName` as the fallback

The app logs these values for debugging:

- Selected UI model
- Last used session model
- Final displayed model

## Logging

The activity logger includes:

- the chosen activity label
- `confidence=high` or `confidence=low`
- the reason the label was selected

That makes it easier to verify why Discord is showing a specific state.

## Configuration

Common settings live in `appsettings.json`:

- `Discord.ClientId`
- `Discord.LargeImageKey`
- `Project.Path`
- `Project.DisplayName`
- `Presence.ModelName`
- `Presence.Details`
- `Presence.State`
- `Presence.LargeImageText`
- `Presence.SmallImageText`
- `Presence.Buttons`
- `Presence.AnalyzingProjectText`
- `Presence.UpdatingFilesText`
- `Presence.RunningCommandText`
- `Presence.PlanningText`
- `Presence.ApplyingEditsText`
- `Presence.RefactoringText`

## Template Values

These placeholders can be used in `Presence.Details`, `Presence.State`, `Presence.LargeImageText`, `Presence.SmallImageText`, and button labels/URLs:

- `{ModelName}`
- `{CodexStatus}`
- `{CodexProcessName}`
- `{ProjectName}`
- `{ProjectPath}`
- `{ProjectFileCount}`
- `{ProjectLineCount}`
- `{ProjectSizeText}`
- `{EditingFileName}`
- `{EditingFileLabel}`
- `{EditingFilePath}`
- `{ChangedFileCount}`
- `{ChangedFilesText}`
- `{ActivityLabel}`
- `{ActivityKind}`
- `{ActivityConfidence}`
- `{ActivityProvenance}`
- `{ActivityReason}`
- `{ActivityLine}`
- `{SessionElapsed}`
- `{SessionStartedAt}`
- `{Tokens}`
- `{EstimatedCost}`

## Discord App

The default Discord application id is:

`1516846793873424474`

The default large image key is:

`codex_logo`

## Notes

- `start.cmd` launches the published exe in the background
- `stop.cmd` stops the running instance
- `git diff` and recent file writes are used together to infer active work
