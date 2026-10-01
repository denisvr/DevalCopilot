# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md), the
[roadmap](mvp-delivery-plan.md), and accepted [ADRs](../decisions/README.md).
Verify against Git and code; previous selections and reviews remain in Git.

## Current selection (2026-10-01): implemented-plan identity through review and correction

### Current review decision: GO (2026-10-01)

- GO covers exactly the reviewed 13-file diff: 11 modified tracked files and two
  untracked files, including this review record, current-work.md and ADR-0017.
  Independently verified main, HEAD, local origin/main and live refs/heads/main at
  bfb6392074901588639de50633cbb115e8ff77c5, nothing staged. The substantive commit
  must have that parent. No next slice is selected; material changes require re-review.
- The one production change makes ImplementedPlan required, derives the Planner root
  from validated inputs/lineage, requires the initial report's reply to equal its
  first input, and carries the implemented plan through correction links. Existing
  review builders and claim handlers remain unchanged; correction inputs/replies,
  budgets, authorization and historical sealed replay preserve their contracts.
- Independent checks on the reviewed production/test tree: solution build with
  --no-restore -p:UseSharedCompilation=false -m:1, zero errors and warnings; full
  Application 2914/2914; full Api 718/718; full Architecture 9/9, no skips.
  Client SHA-256 remains 1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb.
  All 13 changed/new files are free of NUL/trailing whitespace; 144 relative Markdown
  links resolve and git diff --check is clean. No production or test code was edited
  by the reviewer.
- Executor evidence additionally covers full Domain 903, Infrastructure 943 plus
  three existing skips, frontend 1470, typecheck/build/lint, harness 19, audits and
  formatter comparison (153 findings/17 files, recorded baseline). These checks were
  not independently repeated here. No new Chromium run or real-provider evidence
  is claimed. Historical root-target reviews/approvals remain historical.
- Accepted evidence limits: tests were written after the fix and demonstrated red
  afterward against the parent's production file; the reported restored mutations
  are distinct from the reviewer green runs. No byte-for-byte golden was added for
  accepted-root/authorized-final reviews. Unchanged builders, unchanged plan values
  for those forms, and reviewed id/content/structure tests support byte preservation;
  do not claim a golden test exists.
- The reviewer corrected documentation facts before GO: four modified test files,
  three ordinary forms in the new Application matrix, root replies invalid only
  when the initial input was a revision, review versus correction terminology, and
  the actual timing of red evidence. These changes are included in this GO.
- Publish through the single English instruction in the planner chat: reviewed
  substantive commit, normal fast-forward push, live-remote/post-publication
  verification, then current-work.md-only factual closure. No force-push, history
  reconciliation, unrelated edits or next-slice work. Preserve this GO record.

### Verified baseline and publication

- Independently verified main, HEAD, local origin/main, and live refs/heads/main at
  bfb6392074901588639de50633cbb115e8ff77c5, with nothing staged, unstaged, or untracked
  before this planner edit. The preceding substantive commit is
  47e141cd50ba3d05b26bf76476f576c83650f57d, parent
  3fe5f08cb648f3726e385d14be554ae4e1a4fda0: 34 modified and 49 added files,
  including the planner GO. The closure has that substantive parent and changes
  only current-work.md. Its reported post-publication tests were not repeated during
  selection; the published delivery and remaining limitations are recorded there.
- Generated-client SHA-256 independently remains
  1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb.
  Exact executor preflight after this edit: same branch, HEAD and refs; nothing
  staged; only docs/roadmap/planner-handoff.md modified and unstaged; nothing
  untracked. The executor preserves this planner-owned selection.

### Objective and candidate judgment

- Select exactly one Increment 4 outcome: every newly claimed CodeReviewer attempt
  judges the exact Proposal the initial implementation consumed, throughout an
  ordinary first-revision implementation, manual review format repair, ordinary
  correction, and explicit re-review. Keep the Planner root as historical lineage
  and correction reply identity; never substitute its superseded scope for the
  plan that the implementation actually followed.
- Code evidence: ImplementerExecutionReportEligibility.Result.ImplementedPlan falls
  back to OriginalProposal for ordinary forms. The initial ordinary Resolver branch
  resolves the root without supplying the implemented-plan override;
  CreateCodeReviewAttempt uses that property for both review-manifest forms. The
  implementation's first input and production ExecutionReport reply name the revised
  Proposal instead. ADR-0016 separates the identities for authorized depth-two plans
  but expressly preserves the ordinary forms' former review target. The ordinary
  branch also lacks the authorized branch's exact report-reply/first-input comparison;
  derive both identities from the validated input chain, never an unrelated report
  reply. This is code inspection, not an independent red-test run during planning.
- Benefit: review evaluates the accepted challenge resolution rather than findings
  against an abandoned plan. Close target selection, repair, correction carry-through
  and replay compatibility in one executor slice. No new screen or operation is
  needed: existing explicit cockpit actions reach this behavior.
- Alternatives compared: more context sampling has lower value after tracked and
  untracked previews. Manual-run completion/replacement, coordination, stop, takeover,
  commit and publication require Increment 5/6 authority. The escalation's old wording
  is a separate copy issue, not bundled here. Provider controls remain unselected:
  the repository's Codex observer requests quota snapshots without account/bucket
  binding to a particular invocation. The current official
  [app-server contract](https://learn.chatgpt.com/docs/app-server) documents quota
  windows and metered bucket identities; observation is not an invocation reservation
  or evidence of this implementation enforcing a stop threshold. The repository's
  Claude adapters pass --no-session-persistence; the current official
  [CLI contract](https://code.claude.com/docs/en/cli-reference) says those sessions
  cannot be resumed. Claude account allowance and provider-session resume remain
  unproven. The selected slice needs no new external capability or provider assumption.

### Selected contract and boundaries

- Add ADR-0017 to supersede only ADR-0016's preservation of the ordinary first-revision
  code-review target and future review-manifest bytes. Explain the forward-only change;
  leave accepted decision text intact and link the narrow supersession through the ADR
  index and engineering context. Preserve ADR-0004's protocol, ADR-0010's correction
  inputs/replies, and ADR-0016's entire grant, consumption, depth and dispatch contract.
- OriginalProposal is the actual Planner root. ImplementedPlan is the initial
  implementation's exact sequence-zero Proposal. For an initial report, require its
  reply to equal that first input in every supported form. Derive a Resolver plan's
  root through the existing validated PlanningLineage, not arbitrary parent text or
  the latest plan. Preserve complete ordered inputs, provenance/assignment, outcomes,
  chronology, workspace and starting/result checkpoint validation. No malformed-chain
  fallback to the root, synthesized historical data or new column.
- Supported matrix: accepted root (both identities equal), first Resolver revision
  with complete Decisions, that revision with an exact optional second-review
  Acceptance, and the already-correct human-authorized final revision. A challenged
  first revision and an unauthorized depth-two plan remain ineligible.
- Carry the initial ImplementedPlan unchanged through every valid correction link.
  Correction reports still reply to OriginalProposal; RevisionResponses reply to
  findings. Exact correction inputs remain previous report plus all findings in
  timeline order. Do not add a plan input, add plan text to the correction manifest,
  retarget persisted messages or consume another planning grant. Existing correction
  budgets and human guidance remain distinct and unchanged.
- Initial code review, its newly requested manual format repair, correction re-review,
  and a repair of that re-review receive the implemented Proposal's id, summary and
  structured content as resolvedPlan. Reuse the report-chain and review-manifest
  builders, including correction evidence. Keep the 32-KiB bound, evidence fitting,
  fixed instruction, evidence boundary, output schema and exact report/verification
  selection. Never truncate the plan to make it fit.
- Forward-only compatibility: sealed manifests, hashes, artifacts, messages, outcomes
  and approvals remain immutable. Already-claimed undispatched reviews replay sealed
  bytes, including older root-target manifests; do not rebuild or reject them merely
  because the new-claim rule changed. A newly requested eligible format repair,
  including one of an older failed source, uses the corrected target with the same
  recorded report and verification inputs. Preserve source eligibility, one-repair
  authority, duplicate suppression and all ordinary gates. No retroactive approval
  revocation or entitlement to another review is introduced.
- Production changes belong to the existing Application policy/claim path. New shared
  types, if necessary, belong in explicit Policies/Contracts ownership, not an
  operation-bearing feature root. Do not reorganize unrelated legacy types. No public
  DTO, generated-client, frontend, schema, dependency, provider CLI, tool, permission,
  adapter version or output-contract change is selected.

### Stop gates and acceptance evidence

- Stop on branch/HEAD/remote or working-tree drift; inability to derive the exact plan
  from coherent persisted inputs; a need to rewrite history or sealed artifacts,
  revoke approvals, change correction inputs/replies, migrate storage, relax budgets
  or depth caps, change provider contracts, or broaden authority. Malformed evidence
  stays refused without echo. No real provider, auth bypass, shared-database reset,
  production test hook or unsafe fixture cleanup.
- Red before green: substantively different root and revision summaries/steps expose
  the wrong resolvedPlan, not merely an id mismatch. Cover all four plan forms and
  carry-through over more than one valid correction link. Same-run unrelated
  root/revision replies, root instead of revised-plan initial reply, missing/foreign
  first input and broken correction-root linkage fail closed. Reuse existing
  corruption tests for ordering, provenance and checkpoint invariants.
- Real file-backed SQLite command/repair tests prove exact manifest id/content,
  correction input/reply preservation, and malformed-chain refusals without new
  attempts/reservations/artifacts. Test fresh reads with a populated context and the
  existing repair commit seam: competing authority edit before BEGIN, refusal and
  orphan cleanup. Do not rely only on detached fixtures.
- Hosted deterministic flow: production-written first challenge resolution with
  substantively different plans, explicit implementation, verification, invalid-format
  initial review, its manual repair to changes requested, ordinary correction and
  explicit re-review. Cover the optionally Accepted first revision too, and a format
  repair of a correction re-review. Capture the sealed context actually read by the
  review adapter, not only builder output. Use real supervisors, mediator and SQLite,
  with doubles only at external process/provider boundaries. Keep the authorized
  depth-two flow green. Claim/restart coverage proves an older root-target sealed
  review replays byte-identically. No actual provider runs.
- Restored mutations detect root substitution, loss of ImplementedPlan at a correction
  link, and removal of initial reply/input equality. Pin unchanged accepted-root and
  authorized-final review-manifest bytes with fixed inputs; changed ordinary manifests
  differ only in selected plan values.
- Run focused regressions, then solution build and full Domain, Application,
  Infrastructure, Api and Architecture suites sequentially. Run frontend tests,
  typecheck and production build for regression/generated-client stability, plus
  required formatter/baseline comparison, applicable audits and lint. Report failures,
  skips and baseline warnings honestly. No new browser specification is needed without
  changed browser behavior. Check client hash, documentation links, complete tracked/
  untracked whitespace and NUL scans, and git diff --check.
- Update protocol, workflow description, ADR links and a commit-ready current-work
  entry with the forward-only contract and historical-review limitation. Record actual
  commands/outcomes and fresh versus retained evidence. Do not claim real-provider
  reliability, publication or Increment 4 completion.

### Executor handoff and review state

Use one new Claude executor chat for this outcome and its corrections. The complete
English prompt is supplied in the planner chat, not duplicated here. Return the
complete unstaged, uncommitted, unpushed diff, including current-work.md, for Codex
GO/NO-GO. Selection alone granted no commit/push GO; the current explicit review
decision above now authorizes only the reviewed diff and no next-slice work.

The single publication instruction in the planner chat covers the reviewed
substantive commit with current-work.md, normal fast-forward push, live-remote and
post-publication verification, and a tightly bounded current-work.md-only factual
closure. Stop on push failure/divergence; no force-push or history reconciliation.
A material change after GO returns for review before publication.
