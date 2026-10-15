# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md),
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## AppContainer report corrections accepted; GO to publish bounded evidence (2026-10-15)

Independent preflight: main; HEAD/local origin/main/live main all
750bf6c727908d18c47bda7d73660b8edb2861a3, 0/0, empty index; exactly three
modified documents and no untracked paths. Planner before this record:
3a6d0bf9462dc46b0c76be8d9f982e2102b7a18813e9b049b9a2d2509a3a5d58;
all prior raw bytes preserved. Generated client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.

R1-R3 accepted for the experimental report. The original malformed receipt,
producer, binaries, 51-entry manifest and all original artifacts remain
unchanged; the strict original parse failure remains. The derived summary
is valid JSON, visibly derived and not a repair, with independently checked
moniker, package SID, folder/HRESULT facts, source hashes and record provenance.
It introduces no profile query or reconstructed evidence. All 51 original
hashes and all four correction hashes match. Original manifest:
b1f8f8e6a52e56849a347053596703dea19b73b8f593b482a36b22fbe7c6afc4;
correction manifest:
1a2141b0e157ef4facd34cb5131e11c0dd088b0033b3b9395f656d9767676954.
Sections 1-13 remain the exact 81391-byte baseline prefix. Independent checks
and original returned correction documents:
logs/planner-review/p9-appcontainer-gate/review2/.
No native harness, derivation/validator script, profile query, build or suite
was run by Codex in this review; checks parsed retained files read-only.

The observed path remains supported: ordinary operations succeeded, app
scratch read/write succeeded, four source operations returned error 5 and
host snapshots/termination records agree. This is a bounded combined
AppContainer/DACL/integrity observation, not P9, a mechanism acceptance or
proof of all error paths. The CloseHandle, host capture-write and unchecked
GetExitCodeProcess defects remain in the frozen binaries. No binary reuse,
production use or new native attempt is accepted.

Provenance corrections are accepted: pre-redaction text originals are
unavailable; retained copies are derivatives; embedded object-file paths
remain disclosed; host transcript comments are distinguished from JSON
records; class-46 absence is scoped; attempt 1's root/log and the absence of
a profile contents/security/registry snapshot are explicit. The profile has
only creation/folder/token evidence, not a fresh retention verification.

Codex made final reporting-only clarifications in section 14 and current-work:
DONE plus termination corroborates the recorded path, not the numeric child
exit code independently of the unchecked API. The wrapper measures host exit,
not child exit. A later validator invocation was executor-reported refused
by exclusive-create, with original log hash unchanged and no accepted result;
no separate failed-invocation capture was supplied, and none is reconstructed.
Executor checks/counts are labelled as applying to the returned correction;
Codex's final documentation checks are separate. No artifact was rewritten.

GO covers only the frozen three modified documentation paths, including
current-work and this planner record. Freeze raw hashes/lengths, normalized
modes/blobs and baseline entries at
logs/planner-review/p9-appcontainer-gate/publication-manifest.json.
Publish one commit: docs: record AppContainer fixture gate evidence,
with the Claude Sonnet 5.5 co-author trailer and sole parent equal to the
verified baseline. Normal fast-forward push and live ref verification only.
Verify the manifest and exact staged/committed inventory; baseline comparison
uses the recorded parent, never the new HEAD. No edits, manifest refresh,
experiment, validation rerun, generation, closure diff or next slice by the
executor. Stop on baseline, manifest, staging or remote discrepancy.

Retain profile DevalCopilot.P9.Gate.6e2898f9b593578aa98d3297e6a731d5 and SID
S-1-15-2-3528784686-543288942-2972599558-3957689933-405336475-2183898758-483271991.
The one-profile/two-child authority is spent; no query, reuse, new profile,
new child, deletion, cleanup or permission reset. ADR-0050 stays Proposed,
P9 unproven, no production mechanism selected; B's production/credential
integration remains paused. Agent qualification is Blocked/TargetsNotReal,
both allowances unused; Git/GitHub qualification is separate and unstarted.
No experimental real Git/GCM/helper/provider/network or credential operation,
installation/login or ledger reset. Repository publication commands alone
are authorized by the frozen publication prompt. Preserve every artifact,
root/protected DACL, failed attempt/capture limitation, v1 stop-gate defect
and separately unexplained concurrent-replay 409, target-test 500, disposal
NullReferenceException and unnamed Api 2067/2068. Later passes prove neither
flakiness nor irrelevance. Codex selects the next work after independently
verifying publication; this GO selects no next implementation or experiment.

## AppContainer gate review: NO-GO for reporting corrections only (2026-10-15)

Independent preflight: main; HEAD/local origin/main/live main all
750bf6c727908d18c47bda7d73660b8edb2861a3, 0/0, empty index; exactly three
modified documents and no untracked paths. Planner before this record:
14effb32d04369b9b7eaa9b4a4cf260c510d5b33b530c52acc5fa30649795554;
all prior raw bytes preserved. Generated client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.

Observed experimental path is supported. The six control operations returned
success with win32=0; both sets of eight child records match the embedded
transcript. App scratch read/write succeeded and all four source operations
returned error 5. The retained app creation target is absent, three source
file hashes/lengths match the fixed original bytes, and scratch matches the
fixed rewritten bytes. Two termination records report one total, zero active,
successful job query and direct termination. One profile creation follows
control completion and precedes the app launch. All 51 evidence hashes match
b1f8f8e6a52e56849a347053596703dea19b73b8f593b482a36b22fbe7c6afc4.
Sections 1-13 remain the exact 81391-byte baseline prefix. No experiment,
profile access, build, suite, generation or cleanup was run by Codex.
Evidence: logs/planner-review/p9-appcontainer-gate/review1/.

NO-GO applies to the current reporting/evidence presentation, not a finding
that the observed AppContainer source refusals failed. Three bounded items:

R1: runs/run-20261015T210720Z-16872/out/profile-receipt.json is malformed JSON.
Its folder value contains unescaped backslashes; strict parsing fails at
folder with Bad JSON escape sequence: backslash-A. SHA-256 of the original:
ae0b57e45738632d3db2fc869181b36cf96c013a94642a6708bf33fd10e104be.
The host writes the redacted path directly into JSON without JSON escaping.
Do not change the original file or its sealed manifest, source or binary.
Record the defect and add a distinctly labelled derived profile summary in
correction1/, using the valid planned-moniker and retained host PROFILE/TOKEN
records. Serialize and strictly validate that new summary; include source
paths/hashes, the original receipt's invalid status and no claim of a fresh
profile query or repaired original. Do not reconstruct any missing capture.

R2: the harness does not satisfy the full requested native-error guard
contract. Child op_open_close (p9ac-child-v2.c:98) returns success even if
CloseHandle fails; EXPECT_OK checks only the boolean, so a control could
continue after that failure. Host emit/write_capture do not propagate failed
or partial capture writes/flushes into the gate; GetExitCodeProcess's BOOL
is ignored (p9ac-host-v2.c:929). These are static defects/limitations, not
observed failures in the canonical run and not assigned causes of history.
Record the scope explicitly: the observed ordered path passed, while the
complete error-path/stop contract is not established. No binary reuse or
production acceptance follows. No source fix, fault injection or rerun is
requested or authorized in this correction; broader guard auditing is not
part of it. A later native attempt needs separately selected corrections.

R3: correct provenance/precision in section 14 and the current-work entry.
State whether pre-redaction originals of the four in-place edited text
captures survive; if not, mark them unavailable, never reconstruct them.
Call the retained copies redacted derivatives; distinguish runtime SID/path
redaction, later text redaction and the embedded object-file paths. Keep the
original 51 files/manifest unchanged and put all new correction evidence
under correction1/ with a separate manifest. Host transcript is annotated:
287 host plus 16 child JSON records and eight comment markers, not strict
JSONL as a whole; the separate child files are valid JSONL. Scope class-46
unreadability to this observed host/token/API query; do not claim ordinary
tokens universally cannot return it. Fix "before anything was created" to
"before fixtures, profile or children" (empty root/log existed). Distinguish
source descriptor/label read-back from the profile's creation/folder receipt;
no profile contents, security state or registry snapshot was taken.

Keep sections 1-13 byte-identical, preserve this planner and generated client,
and retain all observed positive results and the v1 pre-profile attempt.
Only section 14, its current-work entry and new ignored correction evidence
may change. Expected inventory remains exactly three modified documents,
no untracked paths, empty index. Run read-only strict JSON checks of original
and derivative (retain the named original failure), source/receipt field
cross-checks, all 51 original hash checks, the correction manifest and focused
link/byte/secret/diff/protected-hash checks. No product validation, probe,
profile/ACL/security operation, network, helper, credential or cleanup.
Return unstaged/uncommitted/unpushed in the same executor chat; no publication
or next slice. A challenge is welcome with evidence; pause dependent work
rather than silently broadening or weakening the correction.

The one-profile/two-child authority is spent. Retain profile
DevalCopilot.P9.Gate.6e2898f9b593578aa98d3297e6a731d5, package SID
S-1-15-2-3528784686-543288942-2972599558-3957689933-405336475-2183898758-483271991.
No requery, reuse, deletion, new profile or new child is authorized here.
ADR-0050 stays Proposed, P9 unproven and no production mechanism selected;
B's production/credential integration remains paused. Agent qualification
stays Blocked/TargetsNotReal, both allowances unused; Git/GitHub qualification
is separate and unstarted. Preserve the v1 stop-gate defect, all ignored
artifacts and retained roots/protected DACLs, and the separately unexplained
concurrent-replay 409, target-test 500, disposal NullReferenceException and
unnamed Api 2067/2068. Later passes prove neither flakiness nor irrelevance.

## Owner-authorized AppContainer fixture gate selected (2026-10-15)

The owner answered "sim" in this planner chat to the immediately preceding
bounded AppContainer decision prompt. This explicitly authorizes creation
and retention of ONE new per-user experimental AppContainer profile and
the proposed owned local gate. It does not authorize credentials, real
network/provider qualification, production changes, cleanup or publication.
The earlier pending-decision record below is historical and is resolved only
for this gate. B's production/credential-dependent design remains paused.

Verified dispatch baseline: main; HEAD/local origin/main/live main all
750bf6c727908d18c47bda7d73660b8edb2861a3, 0/0, empty index; exactly one
unstaged modification, this planner; no untracked paths. Prior planner
SHA-256 b19df2632a9558e3c15aa69247aad6404db93d53092b8912256199a7dee1cf1a;
all prior bytes preserved. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.
No profile, native harness or other experimental operation has run in the
planner chat. Use a new executor chat for this selected outcome.

Selected question: can one regular, zero-optional-capability AppContainer
start, use a granted detached scratch fixture and refuse source creation,
truncation and both measured deletion routes? This measures a combined
AppContainer/DACL/integrity configuration, not the cause of a denial, every
escape route, Git object-copy correctness, P9 or GCM compatibility.
No production mechanism or replacement source-object design is selected.

New versioned dependency-free C host and kernel32-only child under ignored
logs/executor/p9-appcontainer-gate/. Installed MSVC/SDK only; capture actual
versions, imports, exact commands and full failed attempts. Prior harnesses,
binaries, captures, manifests and roots remain frozen. Statically review
control flow, paths, native result checks and source hashes before the run.
One canonical attempt, at most two sequential probe children and one profile.
No automatic repeat after a profile was created or a probe child launched;
a run-time defect returns to Codex. Build defects before that boundary may
be corrected with retained versions/complete logs and renewed static review.

Prepare independent fresh ctl/app fixtures under a canonical non-reparse
NTFS run root. Use fixed fictitious bytes, ordinary-user protected source
DACLs and read-back masks/inheritance. Ordinary control first; it must pass
scratch read/write and all four source mutations, with host-confirmed effects,
before profile creation. No real repository, credential or old fixture read.

Only after a valid control, persist a unique planned profile moniker and
then call CreateAppContainerProfile once for the current unelevated user,
with no optional capability SIDs. Moniker: DevalCopilot.P9.Gate.<32 hex>,
within the documented name bound. Abort on ERROR_ALREADY_EXISTS; do not
reuse, derive an existing profile's SID or create another. Retain the new
profile even on failure. Record the creation HRESULT, package SID and folder
locator receipt before launch; do not recursively inspect profile contents
or its registry. The OS-created profile folders/registry are the one
explicitly authorized persistent exception to the owned run-root boundary.
No manual profile ACL/registry changes or profile deletion.

For app only: grant the new package SID read/execute on its owned bin area,
read/write on owned scratch files, and needed traverse on scratch directories.
Set Low no-write-up labels on new app scratch and descendants only. Keep
source and executable integrity unchanged; read back labels (record an absent
label and its documented default separately) and all granted masks/flags.
No package/broad application SID source grant, no null DACL, no source label
lowering, no system/user-directory grant and no ancestor permission changes.
If any required setup/read-back needs privilege enabling or wider access,
stop without adapting the configuration. Source refusals may be due to MIC
as well as the package DACL; do not attribute them to either mechanism alone.

Both launches: CreateProcessW with fixed image/argv, CREATE_SUSPENDED,
EXTENDED_STARTUPINFO_PRESENT, CREATE_UNICODE_ENVIRONMENT and DETACHED_PROCESS;
explicit stdout/stderr/NUL handle list; a private non-breakaway kill-on-close
job, active-process limit 1, assigned before resume. Do not modify the window
station/desktop or call/import USER32 in the child; no UI or grandchildren.
For app add PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES using exactly the
new package SID, CapabilityCount=0, Capabilities=NULL and Reserved=0. No LPAC,
extra capabilities, token patch, broker, service, alternate user or fallback.
Read the suspended child's token: app-container flag true, package SID equal
to the new profile, Low integrity, not elevated. Record capabilities/groups/
privileges safely; distinguish the zero requested capabilities from other
OS-generated token attributes. For control confirm ordinary unelevated
non-AppContainer identity. Any failed token check prevents resume.

Explicit environment only: SystemRoot; TEMP/TMP in the child's own scratch;
LOCALAPPDATA in control scratch or the new profile folder obtained through
GetAppContainerFolderPath for app. Fixed scratch cwd. No PATH, home, auth,
Git/GCM or caller-environment import. No explicit profile/credential operation
by the child. The profile's own OS-provided writable storage is not evidence
that scratch is its only writable location.

Use six named operations in order: scratch read; scratch write/read-back;
source CREATE_NEW; source TRUNCATE_EXISTING; FileOnly DeleteFileW; ParentOnly
DeleteFileW. Separate source paths prevent interactions. Create/truncate
open-and-close is the whole designated source operation; success is a possible
mutation and must not be followed by a write or another operation in app.
Source counterexamples require host-observed source changes. An unexpected
success with unchanged source is inconclusive; redirection or another target
is not diagnosed in this gate and must not be inferred from the return alone.
FileOnly grants user file DELETE without parent FILE_DELETE_CHILD; ParentOnly
is the reverse, as section 12. Control expects success throughout. App expects
scratch success, then ERROR_ACCESS_DENIED for every source mutation, with
unchanged host snapshots. Emit and check each result before proceeding;
first unexpected result or output failure stops the child and matrix.
Check partial/failed WriteFile, every setup/capture/wait/exit API result and
host observations independently of child output. Missing output is missing
evidence, never proof of absent execution or a loader cause.

Bound each child to 30 s and each stream to 64 KiB, draining concurrently.
On failure terminate only the owned job; prove successful accounting with
zero active and direct process termination before disposing resources or
making after-snapshots authoritative. If that proof fails, stop and report
uncertainty; retain resources the child may still use. No later probe follows
a source mutation, failed control, capture, containment or termination gate.

Keep unique complete stdout/stderr, strict named JSON records, separate
metadata, actual wrapper exit captured immediately (not parse-time cmd
ERRORLEVEL), receipts, timestamps, before/after bytes/identities/DACLs/labels,
profile/grant inventory and source/binary/evidence SHA-256 manifests. Redact
personal/logon SIDs and personal profile paths before retaining any output;
package SID/moniker and fixed fixture bytes may be retained. Do not capture
full environment, credentials or profile contents. File hashes do not seal
live ACL metadata; preserve the recorded descriptors and state this limit.

Append section 14 to the feasibility note; sections 1-13 byte-identical.
Add a short current-work entry, including owner authority, result and exact
retained profile identifier. Preserve this planner and generated client.
Expected tracked inventory: three modifications, no untracked paths, empty
index. Validate changed links, byte/secret/diff checks, protected hashes,
inventory and evidence manifest. No product build, suite, journey, mutation
or generation. A negative/inconclusive outcome is valid. Return unstaged,
uncommitted and unpushed for Codex review; no next slice or commit/push.

Primary sources re-read by Codex for the native boundary: Microsoft Learn
CreateAppContainerProfile, GetAppContainerFolderPath, Launch an AppContainer,
UpdateProcThreadAttribute, Mandatory Integrity Control and SetNamedSecurityInfoW.
Executor verifies relevant signatures/constants against installed SDK and
reads the necessary primary sections; docs do not prove installed behavior.
Challenge any unsafe/contradictory/impractical requirement with evidence and
pause dependent work; no silent widening or manufactured consensus.

ADR-0050 remains Proposed; P9 unproven; B authorized only for this local gate.
Owner GCM/base/identity decisions remain design-only. Agent qualification is
Blocked/TargetsNotReal, both allowances unused; Git/GitHub qualification is
separate and unstarted. No real Git/GCM/helper/provider/network or credential
operation, installation/login, ledger reset, cleanup or permission reset.
Closing native handles after termination is normal resource disposal, not
permission to delete retained artifacts/profile or reset fixture security.
Preserve every prior limitation, v1 stop-gate defect and the separately
unexplained concurrent-replay 409, target-test 500, disposal NullReferenceException
and unnamed Api 2067/2068. Later passes prove neither flakiness nor irrelevance.

## A2 publication verified; owner decision before an AppContainer profile gate (2026-10-15)

Independent publication verification: main; HEAD/local origin/main/live main
all 750bf6c727908d18c47bda7d73660b8edb2861a3, 0/0, empty index and clean
tracked/untracked tree before this record. Sole parent
e2510c55112ec55f1fb8a030c1f2e005822926ca; subject and Claude trailer match.
All three committed paths, modes/blobs, parent entries and working raw
hashes/lengths match frozen manifest
5c414dca33bb68e68e292a989385a931e6a64257cf79b06293353669f127f2a3.
Evidence: logs/planner-review/p9-full-restriction-gate/publication-verification.json.
Prior planner SHA-256: 5967981948596626b458d2efaf4ac4e72009a8e248de3107f1359d97de8cd7d9;
all prior bytes preserved. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.
No publication, probe or validation repeated.

Decision: close the current A/A2 experimental sequence. A's tested parent
permission route permits deletion; A2 provided no ordered child result and
no demonstrated viable configuration. This establishes neither a general
impossibility for restricted tokens nor the cause of the A2 failure. Do not
retry either binary, add broad SIDs, grant system access or diagnose the
loader without a separately selected contract. More observer UI and history
would be optional improvements; they do not remove this release blocker.
P9, real supported-version compatibility and human resolution/rearm remain
runtime blockers. No push activation or P9 weakening is selected.

Recommended next outcome, pending owner authority: one small, owned local
AppContainer gate rather than another A2 variant. Primary documentation read
2026-10-15 describes a regular AppContainer's user/package access intersection,
its standard system-resource access, and profile creation with per-user/app
folders and registry storage:
https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer
https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createappcontainerprofile
This motivates a feasibility gate; it is not installed behavior or proof of
P9 or GCM compatibility. No helper compatibility conclusion follows.

The proposed effect needing owner approval is creation and retention of ONE
uniquely named per-user AppContainer profile, using the current unelevated
user. Planner judgment: this is a new persistent OS setup effect beyond the
previous gates' in-memory token and owned-fixture scope, which expressly
excluded profile operations. Do not infer that authority from the owner's
GCM/base or residual-identity decisions. Those design-only decisions stand
and are not being asked again. No AppContainer operation has been performed.

Concrete proposed gate if approved (not an executor dispatch): one ordinary
control, then one regular AppContainer child using CreateProcessW with the
security-capabilities attribute, zero optional capability SIDs, no network,
LPAC, broker, service, alternate user or credential operation. At most two
sequential children, no grandchildren, private pre-resume job containment,
fixed executable/argv, checked output writes and bounded captures/deadlines.
Use newly owned fictitious fixtures only: a detached scratch copy and
independent source create, truncate, FileOnly delete and ParentOnly delete
cases. Control effects must be host-confirmed before the container phase.
Scratch read/write must succeed; every source write must refuse with unchanged
host-observed fixtures. Stop at the first unexpected result or prerequisite
failure. The gate measures the combined configured boundary, not which
access check caused a denial and not all escape routes.

Package-SID grants and reduced integrity labels are restricted to new owned executable/scratch fixtures plus
the new profile's OS-created storage. No source grant to the package SID,
real repository ACL change, system grant, profile reuse, elevation, extra
capability, compatibility workaround or cleanup. Retain the new profile and
fixtures; cleanup/deletion would require a separate decision. Object copying
is fixture-only; no Git object-supply architecture is adopted. If the exact
bounds cannot support launch, containment or capture, stop without widening.
No real Git/GCM/provider, authentication or credential-store test is included.

Owner decision requested: authorize only that bounded profile/fixture gate,
or keep candidate B paused and select independent MVP work. No answer is
inferred from silence or a preselected UI choice. Until explicit approval,
B remains paused; no experiment or executor implementation is dispatched.
After approval, Codex must issue a fresh bounded prompt with verified Git
state, exact native setup, stop gates and acceptance evidence. This record
is not permission to prepare/run an unbounded harness or publish anything.

ADR-0050 stays Proposed, P9 unproven; no production mechanism selected.
Agent qualification remains Blocked/TargetsNotReal, both allowances unused;
Git/GitHub qualification is separate and unstarted. Preserve every ignored
source, binary, failed attempt, capture limitation, retained root/protected
DACL, v1 stop-gate defect and the four separately unexplained failures:
concurrent-replay 409, target-test 500, disposal NullReferenceException and
unnamed Api 2067/2068. Later passes prove neither flakiness nor irrelevance.
No real calls, credential access, installation/login, ledger reset, cleanup,
permission reset, commit/push or closure diff is authorized by this planning.

## Full-restriction gate report accepted; GO to publish inconclusive evidence (2026-10-15)

Independent preflight: main; HEAD/local origin/main/live main all
e2510c55112ec55f1fb8a030c1f2e005822926ca, 0/0, empty index; exactly three
modified documents and no untracked paths. Planner before this record:
743ba7f3199230e64e12a53006986ce7b23f846fb12462f7bdcd43c9a4185dff;
all prior bytes preserved. Generated client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.

Accept the bounded experimental report, not the configuration or P9. The
ordinary control produced seven named lines; its five operations and host
observations agree. The restricted process was created suspended, associated
with its private job and resumed, then recorded exit 0xc0000022 in 25 ms with
zero captured stdout/stderr. Both job queries reported one total and zero
active processes. Host snapshots show the restricted fixtures unchanged.
No widened grant, retry or later probe was observed. All 37 evidence digests
match manifest b7ea3d14b29b4e9defdd38a26a90c2372b8424a9292fba8e14d36d9bf74a8276.
Review artifacts and original returned documents are under
logs/planner-review/p9-full-restriction-gate/review1/.

Codex corrected reporting only in section 13 and its current-work entry:
process creation succeeded, so do not call this a proven absence of launch;
no ordered operation result was captured, which does not prove no child
code or operation executed. The child emitter ignores WriteFile's return.
No independent child-entry observation exists. Loader/resource denial is
conditional inference, with no system-resource ACL or failing-object diagnosis.
No stronger startup or cause claim is accepted. This has no relationship to
ADR-0050's proven NotLaunched classification. No source, binary or raw capture
was changed, and no experiment was rerun.

The executor's challenge that the closed grants may prevent initialization
is material and plausible, not a demonstrated general impossibility. The
selected gate intentionally forbade expanding grants. Its inconclusive
outcome is sufficient to close this experiment; do not repeat it, add a SID,
change a system ACL or silently replace its object-supply architecture.
No further configuration, diagnostic probe or next slice is selected here.
Codex will select the next bounded work after publication verification.

Preserve the false wrapper exit=0 (host exit unmeasured), two failed toolchain
captures, missing-include a1 build failure, valid a2 binaries, newly selected
SDK 10.0.28000.0, and all raw outputs and roots. Earlier SDK observations are
historical, not overwritten. Host capture has eight explicit # metadata lines
and 272 JSON records; the first reviewer parser tried the metadata as JSON
and emitted errors in the tool record, then the corrected read-only parse
retained and counted them separately. Neither parse ran a child.

GO covers only the frozen three modified documentation paths, including
current-work and this planner record. Manifest:
logs/planner-review/p9-full-restriction-gate/publication-manifest.json.
Publish one commit: docs: record full-restriction launch gate outcome,
with the Claude Sonnet 5.5 co-author trailer and sole parent equal to the
verified baseline. Verify all manifest raw hashes/lengths, normalized modes/
blobs and parent entries before staging and after commit. Normal fast-forward
push and live ref verification only. No edits, manifest refresh, experiment,
validation rerun, generation, closure diff or next slice by the executor.
Stop on any baseline, manifest, inventory, staging or remote discrepancy.

ADR-0050 stays Proposed, P9 unproven and B paused. No mechanism or production
change is accepted. Owner decisions remain design-only. Agent qualification
stays Blocked/TargetsNotReal, both allowances unused; Git/GitHub qualification
is separate and unstarted. No real Git/GCM/helper/provider or credential call,
installation, profile, login, ledger reset, cleanup or permission reset.
Preserve all ignored evidence and roots/protected DACLs, the v1 stop-gate
defect and the separately unexplained concurrent-replay 409, target-test 500,
disposal NullReferenceException and unnamed Api 2067/2068. Later passes prove
neither flakiness nor irrelevance. No frozen probe binary is approved for reuse.

## Deletion-route publication verified; selected full-restriction viability gate (2026-10-15)

Independent verification: main; HEAD/local origin/main/live main all
e2510c55112ec55f1fb8a030c1f2e005822926ca, 0/0, empty index and clean
tracked/untracked tree before this record. Sole parent
8ed77baea6f5f90e78636d25b8684c3a092d7b8d; subject and Claude trailer match.
The three modified paths, modes/blobs, parent entries and working raw
hashes/lengths match frozen manifest
71c232fbca6ec5b0f7568e5338c807551a49479ea6c421aaa4bc891427e07409.
Evidence: logs/planner-review/p9-delete-routes/publication-verification.json.
Prior planner SHA-256: 04d3a164ee08b4b4ac2a574b61167d87a746240ab88f501a68901ed57f1cc1c8;
all prior bytes preserved. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.
No publication, experiment or product validation repeated.

Decision: the tested WRITE_RESTRICTED configuration is insufficient for P9.
Do not patch it, change source ACLs, or accept a post-write detector. Select
one owned full-restriction viability experiment, labelled A2 (experimental
only): CreateRestrictedToken with DISABLE_MAX_PRIVILEGE, exactly one fresh
synthetic restricting SID, no WRITE_RESTRICTED, no other flags or SID changes.
Microsoft documents two access checks for restricting SIDs and limits
WRITE_RESTRICTED to write evaluations:
https://learn.microsoft.com/en-us/windows/win32/secauthz/restricted-tokens
https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-createrestrictedtoken
This motivates an experiment, not a claim that removing the flag fixes P9.

Question: can this exact configuration start under the existing unelevated
user without non-owned ACL changes, use a granted detached scratch copy,
and refuse source reads and both measured deletion routes? A child that
cannot read the live source would require a different object-supply design
(such as an independently owned copy) before production use. That is a
hypothesis for later design, not an amendment of Proposed ADR-0050, not a
Git object-copy implementation, and not evidence of GCM compatibility.

Use new versioned dependency-free C source/binary and unique complete
captures under ignored logs/executor/p9-full-restriction-gate/. Freeze all
previous harnesses, fixtures and manifests. At most two sequential children:
one ordinary CreateProcessW control, then one restricted CreateProcessAsUserW
child. Keep the differing launch API as a limitation; no causal equivalence
claim. Both use fixed executable/argv, private desktop, explicit stdio handle
list and pre-resume non-breakaway kill-on-close job. No grandchildren.
No privileges enabled, elevation, profile, service, helper or credential use.

All new fixture descriptors, grants, labels and file operations are confined
to a new canonical non-reparse owned run root. Grant synthetic-SID RX only
to the owned executable area; grant needed scratch access to the detached
fixture copy, output and TEMP. Grant no access to system/user directories,
source fixtures, real repositories or old roots. No broad restricting SID,
capability, null DACL or ambient fallback. If loader, desktop, capture,
containment or termination cannot work under these limits, record and stop;
never expand grants to make the run green.

Use fresh source fixtures: a private readable sentinel (ordinary user may
read it; no synthetic-SID source grant), a FileOnly deletion fixture and a
ParentOnly deletion fixture with protected read-back DACLs as section 12.
Use separate source and scratch copies of fixed fictitious bytes, no real
Git objects. Close fixture I/O handles before launch. The ordinary child must
read the sentinel and scratch copy, write scratch, and delete both designated
deletable files; host observations must agree before restricted launch.
Restricted fixtures are fresh equivalents. Ordered restricted gates: prove
scratch copy read/write; source-sentinel read denied; FileOnly delete denied;
ParentOnly delete denied. After each operation emit a named raw result and
check the expected result before the next. First unexpected result stops
the child; the host also stops the matrix. Never run later probes after a
source mutation or a failed prerequisite. Complete captures and host snapshots
remain even on failure. No DACL/token/host-handle escape probe in this gate.

Record exact token metadata (including flags/restricting SIDs), descriptor
masks/flags, job query success and zero-active accounting, outcomes and
before/after bytes/presence. Redact personal SIDs before writing retained
logs; keep synthetic SIDs. A loader failure or counterexample is a valid
inconclusive/negative outcome. Finite passing cases would not establish P9,
source-copy correctness, escape resistance, Git/GCM or provider compatibility.

Append section 13 to the feasibility note; preserve sections 1-12 byte for
byte. Add one short current-work entry; preserve this planner and generated
client. Expected inventory: three modified documents, no new tracked or
untracked files. Build only the probe after static gate review; one canonical
run, with affected reruns only for a named harness defect, complete retained
logs and no configuration widening. Validate changed links, diff, byte/secret
checks, protected hashes, inventory and evidence manifest. No product builds,
suites, browser checks, generation, real calls, setup or cleanup. Return an
unstaged/uncommitted/unpushed diff for review; no commit/push authority.

Candidate B remains paused; no production mechanism selected. ADR-0050 stays
Proposed and P9 unproven. The owner accepted the restricted GCM/base profile
and residual identity risk for design only, not execution or credentials.
Runtime activation also remains blocked by supported-version/real compatibility
qualification, durable containment/exclusion and human resolution/rearm.
Agent qualification stays Blocked/TargetsNotReal, both allowances unused;
Git/GitHub qualification is separate and unstarted. Preserve v1's stop-gate
protocol defect, all ignored evidence and retained roots/protected DACLs, and
the separately unexplained concurrent-replay 409, target-test 500, disposal
NullReferenceException and unnamed Api 2067/2068. Later passes prove neither
flakiness nor irrelevance. No cleanup, ledger reset or new authority.

## Deletion-route diagnosis accepted; GO to publish bounded evidence (2026-10-15)

Independent preflight: main; HEAD/local origin/main/live main all
8ed77baea6f5f90e78636d25b8684c3a092d7b8d, 0/0, empty index; exactly three
modified documents, no untracked paths. Planner before this record:
40fab92cb632a206c87dd2ebfc346db746f0a32df6be398f373236aa0d9b7d76;
all prior bytes are preserved. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.

Evidence outcome accepted: eight raw child results and job termination
observations match; four ordinary controls completed before the restricted
phase. The restricted Neither/FileOnly cells denied deletion; ParentOnly/Both
deleted. The child path contains one DeleteFileW and no follow-on probe.
Protected single-ACE descriptor masks and read-back/observation evidence
support this bounded diagnostic. The launch API and token were varied
together, with per-case private resources; this is not an isolated proof of
an internal access-check path, the historical v1 cause, or a P9 mechanism.

All 46 evidence digests match unchanged manifest
d54e38a6d94613b6feb540616621d5634efcf75f9f0bf54a343570002a304421.
Published sections 1-11 are a raw byte-identical prefix; 131 local links
resolve; added-byte/secret-shape checks and diff check pass.
Returned note and raw baseline are under
logs/planner-review/p9-delete-routes/review1/. No experiment or build rerun.

Codex corrected two final reporting phrases in section 12: the icacls file
was redacted in place, so the retained derivative is not the original raw
capture (not retained); and future cleanup/permission changes were not
evaluated or authorized, rather than asserting every retained object needs
an owner DACL reset. No original capture was reconstructed and no fixture
was changed. Protected DACLs, retained roots and provenance limits remain.

GO covers only the three frozen documentation paths including current-work
and this planner record. Freeze raw hashes/lengths, normalized modes/blobs
and baseline entries at logs/planner-review/p9-delete-routes/publication-manifest.json.
Publish one commit: docs: record restricted-token deletion route diagnosis,
with the Claude co-author trailer and sole parent equal to the verified
baseline. Then normal fast-forward push and live ref verification.
No edits, probe/build/validation rerun, generation, closure diff or next
slice by the executor. Stop on any manifest, inventory, baseline or remote
discrepancy; do not regenerate the manifest to fit changed inputs.

ADR-0050 remains Proposed, P9 unproven and B paused. No new production
configuration, mechanism or runtime authority follows. No real Git/GCM/
helper/provider or credential operation, installation, qualification,
ledger reset or cleanup is authorized. The old v1 binary remains unapproved
for reuse. Owner design-only decisions and all limitations stand.
Agent qualification stays Blocked/TargetsNotReal, both allowances unused;
Git/GitHub qualification remains separate and unstarted. Preserve all
ignored artifacts, roots, v1's protocol deviation and the four separately
unexplained historical failures. Later passes prove neither flakiness nor
irrelevance. Codex selects the next task after publication verification.

## Counterexample report published; selected owned deletion-route diagnosis (2026-10-15)

Independent publication verification: main; HEAD/local origin/main/live main all
8ed77baea6f5f90e78636d25b8684c3a092d7b8d, 0/0, empty index and clean tracked/
untracked tree before this record. Sole parent
b5f985e25381b3fd990b79b49ed080d6511d33aa; subject and co-author match.
All three modified paths, modes/blobs, parent entries and raw hashes/lengths
match manifest 15a025037f5802eb833a9bd58106dab15e103ba377d755b49ef98aba882dbe4a.
Evidence: logs/planner-review/p9-token-gate/publication-verification.json.
Planner before this record: 9ec25b0c23132282e4610c19188638e650cf554ee944faf4dc7dcd53174453e5;
all prior bytes are preserved. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.
No repeated publication or experiment.

Decision: diagnose the observed deletion using a new, bounded owned fixture
harness before choosing another configuration or mechanism. Microsoft documents
file DELETE or parent FILE_DELETE_CHILD as alternative deletion permissions:
https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-deletefilew
https://learn.microsoft.com/en-us/windows/win32/fileio/file-security-and-access-rights
https://learn.microsoft.com/en-us/windows/win32/fileio/file-access-rights-constants
This motivates the matrix; it does not establish the historical deletion's cause.

Keep candidate A's token flags, synthetic restricting SID, scratch grants,
private desktop, explicit inheritance and pre-resume job containment. New
fixture-only protected DACLs isolate four ordinary-user permission combinations:
neither file DELETE nor parent FILE_DELETE_CHILD; file only; parent only; both.
No synthetic-SID source grant, token workaround, owner-repository ACL change
or candidate B. Validate effective descriptor contents rather than assuming
inheritance did what the setup intended. Ordinary controls must yield denial
for neither, success for the other three, with matching host observations.

Use new versioned source/binary under ignored logs/executor/p9-delete-routes/.
The defective v1 remains frozen and must not run. At most eight sequential
children (four ordinary controls, then four restricted cases on fresh fixtures),
no grandchildren. Each child performs exactly one DeleteFileW on its designated
new file and exits: no S4/H5/H6 or follow-on probe. This explicitly replaces
the earlier viability matrix's stop rule for this diagnostic only: allowed
deletion is a planned measured outcome, not a reason to chain additional
actions. Any failed control, setup, capture, containment or termination gate
stops subsequent cases. Never adapt permissions to obtain a preferred result.

Fixture ACL operations and deliberate deletion of new named fixture files
are authorized only within the new canonical, non-reparse owned run root;
private token/process/desktop/job operations stay within this harness.
Record source and parent DACL rights/flags before launch, ordinary/restricted
raw results, file presence and content before/after, token metadata and job
termination. Keep user/logon SIDs redacted. Close fixture I/O handles before
launch to avoid sharing-conflict explanations. No elevation or new privileges.
No real Git, GCM/helper, provider, credential, network or profile operation.

If useful, read only current ACLs of surviving old owned fixtures, clearly
labelled present-day evidence. Never mutate them or reconstruct the deleted
file's historical descriptor from assumption. Distinguish observed route
dependence, inference and an unproven internal/historical cause. A negative
or inconclusive outcome is acceptable; no P9 acceptance follows.

Append results as section 12 of the feasibility note, preserving sections
1-11; add a short current-work entry; preserve this planner. Expected tracked
inventory: three modifications, no untracked paths. Keep source/build inputs,
unique complete outputs and manifests under the new ignored root. One probe
build and canonical matrix after stabilization; affected reruns only for a
recorded harness defect. No product validation, generation or cleanup.
Return unstaged/uncommitted/unpushed for review; no commit/push authority.

ADR-0050 stays Proposed, P9 unproven, B paused. Owner design-only decisions
stand; no alternative production configuration is selected. Agent qualification
stays Blocked/TargetsNotReal with both allowances unused; Git/GitHub qualification
remains separate and unstarted. Preserve all prior evidence, roots, failures
and limitations, including the v1 stop-gate defect and four separately unexplained
historical failures. No later pass establishes flakiness or irrelevance.

## Candidate-A experimental report accepted; GO to publish evidence only (2026-10-15)

Independent preflight: main; HEAD/local origin/main/live main all
b5f985e25381b3fd990b79b49ed080d6511d33aa, 0/0, empty index; exactly three
modified documents, no untracked paths. Planner before this record:
a1524cfd9ee60d7714c0f36b908bfa9be0eca40106e002c311ec3a00616d0e3c;
all prior bytes are preserved. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.

R1/R2 accepted for the experimental evidence outcome. The first restricted
deletion remains a valid counterexample for this configuration; its cause
remains uninvestigated. The missing first-counterexample stop is disclosed
as a harness defect and protocol deviation. Restricted S4/H5/H6 and all
grandchild probes are retained post-stop observations, not dispatched-gate
acceptance evidence. H5 causal and token-equivalence claims are bounded;
the warning, lookup failure, build failures and capture limits remain.
This accepts the report, not P9, the configuration or binary reuse.

Codex made two final wording corrections in section 11 only: removed the
unsupported general assertion that the launch APIs differ only in token,
and clarified the private job is per child launch. The executor's returned
note is retained at logs/planner-review/p9-token-gate/review2/returned-correction1-note.md.
No harness or experiment changed or ran. Independent final checks: all
42 evidence entries match the unchanged manifest
c2b05831d82365c99e97e4de589725cfc19fd606bb6b43947074f31e907fd6ea;
published sections 1-10 are a byte-identical prefix; 130 local links resolve;
added-byte/secret-shape checks and diff check pass.

GO covers only the three frozen documentation paths including current-work
and this planner record. Freeze raw hashes/lengths, normalized modes/blobs
and baseline entries at logs/planner-review/p9-token-gate/publication-manifest.json.
Publish one commit: docs: record restricted-token deletion counterexample,
with the Claude co-author trailer and sole parent equal to the verified
baseline. Then normal fast-forward push and live remote verification.
No edits, experiment/build/validation rerun, generation, closure diff or
next-slice selection by the executor. Stop on any baseline, manifest,
inventory or remote discrepancy; do not refresh evidence to fit it.

ADR-0050 remains Proposed, P9 unproven and B paused; no different mechanism
or configuration is selected here. Frozen probe binary remains unapproved
for reuse. No real Git/GCM/helper/provider or credential operation, setup,
installation, qualification, ledger reset or cleanup is authorized.
Owner design-only decisions and all recorded limitations stand. Agent
qualification remains Blocked/TargetsNotReal with both allowances unused;
Git/GitHub qualification is separate and unstarted. Preserve all ignored
evidence and roots and the four separately unexplained historical failures;
later passes establish neither flakiness nor irrelevance. Codex selects
the next bounded task after independent publication verification.

## Candidate-A experiment: counterexample verified; NO-GO for reporting correction (2026-10-15)

Independent preflight: main; HEAD/local origin/main/live main all
b5f985e25381b3fd990b79b49ed080d6511d33aa, 0/0, empty index; exactly three
modified documents and no untracked paths. Planner before this record:
99b302787d983852c1cffcf7bf9a0ab56e2cc97a50eb8033959a6ac6933053d9;
all prior bytes are preserved. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.
All 42 evidence digests match manifest
c2b05831d82365c99e97e4de589725cfc19fd606bb6b43947074f31e907fd6ea.
130 local links resolve and diff check passes. Review baseline and returned note:
logs/planner-review/p9-token-gate/review1/. No experiment was repeated.

Substantive result accepted as bounded evidence: restricted-child S3 reports
DeleteFileW success, with before/after host snapshots and the retained fixture
confirming deletion. This exact configuration does not meet P9. The cause
remains uninvestigated; this is not a conclusion about all restricted tokens.
The later grandchild deletion is also recorded, but falls after the stop point.

Decision: NO-GO for two bounded reporting corrections; no new experiment.
R1: the executed harness did not implement the required first-counterexample
stop. run_ops at lines 140-166 continues from successful restricted deletion
to S4 and returns TRUE; role_child then runs H5/H6 and spawns a grandchild.
Only read/scratch failures trigger its operation stop. Remove the statement
that no harness defect was observed. Disclose the control-flow defect and
protocol deviation in section 11 and current-work. Mark all restricted
operations after the first S3 success as post-stop observations, retained
but not acceptance evidence under the dispatched gate. Do not discard the
valid initial deletion counterexample, edit the frozen harness or rerun it.

R2: remove the finding that the child wrote only granted scratch; deletion
is an observed source mutation. H5 establishes only that the supplied
noninheritable handle value was invalid in this child with both controls in
place, not the independent causal effect of the explicit handle list. No
positive inheritance case was run. State the actual launch APIs: control
CreateProcessW, restricted CreateProcessAsUserW, sharing desktop/list/job
setup. Keep token equivalence claims bounded to observed metadata and
identified inheritance reasoning; the grandchild logged only restriction
status and raw flags. Disclose retained build warning C4189 and the initial
header-lookup failure/corrected lookup, without repeating the build or fetch.

Correct only section 11 and the current-work entry. Preserve sections 1-10,
all ignored source/binaries/logs/manifests/fixtures, planner, client, ADRs
and other tracked files. The current harness is not approved for reuse.
Rerun changed-doc links, diff/byte and applicable secret checks once, verify
protected hashes, exact inventory and unchanged evidence digests. No runtime
code change, build, probe, cause investigation, qualification or cleanup.
Return unstaged/uncommitted/unpushed for GO/NO-GO; no publication authority.

P9 remains unproven, ADR-0050 Proposed and B paused. No alternative
configuration or mechanism is selected here. Owner design-only decisions
stand. Agent qualification stays Blocked/TargetsNotReal with both allowances
unused; Git/GitHub qualification remains separate and unstarted. Preserve
all historical evidence and limitations, including the four separately
unexplained failures; later passes prove neither flakiness nor irrelevance.

## P9 evidence published; selected owned candidate-A viability gate (2026-10-15)

Independent publication verification: main; HEAD/local origin/main/live main all
b5f985e25381b3fd990b79b49ed080d6511d33aa, 0/0, empty index and clean tracked/
untracked tree before this record. Sole parent
58c188f48e6894d4d2d1084336c268d86f51dc8b; subject and co-author match.
All four paths (3 M, 1 A), committed modes/blobs, parent entries and working
raw hashes/lengths match manifest
72a4705986cdc7984bf4fc149598a76c0d61b3108f92eb179194f5cbbc45ebc2.
Evidence: logs/planner-review/p9-feasibility/publication-verification.json.
Planner before this record: f77182736b396b005a15b8c6e3bbb5d829812d7ac6267f288bd468dfe3923a47;
all prior bytes are preserved. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.

Decision: select a bounded owned local experiment for candidate A only,
not product implementation, mechanism acceptance or the complete PL1-PL7
matrix. Test whether the unelevated current-user restricted-token launch works,
permits fixture reads and scratch writes, and denies selected fixture writes.
An owned host's noninherited writable fixture handle provides one bounded
handle-duplication challenge. Native metadata concerns only this harness and
its child tree. No Git/GCM execution, credential inspection or network.

Probe configuration: WRITE_RESTRICTED plus DISABLE_MAX_PRIVILEGE, a fresh
synthetic restricting SID (no account), scratch-only grants, a private temporary
desktop, explicit handle inheritance and job containment before resuming.
This is a falsifiable experimental configuration, not a chosen production
sandbox. Use only already installed tools; no elevation, new privilege grant,
alternate-user/service/broker, AppContainer profile or source-repository ACL
change. Native API/token/ACL operations are authorized only for this experiment's
own processes, new private kernel objects and newly created fixture paths.
No owner repository, default desktop, real host process or existing root is a target.

Use at most six named scenario families: unrestricted controls; restricted
launch/read/scratch; direct fixture create/overwrite/delete/DACL; descendant;
handle-list isolation; duplication of the owned host's known fixture handle.
Stop on the first configuration counterexample or unmet launch/containment/
authority prerequisite; retain the evidence without modifying the configuration
to force a pass. A code defect may be corrected with the failed attempt retained.
Every operation needs its raw return/error and an appropriate positive control.
A pass is bounded evidence, not P9 or GCM compatibility proof.

Keep all harness source, binaries, fixtures, complete unique outputs and
SHA-256 manifests under ignored logs/executor/p9-token-gate/. Preserve roots;
no cleanup or global build-server shutdown. Append results and limits to the
published feasibility note without rewriting its prior sections, add one
current-work entry, and preserve this planner. Expected tracked inventory:
three modifications, no new tracked/untracked paths. No other product/ADR edits.
Build only the dependency-free probe; run one canonical bounded matrix after
stabilization. No product suites, journeys, mutation campaign or generation.
Return uncommitted/unpushed for review; no commit/push authority.

P9 remains a release blocker; ADR-0050 stays Proposed and B stays paused.
No real helper/provider/Git qualification or credential access is authorized.
Owner design-only decisions stand. Agent qualification stays Blocked/TargetsNotReal
with both allowances unused; Git/GitHub qualification stays separate and unstarted.
Preserve all ignored artifacts, retained roots and recorded limitations, including
the four separately unexplained historical failures and no inference of flakiness
or irrelevance from later passes. Other runtime blockers remain outside this slice.

## P9 feasibility evidence accepted; GO to publish documentation only (2026-10-15)

Independent preflight: main; HEAD/local origin/main/live refs/heads/main all
58c188f48e6894d4d2d1084336c268d86f51dc8b, 0/0, empty index; exactly four
paths (3 M including planner, 1 untracked note). Planner before this record:
c87fe7ac9c486f1af455050784786114ec06051d2a19c665adfe272741c8a50c;
all prior bytes are preserved. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.

R1-R3 accepted: compatibility and source-grant assumptions are conditional,
the two caller privileges are distinguished from each other and from child
token privileges, and the proof claim is bounded to the sources read.
Owned local adversarial cases remain proposed, unexecuted and insufficient
alone for universal denial claims. No candidate is accepted or shown to meet
P9. B remains paused; no excluded workaround is selected. ADR-0050 remains
Proposed. The independent review confirmed all twelve source digests, 137
local links and the exact inventory; diff check passed. Executor correction
logs remain under logs/executor/p9-feasibility/correction1/.

GO covers only the four frozen documentation paths, including current-work
and the planner record. Freeze raw hashes, lengths, normalized modes/blobs
and parent entries in logs/planner-review/p9-feasibility/publication-manifest.json.
Publish one commit, docs: record publication source-write denial feasibility,
with the Claude co-author trailer and sole parent equal to the verified
baseline, then one normal fast-forward push and live ref verification.
No edit, regeneration, validation rerun, closure diff or next-slice selection
by the executor. If bytes, inventory, baseline or remote differ, stop.

No runtime, probe, credential/helper operation, real gh/GCM/provider call,
installation, qualification, ledger reset or cleanup is authorized. Owner
design-only decisions stand. Agent qualification remains Blocked/TargetsNotReal
with both allowances unused; Git/GitHub qualification is separate and unstarted.
All ignored evidence and limitations remain, including the four separately
unexplained historical failures; later passes establish neither flakiness nor
irrelevance. Codex selects the next bounded work after publication verification.

## P9 feasibility evidence: NO-GO for bounded documentation correction (2026-10-15)

Independent preflight: main; HEAD/local origin/main/live refs/heads/main all
58c188f48e6894d4d2d1084336c268d86f51dc8b, 0/0, empty index; four paths
(3 M including planner, 1 untracked note). Planner before this record:
e9e147ee361b524d103867101e7fbb4be3ff57138f4d937c55017813400b5162;
all prior bytes are preserved. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.
All twelve retained source digests match; 137 local links resolve; diff check
passes. Returned note and four-path raw baseline are retained under
logs/planner-review/p9-feasibility/review-20261015/. No executable validation.

Decision: NO-GO for evidence overstatement, not for an inconclusive outcome.
R1: AppContainer credential isolation does not establish the exhaustive claim
that only a broker, alternate user/service or exported credential could supply
GCM access. Source-repository grant requirements are conditional on effective
access not measured here. WRITE_RESTRICTED's restricting-SID rule is not a
guarantee of credential retrieval, decryption or helper compatibility. Reword
the note and current-work summary to separate documented mechanisms, narrow
inferences and unresolved compatibility. Retain the conservative stop on B;
do not infer feasibility, impossibility or select an excluded workaround.

R2: S06's quota privilege and the own-token exception for assign-primary
privilege concern different privileges; they are not a contradiction.
Preserve S06's "Typically" qualification and separate caller privileges from
the child's token. Keep the genuine desktop ambiguity unresolved.

R3: The sources read do not establish an implementation meeting P9; this does
not prove documentation in general cannot support a denial argument or that
only experiments can do so. Owned local adversarial tests can support a
specified mechanism together with its documented access-control model; finite
passing cases do not prove all writes or escapes impossible. Keep PL1-PL7 as
unexecuted proposed cases. ADR-0050's absent mechanism/proving boundary remains
accurate; no ADR amendment, source-store redesign or probe is selected here.

Accept the executor's challenge that local proof need not invoke a real
provider, and its choice to stop B's dependent design. Inconclusive evidence
is acceptable. The older current-work entry is its historical review return,
not current Git authority; leave it intact and anchor the new entry to the
verified publication as needed. No separate closure edit.

Correct only the feasibility note and its current-work entry, in the same
executor chat. Preserve the index link, ADRs, published execution-evidence
note, planner and client. Reuse the twelve retained sources; no further source
expansion is required. Rerun changed-doc links, diff/byte and applicable secret
checks once; report final inventory/protected hashes and preserve all logs.
No probes, runtime changes, token/profile/ACL/credential operations, builds,
suites, generation, staging, commit or push. Return for GO/NO-GO.

ADR-0050 remains Proposed; P9 remains unproven. Owner design-only decisions
stand. Agent qualification remains Blocked/TargetsNotReal, both allowances
unused; Git/GitHub qualification remains separate and unstarted. Preserve all
ignored evidence and limitations, including the separately unexplained
concurrent-replay 409, target-test 500, disposal NullReferenceException and
unnamed Api 2067/2068 failure. No next slice is selected by this correction.

## ADR-0050 publication verified; selected P9 feasibility evidence only (2026-10-15)

Independent verification: main; HEAD/local origin/main/live refs/heads/main all
58c188f48e6894d4d2d1084336c268d86f51dc8b, 0/0, empty index and clean tracked/
untracked tree before this record. Sole parent e2e89110f9db44d3e96cea21f2d5989abc255af5;
message and co-author trailer match. All five paths (4 M, 1 A), committed modes/
blobs, parent entries, and working-file raw hashes/lengths match the frozen
publication manifest 7e468d94bfd9aa5e5ad0c65a552854e430101cb8a95c0d30508b43b3c57d8299.
Evidence: logs/planner-review/adr50/publication-verification.txt. Preserve the
executor's first verifier stderr/CRLF failure and successful retry. No repeated
publication or executable validation. Planner before this record:
f26ca630d3fb5901afb67ad65e490b0718eb99be02163ca6a58b43ae684b1f86;
all prior bytes are preserved. Generated client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.

Decision: ADR-0050 remains Proposed. Select a documentation-only feasibility
slice for P9, before selecting runtime implementation or accepting the proposal.
This addresses a release blocker; more observation panels and diagnostic history
are optional. Supported GCM versions, real compatibility, fence/containment and
human resolution/rearm remain separate blockers, not work selected here.

Codex inspected the existing process adapter: Process.Start with explicit argv/
environment is no restricted-token or AppContainer boundary. Primary Microsoft
sources describe restricting-SID access checks, WRITE_RESTRICTED, and AppContainer
file/process/credential isolation. These identify candidates, not installed Git/
GCM compatibility or proof of P9. Sources:
https://learn.microsoft.com/en-us/windows/win32/secauthz/restricted-tokens
https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-createrestrictedtoken
https://learn.microsoft.com/en-us/windows/win32/secauthz/appcontainer-isolation
https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer

Bounded evidence assignment: compare only (A) a write-restricted token with
host-owned writable scratch, and (B) AppContainer/LPAC with explicit grants.
Read primary sources directly and inspect relevant existing code; no probes,
process experiments, credential access, ACL/token/profile changes or helper
invocation. Establish the intended threat boundary, denied operations including
delete/rename/ACL/ownership changes, descendants and inherited handles, parent/
token/process escape surfaces, and the unresolved compatibility with installed
noninteractive GCM. Do not claim impossible or feasible from missing documentation.
No source ACL changes, broker, alternate user/service or credential export is
selected. If a candidate needs these, identify the additional authority and stop
its design there. This task gathers evidence; Codex retains mechanism selection,
architecture, any owner escalation and final review. Do not weaken P9.

Deliver one compact pending-decision note at
docs/architecture/github-publication-source-write-denial-feasibility.md, one labelled architecture-index link and a short
current-work entry. Existing ADR/evidence documents and planner remain unchanged.
Use at most two candidate designs and twelve primary source pages; include a
claim/source/limitation matrix, a minimal boundary diagram, and proposed future
local proof cases separately from real GCM qualification. Inconclusive evidence
is an acceptable result. No new ADR, accepted contract or implementation.

Validation: changed-doc local links, diff/byte hygiene and applicable secret
checks once; protected planner/client hashes and final exact status. Retain all
binary/browser evidence; run no builds, suites, mutation, journeys or generation.
Keep complete uniquely named command outputs and source-reading provenance in
ignored logs/executor/p9-feasibility/. Stop on baseline/scope/authority discrepancy;
return uncommitted/unpushed for GO/NO-GO. No publication authority is granted.

Owner's restricted GCM/base profile and design-only identity-risk decision stand;
no repeated permission question. No real gh/GCM/provider/credential operation,
installation/login, qualification, ledger reset or cleanup. Agent qualification
remains Blocked/TargetsNotReal with both allowances unused; Git/GitHub qualification
stays separate and unstarted. Preserve all ignored artifacts, retained roots,
capture limitations and the four separately unexplained historical failures.
Prior records follow unchanged.


## ADR-0050 correction accepted; GO to publish the proposal only (2026-10-15)

Independent preflight: main; HEAD/local origin/main/live main all
e2e89110f9db44d3e96cea21f2d5989abc255af5, 0/0, empty index; five paths
(4 M including planner, 1 untracked ADR). Planner before this record:
30d4ac3ada88d0a555ca8f355a7d46ae8f6ee3ae601b35caf9482cd4884e546a;
all prior bytes remain preserved. Generated client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.

R1-R3 are accepted for this documentation-only preparation outcome: explicit
live-session intent, one initial supervisor claim/launch, inert replay in every
state, dormant old intents, operation-scoped duplicate prevention, conservative
process-creation evidence, all gates on AlreadyPresent, the independent physical
publication fence surviving lease supersession, and distinct local versus real
qualification proof boundaries. The static walkthrough covers the nine requested
scenarios. Neither contract acceptance nor runtime readiness follows.

Codex confirms the proposed release classes conditionally: proven NotLaunched
(including pre-launch AlreadyPresent), or proven local termination plus a fully
admitted acknowledgement/no-update classification and the required final
identity/ref evidence. A generic exception, status string or equality alone is
insufficient. Earlier unresolved timeout, lost response, malformed/truncated
output, identity mismatch or other attention condition wins; later observations
cannot erase it. Post-launch '=' and client rejection require complete-invocation
no-update semantics proved for the supported Git contract. Remote rejection and
unknown effects retain the fence. No real-compatibility proof exists yet.

Codex added that explicit precedence/proof paragraph to decision 8 and clarified
I4 accordingly, with no other executor-document changes. The returned corrected
ADR is preserved as logs/planner-review/adr50/review/returned-correction1-adr50.md,
SHA-256 0f457cb91ce90163a97ce0048363ccc3220412f253b5ff39db7758f05c6f41d1.
No binary evidence was invalidated. Independent final checks: 220 local links,
zero broken; diff whitespace and selected secret-shape checks clean. Published
evidence sections 1-7 are unchanged. Retain the prior source hashes and all
executor results; no probe/build/suite/mutation/journey/generation was repeated.

P9 is an unresolved feasibility blocker, not a demonstrated property: no
source-write-denial mechanism is selected or proved. Alternates, job lifetime,
an after-the-fact comparison and the owner's name-to-ID design acceptance do
not establish it. Do not weaken P9, claim feasibility or implement publication
from this proposal. Supported GCM versions, real qualification and the human
resolution/rearm contract remain activation blockers too. These open blockers
are acceptable to publish as an explicitly Proposed document, not to accept
or activate as an executable contract. No additional owner approval is asked
for the already accepted restricted profile or design-only identity risk.

GO authorizes ONLY publication of the five reviewed documentation paths frozen
in logs/planner-review/adr50/publication-manifest.json, 4 M and 1 A, including
current-work and this planner record. Verify manifest hash and every entry,
stage only literal manifest paths, verify staged modes/blobs, one commit with
the exact e2e89110 baseline as its sole parent, normal fast-forward main push
and live ref equality. Stop on discrepancy or push failure; no force/reset/
rebase/merge. No further edits, tests, regeneration, closure diff or next slice.
If already published matching this manifest, verify/report instead of repeating.

ADR-0050 stays Proposed; accepted ADRs stay unchanged. No runtime implementation,
real gh/GCM/provider/credential access, installation/login, qualification run,
ledger reset or cleanup is authorized. Agent qualification remains
Blocked/TargetsNotReal, both allowances unused; Git/GitHub qualification stays
separate and unstarted. Preserve all artifacts, retained roots, historical
capture/failure limitations and the four separately unexplained failures.
Codex resumes next-step selection after independent publication verification.
Prior records follow unchanged.


## Proposed ADR-0050 review (2026-10-15): NO-GO, bounded contract correction

Independent preflight: main; HEAD/local origin/main/live main all
e2e89110f9db44d3e96cea21f2d5989abc255af5, 0/0, empty index, five paths
(4 modified including this planner, 1 untracked ADR). Planner before this
record: c11601871a9ad4dd35d415ecc188c3a68b5b112917611052bbbf8df1e5bd4cb9;
all prior bytes are preserved. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.
Independent checks: 16 reading-aid hashes match, 220 local links resolve,
diff whitespace clean. No executable checks/probes or real integrations ran.
Original five-path raw baseline and returned ADR are retained under
logs/planner-review/adr50/review/.

The draft respects the owner scope and Proposed status. The executor's concern
about ADR-0008 superseding the active lease is valid. Several internal rules
still conflict, so no publication or ADR acceptance GO is given.

R1 (P1), decisions 6 and transitions X3/X4/X7/X9/X29: the supervisor is
restricted to read-only resolution and cannot make the first claim, while no
unambiguous first-launch owner is assigned. Identical replay both returns
recorded state and conflicts after a claim. A new request after a pre-claim
crash is required but prohibited by the existing-intent conflict rule. Codex
closes the dispatcher ambiguity left in the planning prompt: the request
records intent only; a supervisor may claim that explicit intent ONCE and
perform its initial launch in the original live host session. It may not
invent intent, reclaim, relaunch or auto-arm old intent after restart.
Identical requests always return recorded state without triggering execution;
different identities/values for the same intent conflict. A pre-claim restart
does not implicitly arm the intent; no recovery/rearm API is invented here.
Any later human rearm path remains a separately reviewed activation gate.
Do not impose a global SHA prohibition across unrelated approved scopes.

R2 (P1), I10/decisions 2-4/8/refusal list: one authorized host invocation is
not exactly one remote effect; replace that invariant and related causal
wording while retaining internal-resend/delete-between-creates uncertainty.
Only durable evidence specifically proving no push process was created may
classify a start failure as NotLaunched; a generic reported exception cannot.
Helper interactivity/errors known only after launching the push are post-launch
evidence, not pre-launch refusals. Only known configuration/eligibility defects
can refuse before launch. Apply all selected execution-eligibility gates to the
AlreadyPresent path too; do not add an unselected base-gate bypass. Preserve
the equal-SHA no-op control and fix the repeated claim that every existing ref
was refused. Align release classifications with the full-attempt uncertainty
rules and repair references to nonexistent decision 9.

R3 (P1 design refinement), exclusion and activation: choose an independent
durable publication fence keyed by physical repository identity, acquired
atomically with the claim and consulted by every mutating admission including
workspace preparation. Existing lease supersession may still record lost
ownership trust, but cannot remove/bypass that fence or permit a new mutator.
Lease ownership proof and publication exclusion are separate facts; an Active
lease alone is not sufficient. Add the reconciliation/admission compatibility
extension to the proposed ADR map without editing accepted records. Include
startup ordering and competing-host proof obligations; no mechanism is assumed
implemented. Unknown/remote-rejected attempts retain the fence. A conclusive
human resolution/rearm contract is a prerequisite to runtime activation, not
a generic cleanup, reset or unrecorded escape. No such operation is designed
or authorized in this correction.

Also classify future proof obligations by their cheapest valid boundary:
owned local tests can prove pins, claim/fence behavior and job lifetime;
actual GCM/GitHub compatibility still needs separate explicit authorization.
Do not imply that every local proof requires a real-provider qualification.
Keep the stronger source-write-denial requirement P9 visibly unproven:
alternates and Windows jobs do not establish filesystem access restrictions.
Do not weaken it or silently design an ACL/token/sandbox here; any feasibility
conflict must return for planner discussion. Microsoft's job documentation was
read directly: process-group lifetime/accounting and process security are
separate boundaries. This is an implementation gate, not compatibility proof.

Correction remains in the SAME executor chat, documentation only: ADR-0050,
current-work, and only necessary consistency changes in its index entry and
the appended evidence section 8. Sections 1-7 of the published evidence remain
byte-identical. Preserve this planner and client. No new probe, build, suite,
mutation, journey, credential access or real-helper/provider call. Final once:
links, diff/secret hygiene, exact scope and protected hashes, plus an explicit
static walkthrough of first dispatch, replay, pre/post-claim restart, helper
error, repeated wire request and lease supersession. Return Proposed and
unstaged/uncommitted/unpushed. Owner profile/design-risk acceptance stands;
no reapproval is requested and no runtime risk waiver is inferred. Both
qualification gates and every historical failure/limitation remain unchanged.
Prior records follow unchanged.


## Owner decision recorded; select ADR-0050 contract preparation only (2026-10-15)

The owner explicitly accepts advancing ONLY the contract preparation within
the restricted profile, with the residual identity risk documented. This
resolves the earlier pending answer for design purposes; it is not inferred
from the screenshot's preselection or a template containing two alternatives.
The earlier explicit profile answer and the later owner decision in this chat
are authoritative. Do not ask for either decision again.

Accepted scope: already installed, explicitly enabled, noninteractive Git
Credential Manager; the recorded commit's parent must be the freshly confirmed
tip of an existing remote branch. The name-to-ID race is identified and
analysed, not numerically quantified: a fresh identity read, fixed destination,
redirect refusal and later observation cannot atomically prevent replacement
of a name's repository or undo a wrong effect. Acceptance permits preparing
the contract with this limitation; it does not authorize runtime activation,
credential access, real Git/GitHub/helper calls, installation, login or publication.

Independent current preflight: main, HEAD/local origin/main/live main all
e2e89110f9db44d3e96cea21f2d5989abc255af5, 0/0, empty index, no untracked files,
only this planner file modified. Its hash before this record was
1be8df5d5d6ba61d4f110b5782fbafb8868cc6e529cd3ced954950225ffa0cf7;
all earlier bytes are preserved. The client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.
Publication of the prior four-path evidence diff has already been independently
verified; it must not be repeated. Current-work's old prepublication wording is
a historical delivery checkpoint, not contrary Git evidence.

Select ONE new documentation-only executor slice: prepare Proposed ADR-0050,
Define the first approved GitHub branch publication execution contract. Codex
supplies the design direction below; Claude transcribes, verifies primary
facts and challenges contradictions, without selecting architecture or accepting
the ADR. No runtime code or new probe is selected. Allowed executor paths:
docs/decisions/0050-define-the-first-approved-github-branch-publication-execution-contract.md,
docs/decisions/README.md, an appended decision addendum to the existing execution
evidence note, and docs/roadmap/current-work.md. Preserve this planner file.
Do not rewrite historical probe findings or mark old ADRs superseded yet.

The proposed contract must codify these planner choices:

- Scope and authority: separate explicit execution request, exact coherent
  ADR-0048 consent and ADR-0040/0043 source/target/binding, whole-value rereads
  under short transactions. No Agent, automatic consent, PR, correction push,
  force overwrite, deletion or expansion to another SHA/ref/repository.
- Compatibility and exposure: pinned supported GCM only, explicit opt-in and
  noninteractive operation, no ambient credential-helper/global configuration
  import, token extraction or setup. Helper-owned credential lifecycle and
  installed-version support need explicit proof; never promise zero helper
  effects or qualification from documentation. Unsupported cases refuse.
  Choose the remote default branch as the first profile's base: its fresh,
  fully admitted tip must equal the one recorded parent. Refuse empty/missing
  default refs and owned-branch equality with the default branch. This is a
  conservative narrowing of the accepted existing-remote-branch profile, not
  authority to publish arbitrary unpublished ancestors or fetch them.
- Transport: one full approved SHA to one exact owned refs/heads ref on
  github.com, create-only with the exact explicit empty expectation; existing
  equal SHA is an observation without a send, any different existing ref refuses.
  No implicit/nonempty lease, plus refspec, mirror, tags or recursive submodule
  send. Record the narrow non-overwrite interpretation explicitly, not a general
  reversal of ADR-0005's prohibition of force-push/history rewriting.
- Fresh gates and residuals: local owned branch/HEAD must equal the approved
  commit, physical source/lease/readiness and complete scope are fresh; require
  fresh bound remote ID/names and exact default/owned refs. Repository permission
  observations are at most conservative input screening, never proof of Git's
  principal or branch-write authority. Server rejection remains possible.
  Bracket the effect with identity/ref observations, refuse redirects and
  disclose the owner's design-only residual-risk acceptance exactly.
- Isolation: per-attempt host-owned bare transport scratch with configuration
  isolated from source/global/includes and a proven read-only path to the source
  common object store, no writes to the active checkout or source object store.
  Explicitly propose the narrow ADR-0008 execution-location extension, retaining
  source physical proof, repository exclusion and transport lifecycle proof.
  Alternates resolving an object is not sufficient ownership evidence.
- Dispatch: durable intent, single-use conservative claim before launch and
  repository-wide reservation, including other runs and approvals. A supervisor
  may act only on an explicit intent. No implicit publication after restart,
  automatic resend or resetting consent. No transaction spans a process/network.
  A separate third launch marker is not required. Conclusive pre-launch refusal
  can be distinguished; a claim without a recorded result remains uncertain.
- Lifetime and evidence: Windows child-tree containment must be established
  before external mutation, with conclusive termination evidence for local
  resource release; a PID or elapsed timeout alone is insufficient. Local
  termination never proves a remote non-effect. Propose 15-second remote-read
  bounds, a 120-second send deadline and 32 KiB per stream/64 KiB combined
  capture; these are candidate bounds requiring later executable validation,
  not measured transfer capacity. No raw credential/provider output in the
  journal or ordinary responses; preserve bounded admitted evidence safely.
- Recovery: distinguish NotLaunched, AlreadyPresent, observed acknowledgement/
  rejection and NeedsAttention/MayHaveStarted, without promising exactly-once
  effects. Acknowledgement describes admitted server evidence, equality alone
  never proves causality, a final rejection/later absence does not exclude an
  earlier applied-and-moved effect, and transport-internal resend is possible.
  Read-only reconciliation and append-only observations never relaunch or erase
  uncertainty. Release execution resources/exclusion only on the separate
  proofs appropriate to process termination, ownership and remote uncertainty;
  if unresolved, keep attention/exclusion rather than inventing recovery.

The draft must contain a complete transition/invariant table, proof obligations,
changed-ADR map, compatibility/refusal limits, risk disclosure and proportional
future validation matrix. Do not invent unreviewed wire/schema or UI designs;
identify those as separately bounded implementation contracts. Primary source
confirmation can narrow/qualify a factual claim but cannot silently choose a
different credential source, wider ancestry, weaker exclusion or broader effect.
Material contradictions return to Codex for discussion. No accepted runtime
contract or implementation sequencing is delegated to Claude.

Validation now is documentation-only: local links, diff/secret hygiene, exact
scope and unchanged planner/client hashes. No build, suite, mutation, journey,
client regeneration or synthetic/real transport probe. Future real GCM/GitHub
qualification is separate, unstarted and needs a separate explicit decision.
Return unstaged/uncommitted/unpushed for Codex GO/NO-GO; no publication GO,
closure diff or next-slice selection. Agent qualification remains
Blocked/TargetsNotReal, both allowances unused; all four historical failures
remain separately unexplained and every prior limitation/artifact is retained.
Previous pending-decision records follow as history, not current blockers.


## Owner accepted the restricted profile; identity-risk decision pending (2026-10-15)

The owner explicitly selected "Sim, usar esse perfil restrito (recomendado)"
for the first question in this planner chat. Carry that decision forward:
the first proposed executor is restricted to an already installed, explicitly
enabled, noninteractive Git Credential Manager and a commit whose parent is
the confirmed tip of an existing remote branch. Other helpers, unpublished
ancestry and empty repositories are outside that initial profile. Do not ask
for this profile approval again.

The separate question about accepting the residual name-to-ID race has not
been answered. It remains pending, not implied by the profile acceptance.
No runtime slice is dispatched, no execution ADR accepted, and no actual
credential access, helper/provider invocation, installation, login, commit or
push is authorized by this answer. Qualification and all prior limits stand.
Git baseline remains e2e89110f9db44d3e96cea21f2d5989abc255af5 on main;
only the planner selection/decision record is modified, index empty. Previous
publication verification and both the proposal and review records follow.


## Evidence publication verified; first-push contract awaits owner decisions (2026-10-15)

Codex independently verified e2e89110f9db44d3e96cea21f2d5989abc255af5:
main; HEAD/local origin/main/live refs/heads/main equal; 0/0; empty index;
clean tracked/untracked tree. Sole parent:
810b99b8e4f9c5e4d1b92266d8039a34f5c7c39e. All four paths (3 M, 1 A),
committed and parent modes/blobs, working raw hashes/lengths, message and
co-author trailer match the frozen manifest (SHA-256
32a13a565fa1a703535ec512fcc82d72633209787bbfff9dce8afa409933e092).
No validation or generation was repeated. Evidence is retained in
logs/planner-review/first-push-evidence/publication-verification.txt.
Planner before this record:
af59a3fc61db820082ffb3c2fb10f99c4db1e5ffa9ac3a9991a7e3571f97e91b;
all earlier bytes remain preserved. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.

Planning resumes now, not another evidence or closure publication round.
Push/PR execution and ambiguity recovery remain the next material Increment 6
gap; richer observations/history are optional. The other recorded MVP/release
gaps and both qualification boundaries remain open. No runtime slice is
selected until the material choices below are answered by the owner.

Codex's direction for a future reviewed execution ADR, not an accepted runtime
contract: explicit durable request distinct from consent; exact coherent
approval/scope and strict local branch-tip/SHA equality; fresh physical proof;
repository-wide reservation under the active lease, including across runs and
approvals; one durable may-have-started claim before launch; a supervisor may
act only on that explicit intent; no automatic resend or restart relaunch;
unknown effects remain unknown and ref equality never proves causality.
Termination must be evidenced, not inferred from elapsed time. A new process
containment boundary and any isolated transport location need their own scoped
review and an explicit ADR-0008 extension where applicable. These are technical
design responsibilities for Codex, not thirteen owner approvals.

For the first executor, prefer a narrow create-only boundary: explicit full
SHA/ref, one exact empty-expectation guard, no overwrite, no nonempty/implicit
lease or other force mode, and existing equality reported without a send.
This technical direction requires codification and review before any runtime
use; the previous force-mode exclusion is not a present permission to execute
the candidate flag. Concrete process/output limits remain to be fixed in that
bounded prompt; no unbounded transfer or process is approved here.

Two concrete owner questions are pending, presented in this planner chat:

1. Whether to scope the first contract to an explicitly enabled, already
   installed Git Credential Manager in noninteractive mode, without importing
   ambient Git configuration or doing login/setup, and require the recorded
   commit's parent to be the freshly confirmed tip of an existing remote branch.
   This reduces reachable-history exposure and compatibility scope; other
   helpers, unpublished ancestry and empty repositories would be refused.
   No real credential configuration/store/helper is accessed by this decision.
2. Whether the owner accepts the residual name-to-ID race for preparing the
   contract: fresh identity before send, fixed destination, redirects refused
   and a fresh observation afterwards do not atomically pin Git's name-addressed
   transport to the bound repository ID and cannot undo a wrong effect. An
   explicit rejection keeps product push blocked; no silent risk acceptance.

Codex recommends the restricted compatibility/history profile. The residual
identity-risk decision belongs to the owner; neither a preselected UI option,
silence nor elapsed time supplies acceptance. If accepted, Codex still has to
select and dispatch a concrete bounded implementation/design slice. Claude
must not implement or choose the next step from this pending record.

Primary GCM documentation was read directly: its main-branch environment and
configuration pages document GCM_INTERACTIVE=0 / credential.interactive=false
and the Windows credential-store default. This supports a noninteractive
candidate, not installed-version or real-account compatibility. The initially
tried release-branch documentation URLs returned 404; no installation or helper
invocation followed. Sources:
https://raw.githubusercontent.com/git-ecosystem/git-credential-manager/main/docs/environment.md
https://raw.githubusercontent.com/git-ecosystem/git-credential-manager/main/docs/configuration.md
Git's push manual also documents explicit empty-expectation semantics. All real
compatibility remains unverified. No real probe is authorized even if the owner
accepts the proposed design profile/risk; that needs a separate explicit decision.

Agent qualification remains Blocked/TargetsNotReal, both allowances unused;
Git/GitHub compatibility qualification is separate and unstarted. Preserve all
retained roots, artifacts, logs and recorded limits. The concurrent-replay 409,
older target-test 500, disposal NullReferenceException and unnamed Api 2067/2068
failure remain separately unexplained; later passes prove neither flakiness nor
irrelevance. No real gh/provider call, CLI setup, ledger reset, cleanup, stage,
commit or push is authorized by this planning checkpoint. Prior records follow.


## First-push execution evidence review (2026-10-15): R1-R5 accepted; GO for documentation publication only

Independent preflight: main; HEAD/local origin/main/live refs/heads/main all
810b99b8e4f9c5e4d1b92266d8039a34f5c7c39e, 0/0, empty index; four paths:
three modified (architecture index, current-work, planner) and one untracked
execution-evidence note. Planner before this record:
680aecfacab77cd2b4ba5256f885ab3662a6480ef160b6bd8277905f1a369b77;
all earlier bytes remain preserved. Generated client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.

R1-R5 are accepted for this evidence-gathering outcome. The correction
separates pre-launch refusal, post-launch observations and attempt uncertainty;
restores mandatory repository exclusion; narrows helper and principal claims;
keeps create-only and execution-location proposals distinct from constraints;
and corrects marker semantics and capture/source limitations. The distinction
between an empty synthetic home and an untested no-home environment is now
explicit. No runtime implementation or accepted execution contract results.

Independent checks: 138 original script/output hashes match, 10 correction
manifest entries match, and eight source-file hashes match; 154 local links
resolve, diff whitespace is clean, selected secret-shape checks find nothing.
Original failures, filtered/truncated/unavailable output and documentation
version/read limits are disclosed. No probes, builds, suites, mutations,
journeys or regeneration were repeated. Evidence under
logs/planner-review/first-push-evidence/r1-acceptance-*.

Codex made two final factual wording corrections in the note: the existing
equal-SHA lease case is an up-to-date no-op, not a refusal; the observed server
create refusal concerns a command with an all-zero old value, not a guarantee
that a plain push cannot fast-forward an existing ancestor. The original
returned note is preserved as returned-r1-evidence-note.md (SHA-256
706b8120e2caeb452b083bce7358053e856a58140b50a8cea9249c7a0e131c1f).
These documentation-only edits invalidate no executable evidence.

GO authorizes Claude to publish only the four reviewed documentation paths
frozen in logs/planner-review/first-push-evidence/publication-manifest.json:
3 M and 1 A, including current-work and this planner record. Verify every
entry before staging, only literal manifest paths, then staged modes/blobs,
one commit with the exact baseline as its sole parent, normal fast-forward
push of main and live remote equality. Stop on discrepancy or push failure;
no force/reset/rebase/merge. No edits, generation, repeated validation, closure
diff or next-slice selection during publication. An already-published matching
commit is not an instruction to repeat publication.

This publishes pending-decision evidence only. It does not accept a transport,
lease syntax, execution location, credential arrangement, risk waiver or new
ADR, nor authorize product remote mutation or a real-helper/provider probe.
Codex will resolve technical questions and present concrete residual owner
choices after publication verification; no new runtime slice is selected here.
Agent qualification remains Blocked/TargetsNotReal, both allowances unused;
the separate Git/GitHub compatibility gate is unstarted. Preserve every
artifact, retained root and recorded limit, including the four separately
unexplained historical failures. No CLI setup, ledger reset or cleanup.
Prior records follow unchanged.


## First-push execution evidence review (2026-10-15): NO-GO, documentation correction only

Independent preflight: main; HEAD/local origin/main/live main all
810b99b8e4f9c5e4d1b92266d8039a34f5c7c39e, 0/0, empty index; three modified
paths (architecture index, current-work, preserved planner) and one untracked
execution-evidence note. Planner before this record:
027f7cc53325fc7754f48dda11395a6312b35ff29ec2c7036ec7bdad40976e06;
all earlier bytes are preserved. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.
All 138 script/output hashes in the executor manifest match. The review
checker first used the wrong logical root for out/; corrected mapping gives
zero discrepancies, recorded under logs/planner-review/first-push-evidence/.
Inspected scripts and retained P4b logs support late application after kill
and two receive-pack POSTs after a dropped response on the local double.
No probes, suites, real gh/provider calls or client generation were repeated.

The evidence is useful but its proposed conclusions are not yet coherent:

- R1 (P1): section 3.2 calls unrecognized output and a changed To line
  refusals where nothing is sent. These are post-launch observations and can
  follow an effect. A final rejection plus a later nonmatching/absent ref also
  cannot exclude an earlier applied-and-moved effect after a transport resend.
  Classify evidence of each exchange separately from whole-attempt certainty.
  CAS is not universally harmless if a ref is deleted between repeated creates.
  Elapsed deadlines likewise prove neither orphan termination nor safe release.
- R2 (P1): U10/section 2.1 makes the repository lease optional and substitutes
  uniqueness per approval. ADR-0006/0008 already require repository mutation
  exclusion for pushes, including across different runs/approvals. Retain that
  requirement. Scratch bare transport is a proposed execution-location
  extension of ADR-0008, not presently accepted simply because alternates work.
- R3 (P2): disabling system/global configuration does not remove local helpers
  (the executor's own P3 A3 proves this). Narrow the no-helper refusal to the
  tested synthetic arrangement. Git-native ls-remote uses a candidate transport
  configuration; it does not prove the future push's authenticated principal
  or write access and can succeed anonymously. Authentication feasibility and
  actual compatibility remain unresolved, not a universal impossibility proof.
- R4 (P2): distinguish constraints from proposals. Create-only was not a
  selected product requirement. The explicit empty-expectation lease is a
  meaningful non-overwrite candidate, not authority to allow force modes.
  There must be a durable conservative may-have-started boundary before launch;
  this can be the single-use claim itself. A third marker is optional phase
  detail, not proven mandatory. Marker presence is not evidence of actual launch.
  Keep real Git/GitHub compatibility gating separate from the existing Agent
  qualification ledger; neither gate is relaxed or authorized by this review.
- R5 (P2): correct blanket complete-output claims: scripts use head/tail and
  killrun.js slices captures; the original capture limits must be disclosed,
  never reconstructed. Re-read primary sections used for disputed conclusions,
  qualify versions and unsupported assumptions, and identify exact proposed
  argv/environment or explicitly name what remains unresolved. Keep all
  historical attempts and original evidence immutable.

Codex verified primary Git push/config/credential documentation directly:
an empty explicit lease expectation requires an absent ref; rejected and
remote-rejected statuses have distinct meanings; NOSYSTEM/global-file settings
do not disable repository config; credential.helper has an explicit empty
reset. These facts support the corrections, not an execution authorization.

Select only correction R1-R5 in the SAME evidence executor chat, limited to
the evidence note and current-work; architecture index stays unless its label
needs correction. No runtime work, new ADR acceptance or new feature selection.
No original probe needs repetition merely to correct its claims. Final checks
are changed-document links, diff/secret hygiene, exact scope and unchanged
planner/client hashes. Retain executable results on unchanged inputs.
Review the corrected note before presenting a compact, concrete owner decision
on credential arrangement, reachable-history exposure and residual identity
risk; do not ask the owner to adopt thirteen technical design decisions.
No owner risk acceptance, real-provider permission or publication GO exists.
Qualification remains Blocked/TargetsNotReal, both allowances unused; keep all
four historical failures separately unexplained and every other recorded limit.
Prior records follow unchanged.


## ADR-0049 publication verified; first-push execution evidence selected (2026-10-15)

Codex independently verified publication 810b99b8e4f9c5e4d1b92266d8039a34f5c7c39e:
main, HEAD/local origin/main/live refs/heads/main equal, 0/0, empty index,
clean tracked/untracked tree. Its sole parent is
703037925da345a810b3438715403721beea96d5. The frozen ADR-0049 manifest
(SHA-256 b7d1314e6151702033c851109610c883fecda6e5a423d6e031182c42d0b758bb)
matches all 51 committed paths (19 M, 32 A), modes/blobs, parent entries and
working raw hashes/lengths. Message and co-author trailer match. The first
sandboxed live-ref read failed to resolve the host; the authorized network
read succeeded. No implementation validation or generation was repeated.
Evidence: logs/planner-review/adr50/publication-verification.txt. Repeating the
previous publication prompt is unnecessary and unauthorized on this baseline.
The planner before this record was
89c883008871ca1cb22d1a72646496d3b2f60aac87d7748ca03470a9694c9832;
all earlier bytes remain preserved. The generated client is unchanged at
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.

Roadmap/code assessment: ADR-0049 closes the explicit reported repository
access input, not branch-push permission or publication. Increment 6 still
needs durable push/PR execution and ambiguity reconciliation, automatic
exact-SHA monitoring and bounded CI diagnosis/correction. Increment 4 provider
contracts/real qualification, Increment 5 coordination/recovery and Increment 7
packaging/self-hosting proof also remain release blockers. More observation
panels, richer CI enumeration/history and additional fixture optimization are
optional; none is selected here.

The next implementation decision needs concrete transport/recovery evidence.
LocalCommitGitRunner deliberately sets protocol.allow=never, disables global
configuration for mutations and uses an empty hooks directory. Its existing
authentication exception reads author identity only. LocalCommitSupervisor
and recovery deal with local ref/index effects, not remote send ambiguity;
there is no publication operation, single-use remote dispatch receipt or
remote reconciliation implementation to adopt. Copying those paths or merely
restoring ambient configuration would not prove a safe remote executor.

Official Git documentation confirms URL insteadOf/pushInsteadOf rewriting,
HTTP redirect configuration, and executable credential helpers (including
shell snippets); GitHub documents rename redirects and name reuse. These
features create real destination/authentication boundaries beyond argument
quoting. Sources inspected by Codex:
https://git-scm.com/docs/git-push,
https://git-scm.com/docs/git-config,
https://git-scm.com/docs/gitcredentials,
https://docs.github.com/en/repositories/creating-and-managing-repositories/renaming-a-repository.
No conclusion is made that a GitHub node ID atomically pins a Git name-based
transport, that Git and gh use the same principal, or that a permission report
or dry run proves an eventual push will be authorized.

Select ONE bounded evidence-gathering slice before runtime implementation:
document the feasibility and remaining decisions of the first approved normal
push under the planner constraints below. Deliver only an execution-evidence
note, its architecture index link and current-work, leaving this planner file
byte-identical. Do not reserve or mark an execution ADR Accepted yet, implement
an adapter/queue/API/schema/UI, or let an executor recommendation become an
accepted design. This is substantive prerequisite evidence, not a closure diff
or permission to publish the current repository again. Claude verifies the
constraints and reports counterevidence; Codex retains architecture, next
implementation selection and final judgment.

Planner constraints for the evidence: an execution request distinct from
ADR-0048 consent; exact approved source/target/binding and SHA/owned ref; fresh
physical ownership and local evidence, remote identity/default-branch and
authentication/permission checks; existing Git authentication with no token
extraction or setup; a fixed github.com destination and exact SHA refspec;
no force, default-branch push, deletion, hook execution, extra refs, PR or
Agent; durable intent and a single-use claim committed before any remote
effect, no transaction across external work, repository exclusion and
read-only recovery after uncertain dispatch. A matching remote SHA is an
observed fact, not proof of who pushed it. Unknowns fail closed. Do not invent
an atomic remote identity/ref guarantee or an automatic retry. Identify any
residual race or required authority change explicitly for planner/owner
decision instead of accepting it silently.

The evidence must map code reuse/gaps, authentication/configuration isolation,
URL rewriting and redirect risks, hooks/LFS/submodules and extra-ref effects,
concurrent ref/default-branch/repository changes, bounded output/time and
cancellation, and a crash/replay/recovery state table. Every claim needs a code
anchor, primary source, or an explicitly limited owned-double/local proof;
unverified real CLI/provider behavior remains unverified. No reading of real
credential configuration/stores or credential-helper invocation is authorized.
If synthetic probes are necessary, use only newly owned isolated roots and
fictitious data, no external endpoint and no production repository mutation;
retain scripts and complete uniquely named logs. No old cleanup. Existing
suite evidence is retained because executable inputs must not change. Final
checks: changed Markdown links, diff/secret hygiene and scope/byte identity.
No build, full suite, mutation campaign or journey is required for this scope.

Return unstaged, uncommitted and unpushed for Codex review with a bounded
implementation feasibility conclusion and named unresolved decisions. Evidence
that contradicts a planner constraint requires discussion; do not broaden the
slice. Real qualification stays Blocked/TargetsNotReal with both allowances
unused. Preserve the historical concurrent 409, older target-test 500,
disposal NullReferenceException and unnamed Api 2067/2068 failure separately
unexplained, plus all missing/aborted/overwritten-log facts, sandbox failures,
skips, advisory, migration-generator and real-CLI limits. No real gh/provider,
CLI setup, ledger reset, retained-root/artifact cleanup or publication authority.
Prior records follow unchanged.


## ADR-0049 review (2026-10-15): R1 accepted; GO for the frozen substantive diff

Independent preflight: main; HEAD/local origin/main/live main all
703037925da345a810b3438715403721beea96d5, 0/0, empty index, 51 paths
(19 modified INCLUDING the preserved planner, 32 untracked). Planner before
this record was cb305130d82e3d69ce240253b408891e591ccb449850c4fad27f33fdc2b3e883;
all its earlier bytes remain preserved. Generated client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.

R1 is accepted. Compared with the 51-path dispatch baseline, Claude changed
only the decoder, its tests, panel tests and current-work. Literal null is
required only for PermissionNotReturned.viewerPermission; shared absence
rules, hook, backend, DTOs, client and composition are byte-identical. New
tests retain undefined as a refusal, use the actual generated fromJS on JSON,
preserve the null and six-literal controls, and prove panel refusal without
a no-permission claim or remote facts. Genuine red: four failures of 70;
the original failed logs are preserved. Independent Codex tests: 70/70.
Independent original probe now admits explicit null and refuses omitted and
undefined members. Evidence: logs/planner-review/adr49/review/r1-*.

Executor correction build (includes typecheck), lint and links passed. Retain
backend, client reproduction, fixture/guard, Architecture, sibling and journey
evidence as authorized: no covered executable inputs changed. Earlier Codex
checks remain Application 69/69, adapter 173/173, guards 6/6 and zero-warning
solution build. Original executor full-family evidence and all limitations
remain recorded. No extra full suites, mutation or journey was required.

Final client inspection corrected an evidence claim: Git's textual diff is
+213/-43 (net +170), not +170/-0. Codex compared every top-level generated
declaration by TypeScript AST name/kind and exact normalized declaration text:
all 454 existing declarations are identical, five new declarations are added,
and existing DTO ordering changes. Probe source/output are retained as
client-declaration-probe.cjs/.log under the review directory. No wire or
existing client behavior changes. Accept semantic additive generation with
verified reordering; extra DTO duplication merely to control generated order
is unnecessary. The earlier textual-additivity interpretation is revised on
this evidence; the generated file is unchanged. Codex corrected only that
factual wording in current-work and marked ADR-0049/index Accepted as this
review decision. These documentation edits invalidate no binary checks.

GO authorizes Claude to publish ONLY the reviewed 51-path substantive diff,
including current-work, accepted ADR metadata and this planner record, frozen
in logs/planner-review/adr49/publication-manifest.json. Verify its hash and
every entry before staging; stage only the literal manifest paths; verify
19 M/32 A modes and blobs, one commit with the exact 7030379 baseline parent,
normal fast-forward push of main and live remote equality. Stop on any
discrepancy or push failure. No further edits, regeneration or validation
reruns are needed. A material change returns for review; no force/rebase/reset.
No closure diff or next slice is selected before publication verification.

This publishes an ephemeral repository-access observation, not authority or
readiness to push a branch. Real qualification stays Blocked/TargetsNotReal,
both allowances unused. No real gh/provider invocation, CLI setup, ledger
reset or cleanup. Preserve every log/TRX/manifest, original weakened frontend
run and test-first chronology limitation, failed M3 restoration attempt,
retained root, ignored review artifact and all recorded limitations. The
historical concurrent 409, older target-test 500, disposal NullReferenceException
and unnamed Api 2067/2068 failure remain separately unexplained; later passes
prove neither flakiness nor irrelevance. Prior records follow unchanged.


## ADR-0049 review (2026-10-15): NO-GO, bounded frontend R1

Independent Git preflight: main; HEAD/local origin/main/live main all
703037925da345a810b3438715403721beea96d5, 0/0, empty index. Actual complete
inventory is 51 paths: 19 modified INCLUDING this preserved planner file and
32 untracked. The executor's 18 modified paths excluded the planner; do not
freeze a 50-path publication inventory. Client remains
8d72cb7c0029f08d5e48a81f035117c667273a1732b066db603391bb6d29d104.
Planner before this record was
39ff5ddec7702eab5f1ffe48dc5cacaa4ff1ff6787339b2c91b415b3eb7f1064;
all prior bytes are preserved. No implementation source was changed by Codex.

Code review supports the backend scope, independent probe admission, exact
six-value policy, complete binding comparison, process/environment bounds and
new-port refusal in both sealed compositions. The optional defaulted process
executor seam is accepted as narrowly necessary guard-test instrumentation;
the mechanism and defaults are unchanged. The two additive Architecture
inventory entries are appropriate. No new backend correction is selected.

Independent checks: incremental serial build, zero warnings/errors;
Application permission family 69/69; adapter 173/173; new frontend three files
60/60; resolved-host permission guard facts 6/6. Executor final TRX counters
inspected: API 41/41, launch auth 4/4, controls 2/2, guard regression 116/116,
provider fixtures 205/205, Architecture 148/148. Evidence is retained under
logs/planner-review/adr49/review/ and original executor roots.

R1 (P2): gitHubRepositoryPermissionObservation.ts:196 uses isAbsent for
viewerPermission in PermissionNotReturned. It admits undefined/missing data,
although the selected contract requires an explicit null. Codex's read-only
probe passed actual JSON through the real generated response.fromJS: explicit
null stays null; an omitted member stays undefined; BOTH produce an admitted
PermissionNotReturned reading. Thus this is not a necessary generated-client
null normalization. The UI can claim GitHub returned no permission when the
host answer actually omitted a required fact. Existing tests explicitly
accept undefined and therefore preserve the error. Probe source and complete
sanitized output: decoder-null-probe.cjs and decoder-null-probe.log in the
review directory. No source or emitted product files were written by the probe.

Select one bounded correction in the SAME ADR-0049 executor chat: require
literal null only for this status's viewerPermission, with genuine red/green
regressions for explicit null, undefined, an omitted JSON member through the
generated client, and panel refusal without a no-permission claim or remote
facts. Replace the contradictory undefined-acceptance expectation with refusal;
do not remove the case. Keep all six reported-literal controls and ownership
behavior. Do not change shared absence handling, generated client, hook,
backend, DTOs, schema, guard or process composition. No new wire contract.

Final once: new decoder/hook/panel test files, npm run build (includes
typecheck), lint, changed docs links and diff/secret hygiene, and the Codex
probe showing explicit-null admitted/missing-member refused. Retain backend,
Architecture, generated-client reproducibility, fixtures, sealed guards,
siblings and journey evidence on unchanged covered inputs: the journey
exercises a valid permission and no hook/transport input changes. No full
matrix, extra mutation or browser journey is required for this decoder-only
correction. Broaden only for an actual changed input or unresolved failure.

Update current-work with actual inventory (including the planner), correction
commands/results and retained evidence. Describe the original frontend red as
a deliberately weakened-code run; it does not establish test-first chronology.
Preserve it and every attempt, including the M3 restoration failure; do not
fabricate or relabel historical evidence. The initial red-first evidence gap
can remain disclosed; this correction supplies genuine red for R1.

Return unstaged/uncommitted/unpushed for whole-slice GO/NO-GO. No publication,
closure diff or next slice is authorized. The dispatch baseline of all 51 raw
statuses/paths/hashes/byte counts is retained separately in
logs/planner-review/adr49/review/r1-dispatch-baseline.json. Preserve this planner
edit byte-for-byte during the correction and stop on a material discrepancy.

Qualification remains Blocked/TargetsNotReal, both allowances unused. No real
gh/provider invocation, CLI setup, ledger reset or cleanup. Keep every retained
root/log/TRX/artifact and all limits. The historical concurrent 409, older
target-test 500, disposal NullReferenceException and unnamed Api 2067/2068
failure remain separately unexplained; later passes prove neither flakiness
nor irrelevance. Prior selection and review records follow unchanged.


## ADR-0048 publication verified; ADR-0049 selected (2026-10-15)

Codex independently verified publication 703037925da345a810b3438715403721beea96d5
on main, local origin/main and live refs/heads/main, 0/0, empty index and clean
tracked/untracked tree. Its single parent is
5715fc2b324827e79bf3613473fe3d71ffe286b8; all 85 committed paths (31 M, 54 A),
modes, blobs and working-file raw hashes match the reviewed ADR-0048 manifest.
Message and co-author trailer match. No tests or generation were repeated for
publication verification. Evidence: logs/planner-review/adr49/publication-verification.txt.
The planner before this selection was
12344e270c59b216061f813275fc2d88777241b9f2c46ba0ef794b5c1bf37671;
all prior bytes are preserved. The generated client baseline remains
c22e82b943c625fa9ceaf6fe377cfe6887380d76d641906a17c7b6738e84a6b6.

Roadmap/code assessment: Increment 6 now has explicit authentication, bound
identity, exact-commit check inspection and durable Human publication consent,
but no permission observation or push executor. MVP completion still requires
the durable normal-push/PR path with fresh physical/remote gates and ambiguity
reconciliation, exact-SHA monitoring and bounded CI correction, remaining
Increment 4/5 coordination/provider contracts, packaging/self-hosting proof
and real qualification. Repository permission observation is a useful input
and a bounded next capability, not a sufficient release gate or a substitute
for any of those blockers. Broader CI enumeration, richer history and more
fixture/performance refactoring are optional for this selected outcome. Do not
call current doubles release qualification or this observation push readiness.

Select ONE visible additive slice: explicit ephemeral observation of GitHub's
viewerPermission for the already-bound recorded repository (proposed ADR-0049),
using one fixed read-only GraphQL query. The current identity and CI adapters
do not request permission; existing source/target/binding readers and CLI
snapshot admission supply the unchanged prerequisites. A direct push slice
would also need durable effect/recovery and Git authentication/transport
decisions that this read-only slice deliberately does not settle. No material
owner authority change is needed for this bounded observation.

Primary schema evidence checked on 2026-10-15:
https://docs.github.com/en/graphql/reference/repos#repositorypermission and
https://docs.github.com/en/graphql/reference/repos (Repository.viewerPermission).
The current enum includes SIX literals: READ, TRIAGE, TRIAGE_PLUS, WRITE,
MAINTAIN, ADMIN. Include TRIAGE_PLUS rather than relying on the older five-role
assumption. The nullable field is not denial or proof of a particular account
type. Repository role is not branch-level push authorization; protections and
rulesets remain independent. Reference:
https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches.

Contract: protected bodyless GET
/api/runs/{runId}/github-repository-permission-observation, no query parameters.
Fixed errors github_repository_permission_observation.not_found (404),
body_not_allowed (400), invalid_request (400, query keys). Envelope:
runId/status/source/target/binding/observation. Closed statuses:
NotRecorded, BindingNotRecorded, Unavailable, CliUnavailable, Unknown,
IdentityChanged, PermissionNotReturned, PermissionReported. First two and
Unavailable follow the existing CI scope semantics. CliUnavailable, Unknown
and IdentityChanged retain the coherent local scope but no observation.
Only the last two expose an observation (repositoryNodeId, owner, repository,
observedAtUtc, viewerPermission), with a matching bound node ID and permission
null only for PermissionNotReturned. PermissionReported admits all six exact
literals equally as reported facts, never a derived canPush/allowed verdict.

Use the three unchanged source/target/binding readers before and after the
one external call and compare the complete readings. An orphan, incoherent
scope, post-call drift or backwards observation time fails closed; no partial
remote disclosure. Validate the port result independently. Query constant:
query($owner:String!,$name:String!){repository(owner:$owner,name:$name){id name nameWithOwner viewerPermission}}
Fixed gh api graphql --hostname github.com --method POST with raw -f variables,
existing process executor/scratch and ADR-0041 environment policy, 15 s timeout,
8 KiB per stream/16 KiB combined, JSON depth 8 and one immediate-refusal
singleton slot. Strict whole JSON, no backslash, duplicate/extra fields,
errors/extensions, foreign names, unknown enum, malformed or oversized values.
Null repository/fault-shaped output is Unknown, not a permission verdict.
Caller cancellation and unexpected faults propagate; no generic catch.

Composition consequence found during code inspection: Program registers each
observer globally, while the existing sealed hosts replace only enumerated
ports. Add ONLY the new permission port to SealedSceneGuard's refusal list,
with resolved-host behavioral regressions for both existing clone compositions:
named refusal, zero non-Git process starts, independent refusal queues and
unchanged scene/images. Do not enable this port in any existing sealed clone,
change the guard mechanism or migrate another test class to snapshot reuse.
Retain allowed production Git reads; zero processes is not the assertion.

UI: separate collapsed GitHub repository permission region, explicit
Show/Check/Recheck/Close, no automatic requests or sibling result reuse;
project/run/source/target and opening own callbacks, same-batch single-flight
and stale-completion isolation. Show exact admitted role and a fixed disclosure
that it does not establish branch push permission. No login, token, URLs,
HTML/Markdown, storage, approval/execution control or previous stale answer.
Regenerate the client additively; own new contracts without moving old ones.

Acceptance: genuine focused red/green at adapter/handler/decoder/hook/panel;
three compiling risk mutants (node-ID check, full post-call scope comparison,
stale UI completion), verified restore hashes and rebuild. Final once on stable
inputs: incremental serial solution build/client reproducibility; new
Application family; new Infrastructure adapter class on the real executor
with owned executable doubles; new API family plus auth and two named existing
identity/binding or approval controls; full Architecture; new frontend files
and changed parent tests, build (includes typecheck), lint; strict e2e tsc and
one authenticated collaboration journey extension. Full fixture-contract suite
only if its owned gh role changes. Put exhaustive permutations at cheap
boundaries, admission refusals before delivery setup, representative full
production lineage at host/journey. No fixture migration/cache expansion.
Retain unchanged Domain/persistence/migrations/other adapters/other journeys/
harness/dependency evidence. Broaden only for changed shared inputs, failures
or an unenumerable impact; preserve mandatory CI/release gates. One build
writer, no mutations during validation, at most two proven-isolated test
commands, serialized journey. No routine clean build, shutdown or stress loop.
Because the shared guard inventory changes, also run ADR-0046 isolation and
the existing check-context endpoint class, plus ADR-0047's five resolved-host
guard/isolation controls and the new permission-refusal regressions. This is
targeted composition regression, not permission for broader host refactoring.

Stop for any Git discrepancy, need for new credentials/permissions, unsupported
schema, shared reader/executor/host lifecycle changes, changed wire behavior
of older operations, persistence/recovery or actual mutation. Evidence-based
challenges are welcome; present a bounded alternative before dependent work.
Claude implements and returns unstaged/uncommitted/unpushed for Codex review;
no commit/push GO is granted. Executor must preserve this planner edit byte-for-byte.

All historical limitations remain, including the unattributed concurrent 409,
older target-test 500, disposal NullReferenceException and unnamed Api
2067/2068 failure, separately unexplained. Later passes prove neither
flakiness nor irrelevance. Qualification remains Blocked/TargetsNotReal, both
allowances unused. No real gh/provider invocation, CLI setup, authentication,
ledger reset or cleanup; retain all roots, logs, TRX and ignored artifacts.
No next work beyond this slice is selected.


## ADR-0048 review (2026-10-15): GO for the frozen substantive diff

Codex independently verified main, HEAD/local origin/main/live main at
5715fc2b324827e79bf3613473fe3d71ffe286b8, 0/0, empty index, 85 changed
paths (31 modified, 54 untracked). The planner before this record was
5df2e6d583bfba583102e4bfc424e1effc0d7b5e90a3c1aa2708b666fd2aea72;
all its earlier bytes are preserved. The client remains
c22e82b943c625fa9ceaf6fe377cfe6887380d76d641906a17c7b6738e84a6b6.

R2 is accepted, completing R1 and the whole ADR-0048 review. Only the reader,
API test base, ADR and current-work changed among the 83 R2 baseline paths;
the two additions are the reader and production-host snapshot regressions.
The production handler remains byte-identical at
a7e405fc8146b08389e6f318b1db0d67ecaa771bf98dcea4b88e340e5db5b396.
The reader uses one EF-translated UNION ALL, with up to three candidates per
operand, retaining raw-byte admission, independent absence/orphan/count checks,
full-scope, actor, canonical payload and time validation. No lock, retry,
transaction, cache, schema or shared lifecycle change was introduced.

Codex inspected the new deterministic boundary tests and their named red,
green and compiling-mutant results. An identical competing request replays
the same 200 response with one pair recorded; another identifier conflicts;
stable corruption stays unavailable. The split-read mutant is killed and
M3 is killed on the new reader, whose restored hash matches the report.
Independent checks: incremental serial solution build, zero warnings/errors;
reader 124/124; production-host snapshot facts 3/3. Evidence is retained in
logs/planner-review/adr48/r2-*. The reader command had an unquoted logger
semicolon: the tests passed and their default TRX is retained and copied;
the subsequent wrapper errors and complete test console are preserved in
r2-reader-review-console.log. The wrapper's exit status is not test evidence.
No tests were repeated to repair that reporting mistake.

Executor final TRX independently inspected: Application approval/history
232/232, complete ADR-0048 Api family 180/180, Architecture 141/141.
Earlier unchanged-input Domain, persistence, frontend, client reproducibility,
journey and controls remain retained with their original limitations. The
first broad Application/Infrastructure runs are not represented as final
full-suite passes; only their corrected affected expectations were rerun.

The torn-read path is proven and fixed. The historical identical-request 409
has no captured body/code and its cause remains unproven. The older target-test
500, host-disposal NullReferenceException and unnamed Api 2067/2068 failure
remain separate unexplained observations; later passes establish neither
flakiness nor irrelevance. The initial skeleton-red limits, mutant survivors,
missing/aborted evidence, migration-generator drift, privilege skips, advisory,
real-CLI limits and every other recorded limitation remain preserved.

GO authorizes Claude to publish ONLY the reviewed 85-path substantive diff,
including this planner record and current-work, frozen in the ignored
logs/planner-review/adr48/publication-manifest.json. Verify every entry before
staging and each staged/committed mode/blob against it. One commit with the
exact baseline parent, then a normal fast-forward push of main and live remote
verification; stop on any discrepancy. No edits, regeneration or validation
reruns are needed for publication; a material diff change needs new review.
No separate closure diff or next slice is selected. This is publication of
local approval recording, not authorization to execute a GitHub branch push.

Qualification remains Blocked/TargetsNotReal, both allowances unused. No real
gh/provider invocation, CLI setup, ledger reset or cleanup is authorized.
Preserve all logs, TRX, manifests, historical failures, retained roots and
ignored review artifacts. Prior planner decisions follow unchanged.


## ADR-0048 review (2026-10-15): R1 accepted; NO-GO pending bounded R2 snapshot correction

Git verified: main, HEAD/local origin/main/live main at
5715fc2b324827e79bf3613473fe3d71ffe286b8, 0/0, empty index, 83 paths
(31 modified, 52 untracked). The supplied f14d4976 planner hash was historical;
the actual planner before this record was
09de18a52b003e53af6b41461d005bd25fa3b74e26be02f2f6abc1dfd3da311e.
All earlier bytes are preserved. Client remains
c22e82b943c625fa9ceaf6fe377cfe6887380d76d641906a17c7b6738e84a6b6.

R1 is accepted in isolation: Codex inspected the bounded raw-byte projections
and parsers, built with zero warnings/errors, independently passed reader
120/120 and the production-host scalar-corruption fact (12 cases). Original
malformed ApprovedAtUtc and ProjectId probes now return Incoherent, with the
Coherent control intact. Evidence: logs/planner-review/adr48/r1-*. The executor's
228/228 Application, later 177/177 Api and 141/141 Architecture evidence is
retained with the original failed Api run and all disclosed limits.

Codex performed ONE deterministic reader interleaving, no stress loop, using
an ignored standalone probe and the existing fresh file-backed SQLite fixture
with production migrations and coherent scope. Before the approval-event SELECT,
a second context committed one valid approval/event pair atomically. Results:
initial None; interleaved reading Incoherent; settled unchanged evidence Coherent.
This proves a real torn-read path in the two-statement reader. The unchanged
handler refuses that initial Incoherent result before taking its writer lock.
It does NOT prove that this path caused the historical 409: its body/code were
not captured. Probe source/log: logs/planner-review/adr48/snapshot-probe/ and
snapshot-probe-1.log. No handler/product source was changed by this probe.

Select R2 within the same ADR-0048 correction chat: make approval-row and
approval-event evidence a single consistent database snapshot in this reader,
prefer one bounded SQL statement through existing EF/LINQ. Keep up to three
candidates per evidence kind, all byte guards/R1 parsers, orphan/count/actor/
canonical/time/full-scope validation, and absence distinct from incoherence.
Do not synthesize facts from just the event, deduplicate conflicting evidence,
cache, retry a partial read or swallow faults. Do not add global transactions,
new locks, schema, converters or persistence abstractions. If the bounded local
single-statement shape cannot be translated safely, stop and present evidence
and an alternative rather than expanding scope.

Honor the owner's instruction: RecordGitHubBranchPublicationApprovalCommandHandler
must remain byte-identical at
a7e405fc8146b08389e6f318b1db0d67ecaa771bf98dcea4b88e340e5db5b396.
No handler/order/lock/retry or shared-host lifecycle changes. R2 corrects the
reader's evidence snapshot, preserving the initial-incoherence refusal and the
vanished/changed coherent approval guards rather than relaxing either.

Add genuine deterministic red/green reader and protected production-host
regressions: a competing identical request commits at the controlled boundary;
both HTTP requests return the same 200 response, one row/event persist, and
one command observes the approval/event evidence per reader invocation. Prove
a stable orphan/wrong actor/payload/count, malformed scalars, complete-scope
drift, changed/vanished coherent approvals and competing UUIDs still refuse.
Use a separate prestarted host/context for the writer, one-shot interception,
fixed phase markers and sanitized status/error codes. No sleeps or stress loops.
Kill one compiling split-read mutant (restoring the R1 reader is sufficient if
it compiles), restore the R2 hash and rebuild; rerun M3 for changed reader
coherence logic, not unchanged handler/UI mutants.

Final once: incremental serial build; approval Application families plus event
history registry; complete ADR-0048 Api family; full Architecture; changed C#
format/docs links/secret/diff hygiene. Retain Domain/Persistence/frontend/client/
journey/fixtures/older controls on unchanged inputs. No full long suites by
routine. Preserve original 409, old target-test 500, disposal NRE and all repeat
logs as separate evidence; never assign a common cause without proof.

Update ADR-0048/current-work with R1 acceptance, proven torn-read path, actual
results and unresolved historical attribution. Return unstaged/uncommitted/
unpushed for whole-slice GO/NO-GO. No publication/closure diff/next slice.
Qualification stays Blocked/TargetsNotReal, both allowances unused; no real
gh/provider/network, CLI setup, ledger reset or cleanup. Api 2067/2068 remains
unexplained, and all earlier limits, retained roots and ignored artifacts stand.

## ADR-0048 correction steering (2026-10-15): concurrency evidence pending

Codex verified main, HEAD/local origin/main/live main at
5715fc2b324827e79bf3613473fe3d71ffe286b8, 0/0, empty index; 83 changed
paths (31 modified, 52 untracked). R1 correction is reported, not yet accepted
by independent review. Client remains
c22e82b943c625fa9ceaf6fe377cfe6887380d76d641906a17c7b6738e84a6b6.
The previous planner hash was
f14d497623db0d3bedd36eccd839e141c953d662ddb07c275e28a0b578b753d7.

The concurrent replay failure is material and remains open: in
logs/executor/adr48/correction1/final-restored/03-api-adr48-family.log
(and api-adr48-family.trx), the named fact
Concurrent_identical_requests_from_separate_hosts_record_one_approval_and_one_event_and_all_return_it
had one Conflict among 16 identical requests (176 passed, 1 failed overall).
A later 177/177 pass does not explain it. The log does not establish which
fixed conflict/unavailable code or branch produced the response.

The executor already performed three repetitions and 90 diagnostic runs.
Do not continue stress loops. Preserve all logs/results, including the separate
older target-durability InternalServerError in concurrency/repeat-4 and the
WebApplicationFactory.DisposeAsync NullReferenceException in concurrency/diag-21.
Their causes and any relationship to the 409 are unproven; do not conflate them
or label them flaky. The earlier Api 2067/2068 failure also remains unexplained.

Codex retains judgment; the UI choice is Leave to Codex, not advance approval
of a Fix under lock. The rows/events pre-lock split is a concrete hypothesis:
a competing atomic commit between the reader's statements could produce an
apparent orphan that is refused before lock acquisition. Prove it with one
controlled production-handler interleaving against file-backed SQLite and owned
doubles (not sleeps/repetition). Capture sanitized status/error code, row/event
counts and ordered phase markers. This proves a path; it must not be presented
as proof of the historical failure's cause without matching evidence.

Authorize bounded test/evidence work in this same correction chat only. Return
a deterministic regression and smallest fix proposal, explaining whether it
changes initial-incoherence semantics and proving stable corruption, changed/
vanished coherent approvals, full-scope drift, exact replay and competing-ID
conflicts stay fail-closed. Do not yet change production locking/reader decision
order, add retries, weaken assertions or repair shared host disposal. Preserve
R1 and update current-work with evidence. Run only the new focused case and
needed controls; do not repeat full matrices for evidence gathering.

No GO, stage/commit/push, closure diff or next slice. Qualification remains
Blocked/TargetsNotReal, both allowances unused; no real gh/provider/network,
CLI setup, ledger reset or cleanup. Keep every recorded limit and retained root.
All prior planner bytes are preserved below.

## ADR-0048 review (2026-10-15): NO-GO, bounded stored-scalar admission

Git independently verified: main, HEAD/local origin/main/live main at
5715fc2b324827e79bf3613473fe3d71ffe286b8, 0/0, empty index; 82 submitted
paths (31 modified, 51 untracked). Selection planner hash before this record:
2c97a0d5befc692a92f3272afd35aca72e27b7f6befe960af785fc1efe8fa321;
client c22e82b943c625fa9ceaf6fe377cfe6887380d76d641906a17c7b6738e84a6b6.
Codex changed no implementation, migration, client or executor evidence.

Independent build: zero warnings/errors. Application new families 24/24,
six frontend files 209/209, new migration class 5/5 and selected Api controls
10/10 passed. Executor final TRX counters were inspected: Domain 22/22,
Application/registry 117/117, Persistence 415/415, new Api 176/176,
Architecture 141/141, older controls 13/13 and ADR-0047 guard facts 5/5.
Review logs/TRX and the standalone scalar probe are retained under ignored
logs/planner-review/adr48/. The probe uses the existing reader test's fresh
file-backed SQLite fixture, production migrations and a coherent row/event,
then changes only its own test database. Control is Coherent; both malformed
ApprovedAtUtc and malformed ProjectId throw System.FormatException.

R1: GitHubBranchPublicationApprovalReader projects GUIDs and DateTimeOffsets
directly into typed values in its row and new-event queries. EF parses these
while materializing ToListAsync, before the reader reaches its validation.
Thus malformed stored scalars escape as faults, instead of an Incoherent
reading (GET Unavailable with every fact null; POST unavailable without writes).
The existing corruption cases use well-formed but differing timestamps/IDs and
do not exercise this path. The new-table contract requires bounded defensive
admission of malformed stored values, not merely string/name/payload guards.

Correct only this feature-owned reader and its focused tests: bound and admit
raw stored representations of all GUID/time scalars it projects from the new
approval and its approval-event rows before typed conversion. Preserve valid
writer representations, UTC/order/canonical-payload comparisons, full snapshot
identity and the untracked bounded projections. Treat invalid/oversized/NUL
values as Incoherent. Do not default, clamp, normalize, repair, leak or re-save
corrupt values; never wrap unrelated database/cancellation faults in a blanket
catch. Do not change global converters, old readers, schema/migration, API wire
or frontend to address this finding. If a bounded local projection is not
possible, stop and present a specific alternative before wider changes.

Add genuine red/green named reader regressions for the new approval GUID/time
fields and event ID/time, including malformed and over-bound representations,
with a coherent control and restored original. Add representative production
host GET and replay POST cases proving exact Unavailable/unavailable, every
fact withheld, no writes and recovery to the byte-identical original response.
Use cheap reader/persistence tests for permutations, not a lineage per scalar.
Correction final checks: incremental serial build, affected Application
families, complete new Api family, full Architecture, changed C# format/docs
links/secret/diff hygiene. Retain Domain, Persistence, frontend/build/lint/e2e,
client reproducibility, journey, fixture and older controls when their inputs
remain unchanged; broaden only for actual changed shared inputs or failures.
Do not repeat the long full Application/Infrastructure suites by routine.

Accepted evidence-based adjustments: SQLite establishes its writer lock at
transaction begin; keep the no-op statement and the correct transaction-start
interception. New API-owned scope DTOs preserve additive generation. Project
ownership plumbing is necessary and the positive native journey covers it.
ADR-0046 reuse was optional; no speed-up is claimed. Passing unchanged-input
results from broad runs can be retained separately from the four corrected
inventory/version expectations rerun on final inputs; the full suites are not
claimed as final green runs. Keep their historical failures and named results.
Skeleton-red gaps and the M5 survivor/regression history remain disclosed.

No GO, staging, commit, push, closure diff or next-slice authority. Continue this
correction in the same executor chat. Qualification remains Blocked/TargetsNotReal,
both allowances unused; no real gh/provider/network, CLI setup, ledger reset or
cleanup. Api 2067/2068 stays unexplained; later passes prove neither flakiness nor
irrelevance. All other recorded limitations, historical failures, retained roots
and ignored artifacts remain preserved. The prior selection/history follows.

## Next selected slice (2026-10-14): ADR-0048, explicit local approval of one future GitHub branch push

### Verified publication and priority

Codex independently verified ADR-0047 commit
5715fc2b324827e79bf3613473fe3d71ffe286b8, its single parent
3ed2e898912dd8859061f9197dafe902b6167731, reviewed message/trailer and all
62 committed paths (20 M/42 A), modes/blobs and raw hashes/lengths against
manifest bffa77baec65c895cde98c427863537be8b328f2a1911b98f29e9987f23cd21d.
HEAD, local origin/main and live main agree; 0/0; index and tracked/untracked
state clean before this selection. Planner at publication:
cd9142e1aa10a64cddb03d72862aea0dc4fcebec7bb3b072f1580f13f213e8ce;
client d34e3ce98dc015be75e15141531d7a01b6a33d14fa78325c083c749046cb3e0b.
The executor's post-commit baseline-verifier mistake is reported; independent
comparison used the actual parent and found zero discrepancies. Publication
verification ran no implementation validation and modified no product input.

The primary MVP journey requires Human publication approval, push and draft PR,
exact-SHA CI/correction and restart reconciliation. ADR-0047 now supplies
bounded advisory check text; adding more inspection views does not remove the
publication barrier. Code has local-commit approvals, a completed-delivery
source, observation target and historical repository binding, but no distinct
branch-publication approval record. ADR-0005 requires scoped Human policy for
remote mutation. Select that local authority input now, before any network
writer. Required blockers still include actual push/PR, monitoring/requiredness,
CI evidence/diagnosis/correction, reconciliation, the remaining Increment 4/5
controls, real qualification and packaged self-hosting. More history/export,
analytics, broad fixture migrations and additional advisory viewers are optional.

### Selected bounded contract

Create ADR-0048 for an explicit Human approval of one future normal push of the
exact completed local commit to its exact owned branch in the repository named
by a coherent ADR-0040 target and ADR-0043 binding. It is a new, separate approval
record; never change CiObservation purpose or interpret the target/binding as
approval. No remote observation, permission assertion, push/PR, Agent dispatch,
run transition or automatic work is selected. Future execution requires a
separate explicit request and a separately reviewed executor with fresh local
ownership/commit, remote identity/ref, authentication/permission and ambiguity
checks. Available means a coherent recorded consent, never ready or pushed.
Do not require CommitMatched or green CI: that would prevent a first push.

Protected POST and bodyless GET:
/api/runs/{runId}/github-branch-publication-approval
POST body exactly seven string members, literal property spelling, no duplicate,
unknown or differently cased keys:
{approvalId, operationId, commitSha, branchName, targetId, bindingId, repositoryNodeId}.
UUIDs are nonempty D-format; commit is lowercase 40-hex; node ID uses the existing
policy; branch is the existing recorded owned branch, ordinally. No client host,
owner/repository, URL, refspec, credentials, message, text, action or timestamps.
Bound JSON to 8 KiB in the operation's reader as well as the HTTP limit, including
chunked bodies; standard non-JSON 415 remains. Reject all query keys on either
route; GET refuses any declared/body byte or Transfer-Encoding before dispatch.
Use operation-owned Mediator command/query/result and API-owned DTO graphs;
shared new views belong in Policies/GitHubBranchPublicationApproval, not a sibling
command/query. Regenerate the client additively; no shared-contract promotion.

Fixed non-echoing codes under github_branch_publication_approval:
invalid_request (400), body_not_allowed (400, GET), body_too_large (413),
not_found (404), unavailable (409, POST), conflict (409, POST).
Unexpected faults and caller cancellation propagate. No process or CLI gate.

Read the unchanged source/target/binding readers. Fresh complete values are the
only source of scope; do not inspect origin, latest rows or live Git. Initial
incoherence refuses. Open one short transaction; acquire the existing no-op Run
writer lock as its first statement, reread the complete scope/approval and compare
all source/target/binding values before saving. Client acknowledgements must
exactly match the current scope. A coherent existing same approvalId and same
normalized request replays its original row/event/time; altered inputs or another
UUID conflict. Detect cross-run UUID collisions under the lock. A same-request
race records once; a disappeared/changed existing approval is unavailable, not
recreated. No external call, transaction-spanning filesystem work or retry.

Persist GitHubBranchPublicationApproval in github_branch_publication_approvals
with caller approvalId, host-generated eventId, full source (run/project/operation/
workspace/commit/branch), exact target (id/eventId/names/recorded time), exact
binding (id/eventId/node ID/observed names/observation and recording times), and
approvedAtUtc. Fixed host github.com, action PushExactCommit, policy version 1.
Keep target names and binding observed names exactly in their distinct snapshots.
Unique per run, operation, target, binding and event; restrictive foreign keys;
immutable update/delete guards, no backfill/supersession/revocation or cleanup.
Approved UTC time must be at or after binding recording; equality valid, no clamp.
Save one canonical version-1 Human run.github_branch_publication_approved event
(payload at most 4 KiB) atomically with the row. Preserve normal journal sequence
allocation/notification behavior; no lifecycle, stage, workspace, budget, local
approval or other authority mutation. A failed/cancelled save commits neither.
Migration AddGitHubBranchPublicationApproval changes only this new schema and
necessary event registration; preserve documented unrelated generator drift.

A bounded, untracked feature-owned approval reader validates full pinned values
and exactly its one Human event, actor shape, canonical payload/time, and all
cross-links. Check row/event absence independently, including related foreign
rows; oversized/malformed stored values are withheld, never logged or projected.
Orphan, incoherent or missing authority is never a success or replay.
GET returns {runId,status,source,target,binding,approval} with four closed states:
NotRecorded (no eligible bound source and no approval evidence, all facts null),
ApprovalNotRecorded (coherent bound source, no approval row/event; scope facts only),
Available (coherent scope and approval/event; all facts), Unavailable (all facts
null for incoherence/orphan/drift). Unknown run is the fixed 404. POST returns
{runId,source,target,binding,approval} on create/replay, 200. Approval projection:
{id,eventId,host,action,policyVersion,localCommitOperationId,workspaceId,targetId,
bindingId,repositoryNodeId,owner,repository,branchName,commitSha,approvedAtUtc}.
It copies the target names; full binding snapshots stay validated in storage.

Cockpit: separate collapsed GitHub branch publication approval region under the
recorded target. Show sends nothing; explicit Load/Reload only. Before consent,
show destination, repository node ID, complete refs/heads/<branch>, SHA and the
fixed notice that one future normal push is approved, no push occurs now, and
neither PR/force/merge/delete/rerun nor automatic execution is authorized.
Approve one future push is enabled only for the current settled
ApprovalNotRecorded load; never by a sibling observation or stale callback.
Generate one request UUID on first activation, retain it within that owned opening
for a same-scope recovery retry, single-flight/same-batch guard, no automatic retry.
Failure/refusal/incoherent success clears old read to Not loaded and retains a
fixed alert; explicit Reload is required before another approval. Available shows
recorded consent with no execution button. Ownership includes committed project/
run/source/target and opening; close/scope change/A-B-A end it and ignore old
completions. No storage, polling, key/remount/reset-effect workaround. Add only
necessary closed event-history/summary registrations with accurate fixed wording.

### Acceptance and proportional validation

Use genuine focused red-first Domain/policy/handler/reader/frontend regressions.
Prove create, exact replay/races/collision, stale acknowledgement, full-value
binding drift with unchanged request IDs, backwards/equal clock, orphan/wrong
actor/canonical event corruption, bounded hostile stored data, rollback, trigger/
FK immutability and reopen. Prove zero gh/provider attempts and unchanged existing
source/target/binding/local approvals and lifecycle. Cheap admission tests need
no delivery. Keep one representative production-lineage proof.

Five serial compiling mutants, named kills, restored hashes and rebuild:
remove binding/node acknowledgement; omit complete locked-scope comparison
(coherent metadata drift with request IDs unchanged); omit event-coherence read;
omit approval event insertion; admit obsolete UI write completion.
Compilation failures/no-op edits are not kills and logs are never overwritten.

Final once on stable input: incremental serial solution build (zero warnings/
errors); new Domain/RunEvent-related families; new Application command/query/
reader families; complete Infrastructure Persistence namespace (new migration
and upgrade/reopen/constraint compatibility); complete new API family and MVC auth
inventory; full Architecture; existing binding API read/replay controls and the
five ADR-0047 resolved-host guard facts; new frontend decoder/hook/panel plus
changed target parent, parseEventSummary and runEventHistory tests; npm build
(includes typecheck), lint, strict e2e tsc, one affected collaboration journey;
changed C# format/docs links/secret/bundle/diff checks and additive client
reproducibility without deleting it. Report exact commands/filters, timings,
failure logs/named results and inputs supporting retained evidence.

Native journey uses real auth/host and unchanged owned doubles: Show no request,
Load one GET, approve one POST/row/Human event, explicit reload/reopen retrieves
the same approval, no automatic POST, zero additional gh/provider/Git mutation.
The fixed CommitDifferent remote SHA does not prevent local consent. Do not
change GhRole/harness or run real tools to create this outcome. If the new API
endpoint class uses ADR-0046 reuse, it owns a fresh fixture and per-fact SQLite
backup/host/clock/writable resources/immutable IDs, refuses every unrelated
observer (including identity and contexts), and preserves sealed hashes/scene;
no generic refactor or old-class migration. This new class has no before baseline.

Retain full unrelated Domain/Application/Infrastructure/Api/Vitest, remote
adapters, fixtures/harness, other journeys, canonical E2E chain and dependency
audits when inputs are unchanged. Broaden for changed shared input, failure or a
proved gap; no routine clean build, server shutdown or repeated passing matrix.

### Exclusions, stop gates and authority

No real gh/provider/network invocation, CLI setup/auth, push/PR/executor,
permission discovery, CI monitor/requiredness, diagnostic persistence/Agent
routing, scheduler/recovery framework, revocation or old authority change.
No ledger reset or cleanup. Normal disposal is only for new test-owned resources.
Real qualification stays Blocked/TargetsNotReal, both allowances unused.
Api 2067/2068 remains unexplained; later passes prove neither flakiness nor
irrelevance. Preserve all other limits, overwritten/unavailable/aborted evidence,
retained roots, historical failures and ignored logs/artifacts.

Update ADR-0048/index, context, GitHub architecture, cockpit spec, roadmap,
security and current-work with actual evidence. If authority semantics, migration
or safe bounded composition requires wider changes, stop dependent work and
present an evidence-based alternative; do not manufacture agreement or expand
scope. Owner input is required only for an unresolved material choice. Return
unstaged/uncommitted/unpushed for GO/NO-GO in a new Claude executor chat; no staging,
commit/push or next-slice selection. Preserve the planner record byte-identically.

## ADR-0047 review (2026-10-14): GO for the frozen substantive diff

R1 is resolved. Codex verified that only the feature-owned clone composition
changed: both unrelated GitHub ports now resolve to named refusing proxies,
with no permissive identity registration; shared producer/guard/defaults and
all product inputs remain unchanged in this correction. Effective-host red
attempt 2 failed exactly the identity/context cases; green and the independent
review passed all five guard/isolation facts. The broad fail-on-use first
attempt also rejected legitimate host Git reads. Its documented refinement is
accepted: preserve those reads, refuse/count non-Git starts. This proves zero
non-Git attempts in green, not zero host processes; it changes no product gate.

Independent correction build: zero warnings/errors. Focused resolved-host
review: 5/5, complete log/TRX in logs/planner-review/adr47/. Executor final
correction evidence: complete Api family 128/128, Architecture 134/134,
formatting/hygiene/links clean. Earlier independently reviewed adapter 154/154,
frontend 132/132, Architecture 134/134 and Api 125/125 remain applicable to
unchanged inputs (the final correction Api 128/128 supersedes that earlier
family count). Retained Application/client/journey/fixture evidence and their
recorded limits remain valid; no broad matrix was repeated without cause.

Git review baseline: main, HEAD/local origin/main/live refs/heads/main at
3ed2e898912dd8859061f9197dafe902b6167731, 0/0, empty index. The final inventory
is 62 paths (20 modified, 42 new), including current-work.md, this planner
record and the unchanged additive client at d34e3ce98dc015be75e15141531d7a01b6a33d14fa78325c083c749046cb3e0b.
The reviewed publication manifest is frozen under ignored
logs/planner-review/adr47/publication-manifest.json. Its exact hash and this
planner's final raw hash are provided in the publication prompt. The prior
selection, NO-GO and historical bytes below are preserved.

GO authorizes only that frozen 62-path substantive diff: verify all manifest
entries against Git status/raw hashes/lengths/normalized modes/blobs and the
baseline, stage exactly those paths, verify staged content, commit once with
parent 3ed2e898912dd8859061f9197dafe902b6167731, then normal fast-forward push
main and independently verify live main. Do not edit/regenerate/rerun checks
or make a separate closure diff. Material drift, push failure or divergence
stops publication; no force, reset, merge or rebase. No next slice is selected.

The overwritten npm-build log remains unavailable; aborted/incomplete red,
sandbox ENOENT and VSTest connection evidence stays retained. The Api 2067/2068
failure remains unexplained; later passes prove neither flakiness nor
irrelevance. All other recorded limitations stand. Qualification remains
Blocked/TargetsNotReal, both allowances unused. No real gh/provider, CLI
setup, ledger reset or cleanup is authorized. Preserved logs, historical
failures, retained roots and ignored review artifacts are not publication
inputs and must remain untouched.

## ADR-0047 review (2026-10-14): NO-GO, sealed-host observer correction

Codex independently verified main, HEAD/local origin/main/live main at
3ed2e898912dd8859061f9197dafe902b6167731, 0/0, empty index. The submitted
inventory is 60 paths (20 modified, 40 untracked), including the preserved
planner record. Its selection hash was f4372e3c81070132cbca3978a413eaef111a589d079d549814698b6ec57e3b0a;
client d34e3ce98dc015be75e15141531d7a01b6a33d14fa78325c083c749046cb3e0b.

Independent incremental build passed with zero warnings/errors; Api family
125/125, adapter 154/154, full Architecture 134/134 and six frontend files
132/132 passed. Mutation originals match the submitted handler/adapter/hook.
Review logs/TRX remain under ignored logs/planner-review/adr47/. The first
sandbox frontend run had four ENOENT loading failures (47 executed tests
passed); the first VSTest run aborted before executing tests after a testhost
connection timeout. Separate unsandboxed runs passed; these do not erase the
failed/aborted evidence or establish a product cause.

R1: GitHubCheckRunOutputSealedClone allows unrelated observers. The shared
SealedSceneGuard inventory contains neither IGitHubRepositoryIdentityObserver
nor IGitHubCheckContextsObserver; the new clone registers a permissive identity
double and leaves the production contexts adapter registered. Its test named
"nothing else overrides it" checks only inventory membership of the new port,
not the resolved host composition. Thus the selected rule that only this
operation's observer is enabled is not proved or enforced. No real invocation
was observed. Correct the new feature-owned clone to refuse and record both
unrelated ports, with no permissive identity registration; retain the shipped
producer and existing class defaults. Add behavioral regressions against the
resolved clone host (safe fail-on-use process engine during red), proving
named refusals, zero process attempts and unchanged sealed sources. Replace
misleading inventory-only claims and update affected identity-counter checks.

Keep this a correction of ADR-0047 in the same executor chat. Do not change the
product two-phase contract, shared production code, other fixture defaults or
the native journey to solve it. Body-before-query refusal, ordinal CLI path
comparison and the explicit authenticated CommitDifferent journey request are
accepted with their documented limits. Cancellation's structural-only gate
evidence remains a limitation, not proof of isolated behavioral gate coverage.

Correction validation: focused red/green guard regressions, one incremental
serial build, complete ADR-0047 Api family and full Architecture. If only the
new clone/tests/docs change, retain adapter/Application/frontend/client/journey
and ADR-0046 evidence; broaden for changed shared inputs or failures. Correct
the inventory wording/list to include current-work.md (57 listed source paths
plus current-work.md, planner and generated client = 60 submitted paths).
Keep the overwritten failing npm-build log disclosed as unavailable; do not
fabricate it or replace its history with a new pass. Preserve unique complete
logs and named results for correction failures and aborts.

No GO, staging, commit, push or next-slice authority is granted. Qualification
remains Blocked/TargetsNotReal, both allowances unused; no real gh/provider,
CLI setup, ledger reset or cleanup. The Api 2067/2068 failure remains
unexplained, and all other recorded limitations and retained evidence stand.
The previous selection and historical records below are preserved.

## Next selected slice (2026-10-14): scoped, explicit CheckRun output inspection

### Publication verification and MVP priority

Codex verified ADR-0046 at 3ed2e898912dd8859061f9197dafe902b6167731 with one
parent 9baf8bc1f2c6ad3d287c54f9e2dfe3ab466cc481 and the reviewed message/trailer.
main, HEAD/local origin/main/live main agree; 0/0; empty index and clean tracked/
untracked state before selection. All 21 committed paths (12 M/9 A), modes/blobs
and raw hashes/lengths match manifest SHA-256
44b7fe1e360a0dd21ca7d678509e7117bfb2d7c62cf673d47228c46612ab942d.
Client remains af681d73f06c3929b40bd773618469403a11e6ee0cf58c2a42f5515c2d12f10b.
Publication verification changes no implementation and reruns no validation.

Real qualification; remaining Increment 4 usage/session/compaction/permission
contracts; Increment 5 coordination, scheduling, lifecycle/recovery and linked
replay; Increment 6 scoped publication, workflow correlation, required-check
policy, automatic monitoring, diagnostic/correction routing and reconciliation;
packaged release and self-hosting remain required. More history/export/analytics,
extra providers and broader fixture migration are optional/deferred. The delivered
explicit check collection has identifiers and states but no diagnostic text.

Select ADR-0047: inspect one CheckRun's provider-supplied output for the exact
bound recorded commit, on explicit request. This advances evidence needed for
CI diagnosis without introducing diagnosis, Agent input, durable logs or mutation.
Required-check policy and publication need their own authority contracts; further
metadata-only workflow enumeration is less direct evidence for an actual failure.

GitHub's current public schema documents the required CheckRun and CheckSuite
fields, and node(id:) is a global lookup. Sources checked on 2026-10-14:
https://docs.github.com/en/graphql/reference/checks and
https://docs.github.com/en/graphql/reference/meta#node. This is documentation
evidence, not installed-CLI/account/schema qualification. A single combined node
query would request text before proving association. Therefore select two fixed
phases: metadata admission first, output second only after fresh host validation.

### Selected API and host contract

Protected bodyless GET /api/runs/{runId}/github-check-run-output-observation,
through GetGitHubCheckRunOutputObservation and its operation-owned types/port.
Exactly one query key checkRunId and one nonempty value, no trimming, 1..256 ASCII
letters/digits/_-/+=. Reject missing/duplicate/extra query keys or invalid values
before dispatch with github_check_run_output_observation.invalid_request (400).
Keep the earlier pre-dispatch body refusal for declared length, any transfer
encoding or a readable byte: github_check_run_output_observation.body_not_allowed.
Unknown run: github_check_run_output_observation.not_found (404). No other client
scope, branch, SHA, suite, executable, query, cursor, credential or text input.

Use unchanged ADR-0040 source/target and ADR-0043 binding readers. Compare the
entire coherent scope before/after metadata and again after output, no lock,
transaction or write. Drift has Unavailable precedence and strips all facts.
Require the unchanged fresh direct-executable CLI gate before metadata; before
output reread it and require the same resolved executable path, otherwise
CliUnavailable with no second invocation. Recheck cancellation before each call.

One operation-owned IGitHubCheckRunOutputObserver has metadata and output methods;
the handler, not the adapter's singleton state, owns phase admission. Independently
validate each entire probe, including data that will be withheld. Metadata must
prove bound repository ID, exact heads branch/recorded SHA, requested CheckRun ID,
its repository, suite repository and suite commit. Reject internally incoherent
repository IDs or a wrong echoed check ID as Unknown. Valid foreign associations,
another commit or missing/non-CheckRun node never reach the output method.
Before output the host clock must not precede binding.RecordedAtUtc. Output
repeats all metadata; the complete selected check identity (ID/repositories/suite/
commit) must equal the admitted first identity. Never cache another request's
metadata. Unexpected faults and caller cancellation propagate.

Envelope {runId,checkRunId,status,source,target,binding,observation}; existing
API-owned source/target/binding shapes. Eleven closed statuses:
- NotRecorded/Unavailable: all four fact groups null.
- BindingNotRecorded: source/target only, no process.
- CliUnavailable/Unknown/IdentityChanged: source/target/binding, observation null.
- RefNotReturned: observation with ref null and checkRun null.
- CommitDifferent: observation with differing admitted ref, checkRun null.
- CheckRunUnavailable: matching ref, checkRun null (missing/wrong type/foreign
  association or identity changed between phases); never echo foreign metadata.
- OutputNotReturned: matching ref and admitted check metadata, output null.
- OutputReported: matching ref/check and output object with at least one non-null
  field; empty strings are returned text, not null.

Observation {repositoryNodeId,owner,repository,observedAtUtc,ref,checkRun}; ref
{branchName,commitSha}; checkRun {id,checkSuiteId,commitSha,name,status,conclusion,
output}; output {title,summary,text}, each nullable. Check ID equals the request,
check commit equals source/ref, and all names are observed safe text. Clock stamp
is taken after the last call; earlier than the binding means Unknown, no clamp.
IdentityChanged carries no observation or foreign ID and needs no stamp. Root
identity/ref classification precedes check association; all fields are validated
first. No requiredness, translated verdict, completeness or permission claim.

### Adapter and text contract

GitHubCheckRunOutputObservationAdapter uses the existing executor, ADR-0041
environment/scratch/privacy rules, discarded stderr and no raw-output retention.
Singleton nonqueued slot per process, finally released; phases may interleave
across requests and must not share phase state. At most two sequential processes
per request, no retries; each has 15 s, so external execution is bounded by 30 s.
Metadata caps stay 8 KiB/stream, 16 KiB combined; output phase alone uses
256 KiB/stream, 512 KiB combined, depth 8. Older adapters remain unchanged.
Fixed argv: gh api graphql --hostname github.com --method POST -f query=<constant>
-f owner=<stored> -f name=<stored> -f ref=refs/heads/<recorded> -f id=<selected>.

Metadata document:
query($owner:String!,$name:String!,$ref:String!,$id:ID!){repository(owner:$owner,name:$name){id name nameWithOwner ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid}}}} node(id:$id){__typename ... on CheckRun{id repository{id} checkSuite{id repository{id} commit{oid}}}}}

Output document:
query($owner:String!,$name:String!,$ref:String!,$id:ID!){repository(owner:$owner,name:$name){id name nameWithOwner ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid}}}} node(id:$id){__typename ... on CheckRun{id repository{id} checkSuite{id repository{id} commit{oid}} name status conclusion title summary text}}}

Strict exact JSON shapes, no missing/extra/duplicate/escaped keys, errors,
extensions, escaped metadata literals or fallback ref. Other node types admit
only one literal GraphQL typename and no check fields; null node is supported.
Use existing admitted repository/branch/SHA/opaque-ID rules. Check name uses the
ADR-0045 safe label rules. Same six statuses/nine nullable conclusions as ADR-0045,
independent, no pairing rule. Operation-owned validation; no shared-type promotion.

Only name/title/summary/text decode escapes once. Output strings preserve null,
empty, whitespace and CR/LF/TAB; well-formed Unicode, no other Control, Format,
Surrogate, LineSeparator, ParagraphSeparator or U+FFFD. Title <=256 UTF-8 bytes,
summary <=8 KiB, text <=16 KiB, total trio <=24 KiB. Check bounds before and after
redaction; refuse the whole oversized/invalid probe, no truncation. Reapply the
unchanged internal StreamingOutputRedactor after decoding every admitted text
field. No copied regex, second decode, normalization or markdown/HTML rendering.
Retain the limitation to the five supported secret shapes and Unicode-table
differences; decoded bounds and a worst-case JSON-escape size probe must be proved.

### Cockpit, test reuse and validation

Only CheckRun entries of ContextsReported offer a collapsed output region, with
Show (no request), Check/Recheck/Close. StatusContext entries offer none. Own
answers by committed scope/check ID/parent lifetime/opening; parent close,
refresh or scope change ends child ownership. Preserve same-batch single flight,
late completion and A-B-A guards, no automatic request/effect/timer/storage/key
workaround. Plain inert text/pre only, empty/null disclosures and redaction/
untrusted-evidence statement. No URLs, copying, downloads or Agent action.

Explicitly allow this new endpoint class its own ADR-0046-style fixture instance
through feature-owned composition of the shipped sealed producer/backup mechanism;
fresh clone/DI/observers/clocks/writable roots, immutable identities, source guards
and disposal. Share no fixture instance across classes/runs. Add the new observer
to the existing sealed-scene refusal inventory; only this operation's controlled
test composition overrides it. Do not refactor the producer or migrate another
existing class. A new class has no old benchmark: record setup/wall time and
isolation evidence, invent no baseline or speedup; document this narrow ADR-0047
extension rather than silently broadening ADR-0046.

Focused red-first policy/handler/adapter/decoder/hook/panel cases. Serial compiling
mutants: bound-ID gate, suite-commit gate, kind/source/target-only post-call
comparison, post-decode redaction, stale selected-check callback. Require named
kills, restored hashes and rebuilds; failed compile/no-op is not a kill.

Final matrix once: incremental serial build; new Application/Infrastructure
families; complete new API auth/query/body/error/wire/drift family and MVC auth
inventory; full Architecture and ProviderFixtureContractTests; ADR-0046 isolation
class; new frontend three files plus parent check-context three files; npm build
(includes typecheck), lint, strict e2e tsc and affected collaboration journey;
changed C# format/docs links/secret+bundle hygiene/git diff --check. Preserve
client additive regeneration/reproducibility, fixture isolation and timing logs.
Retain unchanged full Domain/migrations/unrelated suites/harness/other journeys/
canonical chain/dependency audits. Broaden only on changed shared inputs, failure
or demonstrated gaps. Detailed permutations stay at the cheapest boundary.

Native GhRole keeps its fixed different SHA. Add only the two exact query doubles
and contracts; journey proves metadata-only CommitDifferent, exactly one new
HTTP/process, no output query/text/action or writes, and no automatic work on
reload/close. Matched output is proved at process, production-host and UI levels;
do not invent recorded-SHA discovery or change harness/authentication for it.

Add ADR-0047, index/context, architecture, cockpit spec, roadmap/security notes
and current-work evidence. No Domain/schema/event/persistence changes, provider
diagnosis/correction, workflow/log/artifact retrieval, mutation/approval, scheduler,
permission mode, shared executor/environment or dependency change. Stop and discuss
if safe gates or schema require another contract. Return an uncommitted/unpushed
diff in one new executor chat, no commit/push or next-slice authority. Preserve
all limits and unique complete failure logs, including Api 2067/2068 unexplained.
Qualification remains Blocked/TargetsNotReal, both allowances unused; no real
gh/provider, CLI setup, ledger reset or cleanup of retained roots/evidence.

## ADR-0046 correction review (2026-10-14): GO for the frozen substantive diff

This decision supersedes the NO-GO below, preserving its finding and evidence.
Codex verified main, HEAD/local origin/main/live main equal
9baf8bc1f2c6ad3d287c54f9e2dfe3ab466cc481; 0/0; empty index; 21 changed
paths (12 modified including planner, 9 untracked). The planner was byte-identical
to the NO-GO before this update. Client remains
af681d73f06c3929b40bd773618469403a11e6ee0cf58c2a42f5515c2d12f10b.

The mutable-membership finding is closed. Capture severs the seed List<Guid>
alias with an ImmutableArray<Guid>; IdsForClone returns a record copy with
immutable membership. Scalar identity, ordering, null and empty are preserved;
the global LocalCommitLineageIds and other classes' defaults are unchanged.
Three named regressions prove input mutation cannot change the sealed value,
real live clones cannot mutate siblings or later clones, and values remain
intact. The original-alias mutant compiled and was killed by exactly these
three; the seven prior isolation facts passed. Restored identities-file SHA-256:
4198d0538740587f325b03d392822442bb3e1be2b4aab3c21d8fc84415a7ae51.

Independent correction review: incremental serial solution build, zero warnings
and errors; full isolation class 10/10. Complete logs/TRX remain under
logs/planner-review/adr46/correction1/. Executor final logs/TRX agree with
isolation 10/10, endpoint 20/20 and Architecture 127/127. The final complete-class
wall time is 17.2724346 s against the retained 120.228545 s baseline (85.6 percent
reduction). Earlier independent review also ran the preserved 20 facts in 17.700 s.
No duplicate baseline or full Api run was needed for this local correction.
Retained unchanged checks and input-based reasons are accepted.

Both sealed stages of one production lineage remain accepted. There is no
fabricated binding-less state, product endpoint migration or global cache.
The comparison of a single fact is now correctly inconclusive: 10.68 s after
was measured, approximately 9 s before was estimated, so neither improvement
nor regression is established. The seed and per-fact host overhead remain.
Performance acceptance applies only to this class; no product or release gate
is closed. Future fixture migrations need separately selected scope and evidence.

Preserve the original alias probe, failing red attempts, compiling mutants,
compile/script failures, and all other logs. The first correction red attempt
did not compile and its test output was a stale binary; it is honestly excluded
from red evidence. The subsequent compiling red and mutation both demonstrate
the three new regressions. All prior limitations remain, including the bounded
scene fingerprint's inability to detect a reverted transient write.

GO authorizes exactly one substantive commit of the 21 frozen paths, including
current-work and this planner decision, followed by a normal fast-forward main
push and live remote verification in the same executor turn. Freeze the exact
status/path/raw SHA-256/byte count/Git mode/blob/baseline mode/blob entries in
logs/planner-review/adr46/publication-manifest.json; the executor prompt supplies
its SHA-256. Verify every entry and absence of extras before staging, then exact
staged and committed inventories/modes/blobs, with one parent at the baseline.
No implementation/documentation edit, regeneration, validation rerun, separate
closure diff or next-slice selection is authorized. Stop on mismatch, unexpected
state, push failure or remote divergence; no force, reset, merge or rebase.
The planner selects the next slice after independent publication verification.

The unnamed Api 2067/2068 failure remains unexplained; later passes prove neither
flakiness nor irrelevance. Qualification remains Blocked/TargetsNotReal, both
allowances unused. Preserve ledger, historical failures, retained roots and
ignored evidence. No real gh/provider invocation, CLI setup or ledger/evidence
cleanup is authorized. Normal disposal stays confined to new owned test resources.

## ADR-0046 review (2026-10-14): NO-GO for one bounded identity-isolation correction

Codex verified main, HEAD/local origin/main/live main equal
9baf8bc1f2c6ad3d287c54f9e2dfe3ab466cc481; 0/0; empty index; exactly 21
changed paths (12 modified including the preserved planner, 9 untracked). The
planner was byte-identical to the selection before this review update; client
remains af681d73f06c3929b40bd773618469403a11e6ee0cf58c2a42f5515c2d12f10b.
No publication or next slice is authorized. Preserve all prior planner bytes.

### Blocking finding: the seed's mutable execution-membership list is shared (P2)

The fixture constructs GitHubCheckContextsSealedIdentities from delivered.Ids
directly, then ProvisionAsync passes that same Ids object to every Bound.
LocalCommitLineageIds includes AllExecutionIds, whose actual seed value is a
List<Guid> returned by LocalCommitLineage.SeedAsync. IReadOnlyList is an interface,
not an immutable collection; it can be cast back and mutated. Thus the new
identity graph still aliases a mutable seed object between facts, contrary to
the selected isolation contract. File/scene hashes do not detect this in-memory
change. Existing tests check observers, clocks and databases but not membership.

The independent named probe in logs/planner-review/adr46/alias-probe.log uses
the actual compiled identity and Bound record types with the same assignment as
ProvisionAsync. It demonstrates that a membership mutation reaches a second
borrower and the seed. It is a type-level reproduction, not a fixture/host test;
its source and failing result are retained. Production behavior is unaffected.

Correct only the new sealed-identity/clone construction: sever the seed list
alias at capture and preserve the complete ordered membership as an immutable
snapshot. A clone may share genuinely immutable values or receive its own
read-only copy; no mutable collection may be shared with seed or another clone.
Preserve every scalar and null/list distinction. Do not change the existing
global LocalCommitLineageIds or other classes' default setup. Add red-first
regressions for capture independence from mutable input and for real live clones'
membership isolation, including a later clone and sealed identities. Reintroducing
the original mutable alias must compile and be killed by a named regression;
restore verified hashes and rebuild.

### Accepted adaptation and evidence limits

Accept two sealed stages of the same production lineage: TargetRecorded before
the binding POST and BindingRecorded afterward. This preserves the binding-less
fact without fabricated/deleted rows and changes no authority or reuse scope.
Keep this design; no rework to per-fact binding is requested. The isolation
validation class creates its own class fixture to test the mechanism, rather
than migrating another product endpoint or sharing an existing fixture.

The complete-class speedup is supported: paired executor wall logs show
120.228545 s before and 17.5236708 s after, 20/20 facts both times, 85.4 percent
reduction. Codex independently built incrementally with zero warnings/errors,
ran the same 20 facts in 17.700 s wall and ran isolation 7/7. Complete logs/TRX
remain under logs/planner-review/adr46/. Diff inspection confirms the 20 named
facts and independent assertions are preserved. Both compiling original mutants
and restored hashes are verified; the failed compilation/script attempts remain.

Clarify the single-fact report: 10.68 s was measured after; approximately 9 s
before was only estimated. A single-fact speedup or regression is not established
by that comparison. Retain the known seed overhead and do not claim either result
without a paired measurement. No further single-fact benchmark is required here.

### Proportional correction validation

Run the new regressions red first and the one compiling alias mutant serially.
After restoration run one incremental serial solution build, all isolation
cases including the new membership cases, the optimized 20-fact endpoint class
once with full wall timing, full Architecture, changed C# formatting, changed
document links, secret hygiene and git diff --check. Compare the final class
time with the retained 120.228545 s baseline; do not repeat that baseline.
Retain unchanged admission/corruption/drift, default-seam controls, product,
frontend, journeys, adapter, fixture, client and dependency evidence with reasons.
Stop for discussion if a fix needs shared lineage changes, production changes or
wider fixture migration. Present the uncommitted/unpushed correction in this same
executor chat, with current-work evidence and limitations updated.

Preserve the unexplained Api 2067/2068 failure and every other recorded limit;
later passes prove neither flakiness nor irrelevance. Future failures keep complete
logs and named results under unique names. Qualification remains
Blocked/TargetsNotReal, both allowances unused. No real gh/provider invocation,
CLI setup, ledger reset or historical/evidence cleanup. Normal test disposal is
confined to newly created owned disposable resources. No stage, commit or push.

## Next selected slice (2026-10-14): isolated snapshots for check-context API tests

### Verified publication and priority

Codex independently verified ADR-0045 publication at
9baf8bc1f2c6ad3d287c54f9e2dfe3ab466cc481, one parent
be3f1ce4a9787f26686e1f431dd11b2aacb06910, the reviewed commit message and
executor trailer. main, HEAD/local origin/main/live main agree, 0/0, empty
index and clean tracked/untracked state before this selection. All 53 committed
paths (18 M/35 A), modes/blobs and raw working-file hashes/lengths match the
frozen manifest SHA-256
1c12d4b5bc88f2b055b0cc70f49ffaa24592a28829caf2e9ad3e059538b98464.
Client remains af681d73f06c3929b40bd773618469403a11e6ee0cf58c2a42f5515c2d12f10b.
Publication verification reran no validation and edited no implementation.

Required MVP gaps remain: real qualification; Increment 4 Claude usage/session/
compaction/permission contracts; Increment 5 end-to-end coordination, scheduling,
lifecycle/recovery and linked replay; Increment 6 scoped publication, workflow
correlation, required-check policy, automatic observation, diagnostics/correction
and remote reconciliation; packaged release and self-hosting acceptance. Extra
history/export/analytics and additional providers remain optional. ADR-0045
supplies explicit bounded metadata, not complete workflow evidence or authority.

Select one maintenance slice in response to the owner's test-duration request.
The measured EndpointTests setup cost was 84.5 s of 94.2 s for 19 tests; the
corrected class now has 20 named facts, and the complete 29-test API family took
103 s. Repeating production delivery for each read-only scenario is a measured
iteration bottleneck. This slice supports MVP delivery but closes no product,
real-provider or release gate. The earlier general cache/cloning ideas are not
accepted globally: only the class-local contract below is now selected.

### Bounded test-only design

Add ADR-0046 for a run-local, class-owned sealed snapshot used only by
GitHubCheckContextsObservationEndpointTests. Initialize it once per execution
of that class through the unchanged production lineage, real Git local commit,
protected target POST and binding POST. Stop the seed host and all writers before
sealing; use SQLite's consistent backup facility, not a copy of a live db/WAL.
Keep the source snapshot unchanged and open it read-only for backups.

Every named fact receives its own physical SQLite backup, fresh production
WebApplicationFactory/DI scope, observers/counters, clock, clients and writable
artifact/scratch roots. Only the sealed evidence and its read-only Git scene are
class-owned; ownership/disposal is explicit. Preserve persisted identity and
canonical events without rewriting paths or fabricating rows. Fresh CLI evidence
is per-test; it is not cached eligibility. Copy scalar identities, not the seed's
Bound, host, observer or clock objects. No supervisor, provider or Git writer
may mutate the sealed scene during a clone's observation. Other test classes
keep their existing setup. Cache nothing between executions or processes.

Keep all 20 named facts individually selectable and preserve every independent
assertion and corruption/fault/clock control. No merged mega-test, reduced matrix,
mocked reader/DbSet/EF InMemory, authentication bypass, assertion weakening,
global mutable cache, global parallelism or product behavior change. Permit
minimal optional test-base/host construction seams only where required to attach
the owned snapshot; existing callers retain their defaults. Stop for planner
discussion if the design needs production changes or wider fixture migration.

Prove isolation with two concurrently live clones: distinct files and resources,
mutated capability/binding/events in A leave B and the sealed source unchanged;
a subsequent clone starts clean. Observer answers/counters and clock state are
independent. Disposal of A preserves B/source until their owners finish. The seed
really executes the production delivery and records coherent target/binding;
the representative real-adapter projection still passes. Two compiling mutants
(shared clone database; shared mutable observer/counter) must be killed by named
isolation cases, with verified hash restoration and rebuild.

### Proportional evidence and stop gates

Measure one baseline of the current 20-fact class, --no-build/--no-restore, with
complete log/TRX, command wall time and setup/observation breakdown. Compare with
one final run of the same class after changes, counting seed initialization,
backup, host startup and disposal rather than moving cost outside measurement.
Target at least 30 percent reduction and report whether the class is under one
minute; no speedup is assumed. If gain or isolation cannot be demonstrated, stop
and report the limitation instead of expanding scope or weakening evidence.

Develop with focused isolation cases. Final checks once on restored stable
binaries: incremental serial solution build; optimized EndpointTests (also the
after measurement); remaining check-context admission/corruption/drift family;
new isolation cases; full Architecture; one representative existing binding and
local-delivery receipt test if shared test seams change; changed C# formatting,
changed-document links, secret hygiene and git diff --check. Retain product,
frontend, journey, provider-fixture, client, dependency and qualification evidence
because their inputs are unchanged. No full Api or every-suite default. Broaden
only on changed dependencies, failures or demonstrated coverage gaps.

Document ADR-0046, its index, the narrow validation-policy contract and actual
results in current-work. Present an unstaged, uncommitted, unpushed diff for review
in one new executor chat; this selection grants no commit/push or next slice.
Normal disposal is confined to newly created owned disposable test resources;
no ledger, retained root, historical failure or ignored evidence cleanup.
Qualification remains Blocked/TargetsNotReal, both allowances unused. No real
gh/provider invocation or CLI setup. Preserve the unexplained Api 2067/2068
failure and every other recorded limit; later passes prove neither flakiness nor
irrelevance. Future failures retain complete logs and named results, uniquely
named TRX files and sanitized assertions, including failed performance attempts.

## ADR-0045 correction review (2026-10-14): GO for the frozen substantive diff

This decision supersedes the NO-GO below; preserve its findings and evidence.
Codex verified main; HEAD/local origin/main/live main equal
be3f1ce4a9787f26686e1f431dd11b2aacb06910; 0/0; empty index; the unchanged
53-path inventory (18 modified, 35 untracked). All 52 correction1 source-hash
entries match. The planner was byte-identical to the NO-GO record before this
planner-owned update. Generated client remains
af681d73f06c3929b40bd773618469403a11e6ee0cf58c2a42f5515c2d12f10b.

R1 is closed: the new adapter decodes labels once, validates the bounded text,
reapplies the unchanged internal redactor through ProcessChunk/Flush, validates
the result, and exposes only safe text. Shared execution, older adapters, query,
argv, capture limits, wire shape and classification are unchanged. Public and
real-executor/owned-process tests cover both label kinds and all five supported
shape classes, with literal and escaped controls. The production-host API
projection and inert UI disclosure are correct; legitimate escaped text and
single decoding remain covered. The original independent normalization probe
now passes (literal masked, escaped admitted but masked). M6 compiles and is
killed by 17 adapter regressions and the API projection regression; restored
adapter hash is 6c6bfc7c18325de6730b398d1d6983c612742aad6e262c5fa5d68b31208b03c1.

R2 is closed: the new real file-backed database test creates a coherent binding
and canonical event replacement using the unchanged Domain builder, proves
Available through the public binding GET, and keeps Bound/source/target valid.
The same replacement during observation yields Unavailable with every fact
stripped; the original binding response is restored byte-for-byte. The compiling
kind/source/target-only M3 mutant is killed by this new case while the older
drift fact passes. The production handler is unchanged, restored hash
70dc0f7c1a2b61b68d53945ada73b80eacccc4ad14d9b136e9866cdcdc46604c.

Independent review: incremental serial solution build, zero warnings/errors;
adapter 270/270; new frontend three files 66/66; the two specific API correction
regressions 2/2; original redaction probe green. Complete review logs/TRX remain
under logs/planner-review/adr45/correction1/. Executor final TRX agree with
270 adapter, 29 new API family and 127 Architecture passes; frontend build/lint
and 66 tests passed after the disclosed wrong-directory attempt. The affected
journey also passed once. Retained input-based results are accepted; no broad
matrix repetition, clean build or shutdown is requested for publication.

Accept the disclosed bounded evidence limits, without erasing them: first red
assertion printed a fictitious token-shaped value; later assertions are sanitized
and the first log/TRX remain untouched. The old m3 TRX overwrite is recorded,
with the surviving m3a copy and m3b log retained; no missing evidence is invented.
The wrong-directory frontend attempt and its actual rerun are distinguished.
The scripted API engine starts no process; constructing the real adapter creates
the existing host scratch directory. These facts grant no cleanup authority.
Redaction covers only the five documented shapes, not arbitrary unknown secrets.

GO authorizes exactly one substantive commit of the 53 reviewed paths, including
current-work and this planner record, followed by a normal fast-forward push of
main to origin/main and live remote verification in the same executor turn.
Freeze modes/blobs/raw hashes/byte counts/statuses in the ignored
logs/planner-review/adr45/publication-manifest.json; the complete executor prompt
identifies its SHA-256. Before staging verify every entry and absence of extras.
After staging and committing verify exact inventory, modes and blobs against it,
with exactly one parent at the baseline. No file edit, regeneration, validation
rerun, separate closure diff or next-slice selection is authorized. A changed
manifest entry, extra path, baseline/index discrepancy, publication failure or
remote divergence requires stopping and reporting, without force or reconciliation.
The planner selects the next slice only after independent publication verification.

The unnamed Api 2067/2068 failure remains unexplained; later passes prove neither
flakiness nor irrelevance. Preserve all other recorded limits, historical failures,
ledger, retained roots, logs and ignored artifacts. Qualification remains
Blocked/TargetsNotReal with both allowances unused. No real gh/provider invocation,
CLI setup/authentication/configuration, ledger reset or cleanup is authorized.

## ADR-0045 review (2026-10-14): NO-GO, bounded correction only

Codex independently verified main; HEAD/local origin/main/live main equal
be3f1ce4a9787f26686e1f431dd11b2aacb06910; 0/0; empty index; 18 modified
and 35 untracked paths, including the preserved planner selection. The 52
executor source-hash entries match; the separately checked planner was
7ca7baa76b45fe974f88807deb9ebfa9e411efaa3572102c81e6f441de1fa2e5.
The additive generated client remains
af681d73f06c3929b40bd773618469403a11e6ee0cf58c2a42f5515c2d12f10b.
No staging, commit, push, publication authority or next slice is granted.
Preserve the prior selection and all delivery evidence below this record.

### R1: decoded labels bypass supported secret-shape redaction (P1)

The unchanged streaming redactor acts on raw JSON before the new adapter's
Label/GetString decodes escapes. A fictitious classic-token shape is masked
when literal, but encoding its initial character makes it pass raw redaction
and reappear in the admitted StatusContext name. The independent probe composes
the real redactor function with the real adapter parser; it uses no process,
account, credential or network and does not claim a public-boundary test.
Its named assertion fails; both the literal control and escaped admission are
recorded in logs/planner-review/adr45/probe.log with the probe source retained.
The initial probe-project reference setup failure is retained separately and
is not a product failure. This is a normalization bypass of existing supported
shapes, not a demand to detect arbitrary unknown secret formats.

Correct only the new Infrastructure adapter: validate the decoded bounded
label, reapply the existing fixed redaction after the one JSON decode using
the unchanged internal redactor, validate the resulting label, and expose only
that safe text. Do not copy regexes, change the shared executor or old adapters,
reject all escapes, or perform a second decode. Preserve legitimate escaped
Unicode, quotes, backslashes and markup as inert text. Explicitly revise the
unpublished ADR-0045 exact-label claim to safe post-redaction text and disclose
in the new panel that labels may be redacted by the host. Keep wire shape,
query, bounds, enums and status/nullability contracts unchanged.

Require red-first public ObserveAsync regressions for both label kinds and
all five existing supported shape classes using fictitious data, literal and
escaped controls, plus real-executor/owned-process composition and representative
API/UI safe projection. Removing the post-decode redaction must compile and be
killed by the named new regression; restore source hashes and rebuild.

### R2: binding-value comparison remains unproved (P2, evidence gap)

The production full Scope record comparison is correct. The new row-and-event
deletion case kills source/target-only comparison, but changes ScopeKind too.
It does not kill the first surviving variant that retains kind, source and
target comparisons while ignoring binding values. Other current cases make
the binding incoherent and erase source/target, so they do not close that gap.
Add an owned file-backed database case replacing the binding and canonical
event coherently while observation waits: same Bound kind/source/target, changed
binding identity. Prove the replacement is admitted by the unchanged reader,
then require Unavailable with every fact stripped, and restoration afterward.
Use existing test-only corruption/restore helpers; change no production trigger,
reader, handler, schema or migration. Rerun the kind-retaining M3 variant against
this case, require a named kill, restore hashes and rebuild. Preserve both prior
survivors. m3.trx was reused with an overwrite warning; m3a has a retained copy
and m3b has a retained log, but no distinct m3b TRX was found. Record that limit
honestly and use unique result paths for every future run.

### Independent evidence and proportional correction validation

Codex incremental serial solution build: 0 warnings/errors. Adapter 250/250,
new frontend three files 65/65, existing scope-drift host fact 1/1. Complete
logs/TRX are retained under logs/planner-review/adr45/. Executor final TRX
counters agree: Application 117, Infrastructure 250, Api 34, Architecture 127,
fixture contracts 175, all passed. Existing passes do not disprove R1 or R2.

Accept the disclosed skeleton red runs as skeleton evidence, browser/.NET
Unicode-table fail-closed limit, fixed-SHA journey boundary, measured setup cost
and unneeded shutdown disclosure; none establishes comprehensive coverage.
No fixture caching/cloning or test infrastructure optimization is selected.
Use focused new red tests and the two named compiling mutants during correction.
After restoration, one incremental solution build and one final matrix: complete
new adapter class; complete new API admission/endpoint/corruption/drift family
including representative safe projection; full Architecture; the new frontend
decoder/hook/panel files; npm build (includes typecheck), lint, changed C# format,
changed-document links, secret/bundle hygiene and git diff --check. Retain
unchanged Application, older controls, fixture suite, client reproducibility,
strict e2e tsc and journey results with input-based reasons. Broaden only for a
changed shared dependency/fixture/e2e input, a failure or a demonstrated gap;
pause for planner discussion if fixing either finding needs shared changes.

Preserve the unexplained unnamed Api 2067/2068 failure, four privilege skips,
source-map-js advisory, original red-first gaps, prior sandbox loading/connection
failures, migration-generator drift and real-CLI/auth/ref/schema limits, plus
all other recorded diagnostics. Later passes prove neither flakiness nor
irrelevance. Complete logs and named results must survive future failures.
Qualification remains Blocked/TargetsNotReal; both allowances remain unused.
No real gh/provider invocation, CLI setup, ledger reset, cleanup or alteration
of historical failures, retained roots or ignored review artifacts is authorized.

## Selected next slice (2026-10-14): explicitly inspect the bound recorded commit's check contexts

### Independently verified publication and priority

ADR-0044 is published as be3f1ce4a9787f26686e1f431dd11b2aacb06910, with exactly
one parent fda2f2ee36729160fced9b4cad222657d248b877 and the reviewed message and
executor trailer. Codex verified main, HEAD/local origin/main/live main equal
that SHA, 0/0, empty index and clean tracked/untracked state. All 48 manifest
paths match the committed 17 M/31 A inventory, normalized modes/blobs and raw
working-file hashes/lengths; ignored manifest SHA-256 remains
8bccfc7c220f8b3ce47a1dc10eced42f6304d62709b2f99f67ea9c594e852f42.
The client remains c417c9ab1e38255858634d19c981d2750a587dac0dd4e0274a9e9d9aa0fffa8e.
No implementation edit, regeneration, validation rerun or closure diff was made.
This verified publication supersedes the pre-publication status in current-work
without rewriting the delivery evidence. Existing planner bytes are preserved.

Roadmap/ADR-0005/0040-0044 and current code distinguish required gates from
optional improvements. Real qualification and remaining Increment 4 usage,
session/compaction and permission contracts; Increment 5 coordination,
multi-project scheduling, lifecycle/recovery and linked replay; Increment 6
policy-scoped publication, check/workflow evidence and required-check policy,
automatic observation, diagnosis/correction and remote reconciliation; packaged
release acceptance and self-hosting remain required. Existing separate stage
supervisors are not an end-to-end coordinator. Extra history/export/analytics,
additional providers and a journal migration remain optional/deferred.

The delivered CI query reports only a rollup, not the identity or conclusion
of any individual check. Select one explicit bounded collection of CheckRun
and StatusContext metadata for the exact bound branch/commit: this supplies
named evidence needed before failed-check diagnosis and required-check policy.
Do not select durable monitoring/publication or repeat a bound-ref-only panel.
Real qualification cannot close under the preserved no-real-invocation boundary;
larger coordination and authority changes need their own bounded contracts.
Prior ideas and the executor's lineage-cache/cloning proposal are not selected.

### Selected additive ADR-0045 contract

Add GetGitHubCheckContextsObservation, protected bodyless MVC GET
/api/runs/{runId}/github-check-contexts-observation via the mediator, full
API-owned DTO graph and regenerated additive client. Fixed 404
github_check_contexts_observation.not_found; fixed pre-dispatch 400
github_check_contexts_observation.body_not_allowed for positive declared length,
any transfer encoding (including empty chunked), or a readable byte. No client
owner, ID, SHA, branch, cursor, limit, credential, executable or raw query input.

Reuse unchanged ADR-0040 source/target and ADR-0043 binding readers, including
orphans, and ADR-0044 admission order/CLI snapshot/host clock behavior. Read
source/target/binding and canonical events freshly before and after exactly one
observer call, no lock/transaction or write; a changed/lost/incoherent scope takes
Unavailable precedence and strips all facts. Independently validate the entire
probe, then compare opaque bound ID and exact recorded owned branch/SHA ordinally.
Keep observed names exactly, with conservative ASCII scope admission ignoring
ASCII case only. Only observation-bearing replies read the post-call clock;
earlier than binding.RecordedAtUtc is Unknown without observation, never clamped.
Cancellation/unexpected faults propagate. No earlier sibling answer is evidence.

Response {runId,status,source,target,binding,observation}; same source/target/
binding shapes as ADR-0044, with API-owned mapping. Closed statuses:

- NotRecorded, Unavailable: all four nullable fact groups absent.
- BindingNotRecorded: coherent source/target only; no process.
- CliUnavailable, Unknown, IdentityChanged: coherent source/target/binding,
  no observation; IdentityChanged only for a whole valid foreign-ID probe,
  without exposing the other ID. Initial refusals invoke nothing.
- RefNotReturned: observation, null ref/totalCount/contexts.
- CommitDifferent: observation with exact branch and different admitted SHA,
  null totalCount/contexts; remote metadata must be withheld, not leaked.
- NoContextsReturned: matching bound ID/branch/SHA, observation ref present,
  null totalCount/contexts because GitHub returned null statusCheckRollup.
- LimitExceeded: matching identity/branch/SHA, observation ref present,
  totalCount > 50, contexts null; no second page/request.
- ContextsReported: matching identity/branch/SHA, ref present, totalCount 0..50
  equal to contexts.Count, contexts an array including an admitted empty array.

Observation is {repositoryNodeId,owner,repository,observedAtUtc,ref,totalCount,contexts}.
Ref when present is {branchName,commitSha}. Each context is the closed flat shape
{kind,id,name,status,conclusion,checkSuiteId}:
CheckRun maps id/name/status/conclusion/checkSuite.id; StatusContext maps
id/context/state into id/name/status with null conclusion/checkSuiteId.
IDs are opaque, conservative 1..256 ASCII letters/digits/_-/+=, ordinal and
never decoded. Repository IDs continue using their unchanged Domain policy;
context/suite identifier and label rules remain operation-owned, not a new
global policy. Reject duplicate entry IDs; repeated names and shared suite IDs
are allowed and preserved in provider order, never sorted, deduplicated by name,
collapsed into a latest attempt or inferred to be required. No workflow-attempt
claim follows from these IDs. CheckRun.status admits exactly COMPLETED,
IN_PROGRESS, PENDING, QUEUED, REQUESTED, WAITING; conclusion is null or exactly
ACTION_REQUIRED, CANCELLED, FAILURE, NEUTRAL, SKIPPED, STALE, STARTUP_FAILURE,
SUCCESS, TIMED_OUT. StatusContext.status admits the ADR-0044 five literal states.
Preserve independent status/conclusion values without inventing undocumented
pairing rules or inferring success from null. Unknown enums refuse the entire
answer as Unknown, never normalize to success or silently omit an entry.

New narrow operation-owned IGitHubCheckContextsObserver and Infrastructure
GitHubCheckContextsObservationAdapter, composed on the existing process engine;
no existing observer/query/adapter/port/policy or their contracts change. Same
scratch directory, stored-default-auth allowlist/fixed controls, 15 seconds,
singleton immediate-refusal slot/finally release, discarded stderr and identified
start failures as ADR-0044. No output sinks/logs/artifacts, shell/jq/retry/cache,
automatic work or global process changes. Only this new adapter captures at most
128 KiB per stream, 256 KiB combined, depth 12: the old 8/16 KiB limits remain
unchanged. This is a bounded collection-specific increase. A planner sizing
probe of 50 maximal ASCII IDs/suite IDs and 256-byte labels escaped as six-byte
Unicode sequences measured 119208 bytes with two-space pretty JSON, below 131072.
Prove caps, truncation and supported maximum values in tests; oversize is Unknown.

Exactly one command with literal -f values, no -F/interpolation:
gh api graphql --hostname github.com --method POST -f query=<constant> -f owner=<stored owner> -f name=<stored repository> -f ref=refs/heads/<recorded branch>

Constant read-only document:
query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid statusCheckRollup{contexts(first:50){totalCount pageInfo{hasNextPage} nodes{__typename ... on CheckRun{id name status conclusion checkSuite{id}} ... on StatusContext{id context state}}}}}}}}}

Admit only exact root data/repository/id/name/nameWithOwner/ref; null ref or
exact name/prefix/target (refs/heads/, ordinal recorded branch); Commit target
exact __typename/oid/statusCheckRollup; null rollup or exactly contexts; contexts
exact totalCount/pageInfo/nodes; pageInfo exactly hasNextPage; nodes exactly the
fields selected for their one known typename, checkSuite exactly id. No missing,
extra/duplicate/escaped property names anywhere, errors/extensions, unknown
typenames, partial/null entries, non-Commit/null target or fallback ref. totalCount
is a nonnegative Int32, nodes length min(totalCount,50), hasNextPage exactly
(totalCount > 50), without duplicates or invalid entries. Null nodes/connection,
inconsistent counts/page flags, unknown enums, malformed JSON/values or stream
failures refuse whole as Unknown. Validate the returned bounded first page even
when it will be withheld for a foreign ID, different SHA or excessive count.

Labels (only CheckRun.name and StatusContext.context) require a distinct text
contract: nonblank well-formed Unicode, 1..256 UTF-8 bytes, no Unicode Control,
Format, Surrogate, LineSeparator or ParagraphSeparator characters, and no U+FFFD.
Decode valid JSON string escapes once and preserve the exact admitted text,
including quotes, backslashes, non-ASCII and inert markup. Reject invalid
surrogates/escapes rather than throwing an unhandled parse fault or replacing
text. All other string values remain literal/unescaped as in ADR-0044. Check
raw property spelling at every object: decoded property names alone are not
proof of literal keys. Do not apply a blanket backslash ban or weaken any old
adapter; the new labels are an additive, separate untrusted-display contract.

Official GraphQL references checked on 2026-10-14:
[Commits](https://docs.github.com/en/graphql/reference/commits) defines the mixed
StatusCheckRollupContext union and connection/page counts;
[Checks](https://docs.github.com/en/graphql/reference/checks) defines CheckRun,
its non-null suite and literal status/conclusion enums. This collection is what
GitHub returned for the commit, not all workflow attempts, required-check policy,
permission or publication authority. No real CLI/schema compatibility is proved.

### Cockpit, scope and evidence

Separate collapsed GitHub check details region beside the existing GitHub commit
checks region, inside an Available target; Show/Check/Recheck/Close. Existing
regions unchanged. Show/mount/reload/reconnect/timer sends nothing. Same full
source/target and opening ownership, unchanged useOwnedLifetime/useOwnedState,
single-flight, same-batch/old callback/close-reopen/A-B-A protections. Pending
or failure clears old observations. Strict decoder repeats source/target/binding,
ID, count/nullability, literal states, label byte/Unicode and time rules. No
storage/effects/remount/reset tricks, background reads, pagination, links/copy
or executable/provider-text interpretation. Show IDs, kind, exact label, literal
status/conclusion and suite when applicable as bounded inert text. Null conclusion
is not returned, never passed; empty collection is zero returned, never CI success.
Render markup-looking names as text. Fixed disclosure: this is the bounded
collection GitHub returned for this commit, not required-check approval, complete
workflow history, permission or publication authority. LimitExceeded shows no
entries and explicitly says the 50-entry bound prevented details; no silent
truncation, omitted malformed siblings, aggregate verdict or approval action.

Add ADR-0045 and necessary index/context/architecture/cockpit/roadmap/security/
current-work updates only. No Domain/schema/migration/event, durable evidence,
required-check classification/branch protection, workflow/job/step/log/artifact
capture, rerun, push/PR/publication, scheduler or autonomous monitoring. No new
dependency, global policy/parser or shared test infrastructure is selected.

Follow validation-policy. Tests before behavior implementation, focused named
red cases, then incremental affected builds. Detailed label/JSON/collection/state
permutations live in operation policy/adapter tests, not expensive host repeats.
Predispatch/auth/404 use no delivered lineage. Coherent cases use existing
feature-owned lineage and real file-backed SQLite/production composition;
within-test scripted read permutations are fine, no caching/cloning or sharing
mutable lineage across tests. Group related read cases within their own test,
retain independent assertions and report direct setup/request timings for any
class over a minute (skeleton differences alone are estimates).

Five serial targeted compiling mutants: bound-ID equality removed; recorded-SHA
gate removed; post-call binding reread omitted; incomplete connection admitted;
escaped-property-name guard removed. Use their named regressions only, restore
verified hashes/rebuild, and distinguish compilation/no-op failures from kills.
Keep complete logs/TRX and named results for every failure, no blind rerun.

Final matrix once after stable build: one serial incremental solution build/
analyzers and reproducible additive NSwag generation without deletion; new
Application GetGitHubCheckContextsObservation tests; full new Infrastructure
adapter class with real process-double caps/argv/environment/slot/cancellation;
new Api family through auth/mediator/SQLite, launch-session auth and real-host
MVC authorization inventory; full Architecture; full ProviderFixtureContractTests
for an additive fixed-query GhRole extension, all other roles/contracts unchanged.
Also run the existing ADR-0044 matching-five-states, different-commit withholding
and binding-drift tests as regression controls; retain unrelated backend suites.
New frontend decoder/hook/panel plus existing target/identity/branch/binding/CI
decoder/hook/panel files; npm run build (includes tsc -b), lint; strict e2e tsc
for the changed journey; one affected authenticated collaboration journey.

The GhRole extension returns mixed nonempty check/status entries with distinctive
fictitious IDs/labels but the unchanged fixed remote SHA. The journey must prove
CommitDifferent with every context/count/label withheld, one request/process per
explicit Check/Recheck including after real reload, none on Show/close/reopen/
delay, and unchanged database/events/Git/Agent/sibling observation counts.
Matching-SHA mixed/empty/limit/null, duplicate-name identity, escaping and malformed
results are proved at adapter, host and UI boundaries, not falsely claimed for
the fixed-SHA journey. No real provider/network is invoked by tests.

Formatter on changed C#, changed-document links, diff, secrets/bundle hygiene
apply. Retain unchanged Domain/migrations/unrelated backend/full Vitest/other
journeys/harness/dependency-audit evidence with applicability reasoning. Broaden
for shared reader/policy/executor/environment/auth/serializer/client config,
uncertain effects or failures; changed harness/transport/auth/other provider roles
requires harness plus canonical test:e2e:all, dependency changes require audits.
Max two demonstrably isolated checks after build; one build/generator/mutation
writer; serialized journeys; no routine clean rebuild or server shutdown.
Use the allowed outside-sandbox test execution when the known transform-file or
VSTest connection boundary prevents tests from starting; retain any failures,
do not silently rerun, increase timeouts or call them flaky.

### Dispatch and limits

One new Claude executor chat for ADR-0045; corrections stay there. Start main at
be3f1ce4a9787f26686e1f431dd11b2aacb06910 for HEAD/local/live main, 0/0, empty
index/no untracked files; only planner-handoff.md unstaged modified by selection,
whose exact hash is supplied in the prompt. Verify once and report drift.
Preserve this planner file byte-for-byte. Update current-work with actual fresh,
retained/not-run checks, timings, failure evidence and limits. Return uncommitted
and unpushed diff for Codex GO/NO-GO. No staging/publication/next-slice authority.
Challenge unsafe or impractical requirements with evidence before dependent work;
unresolved material choices go to owner, without blocking independent work.

Preserve unnamed Api 2067/2068 as unexplained; subsequent passes prove neither
flakiness nor irrelevance. Keep the ADR-0044 review sandbox frontend ENOENT and
VSTest zero-test connection abort, all historical failures, ledger, retained roots
and ignored review/log/TRX artifacts. Four privilege skips, source-map-js advisory,
original red-first gaps, bounded mutation limits, migration-generator drift,
real CLI/auth/ref/schema limits, browser duplicate-key/negotiation and frontend
chunk-size diagnostics remain recorded. Qualification remains Blocked/TargetsNotReal,
both allowances unused. No real gh/provider call, CLI setup/auth/configuration,
ledger reset or cleanup without a separate explicit decision. This advances one
CI evidence boundary; no increment/MVP/release completion is claimed.

## Review decision (2026-10-14): GO for the reviewed ADR-0044 diff

Codex independently reviewed Git, production code, tests, generated-client drift,
documentation and retained evidence. No blocking finding remains. GO authorizes
publication only of the exact reviewed inventory frozen after this insertion:
48 paths (17 modified, 31 added), including current-work and planner-handoff.
No next slice or Increment 6/MVP/release completion is selected or implied.

Verified main; HEAD/local origin/main/live refs/heads/main all equal
fda2f2ee36729160fced9b4cad222657d248b877, 0/0, empty index. Before this insertion
planner SHA-256 remains
2ae289c4b36efb83b09a64289346d1889c33fb4c0d46f78a7240c4028452ccd5.
All prior selection, owner-transition and review bytes remain intact below.
Client SHA-256 independently remains
c417c9ab1e38255858634d19c981d2750a587dac0dd4e0274a9e9d9aa0fffa8e;
the generated diff is additive (214 lines, none deleted), with the executor's
forced NSwag-regeneration evidence inspected. All 47 recorded source hashes
matched the working files before review; no implementation edit was made.

Accepted behavior: whole-response admission (including escaped property keys),
canonical local source/target/binding admission before and after one bounded
remote observation, whole probe validation before classification, ordinal bound
ID and exact branch/SHA gates, no CI state for a different commit, and only the
five literal rollup values for the recorded commit. The owned UI accepts only
coherent status/scope/binding/ref/state/time combinations, removes previous
observations on pending/failure, and preserves explicit-only actions, stale
callback protection and independent opening/source lifetimes. No write, lock,
migration, remote mutation, permission or required-check approval was added.

Executor clarification accepted: the post-call clock is read only when an
observation would be shown; IdentityChanged carries no observation and requires
no observation timestamp. Regressed observation time is Unknown without clamping.
The two architecture inventory changes add only the new GraphQL adapter and
binding-reader caller and do not weaken the old boundaries. The GhRole extension
is additive and fixed-query only; the fixed-SHA journey honestly proves
CommitDifferent with SUCCESS withheld, not matching-SHA CI or real compatibility.

Independent checks (complete logs/TRX under ignored logs/planner-review/adr44):
incremental serial solution build, 0 warnings/errors (4.4 s); new frontend
decoder/hook/panel 58/58; complete new CI adapter 235/235; Architecture 120/120;
four focused real-host API tests 4/4 (23.8 s) proving all five reported states,
bound-ID mismatch, source/target/binding drift after the call and regressed/equal
clock readings. This is focused independent validation, not another full matrix.

Preserved independent review failures: the sandbox frontend attempt passed 20
decoder cases but two suites could not load due to ENOENT for transform files
under its temporary client directory (frontend.log). The identical command
outside the sandbox passed 58/58 (frontend-unsandboxed.log). The sandbox API
attempt aborted because VSTest failed to connect to testhost after 90 seconds:
api.log/api.trx report zero executed tests, not an unnamed failed test. The same
four-test filter outside the sandbox passed (api-unsandboxed.log/.trx). These
outcomes are retained as observed; later passes do not erase them or establish
flakiness. No timeout was increased, source changed, log removed or test skipped.

Inspected executor final TRX: Application 27, Infrastructure 235, Api including
auth and three existing regression controls 30, Architecture 120, full fixture
family 160; no failures/skips in those selected runs. Frontend 242/242, build
including typecheck, lint (nine existing warnings), strict e2e tsc, affected
authenticated journey 1/1, formatter, links and diff checks are accepted from
retained complete logs. Four compiling mutants were killed and source hashes
restored. Red evidence is correctly limited: tests ran against a compiling
skeleton after contracts/endpoint/policy existed; the 21 already-green policy
cases do not gain genuine red-first evidence retroactively. Byte-hygiene flags
were non-ASCII punctuation matching existing copy conventions, not forbidden
English text, CR/NUL or secret additions. No arbitrary ASCII-only rule is imposed.

The reported setup-cost percentage is an estimate from skeleton versus complete
class timings, not direct phase instrumentation or a measured optimization.
No lineage cache, sharing across tests or cloning is authorized. Unchanged
Domain/schema/migrations, unrelated suites, full Vitest, harness, other journeys,
canonical chain and dependency-audit evidence remain retained under the accepted
proportional matrix; no shared executor/reader/auth/harness/dependency changed.
Mandatory CI, security, real-provider and release gates remain intact.

Preserve the unnamed Api 2067/2068 failure as unexplained: later passes prove
neither flakiness nor irrelevance. Preserve four privilege skips, source-map-js
advisory, original red-first absences, bounded mutation limits, migration-generator
drift, real CLI/auth/ref/schema limits, browser duplicate-key/negotiation messages
and the frontend chunk-size advisory. Real qualification stays
Blocked/TargetsNotReal; both allowances unused. Preserve ledger, historical
failures, retained roots and ignored logs/TRX/review artifacts. No real gh/provider
invocation, CLI setup/auth/configuration, ledger reset or cleanup is authorized.

Claude must reverify the frozen manifest, stage only its paths, verify staged
modes/blobs and --check, make one substantive commit with current-work and planner,
normally fast-forward push main, and verify live main in that same executor
turn. Stop on drift/divergence/push failure, with no force, rebase, merge or
history repair. No edits, regeneration, validation rerun, separate closure diff
or next-slice selection is authorized. Codex verifies publication before selecting
the next bounded slice. The full actionable publication prompt is supplied with
this review decision; the ignored manifest remains outside the commit.

## Selected next slice (2026-10-14): explicitly observe the bound recorded commit's check summary

### Verified publication and MVP assessment

ADR-0043 and the owner-requested validation policy are published as
fda2f2ee36729160fced9b4cad222657d248b877, parent
818d67010ce85bc3adb21cab532c88c97ffa1275. Codex independently verified main,
HEAD/local origin/main/live refs/heads/main equal the published commit, 0/0,
empty index and clean tracked/untracked state. All 88 manifest paths match the
commit with rename detection disabled (39 M, four D, 45 A), normalized modes
and blobs and the working files' raw hashes/lengths. The ignored manifest SHA-256
remains a946d6211b12f990f2901c5fd17bb1c59f941378926d743b363481208af47143.
The first sandbox ls-remote failed with getaddrinfo thread startup; the authorized
read-only live query passed. This is a publication check, not a validation rerun.
No closure-document diff, Git mutation, regeneration or test rerun was performed.
The published current-work entry describes its pre-publication review state;
this verified publication supersedes that status without rewriting its evidence.
Generated client SHA-256 remains
d261934c365f6c36bd9eab893a1dd8ad8ed8a48616ec7b93264236b70f2e3126.

The actual roadmap and ADR-0005/0040/0041/0042/0043 still require real provider
qualification and the remaining Increment 4 provider controls; Increment 5
coordination, multi-project scheduling, lifecycle/recovery and linked replay;
Increment 6 policy-scoped push/PR, exact-SHA CI detail and required-check policy,
diagnosis/correction, automatic monitoring and remote reconciliation; packaged
shell/release acceptance and self-hosting proof. These remain MVP/release gates.
Code still composes separate explicit stage supervisors. The source and target
readers admit pinned historical delivery; the binding reader admits its canonical
Human event. The two existing GraphQL adapters observe identity/ref only; no CI
operation exists. No coordinator, publication or qualification is inferred here.
Extra history drill-down/export, analytics, broader providers and a journal
migration are optional/deferred. Prior follow-up ideas are not selections.

Select one read-only CI summary because it directly advances exact-SHA evidence
using the delivered immutable binding and ref admission, without remote mutation
or autonomous dispatch. A separate bound-ref-only operation would repeat much
of ADR-0042 without returning CI evidence. Required-check policy and complete
workflow/check enumeration are deliberately later: an aggregate cannot prove
required-check satisfaction, completeness, permissions or release readiness.
Owner-requested faster validation is applied below, not by weakening boundaries.

### Selected contract: ADR-0044

Add GetGitHubCiObservation, a protected bodyless MVC GET
/api/runs/{runId}/github-ci-observation through the mediator, API-owned full
transport graph and regenerated additive client. Unknown run: fixed 404
github_ci_observation.not_found. Any positive declared body length, any transfer
encoding (including empty chunked), or readable byte: fixed 400
github_ci_observation.body_not_allowed before dispatch; no rejected value echo.
Return {runId,status,source,target,binding,observation}. Reuse the existing source,
target and binding fact shapes through API-owned mapping, never expose Application
types. The closed statuses and nullable graph are:

- NotRecorded: no target and no binding/event; all four nullable facts absent.
- Unavailable: any source/target/binding/event corruption, orphan or lost/changed
  scope; all four nullable facts absent, nothing invoked on initial refusal.
- BindingNotRecorded: coherent source/target, genuinely no binding/event;
  source/target present, binding/observation absent, no process.
- CliUnavailable, Unknown, IdentityChanged: coherent source/target/binding present,
  observation absent. IdentityChanged is only a whole valid probe whose opaque
  ID differs ordinally from the recorded binding; never echo the different ID.
- RefNotReturned: coherent scope/binding and observation, null ref/null rollupState.
- CommitDifferent: same bound ID and exact recorded branch, a different admitted
  SHA; observation ref present and rollupState null, even if remote supplied SUCCESS.
- NoStatusReported: same bound ID/branch/SHA; observation ref present, rollupState null.
- StatusReported: same bound ID/branch/SHA; observation ref present and rollupState
  exactly ERROR, EXPECTED, FAILURE, PENDING or SUCCESS. Preserve that literal value.

Only the final four statuses carry observation
{repositoryNodeId,owner,repository,observedAtUtc,ref,rollupState}; ref when present
is {branchName,commitSha}. Names are the admitted observed names, never rewritten
as target names. Null rollup means GitHub returned no rollup, not no checks exist.
No status is called Passed/Approved/Ready/Green, including SUCCESS. Fixed product
disclosure: this is GitHub's point-in-time aggregate for this commit, not complete
check/workflow evidence, required-check approval, permission or publication authority.

Read the unchanged ADR-0040 source/target and ADR-0043 binding readers before any
process, including binding orphan detection even when source/target is absent.
Require the unchanged fresh successful DirectExecutable GitHubCli snapshot.
Invoke one operation-owned provider-neutral IGitHubCiObserver, independently
validate its entire result, and freshly reread source/target/binding and canonical
events after it returns. Any changed/lost/incoherent fact takes Unavailable
precedence over remote outcomes and strips all scope/remote facts. No transaction,
write lock, persistence or fallback to a cached sibling-panel answer. Validate
names with the existing ASCII policies and ASCII-case-insensitive target match,
opaque IDs ordinally, owned branch ordinally, and 40 lowercase hex SHA. Entire
probe validation precedes identity/SHA classification; malformed states are
Unknown, never a partially admitted mismatch. Stamp admitted observation from
the host UTC clock after work; if it precedes binding.RecordedAtUtc, return Unknown
without observation, never clamp/retry/fabricate a time. Cancellation/unexpected
faults propagate as on ADR-0042.

Infrastructure GitHubCiObservationAdapter is composed on the existing process
executor, singleton with one nonqueued slot released in finally, and preserves
ADR-0041/0042 scratch root, stored-default-auth environment allowlist/fixed
controls, 15-second timeout, 8 KiB per stream/16 KiB combined, discarded stderr,
identified start-failure handling and no output sinks/logs/artifacts. Existing
adapters/ports/queries/statuses are unchanged; do not extract a broad gateway.
Exactly one command, no shell/jq/cache/pagination/retry/fallback:

gh api graphql --hostname github.com --method POST -f query=<constant> -f owner=<stored owner> -f name=<stored repository> -f ref=refs/heads/<recorded branch>

Constant read-only document (variables only, no interpolation):
query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid statusCheckRollup{state}}}}}}

Strict whole JSON only: root data, data.repository, repository id/name/nameWithOwner/ref;
ref null or exactly name/prefix/target; target exactly __typename/oid/statusCheckRollup
for Commit; rollup null or exactly state. Ref prefix must be refs/heads/, name exactly
the recorded branch. Null/non-Commit target, fallback tag/wrong branch, partial,
duplicate/extra/missing members at any level, any backslash in stdout (keys or values),
unknown/nonstring state, malformed JSON/OID/names/ID, truncation, nonzero/timeout
are refused whole. Literal whitespace/member order controls remain admitted.

Official references checked by Codex on 2026-10-14:
[GitHub Commit/StatusCheckRollup/StatusState](https://docs.github.com/en/graphql/reference/commits)
and [gh api](https://cli.github.com/manual/gh_api). Commit.statusCheckRollup is nullable;
its state covers check runs and statuses and has the five values above. It is
not a required-check policy. Keep schema/CLI compatibility with installed real
tools unverified; this selection does not authorize real gh or a provider call.

Cockpit: separate collapsed GitHub commit checks region inside the Available
target, beside existing regions, with Show/Check/Recheck/Close. Show sends nothing;
Check owns one request. It does not require or reuse a sibling's loaded binding;
the server reads that binding each time. Decoder accepts only the exact closed
status/nullability/scope/ID/ref/SHA/state/time combinations. Lifetime is the full
committed source/target and each opening, using unchanged owned lifetime/state
hooks: A-B-A, close/reopen, old callbacks/completions and same-batch duplicate or
close-then-check send nothing/do not affect successors. Pending/failure removes
old observations. No effects/remount/reset tricks, storage, timers, retries,
background requests, links/copy actions or optimistic CI approval. Show exact
source/target/binding identity, observed ref/SHA, UTC time and literal state as
inert text with the fixed disclosure; drift/ref absence reveals no CI aggregate.

Add ADR-0044 and update ADR index, engineering context, GitHub architecture,
cockpit spec, roadmap/security contract and current-work only as necessary.
Preserve planner-handoff bytes supplied at dispatch. No Domain/schema/migration,
write/event, remote mutation, PR/permission/protection/workflow policy, diagnostics,
artifact/log download, scheduler, autonomous monitoring or publication scope.

### Proportional validation and evidence

Development: tests first at the owning boundary; focused named red cases, then
incremental build and affected classes/files. API pre-dispatch/auth/404 cases
need no completed delivery. Reuse existing feature-owned lineage builders for
coherent paths; no mocked database, mutable shared seed/cache/cloning or bypass.
Pure JSON permutations belong to the adapter, not repeated expensive host cases.
Use real file-backed SQLite for admission, event corruption and after-call drift;
at least one full production-command lineage and owned-double journey remains.
Record setup versus execution cost for classes exceeding the one-minute target.
Four serial compiling mutants only: remove bound-ID equality, remove SHA gate,
skip post-call binding reread, admit an ended UI callback. Name killed tests,
preserve compile/no-op failures separately, restore verified source hashes and
rebuild before testing restored binaries. Never relabel stub runs as genuine red.

Run the stable final matrix once, using current built binaries and --no-build
--no-restore for tests. Default one serial incremental solution build/analyzers,
client reproducibility proof without deletion, new Application CI handler tests,
new Infrastructure CI adapter class (including real process-double execution),
new Api GitHubCiObservation family, full Architecture and launch-session auth.
Regression API controls: ADR-0041 An_observed_identity_is_returned_exactly_with_the_scope_the_vetted_path_the_stored_names_and_no_write;
ADR-0042 A_remote_tip_equal_to_the_recorded_commit_is_commit_matched_with_the_exact_ref_and_no_write;
ADR-0043 The_read_returns_the_same_recorded_graph_and_never_observes_or_writes.
Include a real-host MVC authorization inventory with no anonymous additions.
For the additive owned GhRole fixed-query extension run the full
ProviderFixtureContractTests family; leave every other role/command unchanged.
It may return the existing fixed remote SHA: the authenticated collaboration
journey must prove CommitDifferent with CI withheld, exact one request/process
per explicit check/recheck, none on Show/close/reopen/real reload/delay, unchanged
database/events/Git/Agent counts. StatusReported for all five states, null rollup,
identity mismatch and exact-SHA drift are proved at adapter, host and UI boundaries;
do not fabricate a real remote or weaken the journey's fixed SHA.

Frontend final: complete new decoder/hook/panel files and existing identity,
branch and binding decoder/hook/panel files (composition regression); npm run
build includes tsc -b, npm run lint; strict e2e tsc when journey changes:
npx tsc --ignoreConfig --noEmit --strict --target es2023 --module esnext --moduleResolution bundler --skipLibCheck --noUnusedLocals --noUnusedParameters --verbatimModuleSyntax --allowImportingTsExtensions --erasableSyntaxOnly --types node e2e/*.ts e2e/journey/*.ts e2e/harness/*.ts playwright.config.ts playwright.journey.config.ts
Run the affected collaboration.journey.ts once after binaries stabilize.
Formatter on changed C#, changed-document links, git diff --check and secret/
production-bundle checks apply; no new dependency or lockfile change is selected.

Retain unchanged Domain, migrations, unrelated Application/Infrastructure/Api,
full Vitest, other journeys, harness and dependency-audit evidence from ADR-0043
with its correction applicability, rather than claiming fresh full runs. Broaden
for shared executor/environment/policy/reader changes, host/auth/serializer/client
configuration, uncertain effects or validation failures. Shared journey harness,
transport/authentication or other provider-role changes require harness plus
canonical test:e2e:all; GhRole-only additive contract is covered by its full fixture
family and the affected journey. Dependencies require fresh relevant audits.
Discuss changed scope/matrix with planner before dependent work. Preserve mandatory
CI, security, real-provider and release gates; local selection proves none of them.
No blind reruns or deleted logs. Keep exact commands/filters, times, source hashes,
complete logs/TRX and named failures in ignored test-results/adr-0044.

### Authority, preserved limitations and return

New executor chat for ADR-0044; review corrections stay in that chat. Expected
start: main at fda2f2ee36729160fced9b4cad222657d248b877 for HEAD/local/live main,
0/0, empty index, no untracked files, only planner-handoff.md unstaged modified
by this selection. Its exact dispatch hash is supplied in the prompt. Verify once
before editing; report drift. Do not stage, commit, push or select another slice.
Present uncommitted/unpushed diff and actual current-work evidence for Codex review.
Evidence-based challenges are welcome; pause dependent work on a material dispute,
continue independent authorized work and take unresolved material choices to owner.

Real qualification remains Blocked/TargetsNotReal, both allowances unused. No real
gh/provider call, CLI setup/auth/configuration, ledger reset or cleanup. Preserve
ledger, retained roots, ignored review artifacts/logs/TRX and historical failures.
The unnamed Api 2067/2068 failure remains unexplained; later passes prove neither
flakiness nor irrelevance. Preserve four privilege skips, source-map-js advisory,
original red-first absences, bounded mutation limits, migration-generator drift,
real CLI/auth/ref compatibility limits and recorded browser/build diagnostics.
This is one explicit aggregate observation, not Increment 6/MVP/release completion.

## Review decision (2026-10-14): GO for corrected ADR-0043 and validation policy

All four findings and the residual R3 stale-read issue are accepted as corrected.
This supersedes the NO-GO records below for the reviewed combined diff only.
Claude remains executor; no next slice or MVP/release completion is granted.
Verified main, HEAD/local origin/main/live refs/heads/main equal
818d67010ce85bc3adb21cab532c88c97ffa1275, 0/0, empty index, exactly 88
individual changed files: 39 modified, four deleted and 45 untracked. Before
this GO insertion planner SHA-256 remained
9ab81963c2515148aa55df8f9ab8c7738a2ed91fd976072147b1e1036e1a3fab;
all prior selection, owner transition, policy and review bytes remain intact.
Client independently remains
d261934c365f6c36bd9eab893a1dd8ad8ed8a48616ec7b93264236b70f2e3126.

Final production correction is only the unsuccessful-write load settlement
and its explanatory comment. It now clears the previous reading, keeps the
outcome message and recovering gate, and preserves candidate cleanup, callback
epochs, lifetime ownership and single flight. Coherent success still displays
Available; recovery still requires explicit Reload and a new settled Check.
Six genuine new red cases failed before the fix (four hook/two panel), beside
37 passing controls; two older expectations of stale readings were corrected.
Restoring the old load settlement is a compiling mutant killed by eight cases;
the restored hook hash independently matches both retained hashes:
50ec78951bc1ef602780c7b0f51537197f2c55902c274719e7e777e069f7182d.

Codex independently ran the full binding decoder/hook/panel 85/85 and both
public-hook probes 1/1 each. The original probe verifies recovery order; the
separate probe verifies clearing the read after an ambiguous write. Complete
logs remain under ignored logs/planner-review/adr43/final-review*. Earlier
independent correction evidence remains accepted: incremental serial rebuild
zero warnings/errors, Architecture 114/114 and four names/clock API cases 4/4.
No new .NET, full frontend or browser run is claimed. Executor round-two
frontend build (includes typecheck), lint and hygiene evidence was inspected;
the build's chunk-size advisory and existing lint warnings remain visible.
Original complete evidence and affected correction evidence retain their actual
boundaries as current-work records: initial Domain 1430, Application 5505,
Infrastructure 2278 plus four skips, Api 2155, Architecture 113 and Vitest 3655;
round one binding API 60, Application persistence 7, Architecture 114,
frontend 146 and affected journey 1; round two frontend 85 and both probes.
Unchanged coverage is retained, not represented as rerun after every correction.

The owner-requested validation policy, AGENTS routing and slice-start template
are included in this substantive reviewed diff. Missing original genuine
red-first evidence remains a limitation, despite genuine correction regressions
and bounded mutation evidence. The shorter cockpit label and pre-existing
AbandonedAtUtc converter/snapshot generator drift remain accepted as documented;
no unrelated correction or compatibility claim is made. Identity binding is
historical evidence only, not permission, branch/SHA proof, CI success or
publication authority.

Publication GO is limited to the frozen 88-entry manifest supplied with the
complete executor prompt. Stage only its paths, including the four deletions
and the owner-authored policy/handoffs. Verify staged modes/blobs and exact
add/modify/delete inventory with rename detection disabled; commit the reviewed
substantive slice once, normally fast-forward push main, and verify live main
equals the delivered SHA in the same executor turn. Material content drift,
commit mismatch, push failure or remote divergence stops publication; no
force-push, rebase, merge or history reconciliation. No implementation edit,
regeneration, validation rerun, separate closure diff, cleanup or next selection.

The unnamed Api 2067/2068 failure remains unexplained; later passes establish
neither flakiness nor irrelevance. Preserve all other recorded limitations,
historical failures, complete logs, named results, ledger, retained roots and
ignored artifacts. Qualification stays Blocked/TargetsNotReal, both allowances
unused. No real gh/provider invocation, CLI setup, ledger reset or cleanup is
authorized. No separate closure publication is needed.

## Correction review (2026-10-13): R1/R2/R4 accepted; NO-GO for residual R3 read state

Verified main, HEAD/local origin/main/live refs/heads/main
818d67010ce85bc3adb21cab532c88c97ffa1275, 0/0, empty index and 88 individual
entries (39 modified, four deleted, 45 untracked). The submitted planner hash
remained adef1a246f292bc2bce2b3f5d607612be87334d7f8aac8e240fd2cdd12630c85
before this insertion; all earlier records and owner transition remain verbatim.
Client remains byte-identical at
d261934c365f6c36bd9eab893a1dd8ad8ed8a48616ec7b93264236b70f2e3126.

R1 is accepted: admitted observer names are retained through the locked scope
recheck and recorded exactly in row/response/event/GET/replay, while the target
keeps its selected names. R2 is accepted: a regressed second clock reading now
refuses with no insert, with equality and ordered controls. R4 is accepted:
only the new contracts moved to their selected ownership folders, with a new
architecture guard and unchanged wire graph. The new genuine red regressions
and targeted compiling mutations are accepted as correction evidence; the
absence of original genuine red-first evidence remains a separate limitation.

The R3 recovery order is also accepted. Codex independently repeated its
original public-hook probe: it now passes, armedAfterReload=false and no second
POST without the new check. But the previously selected clearing of both read
and candidate is incomplete: write settle still sets load to
load ?? previous.load. Following an ambiguous write the earlier NotRecorded
reading stays visible, so the panel can assert that no identity is bound when
the host may have recorded it. The hook documentation and ADR-0043 explicitly
say that the read and candidate are cleared after failed/refused/ambiguous
writes. This is the remaining part of R3, not a new slice or authority choice.

An independent separate public-hook probe records loadKind=result and
readingKind=NotRecorded after a lost-response write and fails the required
idle/unloaded reading assertion. Original order probe/config/logs are preserved;
new probe/config and complete named failure log are under ignored
logs/planner-review/adr43/binding-failed-read.test.tsx,
probe-failed-read.config.ts and correction-failed-read-probe.log. Clear only
the stale reading on every failed/refused/ambiguous/incoherent write settlement;
preserve the failure/outcome message, recovering gate, candidate cleanup and
ownership guards. An explicit reload supplies the next authoritative reading.
A coherent successful write must still show its Available binding.

Independent checks: incremental serial rebuild zero warnings/errors, full
Architecture 114/114, four corrected name/clock API cases 4/4, passing original
recovery-order probe and the one failing read-state probe. Executor correction
red 2 failed/2 controls passed (API), four reversed-order frontend failures,
compiling mutation kills/restored hashes, complete binding API 60/60,
Application persistence 7/7, frontend 146/146, frontend build/lint and affected
journey 1/1 were inspected. Reviewer logs/TRX remain under ignored
logs/planner-review/adr43. No independent full matrix or resolved historic
failure is claimed.

Final correction scope is frontend failure-state clearing and its hook/panel
regressions, plus current-work and necessary contract wording. Require genuine
red-before-fix for stale read/display, positive successful-write/ordered-recovery
controls, one targeted compiling cleanup mutant with restored hash, the full
binding decoder/hook/panel tests, frontend build (includes typecheck), lint,
both independent probes, edited links/secrets/diff checks. Assert no automatic
read or extra POST and no retained old NotRecorded statement after an unknown
outcome. Do not rerun .NET builds/suites, migrations, full Vitest, harness,
strict e2e tsc or journeys for this isolated error-settlement change; their
relevant covered inputs/branches remain unchanged. Broaden if the actual diff
changes that boundary or leaves a material concern, with planner discussion.

Keep this correction in the same executor chat, uncommitted and unpushed. No
staging, publication, closure diff, new slice, backend/schema change, real
gh/provider invocation, CLI setup, ledger reset or cleanup is authorized.
Preserve policy files, complete logs, named failures, historical failures,
ledger, retained roots and ignored artifacts. Qualification remains
Blocked/TargetsNotReal; both allowances unused. The unnamed Api 2067/2068
failure stays unexplained; later passes prove neither flakiness nor irrelevance.
Evidence-based challenges remain welcome; unresolved material choices go to owner.

## Review decision (2026-10-13): NO-GO for the submitted ADR-0043 diff

The slice remains uncommitted and unpushed. This is a correction of ADR-0043 in
the same executor chat, not a next-slice selection. Verified main; HEAD/local
origin/main/live refs/heads/main equal
818d67010ce85bc3adb21cab532c88c97ffa1275, 0/0, empty index. Before this review
insertion the planner remained byte-identical at
3fb2ad95fe1e4f391324cd9cdd13813f356b4c01a4b4ed7d577cc91d5148386a;
every previous selection, owner transition and review record is retained below.
Working inventory is 88 individual entries: 39 modified, four deleted and 45
untracked files. The reported 43 tracked changes includes four deletions; its
35 untracked paths counted directories, rather than all individual files.
The generated client independently matches
d261934c365f6c36bd9eab893a1dd8ad8ed8a48616ec7b93264236b70f2e3126.

R1 (P2): BindGitHubRepositoryIdentityCommandHandler discards the admitted
observer result, keeps only a boolean, then persists scope.Target.Owner and
Repository. With observed OCTO-CAT/Hello.World it records octo-cat/hello.world.
The current API case explicitly asserts that wrong behavior; Codex ran it
independently. The selected contract and ADR-0043 describe observed names, not
copies of the local selection. Keep ASCII-case-insensitive scope admission,
retain the validated identity through the locked recheck, and persist/reply/
event exactly its reported names. Reader/replay may compare them to the target
ignoring ASCII case, but must retain the recorded observation without rewriting.
Update the wrong expectation and capture genuine correction red-first evidence.

R2 (P2): the handler replaces a regressed recordedAtUtc with observedAtUtc.
That fabricates a recording timestamp to satisfy the Domain invariant; it is
neither the returned host clock fact nor the selected refusal of incoherent
ordered times. On recorded < observed return the fixed Unavailable refusal,
with no binding/event insert. Do not clamp, reobserve or retry. Add an injected
clock regression against the current handler before fixing it, plus equality
and normal ordered controls. Target recorded <= observed remains required.

R3 (P2): after a failed/refused/ambiguous write, a Check before the required
recovery Reload populates the candidate. Reload retains it, clears write failure
and rearms a new callback, so a second POST is sent without a new Check after
Reload. The independent public-hook probe reproduced armedAfterReload=true
and postCount=2, failing the required recovery-order assertion. The submitted
three failure tests explicitly authorize that wrong Check -> Reload sequence.
Enforce Reload -> new settled Check -> Bind for recovery, and clear invalid
read/candidate state. A pre-reload check or retained callback cannot satisfy
the new candidate requirement. Preserve no automatic reads/retries and all
scope/opening/revision/in-flight ownership rules; change neither host authority
nor ordinary observation semantics. Capture genuine regression evidence for
refusal, network/server failure and incoherent success with negative reversed
order/old callback cases and positive ordered recovery.

R4 (P2): new GitHubRepositoryIdentityBindingView (Application) and
GitHubRepositoryIdentityBindingResponse (Api) were introduced directly under
Features/Runs, although it already contains operation folders. The routed CQRS
standard requires a feature-wide contract in an explicit responsibility folder,
not a new root type. Move only these new types: the Application view to the
existing Policies/GitHubObservation ownership area; the shared API binding DTO
to the GetGitHubRepositoryIdentityBinding operation (or a justified explicit
response-contract area). Update references, preserve wire behavior and add a
narrow architecture guard. Do not refactor legacy root types in this correction.

Independent review: serial incremental rebuild zero warnings/errors; current
Architecture 113/113; two focused API cases 2/2 (the wrong-name storage control
and the genuine independent-writer-during-observation proof). No independent
full matrix is claimed. The separate hook probe has one genuine behavioral
failure; its initial external-test mock-import setup failure was corrected and
retained separately, never counted as a code defect. Complete reviewer build,
TRX and probe source/config/logs remain under ignored logs/planner-review/adr43.
Executor final named TRX counters, frontend/canonical logs, mutation result
and restored source hashes were inspected; passing tests do not establish the
missing behaviors above. Missing original genuine red-first evidence remains
an explicit limit (the stub runs occurred after implementation), not something
that can be reconstructed or relabeled now. New correction regressions can
and must be red before their fixes.

Accepted deviations: the shorter GitHub identity binding region is clear and
avoids the existing region's substring selector; no material product/authority
choice is raised. The new migration only adds its table; the documented
AbandonedAtUtc converter/snapshot generator drift is retained without an
unrelated fix. Older migration compatibility lists and event-header summaries
may add the new table/type without widening payload disclosure. These are not
GO and do not remove other recorded limits.

Correction validation decision, explicitly replacing routine repetition of the
original complete matrix for this round: new affected regressions first and
bounded targeted compiling mutations (names, time clamp, recovery order), then
one incremental serial solution build, the complete binding API family
(Endpoint/Corruption/Durability), the binding Application persistence class,
binding decoder/hook/panel and changed event-summary/history tests, full
Architecture, frontend build (includes typecheck) and lint, strict e2e tsc if
e2e input changes, and the affected authenticated collaboration journey.
Verify regenerated client reproducibility/wire stability, scoped formatter,
edited links, secrets and diff hygiene. No full Domain, unrelated Application,
Infrastructure/migration, full Vitest, harness, other browser workflows or
dependency rerun is required unless the actual correction changes their
covered inputs or leaves a material concern; retain exact original results
with reasons. Schema/Domain/migration changes are not selected and require
discussion. Broadening remains mandatory when evidence warrants it.

Update current-work with the corrections, actual fresh/retained evidence and
all original evidence limitations. Preserve this planner revision and the
owner-authored validation policy files. No staging, commit, push, closure diff,
new slice, real gh/provider, CLI setup, ledger reset or cleanup is authorized.
Qualification stays Blocked/TargetsNotReal, both allowances unused. Preserve
ledger, historical failures, retained roots and ignored artifacts. The unnamed
Api 2067/2068 failure remains unexplained; later passes prove neither flakiness
nor irrelevance. Future failures require complete logs and named results.
Evidence-based challenges remain welcome; stop dependent work for material
disagreement, and unresolved material choices go to the owner.

## Owner-requested decision (2026-10-13): proportional validation for future slices

The owner explicitly requested faster testing for the next slices. Adopt
[the slice validation policy](validation-policy.md), linked from AGENTS.md and
the slice-start template, for future planning and review. This replaces the
planner's routine universal full-suite lists with an explicit boundary/filter,
final regression, broadening-trigger and retained-evidence matrix. It follows
the engineering contract's narrowest-boundary rule and changes no mandatory
CI, security, real-provider fidelity or release/qualification gate.

Evidence: the last isolated parser correction's adapter red/green/mutant
classes each took about one second, while full Infrastructure took 4m58s and
Api 6m18s; clean build 45s and the affected journey 58s. In the current binding
slice, focused Domain 19 took 80ms and Application 7 took 3s, while the 48-case
API class took 3m54s and 410 migration tests 1m01s. Its API setup performs a
complete production local delivery and starts two hosts for a prepared scene.
The initial API run had four failures and the first two journey attempts failed;
those retained development failures explain additional runs, not flakiness.
No current delivery/review judgment or new performance measurement is claimed.

Future default: focused incremental edit/test cycles; named regression filters
for red-first and bounded serial mutations; one proportional final matrix;
retain passing evidence for unchanged coverage. Use complete relevant projects
for shared/transversal changes or uncertain impact, rather than all projects
for every local edit. Avoid redundant clean builds, generation, typechecks and
reviewer reruns. Only demonstrably isolated checks may run concurrently after
a stable build (maximum two commands); never overlap source mutations/builds
against shared outputs. New API refusal tests must not build a complete Git
delivery unless that boundary needs it. Preserve representative production
lineage, real SQLite constraints/atomicity, authorization and independent
assertions. Any fixture/cache/clone optimization needs a separately bounded
implementation and isolation evidence; none is implemented by this decision.

The active ADR-0043 slice and its already requested final acceptance checks
remain unchanged. Do not interrupt active validation, rerun passing checks to
validate these documentation edits, or change its fixtures under this request.
This intentional planner update supersedes only the old byte-identical planner
preflight expectation: the preceding 85b9c327...71d8 selection and every earlier
record/owner transition are retained verbatim. The owner-authorized policy
files (AGENTS.md, validation-policy.md, slice-start-template.md and this planner
record) join the current uncommitted diff for the next review; no separate
publication/closure round and no GO, staging, commit or push is granted.

Verified main and HEAD/local origin/main remain
818d67010ce85bc3adb21cab532c88c97ffa1275, 0/0, empty index. At the start of this
policy update the active executor working inventory was 84 entries: 36
modified, four deleted and 44 untracked. It is ongoing implementation, not a
frozen publication inventory. All source changes remain executor-owned.

Keep complete logs, named failures and phase durations. The unnamed Api
2067/2068 failure remains unexplained; later passes prove neither flakiness nor
irrelevance. Preserve all other recorded limitations, ledger, historical
failures, retained roots and ignored artifacts. Qualification remains
Blocked/TargetsNotReal with both allowances unused. No real gh/provider call,
CLI setup, ledger reset or cleanup is authorized. Profiling targets are not
timeouts or permission to skip checks; claim speedups only after measurement.

## Selected next slice (2026-10-13): explicitly bind a recorded target's repository identity

Publication of the identity-parser correction is independently verified at
818d67010ce85bc3adb21cab532c88c97ffa1275, parent
a634e35499ff5a5dd0bdc84b074afb0529947180. Main, HEAD, local origin/main and
live refs/heads/main agree, ahead/behind is 0/0, index and tracked/untracked
state were clean before this selection. Commit message and reported Claude
Sonnet 5.5 co-author match. The frozen manifest SHA-256 remains
e6dded429be83adc51f5651e9b46e76bfb52d8556d80dcc13c27e6ee69bc2109;
all four modified paths, modes, normalized blobs, raw hashes and byte counts
match. No closure diff, new validation or resolution of prior failures is
claimed. Published planner hash
daae6f3cef829fc0982b2fed44c5d66b61adff9600149bb5aafee9539171ce70
and all its prior records/owner transition are preserved verbatim below.

Decision: select one explicit, immutable, run/target-scoped repository identity
binding (new additive ADR-0043) as the next Increment 6 prerequisite. Code and
accepted ADR-0003/0005/0040/0041/0042 provide pinned local scope, a strict
identity observer, short serialized database writes and owned UI lifetimes;
no durable remote identity binding exists. The published escape correction
closes the observed parser prerequisite. Binding now advances the existing
GitHub/CI architecture's required correlation foundation more directly than
optional history/export work. Exact-SHA CI or publication would otherwise
outrun this missing contract; real qualification remains separately blocked.
No material owner choice is unresolved within this narrow contract. Claude is
executor, with a new executor chat for this slice; Codex owns review/selection.

MVP/release gates still include Increment 4 real qualification and remaining
provider usage/session/compaction/permission controls; Increment 5 end-to-end
coordination, multi-project scheduling, lifecycle/recovery and complete linked
replay; Increment 6 exact-SHA CI, scoped publication and reconciliation; and
Increment 7 packaged self-hosting proof. Existing local delivery and shell
implementation do not close those gates. Extra drilldown, export, analytics,
broader providers and a journal migration remain optional/deferred. This
binding is historical identity evidence, never permission, CI, branch/SHA
proof, publication authority, project-wide alias or a promise of future state.

Selected contract:
- Protected POST and bodyless GET /api/runs/{runId}/github-repository-identity-binding,
  one MVC action per operation, mediator commands/queries and API-owned full
  transport graph. BindGitHubRepositoryIdentityCommand is a manual-transaction
  command. The POST accepts exactly operationId, commitSha, targetId and
  repositoryNodeId, bounded to 8 KiB, with unknown/duplicate members refused
  before dispatch. Fixed invalid-input errors never echo rejected values.
- The source and target are resolved by the unchanged ADR-0040 readers, not
  current HEAD, origin, latest rows, submitted names or browser observations.
  The request must equal the exact recorded operation, commit and target ID.
  Unknown run: github_repository_identity_binding.not_found (404); invalid
  body/values: .invalid (400); absent/incoherent/mismatched local scope:
  .unavailable (409); coherent existing binding with another node ID:
  .conflict (409). No refusal inserts a binding/event.
- For a missing binding require ADR-0041's same fresh successful direct CLI
  snapshot and invoke the existing identity observer once, with no transaction
  or write lock held. Keep its query, argv, environment, caps, slot, strict
  parsing and fault/cancellation behavior unchanged. Independently validate
  node ID and names; names must equal the target ignoring ASCII case only, and
  node ID must equal the request ordinally. Unusable CLI is .cli_unavailable;
  null/invalid observation is .observation_unknown; valid different node ID is
  .identity_changed (all fixed 409, no remote values in errors). Stamp the
  observation with the host UTC clock after validating and re-reading scope.
- Enter one short transaction after external work (or directly for a replay),
  first taking the existing no-op Run write lock, then freshly re-read source,
  target and binding/event coherence. Require the same entire scope as before
  the call. Same coherent binding/node ID replays its original record, event
  and times without another observation or insert; a different binding
  conflicts. Concurrent calls may contend at the existing observer slot, but
  cannot create multiple bindings/events. No retry/queue or external call
  belongs inside a transaction. Failed save/cancellation before commit rolls
  back both inserts; post-commit notifications are hints, as ADR-0040.
- New GitHubRepositoryIdentityBinding table github_repository_identity_bindings:
  host-generated Id and EventId; RunId, ProjectId, LocalCommitOperationId,
  GitWorkspaceId, ObservationTargetId; pinned CommitSha and BranchName;
  RepositoryNodeId and observed Owner/Repository; ObservedAtUtc and
  RecordedAtUtc. Fixed github.com/CiObservation, not caller-controlled columns.
  Unique target/run/operation/event, no global node-ID uniqueness. Restrictive
  foreign keys to target/run/project/operation/workspace; retain with history,
  no update, replacement, deletion, backfill or cleanup path. UTC times must
  satisfy target recorded <= observed <= binding recorded. One additive
  migration creates only this table/indexes/FKs, without rebuilding events or
  changing existing rows/schema. The event ID remains logically validated,
  not a principal-key migration of events.
- Exactly one version-1 Human event run.github_repository_identity_bound,
  atomically saved with the row, same recorded time, no Agent attribution.
  Canonical UTF-8 payload <= 4 KiB carries version, bindingId, targetId,
  operationId, commitSha, branchName, host, repositoryNodeId, owner, repository
  and observedAtUtc. Reader and replay reconstruct/compare exact canonical
  text and event shape; missing, duplicate, foreign or contradictory rows or
  events are Unavailable, never repaired. Bound every stored string by bytes
  before materialization, classify unknown actor forms in SQL, use bounded
  untracked projections, and propagate genuine faults/cancellation.
- GET is persisted-only, no lock/write/process. Refuse declared positive
  length, any transfer encoding (including empty chunked) or readable byte
  before dispatch: .body_not_allowed (400). Return runId, state, source,
  target, binding. Closed states: NotRecorded (binding null; scope pair only
  if coherent target exists), Unavailable (all three null), Available (exact
  coherent source/target and binding). Orphan binding rows/events are
  Unavailable, never NotRecorded. Binding wire facts: id, eventId,
  repositoryNodeId, owner, repository, observedAtUtc, recordedAtUtc. Successful
  POST returns runId/source/target/binding with that same recorded graph.
- Promote only the existing identity port/result unchanged to Application
  Features/Runs/Ports and the opaque-ID policy unchanged to Domain's owning
  project policy area for genuine shared ownership. Update references and
  narrow architecture expectations, proving ADR-0041/0042 outputs unchanged;
  no new observer, broad gateway or shared orchestration. Existing observation
  endpoints remain ephemeral and ungated by this new record.
- A separate collapsed identity-binding region beside the identity/branch
  regions offers Show, explicit Load/Reload, explicit Check/Recheck candidate
  via ADR-0041 and separate Bind this repository identity, then Close. Binding
  is armed only by a settled NotRecorded read with exact scope and a coherent
  Observed candidate; show exact source/target/node/names and immutable/historical
  disclosure before submission. POST reobserves; the browser supplies no proof
  or timestamp. No input/node default from another panel, origin or storage.
  Every action shares a single in-flight guard. Scope/opening/candidate revision
  own callbacks and results: recheck/reload synchronously disarm old callbacks
  even A-B-A/same-batch/same-millisecond cases; close/reopen and old settlements
  cannot act on successors. After any failed/refused/ambiguous write, require
  explicit Reload and a new candidate check before another write. No automatic
  read, write, retry, polling, remount/reset effect or browser storage. GET and
  replay show recorded history, not current verification; inert text/UTC only.

Sizing decision: the planner's retained maximum-value serialization probe
produced 2100 UTF-8 bytes with a supported 256-character '+' node ID,
maximum names and the longest owned branch/time. The initial 2 KiB event
cap was insufficient; it is revised to 4 KiB before dispatch, preserving
all supported IDs and byte guards. The failed named sizing result remains
under ignored logs/planner-review/adr42/binding-payload-bound.jsonl.

Evidence: red-first tests for new behavior, Domain invariants, file-backed
SQLite atomicity/replay/concurrency/corruption and byte guards, migration
upgrade/reopen/no-backfill/restrictive-FK proof, both protected API contracts,
architecture, frontend whole-answer admission and lifetime races. Prove a
second database writer can proceed while the observer is held, not just that
a transaction property is null. Bounded compiling mutations must detect
node-ID mismatch, missing post-call locked recheck, event incoherence and stale
candidate callbacks, with hash-verified restoration. Extend the authenticated
production collaboration journey with explicit load/candidate/bind, exact two
additional identity invocations, one row/event, replay/reload without remote
reads, reopen and actual browser reload; retain original no-other-effect
assertions. Full serial clean build and reproducible generated client, all
five .NET suites, Vitest/typecheck/lint/build, exact strict e2e tsc, harness and
one canonical test:e2e:all; formatter, links, diff/secret/dependency checks.
Retain complete logs and named results for every future failure; an abort's
active test does not establish cause. Report actual fresh/retained evidence.

Only ADR-0043/index/context, architecture/cockpit/security/roadmap/current-work
updates needed for this contract belong with implementation; preserve this
planner edit byte-for-byte. Material challenges or broader changes stop
dependent work for planner discussion; unresolved material choices go to owner.
Planning grants no staging, commit or push authority. No remote mutation,
branch/SHA/CI approval, reconciliation, polling, provider setup or qualification
run, scheduler, permissions, budget/claim/approval/receipt change or dependency.
Preserve the unexplained Api 2067/2068 failure and all other recorded limits;
later passing runs prove neither flakiness nor irrelevance. Qualification stays
Blocked/TargetsNotReal, both allowances unused. Preserve ledger, historical
failures, retained roots and ignored review artifacts. No real gh/provider
invocation, CLI setup, ledger reset or cleanup without a separate explicit
decision; no separate closure publication is needed.

## Review decision (2026-10-13): GO for the ADR-0041 identity escape correction

The bounded correction is accepted for publication. Claude remains executor;
no next slice or MVP/release completion is granted. Verified main with HEAD,
local origin/main and live refs/heads/main equal to
a634e35499ff5a5dd0bdc84b074afb0529947180, 0/0, empty index, no untracked
files and exactly four modified files: this planner record, current-work,
GitHubRepositoryIdentityAdapter and its tests. Before this GO insertion the
planner hash remained
5ffabd94b21d8d4951538b9abea15d99969edeb84d4e218bcedf963673da76fb;
all prior selection, owner transition and review bytes are preserved below.

The production diff is only the six-line pre-parse backslash guard; no query,
argv, environment, bounds, slot, fault/cancellation, API, frontend, persistence
or authority change exists. Five public-observer regressions were genuinely
red (5 failed, 118 passed), green 123/123 and killed the compiling guard-removal
mutant (5 failed, 118 passed). The no-match sed attempt is not counted.
Restored source SHA-256 independently matches both recorded hashes:
c061f5a6b921f6826f48ecc435fcdc6c28cb02867be7524f7be13b08afbd6efd.
Codex rebuilt serially (incremental, zero warnings/errors), ran both full
observer adapter classes 311/311 and full Architecture 106/106, and repeated
its original compiled-parser probe: all five escaped keys and the escaped
value are refused; baseline, literal whitespace/order and restoration pass.
Complete reviewer logs, TRX and probe results remain under ignored
logs/planner-review/adr42 (identity-correction-review and corrected probe).

Codex inspected the executor's retained red/green/mutant TRX and named cases,
clean serial build log, full Infrastructure 2274 plus four skips, Api
2098/2098, Architecture 106/106 and affected collaboration journey 1/1.
The generated client independently remains byte-identical at
8d2108a7909de924312790975c3dc02723a8c56547a05e8e213d90d13413059b.
Codex did not repeat full Infrastructure, Api or browser validation; the
executor evidence and explicitly retained unchanged boundaries are accepted.
Existing journey duplicate-key and stopped-negotiation diagnostics are
visible in the retained log; the journey passes, and no new causal inference
or resolution of earlier limitations is claimed.

Publication authority is limited to the frozen four-file manifest supplied
with the complete executor prompt: stage those paths, verify modes/blobs,
commit the reviewed correction and handoffs together, normally fast-forward
push main, and verify live refs/heads/main equals the delivered commit in
the same turn. Material content drift, push failure or remote divergence
stops publication; no force, rebase, merge or history reconciliation. No
implementation edits, regeneration, closure diff, cleanup or next selection.

The old unnamed Api 2067/2068 failure stays unexplained; later passes establish
neither flakiness nor irrelevance. Preserve every recorded limitation,
historical failure, ledger, retained root and ignored artifact. Qualification
remains Blocked/TargetsNotReal with both allowances unused; no real gh/provider
invocation, CLI setup, ledger reset or cleanup is authorized. Future failures
require complete logs and named results. No separate closure publication.

## Selected next slice (2026-10-13): close ADR-0041 escaped-property admission

ADR-0042 publication is independently verified at
a634e35499ff5a5dd0bdc84b074afb0529947180, parent
7ccbcafb9295f33144b636ad7912d44cb2862d7e. Main, HEAD, local origin/main and
live refs/heads/main agree, ahead/behind is 0/0, index and tracked/untracked
state were clean before this selection. The commit message is "feat: observe
recorded GitHub branch tip" with the reported Claude Sonnet 5.5 co-author.
The frozen manifest SHA-256 remains
3b4cfb463b4bfdb22781f3dfb53d4a0d2bdf0169060815ac9a2b55cda7fdd4be;
all 44 paths (17 modified, 27 added), change types, modes and normalized blobs,
and all working-file raw hashes and byte counts match the reviewed manifest.
No publication discrepancy, closure diff or new validation claim is recorded.
The published planner hash was
f700064ccbc2fcdee9d5797f0f6a5de82948d137f1aff247b3a099054c0f16a9;
its owner transition, prior selections and review records remain verbatim below.

Assessment: Increment 4 real qualification and remaining provider usage,
session/compaction/permission controls, Increment 5 end-to-end coordination,
multi-project scheduling, lifecycle/recovery and complete linked replay,
Increment 6 durable remote binding, scoped publication/reconciliation and
exact-SHA CI, and Increment 7 packaged self-hosting proof remain MVP/release
gates. Existing shell/sidecar implementation and local/manual delivery do not
close those gates. Extra history drilldown, export, analytics, broader providers
and a journal migration remain optional/deferred work, not accepted selections.

The leading candidate was explicit durable repository identity binding, because
ADR-0040 supplies immutable local scope and ADR-0041/0042 supply only ephemeral
remote facts. Code inspection and an independent compiled-parser probe exposed
a prerequisite contract defect: GitHubRepositoryIdentityAdapter admits escaped
keys at all five levels/names (data, repository, id, name, nameWithOwner) through
JsonProperty.Name decoding. All five were admitted; literal baseline, reordered
whitespace and restoration were admitted; an escaped value was refused. Its
existing public adapter tests still pass 117/117. Complete named TRX/log and
probe source/results are retained under ignored logs/planner-review/adr42
(identity-baseline and identity-escaped-properties-probe). This establishes a
strict-response admission defect, not a demonstrated authority bypass or a
real-GitHub compatibility result. ADR-0042's corrected parser remains accepted.

Decision revised on that evidence: select only the ADR-0041 identity parser
correction and its regression evidence before promoting that observer into any
durable binding. Claude remains executor; use a new executor chat for this new
slice. Changes are limited to the identity adapter, its tests and current-work;
preserve this planner edit byte-for-byte. Refuse any backslash in stdout before
parsing (same bounded guard as the branch adapter), covering property names and
values at every level. Keep literal order/whitespace controls, public-observer
red-first regressions for every key, a guard-removal compiling mutant and a
hash-verified restoration. No new ADR is needed to restore ADR-0041's already
accepted no-escapes contract. Durable binding is a candidate only; its storage,
authority and UI contract are not selected. No next remote capability, schema,
API, frontend, query/argv, environment, bounds, slot or authority change belongs
to this correction. Planning grants no staging, commit or push authority.

Acceptance: affected red/green and mutation evidence first, then clean serial
solution build, unchanged generated-client hash
8d2108a7909de924312790975c3dc02723a8c56547a05e8e213d90d13413059b,
fresh full Infrastructure, Api and Architecture suites and the affected normal
authenticated collaboration journey with owned doubles. Formatter, diff/secret
checks and edited-document links must pass. Unchanged Domain, Application,
frontend and other browser evidence may be retained with its exact boundary;
do not invent fresh runs. Preserve complete logs and named TRX results for every
future validation failure; an abort's active test is not a proven cause.
Material discrepancies, contract challenges or changes beyond this bounded fix
stop dependent work for planner review; unresolved material choices go to owner.

The unnamed Api 2067/2068 failure remains unexplained; subsequent executor and
independent 2068/2068 and later 2098/2098 passes prove neither flakiness nor
irrelevance. All recorded limitations remain, including missing original
frontend red-first evidence, bounded mutation coverage, skips, advisory,
deleted-dump/history facts and real CLI formatting/authentication uncertainty.
Qualification remains Blocked/TargetsNotReal with both allowances unused.
Preserve ledger, historical failures, retained roots and ignored review
artifacts. No real gh/provider invocation, CLI setup, ledger reset or cleanup
without a separate explicit decision. No separate closure publication is needed.

## Review decision (2026-10-13): GO for the corrected ADR-0042 diff

R1, R2 and R3 are accepted. This supersedes the NO-GO below for the reviewed
combined slice only. Publication has not happened; Claude remains executor,
and no next slice or increment/MVP/release completion is granted.

Verified main; HEAD/local origin/main/live refs/heads/main equal
7ccbcafb9295f33144b636ad7912d44cb2862d7e, 0/0, empty index, exactly
44 individual changed files (17 modified, 27 untracked). The prior planner
record was byte-identical at
219d2c3ea22a7735c987fb946fb03128f6b926d711edacacfb65984c9e3cf1a5
before this GO insertion; all its selection, owner transition and review
history remain intact. Generated-client SHA-256 independently remains
8d2108a7909de924312790975c3dc02723a8c56547a05e8e213d90d13413059b.
The corrected adapter independently matches
54ff9103b9d74b7dafcc3bf2833d7ce851b423eb4ce5378745c0f526bcbcf618.

R1: the new adapter refuses every backslash before JSON parsing, without
changing any other observer, query/argv, process bound, status or authority.
Ten public-observer property-key regressions were genuinely red on the
submitted parser (10 failures/one passing control); the literal, null-ref and
whitespace/order controls remain green. Removing the guard is a compiling
mutant killed by those ten cases, and the adapter hash is restored.
Codex rebuilt the solution serially (incremental, zero warnings/errors),
independently ran the corrected full adapter class 188/188, and repeated its
original compiled-parser probe: all escaped keys/values are now refused;
baseline and restoration remain admitted. Fresh full log/TRX and probe
results are retained under ignored logs/planner-review/adr42.

R2: the journey now explicitly opens/checks after the real reload and target
reload, awaits that response and settled render, checks the exact recorded
scope/ref/fixed remote SHA and CommitDifferent, requires totals of three
requests/three processes, and reasserts no other effects or automatic reads.
Codex inspected these assertions and the executor's affected journey 1/1 and
canonical 13 Chromium + seven journeys results; it did not rerun the browser
matrix. The fixed remote SHA is accepted, with matched/no-ref controls at the
host, adapter and UI boundaries and no real-GitHub inference.

R3: current-work now names the active test at the red Infrastructure abort
without claiming a crash cause, retains deleted-dump and historical failure
facts, writes the exact strict e2e tsc command and distinguishes correction
checks from unchanged retained evidence. Codex checked the correction's red
and green logs, named TRX counters, mutation log/hash, clean-build and browser
logs. Executor correction evidence is Infrastructure 2268 plus four skips,
Api 2098/2098, Architecture 106/106, frontend 47/47, harness 89/89 and canonical
13+7. Application 5517, Domain 1392, full Vitest 3582, typecheck/lint/build and
audits remain retained with their actual unchanged boundaries. Prior
independent review evidence remains scoped as recorded below (Api 19,
Architecture 106, frontend 47 and the prior adapter 177); no new independent
full Api result is claimed.

The old unnamed Api 2067/2068 failure remains unexplained; later passes prove
neither flakiness nor irrelevance. Preserve missing original frontend red-first
evidence, bounded mutation coverage, privilege skips, existing npm advisory,
historical setup/host/browser incidents and Windows/real-CLI formatting,
stored-authentication/environment limits. Observation is ephemeral advisory
repository/ref/SHA evidence, not CI, permission, durable binding or publication
authority. Real qualification remains Blocked/TargetsNotReal, both allowances
unused; ledger, retained roots and ignored review artifacts remain untouched.

GO authorizes only these frozen 44 files, including this planner update and
current-work.md. Codex records raw hashes/sizes and expected normalized Git
blobs/modes in the ignored publication manifest and supplies its own hash in
the complete publication prompt; verify both the manifest and every entry.
Stop on identity/content/inventory drift, remote divergence, stage/commit/push
failure or publication discrepancy. Stage exactly the reviewed inventory,
commit once with the stated substantive message and existing attribution,
normal fast-forward push main to origin/main, and independently verify the
live remote in this same executor turn. No source/document edit, regeneration,
validation rerun, closure diff, force-push, history reconciliation, cleanup or
next slice is authorized. Codex verifies publication before selecting the next
bounded slice.

## Review decision (2026-10-13): NO-GO; bounded ADR-0042 corrections

The submitted branch-observation slice stays unstaged, uncommitted and unpushed.
Continue corrections in the same Claude executor chat. No next slice, staging,
commit, push or closure publication is authorized. The selection and owner
transition below remain intact; this decision adds a correction round.

Fresh preflight: main, HEAD/local origin/main/live refs/heads/main equal
7ccbcafb9295f33144b636ad7912d44cb2862d7e, 0/0, empty index. The inventory is
44 individual files: 17 modified and 27 untracked files (the executor's 16 new
status paths include directories). Planner selection was byte-identical at
d3eded3dbca5a0e073b9de5ae971eb9e0c798db34e61d2c30b6c2ef859098fec
before this review insertion. Generated client independently matches
8d2108a7909de924312790975c3dc02723a8c56547a05e8e213d90d13413059b;
this additive generated contract supersedes its prior baseline.

### Independent evidence and accepted deviations

Codex inspected the Git diff, new query/port/adapter/branch policy, MVC mapping,
generated transport, decoder/hook/panel/composition, journey/double and relevant
tests/docs. Fresh incremental serial solution build: 0 warnings/errors;
frontend three files 47/47; adapter 177/177; full Architecture 106/106;
branch-observation Api class 19/19. Complete logs and named backend TRX are
retained under ignored logs/planner-review/adr42. These are focused reviewer
checks, not a new independent full Api or canonical browser run.

The sandbox frontend attempt passed the decoder 16 but could not load the
other two suites (ENOENT worker temporary files); its entire log is retained.
The sandbox adapter testhost aborted after its 90-second connection timeout
before reporting test outcomes. Normal-host runs resolve those environment
limitations; neither incident is a production test failure or proof of
flakiness. Preserve both aborted/setup outputs and the successful host results.

Executor final TRX counters independently agree with Domain 1392, Application
5517, Infrastructure 2257 plus four non-executed privilege cases, Api 2098 and
Architecture 106, all with zero failures. The retained executor logs report
Vitest 3582, harness 89 and canonical browser 13+7. Codex did not rerun those
full suites or the browser matrix in this round. Preserve their actual scope,
the recorded last test-only escape edit, and all historical incidents/limits.

Accept the journey's fixed remote SHA and CommitDifferent outcome: the host,
adapter and UI controls separately exercise matched and no-ref states; this
does not establish real GitHub behavior. Accept the identity architecture
inventory assertion listing both selected adapters, with its specific identity
boundary still intact. Missing original frontend red-first evidence remains a
recorded limitation; it cannot be reconstructed retrospectively. The bounded
21-mutant executor evidence is useful, not complete coverage.

### R1 - escaped property strings violate the literal admission contract

An ignored independent reflection probe invoked the submitted adapter's actual
compiled Interpret method, changing only one property spelling at a time.
The literal baseline and restoration are admitted. Each escaped key is also
incorrectly admitted: data as "\u0064ata", ref as "\u0072ef", oid as "\u006fid"
and __typename as "__type\u006eame". The escaped OID-value control is refused.
Probe inputs/script and observed results are retained in
logs/planner-review/adr42/escaped-properties-probe.ps1 and .jsonl. No production
or executor test was edited, and no provider/process/network was invoked.

JsonProperty.Name decodes property strings before the current comparisons,
whereas Text checks escapes only in property values. This is a literal
whole-response/no-escape contract violation, not a demonstrated authority
bypass: the decoded keys happen to mean the same fields in these probes.
Refuse escaped property names as well as values anywhere in this new adapter's
admitted envelope. A small adapter-owned escape refusal before parsing is
sufficient; no generic parser, dependency or change to ADR-0041 is selected.
Write public-observer regressions first for envelope/repository/ref/target
keys, with literal, whitespace/order and null-ref controls, and retain named
red results. Keep the existing fixed query, process, identities and statuses.

### R2 - exercise the explicit check after the real browser reload

The journey's new block proves no automatic request after reload, but stops
with branchRequests=2 and branchCalls=2. The selected post-reload explicit
recheck was not performed. After the existing reload and target reload, open
the branch region, assert unchecked/no request, explicitly Check, await its
specific response and settled rendered result, verify the same source/target
and exact returned ref/fixed remote SHA, then require exactly one new request
and one process call. Reassert no database/event/Git/provider changes.
Keep CommitDifferent and existing assertions; no fixture needs the local SHA.
A comment saying a check is needed is not evidence that the check works.

### R3 - accurate durable evidence

The red Infrastructure log and retained Sequence XML identify the test active
at the abort:
GitHubBranchObservationAdapterTests.A_concurrent_request_is_refused_immediately_with_no_queue_retry_or_extra_spawn_and_the_slot_is_released_after_the_first.
Correct the current-work description calling this hang unnamed. Record it as
the known active test with an aborted run, not as proof of the crash's cause.
Preserve that the hang dumps were deleted, all retained logs/results, and the
historical Api 2067/2068 failure as unexplained; later passes prove neither
flakiness nor irrelevance. Include exact check commands, especially the strict
e2e tsc invocation rather than only "the recorded flag set", and distinguish
initial, corrected, fresh and retained evidence. Keep existing advisories,
skips, original red-first absence and real-CLI/environment limits.

### Correction validation and return

Affected checks first: R1 named red/green observer cases and full adapter class;
R2 strict e2e tsc and affected collaboration journey using only owned doubles.
Then clean serial solution build, full Infrastructure/Api/Architecture, affected
frontend three files, harness and one canonical test:e2e:all. Verify unchanged
generated-client hash/regeneration, affected C# formatting, links/secrets/diff
hygiene. Unchanged Application/Domain/migration/native-shell and full frontend
build/audit evidence may be retained explicitly; rerun any whose boundary is
touched. Do not repeat an unchanged acceptance run for green. Capture complete
logs, exits, named results and failures; diagnose and stop dependent work on a
new failure. Add meaningful compiling escape-guard mutation evidence and restore
hashes; retain unrelated earlier mutants with their scope.

Preserve this planner record byte-for-byte during executor corrections.
Return the complete 44-file combined diff plus any justified regression file,
current-work.md correction entry, exact inventory and actual commands/results
for Codex GO/NO-GO. Challenge a flawed finding with evidence and a bounded
alternative; pause dependent work while a material dispute remains unresolved.

Real qualification remains Blocked/TargetsNotReal, both allowances unused.
Preserve ledger, historical failures, retained roots and ignored review artifacts.
No real gh/provider invocation, CLI setup, ledger reset or cleanup is authorized.
No increment/MVP/release completion, publication or new slice is granted.

## Selected slice (2026-10-13): explicitly correlate the recorded remote branch with the completed local commit

Codex is the planner/reviewer; Claude remains the executor. Select one additive,
explicit, ephemeral remote branch observation, including repository identity in
the same response. The owner-requested transition below is preserved verbatim as
historical context; this selection supersedes only its no-next-slice state.
No staging, commit, push, closure publication or real CLI/provider run is granted.

### Verified baseline and preserved evidence

Fresh Git inspection: main; HEAD, local origin/main and independently queried
live refs/heads/main all 7ccbcafb9295f33144b636ad7912d44cb2862d7e; 0/0,
empty index, no untracked files, only this planner file unstaged. Before this
selection its raw SHA-256 was
c4fc651bcaf1bcc79fe22b60cd2389b8fc2e9d72861973262a220b569f6662b0.
The sandbox's initial live-ref query failed with getaddrinfo() thread failed
to start; the network-enabled read-only query succeeded. This is not a test
result. Generated-client SHA-256 independently matches
29c2c551d813550ca166e23d01122843e2e27a2472bc6ae686c6e3733ca368a8.
No implementation validation was rerun during this planning-only selection.

ADR-0041 is published; its prior GO covers only that published diff. Retain the
unnamed Api 2067/2068 failure as unexplained: later executor and independent
2068/2068 passes establish neither flakiness nor irrelevance. Retain original
red-first absence, 63 killed/nine surviving mutants (equivalence only argued),
npm source-map-js advisory, privilege skips, historical incidents, Windows-only
evidence and unverified real CLI formatting/authentication/environment behavior.
Future validation failures must retain complete logs and named results.

Qualification remains Blocked/TargetsNotReal with both allowances unused.
Preserve the ledger, saved summary, historical failures, retained roots and
ignored review artifacts. No real gh/provider invocation, installation, login,
configuration, ledger reset or cleanup without a separate explicit decision.

### MVP assessment and priority

The actual roadmap and accepted ADR-0005/0009/0029/0038/0040/0041 preserve release
gates: real provider qualification; remaining Increment 4 account, session,
compaction and permission contracts; Increment 5 stage coordination, bounded
multi-project scheduling, lifecycle/recovery and complete linked replay; verified
remote binding, publication/PR intent and reconciliation, exact-SHA CI and bounded
correction; release-level packaged shell acceptance and self-hosting proof.
These are required outcomes, not completed by this selection. The shell and
sidecar implementation exist; their presence is not the full release proof.

Code inspected: Program composes separate explicit Agent/verification/commit
supervisors, not the end-to-end coordinator or multi-project scheduler. The
runtime projector still reports unproven capabilities as Unknown. The GitHub
source reader pins the completed receipt/operation/workspace and recomputes the
owned branch via WorkspaceBranchNamePolicy. The repository observation handler
and adapter read only id/name/nameWithOwner, re-read local scope and persist
nothing. No remote ref/SHA observer, durable remote binding, CI or publication
adapter exists in the tracked feature inventory. The existing hook owns scope
and opening lifetimes, including protection against obsolete reads releasing a
successor's in-flight guard.

Choose the missing remote ref/SHA observation because it directly advances
Increment 6 correlation using already implemented boundaries, without opening
remote mutation, persistent authority or autonomous dispatch. Qualification
cannot be closed under this slice's no-real-invocation boundary. Coordinator,
provider controls and durable publication require separate contracts; additional
history drill-down, exports, analytics, broader provider support and a wider
journal migration are optional/deferred improvements, not this selected work.
Prior possible follow-ups remain ideas unless explicitly selected later.

Official GitHub documentation confirms repository.ref(qualifiedName:) may fall
back to a short-name match, Ref exposes name/prefix/target, and GitObject exposes
oid. Therefore admission must prove returned prefix and exact branch name, not
trust the qualified input alone. A null ref means only that GitHub returned no
ref in this authenticated observation, not proven nonexistence or permission.
Use one response for repository and ref; do not combine two independent reads
or reuse an earlier ADR-0041 answer as a durable identity.

### Selected contract

- Add protected bodyless GET /api/runs/{runId}/github-branch-observation,
  GetGitHubBranchObservationQuery through the mediator, one MVC action,
  complete API-owned transport graph and reproducibly generated client.
  Unknown run: fixed 404 github_branch_observation.not_found. Any declared
  positive length, transfer encoding (including empty chunked) or readable
  body byte: fixed 400 github_branch_observation.body_not_allowed before
  dispatch. No client repository, node ID, branch, SHA, executable or credential.
- HTTP 200 closed statuses: NotRecorded, Unavailable, CliUnavailable, Unknown,
  RefNotReturned, CommitMatched, CommitDifferent. Response repeats runId,
  source and target using the ADR-0041 shapes. NotRecorded/Unavailable carry
  neither scope nor observation and invoke nothing; all other statuses carry
  the exact coherent scope. Only the last three carry an observation:
  repositoryNodeId, owner, repository, observedAtUtc and nullable ref
  {branchName, commitSha}. Ref is null exactly for RefNotReturned; matched and
  different contain the exact admitted branch and remote 40-lowercase-hex SHA.
  Match/difference is ordinal equality against the recorded local commit.
- Reuse ADR-0040 source/target readers unchanged and untracked before and after
  the external call. Any lost, changed or incoherent scope is Unavailable with
  no remote facts. Use ADR-0038's same fresh successful direct-executable gate
  (absolute path, no script, version and observed time); else CliUnavailable
  and zero invocation. No discovery/refresh/auth-status/origin/identity pre-call.
- One operation-owned provider-neutral Application observer and Infrastructure
  adapter on IProcessExecutionAdapter. One fixed read-only GraphQL document:
  query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner ref(qualifiedName:$ref){name prefix target{__typename oid}}}}
  One gh invocation: api graphql --hostname github.com --method POST, then
  separate -f query=<constant>, -f owner=<stored>, -f name=<stored>,
  -f ref=refs/heads/<recorded branch>. No interpolation, -F, endpoint
  placeholders, shell, jq, pagination, cache, retry, fallback or second call.
  Validate stored names and the bounded deterministic owned branch before
  invocation. Do not generalize the existing identity adapter or change it.
- Preserve ADR-0038/0041's OS/profile/temp allowlist and fixed controls, host
  scratch, no ambient tokens/config/host/debug/proxy/pager/browser/PATH, no
  credential extraction, output sinks/logs/artifacts, stderr discarded unread.
  15 seconds, 8 KiB per stream/16 KiB total, singleton one in-flight slot for
  this adapter; contention is Unknown immediately, release in finally.
  Cancellation/unexpected faults propagate; only the same identified expected
  OS executable-start failures translate to Unknown.
- Strict whole-response JSON admission: exact data.repository with id, name,
  nameWithOwner and ref. Repository must be a coherent object, supported opaque
  ID and names matching stored names ignoring ASCII case only. Ref must be
  explicit null, or exact name/prefix/target: prefix refs/heads/, name equal
  ordinally to the recorded branch, target exactly {__typename:"Commit",oid}
  with a 40-character lowercase hexadecimal oid. Reject errors/extensions,
  missing/extra/duplicate fields, escaped strings, malformed/trailing data,
  oversized/truncated streams, nonzero exit, timeout, foreign/renamed names,
  wrong prefix/name, null target and non-Commit object as Unknown, never as
  RefNotReturned or a match. Independently validate the port result in the
  handler and stamp only admitted observations with the host clock.
- One separate collapsed remote branch region beside the identity region in
  the Available target result. Show/Check/Recheck/Close, exact local source
  and recorded scope before checking, inert bounded result text and UTC time.
  No dependency on an earlier identity check. Explain a match is what GitHub
  reported at that time only: no CI, permission, durable binding, approval,
  publication-by-DevalCopilot or future stability claim. No ref returned and
  different SHA have distinct neutral statements, never success-looking.
  Reads belong to committed source+target and each opening. Preserve first-frame
  isolation, A-B-A, unmount, retained callbacks, serial-guarded close, same-batch
  and in-flight refusal, and late-read successor guards. No automatic reads,
  polling, reconnect work, retry, storage, key/remount or reset-effect workaround.

### Scope, evidence and stop gates

Allowed: new query/port/adapter, Program composition, MVC mapping/client,
feature-owned decoder/hook/panel and target-panel composition; focused tests and
a narrow extension of the existing collaboration journey using the owned native
gh double. Add ADR-0042 for this additive contract, index/context/roadmap/cockpit/
GitHub/security notes as needed and current-work.md. Preserve this planner
record byte-for-byte. Existing observation, target, receipt and authority
behavior stay unchanged. No Domain/schema/migration/journal/dependency change.

Acceptance: capture named, meaningful red-first regressions; prove exact
argument vector/environment and bounded real process-double behavior, matching,
different and null-ref controls; fallback tag/wrong-name refusal, null target,
errors with partial data, foreign/invalid identity and OIDs, stream limits,
slot/cancellation/fault release. Prove pre/post-scope corruption refuses answers
through reversible file-backed SQLite/API cases, every unusable CLI gate,
authentication/body/method refusal with zero invocation, actual OpenAPI/wire
shape, and no database/event/Git/provider effects. UI tests cover all closed
states, malformed and contradictory payloads, source identities, ownership
races including an old read settling while the successor remains pending.
Extend the normal authenticated collaboration journey after its production
local delivery and target: exact branch/SHA/render checks with the double,
no automatic calls, explicit recheck after reload, unchanged row counts and Git.
Keep existing assertions, supervisors and owned fixture cleanup policies.

Use a bounded set of meaningful compiling mutations for exact ref/prefix/SHA,
whole-envelope, scope, CLI/body and UI lifetime guards. Preserve source hashes,
named outcomes and survivors; compile failures are not kills. Run affected
checks first, then clean serial solution build, full Application/Infrastructure/
Api/Architecture and Vitest, typecheck/lint/production build, exact strict e2e
tsc, harness then one canonical test:e2e:all; generated client twice identical;
affected C# formatting, links, secrets/diff hygiene and dependency audits.
Retain unchanged Domain/migration/native-shell evidence with explicit scope;
rerun if touched. Capture full command outputs, checked exits, named TRX/results,
real skips, fresh versus retained evidence. On failure preserve everything,
diagnose/report it and stop dependent acceptance; do not rerun unchanged for
green or fix unrelated failures opportunistically.

Stop and discuss any need for changed accepted policy/readers, persistent
binding, publication/CI/permissions, broader branch support, another invocation,
new dependency, real account/CLI work or missing trustworthy correlation.
Evidence-based executor challenges are welcome: pause dependent work, provide
the contrary evidence and bounded alternative for Codex to reassess. Unresolved
material authority choices go to the owner; disagreement grants no expansion.

Expected executor start: main and all three refs at the baseline above, 0/0,
empty index, only this now-updated planner file unstaged, no untracked files.
Codex supplies its new raw hash with the complete prompt. Use one new Claude
executor chat and keep its corrections there. Return complete unstaged,
uncommitted, unpushed diff including current-work.md, inventory, commands/counts,
failure excerpts and retained complete logs for Codex GO/NO-GO. No next-slice
selection, staging, commit, push or separate closure round is authorized.

Protocol sources checked on 2026-10-13:
[repository ref lookup](https://docs.github.com/en/graphql/reference/repos),
[Ref and GitObject](https://docs.github.com/en/graphql/reference/git),
[raw string GraphQL variables](https://cli.github.com/manual/gh_api).
These document the protocol, not installed CLI compatibility or a real remote test.

## Current checkpoint (2026-10-13): published ADR-0041; owner-requested planner chat transition

The owner explicitly requested that next-slice planning happen in a new Codex
planner/reviewer chat. No next slice is selected or dispatched here. Claude
remains the executor; the new planner owns roadmap interpretation, selection
and final review judgment. This transition takes precedence over the ordinary
immediate next-selection workflow; it is not an executor closure task.

Codex independently verified publication at
7ccbcafb9295f33144b636ad7912d44cb2862d7e, parent
8c7eb3c4c5584683ef51aefb8c921331101cf370, expected feature message and
Co-Authored-By trailer. Main HEAD/local/live origin/main agree, 0/0, empty
index and clean working tree before this transition edit. All 43 committed
paths, blobs/modes and raw working-tree SHA-256/size values match the frozen
manifest, whose hash is
77db4b83492f43c006c86752f042139475e7727fb8a9d9eeba2c9e7cf6e3bdac.
No validation was rerun for publication. Only this planner record is now
modified, unstaged and uncommitted; preserve it until the next selection.

Delivered: ADR-0040 local immutable observation scope plus ADR-0041 explicit,
ephemeral repository identity observation. Neither supplies durable remote
binding, branch/SHA proof, permission, CI, publication or automatic observation
authority. Next planning must inspect code, the roadmap and accepted ADRs,
then choose the best bounded MVP prerequisite; do not treat prior possible
follow-ups as selected architecture. Release gates and optional work remain
as recorded below; no increment or MVP completion is claimed.

Preserve ADR-0041 evidence and limits: independent full Api 2068/2068 with
complete log/TRX and frontend 39/39; executor correction Api 2068/2068,
Vitest 3535 and retained canonical e2e 13+7. The earlier unnamed Api failure
2067/2068 remains unexplained, not proven flaky or unrelated. Mutation totals
are 63 killed/nine survivors, with equivalence only argued. Keep the existing
npm advisory, privilege skips, lack of original red-first evidence and real
CLI/authentication/environment uncertainty. Future validation failures need
complete output and identified test results.

Real qualification remains Blocked/TargetsNotReal, both allowances unused.
Preserve ledger, historical failures, retained fixture roots and ignored
review artifacts. No real gh/provider work, CLI setup, ledger reset, cleanup,
commit or push is authorized by this transition. The prior GO applied only
to the published frozen ADR-0041 diff. Do not publish this transition edit
separately or create another closure round.

## Previous selection (2026-10-12, published): explicitly observe the recorded GitHub repository identity

### Review decision (2026-10-13): GO, corrected repository identity observation

Verified main HEAD/local/live origin/main at
8c7eb3c4c5584683ef51aefb8c921331101cf370, 0/0, empty index and exactly
43 combined paths (18 modified, 25 new). The executor preserved the prior
NO-GO planner hash
845253975cf34cbb3afc2475e45f51761a2733b468e05d4b3bdcdd89529638ed.
This decision replaces that checkpoint and belongs to the approved diff.

All three bounded corrections are accepted. Permanent hook regressions prove
that late reads preserve a pending successor's in-flight guard across A-B,
close/reopen and A-B-A, then accept the successor's distinct ID. Real-SQLite
Api cases refuse relative executable paths and NULL/empty/blank versions with
CliUnavailable, no observation, no observer call and no write. The empty
chunked request is fixed body_not_allowed 400 with zero observation/write.
The corresponding F15, H5, H6, E3 and E5 mutants are reported killed with
restored source hashes; the production guards remain unchanged. Current-work
now correctly records 63 killed and nine survivors from the 72-ID matrix;
compile-error variants count only through their final compiling replacement.
Remaining layered defenses are retained; their equivalence is argued in the
current contract, not proved generally. The absence of original red-first
runs and every initial incident remain historical facts.

Independent correction evidence: frontend decoder/hook/panel 39/39, and one
full Api run 2068/2068 with zero failed or skipped, including all 17 observation
cases. Complete log and TRX were retained under ignored logs/planner-review:
adr41-correction-review-20261013.log and
adr41-correction-review/adr41-correction-review-20261013.trx.
The executable test names and outcomes were checked in the TRX. No repeated
independent Api run was used to obtain green. The executor's first full
correction run still failed one unidentified test (2067/2068), losing the name
by retaining only the tail; its second run passed. The failure is unexplained,
not established flakiness and not established to be unrelated to this slice.
It remains a test/host reliability risk despite the subsequent passing evidence.
Future failed validations must retain complete output and named results.

Prior independent review evidence remains Application 21, Infrastructure
adapter 117, Api endpoint 11, Architecture five and frontend 36, plus the
original/mutant comparison and SQLite/body probes recorded in the previous
correction record. Full executor validation, retained checks, existing npm
advisory, privilege skips, initial failures and CLI/environment limits remain
in current-work.md. Client SHA-256 is unchanged at
29c2c551d813550ca166e23d01122843e2e27a2472bc6ae686c6e3733ca368a8.

Accept the fixed authenticated read-only GraphQL invocation, bounded isolated
process, pre/post coherent local scope, strict whole-response admission and
explicit owned UI. No Domain/schema/migration/journal/dependency change is
introduced. Observation remains ephemeral advisory evidence of repository ID
and names only, not branch/SHA correlation, permission, CI, durable binding or
publication authority. Real qualification remains Blocked/TargetsNotReal with
both allowances unused; ledger, historical failures and retained roots remain.

GO authorizes only the frozen combined 43-path diff including this planner
update and current-work.md. Verify manifest identity and all entries, stage
exactly those files, commit, normal fast-forward push and independently verify
the live remote in the same executor turn. Stop on any content/identity drift
or divergence. No edits, validation reruns, closure diff, force-push or next
slice. Codex will verify publication and select the next bounded slice after
the delivery report. This decision itself does not publish anything or certify
an increment/MVP/release as complete.

### Prior publication independently verified

ADR-0040 is delivered at 8c7eb3c4c5584683ef51aefb8c921331101cf370,
parent 27e8b02d85e0d11fa92c52b166c4aa95f9886be7, with the approved feature
message and Co-Authored-By trailer. Main HEAD/local/live origin/main agree,
0/0, empty index and clean working tree before this selection edit. All 81
committed paths, blobs/modes and working-tree SHA-256/size values match the
frozen manifest; its hash remains
1e697a8a153faf40768b90f9f735818d4810c93102875672caf5b1d078201a56.
No validation was rerun for publication. Prior correction evidence, initial
failures and limits remain in current-work.md and the previous selection below.
No closure diff is required.

### Evidence and roadmap disposition

ADR-0005 and github-ci-integration require explicit repository identity before
remote correlation; ADR-0040 supplies only immutable Human-selected names tied
to the recorded branch/SHA. Code has no remote repository observer, durable
remote identity binding, CI observer or publication adapter. Select one bounded
read-only identity observation through the existing CLI/process capability and
normal authenticated cockpit. It is a prerequisite, not an Increment 6 exit or
a persistent remote binding. Exact branch/SHA existence, identity persistence,
CI correlation/monitoring, scoped publication and recovery remain later work.

Release gates remain real Codex/Claude qualification and remaining Increment 4
contracts, Increment 5 coordination/concurrency/recovery and complete replay,
remote CI/publication/reconciliation, packaged shell and self-hosting proof.
Real qualification stays Blocked/TargetsNotReal with both allowances unused.
Additional browsing, exports, analytics, provider expansion and strengthening
ADR-0040's event reference with a wider journal migration are not selected.

### Selected contract

- Protected bodyless GET /api/runs/{runId}/github-repository-observation,
  MVC to one mediator query and an operation-owned Application port. Unknown
  run: fixed 404 github_repository_observation.not_found. Known run: closed
  NotRecorded, Unavailable, CliUnavailable, Unknown or Observed, HTTP 200.
- Read the existing source/target policies unchanged, untracked, before any
  external work. NotRecorded/Unavailable carry no scope or observation and
  invoke nothing. Other statuses carry the exact coherent local source and
  target; only Observed carries repositoryNodeId, observed owner/repository and
  observedAtUtc. No client URL, owner, repository, branch, SHA, executable or
  credentials. No persistence, event, mutation, lock or transaction.
- Require the same current successful direct-executable GitHubCli snapshot
  gates as ADR-0038 (absolute executable, no script, observed version/time,
  no probe failure/staleness), otherwise CliUnavailable with no invocation.
  No discovery, refresh, install, auth-status prerequisite or origin lookup.
- Infrastructure executes one fixed gh api graphql query on github.com, in
  host scratch through IProcessExecutionAdapter. Use --method POST and -f raw
  string fields for a compile-time query and the validated stored owner/name.
  The query is only repository(owner:$owner,name:$name){id name nameWithOwner}.
  HTTP POST transports this read-only GraphQL query; no mutation operation,
  interpolation, client query, endpoint placeholder, shell, jq, pagination,
  cache, retry, browser, arbitrary field or additional invocation is allowed.
- Preserve ADR-0038's non-secret OS/profile/temp environment allowlist and
  fixed controls, discarding ambient token/host/config/debug/proxy/pager/browser
  overrides. No credential extraction, raw sinks, output logs or artifacts.
  One 15-second process budget, 8 KiB per stream and 16 KiB combined; one
  singleton in-flight slot, concurrent requests immediately Unknown.
- Accept only the exact bounded data.repository JSON shape with three fields,
  no errors/extensions/duplicates/extra fields or partial success. Require
  conservative opaque ASCII node id (1..256 letters/digits/_-/+=), supported
  names, one owner/name split and internally coherent name/nameWithOwner.
  Observed names must match stored scope with ordinal ASCII case-insensitive
  comparison; no trim, decoding, .git removal, redirect/rename/transfer adoption.
  Unknown for missing/inaccessible repository, nonzero exit, timeout, truncation
  or invalid/foreign answer; no permission/nonexistence inference. Expected OS
  start failures may be Unknown as in ADR-0038; cancellation and unexpected
  faults propagate, with slot released in every case.
- Independently normalize the port result. Re-read source and target after the
  external call; any lost/changed/incoherent local scope yields Unavailable
  without remote data. No database transaction across external work. Observation
  is current response evidence only, not immutable remote identity or proof of
  remote branch/SHA, permission, CI, push/PR approval or automatic observation.
- Add a collapsed remote identity region only beside an Available ADR-0040
  target. Show, explicit Check/Check again, close. Nothing reads on opening,
  mount, project/run/target change, reconnect or timer. Owned source+opening
  lifetimes, serial-guarded close, same-batch/in-flight guards, whole-response
  decoding against the observed source/target, no key/remount/reset effect or
  browser persistence. Plain inert bounded text; opening again starts unchecked.
- Add ADR-0041 and narrow index/context/cockpit/roadmap/security/GitHub note
  updates, generated client and focused tests. Keep ADR-0040 records/readers,
  receipt, auth and origin behavior unchanged; no Domain/schema/migration or
  journal changes. Extend the collaboration journey after its real local target
  record, with the native gh double only, exact wire/render/source checks and
  zero database/Git/provider changes across identity reads.

### Evidence and stop gates

Capture meaningful red-first cases for gates, GraphQL envelope/source admission,
late answers/close and same-batch actions. Cover parser errors/partial data,
case-only names, actual rename/foreign scope, exact literal variables (including
numeric-looking names and .git suffix), stream bounds, cancellation, slot release
and unexpected faults through real process doubles. Prove pre/post-read scope
corruption refuses answers and unauthorized/body/method requests never invoke.
Use focused compilable mutations for these guards, report survivors honestly,
and restore sources by hashes. Test doubles do not prove real CLI formatting,
default authentication/environment compatibility or GitHub reliability.

Run affected checks first, then clean serial build, relevant full Application,
Infrastructure, Api, Architecture and Vitest; typecheck/lint/build, exact strict
e2e tsc, harness and one canonical e2e:all. Regenerate client twice and compare;
format changed C#, links, secrets, diff hygiene and dependency audits. Retain
unchanged Domain/migration evidence explicitly; rerun if touched. No new package
or standards change. Stop and discuss any required scope widening, real account/
CLI use, persistence, changed ADR admission, or inability to keep the fixed
query/envelope/process boundary. Preserve allowance ledger and fixture roots.

Start one new Claude executor chat. Expected HEAD/main is the published 8c7eb3c
above; empty index, no untracked files, only this planner-handoff selection edit
unstaged. Preserve it and report the completed combined diff with current-work.md
uncommitted and unpushed for Codex GO/NO-GO. No publication authority is granted.

Protocol reference: [GitHub CLI api manual](https://cli.github.com/manual/gh_api)
documents raw string fields, GraphQL variables, fixed hostname and POST transport.

## Previous selection (2026-10-12, published): record an immutable GitHub observation scope for a completed local delivery

### Review decision (2026-10-12): GO, corrected immutable observation scope

Codex independently verified main HEAD/local/live origin/main at
27e8b02d85e0d11fa92c52b166c4aa95f9886be7, 0/0, empty index and the combined
81-path inventory (33 modified, 48 new). The executor preserved the prior
NO-GO planner hash 53db629dafad46c54926f08420e6ee8b3fb45b781f3969d795bb2104a65e1743.
This review decision replaces that checkpoint and is included in the approved diff.

Both blocking corrections are accepted. The new target reader classifies the
exact Human/no-role/no-provider/no-attempt shape in SQL without converting
unknown actor enums. Commit, branch, owner, repository and event payload are
bounded by stored bytes before materialization; oversize/NUL-hidden suffixes
make the whole reading incoherent. Canonical source/event admission remains
intact, and genuine faults and cancellation propagate. The hook synchronously
advances a draft revision on edits and rejects retained submit callbacks,
including same-batch edits; a current edited draft still submits once. Source,
opening, duplicate, armed and ambiguous-outcome guards remain in place.

Independent correction checks passed: Application target tests 24/24 (19 new
storage-admission cases plus five persistence cases), Api reversible corruption
classes 2/2, frontend decoder/hook/panel 54/54. git diff --check has no error;
the generated client's existing CRLF-to-LF notice is retained. The executor's
full correction results and earlier retained build, Domain, Infrastructure,
migration, generated-client, browser, audit and hygiene evidence remain in
[current-work.md](current-work.md); historical failures are not erased.

Mutation judgments now agree with evidence: F4 is killed by the successor
arming regression; B7 survives exclusion checks because this provider's ordinary
transaction already excludes an independent writer before the no-op statement.
Keep the explicit no-op pattern and uniqueness as separate protection; no
provider-independent equivalence is claimed. Six correction mutants are reported
killed with restored source hashes. One nonblocking test-description mismatch
remains: the row labelled repository A-B-A actually edits the owner then restores
the repository's unchanged value. Repository edits, owner A-B-A, same-batch
invalidation and current-draft submission are independently covered, and the
shared revision logic applies to both setters. Do not count that row as a direct
repository A-B-A demonstration; this does not block acceptance.

Accept four restrictive target FKs plus exact logical event validation, avoiding
a wider events-table rebuild. The database does not prevent direct event deletion
or alteration; GET and replay refuse missing, foreign or altered events. The
sibling panel, policy-conformant fixture branch, narrow migration/event allow-list
expectations and unchanged legacy snapshot line remain accepted. The selection
is irreversible and local only: no remote identity, permission, publication or
CI claim. Real qualification remains Blocked/TargetsNotReal; both allowances,
the ledger, historical failures and retained roots remain unchanged.

GO authorizes only this frozen combined diff, including this planner update and
current-work.md. Verify the ignored publication manifest and all entries before
staging, then commit, normal fast-forward push and independently verify the live
remote in the same executor turn. Stop on any identity/content discrepancy or
remote divergence; no force-push, edits, new validation, closure diff or next
slice. Nothing is published by this decision itself. Codex will verify delivery
and select the next bounded MVP prerequisite after the publication report.

### Publication verified and next MVP prerequisite

Codex independently verified ADR-0039 at
27e8b02d85e0d11fa92c52b166c4aa95f9886be7, parent
b8a1f328bde674dd581a8cb15e0760cb678c092a, expected message and trailer.
Main HEAD/local/live origin/main agree, 0/0, empty index and clean working tree
before this selection edit. All 41 manifest entries match committed paths,
blobs/modes and raw hashes/sizes; manifest SHA-256 is
152ff34079c39a373d0528e4ae11c5a6cd4a322bfb4b7314bcc6cbd18e128095.
No validation was rerun for publication and no closure diff is needed.
Preserve Windows/CLI/alias/symlink/mutation limits from the accepted review.

Select one new Claude chat for a local, explicit Human selection of immutable
GitHub repository scope for the exact SHA and branch of a completed manual
local delivery. ADR-0005 and github-ci-integration require repository/branch/SHA
correlation; ADR-0039 observes configuration but deliberately supplies no durable
binding. LocalCommitOperation and ADR-0032 already supply exact completed source
and branch; there is no remote binding, push/PR/CI adapter or observation target.
This records Human-selected scope only. It verifies no remote repository or
permission and grants no publication or automatic observation authority. Remote
identity verification and exact-SHA CI are later slices, followed by scoped
publication and reconciliation. Remaining Increment 4 contracts/qualification,
Increment 5 coordination/concurrency/recovery, complete replay and packaged
self-hosting remain release gates. Additional browsing/export/analytics and
provider expansion are optional, not selected.

### Selected contract

- New Domain GitHubObservationTarget, immutable and at most one per run/local
  operation. Store host-owned id, run/project/local-operation/workspace ids,
  exact 40-character commit SHA, recorded branch, supported owner/repository,
  UTC recorded time and the one Human event id. Purpose CiObservation and host
  github.com are fixed project contract, never client choices. No status that
  implies remote verification, authorization, push, PR or CI success.
- Add one table, mapping, DbSet and additive migration with uniqueness for run,
  operation and event. Retain forever with the run history in this slice; no
  update, replacement, deletion, backfill, override or cleanup operation. Define
  restrictive foreign-key deletion behavior; no cascade that silently removes
  the new record or its event. Existing rows remain unchanged and unbound.
- Protected POST /api/runs/{runId}/github-observation-target: body at most 8 KiB,
  only operationId, commitSha, owner, repository. SHA is exact lowercase 40-hex;
  names use the unchanged ADR-0039 conservative ASCII subset, with no trim,
  decoding or arbitrary URL/host/branch. Use fixed validation/404/409 errors,
  never echo rejected values. Unknown run: github_observation_target.not_found;
  unusable/mismatched source: github_observation_target.unavailable; conflicting
  immutable selection: github_observation_target.conflict.
- In a manual command's short write-locked transaction, use the established
  no-op Run write-lock pattern and fresh untracked reads. Require ManualAgent
  mode and an Available ADR-0032 receipt for that exact run, matching the
  submitted operation id and SHA. Derive project/workspace/branch from pinned
  rows only. Verify the workspace number and WorkspaceBranchNamePolicy.Compute
  agree with the recorded branch; never accept a client branch or current HEAD.
  Reuse LocalDeliveryReceiptReader unchanged; do not dispatch a nested query or
  weaken receipt/digest admission. This is historical recorded source, not fresh
  workspace cleanliness or remote-publication eligibility.
- Atomically insert the target and exactly one version-1 Human event
  run.github_observation_target_recorded, with bounded canonical payload agreeing
  with the target's identities, repository, branch and SHA. Notify only after
  commit. No lifecycle/stage/participant/clock/LastAdvancedAtUtc or source-row
  change; only the no-op lock, target and event belong to this operation.
- Exact same coherent selection replays the original record/time/event without
  another insert or event; a different selection conflicts. Separate-context
  concurrency must serialize to one target and one event. Corrupt, foreign or
  inconsistent existing target/event/receipt never becomes a successful replay.
  Cancellation/save failure rolls back both inserts; unexpected faults propagate.
- Protected bodyless GET at the same route: closed NotRecorded/Unavailable/
  Available. Unknown run uses the same fixed 404. NotRecorded carries no target;
  it may carry only {operationId, commitSha, branchName} as source when the exact
  receipt and workspace branch are admissible. Available carries that source and
  the coherent version-1 target {id, eventId, host, owner, repository,
  recordedAtUtc}. Unavailable carries neither. Read untracked persisted rows
  only, no writes/locks/external work; validate target/source/event together,
  bounded projections, no latest substitution or partial result. No record can
  be inferred from origin or auth observations.

### UI and allowed implementation

Add a collapsed panel beside the live Completed LocalCommitPanel, with explicit
load/reload, close, blank owner/repository fields and a separate explicit Record
GitHub observation target action. Show the exact branch/SHA and immutable target
before recording, with fixed disclosure: local Human selection, no remote
verification, permission, CI result or publication authority; it cannot be
changed by this operation. No default from origin/auth, background probe, URL,
copy/open remote action or automatic recording. A failed/ambiguous POST gets
an explicit reload action before another submit; reload may find the original
record and the same body remains safely replayable. Validate the whole response
against the observed run/operation/SHA/branch and closed name/time/version shape.
Own form/read/write/close lifetimes by committed run/source/open/draft, with
serial guards and same-batch/in-flight protection. No keys/remount/reset effects.
Keep LocalDeliveryReceipt and historical panels read-only and unchanged.

Allowed: new Domain entity and narrow pure name policy, mapping/migration/
DbSets, two vertical slices and MVC endpoints, event constant/classification,
generated client, live panel/hook/decoder, tests/journey, additive ADR-0040 and
narrow decisions/context/cockpit/GitHub architecture/roadmap/security/current-work
docs. Extract the unchanged owner/repository checks from the origin parser into
that Domain policy for both consumers; prove original origin outputs unchanged.
No general repository abstraction, target registry, project-wide policy or
refactor of authority/receipt/commit code. Preserve this selection byte for byte.

### Acceptance and stop gates

Failing tests first. Domain bounds/immutability; real SQLite migration leaves
old runs unbound, key/FK enforcement, exactly one record/event, same retry,
conflicting retry and concurrent same/different bodies using separate contexts,
rollback/cancellation/restart, stale/foreign/not-completed source, malformed mode
and storage, altered receipt/digest/branch/event and unknown run. Prove all
source/workspace/lease/budget/attempt/clock rows and filesystem/Git/provider state
unchanged. API normal auth, body/method/error limits and exact closed wire.
Frontend source/whole-response validation and stale draft/run A-B-A, close/reopen,
same-batch close/submit, duplicate clicks, late answers and ambiguous response.
Extend the real-auth collaboration journey after its actual local commit:
record an explicitly typed target, check exact SHA/branch and one Human event,
reload/reopen, verify persistence and no second record/event or external work.
Retain historical read no-write assertions with the baseline taken after the
intentional target/event writes. No fixture-seeded completion or remote account.
Use a small meaningful mutation set for source, idempotence, atomicity and lifetime.

Run affected checks, clean serial build, full Domain/Application/Infrastructure/
Api/Architecture/Vitest, typecheck/lint/build, exact strict e2e tsc, harness and
one canonical e2e:all; reproducible client, scoped format, migration evidence,
links/hygiene/secrets/audits. Keep failures and actual capability skips explicit.
Stop and discuss if coherent receipt reuse, transaction serialization or this
minimal immutable record requires broader policy/schema/authority changes.
No Git/gh/process/network/provider call from the new operations, no credentials,
remote verification, push/PR/CI adapter, scheduler/recovery/claim/approval change,
CLI setup or cleanup. Real qualification remains Blocked/TargetsNotReal, both
allowances unused; ledger, historical failures and retained roots preserved.
Return one unstaged, uncommitted, unpushed diff for Codex GO/NO-GO. No publication
or subsequent slice is authorized. Challenge a flawed requirement with evidence
before dependent work; discussion does not authorize expansion.

## Previous selection (2026-10-12): observe the project's locally configured GitHub origin

### Review decision (2026-10-12): GO for the corrected combined diff

Codex re-verified main HEAD/local/live origin/main at
b8a1f328bde674dd581a8cb15e0760cb678c092a, 0/0, empty index. The corrected
combined inventory is 41 files (14 modified, 27 new): the two additional
Infrastructure scope files account for the increase from 39. The executor
preserved the prior planner hash
80615ea93a2eb6591224864aeba525c4dff2d227b3843f7ab86e5b61d4d4f95e.

The prior scope race is corrected: the second invocation reads the admitted
config file explicitly with --file instead of rediscovering a repository.
GitHubOriginScopePin rejects a gitfile/reparse directory and missing, directory
or reparse config, holds config read-only without delete sharing before both
invocations, rechecks admission and releases through using on all outcomes.
Real-Git regressions prove the original replacement/parent-fallback attempts
are blocked; foreign junction and missing-config cases are refused. An intact
config without the key remains NotConfigured. The budget, environment, two
invocations, fault propagation, UI, endpoint, Domain and identity behavior stand.

Codex independently ran the two adapter classes: 194/194 by the test runner
(the executor reported 193), and Architecture boundary 7/7. Earlier independent
Application 31, Api 10 and frontend decoder/hook/panel 68 remain applicable.
The current-work correction records five red-first failures and the executor's
full corrected checks, exact strict e2e tsc command and retained evidence.
An additional ancestor-junction probe stopped at baseline Unavailable before
configuration reading; it did not exercise an alias race and supplies no wider
alias guarantee. Its source remains ignored at
logs/planner-review/github-origin-ancestor-race-probe.cs, the temporary project
copy was removed, and the ordinary Infrastructure assembly rebuilt cleanly.
Original scope probe and disposable review fixtures remain retained.

Accept Windows/Git-version/short-path/locked-config limits and the stricter
layout and resolved-identity prerequisites. The file-symlink test returns without
exercising its assertion when link creation is unavailable; the green aggregate
is not evidence that this case executed. Static no-delete-sharing and acquisition-
order assertions are source checks, not killed behavioral mutants; the failed-
compilation variant is not a mutation kill. Preserve those disclosed gaps,
remaining survivors, existing skips/warnings/audit advisory and red-run incidents.
This advisory read proves no remote identity, permission, publication authority,
actual provider qualification or MVP completion. No further blocker was found.

GO authorizes only this corrected 41-file combined diff, including this planner
update and the factual current-work inventory clarification. Freeze an ignored
publication manifest after these edits. Verify every entry, commit, normal
fast-forward push to origin/main and remote verification in the same executor
turn; report the published SHA. Stop on mismatch, divergence or publication
failure. No new edits/checks, force-push, closure diff or next-slice selection.
Codex will verify publication and select the next slice. Real qualification stays
Blocked/TargetsNotReal, both allowances unused; ledger/failures/roots unchanged.
No gh/provider work, CLI installation/configuration or cleanup is authorized.

### Publication verified and MVP priority

Codex independently verified ADR-0038 publication at
b8a1f328bde674dd581a8cb15e0760cb678c092a, parent
7be3b05d3ac2654d08e8cd9ee2f5d189561c89b2, expected message and trailer.
Main HEAD/local/live origin/main agree, 0/0, empty index and clean working tree
before this selection edit. All 40 reviewed manifest entries match committed
paths, blobs/modes and working-file raw hashes/sizes. No validation was rerun
for publication and no closure edit is needed. Fixture-backed authentication
observation is delivered; actual jq/installed-CLI/environment compatibility
remains unverified. It proves no remote permission or MVP completion.

Select one new Claude executor chat for a protected, explicit, read-only
observation of the registered project's repository-local origin configuration.
ADR-0005 and github-ci-integration require explicit repository scoping before
exact-SHA CI or remote mutation. Project currently stores local/physical identity,
not a GitHub target; no push/PR/CI adapter exists. Authentication alone supplies
neither target nor authority. This slice establishes a bounded normalized input
for later repository scoping, not remote identity verification or a publication
contract. Remote binding, exact-SHA CI, publication policy and reconciliation
remain later release gates. Increment 4 qualification/remaining contracts,
Increment 5 coordination/concurrency/recovery, complete replay and packaged
self-hosting also remain release gates. Extra history browsers, analytics,
export and provider expansion are optional and are not selected.

### Selected contract and scope

- Protected bodyless GET /api/projects/{projectId}/github-origin through one
  MVC endpoint and mediator query; fixed 404 projects.not_found for an unknown
  project. Response: projectId, fixed source RepositoryLocalConfig, closed
  Unavailable/NotConfigured/Available status, nullable observedAtUtc and target.
  Only Available has target {host: github.com, owner, repository}; Available
  and NotConfigured have time, Unavailable has neither time nor target.
- Untracked project and existing Git capability reads. A successful, fresh,
  coherent direct-executable snapshot is required; no discovery or refresh.
  Require the stored resolved physical identity and validate the current root
  and tuple before and after the observation using existing read-only ports.
  Unresolved, unavailable, changed or malformed identity returns Unavailable;
  no recheck command, backfill, identity update or lease/ownership mutation.
- A narrow Application observation port returns normalized facts only.
  Infrastructure uses the existing process engine and vetted Git executable,
  fixed read-only argv, approved project root and bounded output. Admit only
  the main repository's own root/config scope, never parent-directory fallback,
  a linked worktree, bare repository, arbitrary path or client-supplied remote.
  Use minimal repository-shape checks, not status/diff/checkpoint capture.
- The one configuration lookup is config --local --no-includes --null --get-all
  remote.origin.url under the established Git hardening discipline. No global,
  system, included or worktree config value is a candidate. Keep ambient Git
  config/path/credential/SSH/proxy overrides out of the child; fixed environment
  controls may disable global/system config, prompting and optional locks.
  No remote get-url, effective URL rewrite, pushurl lookup, credential helper,
  network, shell, hook, pager, fetch, ls-remote or config write.
- Strict NUL-framed output: exit 1 plus empty untruncated stdout means no local
  key (NotConfigured); exit 0 requires exactly one nonempty value. Duplicate
  values, even equal, malformed framing, invalid config, other exits, timeout,
  truncation or expected launch failure are Unavailable. Cancellation and
  unexpected faults propagate. Bound the whole observation to 15 seconds,
  each stream to 8 KiB and combined capture to 16 KiB per invocation; a fixed
  finite command set, no retry, queue, fallback or raw artifact/log sink.
- Admit only literal https://github.com/owner/repository,
  git@github.com:owner/repository or ssh://git@github.com/owner/repository,
  with one optional terminal .git removed. Host spelling is exact; owner is
  1..39 ASCII alphanumeric/hyphen, beginning/ending alphanumeric; repository
  is 1..100 ASCII alphanumeric/dot/underscore/hyphen, beginning alphanumeric,
  excluding dot segments. These are a conservative supported subset, not the
  full GitHub naming policy. No trim, percent decoding, URI normalization,
  credentials in HTTPS, alternate SSH user, port, query, fragment, slash suffix,
  control/whitespace, additional segment, other host/protocol or local path.
  Reject unsupported input without exposing it. Raw URL/stderr/config text
  remains transient inside Infrastructure, never returned, logged or stored.
- Project-level collapsed panel: explicit inspect/recheck/close only; fixed
  English copy explains local origin configuration, ignored overrides/rewrite/
  push destination, no remote verification, permission, CI or publication claim.
  Plain text target, no arbitrary URL/action. Reuse owned-lifetime/state hooks
  with project/open/request ownership and serial-guarded close. No mount,
  project-change, reconnect, timer or automatic remote work; validate the whole
  response against the observed project and closed shape.

Allowed: query/port/adapter/endpoint, narrow DI, generated client, panel/hook/
decoder and focused integration, frontend and browser fixtures; additive ADR-0039
and narrow decisions/context/cockpit/GitHub architecture/roadmap/security docs,
plus current-work evidence. Preserve this planner selection byte for byte.
No Domain/write-policy/schema/mapping/migration/dependency changes; no generic
Git gateway or rewrite of existing inspectors/process engine. No gh invocation,
remote binding/persistence, permission check, push/PR/CI adapter, publication
policy, scheduler, recovery, Agent eligibility or historical-reader change.

### Acceptance and stop gates

Failing tests first for admission, exact scopes/argv/environment/NUL framing,
closed parsing and ownership. Disposable real Git repositories prove missing,
one/multiple local origins, ignored global/include/worktree definitions and
URL rewrites, malicious sentinel URLs and refusal of parent/linked/bare roots.
Use process doubles for bounds/launch/timeout/cancellation and no unexpected
fault swallowing; prove physical identity before/after and no Git call when
admission fails. API auth/404/closed wire and database no-write fingerprint;
UI decoder plus stale A-B-A, close/reopen, same-batch close, recheck and unmount.
One normal-auth browser scenario proves exact target, explicit-only inspection,
project isolation and no database/working-tree/index/commit/provider changes.
No real remote/credential account is test data. Use a small meaningful mutation
set for scope, single-value admission, credentials, identity and lifetime guards.

Run affected checks, then clean serial build, full Application/Infrastructure/
Api/Architecture/Vitest, typecheck/lint/build, strict e2e tsc, harness and one
canonical e2e:all. Check reproducible generated client, scoped format, links,
diff hygiene, secrets and dependency audits; explicitly retain unchanged Domain
evidence and existing skips/warnings/advisories. Keep all failures and limits.
Stop and discuss if safe local scope/root/process isolation needs broader changes,
if an unexpected protocol shape lacks evidence, or any remote/secret/authority
work becomes necessary. Challenge a flawed requirement with bounded evidence;
do not silently weaken it. Return one unstaged, uncommitted, unpushed diff.

Real qualification stays Blocked/TargetsNotReal, both allowances unused;
ledger, historical failures and retained roots are preserved. No providers,
CLI installation/configuration or cleanup. This selection grants no commit/push
or subsequent slice authority. Protocol basis: the official git-config manual
(https://git-scm.com/docs/git-config), checked on 2026-10-12; local scope,
no-includes, NUL framing and missing-key exit still require real boundary tests.

## Previous selection (2026-10-12): explicitly observe stored GitHub CLI authentication

### Review decision (2026-10-12): GO for the corrected combined diff

Codex re-verified main HEAD/local/live origin/main at
7be3b05d3ac2654d08e8cd9ee2f5d189561c89b2, 0/0, empty index, exactly 40 files
(18 modified, 22 new). The executor preserved the prior planner record hash
e5c83ef42d5524ee65738c21d589f06f5eac017332f8707111853a24f3c864d8.

Both prior blocking findings are corrected: the privacy selector admits an
empty hosts object as NoActiveAccount while malformed shapes remain Unknown;
the process exception boundary handles only the identified OS/start failures
and propagates unexpected faults and cancellation, releasing the slot in
finally. The ExecutablePath argument filter agrees with the shared executor's
absolute-path and missing-file validation; the process engine is unchanged.
Codex independently reran Infrastructure adapter 96/96 and Api endpoint 9/9.
The earlier independent Application 20 and frontend/App 42 results remain
applicable. The current-work correction records red-first 15/96, three killed
mutants and the executor's full corrected validation; retained checks are
explicitly distinguished. No additional blocking finding was identified.

Accept the disclosed fixture-only limits: the actual jq evaluator, installed
CLI formatting and environment compatibility remain unverified. This supports
an advisory observation, not real CLI qualification, repository permission,
publication permission in the product, or Increment 6/MVP completion. Preserve
red-run and mutation limitations, existing warnings/audit advisory and capability
skips. Real qualification remains Blocked/TargetsNotReal, both allowances unused;
ledger, historical failures and retained roots stay intact.

GO authorizes only this corrected 40-file diff, including this planner update.
Freeze the publication manifest under ignored logs/planner-review, verify all
entries against Git and publish the exact reviewed diff in the same executor
turn: commit, normal fast-forward push to origin/main, remote verification,
then report the delivered SHA. Stop for any identity mismatch or divergence;
no force-push, new edits, validation reruns, separate closure diff or next-slice
selection. Codex will independently verify publication and select the next
bounded slice. No real gh authentication, provider work, CLI installation or
configuration, allowance use, ledger reset or root cleanup is authorized.

### Publication verified and priority

Codex independently verified publication 7be3b05d3ac2654d08e8cd9ee2f5d189561c89b2,
parent 3f79af59c7aa77d5a62671d3dd2331fdaba75535, message
`feat: inspect historical message attempt attribution` and Co-Authored-By trailer.
All 44 manifest entries match paths/statuses, raw hashes/sizes and committed
blobs/modes; manifest SHA-256 is
50d7b1a4152920f42fcb2ffcbaac09018570ebb292a00d31a2f08805d0d88d49.
Main HEAD/local/live origin/main agree, 0/0, index and working tree clean before
this selection edit. No validation was rerun and no closure edit is needed.

Select a new Claude executor chat for one explicit observation of existing,
stored GitHub CLI authentication for github.com. ADR-0005 and Increment 6
require GitHub authentication health without extracting the CLI token. The
current capability descriptor probes gh.exe --version only; there is no
authentication observer or remote/CI loop. This advances a release prerequisite
outside the blocked Agent qualification and avoids treating binary discovery
as authentication. Further history browsing, export and analytics are not
selected. Auth health is not repository permission, publication authority or
Increment 6 completion. Replay, remaining Increment 4 contracts, Increment 5
coordination/concurrency/recovery, remote/CI reconciliation and packaged
self-hosting remain release gates. Real qualification remains
Blocked/TargetsNotReal; both allowances unused, ledger/failures/roots preserved.

### Selected contract

- Add protected, bodyless GET /api/environment/github-stored-authentication via
  a new mediator query and one MVC endpoint with API-owned transport. It returns
  only fixed host github.com, fixed context StoredDefaultConfig, a closed status
  CliUnavailable/Unknown/NoActiveAccount/Authenticated/IssueReported and nullable
  ObservedAtUtc. Only the last three states carry an observation timestamp.
  No persisted auth snapshot, run/attempt/event row, settings or schema change.
- The query owns its untracked lookup of the existing GitHubCli capability.
  Use only a coherent direct executable target from successful discovery;
  do not discover, refresh or install tools inside this query. Respect existing
  readiness freshness semantics. Missing, invalid or stale target gives
  CliUnavailable and invokes nothing. Database faults keep the normal pipeline.
- Application owns a narrow provider-neutral authentication-observation port;
  Infrastructure owns the GitHub CLI protocol and its adapter. Reuse the existing
  IProcessExecutionAdapter and host-owned scratch working directory, never a
  repository, shell, private provider invoker or second process engine.
- One command, literal arguments: auth status --hostname github.com --active
  --json hosts --jq <fixed privacy projection>. No --show-token or auth token;
  no fallback to human output or alternate command. The constant jq expression
  must validate the hosts/object and target/array shape before producing only
  an entries array of active/state pairs. Preserve invalid input as an invalid
  marker, never an empty success. No login, token, tokenSource, scopes, error
  text, URLs or other account fields reach the parent stdout.
- Interpret only a coherent empty array as NoActiveAccount, exactly one active
  entry with state success as Authenticated, and exactly one with state error
  as IssueReported. Timeout, unknown state, invalid/duplicate fields, inactive
  or multiple entries, malformed/oversized/truncated output, nonzero exit,
  process failure or unsupported flags gives Unknown with no timestamp. JSON
  mode can exit zero while reporting an auth issue: exit zero alone never
  establishes authentication. Unexpected application faults are not swallowed.
- Probe only the default stored credential context. Do not read auth files,
  tokens or credential values; gh accesses its own store. The child receives
  only selected non-secret OS/profile/temp variables and fixed controls disabling
  prompting, CLI/extension update notification and telemetry. No ambient token,
  debug, proxy, custom config/host, browser or pager overrides are copied.
  Disclose that environment-token and custom-config contexts are excluded;
  do not present this as the user's complete ambient CLI session.
- Bound each probe to 15 seconds, 8 KiB per stream and 16 KiB combined, with
  existing process cleanup/cancellation. No raw stream/artifact sink, provider
  payload log or raw diagnostic in errors/API/UI/docs. Discard stderr content.
  One in-flight auth probe per host instance through singleton adapter ownership;
  a concurrent request returns Unknown immediately, with no queue/retry/spawn.

### UI, scope and stop gates

Add a compact host-level GitHub stored-authentication region near readiness,
with explicit check/recheck and close. Nothing probes on mount, project changes,
reconnect, timer or background refresh. Loading/error/not-checked/result states
are explicit; current session/open/check owned lifetimes prevent stale response
or retained callbacks crossing close/reopen/recheck/session end/unmount.
Use fixed English labels/plain text and validate the entire fixed-context
response/status/timestamp shape. Explain the stored-only scope and that it
grants no repository permission or publication authority. Keep Agent readiness,
claim eligibility, budgets, history and local delivery unchanged.

Allowed: the query/port/adapter/endpoint, narrow DI composition, generated client,
readiness component/hook/decoder/styles, focused tests and isolated CLI fixture,
one browser scenario on normal authentication using a test-host observer/fixture,
ADR-0038 and narrow decisions/context/cockpit/roadmap/security/current-work docs.
No Domain policy/schema/mapping/migration/dependency change, generic remote
gateway, repository/PR/CI read or mutation, scheduler/recovery change or CLI
installation/configuration/login/switch/refresh. Never invoke real gh auth or
Agent providers during implementation/validation; use local fixtures only.
Preserve this selection byte for byte. Stop and discuss before any broader
credential, environment, process or authority change. No commit/push authorized.

### Acceptance and authoritative evidence

Write meaningful failing tests before implementation. Prove query target
admission/no-call refusal, real process request argv/environment/root/caps,
single-flight concurrency and release after failure, cancellation/timeout and
cleanup. Test zero-exit error, empty/success/timeout/multiple/inactive cases,
unsupported flags, duplicate/extra fields, truncation and hostile sentinels.
Pin the exact privacy selector/argv and independently restate its expected safe
payloads for synthetic unfiltered CLI responses. Exercise the actual expression
if an authoritative evaluator is already available without installation or new
dependencies; otherwise explicitly retain unverified formatter/installed-CLI
compatibility. A fixture that prints filtered JSON proves the process boundary,
not actual CLI formatting. No actual account or secret is test data.
API tests prove normal auth, no body/client-supplied host, minimal closed wire
and zero writes. UI/browser tests prove explicit-only reads, stale ownership,
exact states and no impact on Agent/local-delivery/history behavior. All tests
must run without GitHub/account/network access; no real compatibility claim.

Run affected checks then clean serial build, full Application/Infrastructure/
Api/Architecture/Vitest, typecheck/lint/build, strict e2e tsc, harness and one
canonical e2e:all; format, reproducible client, links/hygiene/secrets/audits.
Retain unchanged Domain results explicitly. Use a small meaningful mutation
set for state/privacy/target/concurrency/ownership guards. Preserve failures;
no unchanged rerun loop for green. Return one unstaged, uncommitted, unpushed
combined diff with actual checks and retained limitations for Codex review.

Protocol verified against the [official auth-status manual](https://cli.github.com/manual/gh_auth_status),
[environment manual](https://cli.github.com/manual/gh_help_environment) and
[CLI implementation](https://github.com/cli/cli/blob/trunk/pkg/cmd/auth/status/status.go)
on 2026-10-12. Older/incompatible binaries must remain Unknown, never trigger
installation or fallback. Neither this selection nor its discussion grants
remote publication authority or selects any subsequent slice.

## Previous selection (2026-10-12): inspect a historical message's own Agent attempt attribution

### Review decision (2026-10-12): GO for the combined diff

Codex verified main HEAD/local/live origin/main at
3f79af59c7aa77d5a62671d3dd2331fdaba75535, 0/0, empty index; the combined
diff has 44 files (18 modified, 26 new). The executor preserved the starting
planner hash 9d358c5e3d9f34793783bd1e0a01e3dcc5784e81dea85379039630b2db0dc56e.

Code review confirms exact own-attempt/run scoping, SQL enum classification,
bounded assignment reads with oversized-versus-null flags, max-turn storage
class preservation and unchanged assignment/identity rules. Independent checks
passed: Domain 8, Application 94, Api 11, Architecture 6 and frontend 166.
The journey checks actual durable attribution and exact rendered facts before
and after browser reload, while retaining reply/receipt/paging and no-write,
no-provider/no-commit assertions. The executor's full validation remains its
recorded evidence, not independently rerun full-suite evidence.

Accept the 384-byte assignment and 64-byte max-turn caps, with exact admission
after bounded reading; malformed GUID storage remains a fault. NoAgentAttempt
and Unavailable need not be manufactured in the provider-only journey: focused
host/API/UI tests cover them. Missing original frontend red-first evidence and
the mutation-set corrections are honestly disclosed. Initial byte-guard survival
and compile-error mutants remain recorded; only the corrected runs count as kills.
Codex clarified only documentation and a comment: the source reference is read,
but NoAgentAttempt resolves no attempt row. No behavior changed. Existing audit
advisory, console warnings and Blocked/TargetsNotReal remain; both allowances
are unused. Ledger, historical failures and retained roots remain untouched.

Authorize one commit of this exact reviewed combined diff, including Codex's
review edits, then a normal fast-forward push to origin/main, live remote
verification and one SHA report in the same executor chat. No additional checks
or separate closure diff/review are required. Stop on changed identities,
divergence or failed push; no force-push. No provider work, allowance use,
ledger reset or root cleanup is authorized. No subsequent slice is selected;
Codex selects it after independently verifying publication.

### Publication verified and selected outcome

Codex independently verified publication 3f79af59c7aa77d5a62671d3dd2331fdaba75535,
parent e8ae2da199ea21756a29f054677191ff1bd9bf48, message
`feat: inspect recorded historical reply parents` and its Co-Authored-By trailer.
All 42 manifest entries match working-tree hashes/sizes and committed paths,
statuses, blobs and modes; manifest SHA-256 is
f52a260d25cfde27eade89b97db56f252ae5c53a357a9b89f7a2e54f30036c4d.
Main HEAD/local/live origin/main agree, 0/0, clean index and working tree before
this selection edit. No validation was rerun for publication; no closure is needed.

Select a new Claude executor chat for explicit inspection of a historical
message's own Agent attempt attribution with minimal recorded metadata.
ADR-0003/0004/0009 and Increments 5/7 require attributable linked replay.
History now shows safe messages and reply parents, but cannot identify the
attempt that produced a historical provider-observed message. Existing
GetCollaborationMessageEvidence reads full message/attempt entities and more
evidence than this view needs; AgentAttemptRead likewise materializes a full
attempt. Neither is a bounded projection for this new historical surface.
Use the stored AttemptId, not a latest-role/provider/number search.

This closes one attribution access gap, not complete replay or any increment.
Real qualification remains Blocked/TargetsNotReal, both allowances unused.
Provider compatibility/reliability, remaining Increment 4 contracts, Increment
5 coordination/concurrency/recovery, Increment 6 remote/CI reconciliation and
Increment 7 packaged self-hosting remain release gates. Export, analytics and
redesign are optional. Preserve ledger, failures and retained roots.

### Selected contract

- Protected bodyless GET /api/runs/{runId}/collaboration-history/{messageId}/attempt,
  one new mediator query and MVC endpoint, API-owned transport. Fixed shared
  404 collaboration_attempt.not_found for unknown run or missing/foreign source;
  fixed 409 collaboration_attempt.unavailable for unusable source ID/sequence.
  Other database/materialization faults propagate; no rejected-value echo.
- Return run/source IDs, exact source sequence text, closed status
  NoAgentAttempt/Unavailable/Available and a nullable attempt header. Read only
  the exact source's metadata and reference, never summary/content JSON. Known
  version-1.0/type and recognized Simulated/HostConstructed/HumanSubmitted
  provenance mean NoAgentAttempt under this attribution contract, not absence
  of every attempt (a simulated message can have a simulated attempt).
  Unknown version/type/provenance is Unavailable, with no target disclosed.
- ProviderObserved requires a recognized Agent actor with non-null known role
  and provider and its own nonempty AttemptId. Resolve at most that single row
  in this run. Missing/foreign/non-Agent/invalid identity or a role/provider
  mismatch is Unavailable with no attempt ID or metadata. Never substitute,
  traverse repairs or inputs, drain history, or resolve a reply parent here.
- Available requires the existing AgentAttemptIdentity.IsCoherent semantics
  unchanged, plus exact source-actor matching and a positive attempt number.
  Preserve supported role/provider pairs, contract-to-role validation and the
  complete assignment-snapshot validation, including malformed max-turn storage.
  Do not invent a weaker identity check just for this surface.
- The header contains only attempt ID/number, role, provider, response contract,
  recorded status, claimed UTC and nullable dispatched/completed UTC times.
  No outcome, model/effort/profile text, session IDs, checkpoints, raw JSON,
  prompts, arguments, paths, artifacts, token/account data or input references.
  Describe recorded attribution, not current execution, approval, full sealed
  evidence, provider capability or provider reliability.
- Query untracked scalar projections only. Classify stored enums in SQL and
  never materialize unknown strings into enums. Read assignment-validation
  strings only within fixed byte caps before materialization, retaining a flag
  that distinguishes rejected oversized storage from legitimate null. Preserve
  the exact max-turn storage reader, including INTEGER versus TEXT/REAL/BLOB;
  never coerce malformed storage into a valid or absent request. Validate all
  exact existing character/semantic limits before admitting the header. No full
  entity or unrelated attempt text/JSON is selected; no external call or write.

### Scope, ownership and evidence

Allow one narrow read-policy extraction: Domain's existing assignment-snapshot
validation may be extracted without semantic change so Attempt.GetAssignmentSnapshot
and this projection use one policy; AgentAttemptIdentity may gain a scalar
overload used by its existing Attempt overload. Prove equivalence, retaining
all existing callers' behavior. Put new shared read-policy types in explicit
policy folders, not a generic framework. No Domain write/claim/dispatch policy
change, persistence/mapping/migration/dependency change or broad refactor.
If EF cannot preserve the exact storage semantics within the bounded projection,
stop with evidence rather than add raw SQL to Application or weaken admission.

Add explicit Inspect recorded attempt to each history message, with one
inspector, current close and same-source retry. Use history-open and selection
owned lifetimes, including serial-guarded updates for same-batch replacement.
No automatic reads, key/remount/reset effects, polling, live hooks or mutation
controls. Keep paging, reply inspector, receipt and current evidence readers
unchanged in behavior; both inspections must remain independent.
Decoder checks exact selected run/source/sequence, closed status/header shape,
recognized source provenance, Agent actor matching, role/contract compatibility,
positive bounded attempt number and valid timestamps. Render fixed labels/plain
text. Invalid transport is one whole-inspection error, never partial metadata.

Allowed: new query/endpoint/transport, narrow identity/read-policy extraction,
generated client/factory, history inspector/hook/decoder/styles, focused tests,
existing collaboration journey extension, additive ADR-0037 and narrow index,
engineering-context/cockpit-specification/roadmap/current-work updates.
Preserve this planner selection byte for byte. No providers, allowances, ledger
reset, retained-root cleanup, authority, scheduler, recovery or remote feature.

Start with meaningful failing regressions. Real SQLite tests must prove exact
own-link/run scope, duplicate same-role alternatives not substituted, all status
shapes, actor mismatch, unsupported identity/contract/snapshot, unknown enums,
large/NUL storage bounded before reading, exact max-turn storage kinds, no writes
and fault propagation. Prove extracted policy equivalence and test existing
callers. API proves auth, fixed errors, wire shape and source Int64 boundaries.
UI proves whole-response rejection and pending/settled stale callbacks including
A-B-A, close/reopen, reload/unmount and same-batch changes.
Journey uses actual durable AttemptId and compares exact rendered header facts
with stored rows, repeating after browser reload/reopen. Keep receipt, paging,
reply and no-write/no-provider/no-commit assertions; no seeding or provider launch.

Run affected checks then clean serial build, full Domain/Application/Api/
Architecture/Vitest, typecheck/lint/build, strict e2e tsc, harness and one canonical
e2e:all; affected format, reproducible client, links/hygiene/secrets/audits.
Retain unchanged Infrastructure results unless scope changes. Use a small
meaningful mutation set for link/identity/bounds/lifetime guards; preserve all
failures and survivors, with no unchanged rerun loop for green.
Return one unstaged, uncommitted, unpushed combined diff for Codex review.
No commit/push or subsequent slice is authorized by this selection.

## Previous selection (2026-10-12): inspect one recorded historical reply parent

### Review decision (2026-10-12): GO for the corrected combined diff

Codex verified main HEAD/local/live origin/main at
e8ae2da199ea21756a29f054677191ff1bd9bf48, ahead/behind 0/0, empty index.
The combined diff has 42 actual files (19 modified, 23 untracked when expanded
with --untracked-files=all). The executor preserved the NO-GO planner record
SHA-256 3aad67ebdeaf4685b176dd1a28f49eb676ba11e49d9107b0b5b904586d270b8d
before this review decision update.

The single NO-GO finding is closed: useCollaborationReply.close now checks its
inspector owner and guards the selection update by its captured serial. An
ended inspector cannot close a successor, including a replacement queued in
the same React batch. Durable regressions cover settled/pending successors,
A-B-A, close/reopen, batching and current close behavior. The executor records
ten failing-first regressions and a killed serial-guard mutant. Codex independently
ran the corrected hook and inspector suites: 46/46 passed. The original ignored
probe remains evidence; no temporary source is included in the reviewed diff.

Earlier independent checks passed: Application CollaborationHistory 250/250
and four frontend reply suites 183/183. Code review
supports the exact source/parent scope, earlier Int64 order, shared bounded
content admission, fixed errors, decoder coherence and journey's actual parent
outside the page plus post-reload assertions. The correction's full Vitest
3167/3167, typecheck/lint/build, strict e2e tsc, harness 89/89 and canonical
Chromium 13/journeys 5 are executor evidence, not newly rerun reviewer evidence.
Backend and generated-client checks are retained. The earlier relocation is narrow;
the missing original red-first run and mutation survivor remain disclosed.
Malformed GUID-column materialization surfacing as a fault is an accepted
limitation here, not permission to swallow database faults. Existing advisory,
console warnings and Blocked/TargetsNotReal remain unchanged.

Authorize publication of this exact reviewed combined diff, including this
planner decision update, in the same executor chat: one substantive commit,
normal fast-forward push to origin/main, live remote verification and one SHA
report. No additional checks or separate closure edit/review are required.
Stop on changed identities, divergence or failed push; no force-push or repair
of remote history. No provider work, allowance use, ledger reset or root cleanup
is authorized. No subsequent slice is selected; Codex selects it immediately
after independently verifying publication.

### Verified publication and decision

Codex independently verified ADR-0035 publication e8ae2da199ea21756a29f054677191ff1bd9bf48,
parent 609edddbc03760491710f9d79563fcc5e1ac66c4, the substantive message and
Co-Authored-By trailer. The frozen manifest SHA-256 is
522e6d2c1e721ef0e38f98fdb0132a61f19669429cb0ec1b57cc547c6885a2fc.
All 49 paths match their working-tree SHA-256 and size and committed blob and
mode; the committed path set is exact. Main HEAD/local/live origin/main agree,
ahead/behind 0/0, index and working tree were clean. No validation was rerun.
The prior GO is fulfilled; no closure edit or approval round is needed.

Select a new Claude executor chat for one explicit, read-only inspection of a
historical message's own recorded reply parent. ADR-0003/0004 and Increments
5/7 require intelligible durable replay. ADR-0035 now exposes safe message
content, but not the relationship between a challenge, decision or review and
its parent. The live timeline resolves only parents in its latest-100 window;
it returns raw JSON and materializes stored enums, so it is not this reader.
CollaborationMessage.InReplyToMessageId and the existing reply policy provide
the durable identity and protocol semantics. No new persistence is needed.

This is a bounded advance toward linked replay, not complete replay or MVP
completion. Real-provider compatibility/reliability and remaining Increment 4
contracts, Increment 5 coordination/concurrency/recovery, Increment 6 scoped
remote/CI reconciliation and Increment 7 packaged self-hosting remain release
gates. History export, analytics and redesign are optional and not selected.
Real qualification remains Blocked/TargetsNotReal with both allowances unused;
preserve the ledger, historical failures and retained fixture roots.

### Selected contract and scope

- Add protected bodyless GET /api/runs/{runId}/collaboration-history/{messageId}/reply
  through a new mediator query and one MVC endpoint, with API-owned transport.
  Unknown run or missing/foreign source is a fixed shared 404 with no rejected
  value echo; an unusable stored source identity/sequence is a fixed shared 409
  collaboration_reply.unavailable. Return run/source identities, exact source sequence text, a closed
  status (None, Unavailable, Available), and nullable parent entry.
- Read the exact source's header/reference only, scoped by run and message.
  None means a recognized version-1.0 Proposal with no reply reference. An
  unknown source type/version, missing required
  reference, empty/self/missing/foreign/forward target or disallowed parent type
  yields Unavailable and no target identity/content. Never substitute a latest,
  same-role or same-type message. Resolve at most one parent, by its stored ID
  within this run, strictly earlier, version 1.0, with the existing Domain
  CollaborationMessageReplyPolicy.Evaluate; no chain, search or page draining.
- Available describes that recorded protocol relationship only. Its one parent
  uses ADR-0035's safe entry disclosure and content admission unchanged: SQL enum
  classification, 1800/15000-byte BLOB guards, exact character limits, existing
  content policy, and header-only Unavailable content. Link availability and
  content availability are distinct. No raw JSON or foreign/damaged pointer
  disclosure. Query faults propagate; no broad catch converts them to a status.
- Add an explicit per-message Inspect recorded reply action in Recorded
  collaboration, with one inspector, close and same-source retry. Nothing reads
  on initial render or for every row. No recursive navigation, auto-follow,
  polling or cross-lifetime cache. Preserve loaded pages and cursor. Parent
  facts are plain text with the same exact labels; no live or mutation controls.
- Decoder validates the whole response against the selected run/source ID and
  observed exact sequence; status/target coherence, earlier exact Int64 order,
  recognized compatible parent type/version and all ADR-0035 entry rules.
  Owned lifetimes cover committed run, history open/reload and source selection;
  A-B-A, close/reopen, reload, replacement, null and unmount end prior work and
  stale callbacks. Do not use key/remount or effect-driven reset workarounds.

Allowed: the new query/endpoint/DTOs, small history-owned extraction only where
both readers must share the same admission policy, generated client/factory,
history inspector/hook/decoder/styles, focused tests and narrow extension of
the existing collaboration journey. Record additive ADR-0036 and narrow index,
engineering-context, cockpit-specification, roadmap and current-work changes.
Keep ADR-0035 paging, live timeline, evidence/artifact readers and receipts
unchanged in behavior. No Domain/schema/mapping/migration/dependency change,
generic history framework, authority, provider, scheduler, recovery or remote
feature. Preserve this planner selection byte for byte in the executor chat.
Stop and discuss if bounds require expansion or accepted policy reversal.

### Acceptance and return

Start with meaningful failing regressions. Real SQLite/API evidence must prove
auth, scoped 404, root versus broken reference, all reply-policy combinations,
exact own earlier target beyond loaded pages, gaps/2^53/Int64 boundaries,
unknown stored enums/version, unsafe/malformed/NUL/oversized parent content,
healthy header-only parent, SQL projection bounds and zero writes/external calls.
Frontend evidence must cover contradictory responses, exact text, explicit
request/retry/close, unchanged paging and stale ownership including A-B-A.
Extend the existing journey using its actual stored reply ID (no seeded rows),
inspect a parent absent from the initial page, assert its ID/sequence and exact
field labels/values, repeat after browser reload/reopen, and retain the no-write,
no-provider, no-commit and pinned receipt assertions.

Run affected checks first, then clean serial solution build, full Application,
Api, Architecture and Vitest, typecheck/lint/build, strict e2e tsc, harness and
one canonical e2e:all; affected format, reproducible generated client, links,
hygiene/secrets and audits. Retain unchanged Domain/Infrastructure results
explicitly; rerun if their scope changes. Use only a small meaningful mutation
selection for own-link, ordering and lifetime guards; report survivors honestly.
Preserve failures; no unchanged loop for green. Return one unstaged,
uncommitted, unpushed combined diff with current-work evidence for Codex review.
This selection grants no commit/push authority and selects no subsequent slice.

## Previous selection (2026-10-10): historical collaboration messages with bounded safe content

### Review decision (2026-10-10): GO for the corrected combined diff

Codex re-reviewed the combined diff on main at 609edddbc03760491710f9d79563fcc5e1ac66c4
(HEAD/local/live origin/main agree, 0/0, empty index; 15 modified and 34 new paths).
The three prior NO-GO findings are closed: emitted SQLite guards use
length(CAST(column AS BLOB)) with 1800/15000-byte caps; real-SQLite NUL
regressions replay the actual projection; Available transport requires distinct,
provenance-consistent participants while header-only Unavailable rows remain;
the journey asserts every fixed field label/value and reopens after browser reload.
Independent correction checks passed: Application 82, Api 88, Architecture 7
and frontend 162. The executor records the rerun full validation, canonical
Chromium 13/journeys 5, and retained checks in current-work.md.

The five-message UI page and byte-cap tradeoff are accepted: some text over
600/5000 UTF-16 units can be read within the fixed byte caps, then rejected
before parsing by the unchanged policy. Original red-first evidence remains
absent; the reported mutation survivors prove only scoped redundancy, and all
lifetime guards remain. Full linked replay and MVP/increment completion remain
open. Real qualification stays Blocked/TargetsNotReal, both allowances unused;
ledger, historical failures and retained fixture roots remain untouched.

Authorize the executor to commit this reviewed combined diff and push main by
normal fast-forward, verify the delivered SHA against live origin/main, and
report once. No new checks or separate closure edit/review are required for
publication. Stop on a changed diff, divergent remote or failed push. Codex
selects the next bounded slice after independently verifying publication;
the executor must not select it. No next slice is selected yet.

Codex verified publication 609edddbc03760491710f9d79563fcc5e1ac66c4,
parent 09455eec7b4303039d8d6ad78befdfbdf744fa02, message
`test: drain cockpit handlers before removing interception`, exactly five
reviewed paths and the preserved planner hash. HEAD/local/live origin/main
agree on main, ahead/behind 0/0, index and checkout were clean. Independent
review established the installed Playwright mechanism, red-first 12/14 versus
corrected 14/14, full harness 89/89 and strict e2e tsc over 42 sources. The
executor's canonical result is Chromium 13 and journeys 5; earlier failures
and uncertainty about their historical causes remain recorded.

The owner requires immediate next-slice planning after verified publication
and one commit/push/report step after GO, without a separate closure review.
AGENTS.md and the slice-start template record that workflow. Their owner-requested
edits are included in this combined diff. The review decision above controls
publication; keep this slice's corrections and publication in the same Claude
executor chat.

### Evidence and outcome

ADR-0003/0004 and Increment 5/7 require access to the durable conversation and
replay after restart. Historical detail currently exposes metadata, the pinned
receipt and event headers; it cannot show the recorded proposals, challenges,
decisions or reviews. GetCollaborationTimelineQueryHandler exposes only the
latest 100 messages, through a live-event-driven hook, with numeric sequences
and raw StructuredContentJson. Reusing it would miss older messages and its
enum/ParticipantIdentity materialization can fail on unknown stored values.

Select a separate, on-demand, bounded historical collaboration browser. The
existing collaboration_messages (RunId, Sequence) index supports its exclusive
cursor; sequences are global Int64 values, so gaps are valid and the wire uses
canonical decimal strings. Existing Domain content policy bounds summary to
600 characters, JSON to 5000 and fields to 900, with a closed per-type schema.
SQLite HasMaxLength alone does not enforce those limits on stored rows.
This slice reuses the existing content policy, not raw transcript parsing.

### Selected contract

- Protected bodyless GET /api/runs/{runId}/collaboration-history via a new
  mediator query and API-owned response graph; unknown run shared 404, existing
  empty run empty page. limit default 10, range 1..20, refuse rather than clamp;
  beforeSequence optional exact canonical positive ASCII Int64 decimal string
  (blank absent). Fixed shared typed 400/404/409, no rejected-value echo.
- Untracked reads of this run only, descending sequence, exclusive before,
  Take(limit + 1), no offset/count/draining/full entities. HasMore from the extra
  row; next cursor exactly the last admitted sequence when more exists. No
  atomic snapshot or total claim. Selected empty identity/nonpositive sequence
  gives fixed 409 collaboration_history.unavailable; never a partial page.
- Entries: run/message IDs, exact sequence string, UTC occurrence time, closed
  type/version/provenance disclosures, operation-owned actor and recipient,
  content status, nullable summary and a list of validated name/value fields.
  Known protocol version is exactly 1.0; unknown metadata becomes fixed
  Unrecognized rather than raw stored text. Classify stored enums in SQL;
  do not materialize unknown enums or construct invalid ParticipantIdentity.
- Content Available requires recognized version/type/provenance, coherent
  actor/recipient disclosures, IsSafeSummary and the existing Domain
  CollaborationMessageContentPolicy.Validate. Read summary/JSON only with
  database-enforced length guards before materialization; verify limits again
  before parsing. Handle expected content-validation failure per row. Unknown,
  malformed, unsafe or over-bound content retains its header with Unavailable,
  null summary and no fields. Healthy neighbors remain. Never truncate, repair
  or expose malformed content, raw JSON, secret sentinels or unknown field names.
  Do not turn arbitrary query faults into content-unavailable or shared 409.
- This is recorded content, not a fresh approval, validated author authority or
  provider-success proof. No attempt/evidence/artifact lookup, reply resolution,
  current-state enrichment or new protocol/envelope fields. No AttemptId or
  reply target in this first response; full linked replay remains open.
- Add collapsed Recorded collaboration inside ProjectRunHistoryDetail. Open
  one page; older, same-cursor retry and fresh reload are explicit. Render only
  text with fixed labels for admitted types/fields and an explicit unavailable
  state per row. No markdown/HTML rendering, live hooks, polling, automatic
  retries, persistent browser cache or mutation/configuration controls.
- Reuse owned lifetimes for committed run/open/reload; stale reads/actions,
  A-B-A, close/reopen, null and unmount cannot cross lifetimes. Validate complete
  pages: exact run, unique nonempty IDs, exact Int64 order/cursor, page bound,
  dates, closed metadata, content shape/limits and continuation coherence.
  Invalid transport is a whole-page error; a valid Unavailable row is retained.

Allowed: operation-local query/binders/transport, generated client/factory,
historical detail component/hook/decoder/style, focused tests, narrow extension
of the existing collaboration journey, ADR-0035 and narrow decision index,
engineering-context, cockpit-specification, roadmap and current-work updates.
Preserve receipts, event-history and live timeline behavior. No Domain/schema/
mapping/migration change, dependencies, generic history framework, provider
execution, authority/recovery/scheduler change, remote/CI feature or root cleanup.
Stop and discuss evidence if these bounds cannot be met; do not broaden scope.

### Acceptance, remaining gates and publication

Prove auth, fixed safe errors, run isolation, empty pages, exclusive cursor/
lookahead/appends/gaps, 2^53 and Int64 boundaries, all current message schemas,
unknown enums/version/actors, malformed/duplicate/extra fields, unsafe summary/
fields and oversized persisted text without selecting that text. Inspect real
SQLite SQL bounds/projection; reads write nothing and call no external port.
Cover UI ownership, coherent/unavailable/error pages, retry/reload and hostile
text. Extend the prior-delivery journey to inspect the older run's actual durable
messages across paging/reopen/reload, retaining receipt and no-write/no-provider/
no-commit assertions. Use a small meaningful red-first/mutation selection;
report real kills, survivors and restored source identities without full-tree
mutation work. Run affected checks, then serial solution build, full Application/
Api/Architecture/Vitest, typecheck/lint/build, strict e2e tsc, harness and one
canonical e2e:all, affected format, reproducible client generation, links,
hygiene/secrets and audits. Retain unchanged Domain/Infrastructure scope.
Stop/report unrelated failures; no unchanged rerun loop for green.

Return one unstaged, uncommitted, unpushed combined diff for Codex GO/NO-GO.
After GO the executor commits/pushes that reviewed diff and reports the verified
SHA once; no separate closure edit. Codex immediately plans the next slice.
Real qualification remains Blocked/TargetsNotReal, both allowances unused;
preserve historical failures, ledger and retained roots. Full replay, unproven
Increment 4 contracts, Increment 5 autonomous coordination/concurrency/recovery,
Increment 6 remote/CI reconciliation and Increment 7 packaged self-hosting remain
release gates. Export/analytics/redesign are optional and not selected.

## Previous selection (2026-10-10): bounded CockpitGate failure diagnosis and correction

Codex verified published closure 09455eec7b4303039d8d6ad78befdfbdf744fa02,
parent 735f1da86d8967771e1a666026adfe9990346268, message
`docs: close recorded event headers delivery` and its Co-Authored-By trailer.
Main, HEAD/local/live origin/main agree, ahead/behind 0/0, index and checkout
were clean. The two closure paths, blobs and modes match the approved manifest
(SHA-256 b9359f89...8610). No runtime validation was rerun for this verification.

Select one new Claude executor chat for **diagnosing and, only with reproduced
mechanism evidence, correcting the project-selection CockpitGate failure**.
This selection modifies only this planner record. No staging, commit or push is
authorized. Codex retains planning and final review; no other slice is selected.

### Why this boundary now

The canonical Chromium failure stops the chained journey stage before it runs.
The same `Route is already handled!` signature is retained in earlier deliveries,
including ADR-0025; an isolated pass does not establish or remove its cause.
Restoring trustworthy validation of project isolation is a concrete prerequisite
for reliable MVP evidence, rather than a general test cleanup. The earlier gate
correction addressed swallowed fulfill failures stranding teardown, not a proven
cause of this signature. Preserve that correction and its uncertainty.

Current CockpitGate forwards authenticated real host responses, can hold or fail
their delivery, tracks active handlers, releases holds and waits before closing
the context; genuine failures propagate. The existing fake and Chromium scenarios
cover delivery, cancellation and fulfill rejection, but do not reconstruct this
intermittent event order. The spec retains A/B late-result isolation assertions.
Read the installed Playwright implementation and original failure excerpts as
evidence; do not infer a diagnosis from an error name or manufacture one by
making an unrelated fake throw the same text.

### Scope and stop gates

Allowed: e2e/harness/cockpitGate.ts, cockpitGate.test.ts, gateRealScenario.ts and
a narrow project-selection.spec.ts adjustment only if the diagnosed mechanism
requires it. Temporary ignored diagnostics may record bounded, sanitized route
identity/phase and explicit event order. Add focused helpers under that harness
only when necessary. Update current-work.md with evidence and exact scope;
preserve this selection record byte for byte in the executor chat.

Recover the original failure excerpt if available; otherwise say it is unavailable.
Build an explicitly ordered reproduction against real Chromium/the installed
Playwright, then pin the relevant order deterministically in the harness tests.
A fix requires a failing regression on the unchanged gate and a passing control
after the smallest correction. Explain what proves the mechanism and what remains
unknown about the historical failure. If no mechanism can be demonstrated after
bounded diagnosis, return evidence and an actionable blocker without guessing,
changing unrelated code or repeatedly rerunning the canonical suite.

Keep real-host forwarding, hold/fail behavior, no A/B leakage, teardown-before-
context-close and visible genuine errors. No catch-and-ignore of route errors,
private SDK state writes, retries, sleep-based synchronization, increased timeouts,
skips or weaker assertions. No dependency/lockfile/configuration change, production
frontend/backend/API/client/schema change, broad harness rewrite or ADR reversal.
Stop if the fix requires those changes, cannot preserve the safety assertions,
or reveals a production isolation defect; bring evidence and a bounded alternative
to Codex before dependent work. A challenge is welcome; agreement grants neither
expanded scope nor publication.

### Acceptance and return

Run the focused deterministic and real-Chromium gate regressions first, preserving
their red-first evidence and existing failure-propagation controls. Then full
test:harness, strict standalone e2e tsc over every e2e/config source, typecheck,
lint, and one affected project-selection spec run. After an actual correction,
run one canonical test:e2e:all on normal authentication/host composition, without
reusing servers. Preserve any failure; no unchanged repeat for green. If Chromium
fails and skips journeys, report that chain result and at most one separate
journey-stage run as separately scoped evidence. Retain unchanged product/backend
tests, generated-client reproduction and dependency audits with their original
scope rather than presenting them as fresh checks. Confirm the generated client
and excluded files remain unchanged; check diff hygiene, sanitized diagnostics
and local Markdown links. Return an unstaged, uncommitted, unpushed diff or the
bounded blocker report, with changed files, commands/outcomes and concise excerpts.

This is validation support, not replay, autonomy or MVP completion. Release work
still includes real installed-provider qualification and unproven Increment 4
contracts, Increment 5 coordination/concurrency/leases/recovery and full replay,
Increment 6 scoped remote/CI reconciliation, and Increment 7 packaged self-hosting.
Optional history export/analytics/redesign are not selected. Real qualification
remains Blocked/TargetsNotReal, both allowances unused; preserve historical
failures, ledger and retained roots. No real provider invocation, CLI setup,
ledger reset, allowance consumption or retained-root cleanup is authorized.
Normal owned ephemeral browser-test fixture lifecycles remain permitted.

## Verified prior review decision (2026-10-10): recorded-event headers GO, published

The executor published the frozen 47-path diff as
735f1da86d8967771e1a666026adfe9990346268 (parent 7abe509cd777e52baea249df68bd20426ecdfbb3,
`feat: browse recorded event headers of historical runs`) by fast-forward. After the
push, HEAD, local origin/main and an independently queried live refs/heads/main agreed,
ahead/behind was 0/0, and the index and checkout were clean. Manifest identity, status,
hash, size, blob and mode checks passed before staging and after commit. The canonical
test:e2e:all failure remains unresolved, real qualification remains
Blocked/TargetsNotReal, and no next slice is selected. Codex independently verified
the delivered parent, HEAD/local/live remote agreement, 0/0 ahead/behind, empty
index and exactly these two modified closure documents. All 47 committed paths,
statuses, blobs and modes match the approved substantive manifest; the closure's
106 local links and text hygiene passed. **GO for this frozen two-document closure
diff only**, pending normal fast-forward publication. No runtime checks were
rerun for this documentation review. The paragraphs below are the substantive
pre-publication review record.

The owner asked Codex to finish anything interrupted by Claude's credit limit.
Codex found no unfinished feature implementation, completed the missing link
verification and independently reviewed the combined diff. Branch main,
HEAD/local/live origin/main are 7abe509cd777e52baea249df68bd20426ecdfbb3,
ahead/behind 0/0, index empty; 13 modified and 34 new paths. Nothing is published.

**GO for the frozen 47-path combined diff only**, including this planner review
record and the completed current-work entry. Publication must first match the
ignored review manifest's own hash, every path/status, raw hash, size, Git blob
and mode. A changed inventory or identity voids this GO and returns to review.
No next slice is selected, and no implementation or unrelated fix is authorized.

Independent evidence: serial solution build, 0 warnings/errors; Application
event-history 94/94; Api event-history 71/71; full Architecture 64/64; eight
frontend suites 150/150. The first sandbox frontend invocation failed module
imports with temporary-file ENOENT (28 tests ran); the same bounded command
outside that sandbox passed. All 344 pre-existing generated exports have identical
bodies to HEAD after line-ending normalization; exactly seven new exports were
added. The client still hashes to 1b6fb24b..., and 281 local Markdown links in nine
documents passed before this review update. Full executor checks retain their
recorded scope; unchanged checks were not rerun merely to obtain green.

Accept the UI's ten-header requests within the 1..50 host bound, conservative
validation of the lookahead row, operation-local canonical scalar binding and
the pinned constant allow-list. The SQL projection, exact Int64 transport,
closed disclosure and owned UI lifetimes satisfy the selected boundary. Neither
surviving mutant proves missing user-visible protection; retain both explanations.

The canonical test:e2e:all result remains **FAILED** (Chromium 12/13;
project-selection's CockpitGate route already handled; chained journeys skipped).
The separate journey 5/5 and isolated spec 1/1 are diagnostic evidence, not a
replacement canonical result. The gate/spec are unchanged and that spec does
not open historical detail; new client exports leave existing operations intact.
These facts and the focused safety checks support accepting this bounded feature,
but do not establish the intermittent failure's cause. Keep it unresolved; this
GO does not certify clean end-to-end validation or MVP/release acceptance.
Do not swallow that error, weaken assertions, fix the unrelated harness or rerun
unchanged validation for green under this publication prompt.

Real qualification remains Blocked/TargetsNotReal, both allowances unused.
All historical failures, ledger and retained fixture roots remain intact.

## Selected slice (2026-10-10): bounded recorded-event headers for historical runs

Codex independently verified closure
7abe509cd777e52baea249df68bd20426ecdfbb3, parent
5fd38cb0f0c6fc50c35d39e6af9c85cc2acf7193, message
`docs: close project run history delivery`. HEAD/local/live origin/main agree on
main, ahead/behind is 0/0, index and checkout were clean. Both modified documents,
their blob identities and unchanged file modes match the frozen closure manifest.
The initial review-script array comparison was malformed; the corrected check
confirmed the actual Git inventory without a repository discrepancy.

Select one new Claude executor chat for **on-demand recorded event headers in the
historical-run detail**. Codex retains planning and final review. This selection
changes only planner-handoff.md; implementation begins at the closure commit
with an empty index, no untracked files and only this planner record modified.
Carry that selection edit into the eventual reviewable combined diff. No staging,
commit or push is authorized; prior publication GOs apply only to their frozen
published inventories. Do not edit this planner record in the executor chat.

### Evidence and purpose

ADR-0003 and Increment 5/7 require durable history/replay. ADR-0033 exposes older
runs and receipts but leaves events inaccessible in that read-only detail.
GetRunEventsQueryHandler reads all later events without Take; its endpoint also
returns PayloadJson. parseEventSummary can fall back to raw payload JSON.
useCollaborationTimeline is a live-event-driven, latest-100 message reader rather
than a historical journal pager. Reusing those readers would introduce unbounded
reads or a different surface. Add a separate reader without changing consumers.

RunEvent.Sequence is a globally assigned SQLite Int64 key, and the existing
(RunId, Sequence) index supports this page. Sequences can contain gaps between
a run's events; timestamps are not ordering authority. Browser JSON numbers
cannot preserve the entire Int64 range, so this additive operation uses canonical
decimal strings for sequence values/cursors. The current record lacks several
envelope fields described by data-and-recovery.md; this header projection must
not fabricate versions, persistence times, causation or correlations. Full replay
and envelope completion remain separate release work.

### Selected contract

- Protected bodyless GET /api/runs/{runId}/event-history through a new mediator
  query, API-owned response graph and reproducible generated client. Unknown run
  is safe 404; existing empty run is an empty page. limit defaults to 20, accepts
  1..50 and is refused rather than clamped. beforeSequence is an optional string:
  omitted/blank is absent; otherwise canonical ASCII positive decimal, no leading
  zero, sign, fraction, exponent or padding, at most 9223372036854775807. Use fixed
  shared 400 errors without rejected-value disclosure and explicit error schemas.
- Select only this run's headers, untracked, newest first by Sequence, exclusive
  before cursor, Take(limit + 1), no offset/count/aggregate. HasMore uses the extra
  row; NextBeforeSequence is the last admitted sequence only when older rows remain.
  Appends between pages cannot repeat or skip older rows; reload reads current
  newest headers. Pages do not claim an atomic whole-run snapshot or frozen total.
- Page carries RunId, entries, HasMore and NextBeforeSequence. Each entry contains
  only run/event IDs, exact sequence string, occurrence UTC time, known EventType
  or fixed Unrecognized, and an operation-owned actor disclosure (kind/role/provider).
  Admit event types only by exact existing RunEventType constants; never return raw
  unknown values. Classify actor columns without materializing unknown enums or
  constructing invalid ParticipantIdentity. Preserve valid Agent-with-unknown-role
  records. Unknown or inconsistent actors become Unrecognized with null role/provider;
  retain that row and healthy neighbors. Never infer authority or provider success.
- Do not select/parse/return PayloadJson, AttemptId, message/artifact content, or
  current-state enrichment. Do not invent absent fields. If a selected persisted
  event has an empty identity or nonpositive sequence, return one fixed shared 409
  run_event_history.unavailable, no partial page. Unexpected faults retain the
  established pipeline; do not mask arbitrary database errors as unavailable.
- Add a collapsed Recorded events region inside ProjectRunHistoryDetail. Opening
  requests one page; older pages, same-cursor retry and fresh reload are explicit.
  Known labels and values are text. State clearly that only recorded headers are
  shown, newest first, with details omitted and no live/approval/completion claim.
  No selected-event controls, payload viewer, playback clock or live cockpit hook.
- Reuse owned lifetimes for committed run/open/reload; A-B-A, close/reopen, null,
  unmount, retained actions and overlapping reads cannot act on another lifetime.
  Validate the entire page, exact requested run, nonempty unique IDs, canonical
  Int64 sequence strings, strict descending order/cursor ceiling, size, dates,
  closed disclosures and continuation coherence. Compare sequences exactly, never
  through Number. Gaps are valid. Invalid pages are errors, never partial rows.
  A failed older page retains valid rows and retries the same cursor.

Allowed: operation-local query/validation/projection, endpoint/transport/binders,
client generation/factory, historical-detail event components/hooks/decoder/style,
focused tests, a narrow extension of the existing collaboration journey, ADR-0034
and narrow index/context/cockpit/roadmap/current-work updates. Preserve accepted
ADRs 0001..0033, AGENTS.md, slice-start-template.md and the existing receipt.
Exclude schema/mappings/Domain behavior, existing event/live/message consumers,
generic history frameworks, new dependencies, providers, mutation/recovery,
scheduler, Git/remote/CI actions, cleanup and full replay. Stop on an inability
to honor the bounds or exact disclosure without widening this scope.

### Acceptance and return

Red-first boundary regressions: auth/404/empty; multi-run/project isolation;
exclusive cursor/extra row/append between pages; Int64 and 2^53 boundaries;
malformed inputs and fixed typed 400/404/409; all known/unknown event/actor cases;
malformed payload and secret sentinels never selected or returned; real SQLite
SQL bounds, no writes/external calls; unknown rows retained; exact client roundtrip.
UI regressions cover page validation, lifecycle ownership, loading/empty/error,
older retry, reload and hostile text. Extend the existing older-delivery journey
through rendered controls to compare headers with durable rows across explicit
paging and reload, preserving all receipt/no-write/no-provider/no-commit assertions.
Use a small meaningful rebuilt mutation set, restored hashes and honest survivors.

Run affected tests first; then serial solution build, full Application/Api/
Architecture/Vitest, typecheck/lint/build, strict e2e tsc, harness followed by one
canonical test:e2e:all, affected formatting, second client generation, doc links,
hygiene/secrets and audits. Retain unchanged Domain/Infrastructure evidence with
its scope. Stop/report unrelated failures; no rerun loop for green or opportunistic
fix. Return one unstaged, uncommitted, unpushed combined diff with current-work.md,
exact inventory, commands/outcomes and fresh versus retained evidence.

This advances required history access but does not complete replay or the MVP.
All provider, coordination/recovery, remote publication and packaging gates below
remain. Optional export/analytics/redesign and test-reliability improvements are
not selected instead. Real qualification remains Blocked/TargetsNotReal, both
allowances unused. Preserve ledger/failures/retained roots; no real provider,
CLI setup, reset, allowance use or root cleanup is authorized.

## Verified closure decision (2026-10-10): substantive delivery and factual closure published

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
