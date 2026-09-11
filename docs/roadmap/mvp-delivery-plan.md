# MVP delivery plan

## Delivery strategy

Build the MVP as a sequence of visible vertical slices. Every increment must
connect UI, API, application behavior, persistence, and tests far enough to
demonstrate a real product capability. Do not build the complete backend before
the cockpit becomes usable.

The order below follows risk: prove durable authority and process boundaries
before adding autonomous remote mutation.

## Increment 0: Engineering foundation

### Outcome

The repository has discoverable engineering instructions, accepted decisions,
an executable MVP definition, and a threat model.

### Deliverables

- engineering context and agent adapters;
- product vision and MVP scope;
- architecture and data model documentation;
- accepted ADRs;
- security threat model;
- delivery plan.

### Exit criteria

- Codex and Claude Code discover the same canonical engineering contract.
- Every material stack and authority decision has one owner and record.
- The self-hosting demonstration is defined before implementation begins.

## Increment 1: Visible durable walking skeleton

### Outcome

The desktop app creates and displays a persisted simulated run.

### Deliverables

- .NET solution and four backend projects;
- React/Tauri frontend;
- local authenticated MVC boundary and generated client;
- SQLite migrations, event journal, and current run projection;
- SignalR post-commit notification and cursor catch-up;
- project list and first run cockpit;
- deterministic simulated-agent adapter.

### Exit criteria

- starting a simulated run produces visible sequenced events;
- closing and reopening the application preserves the run;
- disconnecting and reconnecting the UI catches up without duplicates;
- the frontend cannot invoke shell commands directly;
- backend, API integration, frontend, and smoke tests run locally.

## Increment 2: Process supervision and environment readiness

### Outcome

DevalCopilot safely runs bounded local commands and exposes their progress.

### Deliverables

- tool discovery and version snapshots;
- typed process request and result contracts;
- stdout/stderr streaming with output limits;
- cancellation, timeout, and process-tree handling;
- artifact storage for raw output;
- environment readiness UI;
- executable test-double harness.

### Exit criteria

- a deterministic child process can succeed, fail, time out, and be cancelled;
- every retry produces a separate immutable attempt;
- restart reconciliation identifies interrupted attempts;
- malicious arguments cannot create a shell command;
- large output follows defined backpressure and artifact policy.

## Increment 3: Git workspace and review evidence

### Outcome

A run owns an isolated worktree and can present trustworthy source evidence.

### Deliverables

- repository registration and canonical root policy;
- baseline validation;
- tool-owned branch and worktree lifecycle;
- writer lease and ownership marker;
- Git fingerprints and checkpoints;
- changed-file and complete-diff queries;
- configured local verification commands;
- diff, command, and test views in the cockpit.

### Exit criteria

- work occurs outside the user's active checkout;
- review and verification bind to one Git fingerprint;
- source changes invalidate stale approvals;
- dirty and externally changed states block safely;
- the application recovers an existing owned worktree after restart.

## Increment 4: Real Codex and Claude Code collaboration

### Outcome

Codex and Claude Code exchange structured, bounded, visible messages without
manual transfer.

### Deliverables

- Codex adapter and provider health checks;
- Claude Code adapter and provider health checks;
- protocol validation and raw transcript artifacts;
- planning, critical review, challenge, decision, implementation, review, and
  revision stages;
- challenge and finding UI cards;
- human instruction and escalation controls;
- loop, duration, and usage budgets.

### Exit criteria

- Claude Code returns an acceptance rationale or a material challenge;
- Codex resolves every challenge explicitly;
- implementation is limited to the resolved plan and worktree;
- findings map to revision responses and source changes;
- invalid protocol output fails closed;
- loop exhaustion creates a useful human escalation.

## Increment 5: Local supervised delivery loop

### Outcome

One objective can reach a locally verified, reviewed commit.

### Deliverables

- end-to-end stage coordinator;
- pause, resume, stop, retry, and takeover;
- scoped approvals and invalidation;
- commit preparation and execution by the orchestrator;
- final evidence summary and run replay;
- crash-recovery scenarios across agent, command, and Git stages.

### Exit criteria

- neither agent commits directly;
- a commit requires the configured review and approval state;
- interrupted runs reconcile rather than guess;
- a complete local run requires no manual message transfer;
- the UI explains every transition and required human action.

## Increment 6: GitHub and CI evidence loop

### Outcome

An approved local change becomes a draft pull request whose CI failures can be
corrected through the same bounded workflow.

### Deliverables

- GitHub CLI capability and authentication health;
- push and draft pull request adapters;
- exact-SHA check and workflow monitoring;
- failed-step log and artifact capture;
- CI failure classification;
- bounded CI diagnose and correction loop;
- GitHub/CI cockpit panel;
- remote reconciliation after timeout and restart.

### Exit criteria

- read-only CI observation is automatic;
- remote mutations follow scoped policy and approval;
- a new push invalidates previous CI and review state;
- failures return to Codex and Claude Code as bounded untrusted evidence;
- duplicate PRs or reruns are not created after an ambiguous response;
- the run stops at a green draft PR or a precise escalation.

## Increment 7: Self-hosting proof

### Outcome

The stable application coordinates a visible improvement to its own candidate
version.

### Demonstration

1. Start a stable packaged DevalCopilot build.
2. Register the DevalCopilot repository.
3. Request one small visible product improvement.
4. Observe planning, critique, any challenge resolution, implementation,
   verification, review, and correction.
5. Inspect diff, test evidence, and frontend screenshot artifacts.
6. Approve publication.
7. Observe the draft pull request and exact-SHA CI.
8. If CI fails, observe bounded automated diagnosis and correction.
9. Finish with a green draft pull request or a clear escalation.
10. Restart the stable application and replay the complete run.

### Exit criteria

- no manual transfer occurs between Codex, Claude Code, or GitHub;
- the stable executable and active checkout are not modified;
- all consequential actions are attributable and policy-authorized;
- the candidate is independently buildable and reviewable;
- unresolved residual risk is visible rather than hidden by a success label.

## Cross-cutting completion checks

Every increment runs the checks applicable to its current boundaries:

- formatting and compiler warnings as errors;
- .NET analyzers including security diagnostics;
- domain and application unit tests;
- file-backed SQLite and adapter integration tests;
- MVC integration tests;
- frontend type checking and unit tests;
- browser smoke tests when a complete workflow exists;
- dependency and secret audits;
- complete diff inspection;
- documentation and ADR link validation.

## Deferred roadmap

After the MVP proves self-hosted development, candidate directions include:

- candidate preview in an isolated data directory;
- signed update and rollback flow;
- optional container or OS sandbox adapters;
- multiple concurrent projects and worktrees;
- additional agent providers;
- reusable workflow templates and project memory;
- cost and quality analytics;
- GitHub App authentication for team use;
- hosted coordination and remote workers.

Each direction requires its own evidence and decision. None is implicit in the
MVP architecture.
