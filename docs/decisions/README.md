# Architecture Decision Records

Architecture Decision Records preserve why material DevalCopilot choices were
made. Accepted records are historical; reverse one by adding a new ADR that
supersedes it.

## Accepted decisions

ADR-0001 (partially superseded by ADR-0006) is listed under
[Partially superseded decisions](#partially-superseded-decisions) and ADR-0050
under [Proposed decisions](#proposed-decisions).

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

- [ADR-0029: Deliver an explicit local commit before closing provider contract gaps](0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md)
  (it narrowly supersedes ADR-0009's increment ordering and extends ADR-0008's expected-HEAD reconciliation for exact recorded host
  commits; provider requirements and other authority rules remain intact)
- [ADR-0030: Approve the complete verification set as one Human decision](0030-approve-the-complete-verification-set-as-one-human-decision.md)
  (additive; an optional Human execution-set form of the existing review, a read-only approval bundle and one explicit action; it
  changes no ADR-0029 authority, reservation, recovery or mixed-decision rule)
- [ADR-0031: Abandon an inactive manual run through an explicit Human decision](0031-abandon-an-inactive-manual-run-through-an-explicit-human-decision.md)
  (it narrowly extends ADR-0014's intake admission so a coherent Abandoned run is terminal and adds one explicit Human terminal
  lifecycle; it cancels, repairs and deletes nothing and changes no claim, reservation, recovery or provider decision)
- [ADR-0032: Show a recorded local-delivery receipt from pinned evidence](0032-show-a-recorded-local-delivery-receipt-from-pinned-evidence.md)
  (additive; one protected read-only receipt reconstructed from the exact rows a completed local commit was admitted against; it
  adds no persistence and changes no ADR-0029 authority, reservation, recovery or approval decision)
- [ADR-0033: Browse project run history and locate prior delivery receipts](0033-browse-project-run-history-and-locate-prior-delivery-receipts.md)
  (additive; one protected read-only, cursor-paged list of a project's recorded runs, each optionally locating its own completed local
  delivery for the existing ADR-0032 receipt; it adds no persistence and changes no admission, recovery, approval or digest decision)
- [ADR-0034: Browse the recorded event headers of a historical run](0034-browse-recorded-event-headers-of-a-historical-run.md)
  (additive; one protected read-only, cursor-paged list of a run's persisted event headers with exact string sequences and closed
  disclosures, no payload and no replay claim; it adds no persistence and changes no admission, recovery, approval or digest decision)
- [ADR-0035: Browse the recorded collaboration messages of a historical run](0035-browse-recorded-collaboration-messages-of-a-historical-run.md)
  (additive; one protected read-only, cursor-paged list of a run's persisted collaboration messages with exact string sequences, closed
  disclosures and content admitted only through the existing content policy, unavailable rows keeping their header; it adds no
  persistence and changes no admission, recovery, approval or digest decision)
- [ADR-0036: Inspect the recorded reply parent of a historical collaboration message](0036-inspect-the-recorded-reply-parent-of-a-historical-collaboration-message.md)
  (additive; one protected read-only inspection of one message's own stored reply parent, resolved by exact identity within the run, with a
  closed None/Unavailable/Available relationship and the unchanged ADR-0035 entry disclosure; it adds no persistence, traverses no chain
  and changes no admission, recovery, approval or digest decision)
- [ADR-0037: Inspect the recorded Agent-attempt attribution of a historical collaboration message](0037-inspect-the-recorded-agent-attempt-attribution-of-a-historical-collaboration-message.md)
  (additive; one protected read-only inspection of the one Agent attempt a message itself references, as a minimal recorded header under the
  unchanged attempt identity and assignment-snapshot rules, read through bounded scalar projections with a closed NoAgentAttempt, Unavailable or
  Available status; it adds no persistence and changes no admission, claim, dispatch, recovery, approval or digest decision)
- [ADR-0038: Observe stored GitHub CLI authentication on explicit request](0038-observe-stored-github-cli-authentication-on-explicit-request.md)
  (additive; one protected read-only observation of the default stored GitHub CLI authentication of github.com through one fixed privacy-filtered
  command, with a closed status, no credential, environment-token or custom-config observation, no persistence and no repository or publication authority)
- [ADR-0039: Observe the project's local GitHub origin on explicit request](0039-observe-the-project-local-github-origin-on-explicit-request.md)
  (additive; one protected read-only observation of the registered project's own repository-local origin URL through two fixed local Git invocations,
  with a closed status and a normalized github.com target for a small supported subset, no remote, network, credential or global/included/worktree
  observation, no persistence and no remote identity, permission, CI or publication authority)
- [ADR-0040: Record an immutable GitHub observation scope for a completed local delivery](0040-record-an-immutable-github-observation-scope-for-a-completed-local-delivery.md)
  (additive; one protected Human selection of the host-fixed github.com owner and repository for the exact recorded commit and branch of a completed
  manual local delivery, one immutable record and one Human event saved atomically from pinned rows, and a protected read of it; local scope selection
  only, with no remote, process, credential or network operation and no remote identity, permission, CI, publication or automatic observation authority)
- [ADR-0041: Observe the GitHub repository identity of a recorded observation target on explicit request](0041-observe-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md)
  (additive; one protected, bodyless, read-only observation of the opaque ID and names that GitHub reports for the recorded ADR-0040 owner and repository,
  through one fixed read-only `gh api graphql` invocation and the stored default authentication; no remote branch, commit, permission, CI result, publication
  approval, persistence or permanent binding)
- [ADR-0042: Observe the recorded GitHub branch tip of a recorded observation target on explicit request](0042-observe-the-recorded-github-branch-tip-of-a-recorded-observation-target-on-explicit-request.md)
  (additive; one protected, bodyless, read-only observation, in one remote response, of the repository identity and the exact recorded branch tip through one fixed
  `gh api graphql` invocation and the stored default authentication, compared with the recorded local commit; never a fallback ref, durable binding, CI result,
  permission or publication approval)
- [ADR-0043: Bind the GitHub repository identity of a recorded observation target on explicit request](0043-bind-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md)
  (additive; one protected POST and bodyless GET of an immutable, human-confirmed binding of the opaque repository ID to a recorded ADR-0040 target, recorded only after
  a fresh host observation agrees, with one Human event; historical identity evidence only, never permission, branch or commit proof, CI approval or publication authority)
- [ADR-0044: Observe the check summary of a bound recorded local commit on explicit request](0044-observe-the-check-summary-of-a-bound-recorded-local-commit-on-explicit-request.md)
  (additive; one protected, bodyless, read-only observation, in one remote response, of GitHub's point-in-time aggregate check state for the exact recorded commit, shown only
  when the freshly observed repository ID equals the immutable ADR-0043 binding and the exact recorded branch points at that commit, through one fixed `gh api graphql`
  invocation and the stored default authentication; never complete check or workflow evidence, required-check approval, permission, persistence or publication authority)
- [ADR-0045: Inspect the check contexts of a bound recorded local commit on explicit request](0045-inspect-the-check-contexts-of-a-bound-recorded-local-commit-on-explicit-request.md)
  (additive; one protected, bodyless, read-only observation of the bounded collection (at most 50) of check-run and commit-status metadata GitHub returns for the exact recorded
  commit, shown only when the freshly observed repository ID equals the immutable ADR-0043 binding and the exact recorded branch points at that commit, through one fixed
  `gh api graphql` invocation and the stored default authentication; never complete check or workflow evidence, required-check approval, permission, persistence or publication
  authority)
- [ADR-0046: Prepare delivery-lineage API tests from a class-owned sealed SQLite snapshot](0046-prepare-delivery-lineage-api-tests-from-a-class-owned-sealed-sqlite-snapshot.md)
  (additive test infrastructure for one endpoint class only; one run-local seed of the unchanged production lineage sealed with SQLite's backup facility and copied per fact, with
  isolation evidence; no production, contract, client or dependency change)
- [ADR-0047: Inspect the output of one GitHub check run of a bound recorded local commit on explicit request](0047-inspect-the-output-of-one-github-check-run-of-a-bound-recorded-local-commit-on-explicit-request.md)
  (additive; one protected, bodyless, read-only inspection of the title, summary and text of one check run, shown only after a first fixed `gh api graphql` phase proved that the
  check belongs to the bound repository and recorded commit and a second fixed phase repeated that proof; untrusted, post-redaction plain text, never persisted, interpreted or sent
  to an Agent, and never complete check or workflow evidence, a diagnosis, required-check approval, permission or publication authority; its one extension of ADR-0046 is a
  feature-owned fixture instance for its own endpoint class)
- [ADR-0048: Approve one future GitHub branch push of a bound recorded local commit on explicit request](0048-approve-one-future-github-branch-push-of-a-bound-recorded-local-commit-on-explicit-request.md)
  (additive; one protected POST and bodyless GET of an immutable, Human-recorded consent to ONE future normal push of the exact recorded local commit to its exact owned branch in the
  repository named by a coherent ADR-0040 target and ADR-0043 binding, with complete exact snapshots and one Human event saved atomically from pinned rows under the write lock; recorded
  consent only, never readiness, a push, a pull request, a permission or any remote observation, and a future executor needs its own request and fresh checks)
- [ADR-0049: Observe the GitHub repository permission of a bound recorded target on explicit request](0049-observe-the-github-repository-permission-of-a-bound-recorded-target-on-explicit-request.md)
  (accepted, additive; one protected, bodyless, read-only observation of the repository permission GitHub reports (`viewerPermission`: one of six exact literals or an explicit null) for the
  stored default authentication on the already-bound recorded repository, through one fixed `gh api graphql` invocation, shown only when the freshly observed repository ID equals the
  ADR-0043 binding; ephemeral reported access, never permission to push the branch, readiness, an approval or a publication, and it neither requires nor changes an ADR-0048 approval)

- [ADR-0051: Inspect recorded inputs of a selected Agent attempt](0051-inspect-recorded-inputs-of-a-selected-agent-attempt.md)
  (accepted, implemented in HIST-INP-01; one protected, bounded inspection of the selected attempt's recorded collaboration-input references, independent of a producing message; ADR-0053 extends the prefix limit and adds explicit member-content inspection, retaining the default-ten behavior; no persistence, invocation, replay, autonomy or publication authority)

- [ADR-0052: Inspect recorded verification evidence of a selected Agent attempt](0052-inspect-recorded-verification-evidence-of-a-selected-agent-attempt.md)
  (accepted, implemented in HIST-VER-01; bounded historical verification membership and manually requested sealed stdout/stderr windows of a selected review or diagnosis attempt, with no current eligibility, snapshot revalidation, mutation, invocation or publication authority; publication is reconciled through the workflow)

- [ADR-0053: Inspect the content of recorded inputs of a selected Agent attempt](0053-inspect-the-content-of-recorded-inputs-of-a-selected-agent-attempt.md)
  (accepted, implemented in HIST-INPC-01; optional reference prefix limit up to the existing 64-input inspection bound with default ten unchanged, complete bounded reference disclosure and one explicitly requested member's guarded collaboration content after whole-set revalidation; no prompt/session attestation, chain traversal, invocation, mutation or publication authority)

- [ADR-0054: Inspect recorded outputs of a selected Agent attempt](0054-inspect-recorded-outputs-of-a-selected-agent-attempt.md)
  (accepted, implementation delivered for review in HIST-OUT-01; complete bounded references and explicit member-content inspection of messages whose own AttemptId attributes them to the selected Agent attempt, with whole-set relationship proof, unchanged historical content admission and exact wire values; no event-derived attribution, full response/replay claim, invocation, mutation or publication authority; publication is reconciled through the workflow)

## Partially superseded decisions

- [ADR-0001: Build a local-first supervised orchestrator](0001-build-a-local-first-supervised-orchestrator.md)

## Proposed decisions

- [ADR-0050: Define the first approved GitHub branch publication execution contract](0050-define-the-first-approved-github-branch-publication-execution-contract.md)
  (proposed, documentation only, not accepted; a design-only contract for one future create-only push of the exact ADR-0048-approved commit to its exact owned branch, restricted to an already
  installed, explicitly enabled, noninteractive Git Credential Manager and a commit whose single parent equals the freshly admitted remote default-branch tip, with per-attempt isolated transport
  scratch (a proposed narrow ADR-0008 execution-location extension), an independent durable publication fence (a proposed ADR-0006/0008 admission and reconciliation compatibility extension), a
  request that only records intent and a single-use may-have-started claim by the supervisor of the original live host session, Windows child-tree containment, an unproven source-write-denial
  requirement, and observations that never conclude causality; it activates nothing, authorizes no real call, credential access, installation, login, commit or push, accepts only the owner's design-time documentation of the residual
  name-to-ID risk, and leaves Git and GitHub compatibility qualification separate and unstarted)

## Status values

- `Proposed`
- `Accepted`
- `Partially superseded by ADR-NNNN`
- `Superseded by ADR-NNNN`
- `Rejected`
