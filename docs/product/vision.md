# Product vision

## Purpose

DevalCopilot turns fragmented human-mediated use of coding agents into one
observable, durable, and policy-controlled software delivery workflow.

Today, a developer often transfers plans, implementation reports, diffs, test
failures, and review comments manually between tools. That manual bridge loses
context, hides decisions, and makes recovery difficult. DevalCopilot replaces
the bridge while preserving human authority.

## Product promise

Given a software objective and an approved repository, DevalCopilot coordinates
specialized agents and engineering tools until it produces either:

- a verified, reviewable change with complete evidence; or
- a precise escalation that explains what is blocked and what decision is
  required.

## Initial user

The MVP serves one experienced developer running the application locally on a
trusted workstation. It is not initially a hosted collaboration service.

## Operating model

Workflow roles and their authority are defined independently of provider
identity. The following are the current MVP assignments, not permanent
requirements of those roles:

- Codex currently performs planning, challenge resolution, and code review.
- Claude Code currently performs critical plan review, implementation, local
  investigation, and correction.
- Roles are asymmetric but not hierarchical. The critical reviewer may
  challenge the planner, who must resolve material challenges explicitly.
- Git records source history and isolates candidate work.
- Local verification and GitHub CI provide objective evidence.
- DevalCopilot controls state transitions, permissions, budgets, retries,
  process lifecycle, and recovery.
- The human approves consequential actions and resolves bounded disagreements.

A different provider can perform a role only after its specific invocation,
response, and safety contracts have been implemented and verified. Provider
neutrality does not imply arbitrary provider support or automatic fallback.

## Product principles

### Evidence over assertion

Agent claims do not establish that a change is correct. Diffs, commands, test
results, CI checks, artifacts, and explicit review outcomes provide evidence.

### Durable state over transcript memory

The product stores structured decisions, attempts, findings, approvals,
checkpoints, and events. Captured provider output remains available as bounded
artifacts when recorded, but is not necessarily a complete harness transcript
and does not become the primary workflow model.

### Constructive disagreement over passive obedience

An agent should challenge a plan when it finds a concrete risk, contradiction,
missing requirement, or better approach. Disagreement must include reasoning or
evidence and must end in a recorded resolution.

### Bounded autonomy over unrestricted execution

The orchestrator automates safe, reversible work and gates consequential or
externally visible actions. Autonomy grows through explicit policy and evidence,
not through a global unrestricted mode.

### Recovery over optimistic execution

Every external action can fail, time out, outlive the UI, or be interrupted by
an application crash. Durable intent, attempts, leases, checkpoints, and
reconciliation are part of the normal design.

### Visible progress over hidden automation

The user can see what each agent proposed, challenged, executed, reviewed, and
changed. The interface makes current state, evidence, cost, and required action
immediately clear.

### Deliberate context and token economy

Tokens are a finite engineering resource. Each agent receives the smallest
context that is sufficient for its current decision, with references available
for progressive retrieval. DevalCopilot avoids replaying complete transcripts,
restating stable decisions, sending unchanged files, or repeating failed work
without new evidence. Higher-cost reasoning is reserved for decisions whose
risk or ambiguity justifies it, and usage remains visible against stage and run
budgets.

### Dogfooding without self-destruction

A stable DevalCopilot version may develop a candidate version in an isolated
worktree. It never modifies its running binary or active data store in place.

## Long-term direction

The role-first architecture can later accommodate additional validated
providers. Their execution, along with execution sandboxes, remote workers,
reusable workflow templates, policy profiles, and team collaboration, remains
future work. None of those possibilities may weaken the deterministic local MVP
or justify speculative infrastructure.

### Status of the ideas below

**Current Direction** is the accepted local-first architecture: the .NET host
owns durable workflow state and policy; agents have roles independent of their
providers; the UI submits intentions; bounded artifacts and typed messages are
evidence, not a replacement for workflow state. The [MVP scope](mvp-scope.md)
and [accepted decisions](../decisions/README.md) remain authoritative for
approved work.

**Deferred Idea** means a possible post-MVP capability, not an approved slice,
delivery date, or permission to change the current roadmap. **Exploratory**
means even its product and technical shape require research. None of the ideas
below changes the current MVP or silently supersedes an ADR. The planner must
assess demand and conflicts before proposing implementation.

### Mobile supervision and remote control — Deferred Idea

- **Objective:** Keep agents and Git operations on the desktop/runtime while a
  mobile companion presents Runs, Attempts, current stage, agent status,
  findings, and test results. It could notify when work finishes, fails, or
  needs intervention; let the human approve or reject decisions, answer agent
  questions, and request Pause, Resume, or Stop.
- **Problem:** A long-running local workflow can require timely human attention
  when the developer is away from the workstation.
- **Relationship to current architecture:** The local .NET runtime remains the
  only source of truth and enforces every action. An authenticated API with
  SignalR notifications and durable catch-up is a possible transport, not a
  decision to expose the present loopback API remotely.
- **Risks:** Remote approvals and stop controls expand the threat surface;
  credentials, device loss, authorization, replay, network failure, and stale
  state require explicit controls. The current per-launch local secret is not
  a remote-device authentication design.
- **Dependencies:** A deliberate remote-access security design, scoped device
  identity and action authorization, reliable reconnection, and a mobile UI.
  Prefer LAN, Tailscale, or VPN evaluation before any direct public exposure.
- **Why deferred:** The MVP is desktop-only and local-loopback by
  [ADR-0001](../decisions/0001-build-a-local-first-supervised-orchestrator.md)
  and [ADR-0002](../decisions/0002-use-tauri-react-and-dotnet.md); remote
  control would require a separate security and product decision.
- **Signals to revisit:** Repeated time-sensitive interventions away from the
  desk, a stable local workflow, and a demonstrably safe remote-access model.

### Harness Session Explorer and unified agent history — Deferred Idea

- **Objective:** Discover existing local Claude Code, Codex, and eventually
  other harness sessions; index them as read-only observations; search by
  project, provider, model, date, and content; and show native transcript,
  tool calls, subagents, and metadata when the source actually provides them.
  Correlate with Run, Attempt, Role, Provider, and Checkpoint only when identity
  and provenance are reliable.
- **Problem:** The current Run/Attempt view cannot browse arbitrary pre-existing
  harness histories, and captured process output is not a complete native
  transcript.
- **Relationship to current architecture:** Keep **authoritative workflow
  data** (Runs, Attempts, decisions, events, and application-owned artifacts)
  strictly separate from **external harness observability**. Imported sessions
  must never establish workflow completion, approval, or source identity.
- **Risks:** Provider-private formats and locations can change; logs can be
  incomplete, huge, sensitive, or attacker-controlled. Search and correlation
  can disclose content or falsely imply that an external session belongs to a
  particular Attempt.
- **Dependencies:** Isolated, versioned reader/parser per provider; explicit
  user consent and read scope; bounded indexing and retention; safe rendering;
  and provenance and correlation rules that fail closed.
- **Why deferred:** The local MVP prioritizes its own typed evidence. Treating
  a native transcript as workflow authority would conflict with
  [ADR-0003](../decisions/0003-use-a-durable-sqlite-event-journal.md) and
  [ADR-0004](../decisions/0004-use-a-structured-agent-collaboration-protocol.md);
  that conflict is not resolved here.
- **Signals to revisit:** Frequent need to inspect sessions created outside
  DevalCopilot, stable-enough provider evidence for safe readers, and clear
  diagnostic value beyond the existing cockpit and artifacts.

### Native harness and agent runtime — Exploratory

- **Objective:** Explore DevalCopilot executing models directly rather than
  only orchestrating external harnesses. Possible components include model
  adapters, tool execution, context management, compaction, memory, retrieval,
  permissions, and an agent loop.
- **Problem:** External harnesses constrain control over tool policy, context,
  evidence, and provider portability.
- **Relationship to current architecture:** A native runtime would be a new
  execution boundary. It must not create another source of workflow authority;
  role and effect policy must remain separate from provider identity under
  [ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md).
- **Risks:** Tool and model safety, secrets, isolation, cost, reliability,
  evaluation, and maintenance become DevalCopilot responsibilities. Moving
  policy outside the authoritative .NET host would conflict with ADR-0002.
- **Dependencies:** Demonstrated product need, an explicit authority/security
  design, provider contracts, sandboxing, evaluation, and recovery evidence.
- **Why deferred:** This is a different product-scale commitment, not a shortcut
  to finishing the external-harness MVP.
- **Signals to revisit:** Repeated unworkable external-harness constraints and
  evidence that a narrow native runtime would deliver material user value
  safely.

### Context lifecycle management — Exploratory

- **Objective:** Consider replaceable policies for active-context selection:
  `PIN`, `KEEP`, `TRUNCATE`, `DROP`, and `ARCHIVE`. Full history may remain
  durable outside the active model window. Candidate strategies could use a
  classifier such as Jev or another relevance-scoring mechanism; none is
  selected here.
- **Problem:** Summary-only compaction can erase important distinctions, while
  replaying full history wastes context and tokens.
- **Relationship to current architecture:** Extend the current progressive,
  bounded context selection without changing the authoritative journal.
  `PIN` would keep selected material in active context, `KEEP` would preserve
  ordinarily selected material, and `TRUNCATE` would retain a bounded form.
  `DROP` means omit from active context, not delete durable evidence; `ARCHIVE`
  means retain outside the active window. Policies should be strategy-based
  and replaceable rather than hardcoded into the workflow model.
- **Risks:** Incorrect relevance decisions, hidden omission, prompt injection,
  irreproducible context, and cost or latency from classifiers.
- **Dependencies:** Provenance for selected context, measurable quality and
  token-budget outcomes, inspectable decisions, and safe fallbacks.
- **Why deferred:** The MVP already favors the smallest sufficient durable
  context; a lifecycle engine is not needed to establish that baseline.
  Deleting authoritative history would conflict with ADR-0003 and is not
  proposed.
- **Signals to revisit:** Measured failures after compaction or repeated
  context-window pressure that bounded selection cannot resolve.

### Harness-specialized workflows — Exploratory

- **Objective:** Evaluate execution profiles for workloads such as software
  engineering, SAP, legacy modernization, security, and frontend work. A
  profile could select an appropriate harness configuration, context, tools,
  and model for the workload.
- **Problem:** One generic invocation setup may not fit domains with different
  evidence, tooling, and safety requirements.
- **Relationship to current architecture:** Profiles could configure validated
  adapters and policies, but must not let a provider name confer role,
  permission, or mutation authority. That boundary is fixed by ADR-0009.
- **Risks:** Profile proliferation, implicit privilege escalation, brittle
  provider assumptions, and unclear verification across domains.
- **Dependencies:** Demonstrated domain-specific journeys, closed permission
  profiles, adapter capability evidence, and workload-specific acceptance
  tests.
- **Why deferred:** The current MVP must first prove one supervised software
  delivery workflow; these domains are not approved implementation scope.
- **Signals to revisit:** Repeated demand for a particular domain and evidence
  that its needs cannot be met by safe configuration of existing capabilities.

### External product inspirations — Exploratory references only

These are prompts for later comparative research, not verified feature-parity
claims, dependencies, requirements, or endorsements:

- **Orca:** multi-agent cockpit, mobile supervision, session discovery, and
  workspace ideas to examine.
- **OpenCode:** multi-provider harness ideas to examine.
- **Devin:** autonomous software-engineering platform ideas to examine.
- **Traycer:** multi-agent orchestration and workspace ideas to examine.
