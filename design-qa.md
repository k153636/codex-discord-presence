# Single-page Overview UI/UX QA

source visual truth path: E:\tool\discord-reference\codex-activity-card.png
implementation screenshot paths:
- E:\tool\codex-dashboard-smoke\dashboard-single-overview-default-final2.png
- E:\tool\codex-dashboard-smoke\dashboard-single-overview-minimum-final2.png

## Intent

Overview is now the complete dashboard. The separate Preview tab and duplicate
status rail were removed so the screen communicates current activity, the
existing Discord preview, and the existing usage metadata at a glance. This is
a composition change, not a spacing-only pass: hierarchy, grouping, density,
alignment, proportions, and minimum-size behavior were reconsidered.

## Visual evidence

- The left zone contains one compact `Current Activity` card with the state as
  the largest visual element and the model/project line below it.
- The right zone contains the unchanged 360 x 148 Discord activity card in a
  restrained stage. The stage has no duplicate explanatory heading; the real
  Discord `Playing:` label remains inside the card.
- The four existing values — Discord, Billing, Usage, and Reset — are balanced
  in a 2 x 2 grid below the activity card. Each uses the same label/value
  hierarchy and accent rail.
- The default and minimum outer baseline is 880 x 420; the resulting client
  capture is 864 x 381. The app opens at this minimum geometry with no overlap,
  off-screen card, or clipped essential value.
- Long model/project content wraps inside its activity card. Narrow metric
  cards switch to a vertical label/value layout so `Connected` and
  `5h 25% used` remain readable at the minimum size.

## Discord preview fidelity

The Discord card keeps its existing payload-driven details/state, elapsed-time
formatting, 100 x 100 large image, 32 x 32 circular small image, gradient,
corner radius, Discord activity icon, English `Playing:` label, and GIF frame
animation. No RPC payload, asset key, image-slot, or elapsed-time behavior was
changed.

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

## Findings

The previous two-tab composition made the user change pages to see the preview
and repeated connection/usage information in a side rail. The single-page
composition removes that navigation cost, gives the preview a direct visual
relationship to the current state, and uses the minimum window area that still
fits the card and all existing values.

final result: passed
