# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md),
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Current decision (2026-10-09): recorded local-delivery receipt

### Review decision: GO for the frozen recorded local-delivery receipt slice

R1-R4 are accepted. Publish only the reviewed 50-path snapshot (14 modified,
36 added at staging), including this planner record and the factual review
entry in current-work.md. Codex independently verified main, HEAD/local
origin/main/live main at 0505e26cb572b7e6fbb0c81d970747bbaed2310a and an
empty index before this decision. The client remains
1620e821b9dac08bda208fc23c94ecb1b8537a739b68cd298c3b19d23b763cf8.

Fresh independent review: serial solution build 0 warnings/errors; receipt Api
16/16; literal member-digest tests 4/4; full Architecture 52/52; five frontend
receipt files 103/103. All four unchanged independent frontend refusal probes
now pass. The real-host/Git reviewer probe's seven original single corruptions
all return Unavailable; baseline and every restoration return Available. No
production or executor test source was modified by Codex. The ignored probe
build emitted two cached NU1900 audit-source warnings, distinct from the clean
solution build and not fresh dependency-audit evidence. The executor's full
Application 4943, Api 1418, Vitest 2561 and canonical Chromium 13/journeys 5
are reported final-tree evidence, not independently repeated full suites.

The pinned run's recorded completion, scalar bounds, approval provider and
both review evidence snapshots now agree with the operation and executions.
The frontend refuses unsafe integers and non-null Unavailable payloads, and
ReceiptReading is in its own file. Digest formats, admission, storage, Git,
recovery and historical rows are unchanged. The receipt remains a check of
recorded consistency, not proof of repository reality or provider reliability;
mutually consistent corruption and the lack of approval digests remain limits.
All earlier failed evidence and mutation qualifications remain recorded.

Freeze a raw SHA-256/byte-size/status manifest after these two review-doc edits.
The executor must verify its hash, all 50 entries, exact Git inventory, empty
index, baseline/live refs and the key file hashes before staging. No snapshot
edit, regeneration, amend, force-push or remote reconciliation is authorized.
Commit the substantive slice and push main normally; verify HEAD/local origin
and independent live ref equality. Run serial build, digest tests 4, receipt
Api 16, Architecture 52, full frontend 2561, typecheck/lint/build, harness 87,
then one canonical browser matrix (Chromium 13, journeys 5), with fresh logs
and checked exit codes. Preserve and stop on any failure, without an unchanged
rerun for green or a success closure. Client hash and checkout must remain
unchanged/clean. On success, commit/push a current-work.md-only factual closure
within this slice's entry, distinguishing fresh and retained evidence, preserving
historical failures and limits, and embedding no closure commit SHA. Verify
remote equality again. This grants no next slice or increment/MVP completion.

### Historical review decision: NO-GO, correction round R1-R4

The returned slice remains UNSTAGED, UNCOMMITTED and UNPUSHED. Keep these
corrections in the same executor chat; no publication or next slice is granted.
Codex verified main, HEAD/local origin/main/live main at
0505e26cb572b7e6fbb0c81d970747bbaed2310a, an empty index and exactly 49 status
paths (14 modified, 35 untracked). The selection was byte-preserved at
281df6a2b4f173b8b6b92150c9e88ca3b2a437bbafd64f3e6c854e84edf08333 before
this review edit. The generated client regenerated to the ledger's reported
1620e821b9dac08bda208fc23c94ecb1b8537a739b68cd298c3b19d23b763cf8.

Independent checks: serial solution build 0 warnings/errors; receipt Api tests
15/15; literal member-digest tests 4/4; Architecture 52/52; the five receipt
frontend test files 87/87. These passing suites do not cover the defects below.
The initial sandbox testhost run aborted before connection; a frontend attempt
could not load three suites because worker temporary files were unavailable.
The checks subsequently ran outside that environment limitation. The reviewer
probe initially needed its test-host content-root manifest and working directory
corrected; those setup failures are not production results. Its restore emitted
NU1900 because package vulnerability sources were unreachable; the solution
build result above is separate, and no fresh dependency-audit success is claimed.

Codex used an ignored independent probe over the real local-commit host and a
completed two-recipe delivery. Baseline and every restoration were Available.
Each following single corruption still incorrectly returned HTTP 200/Available:
run Lifecycle changed to Running; approval ActorAgentProvider changed from the
review attempt's Codex to ClaudeCode; selected Human evidence exit changed to
17 or execution number to 999; Agent review evidence changed to Failed or a
different fingerprint; operation ChangedPathCount changed to -1. Each table
was restored through the fixture's reversible corruption helper. No repository
implementation or executor test was changed by the reviewer.

**R1 - recorded completion and scalar coherence.** The query reads only the
run's project/objective, and the reader accepts invalid delivered counts.
Require the pinned run's recorded completion to agree with the completed
operation and validate the existing scalar bounds of receipt facts, including
ChangedPathCount 1..LocalCommitOperation.MaximumChangedPaths and positive
checkpoint/attempt/execution numbers. Classify incoherent stored completion as
Unavailable/null, including unknown stored lifecycle values, without a 500.
This is historical record consistency, not current invocation eligibility:
do not consult the latest run, live Git, a current lease or workspace status.
Keep legitimate later runs/checkpoints/recipes and historical receipt controls.
The architecture test's blanket ban on RunLifecycle must not prohibit this
required pinned-run consistency check; replace that overbroad assertion with
the actual no-current-eligibility/no-write boundary. Change no writer or recovery
decision. Add failing-first real-host cases and restoration controls.

**R2 - pinned approval and review snapshot coherence.** Bind the approval
message's ActorAgentProvider to its recorded CodeReviewer attempt. For both the
pinned Agent checkpoint review and selected Human review, compare every stored
verification snapshot to the corresponding pinned execution: command/execution
identity, execution number, status, fingerprint, outcome and exit code. Membership
alone, or Human Passed/fingerprint alone, does not prove consistency; the unchanged
Human member digest does not cover those snapshot fields. Reject the entire
receipt as Unavailable/null on a mismatch, never overwrite a snapshot or display
the execution row as a substitute for it. Keep digest bytes and admission
unchanged. Add each reproduced mismatch plus outcome/missing clean-exit controls
for both review forms, and prove intact legacy/single and complete-set deliveries
still read and restore byte-identically.

**R3 - safe frontend integer validation.** Number.isInteger admits values that
cannot represent exact identities/counts. Independent refusal probes all failed
for 2**53 in ChangedPathCount, CodeReviewer AttemptNumber and verification
ExecutionNumber: each normalized as Available. Require safe integers for receipt
numbers and the source checkpoint number, preserving their positive/nonnegative
and ordering rules. Reject invalid responses through the existing inconsistent
state with no receipt facts. Pin the existing changed-path bound and coherent
controls; no generic decoder or generated-client edit. An additional probe found
that Unavailable with receipt=0 is accepted by truthiness; reject falsy non-null
payloads while preserving null/undefined as the generated client's representation
of an absent receipt. No extra state or retry is needed.

**R4 - one top-level C# type per file.** ReceiptReading is a second top-level
type in LocalDeliveryReceiptTestBase.cs. Move it verbatim to ReceiptReading.cs
in the same test folder, with visibility and namespace unchanged. No broad moves.

Write the focused regressions first and capture their failures on this submitted
tree. After correcting it, run affected tests, a clean serial build and the
relevant full Application/Api/Architecture and frontend validation prescribed
below, harness then one canonical browser matrix. Preserve actual failures;
retain unchanged Domain/Infrastructure/audits with explicit scope. Repeat only
meaningful affected mutations, restore hashes and distinguish build/setup errors
from kills. Update ADR-0032/spec/protocol narrowly where claims need clarification
and current-work.md with fresh versus retained evidence and the new inventory.
Return the full unstaged diff for review. Do not change this planner record,
accepted ADRs, admission/digest formats, schema, historical rows or publication
state. The existing selection and its limits below remain in force.

Select exactly one bounded slice: a read-only historical receipt for a completed
host-owned local commit, showing its exact recorded checkpoint, review/approval
identities and verification executions in the cockpit, including after reload.
Claude implements in one NEW executor chat; Codex owns architecture and review.
This selection grants no staging, commit, push, next slice or new mutation
permission. The previous storage slice is published and independently verified.

### Verified baseline and prior publication

Branch main; HEAD, local origin/main and independently queried live
refs/heads/main equal 0505e26cb572b7e6fbb0c81d970747bbaed2310a. The checkout
was clean before this planner edit: nothing staged, unstaged or untracked.
Substantive c27f6e54164068a5016bc4c91caf1ff5de3f2759 has parent
34f56b918a7eb6812282ed095d6ef52f37462754. All six committed blobs match the
frozen reviewed manifest's raw hashes and byte sizes. Closure 0505e26 has that
substantive parent and changes only current-work.md in its entry (+5/-1), with
no embedded closure SHA. Codex inspected that closure and verified the remote.
Reported post-publication build, Infrastructure 57, Api 41 and Architecture 47
are executor evidence; they were not rerun by the planner after publication.

Expected executor start: this same main/HEAD/local/live baseline, empty index,
only docs/roadmap/planner-handoff.md modified, no untracked files. Preserve this
selection and ADR-0029/0030/0031. Generated-client baseline SHA-256:
22074a066c91a1fb1dfc0afc602573f14e72f9c6ffd8047633b2640c20e20b64.
The additive receipt endpoint will legitimately regenerate that client.

### Why this outcome and what it does not complete

The existing LocalCommitPanel displays status, checkpoint number, branch,
parent/tree, changed-path count and the delivered SHA. GetLocalCommitStatus
returns LocalCommitOperationView; neither contains the exact verification
execution list or the source report and approval evidence together. Those facts
are already durably recorded in LocalCommitOperation, LocalCommitAuthorityMember,
AttemptVerificationEvidence and checkpoint-review evidence. The collaboration
journey already completes a real local commit against two enabled recipes.

One receipt lets the owner answer which recorded checks and decisions supported
this particular local delivery without reconstructing several current-status
panels. It advances the roadmap's final evidence summary using the delivered
local-commit capability, with no autonomous coordinator or external authority.
It is more useful now than another incidental test-support slice. Claude
account enforcement, eligible session resume, compaction and permission profiles
remain unproven; this work establishes none of them. Remote publication and
broader lifecycle recovery require separate decisions. No increment or MVP
completion is claimed.

### Selected read contract

1. Add one protected, bodyless GET /api/runs/{runId}/local-delivery-receipt,
   one MVC endpoint sending GetLocalDeliveryReceiptQuery through the mediator,
   API-owned transport types and a reproducibly generated TypeScript client.
   No run returns 404. An existing run returns one closed state: NotRecorded
   (no operation), NotCompleted (known non-completed operation), Unavailable
   (the completed receipt cannot be reconstructed coherently), or Available.
   Only Available carries a version-1 receipt; every other state carries null.
   Unknown or contradictory recorded state is never treated as completion.
2. Available contains safe historical facts: run/operation identity and objective,
   completed local commit SHA, parent/tree, owned branch, completion UTC time,
   checkpoint ID/number/fingerprint and changed-path count; the pinned execution
   report ID, CodeReviewer attempt ID/number and approval-message ID; the selected
   Human checkpoint-review ID and Approved decision; and an ordered list of the
   exact recorded verification command/execution IDs, execution numbers,
   snapshotted command names, Passed/clean-exit facts and completion UTC times.
   Do not return executable/workspace paths, arguments, author email, raw messages,
   provider output, artifacts, lock receipts or credentials. No download/export
   feature or new history-navigation framework is selected.
3. Read the operation and its pinned relational sources untracked. Completed
   operation/run/checkpoint ownership must agree. Resolve the report, actual
   approval message, CodeReviewer attempt and selected Human review by the
   recorded IDs, with same-run/project/workspace/checkpoint checks appropriate
   to each row. Do not substitute the latest report, review, checkpoint, enabled
   recipe or verification execution. A bare FutureAgent review is not the
   recorded CodeReviewer approval. Do not infer an implemented-plan identity
   from report replies or silently upgrade historical root-target reviews.
4. Verification membership comes from that operation's Verification authority
   members, ordered by recorded Sequence, not current recipes. Require 1..32
   unique execution/command pairs with contiguous zero-based order and exact
   equality to the pinned CodeReviewer's ordered evidence and selected Human
   review's complete evidence set (the latter is set equality). Each pinned
   execution must belong to the recorded project/workspace/checkpoint, be
   Passed with clean exit and coherent recorded fingerprints/completion, and
   match its recorded member digest using its immutable command snapshot.
   The selected Human decision and its evidence must match its pinned digest.
   Missing, foreign, duplicate, reordered, over-limit or inconsistent evidence
   makes the entire receipt Unavailable; never show a partial Passed receipt,
   silently truncate, replace a missing member or synthesize zero/defaults.
5. The digest serialization already used at admission remains unchanged. A
   minimal pure LocalCommit-specific digest helper may be extracted only to
   share that same recorded format between admission and historical reading;
   pin its compatibility. Do not reuse LocalCommitAuthorityReader's current
   eligibility path or change any admission/execution/recovery decision.
   The receipt identifies the selected Human decision and verification set;
   it is not a new audit of every historical Human decision or a new authority
   snapshot. No new persistence, backfill, migration or event is required.
6. Read only persisted facts. No Git, filesystem, process, provider, lease
   acquisition, SaveChanges, event, cleanup or recovery action is triggered.
   Later recipe edits/disabling/deletion, verification reruns, a newer run or
   checkpoint, and current workspace/branch changes must not substitute evidence
   or invalidate an otherwise intact historical receipt. Do not re-prove live
   physical identity or require the current lease to remain active. The receipt
   describes recorded delivery, not current cleanliness, invocation eligibility,
   remote publication, provider obedience or verification re-execution.
7. Show a clearly named Local delivery receipt region alongside the completed
   operation. Keep the existing LocalCommitPanel/form and status semantics.
   Render names/objective as plain text, with a fixed historical/local-only
   explanation. Loading, failed, unavailable and absent states are explicit,
   never a success-looking partial summary. A receipt request belongs to the
   committed run/operation lifetime: replacements, A-B-A, unmount, retained
   callbacks and overlapping reads cannot fetch for or display an obsolete
   owner. Reject mismatched run/operation identities or contradictory transport
   shapes through a small receipt-owned normalizer, not a generic decoder.
   Reuse existing ownership utilities; no polling, cache, remount workaround,
   mutation button, automatic claim or automatic retry is added.

### Scope, architecture and stop gates

Allowed: the Application query and minimal feature-local read/digest policy,
API endpoint/complete transport mapping, generated client, receipt hook/component
and their composition, focused tests, a narrow extension of the existing
collaboration journey and documentation. Add ADR-0032 recording this selected
additive read-only contract; update its index, engineering context, protocol,
cockpit spec, roadmap/workflow model only where needed and the delivery ledger.
Preserve accepted ADRs and previously sealed artifacts/history.

Excluded: new Domain behavior, schema/migration/snapshot, provider contract or
model/account control, commands/eligibility/authorization changes, stage
coordination, lifecycle transitions, Git/native/ref/index/lock/lease behavior,
artifact cleanup/retention, janitor, paths or raw artifact delivery, project
publication, new dependencies and generic reporting/export infrastructure.

Stop if historical evidence cannot be identified from the existing pinned rows
without choosing latest state, inventing authority, altering recorded facts,
requiring new storage, changing a digest format or adding external work. Report
that concrete gap rather than displaying a plausible receipt. A pure compatible
digest extraction is the only allowed admission-source edit. Do not fix an
unrelated test failure opportunistically or rerun unchanged acceptance for green.

### Acceptance evidence and return

- Write focused regressions before implementation. Demonstrate the new receipt
  is absent on the parent, then real file-backed SQLite examples for one and
  several checks, exact order and IDs, legacy single-review and complete-set
  Human approvals, all four states and unrelated-run isolation. Cover pinned
  missing/foreign/changed/reordered/extra evidence and digest tampering. A newer
  verification, recipe change or later checkpoint must leave the historical
  receipt intact. Prove the query causes no new rows/events or external calls.
- API evidence uses normal authentication, explicit authorization inventory,
  404 and closed-state/null contracts, and actual OpenAPI/generated-client
  serialization. Domain/Application entities must not leak in the transport.
  Include a completed operation produced through the real local-commit host,
  not only SQL seeds claiming completion; no real provider is required.
- UI tests cover owned loading/error/unavailable/valid responses, malformed
  counts/membership/identities, first-frame A-B and null transitions, A-B-A,
  obsolete refreshes, unmount and newest-read wins. The completed status still
  means local delivery only; receipt failure neither rewrites it nor offers a
  new mutation.
- Extend collaboration.journey.ts after the existing real local commit: assert
  the recorded SHA/checkpoint/reviewer/Human decision and both exact execution
  members/names before and after the existing final reload. Keep its earlier
  source-isolation, no-Agent-commit and membership assertions. Snapshot that
  reading the receipt creates no extra Agent, verification or commit operation.
  Keep normal authentication/composition, all supervisors and owned cleanup.
- Detect meaningful rebuilt mutations of pinned-vs-latest selection, exact
  membership/digest/ownership checks, incomplete-as-success and UI lifetime
  ownership. Restore source hashes; distinguish equivalent survivors and build
  failures from kills. Avoid a giant matrix of redundant guards.
- Run affected checks first, then a clean serial solution build, full
  Application, Api and Architecture suites, full Vitest, typecheck, lint,
  production build, strict e2e tsc, harness and one canonical test:e2e:all after
  harness success. If the digest extraction touches an additional consumer,
  include its affected tests. Retain unchanged Domain/Infrastructure/audits
  with actual scope. Check affected formatting, tracked and untracked hygiene,
  links and byte-identical client regeneration on a second generation.
  Preserve failures and real skips; never claim an unchanged green rerun fixed
  an intermittent failure or proves real-provider reliability.

Return the complete unstaged, uncommitted and unpushed diff, exact inventory,
commit-ready current-work.md entry, actual commands/counts, failures, fresh vs
retained evidence and remaining limits. Do not embed a future SHA. Keep all
corrections in this executor chat. Codex alone gives GO/NO-GO; selecting this
slice is not publication authorization.
