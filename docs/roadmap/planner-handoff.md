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
- **GO:** the bounded one-Agent-claim-slot-remaining warning delivered in
  `e79f03e`. Codex
  independently inspected the frontend/spec diff, ran the full frontend suite
  (605/605), TypeScript/production build, and lint (exit 0, existing warnings).
  The warning reads the original nullable count/flag fields, rejects stale or
  incoherent evidence, and cannot grant a claim; the exhausted banner and
  existing vetoes are unchanged. No backend/API files changed, so their suites
  were not rerun for this slice. This GO does not close Increment 4.
- The prior accepted collaboration-input provenance delivery is `b812263`
  with corrections `a546318` and `679071b`; its review trail remains in Git.
- **Selected next execution slice:** expose the already-configured Claude
  Implementer permission mode as bounded, read-only attempt evidence, under
  the limits below. Codex retains review and acceptance.
- The Provider allowance evidence slice remains closed without delivery:
  neither local CLI exposed a proven safe, authoritative, machine-readable
  account-allowance observation contract. Do not build `Unknown`-only
  persistence/UI scaffolding or substitute direct authenticated API calls
  without a new accepted architecture decision.

## Selected slice: configured Claude Implementer permission mode

- Objective: show the existing fixed `acceptEdits` CLI permission mode as a
  read-only **configured** fact on the Claude Implementer attempt status and
  cockpit. This does not claim that the provider honored the mode, that other
  modes are available, or that another invocation is eligible.
- Derive `acceptEdits` only from coherent durable facts: provider `ClaudeCode`,
  role `Implementer`, permission profile `WorkspaceEditOnly`, and adapter
  contract `claude-implementation-v1`. Otherwise report `Unknown`/null. The
  exact configured mode is evidenced by the current fixed argument list in
  `ClaudeImplementationAdapter`. Keep historical, absent, and malformed
  assignment evidence conservative. This is attempt-scoped evidence; the
  host-wide `ProviderRuntimePreflight` remains `Unknown` for permission mode.
- Exclusions: no adapter argument, provider policy, authorization, claim or
  dispatch behavior, database migration, runtime capability catalog, model or
  effort selection, session resume, context/compaction, account allowance,
  Codex, Gemini, or other Claude role changes. No authenticated provider
  invocation or direct provider API access.
- Stop gates: report a blocker if the current adapter contract no longer
  justifies the exact mapping, if mapping requires policy or persistence
  changes, or if a provider/role/profile/version mismatch cannot fail closed
  without misleading the user. Do not broaden the slice to repair another
  runtime control.
- Acceptance evidence: focused Application/API/frontend tests for the valid
  mapping, absent and mismatched facts, truthful wording, and safe API fields;
  regenerate the NSwag client if the response changes. Run affected .NET
  tests, frontend typecheck/tests/lint/production build, and `git diff --check`.
  Clarify the relevant product/architecture contract only where needed for the
  new fact. Update `current-work.md` in the substantive delivery commit, push
  `main` to `origin/main` normally, verify the remote commit, and report it.

## Decision and preflight protocol

1. At a new chat or slice, read [AGENTS.md](../../AGENTS.md), the
   [shared handoff](current-work.md), and this page; compare branch, HEAD,
   local `origin/main`, and staged/unstaged/untracked changes. Investigate
   discrepancies; never reset or discard work to match prose. Within an
   uninterrupted review/correction round, reuse already-read instructions.
2. On delivery, inspect the actual diff, relevant code/tests, roadmap, and
   accepted ADRs. State GO or actionable NO-GO and distinguish independent
   checks from executor-reported ones. If NO-GO, keep the current slice open
   for a bounded correction; do not start a different one. After GO, select
   the next small, bounded slice in the same review pass when a safe candidate
   is identifiable. Ask the owner only when a material choice or authority
   cannot be resolved from the project contract and evidence.
3. A selected slice records objective, exclusions, safety/stop gates, and
   acceptance evidence. The English executor prompt carries the **exact
   current HEAD SHA**, branch, and expected clean/dirty staged/unstaged/
   untracked state. Claude verifies these once before editing and stops on a
   material discrepancy.
   The prompt also explicitly requires Claude to push the completed slice to
   `origin/main`, verify the remote commit, and report any push failure without
   rewriting or reconciling remote history on its own.
4. The executor updates `current-work.md` with delivery facts in the
   substantive commit. The planner updates this decision checkpoint on
   slice selection or review decisions. Keep both entry points short, with one
   current next action; older details belong in Git and linked contracts.

For a new Codex chat: "Resume as DevalCopilot's planner/reviewer. Follow
AGENTS.md, verify Git against current-work.md, read planner-handoff.md, and
address my request from code and accepted ADRs. Do not treat an executor
report as architecture acceptance or authority to start another slice."
