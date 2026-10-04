# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md),
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Selected slice (2026-10-04): Attested tracked-change text in new Agent manifests

### Review decision (2026-10-04): GO for the reviewed R1-R2 corrected slice

R1 and R2 are closed. The Projects result owns an immutable snapshot at
construction and init/with replacement; no caller collection or returned cast
can replace attested text. The baseline cache retains text only for an admitted
file, sharing the string already owned by its accounted facts; omitted reads no
longer accumulate cached source text outside the observation budget. Independent
real-Git ownership and retention regressions pass. The selected architecture and
all exclusions below remain unchanged; no next slice is selected.

Independent preflight: main, HEAD, local origin/main and live refs/heads/main
at 1d61092e981e714e7a3bc5ac139423b587247bec; empty index; 47 modified tracked
paths and 22 untracked paths, 69 total (68 executor paths plus this record).
The submitted NO-GO record hash
3340b56646d2279bff125109f95e66718e6a127628ba9d55ac77f9cd1c271e95 is historical;
this GO edit supersedes it. Generated client remains
d283da056d20b9f1ea9e14cd6ac452988a3f13226fb8c8f8c6bb90e5790b0819.

Independent correction review: solution build --no-restore
-p:UseSharedCompilation=false -m:1, 0 warnings/errors; Application filter
Tracked|AgentEvidenceProjection|InstructionContext, 629 passed; Infrastructure
TrackedFileSourceReaderTests|GitWorkspaceTracked*, 84 passed; API filter
InstructionDeliveryProjectionTests|UntrackedPreviewClaimDeliveryTests|
UntrackedPreviewFormsDeliveryTests|ClaudeCriticalReviewSupervisorHostedTests,
106 passed; full Architecture, 20 passed. Harness 76 passed, followed by one
canonical test:e2e:all with normal authentication, host composition and all
supervisors: Chromium 11/11 and journeys 2/2, exit 0, no failure, skip or rerun.
All 69 changed/new files passed hygiene; 224 local Markdown links resolved;
git diff --check passed, apart from the known planner CRLF notice. Only the two
older owned e2e roots remain and were untouched. Full suites, frontend and
mutation evidence from the executor remain separately reported in current-work;
these filtered review runs do not replace or relabel those full suites.

GO authorizes only the exact 69-file snapshot handed to the executor, including
this updated planner record and current-work. Verify its path/hash/size manifest
before staging. Any material change returns for review. Commit the reviewed
substantive slice on the verified parent, push main normally to origin/main,
fetch and verify local and live refs equal that commit, then run the specified
post-publication checks sequentially. Stop on failure or remote divergence;
no amend, force push, history reconciliation or unexplained rerun is authorized.
Only after all checks pass, make a separate factual closure within this slice's
current-work entry, push normally and verify live publication and a clean tree.
Preserve prior failure evidence and label fresh versus retained checks. Do not
embed the closure's own SHA or claim Increment 4 completion. Claude remains the
executor; Codex retains next-slice selection after verified publication.

Codex selects exactly one bounded Increment 4 outcome: a new Agent manifest
delivers tracked-file diff, whole hunks and changed-line samples only when their
content agrees with the captured HEAD blob and physically proven current bytes.
Unsafe or unprovable files are explicitly omitted, while safe siblings remain
useful. This closes the known tracked-text disclosure route; it does not extend
context sampling or claim containment of every filesystem read.

### Verified publication and executor preflight

The preceding model-limit slice is published: substantive
53b2eb69a7ea5677b13db4a4735c476003f6454b on
122d4ebaa81f4d5899cf5e69e916de33bc67318c; bounded corrective
432b70ac75ec4f7a9caf8deb2d773c2a22affe8f on that substantive commit; factual
current-work-only closure 1d61092e981e714e7a3bc5ac139423b587247bec on the
corrective commit. All pushes were normal fast-forwards, with no amend or
history reconciliation. Codex fetched and independently verified main, HEAD,
local origin/main and live refs/heads/main equal the full closure SHA with a
clean checkout before this selection. current-work distinguishes fresh full,
filtered and retained evidence and preserves failed checks and their corrections.
Corrective post-publication build 0 warnings/errors, Application 50, harness 69,
then canonical Chromium 11 and journeys 2 passed with no skip or rerun.
Generated client SHA-256:
d283da056d20b9f1ea9e14cd6ac452988a3f13226fb8c8f8c6bb90e5790b0819.

Expected branch main, exact HEAD 1d61092e981e714e7a3bc5ac139423b587247bec; nothing staged; only
docs/roadmap/planner-handoff.md modified after this selection; no untracked paths.
Preserve this planner-owned file byte-for-byte. Claude remains the next executor;
the owner's Codex execution reassignment applied to the completed prior slice.
Selection grants no next-slice implementation by Codex, commit/push GO or
Increment 4 completion.

### Evidence and comparison

GitWorkspaceEvidenceReader still obtains raw git diff HEAD through repository
pathnames. AgentEvidenceProjection protects the two reserved root instruction
names; ADR-0022 protects generic untracked previews. Other tracked blocks can
still contain an outside hard link's text and flow through ChangeEvidenceManifest
into every role's seal and adapter stdin. The code and accepted ADR-0022 explicitly
confirm this gap. Checking a name before Git reopens it cannot close the race.

This demonstrated disclosure path takes priority over another sampling expansion
or another provider observation. Account-allowance observation is not threshold
enforcement or invocation eligibility. Claude account allowance, provider-session
resume and compaction still lack safe executable contracts; CLI defaults and
process doubles cannot establish them. Increment 5 coordination, lifecycle and
publication need separate authority decisions. None is bundled into this slice.

### Selected architecture and precise boundary

- Change only new CaptureForAgentContextAsync delivery and its manifest projection.
  Preserve the raw status/HEAD/diff/hash capture, checkpoint fingerprint and
  serialization, CaptureAsync, ordinary UI evidence and existing preview contract.
  Historical sealed manifests replay byte-identically; no rebuild at dispatch.
- Infrastructure acquires the current tracked source through a held Windows
  handle. Before length or bytes, prove its exact case-sensitive final path under
  the resolved owned worktree, regular/non-device/non-reparse attributes and
  exactly one link. Access failures are never absence; a deletion needs a
  coherent owned-parent/absence observation, otherwise omit. Recheck those facts
  after the bounded read, reread the same
  held handle around identity verification, and observe the source again within
  the existing capture bracket. Use the existing Windows proof, never alias
  enumeration or lexical containment as a substitute. Other hosts omit.
- Read the old side from an exact blob of the captured HEAD, not the index or
  another working path. Resolve the literal path and object type, inspect size
  before content, and verify byte length and blob identity. Fixed Git plumbing
  uses no filters, textconv, shell or object writes. New object reads must disable
  replacement objects and promisor lazy fetching and use literal paths; missing
  local objects are omissions, never a network fetch. A decoded process string is
  not raw-byte proof unless re-encoding matches both object size and identity.
- Do not deliver the raw working-path patch, even after a preliminary name check.
  Build a deterministic, bounded host comparison solely from the owned baseline
  and proven current snapshots. Use a linear common-prefix/common-suffix scan
  and one complete replacement hunk for the remaining middle, with at most three
  unchanged context lines at each edge. Preserve exact line terminators and
  final-newline state; emit canonical quoted paths and numeric hunk ranges.
  No raw headers, function text or unchecked metadata is copied. This is a
  conservative host comparison, not Git's minimal/filter-normalized patch:
  unchanged middle lines may appear as replacements, and line-ending-only
  differences remain visible. State that fixed limitation in the manifest.
  No new diff optimizer is selected.
  A changing source fails RepositoryChangedDuringCapture. Unsupported or
  unprovable sources are omitted with fixed safe accounting, never replaced
  by an unchecked fragment or raw-diff fallback.
- Support ordinary tracked edits, staged additions and deletions against HEAD,
  empty files and exact LF/CRLF/no-final-newline text. Unsupported modes, unmerged,
  symlink/submodule, binary or invalid UTF-8 forms are explicit omissions. Host path/header construction must handle
  admitted quoted paths; a path it cannot
  encode safely is omitted. Repository diff prefixes and raw patch headers do
  not influence this comparison. A mixed capture retains safe sibling hunks.
- Bounds: existing 128 changed paths, 512 KiB raw capture and 10-second Git
  timeout remain. Each before/current source is at most 256 KiB and 8192 lines;
  at most 512 KiB of source buffers is retained per observation, processed in
  ordinal path order. No truncation into apparently complete source. Bound
  verification work by those sizes; no unbounded/quadratic comparison search, new
  diff optimizer, temporary source files or repository/index/object mutation.
- Carry owned immutable, provider-neutral source/omission facts through the
  Projects Application boundary. Every tracked path is accounted for once.
  Builders re-derive the safe projection for any reader; missing or incoherent
  attestation never admits a legacy raw patch. The fixed omission reasons and
  truthful completeness survive every existing manifest reduction. Keep
  instructions reserved, plans/schemas/grants untouched, and the 32 KiB ceiling,
  whole-hunk selection and explicitly incomplete sampling.
- Add ADR-0024 narrowly advancing ADR-0021/0022 for NEW tracked delivery; update
  protocol, engineering context and the one roadmap gap statement. No new
  HTTP/DTO/client, schema/migration, dependency, provider flags/permissions,
  budget, grant, workflow action, scheduler, recovery or browser control.

Codex also verified the local-only/literal object commands on installed
Git 2.53.0.windows.1 against a tracked project file (type and size only).
The official [Git patch contract](https://git-scm.com/docs/diff-format),
[raw blob/type/size contract](https://git-scm.com/docs/git-cat-file),
[stdin identity contract](https://git-scm.com/docs/git-hash-object) and
[local-only object controls](https://git-scm.com/docs/git) support these
capabilities. They do not prove physical containment or race safety; the held
handle and content agreement establish the selected boundary.

### Stop gates and acceptance evidence

Stop if safe agreement requires trusting Git's reopened working path, writing
objects/temp source files, broad process-port redesign, altering fingerprints or
historical replay, weakening ordinary source eligibility/permissions, or widening
scope. Report the concrete blocker; do not replace proof with a success flag or
Unknown-only scaffolding. Containment omissions are normal, accounted outcomes.

Write real NTFS hard-link regressions first against the parent: outside links,
inside multiple names, unavailable facts, parent substitution, second-link and
transient content changes. Prove no outside sentinel in the whole returned Agent
capture, whole manifest/seal or adapter stdin, including oversized hunks/samples,
all seven builders/eight claim handlers and every existing variant. Assert unsafe
sources never reach the new byte-identity operation; ordinary raw observation is
an explicit separate control. Cover quoted paths, repository prefix/filter/EOL
settings and raw header/function-text sentinels remaining quarantined, baseline
corruption/truncation, clean siblings,
all bounds, additions/deletions/empty/line endings and omitted metadata.
Independently prove accepted whole patches reconstruct the intended before/after
bytes in disposable fixtures, and retain existing whole-hunk/sample contracts.

Cover missing/overclaimed reader facts, all manifest fitting steps and historical
restart replay. Extend the existing native-double journeys to inspect a regular
tracked source and its sealed agreement; add no new journey framework or real
provider. Mutate pre/post proof, byte agreement, raw fallback, omission accounting
and replay rebuilding separately; each must fail meaningful focused checks.

Run affected tests first, then sequential relevant full backend suites and
Architecture, build/regenerate client (must remain identical), full Vitest,
typecheck/strict e2e, lint/build, harness and one canonical test:e2e:all only after
harness passes, normal authentication/composition/all supervisors. Retain every
failure with its reason; do not rerun unexplained failures into green. Check
formatter against baseline, hygiene and links; distinguish retained audits if
dependencies stay unchanged. Preserve the two older owned e2e roots.

Return a complete unstaged/uncommitted/unpushed diff, a commit-ready current-work
entry with exact inventory and checks actually run, and concise evidence for
Codex GO/NO-GO. No publication or next-slice authority.

Remaining risk is explicit: raw Git hashing/diff and ordinary local evidence
still read named paths; committed blob content is not confidential by inference;
source topology may change after observation; admitted content is unredacted in
the provider input/artifact viewer; Windows-only physical proof; process doubles
do not prove real-provider reliability.
