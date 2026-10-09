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

The main transcript provides observed model and effort metadata, and session
token totals. Token totals sum input, cache creation, cache read and output
tokens from main-session assistant messages, counting each message ID once
across streaming blocks. Reads are incremental; child sessions and other
sessions are excluded. The current context window is never presented as a
session total.

Hook and statusLine input is forwarded as native bytes, preserving Japanese
project paths. Transcript token totals do not depend on statusLine installation
success; if that installation fails, cost and subscription usage are omitted
while available tokens remain visible.

An observational wrapper around the official `statusLine` captures sanitized
session cost and subscription usage. The original command still receives the
same input and its output, ANSI formatting, errors and exit code pass through.
Existing padding and refresh settings are preserved. Usage observations do not
create activity or change provider selection. Only a matching active session
and project can use its usage record.

Codex and Claude share the same token formatting, five-second waiting details
cycle, five-hour usage percentage and reset countdown, and dashboard billing
and usage metrics. Subscription billing requires observed subscription usage;
cost or context alone cannot establish API or subscription billing. Missing
metadata stays unknown, and an expired usage window is omitted until refreshed.
Session cost is an estimate, not an actual subscription charge. The integration
does not access Claude credentials or make Anthropic account requests.

## Installation safety

Each settings mutation has a local backup. Ownership manifests identify the
exact installed hook definitions and status-line wrapper, including its original
value. Installation is idempotent. Disablement and normal application exit
remove only owned definitions and restore the original status line, preserving
other hooks and subsequent user edits. Altered or unowned app definitions are
treated as conflicts; the settings file remains untouched. Settings are written
using an atomic replacement, with a check for concurrent edits before replacement.

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
- [Claude Code status line](https://code.claude.com/docs/en/statusline)
- [Upstream defaults, pinned revision](https://github.com/rar-file/claude-rpc/blob/eac1fbb29cfd940dfef300b1340744b8995ae5c9/src/default-config.js)
- [Upstream license](https://github.com/rar-file/claude-rpc/blob/eac1fbb29cfd940dfef300b1340744b8995ae5c9/LICENSE)
