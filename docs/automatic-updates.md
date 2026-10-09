# Updates

0.5.0 is distributed as a single `discord-presence-for-codex.exe`, like the previous release. It requires the .NET 10 Desktop Runtime separately. The Dashboard, artwork, default settings, and license notices are embedded in the executable and extracted under local application data when needed.

## Updating

The app checks GitHub Releases for newer versions when update checks are enabled. A standalone EXE cannot automatically replace itself:

1. Download the new EXE from the latest GitHub Release.
2. Quit the running app from its tray menu.
3. Replace the previous EXE and launch the new one.

Settings and provider selections remain under `%LOCALAPPDATA%\CodexDiscordPresence`. Coding CLI processes do not need to stop. Archived releases still require .NET 9; 0.5.0 requires .NET 10.

The initial Setup/portable packaging was replaced with the smaller standalone distribution. The public release does not provide Velopack update feeds or packages. Previously downloaded managed packages cannot acquire automatic updates from this EXE-only release; use the standalone EXE instead.

## Building a release

Use the .NET 10 SDK and run `scripts/BuildStandaloneRelease.ps1`. It creates a new ignored release directory containing exactly one framework-dependent executable. `build.cmd` produces the same format in `publish/`.

`scripts/BuildRelease.ps1` remains a development helper for testing managed-package updates, but its output is not the public distribution. The release workflow publishes only the standalone EXE.
