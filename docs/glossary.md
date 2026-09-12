# Glossary

## Agent

A reasoning and execution participant such as Codex or Claude Code. An agent
does not own workflow state or authority.

## Agent attempt

One invocation of an agent adapter. Retries create new attempts and never
rewrite previous outcomes.

## Approval

A policy decision authorizing one specific consequential action, scope, and
current state. Approval is not a reusable global permission.

## Autonomous session time

Accumulated time during which a run is allowed to advance. Paused and terminal
time is excluded.

## Artifact

Immutable or versioned evidence stored outside the relational event payload,
such as a raw transcript, log, patch, screenshot, or generated report.

## Challenge

A structured objection to a proposal or decision. It identifies the disputed
claim, reasoning or evidence, impact, and suggested alternative.

## Checkpoint

A durable recovery point containing the workflow sequence, Git fingerprint,
relevant materialized state, and outstanding external actions.

## Decision

An explicit resolution that accepts, partially accepts, rejects, or escalates a
proposal or challenge and records the resulting action.

## Event

An immutable fact appended to the run journal with a monotonically increasing
sequence number.

## Evidence

An observable input used to evaluate a claim: a diff, command result, test
result, CI check, artifact, or independently inspected state. Evidence is still
untrusted input and does not grant authority.

## Finding

A review observation with severity, location, rationale, and disposition.

## Handoff

A structured transfer of objective, context, constraints, evidence, and
expected response between agents or workflow stages.

## Orchestrator

The authoritative .NET core that owns workflow state, policy, persistence,
process supervision, Git mutation, remote actions, and recovery.

## Provider account-usage guardrail

A user-configured warning or stop threshold evaluated against a fresh
provider-reported allowance window. It is separate from DevalCopilot-owned
attempt, stage, and run token budgets.

## Provider session

External metadata identifying a Codex, Claude Code, or GitHub interaction. It is
not the primary unit of product history.

## Run

One durable execution of a user objective against a project and initial Git
state.

## Self-hosted development

A stable DevalCopilot version coordinating development of a candidate version
in an isolated checkout. This is distinct from modifying the running binary.

## Stage

A named workflow phase such as planning, execution, local verification, review,
publication, or CI verification.

## Task

A bounded unit of intended work within a run. A task may have multiple agent or
command attempts.

## Tool-owned branch

A branch created for one DevalCopilot run under a configured naming policy. It
does not imply permission to push, force-update, merge, or delete the branch.
