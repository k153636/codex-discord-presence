# Discord Presence for Codex

Codex / AI agent が作業しているように見える Discord RPC を表示する常駐ツールです。

表示文は `appsettings.json` のテンプレートで変更できます。現時点の文言は仮実装です。

## Setup

1. Discord Developer Portal で Application を作成します。
2. Application ID を `appsettings.json` の `Discord:ClientId` に設定します。
3. 必要なら Rich Presence Assets に `codex` や `editing` などの画像キーを登録します。
4. 起動します。

```powershell
dotnet run -- --project "E:\path\to\your\project"
```

または `appsettings.json` の `Project:Path` を変更してください。

## Template Values

`Presence` の `Details` / `State` / `LargeImageText` / `SmallImageText` / `Buttons` では以下を使えます。

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

## Future Token And Cost Display

`TokenUsage` は将来の合計 token 使用量と推定コスト表示用の差し込み口です。
現在は手動値を表示するだけで、Codex から自動取得する実装はまだ入れていません。
