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
checkpoints, and events. Raw transcripts remain available as artifacts but do
not become the primary workflow model.

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
