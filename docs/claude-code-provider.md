# Claude Code integration

Enable **Claude Code** in the dashboard's provider integrations. It is disabled
by default. Enablement is saved separately from the Codex Desktop/CLI profile.
The application keeps one runtime, one RPC transport, and one elapsed timer.
Provider selection uses the existing activation gate, project evidence, and
freshness; enabling a provider does not force it to take over.

## Evidence and privacy

The integration installs observational command hooks into
`~/.claude/settings.json`. Existing hooks, status lines, permissions, and model
settings are preserved. Hooks record sanitized session and lifecycle metadata
under `%LOCALAPPDATA%/CodexDiscordPresence/claude-code/sessions`. Prompts,
tool responses, shell commands, source text, and thinking bodies are discarded.
Only file basenames appear in presence. Subagent lifecycle IDs determine party
membership. Child tool hooks are isolated by `agent_id`, and the small image
tooltip reports a work category only while a matching child tool event is
fresh. Child events cannot replace the main-agent activity line. When a child
is confirmed active but has no fresh tool evidence, Discord shows only the
active subagent count.

For a native Claude session already running before hook installation, a bounded
tail of recent main-session transcripts can bootstrap activity. This requires a
running `claude` process and fresh structured transcript events. File timestamps
only select candidates; they never establish the displayed activity. Once a
session supplies hook evidence, transcript bootstrap cannot resurrect it after
`SessionEnd` or overwrite hook observations. Node-based CLI sessions need hooks;
native-process bootstrap is deliberately limited to the native executable.

The main transcript provides observed model and effort metadata. Unavailable
metadata stays unknown. Tokens, cost, plan, quota and billing are currently
omitted: a transcript's per-message tokens are not a subscription quota or a
session-wide cost. The integration does not access Claude credentials or make
Anthropic account requests.

## Installation safety

Each settings mutation has a local backup. An ownership manifest identifies the
exact installed hook definitions. Installation is idempotent. Disablement and
normal application exit remove only those exact definitions, preserving other
hooks and subsequent user edits. Altered or unowned app definitions are treated
as conflicts; the settings file remains untouched. Settings are written using an
atomic replacement, with a check for concurrent edits before replacement.

Claude may require approval of changed hooks or a new session before it runs new
hook commands. The integration does not restart Claude. A missing `SessionEnd`
(for example, closing a terminal abruptly) is bounded by the configured
`ThinkingStaleTimeoutMinutes` freshness window.

## Discord identity and GIFs

Claude uses the public **Claude Code** application ID `1506443909406920948`
published by [rar-file/claude-rpc](https://github.com/rar-file/claude-rpc), rather
than a Codex or Antigravity ID. This explicitly shares that upstream public
Discord identity; override `DiscordClaudeCode.ClientId` in local user settings
to use your own application. The upstream daemon is not installed or executed.
Animated images use the upstream CDN URLs and are also preserved locally with
attribution and the upstream MIT license in `Assets/RpcArt/ClaudeCode`.

References:

- [Claude Code hooks](https://code.claude.com/docs/en/hooks)
- [Upstream defaults, pinned revision](https://github.com/rar-file/claude-rpc/blob/eac1fbb29cfd940dfef300b1340744b8995ae5c9/src/default-config.js)
- [Upstream license](https://github.com/rar-file/claude-rpc/blob/eac1fbb29cfd940dfef300b1340744b8995ae5c9/LICENSE)
