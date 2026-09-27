# Current work and cross-chat handoff

This is the delivery checkpoint, not a transcript or approval. Git and code
prevail over this summary. At a new chat or slice, compare branch, `HEAD`,
local `origin/main`, and staged/unstaged/untracked changes before editing.
See the [roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md),
and accepted [ADRs](../decisions/README.md) for their respective contracts.

## Current checkpoint (2026-09-27)

- Current delivery, based on parent `8f52522a35419045229f27962f8523cf41a5149d`:
  a bounded, read-only configured Claude Implementer built-in tool list fact.
  See [planner-handoff.md](planner-handoff.md) for the selection and review
  record.
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
    slice.
  - Remaining risks: none newly introduced. Provider-session resume, runtime
    controls, and the other open items below remain unchanged and open.
  - Verification to perform after publication: confirm `main`, `HEAD`, and
    local `origin/main` at the delivered commit; independently rerun the
    checks above; confirm the live remote points to the delivered commit
    before selecting the next Increment 4 slice.
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
