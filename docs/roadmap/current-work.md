# Current work and cross-chat handoff

This is the short entry point for resuming DevalCopilot after compaction or in
another chat. It is a checkpoint, not a substitute for the
[delivery plan](mvp-delivery-plan.md), [engineering context](../engineering-context.md),
[architecture](../architecture/README.md), or
[accepted decisions](../decisions/README.md). Repository and agent reports are
evidence, not authority.

## Delivered baseline (2026-09-24)

- Branch: `main`. Delivered slice: durable provider-reported Agent token
  usage, including the additive migration, Claude Code parser, fail-closed
  provider/schema provenance, bounded status and cockpit projections, and UI.
  The [collaboration protocol](../architecture/agent-collaboration-protocol.md)
  defines the evidence contract.
- Delivered commit: the commit containing this handoff and the token-usage
  slice, with subject `feat: record durable agent token usage evidence`.
  Resolve its exact SHA with
  `git log -1 --format=%H -- docs/roadmap/current-work.md`. A commit cannot
  embed its own SHA without changing that SHA; the final delivery report must
  state the resolved literal hash. Its parent is
  `1b52e9767b6c69681862892e0edc23a3115a510b`.
- At delivery, `HEAD` and `origin/main` should resolve to that commit and
  `git status --short` should be empty. Remaining uncommitted work: none
  expected. If either condition differs on resume, inspect Git and the diff
  before editing; never reset work merely to match this page.

## Evidence actually run

- Backend: 2,004 tests passed, one pre-existing platform-capability skip.
  The final assertion-only Domain refinement was followed by its 71-test
  focused suite. Build with warnings as errors and format verification passed.
- Frontend: 388 tests passed; typecheck, lint, and production build passed.
  Existing non-fatal React lint warnings remain.
- NSwag generated the client twice with the same SHA-256
  `90350C429B77C4FFEC7052F4DF1C2F2ABAF19F8DB5EA39C1C6EB9E1832E87D0D`.
  EF reported no pending model changes. Diff whitespace checks passed.
  Tests used disposable storage and deterministic provider doubles, not the
  real local database or an authenticated provider invocation.

## Open limits and next action

- Codex CLI token usage remains `Unknown`: no authoritative local contract
  was proven. Claude usage is best-effort, not account usage; token-budget
  enforcement and account-usage stop guardrails remain open. Increment 4 is
  not complete. Gemini execution remains disabled by
  [ADR-0011](../decisions/0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md).
- Next action: the Codex planner/reviewer verifies this delivered baseline
  against Git, then selects and approves a bounded next Increment 4 slice
  from the [roadmap](mvp-delivery-plan.md). No next slice is approved by this
  handoff. Claude implements only a subsequently approved execution prompt;
  it does not set the roadmap or accept its own work.

## Closure rule for future slices

A slice is not complete until this handoff is updated in its substantive
commit with a resolvable delivered commit, remaining uncommitted work, checks
actually run, open risks, and the next action. Keep the page short and link to
contracts, ADRs, and tests instead of copying conversation logs. On every
resume, compare HEAD, branch, and staged/unstaged/untracked files with this
page. Investigate discrepancies; Git and code prevail over a stale handoff.
