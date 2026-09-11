# ADR-0004: Use a structured agent collaboration protocol

## Status

Accepted

## Context

The product must enable real conversation between Codex and Claude Code. Claude
Code must be able to question a Codex plan, and Codex must evaluate and act on
that challenge. A free-form transcript does not guarantee that objections are
material, addressed, bounded, or connected to resulting work.

Provider-specific conversation formats may change and cannot define the domain
model. Unlimited debate or correction loops would make cost, duration, and
completion unpredictable.

## Decision

Define a versioned project-owned message protocol with typed proposals,
acceptances, challenges, questions, decisions, execution reports, findings,
revision responses, and escalations.

- Every material challenge receives an explicit resolution.
- Challenges identify impact, evidence or reasoning, and a proposed alternative
  or precise question.
- Acceptance includes evaluation rationale rather than passive agreement.
- Codex is the primary planner and reviewer in the MVP.
- Claude Code is the primary critical executor in the MVP.
- Agent roles do not grant direct Git, shell, GitHub, approval, or workflow
  authority.
- Raw provider output remains an artifact linked to one immutable attempt.
- Invalid structured output fails closed and may receive one bounded format
  repair attempt.
- Debate, review, and CI correction loops have explicit limits and escalate when
  exhausted.

## Consequences

- The UI can render semantic conversation cards and disagreement resolution.
- Metrics can distinguish accepted suggestions, rejected challenges, review
  findings, and human interventions.
- Adapters must validate and translate provider output into project contracts.
- Prompt construction uses selected durable context instead of blindly replaying
  the complete transcript.
- Schema evolution requires protocol versioning and migration.

## Alternatives considered

### Free-form agent chat

Rejected because it cannot reliably enforce resolution, evidence, or bounded
iteration.

### One-way planner-to-executor handoff

Rejected because it encourages passive implementation and discards useful
executor analysis.

### Peer agents with identical authority

Rejected because role ambiguity makes decisions and escalation unpredictable.
The protocol allows disagreement while keeping responsibilities explicit.
