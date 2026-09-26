# Planner/reviewer handoff

This is the short decision checkpoint for a new Codex planner/reviewer chat.
It is not a transcript, delivery ledger, or authority to implement a slice.
The [shared handoff](current-work.md) owns delivered and uncommitted status;
[the roadmap](mvp-delivery-plan.md) owns planned outcomes; accepted
[ADRs](../decisions/README.md) own architectural decisions. Git and code
prevail over any stale summary here.

## Current decision state (2026-09-26)

- The project owner assigns Codex planning, architecture, review, implementation
  prompts, and final acceptance. Claude is the bounded execution agent. This
  assignment changes only when the owner explicitly changes it; see
  [AGENTS.md](../../AGENTS.md).
- The latest Codex-accepted slice is candidate-specific invocation-time fit.
  Verify its delivered commit and clean-tree claim in the
  [shared handoff](current-work.md) and Git rather than copying a SHA here.
- The project owner authorized execution of the planner-proposed **Provider
  allowance evidence** slice by sending its bounded prompt to Claude. Codex
  approves that slice for execution under the boundaries below. It is not yet
  delivered or accepted; no implementation review is currently pending.
- This decision addresses the account-usage observation part of the remaining
  [Increment 4 deliverable](mvp-delivery-plan.md). The shared handoff's open
  risks and the current code confirm that provider account usage is not yet
  collected or enforced. Preserve [ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md)
  and later accepted decisions.

## Approved execution slice: Provider allowance evidence

- Objective: observe and persist bounded, host-scoped allowance snapshots for
  Codex and Claude Code when an authoritative provider contract is verified;
  expose separate read-only provider/window projections in the API and cockpit.
  Keep `Unknown` and `Unavailable` explicit, including freshness and reset
  information when supported. Provider-specific observation belongs in
  Infrastructure behind a provider-neutral Application port.
- Exclusions: no warning or stop threshold, claim-eligibility change, human
  override, token-budget enforcement, Gemini, fallback, parallel executor,
  model/effort/permission selection, context compaction, session resume, or
  Increment 5 work. This slice does not satisfy Increment 4's account-usage
  stop criterion by itself.
- Stop gates: prove each provider's observation contract from authoritative
  evidence before implementing its adapter; never infer account usage from
  per-attempt tokens, process duration, authentication, or an installed CLI.
  If one provider lacks a safe contract, leave it `Unknown` and report that
  limit. If neither provider has one, stop before adding persistence or UI
  scaffolding and report the blocker for planner review. Never read
  undocumented provider storage or persist credentials, cookies, tokens, or
  complete provider payloads. Keep external observation outside EF transactions.
- Acceptance evidence: focused Domain/Application invariant tests, disposable
  SQLite persistence tests, deterministic provider-adapter failure tests,
  authenticated MVC response tests, and frontend tests for separate provider
  windows and honest unknown/stale states. Report formatter, Release build,
  affected tests, migration/model check, architecture tests, generated-client
  drift, frontend type/lint/build checks, and diff review. The executor records
  delivered facts in `current-work.md` in the substantive commit; Codex alone
  reviews and accepts that result.

Executor instruction: implement only this evidence slice under the detailed
prompt already sent by the owner, applying the stop gates above. Report a
blocker if a provider contract or required safety boundary cannot be verified;
do not expand the slice to make the evidence appear available.

## Resume and decision protocol

1. Read the project [AGENTS.md](../../AGENTS.md), the [shared handoff](current-work.md),
   and this page. Compare branch, HEAD, origin divergence, and all staged,
   unstaged, and untracked changes with the handoff before relying on it.
   Investigate discrepancies without resetting or discarding work.
2. For the owner's actual request, inspect relevant source, tests, roadmap
   sections, and ADRs. Treat executor reports as leads to verify, not as
   acceptance or permission to broaden scope. Perform planning and final
   judgment yourself; do not delegate them to the execution agent.
3. If reviewing an implementation, state GO or actionable NO-GO findings from
   the diff and evidence. Distinguish checks run independently from results
   reported by the executor. A GO on one slice does not approve the next.
4. If selecting a slice, record its objective, boundaries, risks, stop gates,
   and acceptance evidence here **only after** the owner asks for that planning
   decision. Include a short executor prompt or a link to a longer approved
   specification; do not turn this page into a prompt archive. Mark proposals
   as unapproved until the owner authorizes execution. Keep the shared
   handoff's next-action wording aligned without duplicating delivery history.
5. On a decision change or accepted delivery, update this page concisely.
   The executor updates delivery facts in `current-work.md`; the planner owns
   this decision state. Link to contracts and tests instead of accumulating
   conversation summaries.

## Starting another Codex chat

Open the chat in this repository and say: "Resume as DevalCopilot's
planner/reviewer. Follow AGENTS.md, verify Git against current-work.md, read
planner-handoff.md, and address my request from the code and accepted ADRs.
Do not infer that an executor report approves architecture or the next slice."
Add the specific question or planning/review task. This prompt locates durable
context; it does not recreate private chat history or replace verification.
