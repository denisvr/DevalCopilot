# Planner/reviewer handoff

Planner-owned scope, rationale and review decisions; not a delivery ledger or
approval log. It replaces its current state on each selection or review
decision. Procedure: [development workflow](development-workflow.md). Git and
code prevail over this summary.

## Active slice: P9-SEM-01 (GO for the reviewed documentation diff)

- **Decision (2026-10-16): GO.** Publish exactly four paths: the
  [semantics note](../architecture/github-publication-native-io-semantics.md),
  [architecture index](../architecture/README.md),
  [current-work.md](current-work.md) and this planner record. The frozen
  publication manifest identifies the approved bytes. No closure edit.
- **R1: resolved.** Conclusion, implications, H37-c, K4 and the checkpoint
  preserve the suffix-only assumption. A possible consumption-on-failure gap
  is distinguished; general stdout-loss detection is not established.
- **R2: resolved.** Provenance limits the five-entry membership/hash claim
  to those checks and explicitly discloses the full-content `wc` reads of
  three excluded reports. Their membership is attributed to review 1;
  the correction did not reopen them. No retrospective authorization.
- **R3: resolved.** API questions, future measurement controls and historical
  uncertainty are separated. Future work cannot validate the canonical
  stderr capture or prove that its recorded exit values were checked queries.
- **Accepted scope.** Documentation-to-source evidence for H37/H40 and only
  the relevant part of U2. The index entry is accepted. No canonical failure,
  measured exit, complete capture, mechanism or P9 proof is established.
  Codex remains planner/reviewer; Claude remains executor. WF-02/WF-03 and
  any next slice remain unselected.

## Independent review and Git discrepancy resolution

Correction review preflight: `main`; HEAD, local `origin/main` and live
`refs/heads/main` all `465e60cfc31d02961fc3970d44e65bb4cfe5ee29`; 0/0;
empty index; five tracked modifications (`AGENTS.md`, `CLAUDE.md`, architecture
index, current-work and planner-handoff), plus the new semantics note untracked.
The returned planner matched the dispatched 8441-byte `c2735ffd...468b` hash
before this GO update. The index and both instruction hashes match review 1.

**Owner instruction edits resolved.** Codex made the AGENTS/CLAUDE edits in
this chat at the owner's explicit request during the slice. Their bytes and
diff match the executor's discrepancy record: SHA-256 `cff3b663...21c4f` and
`d94da915...2453`. They are authorized separate work, not executor edits or
part of P9-SEM-01. Preserve them unstaged; exclude both from this slice's
eventual commit. No whole-checkout staging or instruction rollback.

Independent correction evidence: the reconstructed round-1 note matches
`f98cd743...6930`; a fresh comparison confirms exactly seven hunks, +48/-22.
The complete correction and updated execution facts were inspected. The
inspected executor checker passes on all three submitted paths (44 ms,
zero findings), and `git diff --check` passes. The protected four hashes match.

Review 1's nine explicit input/planner hashes, five memberships and primary
source/control-flow assessment are retained, not rerun; its checker control
and failed grep record remain. No new original-input or web read was needed
for the correction review. No excluded report, fixture, capture, binary, live
state, product, harness or qualification check was opened or run. Missing saved
web-page copies remain a provenance limit.

Reports: `logs/planner-review/p9-sem-01/review1/` retains R1-R3 and original
review evidence; `review2/independent-correction-review.json` records this
correction review. The [validation policy](validation-policy.md) governs reuse.

## Publication boundary

`logs/planner-review/p9-sem-01/publication-manifest.json` freezes the reviewed
four paths, working SHA-256 values, Git blobs, modes and parent entries.
The executor verifies it before staging only those four paths, commits once
with sole parent `465e60cfc31d02961fc3970d44e65bb4cfe5ee29`, pushes `main`
normally to `origin/main` and verifies the live ref in the same chat/turn.
Preserve AGENTS/CLAUDE unstaged and byte-identical. A manifest discrepancy or
material post-GO change returns for review. A failed push or remote divergence
stops; no force-push, reset, cleanup or history reconciliation.

The execution checkpoint intentionally remains the pre-publication report;
do not edit it or this record again merely to announce the resulting commit.
This GO grants no native experiment, qualification, next slice or other work.

## Verified published baseline and continuity

P9-EV-01 is complete and independently verified at
`465e60cfc31d02961fc3970d44e65bb4cfe5ee29`, sole parent
`4c9102c523e2d8bd345f390332a6af470afb9cec`, subject
`docs: record retained P9 capture consistency`. Its four approved paths, modes,
blobs, parent entries and committed/working bytes matched the frozen manifest.
R1's byte-count wording and the architecture-index addition are accepted.
The committed checkpoint's pre-publication wording is historical; no closure
edit or repeat publication is needed.

Publication artifacts: `logs/planner-review/p9-ev-01/publication-manifest.json`
(SHA-256 `fe3752ae5e52d4ca131472902469ea81727065159081aac070c1422b626ed575`)
and `publication-verification.json` in that directory. Earlier verification
is retained; this review rechecked Git and the live ref, not prior captures.

Review and publication stay in their existing planner and executor chats.
After independent publication verification, Codex recommends one continuity
branch under the workflow. No next slice or chat transition is dispatched now.

## Preserved evidence and deviations

The [capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md)
found consistency in counts, ordered children, embedded segments, wrapper
records and completion ordering. The original malformed receipt remains a
named failure; the prior summary remains a derivative. It proves neither
capture completeness nor checked native child exits, denial or compatibility.

**Read-boundary deviation:** P9-EV-01 correction protection hashing read the
excluded retained `ctl`/`app` fixtures. Saved records contain 69 equal hashes:
**11 reader artifacts, 57 gate files and the planner**; 12/56 was incorrect.
The reviewer compared saved records without reopening fixtures. This is not
retrospective authorization, metadata proof or permission for another sweep.
Preserve the initial parser self-test failure, grep crash, transient manifest
placement and document-check failure (6964 bytes, then 6136). The selected
slice does not rerun those checks or broaden their historical claims.

## Standing decision constraints

- [ADR-0050](../decisions/0050-define-the-first-approved-github-branch-publication-execution-contract.md)
  remains Proposed; P9 remains unproven. No mechanism is selected.
- Static-audit acceptance covered reporting only. Frozen patches A/B/C, their
  chain and C's added assertions remain unapplied and unaccepted. The checked
  write-ahead suggestion and sticky-flag alternative remain unaccepted.
- The one-profile/two-child experimental authority is spent. No build, launch,
  profile query, reuse, creation or deletion, security operation, credential,
  helper or provider operation is authorized by this selection.
- Retained profiles, fixtures, protected DACLs, labels, grants and ignored
  evidence stay untouched; no cleanup, permission reset or fixture sweep.
  File hashes do not seal live ACL, label, grant or profile metadata.
- Agent qualification remains `Blocked`/`TargetsNotReal`, both allowances
  unused; no retry. Git/GitHub qualification is separate and unstarted;
  supported GCM versions and real compatibility remain unqualified.
- Owner GCM/ancestry-profile and residual name-to-ID risk acceptance was
  design-only; do not reopen it or infer runtime authority. Human rearm and
  resolution remain absent.
- Preserve the v1 defect/protocol deviation, inconclusive A2 result, native
  error and capture limitations, lost/pre-redaction output and live-state
  uncertainty in the [feasibility note](../architecture/github-publication-source-write-denial-feasibility.md).
  First-push claims needing source re-reading remain in the
  [execution evidence](../architecture/github-branch-publication-execution-evidence.md).
  Reported repository access is not branch-push permission.
- The concurrent-replay 409, target-test 500, disposal `NullReferenceException`
  and unnamed Api 2067/2068 remain separately unexplained; later passes prove
  neither flakiness nor irrelevance. No increment or MVP completion is inferred.

Execution-facing open risks remain in
[current-work.md](current-work.md#open-risks). WF-01 and earlier facts remain
at their published baselines and the frozen
[execution history](current-work-history-ba1d4c2.md) and
[planner history](planner-handoff-history-ba1d4c2.md); neither snapshot is edited.
