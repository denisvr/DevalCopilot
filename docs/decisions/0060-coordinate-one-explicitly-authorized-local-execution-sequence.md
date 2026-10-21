# ADR-0060: Coordinate one explicitly authorized local execution sequence

Status: Accepted

Selected architecture for LOCAL-COORD-01; not implemented or publication GO.

## Context and owner decision

The published baseline `7c78c849fdd556ecf3a539dce479e31fc3cc501b`
delivers explicit implementation, local verification, review, failure diagnosis,
both correction origins, local approval and commit. ADR-0055 coordinates only
planning. ADR-0056 observes host supervision; ADR-0057 to ADR-0059 veto unsafe
admission. None authorizes a mutating successor or a complete scheduler.

On 2026-10-20 the owner explicitly selected **Authorize that bounded local
cycle** in this planner chat: design an opt-in authorization for one accepted
plan, implementation, a pinned verification set, review and up to two shared
corrections, including eligible verification diagnosis, ending at approved
review or escalation. Commit and publication stay separate. This decision
authorizes the product contract and its bounded implementation selection, not
a real-provider experiment, execution of a product run in this planning chat,
or repository commit/push. Codex performed the architectural judgment itself.

## Decision

### Authority and completion boundary

LOCAL-COORD-01 delivers one explicit, single-use local execution sequence.
It narrowly supersedes ADR-0014's per-stage request rule and ADR-0010/0018's
manual-only verification/review/diagnosis/correction progression **only for
operations atomically linked to this sequence**. It extends ADR-0009's ordering
for this bounded outcome before the remaining provider contracts are proved.
ADR-0055's planning authority remains separate and ends where it ends today.

Only `ManualAgent` is admitted. Intake stays inert; no new execution mode,
automatic adoption of historical authority, planning continuation, new plan,
extra-correction authorization, approval, commit or publication is introduced.
Existing manual behavior outside a holding sequence remains unchanged.
An accepted review is evidence awaiting the existing Human checkpoint approval
and explicit ADR-0029 local-commit request, never a completed run or a commit
grant. This does not complete Increments 4, 5 or 6.

### Arming and exact confirmation

At most one sequence may ever be armed per run. The run must be Running,
have no prior WorkspaceMutating Agent claim (successful or otherwise), and
have a coherent, implementable plan in its current owned workspace/checkpoint.
Support an explicitly Accepted Planner root or explicitly Accepted depth-one
revision, and ADR-0016's separately Human-authorized final depth-two revision.
An unreviewed first revision is insufficient for this new opt-in surface even
where an ordinary manual implementation admits it. Validate the existing full
lineage, actual implemented-plan identity, latest-root restrictions and any
separate unconsumed Human grant. Never manufacture an Acceptance or grant.
Both manual and ADR-0055 planning outputs may qualify; a planning sequence must
have released its own fence. Do not adopt any prior implementation or correction.

Require the project's latest Ready owned workspace, active physical repository
lease, latest checkpoint and matching fresh Git fingerprint, no unresolved or
untrustworthy workspace owner, no local-commit reservation and available global
capacity. Configuration/observation alone is not authorization. Arming performs
no provider call, verification, workspace preparation, attempt claim or budget
consumption and acquires no global slot. Capacity can change before the first
claim and that claim decides again.

The Human confirms the exact plan, starting workspace/checkpoint/fingerprint,
the **whole enabled verification set**, requested Codex/Claude model/effort and
Claude turn-limit settings, and the automatic correction allowance. The set
must contain 1 to 32 coherent project-owned recipes, ordered by CommandNumber
then Id, with unique identities/numbers. Pin each recipe's full execution
metadata by value, including ordered arguments, timeout, enabled flag and
configuration timestamps. Pin the run's requested assignment/turn-limit facts
and its immutable budget policies. Malformed stored values refuse arming.

A feature-owned versioned SHA-256 confirmation value binds that complete
candidate, plan-authority identities and all snapshots. Use a specified
unambiguous canonical encoding with explicit null/length framing and ordinal
strings; tests pin it. This value binds the confirmation to the availability
read; it is neither a credential nor the authority/membership store. Persist
recipe membership/snapshots relationally and validate their whole set.
Changing the configuration between display and submission refuses arming.

Capture Git outside the transaction, then take the existing SQLite write lock
and re-read all authority untracked, compare the complete candidate and save
authorization, snapshots and one Human run event atomically. Reciprocal manual
claim, verification, planning, abandonment, recipe and assignment-update races
must serialize at their own final seams. An identical normalized arming tuple
replays its recorded receipt even after termination; a different one conflicts.
Replay never revalidates into a new grant or rearms the sequence.

### Fixed progression and bounds

| Phase | Exact inputs and decision |
| --- | --- |
| Initial implementation | The pinned plan and its complete existing acceptance/decision/Human-authorization inputs; use the ordinary implementation claim. |
| Verification pass | After `Implemented` or `CorrectionApplied`, derive only that linked attempt's exact ExecutionReport and result checkpoint. Run every pinned recipe once, serially, against this checkpoint in pinned order. |
| All verification passed | Claim the ordinary CodeReview of this exact report and complete ordered set of this pass's executions. `Approved` ends at `ReadyForHumanApproval`; `ChangesRequested` selects its whole applicable finding set for correction. |
| Coherent verification failure | Finish the whole pass, including after an ordinary nonzero exit. If all executions satisfy ADR-0018 and at least one failed, claim VerificationDiagnosis of this exact report and complete pass. Findings select diagnosis-origin correction; its Escalation ends at `HumanEscalation`. |
| Correction | Claim the existing origin-specific operation with the preceding exact report and every finding in protocol order. Only `CorrectionApplied` selects the next pass on its exact new checkpoint. |

Run at most two automatic correction claims in total across both origins,
further limited by the run's remaining shared ReviewCorrection allowance.
Pin that smaller allowance at arming. Failed/undispatched claims still spend
the ordinary allowance. Never consume an extra Human correction grant, refill
a budget, repair format, retry a failed operation, reuse a previous pass's
verification, select latest messages/executions, or skip a failed recipe.
No initial implementation retry is permitted.

There are at most three verification passes, six Agent claims and
`3 * recipeCount + 6` operation steps (maximum 102). A pass chooses exactly one
CodeReview or VerificationDiagnosis, never both. Existing Agent count, reserved
invocation time, token stops, Codex account stop, provider readiness and every
claim/dispatch gate apply independently and may stop the sequence earlier.
The sequence has an absolute two-hour advancement deadline from arming; it
neither extends nor replaces any operation's existing timeout/budget.

Exhaustion with unresolved findings ends with one idempotent, fixed
Orchestrator-to-Human escalation replying to the exact linked current report
(or preserves the exact provider diagnosis Escalation). The host exhaustion
message uses the existing five-string Escalation content graph, with fixed
summary `Local execution cycle requires a human decision.` and these values:

| Member | Fixed value |
| --- | --- |
| unresolvedDecision | The authorized local execution cycle has unresolved findings. |
| options | Inspect the recorded evidence and decide on an explicit manual next action. This cycle cannot continue. |
| consequences | No further operation is authorized by this sequence; the run remains awaiting human action. |
| evidence | The authorized automatic correction allowance was reached. |
| recommendedChoice | Inspect the evidence before requesting any manual continuation. |

Record its exact identity and the normal collaboration-message event atomically
with termination. The sequence's unique final-message reference owns its
idempotency; it grants no existing extra-correction or planning authorization.
Never call an ordinary claim to probe exhaustion and accidentally consume a
Human grant. Budget/guard refusals,
invalid output, timeout, cancellation, process-start failure, drift, missing
artifacts, unknown facts and ambiguous persistence end automatic advancement
with `NeedsAttention`, rather than entering diagnosis or another mutation.

### Durable composition, source evolution and coexistence

Use feature-owned LocalExecutionSequence, LocalExecutionSequenceRecipe and
LocalExecutionSequenceStep records through the existing DbContext. Preserve
one sequence per run, unique step ordinals, unique (sequence, pass, recipe)
verification membership and unique operation links. A step discriminates Agent
from verification and records its fixed kind, pass, predecessor, expected
report/checkpoint/fingerprint, reserved operation UUID and one actual link.
Foreign, duplicate, missing, ambiguous and malformed links fail closed.
Bound all reads; overflow refuses instead of returning an authoritative prefix.
No JSON authority graph or duplicate AttemptInputMessage authority is added.

One write-locked observation reserves the next preparation with a unique
token held only by its original live host worker and a two-minute deadline.
The new feature has its own launch identity; AgentSupervision observations
and their host identity confer no authority. Only that original worker may
try the reserved claim once. No later tick/host reclaims a reservation.

Compose the existing implementation, verification, CodeReview, diagnosis and
two correction-origin claims through IApplicationMediator, with internal
sequence/step identity that public ordinary requests cannot bind. Revalidate
the exact grant, original launch, deadline, inputs, snapshots and current
source at each final write-locked claim seam. Save the link, ordinary operation,
its existing input/evidence rows, manifest metadata, counters, budget/grant
consumption and sequence transition in **the same transaction**. Predetermine
verification UUIDs as well as Agent UUIDs. Claim-then-link is prohibited.
No external Git, artifact, provider or process operation runs in that transaction.

Cancellation, exceptions and commit errors never prove absence. An independent
connection checks the exact reserved UUID/link: persisted, definitely absent
or unresolved. Keep potentially referenced artifacts on uncertainty. Definite
absence or uncertainty stops advancement; neither causes a second claim.
Do not wrap ordinary handlers in a replaying generic transaction pipeline.

After a validated linked Agent result, only that attempt's exact report/result
checkpoint may advance the sequence's current source. Reuse the complete
ImplementedPlan versus OriginalProposal and correction-chain validation.
Validate the report/output cardinality, provenance, role, replies, clean-exit
evidence, ordered inputs, resulting checkpoint and latest-workspace facts under
the write lock before reserving a successor. A new unrelated checkpoint, plan,
verification execution or source edit never becomes the sequence's source.
At review/diagnosis claim and dispatch, current selected execution membership
must equal the entire recorded pass, not just have matching counts.

While authority is live or any linked operation/preparation is unresolved,
withhold unrelated Agent claims (all contracts/repairs), workspace verification,
local commit, manual checkpoint capture, run abandonment and changes to the
project's verification recipes or run's requested assignments/turn limit and
token/account stop-policy settings. These run stop policies are also pinned
by value at arming and included in the confirmation; their mutation commands
share the fence rather than silently changing the authorized cycle.
Apply this reciprocally at early and durable seams; only exact linked operations
are exempt from this **new** fence. Their ordinary workspace/global owner gates
are never exempt. Existing result recording and necessary artifact sealing for
already authorized work remain permitted. Reading history/evidence, notifications
and unrelated projects remain available. Other independent safety settings may
still tighten admission; snapshots never override fresh safety guards.

The sequence is not itself a workspace/global execution owner. Between safely
completed steps it holds only its coordination/manual-control fence, not an
execution slot. Ordinary operation rows remain the counted owners, with one
necessary narrow extension of ADR-0057/58's unresolved-row classification:
an operation linked to this sequence that was dispatched and lacks coherent
host-recorded process `Exited` evidence remains an effective owner even if a
legacy result path recorded a recognized terminal row. For an Agent this
requires its existing validated process-evidence tuple with Exited and an exit
code; for verification it requires Exited and an exit code in its own result.
A timeout, cancellation, adapter exception, missing evidence or terminal enum
alone proves no exit. A dedicated undispatched refusal needs no process proof.

Apply that definition in the **same shared** workspace/capacity policies and
their final gates, not only in the new status query. Count each ordinary row
once, unioning retained linked owners with unresolved ordinary owners; ReadOnly
retains workspace exclusion but consumes no global mutating count. Validate
complete link/owner/contract/exit tuples before exclusion or release; missing,
foreign, conflicting or unknown evidence is Unavailable, never a zero count.
This extension adds no separate slot/reservation and leaves unlinked historical
rows unchanged. A recorded terminal result without exit proof remains available
as truthful failure evidence, while its exact operation stays quarantined.
There is no supported release command in this slice. The narrower owner
classification supersession does not change ADR-0059's one/two configuration,
safe-lowering refusal, freshness or exact-owner self-exclusion rules.

Every actual claim and dispatch reads the persisted global limit fresh and
excludes only its exact ordinary owner where allowed. A capacity conflict ends
with attention and consumes no operation; no durable wait/retry queue, fairness
policy, queue priority or automatic pending-admission replay is selected here.

### Stop, deadlines, dispatch and restart

States are `Armed`, `PreparingStep`, `AwaitingStepResult`, `Stopping`,
`ReadyForHumanApproval`, `HumanEscalation`, `Stopped`, `NeedsAttention` and
`Interrupted`. `None` is a read discriminator only. State/reason transitions
and their bounded events are atomic. A bounded independent observer handles
deadlines even while an external preparation or adapter call is blocked.
GET projects elapsed deadlines immediately without asserting a durable write.

**Stop after current operation** is idempotent. If stop wins before the claim
commits, revoke preparation with zero claim/budget/grant consumption. If the
claim wins, that one operation may dispatch and finish while `Stopping`, subject
to every independent gate and its unexpired deadline; never start another recipe
or successor. Stop wins over later completion classification. A late stop of an
already ended sequence returns it unchanged. There is no process cancellation,
run Stop/Pause, resume or rearm. A terminal sequence releases only its own fence
when no unresolved preparation or effective linked owner (including terminal-
without-exit-proof quarantine) remains; ordinary gates still apply.

A claimed operation's coordination result deadline is claim time plus its
actual invocation/recipe timeout plus two minutes, capped by the sequence's
advancement deadline. Checked arithmetic is mandatory. Deadline expiry closes
successor authority; it proves neither process death nor safe release and does
not cancel an already dispatched operation. Existing provider/verification
supervisors remain the executors; role-queue delay can yield attention and is
disclosed. No supervisor throughput rewrite is part of this slice.

Both ordinary dispatch markers validate the exact linked step, source, pinned
configuration/membership and current deadline under their existing write lock.
For a proved linked, still unresolved **undispatched** operation whose sequence
grant expired or was revoked, use dedicated, idempotent refusal recorders:
append AgentOutcome.LocalExecutionSequenceDispatchRefused for the four involved
Agent contracts, and VerificationExecutionStatus.LocalExecutionSequenceDispatchRefused
with no process Outcome/exit/fingerprint for verification. Preserve existing
numeric values and explicit terminal inventories. Require exact association,
no dispatch marker and no process evidence; write no protocol result and refund
nothing. Normal provider-result recorders cannot assert these facts. An unknown
association refuses without fabricated terminalization. All four Agent
supervisors and verification must handle this refusal, not leave a proved
undispatched claim stranded. Source/account/ordinary refusals keep their own reasons.

Before startup artifact recovery, attempt interruption, workspace reconciliation
or any supervisor, close previous-launch sequence advancement and revoke its
unclaimed preparations atomically. No restart resumes, adopts or creates a
successor. A proved undispatched linked claim can be closed with the new refusal
without invoking its adapter. A linked dispatched operation with no coherent
recorded terminal result remains unresolved, even if old recovery would otherwise
mark its row Interrupted. Exclude those exact rows/artifacts from ordinary
interruption/recovery: do not seal/import potentially live partial output,
invent a terminal run outcome or release their ordinary ownership. Preserve the
recorded unresolved Agent/verification row, dispatch marker and its global count.
An unknown association quarantines the affected sequence/workspace rather than
falling back to generic recovery. Do not repair it, cancel unrelated work or
change another run's lifecycle. Existing shared admission may still refuse new
work when global ownership is Unavailable; do not manufacture a trustworthy
count to promise unrelated progress through corrupt authority.

Already coherent recorded terminal results with admitted host exit proof (or
a proved undispatched refusal) permit closure of the fence after validation,
never post-restart advancement. Exited here is the existing host proof about
the invoked process, not new proof about every descendant or a stronger sandbox.
The existing process-tree/privileged-child residual limits remain. A result
returning late in its original live host may still be recorded under ordinary
rules, but cannot reopen
the ended sequence. No second host may attach to its process or write a result
on its behalf. If completion is unproved, the UI must say **No supported recovery
action in this version** and retain exclusion; there is no force release,
cancel/retry/takeover or inferred process-death mechanism. Test this deliberately
conservative limitation. Finite executor leases and process-aware recovery need
separate contracts and evidence; a deadline or host heartbeat is not either.

### Protected API and cockpit

Use four protected MVC operations, one action each and normal mediator dispatch:

- `GET /api/runs/{runId}/local-execution-sequence/availability/{planProposalMessageId}`:
  bodyless/queryless recorded arming availability for one explicitly selected
  plan, with the exact confirmation tuple, bounded recipe identifiers/numbers/
  safe names, requested assignment disclosures, automatic correction allowance
  and fixed refusal/next-action codes. No Git/provider observation or execution.
- `POST /api/runs/{runId}/local-execution-sequence`: queryless, 1 KiB body,
  exactly `planProposalMessageId`, `workspaceId`, `checkpointId`,
  `fingerprintSha256`, `confirmationSha256`; strict valid UUID/hash strings.
  No command text, provider choice, permission, budget, child ID or guidance.
- `GET /api/runs/{runId}/local-execution-sequence`: bodyless/queryless status,
  `None` or the exact sequence, reason/next action, manual-control fence
  (`Blocked`, `Released`, `Unavailable`), pinned/current source, allowance,
  deadline, bounded step receipts and proved final report/review/escalation IDs.
- `POST /api/runs/{runId}/local-execution-sequence/{sequenceId}/stop`:
  bodyless/queryless, bound to the exact run/sequence, stop after current operation.

Unknown runs/foreign identities use non-disclosing 404s; structural errors
use the normal 400 contract; business refusals use fixed non-echoing
`local_execution_sequence.*` Conflict codes. Reject unknown, duplicate,
differently cased or escaped body members, wrong types and extra query/body
data before dispatch. API owns the complete response graph; canonical UTC
strings and Int64 event/revision strings retain exact values. No raw executable,
arguments, storage path, manifest, secret, transcript, internal worker token or
process identity is exposed. Safe DTO mapping must refuse inconsistent tuples.

The cockpit offers **Authorize local execution cycle** for the settled exact
candidate. Show that verification commands can mutate, at most two shared
corrections (or the smaller remaining amount), the two-hour advancement bound,
separate approval/commit, and stop-after-current semantics. One run-owned reader
shares status/fence between collaboration and workspace regions; neither region
can bypass it. Unknown/loading/stale status withholds mutating starts.

Arming and stop submit once with real ten-second cancellation; ambiguous responses
reconcile through status, never automatic POST retry. A new post-command read
is required before actions can be enabled. Scope reads, pending actions, draft
confirmation, notifications, timers and callbacks to run/plan/sequence/visit and
mounted lifetime; cover A-B-A, remount, stale availability/status, stop/arm and
expiry races. The browser never advances steps or persists authority locally.
Coalesce GETs and poll status at most once per five seconds while authority
or a linked owner remains unresolved; notifications/manual refresh are hints,
not parallel readers. Each GET has a real ten-second bound. Transport failure
cannot preserve an earlier enabling reading or create a second queued poll.
Use raw-wire validation where generated revival would lose exact timestamps or
Int64 facts. Render untrusted names as inert text.

Show armed/preparing/dispatch-recorded/awaiting-result/stopping/attention and
historical outcomes truthfully, with the current operation and required action.
Shared workspace/capacity copy describes recorded effective ownership,
including quarantine, rather than calling a terminal-without-exit-proof row
an actively executing process. Preserve the existing closed status schemas.
No `Running` execution indicator is inferred from a run lifecycle, claim,
dispatch, timer or AgentSupervision heartbeat. Keep lifecycle elapsed time and
host observations separate. A successful sequence remains Awaiting Human
approval, not committed, published or proof that its source is still current.

### Persistence, exclusions and alternatives

Use additive migrations, restrictive ownership keys/uniqueness and immutable
authorization/snapshot/link facts with guarded revisions for state. Up/down,
upgrade/reopen and malformed-storage tests are required. Historical rows receive
no sequence/grant; sealed manifests and outputs remain unchanged. New records
are retained for the run lifetime with no delete/purge operation; future run
deletion must explicitly own their hard deletion. Existing accepted physical
source, manifest, process and output admission contracts stay intact.

The largest coherent outcome includes both failure-diagnosis and review-origin
corrections because they consume the same report lineage and shared allowance.
Implementation-only or backend-only dispatch would leave the authorized outcome
unfinished and repeat operator transfer. A full scheduler, pending queue,
execution leases or general recovery would add distinct admission/expiry/process
authority and cannot be inferred from this grant. Blindly reusing the read-only
coordinator's startup terminalization is rejected because mutating descendants
could otherwise overlap. Durable quarantine is chosen until stronger recovery
is separately proved. A timer invoking ordinary HTTP endpoints, claim-then-link,
event-derived inputs and automatic configuration retargeting are rejected.

No package, native/security/profile/helper/credential experiment, provider flag,
permission change, new provider/session feature, remote call, publication
mechanism or general settings/event/workflow engine is selected. ADR-0050 stays
Proposed, P9 unproven and AI Memory deferred. Real-provider reliability and the
privileged-process/sandbox residual risk remain unproved and visible.

## Required evidence

Prove zero-correction approval, review-origin correction, diagnosis-origin
correction, mixed origins sharing the cap, every exhaustion/escalation branch,
exact plan/whole-finding/pass selection and current-source evolution through
production claims/results with deterministic process doubles. No raw-SQL
approval replaces the production lineage in the representative path.

Use file-backed SQLite with independent connections for arming/manual/config/
stop/claim/dispatch races, duplicate workers, link/budget/grant/artifact rollback,
commit uncertainty, malformed/foreign ownership and both orderings of capacity
change versus admission. Prove no external preparation under a write transaction.
Prove every linked mutating entrance obeys limit one/two and exact-owner dispatch;
unlinked/manual/Legacy/Simulated, ADR-0055, existing result recording and independent
project behavior remain unchanged. Never gate on AgentSupervision observations.

Startup tests cover before reservation, preparation, claim, before/after each
dispatch marker, known result before classification and between every phase;
include an unresolved dispatched mutation whose Git fingerprint is unchanged,
verification and read-only unresolved children, late preparation/result and
missing/forged links. Assert no duplicate invocation/successor, no import of
potentially live output, no false terminal row, no lost workspace/global owner
and no supported recovery falsely offered.

Exercise all four auth/body/error/OpenAPI contracts, exact wire decoding,
selection ownership, confirmation drift, both regions' fence and truthful
stopping/restart display. One authenticated browser journey must arm after real
planning, exercise both correction origins with two shared corrections, reach
the exact approved-review boundary without per-stage requests, reload, and
prove zero Human approval, commit or remote mutation. Other permutations belong
at the cheapest hosted/Application boundary. Run existing collaboration and
planning-sequence journeys serially for coexistence.

Retain four compiling controls killed by named regressions: lost atomic link,
wrong pass/recipe identity, lost original-host/startup quarantine, and stale
frontend confirmation/fence. Preserve failed/invalid controls and all complete
failure output, restore source hashes, touch restored files and rebuild before
final checks; source hashes do not authenticate historical binaries. The handoff
and validation policy specify the proportional final matrix. These tests do not
prove real-provider behavior, process death, queue fairness or recovery.
