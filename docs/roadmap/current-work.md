# Current work and cross-chat handoff

This is the delivery checkpoint, not a transcript or approval. Git and code
prevail over this summary. At a new chat or slice, compare branch, `HEAD`,
local `origin/main`, and staged/unstaged/untracked changes before editing.
See the [roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md),
and accepted [ADRs](../decisions/README.md) for their respective contracts.

## Current checkpoint (2026-09-27)

- Latest accepted delivery: immutable assignment provenance for the two
  remaining current Claude Code paths (CriticalReviewer and Implementer
  ReviewCorrection) at claim time, plus five configured adapter facts on
  each path's existing attempt status and cockpit action, in
  `caf45d33396617ca640cb766fe5bc984c93b488c` (parent
  `6312438046e9da0a43e2231ecc76f3e8eee62ac6`), published to `origin/main`
  and verified against the live remote. See
  [planner-handoff.md](planner-handoff.md) for the selection and review
  record.
  - `Attempt.ClaimAgentCriticalReview` now persists a concrete
    `AgentPermissionProfile.ReadOnly` and the fixed adapter contract version
    `claude-critical-review-v1`; `Attempt.ClaimAgentReviewCorrection` now
    persists `AgentPermissionProfile.WorkspaceEditOnly` and the fixed adapter
    contract version `claude-review-correction-v1` — both using the existing
    nullable `AgentPermissionProfile` / `AgentAdapterContractVersion`
    columns already shared with the Implementer and Codex roles; no
    migration was needed. Requested/observed model and effort remain `null`
    for both paths. The `AgentPermissionProfile.ReadOnly` XML comment was
    made provider-neutral (assigned read-only workspace intent, never a
    proven or observed effective isolation boundary, and never that shell,
    network, or MCP actions are absent) since it is now shared by a Claude
    Code path, not only Codex roles. Legacy rows with `null`
    permission-profile/adapter-contract-version columns continue to project
    as `Unknown`/`null` via the existing `Attempt.GetAssignmentSnapshot()`
    degrade-to-Unknown behavior — never retroactively treated as coherent.
  - `GetClaudeCriticalReviewAttemptStatusQueryHandler` now calls
    `Attempt.GetAssignmentSnapshot()`, fails the whole status closed with the
    existing `agent_attempts.invalid_assignment` error for malformed
    assignment metadata (new for this role, mirroring the Implementer
    handler's own try/catch and null-snapshot guard), and discloses five
    facts — `configuredPermissionMode: "plan"`,
    `configuredSessionPersistence: "Disabled"`,
    `configuredPermissionPrompts: "None"`,
    `configuredResumeEligibility: "Ineligible"`, and
    `configuredBuiltInTools: "None"` (the adapter's explicit empty `--tools`
    argument) — only for a coherent current assignment.
    `GetReviewCorrectionAttemptStatusQueryHandler` gained the identical five
    facts for its own path (`configuredPermissionMode: "acceptEdits"`,
    `configuredBuiltInTools: "Read,Edit,Write,Glob,Grep"`, the same three
    other values), added as a pure addition strictly after the existing
    current-review lineage and budget/escalation computation: its own
    `ImplementerExecutionReportEligibility.LoadSnapshotAsync` call is now
    wrapped to fail closed on a corrupt persisted assignment enum anywhere
    in the run (previously an unhandled exception, now the same fail-closed
    `agent_attempts.invalid_assignment` result used elsewhere), and the
    resolved correction attempt's own `GetAssignmentSnapshot()` gates the
    five facts — neither change alters which attempt is resolved as
    "current" or how the budget/escalation facts are computed; both
    existing handler tests covering that lineage remain green unchanged.
    Both status handlers preserve strict `null`/`Unknown` fallback for no
    attempt or a valid historical/mismatched assignment, exactly mirroring
    the Implementer and Codex-role precedent. All facts describe fixed CLI
    arguments only — never observed effective access, an MCP or complete
    security boundary, model or effort, or invocation eligibility.
  - The two response contracts remain role-specific, each gaining exactly
    the same five new fields, mapped through their existing endpoints; no
    raw provider/role/permission-profile/adapter-contract-version field was
    added to either. `ClaudeCriticalReviewAction.tsx` and
    `ReviewCorrectionAction.tsx` each render a new "Configured permission
    mode: … · Configured provider-session persistence: … · Configured
    permission confirmations: … · Configured resume eligibility: … ·
    Configured built-in tools: …" line, whitelist-comparing the single
    recognized literal per fact and rendering `Unknown` for anything else —
    never a provider-supplied value verbatim. Documented in the new
    "Configured Claude CriticalReviewer and Implementer ReviewCorrection
    permission mode, session persistence, permission confirmations, resume
    eligibility, and built-in tools" subsection of
    [run-cockpit-specification.md](../product/run-cockpit-specification.md).
  - Two pre-existing shared cross-role disclosure-guard test files
    (`AgentTokenUsageEndpointTests` and
    `AgentProcessExecutionEvidenceEndpointTests`) scan every role route's raw
    response body for the substring "session"; both already carried a
    narrow, route-gated exception for the Implementer's own
    `configuredSessionPersistence` fact. That exception is now also opted in
    for the CriticalReviewer route (`route is "agent-attempts/implementation"
    or "agent-attempts/claude-critical-review"`), since this attempt status
    now legitimately carries the same fact; every other disclosure check in
    both files, and the exception's own scoping (root-level only, single
    occurrence, exactly one of the two coherent values), is unchanged. No
    equivalent carve-out was needed for the two new endpoint test files
    themselves (`GetClaudeCriticalReviewAttemptStatusEndpointTests`,
    `ReviewCorrectionEndpointTests`) or the frontend, since neither has a
    pre-existing blanket "prompt"/"session" scan.
  - `ClaudeCriticalReviewAdapterTests` and `ClaudeReviewCorrectionAdapterTests`
    were extended with comments and (for ReviewCorrection) new assertions
    tying their existing exact-argument indices to the newly disclosed
    facts; no adapter argument changed.
  - Correction round: `GetClaudeCriticalReviewAttemptStatusQueryHandler`'s
    coherence condition now also requires
    `attempt.AgentResponseContract == AgentResponseContract.CriticalReview`,
    proven by a new focused test that persists an otherwise-fully-coherent
    attempt with a different, valid response contract and asserts all five
    facts remain `null`.
    `GetReviewCorrectionAttemptStatusQueryHandler`'s own coherence condition
    now also states `attempt.AgentResponseContract ==
    AgentResponseContract.ReviewCorrection` explicitly — the attempt
    resolved above was already exclusively selected by that same contract in
    the lineage query, so this is a defensive, explicit restatement of an
    already-guaranteed fact, never a change to lineage selection; both
    existing lineage/budget tests remain green unchanged. The handler's own
    comment on its `LoadSnapshotAsync` try/catch was corrected: it now
    states plainly that this fail-closed path can fire from an unparseable
    assignment enum on *any* attempt in the run, not only the eventual
    review-correction candidate, and that it is a broader run-wide guard
    layered above, not a replacement for, the later per-attempt
    `GetAssignmentSnapshot()` check on the resolved correction attempt
    itself. The cockpit specification already described the coherence rule
    as covering "provider, role, response contract, permission profile, and
    adapter contract version," so no documentation change was needed there.
  - Before the substantive commit, a stale sentence in
    `GetReviewCorrectionAttemptStatusQueryHandler`'s later per-attempt
    `GetAssignmentSnapshot()` check comment (claiming an unrelated corrupt
    attempt on the run could never fail this status) was corrected —
    comment-only, no behavior change — to state that this case is instead
    caught earlier, at the `LoadSnapshotAsync` call, exactly as the
    surrounding correction round already described.
  - Checks actually run: Application.Tests focused
    `GetClaudeCriticalReviewAttemptStatusQueryHandlerTests` 9/9 (1 new
    mismatched-response-contract case) and
    `GetReviewCorrectionAttemptStatusQueryHandlerTests` 6/6 unchanged, full
    Application.Tests 1007/1007; Api.IntegrationTests focused
    `GetClaudeCriticalReviewAttemptStatusEndpointTests` +
    `ReviewCorrectionEndpointTests` 19/19 unchanged; `git diff --check`
    reported no new errors. Domain.Tests 529/529, full Api.IntegrationTests
    322/322, Architecture.Tests 9/9, Infrastructure.IntegrationTests 440/441
    (one pre-existing unrelated skip), frontend 615/615, `tsc -b`, `oxlint`,
    and `vite build` were not rerun in this correction — they are untouched
    by comment-only and coherence-condition-only backend changes with no
    response-shape or frontend change; their results from the immediately
    preceding round remain applicable. The NSwag client was not regenerated
    in this round — no response DTO changed; its prior additive-only diff
    (40 insertion lines) is unchanged. Automated tests never call a real
    provider. Post-publication, `main`, `HEAD`, local `origin/main`, and
    live `origin/main` were confirmed at the delivered commit with a clean
    working tree, and focused
    `GetClaudeCriticalReviewAttemptStatusQueryHandlerTests` +
    `GetReviewCorrectionAttemptStatusQueryHandlerTests` (15/15 combined)
    were reconfirmed against it.
  - Remaining risks: none newly introduced. Provider-session resume, runtime
    controls, and the other open items below remain unchanged and open.
  - Next action: no next Increment 4 slice is selected yet; Codex selects and
    dispatches the next bounded slice.
- Previously accepted delivery: immutable assignment provenance for the three
  current Codex read-only roles (Planner, Resolver, CodeReviewer) at claim
  time, plus two configured adapter facts on each role's existing attempt
  status and cockpit action, in `1f69319614252ad4fb9e6bf1a8e93d61b33ac05d`
  (parent `01ccd3b4639d4ded8311226bea13bca922a0717e`), published to
  `origin/main` and verified against the live remote. See
  [planner-handoff.md](planner-handoff.md) for the selection and review
  record.
  - `Attempt.ClaimAgent` (Planner), `Attempt.ClaimAgentChallengeResolution`
    (Resolver), and `Attempt.ClaimAgentCodeReview` (CodeReviewer) each now
    persist a concrete `AgentPermissionProfile.ReadOnly` and a distinct,
    fixed adapter contract version — `codex-planning-v1`,
    `codex-challenge-resolution-v1`, and `codex-implementation-review-v1`
    respectively — using the existing nullable `AgentPermissionProfile` /
    `AgentAdapterContractVersion` columns already shared with the Claude
    Implementer role; no migration was needed. Requested/observed model and
    effort remain `null` for all three roles — neither adapter arguments nor
    provider output establish those facts, so none are invented. A new
    `AgentPermissionProfile.ReadOnly = 2` member was added (appended, never
    renumbered); existing persisted rows with `null` permission
    profile/adapter-contract-version columns (claimed before this slice)
    continue to project as `AgentPermissionProfile.Unknown`/`null` via the
    existing `Attempt.GetAssignmentSnapshot()` degrade-to-Unknown behavior —
    they are never retroactively treated as coherent.
  - Each role's existing attempt-status query handler
    (`GetAgentAttemptStatusQueryHandler`, `GetChallengeResolutionAttemptStatusQueryHandler`,
    `GetCodeReviewAttemptStatusQueryHandler`) now calls
    `Attempt.GetAssignmentSnapshot()`, fails the whole status closed with the
    existing `agent_attempts.invalid_assignment` error for malformed
    assignment metadata (mirroring the Claude Implementer handler's own
    try/catch and null-snapshot guard, added here for the first time), and
    discloses two new facts — `configuredCommandSandbox: "read-only"` and
    `configuredRolloutPersistence: "Disabled"` — only when that attempt's own
    provider, role, permission profile, and role-specific adapter contract
    version all agree with the current path. No attempt, or a valid
    historical/mismatched assignment, yields `null`/`Unknown` for both facts.
    These mirror the shared `CodexProcessInvoker`'s existing, unchanged
    `--sandbox read-only` and `--ephemeral` arguments, which the
    [Codex CLI reference](https://developers.openai.com/codex/cli/reference)
    documents as the sandbox policy for model-generated commands and as
    running without persisting session rollout files, respectively — stated
    only as configured CLI arguments, never observed effective isolation, a
    complete access-control boundary, provider-session resume eligibility, or
    invocation eligibility. No CLI argument, invocation, claim/dispatch
    authorization or budget, repository/worktree policy, schema, other
    role/provider, session identifier, resume/open/fork action, model/effort
    selection, context/compaction, account allowance, or fallback changed.
  - The three response contracts remain role-specific (no shared assignment
    DTO): `AgentAttemptStatusResponse`, `ChallengeResolutionAttemptStatusResponse`,
    and `CodeReviewAttemptStatusResponse` each gained exactly the same two
    new fields, mapped through their existing endpoints. Only these two
    facts are disclosed — not the raw provider/role/permission-profile/
    adapter-contract-version fields the Implementer status already exposes.
    `CodexPlanningAction.tsx`, `ChallengeResolutionAction.tsx`, and
    `CodeReviewAction.tsx` each render a new "Configured command sandbox: …
    · Configured rollout persistence: …" line, whitelist-comparing the
    single recognized literal per fact and rendering `Unknown` for anything
    else — never a provider-supplied value verbatim. Documented in the new
    "Configured Codex Planner, Resolver, and CodeReviewer command sandbox and
    rollout persistence" subsection of
    [run-cockpit-specification.md](../product/run-cockpit-specification.md).
  - Checks actually run: Domain.Tests 527/527 (3 new/updated
    `AgentAssignmentTests` cases proving each role's concrete claim-time
    assignment and a legacy-null-column case), Application.Tests 999/999
    (new/extended `GetAgentAttemptStatusQueryHandlerTests`,
    `GetChallengeResolutionAttemptStatusQueryHandlerTests`, and a new
    `GetCodeReviewAttemptStatusQueryHandlerTests` covering coherent-current,
    no-attempt, historical/mismatched, and malformed-assignment cases per
    role), Api.IntegrationTests 318/318 (extended endpoint tests per role,
    including 422 fail-closed and disclosure checks), Architecture.Tests 9/9,
    Infrastructure.IntegrationTests 440/441 (one pre-existing unrelated
    skip; the three existing exact-argument adapter tests —
    `CodexPlanningAdapterTests`, `CodexChallengeResolutionAdapterTests`,
    `CodexImplementationReviewAdapterTests` — remain green, confirming
    `--sandbox read-only`/`--ephemeral` are unchanged) — all green. Frontend
    611/611 (6 new cases across the three action components), `tsc -b`
    clean, `oxlint` exited 0 with the same 20 pre-existing warnings (0 new),
    `vite build` production build passed, `git diff --check` reported no new
    errors (only the pre-existing CRLF-normalization warning on the
    generated client file). The NSwag client was regenerated via
    `dotnet build src/backend/DevalCopilot.Api`; the resulting
    `api-client.ts` diff is additive only (24 insertion lines: two new
    optional fields × three response types × 4 lines each). Automated tests
    never call a real provider. Post-publication, `main`, `HEAD`, local
    `origin/main`, and live `origin/main` were confirmed at the delivered
    commit with a clean working tree, and focused
    `GetAgentAttemptStatusQueryHandlerTests`,
    `GetChallengeResolutionAttemptStatusQueryHandlerTests`, and
    `GetCodeReviewAttemptStatusQueryHandlerTests` (25/25 combined) were
    reconfirmed against it.
  - Remaining risks: none newly introduced. Provider-session resume, runtime
    controls, and the other open items below remain unchanged and open.
  - Next action after this slice: superseded by the Claude CriticalReviewer
    and ReviewCorrection assignment-provenance slice recorded at the top of
    this checkpoint.
- Previously accepted delivery: the bounded, read-only configured Claude
  Implementer built-in tool list fact in
  `90c84b79917aa681c25f04182ca485defdfb3357` (parent
  `8f52522a35419045229f27962f8523cf41a5149d`), published to `origin/main` and
  verified against the live remote. See [planner-handoff.md](planner-handoff.md)
  for the selection and review record.
  - The implementation-attempt status and cockpit now show a new
    `ConfiguredBuiltInTools` / `configuredBuiltInTools` fact alongside the
    existing `ConfiguredPermissionMode`, `ConfiguredSessionPersistence`,
    `ConfiguredPermissionPrompts`, and `ConfiguredResumeEligibility` facts —
    `"Read,Edit,Write,Glob,Grep"` (the current `ClaudeImplementationAdapter`'s
    existing, unchanged `--tools` argument, which the
    [Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference)
    documents as restricting built-in tools and explicitly states does not
    affect MCP tools) only when the attempt's own provider, role, permission
    profile, and adapter contract version all agree with the current
    supported implementation path; otherwise `null`/`Unknown`, with the
    existing fail-closed `agent_attempts.invalid_assignment` error preserved
    for invalid or absent assignment metadata (never a success value
    reporting `Unknown`). This is a configured adapter argument fact about
    this attempt, never an observation of effective access, a complete
    security boundary, an MCP tool restriction, or invocation eligibility.
    No CLI argument, invocation or tool policy, preflight, claim/dispatch,
    authorization, other role, model/effort selection, session behavior,
    context/compaction, account usage, or persistence schema changed.
    Labeled "Configured built-in tools" in the UI. Documented in the new
    "Configured Claude Implementer built-in tools" subsection of
    [run-cockpit-specification.md](../product/run-cockpit-specification.md).
    The new fact's value/label do not contain the substring "prompt", so no
    equivalent to the existing root-level `configuredPermissionPrompts`
    disclosure-guard exception in `GetImplementationAttemptStatusEndpointTests`
    was needed; that pre-existing guard and its five negative tests are
    unchanged.
  - Checks actually run: Application.Tests focused
    `GetImplementationAttemptStatusQueryHandlerTests` 8/8, full
    Application.Tests 986/986, Api.IntegrationTests focused
    `GetImplementationAttemptStatusEndpointTests` 9/9, full
    Api.IntegrationTests 312/312, Architecture.Tests 9/9, Infrastructure
    focused `ClaudeImplementationAdapterTests` 6/6 (confirms the adapter's
    `--tools` argument is unchanged) — all green. Frontend focused
    `ImplementationAction.test.tsx` 19/19, full frontend suite 605/605,
    `tsc -b` clean, `oxlint` exited 0 with the same 20 pre-existing warnings
    (0 new), `vite build` production build passed, `git diff --check`
    reported no new errors (only the pre-existing CRLF-normalization warning
    on the generated client file, consistent with prior slices). The NSwag
    client was regenerated via `dotnet build src/backend/DevalCopilot.Api`;
    the resulting `api-client.ts` diff is additive only (one new optional
    field, 4 insertion lines). Domain.Tests and full
    Infrastructure.IntegrationTests were not rerun in this slice beyond the
    focused adapter test above — they are untouched by this change; their
    last confirmed results remain in Git history for the prior delivered
    slice. Post-publication, focused Application
    `GetImplementationAttemptStatusQueryHandlerTests` 8/8 and focused Api
    `GetImplementationAttemptStatusEndpointTests` 9/9 were reconfirmed against
    the delivered commit with a clean working tree.
  - Remaining risks: none newly introduced. Provider-session resume, runtime
    controls, and the other open items below remain unchanged and open.
  - Next action after this slice: superseded by the Codex read-only role
    assignment-provenance slice recorded at the top of this checkpoint.
- Previously accepted delivery: the bounded, read-only configured Claude
  Implementer resume-eligibility fact in
  `1ec9ac5f1c9eb3a25301cd37684c368db1ef24f6` (parent
  `94b2cc3b6128df7d4bac22ae7f47c40a5532d2cc`), published to `origin/main` and
  verified against the live remote. Checkout clean at delivery and after
  publication. Codex reviewed the diff across two correction rounds and
  recorded GO before commit/push, then independently verified publication;
  see [planner-handoff.md](planner-handoff.md) for the review decision
  record.
  - The implementation-attempt status and cockpit now show a new
    `ConfiguredResumeEligibility` / `configuredResumeEligibility` fact
    alongside the existing `ConfiguredPermissionMode`,
    `ConfiguredSessionPersistence`, and `ConfiguredPermissionPrompts`
    facts — `"Ineligible"` (the current `ClaudeImplementationAdapter`'s
    existing, unchanged `--no-session-persistence` argument, which the
    [Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference)
    documents as making a session started under this flag unable to be
    resumed) only when the attempt's own provider, role, permission profile,
    and adapter contract version all agree with the current supported
    implementation path; otherwise `null`/`Unknown`, with the existing
    fail-closed `agent_attempts.invalid_assignment` error preserved for
    invalid or absent assignment metadata (never a success value reporting
    `Unknown`). This is a configured adapter fact about this attempt, never a
    provider-observed result, a host-wide capability assessment, or
    invocation eligibility of any kind. No CLI argument, invocation,
    claim/dispatch policy, authorization, provider preflight, other role,
    model/effort selection, context/compaction, session identifier,
    resume/open/fork action, usage limit, or persistence schema changed.
    Documented in the new "Configured Claude Implementer resume eligibility"
    subsection of
    [run-cockpit-specification.md](../product/run-cockpit-specification.md).
    The new fact's name/label ("resume eligibility") does not contain the
    substring "prompt", so it needed no equivalent to the existing
    root-level `configuredPermissionPrompts` disclosure-guard exception in
    `GetImplementationAttemptStatusEndpointTests`; that pre-existing guard and
    its five negative tests are unchanged.
  - Checks actually run: Application.Tests focused
    `GetImplementationAttemptStatusQueryHandlerTests` 8/8, full
    Application.Tests 986/986, Api.IntegrationTests focused
    `GetImplementationAttemptStatusEndpointTests` 9/9, full
    Api.IntegrationTests 312/312, Architecture.Tests 9/9 — all green. Frontend
    focused `ImplementationAction.test.tsx` 19/19, full frontend suite
    605/605, `tsc -b` clean, `oxlint` exited 0 with the same 20 pre-existing
    warnings (0 new), `vite build` production build passed, `git diff --check`
    reported no new errors (only the pre-existing CRLF-normalization warning
    on the generated client file, consistent with prior slices). The NSwag
    client was regenerated via `dotnet build src/backend/DevalCopilot.Api`;
    the resulting `api-client.ts` diff is additive only (one new optional
    field, 4 insertion lines). Domain.Tests and
    Infrastructure.IntegrationTests were not rerun in this slice — they are
    untouched by this change; their last confirmed results remain in Git
    history for the prior delivered slice. Codex independently verified
    `main`, `HEAD`, local `origin/main`, and live `origin/main` at the
    delivered commit, a clean checkout, and the exact reviewed 12-file list,
    then independently passed post-publication Application 986/986, API
    312/312, Architecture 9/9, frontend 605/605, typecheck, production build,
    lint (20 pre-existing warnings, 0 new), and `git diff --check`.
  - Remaining risks: none newly introduced. Provider-session resume,
    runtime controls, and the other open items below remain unchanged and
    open.
  - Next action after this slice: superseded by the configured built-in
    tools slice recorded at the top of this checkpoint.
- Previously accepted delivery: the bounded, read-only configured Claude
  Implementer permission-confirmations fact in
  `01f1777af877dd0374d7e3d7e461127a02f40d73` (parent
  `c2b0155f26f07bbe09ca67a9c5234228e274a703`), published to `origin/main` and
  verified against the live remote. Checkout clean at delivery and after
  publication. Codex reviewed the diff across two correction rounds and
  recorded GO before commit/push; see [planner-handoff.md](planner-handoff.md).
- The implementation-attempt status and cockpit now show a new
  `ConfiguredPermissionPrompts` / `configuredPermissionPrompts` fact
  alongside the existing `ConfiguredPermissionMode` and
  `ConfiguredSessionPersistence` facts — `"None"` (the current
  `ClaudeImplementationAdapter`'s existing, unchanged `--permission-prompts
  none` argument, which the
  [Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference)
  documents as denying interactive permission-confirmation prompts in print
  mode) only when the attempt's own provider, role, permission profile, and
  adapter contract version all agree with the current supported
  implementation path; otherwise `null`/`Unknown`, with the existing
  fail-closed `agent_attempts.invalid_assignment` error preserved for invalid
  or absent assignment metadata (never a success value reporting `Unknown`).
  No CLI argument, invocation, claim/dispatch policy, authorization, provider
  preflight, other role, model/effort selection, context/compaction, session
  behavior, usage limit, or persistence schema changed. Documented in the new
  "Configured Claude Implementer permission confirmations" subsection of
  [run-cockpit-specification.md](../product/run-cockpit-specification.md).
  The UI label reads "Configured permission confirmations" (not "…prompts")
  to avoid colliding with `ImplementationAction.test.tsx`'s existing blanket
  `/prompt/i` disclosure check on rendered text; the API field name itself
  does use `PermissionPrompts`, matching the CLI flag name.
- One pre-existing, single-file API disclosure guard
  (`GetImplementationAttemptStatusEndpointTests`'s own coherent-assignment
  test, which asserts the response body contains no `"prompt"` substring)
  needed the same narrow, JSON-root-verified, single-occurrence exception
  already applied twice to the two shared cross-role guards for
  `configuredSessionPersistence` — scoped to exactly this one root-level
  property with one of its two coherent values. Five focused tests prove the
  exception still rejects a nested occurrence, a duplicate occurrence, an
  unexpected value, and a genuine leaked prompt alongside the safe fact, and
  permits only the two coherent values. Every other disclosure check in this
  file and the two shared guard files is unchanged.
- Checks actually run: Domain.Tests 524/524, Application.Tests 986/986,
  Infrastructure.IntegrationTests 440/441 (one pre-existing unrelated skip),
  Api.IntegrationTests 312/312 (307 existing + 5 new guard-negative/positive
  cases), Architecture.Tests 9/9 — all green. Frontend 605/605 tests
  (existing `ImplementationAction` assertions extended for the valid,
  absent, and mismatched-sentinel cases), `tsc -b` clean, `oxlint` exited 0
  with the same 20 pre-existing warnings (0 new), `vite build` production
  build passed, `git diff --check` reported no whitespace errors. The NSwag
  client was regenerated via `dotnet build src/backend/DevalCopilot.Api`;
  the resulting `api-client.ts` diff is additive only (one new optional
  field). Codex independently passed the full API suite (312/312), focused
  Application (8/8), focused frontend (19/19), typecheck, and diff check
  across its review rounds.
- No adapter argument, invocation, claim/dispatch policy, authorization,
  provider preflight, other role, model/effort selection, context/compaction,
  session behavior, usage limit, or persistence schema changed. The
  account-allowance evidence candidate remains closed without delivery; no
  safe CLI observation contract was established.
- Next action after this slice: superseded by the configured resume-eligibility
  slice recorded at the top of this checkpoint.

## Open risks

- Provider account-usage and token thresholds remain unenforced. Per-attempt
  token usage is not account allowance or cost. Provider-session resume,
  runtime controls, and context-window/compaction work remain open under
  [Increment 4](mvp-delivery-plan.md).
- [ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md)
  and [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)
  bound claim count and reserved time, not actual wall time or provider usage;
  neither has a human override. Gemini execution remains disabled by
  [ADR-0011](../decisions/0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md).
- Raw linked artifact inspection beyond the bounded collaboration-evidence
  drill-down remains open; see the cockpit specification.

Older delivery and review details remain in Git. The executor updates this
page with actual delivery evidence; Codex records acceptance and next-slice
decisions in [planner-handoff.md](planner-handoff.md).
