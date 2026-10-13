# ADR-0041: Observe the GitHub repository identity of a recorded observation target on explicit request

Status: Accepted

## Context

[ADR-0005](0005-use-git-worktrees-and-a-policy-controlled-github-loop.md) and the
[GitHub and CI integration](../architecture/github-ci-integration.md) design require a verified repository identity before any exact-SHA CI
observation or remote mutation. [ADR-0040](0040-record-an-immutable-github-observation-scope-for-a-completed-local-delivery.md) records only the Human's local
selection of an owner and repository for an exact local commit and branch; [ADR-0038](0038-observe-stored-github-cli-authentication-on-explicit-request.md)
observes only stored authentication health and supplies no target. Nothing yet asks GitHub whether that selected repository exists, under which ID and with which
names. This closes one input gap: an explicit, read-only observation of the remote repository's opaque ID and names for the recorded local target. It proves no
remote branch or commit, permission, CI result, publication approval or permanent binding, and persists nothing. Remote branch and SHA correlation, exact-SHA CI,
scoped publication, reconciliation, the remaining Increment 4 contracts and qualification, and Increment 5 coordination, concurrency and recovery stay separately
gated, and real Agent qualification is unaffected.

## Decision

**One protected, bodyless, read-only observation.** `GET /api/runs/{runId}/github-repository-observation` (`GetGitHubRepositoryObservationQuery` through
`IApplicationMediator`, one MVC endpoint under the protected runs route, API-owned response, the generated client regenerated reproducibly) returns the `runId`,
a closed `status` (`NotRecorded`, `Unavailable`, `CliUnavailable`, `Unknown`, `Observed`), a nullable `source` `{operationId, commitSha, branchName}`, a
nullable `target` `{id, eventId, host, owner, repository, recordedAtUtc}` and a nullable `observation`
`{repositoryNodeId, owner, repository, observedAtUtc}`. An unknown run is the fixed 404 `github_repository_observation.not_found`. The route accepts no client
owner, URL, branch, SHA, executable or credential, and any request body (a declared length, a transfer encoding or a readable byte) is refused before anything
is read or observed with the fixed 400 `github_repository_observation.body_not_allowed`, which never echoes it. It writes no row, event or lock, holds no
transaction across external work and changes no schema; a database fault keeps the normal pipeline.

**Scope from the unchanged ADR-0040 readers.** The handler reads the exact source and target with the existing `GitHubObservationSourceReader` and
`GitHubObservationTargetReader`, untracked. No recorded target is `NotRecorded` and an incoherent one is `Unavailable`: both carry neither scope nor observation and
invoke nothing. Every other status carries the exact coherent source and target. Only `Observed` carries an observation.

**Admission before any process, no discovery.** The handler reads the existing untracked `GitHubCli` discovery snapshot (ADR-0038 admission): last probe
successful, a `DirectExecutable` with no script, a nonblank absolute executable, an observed version and time, and evidence fresh under the existing readiness rule
(`HostCapabilityReadinessProjector.IsStale`). Anything else is `CliUnavailable` with nothing invoked. It never discovers, refreshes or installs a tool, and it has
no authentication-status, origin or other prerequisite.

**Scope is re-read after the external work.** After the observer returns, the handler reads the source and target again; a lost, changed or incoherent scope is
`Unavailable` with no remote data, whatever the observer said. The port's result is validated independently: an opaque node ID of 1 to 256 ASCII letters, digits or
`_ - / + =` (`GitHubRepositoryNodeIdPolicy`, never decoded), an owner and repository inside the unchanged ADR-0039/ADR-0040 conservative name policy, and names equal
to the recorded scope ordinally, ignoring only ASCII case. A rename, transfer, trimmed or `.git`-stripped name is never adopted. Any other result is `Unknown`. Only
then does the handler stamp the host clock for `Observed`. Cancellation and unexpected faults propagate.

**Port and adapter.** Application owns the provider-neutral `IGitHubRepositoryIdentityObserver`, operation-owned and returning only
`{NodeId, Owner, Repository}` or nothing. Infrastructure owns the GitHub CLI protocol in `GitHubRepositoryIdentityAdapter`, composed on the existing
`IProcessExecutionAdapter` inside the host scratch directory (never a repository, shell, jq or second process engine). It runs exactly one invocation with
separate literal arguments:

`gh api graphql --hostname github.com --method POST -f query=<constant> -f owner=<stored owner> -f name=<stored repository>`

with the constant query `query($owner:String!,$name:String!){repository(owner:$owner,name:$name){id name nameWithOwner}}`. The HTTP `POST` only transports the
read-only GraphQL query. The stored names travel only as `-f` raw-string variables (never `-F`, so numeric-looking names and a `.git` suffix are preserved
exactly) and are never interpolated into the query. There is no mutation, arbitrary query or endpoint, shell, jq, pagination, cache, retry or additional command.
Stored names outside the supported subset are never passed to a process.

**Process boundary.** The child receives the ADR-0038 non-secret OS, profile and temp allowlist (`SystemRoot`, `SystemDrive`, `windir`, `USERPROFILE`, `APPDATA`,
`LOCALAPPDATA`, `HOMEDRIVE`, `HOMEPATH`, `USERNAME`, `TEMP`, `TMP`, each only when present) and the fixed controls disabling prompts, update notifications,
telemetry, color and spinners. No ambient token, host, custom configuration, debug, proxy, pager or browser override is forwarded and no credential is extracted:
the observation is the stored default context only, so an environment-supplied token is deliberately not used. There are no output sinks, logs or artifacts; the
timeout is 15 seconds, the caps are 8 KiB per stream and 16 KiB combined, and standard error is discarded unread. The adapter is a singleton with one in-flight
slot: a concurrent observation returns nothing immediately (`Unknown`), with no queue, retry or extra process, and the slot is released in a `finally` after every
outcome.

**Strict response admission.** Only the exact JSON `{"data":{"repository":{"id":"...","name":"...","nameWithOwner":"..."}}}` is accepted: errors, extensions,
duplicate or extra fields at any level, partial data, `null`, non-strings, JSON escapes, trailing text, comments, malformed or oversized values are refused whole.
`nameWithOwner` has exactly one owner/name split and its name part equals `name` exactly. A nonzero exit, a timeout, truncation of either stream and an inaccessible
or missing repository are `Unknown`; neither nonexistence nor permission is inferred. Only the identified OS start failures (the `Win32Exception`, and the executor's
`ArgumentException` for the executable parameter when the vetted absolute path no longer exists) become `Unknown`; the caller's cancellation and every other fault
propagate.

**Cockpit.** Beside an Available ADR-0040 target (inside its result, as a collapsed region of its own, never inside the local-commit region) an identity region
offers "Show GitHub repository identity", "Check GitHub repository identity" (then "Recheck") and "Close GitHub repository identity". It shows the exact branch,
commit and recorded owner and repository, a fixed disclosure that Check queries GitHub through the CLI's default stored authentication and reads only the ID and
names, and, for the answer, fixed statements per status and the ID, names and observation time as inert text (no link, copy or other action). Nothing is read on
mount, opening, a project, run or target change, a reconnect or a timer, and nothing is retried. The committed source and target own the region and each opening is
a lifetime: another source or target, a return to an earlier one, closing and reopening each start collapsed and unchecked; a late answer of an ended or
superseded lifetime is ignored; close is serial-guarded; a second activation in flight, two in one batch and any activation after a close in the same batch send
nothing. The whole answer is accepted only for exactly the observed source and target (same run, source, target identity, names and time, and an observation only
for `Observed`). There is no key, remount, reset effect, browser storage or automatic retry; reloading or closing the target ends the identity lifetime.

## Consequences

- An owner can see, on explicit request and without changing anything, whether GitHub resolves the recorded owner and repository, and under which opaque ID and
  names, as an input for a later verified binding.
- The answer is advisory and instantaneous: it can change immediately after it is read, never gates a claim, delivery or publication, and proves neither a remote
  branch, a commit, a permission, a CI result nor that the repository will keep its ID or names. A later slice must define and persist any binding.
- A fixture double proves the process boundary only; the installed CLI's real formatting of a GraphQL answer, stored-authentication compatibility and GitHub's
  reliability remain unverified here, and an older or incompatible binary is `Unknown`. The observer never installs, upgrades or falls back to another command.
- A user whose only authentication is an environment token or a custom configuration sees `Unknown`; that is accurate for the stored default context.
- A repository that GitHub reports under another name (renamed or transferred) is `Unknown` and is never adopted; the immutable ADR-0040 record is not changed.
- The contract is additive and changes no Domain write policy, schema, migration, journal, admission, claim, scheduling, recovery, approval, budget or digest
  decision and adds no dependency, remote mutation or CI monitoring. It does not complete Increment 6.
