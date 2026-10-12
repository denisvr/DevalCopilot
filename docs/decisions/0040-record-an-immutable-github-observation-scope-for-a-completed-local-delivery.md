# ADR-0040: Record an immutable GitHub observation scope for a completed local delivery

Status: Accepted

## Context

[ADR-0005](0005-use-git-worktrees-and-a-policy-controlled-github-loop.md) and the
[GitHub and CI integration](../architecture/github-ci-integration.md) design require an explicit repository, branch and commit correlation
before any exact-SHA CI observation or remote mutation. [ADR-0029](0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md)
and [ADR-0032](0032-show-a-recorded-local-delivery-receipt-from-pinned-evidence.md) already supply the exact completed local source and its owned
branch, [ADR-0038](0038-observe-stored-github-cli-authentication-on-explicit-request.md) supplies neither a target nor authority, and
[ADR-0039](0039-observe-the-project-local-github-origin-on-explicit-request.md) only shows what the project's own configuration names as `origin`. No
durable binding exists. This closes one input gap: it lets the Human record, once and immutably, which GitHub repository scope is meant for the
exact branch and commit of a completed manual local delivery. It is local scope selection only: never verified remote identity, permission, CI
success, publication approval or automatic observation authority. Remote identity verification, exact-SHA CI, scoped publication, reconciliation,
the remaining Increment 4 contracts and qualification, and Increment 5 coordination, concurrency and recovery stay separately gated, and real Agent
qualification is unaffected.

## Decision

**One immutable record.** `GitHubObservationTarget` (Domain) stores a host-generated id, the run, project, local-commit operation and workspace
identifiers, the exact 40-character lowercase commit, the recorded branch, the selected owner and repository, a UTC recording time and the identifier of
the one Human event saved with it. The host `github.com` and the purpose `CiObservation` are fixed project contract (constants, never columns or client
choices). There is no status that implies remote verification, authorization, push, pull request or CI success. At most one exists per run and per
operation (and per event): the table `github_observation_targets` has unique indexes on each, four restrictive foreign keys (run, project, operation,
workspace; nothing cascades into the record, so no deletion can silently remove it), and no update, replacement, deletion, backfill, override or cleanup
path. It is retained with the run history; existing runs stay unbound and unchanged. The event identifier is validated logically by every read and replay and is
not a database foreign key: `events.Id` is a unique index, not a key, and a principal key would require a migration that rebuilds the `events` table in
SQLite, which this slice deliberately does not do. The database therefore does not prevent a direct deletion or alteration of the event; the four
restrictive foreign keys protect the target's references, and a missing, foreign or altered event makes the whole reading `Unavailable` and refuses a
replay instead of being repaired. The owner and repository rules are the unchanged ADR-0039 conservative ASCII subset, now one Domain policy
(`GitHubRepositoryNamePolicy`) shared with the origin parser, whose outputs are unchanged and proven so by its existing tests.

**One protected write.** `POST /api/runs/{runId}/github-observation-target` (`RecordGitHubObservationTargetCommand` through `IApplicationMediator`,
one MVC endpoint, body at most 8 KiB) accepts only `operationId`, `commitSha`, `owner` and `repository`. The commit is exactly 40 lowercase hexadecimal
characters; names are used exactly as given (no trimming, decoding or normalization); there is no URL, host, branch or credential input (other members are
ignored and never used). Validation errors are fixed and never echo a rejected value; an unknown run is the fixed 404
`github_observation_target.not_found`; an unusable or mismatched source is the 409 `github_observation_target.unavailable`; a different immutable
selection is the 409 `github_observation_target.conflict`. The response repeats the run, the exact source `{operationId, commitSha, branchName}` and the
target `{id, eventId, host, owner, repository, recordedAtUtc}`.

**Source from pinned rows, one short transaction.** The handler owns a short manual transaction whose first statement is the existing no-op Run write that
takes the database write lock, then reads fresh and untracked: a ManualAgent run (the stored form classified as for every Agent path, so a malformed mode is
not admitted), an Available receipt for that run from the unchanged `LocalDeliveryReceiptReader` (never a nested query, never weakened admission), the pinned
operation and workspace rows, the workspace number and `WorkspaceBranchNamePolicy.Compute` agreeing with the recorded branch, and the submitted operation
and commit equal to the receipt's. The project, workspace and branch come only from those rows: never a client branch, the repository's current `HEAD` or
a "latest" record. This is a historical recorded source, not fresh workspace cleanliness or remote publication eligibility. It then inserts the target and
exactly one version-1 Human event `run.github_observation_target_recorded` in one save; the event's bounded canonical payload
(`{"version":1,"targetId","operationId","commitSha","branchName","host","owner","repository"}`, at most 1 KiB) is built from the target alone, so the writer
and every reader compare the same exact text. The notification is a post-commit hint only. The run's lifecycle, stage, participant, clocks,
`LastAdvancedAtUtc`, workspace, lease, approvals and budgets are never written: only the no-op lock, the target and the event belong to the operation.
Cancellation or a failed save rolls both inserts back; unexpected faults propagate.

**Idempotence.** The exact same coherent selection returns the original record, time and event without another insert; a different selection conflicts.
Concurrent requests serialize on the write lock to one target and one event. On this provider the production `BeginTransactionAsync`
already blocks an independent writer (`SQLITE_BUSY`) before the no-op update, which is verified by a test; the selected no-op Run write is kept as the
provider-independent, explicit lock statement rather than relied on as the only guard, and the unique indexes remain a separate backstop. A target, event or receipt that is corrupt, foreign or inconsistent with the
pinned source (identity, commit, branch, names, time, actor, attempt, payload, a second event of the type, a missing event) is never a successful replay.

**Storage admission.** Reads never materialize a stored value this version does not know. The actor kind, role, provider and attempt of the event are
classified in the database (only the exact Human shape with no attempt, role or provider is coherent), so an unrecognized stored value is an incoherent reading
and never an EF conversion fault, and the event payload and the target's commit, branch, owner and repository are bounded by their stored bytes
(`length(CAST(x AS BLOB))`) before they are materialized, because SQLite's text `length()` stops at an embedded NUL and `HasMaxLength` does not bound SQLite
storage. An oversized or invalid stored value makes the whole reading incoherent: nothing is defaulted, repaired or partially shown. Genuine faults and
cancellation still propagate.

**One protected read.** `GET /api/runs/{runId}/github-observation-target` is bodyless and reads persisted rows through bounded untracked projections only
(no write, lock, Git, process, network or provider). The closed `state` is `NotRecorded`, `Unavailable` or `Available`. `NotRecorded` has no target and
carries the source `{operationId, commitSha, branchName}` only when the receipt and the workspace branch are admissible; `Available` carries that source
and the coherent version-1 target, validated together with its one Human event; `Unavailable` carries neither and never says why. An unknown run is the same
fixed 404. Nothing is inferred from an origin or authentication observation, and nothing is defaulted, substituted or partially shown.

**Cockpit.** A collapsed region beside the live Completed local-commit region (not inside it; the commit and receipt regions stay read-only) offers "Show
GitHub observation target", "Load GitHub observation target" (then "Reload"), "Close GitHub observation target" and a separate "Record GitHub observation
target" action with blank owner and repository fields. It shows the exact branch and commit, the immutable selection to be recorded before submission, and
a fixed disclosure that recording is a local Human selection that is not verified remote identity, permission, CI success or publication authority and
cannot be changed afterwards. Nothing is read or recorded on mount, opening, a source change, a reconnect, a timer or a background refresh, nothing is
defaulted from the origin or authentication observations, and nothing links, copies or opens anything. The committed source (run, operation, commit,
branch) and each opening own the draft, reads and write; another source, a return to an earlier one, closing and reopening each start collapsed, blank
and unloaded; a late answer of an ended or superseded lifetime is ignored; a second activation in flight, two in one batch and any activation after a close
in the same batch send nothing. Every answer is accepted only whole and only for exactly this source, and an accepted recording must repeat the submitted
names. After any failed, refused or incoherent recording the outcome is unknown or refused, so no further recording is possible until an explicit reload
has read what the host recorded. A record callback is bound to the draft revision of the render that created it, and every edit advances that revision
synchronously, so a callback retained from an earlier draft (an owner or repository change, A to B to A, or an edit in the same batch) can never submit the
names it captured.

## Consequences

- An owner can bind, once and visibly, which GitHub repository scope later observation of an exact completed local delivery refers to, without any remote,
  credential, process or network operation.
- A recorded scope proves only that the Human selected it. A later slice must verify the remote identity and permission, correlate exact-SHA CI and define
  publication policy and reconciliation before any of it carries authority; a wrong or mistyped selection cannot be corrected by this operation.
- The read answers from the pinned, recorded source, not from the current repository, so a later change to the workspace or repository does not affect it.
- The contract is additive: no change to Domain write policies for existing entities, receipt or admission policy, claim, scheduling, recovery, approval,
  budget or digest decisions, and no dependency, scheduler, push, pull-request or CI adapter. The migration only creates one table; the `events` table is
  untouched.
