# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md),
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Current decision (2026-10-08): local-commit publication GO

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
