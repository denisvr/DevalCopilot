# Planner/reviewer handoff

## Current action: correction 6 accepted; WIN-EXEC-PROFILE-01 accepted; GO for exact reviewed publication; KEEP SANDBOX ENABLED; KEEP SANDBOX ENABLED

### Correction 4 independent review / F attempt 2 proposal (2026-11-02)

Codex verified main, HEAD, local origin/main and fresh live origin/main at
e880c4da311f65d7688196105364f239b4f116f0; index
empty; 263 modified tracked paths and 60 untracked files. Incoming handoff SHA-256:
57FD99870D0430FD453FC2AA09E6CA3B9BD42C33D79C2E87AB3D13F15DBF506B.
Correction 4 is ACCEPTED at the proof-helper scope. This replaces the Correction 3
NO-GO and closes its actionable correction request; no further correction is
released. Earlier core findings and acceptance E remain closed at their reviewed
scopes. F is mandatory and NOT Met. No final slice acceptance or commit/push GO.

- WEP-F1 CLOSED: the exchange deadline begins before writes; blocked input,
  malformed/unsolicited/oversized output, producer failure and stale frame IDs
  cause terminal refusal with cancelled pipes. Closure is bounded and idempotent;
  unresolved closure preserves scratch and prevents the preparation manifest.
  An independent real FSI stopped-reader control with 4 MiB input refused in
  2014 ms against a 2000 ms deadline; later calls refused with the same error,
  and observed child exit permitted scratch removal. Closure covers this helper's
  own process, not a general descendant-containment mechanism.
- WEP-F2 CLOSED: admission facts are per lock entry; byte deduplication no longer
  hides required incompatibility. Both independently reproduced entry orders
  report selected=1 and incompatible=1. The controlled preparation tests refuse
  TargetIncompatible instead of Prepared. Committed-lock classification remains
  133 needed / 53 optional target exclusions / zero required-incompatible npm
  identities; that is selection evidence, not cache integrity evidence.
- WEP-V2 CLOSED: the fixture no longer generates/imports certificates or keys or
  calls a signer. It uses the installed SDK's in-memory signed archive layout
  with placeholder signature bytes. Explicit planner acceptance clarification:
  this is adequate for the helper's signed-layout content-hash normalization
  controls, not a valid cryptographic signature or trust/revocation test. It
  replaces the earlier demand for a validly signed synthetic fixture at this
  layer. Independent controls confirm normalization, signed-layout detection
  and raw-digest inequality; malformed/tamper controls remain. Ordinary NuGet
  restore signature/trust/revocation/audit requirements are unchanged. The two
  earlier real-archive observations retain their narrow scope; the other 88
  previously reported mismatches have NOT been validated.

Independent validation: the controlled full harness ran 48 cases, 47 passed,
zero failed, one linked-archive privilege skip, in 56.950 seconds. All five MJS
syntax checks and git diff --check passed (Git emitted line-ending notices).
Independent probes and their result are retained under
logs/planner-review/win-exec-profile-01/review-05/independent-results.json.
No real cache, provider, authentication, certificate/key generation, Sandbox or F
was used. The unchanged product/backend/frontend/Rust/journey matrix is retained
at its earlier scope; this is not a new full-diff acceptance claim.

Executor disclosures remain qualified: the removed signer ran twelve times in
Correction 3; effects on actual key stores are unknown, not proven residue.
Neither inspect those stores nor replay the signer. Thirteen reported disposable
scratch directories were not removed after the executor's permission refusal;
this review neither retries that deletion nor grants authority to clean unknown
paths. Maximum-size archive throughput and an actually unkillable child were not
demonstrated; deadline/refusal semantics remain the requirement.

### F attempt 4 accepted / final review consolidation (2026-11-02)

Fourth proof completed successfully and cleanup finished within its authorization.
F is Met; WEP-F5 CLOSED by the actual serial solution build. See current-work's
Planner-independent F attempt 4 evidence for exact commands, IDs, source binding,
package identities, negative controls, sealed output verification and limitations.
The real supervisor executed the solution build, 14 relevant tests, offline npm
installation and frontend build, all Passed/Exited 0 under v3 with complete I/O
and unchanged source fingerprint. Eight exports matched their sealed records.
The owner-requested Sandbox feature remains Enabled; zero guest processes and
all identified attempt staging/input copies removed. Host user PATH unchanged.

Consolidate reviews 1-7 and corrected-boundary evidence: WEP-R1/R2/R3/R4/R5,
WEP-V1/V2, WEP-F1/F2/F3/F4/F5/F6 are closed at their recorded scopes. A/C/G source,
closed contracts, additive nullable migration/inspection compatibility and shell/UI
failure guidance are reviewed with the retained full executor matrix; B/D and E
have independent boundary controls, and F now has actual production worktree
proof. Final review rechecked current envelope/adapter failure paths, startup
classification/ordering/safe frames and native protocol bounds alongside the
versioned v3 change and current tracked/untracked scope. No new blocking finding
was identified. Do not reinterpret this as independent replay of every suite or
proof of installed-provider/historical-failure behavior.

The unchanged broad executor matrix remains proportional evidence, plus fresh
80-case independent v3 controls and the actual F proof. Normal project security
settings stayed enabled; no fresh external vulnerability feed or independent
signature/revocation qualification is claimed by an offline toolchain run. The
retained Vite chunk advisory is nonblocking within this slice. General recovery,
ADR-0050, installed-provider qualification, exact-head CI and the successful green
draft-PR/self-hosting exit demonstration remain separate incomplete outcomes.

Final status: slice accepted. Publication GO is limited to the exact reviewed
content identity in the external planner approval record and outgoing complete
prompt, with expected sole parent e880c4da311f65d7688196105364f239b4f116f0.
The executor must verify the approved staged tree/path set, commit that content,
push main normally to origin/main and verify the live ref. No force-push or
history reconciliation. A mismatch, changed substantive diff or divergent remote
stops publication for planner review. No additional source/docs changes belong
in this exact-content GO; current-work already records final proof and acceptance. The real index
is still empty, main/HEAD/local and fresh live origin/main remain the expected
parent e880c4da311f65d7688196105364f239b4f116f0, with 263 modified tracked and
63 untracked slice paths. No commit/push has occurred. Only the exact reviewed
content may receive GO, and a material change returns for review. No new provider,
Sandbox guest/cache allowance or implementation slice is dispatched here.

### Correction 6 independent review / fourth F plan (2026-11-02)

Fresh Git: main, HEAD/local origin/main and live origin/main all
e880c4da311f65d7688196105364f239b4f116f0; empty index; 263 modified tracked
paths and 63 untracked files. Incoming handoff SHA-256:
1C88FF484901FF712937B5327604180C51D0D63822D2B64106D75D1BC6621478.

Correction 6 ACCEPTED; WEP-F6 CLOSED for the TWO named SDK first-use effects.
Source review confirms literal false values only for dotnet.exe, retained WEP-F4
separator, current v3 and historical v1/v2 constants. The test-only prelaunch
guard refuses absent/wrong flags before delegation; it is not a production
isolation or defense-in-depth claim. Do not add a second production policy layer
for this correction: the fixed provider supplies the accepted contract. ADR-0074
and current pointers preserve earlier accepted decisions. History coverage asserts
v1/v2/null labels/status/completion unchanged and one new v3 execution only.

Independent checks: 14 provider controls and 66 hosted toolchain/supervisor-history/
guard cases PASSED, zero failed/skipped. Launched already-built assemblies via
vstest, no repository build/restore command, with BOTH outer flags false, a short
validated process-only PATH and owned CLI home. Hosted SDK fixtures were reviewed
for the same guarded v3 environment before execution. Positive real pack/build/
restore/test, package-relative tool lookup, source/profile/I/O/scratch/history
and pure prelaunch negatives passed. No deliberately unsafe SDK mutation ran.
User PATH read-only identity stayed exactly 1,176 characters / 14 entries /
f4bb293f2f983b1f017ff9a8af2dc81f08590dbc0d94339f5054daeb92e4c12b.
Results/TRX under logs/planner-review/win-exec-profile-01/review-07/.
Executor's clean serial build, format, Architecture 232 and unchanged API client
remain reported evidence, not independently replayed; unchanged broad matrix is
retained at its actual scopes. No F/guest, real cache, provider/authentication,
certificate/store/trust operation or personal PATH change occurred in this review.

Review qualification: current-work's delivery synopsis incorrectly claimed the
previous SDK executions generated a certificate. Planner corrected ONLY that
phrase to a possible setup operation with actual store effects unobserved; no
product code/test changed. SDK 10.0.401 source already cited in the prior review
supports the control mechanism. Neither the older origin-source citation nor
this review proves historical certificate creation or universal side-effect
confinement. No test rerun is needed for that evidence-wording correction.

Owner PATH repair was separately approved and completed before this correction:
80 absent exact hosted-fixture entries removed, other 14 retained with original
raw representation/order/value kind; protected backup round-trip verified,
machine/process PATH unchanged. Raw value has 1,174 characters; expanded value
1,176. Earlier initial refusal was raw-versus-expanded representation, no write
or concurrent change. Completion record is review-06/path-repair-completion.json.
The active executor handoff was deliberately preserved during that owner action.

No further implementation correction is dispatched. E and earlier findings stay
closed at reviewed scopes. WEP-F5 and actual F remain pending; NO final acceptance
or commit/push GO. Next coherent outcome is actual isolated F with corrected SDK
payload, v3 and serial project builds, then review its complete evidence.

FOURTH-ATTEMPT PLAN AUTHORIZED: owner answered "Autorizar esta quarta tentativa".
Fresh preflight matches the recorded 263 modified/63 untracked paths, empty
index and live baseline; CIM Enabled, zero Sandbox processes. Incoming handoff
SHA-256 F3965ED73FF5CB7B39D88CDD6D9182BA8AC87B1DE99B901F5C55524A1B65DA15.
1. Reconcile this baseline/index/counts and freeze exact current source/tooling
   identities; never stage/commit/push the real repository. Keep historical proof
   launchers/results immutable. Select the currently built production runtime
   closure, not stale pre-v3 binaries. Prepare new ignored owned harness/driver.
2. Keep Windows Sandbox ENABLED as instructed. One NEW owned guest, 8192 MiB,
   at most 90 minutes from launch; networking/vGPU/clipboard/audio/video/printer
   redirection disabled; only owned input RO/results RW. Refuse an unrelated
   existing guest. Verify readiness and fresh direct/descendant denial plus
   owned positive controls BEFORE real-cache access. No automatic reboot.
3. After that gate, use the reviewed dependency helper with explicit installed
   SDK 10.0.401 for original committed-lock 99 NuGet/133 applicable npm identities,
   retaining 53 optional exclusions, and exactly the three SDK-derived runtime
   10.0.12 archives/hash identities recorded in sdk-diagnostic-01. Include installed
   WindowsDesktop.App.Ref/10.0.12 and the selected SDK/runtime/apphost/ref, Node/npm
   and Git six-file PE closure. No downloads, credentials/config copies, cache
   writes, source searches, expanded package-cache trees or repacking. Recheck
   all archive identities; record SDK supplements separately from lockfile packages.
4. Bind every payload byte in a manifest (bounds 2 GiB tools / 5 GiB dependencies /
   1 GiB source / 64 MiB exported reports). Make a source-snapshot commit ONLY in
   the disposable guest copy for the source reader's 128-change bound. Read it
   using the REAL Git reader, no fabricated source binding or trust/bound changes.
5. Through the production supervisor and unmodified windows-verification-v3,
   execute the solution build, Infrastructure WindowsVerificationEnvironmentProviderTests
   filter, frontend npm ci --offline and npm --offline run build. Add -m:1 to .NET
   build/test recipes (WEP-F5); keep build servers/shared compilation off. Each
   .NET execution restores LOCKED from the explicit owned local feed within that
   SAME execution, then builds/tests; no reliance on an earlier released cache.
   Every outer SDK invocation uses both first-use flags false. Any proof prelaunch
   guard may refuse missing/wrong v3 flags but must never repair the environment
   or mask a wrong profile/version. Require v3 and actual process/I/O/source facts.
6. Keep normal NuGet signature/trust/revocation/audit/warnings-as-errors. A success
   exit alone does not establish unavailable security evidence. No offline
   revocation override, disabled audit or NSwag generation, skipped tests or ambient fallback
   to obtain green. Missing dependency/security evidence or failed isolation/recipe
   is a precise blocker; stop dependent commands, no blind retries. F requires
   the actual successful commands and complete sealed outputs, not an escalation.
7. On success/blockage, export bounded evidence, close only the identified owned
   guest/controls and remove only that attempt's admitted staging/copies. KEEP
   THE FEATURE ENABLED. Do not clean unknown scratch, PATH or certificate/key stores.
   No real providers, authentication, general recovery or publication work.

The prior three guest attempts are consumed. Keeping the feature enabled does not
renew guest/private-cache authority. The owner has approved this complete fourth plan; it releases ONLY this one
attempt. Keeping the feature enabled does not authorize an additional guest. A successful F still returns for final
diff/acceptance review before any separate exact-content publication GO.

### Correction 5 review / correction 6 decision (2026-11-02)

Independent Git reconciliation: main, HEAD/local origin/main and fresh live
origin/main e880c4da311f65d7688196105364f239b4f116f0; empty index; 263 modified
tracked paths and 60 untracked files. Incoming handoff hash:
8FFE0DD2BF31080956449F80BC02F84143C8B54C5A486D75F19F3F0DDB2FDB5B.

WEP-F4 CLOSED at the reviewed compatibility scope. Source review confirms only
NUGET_PACKAGES gains a separator, with the same freshly owned directory and v2
key set/lifetime. The new hosted fixture checks the REAL SDK-derived
NuGetPackageRoot, direct package-relative concatenation, physical tool existence,
source/profile/I/O, sentinel absence and scratch release. Executor reported red,
green and guarded mutation evidence, provider 13/13, hosted/history 53/53,
Architecture 232 and serial solution build clean with byte-identical API client.
Those runs are executor evidence, NOT independently rerun. Planner checked code
and whitespace; it deliberately did not replay SDK tests after the new host effect
became known. WEP-F5 remains open for the actual isolated serial proof; F NOT Met.
No final GO. Existing E/earlier findings retain their reviewed scope only.

WEP-F6 OPEN, P1: first-use SDK setup mutates persistent host state. Executor paired
runs report user PATH growth with a fresh CLI home and no growth when
DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false. Independent READ-ONLY observation confirms
14,296 characters / 94 entries, SHA-256
85f89065addf10db190d63737e21ab5eabc3e9ce868bea05338a6d374c5d6d23.
Exactly 80 entries match the hosted fixture's GUID/GUID scratch layout and no
longer exist; eight additional dotnet-home tool entries have NOT been attributed
for removal and must remain, together with the other six entries. No raw user PATH
was logged, configuration modified, credential/store inspected or SDK launched.
Read-only summary and a repair proposal are under ignored review-06 artifacts.

The SDK 10.0.401 public source independently establishes both defaults:
Program.ProcessArgs reads DOTNET_ADD_GLOBAL_TOOLS_TO_PATH and
DOTNET_GENERATE_ASPNET_CERTIFICATE with default true; first-time configuration
can add the tools path to the USER environment and invoke the ASP.NET development
certificate generator. This proves a possible second effect, NOT that any actual
certificate was created/persisted. Do NOT inspect real stores or run a negative
control on the real host to find out. Official references:
https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables
https://github.com/dotnet/sdk/blob/v10.0.401/src/Cli/dotnet/Program.cs
https://github.com/dotnet/sdk/blob/v10.0.401/src/Cli/Microsoft.DotNet.Configurer/DotnetFirstTimeUseConfigurer.cs

Executor challenge accepted: a new key is a versioned contract change. Planner
decision for correction 6: windows-verification-v3 = v2 plus EXACTLY two keys,
only for dotnet.exe: DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false and
DOTNET_GENERATE_ASPNET_CERTIFICATE=false. This prevents those automatic first-use
operations, not arbitrary filesystem/registry effects or an isolation boundary.
Keep every other value including the WEP-F4 separator, key, validation, scratch
lifetime, NuGet signature/trust/revocation/audit policy and authority unchanged.
Document in new accepted ADR-0074 (number currently free), extending ADR-0073 and
linking current-version pointers without rewriting history. Historical v1/v2/null
records retain profile/status/completion exactly, are never relabeled/replayed/
redispatched, and only new executions record v3. No data backfill or migration.

Continuing host SDK validation after identifying actual persistent writes was not
a safe way to satisfy required checks. Required validation does not justify
repeating a known unwanted effect. Correction 6 must establish safe process-only
runner flags and prepared child keys BEFORE any SDK invocation. If a check would
remove either guard, stop dependent host execution; use a pure prelaunch negative
control or a separately authorized isolated experiment. No repeated mutation that
writes the real user PATH/certificate store, and no automatic restoration of a
concurrently changed real PATH. This applies to fixture pack/build/test, validators,
formatters and the outer development CLI, not only the production supervisor.

CORRECTION 6 RELEASED to SAME Claude executor chat and SAME slice for WEP-F6 and
the versioned contract/tests/docs above. Preserve planner-handoff and all evidence.
Before any CLI command, prepare a short process-only PATH of validated selected
tools/system directories and set BOTH first-use flags false for that runner.
First implement/check the v3 provider and prelaunch guards without SDK execution;
then run controlled owned SDK fixtures with actual v3 child environments. Guard
against launching a hosted fixture if either flag is missing/wrong. Assert exact
per-executable keys, real restore/build/test and WEP-F4 tool lookup success,
complete I/O/source/profile and scratch release. Across repeated fresh executions,
compare a bounded read-only user PATH identity before/after (do not log raw value).
A change is a blocker, never a reason to reset it. Prove missing-flag mutations
fail before unsafe launch; do not claim that is a physical negative SDK experiment.
Use SDK source plus prelaunch flags/supported positive runs as certificate-control
evidence; no real-store enumeration or certificate generation/trust changes.
Extend hosted history tests for v1/v2/null/new-v3 preservation. Run proportional
provider/hosted/history/architecture/build/format checks once after focused checks;
retain unaffected frontend/Rust/journeys/helper evidence. No real caches, providers,
authentication, downloads, F/guest or historical qualification-evidence content.
Return WEP-F6 evidence and current-work unstaged/uncommitted/unpushed for review.
If controlled real validation cannot be run safely within those constraints,
return a precise blocker rather than the old profile's side effects.

Owner PATH repair is SEPARATE and PENDING permission. Concrete proposal removes
ONLY the 80 absent exact hosted-fixture entries, preserves the other 14 entries
byte-for-byte/in order plus original registry value kind, and changes neither
machine PATH nor current process PATH. Expected resulting length 1,176 and hash
f4bb293f2f983b1f017ff9a8af2dc81f08590dbc0d94339f5054daeb92e4c12b.
Prepared ignored repair-user-path.ps1 is hash-bound, refuses concurrent changes
or newly existing candidates, creates and verifies a current-user DPAPI-protected
backup outside Git before mutation, and verifies the exact result. It has NOT
been executed. No folder, old executor scratch, unknown key/certificate or other
PATH entry is authorized for cleanup. Ask the owner directly for this concrete
repair; no new routine scope decision is needed for correction 6.

Keep Windows Sandbox ENABLED at the owner's instruction. No new guest/F allowance
or publication authority is created by this review. F5 and real F remain pending;
general recovery, ADR-0050 and provider/publication scope remain separate.

### F attempt 3 review / correction 5 decision (2026-11-02)

The owner returned after reboot and explicitly superseded feature-disable cleanup:
KEEP SANDBOX ENABLED until all work is finished and we confirm it is no longer
needed. Close owned guests/controls after each attempt. This overrides EVERY
older feature-disable instruction below, not guest-count/cache/publication limits.

Third attempt launched 15:08:00 UTC; production build completed 15:13:01 UTC.
Fresh direct/descendant positive/negative isolation controls passed before caches.
The accepted helper again prepared 99 locked NuGet and 133 applicable npm identities
with zero missing/mismatched/invalid/incompatible, excluding 53 optional entries.
The three expressly authorized runtime 10.0.12 archives matched the diagnostic
hashes and were admitted separately as an SDK-derived supplement, not lockfile
packages. Installed WindowsDesktop.App.Ref/10.0.12 was included. All 12,054
payload files matched their manifest in the guest: 845,089,726 tool bytes,
245,354,437 dependency bytes and 49,783,988 source bytes. Source snapshot commit
ONLY in the guest: 47bec70b4ae707ffb7414d117863c621db758ef9; clean real-reader
fingerprint 9e6a16360a8678a146de8ef18a4ce037757cf68b4fc4a3e37c0784ccad34e9f3.

WEP-F3 CLOSED at preparation scope: all solution projects restored with the
corrected payload, zero warnings; prior missing SDK-pack errors did not recur.
That is not cryptographic signature/trust/revocation/audit qualification.
The build then failed (exit 1) for the TWO findings below. F remains mandatory
and NOT Met; tests/frontend did not run. No final slice acceptance or commit/push GO.

- WEP-F4 OPEN, P1 acceptance blocker: real shared NSwag generation concatenates
  NuGetPackageRoot with nswag.msbuild without a separator. Recorded attempted path
  ends in nuget-packagesnswag.msbuild/14.7.1/tools/Net100/dotnet-nswag.dll. Static
  inspection ONLY of the owned staged Devalente.Shared.OpenApi.NSwag 0.2.0 archive
  confirms its buildTransitive target uses that exact concatenation. The current
  v2 provider supplies NUGET_PACKAGES without a trailing directory separator.
  This establishes an integration failure and a plausible narrow correction,
  not proof that appending the separator fixes the installed SDK's derived
  NuGetPackageRoot. Reproduce that derivation through the production supervisor
  against an owned SDK-packed fixture before changing the provider. Do not
  patch/repack the private dependency, disable generation or change dependencies.
- WEP-F5 OPEN, proof recipe: CS2012 reports simultaneous writers of Api/obj/Debug/
  net10.0/DevalCopilot.Api.dll. Static project references pass SkipNSwag=True from
  API integration tests and two hosts, while the solution also builds the API
  normally. Distinct global-property instances sharing outputs are a supported
  explanation, not an independently reproduced historical race cause. Planner
  selects -m:1 for future solution build/test proof recipes to serialize project
  builds. Keep real generation, warnings-as-errors and all gates. No project graph
  rewrite or historical evidence edit is authorized; closure needs the actual
  future serial proof, not a claimed fix from static inspection alone.

Production supervisor recorded Failed/Exited, exit 1, v2, complete I/O, identical
checkpoint/completion fingerprint, and two untruncated sealed artifacts. Their
exported lengths/SHA-256 were independently matched. Outer harness exit 2 is now
observed (retained process handle); no outer exit-0 claim. It stopped at the first
failed recipe. The owned guest shutdown completed with zero remaining Sandbox
processes; owned controls stopped; identified attempt staging/input copies were
removed after retaining bounded results under
logs/planner-review/win-exec-profile-01/f-proof-03/. CIM confirms feature Enabled
(InstallState 1), as requested. No automatic restart or feature-disable occurred.
Fresh live origin/main remains e880c4da311f65d7688196105364f239b4f116f0; index
empty; main/HEAD/local origin/main unchanged, 263 modified tracked/60 untracked.

CORRECTION 5 IS RELEASED to the SAME Claude executor chat, SAME slice, for WEP-F4
only and directly affected tests/documentation. Preflight against that baseline
and preserve this planner handoff and all prior implementation/proof evidence.
Use controlled real-SDK fixtures only: no real caches, downloads, authentication,
providers, actual F/guest, certificate generation/store access or historical
qualification evidence. First demonstrate a failing post-restore check of
$(NuGetPackageRoot) plus a package-relative tool path under the current v2 profile,
using an SDK-packed owned local fixture and the production supervisor/adapter.
Record the actual derived root and fixture tool existence, not only environment
string assertions. Test a trailing separator on the owned NUGET_PACKAGES value.
If it resolves the integration, normalize only that value at preparation; retain
the exact key set and physical owned-directory identity. This representational
normalization stays v2: it does not change the accepted keys, paths, authority or
historical records. No new ADR/schema/migration/profile version is required for
that narrow fix. If the controlled evidence contradicts it, report the precise
cause/alternative before making a different substantive change.

Acceptance: real restore/build plus the package-root concatenation control pass
through the supervisor, complete I/O and correct source/profile are recorded;
SDK fixture fail/secret-absence controls remain meaningful; fresh scratch is
owned and released at the same boundary. Add a mutation/negative control showing
the missing separator is caught. Run affected provider and hosted toolchain/history
checks once after focused controls, then proportional required validation; retain
unaffected product/frontend/Rust/journey/helper evidence honestly. Update executor
current-work with WEP-F4 evidence, fresh/retained/not-run scope and Git state.
Return unstaged, uncommitted and unpushed for independent planner review. F5's
serial real-proof recipe remains planner-owned, not a product correction.

No new F attempt is authorized by correction 5 or by the owner's request to keep
the feature enabled. After correction review, prepare a concrete fourth-attempt
plan and obtain fresh bounded guest/cache authority. E and earlier findings remain
closed at their reviewed scopes; do not claim F Met or publication GO. General
recovery, ADR-0050, provider qualification and publication scope remain separate.

### F attempt 2 result and WEP-F3 (2026-11-02)

The owner explicitly authorized a second attempt and final disablement, including
local feature preparation, one 90-minute/8-GiB guest, private archive preparation
only after fresh isolation controls, and a snapshot commit ONLY in the guest.
The feature was enabled without Windows Update or automatic restart; the owner
then restarted manually and reported return. CIM confirmed enabled, no unrelated
guest. Fresh live origin/main and HEAD/local origin/main remained
e880c4da311f65d7688196105364f239b4f116f0; index empty, 263 modified tracked
paths and 60 untracked files. No allowance for another guest remains.

Attempt launched 14:21:47 UTC; the real build completed at 14:32:34 UTC and the
guest requested shutdown. Cleanup finished within the 90-minute allowance.
Fresh controls passed BEFORE real-cache access: zero up non-loopback interfaces,
direct/descendant loopback positives, direct/descendant denial against the owned
host listener, host positive, zero guest connections. This is the observed
configured boundary, not a separate IPv6/DNS or universal containment claim.

The reviewed preparer returned Prepared: all 99 locked NuGet and 133 applicable
npm identities available, zero missing/mismatched/invalid/incompatible; 53 optional
other-target npm entries omitted. NuGet content hashes use the explicitly selected
installed SDK 10.0.401. This validates preparation content at that scope; it does
not establish cryptographic signature/trust/revocation/audit acceptance.

Payload admission in the guest verified all 11,773 files: 783,316,331 tool bytes,
49,774,654 source bytes and 153,451,577 dependency bytes, within approved bounds.
A named API deps.json runtime closure, installed SDK/runtime/ref/apphost components,
Node with bundled npm, and Git's six-file PE import closure were selected; no
credential helper, profile/config copy, download or real-cache write was used.
Only the disposable guest repository committed the exact source snapshot:
5e62125ca56869273c448ad54a64cc78fc24d8eb. The REAL Git reader observed it clean,
fingerprint 40ac8a4bfd3649f3fdd45509f2c4f2633e24d7b3c457f6ea7c6f70e8d3837d5c.
The real verification supervisor/mediator/SQLite pipeline dispatched the solution
build with windows-verification-v2, fresh owned cache and explicit local-only
locked restore in the same execution, with normal security controls retained.

WEP-F3 OPEN: proof toolchain payload incomplete (planner preparation finding,
not a newly demonstrated product defect). The build exited 1 with NU1101 for
Microsoft.WindowsDesktop.App.Ref, Microsoft.NETCore.App.Runtime.win-x64,
Microsoft.WindowsDesktop.App.Runtime.win-x64 and Microsoft.AspNetCore.App.Runtime.win-x64.
The copied toolchain omitted WindowsDesktop targeting components and the prepared
99-package lockfile set does not include these implicit SDK downloads. Installed
SDK metadata identifies net10 targeting/latest runtime packs as 10.0.12 (default
runtime framework 10.0.0); the exact selected download versions must be established,
not guessed from the unversioned NU1101 message. The planner owns the incomplete
payload selection. No repository, lockfile, provider profile or source-reader
change is justified by this evidence. Do not ask Claude to fix product code.

Production evidence records Failed/Exited, exit 1, identical checkpoint/completion
fingerprint, complete stdout/stderr/input facts, profile v2 and two untruncated
sealed artifacts. Exported byte lengths and SHA-256 were independently matched to
the sealed records. The outer Start-Process handle exposed no exit code; its null
is qualified, not reported as zero. The durable production exit 1 is recorded.
No test/frontend command ran after the build blocker. F remains NOT Met and no
signature/trust/revocation/audit acceptance or final publication GO is inferred.

Cleanup: guest shutdown recorded; only the residual WindowsSandboxRemoteSession
PID 29332, exact start 14:21:47.1267594 UTC, was closed. Owned tripwire stopped.
Hash-checked administrative helper disabled ONLY Containers-DisposableClientVM
with NoRestart; result Disabled/RestartNeeded=true. Final CIM InstallState=2,
zero Sandbox processes. No automatic reboot. The identified owned temporary
stage and attempt input copies were removed after retaining bounded manifests,
scripts, payload identities and results under
logs/planner-review/win-exec-profile-01/f-proof-02/. No old executor scratch or
unknown key/certificate store was inspected or removed. Final fresh live remote,
HEAD/local origin/main, empty index and 263/60 counts still match the baseline.

### Authorized SDK diagnostic result and corrected F attempt 3 proposal

The owner explicitly approved the limited named metadata/archive diagnostic.
No build/test, MSBuild target, provider, authentication or Sandbox operation ran.
The installed SDK 10.0.401 bundled metadata selects net10 targeting packs 10.0.12;
its default runtime-pack logic also acquires latest packs 10.0.12 even when the
framework itself targets default 10.0.0. This is a static derivation from local
metadata and matching SDK source, not a new restore observation. References:
https://github.com/dotnet/sdk/blob/v10.0.401/src/Tasks/Microsoft.NET.Build.Tasks/ProcessFrameworkReferences.cs
and installed Microsoft.NET.Sdk.DefaultItems.Shared.targets lines 40-43.

Named local diagnostic results, retained in
logs/planner-review/win-exec-profile-01/sdk-diagnostic-01/result.json:
- Microsoft.WindowsDesktop.App.Ref 10.0.12 archive absent at its exact standard
  cache location; the installed targeting pack IS present (277 files, 61,773,395
  bytes). Copy the admitted installed component, never repack it as a package.
- Microsoft.NETCore.App.Runtime.win-x64 10.0.12 archive present, 39,968,868 bytes,
  raw SHA-256 0f63dee7ca4383cb16848966f81e439399e2058e1671ebe27e5eaf4b200e8691.
- Microsoft.WindowsDesktop.App.Runtime.win-x64 10.0.12 archive present, 39,172,884
  bytes, raw SHA-256 68bce56d2402969d82ac77fbd2b2a54f55fda86ea060366e6ddb832353dab80a.
- Microsoft.AspNetCore.App.Runtime.win-x64 10.0.12 archive present, 12,760,211
  bytes, raw SHA-256 59025d88c8f5178e8569f91c7cbc56ad3ead3d83a777477d17fade3f9cdd8103.
Each present archive's bounded nuspec matches the exact ID/version, and it has
.signature.p7s. Those facts and raw hashes establish identity only; cryptographic
signature, certificate trust, revocation and audit were NOT verified. No profile
search, source metadata/configuration, credentials or expanded package-cache
contents were read. No archive or installed component was modified or copied.

WEP-F3 remains open until the corrected actual F recipe succeeds. The selected
correction is proof preparation only; no product code/profile/lockfile or accepted
ADR change is warranted. Do not dispatch product correction to Claude.

The owner explicitly answered "Autorizar a tentativa corrigida e a desabilitacao"
to the complete third-attempt question. The corrected plan below is AUTHORIZED.
Preflight: main, HEAD/local and fresh live origin/main at the unchanged baseline,
empty index, 263 modified tracked paths and 60 untracked files; CIM disabled,
zero Sandbox processes. Incoming handoff SHA-256:
73466E26F1C0E86A9D28235F2B910D2DEF2C037D7D3F2FAB52549B515F63EC14.
Preparation completed: OperationCompleted, Enabled, RestartNeeded=true,
NoRestart=true, LimitAccess=true; administrative helper exited 0. Evidence:
logs/planner-review/win-exec-profile-01/f-proof-03/enable-result.json.
The owner has returned after reboot; CIM confirms Enabled and no unrelated guest. No guest, real-cache read, build,
test, provider, authentication or disposable commit ran in attempt 3. The one
session and its launch-based 90-minute budget remain unused. Continue the already
authorized corrected plan after feature-readiness/Git checks on return; do not
request the same authority again. No automatic reboot. Disable only Sandbox after
the eventual finished/blocked attempt, as already authorized.
1. Reconcile main at e880c4da311f65d7688196105364f239b4f116f0, empty index,
   263 modified tracked paths and 60 untracked files, and bind the exact current
   source plus controlled proof tooling. Keep the real index/repository untouched.
2. Re-enable only Windows Sandbox and necessary local dependencies using -All
   -NoRestart -LimitAccess; no Windows Update or automatic reboot. Wait for an
   owner-performed reboot if required. One fresh guest, 8192 MiB, 90 minutes from
   launch, networking/vGPU/clipboard/audio/video/printer redirection disabled,
   only fresh owned input RO and results RW. Refuse an unrelated existing guest.
3. Prove fresh direct/descendant network denial and owned positive controls BEFORE
   reading real caches. Then prepare the original 99 locked NuGet/133 applicable
   npm identities with the accepted helper, explicit SDK 10.0.401 and immutable
   admitted bytes; preserve all 53 optional target exclusions.
4. Add the installed WindowsDesktop.App.Ref/10.0.12 targeting component to the
   selected SDK payload, with per-file physical containment, bounds and hashes.
   After the isolation gate only, admit exactly the THREE named runtime archives
   above, verify unchanged ID/version/raw hashes from this diagnostic, and copy
   those exact bytes into the owned local feed. No other package search/fallback,
   repacking, credential/config copy, cache write or download. The supplement is
   explicitly SDK-derived, not claimed to be in the repository's 99 locked set.
   Normal NuGet signature/trust/revocation/audit/warnings-as-errors remain required;
   a missing control or refusal is a blocker. Raw hashes never substitute for trust.
5. Select the same installed tool/runtime closure plus this supplement, within
   2 GiB tools / 5 GiB dependencies / 1 GiB source / 64 MiB exported evidence.
   Use a snapshot commit ONLY in the disposable guest copy to satisfy the real
   source reader's 128-change bound. No real-repository staging/commit/push.
6. Through the production supervisor, real Git reader and unmodified v2 key set,
   run the solution build, Infrastructure WindowsVerificationEnvironmentProviderTests
   filter, frontend npm ci --offline and npm --offline run build. Each .NET
   execution restores locked from the owned local feed within that SAME execution.
   Record source binding, profile, actual outcomes and complete sealed outputs.
   Stop dependent work on a blocker; no blind retries, ambient fallback, providers,
   security weakening or claims of F Met from partial results.
7. After success/blockage, export bounded evidence, prove the owned guest closed,
   stop owned controls and disable ONLY Containers-DisposableClientVM -NoRestart.
   Remove only this attempt's admitted staging/copies. No unrelated cleanup or
   automatic reboot. Any later owner reboot remains manual.

The owner approval releases only this corrected third proof attempt and cleanup;
the previous two guest attempts are consumed. Real-cache access still requires
the fresh isolation gate before archive staging. No publication GO or
next slice is authorized. Earlier findings and E remain closed at their reviewed
scopes; general recovery, publication and provider qualification stay separate.
The planner changed only this handoff and ignored diagnostic/proof artifacts.

### F attempt 1 and correction 3 decision (2026-11-02)

The owner explicitly authorized the bounded F attempt and feature cleanup, then
separately authorized a snapshot commit ONLY in the disposable Sandbox copy to
handle the product source reader's 128-changed-path bound. Neither authorization
permits a commit, staging or push in the real repository. The owner prefers
direct questions and no copy-and-paste prompt artifacts in conversation.

Attempt window: started 11:33:01 UTC; proof work and cleanup finished before
12:00 UTC, within the 90-minute cap. Preflight matched main, HEAD/local origin/main
e880c4da311f65d7688196105364f239b4f116f0, empty index, 263 modified tracked
paths and 54 untracked slice files. Incoming handoff SHA-256:
BF4C54EE2BCF4140787D34197F41BA4B2BA5E5879BA3F0F6A4B5A9591216BF8B.

Observed source preflight: the real reader returned GitInvocationFailed in the
host tool context; closed command signals showed Git's dubious-ownership refusal,
not I/O truncation or incomplete capture. Independently, the real parser refused
the 317 current paths against its 128-path limit. No global trust setting or
product bound was changed. The owner approved a task-local guest snapshot commit
to address the path count; no such commit was actually created because dependency
preparation blocked before source/tool staging. A local baseline bundle/owned
no-checkout clone used for lockfile selection created no new commit. The first
bundle write in the mapped input was denied; creation in owned temporary staging
succeeded. The source limit and host ownership signals are not the two new
dependency findings below and are not an approved product change.

Isolation controls PASSED before real-cache access: one empty guest, Networking
Disable, vGPU/audio/video/printer/clipboard redirection disabled, 8192 MiB memory,
only owned read-only input and owned writable result mappings. The guest reported
zero up non-loopback interfaces. Its direct and descendant loopback controls
connected successfully; both direct and descendant attempts to the owned host
tripwire were denied; the host positive control passed and the listener recorded
zero guest connections. This is the observed configured guest/control boundary,
not universal exploit resistance or a separate IPv6/DNS transport test. No other
guest session was reused. Configuration and bootstrap identities are retained.

After the gate passed, the reviewed preparation tool read only its lockfile-named
archives/content from standard caches and wrote to a physically separate owned
temporary stage. It returned exit 3 / IntegrityMismatch: 99 NuGet identities,
186 npm identities, zero missing NuGet archives, 90 reported NuGet mismatches,
53 missing npm tarballs. No source-cache write, credential/configuration copy,
download, provider discovery, authentication or security-policy change was used.
No product build/test/frontend command was run and no certificate trust/revocation
or audit qualification was performed. F is NOT Met.

Independent diagnosis, not an integrity waiver:
- FluentValidation 12.1.1 and Microsoft.EntityFrameworkCore 10.0.11 are signed
  archives for which the script's raw SHA-512 differs from the lock contentHash,
  while installed SDK 10.0.401 NuGet.Packaging.GetContentHash on the SAME archive
  bytes matches that lock. This proves false rejection for those two samples;
  it does not independently validate the other 88 reported mismatches, establish
  signature/certificate trust or declare all cached packages valid.
- All 53 missing npm entries are optional and incompatible with win32/x64 in
  the ORIGINAL committed lockfile. A neutral-environment runtime check confirms
  the selected Node is win32/x64 v24.12.0. None of these 53 is applicable missing
  content for the chosen target; the preparer incorrectly requires other targets.

Planner decision: F remains mandatory and Blocked(DependencyPreparationCompatibility),
with findings WEP-F1 and WEP-F2 below; no final acceptance or commit/push GO.
The isolation prerequisite was demonstrated for this attempt, replacing the old
reason that no boundary had been identified. It is not reusable proof for another
guest or authority for a second F attempt. WEP-R1/R2/R3/R4/R5/V1 and E remain
closed at their previously reviewed scopes; the new findings concern additional
preparation semantics, not a reversal of those specific boundary fixes.

The planner owns a specification error: earlier instructions equated NuGet lock
contentHash with SHA-512 of the entire signed archive. That is wrong. The following
correction explicitly supersedes that equation; it is not permission to relax
integrity, signature, revocation, audit or warnings-as-errors controls.

CORRECTION 3 IS RELEASED to the SAME Claude executor chat, SAME slice. No routine
owner approval is needed for these technical corrections. Communication to that
external executor remains the existing owner-mediated handoff; no implementation
change has been made by the planner. Executor reads this complete bounded task:

- Preflight once: main; HEAD/local origin/main as above; reconcile live origin
  read-only under the workflow; index empty; 263 modified tracked and 54 untracked
  incoming paths. Planner intentionally changed only this handoff plus ignored
  fresh proof artifacts. Preserve the handoff and existing implementation; do
  not reset, clean, stage or overwrite discrepancies.
- WEP-F1, P2: correct NuGet content-hash semantics in the proof preparer. Use
  NuGet-compatible content hashing for signed AND unsigned archives, with
  already-installed/bundled supported tooling and no new dependency/install.
  The lock content hash of signed packages normalizes the signature entry/ZIP
  bookkeeping; it is not the raw whole-file digest. Keep a separate raw archive
  SHA-256/SHA-512 identity for exact transfer/staging bytes. Feed only the already
  bounded, physically admitted immutable bytes to normalization; never reopen
  the source path or read .nupkg.sha512/.nupkg.metadata as authority. Preserve the
  earlier single-read, regular-file/link, root-overlap and exclusive-write rules.
  Computing content hash does not verify a signer or certificate; ordinary NuGet
  locked restore/signature/revocation/audit validation remains mandatory. Malformed,
  changed payload/ZIP and unavailable validator/toolchain must refuse precisely,
  never fall back to raw hash or a claimed match. Test unsigned and signed-shaped
  real NuGet archives, content tampering and malformed/truncated ZIPs, and retain
  the distinction between content validity, byte identity and signature trust.
  Use controlled fixtures and ephemeral synthetic signing only, never install
  certificates or trust roots or download/read real caches for these tests.
- WEP-F2, P2: make npm preparation target-aware for the supported Windows x64
  proof. Honor original lockfile os/cpu and optional semantics as npm does,
  including allow/deny constraints; do not rewrite locks or drop a required
  dependency to get green. Precisely omit non-applicable OPTIONAL entries and
  record the target and reason. Applicable missing/tampered entries still block;
  a required incompatible or unsupported constraint is a precise blocker, not a
  silently omitted success. Keep original SRI checks, exact verified bytes,
  bounded reads, no source-index/config copying and no HTTP/auth fallback.
  Add controlled cases for the observed Windows x64/other-platform shapes,
  negated constraints, required incompatibility, same-target missing content,
  tampering and target manifest provenance. No general settings subsystem or
  arbitrary platform/profile editor is added.
- Evidence is under logs/planner-review/win-exec-profile-01/f-proof-01/:
  source-preflight.json, results/isolation.json, results/host-tripwire.json,
  results/dependency-preparation.json, results/dependency-manifest.json,
  results/nuget-hash-diagnosis.json, results/npm-target-diagnosis.json and
  results/final-cleanup.json. The new diagnosis scripts are review evidence,
  not architecture authority or permission to re-run real-cache reads.
- Correct the helper comments and any current normative text that repeat the
  wrong content-hash definition; retain historical attempt facts as history and
  add Correction 3 to executor-owned current-work. Do not edit accepted product
  ADR history, frozen snapshots or the planner-owned handoff. No product process,
  environment v2, source-reader limit/trust, ownership, recovery or publication
  feature changes are dispatched.
- Validate controlled helper cases first, then affected harness/format/link
  checks once on stable inputs. This helper-only correction does not by itself
  invalidate the full unchanged backend/frontend/Rust/journey matrix. Broaden
  only for an actual changed dependency or failure. Report evidence scope and
  failures truthfully. Return unstaged, uncommitted and unpushed for review.
- Do NOT re-enable/launch Sandbox, run another F attempt, read real caches,
  authenticate, call providers, download tools/packages, install certificates,
  weaken security controls or renew any historical execution allowance. F will
  need a separately authorized fresh attempt after correction review.

Primary references support the diagnosis:
https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageArchiveReader.cs
https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/Signing/Archive/SignedPackageArchiveUtility.cs
https://docs.npmjs.com/cli/v11/configuring-npm/package-json/

Cleanup COMPLETE: guest self-shutdown marker received; its sole residual remote
client was identified by exact PID/name/start time and terminated only after
guest compute absence. No unrelated session/process was closed. A UI inventory
found no targetable Sandbox window; no app input or UI data deletion was performed.
The host tripwire exited. The known temporary clone/bundle/package stage was
removed only after bounded manifests/diagnosis were retained, using a verified
absolute task-temporary root. No package payload, SDK/Node/Git payload or source
snapshot was transferred into the guest and no guest snapshot commit was made.

The hash-checked administrative cleanup helper disabled ONLY
Containers-DisposableClientVM with -NoRestart, returned exit 0, Disabled and
RestartNeeded True; independent named CIM now reports InstallState 2 and zero
Sandbox processes. Earlier cleanup guards refused while the remote client was
still visible; they did not run DISM or falsely prove closure. Parent/pre-existing
features were not disabled and no reboot was initiated. The owner may restart
Windows when convenient to finalize feature removal.

The real repository remains unstaged, uncommitted and unpushed at the baseline.
Only this planner-owned handoff and ignored proof artifacts changed in the attempt.

### Post-reboot readiness, cleanup instruction and bounded F proposal (2026-11-02)

The owner reported restarting Windows and instructed: disable Windows Sandbox
after the tests. This supplies cleanup authority for the feature, not authority
to disable pre-existing virtualization/WSL/Docker components or restart the PC.
The owner prefers direct questions here over copy-and-paste prompts.

Codex verified only the prepared feature after reboot: Win32_OptionalFeature
reports Containers-DisposableClientVM InstallState 1 (Enabled), and the system
WindowsSandbox.exe entry point exists. No guest was launched. Git remains main
at e880c4da311f65d7688196105364f239b4f116f0 for HEAD/local origin/main, index
empty, 263 modified tracked paths, 54 untracked files. Incoming handoff SHA-256:
2F1B6826CCFCB2ACED3EA079C6DCE8F089B3FE96413FF6C469D45DE0EC52568F.

This establishes feature availability only; WEP-ENV-01/F remains blocked. The
earlier preparation approval did not authorize guest launch, private archive
access or F. The following ONE bounded proof attempt is proposed for explicit
owner authorization, not dispatched by this record:

1. Reconcile Git and freeze the exact approved source snapshot (baseline plus
   current changed/new slice files, hashes and committed lockfiles) without
   staging, committing or copying the host's .git/config, ignored logs, profiles,
   secrets or preserved qualification roots. Use an owned local Git bundle and
   a disposable guest clone/worktree; never modify the host source or real index.
2. Create one task-owned Windows Sandbox configuration with Networking Disable,
   clipboard/audio/video/printer redirection disabled, vGPU disabled and 8192 MiB
   memory. Map only a fresh task-owned input directory read-only and a separate
   task-owned results directory writable. Do not expose the real repository,
   user profile, program directories or caches directly to the guest. Refuse an
   existing unrelated Sandbox session rather than reusing or closing it.
3. Stage only the selected already-installed Windows .NET 10 SDK/runtime, Node
   with its bundled npm, Git executable runtime and the current repository-built
   harness runtime closure. Bound and hash the named runtime payloads; exclude
   user/machine NuGet/npm/Git configuration, credential helpers/plugins and
   unrelated installations. No downloads, new installations, provider discovery,
   authentication or certificate warming. Missing runtime files are blockers.
4. Launch the owned guest once, maximum five minutes to obtain a readiness result.
   Before any real-cache read, require closed evidence of the network-disabled
   configuration, guest readiness and bounded owned positive/negative controls
   for direct and descendant network attempts, covering the whole guest. Use
   only owned tripwire endpoints; no real provider/authentication endpoint.
   A failed/missing control stops dependent proof; never infer containment from
   CLI flags, a timeout alone, feature presence or an offline package source.
5. Only after those controls pass, prepare the original committed lockfile
   dependencies with the reviewed preparation tool. Read only its exact named
   .nupkg archives (including Devalente.Shared) and SRI-addressed npm content
   from named standard caches; never inspect/copy credentials, NuGet.Config,
   .npmrc, source metadata, HTTP/plugin caches or expanded package trees.
   Sources stay read-only. Transfer only verified archive/content bytes into
   the owned input; prove missing/tampered refusal with owned copies. Freeze
   and verify the final manifest before the guest consumes it.
6. Through the production supervisor, Windows v2 environment and real Git
   source observer in a validation-only harness, execute one solution build,
   the Infrastructure WindowsVerificationEnvironmentProviderTests filter,
   frontend npm ci --offline and npm --offline run build. Use no mocked Git
   observer and no full application startup/provider discovery. Record source
   binding, commands, physical outcome, complete I/O, sealed evidence and owned
   cache/profile/config locations. Any validation harness is ignored task-owned
   tooling, not a product-code or dependency change.

Recipe clarification accepted by the planner for this proposal: each .NET
execution gets a fresh cache which is deleted after that execution, so it must
restore from the explicit owned local-only configuration in locked mode within
its SAME build/test execution. Use SDK build/test MSBuild restore properties
(RestoreLockedMode=true, RestoreConfigFile=<owned>, RestoreSources=<local feed>,
RestoreNoCache=true, NuGetInteractive=false), then the command's build/test phase.
Do not use the earlier blanket --no-restore instruction to depend on a previous
execution's released cache, and do not silently fall back to ambient restore.
This changes recipe composition, not sources, integrity, security/audit policy,
product environment keys or authority. Keep warnings-as-errors, normal signature,
revocation and audit controls; missing security evidence remains a precise blocker.
The frontend commands likewise keep original locks and every security control.

Limits: one guest attempt, 90-minute total deadline; up to two GiB selected tool
payloads, five GiB dependency content and one GiB source input; 64 MiB exported
reports/artifacts. Refuse overflow rather than expand or silently truncate required
evidence. No blind retries or unrelated suites. Stop dependent work on missing
packages, tool/runtime/configuration, hash mismatch, security evidence or isolation;
return actual partial results without declaring F Met. No network fallback.

Cleanup is mandatory after a finished/blocked attempt: export bounded evidence,
close only the identified owned guest and prove closure, then disable only
Containers-DisposableClientVM with Disable-WindowsOptionalFeature -Online
-FeatureName Containers-DisposableClientVM -NoRestart. Do not remove payloads,
disable parent/pre-existing features, terminate unrelated processes or reboot.
If guest closure or cleanup cannot be proved, report the exact unresolved state
instead of claiming success or forcing unrelated resources closed. Cleanup does
not release any product ownership. Owner performs any required later reboot.

F execution awaits the owner's direct answer to this bounded proposal. No commit,
push, next slice, real-provider allowance, architecture expansion or general
host-loss recovery is authorized. E and all closed findings remain unchanged.

### Authorized Windows Sandbox prerequisite preparation (2026-11-02)

The owner explicitly selected "Autorizar a preparação limitada" in response to
the bounded Sandbox preparation question. This superseded only the pending
preparation proposal below. It did not authorize Sandbox launch, a guest,
toolchain installation, real-cache preparation, connectivity probes or F.
The owner also asked for direct questions instead of copy-and-paste prompts;
honor that communication preference in this existing planner chat.

Preflight matched main, HEAD/local origin/main
e880c4da311f65d7688196105364f239b4f116f0, empty index, 263 modified tracked
paths and 54 untracked files; incoming handoff SHA-256:
48A7AD220A360272C8AFFB6C84928FF1E250B99B83E63936C62834BFB7733F04.

Minimal prerequisite observations: 64-bit Windows 11 Pro (10.0.26200), eight
physical cores / sixteen logical processors, 31.06 GiB memory, 1320.09 GiB free
on the system volume, firmware virtualization True and HypervisorPresent True.
The CPU query also reported SLAT/VM-monitor flags False while a hypervisor was
already present; do not treat these masked/ambiguous fields as independent proof
that hardware lacks virtualization. No firmware change or wider hardware/account
inventory was performed.

The initial tool process had no administrative token. The planner requested
Windows administrative consent ONCE, using a hidden, fixed, hash-checked helper
limited to the approved feature operation and its result query. It changed no
execution policy. The helper was derived from the owned preparation script,
SHA-256 7040A428302622BF39BB4536F0967CA8D8B277A02294B0D185964AC42793DF58,
and ran only:

Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM
-All -NoRestart -LimitAccess

followed by the exact feature's status query and a closed result record. The
helper checked its administrative token before enabling anything. No raw
DISM/system log, user identity or credential was collected.

Observed result: helper exit 0; Status OperationCompleted; FeatureState Enabled;
RestartNeeded True; NoRestart True; LimitAccess True. Normalized prerequisite
and operation evidence is retained under
logs/planner-review/win-exec-profile-01/sandbox-preparation-01/.

Preparation is complete at this authorized boundary. The owner must restart
Windows when their work is saved; never initiate that restart automatically.
After the owner reports restarting, the planner may verify only the prepared
feature's readiness within the same authorization. Do not launch Sandbox or
assume guest/toolchain readiness or outbound denial. F remains blocked and
mandatory until a separately authorized concrete environment/proof plan passes
its required controls; E and all closed findings remain unchanged. There is
no implementation edit, next-slice selection, commit or push GO.

### Authorized environment inventory (2026-11-02)

The owner's subsequent explicit "autorizado" activated only Review 3's bounded
read-only machine inventory, not F, containment creation, installations or any
publication action. Codex completed that inventory in this same planner chat.
Git remained main at e880c4da311f65d7688196105364f239b4f116f0 for HEAD and local
origin/main, index empty, 263 modified tracked paths and 54 untracked slice files.
The incoming handoff SHA-256 was
67EC0EDE33334E280915DE2CD50C27DC136B49B45F98E87CF161BEA945B16369.

Observed, with no tools/guests/daemons under discovery launched:
- Windows 11 Pro, 64-bit, version 10.0.26200.
- Named Win32_OptionalFeature queries report Disabled (InstallState 2) for
  Containers-DisposableClientVM (Windows Sandbox), Microsoft-Hyper-V-All,
  HypervisorPlatform and Containers; VirtualMachinePlatform reports Enabled (1).
- WindowsSandbox.exe was not found through command resolution or the system
  directory; wsl.exe and docker.exe entry points were found; podman.exe was not
  found through command resolution. This does not identify any daemon state,
  guest, image, selected engine, or ready Windows proof environment.
- ActiveStore firewall profiles Domain, Private and Public each report Enabled
  True and DefaultOutboundAction Allow. This is aggregate effective-default
  evidence, not an inspection of individual rules or proof every connection works.

The initial restricted queries could not obtain CIM/firewall/feature status.
Changed-context OS and firewall queries succeeded. Get-WindowsOptionalFeature
still returned 0x800702E4 (elevation required), so the planner used only the
authorized named Win32_OptionalFeature read query instead. No admin elevation,
feature enablement, UAC-driven operation or policy change was performed. The
initial persistent-store firewall value NotConfigured was not interpreted as a
deny; the effective ActiveStore query above supplies the actual reported default.
Normalized non-secret results are retained under
logs/planner-review/win-exec-profile-01/environment-inventory-01/.

Decision: no ready Windows outbound-denial boundary was established by this
authorized inventory. CLI presence and VirtualMachinePlatform being enabled
do not qualify Docker/WSL or replace the Windows v2 proof. F remains mandatory
and Blocked/OutboundIsolationUnavailable. No further executor code correction
or F execution is dispatched and there is no commit/push GO.

Concrete next prerequisite proposal, PENDING separate owner authorization:
prepare Windows Sandbox on this Windows host, first checking only its documented
minimum CPU/virtualization/memory/free-space prerequisites, then enabling only
Containers-DisposableClientVM and its required parent features with
Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM
-All -NoRestart -LimitAccess. Administrative consent would be limited to that
feature operation. Do not restart automatically, change firmware, fetch missing
component payloads, install SDK/Node/Git, start Sandbox, create a guest, prepare
real dependencies or run F. If payload, privilege or hardware is missing, report
that precise prerequisite instead of falling back. Owner performs any required
reboot. Feature availability alone will not establish F: the later environment
proposal still needs networking disabled from creation, bounded transfers, the
Windows toolchain, direct/descendant negative controls and the approved offline
proof. No security/audit/certificate control may be weakened.

Primary references checked:
- https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-optionalfeature
- https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-install
- https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file
- https://learn.microsoft.com/en-us/powershell/module/dism/enable-windowsoptionalfeature?view=windowsserver2025-ps

This proposal changes the machine and is outside the completed read-only
authorization; do not execute it until the owner explicitly authorizes that
bounded preparation. If the owner provides an already prepared Windows isolated
environment instead, evaluate it without assuming equivalence or relaxing F.

### Review 3 decision (2026-11-02)

Codex independently reconciled main, HEAD, local origin/main and fresh live
origin/main at e880c4da311f65d7688196105364f239b4f116f0; index empty; 263
modified tracked paths and 54 untracked slice files. The incoming planner
handoff matched D276C39CF8E2CB78D3FC07261B9ABAE22A6A4537F9A2C2641E59542A24B7D385.

Close WEP-R4 and accept E at its approved hosted-fixture boundary. The v2
implementation adds only the dotnet-specific owned ProgramFiles(x86) key,
preserves the v1 record through ADR-0073's narrow extension, and does not
relabel historical executions. The hosted fixture really packs/restores/builds
and executes tests with the production supervisor, process adapter, environment
provider, mediator and SQLite pipeline. Its Git fingerprint observer remains
the explicitly approved double; it is not a real DevalCopilot worktree proof.
The tiny test fixture uses exactly the named 16-file repository-build runtime
closure, not a package cache, assets/configuration copy or mocked test target.
Both discovered passing tests and the deliberate assertion failure are checked
with their real exit, I/O, source-binding and profile facts. Configuration-path
enumeration supports configuration scoping, not absence of all machine reads.

Fresh independent validation, against current source/builds:
- API real-toolchain plus v1/null history controls: 14 passed, zero failed/skipped.
- Windows verification environment provider: 12 passed, zero failed/skipped.
- Claude envelope and additive migration/upgrade/reopen/downgrade controls:
  52 passed, zero failed/skipped.
- The Infrastructure assembly copied to its test output matches the freshly
  built production assembly; diff whitespace check passed.

No real provider, authentication, real dependency-cache preparation, preserved
qualification evidence, new machine inventory or containment experiment was
used. The executor's full final matrix and 17 journeys were not independently
replayed; retained evidence keeps its actual scope. No new correction finding
was identified in Correction 2. The earlier specific findings WEP-R1, WEP-R2,
WEP-R3, WEP-R5 and WEP-V1 stay closed. This closes correction review at E, not
final acceptance or an exhaustive approved-content identity for publication.

F remains mandatory and Blocked/OutboundIsolationUnavailable under WEP-ENV-01.
There is NO commit/push GO, no F dispatch and no authorization to weaken the
gate, add containment, inspect real caches, install tools or renew historical
provider allowances. The executor has no further code correction dispatched;
the same executor chat remains the home of this slice when authorized work resumes.

Proposed next owner decision, not yet authorized: a bounded READ-ONLY inventory
by the planner in this existing chat of non-secret Windows edition/architecture,
the state of named virtualization/container optional features and presence of
their entry-point executables, and aggregate firewall-profile outbound defaults.
Use only feature/presence/default status, not raw machine inventories, firewall
rule contents, VM/distro contents, user/organization data or credentials. Do not
start services, guests, daemons, executables under discovery or probes; do not
change policy, create containment, install anything, prepare dependencies or run F.
This may identify a candidate already available, never prove containment. Return
a concrete bounded F environment/proof proposal, or the precise missing mechanism,
before any dependent execution. The prior limit, "No new machine inspection or
experiment is granted," is why owner authorization is necessary for this audit.
An explicit owner authorization here activates only this read-only inventory;
do not demand another activation or executor dispatch for that same inventory.

Only the planner-owned handoff and ignored Review 3 summary changed in this review.
No implementation edits, staging, commit or push. General host-loss recovery,
publication trust implementation, installed-provider qualification and the green
draft-PR exit demonstration remain separate, incomplete outcomes.

### Review 2 decision (2026-11-01)

Correction 1 remains unstaged, uncommitted and unpushed. Codex independently
reconciled main, HEAD, local origin/main and fresh live origin/main at
e880c4da311f65d7688196105364f239b4f116f0; index empty; 263 modified tracked
paths and 53 untracked slice files. The incoming planner handoff matched
51D1CC37C6FDE1A707815F2D1E6D1E5D02CA01496DB2BFF42E523A24240DB46D.

Close WEP-R1, WEP-R2, WEP-R3, WEP-R5 and WEP-V1 for the specific Review 1
defects: source inspection and focused independent controls support the fixes.
This is not exhaustive acceptance of the remaining diff or independent replay
of the executor's full matrix and 17 journeys. WEP-R4 / E remains unmet; F
remains mandatory and Blocked/OutboundIsolationUnavailable. NO-GO for final
acceptance or publication. Continue the SAME slice in the SAME Claude chat;
no further owner decision is needed for the bounded correction below.

WEP-R4 challenge disposition: accept the reported missing NuGet base-path
prerequisite. The NuGet source derives its Windows machine settings path from
ProgramFiles(x86), falling back to ProgramFiles; it supports this explanation,
but is not itself proof of the installed binary's historical behavior:
https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Common/PathUtil/NuGetEnvironment.cs

Authorize windows-verification-v2 for NEW executions. Only dotnet.exe gains
ProgramFiles(x86), derived from a fresh empty directory beneath that execution's
owned scratch. Never read or inherit either Program Files environment value,
point at a real Program Files tree, copy configuration, or provide an ambient
fallback. Keep every other key and authority unchanged. The version changes
because the recorded key contract changes, even though this slice is unpublished;
retain v1/null historical facts without relabeling, backfill, upgrade, replay,
profile selection or new recovery semantics. Record a narrow ADR extension to
ADR-0072 (0073 if still unused), preserving 0072's version-specific decision and
linking the extension; do not silently redefine v1 or touch ADR-0050.

Re-enable and run the four SDK controls; remove their pending-decision skips.
Prove the exact key set, fresh empty owned NuGet base, ambient/machine-config
exclusion controls, genuine build success and compile failure, cache lifetime,
missing SDK, source binding and complete I/O. Do not claim filesystem isolation
or that an empty ProgramFiles(x86) eliminates every possible machine input.

Challenge the claim that dotnet test necessarily requires a download or real
package-cache access. The current repository-owned build outputs already contain
the framework, testhost and adapter. A fresh independent diagnostic used the
production v1 provider plus ONLY the proposed empty owned ProgramFiles(x86) key
in a COPY of its environment and ran dotnet test on the already-built Domain
test DLL with an exact filter: one test discovered, one passed, exit zero. No
restore, real cache preparation, provider or authentication was performed. This
demonstrates feasibility only, NOT hosted E acceptance, test-failure propagation,
F or absence of all machine reads. Stderr also carried a workload-verification
advisory; it grants no update/install permission and is retained in the result.

Authorize a bounded E fixture using only the named framework/testhost/adapter
runtime closure already present in current repository-owned build outputs.
Prefer a tiny owned fixture compiled by the real SDK using direct local assembly
references, then dotnet test on its built DLL. Include real positive and deliberate
assertion-failure cases with nonzero discovered/executed counts through the
production supervisor and v2 provider. Select only the required runtime files;
record their identities/hashes. No real NuGet-cache/archive reads, package-tree,
obj/assets, configuration, credential or whole-output-tree copying; no downloads,
framework installation, dependency changes, new production commands or bypasses.
Do not override VSTest targets or substitute an exit-code executable for actual
test discovery/execution. If a further dependency is genuinely unavailable,
identify its exact file/package and leave that sub-gate unmet without fallback.

Independent Review 2 validation: Infrastructure focused controls 31/31; API
startup/sealing/preparation controls 17/17; dependency harness 11 passed, one
symlink-privilege skip. Initial restricted-context .NET attempts built but aborted
at the 90-second testhost handshake with no case results; the harness initially
failed on Git fixture configuration writes. Later changed-context runs passed;
this establishes neither historical flakiness nor a root cause. Reviewer-owned
feasibility source/result are under logs/planner-review/win-exec-profile-01/review-02/.
No implementation file was changed by the planner, and no full suite or browser
journey was independently replayed in this review.

Run focused v2/SDK/test controls first, then rerun only final evidence invalidated
by the actual correction, with explicit retained-input reasoning for the rest.
Keep the full prior matrix/failed attempts intact. Return the corrected diff
unstaged, uncommitted and unpushed; E needs actual hosted evidence and F still
blocks final GO. General host-loss recovery and publication remain separate.

### Review 1 decision (2026-11-01)

The executor returned the slice unstaged, uncommitted and unpushed. Codex
independently verified main, HEAD, local origin/main and fresh live origin/main
at e880c4da311f65d7688196105364f239b4f116f0; index empty; 250 modified tracked
paths including this planner selection record and 50 untracked slice files.
The delivered planner record retained its dispatched
6F2EF1D628CBDC541DED0EAFB1BD601AEEB017EEF5AB6D518B4EB9119815D574 hash before
this review edit. No implementation file was changed by the planner.

NO-GO for correction of this SAME slice in the SAME Claude chat. F remains
Blocked/OutboundIsolationUnavailable under WEP-ENV-01 and mandatory for final
acceptance; this is not a new blocker or permission to add containment. The
current B/D/E Met claims are not accepted. Other acceptance claims remain
executor-reported evidence, not a blanket approval of the remaining diff.
This blocking review does not claim an exhaustive acceptance of all 300 paths;
substantive review continues after the corrected boundaries stabilize.

- WEP-R1, P1: bounded capture does not establish quiescence. Sink writes and
  flushes omit cancellation; CloseAsync bounds only gate acquisition, not sink
  disposal. If the gate budget expires, the adapter still snapshots/returns
  while the writer remains active, and DisposeAsync's zero-budget close need
  not stop it. A controlled probe against freshly built production code holds
  one sink write, observes CloseAsync=false and DisposeAsync return, then releases
  the write: the buffer grows from 8065 to 8192 bytes and two writes occur after
  close/dispose returned. Correct real cancellation, immutable snapshots and
  writer release; do not seal unproved writers in any supervisor. Add blocked
  write/flush/disposal controls, not only late chunks after a successful close.
- WEP-R2, P2: startup classification is incomplete across output families.
  Verification output recovery still propagates per-item file I/O failures into
  fatal StartupRecovery (its RequireCount call); the Agent/Process helpers catch
  every seal exception as optional, including unclassified exceptions. Implement
  the approved typed optional-file/absence versus database/unknown distinction
  for ALL three families, preserve failed-import partials and keep unknown scope,
  database/commit failures and unclassified errors fatal. Add hosted negative
  controls for verification output and unclassified Agent/Process artifact faults.
- WEP-R3, P2: dependency preparation does not enforce the promised input/output
  boundary. A synthetic-only probe passes an empty source cache as staging;
  prepare returns Prepared and writes feed/cache/profile/config/manifest into
  the source cache. Reject physical root overlap before any write, including
  nested/alias cases. Select committed lockfiles rather than every discovered
  filename. Apply the same bounded regular-file/link constraints to npm content
  (get.byDigest currently reads it whole) and validate the exact bytes placed
  in staging, rather than hashing then reopening the NuGet source for copy.
  Test these refusals with owned synthetic roots only, never real caches.
- WEP-R4, P2: acceptance E does not yet prove the stated SDK toolchain.
  VerificationRealToolchainHostedTests' three dotnet cases invoke an already
  compiled ProcessExecutionFixture.dll for environment/exit checks, not dotnet
  build/test. Preserve those useful runtime/npm controls and add actual .NET
  SDK build/test success/failure under the production supervisor and owned
  profile without external authentication/downloads. Prove the restore/cache
  lifetime for no-restore commands; a scratch cache removed after one execution
  cannot be presumed available to a later execution. Report genuine missing
  prerequisites rather than a skip or broader E Met claim. F stays separate.
- WEP-R5, P2: environment preparation can throw after the dispatch marker and
  before the supervisor's process try/catch. Creating profile/cache/config
  children is outside TryCreateScratch's catch; an I/O/permission failure reaches
  the iteration logger and strands the execution Running without a diagnostic
  or child. Make preparation failures truthful, terminal and recorded once,
  without redispatch or manufactured physical evidence; preserve existing
  ownership authority. Test an exception during partial setup and verify no
  process invocation, closed diagnosis and no repeat. Challenge any required
  authority change before implementing it.
- WEP-V1, P2: complete the required collaboration journey and qualify evidence.
  The baseline journey's button inventory demonstrably omits the already
  published abandonment inspector; accept the pre-existing diagnosis and
  authorize the smallest test-only inventory correction, retaining its no-write
  assertions, then run that journey to completion. Do not waive the required
  journey or change product behavior. Preserve original failures and distinguish
  unchanged-input retained results. One failure followed by passes does not
  establish flakiness, load causation or a transient build cause. Correct the
  report, acceptance scope and current new-file inventory (50 observed, not 51).

Independent review evidence: an Infrastructure build with --no-restore passed
with zero warnings/errors; the capture and dependency-overlap probes above
reproduced their counterexamples using only owned synthetic data. Focused
StartupRecoveryTests plus VerificationRealToolchainHostedTests passed 14/14
after a changed execution context. The first focused attempt built successfully
but aborted on the 90-second VSTest/testhost connection handshake, reporting a
still-running testhost; no test-case result or subsequent termination proof is
claimed for that attempt. The later pass explains no historical failure and
does not prove the missing cases. Sources/results are retained under
logs/planner-review/win-exec-profile-01/review-01/. No real provider, credential,
preserved qualification evidence, real cache or outbound-isolation experiment
was used. Full executor suites/journeys were not rerun by this reviewer.

Executor challenge dispositions: safety-critical startup failures being fatal
and incomplete redirected stdin preventing semantic success follow the approved
contract; neither authorizes new repair or release. The finite 401/429 mapping
is a reported-envelope classification, not installed-version qualification or
proof of historical causes. Narrow version/handler/order/architecture test-pin
updates are expected consequences, subject to diff review rather than a blanket
approval. The discovery-driven real-host smoke exclusion is consistent with the
no-provider-discovery boundary; it supplies no provider qualification evidence.

Rerun changed-boundary controls first, then affected final evidence once on
stable inputs. Retain other results only with exact unchanged-input reasoning.
Return all corrected code/docs/tests unstaged, uncommitted and unpushed for
independent correction review; no next slice, commit, push or new owner decision.

### Decision and verified baseline (2026-11-01)

The owner explicitly authorized the revised WIN-EXEC-PROFILE-01 contract and
its release in this Codex planner chat after the dependency/startup clarifications
and WEP-ACT-01/WEP-DEP-01/WEP-ENV-01/WEP-SIZE-01 dispositions. Codex now releases
implementation and the bounded validation below to the SAME Claude executor
chat that performed only read-only preflight. The previous pending-approval and
dispatch-hold instructions are superseded; no additional owner approval is
needed to begin this slice. The remaining approved scope and authority stay
unchanged. This is not review acceptance, real-provider qualification, permission
to weaken an unmet gate, commit or push authority.

Immediately before this selection record, branch main, HEAD, local origin/main
and a fresh live origin/main all equal
e880c4da311f65d7688196105364f239b4f116f0. The index and working tree were clean,
with no untracked files. After recording selection, only this planner-owned
file is modified, unstaged; no implementation has started. The complete inline
dispatch carries this file's exact SHA-256 for the executor's once-only preflight.

The remote commit is confirmed. Its historical approved-content verification
was not repeated and no preserved qualification evidence was opened. Earlier
publication-phase instructions do not authorize another push. The prior handoff
is preserved in Git at the exact baseline above; this current-state replacement
does not rewrite any frozen snapshot or historical ADR.

### Objective and sizing

Deliver one documented Windows execution profile whose provider responses and
local verification results are interpreted safely, whose process I/O completion
is bounded, and whose required startup recovery failures prevent normal execution.
Contract interpretation, safe failure reporting, shared capture, verification
environment and startup are one coherent execution boundary. General host-loss
resolution needs a different proof contract and publication remains separate.

This slice produces controlled implementation and local toolchain evidence,
never installed-provider qualification. Increments 4/5/6 remain incomplete.

### Resolved implementation contract

- Fresh Claude contracts: claude-critical-review-v2, claude-implementation-v3
  and claude-review-correction-v3. Update claims, requests, version checks,
  turn-limit/guidance classification, repair compatibility and projections.
  Historical versions/manifests remain historical; no implicit upgrade or
  redispatch. Share only a pure Claude wire-envelope reader/classifier, not
  role adapters or role authorization.
- New success requires coherent result metadata, is_error=false, successful
  subtype and object structured_output. Never fall back to narrative result.
  Reject duplicate contract-bearing properties, malformed known metadata and
  contradictory success/error facts; bound JSON. Remove --prompt-suggestions
  and false. Keep other permissions, tools, authentication environment,
  session/model/effort settings and existing turn limits, including the
  CriticalReviewer's one turn.
- Interpret bounded failure envelopes also on nonzero exit. Persist one
  versioned closed diagnostic with its result and expose it through existing
  current/historical surfaces. Categories: InvocationUnavailable,
  EnvironmentUnavailable, TimedOut, Cancelled, CaptureDeadlineExceeded,
  CaptureFault, AuthenticationRequired, ProviderLimitReached,
  ProviderReportedError, NonZeroExit, InvalidEnvelope,
  StructuredOutputMissing and InvalidStructuredOutput. Authentication/limits
  need finite documented signals in recognized error fields; ambiguous output
  stays generic. No raw matched text, payload, exception, environment value,
  credential, account identifier or path-bearing diagnostic. Usage/model facts
  remain independently validated. A reported category is not root-cause proof.
- Separate stdout/stderr completion and stdin delivery from process outcome,
  physical termination, duration and byte truncation. States are Complete,
  DeadlineExceeded, Faulted and Unknown; stdin also supports NotRedirected.
  Complete means EOF/finalization, not retention of all emitted bytes.
  Keep the 5-second post-kill wait, then bound aggregate I/O completion to
  5 seconds plus at most 1 second for cancellation/resource release.
  Cancel actual I/O, prevent late writes and do not leak a redaction tail.
  Seal partial artifacts only after their writers are demonstrably closed.
  Preserve physical facts: observed exit zero with incomplete I/O cannot
  produce semantic success, Process completion success or verification Passed.
  Update all shared-executor consumers for this distinction without changing
  their commands, environments or authority.
- Persist new completion facts and explicit incomplete-artifact classification
  additively. Keep historical null evidence unknown and historical results
  unchanged. Update relevant validators, SQL guards and inspection shapes
  narrowly, including failed verification with an actual Exited/zero tuple.
  No capture state supplies a new exit proof or ownership-release path.
- One fixed, versioned Windows verification environment, recorded for new
  executions; no arbitrary environment editor or settings subsystem. Explicit
  keys: SystemRoot, ComSpec, PATH, TEMP, TMP, USERPROFILE, APPDATA, LOCALAPPDATA;
  DOTNET_ROOT, DOTNET_CLI_HOME, NUGET_PACKAGES,
  DOTNET_CLI_TELEMETRY_OPTOUT=1, DOTNET_NOLOGO=1;
  npm_config_cache, npm_config_userconfig, npm_config_globalconfig.
  Derive OS values from the system boundary, PATH from validated selected tool
  directories and the system directory, and profile/temp/cache/config locations
  from per-execution owned scratch with empty npm user/global config files.
  No ambient PATH/config/token inheritance or fallback. Missing prerequisites
  complete truthfully rather than loop. Validate node.exe plus npm-cli.js,
  not direct npm.cmd launching. Approved npm scripts may use the Windows
  script shell. Standalone ProcessAttempt environment policy stays unchanged.
  No automatic scratch deletion while process/capture termination is unresolved.
- Check every required startup Result and meaningful success payload. Abort for
  database/schema/transaction failures or unknown safety-critical state: sequence
  interruption, attempt/verification reconciliation, local-commit/workspace
  recovery, capability catalog/probe-dispatch reconciliation. A coherent success
  that records NeedsAttention, Interrupted or held ownership is not a failed
  startup: preserve the hold and show its existing state normally.
- Return typed outcomes from artifact recovery. A bounded, per-item optional
  historical output seal/read failure, deferred redundant cleanup or unclosed
  old supervision observation is a warning only when database transactions and
  every execution-safety reconciliation remain proven. Missing output is ordinary
  absence. Database/query/commit errors, ambiguous transaction state, unknown
  scope or failures that undermine authority are fatal, not warnings. Inspect
  CloseInterruptedAgentSupervisions.UnclosedCount; Success alone is insufficient.
  Do not delete unrecorded terminal partial output after deferred import, including
  on a later restart; only independently proved redundant partials may use the
  existing cleanup. Warnings never establish evidence, readiness or release.
- On fatal failure, stop later startup, API serving, Ready and hosted dispatch.
  Emit one bounded versioned closed startup-failure frame over the existing
  child stdout protocol before nonzero exit, with stderr carrying the same safe
  code for direct host use. It contains only stage/reason/action enums. Extend
  the shell's existing get_launch_session error path and the frontend's failed
  startup screen to show fixed guidance and a copyable safe diagnostic even
  without an API/session. No raw stderr parsing, arbitrary text, path, secret,
  database payload, exception, new native command or permission. A failure is
  terminal for that launch; late Ready never restores a session. Bound framing
  and validate fragmented, oversized and malformed records.
- Guidance distinguishes storage/repository unavailability (restore access or
  free space, then reopen once after correction) from incoherent/unknown state
  (preserve data/worktree, copy the safe diagnostic, request reviewed repair).
  Never advise database edits, reset, force-release, deletion or repeated blind
  restart. No in-app repair, automatic retry or promise of self-healing is added.
  Nonfatal closed warning categories reach the existing app shell through bounded
  startup metadata and one warning summary, while relevant evidence stays
  unavailable. Keep order, prior committed work, idempotency and side-effect-free
  compositions; no global external-I/O transaction, adoption or release.

### Dependency preparation for the real local toolchain proof

This is test/proof preparation, not a new product package-import feature.
Create a fresh owned staging root, local package feed, profile and caches.
Resolve exact package identities/versions from the committed NuGet lockfiles
and package-lock.json; preserve lockfile hashes throughout. Read only named
package archives from the standard user package cache (or an already explicitly
configured non-secret cache root), never recursively search the user profile.
Require physically contained regular files and bounded copies; reject links,
escaped paths and content that fails the expected package integrity check.

Copy only exact .nupkg archives, including the locked Devalente.Shared packages
and transitive dependencies, to the owned local feed. Do not copy expanded
package trees, NuGet.Config, .nupkg.metadata, HTTP/plugin caches, credentials,
provider plugins, token variables or a user profile. The source cache remains
read-only. Do not rebuild/repack Devalente.Shared as a substitute for a missing
published archive. Restore into the new NUGET_PACKAGES directory with locked
mode, an explicit host-created NuGet.Config whose sources are cleared and name
only the local file feed, --no-http-cache and no interactive mode. Let NuGet
validate lockfile content hashes and signatures; never change locks or disable
those checks. Build/test use --no-restore. No private-feed or credential-helper
call is permitted or presumed; the normal development login is not inherited.

For npm, transfer only exact lockfile-addressed tarball content by SRI digest
from the existing local cache through the already bundled cacache API into a
new owned cache. Do not copy the source index, metadata, .npmrc or node_modules.
Use the original lockfile with npm ci --offline, owned cache and empty owned
user/global config; prove it succeeds with no source-cache fallback. Only
existing bundled tooling may be used; do not install a helper library. Any
required package lifecycle step must also succeed offline under the owned profile.

Retain a bounded manifest of package identities, integrity values and lockfile
hashes, never source paths/configuration/account data. Prove with controlled
tripwires that no package HTTP/auth request is attempted and no source-cache
write occurs; perform the real proof with outbound access disabled using an
existing validation boundary. Missing/mismatched packages or unavailable
isolation/tooling produce DependenciesUnavailable/IntegrityMismatch or a precise
validation blocker, without remote fallback, authentication or repeated restore.
List exact missing package IDs/versions. The owner/package maintainer can later
supply the exact published archives using their normal authorized environment;
that provisioning is not delegated or authorized by this slice. Security/audit
gates are not disabled: missing advisory/signature evidence remains an explicit
unmet gate. Complete independent controlled work and return for review without
claiming the real proof passed.

### Executor preflight challenges and planner dispositions

The executor reported a matching read-only preflight and zero implementation.
Codex independently reconfirmed the local Git state and the incoming handoff's
0DF0435E3551BDD320AC0AAB001139F104BBA2CEDC0C703A831B191D6E71F4B9 hash
before recording the clarification dispositions. Before this activation edit,
Codex also verified C34EDD762FFDFC49A6A43EC9AA237FCDDD23376F0E85A75B29F4E36ADA374428
for the pending record, unchanged local baseline/index/untracked state and a fresh
matching live main. This activation implements the owner's explicit release
authorization; the final dispatch binds the resulting handoff hash externally.
Acceptance F is unchanged.

- WEP-ACT-01: accept the activation stop. The original scope was approved;
  the later dependency/startup revision was pending at that preflight. The
  subsequent explicit owner authorization now resolves this finding. Codex
  releases execution in the same Claude chat; no pending activation remains.
- WEP-DEP-01: accept possible Windows certificate-revocation unavailability as
  a precise validation blocker, not acceptable substitute evidence. Keep default
  signature/revocation policy and warnings-as-errors. Do not set
  NUGET_CERT_REVOCATION_MODE=offline, disable signature checks, suppress warnings,
  warm the OS certificate cache, install certificates or enable network fallback.
  If required validation cannot complete under those conditions, mark F Blocked
  (CertificateValidationUnavailable); actual invalid/revoked signature or hash
  mismatch is a security/integrity failure, not merely missing evidence. No
  blind retry and no claim that a locked hash proves current revocation status.
- WEP-ENV-01: accept that no outbound-denial mechanism has been established
  by the inspected repository contracts. This does not prove none is installed.
  --offline, local sources and HTTP tripwires are not OS egress containment.
  Do not create firewall/VM/container/security policy or assert tool containment.
  Before F, require an identified existing execution boundary covering the
  proof process tree and passing owned negative controls. If unavailable, do
  not run F; mark it Blocked (OutboundIsolationUnavailable). No new machine
  inspection or experiment is granted. Controlled independent implementation
  may proceed after activation; F remains an unmet acceptance gate and blocks
  final GO/publication. A later authority/environment decision is separate.
- WEP-SIZE-01: retain one coherent slice. Shared capture and result semantics
  require adapter/verification/persistence changes together; fatal startup
  diagnosis without a backend requires the narrow shell/React path. The owner
  requested these boundaries together. Recent slice size is not a sizing rule.
  Implement in ordered internal stages and review the complete final diff;
  challenge a concrete unreviewable dependency before expanding or splitting.
  No generic recovery or publication work is added.

Record this planner-selected architecture as ADR-0072, provided the number is
still unused. Preserve accepted ADR history and identify narrow extensions.
Update affected contracts, threat model, engineering-context summary, decision
index and current-work.md. Do not edit ADR-0050 or frozen historical snapshots.
The executor preserves this planner-owned selection record.

### Acceptance and final validation

A. Independently sourced documented-envelope fixtures through all three Claude
adapters; negative schema/error/duplicate/truncation cases; usage/model bounds;
known diagnostics and generic fallback, never success on nonzero exit.
B. Real owned process fixtures retaining stdout, stderr or both; stdin faults,
timeout/cancellation, byte caps, sink/read faults, split-secret redaction,
bounded completion and no late writes. No provider executable is involved.
C. Atomic result/diagnostic persistence, upgrade/reopen/direct guards,
historical compatibility and unchanged exclusion/termination/release proofs.
D. Hosted propagation for all three Claude supervisors and verification/Process
completion; fatal startup Result/exception failures with no API/Ready/dispatch,
coherent held-state success, optional-evidence warnings, positive unclosed
observation counts, successful ordering and side-effect-free composition.
Native-shell/React tests prove safe fatal guidance without a backend, bounded
frame parsing, late-Ready rejection and warning presentation. Run existing Rust
tests with locked/offline dependencies; no new tool or dependency installation.
E. Real local .NET build/test and Node/npm fixture verification through the
production supervisor: success/failure, missing prerequisites, source binding
and no inherited sentinel secrets.
F. In a disposable owned DevalCopilot worktree, its .NET build, one relevant test
filter and frontend npm build through the supervisor using the isolated offline
dependency preparation above. Prove locked private-package restore, missing and
tampered archive refusal, no credential/config copying, no source-cache writes
and no package HTTP/auth fallback. Record recipes/source and package manifest.
Missing tools/dependencies block this evidence, never become a simulated pass
or permission to inspect credentials/install tools.

Focused development checks first; one final stable-input matrix: solution
build/analyzers/warnings-as-errors with explicit API/NSwag generation; complete
Domain, Application, Infrastructure integration, API integration and Architecture
test projects; frontend tests/lint/build; changed harness checks; affected
authenticated collaboration/local-execution-sequence journeys, serial and headed,
zero retries and deterministic provider/gh doubles, including one diagnostic
failure assertion. Verify changed-C# formatting, generated-client reproducibility,
links, sensitive-data controls, dependency/security gates and complete diff.
Keep one concise log with exact commands, elapsed time, failures and evidence
scope. Retain unchanged-input evidence; broaden only with a named trigger.

## Standing decision constraints

The owner also accepted the supervised publication trust direction: supported
installed Git/GCM are trusted but fallible, without source-write denial against
malicious same-user executables. Exact authorization, closed configuration,
durable evidence and uncertain-outcome recovery remain required. This permits
future contract resolution, not publication implementation; ADR-0050 remains
Proposed and P9 remains unsatisfied. Frozen source-write-denial experiments,
patches and preserved evidence remain untouched.

The owner authorized preparation of a bounded qualification plan only.
Its authentication-comparison and two-model-attempt lots still require separate
execution approval. Old allowances remain spent/frozen; no historical grant is
renewed. Provider behavior/version compatibility and historical failure causes
remain unproved.

Current implementation authority includes controlled local tests and local
.NET/Node toolchain validation, plus local disposable Git/worktree setup.
No real installed Codex/Claude/GitHub/GCM product/test call, authentication,
account operation, provider discovery, credential inspection, preserved
qualification evidence, old ledger/reset, new tool installation, commit or push.
Use deterministic provider/gh doubles; fail closed against unexpected provider
traffic. Ordinary use of the executor chat is not a product qualification call.

No Job Objects, process adoption, PID release, general host-loss recovery,
ownership force-release, reservation recovery, resume/retry/takeover, publication,
draft PR, CI loop, generic workflow engine or unrelated UI/inspector expansion.
ADR-0065/0066's existing invoked-process-only evidence and authority remain.
AI Memory stays deferred. Existing unexplained failures and residual filesystem,
account/context, non-Windows and raw Git-read limits stay open; later passes
do not retrospectively explain or erase them. See current-work.md open risks
and this file at the baseline commit for prior finding/evidence detail.

### Review and continuity

Preserve REV-01 through REV-10 and F-01 through F-14. This selection directly
addresses REV-02/03/07 and F-02/03/04/06/07/11; improves REV-01/F-01 diagnosis
without proving cause. REV-04/F-05/F-13 recovery and REV-05 publication are
separate. The owner approved this revision and Codex released execution; no
activation approval remains pending. Do not repeat existing publication-trust
or qualification-preparation decisions. The final activated dispatch supersedes
earlier conditional text. F stays mandatory; its known possible blockers permit
independent implementation, never a false pass or final GO.

Return unstaged, uncommitted and unpushed for Codex GO/NO-GO, with current-work.md
as implementation evidence, changed paths, checks/results, fresh/retained/not-run
evidence, blockers and Git state. Keep corrections in the same Claude chat.
A material challenge pauses dependent work, not independent authorized work.
No executor self-acceptance or publication follows from this selection.

CONTINUE_THIS_CHAT for Codex review; retain the existing Claude executor chat
that performed only preflight. Current Codex context percentage is not exposed;
no percentage is inferred.

## Retained review and evidence limits

Earlier review findings, failed checks, evidence qualifications and publication
records remain preserved in [this handoff at the selected baseline](https://github.com/denisvr/DevalCopilot/blob/e880c4da311f65d7688196105364f239b4f116f0/docs/roadmap/planner-handoff.md#retained-review-and-evidence-limits).
They are historical evidence, never renewed instructions or allowances. Current
residual risks remain in [current-work.md](current-work.md#open-risks).

### Delivered evidence and preserved limits

The earlier delivered-evidence account remains in [this handoff at the selected
baseline](https://github.com/denisvr/DevalCopilot/blob/e880c4da311f65d7688196105364f239b4f116f0/docs/roadmap/planner-handoff.md#delivered-evidence-and-preserved-limits).
Its qualifications are retained; this selection runs no old check or experiment
and establishes no new provider qualification or historical failure explanation.

### Owner cleanup override (2026-11-02)

The owner explicitly superseded prior feature-disable instructions: keep Windows
Sandbox enabled until all work is finished and we confirm it is no longer needed.
After attempt 3, close only the owned guest and controls; do NOT disable the feature.
This does not authorize additional guests, cache reads, builds or publication.
Fresh preflight remains main at the baseline, empty index, 263 modified tracked
paths and 60 untracked files; fresh live origin/main matches. Attempt 3 may proceed.
