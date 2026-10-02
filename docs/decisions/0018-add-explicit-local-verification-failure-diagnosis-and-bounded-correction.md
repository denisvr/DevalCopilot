# ADR-0018: Add explicit local verification failure diagnosis and bounded correction

Status: Accepted

## Context

[ADR-0010](0010-add-review-correction-response-contract.md) lets a Claude correction consume only the findings of a completed
`ChangesRequested` implementation review, and an ordinary code review requires every enabled verification command's latest
checkpoint-bound execution to be `Passed`. A human who runs the local verification and sees it fail therefore has no way to
turn that failure into findings, because the review that would produce them refuses the checkpoint, and had to transfer the
logs to a provider by hand. The gap is in the workflow, not in the gates: the review gate is correct and stays as it is.

## Decision

**Narrow supersession.** ADR-0018 supersedes only the part of ADR-0010 that makes a changes-requested implementation review the
sole eligible source of correction findings. ADR-0010's historical text is unchanged; its correction inputs, reply identities,
revision-response cardinality, budgets, extra-correction authorization, and re-review requirements all stand, as do
[ADR-0016](0016-add-explicit-human-authorization-of-one-escalated-plan-implementation.md) and
[ADR-0017](0017-review-the-implemented-plan-through-correction.md). The distinction between the actual implemented plan and the
Planner root is preserved: a diagnosis and its corrections resolve the true `ImplementedPlan` through the validated chain.

**A new, closed diagnosis contract.** `AgentResponseContract.VerificationDiagnosis` belongs to the existing
`AgentRole.CodeReviewer`, is `ReadOnly`, and is currently assigned to Codex with its own fixed, versioned adapter and output
schema (`codex-verification-diagnosis-v1`). It reuses the hardened `CodexProcessInvoker` unchanged: no CLI permission, tool,
flag, authentication, or session behavior changes. A valid response is either one to ten `ReviewFinding` messages or exactly one
`Escalation`, each replying to the exact `ExecutionReport`. There is no approval shape: a diagnosis never produces a
`ReviewApproval` and never an approved (or any) `CheckpointReview`. Enum members are appended (`DiagnosisFindingsRecorded`,
`DiagnosisEscalated`, `InputAlreadyDiagnosed`, `VerificationEvidenceChanged`); no existing value is repurposed and the
ordinary review's outcomes, input identity, and feeds never match a diagnosis.

**Protected operations.** A protected diagnosis `POST` names the run and the report; the host derives the complete current
verification selection. A separate protected correction `POST` names the diagnosis attempt and claims the existing
`ReviewCorrection` contract and hardened Claude adapter, with inputs exactly the previous report followed by every finding in
timeline order. The ordinary review-correction endpoint keeps its review-source contract. Every stage is explicitly requested;
nothing runs verification, diagnosis, correction, or re-review automatically.

**Eligibility.** A diagnosis requires a valid current initial or corrected `ExecutionReport` on its exact latest owned
checkpoint, a Ready workspace, and an active lease. Every enabled command must have a latest execution bound to that checkpoint
and fingerprint that is terminal and coherent: `Passed`/`Exited`/0 or `Failed`/`Exited`/nonzero, with at least one failure.
Missing or running evidence, timeout, cancellation, interruption, source drift, process-start failure, contradictory
status/outcome/exit data, and malformed ownership are refused; a failed execution must also have both sealed output rows.
Environment, credential, permission, or out-of-plan problems are escalated to a human and never grant authority to change
recipes, tools, permissions, or plan scope.

**Sealed evidence.** The claim seals the implemented plan, the report, bounded Git evidence, the complete verification metadata,
and verified redacted failure-output excerpts read through the existing artifact store's containment, byte-length, and hash
verification. Excerpts are deterministic prefixes of at most 2 KiB UTF-8 per failed stream and 12 KiB in total inside the existing
32 KiB manifest bound, with captured truncation, local shortening, empty streams, and budget omissions labeled separately. A
missing or unverifiable failed stream refuses the claim; it is never an invented empty log. Executable paths, arguments, storage
paths, and hashes are never injected. Fixed instructions precede the untrusted-evidence boundary; the existing redaction
limitation is explicit and no arbitrary secret detection is promised. A diagnosis-origin correction gets a fixed source notice,
not raw logs and not a second plan input.

**Fresh authority.** The report and the complete ordered verification membership (relational `AttemptVerificationEvidence` rows
beside the sequence-zero input) are pinned at claim. Each diagnosis membership row also carries a nullable, diagnosis-specific snapshot SHA-256 (a
versioned, deterministic digest of the canonical command metadata, complete execution facts and ownership, and both failed
output rows' identity, path, length, hash, truncation, and capture outcome); ordinary review memberships keep it null and are
unchanged, and a diagnosis membership without a valid digest fails closed. The relational rows remain the only membership
authority and the digest is a per-row integrity fact, never a second membership store or a generic snapshot engine; neither it
nor the facts it covers are sent to a provider. The digest is recomputed and compared at the claim seam, at dispatch, before the
result is recorded, and wherever a diagnosis confers correction authority, so a changed hash, length, path, capture flag, or
coherent execution or command metadata is refused even when every identifier is unchanged. Authority is re-read untracked
inside the short claim transaction after external work, again at dispatch, and again before the result is recorded. A semantic
result's own clean-exit process proof is validated first, so drift can only downgrade a result that was acceptable as requested. Active-attempt, duplicate, durability-ambiguity, and
orphan-manifest protections are those of the ordinary claim. Eligibility drift before dispatch ends the attempt truthfully
without a provider invocation. A stale response, whose verification evidence changed while the provider ran, records the closed
outcome `VerificationEvidenceChanged` with the truthful process and artifact evidence and no finding or escalation; Git
fingerprint drift remains `SourceChanged`. One successful diagnosis (findings or escalation) is permitted per exact
report/checkpoint/verification identity; a failed invocation may be requested again explicitly. There is no automatic retry and
no diagnosis format repair.

**Budgets and exhaustion.** A diagnosis consumes the existing Agent count, reserved invocation time, and Codex token-stop
budgets, with the code-review timeout, profile, and current requested model and effort. A diagnosis-origin correction consumes
the same run-wide budgets, the Claude token stop, the current Claude model, effort, and turn-limit requests, and the one shared
`ReviewCorrection` allowance; it receives no separate or replenished allowance. At exhaustion one durable, idempotent
Orchestrator escalation bound to the diagnosis is recorded in a narrow record (`DiagnosisCorrectionEscalation`, uniquely bound to
the diagnosis attempt), with no attempt and no grant consumed. No extra-correction authorization exists for this source, and the
ordinary review's grants cannot be transferred to it. The correction claim and the exhaustion escalation each decide their
authority (lifecycle, workspace, lease, checkpoint, the exact diagnosis with its findings and verification snapshot, the run-wide
gates, and the shared allowance) untracked inside one short transaction that holds the write lock until commit, so drift
committed before the transaction begins consumes nothing and records no stale escalation. The run's correction allowance is
immutable, and the escalation text offers manual review and an explicit human decision, not a supported continuation.

**Correction and re-review.** A correction requires the exact diagnosis and its complete coherent findings to remain applicable
with unchanged verification membership; a later execution, a new checkpoint, or a successful correction invalidates the source.
Revision-response cardinality, correction report and root reply identities, independent HEAD/path/fingerprint validation, and the
immutable resulting checkpoint are unchanged. The ordinary code review of the corrected report judges the `ImplementedPlan` and
still requires every enabled command to be `Passed` for the new checkpoint: the approval gate is not relaxed.

**Forward only.** Nothing historical is rewritten: no message, outcome, artifact, hash, or sealed manifest. Attempts, reviews,
and manifests sealed before this decision replay their existing bytes.

## Consequences

A human can move from a failed local verification to findings and a bounded Claude correction without moving logs between
providers, and the corrected checkpoint still earns approval only through new passing verification and an ordinary review. The
cost is a second read-only Codex path and a second source of correction findings to keep coherent; the shared allowance keeps
the total number of corrections bounded. Process doubles prove contract and sealed-form agreement, not provider reliability, and
the existing best-effort redaction of captured output remains a stated limit. Session persistence, resume, compaction, account
allowance, new providers, recipe mutation, ambiguous-process recovery, and lifecycle completion remain out of scope.
