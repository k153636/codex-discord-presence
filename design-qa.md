# Single-page Overview UI/UX QA

source visual truth path: E:\tool\discord-reference\codex-activity-card.png
implementation screenshot paths:
- E:\tool\codex-dashboard-smoke\preview-polished.png

## Intent

Overview is now the complete dashboard. The separate Preview tab and duplicate
status rail were removed so the screen communicates current activity, the
existing usage metadata, and the existing Discord preview at a glance. The
dashboard uses one vertical reading order: the former left-side information is
at the top and the RPC preview is below it. This is a composition change, not a
spacing-only pass: hierarchy, grouping, density, alignment, proportions, and
minimum-size behavior were reconsidered.

## Visual evidence

- The window uses one near-black flat `SurfaceInset` background. The top zone
  has no sidebar panel or extra surface; the activity type, live state, and all
  existing metadata are drawn directly on the background. The model/project
  helper line is intentionally absent so the dashboard never presents stale or
  invented context.
- The lower preview zone stays on the same background and contains only the
  unchanged 360 x 148 Discord activity card centered directly below the
  overview. Its activity type label is read from the published RPC payload
  rather than fixed to `Playing:`.
- The live values — Discord, Billing, Usage, and Reset when available — are
  rendered one per row below the activity hero. Billing is a compact pill;
  Usage retains its live percentage and adds a matching progress rail.
- Missing Billing, Usage, or Reset data is omitted. The Overview does not show
  fixed `Waiting` or `unavailable` values.
- When no live activity state exists, the activity indicator and state text are
  both omitted instead of leaving a status dot without a status.
- The default and minimum outer baseline is 400 x 660. The app opens at this
  compact portrait-oriented geometry with no overlap, off-screen card, or clipped
  essential value.
- The overview uses a single 16px spacing rhythm for its content margins and
  vertical gaps. The lower preview region is 180px high, giving the 148px card
  16px of vertical breathing room. At the narrow minimum width, the shared
  horizontal inset contracts only as much as needed to keep the 360px preview
  card intact.
- Long live activity content is ellipsized inside the top region. Metrics
  remain vertically stacked at every window width so `Connected` and
  `5h 25% used` remain readable without horizontal compression.

## Discord preview fidelity

The Discord card keeps its existing payload-driven details/state, elapsed-time
formatting, 100 x 100 large image, 32 x 32 circular small image, gradient,
corner radius, Discord activity icon, and GIF frame animation. The activity
type label now follows the published RPC payload. A missing token measurement
does not produce the fixed `Tokens pending` text or a dangling separator.

The source card remains the visual truth for card proportions. Native preview
font resolution continues to use the existing `gg sans` lookup and installed
fallback; Discord's private font rasterization cannot be reproduced exactly
outside Discord.

## Interaction and scope

- There is one Overview surface and no redundant tab navigation.
- The dashboard form remains meaningfully named for accessibility.
- Snapshot refresh, tray lifecycle, image invalidation, and disposal remain in
  the existing form/runtime path.
- No new data, provider, metric, action, or user-facing feature was added.
- The existing metrics were only restyled and conditionally rendered from
  their live values; no new source of data was introduced.

## Findings

The previous side-by-side composition made the user scan across separate
regions. The unified flat canvas puts the current information first and lets the
real Discord card sit directly below it in the same reading order. The
portrait-oriented minimum still fits the card and all existing values while
keeping the information groups vertical.

final result: passed
