# Current work and cross-chat handoff

This is the short delivery checkpoint, not a transcript or an execution
approval. [The roadmap](mvp-delivery-plan.md),
[engineering context](../engineering-context.md),
[architecture](../architecture/README.md), and
[accepted ADRs](../decisions/README.md) retain their own authority. Git and
code prevail over a stale summary here. At a new chat or slice, compare branch,
`HEAD`, the local `origin/main` tracking ref, and all staged, unstaged, and
untracked changes before editing; do not reset work to match this page.

## Current checkpoint (2026-09-26)

- Latest accepted delivery: the bounded, read-only configured Claude
  Implementer permission mode fact in
  `6050203da62fe7b2e02664b8fd246a0a400166c5`, built on parent
  `89575350d6b1ac2ed347b6246da427840b83fcc0` (`main`/`origin/main` at
  the start of this slice). Checkout clean at delivery. Codex recorded technical
  GO after publication, with the premature commit/push noted as a process
  exception in [planner-handoff.md](planner-handoff.md).
- Delivered behavior: the implementation-attempt status query, API response,
  and cockpit (`ImplementationAction`) now expose a new `ConfiguredPermissionMode`
  / `configuredPermissionMode` fact — the fixed `acceptEdits` CLI permission
  mode the existing, unchanged `ClaudeImplementationAdapter` always passes for
  this role. It is derived, never persisted: `GetImplementationAttemptStatusQueryHandler`
  returns it only when the attempt's own `AgentRole.Implementer`,
  `AgentProvider.ClaudeCode`, `AgentPermissionProfile.WorkspaceEditOnly`, and
  `AgentAdapterContractVersion == "claude-implementation-v1"` all agree;
  otherwise it is `null`/`Unknown`, exactly like every other assignment fact
  on this status. `ImplementationAction` renders it beside the existing
  provider/role/permission-profile/adapter-contract line with the same
  fail-closed sentinel-matching convention already used for those facts,
  labeled "Configured permission mode" — never claimed as provider-observed
  effective behavior, mode availability, or invocation eligibility. No adapter
  argument, provider policy, authorization, claim/dispatch behavior, database
  schema/migration, or other Claude role changed. See the new "Configured
  Claude Implementer permission mode" subsection of
  [run-cockpit-specification.md](../product/run-cockpit-specification.md).
- Checks actually run: full .NET suites green (Domain.Tests 524/524,
  Application.Tests 985/985 incl. 2 new cases for the coherent and
  mismatched-adapter-contract-version paths, Infrastructure.IntegrationTests
  440/441 with 1 pre-existing unrelated skip, Api.IntegrationTests 296/296
  incl. 1 new mismatched-contract-version case, Architecture.Tests 9/9);
  frontend 605/605 tests (existing `ImplementationAction` assertions extended
  for the valid, absent, and mismatched-sentinel cases), `tsc -b` clean,
  `oxlint` exited 0 with the same 20 pre-existing warnings (0 new), `vite
  build` production build passed, `git diff --check` reported no whitespace
  errors. The NSwag client was regenerated via `dotnet build
  src/backend/DevalCopilot.Api` (its own MSBuild target runs on every Debug
  build); the resulting `api-client.ts` diff is additive only (one new
  optional field on `ImplementationAttemptStatusResponse`).
- No provider API access, persistence, adapter, threshold, authorization
  override, claim eligibility, or provider-account-usage change of any kind. The
  provider-allowance evidence slice remains closed without implementation:
  neither CLI offered a proven safe account-allowance observation contract.
  Do not revive it as `Unknown`-only scaffolding or direct authenticated API
  access without a new approved decision.
- Next action: Codex investigates remaining Increment 4 runtime-control
  contracts before selecting another bounded slice. Under the corrected
  workflow in [AGENTS.md](../../AGENTS.md), Claude submits its next completed
  diff without commit or push; Codex GO must precede both. No next execution
  slice has been selected yet.

## Open risks

- Provider account-usage thresholds and token budgets remain unenforced;
  provider-reported per-attempt token usage is not account allowance or cost.
  The provider-session-resume, runtime-control, and context-window/compaction
  roadmap items also remain open. See [Increment 4](mvp-delivery-plan.md).
- [ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md)
  and [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)
  enforce claim-count and reserved-time ceilings, not actual provider wall
  time, token, or account allowance; neither has a human override.
- Gemini execution remains disabled by
  [ADR-0011](../decisions/0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md).
  Raw linked artifact inspection beyond the bounded collaboration-evidence
  drill-down is not implemented; see [the cockpit specification](../product/run-cockpit-specification.md).

## History and closure rule

Older delivery details remain in Git (for example `git show
679071b:docs/roadmap/current-work.md`) and in the linked product contracts;
they are intentionally not copied into this entry point. The previous
provider-separated token-usage delivery is `e3805a8` with correction
`281b3e1`. Every future substantive slice commit updates this page with its
resolvable delivery identity, remaining uncommitted work, checks actually run,
open risks, and next action. The executor reports delivery facts; only the
planner/reviewer records acceptance and future slice decisions.
