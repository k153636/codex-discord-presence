# K's Codex RPC

Codex CLI / Codex Desktopの作業状況を、WindowsのDiscord RPCに表示するローカル常駐アプリです。

Codexの現在の作業内容をDiscord Rich Presenceに表示します。
「Thinking」や「Editing」だけではなく、Codexセッションから取得できる推論サマリー、使用中のMCPサーバー、編集中のファイル、コマンド実行、調査状態などを確認できます。

<p>
  <a href="https://github.com/k153636/codex-discord-presence/releases/latest"><img src="https://img.shields.io/github/v/release/k153636/codex-discord-presence?display_name=tag&style=for-the-badge&label=Download" alt="Latest release"></a>
  <a href="https://k153636.github.io/codex-discord-presence/"><img src="https://img.shields.io/badge/Website-K%27s%20Codex%20RPC-5865F2?style=for-the-badge&logo=googlechrome&logoColor=white" alt="Website"></a>
  <a href="https://github.com/k153636/codex-discord-presence"><img src="https://img.shields.io/badge/Source-GitHub-181717?style=for-the-badge&logo=github&logoColor=white" alt="Source repository"></a>
  <a href="https://github.com/k153636/codex-discord-presence/blob/main/LICENSE"><img src="https://img.shields.io/github/license/k153636/codex-discord-presence?style=for-the-badge&label=License" alt="MIT License"></a>
</p>

## Discordに表示できるもの

- Codexセッションから取得できる推論サマリー
- 使用中のMCPサーバー
- 編集中のファイルやファイル数
- 読み取り、編集、計画、調査、コマンド実行などの状態
- 使用モデルと推論レベル
- 取得可能な場合のセッション時間、トークン数、推定コスト
- プロジェクト情報とGitの変更数
- サブエージェントのParty情報
- Discord RPC内のWebサイトなどのボタン

Codexのセッションイベントやプロセス情報など、取得できる活動情報をもとにDiscord RPCを更新します。

## Codexの解析と状態判定はローカルで行われます

Codexのセッション情報、プロジェクト情報、Git情報などの解析と状態判定は、Windows上でローカルに行われます。

ただし、アプリは完全オフラインではありません。
Discord DesktopへのRPC接続や、設定が有効な場合のGitHub Release更新確認など、必要に応じてネット通信を行います。

プロジェクトが運営するアカウント、セッション用サーバー、クラウド上のチームダッシュボードはありません。

Discordに表示される内容には、プロジェクト名、ファイル名、推論サマリーなどが含まれる場合があります。詳しくは[データの流れとプライバシー](https://k153636.github.io/codex-discord-presence/privacy.html)を確認してください。

## 必要なもの

- Windows x64
- .NET 9 Desktop Runtime
- Discord Desktop
- Codex CLIまたはCodex Desktop

## インストール

1. [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)をインストールします。
2. Discord Desktopを起動します。
3. [最新のGitHub Release](https://github.com/k153636/codex-discord-presence/releases/latest)から`discord-presence-for-codex.exe`をダウンロードします。
4. 実行ファイルを起動します。

アプリはWindowsのタスクトレイに常駐します。
トレイメニューから、Discord RPCの有効化、Dashboardの表示、設定編集、終了を行えます。

## Codex CLIとCodex Desktop

Desktop用とCLI用の検出設定に対応しています。

実行中のCodex、セッションログ、CLIのコマンドラインなどを確認し、使用中の環境に合わせて表示を更新します。

## 詳細

- [Webサイト](https://k153636.github.io/codex-discord-presence/)
- [FAQ](https://k153636.github.io/codex-discord-presence/faq.html)
- [互換性](https://k153636.github.io/codex-discord-presence/compatibility.html)
- [データの流れ](https://k153636.github.io/codex-discord-presence/privacy.html)
- [最新のGitHub Release](https://github.com/k153636/codex-discord-presence/releases/latest)

## 注意事項

このプロジェクトはOpenAIまたはDiscordの公式製品ではありません。

現在の公開版はWindows x64向けです。
トークン数、コスト、レート制限情報などは取得できない場合があります。

MIT License
