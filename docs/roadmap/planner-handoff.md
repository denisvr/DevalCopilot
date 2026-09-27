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
- **Selected next execution slice:** expose the current Claude Implementer
  adapter's disabled provider-session persistence as an attempt-scoped,
  read-only configured fact, under the limits below. This does not implement
  resume or claim provider-session availability.
- **GO, pending publication:** Codex reviewed the uncommitted configured
  Claude Implementer session-persistence diff on `a114f29`. The two earlier
  disclosure-guard findings are corrected: only the implementation-status
  route permits exactly one root-level `configuredSessionPersistence` property
  with `Disabled` or null; nested, other-route, unexpected-value, and actual
  session disclosures remain rejected. The product spec retains the invalid-
  assignment error. Codex independently passed API 307/307, focused
  Application 8/8, focused frontend 19/19, TypeScript typecheck, and diff
  check; broader suites are executor-reported. Claude may make only the
  mechanical `current-work.md` delivery-status cleanup described in the GO
  handoff, then commit this reviewed slice and push `main` fast-forward to
  `origin/main`. Any substantive post-GO change returns for review.
- The Provider allowance evidence slice remains closed without delivery:
  neither local CLI exposed a proven safe, authoritative, machine-readable
  account-allowance observation contract. Do not build `Unknown`-only
  persistence/UI scaffolding or substitute direct authenticated API calls
  without a new accepted architecture decision.

## Selected slice: configured Claude Implementer session persistence

- Objective: on the existing implementation-attempt status and cockpit, show
  that the current `ClaudeImplementationAdapter` passes
  `--no-session-persistence`. The [Claude CLI reference](https://code.claude.com/docs/en/cli-reference)
  says this print-mode flag prevents saving the provider session and therefore
  prevents resuming it. Label the fact as **configured**, not as a live
  provider-observed outcome. DevalCopilot's own durable attempt history and
  any bounded provider-session identifier remain distinct.
- Derive `Disabled` only when the attempt's own provider `ClaudeCode`, role
  `Implementer`, permission profile `WorkspaceEditOnly`, and adapter contract
  `claude-implementation-v1` agree with the unchanged current adapter. For no
  attempt or a valid but mismatched/historical assignment, return
  null/`Unknown`; retain the existing fail-closed error for an invalid or
  absent assignment. Do not infer a historical invocation from today's
  adapter. Keep the host-wide `ProviderRuntimePreflight.Sessions` unchanged.
- Exclusions: no adapter arguments, persistence schema, provider-session ID
  exposure, session open/resume/fork UI, session lookup, provider storage
  access, authentication, authorization, claim/dispatch logic, model/effort
  selection, compaction, account allowance, Codex, Gemini, or other Claude
  roles. No authenticated provider invocation or direct provider API call.
- Stop gates: report a blocker if the installed adapter no longer passes the
  flag, if the provider contract no longer supports the stated meaning, if
  the coherent attempt mapping cannot fail closed, or if implementation
  requires changing session behavior, policy, or persistence. Do not broaden
  scope to make resume possible.
- Acceptance evidence: focused Application/API/frontend tests for coherent,
  no-attempt, and mismatched/unknown cases; truthful UI wording; additive
  NSwag regeneration if the response changes; affected .NET tests, frontend
  tests/typecheck/lint/build, and `git diff --check`. Clarify the product
  specification and prepare `current-work.md` with actual checks and risks.
  Present the complete diff uncommitted and unpushed for Codex GO/NO-GO.

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
