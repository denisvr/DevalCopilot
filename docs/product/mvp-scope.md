# MVP scope

## Objective

Deliver a visually complete, local application that removes manual message
transfer between Codex, Claude Code, Git, and GitHub for supervised software
changes across the user's registered projects.

The defining acceptance case is DevalCopilot coordinating a change to its own
repository and producing a green draft pull request or a clear escalation.

## Primary journey

1. The user registers a local repository and verifies required capabilities.
2. The user describes an objective and starts a supervised run.
3. DevalCopilot records the initial branch, commit, worktree state, tool
   versions, and applicable project instructions.
4. DevalCopilot creates an isolated branch and worktree.
5. Codex proposes a structured plan.
6. Claude Code evaluates the plan and accepts it or raises evidence-based
   challenges.
7. Codex resolves every material challenge or escalates it to the user.
8. Claude Code implements the resolved plan.
9. DevalCopilot runs configured local verification commands.
10. Codex reviews the complete diff and verification evidence.
11. Claude Code responds to findings and performs bounded corrections.
12. The user approves remote publication.
13. DevalCopilot pushes the branch and creates or updates a draft pull request.
14. DevalCopilot monitors checks for the exact head commit.
15. Relevant CI failures enter a bounded diagnose, correct, verify, and repush
    loop.
16. The run finishes with a green reviewable pull request or a precise human
    escalation.
17. Closing and reopening DevalCopilot preserves the complete run and reconciles
    unfinished external work.

## Required capabilities

### Project and environment

- Register and validate one or more local repositories.
- Detect Git, Codex, Claude Code, GitHub CLI, .NET, Node, and optional Docker.
- Read project-owned instructions without allowing them to expand authority.
- Define project-specific verification commands and approved filesystem roots.

### Workflow

- Run bounded workflows concurrently across distinct registered projects.
- Default to two active mutating runs globally, allow the user to reduce the
  limit to one, and queue additional work visibly.
- Permit at most one mutating run per canonical repository and one writer per
  worktree.
- Support start, pause, resume, stop, retry, and human instruction injection.
- Persist stages, tasks, attempts, handoffs, challenges, decisions, approvals,
  findings, checkpoints, and events.
- Bound debate, correction, duration, process, and publication attempts.
- Enforce provider-specific attempt, stage, and run token budgets.
- Observe provider account-usage windows when available and stop new attempts
  at user-configured Codex and Claude thresholds.

### Agent execution

- Invoke Codex and Claude Code through independent typed adapters.
- Persist provider session identifiers as attempt metadata and resume eligible
  sessions without making provider history authoritative.
- Discover supported model, effort, permission-mode, context, and compaction
  capabilities without assuming that both providers expose the same controls.
- Capture stdout, stderr, exit status, timing, and structured response data.
- Preserve raw output as an artifact.
- Reject invalid structured responses without inventing success.
- Create a new attempt for every retry.

### Git and review

- Validate a clean or explicitly accepted initial state.
- Create an isolated worktree and tool-owned branch.
- Record Git fingerprints before and after every mutating stage.
- Display changed files and a complete diff.
- Prevent concurrent writers for the same worktree.
- Create a commit only after the applicable review and approval gates.

### GitHub and CI

- Reuse an existing authenticated GitHub CLI session without storing its token.
- Push an approved tool-owned branch.
- Create or update a draft pull request.
- Monitor checks by pull request and exact head SHA.
- Capture failed jobs, steps, annotations, logs, and available artifacts.
- Feed bounded, sanitized evidence into the correction loop.
- Recover monitoring after restart.

### User experience

- Provide a project list and environment readiness view.
- Provide the approved run cockpit defined by
  [run-cockpit-specification.md](run-cockpit-specification.md), including
  collapsible navigation, Workflow, Agent Collaboration, Usage & Evidence, and
  live-output surfaces.
- Switch atomically among concurrently active project runs without combining
  their state.
- Show accumulated autonomous session time and make the active participant
  visually unmistakable.
- Allow model, effort, and permission-mode selection at safe attempt boundaries
  when supported by the provider.
- Show provider context-window usage and allow safe manual compaction when
  supported.
- Keep Codex and Claude run budgets and account-usage guardrails separate.
- Support Dark Navy and Light themes with accessible status and attention
  treatment.
- Stream durable events to the UI in near real time.
- Show agent dialogue as typed cards rather than an undifferentiated transcript.
- Show diffs, commands, tests, CI checks, findings, artifacts, and decisions.
- Make required human action visually unmistakable.
- Provide run history and replay from persisted events.

### Recovery and safety

- Detect interrupted agent and command attempts at startup.
- Reconcile Git state, child process state, pull request state, and CI state.
- Offer safe retry, resume, abandon, or manual takeover actions.
- Never infer that a timed-out or disconnected external action failed without
  reconciliation.

## MVP quality bar

- No silent state transitions.
- No unbounded agent or CI loops.
- No generic shell execution from the frontend.
- No credential persistence in application data.
- No mutation of the running DevalCopilot checkout.
- No concurrent mutating runs for the same canonical repository.
- No new provider attempt after its configured account-usage stop threshold is
  reached, unless a scoped human override is valid.
- No success claim without recorded evidence.
- No loss of completed run history after restart.
- No routine replay of complete transcripts, repository trees, diffs, or
  unchanged context when a smaller referenced selection is sufficient.
- Representative failure and recovery paths have automated tests.

## Explicitly deferred

- Cloud hosting, accounts, tenants, and team collaboration.
- Multiple simultaneous writers for the same repository or worktree.
- Automatic merge to the default branch.
- Automatic release, deployment, or in-place application update.
- Force-push, remote branch deletion, and branch protection changes.
- Generic plugin execution.
- Agents beyond Codex and Claude Code.
- Remote workers and mandatory container isolation.
- Visual dependency graphs as the primary run interface.
- Cost optimization beyond usage visibility, context economy, and configured
  run and provider-account guardrails.

## Exit demonstration

The MVP demonstration starts from a stable DevalCopilot build and requests a
small visible improvement to DevalCopilot itself. The resulting run must show a
real agent challenge or explicit acceptance rationale, implementation, local
verification, review, Git publication, CI observation, any necessary correction,
and a final green draft pull request. The user must not copy messages between
tools at any point.
