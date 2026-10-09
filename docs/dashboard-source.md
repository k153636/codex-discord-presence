# Dashboard source and integration

The application displays the original Claude Design standalone export directly
in Microsoft Edge WebView2. Its HTML, CSS, component template, and embedded
provider artwork are not a recreation in Windows Forms.

## Original export

- Application source: `Assets/Dashboard/Dashboard.html`
- Received export filename: `Dashboard.html`
- SHA-256: `587933D7CFF0CCE6A4D148BFE4038CCC0EBBB9339AC2474E5F4EE88DBB201D16`
- Original canvas: 400 by 412 CSS pixels, plus its own 1 pixel border on each
  side; the host client area is 402 by 414 pixels at 100% scale.

`OriginalDashboard_RemainsByteIdenticalToReceivedExport` guards the original
bytes. Replace the export and update this provenance only for an approved new
design. Do not modify the received HTML to imitate another layout.

## Runtime integration

`Application/CodexDashboardForm.cs` hosts the export. Windows Forms owns window
lifetime; the original HTML owns the visible dashboard. It is loaded from a
local virtual host, without requiring a Claude account or online design service
when opening the dashboard.

`Assets/Dashboard/runtime-adapter.js` supplies actual provider enable settings,
owner, project, quota, connection status, and acknowledged Discord fields. It
reuses the exported component and its styles. Export example data never becomes
live application data. Runtime additions implement the user's later subagent
small image policy, real buttons and window controls, missing-data visibility,
and accessible labels. They do not introduce a second visual design.

DOM reconciliation preserves image elements and focus during elapsed-time
updates. The existing five second selection gates receive confirmation after
the browser paints. Provider checkboxes persist through the same state store as
the tray menu. Solo sessions omit the small image and tooltip.

Local RPC GIFs remain GIFs and are used before remote image fallbacks. When
Windows disables client animations, the dashboard uses cached first-frame PNG
data without modifying those source assets.

## Runtime and validation

The dashboard requires Microsoft Edge WebView2 Runtime. RPC and tray operation
continue if the dashboard cannot initialize. Release packages must include
`Assets/Dashboard` beside the executable. Source builds require .NET 10 SDK;
the self-contained publish includes the .NET Desktop Runtime.

`scripts/PreviewCapture` renders sanitized snapshots through the production
WebView2 form, rather than reading personal session/account data. Its optional
scale argument exercises browser layout at the requested scale; it does not
change the physical monitor DPI or Windows settings. UI integration checks
should cover all three providers, empty/disabled/unacknowledged states,
connection states, persisted checkboxes, keyboard focus, tooltip visibility,
and scaling without viewport overflow.
