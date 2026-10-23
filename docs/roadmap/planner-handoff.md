# Planner/reviewer handoff

## Current action: LOCAL-QUEUE-INSPECT-01

Implementation of accepted [ADR-0063](../decisions/0063-inspect-recorded-local-execution-queue-admissions.md)
is **reviewed GO (review 1)**. Publish the exact approved content in the **same
LOCAL-QUEUE-INSPECT-01 Claude executor chat**, then return the publication report
for independent verification. The LOCAL-QUEUE-01 executor chat is complete.
Codex resolved the architecture, scope and review itself. Selection alone grants
no publication; this GO is bounded by its external content identity and prompt.

### Independently verified publication and continuity

LOCAL-QUEUE-01 is published on main as
**8b9a824f0a68317432794afed80ab12250b16721**, tree
`56f4f31212101a4a91ec9dc3e23387adc963e5b6`, sole parent
`c1e9392161ca8d62ffc157e8a74a7ae0e7e625b1`. Fresh live refs/heads/main,
HEAD and local origin/main agree; divergence 0/0; index and working tree were
clean before these planner inputs. The exact approved set is 167 paths,
107 modified and 60 added. LCQ-R1 through LCQ-R4 and LCQ-V1/LCQ-V2 are resolved.
No second commit or closure-document diff is needed. The implementation record's
prepublication wording is historical, not an instruction to publish again.

Records: `logs/planner-review/local-queue-01/publication-verification.json`,
`continuity-after-publication.json`, review-04 GO and all earlier review/control
logs. Detailed historical planning/reviews are preserved in this handoff at
8b9a824 and ignored evidence, not copied into the active selection.

Codex: **CONTINUE_THIS_CHAT**. Observed token_count at
2026-10-23T09:39:41.534Z: last context 169958 / declared window 475000 = 35.78%.
This is current observed context, not cumulative tokens or account quota.
Source is the session record named by continuity-after-publication.json.
Claude continuity is separate: the next selected slice starts a new executor.

### Review 1: GO, publication pending

Prepublication Git: main, HEAD and local origin/main at
8b9a824f0a68317432794afed80ab12250b16721, divergence 0/0, index empty. The
reviewed set has 53 paths (15 modified, 38 new), all within scope. The first
return's file-count arithmetic differs; no unexplained path was found. The
four planner inputs were hash-verified unchanged by the executor before review.

No blocking finding. Independent Application inspection consumers pass 354/354
(2 m 32 s), protected API inspection consumers 96/96 (1 m 10 s), frontend's
five new decoder/wire/owner/surface files 55/55 (15.34 s wall). Executor final
Architecture 192/192, whole Vitest 7454 and tooling, generated client stability
and the two serial final journeys satisfy the selected matrix. Full prior suites
remain retained at their old scopes, never fresh results for the new query.
Both compiling controls kill the named permanent tests and restored hashes
match the freshly checked sources. Invalid/failed earlier attempts remain.

The retained journey-queue-1 failure is explicitly dispositioned: its expected
Passed/exit-0 assertion selected a first-pass verification that actually Failed
with exit 1, as the failed log itself shows. Current source requires that actual
Failed/exit-1 evidence for linked step 2 and opens linked review step 8 in run
history, checking exact GET paths and no mutations. Separately inspected final
logs show queue 1 passed (48.0 s) and local-execution 1 passed (29.9 s); the
corrected development queue run also passed (47.1 s). The original failure is
preserved, not relabeled as a pass. Detailed independent disposition is in
`logs/planner-review/local-queue-inspect-01/review-01/journey-failure-disposition.md`.

Executor interpretations resolved: the single provider-neutral EF statement
and WAL snapshot tests meet the consistency contract. Recipe count/bounds and
the membership/name of referenced recipes suffice for this ledger; it neither
recertifies unused pinned recipe flags/order nor proves an operation outcome.
The existing sequence/selected-operation inspectors keep those separate proofs.
The recorded lifecycle matrix is supported by the production-driven cases;
the historical monotonic-clock and typed GUID/time conventions remain qualified.
Reloading the sequence index unmounts and ends the ledger visit; reopening
explicitly is correct, not a promise to preserve an ended owner's state.

Hygiene: 236 local links/fragments checked, no errors or conflict markers;
diff check clean, with the existing generated-client line-ending advisory.
No production correction or temporary reviewer test was introduced. Planner
review edits are only this handoff, the decision index and MVP status wording.
Exact approved paths/tree and the review evidence are external to the tracked
content under `logs/planner-review/local-queue-inspect-01/review-01/`.
The publication prompt supplies the expected parent, tree and inventory hash.
No further product/documentation edits or closure commit are authorized.

### Outcome, sizing and authority

Inspect every recorded admission of one queued local cycle, including a wait
retired without an operation, in current-run Usage & Evidence and selected run
history. An explicit action on a proved linked step opens its existing operation
evidence. The live status exposes only outstanding admission bookkeeping, while
the existing step index cannot represent an admission that created no step.

Ship the protected bounded read, all five admission states, strict client,
ended-request ownership, exact step links and both surfaces together. They share
one recorded-run proof and acceptance path. A cross-project queue census requires
a different bounded membership/snapshot contract; scheduler/recovery changes need
separate authority. Neither belongs in this recorded-run outcome. No new owner
execution grant is needed for read-only inspection (the ADR-0061 precedent).

### Resolved boundary

- One bodyless/queryless protected GET under the existing inspection route:
  `/api/runs/{runId}/local-execution-sequence/inspection/admissions`.
- Closed NotRecorded/NotQueued/Recorded/Unavailable graph; unknown run fixed
  inspection 404. Immediate with any admission is unavailable, never queued.
- One provider-neutral guarded EF statement for the root, bounded ledger and
  reference facts. No split-query race, write lock, retry or mutation. Sequence
  revision alone cannot detect waiting-reason changes. Challenge translation
  limitations before changing this design.
- At most 103 admission rows to prove overflow; enforce Domain's smaller
  recipe-dependent bound. Prove historical root, source, report, predecessor,
  correction origin, ordering, lifecycle and the entire admission/step tuple.
  Waiting and retired-without-step rows remain inspectable without an operation.
- No live SourceHolds/ownership/admission reads or latest-source substitution.
  Existing historical projection guards and pure Domain mappings are reusable;
  do not change execution gates to support inspection.
- Explicit collapsed reads, reload and direct step drill-down through the
  existing local inspector. If its index lacks a linked step, require explicit
  index reload; no unrelated-history search or substituted evidence. Closed
  panels, history selection and ended A-B-A/opening owners obey ADR-0063.
- Four frozen planner inputs: this handoff, ADR-0063, decisions/README.md and
  mvp-delivery-plan.md. Executor updates current-work and affected architecture
  documentation, preserving these resolved inputs unless a challenge is resolved.

### Proportional validation and acceptance

Build once before a group and reuse only current restored binaries. Focused
red/green during development; one final matrix on stable input, no overlapping
build/generator/mutation writers. Keep exact commands, timings and source scope.

| Boundary | Final evidence | Broaden only when |
| --- | --- | --- |
| Historical query | All new LocalExecutionAdmissionHistory classes plus existing LocalExecutionInspection Application classes; real file SQLite, independent connections, one-statement race/command proof, all states, both correction origins, malformed storage, row bounds and no effects | Shared execution/authority/transaction consumers change: full relevant Application/API; otherwise stop before such scope expansion |
| MVC | New admission-history complete auth/body/query/error/graph matrix plus all existing LocalExecutionInspection API classes | Shared authentication, binding, error or request pipeline changes |
| Architecture | Full Architecture project | New failure requires a named check |
| Frontend | Whole Vitest, lint, build (includes product tsc), strict e2e tsc; generated client rebuilt byte-identically after successful API generation | Shared ownership/harness behavior changes invalidate additional consumers |
| Browser | Extend queue journey: held Waiting/zero step ledger, consumed ledger after release, current/history, explicit linked-step evidence and no extra mutation requests; run existing local-execution journey serially | Shared harness/authentication/transport changes: also planning-sequence and collaboration serially |
| Unchanged boundaries | Retain Domain 1757, Persistence/transactions 750, full Application 7905, API 3265 at their recorded scopes; do not call them fresh final runs | Domain, schema/migrations or shared authority changes require a challenge and revised matrix |

New query tests use the LocalExecutionAdmissionHistory name so focused filters
can enumerate the changed boundary. Useful filters are
`FullyQualifiedName~LocalExecutionAdmissionHistory|FullyQualifiedName~LocalExecutionInspection`.
Do not duplicate every policy permutation in MVC. Prove malformed reader behavior
where reachable; if storage rejects a test mutation, label that storage evidence
rather than pretending the reader ran. Adversarial storage fixtures must state
any test-only guard bypass. No production test hook is permitted.

Two compiling controls: omit one required admission/step tuple comparison; remove
an ended frontend read/selection-owner guard. Each must kill a named permanent
regression; retain invalid attempts, restore hashes, touch and rebuild before
passing evidence. No wider mutation campaign is selected.

No full Infrastructure, dependency audit for unchanged dependencies, Domain rerun,
real provider or native experiment is automatically selected. CI/security/release
checks retain their own requirements. Preserve all earlier qualifications.

### Publication and stop boundary

The implementation was returned unstaged, uncommitted and unpushed with its
execution evidence. Publish only the exact content covered by review 1 GO;
verify staged/committed tree, sole parent and path inventory, push main normally
and verify the live ref and clean 0/0 state. A mismatch, remote divergence or
failed push stops publication without force or reconciliation. Material edits
return for review. The planner independently verifies publication and checks
current context usage before selecting another slice. A conflict with the
read-only boundary or evidence contract never grants a production fallback or
authority expansion.

## Retained review and evidence limits

Do not rewrite the published GLOBAL-LIMIT-01 record. Its final independent
frontend evidence is 958/958 plus lint, build/typecheck and strict e2e checking;
two reviewer compiler-invocation failures precede the corrected pass. Full
Application 7061/API 3050 retain unchanged backend-source scope. Persistence 468
retains its first 466/468 failure and accepted table-list repairs. Whole Vitest
6835 is pre-correction evidence, not a fresh whole-suite run. Both corrected
journeys passed serially; the volunteered 1602-hook run has no separate log.

The original Application red run is 21 failures/55 passes: 18 behavioral and
three invalid rowid-insertion setup failures. The original third control's
unused-variable attempt did not compile; its corrected-hook control compiled
and killed its named regression. Preserve their distinct scopes and all
warnings/skips, bounded control sensitivity and source-versus-binary limits.

Detailed prior review and constraints: this file at `7c78c84`,
[current-work](current-work.md#active-evidence-limits),
[WF-04-era limits](planner-handoff-history-e614c79.md#retained-review-and-evidence-limits),
[earlier limits](planner-handoff-history-0a57216.md#retained-review-and-evidence-limits),
and ignored per-slice planner/executor logs. WEX's initial API mutant-provenance
uncertainty and overwritten correction failure log remain. Earlier lost,
derivative, partly retained and chat-only output, invalid/no-op controls,
measurement limits and unexplained failures stay qualified; later passes
reconstruct, authenticate or explain none of them. Frozen snapshots remain
untouched; no historical result is reconstructed by this selection.

## Standing decision constraints

- ADR-0050 stays Proposed; P9 unproven; no mechanism selected. Frozen patches
  A/B/C, C's assertions, write-ahead and sticky-flag alternatives stay
  unapplied/unaccepted. Owner GCM/ancestry-profile and residual name-to-ID risk
  acceptance was design-only; human rearm/resolution is absent.
- One-profile/two-child authority is spent. No native build, launch, profile,
  security, helper, credential or provider operation is renewed. Retained
  profiles, fixtures, DACLs, labels, grants and ignored evidence stay untouched;
  no cleanup, sweep or permission reset. Hashes do not seal live metadata.
- Qualification: `Blocked`/`TargetsNotReal`, both allowances unused; Git/GitHub
  separate/unstarted; GCM unqualified. Repository access grants no branch push.
- Preserve defects, deviations, lost/pre-redaction output and uncertainty in the
  [feasibility note](../architecture/github-publication-source-write-denial-feasibility.md),
  [capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md),
  [native semantics note](../architecture/github-publication-native-io-semantics.md)
  and [execution evidence](../architecture/github-branch-publication-execution-evidence.md).
  Earlier excluded-artifact reads grant no new reads. Original/failed evidence
  stays frozen, including the 69-file 11/57 correction.
- Concurrent-replay 409, target-test 500, disposal `NullReferenceException`,
  unnamed Api 2067/2068 and interrupted reviewer synthetic run stay unexplained.
  Later passes prove neither flakiness nor irrelevance. TEST-48-01 cannot cover
  hung submissions; retention beside disposal failure is unproven.
- Increments 4/5/6 incomplete. Account/context, raw Git read, non-Windows limits:
  [current-work](current-work.md#open-risks). Production code and necessary tests
  are selected only for LOCAL-QUEUE-INSPECT-01 under ADR-0063's read-only contract.
  PLAN-COORD-01, AGENT-SUP-01, WORKSPACE-EXCL-01, GLOBAL-ADMIT-01,
  GLOBAL-LIMIT-01, LOCAL-COORD-01, LOCAL-INSPECT-01 and LOCAL-QUEUE-01 are
  independently verified published. Inspection grants no mutation, scheduling,
  executor lease, operation retry/resume, quarantine release, recovery, approval,
  commit or publication authority. No dependency, experiment, capture-limit or
  standards edit is selected.
