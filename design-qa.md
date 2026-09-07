# Discord activity preview design QA

source visual truth path: E:\tool\discord-reference\codex-activity-card.png
implementation screenshot path: E:\tool\codex-dashboard-smoke\preview-font-check.png
comparison image path: E:\tool\codex-dashboard-smoke\activity-card-side-by-side-font-check.png

## Normalized comparison

- Source pixels: 390 x 180.
- Implementation window pixels: 976 x 699.
- Comparison crop: 390 x 180 from the implementation window.
- Display scale: 100%; no resampling was used for the focused card comparison.
- State: Preview tab, dark theme, Codex activity, Thinking, gpt 5.6 luna max 1.5x • 75.3M Token.
- Dynamic exception: the elapsed time is expected to advance, and the large GIF frame is expected to differ from the captured source frame.

## Full-view comparison evidence

The rendered WinForms dashboard shows the same Preview tab, dark surface, left status rail, fixed Discord activity-card proportions, 100 x 100 large image, 32 x 32 circular small image, title, details, state, game icon, and elapsed line. No P0, P1, or P2 difference was observed in the visible implementation.

## Focused region comparison evidence

activity-card-side-by-side-font-check.png places the 390 x 180 source crop beside the same-size implementation crop. Card bounds, image slots, text baselines, gradient direction, corner radius, and content order are aligned. The implementation uses the actual published payload values. The elapsed value differs only because it is computed from the current published start timestamp.

## Fidelity surfaces

- Fonts and typography: the preview now resolves Discord's `gg sans` family when it is installed and otherwise uses the installed `Segoe UI Variable Text` fallback. The current host does not register `gg sans`; the fallback keeps the reference's compact widths, weights, wrapping, and ellipsis within native WinForms rasterization differences.
- Spacing and layout rhythm: card dimensions, 12 px image inset, 100/32 px image sizes, content column, and vertical line spacing match the reference.
- Colors and visual tokens: dark vertical card gradient, Discord text colors, and green elapsed line are aligned to the reference.
- Image quality and asset fidelity: the source RPC GIFs remain GIFs, are copied beside the published single-file executable, and are rendered through ImageAnimator with the original stream retained.
- Copy and content: state and details are read from the last payload successfully sent to Discord, so they are not reinterpreted by a state-name switch.

## GIF evidence

Two screenshots of the same implementation state were captured 1.5 seconds apart. The large-image region changed in 3,984 of 10,000 pixels, confirming animated-frame redraw.

## Findings

No actionable P0, P1, or P2 findings.

## Comparison history

- Initial implementation already matched the reference card geometry and content order.
- The previous iteration changed payload selection to prefer the last successfully published Discord payload, preserved animated GIF streams, enabled ImageAnimator frame callbacks, and copied all RPC GIF assets to the published output.
- This iteration changed elapsed rendering from an hour/minute clock to Discord-style minutes/seconds, switching to hours/minutes/seconds after one hour, and added automatic Discord-compatible font-family selection for the preview card.
- Post-fix evidence: activity-card-side-by-side-font-check.png and the GIF frame pixel comparison above.

## Implementation Checklist

- [x] Mirror the state and details from the successfully published Discord payload.
- [x] Keep future state strings data-driven without UI state enumeration.
- [x] Resolve future GIF keys from local Assets\RpcArt files or HTTP(S) references.
- [x] Animate GIF frames and invalidate the WinForms preview safely.
- [x] Format elapsed time as minutes/seconds, then hours/minutes/seconds after one hour.
- [x] Resolve `gg sans` when installed with an automatic Windows fallback.
- [x] Validate focused tests, full tests, Release publish, running process, RPC initialization, and rendered presence log.

## Follow-up Polish

- P3: Discord's private `gg sans` font is bundled inside Discord rather than registered as a Windows font on this host, so the native preview uses `Segoe UI Variable Text` unless `gg sans` is available. Live desktop-card antialiasing and timing cannot be reproduced pixel-for-pixel outside Discord itself.

final result: passed
