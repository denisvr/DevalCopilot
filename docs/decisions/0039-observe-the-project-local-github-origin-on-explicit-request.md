# ADR-0039: Observe the project's local GitHub origin on explicit request

Status: Accepted

## Context

[ADR-0005](0005-use-git-worktrees-and-a-policy-controlled-github-loop.md) and the
[GitHub and CI integration](../architecture/github-ci-integration.md) design require an explicit repository scope before any exact-SHA CI
observation or remote mutation, and [ADR-0038](0038-observe-stored-github-cli-authentication-on-explicit-request.md) deliberately supplies
neither a target nor authority. A registered project stores only its local root and physical identity; there is no GitHub target and no
push, pull-request or CI adapter. This closes one scoping-input gap: it shows what the project's own repository configuration names as its
origin. It is configured scope, never verified remote identity, permission, CI success or publication authority; remote binding, persistence,
exact-SHA CI, publication policy and reconciliation stay separately gated, and real Agent qualification is unaffected.

## Decision

**One protected, bodyless, read-only observation.** `GET /api/projects/{projectId}/github-origin` (`GetProjectGitHubOriginQuery` through
`IApplicationMediator`, one MVC endpoint under the protected projects route, API-owned response, the generated client regenerated
reproducibly) returns the `projectId`, the fixed `source` `RepositoryLocalConfig`, a closed `status` (`Unavailable`, `NotConfigured`,
`Available`), a nullable `observedAtUtc` and a nullable `target`. Only `Available` carries `target` (`host` fixed `github.com`, `owner`,
`repository`); `Available` and `NotConfigured` carry the host's own observation time; `Unavailable` carries neither and never says why. An unknown
project is the fixed 404 `projects.not_found`; a malformed identity is not routed. The route accepts no body, path, remote name, URL or other
client choice, stores nothing and writes no row (no run, attempt, event, setting, snapshot, identity, lease or marker, and no schema change).

**Admission before any process.** The handler reads the project and the shared `Git` discovery snapshot untracked and proceeds only for a snapshot
whose last probe succeeded, whose launch is a `DirectExecutable` with no script, whose executable is a nonblank absolute path with an observed
version and time, and whose evidence is fresh under the existing readiness rule (`HostCapabilityReadinessProjector.IsStale`); it never
discovers, refreshes or installs a tool. It also requires a stored, resolved physical identity (a 16-byte file identity). Through the existing
read-only root and physical-identity ports it validates that the current root and its identity tuple still equal the stored tuple before the
observation and again after it, and an unresolved, unavailable, changed or malformed identity is `Unavailable` without updating the identity,
invoking the recheck command, leasing or claiming anything. A project that was never rechecked therefore stays `Unavailable` until the owner
uses the existing explicit identity recheck or workspace preparation; the cockpit says so as a possible cause without claiming which applies.
The answer the observer returns is itself normalized (a target exactly for `Available`, none otherwise) and only the handler stamps the time,
after the second validation.

**Port and adapter.** Application owns the provider-neutral `IGitHubOriginObserver`; Infrastructure owns the Git protocol in
`GitHubOriginAdapter` (and the pure `GitHubOriginUrlParser`), composed on the existing `IProcessExecutionAdapter` with the vetted direct
executable, never a shell, a generic Git gateway, a second process engine or a `gh` invocation. It runs exactly two fixed invocations with literal
arguments inside the project root as their own approved root, under one 15 second budget (the second receives only the remainder) and 8 KiB per
stream and 16 KiB combined per invocation, without sinks, input, retries or fallbacks:

1. `rev-parse --is-bare-repository --git-dir --git-common-dir --show-toplevel` is the minimal shape check. The output must be exactly four
   newline-terminated lines: `false`, then a Git directory and a common directory that both resolve to exactly `<root>\.git`, then a top level
   equal to the root. A parent-directory repository, a subdirectory, a bare repository, a linked worktree, a submodule or separate Git directory and
   any other shape is `Unavailable` before any configuration is read; there is no parent fallback. No status, diff, HEAD or checkpoint is read.
2. `config --file <root>\.git\config --no-includes --null --get-all remote.origin.url` observes only that one file, the repository's own
   configuration, bound by path rather than found by a second repository discovery (a correction of the first form, `--local`, which re-discovered
   the repository and could be redirected between the two invocations). Before the first invocation the adapter validates that `<root>\.git` is a
   real, non-reparse directory holding a real, non-reparse regular `config` file (never a gitfile, junction, symbolic link or directory) and holds
   that file open read-only, without delete sharing, until the answer is read (`GitHubOriginScopePin`). An open file cannot be renamed, replaced or
   deleted and neither can its containing directories, so `.git` cannot be moved, replaced by a gitfile or removed to expose a parent repository
   while the invocations run; the facts are checked again after the open and after the second invocation, and a vanished, replaced or redirected
   scope is `Unavailable`, never a foreign value and never a misleading `NotConfigured` (an absent key in the intact, held file stays
   `NotConfigured`). The hold writes nothing, takes no exclusive lock and does not affect Git's own read. Global, system,
   included, conditionally included, worktree-scope and command-line values are never candidates, and neither are URL rewrites (`insteadOf`,
   `pushInsteadOf`) nor `pushurl`: the raw key is read, never `remote get-url` or an effective URL. Nothing contacts a network, credential helper,
   SSH, proxy, shell, hook, pager or filesystem monitor, and nothing writes configuration.

Both invocations carry the established hardening prefix (`--no-pager`, `core.fsmonitor=false`, `core.untrackedCache=false`,
`protocol.allow=never`) and a child environment that contains only `GIT_TERMINAL_PROMPT=0`, `GIT_OPTIONAL_LOCKS=0`, `GIT_CONFIG_NOSYSTEM=1` and
`GIT_CONFIG_GLOBAL` set to the null device. The process executor never forwards the host's ambient variables, so no Git, path, credential, SSH or
proxy override reaches the child; the null global and no-system controls are defense in depth.

**Interpretation is closed.** Exit 1 with empty, untruncated standard output is `NotConfigured` (an exit 1 with output is not). Exit 0 requires
exactly one non-empty NUL-terminated value: a duplicate value (even an identical one), a missing or empty terminator, an embedded NUL, any other
exit, a timeout, an exhausted budget, truncation of either stream and an OS start failure (the identified `Win32Exception`, or the executor's
`ArgumentException` for the executable parameter when the vetted path no longer exists) are `Unavailable`. Only the caller's own cancellation is
raised and every other fault (an `InvalidOperationException`, another argument fault) propagates. The raw value, configuration and standard error
are transient inside Infrastructure and are never returned, logged or stored.

**Supported subset.** Exactly three literal spellings are admitted, each with one optional terminal `.git` removed:
`https://github.com/owner/repository`, `git@github.com:owner/repository` and `ssh://git@github.com/owner/repository`. The match is ordinal and the
input is never trimmed, decoded or normalized. The owner is 1 to 39 ASCII letters, digits and hyphens, beginning and ending alphanumeric; the
repository is 1 to 100 ASCII letters, digits, dots, underscores and hyphens, beginning alphanumeric (so dot segments are impossible). These are a
conservative supported subset, not the full GitHub naming policy. HTTPS credentials, an alternate SSH user, a port, an escape, whitespace or control
character, a query, fragment, extra, empty or trailing segment, another host or protocol, and a local path are `Unavailable`.

**Cockpit.** A collapsed project-level region offers "Show project GitHub origin", "Inspect GitHub origin" (then "Inspect again") and "Close project
GitHub origin", and reads only on the explicit inspection. A fixed English note states that only the origin URL in the repository's own local Git
configuration is read, that global, system, included and worktree settings, URL rewrite rules and a separate push destination are ignored (so Git
itself may use a different address), and that nothing contacts GitHub and the result is not a verified remote identity, permission, CI result or
publication authority. The target is plain text (host, owner, repository) with no link, copy or other action. Nothing is read on mount, opening, a
project change, a reconnect, a timer or a background refresh. The mounted hook, the committed project and each opening of that project own the
reads: another project, a return to an earlier one, closing and reopening each start collapsed and unchecked; a late answer of an ended or
superseded lifetime is ignored; a second activation in flight is ignored; and a callback retained from an ended lifetime starts and closes nothing
of its successor (close is bound to its own opening and serial-guarded). The answer is accepted only whole, for exactly the observed project, the
fixed source and host, a valid time exactly for the two observed statuses and a target of the supported ASCII shape for `Available` alone.

## Consequences

- An owner can see which GitHub repository the project's own local configuration names as `origin`, as an input for later repository scoping,
  without any remote, credential or network operation.
- The answer is advisory and instantaneous: it can change immediately after it is read, never gates a claim or a delivery, and proves neither that the
  repository exists, that the URL is the one Git would use, nor any permission. A later slice must bind a target explicitly and verify it before any
  exact-SHA CI or publication.
- A project with an unresolved physical identity, a layout other than its own main repository (a linked worktree, a submodule or a separate Git
  directory), or a missing, duplicate or unsupported origin is `Unavailable`; the cause is deliberately not reported.
- The shape check compares Git's reported paths with the registered root and is conservative: a root that Git reports under a different spelling
  (for example a short name) is `Unavailable`, never guessed.
- The scope binding relies on Windows sharing semantics (an open file pins its ancestors). The first invocation's repository discovery and the
  configuration read share one held scope, but the hold is acquired after the handler's own root and identity validation and cannot prove what
  Git itself opens internally; a hold that fails to acquire (an antivirus or backup lock without read sharing, a locked config) is `Unavailable`.
  A file symbolic link for `config` needs a privilege the test account lacks, so that one refusal is implemented and statically asserted but not
  exercised by a real link here.
- The contract is additive. It changes no Domain, write-policy, schema, mapping, migration, dependency, admission, claim, scheduling, recovery,
  approval or digest decision, adds no remote binding or persistence, and does not complete Increment 6.
