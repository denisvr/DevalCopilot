# Agent collaboration local delivery

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds two contracts of the explicit local commit
([ADR-0029](../decisions/0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md)):
the complete-set Human approval that it requires and the recorded, read-only
local-delivery receipt of a completed local commit. The approval is an
authenticated Human decision about a checkpoint and its verification
evidence; the repository source evidence that Agent manifests and human
checkpoint inspection carry is in the
[source evidence](agent-collaboration-source-evidence.md) companion and is
never approval. Local delivery here is the explicit local commit: it is not
remote publication, and neither contract pushes or publishes anything. Remote
observation and branch publication approval are separate subjects of
[GitHub and CI integration](github-ci-integration.md), and the local commit's
recovery is in
[data and recovery](data-and-recovery.md#explicit-local-commit-recovery). The
sections below are moved verbatim and the protocol's original headings
forward here. Their statements, including the decisions, refusal code and
unchanged local-commit rules they name, are as written at the baseline; the
move reconciles or endorses none of them. Every other subject not moved to a
companion stays in the protocol. Mapping:
[protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## Complete-set Human approval for local delivery

The explicit local commit ([ADR-0029](../decisions/0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md))
needs a Human approval whose verification membership equals the CodeReviewer approval's and the complete set of enabled recipes.
[ADR-0030](../decisions/0030-approve-the-complete-verification-set-as-one-human-decision.md) lets the existing manual review operation
carry that membership. It is an authenticated Human decision about the checkpoint and its verification evidence only: it is not
provider output, never synthesizes an Agent approval, claims no attempt or budget and starts no stage.

- **Request.** The optional `verificationExecutionIds` array (at most 32 unique nonempty UUIDs; an 8 KiB request body) is the Human
  execution-set form. The scalar `verificationExecutionId` is the unchanged legacy form; two non-null forms are refused as ambiguous,
  never merged, deduplicated or truncated, a non-Human reviewer cannot use the set form, a decided review needs at least one member and
  `Pending` carries none.
- **Complete approval.** `Approved` requires exactly the latest, coherent clean `Passed` execution of every currently enabled recipe
  for the current checkpoint and fingerprint, with a matching current command snapshot; any subset, extra, disabled, repeated or stale
  member is refused (`reviews.approval_requires_complete_verification_set`). The members are saved with
  the review in one transaction after fresh untracked re-reads under the write lock; no Git or provider work happens inside it. They have
  no stored sequence: the host derives the canonical `CommandNumber` order (then execution number) when it presents them.
- **Bundle.** `GET .../checkpoints/{checkpointId}/approval-evidence` returns the source identities and fingerprint and, per enabled
  recipe in command order, its command and execution identities, recipe label and execution number, or a fixed refusal; it is
  derived from the recipes and their latest executions, not from the bounded execution history, and it authorizes nothing.
- **Unchanged.** Historical rows and sealed manifests, FutureAgent provenance, the mixed-Human-decision gate, the local commit's
  reservation, digest, seam and recovery rules and every other authority decision. A Pending, Changes-requested or Escalated Human
  decision still blocks local delivery.

## Recorded local-delivery receipt

A completed explicit local commit ([ADR-0029](../decisions/0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md))
has a protected, read-only historical receipt ([ADR-0032](../decisions/0032-show-a-recorded-local-delivery-receipt-from-pinned-evidence.md)),
`GET /api/runs/{runId}/local-delivery-receipt`, answering `NotRecorded`, `NotCompleted`, `Unavailable` or `Available` (only the last
carries the version-1 receipt). Every fact is resolved through the identifier the operation recorded: the pinned execution report, the
CodeReviewer attempt (launched against exactly that report, with its single provider-observed approval message and no finding), the
Agent and selected Human checkpoint reviews, and the operation's ordered Verification authority members. Those members must be 1 to
32 unique, contiguous pairs equal to the CodeReviewer's ordered evidence claim and, as sets, to both reviews' evidence; each execution
must belong to the recorded project, workspace, checkpoint and command, be `Passed` with a clean exit, and match its recorded member
digest recomputed from its own immutable command snapshot (the admission digest format, shared and unchanged); the selected Human
decision must match its recorded digest. The pinned run must record the same completion (lifecycle and stage `Completed`, last advance
equal to the operation's completion time), the recorded counts and numbers must be in bounds, the approval message must carry the
attempt's own provider, and both reviews' stored copies of each execution (number, status, outcome, exit code, fingerprint) must equal
the pinned execution. Any gap makes the whole receipt `Unavailable`. The query is read-only (no filesystem, Git,
process, provider, lease, event or write) and never substitutes a later report, review, checkpoint, recipe or execution, so reruns and
recipe edits after delivery leave an intact receipt unchanged. It does not infer the implemented plan from report replies and says
nothing about current workspace state or remote publication.
