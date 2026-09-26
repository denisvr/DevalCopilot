# Planner/reviewer handoff

This is the short decision checkpoint for a new Codex planner/reviewer chat,
not a delivery ledger or an approval log. The
[shared handoff](current-work.md) records delivered facts;
[the roadmap](mvp-delivery-plan.md) records planned outcomes; accepted
[ADRs](../decisions/README.md) own architectural decisions. Verify this page
against Git and code before relying on it. Historical decision detail remains
in Git rather than accumulating here.

## Current decision (2026-09-26)

- The owner assigns Codex planning, architecture, review, implementation
  prompts, slice selection, and final acceptance; Claude is the bounded
  executor. Codex may select and dispatch bounded slices without routine owner
  approval, asking only when a material choice or authority needs owner input.
  See [AGENTS.md](../../AGENTS.md).
- **GO (technical review):** `6050203` exposes the fixed Claude Implementer
  `acceptEdits` mode as configured attempt evidence only when provider, role,
  permission profile, and adapter contract agree. Codex inspected the actual
  diff against the adapter and [ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md),
  and independently passed focused Application (7), API (3), and frontend (19)
  tests. The broader suite and build counts in [current-work.md](current-work.md)
  are executor-reported, not independently rerun in this review. This GO does
  not close Increment 4.
- **Process exception:** Claude committed and pushed `6050203` before this GO,
  following the previous prompt's incorrect instruction. Do not rewrite that
  history solely to repair the sequence. Going forward, Codex reviews the
  uncommitted diff first; only an explicit GO authorizes Claude to commit and
  push that reviewed diff. Slice selection needs no routine owner approval,
  but is not commit/push authorization.
- No next execution slice selected in this review. Investigate the remaining
  Increment 4 runtime-control contracts before dispatching another bounded
  slice.
- The Provider allowance evidence slice remains closed without delivery:
  neither local CLI exposed a proven safe, authoritative, machine-readable
  account-allowance observation contract. Do not build `Unknown`-only
  persistence/UI scaffolding or substitute direct authenticated API calls
  without a new accepted architecture decision.

## Decision and preflight protocol

1. At a new chat or slice, read [AGENTS.md](../../AGENTS.md), the
   [shared handoff](current-work.md), and this page; compare branch, HEAD,
   local `origin/main`, and staged/unstaged/untracked changes. Investigate
   discrepancies; never reset or discard work to match prose. Within an
   uninterrupted review/correction round, reuse already-read instructions.
2. Claude presents an uncommitted, unpushed diff and validation evidence.
   Inspect the actual diff, relevant code/tests, roadmap, and accepted ADRs.
   State GO or actionable NO-GO and distinguish independent checks from
   executor-reported ones. NO-GO keeps the current slice uncommitted for a
   bounded correction and re-review. Only an explicit GO for the reviewed diff
   authorizes Claude to commit and push; a material post-GO change requires
   re-review. After verified publication, select the next small, bounded slice
   when a safe candidate is identifiable. Ask the owner only when a material
   choice or authority cannot be resolved from the project contract and
   evidence.
3. A selected slice records objective, exclusions, safety/stop gates, and
   acceptance evidence. The English executor prompt carries the **exact
   current HEAD SHA**, branch, and expected clean/dirty staged/unstaged/
   untracked state. Claude verifies these once before editing and stops on a
   material discrepancy.
   The prompt explicitly forbids commit and push until Codex reviews the
   uncommitted diff and grants GO. After GO, Claude commits the reviewed diff,
   pushes to `origin/main`, verifies the remote commit, and reports any push
   failure without rewriting or reconciling remote history on its own.
4. The executor prepares `current-work.md` with delivery facts before review
   and includes it in the substantive commit only after GO. The planner
   updates this decision checkpoint on slice selection or review decisions.
   Keep both entry points short, with one current next action; older details
   belong in Git and linked contracts.

For a new Codex chat: "Resume as DevalCopilot's planner/reviewer. Follow
AGENTS.md, verify Git against current-work.md, read planner-handoff.md, and
address my request from code and accepted ADRs. Do not treat an executor
report as architecture acceptance or authority to start another slice."
