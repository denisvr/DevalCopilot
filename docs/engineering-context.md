# Engineering context

- Standards revision: `805439c3316ed64b0a56c933103156e9fb853a2b`
- Root namespace: `DevalCopilot`
- Topology: modular monolith with a thin desktop shell and one local host
- Security profile: S2, tailored for a privileged local developer tool
- Backend: .NET 10 and ASP.NET Core MVC
- Frontend: React and TypeScript hosted in Tauri 2
- Persistence: Entity Framework Core 10 with file-backed SQLite
- Generated clients: NSwag TypeScript for the MVC API
- Live transport: SignalR notifications with cursor-based durable replay
- Initial operating system: Windows-first, with cross-platform boundaries
  preserved where they do not compromise the MVP
- Deployment: local desktop only; no production service deployment is planned
  for the MVP

## Project decisions

- [ADR-0001](decisions/0001-build-a-local-first-supervised-orchestrator.md):
  Build a local-first supervised orchestrator.
- [ADR-0002](decisions/0002-use-tauri-react-and-dotnet.md): Use Tauri,
  React, and .NET 10 with one authoritative .NET host.
- [ADR-0003](decisions/0003-use-a-durable-sqlite-event-journal.md): Use a
  durable SQLite event journal with materialized state.
- [ADR-0004](decisions/0004-use-a-structured-agent-collaboration-protocol.md):
  Use a structured and bounded agent collaboration protocol.
- [ADR-0005](decisions/0005-use-git-worktrees-and-a-policy-controlled-github-loop.md):
  Use Git worktrees and a policy-controlled GitHub/CI loop.
- [ADR-0006](decisions/0006-support-bounded-concurrent-runs-across-projects.md):
  Support bounded concurrent runs across projects.
- [ADR-0007](decisions/0007-registration-time-repository-identity-is-non-authoritative-for-mutation-exclusion.md):
  Registration-time repository identity is non-authoritative for mutation
  exclusion.
- [ADR-0008](decisions/0008-physical-repository-identity-and-tool-owned-worktree-ownership.md):
  Physical repository identity and tool-owned worktree ownership.
- [ADR-0009](decisions/0009-separate-agent-roles-effects-and-provider-assignments.md):
  Separate agent roles, effects, and provider assignments.
- [ADR-0010](decisions/0010-add-review-correction-response-contract.md): Add a
  role-scoped review-correction response contract.
- [ADR-0011](decisions/0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md):
  Require administrator-provisioned policy before Gemini CLI execution.
- [ADR-0012](decisions/0012-add-a-durable-run-wide-agent-claim-budget.md): Add
  a durable run-wide Agent claim budget.
- [ADR-0013](decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md):
  Add a durable run-wide Agent invocation-time budget.
- [ADR-0014](decisions/0014-add-manual-agent-run-intake-with-durable-execution-mode-isolation.md):
  Add manual Agent run intake with durable execution-mode isolation.
- [ADR-0015](decisions/0015-add-direct-human-guidance-to-explicit-mutation-requests.md):
  Add direct human guidance to explicit mutation requests (its request scope is narrowly extended to the diagnosis-origin
  correction by ADR-0019).
- [ADR-0016](decisions/0016-add-explicit-human-authorization-of-one-escalated-plan-implementation.md):
  Add explicit human authorization of one escalated-plan implementation (its preservation of the ordinary
  first-revision review target is narrowly superseded by ADR-0017, and its single-source-text-form requirement by
  ADR-0020).
- [ADR-0017](decisions/0017-review-the-implemented-plan-through-correction.md):
  Review the implemented plan through correction.
- [ADR-0018](decisions/0018-add-explicit-local-verification-failure-diagnosis-and-bounded-correction.md):
  Add explicit local verification failure diagnosis and bounded correction (it narrowly extends ADR-0010's eligible finding source).
- [ADR-0019](decisions/0019-add-direct-human-guidance-to-diagnosis-origin-corrections.md):
  Add direct human guidance to diagnosis-origin corrections (it narrowly extends ADR-0015's request scope and ADR-0018's
  correction request).
- [ADR-0020](decisions/0020-correct-the-escalation-explanation-and-accept-its-two-canonical-forms.md):
  Correct the escalation explanation and accept its two canonical forms (it narrowly supersedes ADR-0016's
  single-source-text-form requirement).
- [ADR-0021](decisions/0021-add-bounded-root-instruction-context-to-agent-manifests.md):
  Add bounded root instruction context to Agent manifests (additive; it changes no existing authority decision).
- [ADR-0022](decisions/0022-admit-generic-untracked-previews-only-from-physically-proven-single-name-files.md):
  Admit generic untracked-file previews only from physically proven single-name files (additive; it narrows what those
  previews may deliver and changes no existing authority decision).
- [ADR-0023](decisions/0023-record-claude-reported-model-context-limits-in-historical-attempt-evidence.md):
  Record Claude-reported model context limits in historical attempt evidence (additive; it records and shows what Claude
  reported and changes no existing authority decision).
- [ADR-0024](decisions/0024-deliver-new-tracked-change-text-only-from-attested-snapshots.md):
  Deliver new tracked-change text only from attested snapshots (it narrowly advances ADR-0021 and ADR-0022 for new tracked
  delivery only and changes no existing authority decision).
- [ADR-0025](decisions/0025-stop-new-codex-attempts-at-an-explicit-account-usage-percentage.md):
  Stop new Codex attempts at an explicit account-usage percentage (additive; it adds an optional run-scoped guard
  for Codex claims and pre-dispatch and changes no existing authority decision).
- [ADR-0026](decisions/0026-warn-explicitly-about-a-codex-account-usage-percentage.md):
  Warn explicitly about a Codex account-usage percentage (additive; an optional run-scoped advisory percentage, checked only on
  explicit request, independent of the ADR-0025 stop, and it changes no existing authority decision).
- [ADR-0027](decisions/0027-compare-attested-tracked-sources-for-human-checkpoint-inspection.md):
  Compare attested tracked sources for human checkpoint inspection (it advances ADR-0024's deferred ordinary checkpoint diff
  query for the authenticated human inspection only and changes no existing authority decision).
- [ADR-0028](decisions/0028-let-the-owner-choose-immutable-run-budgets-at-manual-intake.md):
  Let the owner choose immutable run budgets at manual intake (it narrowly advances the fixed-default creation behavior of
  ADR-0012, ADR-0013 and ADR-0014 for new manual runs only and changes no claim, immutability, consumption or historical-policy
  decision).
- [ADR-0029](decisions/0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md): Deliver an explicit local
  commit before closing provider contract gaps (it narrowly supersedes ADR-0009's increment ordering and extends ADR-0008's
  expected-HEAD reconciliation for exact recorded host commits only).
- [ADR-0030](decisions/0030-approve-the-complete-verification-set-as-one-human-decision.md): Approve the complete verification set as
  one Human decision (additive; an optional Human execution-set form of the existing review plus a read-only approval bundle, with no
  change to ADR-0029's authority, reservation, recovery or mixed-decision rules).
- [ADR-0031](decisions/0031-abandon-an-inactive-manual-run-through-an-explicit-human-decision.md): Abandon an inactive manual run through
  an explicit Human decision (it narrowly extends ADR-0014's intake admission so a coherent Abandoned run is terminal; it cancels, repairs
  and deletes nothing and changes no claim, reservation, recovery or provider decision).
- [ADR-0032](decisions/0032-show-a-recorded-local-delivery-receipt-from-pinned-evidence.md): Show a recorded local-delivery receipt from
  pinned evidence (additive; one protected read-only receipt of a completed local commit, reconstructed from its pinned rows with no
  persistence change and no change to ADR-0029's authority, reservation, recovery or approval decisions).
- [ADR-0033](decisions/0033-browse-project-run-history-and-locate-prior-delivery-receipts.md): Browse project run history and locate prior
  delivery receipts (additive; one protected read-only, cursor-paged list of a project's recorded runs, each optionally locating its own
  completed local delivery for the ADR-0032 receipt, with no persistence change and no change to any admission, recovery, approval or
  digest decision).
- [ADR-0034](decisions/0034-browse-recorded-event-headers-of-a-historical-run.md): Browse the recorded event headers of a historical
  run (additive; one protected read-only, cursor-paged list of a run's persisted event headers with exact string sequences and closed
  disclosures, with no payload, no persistence change, no replay claim and no change to any admission, recovery, approval or digest
  decision).
- [ADR-0035](decisions/0035-browse-recorded-collaboration-messages-of-a-historical-run.md): Browse the recorded collaboration messages of a
  historical run (additive; one protected read-only, cursor-paged list of a run's persisted collaboration messages with exact string
  sequences, closed disclosures and content admitted only through the existing content policy, with no persistence change and no change to
  any admission, recovery, approval or digest decision).
- [ADR-0036](decisions/0036-inspect-the-recorded-reply-parent-of-a-historical-collaboration-message.md): Inspect the recorded reply parent of a
  historical collaboration message (additive; one protected read-only inspection of one message's own stored reply parent with a closed
  None, Unavailable or Available relationship and the unchanged ADR-0035 entry disclosure, with no persistence change, no chain traversal and
  no change to any admission, recovery, approval or digest decision).
- [ADR-0037](decisions/0037-inspect-the-recorded-agent-attempt-attribution-of-a-historical-collaboration-message.md): Inspect the recorded
  Agent-attempt attribution of a historical collaboration message (additive; one protected read-only inspection of the one Agent attempt a
  message itself references, as a minimal recorded header under the unchanged identity and assignment-snapshot rules, with no persistence change and
  no change to any admission, claim, dispatch, recovery, approval or digest decision).
- [ADR-0038](decisions/0038-observe-stored-github-cli-authentication-on-explicit-request.md): Observe stored GitHub CLI authentication on explicit
  request (additive; one protected read-only observation of the default stored github.com authentication, with no credential, environment-token or
  custom-config observation, no persistence and no repository or publication authority).
- [ADR-0039](decisions/0039-observe-the-project-local-github-origin-on-explicit-request.md): Observe the project's local GitHub origin on explicit
  request (additive; one protected read-only observation of the registered project's own repository-local origin URL, a normalized github.com target for a
  small supported subset, no remote, network or credential operation, no persistence and no remote identity, permission, CI or publication authority).
- [ADR-0040](decisions/0040-record-an-immutable-github-observation-scope-for-a-completed-local-delivery.md): Record an immutable GitHub observation scope for a
  completed local delivery (additive; one protected Human selection of the fixed-host owner and repository for the exact recorded commit and branch, one
  immutable record and one Human event saved atomically from pinned rows, no remote, process, credential or network operation and no remote identity,
  permission, CI, publication or automatic observation authority).
- [ADR-0041](decisions/0041-observe-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md): Observe the GitHub repository
  identity of a recorded observation target on explicit request (additive; one protected, bodyless, read-only observation of the opaque ID and names GitHub
  reports for the recorded owner and repository through one fixed `gh api graphql` invocation and the stored default authentication, no persistence and no
  remote branch, commit, permission, CI, publication or binding authority).
- [ADR-0042](decisions/0042-observe-the-recorded-github-branch-tip-of-a-recorded-observation-target-on-explicit-request.md): Observe the recorded GitHub branch tip
  of a recorded observation target on explicit request (additive; one protected, bodyless, read-only observation of the repository identity and the exact recorded
  branch tip, compared with the recorded local commit, through one fixed `gh api graphql` invocation and the stored default authentication, no persistence and no
  fallback ref, CI, permission, durable binding or publication authority).

## Product-specific architecture

- [ADR-0029](decisions/0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md) permits the selected explicit
  local-commit slice before Increment 4's unproven provider contracts close. It requires exact physically admitted snapshot bytes,
  current passing verification, Agent and human approval, exclusive durable intent, hook-free unsigned Git execution and exact
  restart reconciliation. The slice is delivered and published to `origin/main` (see `docs/roadmap/current-work.md` for its
  evidence and limits); Increment 4 remains incomplete. Only a recorded host commit may extend the expected worktree HEAD, and
  remote publication and automatic orchestration remain separately gated.
- [ADR-0030](decisions/0030-approve-the-complete-verification-set-as-one-human-decision.md) makes that local commit usable with
  several enabled verification recipes. The existing protected manual review accepts an optional Human execution-set form
  (1 to 32 unique identifiers, an 8 KiB body, never merged with the legacy scalar); `Approved` in that form means exactly one latest,
  coherent clean `Passed` execution for every currently enabled recipe of the current checkpoint and fingerprint, saved as one review
  with its members in `CommandNumber` order inside the existing manual transaction after fresh untracked re-reads under the write
  lock. A protected read-only `GET /api/projects/{projectId}/checkpoints/{checkpointId}/approval-evidence` returns that complete
  bundle (never derived from the latest-20 history window; more than 32 recipes is refused) and grants no authority. The cockpit adds
  one explicit "Approve all enabled checks" action bound to the full observed source identity and owned by the committed bundle's lifetime. Legacy scalar approvals stay single-member
  facts without delivery authority for several recipes; nothing is published, combined or autonomous.
- [ADR-0031](decisions/0031-abandon-an-inactive-manual-run-through-an-explicit-human-decision.md) gives an owner one explicit exit from an
  inactive manual task. The protected `POST /api/runs/{runId}/abandon` (a required normalized reason of at most 2 KiB UTF-8, an 8 KiB body)
  atomically ends exactly a `Created` or `Running` `ManualAgent` run as the appended lifecycle `Abandoned` and records the immutable
  reason, the UTC time and exactly one Human-authored `run.abandoned` event, preserving its `Stage`, clearing its participant and freezing
  its clock. It is decided from fresh untracked reads under the write lock and refused while any attempt, verification execution or
  local-commit operation of the project is active, unrecognized or ambiguous (`Prepared`, `Executing` and `NeedsAttention` all block) or
  any workspace is `Preparing`, `Committing` or unrecognized; it changes no workspace, lease, branch, index, file or artifact and cancels
  no process. `Lifecycle` is the claim race token: Claude claims and the dispatch marker lose through their Run update, and the Codex
  planning, challenge-resolution and code-review claims (and their repairs) now re-read the lifecycle inside their write-locked claim
  transaction. The same normalized reason replays the recorded result; a different one conflicts; incoherent facts are never treated as
  abandoned. Normal intake treats only a coherent abandonment as terminal and creates a distinct run with fresh budgets that inherits no
  authority. A protected read-only `GET /api/runs/{runId}/abandonment` reports advisory eligibility and the recorded reason and time;
  the cockpit adds an "Abandon run" form owned by the committed run and eligible-form lifetime. Nothing is cancelled, repaired,
  released, deleted, published or created automatically.
- [ADR-0032](decisions/0032-show-a-recorded-local-delivery-receipt-from-pinned-evidence.md) shows what a completed local commit was
  delivered against. The protected, bodyless `GET /api/runs/{runId}/local-delivery-receipt` answers one closed state (`NotRecorded`,
  `NotCompleted`, `Unavailable`, `Available`; only `Available` carries the version-1 receipt) reconstructed only from the operation's
  pinned rows: its recorded checkpoint, execution report, CodeReviewer attempt and approval message, selected Human review and ordered
  verification authority members, each execution checked against its own immutable snapshot and recorded digest (the digest
  serialization of admission, extracted unchanged). Anything missing, foreign, duplicated, reordered, over 32 members or changed makes
  the whole receipt `Unavailable`; the latest or current records never substitute. It reads no filesystem, Git, process or provider
  state, writes nothing and needs no migration, and it describes recorded delivery, never current workspace state or remote
  publication. The cockpit shows a "Local delivery receipt" region beside a Completed operation, owned by the committed run and
  operation lifetime, read once and never polled.
- [ADR-0033](decisions/0033-browse-project-run-history-and-locate-prior-delivery-receipts.md) lets an owner browse a project's earlier
  objectives and inspect an older delivery after another run replaced it in the cockpit. The protected, bodyless
  `GET /api/projects/{projectId}/run-history` returns a page of that project's runs in descending `ExecutionNumber` (an exclusive,
  positive `beforeExecutionNumber` cursor; `limit` 10 by default and 1 to 20, never clamped and refused with the shared Problem Details contract that never repeats the rejected value, as is a 404; one extra row for `HasMore` over the unique
  project/number index; no offset, timestamp ordering or count). An entry carries only the identities, number, objective, lifecycle and
  stage (or the fixed `Unrecognized`), the exact stored mode, the two UTC times and an optional receipt source (the run's own recorded
  Completed operation's run, operation, commit and checkpoint identities and number) that only locates the ADR-0032 receipt; no coherent
  source is `null`, which implies nothing about whether a delivery exists, failed or succeeded. It reads persisted rows only and writes,
  claims and repairs nothing. The cockpit adds a collapsed "Run history" region for the selected project whose reads and selection belong
  to the committed project, open and selected-row lifetimes; a page is accepted only whole and coherent, older pages and a reload are
  explicit, and the selected row reuses the existing receipt component, hook and normalizer without mounting any live cockpit control.
- [ADR-0034](decisions/0034-browse-recorded-event-headers-of-a-historical-run.md) lets an owner browse the recorded journal of an older run
  as bounded headers. The protected, bodyless `GET /api/runs/{runId}/event-history` returns a page of that run's persisted event
  headers in descending `Sequence` (an exclusive `beforeSequence` cursor that is a canonical positive decimal string up to
  9223372036854775807, because a browser number cannot carry an Int64; `limit` 20 by default and 1 to 50; neither is clamped, both are
  refused with the shared Problem Details contract that never repeats the rejected value; a 404 for an unknown run; one fixed 409
  `run_event_history.unavailable` for a selected header without an identity or a positive sequence, never a partial page; one extra row
  for `HasMore` over the existing `(RunId, Sequence)` index, no offset, count or draining). Sequences are global, so a run's sequences have
  valid gaps, timestamps never order, and pages are not an atomic snapshot. An entry carries only the run and event identities, the exact
  sequence string, the UTC time, an event type admitted only by exact `RunEventType` constant (else the fixed `Unrecognized`) and an
  actor disclosure classified by the database without materializing a stored enum (a valid Agent with no recorded role is preserved;
  an unknown or inconsistent actor is `Unrecognized` with no role or provider); it never selects a payload, attempt, message or artifact
  and invents no version, persistence time, causation or correlation. The existing events route, live notifier and collaboration
  timeline are neither changed nor reused. The historical run detail adds a collapsed "Recorded events" region whose reads belong to the
  committed run and open lifetimes (older pages, a same-cursor retry and a reload are explicit; a page is accepted only whole, with
  exact-integer sequence ordering) and that states it shows recorded headers only, not live status, a current approval or a complete
  replay. This is a first journal browser: full replay and envelope completion remain open.
- [ADR-0035](decisions/0035-browse-recorded-collaboration-messages-of-a-historical-run.md) lets an owner browse the recorded conversation of an
  older run. The protected, bodyless `GET /api/runs/{runId}/collaboration-history` returns a page of that run's persisted collaboration messages
  in descending `Sequence` (an exclusive `beforeSequence` cursor that is a canonical positive decimal string up to 9223372036854775807;
  `limit` 10 by default and 1 to 20; neither is clamped, both are refused with the shared Problem Details contract that never repeats the
  rejected value; a 404 for an unknown run; one fixed 409 `collaboration_history.unavailable` for a selected row without an identity or a
  positive sequence, never a partial page; one extra row for `HasMore` over the existing `(RunId, Sequence)` index, no offset, count or
  draining). An entry carries the identities, the exact sequence string, the UTC time, the version (only `1.0`), type and provenance (else
  the fixed `Unrecognized`), the sender and recipient classified by the database without materializing a stored enum, a content status, a
  nullable summary and validated name/value fields. The summary and content JSON are selected under database byte guards (three bytes per admitted character of 600 and
  5000) and rechecked before parsing; content is `Available` only for recognized, coherent metadata, a safe summary and a content that
  passes the existing Domain `CollaborationMessageContentPolicy.Validate`, otherwise the row keeps its header as `Unavailable` with no
  text and its neighbors are unaffected (an unexpected fault is never masked). It returns no attempt, reply target, raw JSON or artifact
  and states recorded content only, never a current approval or provider success. The historical run detail adds a collapsed "Recorded
  collaboration" region (five messages per page so the journey's nine-message run needs explicit older paging) with the same owned
  lifetimes, whole-page validation, explicit older, retry and reload actions as the event headers. The live timeline route and hook are
  untouched; full linked replay (attempt and reply resolution) remains open.
- [ADR-0036](decisions/0036-inspect-the-recorded-reply-parent-of-a-historical-collaboration-message.md) lets an owner see which recorded message
  another message replied to, even when that parent is on a page that was never loaded. The protected, bodyless
  `GET /api/runs/{runId}/collaboration-history/{messageId}/reply` reads the source's own stored header and reply reference (no text column) by
  exact run and message identity and resolves at most one parent by its stored identity: same run, strictly earlier, version 1.0, a type
  the existing `CollaborationMessageReplyPolicy.Evaluate` allows for the source's type. It never substitutes another message, drains pages or
  follows a chain. The answer is the exact source sequence string, a closed status (`None`: a recognized version-1.0 Proposal with no
  reference; `Unavailable`: every other unusable shape, with no target identity or content; `Available`) and, only for `Available`, the
  parent under the unchanged ADR-0035 disclosure (so the relationship and the content availability are distinct). An unknown run and a
  missing or foreign source are one fixed 404 (`collaboration_reply.not_found`), an unusable stored source identity or sequence is one fixed
  409 (`collaboration_reply.unavailable`) and a database fault is never masked as a status. The history's projection and entry builder are
  one small shared extraction, so the page and the inspection admit a message identically. The cockpit adds one explicit "Inspect recorded
  reply" action per message into one inspector (close and a same-source retry) owned by the committed run's open history lifetime and each
  source selection, validates the whole answer against the observed source (closed status and target shape, `None` only for a Proposal,
  exact Int64 earlier order, a parent type the restated reply table allows) and reads nothing on render, recursively or by polling. Chain
  traversal, attempt and evidence linkage and complete replay remain open.
- [ADR-0037](decisions/0037-inspect-the-recorded-agent-attempt-attribution-of-a-historical-collaboration-message.md) lets an owner see which Agent
  attempt produced a historical provider-observed message. The protected, bodyless
  `GET /api/runs/{runId}/collaboration-history/{messageId}/attempt` reads the source's own stored metadata and `AttemptId` (never its summary or
  content JSON) and resolves at most that one attempt within the same run through bounded, untracked scalar projections, never a latest or
  same-role attempt. Its answer is the exact source sequence string, a closed status (`NoAgentAttempt`: a known version-1.0 message with a recognized
  provenance other than provider-observed, which is not absence of every attempt; `Unavailable`: every other unusable shape, with no target;
  `Available`) and, only for `Available`, a minimal header (attempt identity and number, role, provider, response contract, recorded status and
  claimed, dispatched and completed UTC times). `Available` requires the message's recognized Agent actor to match the attempt's role and provider, a
  positive number and `AgentAttemptIdentity.IsCoherent`, unchanged. Two narrow extractions share the existing rules with the scalar reader:
  `AgentAssignmentSnapshotPolicy` (the assignment read-validation `Attempt.GetAssignmentSnapshot` held inline, now delegated to) and a scalar
  `AgentAttemptIdentity.IsCoherent` overload, with equivalence proved against the original rules. Enums are classified in SQL, every
  assignment-validation string is selected only under a `length(CAST(... AS BLOB))` guard (three bytes per admitted character) with a flag that
  distinguishes rejected oversized storage from a legitimate null, and the max-turn storage keeps its exact INTEGER/TEXT/REAL/BLOB form under a small
  guard. An unknown run and a missing or foreign source are one fixed 404 (`collaboration_attempt.not_found`), an unusable stored source identity or
  sequence is one fixed 409 (`collaboration_attempt.unavailable`) and a database fault is never masked as a status. The cockpit adds one explicit
  "Inspect recorded attempt" action per message into one inspector (close and a same-source retry) owned by the committed run's open history lifetime
  and each source selection (its close bound to its own inspector and serial-guarded), independent of the reply inspector, validating the whole answer
  against the observed source; it describes recorded attribution only, never current execution, an outcome, an approval or sealed evidence. Message
  evidence, attempt history, repair lineage and complete replay remain open.
- [ADR-0038](decisions/0038-observe-stored-github-cli-authentication-on-explicit-request.md) lets an owner see whether the GitHub CLI's default
  stored authentication for github.com is active, absent or reporting a problem, because discovery alone proves only that a binary exists. The
  protected, bodyless `GET /api/environment/github-stored-authentication` returns the fixed host `github.com`, the fixed context
  `StoredDefaultConfig`, a closed status (`CliUnavailable`, `Unknown`, `NoActiveAccount`, `Authenticated`, `IssueReported`) and an observation time for the
  last three only. It invokes one fixed `gh auth status --hostname github.com --active --json hosts --jq <selector>` (a constant privacy projection
  that emits only active/state pairs, with an empty-`hosts` branch for the no-account answer and an `invalid` marker for an unexpected shape) through the existing process executor in the host scratch
  directory, only for a current, successful, direct-executable GitHubCli discovery snapshot (anything else is `CliUnavailable` with nothing invoked;
  it never discovers, refreshes or installs). The child gets only non-secret OS/profile/temp variables and fixed prompt, update and telemetry controls,
  never an ambient token, host, config, debug, proxy, browser or pager value, so environment-token and custom-config contexts are not observed and
  are disclosed. Probes are bounded to 15 s, 8 KiB per stream and 16 KiB combined, a singleton adapter permits one in-flight probe (a concurrent
  request is `Unknown`), and exit zero alone never proves authentication. It writes nothing and needs no migration. The cockpit adds a collapsed
  host-level region read only by its explicit check and recheck actions, owned by the mounted component and each opening. It is neither repository
  permission nor publication authority; selector evaluation and installed-CLI formatting are not verified without a real `gh`.
- [ADR-0039](decisions/0039-observe-the-project-local-github-origin-on-explicit-request.md) lets an owner see which GitHub repository a registered project's
  own local Git configuration names as `origin`, as an input for later repository scoping. The protected, bodyless
  `GET /api/projects/{projectId}/github-origin` returns the project, the fixed source `RepositoryLocalConfig`, a closed status (`Unavailable`,
  `NotConfigured`, `Available`), a time for the last two and a normalized `{host: github.com, owner, repository}` target for `Available` alone; an
  unknown project is the fixed 404 `projects.not_found`. It runs only for a current, successful, direct-executable `Git` snapshot and a stored resolved
  physical identity that the current root still matches before and after the observation (never rechecking, updating or backfilling an identity), and
  it invokes two fixed read-only commands through the existing process executor inside the project root: `rev-parse` as a minimal shape check (the
  root's own main repository only: no parent fallback, linked worktree, bare repository, submodule or separate Git directory) and `config --file
  <root>\.git\config --no-includes --null --get-all remote.origin.url` against a read-only hold on that validated, non-reparse file that pins the
  administrative scope for the whole observation (a replaced, redirected or vanished scope is `Unavailable`), under 15 s, 8 KiB per stream and 16 KiB combined, with only fixed Git controls in the child
  environment. Global, system, included, worktree-scope and command-line values, URL rewrites and `pushurl` are never candidates, nothing contacts a
  network, credential helper or hook, and nothing is written. Exit 1 with empty output is `NotConfigured`; exactly one non-empty NUL-terminated value
  in one of three literal spellings (`https://github.com/o/r`, `git@github.com:o/r`, `ssh://git@github.com/o/r`, one optional `.git`, conservative
  ASCII owner and repository rules) is `Available`; everything else (duplicates, credentials, ports, escapes, other hosts, truncation, timeouts) is
  `Unavailable` without exposing the raw value. The cockpit adds a collapsed project-level region read only by its explicit inspect and recheck
  actions, owned by the committed project and each opening. It is configured scope, never verified remote identity, permission, CI success or publication
  authority.
- [ADR-0040](decisions/0040-record-an-immutable-github-observation-scope-for-a-completed-local-delivery.md) lets the Human record, once and immutably, which
  GitHub repository scope is meant for the exact branch and commit of a completed manual local delivery. `POST /api/runs/{runId}/github-observation-target`
  (body at most 8 KiB: only `operationId`, an exact lowercase 40-hex `commitSha`, `owner` and `repository` in the unchanged ADR-0039 ASCII subset, shared as
  one Domain policy; no trimming, URL, host, branch or credential) takes one short manual write-locked transaction (the existing no-op Run write), reads fresh
  and untracked a ManualAgent run, an Available ADR-0032 receipt from the unchanged `LocalDeliveryReceiptReader` for that exact operation and commit, and the
  pinned operation and workspace rows whose number reproduces the recorded branch through `WorkspaceBranchNamePolicy` (never a client branch, `HEAD` or a
  latest record), then inserts one `GitHubObservationTarget` (host `github.com` and purpose `CiObservation` fixed; unique per run, operation and event;
  restrictive foreign keys; no update, replacement, deletion or backfill; existing runs stay unbound) and exactly one version-1 Human event
  `run.github_observation_target_recorded` with a bounded canonical payload, notifying after the commit and changing no lifecycle, stage, participant,
  clock, workspace, lease, approval or budget. The same coherent selection replays its original record, time and event; a different one is the fixed 409
  `github_observation_target.conflict`; an unusable or mismatched source is `github_observation_target.unavailable` (409); an unknown run is
  `github_observation_target.not_found` (404); no error echoes a value. The protected bodyless `GET` at the same route reads persisted rows only and answers
  a closed `NotRecorded` (source only when admissible), `Unavailable` (neither) or `Available` (source and the coherent target validated with its one Human
  event). The cockpit adds a collapsed region beside, not inside, the Completed local-commit region with explicit load, reload, close and a separate record
  action over blank owner and repository fields, showing the exact branch and commit and the immutable selection before submission, owned by the committed
  source and each opening, and requiring an explicit reload after any failed or ambiguous recording. It is local scope selection, never verified remote
  identity, permission, CI success, publication approval or automatic observation authority.
- [ADR-0041](decisions/0041-observe-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md) adds one explicit, read-only
  observation of the identity GitHub reports for that recorded target. The protected, bodyless `GET /api/runs/{runId}/github-repository-observation`
  (`GetGitHubRepositoryObservationQuery` through the mediator; any request body is refused before anything is observed with the fixed 400
  `github_repository_observation.body_not_allowed`; an unknown run is the fixed 404 `github_repository_observation.not_found`) reads the scope with the
  unchanged ADR-0040 source and target readers and answers a closed `NotRecorded` or `Unavailable` (no scope, nothing invoked), `CliUnavailable` (no fresh,
  successful, direct-executable `GitHubCli` snapshot; nothing invoked, nothing discovered), `Unknown` or `Observed` (only then the opaque repository node ID,
  the observed names and the host time). It re-reads the scope after the external work and refuses the answer when it was lost, changed or became incoherent,
  and validates the port's result independently (conservative ID, supported names equal to the recorded ones ignoring only ASCII case, never a rename). The
  operation-owned port `IGitHubRepositoryIdentityObserver` is implemented by `GitHubRepositoryIdentityAdapter` on the existing process executor: one fixed
  `gh api graphql --hostname github.com --method POST -f query=<constant read-only query> -f owner=<stored owner> -f name=<stored repository>` in the host scratch
  directory, the ADR-0038 environment allowlist, 15 seconds, 8 KiB per stream, no sinks, stderr discarded, one single-flight slot, and exact-JSON admission.
  Nothing is written, persisted, bound or locked. The cockpit adds a collapsed identity region inside an Available target's result with explicit Show, Check
  (then Recheck) and Close, owned by the committed source and target and each opening, showing inert text. It proves no remote branch, commit, permission, CI
  result, publication approval or permanent binding.
- [ADR-0042](decisions/0042-observe-the-recorded-github-branch-tip-of-a-recorded-observation-target-on-explicit-request.md) adds one further explicit, read-only
  observation of the exact recorded branch tip. The protected, bodyless `GET /api/runs/{runId}/github-branch-observation`
  (`GetGitHubBranchObservationQuery` through the mediator; any request body is refused before dispatch with the fixed 400 `github_branch_observation.body_not_allowed`;
  an unknown run is the fixed 404 `github_branch_observation.not_found`) reuses the unchanged ADR-0040 readers before and after the external call and the ADR-0038
  `GitHubCli` snapshot gate, and answers `NotRecorded`/`Unavailable` (no scope, nothing invoked), `CliUnavailable`, `Unknown`, `RefNotReturned`, `CommitMatched` or
  `CommitDifferent` (only these three carry the repository identity and a ref that is null exactly for `RefNotReturned`). The operation-owned port
  `IGitHubBranchObserver` is implemented by `GitHubBranchObservationAdapter` on the existing process executor: one fixed
  `gh api graphql --hostname github.com --method POST -f query=<constant read-only query> -f owner=<stored owner> -f name=<stored repository> -f ref=refs/heads/<recorded owned branch>`
  with the ADR-0041 environment, bounds and single-flight slot, and a whole-response admission that also proves the returned ref exactly (prefix `refs/heads/`, the
  recorded branch name, a `Commit` target with a 40-character lowercase hexadecimal OID) because GitHub's `qualifiedName` lookup can fall back to short names. The
  handler validates the result again and compares the remote SHA with the recorded one ordinally. Nothing is written, persisted, bound or locked. The cockpit adds a
  separate collapsed remote branch region beside the identity region with Show, Check (then Recheck) and Close, owned by the committed source and target and each
  opening. A match is only what GitHub reported at that time; it is not CI, a permission, a durable binding, a publication approval or a publication performed by
  DevalCopilot.

- The .NET host is the only authority for workflow state, policy, persistence,
  process execution, Git mutation, and remote publication.
- Tauri owns window lifecycle, sidecar lifecycle, packaging, and narrowly
  scoped native capabilities. It contains no workflow or domain policy.
- React renders current state and submits intentions. It never executes Git,
  agent, shell, or GitHub commands directly.
- MVC operations own commands and queries. SignalR carries notifications only;
  it is never the source of durable state and does not dispatch business
  commands.
- Long-running agent or GitHub operations never execute inside an EF Core
  transaction. Application commands persist intent in short transactions, and
  supervised background processes perform external work.
- SQLite writes are serialized through an application-owned ingestion boundary.
- The run scheduler permits bounded concurrency across distinct repositories,
  while enforcing one mutating run per canonical repository and one writer per
  worktree.
- Event order is defined by a monotonic integer sequence, not wall-clock time.
- Large logs, screenshots, patches, and generated reports are stored as hashed
  filesystem artifacts with metadata in SQLite.
- Agent context is assembled progressively from the smallest sufficient set of
  durable records and evidence. Complete transcripts and unchanged repository
  content are not replayed by default.
- Agent stages that receive bounded Git change evidence also receive bounded, identity-verified text previews of
  eligible untracked files, each explicitly marked complete, shortened, or omitted. A preview is admitted only from a
  handle proven physically inside the approved worktree (Windows) that, before any read and again after the bounded
  read, is a regular, non-reparse file with exactly one name (a file any other name reaches is omitted, not
  enumerated) and whose bytes match the fingerprint's raw-content identity; other hosts omit it. Its text exists only
  in the sealed manifest, the provider input and the existing authenticated sealed-artifact viewer. This closes
  generic untracked-preview delivery, not every filesystem read: the raw Git hashing behind the checkpoint fingerprint
  can still read an outside hard link; the tracked diff of NEW Agent delivery is closed separately by ADR-0024 and the human
  checkpoint inspection by ADR-0027 (see
  [ADR-0022](decisions/0022-admit-generic-untracked-previews-only-from-physically-proven-single-name-files.md) and
  [the protocol](architecture/agent-collaboration-protocol.md#bounded-untracked-file-previews-in-agent-manifests)).
- Every newly claimed Agent stage also receives the exact root `AGENTS.md` and `CLAUDE.md` of its own project's owned
  worktree, in one versioned `projectInstructionContext` section bound to the claim's workspace and checkpoint and preceded by a
  fixed boundary that makes it untrusted advisory context beneath the authorized plan, role, schema, permissions, command
  restrictions and human decisions. Each file is Complete (whole text, verified length and SHA-256), Absent, or Omitted with a
  fixed reason; only those two root paths are ever read, nothing is imported or followed, their text is withheld from the generic
  untracked and tracked-diff evidence of the same delivery, and the text is carried by the sealed
  manifest, the provider input and the existing authenticated sealed-artifact viewer. Already sealed manifests replay unchanged, the host proves containment on the open handle (Windows
  only), and the file can still change after observation (see
  [ADR-0021](decisions/0021-add-bounded-root-instruction-context-to-agent-manifests.md) and
  [the protocol](architecture/agent-collaboration-protocol.md#project-instruction-context-in-agent-manifests)).
- The tracked side of that evidence is a deterministic selection of complete file headers and hunks under an 8 KiB
  UTF-8 bound, fitted with the untracked previews inside the 32 KiB manifest ceiling; omitted tracked material is
  accounted for with fixed reasons and the selection is never described as a complete or applyable patch. A valid
  text hunk too large to select whole adds only a separate, at most 4 KiB, explicitly incomplete sample of its changed
  lines outside `diff` (see
  [the protocol](architecture/agent-collaboration-protocol.md#bounded-tracked-hunk-evidence-in-agent-manifests)).
  For a NEWLY claimed stage the text of that selection is never taken from Git's patch (ADR-0024): every tracked changed
  path is attested from the exact blob of the captured HEAD and, on Windows, from a held handle proven to be the single-name
  regular file of the owned worktree before and after the bounded read and around an independent identity check, and the
  host writes one conservative replacement hunk per file from those snapshots (not Git's minimal or filter-normalized
  patch, which the manifest states). A path that is unsafe, unprovable, unsupported or outside the bounds (256 KiB and
  8192 lines per source, 512 KiB retained per observation) is an explicit fixed-reason omission beside its delivered
  siblings, builders re-derive that for any reader, and sealed manifests replay unchanged (see
  [ADR-0024](decisions/0024-deliver-new-tracked-change-text-only-from-attested-snapshots.md) and
  [the protocol](architecture/agent-collaboration-protocol.md#attested-tracked-change-text-in-agent-manifests)).
- The two Claude paths that can edit the worktree (initial implementation and review correction) may carry one
  owner-requested, run-scoped agentic-turn limit, snapshotted immutably on each claimed attempt and passed as the
  provider's documented `--max-turns` argument under the `claude-implementation-v2` and `claude-review-correction-v2`
  contracts. It is a provider-loop request beside the host timeout, never a measured count, a token, cost, or account
  ceiling, or a host-enforced limit (see
  [the protocol](architecture/agent-collaboration-protocol.md#optional-claude-agentic-turn-limit-for-mutation-attempts)).
- The explicit mutation requests (initial implementation, ordinary review correction and, by ADR-0019, the correction of a
  verification diagnosis's findings) may carry optional bounded
  direct human guidance, snapshotted immutably on the exact claimed attempt and sealed into its context beside a fixed
  advisory boundary. It is advisory clarification only, available within the ordinary correction budget, and a fact of what
  the host supplied, never of provider compliance (see
  [the protocol](architecture/agent-collaboration-protocol.md#optional-direct-human-guidance-for-mutation-requests)).
- A completed second challenge round ends a proposal lineage in a human escalation. A human may explicitly authorize exactly
  one initial implementation claim for that final plan with a required, bounded reason; authorizing and requesting the
  implementation are separate operations, the grant is consumed atomically by the ordinary explicit claim (and stays spent
  even if the attempt fails), and no automatic review or resolution cap, budget, or execution gate is raised or bypassed. The
  escalation record itself states those options in fixed text and selects, grants, and approves nothing; it is recognized as a
  source only when its content is ordinal-equal to one of two complete canonical serializations, the current one or the original
  one that earlier records keep (see
  [ADR-0016](decisions/0016-add-explicit-human-authorization-of-one-escalated-plan-implementation.md),
  [ADR-0020](decisions/0020-correct-the-escalation-explanation-and-accept-its-two-canonical-forms.md), and
  [the protocol](architecture/agent-collaboration-protocol.md#explicit-human-authorization-of-one-escalated-plan-implementation)).
- Every newly claimed code review, its manual format repair, and re-reviews throughout the ordinary correction chain judge
  the exact Proposal the initial implementation consumed (the root, the revision, or the authorized final plan), while the
  Planner root remains the lineage identity and correction reply target. The change is forward only: a review sealed earlier
  replays its existing bytes, and history is never rewritten (see
  [ADR-0017](decisions/0017-review-the-implemented-plan-through-correction.md)).
- A human may explicitly request a read-only Codex diagnosis of the current failed local verification of an exact ExecutionReport. It
  yields one to ten findings or one bounded escalation, never an approval, and may then separately request a Claude correction of
  those findings through the existing correction contract and its one shared allowance, optionally with the same bounded advisory
  direct guidance as an ordinary correction, available only within that allowance (ADR-0019). Verification is rerun explicitly and an
  ordinary code review still requires every enabled command to be Passed (see
  [ADR-0018](decisions/0018-add-explicit-local-verification-failure-diagnosis-and-bounded-correction.md) and
  [the protocol](architecture/agent-collaboration-protocol.md#explicit-verification-failure-diagnosis)).
- Token usage is a visible, best-effort measurement at attempt and run level
  when provider data is available. It is not an account or cost budget and no
  account-usage threshold is enforced for Claude or by the display-only allowance. The enforced token control is an
  owner-configured, run-scoped, provider-separated
  stop on locally recorded token activity that refuses the next Agent claim for
  that provider once reached or unprovable; it is a retrospective local
  guardrail, not an account allowance or a per-attempt cap, and the advisory
  warning is separate (see the open risks in `docs/roadmap/current-work.md`).
  A separate, optional, run-scoped Codex account-usage percentage stop (1 through 100) is enforced at the Codex claim
  and again immediately before dispatch from a strict read-only observation that never reuses the display-only
  allowance projection; reaching it, or failing to observe it, ends a claimed attempt before dispatch with a
  bounded recorded decision. It is a local guard over a provider-reported percentage, not eligibility or capacity
  ([ADR-0025](decisions/0025-stop-new-codex-attempts-at-an-explicit-account-usage-percentage.md)).
  A separate, optional, run-scoped advisory Codex warning percentage (1 through 100, independent of the stop with no required
  ordering) is read only by one explicit "Check Codex account warning" request, which makes at most one strict observation and
  writes nothing; no claim, gate, dispatch or invocation path reads it, and a warning never refuses or stops anything
  ([ADR-0026](decisions/0026-warn-explicitly-about-a-codex-account-usage-percentage.md)). Claude account usage, session resume and
  manual compaction remain open.
- A human inspecting a checkpoint (the existing protected diff route) receives the same kind of host comparison, not Git's raw
  working-path patch (ADR-0027): an explicit inspection capture in the existing coherent bracket attests every tracked changed path
  with the ADR-0024 proof, the query re-derives coverage from those immutable facts, and the response carries the comparison text
  (at most 512 KiB of UTF-8, whole file blocks in ordinal order), a completeness flag and tracked/compared counts, the fixed
  host-comparison limitation, and every tracked path without text with a fixed reason; it never shows "No tracked diff." for an
  all-omitted capture. A physically proven tracked root `AGENTS.md` or `CLAUDE.md` is inert source text there, while Agent delivery
  still reserves both names to the controlled instruction section. This closes the HTTP/UI text-delivery route of the ordinary
  checkpoint diff, not every filesystem read: the raw Git hashing behind the fingerprint still reads named paths, and files can
  change after observation (see
  [ADR-0027](decisions/0027-compare-attested-tracked-sources-for-human-checkpoint-inspection.md)).
- Manual run intake may carry two optional, immutable, run-wide choices (ADR-0028): a claimed-Agent-attempt ceiling of 1 through 16 and
  a reserved-invocation-time ceiling of 1 through 120 whole minutes, each defaulting independently to the existing 16 and 120 when
  omitted or null. They are stored in the existing `Run` columns by the same atomic creation, never clamped, rounded or changed
  afterwards, and enforced by the unchanged claims (claims stay consumed after failure or interruption; time is reserved from
  configured timeouts, never measured). A ceiling below a role's configured timeout prevents that claim; the simulated demo and
  every historical run keep their policies, and the 1..16 / 1..120 range does not limit them (see
  [ADR-0028](decisions/0028-let-the-owner-choose-immutable-run-budgets-at-manual-intake.md)).
- The three Claude adapters also read the optional `modelUsage` map of the one clean-exit, untruncated result envelope
  and keep only each model identifier with its reported context-window and maximum-output limits, admitted whole or not
  at all (at most 16 unique ASCII identifiers, positive 32-bit integers, output not above the window) and independent of
  the business result and of token usage. The completion transaction records them once as one nullable canonical
  project-owned snapshot on the attempt (at most 4 KiB), and only the existing historical attempt-evidence route and its
  selected detail show them, as "Claude-reported model limits": models listed by Claude, not proven used, never remaining
  context or next-invocation capacity, "Not recorded" when absent. It changes no argument, eligibility, token control,
  filesystem or manifest behavior (see
  [ADR-0023](decisions/0023-record-claude-reported-model-context-limits-in-historical-attempt-evidence.md) and
  [the protocol](architecture/agent-collaboration-protocol.md#claude-reported-model-context-limits)).
- Git, Codex, Claude Code, and GitHub integrations are replaceable
  Infrastructure adapters behind narrow Application ports.
- The MVP uses existing local CLI authentication and never copies provider
  credentials into the application database.

## Security tailoring

S2 is selected because the application executes code, edits repositories,
interacts with privileged developer credentials, and may publish remote Git
changes. The application is local-only, but compromise could materially affect
source repositories and connected systems.

- The local API binds only to loopback on an ephemeral port.
- A per-launch secret authenticates the bundled frontend and remains in memory.
- Browser storage does not contain credentials or provider tokens.
- The frontend has no generic shell capability.
- Approved project roots constrain filesystem access.
- Repository text, tool output, generated content, and CI logs are untrusted.
- Push, pull request, merge, release, deployment, destructive Git, and workflow
  actions follow explicit risk policies.

## Testing tailoring

- SQLite integration tests use disposable file-backed databases because SQLite
  is the production provider. EF Core InMemory is not provider evidence.
- MVC integration tests use `WebApplicationFactory` and a disposable SQLite
  database.
- Agent, Git, GitHub, and local process adapters use deterministic executable
  test doubles for contract and failure-path tests. Automated tests do not
  call real providers.
- Browser workflows use Playwright against a test host and test adapters.
- Docker or Testcontainers are used only when the real boundary under test
  requires a containerized provider.

## Package adoption

Adopt only released `Devalente.Shared.*` packages whose responsibilities are
needed by a current slice. The likely initial set is CQRS abstractions and
mediator, Results, FluentValidation integration, EF Core integration, MVC
Problem Details, and NSwag generation. Auditing, soft deletion, and security
packages remain opt-in according to actual entity and host requirements.

## Approved deviations

None. Local-only tailoring and file-backed SQLite integration tests are project
decisions within the shared standards rather than exceptions to them.
