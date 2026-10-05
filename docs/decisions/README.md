# Architecture Decision Records

Architecture Decision Records preserve why material DevalCopilot choices were
made. Accepted records are historical; reverse one by adding a new ADR that
supersedes it.

## Accepted decisions

- [ADR-0001: Build a local-first supervised orchestrator](0001-build-a-local-first-supervised-orchestrator.md)
- [ADR-0002: Use Tauri, React, and .NET](0002-use-tauri-react-and-dotnet.md)
- [ADR-0003: Use a durable SQLite event journal](0003-use-a-durable-sqlite-event-journal.md)
- [ADR-0004: Use a structured agent collaboration protocol](0004-use-a-structured-agent-collaboration-protocol.md)
- [ADR-0005: Use Git worktrees and a policy-controlled GitHub loop](0005-use-git-worktrees-and-a-policy-controlled-github-loop.md)
- [ADR-0006: Support bounded concurrent runs across projects](0006-support-bounded-concurrent-runs-across-projects.md)
- [ADR-0007: Registration-time repository identity is non-authoritative for mutation exclusion](0007-registration-time-repository-identity-is-non-authoritative-for-mutation-exclusion.md)
- [ADR-0008: Physical repository identity and tool-owned worktree ownership](0008-physical-repository-identity-and-tool-owned-worktree-ownership.md)
- [ADR-0009: Separate agent roles, effects, and provider assignments](0009-separate-agent-roles-effects-and-provider-assignments.md)
- [ADR-0010: Add a role-scoped review-correction response contract](0010-add-review-correction-response-contract.md)
  (its changes-requested-review-only finding source is narrowly extended by ADR-0018)
- [ADR-0011: Require administrator-provisioned policy before Gemini CLI execution](0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md)
- [ADR-0012: Add a durable run-wide Agent claim budget](0012-add-a-durable-run-wide-agent-claim-budget.md)
- [ADR-0013: Add a durable run-wide Agent invocation-time budget](0013-add-a-durable-run-wide-agent-invocation-time-budget.md)
- [ADR-0014: Add manual Agent run intake with durable execution-mode isolation](0014-add-manual-agent-run-intake-with-durable-execution-mode-isolation.md)
- [ADR-0015: Add direct human guidance to explicit mutation requests](0015-add-direct-human-guidance-to-explicit-mutation-requests.md)
  (its request scope is narrowly extended to the diagnosis-origin correction by ADR-0019)
- [ADR-0016: Add explicit human authorization of one escalated-plan implementation](0016-add-explicit-human-authorization-of-one-escalated-plan-implementation.md)
  (its preservation of the ordinary first-revision review target is narrowly superseded by ADR-0017, and its
  single-source-text-form requirement by ADR-0020)
- [ADR-0017: Review the implemented plan through correction](0017-review-the-implemented-plan-through-correction.md)
- [ADR-0018: Add explicit local verification failure diagnosis and bounded correction](0018-add-explicit-local-verification-failure-diagnosis-and-bounded-correction.md)
  (its correction request narrowly accepts optional direct human guidance under ADR-0019)
- [ADR-0019: Add direct human guidance to diagnosis-origin corrections](0019-add-direct-human-guidance-to-diagnosis-origin-corrections.md)
- [ADR-0020: Correct the escalation explanation and accept its two canonical forms](0020-correct-the-escalation-explanation-and-accept-its-two-canonical-forms.md)
- [ADR-0021: Add bounded root instruction context to Agent manifests](0021-add-bounded-root-instruction-context-to-agent-manifests.md)
  (additive; it changes no existing authority decision)
- [ADR-0022: Admit generic untracked-file previews only from physically proven single-name files](0022-admit-generic-untracked-previews-only-from-physically-proven-single-name-files.md)
  (additive; it narrows what generic untracked previews may deliver and changes no existing authority decision)
- [ADR-0023: Record Claude-reported model context limits in historical attempt evidence](0023-record-claude-reported-model-context-limits-in-historical-attempt-evidence.md)
  (additive; it records and shows what Claude reported and changes no existing authority decision)
- [ADR-0024: Deliver new tracked-change text only from attested snapshots](0024-deliver-new-tracked-change-text-only-from-attested-snapshots.md)
  (it narrowly advances ADR-0021 and ADR-0022 for new tracked delivery only and changes no existing authority decision)
- [ADR-0025: Stop new Codex attempts at an explicit account-usage percentage](0025-stop-new-codex-attempts-at-an-explicit-account-usage-percentage.md)
  (additive; an optional run-scoped Codex guard at claim and before dispatch that changes no existing authority decision)
- [ADR-0026: Warn explicitly about a Codex account-usage percentage](0026-warn-explicitly-about-a-codex-account-usage-percentage.md)
  (additive; an optional run-scoped advisory percentage checked only on explicit request, independent of the ADR-0025 stop and
  changing no existing authority decision)
- [ADR-0027: Compare attested tracked sources for human checkpoint inspection](0027-compare-attested-tracked-sources-for-human-checkpoint-inspection.md)
  (it advances ADR-0024's deferred ordinary checkpoint diff query for the authenticated human inspection only and changes no
  existing authority decision)
- [ADR-0028: Let the owner choose immutable run budgets at manual intake](0028-let-the-owner-choose-immutable-run-budgets-at-manual-intake.md)
  (it narrowly advances ADR-0012, ADR-0013 and ADR-0014's fixed-default creation behavior for new manual runs only and changes
  no claim, immutability, consumption or historical-policy decision)

## Status values

- `Proposed`
- `Accepted`
- `Partially superseded by ADR-NNNN`
- `Superseded by ADR-NNNN`
- `Rejected`
