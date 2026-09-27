# Current work and cross-chat handoff

This is the delivery checkpoint, not a transcript or approval. Git and code
prevail over this summary. At a new chat or slice, compare branch, `HEAD`,
local `origin/main`, and staged/unstaged/untracked changes before editing.
See the [roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md),
and accepted [ADRs](../decisions/README.md) for their respective contracts.

## Current checkpoint (2026-09-27)

- Latest accepted substantive delivery: `1d9f6b4876b43b0585ed8422d4de653966e8e8dc`
  (parent `a114f29064befa25f95541678fa73db6da2ab331`), published to
  `origin/main` and accepted by Codex after two correction rounds. No
  uncommitted slice work remained at delivery. Later documentation-only
  checkpoint commits do not change this product delivery.
- The implementation-attempt status and cockpit now show
  `ConfiguredSessionPersistence: Disabled` only for a coherent current Claude
  Implementer assignment matching the unchanged `--no-session-persistence`
  adapter argument. Valid historical/mismatched assignments show `Unknown`;
  invalid or absent assignment metadata retains the existing fail-closed
  error. This is configured evidence, not a provider-observed session outcome
  or DevalCopilot's durable attempt history. The two shared API disclosure
  guards permit only this root-level fact on the implementation-status route;
  negative tests still reject nested, other-route, unexpected-value, and
  actual session disclosures. See the
  [cockpit specification](../product/run-cockpit-specification.md).
- Executor-reported checks: Domain 524/524, Application 986/986,
  Infrastructure 440/441 (one pre-existing skip), API 307/307,
  Architecture 9/9, frontend 605/605; TypeScript typecheck, lint, production
  build, and `git diff --check` passed. NSwag regeneration was additive-only.
  Codex independently passed API 307/307, focused Application 8/8, focused
  frontend 19/19, typecheck, and diff check before GO.
- No adapter behavior, provider-account-usage threshold, persistence schema,
  authorization, claim/dispatch policy, or provider-session resume behavior
  changed. The account-allowance evidence candidate remains closed without
  delivery; no safe CLI observation contract was established.
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
