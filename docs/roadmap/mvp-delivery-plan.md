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
- context-window visibility and safe manual compaction when supported
  (historical attempt evidence shows the model limits Claude reported, per
  ADR-0023; remaining context, live capacity and compaction remain open);
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

**Partial delivery, not completion.** Each control is bounded by its ADR and
the [protocol](../architecture/agent-collaboration-protocol.md#review-correction);
budgets and stops are local guards, not account, cost or capacity budgets:

- the review-correction control: by default two claims per run, then a
  durable escalation and one explicit human-authorized claim;
- default run-wide ceilings of 16 claimed Agent attempts, with no human
  override (ADR-0012), and 120 minutes of reserved, not measured, invocation
  time (ADR-0013); smaller immutable values may be chosen at manual intake
  (ADR-0028);
- the per-provider token-activity stop, and the Codex-only account-usage stop
  (ADR-0025) and advisory warning (ADR-0026); the account-usage stop exit
  criterion is met for Codex only;
- the optional Claude agentic-turn limit and advisory guidance for
  diagnosis-origin corrections (ADR-0015, ADR-0018, ADR-0019);
- root instruction context (ADR-0021) and attested untracked, tracked and
  human-checkpoint text (ADR-0022, ADR-0024, ADR-0027);
- the per-lineage plan-challenge cap and escalation (ADR-0016, ADR-0020;
  [bounded loops](../architecture/workflow-model.md#bounded-loops)).

**Open:** Claude account usage, eligible session resume, live context
capacity, manual compaction and additional safe permission profiles (each
unavailable while its contract is unproven), the remaining loop, token and
account-usage controls, other loop-exhaustion escalations (such as CI
correction rounds, in later increments) and hardening raw Git observation,
which can still read linked content outside the attested text routes.

## Increment 5: Local supervised delivery loop

On 2026-10-06 the owner authorized one bounded Increment 5 slice before the
remaining Increment 4 provider contracts are proven: an explicit
host-executed local commit of the exact verified, Agent-reviewed and
human-approved owned-worktree checkpoint, with its durable intent and restart
recovery
([ADR-0029](../decisions/0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md#sequencing-without-changing-completion-criteria)).
It is delivered on `origin/main` as a bounded capability; this exception
completes neither Increment 4 nor Increment 5.

**Planning coordination (partial).** ADR-0055 / PLAN-COORD-01 adds one explicitly armed, read-only planning sequence that the host
advances to Accepted planning or the Human escalation. It is a bounded capability (review and publication provenance: roadmap checkpoints and Git), not an Increment 4
or 5 completion: it starts no implementation, mutating coordination, verification, commit or publication, and every provider-contract
boundary below stays open.

**Local execution coordination (partial).** ADR-0060 / published LOCAL-COORD-01 adds one explicitly armed, Human-confirmed local
execution sequence that the host advances from one accepted plan through implementation, whole pinned verification passes, ordinary
review or eligible failure diagnosis and at most two shared corrections to an approved review awaiting the Human's own approval, a
Human escalation or an attention state. It is a bounded capability, not an Increment 4, 5 or 6 completion: it grants no approval,
commit or publication, adds no queue, executor lease, retry, takeover or process-aware recovery (a linked dispatched operation without
coherent host Exited evidence stays an owner), and every provider-contract boundary below stays open.

**Recorded local execution inspection (partial).** ADR-0061 / published LOCAL-INSPECT-01 adds only read-only inspection of that
recorded cycle (index, one selected step, one sealed verification stream window; current run and run history). It completes no
increment, grants and changes no authority and leaves queueing, process-aware release, replay and every provider-contract boundary open.

**Queued local execution (partial).** ADR-0062 / published LOCAL-QUEUE-01 adds an explicitly queued intake mode for that cycle:
a durable pending admission, a coordinator-owned reservation of the oldest eligible admission before external preparation, ownership
accounting in every existing entrance, truthful waiting disclosure and a startup that never resumes waiting work. It completes no
increment, adds no priority, executor lease, retry, takeover or process-aware release, and leaves every provider-contract boundary open.

**Recorded queue-admission inspection (partial).** ADR-0063 / independently verified published LOCAL-QUEUE-INSPECT-01 adds the bounded admission
ledger of one queued cycle in current-run and run-history inspection, including waits retired without a step and exact links to existing
step evidence. It is read-only, adds no execution or queue-control authority and completes no increment.

**Outstanding queue-admission overview (partial).** ADR-0064 / independently verified published LOCAL-QUEUE-OVERVIEW-01 adds a bounded, coherent,
read-only view of Waiting, Reserved and Unresolved admissions across projects, including an ended cycle's held reservation, with explicit
selection of the existing recorded inspector. LQO-R1 is resolved. It changes no queue or execution authority, adds no recovery action and
completes no increment.

**Proved terminal-quarantine release (reviewed GO; publication pending).** ADR-0065 / LOCAL-QUARANTINE-RELEASE-01 selects original-host dispatch
and termination evidence plus a separate exact Human confirmation to release only a terminal local operation's ownership. Results and ended
cycles stay unchanged; missing proof and Unresolved pre-claim reservations stay held. This is no executor lease, general recovery,
retry/resume/takeover or automatic approval/commit/publication, and completes no increment.
Review 4 resolves LQR-V1 after the permanent request-inventory correction and headed/headless passes; LQR-R1/R2/R3/R4 remain resolved.
[ADR-0066](../decisions/0066-bind-local-release-intent-to-an-immutable-relational-snapshot.md) resolves the checksum translation challenge through
a typed intent snapshot in the same receipt row, without a SQLite hashing capability or new release authority. That implementation is reviewed;
publication is authorized only for the exact reviewed-content identity in the planner's GO. Independent publication verification still follows.

**Agent supervision observations (partial).** ADR-0056 / AGENT-SUP-01 adds, for all seven existing Agent response contracts, a
durable disclosure of what the host's own observer last reported for the current Agent attempt (phase, last report, absolute deadline,
first attention) with an independent bounded sweep and startup closure of a previous host's observations. It is operational evidence
only, not Increment 5's executor lease, guardian, retry, resume, cancel, takeover or ownership release, not process liveness and not
provider progress; observation status never gates an action, and every provider-contract boundary below stays open.

**Workspace execution exclusion (partial).** ADR-0057 / WORKSPACE-EXCL-01 closes the reciprocal gap between the existing Agent and
verification claims: one shared policy over existing rows admits a claim only into an Idle workspace (all seven Agent contracts, repairs
and coordinated planning claims, verification and the explicit local commit), re-decides at each claim's write-locked commit seam and at
both single-use dispatch gates, and a protected status lets the cockpit and workspace panel withhold execution-start actions. It adds no
table, queue, slot, scheduler, takeover or lease release, does not cover standalone Process attempts, and is not Increment 5's global
two-mutating-run scheduler or process-aware recovery.

**Global mutating admission (partial).** ADR-0058 / GLOBAL-ADMIT-01 bounds recorded potentially mutating owners per authoritative
database across the existing mutating Agent claims, verification and local commit, with the same early, write-locked and dispatch-time
decisions, a protected disclosure and withholding of new mutating starts in both cockpit regions. ADR-0059 / GLOBAL-LIMIT-01 makes the
bound the operator's durable choice of one or two (default two), changed through one global control and one protected command and read
fresh at every admission. It adds no queue, lease, takeover or release, and is not ADR-0006's durable queue or process-aware recovery;
Increment 5 stays incomplete.

**Partial delivery, not completion.** Explicit Human-requested capabilities,
each bounded by its [ADR](../decisions/README.md):

- local delivery: complete-set approval, metadata-only abandonment of an
  inactive manual run and a historical receipt (ADR-0030 to ADR-0032);
- read-only browsers of run history, event headers, collaboration messages,
  reply parents and attempt attribution (ADR-0033 to ADR-0037), and the
  existing Agent-attempt history, evidence and sealed windows of a selected
  historical run, including an attempt that emitted no message
  ([inspection contract](../architecture/agent-collaboration-evidence-inspection.md#agent-attempt-history-and-evidence-inspection)),
  not full linked replay;
- Increment 6 groundwork: unpersisted point-in-time observations (ADR-0038,
  0039, 0041, 0042, 0044, 0045, 0047, 0049) and records of observation scope,
  identity binding and consent to one future push (ADR-0040, 0043, 0048).

No groundwork is readiness, permission, a push or publication authority:
ADR-0048 records consent only, and ADR-0049's reported access is not
branch-push permission. ADR-0050 is Proposed and authorizes nothing.

**Open:** the coordination, scheduling, autonomy, recovery and replay
deliverables below; Increment 6's push and draft pull request, branch-push
permission, rulesets and protection, required-check policy, workflow and log
enumeration, diagnosis and correction routing, rerun, revocation, monitoring
and reconciliation. Increment 4's open provider contracts stay unavailable.
The remaining deliverables and exit criteria below are unchanged.

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
