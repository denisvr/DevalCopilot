# Planner/reviewer handoff

This is the short decision checkpoint for a new Codex planner/reviewer chat,
not a delivery ledger or an approval log. The
[shared handoff](current-work.md) records delivered facts;
[the roadmap](mvp-delivery-plan.md) records planned outcomes; accepted
[ADRs](../decisions/README.md) own architectural decisions. Verify this page
against Git and code before relying on it. Historical decision detail remains
in Git rather than accumulating here.

## Current decision (2026-09-27)

- The owner assigns Codex planning, architecture, review, implementation
  prompts, slice selection, and final acceptance; Claude is the bounded
  executor. Codex may select and dispatch bounded slices without routine owner
  approval, asking only when a material choice or authority needs owner input.
  See [AGENTS.md](../../AGENTS.md).
- **Publication verified:** the reviewed configured Claude Implementer session-
  persistence slice is commit `1d9f6b4876b43b0585ed8422d4de653966e8e8dc`
  (parent `a114f29`). Codex's two correction rounds ended in GO for the
  uncommitted diff. Before GO, Codex independently passed API 307/307,
  focused Application 8/8, focused frontend 19/19, TypeScript typecheck,
  and diff check; broader checks are executor-reported in
  [current-work.md](current-work.md). Post-GO cleanup changed only the shared
  handoff's delivery status. Local `HEAD`, local `origin/main`, and the remote
  `origin/main` were verified equal to the delivered commit, with a clean tree.
  This does not close Increment 4.
- **No next execution slice selected.** Investigate the remaining Increment 4
  provider runtime controls against code, accepted ADRs, and provider-supported
  contracts before selecting another bounded slice. Do not infer that a
  provider session can be resumed from the newly reported configured fact.
- The Provider allowance evidence slice remains closed without delivery:
  neither local CLI exposed a proven safe, authoritative, machine-readable
  account-allowance observation contract. Do not build `Unknown`-only
  persistence/UI scaffolding or substitute direct authenticated API calls
  without a new accepted architecture decision.

## Standing review protocol

Follow [AGENTS.md](../../AGENTS.md): the executor first presents an uncommitted,
unpushed diff; only Codex's explicit GO for that reviewed diff authorizes its
commit and push to `origin/main`. A material change after GO requires re-review.
Slice selection is not publication authorization. Keep this checkpoint focused
on the current decision; completed slice detail remains in Git and the linked
contracts.
