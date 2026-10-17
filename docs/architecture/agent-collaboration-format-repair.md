# Agent collaboration format repair

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds the manual read-only format repairs, ADR-0004's optional bounded format
repair: one human-requested, fresh, schema-constrained attempt after a Codex
Planner, Claude Code CriticalReviewer, Codex Resolver or Codex CodeReviewer
response fails structural validation. The sections below are moved verbatim
except one link that now points back to the protocol; the protocol's original
headings forward here. The second section's references to the Planner's repair
mean the preceding section. Structured output failure stays in the protocol;
the base review-correction contract is in the
[correction and budgets](agent-collaboration-correction-and-budgets.md)
companion, and the challenge rounds and escalation are in the
[guidance and escalation](agent-collaboration-guidance-and-escalation.md)
companion. Mapping:
[protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## One manual Codex Planner format repair

A human may request **one** repair of a Codex Planner attempt whose recorded outcome is exactly
`InvalidStructuredOutput` (a clean-exit provider response that failed the Proposal contract's
validation; the attempt's status is `Failed` and its sealed response stays inspectable through
[Agent-attempt history and evidence inspection](agent-collaboration-protocol.md#agent-attempt-history-and-evidence-inspection)). This is
ADR-0004's optional bounded format repair, implemented for the Planner/Proposal contract only. It is a
**fresh, schema-constrained Planner invocation**, never a claim that the original response's meaning was
preserved, transformed, or corrected, and it is a DevalCopilot-owned retry contract, not evidence of model,
account-allowance, session, or invocation eligibility.

- **Operation.** `POST /api/runs/{runId}/agent-attempts/{sourceAttemptId}/codex-plan-repair` (protected, no
  request body, no free text). The ordinary `POST …/agent-attempts/codex-plan` request stays available under
  its own rules. Both send `CreateCodexPlanningAttemptCommand`; the repair carries `RepairSourceAttemptId`, so
  the repair is the same handler and therefore has the same protections.
- **Eligibility (server-owned).** The source must belong to the run (an unknown id and another run's id are the
  same 404 `agent_attempts.repair_source_not_found`), be a dispatched Codex Planner/Proposal Agent attempt with status `Failed` and outcome
  `InvalidStructuredOutput` (the only coherent pair; a persisted `Completed` row with that outcome is refused;
  `Attempt.IsEligiblePlanningRepairSource`, else 409
  `agent_attempts.repair_source_ineligible`), have no `AgentRepairSourceAttemptId` itself (a repair is never
  repaired: `agent_attempts.repair_of_repair_forbidden`), have no existing repair
  (`agent_attempts.repair_already_requested`), and be the run's **latest Agent attempt**
  (`agent_attempts.repair_source_not_latest`; a still-running newer Agent attempt is a newer attempt), and have
  been made against exactly the workspace, checkpoint, and fingerprint the repair claim selects
  (`agent_attempts.repair_source_checkpoint_mismatch`: a newer valid checkpoint is a different context, so use an
  ordinary request; the source identity is immutable, so this is checked at the request and again at the durable
  claim boundary against the same selected values). The
  cockpit may suggest the action but never decides eligibility.
- **Claim.** All ordinary planning-claim gates apply unchanged and in the same order: active run, ready
  workspace, active lease, current checkpoint, no other Running attempt of any kind, the count budget
  (ADR-0012) and reserved-time budget (ADR-0013), an observed Codex capability, and a fresh Git fingerprint
  equal to the checkpoint's. The repair takes the next Agent budget slot and attempt number and snapshots the
  Run's current Codex model/effort exactly like an ordinary claim. Source eligibility is checked at the request
  and **again inside the claim's own database transaction**, immediately after the guard UPDATE of the
  preference check (a write that takes SQLite's write lock), so no other claim can commit between the re-check
  and the insert. A source that stopped being eligible, or a competing repair, rolls the claim back and deletes
  the already-sealed manifest, exactly like a changed preference; the same holds for every other failure path of
  the ordinary claim.
- **Persistence.** The additive `AddCodexPlanningRepairLink` migration adds a nullable
  `attempts.AgentRepairSourceAttemptId` (no default, no backfill), a self-referencing foreign key with
  `NO ACTION` delete behavior (deleting a referenced source on its own is refused, while the existing
  Attempt-to-Run cascade still deletes a run with its source and repair together; SQLite checks it at
  statement end, and an integration test proves both), and the filtered unique index `ix_attempts_agent_repair_source`
  (`WHERE AgentRepairSourceAttemptId IS NOT NULL`), the database backstop for at most one repair per source. If
  that index ever rejects a claim, the handler reports `agent_attempts.repair_already_requested`. The link is
  set only by `Attempt.ClaimAgentPlanningRepair` and is immutable. Repair chains are excluded by the
  handler and by the Domain eligibility rule, not by a database constraint. The new attempt is otherwise an
  ordinary read-only Planner attempt (provider Codex, contract Proposal, `ReadOnly` profile, adapter
  `codex-planning-v1`).
- **Manifest.** The sealed context manifest (`AgentContextManifest`, host-constructed) is the ordinary planning
  manifest for the current verified context — the run objective, project/workspace/checkpoint identities and
  fingerprint, the unchanged expected Proposal schema, and the project instruction context of ADR-0021 captured fresh
  at the repair's own claim, with no human instruction and no prior decisions, exactly as an ordinary claim — plus one fixed host-authored
  `formatRepairNotice` (`ContextManifestBuilder.FormatRepairNotice`): an earlier planning response failed
  structural validation, this is a fresh planning request, and exactly one Proposal satisfying the unchanged
  schema is required. It never contains the source's raw response, parser or validation detail, artifact path,
  attempt identity or outcome, or any human-supplied text.
- **Dispatch and result.** Nothing downstream changed: the supervisor picks the repair up as an ordinary
  Planner attempt (including replay after a restart while it is still `Running` and undispatched), the single
  read-only adapter dispatch and its arguments, timeout, capture limits, parser, result recording, and the
  one-Proposal ledger rule are reused. The repair yields either one ordinarily validated Proposal or its own
  truthful outcome (including `InvalidStructuredOutput` again, after which nothing more can be repaired). A
  dispatched repair that a restart interrupts is reconciled `Interrupted`, is never re-invoked, and consumes the
  source's one repair. The source attempt and its evidence are never modified.
- **Read model.** The Planner status (`GET …/agent-attempts/codex-plan`) adds `repairSourceAttemptId` and
  `repairSourceAttemptNumber` to the latest Planner attempt — lineage only, null for an ordinary attempt. No
  response of either operation carries the source's response, a path, a hash, or a diagnostic.
- **Limits.** The retry consumes a real Agent budget slot and reserved invocation time and can fail like any
  claim. It is not an automatic retry, provider-session resume, schema relaxation, or a way to bypass a budget.

## One manual format repair of the remaining read-only stages

The same accepted, bounded recovery (ADR-0004) exists for the three other read-only collaboration stages:
the Claude Code **CriticalReviewer** (`CriticalReview` contract), the Codex **Resolver** (`ChallengeResolution`), and
the Codex **CodeReviewer** (`ImplementationReview`). A human may request **one** repair of such an attempt whose
recorded outcome is exactly `InvalidStructuredOutput`. Like the Planner's, it is a **fresh, schema-constrained
invocation of the same read-only role** with the ordinarily validated durable context; it neither transforms nor
preserves the meaning of the failed response, replays none of it, and is not evidence of model, account-allowance,
session, or invocation eligibility. The Planner's repair is unchanged. A mutating Implementer or ReviewCorrection
attempt has no repair.

- **Operations.** Three protected, bodyless `POST` operations (no request body, target, prompt, provider, model,
  or free text; a body sent anyway changes nothing):
  `/api/runs/{runId}/agent-attempts/{sourceAttemptId}/critical-review-repair`,
  `…/challenge-resolution-repair`, and `…/code-review-repair`. Each sends one command through
  `IApplicationMediator` — `CreateClaudeCriticalReviewAttemptCommand`, `CreateChallengeResolutionAttemptCommand`,
  or `CreateCodeReviewAttemptCommand`, built by its `ForRepair(runId, sourceAttemptId)` — and the role's existing
  claim handler derives the target from the source's persisted inputs. There is no second orchestration layer and
  no generic repair framework; each role keeps its own validation, manifest builder, schema, result handler, and
  supervisor. The response is `{ attemptId, attemptNumber, repairSourceAttemptId }` and nothing from the source.
- **Source eligibility (server-owned).** The source must belong to the run (an unknown id and another run's id are
  the same 404 `agent_attempts.repair_source_not_found`, message "The selected attempt was not found for this run.")
  and satisfy `ReadOnlyFormatRepairPolicy.IsEligibleSource` for the path's own response contract: an Agent attempt
  with status `Failed` and outcome exactly `InvalidStructuredOutput`, dispatched and concluded, with
  host-measured process evidence proving a clean exit, the path's exact provider, role, response contract,
  expected message type, protocol `1.0`, `ReadOnly` permission profile, and current known v1 adapter contract
  (`claude-critical-review-v1`, `codex-challenge-resolution-v1`, `codex-implementation-review-v1`), and a
  well-formed assignment snapshot; otherwise 409 `agent_attempts.repair_source_ineligible`. It must carry no
  `AgentRepairSourceAttemptId` itself (`agent_attempts.repair_of_repair_forbidden`), have produced **no durable
  collaboration message** (a source that recorded a semantic result did not fail structurally), have no existing
  repair (`agent_attempts.repair_already_requested`), be the run's **latest Agent attempt**
  (`agent_attempts.repair_source_not_latest`), and — with the workspace context known — have been made against
  exactly the selected workspace, checkpoint, and fingerprint (`agent_attempts.repair_source_checkpoint_mismatch`).
  A stored value that cannot be materialized (an unreadable enum, assignment, process, or input row) fails
  closed as `agent_attempts.repair_source_ineligible` with a fixed message that echoes no stored value; only
  `InvalidOperationException` from materializing the source means "unreadable" — database and cancellation
  failures propagate and are never reported as invalid source evidence.
- **Exact retained context.** The repair retains exactly the source's recorded inputs, validated again by the
  ordinary role gates rather than trusted from the source rows:
  - *CriticalReviewer:* the source's one `Proposal` input (sequence 0), the root Proposal or the first Resolver
    revision, through the same bounded-lineage rule as an ordinary review.
  - *Resolver:* the original Proposal and the complete ordered Challenge set. The challenged review is derived from
    the first Challenge's owning attempt, validated as an ordinary request would be (its own provider-observed
    Challenges replying to the Proposal, bounded lineage), and the Proposal plus its ordered Challenges must equal the
    source's recorded ordered inputs — another review, or a merely overlapping, partial, reordered, or extended
    Challenge set, is `agent_attempts.repair_source_inputs_mismatch` (409); a missing, gapped, or otherwise
    malformed recorded input set is `agent_attempts.repair_source_ineligible`.
  - *CodeReviewer:* the ExecutionReport (initial implementation or successfully applied correction report) and the
    ordered (verification command, verification execution) pairs. The report chain is validated as for an
    ordinary review, and the *currently enabled and latest `Passed`* verification selection must still equal the
    source's recorded set: a rerun, an enabled-command change, a reorder, or a replacement is an ordinary new
    review (`agent_attempts.repair_source_inputs_mismatch`), never substituted into the repair. The ordinary
    verification errors (`no_verification_commands_enabled`, `verification_evidence_missing`, `_running`, `_not_passed`)
    still apply.

  Recorded inputs are read from non-enum columns only, so a source with an unreadable enum column is refused rather
  than crashing, and the database's unique indexes make duplicate message or execution identities impossible.
- **Claim.** Every ordinary gate applies unchanged and in the same order: active run (`runs.not_active` for the
  CriticalReviewer, `runs.not_running` for the others), ready workspace, active lease, current checkpoint, no other
  Running attempt of any kind, the count budget (ADR-0012) and reserved-time budget (ADR-0013), the provider
  token stop, an observed provider capability, a fresh Git fingerprint equal to the checkpoint's, and a fresh
  model/effort snapshot. An invalid repair source is refused **before** the provider probe, Git capture, or manifest
  sealing, and the source check precedes the running-attempt check so a request that lost the race to the source's one
  repair is told "already requested". The repair takes the next Agent budget slot and attempt number.
- **Atomic commit seam.** At the durable claim boundary the source, its exact inputs (with the report chain and
  verification selection for the CodeReviewer, the reviewed lineage for the CriticalReviewer and Resolver), and
  the execution context (run still active, workspace still ready, lease still active, selected checkpoint still the
  workspace's latest) are **re-read inside the same short database transaction, after a guard statement has run in
  it, before the linked Attempt, its inputs, verification rows, and artifact metadata are persisted and committed**;
  external Git and artifact work stay outside it. The Resolver and CodeReviewer already used a short claim
  transaction with the Codex preference and stop-policy compare-and-set statements as guards; the CriticalReviewer
  gains a bounded repair-only transaction (its ordinary request keeps the single concurrency-token save) whose guard is the
  stop-policy compare-and-set, followed by the Claude preference read. A late read alone is never the guarantee: on
  SQLite (Microsoft.Data.Sqlite's default serializable transaction) the write lock is taken when the transaction
  begins, so a competing writer cannot commit between those re-reads and the insert. Every authority read at this
  seam (owning attempts, messages, report chains, verification commands and executions, and the reviewed
  lineage, in all three repair paths) is **untracked**: the claim's own context already holds the earlier entity
  instances, and a tracked re-query would silently return them instead of a change committed just before the
  transaction began. Tracking is not disabled globally and the tracker holding the Run and the pending claim
  writes is never cleared. The ordinary request-time validators are unchanged; the repair paths opt into the
  untracked reads. The five repair helper types (`AgentRepairLineage`, `ReadOnlyFormatRepairInputs`,
  `ReadOnlyFormatRepairLink`, `ReadOnlyFormatRepairManifest`, `ReadOnlyFormatRepairSource`) live in the Application
  `Policies/FormatRepair` folder, not in the feature root; orchestration stays in the handlers. A source or input that
  changed refuses the claim with the same codes as the request-time check, rolls back completely, and deletes the
  already-sealed manifest. The global unique index `ix_attempts_agent_repair_source` is the at-most-one backstop
  (`agent_attempts.repair_already_requested` if it ever rejects a claim); no column, migration, default, or backfill
  was needed. Cancellation always propagates, never becoming a `Result`; a failed save or commit is resolved by the
  independent durability probe: persisted → success, not persisted → the manifest is deleted, unresolved → the
  manifest is preserved and `attempts.persistence_unresolved` is reported.
- **Manifest.** The sealed manifest is the ordinary role manifest for the current verified context plus one fixed,
  host-authored `formatRepairNotice` member, placed immediately before `untrustedEvidenceBoundary` so it stays in the
  trusted part of the document; the unchanged expected schema and the untrusted-evidence boundary remain. The
  notices are
  `ClaudeCriticalReviewContextManifestBuilder.FormatRepairNotice` (critical review),
  `ChallengeResolutionContextManifestBuilder.FormatRepairNotice` (challenge resolution), and
  `CodeReviewContextManifestBuilder.FormatRepairNotice` (both the initial and the correction-evidence form): an
  earlier response for this proposal, these challenges, or this implementation failed structural validation, this is a
  fresh request, and exactly one response satisfying the unchanged `expectedOutputSchema` is required. The source's
  raw output, parser or validation diagnostic, artifact path, attempt identity, outcome, and any human text are
  never included; the source identifier is durable provenance and not provider instruction. Restart replay uses the
  sealed manifest and the claimed assignment and never rebuilds from a later source or setting.
- **Dispatch.** `MarkAgentAttemptDispatched` protects the three repair paths against an incoherent link, source, or
  input identity (`agent_attempts.invalid_repair_link`, no provider process): the repair's own exact tuple, a distinct
  earlier same-run source that is still an eligible failed source, the same workspace, checkpoint, and fingerprint,
  no durable message from the source, and the repair's recorded inputs (and verification pairs) equal to the
  source's. It deliberately does not re-apply the claim-time "latest" and "no repair" tests, since the repair now owns
  that slot, and the ordinary duplicate-input classifications (`input_already_reviewed` and its counterparts) stay.
  A committed repair consumes the source's one repair even if it is interrupted or never dispatched.
- **Result.** The role's unchanged adapter, arguments, parser, and result handler record either the ordinary
  validated result (an Acceptance or Challenges; Decisions plus a revised Proposal; a review approval or
  findings) or the repair's own truthful outcome. An invalid repair records no semantic message and cannot be
  repaired again. A repair adds no challenge round, does not bypass the two-round planning limit or the exactly-once
  depth-two escalation (only the repair's valid result can record it), and grants no implementation authority,
  correction authorization, automatic follow-up, or inferred success.
- **Read model.** The CriticalReviewer, Resolver, and CodeReviewer status routes add `repairSourceAttemptId` and
  `repairSourceAttemptNumber` (present for a repair; the number is null only if no source attempt of the run is
  found). The Agent-attempt history entries and evidence add the same two fields for **every** repair, including
  the Planner's, and read null unless the link can be proved (an earlier Agent attempt of the same run, found
  through non-enum columns). All are provenance only — never a claim that the repair fixed or preserved the source.
- **Limits.** The repair consumes a real Agent budget slot and reserved invocation time and can fail like any claim.
  It is not an automatic retry, fallback, or debate, provider-session resume, schema relaxation, or a way around a
  budget, and it never touches Git or publication policy.
