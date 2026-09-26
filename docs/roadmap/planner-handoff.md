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
  The subsequent provider-separated run token-usage projection was delivered
  in `e3805a8` but has a **NO-GO** review decision pending a narrow frontend
  correction; see below. Do not treat delivery as acceptance.
- The owner-authorized **Provider allowance evidence** execution slice stopped
  at its explicit evidence gate. Claude reported no implementation; Codex
  verified no code, test, or migration changes in the current checkout. The
  slice is closed without delivery or acceptance. No next slice is approved.
- Provider account usage remains unobserved and unenforced. The remaining
  [Increment 4 deliverable and exit criterion](mvp-delivery-plan.md) are open;
  preserve [ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md)
  and later accepted decisions.

## Review NO-GO: provider-separated run token usage (`e3805a8`)

- The backend partitions the existing dispatched-attempt evidence into Codex,
  Claude Code, and Unattributed in the existing single pass; the run-wide
  summary remains intact. Focused Application (8), API (3), and frontend (66)
  tests passed independently during review. No migration or provider adapter
  was added. These observations do not accept the slice yet.
- Correct two frontend presentation defects within this slice: (1) an
  unrecognized `attribution` such as `toString`, `constructor`, or `__proto__`
  currently resolves through inherited `Object` properties rather than the
  promised "Unrecognized provider" label; use an own-property-safe lookup and
  test those values; (2) a provider bucket currently reuses "Run token total"
  and "not the run total" wording, conflating a bucket's subtotal with the
  separate run-wide summary. Use provider-scoped wording while retaining the
  existing run-wide copy, completeness semantics, and partial-vs-total rule.
- Do not expand this correction into budgets, provider account usage, adapters,
  persistence, or another slice. Claude reports a correction commit and checks;
  Codex reviews it for GO/NO-GO. No next slice is approved.

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
