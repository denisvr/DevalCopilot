# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md), the
[roadmap](mvp-delivery-plan.md), and accepted [ADRs](../decisions/README.md).
Verify against Git and code; previous selections and reviews remain in Git.

## Current selection (2026-10-02): atomic project/run switching for cockpit and workspace evidence

### Current review decision: GO (2026-10-02)

- GO covers exactly the reviewed 21-file diff: 14 modified tracked files and seven
  new files, including current-work.md and this planner-owned record. Independently
  verified main, HEAD, local origin/main and live refs/heads/main at
  fbadc4e6b25e5015988b368f456b7a817ee06dd3, nothing staged. The substantive commit
  must have that parent. No next slice is selected; material changes require review.
- R1-R3 are corrected in the same frontend-only slice: draft versions preserve
  newer edits including a return to submitted values; the newest accepted execution
  read owns one bounded polling timer, starting for Running and ending for terminal,
  empty or failed results; catch-up/output reads refuse work after layout ownership
  cleanup, including before passive cleanup and after awaits. Exact server requests,
  accepted operations, same-owner catch-up/cursors and backend authority are retained.
- Independent final-tree checks: full frontend 1531/1531 in 113 files; npm run build
  including tsc -b; lint with ten existing warnings and zero errors; npm audit with
  zero vulnerabilities; harness 19/19; full Chromium 6/6 through the real authenticated
  host with reuseExistingServer: false; solution build
  --no-restore -p:UseSharedCompilation=false -m:1, zero errors/warnings. Normal build
  generation preserves client SHA-256
  1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb.
  No E2E owned root remains. All 21 changed/new files have no NUL/trailing whitespace;
  only this preserved planner record uses CRLF. The three changed documents' 106
  relative links resolve and git diff --check is clean apart from the CRLF notice.
- The corrected tests reproduce the prior review's draft, polling and commit-boundary
  scenarios; they cover obsolete Running versus newer terminal results, one polling
  chain, replacement/unmount, retained refresh/notification/reconnect in a consumer
  layout effect, and an old output timer fired before passive cleanup. Executor
  red/green reports are distinct from the reviewer's independently run green checks.
  No production/test code was edited by the reviewer.
- Evidence limits remain explicit: no actual provider or packaged Tauri run; Chromium
  uses Vite/Strict Mode; ownership is per hook/component instance; unrelated read
  hooks preserve their prior contracts. Project evidence panels keep independent
  checkpoint snapshots without a new synchronization/freshness bound. Backend test
  suites, dotnet format and NuGet scan were not repeated for the frontend-only change;
  their prior delivery evidence is retained, and formatter cleanliness is not claimed.
- Before GO the reviewer corrected only current-work.md facts: bare-hook/run-view
  tests omit App's selection keys, whereas CandidateWorkspacePanel tests exercise
  keyed children; same-owner error clearing retains its existing per-hook behavior;
  independently held checkpoint snapshots have no promised brief disagreement bound.
  These documentation edits are included in this GO.
- Publish only through the single English instruction in the planner chat: reviewed
  substantive commit including current-work.md and this GO, normal fast-forward push,
  live-remote and post-publication verification, then a current-work.md-only factual
  closure. No force-push, history reconciliation, unrelated edits or next-slice work.
  A failure or material change returns for review before publication continues.
### Verified publication and executor preflight

- The preceding implemented-plan slice is published: substantive
  c13b6f086be3b08db2bfa5382a7709269c64be91, parent
  bfb6392074901588639de50633cbb115e8ff77c5, contains the 13 reviewed files;
  closure fbadc4e6b25e5015988b368f456b7a817ee06dd3 has that substantive parent
  and changes only current-work.md. Git independently confirms these facts.
  Its reported post-publication tests were not repeated during selection;
  current-work.md distinguishes filtered post-publication and retained evidence.
- Independently verified branch main, HEAD, local origin/main and live
  refs/heads/main at fbadc4e6b25e5015988b368f456b7a817ee06dd3, with no staged,
  unstaged or untracked changes before this planner edit.
- Generated-client SHA-256 remains
  1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb.
- Exact expected executor state after this edit: same branch, HEAD and refs;
  nothing staged; only docs/roadmap/planner-handoff.md modified and unstaged;
  nothing untracked. Preserve this planner-owned record. Selection grants no
  implementation acceptance or commit/push GO.

### Objective, benefit and candidate judgment

- Select exactly one Increment 4 stabilization outcome: selecting another
  project/run immediately presents only that selection's cockpit and candidate
  workspace evidence, or its truthful loading/error state. An old response,
  notification, refresh, polling pass or local action continuation cannot restore
  the previous selection's data, identifiers, pending/error state or drafts.
  This prevents misleading plans, stages, paths, diffs and verification/review
  targets while the user operates the explicit Agent workflow.
- Concrete code evidence: App reuses RunCockpitView and CandidateWorkspacePanel
  across selections. useRunCockpit retains the previous cockpit until another
  response succeeds, and does not clear it for null. useProjectGitEvidence has
  no owner/request guard; a late previous-project response overwrites the new
  project's checkpoint. The workspace, verification-command/execution and
  checkpoint-review hooks have the same unowned continuation pattern. Existing
  per-control claim guards do not make the header, stage rail or source evidence
  belong to the current selection.
- Independent temporary diagnostics against the published tree failed all three
  cases: loaded A then pending B still returned A's cockpit; A then null still
  returned A; Git evidence resolved B then late A and ended at A's checkpoint.
  The diagnostic file was removed without leaving implementation changes.
  Existing useRunCockpit, CandidateWorkspacePanel and WorkspaceEvidencePanel
  tests passed 19/19. These are planning reproductions, not executor validation.
- Additional sampling has lower value than this demonstrated selection breach;
  bounded tracked/untracked context and sealed artifact-window inspection already
  exist. Model/effort controls also already exist. Account threshold enforcement
  still lacks a proved account/bucket-to-invocation binding, and Claude allowance
  has no established safe machine-readable contract. Observation is not admission.
  Resume/compaction would require a separately selected persistent-session execution
  contract: current adapters are one-shot; Codex is ephemeral and Claude disables
  persistence. The current official [Codex App Server contract](https://learn.chatgpt.com/docs/app-server)
  describes thread-based compaction and multi-bucket allowance reads; the
  [Claude CLI contract](https://code.claude.com/docs/en/cli-reference)
  explicitly says disabled persistence prevents resume. Neither establishes safe
  resume or account-stop enforcement for the application's existing invocations.
- This applies existing ADR-0002/0003/0005/0006/0008 and ADR-0014 boundaries;
  ADR-0016/0017 planning/review identities remain unchanged. React owns
  presentation, the host owns mutation and eligibility, and SignalR is only a
  notification source. No new or superseding ADR is anticipated. The cockpit
  specification already requires atomic selection and no mixed-run display.

### Precise scope and exclusions

- Production scope: useRunCockpit; useProjectWorkspace; useProjectGitEvidence;
  useProjectVerificationCommands; useProjectVerificationExecutions;
  useProjectCheckpointReviews; their App/RunCockpitView/CandidateWorkspacePanel,
  WorkspaceEvidencePanel, VerificationCommandsPanel and CheckpointReviewPanel
  composition/local state where needed. Small feature-owned ownership utilities
  and existing verification-output viewer boundaries may change only as needed
  to keep this workspace/evidence journey coherent. Add focused tests, one real
  browser selection regression, cockpit-spec clarification and current-work.md.
- Run snapshots, event cards, connection/loading/errors and refresh waiters belong
  to the current committed selection lifetime, including A-to-B-to-A and unmount.
  A response naming another run must not become the selected cockpit. Prevent old
  event sequences/process ids from feeding downstream hooks or output selection.
- Project metadata, command/execution/review lists, checkpoint files/diff and errors
  belong to the current project lifetime. Inspection also belongs to the exact
  checkpoint; a newer checkpoint must not retain or accept an old checkpoint's
  files/diff. Disabled/null selection cannot expose or inspect stale evidence.
  Order overlapping reads so an older response cannot overwrite a newer result.
- Existing preparation, identity recheck, checkpoint capture, command configuration/
  update/removal, verification execution and checkpoint-review submission keep
  their exact server operations. Guard their UI continuations and reset local
  drafts/selections/output on owner replacement. Discarded handlers must not start
  work for a replacement owner; accepted server operations remain real and are
  neither undone nor retried. No stale completion clears a newer draft/pending
  operation or triggers a follow-up refresh for the replacement selection.
- Preserve same-owner catch-up coalescing, stable event cursors/deduplication,
  post-connect/reconnect catch-up, truthful refresh results, useful same-owner
  error recovery, bounded execution polling, on-demand diff/output and per-instance
  ownership. Keyed subtrees may help but are not a substitute for guarded in-flight
  continuations. Do not modify every read hook merely for stylistic consistency.
- Exclude backend production/tests, HTTP contracts, generated-client edits, schema/
  migrations, dependencies, provider invocation/configuration/session behavior,
  account allowance/thresholds, context sampling/compaction, workflow/authorization/
  budget changes, historical evidence rewriting, scheduler/coordinator, lifecycle
  completion/replacement and general UI redesign. Existing Agent-action ownership,
  authorization/status/timeline and artifact inspectors retain their contracts;
  only integration fixes necessary to the scoped selection boundary are allowed.

### Stop gates and acceptance evidence

- Stop and report a preflight mismatch, required authority/contract/ADR reversal,
  backend/API/schema/provider change, dependency or generic query-framework need,
  or a fix that requires broadening outside the named journey. Do not weaken real
  authentication, host composition, test assertions or isolation to run a browser.
- Write discriminating regressions first: reproduce the three planner failures;
  observe committed/rendered frames, not only eventual state. Cover A-to-B-to-A,
  null/disabled state, unmount/remount, stale success/failure/notification/refresh,
  older and newer overlapping reads, and checkpoint C1-to-C2 during inspection.
  Prove current unchanged-owner success and recoverable failure still work.
- Cover each of the five project hooks and their visible consumers. With materially
  different project/run/checkpoint/command/execution ids, prove no mixed evidence or
  stale action target and no obsolete continuation resets a newer form or operation.
  Retain accepted-server-operation semantics; verify request ids and literal
  verification arguments. Detailed permutations belong in focused frontend tests.
- Add a Chromium regression using two owned projects/manual runs with distinct
  objectives and explicit selection. Delay actual authenticated cockpit responses;
  while B loads or fails, A's header/stage/evidence/actions must not appear as B.
  Exercise return selection and late completion. Keep the existing real host,
  generated HTTP client, normal auth, reuseExistingServer: false, order-independent
  fixtures and owned-root cleanup. No real provider invocation is needed.
- Run affected tests first, then full frontend vitest, tsc -b, production build,
  lint, npm audit, harness and full Chromium suite. Run solution build with normal
  client generation and compare the exact client hash; backend test suites need
  not repeat for a strictly frontend change. Distinguish all retained evidence,
  skips, baseline warnings and environment blockers from checks actually run.
- Check the complete tracked/untracked diff, whitespace, NUL bytes and Markdown
  links. Document only the observed guarantees and limits. Return a complete
  unstaged, uncommitted, unpushed diff with a commit-ready current-work.md entry,
  actual commands/results, inventory and remaining risks for Codex GO/NO-GO.
  Keep implementation and corrections in one new Claude executor chat. No next
  slice or publication is authorized here.
