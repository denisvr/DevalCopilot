# ADR-0030: Approve the complete verification set as one Human decision

Status: Accepted

## Context

[ADR-0029](0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md) delivers an explicit local commit only
when the current Human review evidence has exactly the same verification membership as the CodeReviewer's approval and the current
complete set of enabled recipes. The Domain `CheckpointReview` and its relational `CheckpointReviewEvidence` members already hold one to
several unique command and execution pairs, and every code-review claim already records all enabled recipes in `CommandNumber` order.
But the manual review request, its command and the cockpit panel could submit only one `verificationExecutionId`. With two or more
enabled recipes (the ordinary tests-plus-lint case) a Human approval could therefore never equal the required membership, and the
local commit was refused with `membership_mismatch`. Disabling a recipe or combining several separate Human decisions would have
weakened or changed the accepted gate, so neither is an answer.

## Decision

**An optional execution-set form of the existing review.** `POST /api/projects/{projectId}/reviews` accepts an optional
`verificationExecutionIds` array beside the unchanged scalar `verificationExecutionId`. The array has at most 32 entries, each a
nonempty, unique UUID, and the whole body is bounded to 8 KiB by the transport. A missing or null array selects the legacy form with
exactly its previous behavior. Two non-null evidence forms (a scalar and an array, even an empty one) are ambiguous and refused with
a fixed message: they are never merged, deduplicated or truncated, and an invalid UUID, a null element or a non-array value is a
model-binding refusal. The set form is for `Human` reviews only. A decided review (`Approved`, `ChangesRequested`, `Escalated`)
needs 1 to 32 identifiers; `Pending` carries no evidence and an empty array may represent only `Pending`. A request that is refused
persists no review and no member.

**Exact meaning of a complete approval.** `Approved` in the set form requires exactly the complete, currently enabled recipe set: for
every enabled recipe its single latest execution bound to the current workspace checkpoint and fingerprint, which is a coherent clean
`Passed` completion (the project, workspace and workspace path match, exit code zero, dispatched and completed, completion fingerprint
equal to the checkpoint's) whose command snapshot still equals the recipe. No subset, extra or disabled recipe, repeated recipe,
missing recipe, older execution standing in for a newer failed one, or truncation qualifies. The members are stored as relational
rows with no sequence column, and the database does not return them in insertion order, so no stored order exists. The host derives the
canonical presentation instead: the bundle query and the review-history projection order members by the recipe's `CommandNumber` (then
the execution number), never by the caller's array order, timestamps or identifier order, and the complete-set comparison is by
membership. No sequence column or migration is added. `ChangesRequested` and `Escalated` may cite a
bounded set of coherent terminal executions of the exact checkpoint under the existing terminal-evidence rules, at most one execution
per recipe. A legacy scalar `Approved` stays a checkpoint-bound single-member fact and gains no additional delivery authority.
`FutureAgent` provenance, provider-written review completion and the mixed-Human-decision gate of ADR-0029 are unchanged: a Pending,
Changes-requested or Escalated Human decision still blocks local delivery, and the local commit still pins one Human review whose
membership equals the Agent's.

**One review, one atomic transaction.** The existing manual transaction records one `CheckpointReview` and all of its members. Git is
observed outside the transaction, as before. After the write lock is acquired the source (workspace readiness, active lease and exact
current checkpoint), every selected execution, and, for a complete approval, the enabled recipes and their latest executions are read
again untracked; a changed selection is refused and never retargeted. A reservation of the workspace by an admitted local commit
already makes it not `Ready`, and the existing database guards of the review insert still apply. No migration, new persistence
representation or generic approval framework is added.

**A read-only approval bundle.** `GET /api/projects/{projectId}/checkpoints/{checkpointId}/approval-evidence` (query
`GetCheckpointApprovalEvidenceQuery`, protected like every operation) returns the complete eligible `Passed` bundle bound to its
source identities (project, workspace, checkpoint, checkpoint number and fingerprint) with each member's command identity, command
number, recipe label, execution identity and execution number in `CommandNumber` order, or a fixed refusal
(`approval_evidence.no_enabled_recipes`, `approval_evidence.too_many_recipes`, `approval_evidence.verification_incomplete`, or the
review source codes). It reads the enabled recipes and each recipe's latest execution directly, never the latest-20 history window; more
than 32 enabled recipes is refused explicitly, never returned partially. It observes the source once outside any transaction, re-reads
the source and set afterwards, writes nothing, claims nothing, invokes no provider and authorizes nothing: the review request decides
again from fresh facts.

**The explicit action.** The checkpoint-review surface adds "Approve all enabled checks" for a Human reviewer. It shows the exact
bundle it will submit and offers the action only for a settled, successful source read and bundle read that name the same project,
workspace, checkpoint, checkpoint number and fingerprint the source evidence admitted; a missing, malformed or mismatching identity
fact, pending, failed or inconsistent reads, a refresh in flight and a non-Human reviewer withhold it, and an identity is never
repaired or retargeted. Two lifetimes own the action. The reads belong to the committed source's lifetime (every admitted identity fact
is part of it; replacement, A-to-B-to-A, unmount and overlapping reads end or order them). The approval action, its pending guard, its
error, its continuation and its accepted result belong to the narrower lifetime of the committed bundle: replacing the members ends it,
and returning to an earlier member set begins a new one, so equal keys never revive an obsolete callback, while an equal refresh of the
same members keeps it. An obsolete completion writes no error onto, clears no guard of and refreshes nothing for its replacement, and
ownership is checked again before the deferred review-list refresh. A handler never submits a bundle it already had accepted, even
through a retained callback. An operation the host accepted stays real, is never reported as failed because the following read failed,
and is never retried automatically. The legacy decision buttons are preserved; the panel states that the single-run Approve does not
cover several enabled checks.

**Compatibility.** Historical rows, sealed manifests, the generated client's existing members and the legacy scalar contract are
unchanged. The generated TypeScript client gains the optional array and the new operation through the normal API build.

## Consequences

- A project with two or more enabled recipes can obtain one Human approval of the whole current verification set and then use the
  existing explicit local commit; the Human membership equals the Agent's exactly, so the local-commit admission, digest, seam and
  recovery rules are untouched.
- A rerun, a recipe change or any newer execution after the bundle was read makes the submitted set stale and the request is refused
  with `reviews.approval_requires_complete_verification_set`; the owner refreshes the evidence and approves the current set. Two
  approvals of identical sets are two Human facts and are not combined.
- Not decided here: remote publication, autonomy or scheduling, combining or superseding separate Human decisions, approving a
  subset, a Future agent set approval, more than 32 enabled recipes, any change to the Git adapter, reservation, recovery or lock
  behavior, and any ranking of recipe importance. Each needs its own decision.
