# Planner/reviewer handoff

Planner-owned scope, rationale and review decisions; not a delivery ledger or
approval log. It replaces its current state on each selection or review
decision. Procedure: [development workflow](development-workflow.md). Git and
code prevail over this summary.

## Active slice: TEST-48-01 (GO for the reviewed test diagnostics)

- **Decision (2026-10-16): GO.** Publish exactly the two test files and two
  checkpoints frozen in `logs/planner-review/test-48-01/publication-manifest.json`.
  Codex remains planner/reviewer; Claude remains executor. No blocking finding
  remains. No next slice, closure edit or runtime publication is authorized.
- **Accepted result.** The existing identical-request fact retains four hosts,
  sixteen concurrent submissions and the same success, response equality,
  row/event count and identity conditions. Its diagnostics precede assertions,
  associate each returned response with its original request/host ordinal and
  contain only those ordinals, numeric HTTP status and a fixed admitted code
  or fallback. Parsing is bounded to 8 KiB and depth 8; at most sixteen
  response records fit 256 UTF-8 bytes each, beside one bounded summary line.
  No new raw-body or arbitrary error-text output is introduced.
- **Executor decisions addressed.** The aggregate `Assert.True` has the same
  all-200 condition for the unchanged sixteen-result submission and avoids
  `Assert.All` formatting a response object. The six closed fallback literals
  preserve the allowed diagnostic boundary. The unused filter alternative
  matches no test and does not invalidate the 36 selected synthetic cases.
  Separate synthetic tests avoid the base class's Git-scene construction.
- **Independent review.** Codex inspected both complete test sources, the
  checkpoint and planner diffs, exact returned identities, development
  failures and final evidence. The executor TRX contains 37 passes, zero
  failures: 36 synthetic cases and one real fact (12.7488 s), with all sixteen
  expected 200 diagnostic records. Source hashes match the before/after
  records. The independent API-test-project build passed with zero warnings
  or errors (1982 ms). No product source, contract, dependency or fixture changed.
- **Retained failure-message gap resolved.** The executor checked the thrown
  synthetic assertion message inside its test but did not retain that message.
  Another real concurrent run was unnecessary. An ignored reviewer console
  probe directly invoked six cheap synthetic facts from the reviewed assembly,
  then captured the actual `TrueException` from the reviewed formatter and
  status assertion with fabricated unavailable/conflict responses. It verified
  both codes, request/host ordinals and absence of the sentinel/raw JSON. The
  actual message and probe are retained under
  `logs/planner-review/test-48-01/review1/capture-probe/`. No host, provider,
  production lineage or real concurrent fact was invoked by that probe.
- **Review failures and limits, preserved.** The independent VSTest synthetic
  attempt announced one matched assembly and produced no named result before
  interruption; its cause remains unexplained, never a passing test run. A
  project-based capture-probe build then failed NU1900 while reaching package
  vulnerability feeds; no audit was suppressed. Direct compilation reused
  the installed reference assemblies and the reviewed built test dependencies,
  without restore or new packages; compile and capture exited 0. Its CS2023
  warning records `/noconfig` being ignored in the response file. This is probe
  evidence, not a clean project build or a VSTest pass. The separate successful
  product/test-project build and executor TRX retain their actual scope.
- **Other evidence.** Original executor CS9007 and synthetic unknown/malformed
  expectation failures remain retained. Its formatting and four-file hygiene
  checks passed. The executor's saved `complete-diff.patch` omits the new file
  because ordinary `git diff` excludes untracked paths; Codex read that file
  separately and retains its own complete four-path review artifact. Product,
  snapshot, architecture, persistence, frontend, client, browser and qualification
  evidence remain retained, not rerun. No stress loop or real-fact repeat ran.
- **Remaining limits.** The historical 409 remains unattributed; the accepted
  [ADR-0048](../decisions/0048-approve-one-future-github-branch-push-of-a-bound-recorded-local-commit-on-explicit-request.md)
  snapshot fix is unchanged. A hung submission still yields no record, and
  retention beside a host-disposal failure was not exercised. Neither a later
  pass nor the direct probe explains any historical or review interruption.
  These limits do not change approval semantics or expand this diagnostics slice.

## Publication boundary

Verified review preflight: `main`; HEAD, local `origin/main` and live main all
`f7e42c1c7d574a9724ee8755684e4b39179bc53f`; 0/0; empty index;
three tracked paths unstaged (current-work, planner-handoff and the durability
test), and exactly the sibling diagnostics test untracked. The executor
preserved the selected planner's 11228-byte identity through its return;
this planner-owned GO replaces that selection and is included in the manifest.

Verify the frozen manifest before staging and the staged/committed modes and
blobs against it. Commit exactly the four reviewed paths once with sole parent
`f7e42c1c7d574a9724ee8755684e4b39179bc53f`, subject
`test: retain bounded approval replay diagnostics`; push `main` normally to
`origin/main`, verify the live ref and report the SHA in the same executor turn.
A manifest discrepancy or material change returns for review. A failed push or
diverged remote stops publication; no force-push, reset, cleanup or history
reconciliation. Keep the committed pre-publication wording; no closure edit.

## Verified baseline and continuity

DOC-49-01 remains published at `f7e42c1c7d574a9724ee8755684e4b39179bc53f`,
sole parent `cab3756c24e58524f25ef3ba607236d63fa9426e`. Its independent
publication verification is retained at
`logs/planner-review/doc-49-01/publication-verification.json`; its manifest
SHA-256 remains `f2833d7fc2d6374867697bfae7ee9a2e920409ad2479cb01c5e3ebd7db396a23`.
R1 is resolved; the original checker exit 1 and the two pre-existing
engineering-context false positives retain their original classification.
No closure edit or repeat publication of that delivery follows.

Review and publication remain in this planner chat and the same TEST-48-01
executor chat. Codex independently verifies publication before the next
continuity decision. No observed context-usage percentage is asserted and no
next slice or chat transition is dispatched by this GO. WF-02/WF-03 stay
unselected. WF-COL-01 and P9-SEM-01 remain published at their prior baselines;
ADR-0021 delivery of the root instructions was not exercised and existing
sealed manifests remain unchanged. Increment 4 provider qualification,
Increment 5 coordination/recovery and Increment 6 publication/CI orchestration
remain incomplete. ADR-0049 reports repository access only; ADR-0050 remains
Proposed, P9 unproven, no mechanism selected and human rearm/resolution absent.

## Preserved evidence and uncertainty

The [semantics note](../architecture/github-publication-native-io-semantics.md)
narrows only H37/H40 and part of static-audit U2. H40's failed-query output
is undocumented; query and wait rights differ. H37's stdout argument is
conditional on suffix-only loss, with possible consumption-on-failure open.
K1-K4 remain open; future work cannot validate the historical stderr capture
or the unchecked canonical exit query. No saved web-page copies exist.

P9-SEM-01's `wc -lc` deviation read the full contents of three excluded audit
reports for counts. No content was displayed or used in the analysis; review 1
identified their manifest memberships without reopening them. The correction
did not reopen them. This is not retrospective permission or a new read grant.
Preserve the failed grep and the recorded pre-final wording mismatch.

P9-EV-01 remains verified at `465e60c`, sole parent `4c9102c`; the
[capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md)
establishes retained-byte consistency only. The malformed receipt remains a
named failure and the earlier summary a derivative. Correction protection
hashing read excluded ctl/app fixtures: 69 equal saved hashes comprised
11 reader artifacts, 57 gate files and the planner; 12/56 was wrong. The
reviewer compared saved records without reopening fixtures. Preserve that
read-boundary deviation and prior parser/checker failures; no metadata proof
or another sweep follows. Original evidence and publication records stay frozen.

## Standing decision constraints

- [ADR-0050](../decisions/0050-define-the-first-approved-github-branch-publication-execution-contract.md)
  remains Proposed; P9 remains unproven. No mechanism is selected.
- Frozen patches A/B/C, their chain and C's added assertions remain unapplied
  and unaccepted. The write-ahead and sticky-flag alternatives stay unaccepted.
- The one-profile/two-child experimental authority is spent. No renewed native
  build, launch, profile query, reuse, creation/deletion, security operation,
  credential, helper or provider operation is authorized by this selection.
- Retained profiles, fixtures, protected DACLs, labels, grants and ignored
  evidence stay untouched; no cleanup, permission reset or fixture sweep.
  File hashes do not seal live ACL, label, grant or profile metadata.
- Agent qualification stays `Blocked`/`TargetsNotReal`, both allowances unused.
  Git/GitHub qualification is separate and unstarted; supported GCM versions
  and real compatibility remain unqualified.
- Owner GCM/ancestry-profile and residual name-to-ID risk acceptance was
  design-only. Human rearm/resolution remains absent; no runtime authority.
- Preserve the v1 defect/protocol deviation, inconclusive A2 result, native
  error/capture limitations, lost/pre-redaction output and live-state
  uncertainty in the [feasibility note](../architecture/github-publication-source-write-denial-feasibility.md).
  First-push claims requiring source re-reading remain in the
  [execution evidence](../architecture/github-branch-publication-execution-evidence.md).
  Reported repository access is not branch-push permission.
- Concurrent-replay 409, target-test 500, disposal `NullReferenceException`
  and unnamed Api 2067/2068 remain separately unexplained. Later passes prove
  neither flakiness nor irrelevance; no increment or MVP completion is inferred.

Execution-facing open risks remain in
[current-work.md](current-work.md#open-risks). WF-01 and earlier facts remain
at their published baselines and the frozen
[execution history](current-work-history-ba1d4c2.md) and
[planner history](planner-handoff-history-ba1d4c2.md); neither snapshot is edited.
