# Current work and cross-chat handoff

This is the short delivery checkpoint, not a transcript or an execution
approval. [The roadmap](mvp-delivery-plan.md),
[engineering context](../engineering-context.md),
[architecture](../architecture/README.md), and
[accepted ADRs](../decisions/README.md) retain their own authority. Git and
code prevail over a stale summary here. At a new chat or slice, compare branch,
`HEAD`, the local `origin/main` tracking ref, and all staged, unstaged, and
untracked changes before editing; do not reset work to match this page.

## Current checkpoint (2026-09-27)

- Latest accepted delivery: commit
  `1d9f6b4876b43b0585ed8422d4de653966e8e8dc`, the bounded, read-only
  configured Claude Implementer session persistence fact, built on parent
  `a114f29064befa25f95541678fa73db6da2ab331`. Remote `origin/main`
  verified at the delivered commit; checkout clean at delivery. Codex reviewed
  the uncommitted diff across two correction rounds and recorded **GO**; see
  [planner-handoff.md](planner-handoff.md).
- Delivered behavior: the implementation-attempt status query, API response,
  and cockpit (`ImplementationAction`) now expose a new
  `ConfiguredSessionPersistence` / `configuredSessionPersistence` fact
  alongside the existing `ConfiguredPermissionMode` fact — `"Disabled"` (the
  current `ClaudeImplementationAdapter`'s existing, unchanged
  `--no-session-persistence` argument) only when the attempt's own provider,
  role, permission profile, and adapter contract version all agree with the
  current supported implementation path; otherwise `null`/`Unknown`, with the
  existing fail-closed `agent_attempts.invalid_assignment` error preserved
  for invalid or absent assignment metadata (never a success value reporting
  `Unknown`). No adapter argument, session behavior, persistence schema,
  provider-session identifier exposure, resume/open/fork UI, authorization,
  claim/dispatch logic, or other Claude role changed;
  `ProviderRuntimePreflight.Sessions` is untouched. Documented in the new
  "Configured Claude Implementer session persistence" subsection of
  [run-cockpit-specification.md](../product/run-cockpit-specification.md),
  which also states plainly that this fact is separate from DevalCopilot's
  own durable attempt history. Two pre-existing, shared cross-role API
  disclosure guards (`AgentProcessExecutionEvidenceEndpointTests` and
  `AgentTokenUsageEndpointTests`) were narrowed during review to permit
  exactly this one root-level property with value `Disabled`/`null`, scoped
  to the implementation-status route only and verified JSON-root-level via
  `JsonDocument`, with negative tests proving a nested occurrence, an
  unexpected value, or the same literal on a non-permitted route is still
  rejected; every other disclosure guard is unchanged.
- Checks actually run: full .NET suites green (Domain.Tests 524/524,
  Application.Tests 986/986, Infrastructure.IntegrationTests 440/441 with 1
  pre-existing unrelated skip, Api.IntegrationTests 307/307, Architecture.Tests
  9/9); frontend 605/605 tests, `tsc -b` clean, `oxlint` exited 0 with the
  same 20 pre-existing warnings (0 new), `vite build` production build
  passed, `git diff --check` reported no whitespace errors. The NSwag client
  was regenerated via `dotnet build src/backend/DevalCopilot.Api`; the
  resulting `api-client.ts` diff is additive only (one new optional field).
- No provider API access, persistence, adapter, threshold, authorization
  override, claim eligibility, or provider-account-usage change of any kind. The
  provider-allowance evidence slice remains closed without implementation:
  neither CLI offered a proven safe account-allowance observation contract.
  Do not revive it as `Unknown`-only scaffolding or direct authenticated API
  access without a new approved decision.
- Next action: Codex investigates remaining Increment 4 provider runtime
  controls against code, accepted ADRs, and provider contracts before selecting
  another bounded slice; none is selected yet.

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
