# Claude Code spinner labels

Claude Code's terminal can display spinner words such as `Herding`,
`Cerebrating`, `Determining`, `Roosting`, and `Cooking`. These are display
labels, not distinct tool-operation states. They were not present as activity
fields in the local session JSONL files inspected during implementation.

The Claude provider reads the current terminal label using a short-lived
`--claude-spinner` helper. Console attachment stays outside the tray process.
Windows Terminal uses visible accessibility text because its legacy ConPTY
buffer can differ from the screen. Other terminal hosts retain the existing
lifecycle/tool labels.
The helper returns only the extracted word. Screen text is neither persisted
nor sent to Discord.

Reading requires exactly one native `claude` process. For Windows Terminal,
exactly one window must match that process's console title. Ambiguous matches,
inaccessible terminals, multiple spinner rows, and completed-turn messages
fall back to the existing lifecycle/tool labels. Only visible tabs are read.

The label is checked at most once every three seconds while the selected
Claude session is thinking. A two-second helper timeout prevents an unavailable
accessibility provider from blocking the runtime. MCP, file operations, input
waiting, and completed turns retain their existing labels. Reading the spinner
does not update activity timestamps or influence provider selection.
