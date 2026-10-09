# Automatic updates

The next distribution includes a Velopack Setup installer, a portable ZIP, and a
versioned update package/feed. Both Setup and the extracted portable app support
updates. The old standalone EXE has no replacement mechanism: move to Setup or
the portable ZIP once to enable it. Old release download links remain valid.

The next Setup and portable distributions include the .NET 10 Desktop Runtime.
Their full and delta update packages carry the runtime with the app, so an
existing .NET 9 installation can move to .NET 10 without installing a separate
runtime. Historical standalone EXEs retain their .NET 9 Desktop Runtime
requirement and need the one-time move described above.

## What users experience

- Stable GitHub releases are checked at startup and every six hours. Prereleases
  and downgrades are excluded. Failures leave the app running and retry after 15 minutes.
- Automatic updates are on by default. Verified packages download in the background.
- Only the RPC app restarts; coding CLI processes keep running. Automatic restart
  waits for 60 uninterrupted seconds of Ready, Waiting, or Offline, with the
  Dashboard closed. An active, stalled, or stale observation cancels the countdown.
- The tray **Updates** menu shows the version, progress, and restart countdown.
  It offers **Check for updates**, **Update and restart now**, **Remind me tomorrow**,
  **Automatic updates**, and release notes. A manual restart bypasses the idle wait
  and postponement. Automatic-update preference and 24-hour postponement survive exit.
- Settings and provider selections stay under `%LOCALAPPDATA%\CodexDiscordPresence`.
  Edited package settings are migrated as overrides before replacement, with a
  dated backup under `update-backups`. Unchanged defaults can evolve with releases.
  The tray settings shortcut opens persistent `user-settings.json`.
- Project/launch arguments and Discord session start time survive an update restart.
  The application saves state and closes RPC before the updater applies the package.
- A source build or a loose development EXE can report a new release, but cannot
  automatically overwrite itself. `EnableUpdateCheck: false` disables background
  checks and automatic application; the tray can still request a manual check.

Package checksums detect incomplete or damaged downloads; this is not a substitute
for signing release binaries. Velopack owns package validation and file replacement.
See its [update integration documentation](https://docs.velopack.io/integrating/overview).

## Preparing a release

Use a supported, patched .NET 10 SDK. Self-contained distribution increases
download and installed size because it includes the runtime. Runtime security
fixes require rebuilding and distributing the app with the patched SDK; a
system-wide .NET update does not patch the bundled runtime. See Microsoft's
[self-contained deployment guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/runtime-patch-selection).

1. Set the project `<Version>` to the three-component release number. The prepared
   multiple-CLI release is `0.5.0`; packaging does not publish it.
2. Run `package.cmd` (or `scripts/BuildRelease.ps1`). The pinned `vpk` tool and
   application library must have the same version. Each run creates a fresh directory
   under ignored `Releases/`, so rebuilding never replaces an earlier package.
3. Distribute `K.CodePresence-win-Setup.exe` or `K.CodePresence-win-Portable.zip`.
   Both include the .NET 10 Desktop Runtime. The self-contained package must omit
   Velopack's `--framework` option; see its
   [bootstrapping guidance](https://docs.velopack.io/packaging/bootstrapping).
4. Attach **all** files from that run's output directory to the same stable GitHub release, including
   `releases.win.json`, the full `.nupkg`, and any delta package. An EXE-only release
   cannot be consumed by this updater. Preserve earlier update packages as needed
   for delta generation; `-PreviousReleaseDirectory <folder>` copies an earlier feed
   and packages into the fresh output directory before building deltas. Full packages
   provide the fallback. Upload only the intended release's output directory.

The **Prepare release** workflow builds artifacts on manual dispatch. Pushing a
matching `v0.*` tag also creates a **draft** GitHub release with the complete asset
set. Review the draft and publish it when ready; users only see published stable
releases. Historical `v1.*` tags are intentionally excluded.

For a local update exercise, `scripts/BuildRelease.ps1 -Version 0.2.6` overrides the
assembly and package version together. Such exercise packages are local artifacts
and should not be published as product releases.
