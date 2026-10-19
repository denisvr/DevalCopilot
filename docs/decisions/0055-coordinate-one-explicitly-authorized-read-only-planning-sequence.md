# ADR-0055: Coordinate one explicitly authorized read-only planning sequence

Status: Accepted

## Context

The local collaboration stages execute real, durably claimed attempts, but a
human requests each stage separately. The three planning supervisors consume
claims; they neither select the next input nor authorize the next claim.
Historical inspection does not close that coordination gap.

ADR-0014 deliberately makes intake inert and every subsequent stage explicit.
ADR-0009 also puts completion of Increment 4 before the full local delivery
loop. A coordinator cannot silently reinterpret either decision as standing
permission to advance a manual run. Existing source, budget, dispatch and
recovery controls remain necessary even for read-only stages.

The existing protocol already has a complete bounded planning lineage: a
Planner root, a CriticalReviewer acceptance or challenges, a Resolver revision,
one further critical review, and at most one further resolution. Depth two
requires the existing Human decision. These are real message and attempt
identities, not a workflow inferred from display text or the newest page.

## Decision

### Narrow authority and sequencing extension

PLAN-COORD-01 delivers that complete read-only lineage as one explicitly
authorized planning sequence. It narrowly supersedes ADR-0014's per-stage
request requirement **only for the steps belonging to this recorded sequence**.
`ManualAgent` remains immutable; intake still starts nothing. Legacy, Simulated,
unrecognized modes and all runs without this authorization keep their behavior.
There is no new execution mode or migration of historical authority.

It narrowly extends ADR-0009's ordering to permit this read-only coordination
before the remaining provider controls are closed. It does not authorize the
full Increment 5 loop early. ADR-0006's concurrency, physical repository/worktree
ownership and independent budgets remain intact. No provider contract, profile,
credential, fallback, verification or publication authority is added.

### One authorization, one fixed lineage

The authenticated Human arms at most one sequence for a `ManualAgent` run, for
its recorded objective and one exact project/workspace/checkpoint/fingerprint.
Only a `Created` run with no attempt, collaboration message or local-commit
operation may first arm it. The workspace must be the project's latest Ready,
owned workspace with its active repository lease and latest checkpoint. Refuse
active or ambiguous project attempts, verification executions and local commits.
Capture fresh Git evidence outside the transaction, then re-read all authority
untracked under the short SQLite write lock and require the same tuple; never
retarget a newer workspace/checkpoint. Arming invokes no provider, prepares no
workspace and consumes no Agent claim/time budget.

A unique RunId prevents a second sequence for that run for its entire lifetime.
An identical arming tuple replays the recorded receipt, including after the
sequence ended; a different tuple conflicts. Replaying never rearms it. First
arming and manual claim/verification/commit/abandonment races are serialized in
both directions. Existing rows receive no sequence or permission in migration.

The fixed progression is:

| Step | Exact source | Next decision |
|---|---|---|
| 0 Planner | The pinned run objective and source | Its one validated Proposal goes to step 1 |
| 1 CriticalReviewer | Step 0's Proposal | Accepted ends planning; Challenged goes to step 2 |
| 2 Resolver | Step 1's exact completed Challenged attempt and whole ordered input set | Its validated depth-one Proposal goes to step 3 |
| 3 CriticalReviewer | Step 2's revised Proposal | Accepted ends planning; Challenged goes to step 4 |
| 4 Resolver | Step 3's exact completed Challenged attempt and whole ordered input set | Depth two ends at the existing Human escalation |

This authorization explicitly includes the otherwise optional second review
round. There are at most five claimed attempts, with no automatic retry,
format repair, new root, adoption of a manual attempt or third challenge round.
Reuse `PlanningLineage` and existing role-owned eligibility/result rules.
Validate the complete predecessor outcome, output cardinality, provenance,
author, replies, source and ordered input membership before selecting the next
identity. Never select "latest proposal", a subset of challenges, an event's
attempt attribution, or a loaded UI window. Do not change result recording to
make coordinator selection easier. Existing escalation text/forms and the
separate Human authorization of a depth-two implementation remain unchanged.

`ReadyForImplementation` records completion of an Accepted root or first
revision; it grants no implementation or current-source approval. The final
depth-two result is `AwaitingHumanDecision`, never ready to implement. The
run itself is neither completed nor failed by either planning outcome.

### Durable step identity and claim composition

Persist a feature-owned `PlanningSequence` and up to five
`PlanningSequenceStep` records through the existing DbContext. Each step has a
unique (SequenceId, Ordinal), a fixed role/response contract, exact predecessor
input identity and, once claimed, one immutable unique AttemptId. Use relational
identity/membership and constraints, not a JSON authority blob or a second copy
of the attempt's authoritative ordered inputs. Once linked, the attempt's
`AttemptInputMessage` rows must equal the step's expected protocol input.

Reserve one step-preparation intent with compare-and-set under the write lock
before external preparation. That intent gives only the original live host
worker authority to try this step once; another tick does not run it again.
Its preparation deadline is two minutes from reservation. Cancellation or an
exception never establishes that a claim did not commit. Read back the exact
step/attempt association independently to distinguish persisted, absent and
unresolved outcomes; preserve potentially referenced artifacts on uncertainty.
Absent or unresolved after a failed preparation ends automatic advancement
with `NeedsAttention`, never another claim attempt.

Compose the existing three claim operations through `IApplicationMediator`,
with an internal sequence/step identity that their MVC requests cannot supply.
Keep all original manifest, source, model/effort, budgets, token and account
guards. Recheck the preparation authorization, deadline, expected step and exact
pinned source at their final durable seam. Commit the step-to-attempt link,
attempt, input memberships, artifact metadata and ordinary budget consumption
atomically in that same transaction. A two-command claim-then-link gap is
prohibited. Adapt the ordinary Claude review claim's save boundary as necessary
to achieve this atomicity; do not wrap Git, artifacts or provider observations
in a database transaction or introduce a second execution engine.

At most one step is outstanding per sequence. Duplicate workers and ambiguous
responses cannot create another attempt for its ordinal. A late result can
finish its existing attempt but never revive or advance a closed sequence.

### Manual coexistence and stopping

While a sequence can advance or has an unresolved linked attempt, refuse
unrelated Agent claims for its run (all claim paths and repair variants), and
verification/local-commit claims for its owned workspace. Refuse abandonment
while automatic authority remains live. Check early, before external work,
and atomically at the durable seam; a frontend disabled button is insufficient.
The reciprocal arming gate reads those operations under the same serialization.
No unrelated project is blocked. Other read-only inspection stays available.
Source/checkpoint or ownership changes invalidate continuation; never follow
them automatically. A newer checkpoint capture grants no continuation.

The Human action is labelled **Stop after current step**. If it wins before a
step's claim commits, the preparation authority is revoked and no attempt or
budget is consumed by that preparation. If the claim wins, the already claimed
read-only step may still dispatch and finish, and the sequence is `Stopping`;
no successor is allowed. This is not process cancellation, run Stop or Pause.
Repeated stop requests are idempotent. Once drained it is `Stopped`. A stop
that wins before completion classification keeps `Stopped`, even if the
provider later produces a valid acceptance. A terminal completed sequence is
returned unchanged by a late stop. Manual actions return only after automatic
authority is closed and no linked attempt remains active/ambiguous, subject to
their unchanged ordinary admission. Stale preparation workers remain fenced.
There is no resume or rearm in this slice; later manual work is explicit.

### Waiting, dispatch deadline and recovery

The closed state vocabulary is `Armed`, `PreparingStep`, `AwaitingStepResult`, `Stopping`,
`ReadyForImplementation`, `AwaitingHumanDecision`, `Stopped`, `NeedsAttention`
and `Interrupted`. A read with no sequence reports `None`, not a persisted
state. Reasons and next Human actions use fixed codes and text. Every durable
transition records a bounded run event; SignalR remains a post-commit hint.

After claim, the step's result deadline is its claim time plus its actual
reserved invocation timeout plus two minutes. Use checked arithmetic and the
existing claim-path timeout; this is a coordination observation deadline, not
measured invocation time, an account budget or proof of process death. The
three existing supervisors keep their current bounded concurrency. Role-queue
delay can exhaust this deadline; this conservative first coordinator reports
attention rather than silently retrying or consuming further claims.

The dispatch marker's atomic gate validates a linked attempt's exact session,
step, source and unexpired result deadline. `Stopping` retains its already
claimed step's grant; `NeedsAttention` and expiry do not. A dedicated
`PlanningSequenceDispatchRefused` pre-dispatch outcome and recorder may resolve
only a proved linked, Running, undispatched planning-family attempt whose
sequence authority/deadline is no longer dispatchable, rechecked under the
write lock. It invokes no provider, writes no protocol output and refunds no
budget. Do not reuse `WorkspaceNoLongerEligible` for this different fact. Wire
the refusal through all three supervisors; ordinary unlinked dispatch is
unchanged. Unprovable associations fail closed without fabricated outcomes.
Append the outcome without renumbering existing values. Its Domain transition
and process-evidence policy admit only those three read-only contracts with no
dispatch/process evidence. Ordinary provider-result recorders cannot assert it.
Existing status/history decoding may add its exact truthful disclosure for
those contracts only; other outcome and historical-content admission remains
unchanged.

An independent bounded coordinator sweep observes deadlines. The GET also
reports an elapsed deadline as attention immediately, even before the sweep
persists it. Expiry closes automatic advancement; it never marks a dispatched
attempt terminal, cancels its process, releases its ownership or enables a
competing writer. A late terminal record is retained but starts no next step.
An outstanding/unknown attempt stays visible as awaiting reconciliation.

On host startup, preserve the current artifact and Agent/implementation
reconciliation order in `Program.cs`, then interrupt every formerly advancing
sequence before any hosted coordinator or provider supervisor starts. Include
`Armed`, preparation-only and between-step sessions, even when no attempt was
ever created. Interrupted sessions never auto-resume, redispatch or adopt a
result. Existing attempt recovery remains authoritative; a failure to establish
recovery cannot enable the coordinator. OpenAPIGeneration and
ManualQualification remain side-effect-free compositions.

If ordinary attempt recovery makes the run itself Interrupted, this slice
adds no run-resume authority: show inspection/reconciliation requirements and
withhold claims under the existing lifecycle gate. A between-step interruption
may leave the run Running and permit later explicit manual work after the
sequence fence closes. Neither case authorizes automatic continuation.

### Visible and authenticated surface

Three protected MVC operations, each through one mediator request:

- `POST /api/runs/{runId}/planning-sequence`: at most 2 KiB, exactly the
  nonempty workspaceId, checkpointId and 64-lowercase-hex fingerprintSha256
  arming tuple. No role, provider, permission, step, mode or process input.
- `GET /api/runs/{runId}/planning-sequence`: bodyless recorded status, bounded
  step receipts, reason/next action and exact final plan/escalation references
  when proved. For `None`, include an arming-availability projection from fresh
  recorded admission facts: either the exact candidate workspace/checkpoint/
  fingerprint tuple or a fixed refusal reason. It performs no Git/provider
  observation and proves no runtime readiness; POST always checks afresh.
  Existing sequences expose their immutable tuple, never a replacement target.
  Also project the sequence's manual-control fence as Blocked, Released or
  Unavailable from current authority and linked-attempt facts. Released lifts
  only this fence and never asserts that an ordinary manual operation is eligible.
  No transcript, artifact body, path or provider payload is returned.
- `POST /api/runs/{runId}/planning-sequence/{sequenceId}/stop`: bodyless stop
  after current step, bound to the exact run/sequence.

Unknown runs and foreign sequences use non-disclosing 404s. Business conflicts
use fixed `planning_sequence.*` codes; structural errors retain the normal
validation pipeline. Closed API-owned response graphs expose canonical UTC
round-trip strings and string Int64 event sequences/revisions, with safe
operation-owned raw-wire decoding where generated revival loses those facts.
Reject malformed authority and inconsistent tuples, never coerce them into
permission or display a partial successful answer.

The cockpit offers explicit arming with a visible five-step maximum, the
included second review and separate implementation boundary. The host status
owns eligibility and final plan identity. Show arming, preparation, pending
dispatch, dispatch-recorded/awaiting-result, stopping, attention and restart
truthfully. No new state is labelled executing from a claim or dispatch marker:
there is no executor heartbeat/lease proof in this slice. For coordinated runs,
the header/runtime treatment must not use the legacy `Running` lifecycle or
active-participant projection as a live-execution indicator; describe any
retained run duration as lifecycle elapsed time, not measured agent activity.
Loading/unavailable sequence status withholds both arming and manual mutation
actions until ownership can be decided. Revalidate after notifications and
use bounded status polling while a sequence is nonterminal; UI closure does
not stop the host coordinator. Scope requests, callbacks and queued actions to
run/sequence/visit so switching A-B-A, remounting, stop/start races and stale
responses cannot act on or disclose another selection. No browser persistence,
frontend-driven advancement, auto-retry POST or generic runtime controls.

### Boundaries and consequences

This is one fixed read-only coordinator, not a generic workflow engine or the
complete autonomy guardian. Global mutating-run scheduling, executor leases,
heartbeats, full cancellation/retry/takeover, workspace preparation, automatic
implementation, verification, diagnosis/correction, local commit and remote
publication remain separate work. This slice does not claim those exit criteria.
The existing repository lease and per-run attempt uniqueness are retained; it
adds no mutating concurrency or global/provider capacity guarantee. Capacity
and lease failures stop with attention without hiding their original reason.

Keep provider invokers, CLI permissions, schemas, process transport, source
delivery and artifact admission unchanged. New session/step records are
retained for the run lifetime; this slice exposes no delete/purge operation.
Any future run deletion must explicitly own hard deletion of these dependents.
Do not rewrite existing attempt, message or authority facts in migration.

## Alternatives considered

- Full local plan/edit/verify/review/correct/commit coordination: deferred
  because it adds mutation authorization, recipe-set identity, correction
  routing, scheduler ownership, cancellation and ambiguous-write recovery.
  Those are different authority and validation boundaries; grouping them here
  would make the first coordinator's safety judgment depend on unselected policy.
- More historical inspection/replay: useful but does not advance stage
  coordination, and graph replay requires its own traversal and disclosure policy.
- Scheduler or general pause/resume first: neither selects exact next protocol
  inputs. Both require cross-operation execution ownership beyond this read-only
  sequence, so they do not replace the selected observable outcome.
- Provider gap work first: the gaps remain required for MVP completion, but no
  new provider capability is needed to coordinate these already supported roles.
  Real-provider qualification is not authorized by this decision.
- A timer calling existing POSTs or claiming then attaching an attempt:
  rejected because neither has atomic durable authorization and step identity.
- New execution mode or automatic migration/adoption: rejected; explicit,
  single-use authorization preserves inert manual intake and historical facts.
- Plan-only/list-only infrastructure: rejected as an incomplete outcome. The
  two bounded review/resolution rounds share the same lineage and recovery
  contract and can ship together without adding a mutating stage.

## Required evidence

Prove both acceptance paths and second-round escalation through production
claims/results with deterministic adapters; exact whole-input selection;
two-worker, replay, stop/claim, arming/manual/verification/abandonment races;
atomic link/budget/artifact rollback and commit uncertainty; deadlines before
and after dispatch; source/lease/mode drift; startup at every boundary;
unlinked/manual/simulated/legacy non-regression and independent project progress.
Use real file-backed SQLite migrations/constraints and independent connections.
Prove that a restart or late completion creates no successor, and every refusal
has zero unauthorized provider/verification/Git calls.

Exercise all three MVC auth/body/error/wire contracts and authorization
inventory; frontend decoding, selection ownership, pending/attention display
and stop races; and one authenticated browser journey that arms once and reaches
a proved terminal planning outcome without per-stage requests. Other branches
and recovery permutations may be proved at the hosted/Application boundary.
Bounded compiling negative controls must expose a lost claim fence, lost
atomic link, wrong predecessor identity and stale UI action. Keep original
failures and exact commands under the proportional validation policy; do not
reconstruct earlier evidence. Mandatory CI and release gates remain unchanged.
