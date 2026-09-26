# Planner/reviewer handoff

This is the short decision checkpoint for a new Codex planner/reviewer chat,
not a delivery ledger or an execution authorization. The
[shared handoff](current-work.md) records delivered facts;
[the roadmap](mvp-delivery-plan.md) records planned outcomes; accepted
[ADRs](../decisions/README.md) own architectural decisions. Verify this page
against Git and code before relying on it. Historical decision detail remains
in Git rather than accumulating here.

## Current decision (2026-09-26)

- The owner assigns Codex planning, architecture, review, implementation
  prompts, and final acceptance; Claude is the bounded executor. The owner
  permits Codex to plan later slices without a separate planning request, but
  each executor slice still needs distinct owner authorization. See
  [AGENTS.md](../../AGENTS.md).
- **GO:** the bounded one-Agent-claim-slot-remaining warning delivered in
  `e79f03e`, parent `b3cc8ec` (the owner's recorded approval). Codex
  independently inspected the frontend/spec diff, ran the full frontend suite
  (605/605), TypeScript/production build, and lint (exit 0, existing warnings).
  The warning reads the original nullable count/flag fields, rejects stale or
  incoherent evidence, and cannot grant a claim; the exhausted banner and
  existing vetoes are unchanged. No backend/API files changed, so their suites
  were not rerun for this slice. This GO does not close Increment 4.
- The prior accepted collaboration-input provenance delivery is `b812263`
  with corrections `a546318` and `679071b`; its review trail remains in Git.
- **No next execution slice is approved.** The next planner investigation is
  provider runtime-control contracts (model, effort, permission, and context
  capability), which still needs a safe, bounded proposal verified against
  provider-supported surfaces. Do not convert this investigation into an
  executor prompt or `Unknown`-only scaffolding by assumption.
- The Provider allowance evidence slice remains closed without delivery:
  neither local CLI exposed a proven safe, authoritative, machine-readable
  account-allowance observation contract. Do not build `Unknown`-only
  persistence/UI scaffolding or substitute direct authenticated API calls
  without a new owner-approved architecture decision.

## Decision and preflight protocol

1. At a new chat or slice, read [AGENTS.md](../../AGENTS.md), the
   [shared handoff](current-work.md), and this page; compare branch, HEAD,
   local `origin/main`, and staged/unstaged/untracked changes. Investigate
   discrepancies; never reset or discard work to match prose. Within an
   uninterrupted review/correction round, reuse already-read instructions.
2. On delivery, inspect the actual diff, relevant code/tests, roadmap, and
   accepted ADRs. State GO or actionable NO-GO and distinguish independent
   checks from executor-reported ones. If NO-GO, keep the current slice open
   for a bounded correction; do not start a different one. After GO, prepare
   the next small, bounded **unapproved** proposal in the same review pass
   when a safe candidate is identifiable. This planning needs no fresh owner
   permission; execution still does.
3. A proposal records objective, exclusions, safety/stop gates, acceptance
   evidence, and a short English executor prompt or a link to its full spec.
   When the owner approves execution, record the decision, commit it, then
   put the **exact post-approval HEAD SHA**, branch, and expected clean/dirty
   staged/unstaged/untracked state in the prompt sent to Claude. The executor
   verifies these once before editing and stops on a material discrepancy.
   The prompt also explicitly requires Claude to push the completed slice to
   `origin/main`, verify the remote commit, and report any push failure without
   rewriting or reconciling remote history on its own.
   The approval commit cannot contain its own SHA; the subsequent prompt can.
   A GO or an unapproved proposal never approves the next executor slice.
4. The executor updates `current-work.md` with delivery facts in the
   substantive commit. The planner updates this decision checkpoint on
   approval or review decisions. Keep both entry points short, with one
   current next action; older details belong in Git and linked contracts.

For a new Codex chat: "Resume as DevalCopilot's planner/reviewer. Follow
AGENTS.md, verify Git against current-work.md, read planner-handoff.md, and
address my request from code and accepted ADRs. Do not treat an executor
report as architecture approval or authority to start another slice."
