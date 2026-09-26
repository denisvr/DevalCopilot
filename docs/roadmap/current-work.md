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

- Latest accepted delivery: recorded collaboration-input provenance, delivered
  in `b812263af8995b717f6e0bd7d0e9c774be26e187` and corrected in
  `a5463187216b5632920345e5cb9023b3b226ba5b` and
  `679071b585ef05025b1154e56ffd7ff8f2ba9fb4`. Codex reviewed the final
  correction and recorded **GO** in [planner-handoff.md](planner-handoff.md).
  The branch was `main` and the checkout clean at review. `origin/main` was a
  stale local tracking ref at `c914c19`; verify its live state before relying
  on it. The executor reported no remaining uncommitted work; this separate
  process-documentation update is not part of the delivered feature.
- Delivered behavior: the existing collaboration-message evidence drill-down
  shows a bounded, ordered list of the producing Agent attempt's durable
  `AttemptInputMessage` references. Same-run ownership and sequence coherence
  fail closed; only a Planner's zero-row set is `Empty`; all other zero-row
  roles are `Invalid`. A valid 11-input review-correction set truthfully shows
  10 entries and an omission signal. Missing attempt/actor role or provider
  returns `AttemptLinkBroken`. This is recorded collaboration input identity,
  not a complete prompt, provider transcript, or resumable native session.
  See [the collaboration protocol](../architecture/agent-collaboration-protocol.md).
- Checks actually run by the executor across the delivery and corrections:
  Release build 0 warnings/0 errors; Domain 524/524; Application 983/983;
  Infrastructure.IntegrationTests 440/441 (one pre-existing capability skip);
  Api.IntegrationTests 295/295; Architecture 9/9; EF reported no pending model
  changes; `dotnet format --verify-no-changes` and `git diff --check` clean;
  NSwag byte-identical after corrections, with no generated enums. The initial
  frontend delivery passed 578/578 tests, TypeScript, lint (0 new warnings),
  and production build; the backend-only corrections did not rerun frontend.
  Codex independently inspected the delivery/correction diffs and ran focused
  Application (35/35) and API (4/4) tests on the final state; it did not rerun
  the executor's full matrix.
- No migration, adapter, provider-account observation, native-session resume,
  budget enforcement, or new runtime-control contract was delivered. The
  provider-allowance evidence slice remains closed without implementation:
  neither CLI offered a proven safe account-allowance observation contract.
  Do not revive it as `Unknown`-only scaffolding or direct authenticated API
  access without a new approved decision.
- Next action: the owner approved the bounded frontend-only one-Agent-claim-
  slot warning in [planner-handoff.md](planner-handoff.md). Claude may execute
  only that slice after checking the exact Git preflight in Codex's prompt;
  Codex retains GO/NO-GO and final acceptance.

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
