# ADR-0048: Approve one future GitHub branch push of a bound recorded local commit on explicit request

Status: Accepted

## Context

[ADR-0005](0005-use-git-worktrees-and-a-policy-controlled-github-loop.md) and the [GitHub and CI integration](../architecture/github-ci-integration.md) design require a scoped Human
policy decision before any remote mutation, and the primary MVP journey requires a recorded Human publication approval before a push and draft pull request. The repository already
records the Human's local-commit approvals, the immutable [ADR-0040](0040-record-an-immutable-github-observation-scope-for-a-completed-local-delivery.md) selection of the owner and
repository for a completed local commit, and the immutable [ADR-0043](0043-bind-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md) binding of
that repository's opaque ID. None of them is a decision about publishing: the target is a local selection whose fixed purpose is `CiObservation`, the binding is historical identity
evidence, and the ADR-0044, ADR-0045 and ADR-0047 observations are advisory. This ADR closes that one local authority input and nothing more: it lets the Human record, once and
immutably, consent to ONE future normal push of the exact completed local commit to its exact recorded owned branch in the repository named by a coherent target and binding.

The record is consent only. It performs and checks nothing: no push, remote observation, permission check, process, Git operation, Agent dispatch, run transition or automatic work
exists in this slice, and it is never readiness, eligibility, a pushed commit, a pull request, a force push, a merge, a deletion, a rerun or automatic execution. A future executor
needs its own explicit request, its own review and fresh checks of local ownership and commit, the remote repository identity, the reference, authentication, permission and
ambiguity before anything is sent. Recording consent therefore does not require a matching remote commit, green CI, a usable CLI or any other observation: that would prevent
approving a first push. Remote permission discovery, CI monitoring and requiredness, diagnostic persistence and Agent routing, a scheduler or recovery framework, revocation, the
actual push and pull request, the remaining Increment 4 contracts and qualification, and Increment 5 coordination, concurrency and recovery stay separately gated, and real Agent
qualification is unaffected (Blocked/TargetsNotReal, both allowances unused). The ADR-0040 through ADR-0047 contracts, readers, ports, adapters, statuses and wire answers are unchanged.

## Decision

**One immutable record.** `GitHubBranchPublicationApproval` (Domain; table `github_branch_publication_approvals`) stores the caller-supplied approval identifier, a host-generated event
identifier, the complete exact source (run, project, local-commit operation, workspace, the 40-character lowercase commit and the owned branch), the exact target snapshot (identifier,
event identifier, the recorded owner and repository, UTC recording time), the exact binding snapshot (identifier, event identifier, the opaque node ID, the owner and repository the host
observed, UTC observation and recording times) and the UTC approval time. The fixed contract (host `github.com`, action `PushExactCommit`, policy version 1) is project constant, never a
column or a client choice, and the target names and the binding's observed names stay in separate exact snapshots (they may differ only in ASCII case). The branch is required to be
printable ASCII (the deterministic owned workspace branch is), so the canonical event payload always fits its bound. UTC ordering is an invariant: target recorded ≤ binding observed ≤
binding recorded ≤ approved; equality is valid and a backwards clock is never clamped. At most one approval exists per run, per operation, per target, per binding and per event (unique
indexes on each), with six restrictive foreign keys (run, project, operation, workspace, target, binding) so nothing cascades into the record. It is retained with the run history:
SQLite triggers (`BEFORE UPDATE` and `BEFORE DELETE`, aborting with `github_branch_publication_approval.immutable`) make the row immutable for every caller, and there is no replacement,
supersession, revocation, backfill or cleanup. One additive migration, `AddGitHubBranchPublicationApproval`, creates only this table, its indexes, keys and two triggers; the `events` table
is not rebuilt and no existing row or schema changes. The event identifier is logically validated, not a database key, as in ADR-0040 and ADR-0043.

**Two protected operations on one route.** `POST` and a bodyless `GET` of `/api/runs/{runId}/github-branch-publication-approval` (`RecordGitHubBranchPublicationApprovalCommand`, an
`IManualTransactionCommand`, and `GetGitHubBranchPublicationApprovalQuery`, both through `IApplicationMediator`, one MVC endpoint class per operation, operation-owned results and
API-owned DTOs for the whole transport graph; the generated client is regenerated reproducibly and additively and never edited by hand, and the three scope facts have their own
response types so no existing contract is moved or promoted). The POST body is exactly seven string members, spelled exactly:
`{approvalId, operationId, commitSha, branchName, targetId, bindingId, repositoryNodeId}`. The identifiers are nonempty 36-character D-format UUIDs (white space that .NET's parser would
tolerate is refused), the commit is 40 lowercase hexadecimal characters, the node ID follows the existing conservative policy, and the branch is nonempty and at most 512 characters; no
value is trimmed, normalized or compared ignoring case. A resource filter refuses before model binding and dispatch any query key, a body that is not one JSON object with exactly these
members each exactly once (unknown, duplicate, differently cased, **escaped** — a name written with a JSON escape is refused even when it decodes to an expected name — missing, mistyped,
nested or trailing content), and any body over 8 KiB, whether declared, chunked or undeclared: at most one byte more than the limit is ever read and the strict reader is itself bounded
to 8 KiB, with the action's request size limit as the HTTP-level bound. A non-JSON content type keeps the framework's standard 415. The client supplies no host, owner, repository, URL,
refspec, credential, message, action, text or timestamp. The GET refuses any query key, a positive declared length, any transfer encoding (including an empty chunked one) and a readable
byte before dispatch.

**Fixed, non-echoing codes under `github_branch_publication_approval`:** `invalid_request` (400), `body_not_allowed` (400, GET), `body_too_large` (413), `not_found` (404, an unknown
run), `unavailable` (409, POST) and `conflict` (409, POST). The 413 is built by the filter in the shared problem-details shape because the shared error type has no 413. No error repeats a
submitted, stored or remote value. Unexpected faults and caller cancellation propagate.

**Admission, replay and atomicity.** The scope is read only through the unchanged ADR-0040 source and target readers and the ADR-0043 binding reader (one feature-owned
`GitHubBranchPublicationScopeReader` composes them; the binding reader receives the coherent target only), over fresh untracked pinned rows: never an origin, a latest row, live Git or
any remote. An initial incoherent or incomplete scope, an incoherent approval or any acknowledgement that does not equal the scope refuses with `unavailable`. The handler then opens one
short transaction whose first statement is the existing no-op Run write (the SQLite driver begins its serializable transaction immediately, so the write lock is held from the
beginning of the transaction and every read below is under it), rereads the complete scope and approval, requires **every** source, target and binding value to equal the initial
reading (record equality over all three), and only then decides. A coherent existing approval with the same identifier and an identical acknowledgement replays its original row, event
and time; any other request for a run that already holds an approval conflicts, and so does an identifier that another run's approval already owns (checked under the lock). An approval
that was present at the initial reading and vanished or changed under the lock is `unavailable` and is never recreated; an incoherent record never replays. Concurrent identical requests
record once. A host clock earlier than the binding's recording is `unavailable`. The approval and exactly one canonical version-1 Human event are saved together; a failed or cancelled save
commits neither; the run, lifecycle, stage, clocks, workspace, lease, local approvals and budgets are never written, the ordinary journal sequence allocation and post-commit notification
are preserved (a notifier failure after the commit cannot undo the record), and no external call, filesystem work, retry or long transaction exists.

**Canonical event.** `run.github_branch_publication_approved`, version 1, authored by the Human with no attempt, role or provider, at the approval time, with a UTF-8 payload of at most 4 KiB
built from the approval alone: the version, the approval, run, project, operation and workspace identifiers, the commit, the branch, the fixed host, action and policy version, the complete
target snapshot, the complete binding snapshot and the approval time. The longest supported values (a 256-character node ID, the maximum names and branch, and the default JSON encoder's escaping of `+`) measured 3224 UTF-8 bytes with a temporary probe, which was deleted; the Domain tests assert the bound at those values.

**Bounded reader.** One untracked feature-owned reader selects the rows of the run and of any run that names this run's operation, target or binding, and the run's events of the type,
under stored-byte guards (`length(CAST(x AS BLOB))`, because SQLite's text `length()` stops at an embedded NUL), classifies the actor in the database without materializing a stored enum,
and withholds oversized or malformed values. An approval is coherent only when its entity invariants hold, every pinned value, cross-link and both snapshots equal the scope reading, the
run owns exactly one event of the type and it is the approval's own Human event with the same time and the exact canonical payload. Row and event absence are detected independently;
an orphan row or event, a record of another run, or any record without a whole coherent scope is incoherent, never absent, and never a success or a replay.

Every identifier and time the reader projects, from the approval row (`Id`, `EventId`, `RunId`, `ProjectId`, `LocalCommitOperationId`, `GitWorkspaceId`, `TargetId`, `TargetEventId`,
`BindingId`, `BindingEventId`, `TargetRecordedAtUtc`, `BindingObservedAtUtc`, `BindingRecordedAtUtc`, `ApprovedAtUtc`) and from the approval event (`Id`, `OccurredAtUtc`), is projected as its
raw stored bytes, bounded in the database by the supported width (36 bytes for an identifier, 33 for a time) and withheld above it, and is admitted from those bytes before any typed
value exists: an identifier only as the exact 36-character D-format text of a non-empty identifier (hex case is not significant, as for every other reader), and a time only in the
provider writer's `yyyy-MM-dd HH:mm:ss.FFFFFFFzzz` text (the entity still requires UTC and the order). Padding, braces, missing hyphens, another date and time separator, a blob, an
embedded NUL, a non-ASCII byte and an oversized value are not supported representations. Such a value is an incoherent reading: never an EF conversion fault, never defaulted, clamped,
normalized, repaired, disclosed or saved again. The correction is local to this reader: no global converter, existing reader, schema, wire shape or frontend changed, and no exception
handler was added around database access, so a real database fault and a cancellation still propagate.

The approval rows and the run's approval events are read as one consistent snapshot: they are the two bounded operands (at most three candidates each) of one `UNION ALL` statement through
the same EF/LINQ boundary, so a competing request's atomic commit of a row and its event is seen whole or not at all. Reading them with two statements was shown (Codex's snapshot probe, then
the deterministic tests of this ADR) to let a commit land between the statements, which made one valid pair read as an event without its row, `Incoherent`, and made an identical competing
request answer 409 `unavailable` instead of replaying. No lock, transaction, cache, retry, schema or converter was added and the handler is unchanged: it still takes the write lock at
transaction begin and rereads there. This establishes a torn-read path; it does not establish the cause of the one historical 409 whose response body was not captured.

**GET.** Returns `{runId, status, source, target, binding, approval}` with four closed statuses: `NotRecorded` (no eligible bound source and no approval evidence, every fact null),
`ApprovalNotRecorded` (a coherent bound scope and no approval row or event: source, target and binding present, approval null), `Available` (coherent scope and approval: all facts) and
`Unavailable` (incoherence, an orphan or drift: every fact null, never the reason). An unknown run is the fixed 404. The POST returns `{runId, source, target, binding, approval}` with 200
for a creation and for a replay. The approval projection is `{id, eventId, host, action, policyVersion, localCommitOperationId, workspaceId, targetId, bindingId, repositoryNodeId, owner,
repository, branchName, commitSha, approvedAtUtc}`; its owner and repository copy the target names, and the full binding snapshot is validated in storage though not duplicated there.
`Available` means recorded consent only, never ready, eligible or pushed.

**Cockpit.** Under a recorded target (and only when the committed project is known) a separate collapsed "GitHub branch publication approval" region offers Show (which sends nothing), Load
(then Reload), "Approve one future push" and Close. Before anything is recorded it shows the destination (`github.com / owner / repository`), the bound repository ID after the load, the full
`refs/heads/<branch>` and the commit, and a fixed statement that one future normal push is approved, no push occurs now, and a pull request, force-push, merge, deletion, rerun and automatic
execution are not authorized. The approval is enabled only by the current settled `ApprovalNotRecorded` load of the same opening; one request UUID is created by the first approval that
actually sends and is retained for the owned opening so a recovery retry after an explicit reload repeats it; one in-flight guard covers both actions and every action disarms the
approval synchronously (same-batch and retained callbacks are inert). Any refusal, failure or incoherent success clears the old reading to Not loaded and keeps a fixed alert until an
explicit reload; nothing retries, polls, uses browser storage, or relies on a key, remount or reset effect. The committed project, run, source and target, and each opening, own every
callback (close, a scope change and A to B to A end ownership, and a completion of an ended lifetime can neither settle into nor release the single-flight guard of its successor).
`Available` shows the recorded consent with no execution button. The history and summaries add only the closed event type and wording that separates recorded consent from executed
publication ("nothing was pushed").

## Consequences

- The Human can record, visibly and once, the exact consent that a later, separately reviewed executor must find before it may even consider a push; the record names the exact commit, branch,
  repository and node ID so later drift can be detected, and nothing in this slice contacts GitHub.
- Consent cannot be corrected or withdrawn by this operation (no replacement or revocation exists); any later withdrawal, expiry, scope change or a different consent needs its own decision.
- An approval proves what the Human consented to at that time, not that a push is possible, permitted, safe or even that the repository still exists; every future execution must re-check.
- The `CiObservation` purpose of the target is unchanged and the target and binding are not reinterpreted as approvals; existing runs stay unapproved and unchanged, and the migration only
  creates one table, its keys and its two triggers.
- The contract is additive and changes no Domain claim, scheduling, recovery, approval, budget or digest decision, adds no dependency, remote mutation, scheduler, provider setup or CI monitoring,
  and does not complete Increment 6. The Api 2067/2068 failure and every other recorded limitation stand.
