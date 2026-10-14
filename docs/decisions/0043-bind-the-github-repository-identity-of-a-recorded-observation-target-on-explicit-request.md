# ADR-0043: Bind the GitHub repository identity of a recorded observation target on explicit request

Status: Accepted

## Context

[ADR-0005](0005-use-git-worktrees-and-a-policy-controlled-github-loop.md) and the
[GitHub and CI integration](../architecture/github-ci-integration.md) design require a verified repository identity that later correlation can rely on.
[ADR-0040](0040-record-an-immutable-github-observation-scope-for-a-completed-local-delivery.md) records only the Human's local selection of an owner and repository
for an exact local commit and branch, [ADR-0041](0041-observe-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md) observes the
opaque repository node ID and names only as an ephemeral answer, and [ADR-0042](0042-observe-the-recorded-github-branch-tip-of-a-recorded-observation-target-on-explicit-request.md)
observes the branch tip ephemerally. Names can be renamed or transferred, so nothing durable yet records which remote repository (by its opaque ID) the Human
confirmed for a recorded target. This closes one input gap: it lets the Human record, once and immutably, the node ID that a fresh host observation reported for the
recorded owner and repository. It is historical identity evidence only: never permission, branch or commit proof, a CI result, publication approval or authority,
a project-wide alias for any other delivery, or a promise about the remote's future state. Exact-SHA CI, scoped publication, push, pull request, reconciliation,
the remaining Increment 4 contracts and qualification, and Increment 5 coordination, concurrency and recovery stay separately gated, and real Agent qualification
is unaffected. The ADR-0041 and ADR-0042 observations, their ports, adapters, statuses and wire answers are unchanged and stay ephemeral and ungated by this record.

## Decision

**One immutable record.** `GitHubRepositoryIdentityBinding` (Domain) stores a host-generated id, the identifier of the one Human event saved with it, the run,
project, local-commit operation, workspace and ADR-0040 target identifiers, the pinned 40-character lowercase commit and branch, the confirmed opaque
`RepositoryNodeId`, the observed owner and repository, the UTC observation time and the UTC recording time. The host `github.com` and the purpose `CiObservation` are
fixed project contract, never columns or client choices. At most one binding exists per target, per run, per operation and per event (unique indexes on each);
a node identifier is deliberately not unique across bindings. The table `github_repository_identity_bindings` has restrictive foreign keys to the target, run,
project, operation and workspace (nothing cascades into the record), is retained with the run history, and has no update, replacement, deletion, backfill or cleanup
path. UTC ordering is an invariant: target recorded ≤ observed ≤ binding recorded. One additive migration creates only this table, its indexes and foreign keys:
the `events` table is not rebuilt, no existing row or schema changes and nothing is backfilled. The event identifier remains logically validated, not a database key,
exactly as in ADR-0040.

**One protected write.** `POST /api/runs/{runId}/github-repository-identity-binding` (`BindGitHubRepositoryIdentityCommand`, an `IManualTransactionCommand`,
through `IApplicationMediator`; one MVC endpoint; API-owned request and response types for the whole graph; the generated client regenerated and never edited by hand)
accepts exactly `operationId`, `commitSha`, `targetId` and `repositoryNodeId`, bounded to 8 KiB. Unknown, duplicate, misspelled or mistyped members, a non-object
or malformed body are refused before dispatch by a resource filter with the fixed 400 `github_repository_identity_binding.invalid`; there is no owner, repository,
branch, URL, executable, credential or observation time input. Values outside the contract (a commit other than 40 lowercase hexadecimal characters, an empty
identifier, a node ID outside the conservative `_ - / + =` ASCII set of 1 to 256 characters) get the same fixed 400. No error ever echoes a rejected, stored or remote
value. The response repeats `{runId, source, target, binding}` with the binding facts `{id, eventId, repositoryNodeId, owner, repository, observedAtUtc, recordedAtUtc}`.

**Fixed errors.** 404 `…not_found` (unknown run); 400 `…invalid`; 409 `…unavailable` (the source, target or any binding or event is absent, incoherent or does not
match the submitted operation, commit and target); 409 `…conflict` (a coherent binding already holds another node ID); 409 `…cli_unavailable` (no current,
successful, direct-executable `GitHubCli` snapshot); 409 `…observation_unknown` (the observer answered nothing or an invalid or foreign answer); 409
`…identity_changed` (a valid observation reports another node ID than the request). A refusal inserts nothing.

**Scope, observation, then one short write-locked transaction.** The handler resolves the scope with the unchanged ADR-0040 `GitHubObservationSourceReader` and
`GitHubObservationTargetReader` over pinned rows (never the current `HEAD`, an origin, a latest record or a browser observation) and requires the submitted
operation, commit and target to equal it. A coherent same-identifier binding is replayed (below). For a missing binding it requires the same fresh `GitHubCli`
snapshot as ADR-0041 (no discovery, refresh or installation) and invokes the existing `IGitHubRepositoryIdentityObserver` exactly once with no transaction and no
write lock held, then validates the result independently: the unchanged node-ID and name policies, names equal to the target's ignoring only ASCII case, and a node ID
equal to the request ordinally. Only afterwards does it open one transaction whose first statement is the existing no-op Run write that takes the database write lock,
freshly rereads the source, target and binding and the event, requires the whole scope to be exactly the one observed, stamps the host UTC observation and recording
times (a second clock reading earlier than the first is refused as `…unavailable`, never clamped, retried or reobserved), records the exact names the observer reported (equal to the target's only ignoring ASCII case), and saves the binding and exactly one version-1 Human event `run.github_repository_identity_bound` (same recording time, no Agent attribution) atomically.
Nothing else is written: not the run, its lifecycle, stage, clocks, workspace, lease, approvals or budgets. A failed save or a cancellation before commit rolls both
inserts back; a post-commit notification is only a hint and its failure cannot undo the record (ADR-0003, ADR-0040). Concurrent requests may contend at the existing
observer slot, which refuses immediately, and cannot create more than one binding or event; there is no queue or automatic retry, and no external call or retry runs
inside a transaction.

**Replay.** A coherent binding with the same node identifier returns its original record, event and times without another observation or insert, after fresh coherent
reads under the short write lock; it needs no CLI snapshot. A different identifier conflicts. An incoherent record never replays and is never replaced.

**Canonical event.** The payload is canonical UTF-8 text of at most 4 KiB, built from the binding alone:
`{"version":1,"bindingId","targetId","operationId","commitSha","branchName","host","repositoryNodeId","owner","repository","observedAtUtc"}`. A planner sizing probe of the
largest supported values (a 256-character node ID, the maximum names, branch and time) measured 2100 UTF-8 bytes, so the first 2 KiB bound was insufficient and is
4 KiB. The reader and every replay rebuild and compare the exact text, the Human actor shape, the times and the pinned identities.

**Storage admission.** Reads use bounded untracked projections. The actor kind, role, provider and attempt are classified in the database (only the exact Human shape
is coherent, so an unknown stored value is an incoherent reading, never an EF conversion fault), and the payload and every stored text (commit, branch, node ID,
owner, repository) are bounded by their stored bytes (`length(CAST(x AS BLOB))`) before they are materialized, because SQLite's text `length()` stops at an embedded
NUL. Missing, duplicate, foreign or contradictory rows or events, an oversized value, a time out of order, and an orphan binding or event (no coherent target) are
incoherent as a whole: nothing is repaired, defaulted or partially shown. Genuine faults and cancellation propagate.

**One protected read.** `GET /api/runs/{runId}/github-repository-identity-binding` is bodyless and reads persisted evidence only (no lock, write, process or network).
A positive declared length, any transfer encoding (including an empty chunked one) or a readable byte is refused before dispatch with the fixed 400
`github_repository_identity_binding.body_not_allowed`. It returns `{runId, state, source, target, binding}` with the closed states `NotRecorded` (no binding; the
source and target pair only when the target is coherent), `Unavailable` (all three null, including for orphan bindings or events, which are never `NotRecorded`) and
`Available` (the exact coherent source, target and binding). A recorded binding never implies a current remote verification.

**Reuse.** Only the existing identity port `IGitHubRepositoryIdentityObserver` and its result `GitHubRepositoryIdentity` are promoted, unchanged, to the Application
`Features/Runs/Ports` folder, and the opaque-ID policy `GitHubRepositoryNodeIdPolicy` unchanged to Domain's `Features/Projects` policy area beside the name policy,
because ADR-0041, ADR-0042 and this decision genuinely change together. References and architecture expectations are updated and the existing tests prove the
ADR-0041 and ADR-0042 behavior unchanged. There is no new observer, broad gateway or shared orchestration.

**Cockpit.** Beside the identity and branch regions inside an Available target's result, a separate collapsed region offers "Show GitHub identity binding",
"Load GitHub identity binding" (then "Reload"), "Check candidate identity" (then "Recheck", through ADR-0041), "Bind this repository identity" and "Close GitHub
identity binding". Binding is armed only by a settled, coherent `NotRecorded` read with the exact source and target and an `Observed` candidate of the same
opening, and before submission the region shows the exact source, target, node ID, names and a fixed immutable and historical disclosure. The POST observes again;
the browser supplies no proof or timestamp, defaults nothing from another panel, an origin or storage, and uses no automatic read, write, retry, polling or
remount effect. One in-flight guard covers all actions. The committed scope, each opening and the candidate revision own callbacks and results: a reload or recheck
disarms older callbacks synchronously (including A to B to A, same-batch and same-millisecond cases), and a closed, superseded or reopened lifetime cannot act on its
successor. After any failed, refused or ambiguous write the read and the candidate are cleared, so an explicit reload and a new candidate check must come before
another write, in the order reload, then a new settled candidate check, then bind (a candidate checked before the recovery reload is discarded). Values render as inert text with UTC times; recorded evidence is not presented as a fresh check.

## Consequences

- The owner can bind, once and visibly, the repository's opaque ID that GitHub reported for the recorded owner and repository, so later correlation can detect a
  rename, transfer or replacement instead of trusting names alone, without any remote mutation.
- A binding proves only what was confirmed at that time. A wrong binding cannot be corrected by this operation; verification of permission, exact-SHA CI, scoped
  publication and reconciliation are still separate, separately gated slices.
- Ephemeral ADR-0041 and ADR-0042 observations stay available and are not gated by the record. Existing runs stay unbound and unchanged; the migration only
  creates one table and the `events` table is untouched.
- The contract is additive: no change to claim, scheduling, recovery, approval, budget or digest decisions, no remote mutation, scheduler, provider setup or new
  dependency, and the Api 2067/2068 failure and every other recorded limitation stand.
