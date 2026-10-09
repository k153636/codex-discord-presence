# Claude Code artwork

Source: https://github.com/rar-file/claude-rpc
Reference revision: `eac1fbb29cfd940dfef300b1340744b8995ae5c9`.
The four animated GIFs are the CDN assets referenced by that revision's
`src/default-config.js`. They are preserved as animated GIFs, with the upstream
MIT license included. Discord uses the upstream HTTPS CDN URLs; the local
copies preserve the source artwork and attribution.

- `clawd-working-typing.gif`: thinking
- `clawd-working-building.gif`: tool activity
- `clawd-sleeping.gif`: idle, waiting for input, and the persistent Clawd small icon
- `clawd-notification.gif`: brief large-image response to an observed user prompt;
  restored to the current activity image six seconds after Discord acknowledges it.
  The GIF has 300 frames at 20 ms per frame. RPC has no animation playback controls,
  so the six-second display window cannot guarantee frame-accurate single playback
  in every Discord client. Source animation and loop metadata are preserved.

`clawd-icon.png` is a static crop of the notification GIF's first frame,
provided by the Claude Design cloud dashboard handoff. It makes Clawd readable
in the dashboard's 16px provider chip and 20px owner row. The original animated
GIF is preserved, and the same upstream MIT license applies to this derivative.
