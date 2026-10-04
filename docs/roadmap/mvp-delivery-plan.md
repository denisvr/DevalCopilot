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

- bounded role-first architecture stabilization before the revision stage,
  separating workflow role, response contract, execution effect, provider
  assignment, and collaboration provenance as defined by
  [ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md);
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
- context-window visibility and safe manual compaction when supported (historical attempt evidence shows the model limits Claude
  reported, per [ADR-0023](../decisions/0023-record-claude-reported-model-context-limits-in-historical-attempt-evidence.md); remaining
  context, live capacity and compaction remain open);
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

The implemented review-correction control uses a durable default of two
claimed attempts per run. Every claim consumes that budget, including claims
that later fail or are interrupted. Exhaustion creates a durable escalation;
continuation requires an explicit human authorization for one additional claim.
This does not introduce generic pause/resume, provider fallback, Gemini,
automatic orchestration, or the deferred token/account-usage controls.

A second, independent control implements a durable run-wide Agent claim
budget: a default maximum of 16 claimed Agent attempts per run, spanning all
six Agent-claiming paths. Every claim permanently consumes one slot,
including claims that later fail or are interrupted; Simulated and Process
attempts never consume it. Unlike the review-correction control, exhaustion
has no human override — it is a hard stop for the run's remaining
Agent-claiming paths. See
[ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md).

A third, independent control implements a durable run-wide Agent
invocation-time budget: a default maximum of 120 minutes of reserved Agent
invocation time per run, enforced alongside (never instead of) the 16-claim
count budget above, on the same six Agent-claiming paths, at the same check
point. Every claim permanently reserves its own configured timeout, including
claims that later fail or are interrupted; Simulated and Process attempts
never consume it. A historical run recorded before this control existed keeps
no time-budget policy at all (truthfully `NULL`), never a fabricated ceiling.
See
[ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md).
This is a reservation ceiling only — it does not measure actual wall-clock
process duration, enforce provider account-usage limits, or independently
guarantee a provider invocation cannot outlive its own configured timeout.

A separate, owner-configured, run-scoped token-activity stop is also
implemented for each of Codex and Claude Code: once that provider's locally
recorded, provider-reported usage has reached the configured threshold, or
staying below it cannot be proved from the persisted evidence, the next Agent
claim for that provider is refused before any external work; an already
claimed attempt is unaffected. It is a retrospective local guardrail, not a
provider account allowance, a per-attempt cap, or a token reservation, and it
is distinct from the advisory token-activity warning. See the
[architecture description](../architecture/agent-collaboration-protocol.md#per-provider-run-token-activity-stop-at-agent-claim).
The provider account-usage stop-threshold exit criterion above, and the
remaining Increment 4 loop, token, and account-usage controls, remain open.

The two Claude paths that can edit the worktree also accept an optional,
owner-requested, run-scoped agentic-turn limit (1 through 100), snapshotted
immutably on each claimed attempt and passed as the provider's documented
`--max-turns` argument; it is a provider-loop request alongside the host
timeout, not a measured turn count, a token, cost, or account ceiling, a
replacement for any budget or stop, or an eligibility claim. See the
[architecture description](../architecture/agent-collaboration-protocol.md#optional-claude-agentic-turn-limit-for-mutation-attempts).

An explicit Claude correction of the findings of a verification diagnosis ([ADR-0018](../decisions/0018-add-explicit-local-verification-failure-diagnosis-and-bounded-correction.md))
may carry the same optional, bounded, advisory direct human guidance as the other explicit mutation requests
([ADR-0015](../decisions/0015-add-direct-human-guidance-to-explicit-mutation-requests.md), extended by
[ADR-0019](../decisions/0019-add-direct-human-guidance-to-diagnosis-origin-corrections.md)): the same normalization and bounds, one immutable
snapshot sealed once into the existing manifest envelope, and availability only within the shared correction allowance. It grants no extra
correction, authorization, permission or source authority, and the recorded text shows what the host supplied, not that a provider followed it.
See the [architecture description](../architecture/agent-collaboration-protocol.md#explicit-verification-failure-diagnosis).

Every newly claimed Agent stage also receives the exact root `AGENTS.md` and `CLAUDE.md` of its own project's owned worktree
as one bounded, identity-verified, fixed-boundary section of its sealed context manifest
([ADR-0021](../decisions/0021-add-bounded-root-instruction-context-to-agent-manifests.md)), replacing the earlier fixed
DevalCopilot documentation references. Only those two root files are read; each is Complete, Absent or Omitted with a fixed
reason, already sealed manifests replay unchanged, and the content is untrusted advisory context that grants no authority.
Imports, other instruction files, provider-side discovery, resume and compaction remain unselected. See the
[architecture description](../architecture/agent-collaboration-protocol.md#project-instruction-context-in-agent-manifests).

The bounded text previews of untracked files that those stages receive are admitted only from a physically proven, regular,
single-name file ([ADR-0022](../decisions/0022-admit-generic-untracked-previews-only-from-physically-proven-single-name-files.md)):
the open handle's final path must be exactly the reported path of the owned worktree and the operating system must report, before
any read and again after the bounded read, exactly one link, so a file that any other name reaches is an explicit omission while
its healthy siblings are still delivered. This closes generic untracked-preview delivery, not every filesystem read: raw Git
hashing for the checkpoint fingerprint can still read an outside hard link, and hardening it remains an unselected, separate
decision. See the
[architecture description](../architecture/agent-collaboration-protocol.md#bounded-untracked-file-previews-in-agent-manifests).

The tracked-file text those stages receive is closed the same way for newly claimed stages
([ADR-0024](../decisions/0024-deliver-new-tracked-change-text-only-from-attested-snapshots.md)): the old side is the exact blob of
the captured HEAD, the current side comes from a held handle proven physically inside the owned worktree as a single-name regular file,
and the host writes a conservative one-hunk comparison from those two owned snapshots instead of delivering Git's working-path patch.
Unsafe, unprovable or unsupported files are explicit omissions beside their delivered siblings, already sealed manifests replay
unchanged, and the raw observation and checkpoint fingerprint are unchanged. See the
[architecture description](../architecture/agent-collaboration-protocol.md#attested-tracked-change-text-in-agent-manifests).

The plan-challenge loop is bounded and its exhaustion escalates: a proposal
lineage may have one optional second critical review of the first Resolver
revision and, if challenged, one explicit second resolution. That successful
second resolution records one orchestrator-authored human escalation in the
same atomic save, and the resulting depth-two revision is never reviewable or
resolved again and is implementable only through one explicit human authorization
of exactly that final plan, followed by a separate explicit implementation request
([ADR-0016](../decisions/0016-add-explicit-human-authorization-of-one-escalated-plan-implementation.md);
the escalation's own fixed text says so, and both its current and original serialization are recognized by
[ADR-0020](../decisions/0020-correct-the-escalation-explanation-and-accept-its-two-canonical-forms.md)).
The cap is per lineage — more conservative than the
per-material-issue wording in the [workflow model](../architecture/workflow-model.md#bounded-loops), because the system does not decide whether
two challenges are the same issue. Other loop-exhaustion escalations (for
example CI correction rounds) remain part of later increments. See the
[architecture description](../architecture/agent-collaboration-protocol.md#optional-second-challenge-round-and-escalation).

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

- Gemini as an alternative Implementer only after the administrator-provisioned
  policy prerequisite in ADR-0011 is satisfied; assignment history is in place,
  while manual fallback remains a separate policy slice and automatic fallback
  still requires safe terminal classifications, reconciliation, and budgets;
- candidate preview in an isolated data directory;
- signed update and rollback flow;
- optional container or OS sandbox adapters;
- multiple simultaneous mutating runs within one canonical repository;
- additional agent providers beyond Gemini;
- parallel candidate executors, requiring their own candidate, workspace,
  lease, checkpoint-selection, review, budget, and cleanup decision rather than
  being inferred from multi-provider support;
- reusable workflow templates and project memory;
- cost and quality analytics;
- GitHub App authentication for team use;
- hosted coordination and remote workers.

Each direction requires its own evidence and decision. None is implicit in the
MVP architecture.
