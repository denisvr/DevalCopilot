# ADR-0031: Abandon an inactive manual run through an explicit Human decision

Status: Accepted

## Context

[ADR-0014](0014-add-manual-agent-run-intake-with-durable-execution-mode-isolation.md) admits another objective for a project only
after every run of it has a recognized terminal lifecycle. Manual Agent failures, an exhausted claim budget or time budget, and a
human escalation do not end a run on their own (a claimed attempt that fails keeps its slot and the run stays `Running`), and a
successful explicit local commit ([ADR-0029](0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md)) is the only
production transition that completes a manual one. An owner therefore had no safe action to end a manual task that will not be delivered, and
could not record a different objective in that project. `RunLifecycle` had no value that said so, and reusing `Failed`,
`Interrupted` or `Completed` would have told a false story about what happened.

The existing `Run.Lifecycle` concurrency token already makes any claim that commits through a Run update lose to a committed
transition, so a bounded, metadata-only closure is possible without a process-cancellation, lease-release or Git-recovery mechanism.
This decision deliberately advances one explicit manual recovery control before the remaining Increment 4 provider contracts close,
as ADR-0029 did for local delivery. It neither declares Increment 4 or 5 complete nor weakens any provider requirement.

## Decision

**One explicit Human closure, `Abandoned`.** `POST /api/runs/{runId}/abandon` (`AbandonManualRunCommand`, protected like every
operation) accepts only a required human `reason` and a body of at most 8 KiB. The reason is trimmed with CRLF normalized to LF, must
be nonblank, well-formed text with no control, format or line/paragraph-separator character other than LF, and at most 2 KiB of UTF-8;
every character is classified by its Unicode scalar value, so a supplementary-plane Format character (for example U+E0001) is refused
exactly as a basic-plane one is and ordinary supplementary text is kept; the same text is stored and compared. The host decides the mode, lifecycle, project, times and participants. Only a run whose stored
mode is exactly `ManualAgent` and whose lifecycle is `Created` or `Running` can be abandoned; `Legacy`, `Simulated`, unrecognized
modes, every other lifecycle and every unrecognized stored value are refused with fixed codes and nothing is written.

**What it records, atomically.** `RunLifecycle.Abandoned = 5` is appended without renumbering any value. One short manual
transaction takes the SQLite write lock with its first statement, decides from fresh untracked reads, and saves, together: the run's
terminal transition (its `Stage` is preserved because abandonment is not objective completion or forward progress, its active
participant is cleared, time accumulated while `Running` is frozen up to the abandonment and time spent `Created` is never counted),
the immutable `AbandonmentReason` and `AbandonedAtUtc` (two nullable columns added by one focused migration with no default, no
backfill and no rewrite of any historical row), and exactly one `run.abandoned` event authored by the Human actor, carrying only the
reason. The Run that is changed is the one the locked database holds: if the context's change tracker already held an older copy of
that same Run, that one entity is reloaded under the lock, with its original concurrency values, before the transition, so a newer
stage, participant or lifecycle committed earlier is never overwritten, mis-clocked or left behind (the rest of the tracker is
untouched). No external work belongs in the transaction. A refusal records nothing; a failed or rolled-back save records nothing.

**What blocks it.** From the same fresh reads, the abandonment is refused while any attempt of any run of the project is not
`Completed`, `Failed` or `Interrupted` (a `Running` attempt, or a status this build does not recognize), any verification execution
of the project is not terminal or is unrecognized, any local-commit operation of the project is `Prepared`, `Executing`,
`NeedsAttention` or unrecognized, or any workspace of the project is `Preparing`, `Committing` or in an unrecognized state. A missing
workspace is valid for an untouched `Created` run. Every other known workspace state, its lease, its marker and its source trust are
retained exactly as they are: abandonment changes no workspace, lease, branch, index or file, and a workspace does not become `Ready`
because a run ended. It is never an override for an ambiguous local delivery: that state blocks it.

**Serialization with claims and admissions.** Duplicate abandonments and claims are serialized by the write lock and by
`Lifecycle`, which is an EF concurrency token. A Claude claim, a Codex claim of a `Created` run and the dispatch marker commit through
a Run update that includes the token, so an abandonment that commits first makes them lose. The three Codex claim paths that can claim
a `Running` run without changing a Run column (the planning claim and its repair, the challenge-resolution claim and its repair, and
the code-review claim and its repair) had no guard that read the lifecycle, so an abandonment that committed after such a claim decided
did not stop it from inserting an attempt on the ended run; this decision adds one fresh untracked lifecycle read inside their existing
short claim transaction, after their first guard write took the write lock, which refuses the claim and rolls it back with its sealed
manifest removed. The refusal keeps the code each handler already uses for a run that has ended: `runs.not_active` for the planning
claim and its repair (a `Created` or `Running` run is claimable there) and `runs.not_running` for the challenge-resolution and
code-review claims and their repairs (only a `Running` run is claimable there). The verification-diagnosis and diagnosis-correction claims already re-read the lifecycle there.
Local-commit admission re-reads the run as `Running` and manual in its locked seam, so an abandonment that wins refuses the admission
and its reservation rolls back, while an admission that wins leaves a `Committing` workspace and an open operation that the
abandonment refuses. A verification claim made after the closure remains project work under its existing rules; it neither needs nor
resumes the abandoned run. A prepared artifact that an admission never reached stays inert, as ADR-0029 already states; there is no
new janitor.

**Idempotence.** The same normalized reason against a coherently abandoned run returns the recorded reason and time without another
event or timestamp; a different reason conflicts (`run_abandonment.reason_conflict`). An abandoned run whose recorded facts are not
coherent is never treated as abandoned, answers neither a replay nor a conflict (`run_abandonment.abandonment_incoherent`) and blocks
intake.

**Coherence.** One rule, shared by intake, replay, the project summary and the read-only status, decides whether abandonment facts are
coherent: lifecycle `Abandoned`, exact `ManualAgent` mode, a canonical reason, a recorded time equal to the run's last advance, no
active participant, and exactly one Human-authored, run-scoped event carrying that same reason at that same time. Any other
combination, including a malformed stored participant or payload, is incoherent without throwing. The recorded time is stored and read
through a mapping specific to that one column: it writes exactly the text form the SQLite provider writes for the run's other
timestamps and reads back only that form, and only when SQLite stores it as TEXT: the provider's reader decodes a BLOB to text
before any converter could see it, so the column's own type mapping preserves the actual storage class at the read boundary and a
BLOB, even one holding the exact UTF-8 bytes of an admissible timestamp, is a damaged row, not a recorded time. A damaged or
hand-edited value (unparsable text, a number, a BLOB of any content, an out-of-range time) reads as no time at all, never as a
valid, default or approximate one, and is only read, never repaired: its storage class and bytes are left as they are by a refused
request and by an unrelated save of the same row. A missing time is never coherent, so that one run's
abandonment is incoherent (it answers no replay and permits no intake) while every other run, the project list and the run's own
cockpit keep loading. No other timestamp mapping, the column or the model snapshot changes.

**Normal intake.** `RunIntentRecorder` recognizes a coherent `Abandoned` run as terminal and nothing else changes: the other terminal
and unknown-state rules, the serialized number reservation and the conflict behavior are exactly as before. A new run is created only
by the ordinary operation, with its own identity, objective, execution number and fresh budgets (the defaults or the owner's explicit
choices, ADR-0028). It inherits no plan, grant, approval, message, artifact, reservation, consumed slot or decision of the abandoned
run, whose history is never changed; every claim reads only its own run's rows.

**Read-only status.** `GET /api/runs/{runId}/abandonment` (`GetManualRunAbandonmentQuery`) returns an advisory eligibility with a fixed
refusal code and, for a coherently abandoned run, the recorded reason and time. It writes nothing, invokes nothing and authorizes
nothing; the command decides again. The project run summary shows the `Abandoned` lifecycle and offers another objective only for
coherent facts.

**The cockpit.** A manual run's cockpit offers an "Abandon run" form only from a settled, successful status read of that run that is
eligible; a pending or failed read withholds it. It explains that abandonment is not a completion, that history, changes and evidence
remain, that nothing is cancelled, repaired or deleted, and that new work still needs normal checks. Its draft, request guard, error and
continuations belong to the committed run and eligible-form lifetime and the draft version (A-to-B-to-A, unmount, an obsolete
callback and an overlapping read never act on a replacement). A request that reached the host stays real: an unknown outcome (no
response or a 5xx) is reconciled only by reading the status and is never retried automatically, and a failed follow-up refresh never
turns a recorded abandonment into a reported failure. A recorded abandonment shows its persisted reason and time, never a success
label, and the Agent request actions are no longer offered for it. The generated client is the only transport.

**Migration.** `AddRunAbandonment` adds `AbandonmentReason` and `AbandonedAtUtc`, both nullable `TEXT`. Its `Down` drops them with native
`ALTER TABLE ... DROP COLUMN`: the provider's table-rebuild implementation of `DropColumn` fails while the local-commit guard trigger
on `attempts` references `runs`, whereas the native statement avoids EF's drop, recreate and rename of the whole table and leaves
every trigger and index unchanged. It is not free: SQLite still rewrites the table's content to remove a column, as its
[ALTER TABLE DROP COLUMN](https://www.sqlite.org/lang_altertable.html#alter_table_drop_column) contract states, and the reverse
migration is destructive for the two columns.

## Consequences

- An owner can end an exhausted, escalated or unwanted manual task, keep its complete history, and record a different objective in the
  same project, with all earlier evidence, sealed bytes, workspace and source unchanged.
- The explicit closure is not a cancellation, pause, lease release, workspace repair, lock adoption, deletion, commit recovery,
  remote publication, provider control, scheduler or automatically created replacement run. A run with active or ambiguous work must
  first reach a terminal state by the existing mechanisms; recovery of an `ambiguous` local delivery remains separately gated.
- A project verification execution or workspace flagged for attention elsewhere keeps its own rules; an abandoned run offers no
  further Agent request.
- An abandonment cannot be undone or edited: the reason and time are immutable, and a different reason conflicts.
- Not decided here: process cancellation, pause or resume, releasing a lease, repairing or recreating a workspace, deleting any
  branch, worktree, file or artifact, abandoning a `Legacy` or `Simulated` run, a bulk or automatic abandonment, a reopening or
  rewriting of an abandonment, and any change to the scheduler or the remote-publication gates. Each needs its own decision.
