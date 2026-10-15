# ADR-0049: Observe the GitHub repository permission of a bound recorded target on explicit request

Status: Accepted

## Context

[ADR-0005](0005-use-git-worktrees-and-a-policy-controlled-github-loop.md) and the
[GitHub and CI integration](../architecture/github-ci-integration.md) design require remote permission evidence before any later publication decision.
[ADR-0040](0040-record-an-immutable-github-observation-scope-for-a-completed-local-delivery.md) records the exact local commit, branch and Human-selected owner and
repository, [ADR-0043](0043-bind-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md) records the first durable remote fact (the immutable
opaque repository ID the Human confirmed), [ADR-0041](0041-observe-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md) and
[ADR-0044](0044-observe-the-check-summary-of-a-bound-recorded-local-commit-on-explicit-request.md) define the explicit, read-only observation shape and the
`GitHubCli` snapshot gate, and [ADR-0048](0048-approve-one-future-github-branch-push-of-a-bound-recorded-local-commit-on-explicit-request.md) records the local consent that a future
push needs. No operation yet reports what access GitHub says the stored default authentication has on the bound repository. This closes one input gap: on explicit request,
show the repository permission GitHub reports (`Repository.viewerPermission`) for the already-bound recorded repository, but only when the freshly observed repository ID equals
the immutable binding.

It is ephemeral reported access and nothing more: not permission to push the recorded branch (branch protection, rulesets, required reviews, token scopes, organization policy and
fork or archive state are all outside it), not readiness, not an approval, not a publication approval and not a publication performed by DevalCopilot. No ADR-0048 approval is
required, consumed or changed, and the approval does not depend on this observation. The actual push and pull request, ruleset and permission administration, durable permission
storage, CI routing and monitoring, revocation, scheduling and restart reconciliation, the remaining Increment 4 contracts and qualification, and Increment 5 coordination stay
separately gated, and real Agent qualification is unaffected (it remains `Blocked`/`TargetsNotReal`, both allowances unused). The ADR-0040 through ADR-0048 contracts, readers,
ports, adapters, statuses and wire answers are unchanged.

**Schema evidence.** The permission enumeration was verified against GitHub's own published schema, not recalled: `https://docs.github.com/public/fpt/schema.docs.graphql`
(read 2026-10-15) defines `enum RepositoryPermission` with exactly six values, `ADMIN`, `MAINTAIN`, `READ`, `TRIAGE`, `TRIAGE_PLUS` and `WRITE`, and `Repository.viewerPermission`
as the nullable `RepositoryPermission` ("will return null if authenticated as an GitHub App"); GitHub's GraphQL changelog for 2026 lists `TRIAGE_PLUS` as added on 2026-09-21. A
third-party mirror of the schema (`octokit/graphql-schema`) was read in the same session and still showed five values, so an older copy of the enum is not authority. All six are
admitted as reported facts; nothing assumes that the set is complete, and any other value is refused as `Unknown`. The installed CLI's real behavior, stored authentication
compatibility and GitHub's reliability remain unverified.

## Decision

**One protected, bodyless, read-only observation.** `GET /api/runs/{runId}/github-repository-permission-observation` (`GetGitHubRepositoryPermissionObservationQuery` through
`IApplicationMediator`, one MVC endpoint under the protected runs route, API-owned response types for the whole transport graph, the generated client regenerated reproducibly and
never edited by hand) returns `{runId, status, source, target, binding, observation}`. The `source` `{operationId, commitSha, branchName}`, `target`
`{id, eventId, host, owner, repository, recordedAtUtc}` and `binding` `{id, eventId, repositoryNodeId, owner, repository, observedAtUtc, recordedAtUtc}` are the existing fact
shapes, mapped from Application views. The `observation` is
`{repositoryNodeId, owner, repository, observedAtUtc, viewerPermission: null | "READ" | "TRIAGE" | "TRIAGE_PLUS" | "WRITE" | "MAINTAIN" | "ADMIN"}`. The fixed non-echoing errors
are the 404 `github_repository_permission_observation.not_found` (unknown run), the 400 `github_repository_permission_observation.invalid_request` (any query key, however spelled or
repeated) and the 400 `github_repository_permission_observation.body_not_allowed` (a positive declared length, any transfer encoding including an empty chunked one, or a readable
body byte). A resource filter refuses a query or a body before model binding and dispatch (the query first), so no delivery, database read or process is involved and nothing a
caller sent, stored or remote is repeated. The route writes no row, event or lock, holds no transaction across external work, persists nothing and changes no schema.

**Closed statuses and nullability.**

| Status | source, target | binding | observation | Invoked |
|---|---|---|---|---|
| `NotRecorded` (no target and no binding or event) | absent | absent | absent | nothing |
| `Unavailable` (any source, target, binding or event is incoherent or orphaned, or the whole scope changed during the observation) | absent | absent | absent | nothing on the initial refusal |
| `BindingNotRecorded` (coherent source and target; genuinely no binding row and no event) | present | absent | absent | nothing |
| `CliUnavailable`, `Unknown` | present | present | absent | nothing for the first |
| `IdentityChanged` (a whole valid answer whose opaque ID differs ordinally from the binding; the other ID and every other fact of that answer are never returned) | present | present | absent | one observation |
| `PermissionNotReturned` (bound ID, an explicit null permission) | present | present | present, `viewerPermission` null | one observation |
| `PermissionReported` (bound ID, one of the six literals) | present | present | present, `viewerPermission` exactly that literal | one observation |

`viewerPermission` is null exactly for `PermissionNotReturned`. A null permission is neither a denial nor proof of any account or token type, and an inaccessible or null
repository is `Unknown`, never `PermissionNotReturned`. The six literals are reported facts: nothing ranks, orders, translates, compares or collapses them, and no status, field or
text such as `canPush`, `allowed` or `ready` is derived. The names in an observation are the observed names (equal to the recorded ones only ignoring ASCII case).

**Scope and binding from the unchanged readers.** The handler reads the source, target and binding with the existing `GitHubObservationSourceReader`,
`GitHubObservationTargetReader` and `GitHubRepositoryIdentityBindingReader` before any process, with the ADR-0044 semantics: missing evidence is distinct from incoherence, a binding
or its event without a coherent target is `Unavailable`, never `NotRecorded` or `BindingNotRecorded`.

**Admission before any process, no discovery.** The handler requires the same fresh, successful, direct-executable `GitHubCli` snapshot as ADR-0038, ADR-0041 and ADR-0044 (no
script, a nonblank absolute executable, an observed version and time, fresh under `HostCapabilityReadinessProjector.IsStale`); anything else is `CliUnavailable` with nothing
invoked. It never discovers, refreshes or installs a tool and uses no answer any other region displayed: the host reads the binding itself on every request.

**Observe, read again, validate, then compare.** The handler invokes the operation-owned, provider-neutral `IGitHubRepositoryPermissionObserver` exactly once with no transaction
and no write lock held. After it returns it reads the whole scope (source, target, binding and their events) again and compares the two readings by complete value; any lost,
changed, replaced (even by a different coherent binding and event with new identifiers) or incoherent fact makes the answer `Unavailable` with every fact stripped, whatever the
observer said. It then validates the entire probe independently (`GitHubRepositoryPermissionPolicy`): the conservative opaque ID (`GitHubRepositoryNodeIdPolicy`, unchanged, never
decoded), an owner and repository inside the unchanged name policy and equal to the recorded scope ignoring only ASCII case (a rename, transfer, trimmed or `.git`-stripped name is
never adopted), and a permission that is absent or exactly one of the six case-sensitive literals. A malformed answer is `Unknown`, never a partially admitted identity change. Only
then is the node ID compared with the binding ordinally (`IdentityChanged` on a mismatch). The observation is stamped from the host UTC clock after the revalidation; a reading
earlier than `binding.RecordedAtUtc` is `Unknown` without an observation, never clamped, retried, reobserved or fabricated. Cancellation and unexpected faults propagate.

**Port and adapter.** Application owns `IGitHubRepositoryPermissionObserver`, returning only `{NodeId, Owner, Repository, ViewerPermission}` or nothing. Infrastructure owns the
GitHub CLI protocol in `GitHubRepositoryPermissionObservationAdapter`, composed on the existing `IProcessExecutionAdapter` inside the host scratch directory; the older adapters are
unchanged and no generic observation framework is extracted. It validates the supported names before invoking, refuses anything else without a process, and runs exactly one
invocation with separate literal arguments:

`gh api graphql --hostname github.com --method POST -f query=<constant> -f owner=<stored owner> -f name=<stored repository>`

with the one fixed read-only document
`query($owner:String!,$name:String!){repository(owner:$owner,name:$name){id name nameWithOwner viewerPermission}}`. The HTTP `POST` only transports the query. The stored names
travel only as `-f` raw-string variables (never `-F`) and are never interpolated. There is no mutation, shell, jq, discovery, authentication refresh, credential read, pagination,
cache, retry, fallback or second invocation, and the query selects no collaborator list, branch protection, ruleset, `viewerCan*` field, ref or anything else.

**Process boundary.** Unchanged from ADR-0041: the ADR-0038 non-secret OS, profile and temp allowlist and the fixed controls disabling prompts, update notifications, telemetry,
color and spinners, run in the host scratch directory; no ambient token, custom configuration, host, debug, proxy, pager, browser or `PATH` override is forwarded and no credential
is extracted; no output sinks; 15 seconds, 8 KiB per stream and 16 KiB combined; JSON depth 8; standard error is discarded unread. The adapter is a singleton with one in-flight
slot: contention returns nothing immediately (`Unknown`), with no queue, and the slot is released in a `finally` after every outcome. Only the executable-start failures ADR-0041
already identifies become `Unknown`; cancellation and every other fault propagate.

**Strict whole-response admission.** The JSON is admitted whole or not at all: exactly `data.repository` with exactly `id`, `name`, `nameWithOwner` and `viewerPermission`, with the
ADR-0041 name and ID rules and internal name coherence, and `viewerPermission` an explicit `null` or a string equal to one of the six literals. Any backslash anywhere in the
output (so no escaped key or value is ever decoded into an admitted one), errors, extensions, partial data, missing, extra or duplicate members at any level, a non-string or
unknown permission, invalid or foreign names or IDs, a timeout, a nonzero exit and an oversized or truncated stream are refused whole as `Unknown`. Literal whitespace, member order
and an explicit null are admitted as specified.

**Cockpit.** Beside the ADR-0041 identity, ADR-0042 remote branch and ADR-0043 identity binding regions, inside an Available ADR-0040 target's result, a separate collapsed "GitHub
repository permission" region (its own strict decoder, hook and panel) offers "Show GitHub repository permission" (which sends nothing), "Check GitHub repository permission" (then
"Recheck") and "Close GitHub repository permission". It shows the recorded owner and repository and the fixed disclosure "Repository access does not establish permission to push this
branch." before anything is checked, and renders the exact admitted role, the bound and returned identity and the host observation time as inert plain text, never a link, copy,
execution control, HTML or Markdown, and no approval or other control is gated on it. An identity change and any drift expose no role. Ownership follows ADR-0041 and ADR-0044: the
committed project, run, source and target and each opening own the region; another scope, a return to an earlier one (A-B-A), closing and reopening each start collapsed and
unchecked; a late answer of an ended or superseded lifetime is ignored (including one that settles while its successor is pending); close is serial-guarded; a second activation in
flight, two in one batch and any activation after a close in the same batch send nothing; pending and failure remove any earlier answer. The whole answer is accepted only for
exactly the observed source and target and only in the documented status, nullability, ID, literal and time combinations. There are no automatic reads, polling, reconnect work,
retries or browser storage.

**Composition and test infrastructure.** The observer is registered normally as a singleton. Only its port is added to the sealed-scene guard's refusal inventory in the test
infrastructure (the guard mechanism, producers and defaults are unchanged), so both existing sealed clone compositions resolve a named refusing permission observer, start no
non-Git process and keep their refusal queues isolated.

## Consequences

- An owner can see, on explicit request and without changing anything, what repository access GitHub reports for the stored default authentication on a repository whose
  identity was confirmed, as one input for a later publication-readiness decision.
- The answer is advisory and instantaneous: it can change immediately, never gates a claim, approval, delivery or publication, and proves no permission to push the branch,
  readiness, approval or publication performed by DevalCopilot. A later slice must define how any permission evidence is combined with the other checks a push needs.
- A role is shown only for the bound repository; a replaced repository, a moved scope or any lost binding yields none.
- A fixture double proves the process boundary only; the installed CLI's real formatting of a GraphQL answer, stored-authentication compatibility, GitHub's reliability and any
  further enumeration values remain unverified, and an older or incompatible binary or an unknown value is `Unknown`. The observer never installs, upgrades or falls back to another
  command.
- The contract is additive and changes no Domain, schema, migration, journal, admission, claim, scheduling, recovery, approval, budget or digest decision and adds no dependency,
  remote mutation, persistence, CI monitoring or publication. It does not complete Increment 6.
