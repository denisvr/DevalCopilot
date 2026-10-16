# Planner/reviewer handoff

Planner-owned scope, rationale and review decisions; not a delivery ledger or
approval log. It replaces its current state on each selection or review
decision. Procedure: [development workflow](development-workflow.md). Git and
code prevail over this summary.

## Active slice: WF-COL-01 (GO for the reviewed documentation diff)

- **Decision (2026-10-16): GO.** Publish exactly `AGENTS.md`, `CLAUDE.md`,
  [current-work.md](current-work.md) and this planner record at the frozen
  manifest identities. No closure edit or additional substantive change.
  Codex remains planner/reviewer; Claude remains executor. WF-02/WF-03 and
  any P9 follow-up remain unselected.
- **Accepted result.** Both roles may initiate technical discussion or
  brainstorming, proactively raise material concerns and reconsider positions
  using evidence. The planner must address material executor challenges with
  a recorded decision and rationale. Neither role is infallible; brainstorming
  ideas remain proposals. Existing roles, disagreement handling, explicit
  acceptance and publication authority remain intact.
- **Root identities.** SHA-256: `AGENTS.md`
  `cff3b6632fc2124c77bd5ca3f639409d5fe67029f2b7821595b4a5eb07021c4f`;
  `CLAUDE.md`
  `d94da915f775011197904e19f0729d6437db9fcacc7b49d2d10a9fe53cd62453`.
  Codex made these edits in this chat at the owner's explicit request during
  P9-SEM-01. They are not executor-authored or accidental changes.
- **Boundary.** No automatic messaging feature, product orchestration or
  broader Agent permission is introduced. The root import, workflow routing
  and accepted
  [ADR-0021](../decisions/0021-add-bounded-root-instruction-context-to-agent-manifests.md)
  boundary are preserved: bounded advisory root text, no link-following in
  product capture, existing sealed manifests unchanged. New-text delivery
  through a product manifest was not exercised and is not claimed proven.
- **Minor notes accepted.** The named section creates a maintenance coupling
  in CLAUDE; neither root selects a communication mechanism. Executor finding
  dispositions remain governed by the shared workflow. No change is required.

## Independent review and publication boundary

Review preflight: `main`; HEAD, local `origin/main` and live main all
`b48738f0f160336df945ad3a6ad1802a6876458b`; 0/0; empty index; exactly the
four slice paths unstaged, no untracked paths. The returned planner matched
the dispatched 8013-byte `7f74fee3...a759` hash before this GO update.

Independent review inspected the complete root/checkpoint diff and executor
evidence. Root identities match; byte lengths 4513 and 703 are below 8192.
The first-line import is preserved. History and Open risks are byte-identical
from the History heading onward. The inspected unchanged checker passes on all
four paths (41 ms, zero findings); `git diff --check` passes. No blocking
finding. Evidence: `logs/planner-review/wf-col-01/review1/independent-review.json`.
Prior P9 source/publication assessment is retained, not rerun; no experimental
input, native/provider operation, product suite or manifest sealing was needed.
Validation remains proportional under the [validation policy](validation-policy.md).

`logs/planner-review/wf-col-01/publication-manifest.json` freezes all four
reviewed paths, modes, Git blobs, working-byte SHA-256 values and parent entries.
After verifying it, the executor stages only those four paths, commits once
with sole parent `b48738f0f160336df945ad3a6ad1802a6876458b`, pushes `main`
normally to `origin/main` and verifies the live ref in the same chat/turn.
Expected final checkout: clean, empty index, no untracked paths and 0/0.
A manifest discrepancy or material change returns for review. A failed push
or diverged remote stops; no force-push, reset, cleanup or reconciliation.
Do not edit the pre-publication checkpoint merely to announce the commit.
No next slice or additional runtime authority follows from this GO.

## Verified published baseline

P9-SEM-01 is complete at `b48738f0f160336df945ad3a6ad1802a6876458b`,
sole parent `465e60cfc31d02961fc3970d44e65bb4cfe5ee29`, subject
`docs: record P9 native I/O semantics`. Exactly four approved paths, all mode
100644, committed blobs and parent entries match the frozen manifest, and
committed/working bytes match its lengths and SHA-256 values. R1-R3 and the
index entry are accepted. The normal fast-forward push and live equality
are independently verified; no closure edit or repeat publication is needed.

Pre-selection Git: `main`; HEAD, local `origin/main` and live main all equal
that published SHA; 0/0; empty index; no untracked paths; only AGENTS/CLAUDE
unstaged at the hashes above. Selection adds only this planner modification.
The executor prompt carries its final byte length and hash.

Publication evidence: `logs/planner-review/p9-sem-01/publication-manifest.json`
(SHA-256 `27a8e04c8e8c6cf9d9ccce9d22e19924a7441a7e7a5509b313ded13f60e0a6f5`)
and `publication-verification.json`. Independent verification read only the
four document blobs/working files and excluded root instructions, not original
experimental inputs, fixtures or live security state.

Review and publication stay in the existing planner and WF-COL-01 executor
chats. No context usage percentage is asserted. Codex independently verifies
publication before the workflow's next continuity decision. No next slice or
chat transition is dispatched now.

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
