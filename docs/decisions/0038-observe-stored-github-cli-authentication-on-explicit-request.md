# ADR-0038: Observe stored GitHub CLI authentication on explicit request

Status: Accepted

## Context

[ADR-0005](0005-use-git-worktrees-and-a-policy-controlled-github-loop.md) and Increment 6 of the
[delivery plan](../roadmap/mvp-delivery-plan.md) require GitHub CLI authentication health without extracting the CLI's token, and
[ADR-0001](0001-build-a-local-first-supervised-orchestrator.md) uses existing local CLI authentication. Today the `GitHubCli` capability
descriptor probes `gh.exe --version` only, so a discovered binary says nothing about authentication, and there is no authentication observer.
This closes one authentication-health observation gap. It is not repository permission, publication authority, a remote or CI operation, or
Increment 6; remote publication stays separately gated, and real Agent qualification is unaffected.

## Decision

**One protected, bodyless, read-only observation.** `GET /api/environment/github-stored-authentication`
(`GetGitHubStoredAuthenticationQuery` through `IApplicationMediator`, one MVC endpoint, protected like every environment operation, API-owned
response, the generated client regenerated reproducibly) returns only the fixed `host` `github.com`, the fixed `context` `StoredDefaultConfig`, a
closed `status` (`CliUnavailable`, `Unknown`, `NoActiveAccount`, `Authenticated`, `IssueReported`) and a nullable `observedAtUtc`, present only
for the last three statuses and taken from the host clock. It accepts no body, host or other client choice, stores nothing and writes no row
(no run, attempt, event, setting or snapshot, and no schema change); a database fault keeps the normal pipeline.

**Target admission, no discovery.** The query reads the existing untracked `GitHubCli` discovery snapshot and invokes the observer only for a
snapshot whose last probe succeeded (`ReasonCode` None), whose launch is a `DirectExecutable` with no script, whose executable is a nonblank
absolute path with an observed version and time, and whose evidence is fresh under the existing readiness rule
(`HostCapabilityReadinessProjector.IsStale`, extracted from the projector, unchanged in behavior: stale once the due time plus its two-minute grace
has passed). Anything else is `CliUnavailable` and nothing is invoked. The query never discovers, refreshes or installs a tool.

**Port and adapter.** Application owns the provider-neutral `IGitHubStoredAuthenticationObserver`; Infrastructure owns the GitHub CLI protocol in
`GitHubStoredAuthenticationAdapter`, composed on the existing `IProcessExecutionAdapter` inside the host scratch directory (never a repository,
shell, private invoker or second process engine). The adapter runs exactly one command with literal arguments:
`auth status --hostname github.com --active --json hosts --jq <selector>`. The constant selector has three branches: a `.hosts` object with no
keys (the official answer, with exit zero, when no host is known or the requested host is not configured) emits `{entries: []}`; a `.hosts` object
whose `["github.com"]` is an array emits only `{entries: [{active: (.active == true), state: success|error|other}]}`; every other original shape
(a missing, null or non-object `hosts`, a non-empty map without an array for `github.com`, an explicit null or non-array target) emits
`{invalid: true}` and so never defaults to an empty successful array. The legitimately empty map is thus `NoActiveAccount`, and malformed input is
never broadly treated as empty.

**Interpretation is closed.** A coherent empty `entries` array is `NoActiveAccount`; exactly one entry with `active` true and state `success` is
`Authenticated`; with state `error` it is `IssueReported`. A timeout, an unknown state, an inactive or multiple entries, a missing, extra or
duplicate field, malformed, oversized or truncated output, a nonzero exit, unsupported flags, an OS failure to start the vetted executable and a concurrent probe
are `Unknown` with no time. A zero exit never proves authentication, and the CLI can exit zero while reporting an authentication problem, which is
`IssueReported`. Only the caller's own cancellation is raised. The adapter converts only the identified expected start failures (the OS `Win32Exception`, and the executor's `ArgumentException` for the executable parameter when the vetted absolute path no longer exists, identified by parameter and never by message) into `Unknown`; every other fault (an `InvalidOperationException`, another argument fault, an I/O fault) propagates through the normal pipeline, and the single probe slot is released in a `finally` either way. The handler additionally refuses an
observation whose time does not match its status, so a port can neither claim `CliUnavailable` nor an observed status without a time.

**Stored default context only.** The adapter reads no auth file or credential value; the CLI accesses its own store. The child receives only a short
allowlist of non-secret OS, profile and temp variables (`SystemRoot`, `SystemDrive`, `windir`, `USERPROFILE`, `APPDATA`, `LOCALAPPDATA`,
`HOMEDRIVE`, `HOMEPATH`, `USERNAME`, `TEMP`, `TMP`, each only when present) and fixed controls disabling prompts, CLI and extension update
notifications, telemetry, color and spinners. No ambient token, debug, proxy, custom host or config directory, browser or pager variable, and not
`PATH`, is forwarded. Consequently an environment-supplied token and a custom-config context are deliberately NOT observed; the observation is not the
owner's complete ambient CLI session, and the cockpit says so.

**Bounds, cleanup and concurrency.** Each probe is bounded to 15 seconds, 8 KiB per stream and 16 KiB combined, through the existing process
cancellation and tree cleanup. There is no raw stream or artifact sink, no provider payload or diagnostic in an error, the API, the UI or a log, and
standard error is discarded. The adapter is a singleton and owns one in-flight probe slot: a concurrent request returns `Unknown` immediately with no
queue, retry or extra process, and the slot is released after every outcome including a fault or cancellation.

**Cockpit.** A compact host-level region beside the provider runtimes is collapsed until opened and reads only on its explicit "Check stored
authentication" or "Check again" action; nothing is read on mount, opening, a project change, a reconnect, a timer or a background refresh. A
fixed, plain-text statement per status (and the observation time for the observed three) is shown with the stored-only scope and that authentication
grants neither repository permission nor publication authority. The reads belong to the mounted component (its end, including the session ending or
an unmount, ends everything) and to each opening: closing and reopening is a new lifetime that starts unchecked, a late answer of an ended or
superseded lifetime is ignored, a second activation in flight is ignored, a callback retained from an ended opening starts and closes nothing of its
successor, and there is no key, remount or reset workaround. The answer is accepted only whole: the exact fixed host and context, a status of the
closed set, and a valid time exactly for the three observed statuses. Agent readiness, claim eligibility, budgets, history and local delivery are
unchanged.

## Consequences

- An owner can see whether the CLI's stored default account for github.com is active, absent or reporting a problem, without a token leaving the CLI
  and without any repository or remote operation.
- The result is advisory and instantaneous: it can change immediately after it is read, it never gates a claim or a delivery, and it proves neither
  repository permission nor the ability to publish.
- Exercising the selector's actual evaluator and the installed CLI's `--json hosts` formatting needs a real `gh`; no such evaluator is available
  without installing one, so those remain unverified here. Tests pin the exact selector and argument vector, restate its expected safe payloads for
  synthetic unfiltered responses, and prove the process boundary with isolated fixtures that print already-filtered JSON. An older or incompatible
  binary is `Unknown`; the observer never installs, upgrades, falls back to human output or runs another command.
- A user whose only authentication is an environment token or a custom configuration sees `NoActiveAccount` or `Unknown`; that is accurate for the
  stored default context and is disclosed, not a statement about the ambient session.
- The contract is additive and changes no admission, claim, scheduling, recovery, approval or digest decision, and it does not complete Increment 6.
