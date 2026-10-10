# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md),
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Current decision (2026-10-10): substantive delivery verified; GO for factual closure

Codex independently verified substantive commit
5fd38cb0f0c6fc50c35d39e6af9c85cc2acf7193, parent
cac61f8a6ea5f4e55d424af0fc6c8c91ff7f9a0f, message
`feat: browse project run history and prior delivery receipts`. Branch main,
HEAD/local/live origin/main agree and ahead/behind is 0/0. Its 43 committed paths
(15 modified, 28 added), normalized blob identities and file modes match the held
publication manifest; no extra path or ignored manifest was committed.

The executor returned only five added lines in current-work.md, unstaged and
uncommitted, recording the delivered substantive SHA and publication facts.
The closure correctly distinguishes retained validation from fresh Git checks
and preserves the historical failures, unconfirmed diagnoses and release limits.
Codex accepts it without correction and updates this planner decision.
**GO is only for these two reviewed documentation files**, current-work.md and
planner-handoff.md, on main at the verified substantive commit, with an empty
index and no untracked files. The executor prompt supplies the frozen two-path
manifest and its hash. Stage only those bytes, commit and make one normal
fast-forward push; stop on drift, failure or divergence. Report the closure
commit SHA externally, without adding another closure entry for that doc commit.
No runtime suite is rerun for these documentation changes.

### Next bounded proposal, not a selected slice

The roadmap still requires run replay (Increment 5 and the self-hosting restart
proof). ADR-0033 delivers metadata/receipt discovery and explicitly leaves full
event replay open. GetRunEventsQueryHandler currently reads every event after
AfterSequence with ToListAsync and no Take; useCollaborationTimeline is driven
by live event changes, so neither is a ready bounded historical reader.

Prepare a separately assessed slice for explicit, bounded pages of persisted
events for a selected historical run, with stable sequence cursors, safe typed
disclosures and owned UI lifetimes, without mounting live workflow controls or
automatically draining the journal. Before selecting it, settle the page/snapshot
contract, supported event disclosures and compatibility with ADR-0003 and the
existing consumers. Do not represent a first journal browser as complete replay.
This proposal advances required history/replay work and avoids a real-provider
dependency; it does not displace the provider, coordination, recovery, remote
publication and packaging release gates recorded below. It is not permission
for executor research or implementation in this chat. After closure publication
verification, Codex owns the final selection and complete new execution prompt.

Real qualification remains Blocked/TargetsNotReal, both allowances unused.
Preserve the ledger, historical failures and retained roots. No provider launch,
CLI setup, reset, cleanup or increment/MVP completion is authorized or implied.

## Substantive publication decision (2026-10-10): GO for the corrected run-history slice

Codex independently reviewed the combined owner-requested transition documents,
ADR-0033, history implementation and correction diff. The two correction findings
are resolved: feature-local scalar binding and fixed validation messages produce
safe shared errors with explicit 400/404 schemas and generated typed responses;
detail-close actions have a committed selection lifetime, including A-B-A and
close/reselect. The existing receipt and workflow authority remain unchanged.

**GO is limited to the reviewed 43 paths**, including this planner decision and
current-work.md: 15 modified tracked files and 28 new files. The expected branch
is main, HEAD/local/live origin/main are
cac61f8a6ea5f4e55d424af0fc6c8c91ff7f9a0f, and the index is empty. AGENTS.md and
slice-start-template.md retain their accepted owner-requested bytes. A frozen
publication manifest records each approved path/status/raw hash/size and its
Git-normalized blob identity; the executor prompt supplies its path and hash.
Do not edit or regenerate approved bytes before staging. Return material drift
for review; the old support GO does not authorize this publication.

Fresh independent correction review: serial solution build 0 warnings/errors;
Application history 14/14, Api error-contract 31/31, Architecture 58/58 and all six
frontend history files 122/122. Normal build generation reproduces client SHA-256
513c287e435a3777a8618769648de8128f1080240d0fd3e2b03d507f3e549061.
The preceding independent Api history 48/48 and code review are retained. The
executor reports corrected full Application 4957/4957, Api 1788/1788, Architecture
58/58, Vitest 2685/2685 in 169 files, harness 87/87, Chromium 13/13 and journeys
5/5, plus the recorded typecheck/build/lint/format/link/hygiene/audit results.
These full runs are executor evidence, not independently repeated reviewer runs.

The earlier Api 1755/1757 and isolated 2/2 remain historical evidence. Bounded
diagnostics reproduce OriginUnresolved under ambient orphan process activity,
but establish neither original cause; the pipe race remains an unconfirmed
hypothesis. No relevant guard or test was weakened. The corrected full run is
green, so the review validation gate is satisfied without an unrelated fix or
failure waiver. Preserve both observations as remaining test reliability risks.
Existing dependency advisories, capability skips and duplicate-key observations
retain their recorded scope; this GO does not claim a clean release audit.

Claude may stage only the frozen reviewed inventory, commit the substantive slice
with current-work.md, then make one normal fast-forward main push and verify
HEAD/local/live origin/main and the committed inventory. Stop on push failure or
divergence; no force-push, history reconciliation or unrelated work is authorized.
After verified publication, prepare only a factual current-work closure diff
recording the actual substantive commit and remaining state, and return it
uncommitted for review. Do not place a commit's own SHA in that same commit.

No next slice is selected before publication is independently verified. Codex
retains the next-slice decision. Real qualification remains Blocked/TargetsNotReal,
both allowances unused, and all release/MVP gates in the selected-slice assessment
below remain. No real provider, CLI setup, ledger reset, retained-root cleanup,
publication/CI feature or increment/MVP completion is authorized or implied.

## Previous review decision (2026-10-10): NO-GO; correct the selected run-history slice

Codex remains planner/reviewer and Claude remains executor. Keep corrections in
the existing executor chat. No different slice, staging, commit or push is
authorized. The owner-requested collaboration/transition content remains accepted;
the combined implementation diff requires the corrections and evidence below.

Independent Git verification found main, HEAD and local/live origin/main at
cac61f8a6ea5f4e55d424af0fc6c8c91ff7f9a0f, an empty index and 40 changed paths:
15 modified tracked files and 25 untracked new files. AGENTS.md and
slice-start-template.md are byte-preserved. The submitted planner record was also
byte-preserved; this review now updates that record. The inventory is in the
current-work entry; no executor implementation file is changed by this review.

### Required corrections and validation gate

1. **Safe, explicit HTTP error contract.** Malformed/overflow query scalars must
   return the shared ApiProblemDetails/ApiError validation contract without
   echoing rejected values. Declare the actual 400 and 404 schemas in OpenAPI
   and regenerate the client from the real document. The current int? binding
   returns framework ValidationProblemDetails with the rejected value; the
   operation advertises only 200 and its generated error branches are untyped.
   Follow the engineering results/errors and API-client contracts locally to
   this feature. Existing routes with the same omission are not a waiver. Do
   not change global MVC behavior or unrelated routes without discussing scope.
   Preserve default/range/exclusive-cursor behavior and numeric public scalars.
2. **Selected-detail lifetime.** A retained detail A close callback currently
   clears selection B and also a newer selection A after A-B-A. It closes over
   select(null), which is owned only by the open history lifetime. Give detail
   actions a committed selected-row lifetime using the existing owned utilities;
   an ID equality check alone does not cover A-B-A. Preserve receipt behavior
   and avoid key/remount workarounds. Add regressions through the real panel
   wiring for both stale-close cases and a working current close.
3. **Unresolved full Api failures.** Preserve the reported 1755/1757 full run and
   isolated 2/2 diagnostic. Load sensitivity is a hypothesis, not established
   cause. Provide original failure excerpts and bounded diagnostic evidence for
   the fixture stdin pipe closure and native process-origin proof failure. Do not
   weaken ownership refusal or conservative OriginUnresolved behavior, skip
   tests, increase retries/timeouts for green, or fix unrelated code in this
   correction. After the actual corrections, one relevant full Api validation
   is justified by the changed tree; a repeated unrelated failure remains a
   stop/report gate requiring a separately reviewed diagnosis or disposition.

### Independent review evidence and next return

Reviewer build: serial solution build, 0 warnings/errors. Submitted history
tests: Application 14/14, Api 48/48, Architecture 58/58 and frontend 109/109 passed
independently. Five temporary reviewer
probes failed as expected on the submitted tree: two retained-detail close cases,
two missing API response schemas, and one rejected-value disclosure. Those probe
sources were removed after recording the findings. The frontend host run followed
a sandbox worker temporary-module failure; that infrastructure failure is not
reported as a product assertion failure. No full-suite green rerun or real-provider
qualification was performed by the reviewer. Executor full-suite, journey, mutation
and audit evidence remains reported evidence with its recorded limits.

Run affected regressions first, then the relevant full validation on the corrected
tree under the existing selected acceptance contract. Return the complete diff,
updated current-work evidence, exact inventory and remaining failures/risks for
planner review. Distinguish new checks from retained checks, and regenerate twice
to prove client reproducibility. Preserve the transition, qualification ledger,
historical failures and retained roots. Real qualification remains
Blocked/TargetsNotReal; both real allowances remain unused. Neither this correction
decision nor discussion grants publication authority or MVP completion.

## Selected slice (2026-10-10): read-only project run history and prior local-delivery receipts

Codex is the planner/reviewer; Claude remains the executor. The owner-requested
documentation transition is accepted without correction findings. Its collaboration
rules preserve the engineering contract and publication boundary. Carry those edits
into the selected slice for review together with its final diff; no staging, commit
or push is authorized now. The previous support GO applies only to that published
support, not these documents or the selected implementation.
Publication disposition is **NO-GO now**: implement the bounded selection below
and return the combined diff for review. This is not a defect finding against the
owner-requested rules; their content is accepted and needs no correction.

### Fresh verified preflight and preserved evidence

Branch main; HEAD, local origin/main and an independently queried live
refs/heads/main equal cac61f8a6ea5f4e55d424af0fc6c8c91ff7f9a0f. Empty index,
no untracked paths, exactly four modified tracked documents: AGENTS.md,
docs/roadmap/current-work.md, docs/roadmap/planner-handoff.md and
docs/roadmap/slice-start-template.md. This selection changes only this planner
record and preserves the other three documents. The closure has substantive
767eede2b252f00bf7df1200a8bd767f292acc7c as its direct parent and changes only
current-work.md. Git diff hygiene is checked; no runtime suite is rerun for this
planning/documentation review. The generated-client baseline SHA-256 is
1620e821b9dac08bda208fc23c94ecb1b8537a739b68cd298c3b19d23b763cf8.

The real qualification remains Blocked/TargetsNotReal, with both real allowances
unused. No provider relaunch, CLI installation/login/configuration, ledger reset,
allowance consumption or retained-root cleanup is selected. Preserve the ledger,
historical failures, observation limits and retained fixtures. Published support
and deterministic doubles establish neither CLI compatibility nor provider
reliability, and no increment/MVP completion is claimed.

### Evidence, priority and remaining MVP gates

GetProjectRunSummariesQueryHandler selects one most-relevant run per project.
App.tsx can open only that run in RunCockpitView. ADR-0014/0031 permit subsequent
objectives, but there is no project-run-history query or selector. ADR-0003's
durable records and ADR-0032's pinned receipt remain readable by run identity;
the owner cannot discover and inspect an older delivery through the normal UI
after another objective replaces the selected run. Agent attempt history is
within one run and does not close this gap.

Select the first bounded project-run-history surface: browse persisted objectives
and inspect an older recorded local-delivery receipt. This advances required MVP
history access and the already delivered local evidence summary. It adds no
workflow stage, coordination or mutation and does not extend ADR-0029's sequencing
exception to autonomous or remote work. A separate read-only region avoids mounting
RunCockpitView's current eligibility, configuration and mutation controls for an
historical selection. Full event replay is still open, not implied by this slice.

Release/MVP gates remain: actual installed-provider compatibility and real
collaboration evidence; the unproven Increment 4 contracts for Claude account
usage, eligible sessions, compaction and additional safe permission controls;
Increment 5 coordination, bounded global concurrency/queueing, pause/resume/stop,
retry/takeover and broader recovery; complete history/replay; Increment 6 scoped
publication, exact-SHA CI observation/correction and ambiguous-action recovery;
and Increment 7's stable packaged self-hosting demonstration. Existing claim,
reserved-time, token-activity and Codex account guards are implemented and are not
being represented as absent. Runtime preflight still reports unknown capabilities.
Security-plan rows marked Planned are not proof that their controls are missing:
release evidence must be reconciled with code/tests. Retained dependency findings
and capability skips keep their actual scope and need release disposition, not
an invented clean audit. None of these gates is waived here.

Optional work includes receipt export, analytics, visual redesign, generic history
infrastructure, Gemini/fallback, parallel candidates, stronger optional OS/container
isolation and automatic updates. None is selected to displace required MVP work.

### Selected contract and scope

1. Add one protected bodyless GET /api/projects/{projectId}/run-history through
   GetProjectRunHistoryQuery and the mediator, with API-owned transport types and
   reproducible generated-client output. Unknown project is 404; an existing empty
   project is a successful empty page. Use an exclusive beforeExecutionNumber cursor,
   positive when present, and limit default 10, range 1..20. Order by descending
   ExecutionNumber, backed by the existing unique project/number index; take one
   extra row for HasMore and return the last admitted number as the next cursor only
   when older rows remain. No offset, timestamp ordering, total-count scan, implicit
   clamping or automatically draining all pages.
2. Each entry carries only persisted run/project identity, execution number,
   objective, known lifecycle/stage or fixed Unrecognized disclosures, exact stored
   execution-mode disclosure, creation and last-advance UTC times, and an optional
   completed-local-delivery receipt source. Unknown lifecycle/stage/mode must not
   drop the row, coerce it to a known state or fail healthy neighboring rows. Classify
   enum columns without materializing an unknown enum; reuse the exact execution-mode
   storage read. Do not load full Run entities or change storage/mappings.
3. The optional source contains only run/operation/commit/checkpoint identity and
   checkpoint number required by the existing ReceiptSource. Read it from that run's
   own recorded completed operation, with same-project/run ownership, recorded
   Completed run/stage and structurally valid source fields. It locates a receipt,
   never certifies its availability or approval. When no coherent completed source
   can be identified, return null and explain that no completed delivery source is
   available in this view; never infer no operation, failed delivery or success.
   Do not duplicate receipt reconstruction/digests or consult current eligibility.
4. Add a collapsed-by-default Run history region for the selected project. Opening
   it requests one page; explicit actions read older pages or reload the first page.
   Selecting a row shows its recorded objective/status/mode/times and, when it has a
   valid source, the existing LocalDeliveryReceipt component/hook/normalizer.
   Preserve ADR-0032's exact source checks, read-once behavior, unavailable/error
   semantics and explicit retry. Do not relax its decoder or change its endpoint.
   A metadata snapshot is not live progress, delivery success, current workspace,
   current verification or publication. Keep the live cockpit/current project
   selection and intake behavior unchanged.
5. History reads and selection belong to the committed project/open/selected-row
   lifetimes. A-B-A, close/reopen, null selection, unmount, retained callbacks and
   overlapping reads must not start obsolete work or display an old owner's frame.
   Reuse useOwnedLifetime/useOwnedState; no key/remount workaround. Validate response
   project identity, unique run IDs/numbers, safe positive integers, descending order,
   page size, cursor coherence and source/run identity before accepting a page. An
   invalid page is an explicit error, never a plausible partial page. A failed older
   page retains only valid rows of its own lifetime and retries that same cursor.
   Reload starts a fresh first-page/selection lifetime. Render all values as text.
6. New history behavior reads only persisted facts untracked, writes nothing, and
   invokes no Git, filesystem, provider, process, readiness refresh, lease, recovery
   or mutation action. No live cockpit/action hooks are mounted for a historical
   selection. No event playback clock, timeline extension, artifact viewer, current
   worktree panel, export, router/cache framework or browser persistence is added.

Allowed changes: the Projects history query and operation-local projection/validation,
its MVC endpoint/transport graph, generated client and client factory, cockpit history
hooks/normalizer/components/styles and minimal App composition, focused tests and a
narrow extension of the existing collaboration journey. Add ADR-0033 recording this
additive contract, update its index/engineering context/cockpit spec/roadmap narrowly,
and update current-work.md for delivery while preserving its transition and history.
Do not edit this planner record in the executor chat or the accepted ADRs 0001..0032.
The pre-existing AGENTS.md and slice-start-template.md edits remain byte-preserved.

Excluded: Domain behavior, schema/migrations/mappings, existing admission/dispatch/
completion/recovery/approval algorithms, provider contracts or configuration, scheduler,
autonomy, mutation controls, Git/native/index/ref/lock behavior, cleanup/retention,
remote publication/CI, full replay, new dependencies or unrelated refactoring.

Stop and report a concrete gap if the existing rows cannot supply this metadata/source
without inventing history, changing storage, weakening receipt identity/digest rules,
using latest/current authority, adding external work or expanding a deferred lifecycle.
Stop on a material Git discrepancy or an unrelated failure; preserve evidence and
propose a bounded alternative for discussion. Disagreement pauses dependent work;
neither agreement nor selection grants publication authority.

### Acceptance evidence and return

- Focused regressions before implementation: missing and empty projects, stable
  project-isolated multi-page ordering/cursors, invalid limits/cursors, new insert
  between pages, all existing lifecycle/mode disclosures and unknown stored enum/mode
  controls that retain their row. Use real file-backed SQLite and normal authentication.
  Show protected absent/wrong-credential behavior, ownership, safe 404/400 contracts,
  explicit authorization inventory and actual OpenAPI/generated serialization.
- Prove completed receipt sources use the pinned operation, never another run's
  delivery or current checkpoint, and null sources are disclosed without inventing an
  outcome. Include a production-created local commit; reuse the existing fixture and
  receipt regressions instead of manufacturing SQL approval authority. Verify history
  and receipt reads create no rows/events/claims and call no external ports.
- Frontend tests cover empty/loading/error/paging/retry/invalid responses, first-frame
  project and row replacement, A-B-A, close/reopen, unmount, retained actions, overlapping
  reads and source changes. The historical region offers no mutation/configuration
  controls and has no live timer/polling. Existing cockpit actions remain unchanged.
- Extend collaboration.journey.ts after its real local commit and receipt assertions:
  create a subsequent manual objective through the normal UI/API, reopen the older run
  in history, verify the old SHA/checkpoint/exact verification members, reload and repeat.
  Keep existing isolation and no-Agent-commit checks. Reads add no Agent, verification
  or local-commit operation; the intentional new run/intake event are distinguished.
  Use normal Program composition, authentication, all supervisors and owned doubles.
- Detect a small set of meaningful rebuilt mutations of project scoping, exclusive
  cursor/pinning and UI lifetime ownership; restore source hashes, distinguish build
  failures/equivalent survivors from kills. Do not add a redundant guard matrix.
- Run affected tests first, then a clean serial solution build (normal client generation),
  full Application, Api and Architecture suites, full Vitest, typecheck, lint, production
  build, strict e2e tsc, harness and one canonical test:e2e:all after harness success.
  Include affected formatting, second byte-identical client generation, documentation
  links, tracked/untracked secret/hygiene checks and dependency audits. Preserve actual
  advisory/failure/skip results; no unchanged rerun for green. Retain unchanged Domain
  and Infrastructure evidence explicitly unless touched by the agreed implementation.

Return one complete unstaged, uncommitted, unpushed diff including the four starting
documents, a short current-work entry with actual commands/counts and fresh versus
retained evidence, exact changed-file inventory, failure excerpts and remaining limits.
Keep corrections in this one new Claude executor chat. Codex reviews the combined diff
and gives explicit GO/NO-GO before any staging, commit or normal main publication.

## Historical transition (2026-10-10): support published; transition to a new planner/reviewer chat

The manual qualification support slice is published. Independent read-only Git
verification in this transition found branch main, HEAD, local origin/main and
live refs/heads/main all at
cac61f8a6ea5f4e55d424af0fc6c8c91ff7f9a0f, with a clean checkout before the
documentation edits below. Its parent is the substantive support commit
767eede2b252f00bf7df1200a8bd767f292acc7c, whose parent is
88b9ffb0dfbf399d93ce4c38c742eb56f95847fb. The closure changes only
current-work.md. The delivery ledger records the executor's fresh post-publication
checks and retained evidence; this transition did not repeat runtime checks.

The owner requested new collaboration rules and a new planner/reviewer chat
before selection of the next slice. [AGENTS.md](../../AGENTS.md) now requires a
complete copy-and-paste prompt after each planning/review analysis. The executor
may challenge requests and findings with evidence and alternatives; both roles
must discuss toward agreement, and the planner must reconsider when warranted.
Unresolved material choices go to the owner. Scope, safety, quality and explicit
publication authority remain governed by the standing contract.

The transition is documentation-only and intentionally remains unstaged,
uncommitted and unpushed on that baseline. Its exact four modified paths are
AGENTS.md, docs/roadmap/slice-start-template.md, this planner record and
docs/roadmap/current-work.md; there are no staged or untracked paths. The next
planner must verify that state, preserve these owner-requested edits, and resolve
their review/publication separately from selecting implementation work.

No next slice is selected or dispatched here. The new planner/reviewer must read
the roadmap and accepted ADRs, inspect the current code and evidence, and then
select a bounded next outcome. Do not present support delivery as qualification
or increment/MVP completion. The original real session remains
Blocked/TargetsNotReal and both real allowances are unused, as recorded in the
delivery ledger. Compatibility of the installed CLIs with the adapters remains
unproven. A real relaunch or environment installation/login/configuration needs
a separate decision; this transition authorizes none of them. Preserve the real
ledger, historical failures, retained evidence and prior fixture roots.

### Historical final review: GO for the frozen manual qualification support snapshot

#### Historical review and bounded publication authority

Codex accepts R1-R5 and both rehearsal corrections. Main, HEAD, local
origin/main and an independent live refs/heads/main match
88b9ffb0dfbf399d93ce4c38c742eb56f95847fb; the index is empty. The returned
inventory is 75 paths (four modified tracked, 71 untracked), including this
planner record and current-work. The executor preserved the prior planner
record (ba3eb6a8451311d0e7e54f152e750a7c4e6e330165f153719a095434ca84835d),
ADR-0029 and the generated client. This GO supersedes that historical planner
hash and the historical NO-GO decisions below.

The environment now retains its exact created root and independently generated
token. Tests neither discover cleanup candidates by directory-set difference
nor read a candidate marker to mint expected ownership. Native uncertainty
preserves that exact root, without a disposal/finally override. The separately
labelled offline scripted teardown requires the held root, a scripted observation,
bounded host stop, measured exits of both owned doubles and a preserved root;
it never applies to a native observation. The runtime cleaner, accepted
ChildProcessWatch policy and manual executable's native default are unchanged.
The scoped reference and test-only teardown are accepted, not provider confinement
or a manual cleanup bypass.

Fresh independent reviewer evidence on the final executable tree: serial
solution build with --no-restore, UseSharedCompilation=false and -m:1 passed
with zero warnings/errors; the full offline ManualQualification selection passed
291/291 and Architecture 52/52, zero failures/skips. The tests ran on the normal
Windows host with owned doubles, scripted observations and native helper controls;
no real provider was called. git diff --check passed, and the generated client
retains 1620e821b9dac08bda208fc23c94ecb1b8537a739b68cd298c3b19d23b763cf8.
The prior reviewer fixture root remains untouched. The real ledger still contains
only its saved Blocked/TargetsNotReal summary and no allowance files.

Freeze the reviewed 75-path snapshot after these review records. Publication is
authorized only when the frozen manifest's own hash, exact statuses, every raw
file hash/size, branch, refs and empty index match. Stage only those listed paths,
check the staged inventory and git diff --cached --check, commit the substantive
support and reviewed documentation, and push main normally to origin/main.
No source/doc regeneration, amendment, force push, history reconciliation or
unreviewed change is authorized. Stop on any mismatch or failed required check.

After remote verification, run once and sequentially from fresh logs: serial
solution build, the full offline ManualQualification selection (291 expected),
full Architecture (52 expected), npm run test:harness (87 expected), then one
canonical npm run test:e2e:all (Chromium 13 and journeys 5 expected). These are
offline checks only. Preserve complete outputs/counts, the native rehearsal's
actual outcome, any intentionally preserved fixture, client hash and diff state.
Do not repeat a failed unchanged run for green or silently delete preserved roots.
Then make a narrow current-work-only factual closure with the substantive SHA,
verified publication, actual fresh/retained checks, remaining limits and preserved
failures; push and verify normally. Embed neither commit's own SHA in itself.

This GO publishes support, not a successful real-provider qualification. The
original session was Blocked/TargetsNotReal before POST or inference, and both
real allowances remain unused. No real session relaunch, CLI installation/login/
configuration, ledger reset, allowance consumption or new slice is authorized.
Environment readiness and any later manual launch need a separate decision after
verified publication. No increment or MVP completion is claimed. Preserve all
historical failures, the unproven cause of the prior 273-pass/1-failure run,
stale-binary qualifications, mutation scopes and the documented observation limits.

### Historical fifth review: rehearsal structure accepted; NO-GO on new test cleanup authority

Codex reviewed the fourth correction on main at
88b9ffb0dfbf399d93ce4c38c742eb56f95847fb. HEAD, local origin/main and live
refs/heads/main match; the index is empty and there are 72 status paths (four
modified tracked, 68 untracked). The executor preserved the previous planner
record (b1f700be55628e525b224fae44906759f8fc90fba3e375667c98a2ee14b03420),
ADR-0029 and the generated client. This new review supersedes that planner hash.
R1-R5 remain accepted. ChildProcessWatch is unchanged. The read-only offline
process-table seam, closed manual entry, deterministic proof inputs and exact
native safe-outcome assertions are appropriate; keep them. The prior reviewer
273-pass/1-failure run and its unproven cause stay preserved, not relabelled.

The new Rehearsal cleanup nevertheless introduces two concrete authority defects:

- PreservedRoots selects every newly appearing manual-prefixed directory in
  the global temp directory, excluding only the initial snapshot and the
  test's named sentinel. A concurrent foreign root created after that snapshot
  is selected as if it belonged to this rehearsal. RemovePreservedRoots then
  reads each selected directory's marker and uses that same value as the
  expected token for OwnedRootCleaner.Remove. A self-consistent foreign marker
  proves no independent ownership by this test. The initial-set difference,
  prefix and marker read cannot mint deletion authority.
- The native uncertainty branch explicitly calls RemovePreservedRoots after
  asserting ChildrenProven=false and exit 6. It therefore removes the root the
  accepted session gate deliberately preserved. Knowing a root's owner does
  not prove the unresolved process stopped; fixture teardown cannot silently
  bypass the selected native shutdown gate.

A non-destructive reviewer probe constructed the actual private Rehearsal
helper with an empty initial set, one candidate of its own, a separately created
concurrent foreign root and the excluded sentinel, all under a controlled
reviewer temp directory. PreservedRoots returned two candidates and included
the foreign root. No RemovePreservedRoots or cleanup was executed; both marker
files remain. The inspected method's deletion authority follows from reading
those candidates' markers. Probe code/output are ignored scratch under
node_modules/.cache/codex-manual-qualification-review-20261009/correction-review/,
including rehearsal-cleanup-candidates.txt. No user root was deleted.

Fresh reviewer build passed with zero warnings/errors; Architecture passed
52/52. The safe offline selection excluding ManualQualificationRehearsalTests
passed 275/275, zero failures/skips. The reviewer intentionally did not execute
the three rehearsals with this cleanup, and does not claim a fresh full 278
result. The executor's 278-pass run remains separately reported evidence.
git diff --check passed, protected hashes and the real ledger are unchanged.

### Bounded correction: retain ownership and honor native preservation

Use only the exact root this rehearsal created, held from its creation through
a trusted fixture/environment reference or capability. Keep its independent
ownership authority; never discover cleanup candidates by global set difference
or obtain an expected token by reading the candidate's own marker. A directory
snapshot may remain an assertion of isolation, never authority to delete.
Do not change the existing runtime cleaner or its ownership rules.

When the native proof is unproven, leave the exact owned root preserved and
assert its marker and safe summary; do not force cleanup in the test or finally.
The old reviewer root and concurrent/new foreign roots must stay untouched.
For a scripted uncertainty case, retaining the root is allowed; if explicit
offline fixture teardown is needed, bind it to the independently held exact
ownership capability and separate actual fixture-shutdown authority. A fake
process-table result is not evidence that a native unresolved process stopped.
Do not expose a manual cleanup bypass or introduce a janitor.

Add failing-first, safe regressions for a valid foreign root appearing after
the initial snapshot, candidate marker/token substitution and absence of cleanup
under native uncertainty. Use controlled roots and spies where needed; do not
run an unsafe negative test that deletes an unrelated root. Keep deterministic
complete-observation cleanup, physical preservation, foreign sentinel and all
accepted qualification/proof assertions. A small mutation of each ownership
and shutdown guard is enough. Disclose the new test cleanup defect and any
intentionally retained test root; do not describe unsafe removal as preservation.

Scope is the rehearsal fixture/test ownership and narrow documentation only.
A minimal offline root-reference/capability seam is allowed if necessary; no
production service, accepted process policy, CLI/default, provider, dependency,
ADR, generated client or real ledger change. Run affected safe tests first,
serial build/client hash, full offline ManualQualification and Architecture
after removing the unsafe cleanup. Retain browser/harness evidence unless
covered host/rehearsal/build wiring changes again. Return the complete unstaged,
uncommitted, unpushed diff in this same chat. No publication, real launch, CLI
setup, allowance consumption, root janitor or next slice is authorized.

### Historical fourth review: R1-R5 corrections accepted; publication NO-GO on the fresh offline rehearsal failure

Codex reviewed the third R4 correction on main at
88b9ffb0dfbf399d93ce4c38c742eb56f95847fb. HEAD, local origin/main and an
independent live refs/heads/main match; the index is empty and the inventory
remains 70 status paths (four modified tracked, 66 untracked). The executor
preserved the previous planner bytes (9e5b90c9711be25998c6d213c44a27de4192809a5d48b823430d42b56d0d3f9a),
ADR-0029 and the generated client. This review supersedes that planner hash.

The three last R4 probes now each return OriginUnresolved, Proven=false.
Supplying the unreadable-baseline-parent proof to the scripted session now
preserves the root and returns exit 6. The earlier orphan and remembered-child
probes also remain fixed, and the accepted R1-R3 probes remain correct. R5 stays
accepted. Separating protected-baseline-only controls from new children is
appropriate; the WMI success control was not a valid positive ancestry proof
when its parent identity was unreadable. Its replacement with a known,
pre-existing readable parent preserves the intended control. The exemption
for unchanged unreadable baseline entries, without using them to prove new
children unrelated, is accepted with the documented same-name/same-parent
unobservable-reuse limit. This is a bounded observation, not confinement.

Fresh reviewer build of the Api test project passed with zero warnings/errors;
full Architecture passed 52/52. The full offline ManualQualification selection
FAILED: 273 passed, 1 failed, 0 skipped of 274, exit 1. The failing test was
ManualQualificationRehearsalTests.The_whole_session_runs_through_the_real_composition_against_the_owned_doubles_and_cleans_up_only_its_root,
at line 78, Assert.True(report.Shutdown.IsProven), Expected True / Actual False.
All earlier assertions in that test, including qualification, both owned-double
stages, lineage, manifests, source observations and consumed test allowances,
preceded that failure. They do not prove shutdown. The assertion printed no
safe summary or bounded shutdown reason; host versus child proof and the exact
cause of this run are unproven. The selection was not rerun unchanged. A new
manual fixture root remains from this failed rehearsal; it was not removed by
the reviewer. Reviewer scratch probes ran during this selection, so concurrent
host activity existed, but it is not claimed to explain the historical failure.
This run used only owned doubles, not real providers or the real ledger.

### Bounded completion: rehearsal diagnostics and deterministic shutdown evidence

Do not lower ChildProcessWatch's accepted uncertainty guards to make a busy
machine yield a clean proof. An unknown-origin result is legitimate conservative
behavior; an actual owned leftover or host that did not stop remains a defect
to investigate. Capture the existing allowlisted summary/bounded reason in
shutdown assertions and retain this reviewer failure. Do not retrospectively
claim its cause without captured evidence. Do not use an unchanged rerun for a
green number, add retries/sleeps/skips, ignore failures or delete preserved roots.

Make the complete offline rehearsal's shutdown evidence deterministic. A narrow
read-only process-table/watch construction seam in manual support is permitted
if needed, supplied only by offline tests; the manual executable must still
always use the native default with the same gate. Do not replace any production
process, discovery, adapter or supervisor service, and do not expose a CLI bypass.
Keep full real Program/Kestrel/authentication, owned provider doubles, exact
lineage/manifests, independent source observations, single-use test ledger and
physical owned-root/foreign-sentinel assertions.

Prove the two meaningful shutdown outcomes through this composition: a
controlled complete process observation authorizes only owned cleanup, and a
controlled unknown/identity-unavailable observation preserves the root with
exit 6 and bounded evidence. Keep native-table/helper tests separately as
native evidence, not labelled deterministic or real-provider qualification.
If keeping a native full rehearsal, assert its actual safe outcome precisely
rather than accepting any failure or requiring ambient uncertainty to disappear;
known owned leftovers, enumeration/host failures must not be silently waived.
Disclose intentional test changes and distinguish scripted proof inputs from
native process evidence. The launcher default and safety contract stay unchanged.

Run affected tests first, serial build/client hash, full offline
ManualQualification and Architecture. Retain existing browser/harness evidence
only if covered composition/rehearsal/build wiring is unchanged; otherwise rerun
the relevant canonical checks once. Update current-work with the fresh reviewer
failure, the corrected rehearsal contract, actual fresh/retained evidence and
remaining limits. Also correct its arithmetic wording: the reported red-first
selection had seven failures and two passing controls, not eight total cases.
Preserve all failed evidence and mutation qualifications. No staging, commit,
push, real session relaunch, CLI setup, ledger reset or next slice is authorized.

The real ledger still contains only summary-1ab7697e3f8a4608bc4af9e477a82b96.json
(1420 bytes), with no allowance files. Both real allowances remain unused; the
original Blocked/TargetsNotReal readiness result and environmental blocker are
unchanged. All five support corrections are accepted; this NO-GO is limited to
the fresh offline acceptance failure and its proof, not another process-policy
redesign. Return the complete unstaged diff in the same executor chat.

### Historical third review: the two R4 probes are fixed; ambiguous baseline identity still authorizes cleanup

Codex independently verified main, HEAD, local origin/main and live
refs/heads/main at 88b9ffb0dfbf399d93ce4c38c742eb56f95847fb, an empty
index and 70 status paths (four modified tracked, 66 untracked). The executor
preserved the previous planner record (37650daa6dfa6a84eb00d43c516cebc72737ecdd8682b6a69317a7047dbb0f9d),
ADR-0029 and the generated client. This review supersedes that protected planner
hash. No staging, commit or push is authorized. R1, R2, R3 and R5 stay accepted.

The two previous R4 reproductions now correctly return OriginUnresolved and
IdentityUnavailable, and the latter preserves the root through the session.
The three-valued classification is an appropriate bounded direction. However,
unreadable baseline identities and link ordering still become positive unrelated
proofs. Fresh deterministic probes against the rebuilt support returned
Proven=true / Reason=Proven for each of these cases:

- A parent is present in the baseline and final table with no readable creation
  time. A new helper appears beneath it with a known creation time. Classify
  treats Baseline.Same (null equals null) and Order.Unknown as Unrelated.
- A new helper has no readable creation time beneath a baseline parent whose
  creation time is known. Order.Unknown again becomes Unrelated.
- A baseline pid with a known old creation time reappears with unreadable time,
  a different executable name and a changed parent that is now absent.
  Baseline.Unreadable suppresses the unknown-origin result, so this possibly
  reused pid cannot keep the shutdown unproven.

Supplying the first proof to the existing scripted session returns Qualified,
exit 0 and Cleanup.Removed=true. These are deterministic support probes, not
provider calls or deletion of a user fixture. The existing test named
Pre_existing_processes_with_unavailable_timestamps_and_their_new_children_are_not_failures
bundles two different premises: an unchanged protected baseline process alone
should remain a control, but its new child's ancestry is not positively proven
by an unreadable identity. A test expectation or documented limitation cannot
relax the selected gate.

### Remaining bounded R4 correction

Keep unchanged pre-existing protected processes from causing a blanket failure.
Do not extend that exception into proof for newly observed children, uncertain
parent links or plausible pid reuse. A baseline match with missing identity,
or a missing parent/child ordering fact, cannot by itself resolve a new process
as unrelated. A baseline pid with an unreadable current identity and changed,
unresolved ancestry must remain an ambiguous candidate. Cache unrelated status
only from positive identity and ancestry evidence; an unknown result must not
later become a trusted cache entry. Preserve the root with a bounded unproven
reason when uncertainty remains. No killing, adoption, complete containment
framework, production service change or real launch is selected.

Write failing-first regressions for the three probes and session preservation.
Separate protected-baseline-only controls from their newly observed children;
keep known unrelated ancestry, positively different pid reuse, the previous
two fixes and no-leftover success as controls. Run a small mutation of the
repaired admission-to-unrelated and unreadable-baseline guards. Keep R1-R3/R5
and their regressions intact. Correct the playbook and current-work statements
that currently treat these ambiguities as proof; preserve all prior evidence.

Fresh reviewer evidence on this returned tree: Api test-project build passed
with zero warnings/errors; offline ManualQualification passed 264/264 and full
Architecture 52/52, no failures/skips, on the normal Windows host. Independent
probes confirmed the previous R4 fixes and the three remaining false proofs.
Code and results are ignored reviewer scratch under
node_modules/.cache/codex-manual-qualification-review-20261009/correction-review/,
including r4-round2-reproductions.txt. No executor source or tests were edited
by the reviewer. The canonical browser matrix and full Api suite were not
repeated; keep executor evidence with its actual scope. The earlier sandbox
VSTest abort remains historical, not relabelled.

The real ledger still contains only its original saved Blocked/TargetsNotReal
summary and no allowance files. No real session was relaunched, no provider
called, no CLI installed/configured and no ledger reset. Keep those boundaries
and return the whole unstaged, uncommitted, unpushed diff in the same chat.
Run affected checks first, full offline ManualQualification, Architecture and
serial build/client-hash verification; retain the canonical offline browser
result unless covered host/rehearsal/build wiring changes. No next slice or
increment/MVP completion is selected.

### Historical correction review (2026-10-09): R1, R2, R3 and R5 accepted; R4 remains NO-GO

The first correction round is reviewed. Codex independently verified main, HEAD,
local origin/main and live refs/heads/main at
88b9ffb0dfbf399d93ce4c38c742eb56f95847fb, an empty index and 70 status paths:
four modified tracked and 66 untracked. The executor preserved this planner's
previous reviewed bytes (464379ed9729a0ef7302b14800dd1baed4f6cbc095a712a1e1b1becd4bde8da4),
ADR-0029 and the generated client. This new review supersedes that planner hash.
No production, existing double-only host, provider permission or client change
is accepted or selected. No staging, commit or push is authorized.

R1 is accepted: a foreign accepted/observed attempt pair now returns
PlannerAttemptIdentityMismatch and sends no reviewer POST. R2 is accepted:
failed Planner observations now record source and workspace changes while
preserving the first InvalidStructuredOutput failure; both final reads are
independent and unreadable evidence remains unproven. R3 is accepted: dispatch
with no process evidence now reports DispatchedNoExecutionEvidence, without an
observed-execution claim. R5 is accepted: ProcessEnvironmentCollection is in
its own file. Keep their regressions and behavior; no further redesign of these
accepted corrections is selected.

R4's all-name descendant tracking and native enumeration-error handling are
improvements, but absence of known leftovers is weaker than the selected
shutdown gate. Two deterministic reviewer probes still return ChildProof
Proven=true / Reason=Proven:

- After a baseline with the launcher, a new live helper-with-another-name.exe
  appears whose parent is already absent from the first sample. The helper is
  ignored because it was never remembered and its name is outside orphanNames.
  It may be unrelated, but that is not proven; do not attribute or kill it.
- A shell and helper are first sampled under this launcher with known creation
  times. Later the shell is absent and the same live helper's creation time is
  unavailable. The remembered (pid, known-time) key does not match the current
  (pid, null) key, so the known possible leftover disappears from the proof.
  Native StartOf legitimately returns null when opening/querying fails. A
  missing identity is not evidence that the original child stopped.

Feeding that second false proof into the existing session allows Qualified,
exit 0 and cleanup Removed=true. These are support-gate reproductions using
scripted process tables and cleanup, not real providers or deletion of user
files. The disclosed sampling limitation cannot silently relax R4's requirement
that uncertain ancestry/identity preserve the root.

### Bounded completion of R4

Keep the proof conservative for relevant ambiguous candidates. A newly observed
orphan of any name whose origin cannot be resolved must leave proof unproven,
without claiming it is session-owned. A pid merely being present with a reused
or unavailable creation identity cannot prove its parent chain unrelated.
Likewise, do not mark a previously remembered child gone when its current
creation identity cannot be read. Distinguish a positively different identity
(which may prove the remembered process ended) from an unavailable one. Do not
make unrelated protected system processes with missing timestamps a blanket
failure; apply this to plausible descendants/new orphans and remembered ids.
No process should be killed or adopted through this support.

Return a bounded unproven reason and preserve the root when this uncertainty
cannot be resolved. Do not replace this with a complete-process-containment
framework, production process-service changes, retries or more real calls.
The small manual support may preserve its root for attention rather than
assert a stronger proof than it has.

Write failing-first cases for both reviewer reproductions, parent-id reuse and
relevant missing creation identities; include session/root-preservation tests,
known descendant/orphan controls, a positively different reused identity,
pre-existing/unrelated-process controls and the existing no-leftover success.
A small mutation of each repaired uncertainty guard is sufficient. Preserve
all earlier failures and matrices rather than manufacturing another large set.
Run affected tests first, the full offline ManualQualification selection,
Architecture and serial build/client-hash check. Retain the executor's canonical
offline browser result unless the correction changes its covered support or
build wiring. Unrelated production/frontend suites need not be repeated.
Update the playbook/current-work to distinguish tracking gaps and uncertainty
from a proven shutdown; retain the original Blocked real-session result.

### Fresh reviewer evidence and unchanged real-session authority

The reviewer Api test-project build passed with zero warnings/errors; the
corrected offline ManualQualification selection passed 245/245 and Architecture
52/52, zero failures/skips. The tests were launched on the normal Windows host,
with owned doubles/scripted controls only. This is a new corrected-tree check,
not relabelling the previous sandbox VSTest connection abort; that abort remains
historical evidence. Independent probes confirmed accepted R1-R3 and the two
remaining R4 gaps. Their code/results are ignored scratch artifacts under
node_modules/.cache/codex-manual-qualification-review-20261009/correction-review/.
Codex did not repeat the canonical browser matrix or full Api suite.

The real ledger still contains only its original summary and no allowance
files. The readiness-blocked manual session was not relaunched. No provider
invocation, CLI installation/login/configuration, ledger reset or new session
is selected. Environment setup and any later manual launch remain a separate
planner/owner decision after support review. Return the complete unstaged,
uncommitted, unpushed diff for R4 re-review in this same executor chat.

### Historical first review decision and independently verified facts

The returned support diff is NOT authorized for staging, commit or push. Keep
all corrections in the same executor chat and the same selected slice. Codex
verified main, HEAD, local origin/main and live refs/heads/main at
88b9ffb0dfbf399d93ce4c38c742eb56f95847fb, an empty index, and exactly 61 status
paths: four modified tracked files (including this planner record) and 57
untracked files. ADR-0029 and the generated client retain their selected hashes.
The executor preserved the pre-review planner bytes, SHA-256 163bee01308423dc5f66210596312d80031ef76ecfa43e771577a7ecffd9bb12.
This new planner review supersedes that protected planner hash for correction.

Codex inspected the saved real-session summary: Blocked / TargetsNotReal,
Codex NotObservedExecutableNotFound, Claude DirectExecutable version 2.1.276,
Git 2.53.0, no POST and neither allowance consumed. This is readiness evidence,
not a successful provider qualification. Only a summary file exists in the
real ledger directory; no allowance file was found. Codex launched no real
provider and did not install or configure a CLI. Do not relaunch the manual
session or clear/reset its ledger during these corrections. Environment setup
and a later launch remain a separate planner/owner decision after review.

The separate manual executable, closed existing double-only host, unchanged
production services and no-production-change boundary are appropriate. The
following support defects nevertheless require correction before acceptance.

### R1: bind a stage to the attempt accepted by its POST

QualificationSession discards SubmissionResult.AttemptId. A reviewer-only
scripted probe returned a different random accepted identity for each POST,
while the read returned its usual coherent attempts. The session returned
Qualified and submitted both stages. Require a nonempty accepted identity and
bind every observed stage used for qualification to that exact identity. A
foreign observation must not authorize the reviewer or a qualified result.
Do not replace this with latest/first-role substitution, another POST or retry.
Add independent planner and reviewer mismatch regressions, missing/empty-id
cases and same-id controls. Keep the accepted and observed identities truthful
in bounded evidence without exposing paths or provider session identifiers.

### R2: inspect source and workspace after failed or ambiguous stages too

The short-circuit at planner/reviewer is null skips SourceDrifted entirely.
With both repositories changed after a failed InvalidStructuredOutput Planner,
Codex observed only the setup check: SourceChecks 1, no differences recorded,
two source reads and one workspace read. The safe summary can therefore label
source unchanged after a provider failure without observing it. Perform the
post-stage observation for every submitted stage, including failed/refused,
ambiguous, timed-out and cancelled paths, before cleanup. Use bounded shutdown
and final observation when an effect might still be in flight; absence of a
read must remain unproven, never unchanged. Preserve the first failure code,
record additional source differences/unreadable facts, retain required evidence
and start no later stage. A failure checking one repository must not suppress
the other check. Cover both roles, late source/workspace changes, cancellation,
unreadable observations and the unchanged success controls.

### R3: distinguish dispatch from observed process execution

SafeSummary writes providerInvocationObserved from Dispatched alone. Codex
reproduced true for a Failed/ProviderInvocationFailed dispatched attempt with
no process evidence. Production legitimately commits the dispatch marker before
adapter execution, and can then record a failure with no child process result.
Keep dispatch, allowance consumption and POST submission separate. Report an
observed invocation only from sufficient existing host-measured process evidence;
missing evidence is unknown/not proven, not proof that no process ran. Preserve
its conservative may-have-run handling and never retry. Cover dispatched without
process evidence, running/ambiguous observations, terminal measured execution
and pre-dispatch refusal. Narrow the playbook and ledger claims accordingly.

### R4: do not prove all children stopped from three executable names

ChildProcessWatch considers only node.exe, codex.exe and claude.exe. A local
reviewer probe started an owned live ping.exe child after Begin(); Leftovers
returned zero and NoLeftoverRemainsAsync returned true while that child was
still alive. This can authorize deletion while an owned shell/helper survives.
The shutdown proof must cover session-owned descendants regardless of executable
name, with conservative identity/ancestry handling. Do not kill or attribute
unrelated user processes as owned. If relevant identities, ancestry, enumeration
or shutdown cannot be proven, report unproven and preserve the root. ProcessTable
must also distinguish an actual enumeration failure from normal end-of-table;
a failed/partial read cannot become a successful empty proof. Add deterministic
non-provider-name descendant/orphan and enumeration-failure regressions, a real
owned helper control, unrelated-process controls and root-preservation tests.
Do not claim universal provider-side confinement or change production process
services; the bounded manual support owns this proof.

### R5: one top-level C# type per file

ManualQualificationRehearsalTests.cs currently also declares the top-level
ProcessEnvironmentCollection. Move that collection to its own file verbatim,
with the same namespace, visibility and members. No unrelated reorganization
is selected. Nested implementation-only types are not this finding.

### Evidence, correction validation and return

Codex rebuilt the Api integration test project with --no-restore,
-p:UseSharedCompilation=false and -m:1: zero warnings and errors. Four isolated
reviewer probes reproduced R1-R4 against the submitted support. They use scripted
readings or an owned local ping child, not a provider. The ping child was killed
and awaited. The attempt to run the offline ManualQualification test selection
aborted before a test result: VSTest could not connect to testhost within 90
seconds (exit 1). No test pass/failure count is claimed for that attempt, no
assertion failure is inferred, and it was not rerun. Preserve this infrastructure
failure alongside the executor's earlier reported 1601/52/offline-browser
results; those earlier results do not detect the review reproductions.

Write the failing regressions before each correction, prove the focused changes
and a small meaningful mutation of each guard, then run the relevant offline
selection, full Architecture and serial build/client-hash check. If host/rehearsal
or build wiring changes, harness then one canonical offline browser run. Retain
unrelated production and frontend checks with exact scope. No real invocation,
installation, authentication/configuration change or production patch is selected.
Keep the original Blocked session and all failures in current-work.md; replace
claims that the support has no defect or that dispatch proves invocation. Return
the complete unstaged, uncommitted, unpushed diff and fresh/retained evidence for
re-review. No next slice or increment/MVP completion is selected.

### Verified publication of the recorded local-delivery receipt

Codex independently verified main, HEAD, local origin/main and live
refs/heads/main at 88b9ffb0dfbf399d93ce4c38c742eb56f95847fb with a clean
checkout and an empty index before this planner update. Substantive
5e870bb7750299a851a2e6c7a36be755754e86a5 has the expected parent
0505e26cb572b7e6fbb0c81d970747bbaed2310a and exactly the reviewed 50 paths.
The frozen manifest SHA-256 remains
943558a769452cd369714a39d95d2a3d8ffc00ed0300e578fb21bc82b5d2f196.
Forty-nine committed blobs match the manifest's raw bytes and sizes exactly.
The generated client alone is the approved CRLF working file converted to LF
by the existing .gitattributes rule; its committed text equals that exact
conversion, and its working hash remains
1620e821b9dac08bda208fc23c94ecb1b8537a739b68cd298c3b19d23b763cf8.
This is Git's existing clean conversion, not an implementation change.

Closure 88b9ffb has the substantive parent and changes only current-work.md,
six added lines within the receipt entry. It preserves the review and failures,
distinguishes fresh and retained results, states the remaining limits and
embeds no closure SHA. Codex inspected the closure; the reported fresh build,
digest 4, receipt Api 16, Architecture 52, Vitest 2561, harness 87 and browser
13/journeys 5 are executor evidence, not newly repeated planner tests. The
accepted implementation and recorded-consistency limits remain unchanged.
No increment or MVP completion is declared.

### Selected slice and owner authorization

On 2026-10-09 the owner answered "sim prossiga" to Codex's explicit question
asking permission for at most two real calls, one per provider, with no automatic
retry. Select one bounded manual qualification of the existing Codex Planner to
Claude CriticalReviewer exchange. Claude implements and executes it in one NEW
executor chat; Codex retains planning, diagnosis and GO/NO-GO judgment. The
previous receipt slice is published and independently verified as above.

This authorizes at most one real Planner CLI invocation and one real
CriticalReviewer CLI invocation, through their existing production adapters,
for this one disposable acceptance session. It is not a monetary/token cap or
a claim about the provider's internal HTTP requests. Existing host timeouts,
process/output bounds and read-only permission profiles remain unchanged.
No extra prompt-based health check, retry, repair, Resolver, mutation or fallback
is authorized. Failure of the first stage stops the session; the second starts
only after one coherent, durably recorded Proposal. Any exhausted or uncertain
permission remains spent for this session; do not start another session to get
green. Further provider invocations require a new owner decision.

### Verified executor start

Expected branch main; HEAD, local origin/main and independently queried live
refs/heads/main all 88b9ffb0dfbf399d93ce4c38c742eb56f95847fb. Empty index;
only docs/roadmap/planner-handoff.md modified by this selection; no untracked
files. Preserve this planner record, ADR-0029 and all accepted ADRs. Client
working SHA-256 remains
1620e821b9dac08bda208fc23c94ecb1b8537a739b68cd298c3b19d23b763cf8.
No production/generated-client change is selected.

### Outcome and implementation boundary

One separately launched manual acceptance session creates a new owned disposable
repository/data root, registers/prepares it through protected production commands,
records a ManualAgent run, requests Codex planning once, then requests Claude
critical review of its exact persisted Proposal once. Record either one genuine
Acceptance or one genuine Challenge replying to that Proposal. Acceptance is not
required for a valid compatibility observation; no fixed wording is expected.
Neither Agent writes repository source, refs or index, and no later stage runs.
The result is version-specific compatibility evidence, not provider obedience,
general reliability, live account capacity, or increment/MVP completion.

Use normal Program composition, Kestrel authentication, migrations, startup
reconciliation, all supervisors, real discovery, real adapters/process executor,
sealed artifacts, parsers and result writers. Test-only differences are owned
SQLite/workspace/artifact roots, ephemeral launch secret and loopback addresses.
Do not replace provider, discovery, eligibility, process or supervisor services.
The current BrowserJourneyHost deliberately requires installed doubles: keep
that closed contract intact. Add a separate feature-owned manual launcher/small
support executable, outside ordinary suite discovery, rather than a real-provider
switch in the canonical journey host. Necessary project/solution build wiring is
allowed; no new dependency version or generic harness framework is selected.
Reuse ownership support only where semantics match; do not weaken its proof.

Require deliberate opt-in before any host/provider startup. Resolve real targets
through normal readiness and verify they are installed providers, never the
fixture bin. Non-inference version/help/authentication readiness observations are
allowed, but no billable health prompt or extra workflow attempt. Do not install,
update, log in, copy credentials, edit user config or change model/effort defaults.
Unsupported installed arguments or launch shapes are blockers; the adapters,
CLI schema, environment allowlists and permission profiles must not change here.
Use the existing manual-intake ceilings of two Agent claims and twenty reserved
invocation minutes, covering the unchanged ten-minute timeout of each role.

Use a tiny fixed fictitious repository/objective and benign root instructions,
not this repository, a user's registered project or personal data. Setup Git
writes belong solely to this new fixture. Snapshot source bytes, HEAD, branch
and real index after setup/preparation and around each Agent call. Confirm no
Agent mutation, unchanged original source repository, exactly the two permitted
role/provider attempts at most, exact sealed input/reply lineage and truthful
terminal process/protocol evidence. Fail rather than invent missing evidence.
Exercise only existing authenticated HTTP operations/generated client; no SQL
insertion or mutation of authority rows. Read-only inspection of the disposable
rows and artifacts may supplement public results.

Consume each launcher allowance before submitting its one POST. A timeout,
transport ambiguity, failed response or accepted request never authorizes that
POST again. Poll existing read endpoints within a finite deadline; no automatic
claim or recovery invocation on restart. Keep an invocation ledger tied to this
session so an interrupted launcher cannot reset the allowance. Do not count a
pre-start refusal as proof of a provider invocation. Record actual observed
invocations and unused allowances separately. Setup/readiness/validation must
finish before spending either real allowance; the final manual session runs once.

The launcher, evidence summary and cleanup are bounded. Ephemeral API credentials
stay in memory/session environment, never arguments, URLs, browser storage,
traces, screenshots, committed files or summary. Report allowlisted versions,
opaque local attempt/message identities, role/provider/status, input/reply match,
sealed byte/hash agreement and unchanged-source facts. No user/executable path,
provider session ID, full output, raw manifest, secret or account identity is
published. Keep necessary local artifacts under established authenticated access
and retain only bounded failure excerpts that have been checked for secrets.
Stop host/children before deleting only this session's physically proven owned
root. If containment or child shutdown is unproven, preserve it for attention;
never clean historical fixtures. No claim of complete provider-side confinement.

### Offline tests and acceptance evidence

The real session is manual acceptance, never an integration test or automatic
CI check. The engineering integration-test standard prohibits real third-party
calls from those suites. Keep it out of dotnet test, Vitest, harness, both
canonical Playwright configurations and test:e2e:all. The explicit manual command
may use existing browser tooling without adding a test automatically discovered
by a suite. Offline tests use deterministic substitutes solely for launcher
control/cleanup; no such substitute stands as the actual qualification result.

Write focused regressions first for missing opt-in, fixture/unsupported target,
call budget and first-stage dependency, timeout/unknown POST outcome, duplicate
launcher/restart, malformed/foreign response lineage, changed source, safe summary
and failure cleanup. Prove the failure paths launch no extra Agent and preserve
foreign roots. Add meaningful mutations of opt-in, allowance consumption,
lineage and source checks, restoring hashes; do not manufacture a large matrix.

Run a clean serial solution build and verify the client hash; affected offline
launcher/host/support tests and full Architecture; applicable format, tracked and
untracked hygiene and local links. If canonical journey support/build wiring was
touched, run harness then one canonical offline browser matrix; otherwise retain
its published evidence with exact scope. Retain unchanged production Domain,
Application, Infrastructure, Api and frontend suites instead of rerunning them
without a relevant change. Do not run dependency audits again unless a dependency
or lockfile legitimately changed. Build the separate manual support target if it
is not part of the solution. Then run the single authorized real session and keep
its actual result, including a blocker, without a green rerun.

Document the opt-in invocation and limits in a small manual-acceptance playbook;
update current-work.md with actual inventory, command/exit/count evidence, the
versions observed, real-versus-double and fresh-versus-retained distinctions,
quota allowance consumption and failures. No new ADR is required for this
acceptance-only support; an architecture change is outside this selection.

### Stop gates and return

A real response, authentication, quota, executable/profile or composition mismatch
stops the session. Preserve the first failure; do not relax the contract, substitute
a double, patch production, renew authorization or move to another role. A changed
repository or unsafe cleanup stops further work and requires planner diagnosis.
Do not broaden this into implementation, correction, verification, Git delivery,
push/PR, resume, compaction, account guardrails, autonomous coordination, a new
permission profile, new schema or product UI. Never change accepted ADRs.

Return the complete unstaged, uncommitted and unpushed support/docs diff and the
actual manual result for Codex review, even if qualification is blocked. No
staging, commit/push GO, next slice or increment/MVP completion is granted.
All review corrections and publication stay in this same new executor chat.

## Historical decision (2026-10-09): recorded local-delivery receipt

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
