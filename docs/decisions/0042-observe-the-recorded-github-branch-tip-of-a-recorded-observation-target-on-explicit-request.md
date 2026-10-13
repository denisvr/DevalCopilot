# ADR-0042: Observe the recorded GitHub branch tip of a recorded observation target on explicit request

Status: Accepted

## Context

[ADR-0005](0005-use-git-worktrees-and-a-policy-controlled-github-loop.md) and the
[GitHub and CI integration](../architecture/github-ci-integration.md) design require a verified repository identity and an exact remote head before any exact-SHA CI
observation or remote mutation. [ADR-0040](0040-record-an-immutable-github-observation-scope-for-a-completed-local-delivery.md) records only the Human's local
selection of an owner and repository for an exact local commit and branch, and
[ADR-0041](0041-observe-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md) observes only the repository's opaque ID and names.
Nothing yet asks GitHub which commit the recorded branch points at. This closes one input gap: an explicit, read-only observation, in one remote response, of the
repository identity and the exact recorded branch tip, and whether that tip equals the completed local delivery's recorded commit. It is ephemeral advisory evidence,
not a durable binding, a CI result, a permission, a publication approval or a publication performed by DevalCopilot, and it persists nothing. Exact-SHA CI,
scoped publication, push, pull request, reconciliation, the remaining Increment 4 contracts and qualification, and Increment 5 coordination, concurrency and recovery
stay separately gated, and real Agent qualification is unaffected.

## Decision

**One protected, bodyless, read-only observation.** `GET /api/runs/{runId}/github-branch-observation` (`GetGitHubBranchObservationQuery` through
`IApplicationMediator`, one MVC endpoint under the protected runs route, API-owned response types for the whole transport graph, the generated client regenerated
reproducibly and never edited by hand) returns the `runId`, a closed `status` (`NotRecorded`, `Unavailable`, `CliUnavailable`, `Unknown`, `RefNotReturned`,
`CommitMatched`, `CommitDifferent`), a nullable `source` `{operationId, commitSha, branchName}`, a nullable `target`
`{id, eventId, host, owner, repository, recordedAtUtc}` and a nullable `observation`
`{repositoryNodeId, owner, repository, observedAtUtc, ref: null | {branchName, commitSha}}`. An unknown run is the fixed 404 `github_branch_observation.not_found`.
The route accepts no client repository, node ID, branch, SHA, executable or credential, and any request body (a positive declared length, a transfer encoding,
including an empty chunked one, or a readable byte) is refused before dispatch with the fixed 400 `github_branch_observation.body_not_allowed`, which never echoes
it. It writes no row, event or lock, holds no transaction across external work and changes no schema; a database fault keeps the normal pipeline.

**Statuses and their carried facts.** `NotRecorded` and `Unavailable` carry neither scope nor observation and invoke nothing. Every other status carries the exact
coherent source and target. Only `RefNotReturned`, `CommitMatched` and `CommitDifferent` carry an observation, and `ref` is null exactly for `RefNotReturned`; the
other two contain an admitted exact branch name and a remote 40-character lowercase hexadecimal SHA. The handler compares the remote SHA with the recorded local
SHA with ordinal equality alone: equal is `CommitMatched`, any other admitted SHA is `CommitDifferent`.

**Scope from the unchanged ADR-0040 readers.** The handler reads the exact source and target with the existing `GitHubObservationSourceReader` and
`GitHubObservationTargetReader`, untracked, before and after the external call. No recorded target is `NotRecorded` and an incoherent one is `Unavailable`. A scope
that is lost, changed or incoherent after the call is `Unavailable` with no remote facts, whatever the observer said.

**Admission before any process, no discovery.** The handler requires the same fresh, successful `GitHubCli` `DirectExecutable` snapshot as ADR-0038 and ADR-0041
(no script, a nonblank absolute executable, an observed version and time, fresh under `HostCapabilityReadinessProjector.IsStale`); anything else is
`CliUnavailable` with nothing invoked. It never discovers, refreshes or installs a tool, and has no authentication-status, origin or prior identity prerequisite.

**Independent validation, then the host clock.** The port's result is validated again in the handler: an opaque node ID of 1 to 256 ASCII letters, digits or
`_ - / + =` (`GitHubRepositoryNodeIdPolicy`, unchanged and never decoded), an owner and repository inside the unchanged conservative name policy and equal to the
recorded scope ordinally, ignoring only ASCII case (a rename, transfer, trimmed or `.git`-stripped name is never adopted), and a ref that is either absent or has
exactly the recorded owned branch name and a well-formed commit SHA. Any other result is `Unknown`. Only an admitted observation is stamped with the host clock.
Cancellation and unexpected faults propagate.

**Port and adapter.** Application owns the provider-neutral `IGitHubBranchObserver`, operation-owned, returning only `{NodeId, Owner, Repository, Ref}` or nothing.
Infrastructure owns the GitHub CLI protocol in `GitHubBranchObservationAdapter`, composed on the existing `IProcessExecutionAdapter` inside the host scratch
directory; the ADR-0041 repository identity observer and adapter are unchanged. It validates the supported names and the bounded deterministic owned branch
(`devalcopilot/workspace/<32 lowercase hexadecimal project digits>/<decimal workspace number from 1 to 2147483647 without a sign or leading zero>`, the form the
Domain `WorkspaceBranchNamePolicy` computes; `GitHubOwnedBranchNamePolicy`) before invoking, and refuses any other branch without a process. It runs exactly one
invocation with separate literal arguments:

`gh api graphql --hostname github.com --method POST -f query=<constant> -f owner=<stored owner> -f name=<stored repository> -f ref=refs/heads/<recorded branch>`

with the one fixed read-only document
`query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner ref(qualifiedName:$ref){name prefix target{__typename oid}}}}`.
The HTTP `POST` only transports the query. The stored names and branch travel only as `-f` raw-string variables (never `-F`) and are never interpolated. There is
no mutation, shell, jq, pagination, cache, retry, fallback or second invocation.

**Process boundary.** Unchanged from ADR-0041: the ADR-0038 non-secret OS, profile and temp allowlist and the fixed controls disabling prompts, update
notifications, telemetry, color and spinners, run in the host scratch directory; no ambient token, custom configuration, host, debug, proxy, pager, browser or `PATH`
override is forwarded and no credential is extracted; no output sinks, logs or artifacts; 15 seconds, 8 KiB per stream and 16 KiB combined; standard error is
discarded unread. The adapter is a singleton with one in-flight slot: contention returns nothing immediately (`Unknown`), with no queue, and the slot is released in
a `finally` after every outcome. Only the executable-start failures ADR-0041 already identifies (the `Win32Exception`, and the executor's `ArgumentException` for
the executable parameter when the vetted absolute path no longer exists) become `Unknown`; cancellation and every other fault propagate.

**Strict whole-response admission.** The JSON is admitted whole or not at all: exactly `data.repository` with exactly `id`, `name`, `nameWithOwner` and `ref`,
with the ADR-0041 name and ID rules and internal name coherence. `ref` must be an explicit `null` or exactly an object with `name`, `prefix` and a `target` object.
GitHub's `qualifiedName` lookup can fall back to short names, so the supplied qualified name alone proves nothing: the `prefix` must be exactly `refs/heads/`, the
`name` ordinally equal to the recorded branch, and the `target` exactly `{__typename: "Commit", oid: <40 lowercase hexadecimal characters>}`. Errors, extensions,
partial data, missing, extra or duplicate fields at any level, escaped strings, malformed or trailing data, invalid or foreign names or IDs, a wrong prefix or name
(including a tag or another branch returned by the fallback), a null or non-Commit target, an invalid OID, a timeout, a nonzero exit and an oversized or truncated
stream are `Unknown`, never `RefNotReturned` and never a match. A valid null ref means only that GitHub returned no ref for this observation; it does not infer
nonexistence or a permission problem.

**Cockpit.** Beside the ADR-0041 identity region, inside an Available ADR-0040 target's result, a separate collapsed remote branch region offers "Show GitHub remote
branch", "Check GitHub remote branch" (then "Recheck") and "Close GitHub remote branch". It shows the exact recorded branch, local commit, owner and repository
before anything is checked, with no dependency on an earlier identity check, and renders bounded facts as inert text with the UTC observation time, distinguishing
reference not returned, a different commit and a matching commit, and stating that a match is only what GitHub reported at that time, not CI, a permission, a
durable binding, a publication approval, a publication performed by DevalCopilot or future stability. Ownership follows ADR-0041: the committed source and target and
each opening own the region; another source or target, a return to an earlier one, closing and reopening each start collapsed and unchecked; a late answer of an
ended or superseded lifetime is ignored (including one that settles while its successor is still pending); close is serial-guarded; a second activation in flight,
two in one batch and any activation after a close in the same batch send nothing. The whole answer is accepted only for exactly the observed source and target,
with the ref's branch equal to the recorded one and its commit equal to, respectively different from, the recorded commit for the corresponding status. There are
no automatic reads, polling, reconnect work, retries, browser persistence, key, remount or reset-effect workaround.

## Consequences

- An owner can see, on explicit request and without changing anything, what GitHub reports for the recorded repository and branch and whether its tip equals the
  recorded local commit, as an input for a later verified binding.
- The answer is advisory and instantaneous: it can change immediately after it is read, never gates a claim, delivery or publication, and proves no CI result,
  permission, durable binding, publication approval or publication performed by DevalCopilot. A later slice must define and persist any binding.
- A fixture double proves the process boundary only; the installed CLI's real formatting of a GraphQL answer, stored-authentication compatibility and GitHub's
  reliability remain unverified, and an older or incompatible binary is `Unknown`. The observer never installs, upgrades or falls back to another command.
- Only the deterministic owned workspace branch form is supported; another branch name is never sent to a process. A repository whose answer names another
  repository, a fallback ref, an escaped or ambiguous shape is `Unknown`.
- The contract is additive and changes no Domain, schema, migration, journal, admission, claim, scheduling, recovery, approval, budget or digest decision and adds no
  dependency, remote mutation, CI monitoring or durable remote binding. It does not complete Increment 6.
