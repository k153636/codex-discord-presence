# Dashboard UI/UX design QA

source visual truth path: E:\tool\discord-reference\codex-activity-card.png
implementation screenshot paths:
- E:\tool\codex-dashboard-smoke\dashboard-polish-overview-default-final.png
- E:\tool\codex-dashboard-smoke\dashboard-polish-preview-final.png
- E:\tool\codex-dashboard-smoke\dashboard-polish-overview-minimum-final2.png
- E:\tool\codex-dashboard-smoke\dashboard-polish-preview-minimum-final.png
comparison image path: E:\tool\codex-dashboard-smoke\activity-card-aligned-side-by-side-dashboard-polish-final.png

## Design intent

This pass is a structural UI/UX polish, not a spacing-only pass. The semantic
data, tab behavior, Discord payload, animated assets, elapsed-time rules, and
English copy baseline remain unchanged. The layout was reconsidered where the
previous composition had weak hierarchy, excessive empty space, or competing
groups.

## Full-view comparison evidence

- Overview uses a wide two-part composition: the current activity is the
  primary hero, while Discord, Billing, Usage, and Reset form a compact 2 x 2
  comparison grid. At the desktop baseline, the hero and metric group share a
  common top edge and the metrics use a label-then-value hierarchy.
- Overview falls back to a compact stacked layout at the 760 x 520 form
  minimum. The hero, metric grid, long model/project text, borders, and values
  remain visible without clipping or overlap.
- Preview keeps the exact 360 x 148 Discord card geometry and places it in a
  centered stage. The stage creates a clear relationship between the app's
  preview context and the Discord card without changing the card itself.
- The Preview status rail groups the existing activity, connection, Billing,
  Usage, and Reset values with consistent indicator/accent treatment. No new
  data or provider was introduced.
- The rendered screenshots use the English fixed labels `Current Activity` and
  `Playing:`. The source screenshot's Japanese labels are an intentional locale
  difference.

## Focused Discord card comparison

`activity-card-aligned-side-by-side-dashboard-polish-final.png` places the
aligned 360 x 148 source card beside the same-size implementation card. Card
bounds, image slots, content order, gradient direction, corner radius, text
baselines, the 14 px Discord activity glyph, and elapsed-line icon position
remain aligned. The large GIF frame and elapsed value are expected to differ
between captures.

## Interaction and accessibility evidence

- Overview and Preview remain the only tabs; no new product surface was added.
- The selected tab has a stronger surface/underline distinction, while the
  hover state is quieter and does not compete with the selected state.
- Tab buttons retain keyboard focusability and expose an accessible description.
- Left/Right keyboard navigation was exercised on the running smoke window:
  pressing Right on Overview selected Preview.
- The form keeps the existing desktop minimum size of 760 x 520 and exposes an
  accessible dashboard name.

## Fidelity surfaces

- Typography: the Discord card continues to resolve `gg sans` when available,
  with the existing `Segoe UI Variable Text` fallback. Dashboard hierarchy uses
  the existing type scale with larger state values and quieter labels.
- Density and alignment: content is max-width constrained, aligned to shared
  edges, and uses a deliberate wide/compact breakpoint rather than stretching
  every surface across the window.
- Component proportions: the activity hero, metric cards, status rail, preview
  stage, and Discord card have separate proportions appropriate to their roles.
- Colors and tokens: the existing dark surfaces, borders, accents, Discord
  colors, and green elapsed line are retained.
- Asset fidelity: source RPC GIFs remain GIFs and continue to animate through
  the existing ImageAnimator path. The elapsed-line Discord glyph remains the
  actual 14 x 14 transparent raster used by the preview.
- Content and behavior: dashboard values remain derived from the existing
  snapshot and the Discord card remains driven by the last successfully sent
  payload; no new state enumeration or RPC payload behavior was added.

## GIF evidence

Two screenshots of the same implementation state were captured 1.5 seconds
apart. The large-image region changed in 3,984 of 10,000 pixels, confirming
animated-frame redraw.

## Findings

Previous structural weakness: Overview presented a broad hero followed by a
separate lower strip, and Preview placed the card in an unstructured open
field. The revised composition establishes primary/secondary hierarchy,
grouped comparison metrics, stronger alignment, responsive density, and a
clear Preview stage. No unintended P0, P1, or P2 regression was observed.

## Comparison history

- Earlier iterations aligned the Discord card's payload content, GIF behavior,
  elapsed format, font fallback, activity icon, and English fixed labels.
- This iteration changes only the dashboard presentation layer: hierarchy,
  grouping, density, alignment, component proportions, tab interaction cues,
  and accessibility metadata.

## Implementation Checklist

- [x] Preserve the published Discord payload, card geometry, elapsed behavior,
  GIF animation, icon fidelity, and English baseline.
- [x] Re-evaluate Overview hierarchy and metric grouping at desktop width.
- [x] Provide a compact minimum-size layout without clipping or overlap.
- [x] Re-evaluate Preview grouping and center the unchanged Discord card in a
  dedicated stage.
- [x] Preserve semantic activity and usage values without adding providers or
  visible features.
- [x] Validate focused tests, full tests, smoke screenshots, Release publish,
  running process, RPC initialization, and rendered presence log.

## Follow-up note

Discord's private `gg sans` font is bundled inside Discord rather than
registered as a Windows font on this host. The native preview therefore uses
the existing automatic fallback when `gg sans` is unavailable; live Discord
antialiasing and timing cannot be reproduced pixel-for-pixel outside Discord.

final result: passed
