# Agent collaboration protocol

## Purpose

The protocol enables constructive disagreement and evidence-based resolution
without creating an unbounded free-form conversation. It separates durable
workflow semantics from provider-specific session formats.

Codex and Claude Code have primary roles, not absolute authority. The
orchestrator owns authority and enforces the resolved plan, policies, budgets,
and gates.

The typed dialogue (the message envelope, role-first message authorization and
eligibility, message types, version 1.0 reply semantics, default interaction,
authority matrix and structured output failure) is in the
[dialogue](agent-collaboration-dialogue.md) companion, and the durable ledger
boundary, run execution mode and isolation and explicit abandonment are in the
[ledger](agent-collaboration-ledger.md) companion; their original headings
follow Purpose, before Context assembly, and forward there.

Attempt-input preparation (context assembly, token efficiency and untrusted
content) is in the [context](agent-collaboration-context.md) companion, and
host runtime preflight is in the
[provider contracts](agent-collaboration-provider-contracts.md#provider-runtime-configuration)
companion; their original headings below forward there. The companion
directory under Context assembly routes the protocol's other moved subjects.

## Message envelope

Moved to [Message envelope](agent-collaboration-dialogue.md#message-envelope)
in the dialogue companion.

## Role-first message authorization and eligibility

Moved to [Role-first message authorization and eligibility](agent-collaboration-dialogue.md#role-first-message-authorization-and-eligibility)
in the dialogue companion.

## Current durable ledger boundary

Moved to [Current durable ledger boundary](agent-collaboration-ledger.md#current-durable-ledger-boundary)
in the ledger companion.

## Run execution mode and isolation

Moved to [Run execution mode and isolation](agent-collaboration-ledger.md#run-execution-mode-and-isolation)
in the ledger companion.

### Explicit abandonment and the claim paths

Moved to [Explicit abandonment and the claim paths](agent-collaboration-ledger.md#explicit-abandonment-and-the-claim-paths)
in the ledger companion.

## Message types

Moved to [Message types](agent-collaboration-dialogue.md#message-types)
in the dialogue companion.

### Proposal

Moved to [Proposal](agent-collaboration-dialogue.md#proposal)
in the dialogue companion.

### Acceptance

Moved to [Acceptance](agent-collaboration-dialogue.md#acceptance)
in the dialogue companion.

### Challenge

Moved to [Challenge](agent-collaboration-dialogue.md#challenge)
in the dialogue companion.

### Question

Moved to [Question](agent-collaboration-dialogue.md#question)
in the dialogue companion.

### Decision

Moved to [Decision](agent-collaboration-dialogue.md#decision)
in the dialogue companion.

### Execution report

Moved to [Execution report](agent-collaboration-dialogue.md#execution-report)
in the dialogue companion.

### Review finding

Moved to [Review finding](agent-collaboration-dialogue.md#review-finding)
in the dialogue companion.

### Revision response

Moved to [Revision response](agent-collaboration-dialogue.md#revision-response)
in the dialogue companion.

### Escalation

Moved to [Escalation](agent-collaboration-dialogue.md#escalation)
in the dialogue companion.

### Human instruction

Moved to [Human instruction](agent-collaboration-dialogue.md#human-instruction)
in the dialogue companion.

## Version 1.0 reply semantics

Moved to [Version 1.0 reply semantics](agent-collaboration-dialogue.md#version-10-reply-semantics)
in the dialogue companion.

## Default interaction

Moved to [Default interaction](agent-collaboration-dialogue.md#default-interaction)
in the dialogue companion.

### Planning

Moved to [Planning](agent-collaboration-dialogue.md#planning)
in the dialogue companion.

### Critical review

Moved to [Critical review](agent-collaboration-dialogue.md#critical-review)
in the dialogue companion.

### Resolution

Moved to [Resolution](agent-collaboration-dialogue.md#resolution)
in the dialogue companion.

### Execution and review

Moved to [Execution and review](agent-collaboration-dialogue.md#execution-and-review)
in the dialogue companion.

## Authority matrix

Moved to [Authority matrix](agent-collaboration-dialogue.md#authority-matrix)
in the dialogue companion.

## Structured output failure

Moved to [Structured output failure](agent-collaboration-dialogue.md#structured-output-failure)
in the dialogue companion.

## Context assembly

Moved to [Context assembly](agent-collaboration-context.md#context-assembly)
in the context companion.

The companion directory that follows is not part of context assembly: it
lists the companions that hold the protocol's other moved subjects, whose
original headings below forward to them. The dialogue and ledger companions
are routed from Purpose instead: their original headings precede Context
assembly and forward to them.

The provider CLI safety contracts (Claude Code critical review and
implementation, and the deferred Gemini CLI boundary) are in the
[provider contracts](agent-collaboration-provider-contracts.md) companion; their
original headings below forward there.

Per-attempt execution evidence (semantic outcome versus host-measured and
provider-reported facts) and the run-wide Agent process-duration summary are in
the [execution evidence](agent-collaboration-execution-evidence.md) companion;
their original headings below forward there.

Read-only evidence inspection (recorded collaboration-input provenance, the
sealed Agent-artifact window, and Agent-attempt history and evidence) is in the
[evidence inspection](agent-collaboration-evidence-inspection.md) companion;
their original headings below forward there.

The manual read-only format repairs (the Codex Planner's and the remaining
read-only stages') are in the [format repair](agent-collaboration-format-repair.md)
companion; their original headings below forward there.

Provider-reported usage evidence (the Claude Code and Codex token-usage
contracts and Claude-reported model context limits) is in the
[usage evidence](agent-collaboration-usage-evidence.md) companion; their
original headings below forward there.

Run-scoped token-activity controls (the advisory per-provider warnings and the
per-provider stop at Agent claim) are in the
[token-activity controls](agent-collaboration-token-activity-controls.md)
companion; their original headings below forward there.

Account-usage observation and controls (the provider account-allowance
contracts, the run-scoped Codex account-usage stop at claim and dispatch and
the advisory Codex account-usage warning) are in the
[account usage](agent-collaboration-account-usage.md) companion; their original
headings below forward there.

Invocation configuration (the Codex model and reasoning-effort catalog
contract, the explicit Codex model and reasoning-effort requests, the explicit
Claude model-alias and effort requests and the optional Claude agentic-turn
limit for mutation attempts) is in the
[invocation configuration](agent-collaboration-invocation-configuration.md)
companion; their original headings below forward there.

Human guidance, escalation and diagnosis (bounded guidance for one authorized
review correction, optional direct human guidance for mutation requests, the
optional second challenge round and escalation, explicit human authorization
of one escalated-plan implementation and explicit verification failure
diagnosis) are in the
[guidance and escalation](agent-collaboration-guidance-and-escalation.md)
companion; their original headings below forward there.

Review correction, with its correction budget and escalation, the run-wide
Agent claim-count and reserved invocation-time budgets and their manual-intake
choice, is in the
[correction and budgets](agent-collaboration-correction-and-budgets.md)
companion, and the provider configuration observations formerly in the same
section are in the
[invocation configuration](agent-collaboration-invocation-configuration.md#provider-configuration-observations)
companion; the original Review correction heading below forwards to both.

Repository source evidence (bounded untracked-file previews, bounded
tracked-hunk evidence and attested tracked-change text in Agent manifests, the
attested checkpoint comparison for human inspection and project instruction
context in Agent manifests) is in the
[source evidence](agent-collaboration-source-evidence.md) companion; their
original headings below forward there.

The complete-set Human approval for local delivery and the recorded
local-delivery receipt of the explicit local commit are in the
[local delivery](agent-collaboration-local-delivery.md) companion; their
original headings below forward there. Local delivery is not remote
publication.

## Provider runtime configuration

Moved to [Provider runtime configuration](agent-collaboration-provider-contracts.md#provider-runtime-configuration)
in the provider contracts companion.

### Claude Code critical-review CLI safety contract

Moved to [Claude Code critical-review CLI safety contract](agent-collaboration-provider-contracts.md#claude-code-critical-review-cli-safety-contract)
in the provider contracts companion.

### Claude Code implementation CLI safety contract

Moved to [Claude Code implementation CLI safety contract](agent-collaboration-provider-contracts.md#claude-code-implementation-cli-safety-contract)
in the provider contracts companion.

### Deferred Gemini CLI execution boundary

Moved to [Deferred Gemini CLI execution boundary](agent-collaboration-provider-contracts.md#deferred-gemini-cli-execution-boundary)
in the provider contracts companion.

### Review correction

Moved to [Review correction](agent-collaboration-correction-and-budgets.md#review-correction)
in the correction and budgets companion. Its former provider configuration
observations are in
[Provider configuration observations](agent-collaboration-invocation-configuration.md#provider-configuration-observations)
in the invocation configuration companion.

### Bounded human guidance for one authorized review correction

Moved to [Bounded human guidance for one authorized review correction](agent-collaboration-guidance-and-escalation.md#bounded-human-guidance-for-one-authorized-review-correction)
in the guidance and escalation companion.

### Optional direct human guidance for mutation requests

Moved to [Optional direct human guidance for mutation requests](agent-collaboration-guidance-and-escalation.md#optional-direct-human-guidance-for-mutation-requests)
in the guidance and escalation companion.

### Execution evidence versus semantic outcome

Moved to [Execution evidence versus semantic outcome](agent-collaboration-execution-evidence.md#execution-evidence-versus-semantic-outcome)
in the execution evidence companion.

### Run-wide Agent process-duration evidence summary

Moved to [Run-wide Agent process-duration evidence summary](agent-collaboration-execution-evidence.md#run-wide-agent-process-duration-evidence-summary)
in the execution evidence companion.

### Recorded collaboration-input provenance

Moved to [Recorded collaboration-input provenance](agent-collaboration-evidence-inspection.md#recorded-collaboration-input-provenance)
in the evidence inspection companion.

### Sealed Agent-artifact window inspection

Moved to [Sealed Agent-artifact window inspection](agent-collaboration-evidence-inspection.md#sealed-agent-artifact-window-inspection)
in the evidence inspection companion.

### Agent-attempt history and evidence inspection

Moved to [Agent-attempt history and evidence inspection](agent-collaboration-evidence-inspection.md#agent-attempt-history-and-evidence-inspection)
in the evidence inspection companion.

### Provider token-usage contracts

Moved to [Provider token-usage contracts](agent-collaboration-usage-evidence.md#provider-token-usage-contracts)
in the usage evidence companion.

### Claude-reported model context limits

Moved to [Claude-reported model context limits](agent-collaboration-usage-evidence.md#claude-reported-model-context-limits)
in the usage evidence companion.

### Provider account-allowance contracts

Moved to [Provider account-allowance contracts](agent-collaboration-account-usage.md#provider-account-allowance-contracts)
in the account usage companion.

### Codex model and reasoning-effort catalog contract

Moved to [Codex model and reasoning-effort catalog contract](agent-collaboration-invocation-configuration.md#codex-model-and-reasoning-effort-catalog-contract)
in the invocation configuration companion.

### Explicit Codex model and reasoning-effort requests

Moved to [Explicit Codex model and reasoning-effort requests](agent-collaboration-invocation-configuration.md#explicit-codex-model-and-reasoning-effort-requests)
in the invocation configuration companion.

### Explicit Claude model-alias requests

Moved to [Explicit Claude model-alias requests](agent-collaboration-invocation-configuration.md#explicit-claude-model-alias-requests)
in the invocation configuration companion.

### Explicit Claude effort requests

Moved to [Explicit Claude effort requests](agent-collaboration-invocation-configuration.md#explicit-claude-effort-requests)
in the invocation configuration companion.

### Optional Claude agentic-turn limit for mutation attempts

Moved to [Optional Claude agentic-turn limit for mutation attempts](agent-collaboration-invocation-configuration.md#optional-claude-agentic-turn-limit-for-mutation-attempts)
in the invocation configuration companion.

### Per-provider run token-activity warnings

Moved to [Per-provider run token-activity warnings](agent-collaboration-token-activity-controls.md#per-provider-run-token-activity-warnings)
in the token-activity controls companion.

### Per-provider run token-activity stop at Agent claim

Moved to [Per-provider run token-activity stop at Agent claim](agent-collaboration-token-activity-controls.md#per-provider-run-token-activity-stop-at-agent-claim)
in the token-activity controls companion.

### Run-scoped Codex account-usage stop at claim and dispatch

Moved to [Run-scoped Codex account-usage stop at claim and dispatch](agent-collaboration-account-usage.md#run-scoped-codex-account-usage-stop-at-claim-and-dispatch)
in the account usage companion.

### Run-scoped advisory Codex account-usage warning

Moved to [Run-scoped advisory Codex account-usage warning](agent-collaboration-account-usage.md#run-scoped-advisory-codex-account-usage-warning)
in the account usage companion.

### One manual Codex Planner format repair

Moved to [One manual Codex Planner format repair](agent-collaboration-format-repair.md#one-manual-codex-planner-format-repair)
in the format repair companion.

### One manual format repair of the remaining read-only stages

Moved to [One manual format repair of the remaining read-only stages](agent-collaboration-format-repair.md#one-manual-format-repair-of-the-remaining-read-only-stages)
in the format repair companion.

### Optional second challenge round and escalation

Moved to [Optional second challenge round and escalation](agent-collaboration-guidance-and-escalation.md#optional-second-challenge-round-and-escalation)
in the guidance and escalation companion.

### Explicit human authorization of one escalated-plan implementation

Moved to [Explicit human authorization of one escalated-plan implementation](agent-collaboration-guidance-and-escalation.md#explicit-human-authorization-of-one-escalated-plan-implementation)
in the guidance and escalation companion.

### Explicit verification failure diagnosis

Moved to [Explicit verification failure diagnosis](agent-collaboration-guidance-and-escalation.md#explicit-verification-failure-diagnosis)
in the guidance and escalation companion.

### Bounded untracked-file previews in Agent manifests

Moved to [Bounded untracked-file previews in Agent manifests](agent-collaboration-source-evidence.md#bounded-untracked-file-previews-in-agent-manifests)
in the source evidence companion.

### Bounded tracked-hunk evidence in Agent manifests

Moved to [Bounded tracked-hunk evidence in Agent manifests](agent-collaboration-source-evidence.md#bounded-tracked-hunk-evidence-in-agent-manifests)
in the source evidence companion.

### Attested tracked-change text in Agent manifests

Moved to [Attested tracked-change text in Agent manifests](agent-collaboration-source-evidence.md#attested-tracked-change-text-in-agent-manifests)
in the source evidence companion.

### Attested checkpoint comparison for human inspection

Moved to [Attested checkpoint comparison for human inspection](agent-collaboration-source-evidence.md#attested-checkpoint-comparison-for-human-inspection)
in the source evidence companion.

### Complete-set Human approval for local delivery

Moved to [Complete-set Human approval for local delivery](agent-collaboration-local-delivery.md#complete-set-human-approval-for-local-delivery)
in the local delivery companion.

### Recorded local-delivery receipt

Moved to [Recorded local-delivery receipt](agent-collaboration-local-delivery.md#recorded-local-delivery-receipt)
in the local delivery companion.

### Project instruction context in Agent manifests

Moved to [Project instruction context in Agent manifests](agent-collaboration-source-evidence.md#project-instruction-context-in-agent-manifests)
in the source evidence companion.

## Token efficiency

Moved to [Token efficiency](agent-collaboration-context.md#token-efficiency)
in the context companion.

## Untrusted content

Moved to [Untrusted content](agent-collaboration-context.md#untrusted-content)
in the context companion.

## Protocol evolution

The envelope is versioned. Additive optional fields may remain compatible.
Changing message meaning, authority, required fields, or resolution behavior
requires a protocol version and migration plan.
