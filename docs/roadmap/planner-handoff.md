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
- The latest Codex-accepted slice is the provider-separated run token-usage
  projection (`e3805a8`), with its frontend correction (`281b3e1`). Codex
  reviewed the correction and recorded **GO** below.
- The owner-authorized **Provider allowance evidence** execution slice stopped
  at its explicit evidence gate. Claude reported no implementation; Codex
  verified no code, test, or migration changes in the current checkout. The
  slice is closed without delivery or acceptance.
- Provider account usage remains unobserved and unenforced. The remaining
  [Increment 4 deliverable and exit criterion](mvp-delivery-plan.md) are open;
  preserve [ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md)
  and later accepted decisions.
- **Approved for Claude execution by the owner:** the bounded, read-only
  collaboration-input provenance slice below. Approval is for this slice only;
  Codex still owns review and final acceptance.
- The owner has granted Codex standing permission to plan and propose later
  slices without requesting permission to do the planning. A later proposal is
  not execution approval; Claude still needs a separately approved bounded
  prompt before starting it.

## Approved execution slice: recorded collaboration-input provenance

- Objective: let an owner inspect the ordered, durable collaboration-message
  inputs recorded for the exact Agent attempt behind a provider-observed
  timeline card. Extend the existing evidence query/API and on-demand cockpit
  drill-down; reuse `AttemptInputMessage`, not a provider transcript or a new
  persistence model. Label these as **recorded collaboration inputs**, not the
  complete prompt, complete context manifest, or a resumable session.
- Boundaries: read-only; no new migration, adapter, CLI probing, raw artifact
  access, native-session open/resume, automatic continuation, changed claim or
  dispatch policy, token/account budget, or generic handoff framework. A
  planning attempt legitimately has no `AttemptInputMessage` rows. Other roles
  must not render a missing or incoherent set as complete. This slice advances
  the Increment 4 structured/visible collaboration and context-provenance
  outcome; it does not close its provider-session-resume or account-usage exit
  criteria.
- Safety gates: resolve the producing attempt through the current run-scoped,
  role/provider-coherent evidence path; resolve each input message in the same
  run and preserve the stored sequence. Do not leak a cross-run or unresolved
  input reference. Apply a small explicit result cap with an honest omission
  signal, and fail closed on gaps, duplicates, or missing/foreign messages.
  Preserve the existing `NoAgentEvidence`/`AttemptLinkBroken` semantics and
  exclude native session IDs, raw prompts, context-manifest content, secrets,
  and raw provider output. Do not imply an input is visible in the currently
  loaded 100-message timeline window when it is not.
- Acceptance evidence: focused Application and API tests for correct ordered
  same-run inputs, empty planning input, cross-run/missing/gapped input,
  bounded overflow, and unchanged existing evidence statuses; frontend tests
  for honest labels, unavailable/omitted states, and a timeline-window miss.
  Run relevant backend/frontend build, test, formatting, generated-client, and
  diff checks under the engineering contract. Update `current-work.md` in the
  substantive delivery commit and report the exact commit/checks/risks. Codex
  reviews and decides GO/NO-GO separately.
- Executor prompt: "Implement the planner-approved
  recorded collaboration-input provenance slice in the existing
  collaboration-message attempt-evidence drill-down. Read the required project
  entry documents at this new slice boundary, verify Git and this recorded
  approval, then follow the objective, exclusions, safety
  gates, and acceptance evidence above. Use only durable `AttemptInputMessage`
  links and same-run collaboration messages; never claim a complete prompt or
  native-session resume. Stop and report if the persisted input contract cannot
  support a truthful, bounded projection. Validate the change, update
  `docs/roadmap/current-work.md` in the substantive commit, and report facts;
  do not approve another slice."

## Review GO: provider-separated run token usage (`e3805a8` + `281b3e1`)

- The backend partitions the existing dispatched-attempt evidence into Codex,
  Claude Code, and Unattributed in the existing single pass; the run-wide
  summary remains intact. Focused Application (8), API (3), and frontend (66)
  tests passed independently on the initial review; that review found two
  frontend presentation defects and recorded NO-GO.
- Commit `281b3e1` resolves both findings within the same slice: an own-key-safe
  `Map` lookup labels prototype-colliding and other unknown attributions
  honestly, and provider buckets use provider-scoped total/partial wording
  while the run-wide copy and completeness rules remain unchanged. Codex
  inspected the correction diff and independently ran the full frontend suite
  (563/563), TypeScript/production build, and lint (exit 0, existing warnings).
  The correction touched no backend, API, adapter, persistence, or threshold
  files; backend tests were not rerun after this frontend-only commit.
- **GO** for this bounded read-only slice. It does not implement token budgets
  or provider account-usage guardrails; Increment 4 remains open. No next
  execution slice is approved by this review.

## Closed without delivery: Provider allowance evidence

- The approved gate required a proven, safe, authoritative, machine-readable
  allowance-observation contract reachable within the existing local CLI
  boundary. Neither provider contract was established. Interactive usage
  displays are not a documented headless adapter interface; per-attempt token
  usage and authentication state are not account allowance. An app-private
  Codex binary path is not an approved discovery contract under the
  [collaboration protocol](../architecture/agent-collaboration-protocol.md).
- Decision: **NO-GO for implementation under this slice**. Do not add
  `Unknown`-only persistence/API/UI scaffolding, and do not substitute API
  rate-limit headers for CLI subscription allowance. Direct authenticated API
  integration would be a materially different credential, accounting, and
  architecture decision requiring owner approval and an ADR before design or
  execution. Revisit only with a verified provider-supported contract or an
  explicitly approved alternative.
- This is a compliant stop at the pre-implementation gate, not a failed code
  review. No tests or build are claimed for an undelivered slice. Codex must
  inspect remaining Increment 4 controls before proposing another bounded
  slice; the owner must authorize execution separately.

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
   and acceptance evidence here. The owner has granted standing permission to
   plan later slices without asking first. Include a short executor prompt or
   a link to a longer approved specification; do not turn this page into a
   prompt archive. Mark proposals as unapproved until the owner authorizes
   execution. Keep the shared handoff's next-action wording aligned without
   duplicating delivery history.
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
