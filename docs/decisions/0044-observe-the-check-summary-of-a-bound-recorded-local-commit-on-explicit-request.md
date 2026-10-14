# ADR-0044: Observe the check summary of a bound recorded local commit on explicit request

Status: Accepted

## Context

[ADR-0005](0005-use-git-worktrees-and-a-policy-controlled-github-loop.md) and the
[GitHub and CI integration](../architecture/github-ci-integration.md) design require exact-SHA CI evidence before any later publication or reconciliation decision.
[ADR-0040](0040-record-an-immutable-github-observation-scope-for-a-completed-local-delivery.md) records the exact local commit, branch and Human-selected owner and
repository, [ADR-0041](0041-observe-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md) and
[ADR-0042](0042-observe-the-recorded-github-branch-tip-of-a-recorded-observation-target-on-explicit-request.md) observe the repository identity and the branch tip
ephemerally, and [ADR-0043](0043-bind-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md) records the first durable remote fact,
the immutable opaque repository ID the Human confirmed. No operation yet reports anything about checks. This closes one input gap: on explicit request, show GitHub's
single point-in-time aggregate check state for the recorded commit, but only when the freshly observed repository ID equals the immutable binding and the exact recorded
branch currently points at the recorded commit. It is GitHub's aggregate (`Commit.statusCheckRollup.state`, which combines check runs and commit statuses) and nothing
more: not complete check or workflow evidence, not a required-check or branch-protection decision, not a permission, not a publication approval and not a publication
performed by DevalCopilot. Required-check policy, check and workflow enumeration, scoped publication, push, pull request, reconciliation, automatic monitoring, the
remaining Increment 4 contracts and qualification, and Increment 5 coordination stay separately gated, and real Agent qualification is unaffected. The ADR-0040
through ADR-0043 contracts, readers, ports, adapters, statuses and wire answers are unchanged.

## Decision

**One protected, bodyless, read-only observation.** `GET /api/runs/{runId}/github-ci-observation` (`GetGitHubCiObservationQuery` through `IApplicationMediator`, one MVC
endpoint under the protected runs route, API-owned response types for the whole transport graph, the generated client regenerated reproducibly and never edited by hand)
returns `{runId, status, source, target, binding, observation}`. The `source` `{operationId, commitSha, branchName}`, `target`
`{id, eventId, host, owner, repository, recordedAtUtc}` and `binding` `{id, eventId, repositoryNodeId, owner, repository, observedAtUtc, recordedAtUtc}` are the existing
fact shapes (the same API-owned response types), mapped from Application views. The `observation` is
`{repositoryNodeId, owner, repository, observedAtUtc, ref: null | {branchName, commitSha}, rollupState: null | "ERROR" | "EXPECTED" | "FAILURE" | "PENDING" | "SUCCESS"}`.
An unknown run is the fixed 404 `github_ci_observation.not_found`. The route accepts no client repository, node ID, branch, SHA, state, executable or credential, and
any request body (a positive declared length, any transfer encoding, including an empty chunked one, or a readable byte) is refused before dispatch with the fixed 400
`github_ci_observation.body_not_allowed`, which never echoes it. It writes no row, event or lock, holds no transaction across external work, persists nothing and changes
no schema; a database fault keeps the normal pipeline.

**Closed statuses and nullability.** Four fact groups are nullable (`source`, `target`, `binding`, `observation`) and each status fixes exactly which are present.

| Status | source, target | binding | observation | Invoked |
|---|---|---|---|---|
| `NotRecorded` (no target and no binding or event) | absent | absent | absent | nothing |
| `Unavailable` (any source, target, binding or event is incoherent or orphaned, or the scope or binding was lost or changed during the observation) | absent | absent | absent | nothing on the initial refusal |
| `BindingNotRecorded` (coherent source and target; genuinely no binding row and no event) | present | absent | absent | nothing |
| `CliUnavailable`, `Unknown` | present | present | absent | nothing for the first |
| `IdentityChanged` (a whole valid answer whose opaque ID differs ordinally from the binding; the other ID is never returned) | present | present | absent | one observation |
| `RefNotReturned` (valid null ref) | present | present | present, `ref` null, `rollupState` null | one observation |
| `CommitDifferent` (bound ID, exact branch, another valid commit) | present | present | present, `ref` present, `rollupState` null even if GitHub supplied one | one observation |
| `NoStatusReported` (bound ID, exact branch, the recorded commit, GitHub returned no rollup) | present | present | present, `ref` present, `rollupState` null | one observation |
| `StatusReported` (bound ID, exact branch, the recorded commit, a rollup) | present | present | present, `ref` present, `rollupState` exactly one of the five literal values | one observation |

The names in an observation are the observed names (equal to the recorded ones only ignoring ASCII case), never rewritten as the target's. A null `rollupState` means only
that no rollup is exposed (GitHub returned none for the recorded commit, or the commit is not the recorded one); it never means that no checks exist. No status or text
translates a state: `SUCCESS` is never called passed, approved, ready or green, and `StatusReported` carries GitHub's literal value.

**Scope and binding from the unchanged readers.** The handler reads the source, target and binding with the existing `GitHubObservationSourceReader`,
`GitHubObservationTargetReader` and `GitHubRepositoryIdentityBindingReader` (untracked, bounded) before any process, including binding and event orphan detection when the
source or target is absent: a binding or its event without a coherent target is `Unavailable`, never `NotRecorded` or `BindingNotRecorded`. No target and no binding or
event is `NotRecorded`; a coherent target with neither a binding row nor a binding event is `BindingNotRecorded`; anything else that is not one coherent bound scope is
`Unavailable`.

**Admission before any process, no discovery.** The handler requires the same fresh, successful `GitHubCli` `DirectExecutable` snapshot as ADR-0038, ADR-0041 and
ADR-0042 (no script, a nonblank absolute executable, an observed version and time, fresh under `HostCapabilityReadinessProjector.IsStale`); anything else is
`CliUnavailable` with nothing invoked. It never discovers, refreshes or installs a tool, and depends on no authentication-status, origin, identity or branch observation
and on no answer another region displayed: the host reads the binding itself on every request.

**Observe, read again, validate, then compare.** The handler invokes the operation-owned, provider-neutral `IGitHubCiObserver` exactly once with no transaction and no write
lock held. After it returns it reads the source, target, binding and their canonical events again as one scope and compares the whole with the first reading; any lost,
changed or incoherent fact (including a changed or lost binding or binding event) makes the answer `Unavailable` and strips every scope and remote fact, whatever the
observer said. It then validates the entire result independently before any classification: an opaque node ID of 1 to 256 ASCII letters, digits or `_ - / + =`
(`GitHubRepositoryNodeIdPolicy`, unchanged, never decoded), an owner and repository inside the unchanged conservative name policy and equal to the recorded scope ordinally
ignoring only ASCII case (a rename, transfer, trimmed or `.git`-stripped name is never adopted), a ref that is absent or has exactly the recorded owned branch name
(`GitHubOwnedBranchNamePolicy`, ordinal) and a 40-character lowercase hexadecimal commit, and a rollup that is absent or one of the five literal states and exists only
beside a ref (`GitHubCiRollupStatePolicy`). A malformed answer is `Unknown`, never a partially admitted identity change or commit difference. Only then is the node ID
compared with the binding ordinally (a mismatch is `IdentityChanged`) and the commit with the recorded commit ordinally. The observation is stamped from the host UTC clock
after the external work; a reading earlier than `binding.RecordedAtUtc` is `Unknown` without an observation, never clamped, retried, reobserved or fabricated. The clock is
read for the statuses that carry an observation; an identity change carries none. Cancellation and unexpected faults propagate.

**Port and adapter.** Application owns `IGitHubCiObserver`, returning only `{NodeId, Owner, Repository, Ref, RollupState}` or nothing. Infrastructure owns the GitHub CLI
protocol in `GitHubCiObservationAdapter`, composed on the existing `IProcessExecutionAdapter` inside the host scratch directory; the ADR-0041 and ADR-0042 adapters are
unchanged and no broad gateway is extracted. It validates the supported names and the bounded owned branch before invoking, refuses anything else without a process, and runs
exactly one invocation with separate literal arguments:

`gh api graphql --hostname github.com --method POST -f query=<constant> -f owner=<stored owner> -f name=<stored repository> -f ref=refs/heads/<recorded branch>`

with the one fixed read-only document
`query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid statusCheckRollup{state}}}}}}`.
The HTTP `POST` only transports the query. The stored names and branch travel only as `-f` raw-string variables (never `-F`) and are never interpolated. There is no
mutation, shell, jq, pagination, cache, retry, fallback or second invocation. The query selects one aggregate state and nothing else: no list of checks, check suites,
workflow runs, contexts, pull requests, logs or artifacts.

**Process boundary.** Unchanged from ADR-0041 and ADR-0042: the ADR-0038 non-secret OS, profile and temp allowlist and the fixed controls disabling prompts, update
notifications, telemetry, color and spinners, run in the host scratch directory; no ambient token, custom configuration, host, debug, proxy, pager, browser or `PATH`
override is forwarded and no credential is extracted; no output sinks, logs or artifacts; 15 seconds, 8 KiB per stream and 16 KiB combined; standard error is discarded
unread. The adapter is a singleton with one in-flight slot: contention returns nothing immediately (`Unknown`), with no queue, and the slot is released in a `finally` after
every outcome. Only the executable-start failures ADR-0041 already identifies (the `Win32Exception`, and the executor's `ArgumentException` for the executable parameter when
the vetted absolute path no longer exists) become `Unknown`; cancellation and every other fault propagate.

**Strict whole-response admission.** The JSON is admitted whole or not at all: exactly `data.repository` with exactly `id`, `name`, `nameWithOwner` and `ref`, with the
ADR-0041 name and ID rules and internal name coherence. `ref` must be an explicit `null` or exactly an object with `name`, `prefix` and a `target` object. GitHub's
`qualifiedName` lookup can fall back to short names, so the supplied qualified name alone proves nothing: the `prefix` must be exactly `refs/heads/`, the `name` ordinally
equal to the recorded branch, and the `target` exactly `{__typename: "Commit", oid: <40 lowercase hexadecimal characters>, statusCheckRollup}` where `statusCheckRollup` is
an explicit `null` or exactly `{state}` with one of `ERROR`, `EXPECTED`, `FAILURE`, `PENDING` or `SUCCESS`. Any backslash anywhere in the output (keys or values), errors,
extensions, partial data, missing, extra or duplicate fields at any level, unsupported or non-string states, invalid or foreign names or IDs, a wrong prefix or name
(including a tag or another branch returned by the fallback), a null or non-Commit target, an invalid OID, a timeout, a nonzero exit and an oversized or truncated stream are
refused whole as `Unknown`, never `RefNotReturned`, never a match. Literal whitespace, member order and explicit nulls are admitted as specified. A valid null ref means
only that GitHub returned no ref for this observation, and a valid null rollup only that it returned no rollup; neither infers nonexistence, a permission problem or the
absence of checks. The cited GraphQL reference (`Commit`, `StatusCheckRollup`, `StatusState`) defines the nullable rollup and the five states; real installed-CLI, stored
authentication and schema compatibility remain unverified.

**Cockpit.** Beside the ADR-0041 identity, ADR-0042 remote branch and ADR-0043 identity binding regions, inside an Available ADR-0040 target's result, a separate collapsed
"GitHub commit checks" region offers "Show GitHub commit checks" (which sends nothing), "Check GitHub commit checks" (then "Recheck") and "Close GitHub commit checks". It
shows the exact recorded branch, local commit, owner and repository and the fixed disclosure (GitHub's point-in-time aggregate for this commit does not establish complete
check or workflow evidence, required-check approval, permission or publication authority) before anything is checked, with no dependency on a sibling region's loaded binding
or earlier answer, and renders bounded facts as inert text: the bound repository ID, the observed repository facts, the returned ref and commit, the UTC observation time and
GitHub's literal state, never a link, copy or other action, never a translated word and no optimistic approval. A different commit, a ref not returned, an identity change and
any drift expose no check state. Ownership follows ADR-0041 and ADR-0042: the committed source and target and each opening own the region; another source or target, a
return to an earlier one, closing and reopening each start collapsed and unchecked; a late answer of an ended or superseded lifetime is ignored (including one that settles
while its successor is still pending); close is serial-guarded; a second activation in flight, two in one batch and any activation after a close in the same batch send
nothing; pending and failure remove any earlier observation. The whole answer is accepted only for exactly the observed source and target and only in the documented status,
nullability, ID, ref, commit, state and time combinations. There are no automatic reads, polling, reconnect work, retries, browser persistence, key, remount or reset-effect
workaround.

## Consequences

- An owner can see, on explicit request and without changing anything, what GitHub aggregates for the recorded commit of a repository whose identity was confirmed, as an
  input for later exact-SHA CI correlation.
- The answer is advisory and instantaneous: it can change immediately after it is read, never gates a claim, delivery or publication, and proves no required-check
  satisfaction, complete evidence, permission, publication approval or publication performed by DevalCopilot. A later slice must define any required-check policy,
  enumeration or monitoring.
- A rollup that is `SUCCESS`, `PENDING` or `FAILURE` is shown only for the recorded commit; a moved branch, a replaced repository or any lost binding yields no state.
- A fixture double proves the process boundary only; the installed CLI's real formatting of a GraphQL answer, stored-authentication compatibility and GitHub's reliability
  remain unverified, and an older or incompatible binary is `Unknown`. The observer never installs, upgrades or falls back to another command.
- Only the deterministic owned workspace branch form is supported; another branch name is never sent to a process.
- The contract is additive and changes no Domain, schema, migration, journal, admission, claim, scheduling, recovery, approval, budget or digest decision and adds no
  dependency, remote mutation, persistence, CI monitoring or publication. It does not complete Increment 6.
