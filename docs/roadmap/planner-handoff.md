# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md),
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Current decision (2026-10-08): explicitly abandon an inactive manual run

Exactly one next slice is selected: a human can end an inactive ManualAgent run
as Abandoned, retain its reason and complete history after restart, and then
record another objective through normal intake. Claude is executor; Codex owns
planning, architecture and acceptance. This is implementation authorization,
not staging, commit, push, next-slice or increment-completion GO.

### Final review decision (2026-10-09): GO for the reviewed abandonment slice

Codex independently verified main, HEAD, local origin/main and live
refs/heads/main at 3603cea9ba0d6e11888e99d4974ce338e4fe34ad; empty index;
95 status paths (35 modified tracked, 60 untracked). Before this review edit,
the planner record was preserved at 0986797bb2788264bc2bb50fc9d5199be3e2305ebd17ca3d91787ca3f453dea6.
The client remains 22074a066c91a1fb1dfc0afc602573f14e72f9c6ffd8047633b2640c20e20b64;
ADR-0029/0030 have no diff against HEAD. No source or test file was edited by
the reviewer. Only this decision record and a factual review bullet in the
ledger were updated before freezing the approved snapshot.

R1-R4, including the remaining R3 storage-class case, are accepted. The new
provider-side mapping admits only TEXT before the date converter and stays
confined to AbandonedAtUtc. The independent real SQLite probe now reports
blob, abandonment_incoherent, no recorded abandonment, failed replay and
failed intake for the matching date-shaped BLOB. Normal TEXT closure, the
populated-tracker Run transition and supplementary Format refusals remain
correct. Fresh reviewer checks: serial solution build 0 warnings/errors;
Application abandonment selection 253/253; Infrastructure migration/mapping
selection 25/25; Api abandonment selection 38/38; full Architecture 47/47;
git diff --check and the changed-file whitespace/NUL scan clean (the existing
planner/client CRLF notices remain). No skips in those selections. The full
suites, browser and mutations retain their precisely scoped executor evidence.

Explicit judgment on the reported full Api result: preserve 1378 passed and
1 failed; do not relabel it green. The failure is the existing competing-
preparation test's final expectation of one inert artifact, after its commit,
ref, run, workspace, admission and source-preservation assertions passed.
The executor reports the same assertion on unchanged HEAD (six class runs),
and intermittent outcomes on this tree; the historical cause remains unproven.
This specific result does not block the bounded abandonment slice. It is not
a blanket waiver for another failure or a required CI job.

The reviewer found that ScriptedLocalCommitPreparer invokes AfterPrepare for
every outcome, while CompeteAsync assumes the parked result was Prepared.
In an isolated diagnostic over the real host and Git fixture, two Prepared
results yielded Completed, parked HTTP 409 and one inert leaf. A controlled
first CheckpointNotCurrent refusal yielded no facts, the other Prepared
operation Completed, parked HTTP 409 and zero leaves, reproducing the failed
assertion's shape without deleting an unadmitted successful artifact. This
proves the test's unguarded premise can fail for safe behavior, not the event
order of the historical failing run. Do not claim that run's cause is fixed.
Source, captured outcome controls and raw logs are ignored scratch under
frontend node_modules/.cache/codex-competing-review-20261009. Initial diagnostic
setup had a content-root startup failure, then its own content-root setting
was corrected; no acceptance failure was rerun to obtain green. A local cached
restore omitted the probe's audit and makes no audit-cleanliness claim.

Publication authorization covers only the frozen 95-path snapshot, including
this record and the commit-ready ledger. A raw-hash/size/status manifest is
stored in ignored frontend node_modules/.cache/codex-abandon-publication-20261009.
The publication prompt supplies its exact hash and the three final file hashes.
Verify every entry and the exact Git inventory before staging; stop on any
material change and return for review. Commit the substantive slice on the
verified baseline, push main normally, independently verify the live remote,
and run the prescribed relevant post-publication checks sequentially from
fresh logs. Preserve failures and stop on a new failed check without retrying
an unchanged acceptance run. Then make only a tightly bounded current-work.md
factual closure commit and normal push, verifying the live ref and clean tree.
Do not amend, force-push, reconcile remote history, alter implementation or
regenerate the approved manifest. No next slice or increment/MVP completion
is authorized. The inert-preparation fixture premise remains a bounded follow-up
candidate for planner selection after verified publication.

### Re-review decision (2026-10-09): NO-GO, R3 storage-class correction only

Codex independently verified main, HEAD, local origin/main and live
refs/heads/main at 3603cea9ba0d6e11888e99d4974ce338e4fe34ad; empty index;
93 status paths (35 modified tracked, 58 untracked). Before this review edit,
the executor preserved the planner record at
d3711253beb3197282f123a58277a214c37c085cd9b414fd64995adb30535f71.
The ledger is 0afaf2c7404c977a4dfc7a1c15a5f2401b74823b295c181a6d70c7123469efad;
the generated client remains
22074a066c91a1fb1dfc0afc602573f14e72f9c6ffd8047633b2640c20e20b64.
ADR-0029 and ADR-0030 have no diff against HEAD. Nothing is staged, committed
or pushed. No publication manifest or next slice is authorized.

R1, R2 and R4 are accepted: the exact Run is reloaded under the existing write
lock; Unicode scalar categories now refuse supplementary Format characters;
the externally consumed types are split into their own files, and the two
refusal codes and native DROP COLUMN explanation are aligned. The previously
accepted three Codex lifecycle guards stay accepted and unchanged.

Architectural judgment on R3: accept a mapping specific to AbandonedAtUtc,
rather than a reader-only catch. Whole-row readers such as the cockpit also
need safe materialization. The current converter, however, sees a string only
after the SQLite reader has already decoded the stored value. It cannot tell
a BLOB containing a valid date from valid TEXT. The existing BLOB regression
uses arbitrary bytes, not the bytes of an admissible timestamp.

Remaining R3 (medium): in an isolated real file-backed SQLite reviewer probe,
a coherent abandonment's AbandonedAtUtc was replaced with a BLOB containing
UTF-8 bytes of the exact matching provider-format timestamp. SQLite typeof
confirmed blob. GET still exposed a coherent recorded abandonment, same-reason
replay succeeded, and ordinary manual intake created another Run. Thus the
new mapping admits a malformed stored fact as closure authority, contrary to
ADR-0031 and its own BLOB-refusal claim. Preserve the actual storage class at
the column's read boundary: only TEXT with the admitted timestamp form may
become a recorded time. A non-TEXT value must remain incoherent for that run,
without throwing through whole-row reads or affecting healthy siblings.

Keep this correction confined to the one new column, its focused regressions
and matching documentation. No general storage framework, older timestamp
mapping, column/schema change, Application SQLite-specific persistence port,
claim redesign, cancellation, workspace/Git/native/lock/recovery change or
new authorization is selected. Keep valid TEXT writing, precision and offsets
unchanged, and keep invalid values untouched on refused requests and unrelated
saves. Do not substitute defaults or silently repair a damaged abandonment.

Write failing-first tests with a BLOB containing an otherwise admissible
matching timestamp (whole seconds and a fractional/offset control), alongside
identical TEXT, NULL and malformed values. Cover GET, same/different-reason
replay, blocked intake, whole-row cockpit, healthy project/sibling isolation
and a populated tracker. Assert storage class and bytes are retained when
nothing should write. A mutation that drops the storage-class admission must
fail these regressions. Preserve every earlier failure and mutation result,
with fresh versus retained evidence explicit.

Fresh reviewer checks on the corrected submission: serial solution build
0 warnings/errors; Domain RunAbandonmentTests 54/54; Application abandonment
selection 223/223; Infrastructure AddRunAbandonmentMigrationTests 16/16;
Api abandonment selection 36/36; full Architecture 47/47. These all passed
with no skips. The reviewer probe confirms the previous R1 and R2 reproductions
are corrected and the original unparsable TEXT now yields the incoherent code,
but the date-shaped BLOB admits GET/replay/intake. Its source and captured
output are ignored scratch under frontend node_modules/.cache/
codex-abandon-review-20261008 (r3-blob-review-output.txt); it uses and removes
only its own temporary SQLite database. The initial sandbox probe build failed
without usable diagnostics; the outside-sandbox probe ran successfully, not
as an acceptance-test rerun. No production or test source was edited by Codex.
The full suites, frontend, canonical browser and mutation matrices remain
executor-reported evidence, not reviewer reruns in this round.

Return the full corrected unstaged, uncommitted, unpushed diff in the same
executor chat with a commit-ready ledger and exact inventory. Run affected
checks first, rebuild and restore the required mutation, then the relevant
final validation. Preserve this planner record and ADR-0029/0030. No staging,
commit, push GO or next-slice authorization is granted by this decision.

### Review decision (2026-10-08): NO-GO, one bounded correction round

Codex independently verified main at
3603cea9ba0d6e11888e99d4974ce338e4fe34ad, equal to local origin/main and the
live refs/heads/main; empty index; 84 status paths (35 modified tracked and
49 untracked). The executor preserved the selection record at
ce42b149f7ba2085033e54b0ed8210e80136361441c1caa4537bf14510afce63 before this
review edit. The generated client is
22074a066c91a1fb1dfc0afc602573f14e72f9c6ffd8047633b2640c20e20b64.
No staging, commit, push, publication manifest or next slice is authorized.

Architectural judgment on the reported stop gate: accept the three bounded
Codex claim lifecycle confirmations after their existing write-locking guards.
They close the demonstrated Running-run gap without redesigning claims,
reservation, dispatch, Git or recovery. Keep planning's runs.not_active and
resolution/review's existing runs.not_running refusals, rollback and orphan
manifest cleanup. The full slice remains NO-GO for the following findings.

R1 (high): AbandonManualRunCommandHandler loads its write target with a tracking
SingleAsync after fresh admission reads. That query can return an older Run
already in this context. In a real file-backed SQLite reviewer probe, the
context held Running/Plan/None, another context committed Running/Critique/Agent,
and abandonment returned success while persisting Abandoned/Critique/Agent.
Because clearing None was unchanged relative to the stale tracked original,
EF did not clear the newer participant. GET then reported
run_abandonment.abandonment_incoherent. Refresh exactly the Run being mutated
from the locked database before applying its domain transition and keep its
original concurrency values aligned; do not clear or disable the whole tracker.
Add failing-first populated-Run regressions, including stale None to Agent,
Changed Created to Running, same-lifecycle advances and a current control.
Require a coherent persisted closure, current stage/clock, cleared participant,
one event and successful normal intake, or a justified refusal with no writes.

R2 (medium): RunAbandonmentPolicy skips every valid surrogate pair before its
Unicode-category check. U+E0001 and U+E0020 are Format characters but both are
admitted by the host, while the client rejects them. Validate Unicode scalar
categories, preserving valid supplementary text, unpaired-surrogate refusal,
normalization and the UTF-8 byte bound. Add Domain, command/endpoint no-write
and frontend agreement regressions with supplementary Format characters and
ordinary supplementary-text controls. Do not weaken the accepted reason rule.

R3 (medium): RunAbandonmentReader materializes AbandonedAtUtc as DateTimeOffset
before testing coherence. A reviewer-owned SQLite row with that new column set
to the TEXT not-a-date throws FormatException through the abandonment query.
Provide focused defensive reading of this new field so malformed facts remain
incoherent per run, never become a valid/default timestamp, cannot replay or
permit intake, and do not break healthy project summaries. Keep normal old
timestamp mappings and the SQLite write-lock/Git boundaries unchanged. Prove
GET, replay, intake and a healthy sibling/project, including a populated tracker.
No generic persistence or stored-state framework is authorized.

R4 (contract): one top-level C# type per file, and nested types may be private
implementation details only (ENGINEERING.md and csharp-style.md). Split the
new ManualRunAbandonmentView and ManualRunAbandonmentResponse from their result
and response files. Move the externally consumed nested Facts/EventFacts,
Reading and Snapshot from RunAbandonmentPolicy, RunAbandonmentReader and
AbandonmentScene into descriptive type-owned files with unchanged visibility
and behavior. Keep JSON names and the generated client contract unchanged.
Also align ADR-0031's lifecycle-refusal passage with the two actual codes and
clarify that native DROP COLUMN avoids EF's drop/recreate/rename sequence;
SQLite still rewrites table content to remove a column. No migration behavior
change or historical cleanup is requested. See the primary SQLite contract:
[ALTER TABLE DROP COLUMN](https://www.sqlite.org/lang_altertable.html#alter_table_drop_column).

Reviewer checks on the submitted tree: serial solution build 0 warnings/errors;
Domain abandonment 42/42; Application abandonment 141/141; Infrastructure
migration 6/6; Api abandonment 27/27; Architecture boundary 7/7; four affected
frontend files 88/88 outside the sandbox. The initial sandbox Vitest attempt
failed three suites on worker temporary-file ENOENT (35 pure tests passed);
it is environmental evidence, not a code correction or a passing suite.
The isolated probe initially failed package-audit network access; its local
cached-package restore omitted that audit and makes no audit-cleanliness claim.
The reviewer did not rerun the canonical browser or full suites. The
executor-reported full-suite/browser results remain executor evidence.
Reviewer probes are ignored scratch work under frontend node_modules/.cache/
codex-abandon-review-20261008 and use only their own temporary SQLite file;
no production or test source was edited by Codex.

Return the complete unstaged, uncommitted and unpushed corrected diff in this
same executor chat with a commit-ready current-work.md entry. Preserve this
planner record and ADR-0029/0030. Run affected checks first, then the relevant
full validation on the final code; rebuild every mutation and restore hashes.
Preserve every failure and distinguish fresh from retained evidence. No retries
of an unchanged failing acceptance run to obtain green. This is correction of
the current slice only; no publication or next-slice authorization is granted.

### Independently verified baseline

Branch main; HEAD, local origin/main and live refs/heads/main are
3603cea9ba0d6e11888e99d4974ce338e4fe34ad. Before this planner edit the checkout
was clean: nothing staged, unstaged or untracked. Published substantive
5a36784577358fc31a624220b4264493081e0eb2 has parent
426463717c0e724780b511763681630c1a5b63a7. Its 75 paths and M/A statuses equal
the approved manifest; the 74 current working-file hashes outside current-work.md
still equal that manifest. Closure 3603cea9 has that substantive parent and
changes only current-work.md (4 additions). Codex verified these facts and
the closure diff. Reported post-publication counts are executor evidence,
not tests newly rerun for this selection.

Expected executor start after this edit: same branch/HEAD, empty index,
only docs/roadmap/planner-handoff.md modified, and no untracked files.
The generated client baseline is
46036f0fe25f99a3fec4ad1984d7d7d2995e6f7aa616a88551a8a3c31f641bac.

### Value and architectural judgment

RunIntentRecorder admits another objective only after a recognized terminal
lifecycle. Manual Agent failures and budget exhaustion do not themselves end
the run; a successful explicit local commit now can, but an owner has no safe
production action to abandon a task that will not be delivered. RunLifecycle
has no Abandoned state. The Lifecycle concurrency token protects claims that
emit Run updates. The review above confirmed a gap in three Codex claims and
accepts their focused in-transaction lifecycle confirmations, keeping this
closure possible without a process-cancellation or Git-recovery mechanism.

This provides an explicit exit from an exhausted, escalated or unwanted manual
task and proves a second objective can proceed with history intact. More context
sampling would not solve that usability gap. Remote push/PR and autonomous
coordination need substantially different external-effect and recovery authority.
Claude account enforcement, provider-session resume and compaction still lack
accepted safe contracts. None is selected or inferred from defaults.

This deliberately advances one explicit manual recovery control before closing
all Increment 4 provider gaps, as ADR-0029 already did for local delivery.
ADR-0031 must record that narrow ordering and terminal-authority decision,
extending ADR-0014's intake contract without silently rewriting accepted ADRs.

### Selected contract

1. Add protected MVC POST /api/runs/{runId}/abandon, dispatching one
   AbandonManualRunCommand. The body contains only a required human reason:
   trim outer whitespace, normalize CRLF to LF, reject remaining control
   characters except LF, nonblank and at most 2 KiB UTF-8; body at most 8 KiB.
   The host determines mode, lifecycle, project, times and participants.
   Admit only exact stored ManualAgent and Created or Running. Legacy,
   Simulated, unknown modes/lifecycles and other terminal outcomes refuse.
2. Append RunLifecycle.Abandoned without renumbering existing values. Preserve
   Stage: abandonment is not successful objective completion. Clear the active
   participant, freeze accumulated time (do not count Created time), and persist
   immutable normalized AbandonmentReason and AbandonedAtUtc on Run. Add the
   focused nullable-column migration, with no defaults, backfill or historical
   rewrite. Save the terminal transition and exactly one Human-authored
   RunAbandoned journal event atomically using normal event sequencing.
3. Decide again from fresh untracked authority after acquiring the existing
   short SQLite write transaction. No external work belongs in it. Require no
   nonterminal or unrecognized Attempt belonging to this project's runs, and
   no nonterminal or unrecognized project verification execution. Require no
   nonterminal or unrecognized project local-commit operation: Prepared,
   Executing and NeedsAttention all block. Preparing or Committing workspaces,
   and unknown workspace statuses, block as well. A missing workspace is valid
   for an untouched Created run. Other known workspace states may be retained:
   this action changes no workspace, lease, marker or source trust.
4. Serialize duplicate abandonments and races with Agent/local-commit admission.
   Existing Lifecycle guards must remain effective: if a claim/admission wins,
   abandonment refuses; if abandonment wins, an already-preparing claim cannot
   commit an Attempt or operation, dispatch an Agent or promote a ref/index.
   Preserve orphan-manifest cleanup and any inert-artifact limits. A project
   verification claimed after closure remains project work under its existing
   rules, not a resumed abandoned run. This adds no persistent workspace freeze.
   Same normalized reason on a coherently Abandoned run returns the recorded
   result without another event or timestamp; a different reason conflicts.
   A refusal or definitely rolled-back save records nothing.
5. Extend normal intake to recognize coherent Abandoned facts as terminal.
   Incoherent new abandonment facts must not authorize another intent or a
   successful replay. Do not broaden the other terminal or unknown-state rules.
   No task is created automatically. New intake creates a distinct run, objective,
   execution number and normal fresh budgets; old consumed slots, reservations,
   grants, messages, approvals and manifests remain historical and unchanged.
   Previous-run plans, grants or approvals never become the new run's authority.
6. Add protected read-only GET /api/runs/{runId}/abandonment through one
   GetManualRunAbandonmentQuery. It exposes fixed safe eligibility/refusal facts
   and coherent recorded abandonment reason/time; it writes and invokes nothing.
   Its eligibility is advisory. Surface Abandoned truthfully in cockpit/history
   and project intake, with the persisted reason/time after reload.
7. Add an explicit cockpit form labelled "Abandon run", offered only from settled
   successful current-run status. Explain that the objective is not completed,
   existing changes/evidence remain, and new work still needs normal checks.
   Own drafts, versions, guards and continuations by the committed run/status
   lifetime, including A-to-B-to-A, unmount, stale callbacks and overlapping reads.
   Pending/failed reads withhold submission. Never cancel a process or retry an
   unknown POST automatically; reconcile through the read-only status. Accepted
   closure stays real even if a later refresh fails. Use only the generated client.
8. Update ADR-0031/index, engineering context, protocol, workflow/recovery and
   cockpit/roadmap descriptions narrowly, plus commit-ready current-work.md.
   Keep ADR-0029/0030 and the planner record unchanged during execution.

### Exclusions, stop gates and evidence

No active-process cancellation, pause/resume, lease release, workspace repair,
lock adoption, branch/worktree/file/artifact deletion, commit recovery changes,
remote publication, provider controls, automatic new runs, scheduler or generic
lifecycle/recovery framework. A workspace does not become Ready because a run
ended. Abandonment is not an override for ambiguous local delivery.

Stop if safe serialization requires changing Git/native/ref/index recovery,
loosening a reservation or accepting an unknown effect as finished. Report an
existing claim-guard gap before redesigning claim or dispatch infrastructure.
Do not substitute Failed, Interrupted or Completed for this human decision.

Acceptance: failing-first proof that an inactive manual run blocks new intake;
Created and Running closure, frozen clocks and preserved Stage/history;
validation, auth, Legacy/Simulated/unknown and active/ambiguous refusals with no
writes; real file-backed SQLite atomic rollback, populated trackers and exact
duplicate arbitration; claim-versus-abandonment in both orders across all eight
Agent paths including repair forms, plus local-commit preparation/admission and
verification seam controls; unchanged workspace/lease/ref/index/source bytes;
fresh intake with distinct identity/budgets and no inherited authorization;
migration upgrade/down preserving old facts; restart and unknown-response
reconciliation; UI lifetime, accepted-operation and same-text draft edits.

Use a production-written authenticated browser journey to record an objective,
obtain a terminal process-double planning attempt, abandon through the rendered
form, reload the durable reason/history, and record a second objective through
intake in the same project. Confirm the earlier sealed bytes and workspace stay
unchanged and no extra provider invocation occurs during abandonment/reload.
Use deterministic seam signals, not sleeps or retries, for race proofs. Detect
omitted active/ambiguous guards, lost atomic event, duplicate event, stale mode
or lifecycle admission and obsolete UI ownership with focused mutations.

Run affected checks first, then a serial solution build, relevant full backend
suites, frontend Vitest/typecheck/lint/build, strict e2e tsc, harness and one
canonical test:e2e:all after harness succeeds. Reproduce the generated client,
check baseline-scoped formatting, audits, links and tracked/untracked hygiene.
Preserve failed outputs and separate fresh from retained evidence. Return the
complete unstaged, uncommitted, unpushed diff for Codex GO/NO-GO.

## Previous selected slice (2026-10-08): complete human verification-set approval for local delivery

Exactly one new slice is selected: make the existing explicit local-delivery
journey usable with several enabled verification recipes, from settled human
approval evidence through one atomic human review, the existing local commit,
and persisted results after reload. Claude is executor; Codex retains planning,
architecture and acceptance. This is implementation authorization only: no
staging, commit, push, remote publication, autonomy or next-slice GO.

### Independently verified baseline and previous delivery

Branch main; HEAD, local origin/main and live refs/heads/main are
426463717c0e724780b511763681630c1a5b63a7. The checkout was clean, with nothing
staged, unstaged or untracked, before this planner edit. The substantive commit
5319975b1b80988aeb1c2cba8c570fe2197ef2a8 has the expected parent
a9e0e2b0b3b05bd54d8d45aec6258c035a02e930.
The closure has that substantive parent and changes only current-work.md
(4 insertions). The published 149 paths equal the approved manifest; all
148 working-file hashes outside current-work.md still equal its entries.
The planner verified Git, the manifest comparison and the closure diff; the
post-publication suite counts remain executor-reported evidence, not newly
rerun tests. ADR-0029 and the generated client retain their reviewed hashes.
Expected executor start after this selection: the same branch/HEAD, an empty
index, only docs/roadmap/planner-handoff.md modified, and no untracked files.

### Why this candidate

The Domain CheckpointReview and its relational evidence already support several
unique command/execution members. Code-review claims order all enabled recipes
by CommandNumber, and completion records all their members. LocalCommitAuthorityReader
already requires the complete current verification set and exact matching human
membership. However, RecordCheckpointReviewRequest, its command/handler and the
panel submit only one VerificationExecutionId. That transport/UI gap blocks the
ordinary tests-plus-lint case with membership_mismatch; disabling a recipe or
combining separate human reviews would weaken or change the accepted gate.

This closes a concrete limitation of the authorized manual local-delivery path.
A remote push/draft-PR slice needs separate publication authority and ambiguous
remote-effect reconciliation; automatic coordination needs its own dispatch,
lease and recovery contract. Unproven Claude account enforcement, provider
resume and compaction still lack safe contracts. More context sampling would
not close any of these gaps. None is selected here. This choice grants no new
Git mutation capability and declares no increment or MVP complete.

### Selected architecture and contract

1. Extend the existing protected POST /api/projects/{projectId}/reviews with
   an optional verificationExecutionIds array, bounded to 32 nonempty unique
   UUIDs and an 8 KiB request body. Preserve the existing scalar field and its
   behavior for callers and historical facts. Two non-null evidence forms are
   ambiguous and refused, never merged or deduplicated silently. A decided
   array must contain 1..32 IDs; Pending has no evidence members. Null/omitted
   array uses the legacy form; an empty array may represent only Pending.
2. The new array form is for Human reviews. Approved in that form means exactly
   the full currently enabled recipe set: one latest Passed execution for each,
   same project/workspace/current checkpoint and fingerprint, coherent clean
   completion and matching current command snapshot. No subset or overlap,
   fallback to an older Passed execution, missing recipe, repeated command,
   or silent truncation qualifies. Order is derived by the host's CommandNumber,
   never by caller array order, timestamps or UUID chronology. ChangesRequested
   and Escalated may cite a bounded set of coherent terminal executions under
   the existing rules. Legacy scalar approvals stay checkpoint-bound facts;
   they gain no additional delivery authority. Existing FutureAgent/provider
   provenance rules and provider-written review completion remain intact.
3. Keep one normal review plus relational CheckpointReviewEvidence rows, saved
   atomically through the existing manual transaction. Observe source outside
   the transaction; after acquiring the write lock, re-read every source,
   selected execution and, for complete approval, enabled recipe and latest
   execution fresh and untracked. A changed selection is refused, never
   silently retargeted. Existing Ready/lease/current-source checks and all
   local-commit reservation write guards remain mandatory. No new persistence
   representation, chronology, migration or generic approval framework is needed.
4. Add one protected read-only MVC operation, GET
   /api/projects/{projectId}/checkpoints/{checkpointId}/approval-evidence,
   dispatched as GetCheckpointApprovalEvidenceQuery. It returns a bounded,
   complete eligible Passed bundle with source identities/fingerprint and
   ordered command/execution identities, recipe labels and execution numbers,
   or a fixed safe refusal. Require current owned Ready source and coherent
   current verification, including command snapshots and completion fingerprint.
   It must not use the history GET's latest-20 window as the recipe universe.
   More than 32 enabled recipes refuses explicitly; no success-shaped partial
   bundle. The query writes nothing, claims nothing, invokes no provider and
   authorizes nothing; the POST independently decides again. Use existing
   bounded source observation and perform no Git work inside a transaction.
5. Add an explicit Human action, "Approve all enabled checks", in the existing
   checkpoint review surface. Show the exact bundle it will submit. Offer it
   only for settled successful source/bundle reads belonging to the same
   project/workspace/checkpoint/fingerprint. Pending or failed refresh and
   inconsistent responses withhold it; an obsolete callback starts no request.
   Own pending guards and continuations by the committed source/bundle lifetime,
   including A-to-B-to-A, unmount, changed members and overlapping refreshes.
   An accepted POST stays real; a refresh failure must not relabel it a failed
   approval or cause automatic retry. Preserve the legacy decision controls
   and make the complete-set action's local-delivery meaning clear.
6. Use the generated client, regenerated by the normal Api build, for both
   operations. Extend the existing production-written collaboration journey
   to enable and run two distinct recipes, obtain the real CodeReviewer's
   approval of both, approve both through the rendered Human action, request
   the existing explicit local commit, and confirm memberships/commit/completed
   run after reload. No raw-SQL approval or fake-Git substitute proves this.
7. Record ADR-0030 for the additive complete-set Human approval contract and
   compatibility, following this planner decision. Do not rewrite ADR-0029.
   Update the narrow workflow/cockpit/protocol descriptions, engineering context
   and roadmap to distinguish the published local commit from this selection.
   Include the commit-ready current-work.md entry with actual checks and limits.

### Boundaries, stop gates and acceptance

No remote push/PR/CI, orchestration, scheduling, cancellation/takeover, provider
controls, new approval supersession, merged human decisions, lock adoption,
Git adapter/protocol redesign, branch/worktree cleanup or unrelated hardening.
Keep the full human-decision gate: Pending or a conflicting decision still
blocks local delivery. Historical rows and sealed manifests are never rewritten.

Stop and report if completion requires loosening exact verification membership,
using a bounded history page as complete evidence, synthesizing Agent approval,
relaxing reservation/physical-source/recovery proof, changing accepted Git
execution, adding a migration or expanding the transaction over external work.
A protected endpoint/schema/client mismatch is a defect to fix within this
boundary, never a reason to use raw fetch or disable authentication.

Acceptance must include failing-first proof of the current single-member gap;
legacy scalar/one-recipe controls; array binding/body/count/duplicate/cross-owner
refusals with no rows; real file-backed SQLite multi-member atomicity and rollback;
source/recipe/latest-execution changes at the write seam with populated trackers;
reservation exclusion; exact full-set local-commit admission, digest/seam refusal
and restart/reload preservation; history beyond 20 executions; frontend settled
read and lifetime/accepted-operation cases; and the production browser journey
through two verified recipes and the real local commit with the main checkout
unchanged. Detect mutations that admit a subset, omit one member, trust a stale
recipe/execution or accept obsolete UI ownership. Rebuild every backend mutant,
restore source hashes, then build clean before final validation.

Run affected checks first, then relevant full .NET suites sequentially after a
successful solution build; frontend Vitest/typecheck/lint/build, strict e2e tsc,
harness and one canonical test:e2e:all after the harness succeeds. Check generated
client reproduction, formatter scope/baseline, audits, links and all tracked plus
untracked whitespace/NUL/EOF. Preserve failures and distinguish fresh from
retained evidence; do not rerun unchanged failures just to obtain green.
Return the complete unstaged, uncommitted, unpushed diff, exact inventory and
commit-ready ledger to Codex GO/NO-GO. Do not edit this planner-owned record.

The previous publication GO and its review findings below are historical and
apply only to the now-published 149-path snapshot, not this new slice.

## Review decision (2026-10-08): NO-GO for the complete-set approval return

The selected slice remains active. No staging, commit, push or next-slice GO is
given. Claude must correct this same diff and return it for review.

Codex independently verified main, HEAD/local origin/main/live refs/heads/main
at 426463717c0e724780b511763681630c1a5b63a7, an empty index and 47 paths:
26 modified tracked and 21 untracked. Before this review edit the planner hash
was 8c3e8c3347a71b28d3fd602119d721c97b19a4adc3a126b3dc9a39a1aa4dd65e.
ADR-0029 remains cf9a28d43ebc6b07ed1ed7daf01813348c9974b899e1c750461c584bf3f8b294;
the independently regenerated client remains
46036f0fe25f99a3fec4ad1984d7d7d2995e6f7aa616a88551a8a3c31f641bac.
The expected correction start is the same branch/HEAD and inventory, with no
staged changes and this newly edited planner record preserved.

### R1: own approval by the committed bundle lifetime

useCheckpointApprovalEvidence owns actions only by project/checkpoint/fingerprint.
A member replacement does not end the pending action; equality of bundle keys
also reactivates an obsolete callback after a same-source A-to-B-to-A replacement.
The handler does not itself refuse another approval of its accepted bundle.
Independent probes reproduced an old failure appearing on replacement members,
an obsolete A callback submitting after A-to-B-to-A, and two POSTs from a retained
callback after the first acceptance. End action ownership on committed bundle
replacement, even when returning to an earlier identity. Own duplicate protection
and accepted-operation guards by that lifetime. An obsolete completion must not
write replacement errors, clear its pending guard or refresh it; an already
accepted server operation remains real. Preserve current unchanged-owner behavior.

### R2: bind the bundle to the full observed source

ApprovalBundleSource and the panel omit the observed workspace and checkpoint
number. The decoder ignores response.workspaceId and only checks that the
response checkpoint number is positive. Real-panel probes kept the action enabled
for a foreign workspace, an absent workspace and checkpoint number 999 against
source checkpoint 2. Require the full admitted source identity, including workspace
and checkpoint number, in decoding and committed lifetime ownership. Missing,
malformed or mismatching facts withhold the action; never repair them or silently
retarget. Add replacement and valid-control coverage without weakening fixtures.

### R3: resolve the acceptance-evidence gaps without green retries

Two full Api runs returned HTTP 500 from
LocalCommitEndpointTests.Concurrent_identical_and_competing_requests_admit_exactly_one_operation.
Later passing runs and unchanged admission code do not explain those responses;
no response body or server exception was captured. Add bounded sanitized failure
diagnostics and obtain a defensible cause or comparative evidence before claiming
this acceptance complete. Do not serialize the suite globally, retry, sleep,
ignore failures or weaken the concurrency assertion. A production admission/Git
change outside this slice requires a new planner decision.

The full Infrastructure run also failed the real cancellation test. Its captured
stack is TaskCanceledException at CodexProcessInvoker.cs:141 while writing the
schema, before starting the child process. The one-second timer is armed before
that preparation; isolated passes do not prove it reached a running child in the
failed run. Establish the test's cancellation phase deterministically, or provide
bounded evidence that distinguishes a pre-existing preparation issue from the
claimed running-process evidence. Do not invent process evidence for a child
that never ran. Preserve both original failures and distinguish diagnosis,
corrective runs and retained results.

### Documentation and independent evidence

CommandNumber ordering in the review-history projection is a reasonable bounded
presentation change. ADR-0030 must not claim relational members are persisted in
that order: there is no persisted sequence. State host-derived canonical
presentation/order instead, and align the UI contract with R1/R2.

Codex ran a successful solution build (0 warnings/errors, client identical),
Application selected tests 82/82, Api selected tests 36/36, Infrastructure review
transaction tests 6/6 and Architecture 40/40, all without skips.
An isolated ignored reviewer Vitest harness reproduced six new failing cases
above while its 41 copied existing cases passed. The final valid output is under
frontend/node_modules/.cache/codex-approval-review-20261008/reviewer-output-r3.txt;
an earlier harness-cache startup failure is not product evidence. Convert the
reproductions into normal regression tests, then run affected checks before the
relevant full validation. Preserve fresh versus retained evidence and all failed
runs in the commit-ready current-work.md entry. No full Infrastructure pass or
publication readiness is asserted by this review.

## Correction review (2026-10-08): NO-GO; isolate concurrent preparation artifacts

The slice remains active in the same executor chat. Codex authorizes the bounded
production correction below within this slice, not staging, commit, push or a
new slice. This resolves the earlier R3 stop gate; routine owner approval is not
needed for this implementation decision.

Codex independently verified main, HEAD/local origin/main/live refs/heads/main
at 426463717c0e724780b511763681630c1a5b63a7, an empty index and 53 paths:
30 modified tracked and 23 untracked. Before this planner edit the handoff hash
was 47c84942495982e9dc44a58b9982d05d77b84d6f57e17b9cbd8322bb42fac571.
ADR-0029 and the generated client retain the exact hashes above.
Expected correction start is that same branch/HEAD/inventory, with this new
planner edit preserved.

### Assessment of R1, R2 and R3 diagnosis

The inspected UI now includes workspace and checkpoint number in its source
identity, and separates source-read ownership from committed member-set action
ownership. Codex ran the three focused ordinary frontend files: 109/109 passed,
including the converted ownership, duplicate-acceptance and identity regressions.
The bounded child-start cancellation test and its two pre-launch characterization
cases are appropriate test corrections; the Infrastructure class passed 12/12.
The diagnostic host tests passed 5/5. An independent sequential solution build
passed with 0 warnings/errors and reproduced the client. The first focused Vitest
invocation encountered a stale sandbox module-cache ENOENT; an isolated owned
cache resolved that tooling failure, and it is not product evidence.

Codex inspected the retained concurrency diagnostics and production code.
PrepareOnWindowsAsync shares work and operation directories by OperationId;
its finally recursively deletes those shared directories. File.Copy at line 219
was observed failing for both promotion.index and prepared.index. Preparation
occurs before database admission, so concurrent identical requests legitimately
reach that code together. Later passing Api runs do not remove this defect.
One admitted row proves admission arbitration, not that its prepared artifact
cannot be deleted or overwritten by another preparation. Publication remains
NO-GO pending a production correction and discriminating evidence.

### Authorized bounded correction

Give each preparation invocation its own host-owned scratch and prepared-artifact
leaf, with a fresh preparation identity independent of the caller's OperationId.
Different preparer instances must not share mutable leaves merely because their
requests have the same operation ID. Keep the operation ID and request identity
unchanged. Persist the winning leaf's exact relative artifact path through the
existing PreparedIndexRelativePath fact; no migration or port-shape change is
needed. Preserve exclusive creation/no unintended replacement and validate every
cleanup target inside the owned storage root.

A preparation cleans only scratch/artifacts it owns. Controlled-observation
scratch must not be removed through a competing preparation's ancestor cleanup.
Terminal cleanup must address the recorded artifact, without recursively deleting
siblings belonging to another in-flight preparation. Existing recorded legacy
artifact paths must remain readable/executable/recoverable and safely cleanable.
An unadmitted successful preparation may leave inert unreachable artifacts;
report that limit rather than introduce a general janitor or deletion authority.

This change may touch LocalCommitStorage and the preparer's, observation's and
repository's artifact allocation/cleanup paths, plus focused tests and docs.
Keep database admission/idempotent replay, authority gates, commit construction,
hash checks, prepared ref protocol, HEAD protection, native index effects and
lock/recovery authority unchanged. No process-local mutex or mapping the
exception to a conflict is an adequate substitute for isolated artifact ownership.
No new global serialization, retries, sleeps, overwrite or lock adoption.

### Required evidence and stop gates

Write bounded deterministic failing-first interleavings through real Git, with
two preparation instances sharing the same storage root. Cover identical
operation IDs, competing requests and refusal/cancellation cleanup while another
preparation is active. Assert scratch independence, distinct immutable artifact
paths, exact hashes and survival of the admitted artifact after the loser finishes
or fails. Detect restoring shared scratch, shared prepared-artifact paths or
ancestor cleanup; source restores must be byte-identical and rebuilt.

Retain the real protected endpoint race assertions (OK or Conflict only, exactly
one durable operation and admission event). Prove execution/restart recovery of
the admitted operation after the competing preparation completes, and legacy-path
compatibility and main-checkout preservation. Counts or repeated probabilistic
green runs alone do not establish ownership.

Run affected checks first, then relevant full backend suites sequentially after
a clean build, followed by the established frontend/harness/canonical browser
checks. Keep original failures and label fresh versus retained evidence in the
commit-ready current-work.md. Stop if this needs new admission semantics, a
migration, Git/native protocol changes or weaker ownership proof; return the
specific remaining gap for planner judgment. Return the whole unstaged,
uncommitted, unpushed diff for GO/NO-GO.

## Publication decision (2026-10-08): GO for complete human verification-set approval

R1-R5 are accepted. GO applies only to the reviewed 75-path working snapshot,
including this planner decision and the commit-ready current-work.md entry.
It authorizes the substantive commit, normal fast-forward main publication,
the checks below and one tightly bounded factual documentation closure.
It does not select another slice or declare any increment or MVP complete.

Codex verified main, HEAD/local origin/main/live refs/heads/main at
426463717c0e724780b511763681630c1a5b63a7, empty index and 75 paths:
46 modified tracked and 29 untracked. The R5 split reconstructed the exact
previous ScriptedLocalCommitRepository.cs SHA-256,
ff29ad0e469ece1dd8a45b1ce8808268fc884726b29a09ae1ec5c48c12d7f4fe.
No logic changed. Fresh independent solution build: 0 warnings/errors;
Architecture: 40/40. The earlier independent completed-boundary selection
passed 42/42. The executor's after-split 42/42 log was inspected rather than
rerun. Full Api 1341/1341 and the R4 eight-mutant matrix remain inspected,
retained evidence. All earlier validation retains its stated scope.
The original failed independent selection and unproven historical native
outcome remain disclosed. The preparation collision guard's surviving mutant
is not represented as equivalent or proven by an unexercised collision case.

Before this GO edit, the planner hash was
9c8e9dec170b28e1669745b02b5d71288b8c9646726ace88e466ad895dd5356d.
Current-work.md remains
a29ba8d55c298143d06093df21bbb0d2d1bdeca4cb6ec728db6ba35647bad643.
ADR-0029 remains
cf9a28d43ebc6b07ed1ed7daf01813348c9974b899e1c750461c584bf3f8b294.
The generated client remains
46036f0fe25f99a3fec4ad1984d7d7d2995e6f7aa616a88551a8a3c31f641bac.
A separate ignored publication manifest freezes the final raw hashes, sizes
and Git statuses of all 75 paths after this edit. Verify its announced hash,
exact inventory and every entry before staging; do not regenerate it.

Stage only those reviewed paths, verify the staged inventory with no renames
and require git diff --cached --check to pass. Make no snapshot edits.
Commit the substantive slice, including current-work.md and this record,
with the verified baseline as its parent. Push main normally to origin/main.
Fetch and independently query live refs/heads/main; require HEAD, local
origin/main and live main to equal the substantive commit, with a clean tree.
Stop on divergence or failure; no amend, force push or history reconciliation.

Post-publication checks use fresh logs and run sequentially after a serial
solution build (--no-restore, UseSharedCompilation=false, -m:1):
Application RecordCheckpointReviewCompleteSetTests and
GetCheckpointApprovalEvidenceQueryHandlerTests;
Infrastructure LocalCommitGitPreparationIsolationTests and
LocalCommitStorageOwnershipTests;
Api Features.Runs.LocalCommit plus CheckpointApprovalEvidenceEndpointTests;
full Architecture. Tests use --no-build --no-restore.
Then full Vitest, typecheck, lint, production build, harness, and exactly one
canonical test:e2e:all after the harness succeeds, with normal authentication,
host composition and all supervisors. Report actual counts and skips; preserve
the complete browser output. Verify the client hash, diff and clean checkout.
Stop and preserve a failure; do not rerun unchanged failures for green.
The full backend suites, audits, formatter and mutation matrices may remain
retained evidence with their exact scope.

Only after these checks succeed, edit current-work.md within this slice's
entry to record the substantive SHA, verified publication, actual fresh checks
and retained evidence, historical planner hashes and inventories, and unchanged
limits. Preserve all failure evidence. Do not embed the closure's own SHA.
Commit that file alone, push normally, then fetch/query the live ref and verify
all three refs equal the closure with a clean checkout. Any material change
to the reviewed snapshot requires renewed review before publication.

## Previous correction review (2026-10-08): R4 accepted; bounded organization correction remains

The completed-boundary signalling and failure-safe owned-capability cleanup
satisfy R4 in the inspected code, deterministic tests and mutation evidence.
R1/R2, preparation isolation and the cancellation correction remain accepted.
No production ref, native or recovery defect is claimed fixed or disproven:
the original independent failure's native result was not captured. Preserve
that failure and the earlier limits. No next slice is selected.

Codex independently verified main, HEAD/local origin/main/live refs/heads/main
at 426463717c0e724780b511763681630c1a5b63a7, empty index and 73 paths:
46 modified tracked and 27 untracked. Before this review edit the planner hash
was 3c38e4f9c5726559b8b3c51d3e6e74ab2a31e702b42cd22f8602e6f52fa06ecb.
The current-work.md hash is
fe9b4b9c37389ee51a3aedd693665135820329718486f35cd5d433377d2c4d2c.
ADR-0029 and the generated client retain their protected exact hashes.

Fresh independent validation after a serial solution build: 0 warnings/errors;
ScriptedLocalCommitRepositoryTests plus LocalCommitRestartRecoveryTests 42/42,
0 skipped, including all six restart boundaries; Architecture 40/40.
Tracked diff and all 73 working files have no whitespace or NUL findings.
Codex inspected the executor's full Api log (1341/1341) and eight detected
R4 mutants, and independently matched the three restored target hashes.
That full suite and mutation matrix are inspected executor evidence, not
newly rerun by Codex. Unchanged earlier evidence remains retained with its
recorded scope.

### R5: one top-level C# type per file

Publication remains NO-GO solely for this mechanical organization correction.
ENGINEERING.md line 95 explicitly requires "Keep one top-level C# type per file."
ScriptedLocalCommitRepository.cs introduces LossPoint and LossReport beside
the decorator. Move each new type, with its existing documentation and unchanged
namespace, visibility, members and semantics, to LossPoint.cs and LossReport.cs
in the same LocalCommit test folder. Leave the decorator in its existing file.
Do not alter any logic or move unrelated pre-existing types.

Expected correction start: the same branch/HEAD, empty index and 73-path
inventory, preserving this newly edited planner record. A pure split adds two
untracked files; expected return is 75 paths, 46 modified and 29 untracked.
Update only the current-work.md inventory and correction/evidence wording.
Keep its original failure and fresh-versus-retained distinctions.

After the mechanical split, run a serial solution build, the same affected
42-test selection and Architecture, plus tracked/untracked hygiene. Verify
the generated client and ADR-0029 remain identical. Retain the full Api,
frontend/browser and mutation results; no repeated full suite or mutation
matrix is required for this unchanged logic. A failure or a required behavioral
change returns for judgment, never for an unchanged green retry. Return the
complete unstaged, uncommitted, unpushed diff. No staging, commit or push is
authorized until the resulting snapshot is reviewed.

## Previous correction review (2026-10-08): NO-GO on restart-boundary evidence

The preparation isolation correction is sound in the inspected code and its
deterministic interleavings. R1/R2 and the earlier cancellation correction remain
accepted within this review. Publication is still NO-GO because the independent
Api acceptance selection failed once. Correct this same slice; no next work,
staging, commit or push is authorized.

Codex verified main, HEAD/local origin/main/live refs/heads/main at
426463717c0e724780b511763681630c1a5b63a7, empty index and 69 paths:
43 modified tracked and 26 untracked. The planner hash before this edit was
41aa6002fbe971e804995e00c25b30f0d9c2c6b150afb66113dc4c06a72314d9.
ADR-0029 and the generated client still retain their exact protected hashes.
Expected correction start is the same branch/HEAD/inventory, preserving this
new planner edit and the factual current-work.md edits below.

### Fresh independent result and precise gap

The solution built with 0 warnings/errors and the client remained identical.
Application selected tests passed 82/82; Infrastructure isolation/storage/Git
execution/observation/review transaction/process evidence passed 69/69;
Architecture passed 40/40; the three focused frontend files passed 109/109.
Codex inspected the executor's full-suite logs and canonical browser output:
the reported full counts and Chromium 13 plus journeys 4 are present. Those logs
do not override the following fresh failure.

Command:
dotnet test tests/DevalCopilot.Api.IntegrationTests/DevalCopilot.Api.IntegrationTests.csproj --no-build --no-restore --filter "FullyQualifiedName~Features.Runs.LocalCommit|FullyQualifiedName~CheckpointApprovalEvidenceEndpointTests" --logger "console;verbosity=minimal"

Result: 134 passed, 1 failed, 0 skipped of 135, in 4 minutes 45 seconds.
The failure was LocalCommitRestartRecoveryTests.
Restart_after_each_boundary_decides_only_from_exact_recorded_evidence,
AfterRefBeforePlan. At AssertFlightAsync line 86 the expected recorded CommitSha
was 901a6a8ba39064b9676779f6f70124144ad0e027, while the observed tip was
c9087bb3ead0a9a47cc476b7d07edf3ca0d2df66. Teardown additionally failed at
LocalCommitScene.Dispose line 261 because prepared.index was still open.
The test was not rerun for a green outcome.

CrashAtAsync waits for Calls to contain PromoteRefAsync, then delays 800 ms
before disposing the host. ScriptedLocalCommitRepository records that call on
entry, before awaiting the real effect. This does not prove the selected
AfterRefBeforePlan loss boundary was reached; stopping the host can precede
completion. The code establishes the synchronization gap, but the actual native
outcome of the failed run was not captured. Do not declare a production ref or
recovery defect fixed or disproven without that evidence.

### Authorized R4: bounded test-fixture correction

Replace call-entry-plus-sleep synchronization with explicit, bounded signals at
the actual selected before/after-effect loss hook. For after-effect boundaries,
capture and require the real successful effect/result before signalling host
loss; a refusal or native failure is a diagnosable failure of reaching that
boundary, not a simulated promoted state. Wait for the executing path to unwind
or otherwise prove no child/effect remains in flight before teardown/restart.
Preserve every expected ref/index/receipt/journal/recovery outcome.

Release capabilities deliberately retained by the test through the existing
owned acquisition/release API in failure-safe fixture cleanup, after stopping
the host. Never adopt a lock, delete a lock by pathname, remove foreign locks or
weaken recovery assertions. A failed assertion must not bypass owned-handle
cleanup and cause a second teardown exception. Preserve the simulated foreign
lock across restart assertions until the test releases its own capability.

Scope is the restart test, its scripted repository/helper lifecycle and focused
regressions. No production Git/native/ref/lock/recovery change is authorized.
If the synchronized seam still returns an unexpected real outcome,
capture its bounded reason and stop for planner judgment.

Prove deterministically that method entry alone cannot satisfy the after-effect
boundary, including an outstanding effect, and that assertion-failure cleanup
releases only the fixture-owned capability. Cover all six existing boundaries.
Detect replacing completed-boundary signalling with entry signalling. No sleeps,
retries, skips, global test serialization or weakened assertions.

Run affected checks first, then the full Api suite after a clean build, plus
Architecture and hygiene. Unchanged Domain/Application/Infrastructure/frontend/
browser results may be retained with exact scope; do not rerun unchanged
failures just to obtain green. Preserve this failed selection in current-work.md
alongside the earlier failures, and return the complete unstaged diff.

Codex also corrected only the ledger's P6 wording: the overwrite mutation
survived a matrix that never created an existing destination; it is not proven
equivalent or impossible to exercise. Exclusive creation remains in the source.
No executable file was changed by this review.

## Previous decision (2026-10-08): local-commit publication GO

The owner authorized bounded Increment 5 work while the unproven Increment 4
provider contracts remain open. Codex selected exactly one slice: an explicit,
host-executed local commit of the exact verified, Agent-reviewed and human-approved
checkpoint in the owned worktree, with its necessary durable execution and recovery.
[ADR-0029](../decisions/0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md)
records the accepted sequencing change and exact contract. It neither declares
Increment 4 complete nor removes provider requirements or weakens their gates.
The owner transferred this slice's execution from Claude to a new Codex executor chat
on 2026-10-06 because Claude credits were exhausted. This planner chat retains
architecture, acceptance and GO/NO-GO; the executor does not acquire those roles.
The English execution prompt is supplied in the planner conversation, not here.
Selection grants implementation within this boundary, not staging, commit or push GO.

## Latest review (2026-10-08): GO for the reviewed 149-path snapshot

Codex accepts the complete corrected ADR-0029 local-commit slice, including R1-R8.
Claude remains executor. This is publication GO for this snapshot only, not a
next slice, provider-contract closure or Increment 4/5/MVP completion claim.

Codex verified main, HEAD/local origin/main/live refs/heads/main at
a9e0e2b0b3b05bd54d8d45aec6258c035a02e930, an empty index and **149 paths:
29 modified tracked and 120 untracked**. ADR-0029 and the generated client retain
the full hashes below. The final raw-file manifest, including this GO record and
the reviewed current-work.md, is external and supplied in the planner conversation;
verify its own hash, inventory, every raw SHA-256 and size before staging.

Independent checks against a fresh nonincremental solution build:
0 warnings/errors; Application local-commit selection 116/116; Infrastructure
local-commit/native selection 211/211; Api local-commit suite 92/92; full
Architecture 40/40. None failed or skipped. The generated client was reproduced
unchanged. These selected suites are not independent full-suite reruns.

Codex repeated the original disposable diagnostics against the corrected production
code: the recipe request during attention is refused (Accepted=false, rows stay
1); both unknown ref/HEAD lock restarts keep Workspace/Operation=NeedsAttention,
Run=Running and the foreign sentinel unchanged; a stdout fault after commit ACK
is Uncertain/commit:stdout_unconfirmed while the healthy control is Acknowledged.
The disposable host probe initially failed to compile because its own default
source glob included its new protocol subproject; excluding that subproject fixed
only the external harness. No production/test implementation changed in this review.

R6 exclusion is bound to the open operation and exact workspace/project in both
early checks and all seven write seams; terminal/unrelated controls remain.
R7 requires positive Clear reference-lock evidence before safe release or any
recovery decision/effect; default/unreadable shapes are Unproven. R8 requires
confirmed EOF on both streams and preserves uncertain/unconfirmed faults.

Codex inspected the executor's final full-suite and browser logs:
Domain 1277; Application 4604; Infrastructure 1589 passed + 4 existing skips;
Api 1286; Architecture 40; Vitest 2232; harness 87; Chromium 12 and journeys 4.
These remain executor runs independently inspected, not rerun by Codex.
All 148 files except the subsequently updated delivery ledger matched the
executor's pre-mutation raw-hash list. The final matrix reports 64 rebuilt
mutants, 62 detected: M31 is equivalent; M39c does not reach the trigger because
the early refusal already rejects, and direct trigger mutations do fail.
The contaminated-binary run and earlier failures remain disclosed.

Independent hygiene found no NUL, trailing whitespace or new blank EOF;
248 local file links in 8 changed Markdown documents resolve. Two receipt
migration files have mixed LF/CRLF endings (including the generated designer);
current-work.md's uniform-LF claim was corrected factually. No executable source
was edited. git diff --check passes with the existing conversion notices.
Scoped format evidence is not whole-solution formatter cleanliness.

Accepted limits remain explicit: one enabled recipe can qualify; ambiguous pending
index attention may stay nonterminal; existing foreign locks are never adopted or
deleted; namespace inspection is point-in-time and covers only this transaction's
owned-ref and HEAD locks; Windows physical proof only; unsupported forms refuse;
the source-map-js audit advisory predates this diff; process doubles do not prove
real-provider reliability. No blind retry, override or remote project publication.

Publish only the frozen reviewed snapshot under the single instruction supplied
in chat: substantive commit including current-work.md and this GO record, normal
fast-forward push, fetch plus independent live-ref verification, fresh sequential
post-publication checks, then a tightly bounded factual current-work.md-only
closure and its normal push/verification. Stop on discrepancy, divergence or a
failed required check; preserve evidence and do not rerun unchanged to obtain green.
A material post-GO change requires re-review. Do not amend or force-push.

## Historical review before R6-R8 correction (2026-10-08): NO-GO

The owner transferred execution back to Claude; Codex remains planner/reviewer.
Earlier executor transfers and review counts below are historical. No new slice,
staging, commit, push or publication is authorized.

Codex independently verified main, HEAD/local origin/main/live refs/heads/main at
**a9e0e2b0b3b05bd54d8d45aec6258c035a02e930**, an empty index, and **142 paths:
29 modified tracked and 113 untracked**. The live ref was read directly.
ADR-0029 remains cf9a28d43ebc6b07ed1ed7daf01813348c9974b899e1c750461c584bf3f8b294;
the generated client remains e215388b6ac6030b1024c73b39b1053cbc4f2912446b00b0fc31b0b9f8449969.
The pre-review planner hash ee9e967c0a0e6a907c540f56528a799e11dcc08dd1b21e6b059a33f038191a1c
is historical after this planner edit. Preserve the new hash supplied in the prompt.

The return now supplies substantial real-Git, SQLite, actual startup, authority,
exclusion and fault coverage; it is no longer the earlier untested partial return.
Reported full-suite and browser counts remain executor evidence, not independent
full reruns by Codex. Three independently reproduced gaps prevent acceptance.

### R6 — P1: an ambiguous operation loses write exclusion

CommitReservation and all seven AddLocalCommitSeamGuards predicates test only
WorkspaceStatus.Committing. NeedsAttentionAsync changes the workspace to
NeedsAttention while the admitted operation and run remain nonterminal.
Codex established that state using the production foreign-index-lock refusal,
then invoked ConfigureVerificationCommandCommandHandler on the same file-backed
database. It accepted and persisted a second recipe:
Workspace=NeedsAttention, Operation=NeedsAttention, Accepted=true, RowsBefore=1,
RowsAfter=2.

Retain exclusion for the entire admitted nonterminal operation, including its
NeedsAttention state, in the early guard, competing writers' own transaction
seams and conflict mapping. Bind it to the exact operation/workspace/project;
do not block every historical or unrelated NeedsAttention workspace. Keep the
existing Ready admission rules and terminal safe release. Prove fresh recipe
create/update/delete refusal and the seven stale-writer seam guards across
Committing to NeedsAttention, with rollback, populated trackers, unrelated-project
and proven terminal-release controls. This completes R4/R5 reservation continuity.

### R7 — P1: startup releases a reservation with unknown ref/HEAD locks

InspectAsync observes index.lock but does not observe the owned reference or HEAD
locks. Recovery can therefore Interrupt and release Ready from parent/preimage.
Using a production-written operation stopped after its execution marker and before
index acquisition, Codex placed a foreign lock, then started the actual new host.
Both cases produced Workspace=Ready, Operation=Interrupted, Run=Interrupted,
LockPreserved=true: first the exact owned ref lock in the common directory, then
HEAD.lock in the linked-worktree administrative directory. Preserving the file
does not satisfy the selected requirement to retain reservation/NeedsAttention.

Require positive, bounded evidence that the exact reference/HEAD lock namespace
used by this transaction is clear before safe release, terminal recovery or new
recovery effects. Missing/unreadable proof is unknown. Audit normal unpromoted
release and promoted/index-pending restart paths as well. A minimal typed addition
to the existing LocalCommitInspection facts is authorized for this purpose; it
supersedes the earlier blanket no-Application-port-change restriction only for
these provider-neutral inspection facts. Default or historical inspection shapes
must not imply clear locks. Native/path interpretation stays in Infrastructure.
Never adopt, delete or rename a foreign lock, infer ownership from bytes/PID/age,
retry CAS, or add takeover authority. Prove both locks through actual startup
with run/workspace/operation/events, unchanged foreign bytes and no mutation,
plus lock-free controls and execution safe-release coverage.

### R8 — P2: final output proof faults are accepted as acknowledgements

HasTrailingBytesAsync returns null on a failed or cancelled read, but
ShutdownCoreAsync rejects only true. DrainStandardErrorAsync also swallows read
faults, allowing a completed drain task to stand for confirmed EOF.
Codex compiled the unchanged production owner in a disposable probe, drove its
real child pipes with the existing protocol fixture, and closed stdout immediately
after commit_acknowledged. Healthy control and fault both returned
Outcome=Acknowledged, Reason=none.

Require explicit successful stdout/stderr EOF evidence as well as exact ACK and
clean exit. A stream fault or cleanup timeout must remain uncertain/unconfirmed,
not successful completion or evidence for release. Keep independent bounded
cleanup, actual stdin EOF, single terminal send and owned-child reaping. Add
deterministic production-owner regressions after commit and abort ACKs, including
stdout read failure/cancellation and stderr drain failure, healthy controls and a
rebuilt mutation that restores the permissive behavior. Correct the reader comment
that currently describes null as false.

Independent probes and compact JSON results are outside the repository at
%TEMP%/devalcopilot-local-commit-final-review-6af572dfbf1e438f839d4737bcbd9282:
results-run2.txt for R6/R7; protocol/results.txt for R8. The real-host probe needed
an explicit test content-root environment value after an initial harness startup
failure; no production composition was changed. The successful host probe and
protocol probe each exited 0. No real provider ran, and Codex edited no slice code
or tests. These diagnostics are not full-suite validation.

Correct R6-R8 together in the same Claude executor chat. Retain earlier failure
evidence, exact safe ownership/CAS/native/profile behavior and all exclusions.
Update current-work.md with failing-first regressions, rebuilt/restored mutations,
actual commands, fresh versus retained evidence and remaining limits. Run affected
checks first, then relevant full validation after the final production edits.
Return the complete unstaged, uncommitted, unpushed diff for Codex review. No
publication or next slice is authorized.

## Original review (2026-10-06): historical R1-R5 findings

The completed executor return is not approved for publication. Codex independently
verified main, HEAD/local origin/main/live refs/heads/main at
**a9e0e2b0b3b05bd54d8d45aec6258c035a02e930**, an empty index and **102 paths:
26 modified tracked files, 76 untracked files**. No commit or push occurred.
ADR-0029 is unchanged at cf9a28d43ebc6b07ed1ed7daf01813348c9974b899e1c750461c584bf3f8b294;
the generated client remains e215388b6ac6030b1024c73b39b1053cbc4f2912446b00b0fc31b0b9f8449969.
The planner updates only this file to record the decision. Preserve the updated
planner hash supplied in the correction prompt, not the historical transfer hash.

### R1 — P1: the ref CAS can mutate the main checkout's branch

LocalCommitGit.Repository.cs invokes update-ref with default dereferencing, after
separate branch-binding checks, and does not prove binding/ownership again around
index promotion and terminal confirmation. Codex wrapped the real child-process
adapter to replace the owned branch with a symbolic ref to the main branch at the
update-ref boundary. The production adapter returned Promoted; main HEAD moved to
the proposed commit and workspace binding resolved to refs/heads/master.

Correct the ref operation so it cannot follow a redirected owned ref, pin/prove
HEAD and the exact recorded owned ref at the mutation seam, and revalidate binding,
ownership and recorded ref/object/index evidence before promotion/success. A
preliminary symbolic-ref read or an expected parent alone is insufficient. Include
real-Git tests for symbolic ref and HEAD changes before/at/after CAS and prove the
main ref/index/files remain untouched. External interference must fail closed.

### R2 — P1: mutable working-path Git reads can execute external filters

The immutable conversion check does not cover CaptureAsync in preparation or the
ordinary status commands in execution/inspection. Codex prepared valid bytes,
then added a working .gitattributes selecting a sentinel clean filter. ExecuteAsync
returned Promoted and the sentinel proved the external filter ran in the final
status; SourceConsistent was false. Reporting dirty afterwards does not revoke the
repository executable that already ran.

Close every Git observation in preparation, execution, inspection and recovery
against mutable/unrepresented attributes and converters, including unchanged files,
deletions and post-promotion reads. Do not treat no-ext-diff/no-textconv or a prior
immutable check as clean-filter suppression. Preserve the accepted checkpoint
identity; if a safe bounded observation cannot be proved, stop rather than invoke
the converter. Add sentinel regressions for attribute/configuration changes at
these seams and controls for supported identity conversion. No raw fingerprint
redesign, blanket retry or repository helper execution is authorized.

### R3 — P1: equal index bytes do not prove lock ownership

InspectAsync calls a lock OwnedByOperation solely when its bytes hash to the
prepared index. Cleanup and promotion hash a pathname and later delete/move it.
Codex created a foreign index.lock with identical prepared bytes: the adapter
classified it OwnedByOperation, CleanupAsync returned true and removed it. The
existing recovery test explicitly models such a copy as owned without recording
any acquisition proof, so its green result does not prove exclusive ownership.

Persist/prove operation-specific acquisition and physical lock/artifact identity,
retain and revalidate the appropriate handle/identity across effects, and never
adopt, remove or promote another process's lock because its content matches.
Cover same-byte foreign locks and replacement between inspection/hash and delete/
rename, as well as owned recovery and partial acquisition crashes. Extend the
same proof to prepared artifacts and administrative index promotion; lexical
containment plus shared pathname hashing is not physical operation ownership.

### R4 — P1: unsafe external state is released as Ready after refusal

Code inspection: ExecuteAsync maps ownership_unproven, branch_changed and
index_changed to NotPromoted. ExecuteLocalCommitCommandHandler passes every
NotPromoted result, and authority-marker refusal, to FailAsync. The outcome recorder
fails the run and unconditionally calls FinishCommit through ReleaseReservation,
returning the workspace to Ready without proving unchanged parent/index and
ownership. A host not issuing its mutation is not proof that no external change
occurred. ADR-0029 allows release only from a proven unpromoted, still-owned state.

Require exact non-promotion and safe-release evidence; distinguish external
interference/unprovable state from a definitely unpromoted safe failure. Preserve
reservation/NeedsAttention and a nonterminal ambiguous operation/run when proof is
missing. Test these paths through the real file-backed host and persisted outcome,
not only adapter enums. Audit persistence/notifier failures, populated trackers,
reservation continuity in NeedsAttention and recovery consequences in the same fix.

### R5 — P1: required authority, exclusion and recovery acceptance is missing

Domain 1209 and Application 4488 are unchanged baseline totals; no new tests in
those suites exercise the operation/policies. The five new API tests cover happy
path, authentication/missing IDs, malformed transport and duplicate admission.
The 28 real-Git tests include manually constructed inspection shapes, but do not
prove actual startup recovery commands with SQLite/run/workspace/event outcomes.
No new write-seam trigger migration suite, full exclusion matrix, persistence-fault
journey or required mutation evidence is supplied. Full green baseline suites and
the happy browser journey cannot substitute for those explicitly required proofs.

Complete the ADR-0029 acceptance within this slice: malformed/stale/full-membership
and mixed-human authority, populated trackers, admission/execution seams, both
reservation race directions against claims/verification/recipe/checkpoint/review
writes, trigger error mapping and rollback, migration up/down, all ref/index/DB
failure boundaries, actual host restart, terminal evidence exactly once and unique
completed head chains. Include unrelated-run/main-checkout controls and meaningful
rebuilt mutations, with final full validation and honest failure/fresh-retained
reporting. Do not mirror implementation or fabricate architectural assurance from
count totals. Correct current-work.md's broad delivered-scope claims to the evidence.

Independent reproduction evidence: an external disposable console probe linked
the existing real-Git scene and used the production LocalCommitGit and real process
adapter. Its three outputs were:

- redirect-owned-ref-at-CAS: Outcome=Promoted, MainMoved=true,
  BindingNow=refs/heads/master.
- working-attributes-after-prepare: Outcome=Promoted, FilterRan=true,
  SourceConsistent=false.
- foreign-lock-identical-index-bytes: Classified=OwnedByOperation,
  Cleaned=true, LockRemoved=true.

Probe source is outside the repository at
%TEMP%/devalcopilot-local-commit-review-457b435657604ff0bdd4d3d2266ff809.
The first launch failed before build because the sandbox denied NuGet.Config
access. The elevated launch completed all three reproductions, exit 0. No real
provider ran and no implementation/test source was edited by the reviewer. Its
build is not a full-suite validation. Executor suite/browser results remain
reported evidence, not independently repeated by this planner. git diff --check
passed with the existing CRLF notices. The independent 102-file hygiene scan
resolved 251 local links and found mixed line endings in current-work.md; normalize
that file consistently during the documentation correction. No other scan issue
was reported.

Keep all corrections in the current Codex EXECUTOR chat. Return the complete
unstaged/uncommitted/unpushed diff with current-work.md and actual evidence for
re-review. No commit/push GO, next slice or increment completion is granted.

## Continuation ruling after the partial correction (2026-10-06)

The executor correctly stopped at the ownership gap. The partial correction is
still NO-GO; three individual real-Git regressions and an Application build do not
close R1-R5. Codex reverified main, HEAD/local origin/main/live main at
**a9e0e2b0b3b05bd54d8d45aec6258c035a02e930**, an empty index and the same
**102 paths (26 modified, 76 untracked files)**. ADR-0029 and the generated client
retain the hashes recorded above. The sandbox's first live-ref request could not
connect; the approved read-only network request verified the live SHA.

This is an implementation gap inside ADR-0029's existing physical ownership and
bounded recovery contract, not authority to start a new slice. Continue the same
executor chat. The following implementation constraints refine the correction;
ADR-0029 remains byte-preserved, and R1-R5 acceptance remains required.

### Physical index effects and conservative restart ownership

- Add focused operation facts for acquisition and the planned index replacement,
  durably recorded before ref mutation and before its index effects. Persist the
  observed administrative-directory, preimage, prepared-artifact and created-lock
  physical identities, exact lengths/hashes and operation-derived names. Keep
  native handles and interop in Infrastructure; Application owns short receipt
  transactions through its DbContext and existing manual-transaction semantics.
  A file ID, UUID, pathname or content hash alone is not an ownership capability.
- Acquire the prepared artifact, existing index and exclusively created index.lock
  through physically proven held handles. Revalidate regular-file/link facts,
  namespace binding and bytes across effects. Apply an explicit bounded index
  artifact limit, including the preimage; 16 MiB per index is the selected cap.
  Do not reopen an unbounded pathname to perform the effect or recursive cleanup.
- Replace the hash-then-File.Move(overwrite:true) sequence. Use handle-bound native
  renames with replacement disabled: move the held exact preimage to one
  operation-derived quarantine name in the same proven administrative directory,
  then rename the held prepared lock to the now-vacant index. Protect and prove
  directory binding. An occupied quarantine/index name is a conflict, never
  permission to overwrite it. This pair is NOT one atomic transaction; record and
  test the intervening missing-index boundary. A foreign index appearing in that
  interval must survive unchanged. No source file or main-checkout path may move.
- Delete only a file still owned through the live acquisition handle, using a
  handle-bound disposition after its proof. Disposal alone closes handles; it must
  not silently release a reservation or remove a pathname. Unexpected replacement,
  failed rename/disposition, incomplete receipt or uncertain namespace fails closed.
- Restart loses the original live acquisition. Do not adopt, promote or delete an
  extant index.lock or quarantine solely from matching bytes or recorded file IDs;
  such unresolved objects require NeedsAttention. Physical IDs describe an object,
  not perpetual ownership after deletion/recreation. Do not add an anchor/hard-link
  protocol, USN journal, transaction filesystem or generic cleanup/retry framework.
- Recovery still proves Interrupted for exact, safely unpromoted states and
  Completed for the exact promoted ref/object/index with no unresolved ownership
  conflict. Finishing a pending index with an absent lock requires a NEW exclusive
  acquisition, its durable receipt and fresh exact preimage/artifact/directory
  proofs before the same no-replace effects; it never repeats commit-tree or ref CAS.
  Any missing proof, existing unknown lock/quarantine or unexpected state stays
  reserved/NeedsAttention. No manual takeover authority is added.

### Converter-free observations remain part of R2

The partial change removed two dangerous status calls but leaves preparation's
ordinary CaptureAsync exposed and makes SourceConsistent permanently false.
Finish this correction without changing checkpoint fingerprint semantics or the
ordinary reader's contract. A focused, controlled read-only Git observation view
is permitted inside this capability: owned fixed configuration and attributes,
explicit pinned HEAD/index and read-only object access, with no repository filter,
hook, fsmonitor, external diff/textconv, signing or lazy-fetch authority. Mutable
repository configuration and info/attributes must not regain precedence. Apply
immutable attribute decisions to every path the observation can read, not merely
the changed-file plan. Reject unsupported conversions; never normalize approval.
Prove the existing checkpoint fingerprint agrees for admitted controls and provide
an actual converter-free post-commit consistency observation. Unsafe or incomplete
observations refuse or require attention; a constant false is not a positive proof.
This is a local-commit observation boundary, not a generic Git framework or global
fingerprint redesign. Preserve all applicable source containment and byte limits.

### Independent primitive qualification, not implementation acceptance

Codex checked the official Windows contracts for
[CreateFileW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew),
[handle information](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfileinformationbyhandleex),
[handle effects](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle),
[rename](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info),
[disposition](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_disposition_info)
and [file ID limitations](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/ns-fileapi-by_handle_file_information).
An independent disposable native probe is retained outside the repository at
%TEMP%/devalcopilot-index-handle-probe-c648413785c7444cae8227e6da314e60.

The qualified no-replace probe confirmed both renames and held-file deletion on
this host; it retained the physical identities. Injecting a foreign destination
between renames returned error 183 and preserved its bytes. Normal pathname
write/delete and directory rename attempts were blocked by the held handles.
Do not infer protection from sharing modes alone: an overwrite-enabled native
rename succeeded in a separate diagnostic case, so that form is not admitted.
The first relative-directory rename returned error 87; a subsequent absolute-path
attempt with overly restrictive directory sharing returned 32. A sharing matrix
and a corrected no-replace probe qualified the actual host combination. Production
must still prove directory ancestry, native replacement races and crash behavior.

A separate real-Git fixed-configuration observation probe produced byte-identical
status/diff before interference. After adding a mutable working .gitattributes and
local sentinel filter configuration, it reported the new attributes path, kept the
expected tracked diff and did not execute the sentinel; pinned branch/parent stayed
unchanged. Its first launch failed because the PowerShell argument array was built
incorrectly; the corrected explicit argv launch passed. It did not prove the full
checkpoint fingerprint, application integration or every attribute/configuration
shape. Neither probe ran a real provider, modified this implementation, or supplies
missing R5 evidence.

Complete the remaining R1/R4 integration and R5 proof matrix in this same correction,
then run affected checks followed by relevant full validation. Use a persistent
process session and incremental polling for longer suites; a 30-second tool yield
is not a test deadline or result. Preserve actual failures and report fresh versus
retained evidence honestly. Return the entire unstaged, uncommitted, unpushed diff,
commit-ready current-work.md, actual checks/skips, native and recovery limits and
exact inventory for re-review. No staging, publication GO or next slice is granted.

## Native-buffer blocker diagnosis and continuation (2026-10-06)

NO-GO remains. Codex independently verified main, HEAD/local origin/main/live main
at **a9e0e2b0b3b05bd54d8d45aec6258c035a02e930**, an empty index and **109 paths:
27 modified tracked files and 82 untracked files**. The executor preserved the
prior planner record at 995f068c5af2fa2c35ef570d40d87fff241b2694e6ce13651c7240a8a3657f47;
ADR-0029 and the generated client retain their previously recorded hashes. The
first sandbox live-ref request could not connect; the approved read-only network
request verified the SHA. This update changes only the planner record.

The reported error 123 and success with a missing index do not establish an
unsupported Windows primitive. WindowsIndexEffectHandles.RenameNoReplace allocates
only the header plus encoded pathname bytes and fills that entire allocation. It
leaves no UTF-16 NUL at the end of FileName. The official
[FILE_RENAME_INFO contract](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info)
describes a NUL-terminated FileName independently of its byte-count field.

An independent bounded-memory native probe reproduced both symptoms deterministically:

- FileNameLength/dwBufferSize describe the intended pathname, but a controlled
  nonterminated tail EXTRA follows it: the call returns success and creates
  indexEXTRA; the intended index is absent.
- The same shape with a controlled *INVALID tail returns error 123.
- An explicit UTF-16 NUL yields success at exactly index.

The earlier planner primitive probe also omitted an explicit terminator and did
not control adjacent memory. Its isolated green result was insufficient qualification
of this boundary; this deterministic evidence supersedes that qualification. It
still does not reconstruct every historical failing event or prove the application.
Probe sources and results are outside the repository at
%TEMP%/devalcopilot-rename-buffer-review-9e440ded34d84416b8fec091fdd2766e.
No implementation or test source was edited by the planner.

Continue the SAME executor correction, without changing ADR-0029:

1. Correct the production rename marshalling: validate the ABI layout/checked size,
   allocate and explicitly zero the complete buffer including its terminating
   UTF-16 code unit, keep FileNameLength equal to pathname bytes excluding NUL,
   and pass the complete buffer size. Preserve RootDirectory=null for the qualified
   absolute-path form and ReplaceIfExists=false. Test the actual production buffer
   with deliberately poisoned adjacent bytes and both rename destinations; do not
   substitute an independently correct test encoder. Removing termination must
   deterministically restore a failure or wrong-name result.
2. A native success and intact source handle are not proof of the intended destination.
   Restore exact namespace confirmation after each rename, including the physical
   identity/bytes at the expected administrative index, before deleting the held
   preimage or returning Promoted/Completed. Pinned bytes may support the controlled
   source observation; they cannot substitute for this real-index confirmation.
   Missing/wrong-name/replaced indexes must remain reserved/NeedsAttention. Never
   remove the check on an unsupported assumption that a post-rename name oracle is
   inherently unreliable. The probe confirmed both final handle names and freshly
   opened named-handle identities on this host after a terminated rename.
3. Complete R2 isolation. LocalCommitGit.Observation currently passes the REAL
   administrative directory as --git-dir. The private index and --attr-source do
   not isolate its repository configuration or info/attributes. A real-Git probe
   first admitted filter=unspecified, then injected info/attributes before the
   status/binary-diff snapshot: the sentinel executed. The owned fixed Git-dir
   control did not execute it. The initial status-only diagnostic did not trigger
   the filter and was not sufficient evidence; the complete snapshot did. Use the
   already-authorized owned fixed-configuration view with bounded, physically
   proven artifacts and read-only object access. Cover mid-observation info/attributes
   and local configuration changes, unchanged/deleted/new paths, and positive
   checkpoint-fingerprint and clean-post-commit controls. Do not redesign the
   ordinary fingerprint or enlarge conversion authority.
4. Finish the existing R3 physical/receipt proof, including the accepted full
   volume/128-bit file-ID identity when it participates in a comparison; the new
   64-bit FileIndex tuple is not sufficient for the admitted ReFS contract. Keep
   IDs as observations, not restart ownership. Preserve conservative handling of
   existing locks/quarantine and the previously specified fresh-acquisition rules.
   Finish R1 binding/ownership and R4 safe-release checks, followed by all R5
   authority, exclusion, migration, persistence-fault, actual restart and head-chain
   evidence. The blocker fix is not permission to omit those tests.

Additional terminated native-pair controls passed across ASCII, spaces, Unicode
and longer directory names: four successful pairs and four intentional collision
cases. Each collision returned 183 and preserved the foreign index. A named-handle
control confirmed both exact destinations and their original/prepared physical
identities. These are independent primitive probes, not production-suite results.
The failed full/affected executor runs remain evidence and must be preserved.
Correct current-work.md's description of this blocker to include the marshalling
cause and the distinction between primitive proof and outstanding integration.

Run the focused regressions first, then the relevant full validation and required
mutations with fresh builds and honest fresh/retained reporting. Do not weaken
positive controls to accept Ambiguous, retry native calls, overwrite destinations,
adopt locks, or reinterpret an absent real index as completion. Return the complete
unstaged/uncommitted/unpushed diff for re-review. Preserve the new planner hash
provided in the continuation prompt and ADR-0029. No publication or next slice is
authorized.

## Private-observation fingerprint diagnosis and continuation (2026-10-06)

NO-GO remains for the SAME local-commit slice. Codex independently verified main,
HEAD/local origin/main/live refs/heads/main at
**a9e0e2b0b3b05bd54d8d45aec6258c035a02e930**, an empty index and **109 paths:
27 modified tracked files and 82 untracked files**. The executor preserved the
previous planner record at 90bbaff26a2c8b46121e60ff078b7b0e3e9f0c4aa17f782312b0606b4836a7e6.
ADR-0029 and the generated client retain their previously recorded hashes.
Only this planner record is edited here; no production or test implementation is
accepted, staged, committed or pushed.

The reported CheckpointNotCurrent is not evidence that converter suppression and
the accepted fingerprint are incompatible. The current private Git config omits
core.filemode, whose default is true. The Windows fixture's real repository records
false. Its unchanged 100755 run.sh is therefore reported as changed to 100644 by
the submitted private view. This adds both a porcelain record and a mode-change
diff block. A second difference is LocalCommitGitRunner's core.quotePath=false:
the ordinary checkpoint reader uses Git's default true, so a tracked Unicode
filename has a different diff header even after the file-mode discrepancy is fixed.
See the official [Git configuration contract](https://git-scm.com/docs/git-config).

Independent disposable Git 2.53.0.windows.1 probes compared the exact fingerprint
components, without invoking the application's private observer:

- ASCII tracked modification, deletion, untracked addition, and an unchanged
  executable entry: submitted view differs in status and diff; fixed
  core.filemode=false makes both byte-identical to the ordinary observation.
- Add a modified tracked Unicode filename: filemode=false makes status identical,
  but diff still differs; additionally overriding core.quotePath=true in the
  observation makes both components byte-identical.
- Add real administrative info/attributes and a configured sentinel clean filter
  after the baseline observation. The fixed private view still produces identical
  status/diff and the same SHA-256 fingerprint, and the sentinel does not execute.
  The established head/status/diff/untracked-path/untracked-hash serialization is
  unchanged. Both fingerprints were
  0ede5090dae670c71c03b134f4dc45e9d0b167850451a262a9fda20935a0668e.

Sources and results are outside the repository at:
%TEMP%/devalcopilot-fingerprint-review-9302a9f1f8f24c2998af9ad79266046f,
%TEMP%/devalcopilot-fingerprint-unicode-review-e9612e053e4a4eeda7000f7042aaf1e8,
and %TEMP%/devalcopilot-fingerprint-isolation-review-c51d06f3f2ee4b8abe6f38ee001bcc83.
These are mechanism/compatibility probes, not production adapter, native-effect,
SQLite recovery or acceptance-suite results.

Continue within ADR-0029, without another architecture or slice:

1. Give the Windows local-commit observation its explicit fixed profile:
   core.filemode=false and core.quotePath=true, scoped to the private observation.
   Override the runner's contrary quote setting there. Keep the private Git-dir,
   fixed configuration, disabled executable capabilities, immutable attribute
   source, controlled index and object access. Never copy arbitrary repository,
   global or included configuration into it or return to the real Git-dir.
2. Preserve executable modes in exact tree construction as already required:
   unchanged/changed existing 100755 entries retain 100755 and new entries use
   100644. Disabling working-filesystem mode detection is not staging a chmod.
   Do not canonicalize the old checkpoint, strip status/diff records, compare
   only selected paths or waive exact fingerprint equality. An unsupported
   presentation/configuration that changes the raw observation continues to
   refuse rather than trying alternative profiles until its hash matches.
3. Write failing-first production-boundary regressions for the fixture's unchanged
   executable entry and Unicode tracked path; additions/deletions/binary and
   actual source changes remain controls. Compare ordinary capture and production
   preparation, and prove actual execution/inspection/recovery remain isolated.
   Inject real configuration/info-attributes changes at observation seams and
   assert zero sentinel execution. Mutating either fixed value must be caught;
   an external content change must still refuse. Keep a negative incompatible
   raw-observation control instead of widening compatibility silently.
4. The native terminator, namespace confirmations and full FILE_ID_INFO changes
   are still unaccepted until their production tests and mutations pass. Remove
   the stale comment that claims native rename success alone proves its namespace:
   the new freshly opened named-handle checks are necessary. All original R1-R5
   binding, ownership, safe-release, durable acquisition, exclusion, migration,
   fault, startup recovery and completed-head-chain requirements remain in force.

Run affected checks first, then the relevant final validation sequentially.
Preserve the CheckpointNotCurrent failure and distinguish independent probes,
production results, fresh and retained evidence in current-work.md. Return the
complete unstaged, uncommitted, unpushed diff with a commit-ready entry only when
all bounded acceptance is met; otherwise report the next precise failed invariant.
Preserve the new planner hash supplied in the continuation prompt and ADR-0029.
No staging, commit/push GO, retry authority or next slice is granted.

## Prepared ref-transaction continuation for R1 (2026-10-06)

NO-GO remains for the SAME slice. Codex independently verified main,
HEAD/local origin/main/live refs/heads/main at
**a9e0e2b0b3b05bd54d8d45aec6258c035a02e930**, an empty index and **110 paths:
27 modified tracked files and 83 untracked files**. The executor report and
current-work.md instead say 118/91; that inventory is not supported by the current
Git listing. Correct it against actual paths, disclose any identified intervening
change, and do not invent an explanation if the historical difference cannot be
reconstructed. The prior planner record was preserved at
970ef30f69aba1d36c57f4cd0e310a92ad2a421340315ccb4cb3b14655718813.
ADR-0029 and the generated client retain their previously recorded hashes.

The symbolic-ref-at-update-ref defect is real. The test named
A_symbolic_redirect_of_the_owned_branch_at_the_CAS_never_moves_main_or_reports_promotion
currently asserts Promoted. Its reported green result pins the known defect, not
acceptance of R1. Restore the required non-promotion and preservation assertions
first and keep its failing baseline as evidence; no test weakening is authorized.

A one-shot --no-deref CAS checks the old OID but can replace a symbolic ref whose
target resolves to that OID. It preserves main but does not establish direct-ref
type. A pre-process symbolic-ref read cannot close this gap. The official
[update-ref transaction contract](https://git-scm.com/docs/git-update-ref) provides
start/prepare/commit/abort; prepare locks the queued reference, and an explicit
started transaction without commit aborts at session end. Git 2.53's
[files ref backend](https://raw.githubusercontent.com/git/git/v2.53.0/refs/files-backend.c)
confirms the old-OID/type distinction and its locking behavior.

A disposable real-Git probe qualified a bounded alternative:

- Start a held update-ref --no-deref --stdin session in the exact linked-worktree
  administrative context. Send start, one update of the recorded owned ref with
  exact proposed/parent OIDs, then prepare; do not pre-send commit.
- After prepare: ok, inspect the actual ref with a nonrecursive symbolic-ref read
  and verify direct type, exact parent and the held owned HEAD binding. A direct
  control committed; an already-redirected ref was detected and aborted, retaining
  its target and leaving main unchanged.
- Competing normal Git symbolic-ref writes to the owned branch and HEAD were
  refused by the prepared transaction's locks on this installed files backend.
  A separate live read handle denying write/delete on the exact HEAD also blocked
  a raw pathname write, without preventing the branch transaction's commit.
- Main HEAD/ref, index bytes and source content remained unchanged in both cases.

Probe source/results are outside the repository at
%TEMP%/devalcopilot-ref-transaction-head-review-c22848e10eb04df4a82245c4e0a5242c;
the preceding protocol control is at
%TEMP%/devalcopilot-ref-transaction-review-efd223432d634c4a9925f0cbfa13b91d.
Initial probe transport sent CRLF and failed; Git's stdin protocol requires LF.
An explicit symref-verify HEAD in the same update transaction failed because the
files backend already adds HEAD bookkeeping. A cross-worktree HEAD alias variant
was rejected as a bad name. Neither failed variant is an accepted design.
These are independent provider/primitive probes, not production acceptance.
They do not establish protection against arbitrary writers bypassing Git's locks.

The planner selects this bounded implementation continuation within ADR-0029:

1. Replace the one-shot ref operation with a focused, bounded Infrastructure-owned
   prepared Git transaction, hidden behind the existing local-commit Application
   capability. No general interactive shell/process framework, new Git dependency,
   custom reference hook, raw ref rewrite or additional mutation authority.
   Reuse the runner's fixed executable, controlled environment, disabled hooks,
   bounded capture and explicit proven git-dir/work-tree context. Pin/prove the
   supported ref backend/protocol; unsupported behavior refuses rather than
   guessing from CLI defaults or relying on undocumented behavior silently.
2. Hold/prove the exact regular, single-name, bounded administrative HEAD through
   a live Windows handle that denies write/delete; verify its literal binding to
   the recorded owned branch. Retain that binding protection through the relevant
   ref/index effects and final inspection, with explicit disposal semantics.
   Do not rely on the implicit HEAD reflog lock alone, mutate HEAD, or derive
   context from a newly redirected workspace .git file.
3. Wait for exact bounded start/prepare acknowledgements, then freshly validate
   the actual direct ref and expected parent under the still-prepared Git lock,
   ownership, binding and held index acquisition. Reject a symbolic/unknown ref
   and abort without repairing or restoring it. Send commit only after proof.
   Keep the parent's OID CAS and --no-deref defense. Validate post-effect evidence
   before allowing index promotion or any success record. A second unlocked
   preliminary read or a changed test expectation does not satisfy R1.
4. Define timeout/cancellation/EOF/kill and lost-ack outcomes at each phase. No
   automatic restart or resend of commit. Keep durable execution facts before
   effects and no SQLite transaction across process I/O. An uncertain commit,
   lost process ownership or extant unknown Git ref/HEAD lock remains reserved/
   NeedsAttention; neither execution nor startup adopts or removes it by path,
   PID, name or matching bytes. A confirmed owned abort still requires R4's fresh
   safe-release proof. Recovery uses exact recorded effects, never another ref CAS.
5. Prove the original redirect race red/green through the production boundary;
   add direct success, stale parent, detached/redirected HEAD, pre-prepare redirect,
   concurrent Git mutation while prepared, explicit-context redirection, protocol
   fault and before/after-commit restart controls. Main ref/HEAD/index/files and a
   foreign redirection must remain intact. Failures remain fail closed. Qualify
   the safety limits of provider locks honestly and stop if the required actual
   boundary cannot be proved; do not silently reinterpret interference as success.

Complete the remaining R2/R3 mutations and R4/R5 file-backed authority, exclusion,
migration, persistence-fault, startup/event/head-chain acceptance already selected.
Use bounded waits and capture final command results; missing console output is
not evidence that a suite passed. Update current-work.md with accurate inventory,
failing-first assertions, fresh/retained distinctions and limitations. Return the
whole unstaged, uncommitted, unpushed diff for review. Preserve the new planner hash
supplied in the prompt and ADR-0029. No staging, publication GO or next slice.

## Prepared-session liveness diagnosis and continuation (2026-10-06)

NO-GO remains for the SAME slice; the preceding prepared-transaction direction
remains selected. Codex reverified main, HEAD/local origin/main/live main at
**a9e0e2b0b3b05bd54d8d45aec6258c035a02e930**, an empty index and **110 paths:
27 modified tracked, 83 untracked**. The executor's corrected inventory matches.
The earlier 118/91 count used a collapsed listing and a PowerShell wildcard count,
not a file-path inventory; it does not establish a repository change. The prior
planner hash was ec55a8e4955b100ea4207affc01835b51b60d9047fb3428ec6b36b1a80feaedc.
ADR-0029 and the generated client retain their recorded hashes. The failed R1
refusal assertion remains required; the current one-shot implementation is unsafe.

The executor removed its attempted interactive owner after a reported timeout.
Codex inspected that removed source through the executor's recorded diff. It
created a second StreamWriter over Process.StandardInput.BaseStream with
leaveOpen: true, closed only that wrapper, then awaited process exit. Its abort,
disposal and several reads used CancellationToken.None without an internal deadline;
stderr was collected with unbounded ReadToEndAsync. Those are concrete implementation
defects, not evidence that prepared transactions prevent bounded read-only proof.

An independent .NET 10/real-Git probe outside the repository reproduced the pipe
lifetime defect for both commit and abort. Direct-ref/HEAD proof reads under the
prepared lock returned in 83 ms and 63 ms respectively. Git acknowledged commit: ok
or abort: ok but remained alive after the leave-open writer closed (a bounded
1.2-second observation); closing the actual Process.StandardInput then produced
exit 0 in both cases. Main ref/HEAD/index/source stayed unchanged and the aborted
foreign symbolic redirect stayed intact. Source and results are at
%TEMP%/devalcopilot-ref-stdin-review-bbb6a0068f59409ca560456831054184.
This qualifies the reproduced mechanism, not the exact phase of the historical
executor timeout or production acceptance. The
[StreamWriter leaveOpen contract](https://learn.microsoft.com/en-us/dotnet/api/system.io.streamwriter.-ctor?view=net-10.0)
and [Git transaction protocol](https://git-scm.com/docs/git-update-ref) support
this distinction. No production file or test was changed by the planner.

Continue the already-selected bounded Infrastructure owner, with these corrections:

- Own one stdin protocol writer and close the actual child stdin pipe after the
  terminal command/acknowledgement, before waiting for process exit. Disposing a
  leave-open wrapper is not EOF. Do not race multiple writers or pre-send commit.
- Enforce an internal 30-second transaction deadline, including prepared-lock proof,
  independently of caller cancellation. Bound protocol phases and cleanup separately;
  cleanup has at most five additional seconds. CancellationToken.None cannot bypass
  these budgets. Drain/cap stdout and stderr while reading, including a line without
  a terminator; a length check after ReadLine/ReadToEnd does not bound allocation.
- Bound abort, close, kill-tree and reap/disposal on every launch, protocol, proof,
  cancellation and exit failure. Terminate only the child owned by this transaction;
  never kill unrelated jobs or adopt/remove unknown ref/HEAD/index lock files.
  Closing stdin or killing a process does not itself prove non-promotion or safe release.
  Lost commit acknowledgement remains uncertain until exact durable evidence resolves
  it; no resend, restart or second CAS. Keep the R4 safe-release gate.
- Add deterministic phase-labelled regressions through the production boundary for
  normal commit and abort exit, actual EOF, caller cancellation, absent/malformed/
  overlong acknowledgements, missing newline, stalled proof, stalled exit and bounded
  disposal. Record which phase timed out. Restore the pipe-closure/deadline mutations,
  rebuild, prove detection and restore bytes before the final baseline.

Preserve the prior exact-context, held physical HEAD/index, direct-ref proof and
R1 race requirements. Complete R2/R3 mutations and R4/R5 authority, exclusion,
migration, persistence-fault, startup/event/head-chain proofs before claiming the
whole slice review-ready. Diagnose an actual remaining read timeout by command,
phase and bounded result; do not weaken a safety assertion or abandon the selected
boundary based on an aggregate test wait. Return the complete unstaged, uncommitted,
unpushed diff with an accurate current-work.md evidence entry. Preserve the new
planner hash supplied in the prompt and ADR-0029. No publication or next slice GO.

## Prepared owner partial-return review (2026-10-07)

NO-GO remains for the SAME slice. Codex verified main, HEAD/local origin/main/live
main at a9e0e2b0b3b05bd54d8d45aec6258c035a02e930, an empty index and **111 paths:
27 modified tracked, 84 untracked**. The added path is LocalCommitRefTransaction.cs.
The prior planner hash was f84cf9fb00838ca7dc0c6469cb4a7198572d2e9363afefb63580084280f7e30b;
ADR-0029 and the generated client remain at the hashes already recorded. This is
partial progress, not a completed R1 or R2-R5 review return.

The new owner closes actual stdin. Codex reran the two supplied real-Git tests
with --no-build --no-restore: 2/2 passed, none skipped. Code inspection and an
independent probe establish three further bounded corrections:

1. **Deadline/cleanup ownership is incomplete.** PhaseBudget is unused. Prepared
   proof calls in PromoteRefAsync receive CancellationToken.None rather than the
   owner's deadline, so sequential read timeouts can outlive the transaction budget
   while its ref locks stay held. DisposeAsync links cleanup waits to the already
   cancelled transaction deadline; after kill it can dispose without actually
   reaping the child. StartAsync exception cleanup only disposes Process and can
   lose ownership of a started child. Keep one disposed cancellation scope per
   phase, pass the owner budget through every proof read, and use an independent
   cleanup deadline to terminate/reap owned children and finish stream drains.
   Do not allocate an undisposed linked CancellationTokenSource per character.

2. **Protocol/output faults can be accepted.** ReadBoundedAsync represents stderr
   overflow as a non-null string, while FinishAsync tests only non-null; it can
   return true for exceeded output. ReadLineAsync discards every CR, transforming
   malformed bytes into the accepted acknowledgement. Count actual bounded input
   bytes and admit only the exact supported LF/UTF-8 protocol; explicitly pin
   stream encodings and propagate overflow/decoding/protocol failure distinctly.
   Close/cancel/reap faults conservatively; do not infer safe release from them.
   Codex compiled an unchanged copy of the actual owner outside the repository and
   drove its completion through an owned protocol-double child. Healthy control,
   5,000-character stderr and com<CR>mit: ok all returned Accepted=true/exit 0.
   Source/fixture/results: %TEMP%/devalcopilot-ref-owner-review-1e7049fd9f214175a2cb44f9dcd05239.
   This proves the owner's fault acceptance, not real-Git promotion or full validation.

3. **The rewritten R1 test no longer tests the CAS race.** It installs the symbolic
   redirect before ExecuteAsync. Recursive symbolic-ref HEAD then names the main
   ref, so AcquireIndexEffectsAsync rejects the initial binding before starting
   LocalCommitRefTransaction. Keep this as a separately named early-refusal control.
   Add the required redirected-ref race AFTER successful initial/acquisition checks
   at the actual new transaction boundary, plus proof-boundary interference, and
   assert that prepare/proof/abort were reached as appropriate. Do not disconnect
   the mutator or accept an early refusal as proof of the prepared-lock defense.
   The meaningful rebuilt mutation must bypass direct-ref proof and make the real
   race fail while preserving the normal-success control.

Complete these corrections together with the already-required live physical HEAD
protection and explicit proven context for proof reads. Preserve R4 exact safe-release
and uncertain-outcome behavior. Then finish the agreed R2/R3 mutations and R4/R5
file-backed authority, exclusion, migration, persistence/startup/event/head-chain
acceptance before returning the whole slice as review-ready. The current-work.md
claim that the rewritten redirect test exercises the transaction is unsupported;
label it as early refusal, and distinguish the historical failing race from fresh
evidence. No architecture expansion, application-port change, test weakening,
staging, commit, push or next slice is authorized. Preserve the new planner hash
supplied in the continuation prompt and ADR-0029.

## Executor transfer checkpoint (2026-10-06)

Continue the SAME selected local-commit slice and existing partial implementation;
this is not a new outcome or permission to redesign ADR-0029. The owner explicitly
authorized a new Codex executor chat to finish Claude's work. It supersedes the
original Claude dispatch preflight below, which remains historical selection evidence.

Codex independently verified main, HEAD, local origin/main and live refs/heads/main
at a9e0e2b0b3b05bd54d8d45aec6258c035a02e930, an empty index, 21 modified tracked
files and 76 untracked files (97 paths, counted as files rather than status directory
entries). No commit or push has occurred. The only planner edit in this transfer
is this handoff; its updated identity is supplied in the continuation prompt.
ADR-0029 remains byte-identical, SHA-256
cf9a28d43ebc6b07ed1ed7daf01813348c9974b899e1c750461c584bf3f8b294.
The partially regenerated client now hashes to
e215388b6ac6030b1024c73b39b1053cbc4f2912446b00b0fc31b0b9f8449969;
it is an unreviewed implementation change, not the original c45dde6e baseline.

The working tree contains the operation Domain model, Committing state/events,
Application request/execution/recovery/status and policy, Git preparation/repository
adapter, two migrations (operation storage and SQLite write-seam trigger guards),
startup/supervisor composition, cockpit controls/hooks/client, real-Git tests and
an API lineage/endpoint fixture. Their presence establishes progress, not correctness.
The trigger guards and their error mapping, reverse races, exclusion completeness,
Git effect/recovery boundaries and startup HEAD chain still require acceptance proof.

Claude reported preparation 15/15 and Git adapter 28/28 and a clean build, but this
planner has not independently rerun or accepted those reports. The API fixture was
being developed when credits ended. current-work.md still describes the previous
published budget slice; no local-commit delivery entry exists. There is no newly
added local-commit browser journey in this snapshot. Domain/Application/migration,
hosted/API race and recovery evidence, the complete browser journey, documentation,
mutations and full final validation must be inventoried and completed rather than
assumed. Existing files/tests must be inspected before deciding what remains.

Several dotnet and node processes were visible during transfer; ownership and job
state were not established. Inspect before launching builds or browser hosts to
avoid collisions. Do not kill unrelated processes or discard existing uncommitted
work. Keep this handoff and ADR-0029 unchanged after the transfer. All original
objective, exclusions, stop gates, evidence and uncommitted review-return rules
remain in force. This transfer grants no staging, commit, push or next slice.

## Independently verified baseline and original dispatch state

The immutable manual-run budgets slice is published and complete. Codex verified
main; HEAD, local origin/main and live refs/heads/main all
**a9e0e2b0b3b05bd54d8d45aec6258c035a02e930**. Live main was read directly with
`git ls-remote`. Substantive 40ab3e84502f1c22311e56e6979957d0d4477388 has
expected parent e0ab52676c70e8d9c9d0760343e5607ec4aed8f4 and its 35 paths equal
the approved manifest (SHA-256 ab0757d6ed4a2bceb757bc7bb0d07aa316cbae9b50c0706226a9cd27a0d3bd2e).
All 34 current files except closure-edited current-work.md match the approved raw
hashes and sizes. Closure a9e0e2b changes only that slice's current-work.md entry.
Codex inspected its fresh browser log: Chromium 12/12, journeys 4/4, exit 0.
Other post-publication results remain executor evidence, not independent reruns.

Before this selection the index was empty, only planner-handoff.md was modified
by Codex's earlier unselected proposal, and nothing was untracked. That proposal
is now superseded by this owner-authorized selection; no unexpected code changed.
After planner edits, the exact expected executor preflight is:

- Branch main; HEAD/local origin/main/live main at the full baseline SHA above.
- Nothing staged.
- Four modified tracked files: docs/decisions/README.md,
  docs/engineering-context.md, docs/roadmap/mvp-delivery-plan.md,
  docs/roadmap/planner-handoff.md.
- One untracked file: docs/decisions/0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md.
- No implementation diff or other untracked path.
- Generated api-client.ts SHA-256:
  c45dde6e9217a6bc1803c0993168c80dc571172120ef23e9b47cf35253d145db.

Preserve the planner record and accepted ADR-0029 byte-for-byte during execution.
Read the selected contract and routed standards; report a material preflight
mismatch before editing. The original preparation SourceCommitSha stays immutable.

## Objective, benefit and implementation boundary

Finish one manual collaboration journey with a durable, reviewable local commit,
then a Completed run and persisted local delivery evidence after reload. This
reduces the remaining MVP gap more than another context sampler or allowance
observation. It uses the existing isolated worktree, exact checkpoint, verification,
Agent review and explicit human approval; it does not publish remotely.

The one coherent slice includes the protected POST and read-only status, focused
Domain operation and migration, fresh approval/verification/ownership policy,
serialized workspace reservation and single-use execution marker, a bounded Git
adapter behind Application ports, hosted execution, startup recovery before normal
workspace reconciliation, sequenced events and selected-run cockpit controls.
Only the exact recorded host commit extends expected-HEAD reconciliation.

ADR-0029 resolves the essential seams:

- Immutable physically proven complete bytes, isolated index and explicit raw
  blobs/tree, not previews, working-path patches or git add/commit.
- Current exact enabled recipe snapshots and Passed executions, the latest
  applicable real CodeReviewer approval and implemented-report lineage, and
  explicit Approved human evidence with no mixed human decisions on that checkpoint.
  No timestamp or UUID is used to infer review supersession.
- Durable Committing reservation, competing claims and recipe/review/checkpoint
  writes excluded at their transaction seams; all authority re-read untracked.
- Hook-free, unsigned, network-free commit-tree and expected-parent update-ref;
  no external filter, immutable attribute source and identity-only built-in
  conversion. CRLF normalization is refused rather than silently committed.
- Owned administrative index lock and exact preimage/prepared-index promotion;
  no source overwrite, main-checkout mutation or removal of another process's lock.
- One admitted operation per run, idempotent identical operation UUID, exact
  terminal facts and ambiguous results requiring attention, never blind retry.
- Restart reconciliation of recorded ref/object/tree/parent/trailer/index facts;
  no plausible-commit search, adoption of external advances or invented outcome.
- Lifetime-owned form and request state; unknown POST outcome reconciles through
  read-only status, and pending/failed reads never enable mutation.

A failure proved unpromoted fails the run; an interruption proved unpromoted
interrupts it; confirmed promotion completes it. Ambiguity stays nonterminal and
reserved or NeedsAttention. Post-observation edits never enter the immutable commit
and never justify showing the worktree as clean. These are outcomes of this one
operation, not new generic run completion, replacement or retry authority.

## Exclusions and stop gates

Exclude automatic coordinator, multi-project scheduler/concurrency redesign,
pause/resume/takeover, generic retry/recovery, run replacement, provider-session
resume/compaction, Claude account enforcement, new permissions, push/PR/CI/merge,
branch/worktree cleanup, generic filesystem hardening and raw fingerprint changes.
Budget, token, account and grant policies retain their existing scope and meaning.

Stop and report the exact evidence gap if complete source/tree identity, safe
attribute handling, transaction-seam exclusion, owned index promotion or exact
recovery cannot be proved within ADR-0029. No fallback to git add, blind Git retry,
unchecked conversion, partial source, weak approval or broader ownership is allowed.
Unsupported host/file/configuration states get fixed refusals, not false eligibility.

## Acceptance evidence and review return

Require real Git, file-backed SQLite and protected API evidence for exact tree,
parent, branch and index; additions/deletions/binary/LF controls; every refusal;
physical links; hook/filter sentinels; source, ownership, author, authority and index
changes; populated trackers; transaction-seam races; same-ID replay/conflicts and
competing claims/configuration writes. Prove crash/persistence boundaries before
and after ref/index promotion and outcome recording, including restart and external
interference. Pin main-checkout and unrelated-run isolation. Doubles may drive
providers, not substitute for the actual Git mutation proof.

Extend a production-written native-double browser journey through explicit human
checkpoint approval and the rendered local-commit control, real host Git execution,
terminal evidence and final reload, with normal authentication, Program composition
and all supervisors. No raw SQL supplies the acceptance approval/commit lineage.
Use targeted red/green regressions and meaningful mutations for source construction,
approval/exclusion gates, expected-parent CAS and recovery; rebuild mutated binaries
and restore source hashes before the baseline. Run relevant full validation after
implementation, .NET sequentially, generated-client regeneration, frontend checks,
harness then one canonical test:e2e:all, audits, baseline-aware format, links and
tracked/untracked whitespace/NUL hygiene. Preserve unexpected failures and distinguish
fresh from retained evidence; an unchanged rerun does not establish a fix.

Return the complete unstaged, uncommitted, unpushed diff, including a commit-ready
current-work.md entry, actual commands/counts/skips, failure excerpts, changed paths,
remaining limits and generated-client identity, for Codex GO/NO-GO. No publication
is authorized by this selection. A future GO will give one instruction for the
reviewed substantive commit, normal fast-forward push, live verification, fresh
checks and tightly bounded factual documentation closure. Material post-GO changes
return for review. No next executor slice is authorized.

## Open Increment 4 evidence gaps remain open

Claude account usage still lacks a bounded fresh non-invoking observation contract
for this host. Last-known /usage bars and post-invocation status-line evidence are
not pre-claim enforcement or invocation eligibility. No scraping, credential
extraction or undocumented endpoint is authorized. Both providers document resume,
but ownership/tamper/account isolation, permission/schema preservation and ambiguous
outcome/restart contracts are not proved by defaults or a session ID. Current
adapters deliberately use ephemeral/no-session-persistence envelopes. Compaction
also needs a safe owned retained session. Additional profiles cannot enlarge the
accepted read-only or workspace-edit envelopes. No Unknown-only scaffolding is selected.

Official provider contracts were checked on 2026-10-06:
[Claude costs](https://code.claude.com/docs/en/costs),
[Claude status line](https://code.claude.com/docs/en/statusline),
[Claude CLI](https://code.claude.com/docs/en/cli-reference),
[Codex non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode) and
[Codex App Server](https://learn.chatgpt.com/docs/app-server).
Git primitive sources and the independent disposable planning probe are recorded
in ADR-0029. They qualify this design, not its implementation or real-provider reliability.
