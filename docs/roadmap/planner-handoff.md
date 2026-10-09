# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md),
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Current decision (2026-10-09): GO for race-safe first-use local-commit storage

Exactly one bounded slice is accepted: eliminate benign first-use initialization
refusals in the host-owned local-commit control storage and prove concurrent
preparation, execution and recovery without fixture pre-initialization.
Claude is executor; Codex owns architecture and acceptance. Start one new
executor chat after the verified prior publication. The selection below was
implementation-only; the reviewed snapshot now has the publication GO below.

### Review decision and publication authority

Codex gives GO for the complete six-path unstaged diff on main at
34f56b918a7eb6812282ed095d6ef52f37462754, including this review record and
the factual ledger edits. Local and live origin/main match; the index is empty.
Four tracked paths are modified and two test files are untracked. No source or
test correction was required at review. ADR-0029/0030/0031 and the generated
client are unchanged.

The non-truncating read open inspects the actual handle's length, allows
overlapping initializers, refuses occupied configuration/hooks and rechecks
every call. The internal observer is bounded test synchronization, not authority.
The cold Api fixture keeps the previous exact outcome, artifact, admission,
cleanup and restart assertions. Read/write/delete sharing is accepted within
this contract; it does not freeze a pathname against later hostile replacement.
The surviving Delete-sharing mutant is not evidence of a required product
behavior, and no broader filesystem or recovery guarantee is granted.

Fresh independent Codex checks, each once, sequentially: solution build
--no-restore -p:UseSharedCompilation=false -m:1 (0 warnings, 0 errors);
Infrastructure initialization, invalid-control-state, preparation and ownership
classes (57 passed); Api competition, refused-preparation and restart classes
(41 passed); full Architecture (47 passed). No failure or skip occurred in
these selections. Codex inspected the executor's final sequential logs:
full Infrastructure 1669 passed and four existing symlink-capability skips,
full Api 1402 passed, Architecture 47 passed, and the rebuilt mutation matrix
(14 killed, one scoped survivor). Those full runs and mutations remain executor
evidence, distinct from the independent checks. Historical failures and their
unproven causes remain recorded.

Codex corrected only two factual ledger phrases: empty read-only files are
accepted, and the executor's 40-test selection is not assigned an unverified
class composition. The independent selection and its exact filters are recorded
separately. No implementation, test, client, dependency or ADR changed.

Publication must use the frozen reviewed six-path manifest, with raw hashes,
sizes and statuses checked against Git before staging. Commit that substantive
snapshot, push main normally as a fast-forward, fetch and independently verify
live origin/main, then run the same build and three test selections on the
published substantive commit from fresh logs. Preserve any failure and stop;
do not repeat an unchanged failing acceptance run for green. Only after success
may the executor make a current-work.md-only factual closure within this entry,
commit/push it normally and verify all refs and a clean checkout again. No amend,
force push, history reconciliation or material post-GO edit is authorized.
Any material change returns for review. No next slice is selected and no
increment or MVP completion is claimed.

### Independently verified start and published predecessor

Branch main; HEAD, local origin/main and independently queried live
refs/heads/main are 34f56b918a7eb6812282ed095d6ef52f37462754. Before this
planner edit the checkout was clean: nothing staged, unstaged or untracked.
Substantive ffd0be0f8b16558e13fe4e481b23217a568386cb has parent
c12c7dd5b9c53064be66bd998d966659fdbd9822. Its 18 paths and statuses match
the approved manifest; the 17 current raw hashes outside the closure ledger
still match it. Closure 34f56b9 has that substantive parent and changes only
current-work.md within the slice's entry (5 additions, 1 deletion). Codex
reviewed the closure and verified the remote. Reported post-publication
build/Api 1402/Architecture 47 are executor evidence, not new planner test runs.

Expected executor start after this edit: same branch/HEAD and local/live refs,
empty index, only docs/roadmap/planner-handoff.md modified, no untracked files.
Generated client SHA-256:
22074a066c91a1fb1dfc0afc602573f14e72f9c6ffd8047633b2640c20e20b64.
Preserve this planner selection and ADR-0029/0030/0031 byte-for-byte.

### Why this candidate

The previous slice restored precise outcome/artifact proofs, conditional on
sequential initialization of storage. Production LocalCommitStorage still
checks File.Exists(empty.gitconfig), then writes/truncates the pathname through
File.WriteAllBytes. Two first users may race; an IOException returns false and
is surfaced by the real preparer as HooksDirectoryNotEmpty. The availability
failure is concrete, not an unproven provider contract or a request for more
context samples. Removing it makes the existing local-delivery path and its
concurrency acceptance usable from a fresh installation.

Codex's independent single probe invoked the unchanged Infrastructure method
through two separate LocalCommitStorage instances sharing each of 300 fresh,
owned temporary roots: 22 pairs had one false result; later sequential calls
had no failure. It establishes a reproducible mechanism, not the historical
Api failure's cause or a production frequency. The probe also confirmed that
OpenOrCreate with read access can create an empty file, admit an overlapping
read handle and preserve existing nonempty bytes. It is contract research,
not an implementation or acceptance result. Ignored source and a result note
are under frontend node_modules/.cache/codex-cold-storage-selection-20261009.

Increment 4 still lacks proven Claude account enforcement, eligible session
resume, compaction and additional permission contracts. An autonomous
coordinator, lifecycle recovery controls or remote publication require broader
authority decisions. This slice removes a demonstrated defect in the already
accepted ADR-0029 local-delivery path before taking on those larger outcomes.
No increment or MVP completion is claimed.

### Selected contract and architectural direction

1. Concurrent benign first use of one host-owned storage root must initialize
   its empty configuration without a false refusal caused solely by another
   initializer. Support independent storage instances sharing that root; a
   per-instance monitor or fixture warm-up is not the solution.
2. Use a documented filesystem create/open operation that never truncates or
   rewrites an existing configuration file. Prefer a single non-truncating
   open-or-create with read access and compatible reader sharing; inspect the
   actual opened file's zero length before accepting it. Do not use a separate
   existence test followed by a write, or introduce retries, sleeps, a global
   scheduler, a named lock or new recovery authority.
3. Preserve fail-closed admission: a nonempty or unopenable control file, a
   directory at its filename, genuine I/O/access failure, or any entry in the
   hooks directory refuses initialization. Preserve existing bytes and entries:
   no overwrite, truncate, repair, delete, adoption or replacement. An existing
   valid empty file remains usable. Recheck the configuration and hooks on
   every call; a prior successful initialization is not permanent authority.
   Keep current caller mappings and signatures; improving their coarse refusal
   name is not selected.
4. Keep the change local to Infrastructure/Features/Runs/LocalCommitStorage
   initialization. A small internal, feature-owned collaborator or opening seam
   is allowed only if necessary for deterministic interleaving/real-I/O tests;
   the default path must still execute the real filesystem operation. No public
   production test switch, generic filesystem framework or Application port.
   This restores ADR-0029's empty controlled environment, not a new ADR reversal.
5. Remove the previous fixture's sequential initialization in
   LocalCommitTestBase.Support.StartCompetitionAsync. Establish the cold
   precondition before requests, then retain both actual Prepared results,
   captured identities, exact admission, real cleanup, inert-sibling bytes,
   execution and restart assertions in both orders. Retain the induced-refusal
   controls and failure-safe release. Do not serialize, retry or manufacture a
   successful preparation to make a cold-root test pass.
6. Prove invalid control state prevents the local-commit adapter from issuing
   Git commands, and sentinel hooks/configuration bytes remain unexecuted and
   unchanged. A second call after successful initialization must refuse when
   the control file or hooks directory has become invalid. The source checkout,
   recorded facts and unadmitted artifacts retain their existing guarantees.

Official .NET 10 contracts checked:
[FileMode](https://learn.microsoft.com/en-us/dotnet/api/system.io.filemode?view=net-10.0)
and [FileShare](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-10.0).
Create overwrites/truncates, whereas OpenOrCreate opens an existing file without
that behavior; sharing flags control overlapping opens. Verify the actual
chosen access/sharing on the Windows host, not from a CLI default or mock.

### Boundaries and stop gates

Allowed: that initialization method, a necessary narrow internal collaborator,
focused Infrastructure initialization/preparation tests, the cold competition
fixture and necessary local Api assertions, current-work.md and narrowly
relevant documentation. No Domain/Application behavior, endpoint/schema/client,
frontend/browser feature, package, migration, Git arguments or process protocol,
native/ref/index/lock, artifact layout, reservation, cleanup, recovery, janitor,
provider policy or remote publication change. ADR-0029/0030/0031 are preserved.

Stop and report if the bounded solution requires clearing occupied state,
accepting an unproven/nonempty configuration, weakening hooks suppression,
adopting a foreign lock, broad filesystem containment work, changing a Git or
recovery authority, or hiding a new Api/Infrastructure failure. Do not repair
unrelated cancellation or admission failures opportunistically. This does not
claim to freeze control files against hostile replacement after the final
check or to prove cross-platform physical containment.

### Acceptance evidence and return

Write failing-first regressions before production changes. Use finite,
controlled first-use/sharing interleavings and independent instances; a stress
sample can supplement them but cannot be the only proof. Keep any test seam
local and distinguish actual real-I/O evidence from an injected fault. Cover
fresh initialization, benign overlapping opens, existing empty/nonempty state,
directory/unopenable state, later hook insertion/configuration change, and
unchanged sentinel bytes. Keep synchronization and failure cleanup bounded.

Run affected Infrastructure initialization/preparation and Api competition,
refusal, cleanup and restart classes first. Then run a serial final solution
build, full Infrastructure, full Api and full Architecture on final code;
report actual counts/skips. Preserve failures, and stop on an unchanged failing
acceptance run without repeating for green. Rebuild/restore meaningful mutations:
destructive open or invalid-file acceptance, incompatible sharing/race-prone
initialization, and cached/skipped hooks or configuration validation. Explain
equivalent/surviving mutants and any unproved interleaving, never count a failed
build as a kill.

The generated client must remain byte-identical. Check tracked/untracked
whitespace, NUL, type layout, affected formatting and documentation links.
Retain Domain/Application, unchanged frontend/browser and dependency audits
with actual scope; no new browser suite or real provider is required. Preserve
all historical failures and the predecessor's initialized-storage limitation
as historical evidence, not a retroactive green claim.

Return the complete unstaged, uncommitted, unpushed diff and commit-ready
current-work.md entry with exact inventory, actual command outcomes, failing
excerpts, fresh/retained distinction and residual risks. Keep corrections in
this executor chat. That uncommitted review return has now been accepted above;
only the frozen snapshot has publication GO. No subsequent slice may be selected
by the executor.
