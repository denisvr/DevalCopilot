# Current work and cross-chat handoff

This is the delivery checkpoint, not a transcript or approval. Git and code
prevail over this summary. At a new chat or slice, compare branch, `HEAD`,
local `origin/main`, and staged/unstaged/untracked changes before editing.
See the [roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md),
and accepted [ADRs](../decisions/README.md) for their respective contracts.

## Current checkpoint (2026-09-27)

- Latest accepted delivery: the bounded, read-only configured Claude
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
- Next action: Codex investigates the remaining Increment 4 provider runtime
  controls against code, accepted ADRs, and provider contracts before
  selecting another bounded slice. None is selected yet.

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
