# Claude Design presence

Claude Code integration detects executed Design operations rather than the
word "design" in a prompt, slash command, skill description, or tool discovery.
Natural-language requests and `/design` therefore share the same detection path.

Both lifecycle hooks and main-session transcript bootstrap recognize the
observed `Artifact` tool shapes:

- `action: "quickstart"` with `intent: "design"`;
- a `file_path` ending in `.dc.html`, the Design canvas format.

Generic artifacts, Docs/Slides quickstarts, Figma tools, and generic design
skills do not count as Claude Design execution. Unknown tool identities fail
closed; integrations that hide or rename the execution evidence need a verified
adapter before they can be recognized.

Pending Design calls display `Claude Design` through the normalized
`{FeatureLabel}` template value. Successful calls keep that label for the rest
of the current main-agent turn, including subsequent file/MCP operations.
Failed calls remove their pending evidence. A new prompt, Stop, SessionEnd, or
session reset clears the label. Subagent transcripts do not set the main label.

The default Details template shows the feature before the model. State remains
one activity line, such as `Editing canvas.dc.html` or `MCP blender`. Custom
Details templates can include `{FeatureLabel}` to opt into the same display.
Only normalized booleans are persisted; artifact URLs, contents, and request
text are not retained by this detector.

Official reference: [Claude Code artifacts](https://code.claude.com/docs/en/artifacts).
