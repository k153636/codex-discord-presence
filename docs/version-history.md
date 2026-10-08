# Release numbering

K's Code Presence uses `major.minor.patch` versions. The existing releases are
renumbered to describe the project's development stage; the planned release
with multiple CLI integrations is **0.5.0**. This does not publish 0.5.0 now.

| Display version | Historical GitHub tag |
| --- | --- |
| 0.1.0 | v1.0.0 |
| 0.1.1 | v1.1.0 |
| 0.2.0 | v1.2.0 |
| 0.2.5 | v1.2.5 |

The historical tags and downloaded binaries are retained so existing download
and source links continue to work. Archived binaries therefore still contain
their original assembly version. Their release titles and notes identify the
new display version. Future releases use the new version in the title, tag,
assembly metadata, and update package.

The archived standalone executables remain framework-dependent .NET 9 builds
and require the .NET 9 Desktop Runtime. The next Setup installer and portable ZIP
include the .NET 10 Desktop Runtime, as do their automatic-update packages.
Changing the source target does not change archived binaries or their runtime
requirements.

The update checker maps only these four archived GitHub release IDs and their
original tags. It does not reinterpret arbitrary or future 1.x versions.

Old executables only check for updates; they cannot acquire automatic-update
support themselves. Install the updater-enabled distribution once when it is
released. Existing settings and provider enablement remain under
`%LOCALAPPDATA%\CodexDiscordPresence`.
