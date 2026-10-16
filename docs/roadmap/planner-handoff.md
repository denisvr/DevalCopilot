# Planner/reviewer handoff

Planner-owned scope, rationale and review decisions; not a delivery ledger or
approval log. It replaces its current state on each selection or review
decision. Procedure: [development workflow](development-workflow.md). Git and
code prevail over this summary.

## Active slice: P9-EV-01 (GO for the reviewed documentation diff)

- **Decision (2026-10-16): GO.** Four paths:
  the [cross-check note](../architecture/github-publication-retained-capture-crosscheck.md),
  [architecture index](../architecture/README.md), `current-work.md` and this
  review record. One substantive commit and normal fast-forward publication;
  no closure edit or next slice.
  WF-02 and WF-03 remain unselected.
- **R1: resolved.** The note now states 112,425 bytes = 112,138 bytes of
  LF-terminated host lines + 287 bytes from replacing LF with CRLF. Independent
  byte comparisons already support it. The reader computed correctly; this
  was wording precision.
- **Index: accepted.** One discoverable evidence link explicitly disclaims
  an accepted contract or P9 proof. The scope amendment authorized it.
- **Rationale.** The [roadmap](mvp-delivery-plan.md) leaves Increments 4 to 7
  incomplete. The captures narrow audit U4/U10, not native-error gaps or P9.
  No mechanism, patch chain, write-ahead design or new experiment is accepted.

## Independent review evidence and deviations

Review preflight: `main`; HEAD, local `origin/main` and live main all
`4c9102c523e2d8bd345f390332a6af470afb9cec`, 0/0; empty index; exactly the
three expected tracked modifications and the new note untracked. The returned
planner matched the dispatched 6037-byte hash until this review update.

Retained independent review: four full manifest digests, 22 inputs and manifest
memberships, 10 output entries and capture comparisons pass; 287 host records,
311 transcript lines and the named receipt failure agree. Eight-control report
inspected, not rerun. `logs/planner-review/p9-ev-01/review1/independent-review.json`.

Correction review: the note differs only by R1's formula; the index has one
entry. The original output manifest and all 10 entries still match; all eight
correction-manifest entries match. The inspected document checker passes:
24 links, UTF-8/LF, final newline, whitespace and sensitive shapes; current-work
is 6136 bytes. The incoming open-risks fragment exists; diff checks pass.
No reader, product suite, build, generation, qualification or native rerun.

**Read-boundary deviation:** correction hashing read the retained `ctl`/`app`
fixture files outside the authorized scope. This is not retrospective
permission. Saved lists contain 69 equal hashes: **11** reader artifacts,
**57** gate files and the planner (the reported 12/56 breakdown is incorrect).
The reviewer compared saved records without reopening fixtures. Hashes do not
prove metadata/ACL/profile state. Preserve the deviation; no further sweep,
native investigation, cleanup or permission reset. Capture comparisons remain
valid at their original boundary; no new work is authorized.

Keep the initial parser self-test failure, grep crash, transient manifest
placement and document-check failure (6964 bytes, then 6136) retained. Existing
artifacts stay frozen. Lost output, native exit measurement, buffer truncation,
live state and pre-redaction text remain unknown. The note describes the
original capture pass; correction reads do not enlarge its claims.

## Publication and continuity boundary

The frozen manifest identifies the reviewed bytes. Publish unchanged;
discrepancies return for review. The executor commits,
pushes and verifies the live ref in the same chat/turn. A failed push or remote
divergence stops. Codex independently verifies publication, then recommends
one continuity branch. No next slice is dispatched now.

WF-01 is published and independently verified at the baseline above, sole
parent `ba1d4c2d9e9c5df285777267a7402e75f28e4f87`. No repeat publication.
The [execution history](current-work-history-ba1d4c2.md) and
[planner history](planner-handoff-history-ba1d4c2.md) retain exact blobs
`ebb007040fa73ab0252511180e53e2e64f5285df` and
`dd3bb1d70459322db9147c99c8fe53a01829e38a`.

## Standing decision constraints

- [ADR-0050](../decisions/0050-define-the-first-approved-github-branch-publication-execution-contract.md)
  remains Proposed; P9 remains unproven.
- Static audit acceptance covered reporting only. Frozen patches A, B and C
  are unapplied proposals; their chain and patch C's added gate assertions are
  unaccepted.
- The checked write-ahead suggestion is a deferred, unaccepted design idea;
  neither it nor the sticky-flag alternative is selected.
- The one-profile/two-child experimental authority is spent. No renewed
  build, launch, profile query, reuse, creation or deletion, security
  operation, or credential, helper or provider operation is authorized.
- Retained profiles, fixtures, protected DACLs, labels, grants and ignored
  evidence stay untouched; no cleanup or permission reset.
- Agent qualification remains `Blocked`/`TargetsNotReal` with both allowances
  unused; no retry is selected. Git/GitHub qualification is separate and
  unstarted; real compatibility and supported GCM versions are unqualified.
- The owner's acceptance of the restricted GCM/ancestry profile and of the
  residual name-to-ID risk was design-only. Do not reopen those answers or
  treat them as runtime authority. Human rearm/resolution remains absent.
- Further P9 unknowns are listed in the
  [feasibility note](../architecture/github-publication-source-write-denial-feasibility.md);
  first-push evidence limits, including summarized statements that need a
  source re-read before becoming contract, are in the
  [execution evidence](../architecture/github-branch-publication-execution-evidence.md).
  A reported repository permission is not push permission.
- No Increment 4, 5, 6, 7 or MVP completion is inferred from earlier reports
  or from this migration.

Execution-facing open risks and evidence limits are in
[current-work.md](current-work.md#open-risks).
