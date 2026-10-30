# Agent collaboration provider contracts

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds host runtime preflight, the body of the protocol's
[provider runtime configuration](agent-collaboration-protocol.md#provider-runtime-configuration)
moved into the first section below, and the provider CLI safety contracts
formerly under that heading. The protocol heading forwards to that section;
its subsection headings forward to this and other companions. The preflight
statements, including their increment wording and the one manual observation
of a Codex desktop installation layout, are as written at the baseline; the
move reconciles or endorses none of them.
The sections below are moved verbatim; the protocol's original headings forward
here. The implementation contract builds on the critical-review contract that
precedes it, so its references to that contract "above" mean the preceding
section. Mapping: [protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## Provider runtime configuration

Host runtime preflight is intentionally narrower than provider runtime configuration, and is
itself layered into four distinct stages that must not be confused with one another:

1. **Executable discovery** — resolving a fixed candidate name to an absolute, existing path
   using only the host's normalized `PATH` and a small set of fixed fallback directories. Never a
   shell, never `PATHEXT`, never a filesystem enumeration.
2. **Launch-target validation** — for a provider whose only local discovery in Increment 2 is a
   native executable, discovery and launch-target validation coincide. Increment 4 adds one
   further, still narrowly bounded discovery path for a provider distributed as an npm package: a
   fixed, catalog-owned package-root candidate's `package.json` is read directly (strict size
   limit, strict parsing, exact expected package identity), and only its own declared `bin` entry
   is resolved, and only when that entrypoint remains beneath the package root. This never
   executes npm, npx, a package-manager shim, `.cmd`, `.bat`, cmd.exe, PowerShell, or any shell.
   More than one independently valid launch target is treated as ambiguous and fails closed —
   never chosen between silently.
3. **Version observation** — a successful direct executable version probe (or, for a validated
   npm-package launch target, a direct, fully qualified Node executable running that validated
   entrypoint) proves only that DevalCopilot observed one local runtime version at a point in
   time.
4. **Authentication/invocation readiness** — everything beyond an observed version: model
   availability, permission mode, context capacity, account usage, session continuity, and
   whether an invocation is actually eligible. These facts remain explicit `Unknown` until a
   later provider-owned adapter observes them through a reviewed contract. A future `Unsupported`
   value requires affirmative provider evidence; it is never inferred from the absence of a
   preflight probe.

A locally installed Codex desktop application's own private, versioned installation layout was
observed once, manually, through read-only inspection outside this discovery contract. That
layout is evidence that a directly executable Codex CLI can exist on a host — it is not an
approved stable discovery contract, and it is never added as an automatic fallback: it belongs to
a different application, is not catalog-owned, and carries no stability guarantee DevalCopilot
can rely on. In particular, an app-private or portable Codex desktop installation layout is never
searched for automatically at attempt-dispatch time; resolving the actual launch target for a new
Codex Planning attempt only reuses the durable `HostCapabilitySnapshot` row's resolved path from
capability discovery's own last successful probe, and only when that snapshot's reason code is
still `None` — it is revalidated, never re-searched, by this narrower dispatch-time read.

### Windows Node-hosted launch environment

A controlled diagnostic through the production child-process adapter observed the fixed Node host
abort at startup under an empty environment, and under the Codex allowlist shape with only
`USERPROFILE`, and start successfully when `SystemRoot` was the only addition. A Node-hosted
launch therefore carries one explicit OS prerequisite, while the child environment stays an
explicit allowlist:

- **Applicability.** The rule (the internal `WindowsNodeProcessEnvironment` policy) applies only
  when the host is Windows and the request starts the Node runtime. There are four source entry
  paths, all using the one policy:
  - capability discovery of a `ProviderLaunchTarget.NodeScript`;
  - the native probe of the explicit `Capability.Node`, decided from the requested capability
    value and never from an executable's file name, extension or path, so another capability's
    executable named `node.exe` does not receive it and every other native capability keeps its
    empty environment;
  - a non-null `LaunchScriptPath` in the shared Codex invocation, which serves Planning,
    ImplementationReview, ChallengeResolution and VerificationDiagnosis;
  - a non-null script path in the shared Codex App Server session, which serves the model-catalog,
    account-allowance and account-usage-guard observations.

  A direct native Codex launch, any other native request and every non-Windows request keep their
  existing environment exactly and never evaluate the system-directory reader. No role or adapter
  copies the rule.
- **Value.** Exactly one entry, `SystemRoot`, is added: the parent directory of
  `Environment.SystemDirectory`, the managed getter backed by `GetSystemDirectoryW`. The ambient
  `SystemRoot` and `windir` variables, a hardcoded installation path, P/Invoke, a directory search
  and repository or user configuration are never consulted or used as a fallback.
- **Validation.** Lexical only, with no existence, content, metadata, reparse-point or ACL
  inspection of this API-derived value. The system directory must be nonblank, fully qualified,
  free of control and NUL characters and of dot or dot-dot components, with at most one trailing
  directory separator removed. Its parent must be nonblank, fully qualified and not a filesystem
  root. No drive, installation-directory name or system-directory leaf name is assumed.
- **Refusal.** A Windows request that requires the Node runtime and whose value cannot be resolved
  or validated, including a reader that throws an expected OS or path failure, is refused before
  any process dispatch and before any scratch directory, schema or output artifact exists.
  Discovery (the script and the native Node probe) reports the existing `ExecutableInaccessible`
  reason; the Codex invocation returns its existing closed failure with no process evidence; the
  App Server session returns the exact unknown result its caller supplied, creating no scratch
  directory, starting no process and running neither the handshake nor the caller's exchange. In
  the session the check follows the existing cancellation and launch-component revalidation and
  precedes scratch creation. There is no retry, fallback, or launch with the smaller map, and
  neither a value nor exception detail leaves the policy. Cancellation still propagates.
- **Unchanged.** The Codex invocation and the App Server session still forward an existing
  nonempty `USERPROFILE` and `CODEX_HOME` unchanged (null and empty are omitted, whitespace is
  neither trimmed nor rejected) and nothing else, plus `SystemRoot` on a Windows script-hosted
  launch; the native Node probe carries `SystemRoot` alone. The session's argument order
  (script prefix when present, then `app-server` and `--stdio`), redirection, no-shell setting,
  duplex JSON-RPC handshake and exchange, bounds, stderr draining, cancellation and owned-process
  cleanup are unchanged. `PATH`, `NODE_OPTIONS`, tokens, proxy, certificate, temp and every other
  ambient entry stay excluded, and no environment value is logged, persisted or returned.

Evidence limits: the diagnostic was a single fixed-order run against a probe script, so it
establishes that `SystemRoot` was sufficient in two contexts, not that it is minimal or the reason
inside Node. The native Node probe and the App Server script launch were added by source
inference that they share the same exposure; neither was observed under the empty or the
extended environment with a real runtime. The regression tests use fake process adapters,
fabricated paths and capturing callbacks, except that the tests of the four Codex wrappers read
the real system directory to compute their expected value, and the fixture-driven model-catalog
and account-allowance adapter tests run the production binding, so on Windows their script-path
cases pass the real derived value to the fixture child (a test double, never Node or Codex). The
new App Server environment tests capture the prepared process start information and throw a
handled start failure, so they launch no child. They prove request construction, refusal and
fixture behavior only. None establishes provider or production-host compatibility,
authentication or qualification.

## Claude Code critical-review CLI safety contract

The installed `@anthropic-ai/claude-code` package (version `2.1.276` at the time this contract
was evidenced) ships a native `claude.exe` as its declared `bin` entry — a `DirectExecutable`
launch target, never a Node-hosted script. Every fact below was proven from authoritative local
evidence only (`claude --version`, `claude --help`, the package's own `package.json` and bundled
`cli-wrapper.cjs`, and embedded literal strings in the compiled binary) — never by invoking a real
authenticated model to discover a flag. A Claude critical-review attempt invokes this exact,
fixed argument list, with the context manifest delivered over stdin and no argument ever
containing prompt text, repository content, or a credential:

```
--print
--input-format text
--output-format json
--json-schema <inline JSON Schema for the Acceptance/Challenge union>
--safe-mode
--restricted
--disable-slash-commands
--no-chrome
--permission-prompts none
--prompt-suggestions false
--tools ""
--strict-mcp-config
--permission-mode plan
--no-session-persistence
--session-id <fresh random GUID>
--max-turns 1
```

- **Non-interactive**: `--print` runs one bounded turn and exits; it is never given a TTY.
- **Stdin, never argv**: `--input-format text` (the default) reads the prompt/context from
  stdin; the CLI's own embedded strings confirm a bounded stdin-wait and UTF-8 stdin handling.
- **Structured output**: `--output-format json` wraps the result in one JSON envelope on stdout
  (fields observed in the binary's own string table: `is_error`, `result`, `session_id`,
  `subtype`, `num_turns`, `total_cost_usd`, `duration_ms`); `--json-schema` constrains the
  provider's own `result` field to this slice's fixed Acceptance/Challenge schema. Unlike Codex,
  Claude's print mode has no file-based final-response flag — the adapter extracts `result` from
  this envelope and writes it to the same sealed final-response artifact Codex's CLI writes
  directly, so every later parsing/recording step is identical between both providers.
- **Fully isolated from local customizations and indirect execution**: `--tools ""` and
  `--strict-mcp-config` alone disable built-in tools and non-declared MCP servers, but the
  installed CLI's own `--help` proves neither one disables hooks, plugins, skills, CLAUDE.md
  auto-discovery, Claude-in-Chrome, or user/project/local settings files — a `SessionStart` hook
  or a project-local setting could otherwise still cause a command to run with no built-in tool
  ever invoked. `--safe-mode` disables CLAUDE.md, skills, plugins, hooks, MCP servers, custom
  commands/agents, output styles, workflows, themes, and keybindings outright; `--restricted`
  independently removes command/code-running tools and WebFetch and ignores user/project/local
  settings files (a second, independent path to the same guarantee); `--disable-slash-commands`
  disables all skills; `--no-chrome` disables the Claude-in-Chrome integration entirely;
  `--permission-prompts none` denies anything that would otherwise prompt, independent of the
  permission mode; and `--permission-mode plan` is the strongest available permission mode — it
  never executes an action, only plans one, even if every tool were somehow still reachable.
  `--prompt-suggestions false` suppresses the provider's own predicted-next-prompt side output.
  `--bare` is deliberately never passed: the installed version's own `--help` states it changes
  the authentication contract itself (Anthropic auth becomes strictly
  `ANTHROPIC_API_KEY`/`apiKeyHelper`; OAuth and keychain are never read), which would silently
  break this slice's reliance on existing local CLI authentication.
- **No session resume**: `--no-session-persistence` plus a fresh, per-attempt random
  `--session-id` guarantee this invocation can never continue or fork an unrelated prior session;
  `--continue`, `--resume`, and `--fork-session` are never passed.
- **Bounded turns**: `--max-turns 1` bounds the agentic loop to a single turn; `--max-turns` was
  confirmed present in the compiled binary's embedded string table (alongside a corroborating
  `error_max_turns` result subtype) even though it is not listed in the CLI's own abbreviated
  `--help` output.
- **Cancellation and process-tree compatibility**: proven generically by the same process-tree
  termination infrastructure already established for Codex — a `DirectExecutable` launch target
  is handled identically regardless of which provider resolved to it.

The stdout envelope itself is also validated, not merely parsed: `is_error` must be present and a
genuine JSON boolean (only a literal `false` is ever treated as success — a missing or
non-boolean value fails the whole envelope closed), `result` must be present, and a present
`session_id` must be a well-shaped bounded string (a genuine JSON `null` is tolerated exactly like
an absent field, since `--no-session-persistence` means the provider may legitimately have no
session to report; anything else malformed rejects the whole envelope, never just that field, on
the reasoning that a provider which cannot even shape its own bookkeeping field correctly is not
a source whose `result` should be trusted either).

One material limitation, honestly disclosed rather than assumed away: the exact shape of the
`result` field when `--json-schema` is supplied (a JSON string containing schema-conformant text,
versus the schema-conformant JSON value directly) was inferred from embedded evidence and general
knowledge of the CLI's public contract, not observed from a real authenticated invocation — the
adapter therefore normalizes either shape defensively, and the downstream parser fails closed
(`InvalidStructuredOutput`, never a false Acceptance) if that inference is ever wrong.

## Claude Code implementation CLI safety contract

The Claude implementation attempt invokes the same installed `claude.exe` `DirectExecutable`
launch target as the critical-review attempt above, using a dedicated argument list — never the
critical-review adapter reused by changing flags, since this role's tool allowlist and permission
mode differ in kind (mutating repository edits) from every other Claude usage in this protocol.
The context manifest is delivered over stdin exactly as above:

```
--print
--input-format text
--output-format json
--json-schema <inline JSON Schema for the implementation report>
--safe-mode
--restricted
--disable-slash-commands
--no-chrome
--permission-prompts none
--prompt-suggestions false
--tools "Read,Edit,Write,Glob,Grep"
--strict-mcp-config
--permission-mode acceptEdits
--no-session-persistence
--session-id <fresh random GUID>
```

Every hardening flag shared with the critical-review contract above (`--safe-mode`,
`--restricted`, `--disable-slash-commands`, `--no-chrome`, `--permission-prompts none`,
`--prompt-suggestions false`, `--strict-mcp-config`, `--no-session-persistence`, and the same
never-`--bare`/`--continue`/`--resume`/`--fork-session`/`--dangerously-skip-permissions` set)
applies identically. Two differences are deliberate:

- **An explicit tool allowlist, not the empty one**: `--tools "Read,Edit,Write,Glob,Grep"` grants
  only file-reading and file-editing tools. The installed CLI's own `--help` states this option
  supplies "the list of available tools" — never "additional" tools, the wording `--add-dir` uses
  for its own genuinely additive option — and its three parallel forms (`""` disables all tools,
  `"default"` restores every tool, an explicit list names exactly the available set) are only
  coherent under replacement semantics: `""` could not mean "no tools" if a passed list were
  merely added to an existing default set. The compiled binary's own embedded implementation
  corroborates this: the CLI computes a `baseToolsCli`/`getAllBaseTools` tool universe distinct
  from the separate `allowedToolsCli`/`disallowedToolsCli` permission-grant layer used by the
  unrelated `--allowedTools`/`--disallowedTools` flags this adapter never passes, and applies it
  through a coordinator tool filter (`applyCoordinatorToolFilter`). Every name in the allowlist —
  and every name deliberately absent from it (Bash, WebFetch, WebSearch, NotebookEdit, Task/Agent,
  ExitPlanMode) — is confirmed present as a literal, capitalized tool identifier in the installed
  CLI binary: this attempt never runs a shell, script, or arbitrary process, never browses the
  web, and never spawns a sub-agent. `--restricted`'s own `--help` text independently confirms
  Bash/PowerShell/REPL/code-running tools and WebFetch are removed "unless `--tools` names them"
  (this allowlist never does) and that it "confines the file tools to the working directories". No
  Git command, verification command, commit, push, package install, or network operation is ever
  reachable through this allowlist.
- **`--permission-mode acceptEdits`, not `plan`**: the strongest available mode still compatible
  with real file mutation, since `plan` never edits anything and `bypassPermissions` is explicitly
  refused by `--restricted`. Beyond the enum name and the corroborating embedded string
  "auto-accept edits", the compiled binary contains an actual `realpathSync`-backed, cached
  path-resolution utility (`realpathSync(path) { val = lazyFs().realpathSync(path); }`) together
  with a `blockReadsOutsideWorkingDirectories` permission concept and a `checkPathSafetyForAutoEdit`
  function named specifically for this mode's own auto-applied-edit path — real implementation
  evidence, not merely a flag description, that edits accepted under this mode are checked against
  a working-directory boundary using symlink/junction-resolving real-path comparison, consistent
  with `--restricted`'s own textual confinement guarantee above. The one point that remains a
  disclosed inference rather than a directly observed behavior is the mode's precise interactive
  consequence (that it truly never blocks on a prompt for an in-boundary edit) — the CLI ships no
  bundled documentation asserting this in so many words, and confirming it further would require
  an authenticated invocation this project's evidence discipline forbids. Mirrors the
  `result`-field-shape limitation the critical-review contract above already discloses in kind.
- **No `--max-turns` by default; an optional owner request adds exactly one**: implementation is
  inherently multi-step (read, edit, re-read), unlike a single-turn critical review, so no turn
  limit is passed unless the attempt's immutable snapshot requested one (see
  ["Optional Claude agentic-turn limit for mutation attempts"](agent-collaboration-protocol.md#optional-claude-agentic-turn-limit-for-mutation-attempts));
  otherwise the bound remains the process-level timeout, cancellation, and process-tree
  termination already established for every other provider adapter.

Because each initial Implementer invocation can genuinely mutate the worktree, the
orchestrator always independently re-reads fresh Git evidence after this adapter returns —
regardless of whether the process succeeded, failed, or threw — and never assumes a failed or
cancelled process left the worktree untouched.

## Deferred Gemini CLI execution boundary

Gemini CLI 0.60.0 is not currently selectable or claimable. Its system settings
are administrative configuration that require trusted administrator ownership
and permissions. DevalCopilot must never manufacture that policy in
user-owned attempt scratch state. A future slice must define trusted
installation and policy provisioning, ACL validation, exact semantic policy
validation, readiness reporting, and a real no-model contract test before any
Gemini execution can be enabled.

Gemini authentication material must not be copied into scratch or persistence.
No automatic fallback or parallel execution is authorized. Durable assignment
facts do not authorize automatic verification or publication.
