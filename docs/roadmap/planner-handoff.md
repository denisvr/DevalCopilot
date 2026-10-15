# Planner/reviewer handoff

Planner-owned scope, rationale and review decisions; not a delivery ledger or
approval log. It replaces its current state on each selection or review
decision. Procedure: [development workflow](development-workflow.md). Git and
code prevail over this summary.

## Active slice: WF-01 (GO for the reviewed diff)

- **Selection.** The owner approved WF-01, a human documentation-workflow
  migration: bounded, explicit development continuity with exact preservation
  of the prior checkpoint history. Exactly seven paths: `AGENTS.md`, the new
  development workflow, both checkpoints, both new history snapshots and the
  slice-start template.
- **Rationale (as dispatched).** Make human-operated continuity bounded and
  explicit while preserving historical evidence, role ownership and
  publication controls: the baseline checkpoints held 1,276,430 and 499,010
  bytes of accumulated entries, and next work now follows an explicit
  `CONTINUE_THIS_CHAT`/`START_NEW_CHAT` decision instead of unconditional
  dispatch.
- **Review: GO (2026-10-15).** Codex accepts the seven-path documentation
  diff, including this planner-owned review update, for one substantive
  commit and normal fast-forward publication. Next planner action:
  independently verify publication, then recommend chat continuity under
  the workflow. No subsequent slice is selected.
- **Exclusions.** No runtime code, tests, provider adapters, capture limits,
  ADRs, EngineeringStandards, dependencies, credentials, experiments, retained
  artifacts or cleanup behavior. WF-02 and WF-03 are not selected, and no
  later slice has speculative authority.
- **Design constraints.** ADR-0021 root-file capture is unchanged; linking
  the workflow from `AGENTS.md` does not import it at runtime. The snapshots
  are evidence, not current instructions or renewed approvals.

## Review evidence and publication boundary

Codex independently verified `main`, HEAD, local `origin/main` and live
`refs/heads/main` at `ba1d4c2d9e9c5df285777267a7402e75f28e4f87`, an empty
index and exactly the seven expected modified/new paths. Both snapshots are
byte-identical to their required Git blobs, including raw and path-filtered
hash equality. All authored files meet their UTF-8 size/encoding bounds;
the generated client remains `8d72cb7c...d104`.

Independent checks: 163 local links and 35 fragments resolve, plus 11 tracked
incoming Markdown links outside the authored files; diff/whitespace checks
pass, and the authored-text sensitive-shape scan has no hits. The reviewer's
first fragment checker rejected undefined bracket text as a reference; the
checker was corrected to treat it as literal Markdown, with no source defect
inferred. Rule relocation, active constraints and continuity branches were
reviewed against the dispatch and baseline. No product build, suite, native
operation, qualification or experimental rerun was performed.

GO applies only to the frozen seven-file content identified in the review
response. Publish it without further edits, validation reruns or a closure
diff; a material discrepancy returns for review. Keep all standing decision
constraints below. The executor reports the published SHA and live-ref
equality in the same turn; it selects no next slice.

## Prior decision state (baseline `ba1d4c2`)

The last recorded decision was GO to publish the static AppContainer audit
report only. Git shows `ba1d4c2d9e9c5df285777267a7402e75f28e4f87` (parent
`69b2adb9b37675ef0d57b3ebe2e10ae9c3dde757`) published it on `origin/main`.
Detail: [planner history snapshot](planner-handoff-history-ba1d4c2.md), exact
blob `dd3bb1d70459322db9147c99c8fe53a01829e38a` of this file at that commit.
References elsewhere to named sections of this file dated at or before that
commit resolve there.

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
