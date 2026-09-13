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
- React frontend, developed browser-hosted first for iteration speed, wrapped
  by the Tauri shell before this increment is considered done;
- local authenticated MVC boundary using the per-launch bootstrap session
  described in [system-overview.md](../architecture/system-overview.md), and a
  generated TypeScript client committed under the canonical
  `Devalente.Shared.OpenApi.NSwag` policy: a committed `config.nswag`,
  generation with `noBuild=true` after a successful API build, generation
  failures fail the build, and CI regenerates the client and fails on drift;
- SQLite migrations, event journal, and current run projection;
- SignalR post-commit notification and cursor catch-up over LongPolling only;
- project list and first run cockpit;
- approved Dark Navy and Light cockpit shell with collapsible global navigation,
  Workflow, and Usage & Evidence rails;
- responsive Agent Collaboration surface, project switcher, active-participant
  treatment, and autonomous session timer;
- deterministic simulated-agent adapter, claimed and executed by a hosted
  component outside the `StartRun` command transaction.

### Exit criteria

- starting a simulated run produces visible sequenced events;
- the cockpit follows the behavior and hierarchy in
  [run-cockpit-specification.md](../product/run-cockpit-specification.md);
- collapsing either contextual rail releases space to Agent Collaboration;
- switching simulated projects never combines run state;
- closing and reopening the application preserves the run;
- disconnecting and reconnecting the UI catches up without duplicates;
- the frontend cannot invoke shell commands directly;
- `Api.IntegrationTests` prove `401` for an absent or incorrect per-launch
  credential and success for the correct one;
- Rust, the Windows C++ build tools, and the WebView2 runtime are verified
  present before Tauri packaging work begins;
- the packaged Tauri shell launches the sidecar through the stdin bootstrap
  handoff and reaches the same cockpit the browser-hosted frontend reaches,
  confirmed by a manual smoke check;
- backend, API integration, frontend, and browser-hosted Playwright smoke
  tests run locally. Playwright covers the browser-hosted slice only; the
  packaged Tauri shell is validated by the manual smoke check above. The
  browser-hosted composition authenticates through the real API and the real
  authentication handler using a session context the test harness generates
  in memory — never an anonymous or development-only bypass — as described in
  [system-overview.md](../architecture/system-overview.md). A native-shell E2E
  tool such as WebdriverIO is not added yet and is recorded here only as the
  likely future choice if native-only behavior later requires automated
  coverage.

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
- loop, duration, token, and usage budgets;
- progressive context manifests and token-usage evidence;
- provider-specific model, effort, and permission-mode discovery and selection
  at safe attempt boundaries;
- provider session correlation and eligible resume behavior;
- context-window visibility and safe manual compaction when supported;
- separate Codex and Claude account-usage snapshots, warning thresholds, and
  stop guardrails.

### Exit criteria

- Claude Code returns an acceptance rationale or a material challenge;
- Codex resolves every challenge explicitly;
- implementation is limited to the resolved plan and worktree;
- findings map to revision responses and source changes;
- invalid protocol output fails closed;
- repeated attempts do not receive unchanged full context by default;
- reaching a provider account-usage stop threshold prevents a new invocation
  for that provider without silently stopping unrelated eligible work;
- provider runtime controls expose `Unknown` or `Unsupported` rather than
  inventing model, context, usage, or compaction capability;
- loop exhaustion creates a useful human escalation.

## Increment 5: Local supervised delivery loop

### Outcome

One objective can reach a locally verified, reviewed commit.

### Deliverables

- end-to-end stage coordinator;
- bounded multi-project run scheduler with a default global limit of two active
  mutating runs;
- repository-level mutation lease and visible queue reasons;
- pause, resume, stop, retry, and takeover;
- autonomy policy, durable dispatch intent, executor lease, heartbeat, and
  deadline projections;
- an autonomy guardian that marks stale or failed dispatches truthfully and
  exposes bounded recovery actions;
- scoped approvals and invalidation;
- commit preparation and execution by the orchestrator;
- final evidence summary and run replay;
- crash-recovery scenarios across agent, command, and Git stages.

### Exit criteria

- neither agent commits directly;
- a commit requires the configured review and approval state;
- interrupted runs reconcile rather than guess;
- a complete local run requires no manual message transfer;
- two distinct repositories can progress concurrently while a second mutating
  run for either repository remains queued;
- pausing, stopping, or exhausting a budget for one run does not silently alter
  an unrelated run;
- enabling autonomy alone presents the run as armed, never as executing;
- a run is presented as running only while a claimed attempt has a current
  executor, lease, objective, heartbeat, and next expected signal;
- a stale heartbeat, lease, or dispatch becomes a visible waiting or attention
  state with an actionable reason, not an indefinitely working indicator;
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
- multiple simultaneous mutating runs within one canonical repository;
- additional agent providers;
- reusable workflow templates and project memory;
- cost and quality analytics;
- GitHub App authentication for team use;
- hosted coordination and remote workers.

Each direction requires its own evidence and decision. None is implicit in the
MVP architecture.
