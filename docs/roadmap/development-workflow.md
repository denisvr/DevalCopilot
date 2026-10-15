# Development workflow

This is the mandatory human-operated procedure for planner/reviewer and
executor chats, routed from [AGENTS.md](../../AGENTS.md). It changes no product
orchestration, authority model or instruction capture: ADR-0021 captures only
the root `AGENTS.md` and `CLAUDE.md`, and a link never imports this file.

## Startup routing

- **Shared:** the engineering contract and its task routing,
  [engineering context](../engineering-context.md) and
  [current-work.md](current-work.md); then reconcile Git (branch, `HEAD`,
  local `origin/main`, the live remote when an action depends on it, and
  staged/unstaged/untracked state). Investigate discrepancies; Git and code
  prevail over a stale summary.
- **Planner/reviewer:** also [planner-handoff.md](planner-handoff.md) and the
  planning, review, publication verification and continuity sections below.
- **Executor:** also the selected resolved prompt and the execution, review
  and correction, and publication sections below.
- **Progressive only:** historical snapshots, long records and detailed linked
  standards: headings, latest relevant entries and targeted evidence.
- **Reuse:** during one slice and its correction rounds, reuse already-read
  unchanged instructions; read newly relevant or changed material. Compaction
  alone requires no full reread, handoff update or restart; recover missing
  context from the checkpoints and verify it against Git.

## Chats

- One executor chat holds a selected slice's implementation and all its
  corrections. The next executor chat starts only after the prior slice is
  published and verified, except for a documented recovery transition.
- Planner review and its correction rounds stay in one planner chat when
  practical; a new one suits a distinct outcome or a long context. Never
  restart an active review merely because of compaction.

## Planning and dispatch

- Every planning or review response includes a complete copy-and-paste prompt
  inline; a file link may supplement it but never replaces it. The prompt
  matches the actual decision (bounded execution, review correction,
  authorized publication, evidence gathering, owner decision or chat
  transition) and preserves a pending boundary instead of implying authority.
- A prompt for repository action states the planner-verified expected
  branch, `HEAD` and staged/unstaged/untracked state; the recipient verifies
  them once before acting. Prompts state the objective and boundaries without
  repeating standing rules or copying roadmap, ADR or handoff contents; see
  the [template](slice-start-template.md).
- Validation follows the [validation policy](validation-policy.md): focused
  checks, one proportional final matrix, broadening triggers and retained
  evidence, without every suite by default or repeated passes on unchanged
  inputs. Mandatory CI, security, real-provider integration and release gates
  stay. Dispatched acceptance requirements stay until the planner explicitly
  revises them with evidence.

## Execution and reports

- Return the completed slice unstaged, uncommitted and unpushed, with
  current-work.md updated.
- Report changed and new files, exact check commands and outcomes, fresh
  versus retained versus not-run evidence, risks or blockers and short failure
  excerpts; complete logs only on request. No document embeds its own future
  commit SHA.

## Review and corrections

- The planner decides GO or NO-GO from Git, code, tests and evidence.
- Findings keep stable identifiers across rounds with their disposition and
  evidence; never silently renumber or drop one.
- A NO-GO is a correction of the same slice in the same executor chat,
  uncommitted and unpushed, never permission for a different slice.
- A correction reruns affected checks first, then only the final evidence the
  change could invalidate. Report what was rerun; retained results keep their
  actual scope and limitations.
- GO covers the reviewed substantive diff; a material change returns for review.

## Publication

After explicit GO, the executor in that same turn commits the reviewed diff,
including current-work.md, pushes `main` normally (fast-forward) to
`origin/main`, verifies the live remote ref and reports the SHA. There is no
separate closure-document diff or approval round. A failed push or diverged
remote stops publication without force-push or history reconciliation.

## Publication verification and continuity

After independently verifying publication, the planner recommends exactly one
of `CONTINUE_THIS_CHAT` or `START_NEW_CHAT` and explains why. Cite context usage
only when observed or owner-reported, naming the source; never infer it from
transcript length, account quota, model window limits or compaction. About 60%
reported usage triggers reassessment, not automatic abandonment. Prefer a
fresh planner chat for substantial work when capacity is uncertain.

- `CONTINUE_THIS_CHAT`: assess the next work, then select and dispatch one safe
  bounded slice once, with the complete executor prompt; or state the specific
  blocker with a bounded evidence or owner-decision prompt.
- `START_NEW_CHAT`: give a complete planner opening prompt with the verified
  published baseline, expected Git state, current decisions and constraints,
  authoritative references and remaining objective. The receiving planner
  selects; the outgoing chat dispatches nothing.

Executor continuity is assessed separately; a planner transition does not
move an active executor. A recovery transition keeps the same slice, finding
identifiers, exact Git state, evidence, unresolved work and authority
boundaries; it is not a new grant or a different slice.

## Checkpoints

- [current-work.md](current-work.md) holds execution facts and delivery
  evidence (delivered commit, uncommitted work, checks actually run, open
  risks, next action); the executor updates it when delivering a slice.
- [planner-handoff.md](planner-handoff.md) holds selected scope, rationale and
  review decisions, distinguishing selected work from ideas and verified
  against Git, code, the roadmap and accepted ADRs; the planner updates it
  when selecting a slice or recording a review decision.
- Updates replace the current state instead of prepending entries, stay short,
  link authoritative contracts and ADRs, and follow durable events only, never
  conversation length or compaction. The planner carries delivered facts
  forward in its next selection record.

## Historical snapshots

Exact baseline blobs of commit `ba1d4c2d9e9c5df285777267a7402e75f28e4f87`:
[current-work-history-ba1d4c2.md](current-work-history-ba1d4c2.md) (blob
`ebb007040fa73ab0252511180e53e2e64f5285df`) and
[planner-handoff-history-ba1d4c2.md](planner-handoff-history-ba1d4c2.md) (blob
`dd3bb1d70459322db9147c99c8fe53a01829e38a`). They preserve statements made at
the time, including old pending, uncommitted and publication wording, as
evidence, never current instructions, renewed approval or current status.
They are never edited; their links are frozen. A reference to either
checkpoint's earlier entries, from a snapshot or another document, means that
baseline content. Archiving an issue does not resolve it.

## Rule locations

| Baseline `AGENTS.md` topic | Now |
|---|---|
| Contract, routing, language, trust, ADRs | AGENTS.md |
| Roles, challenge, disagreement | AGENTS.md |
| Review and publication authority, Git discrepancies | AGENTS.md; detail here |
| Startup reading and reuse; chats | Startup routing; Chats |
| Actionable prompts, preflight, validation | Planning and dispatch |
| Reports; corrections; GO/NO-GO | Execution; Review and corrections |
| Commit, push, closure | Publication |
| Next work | Publication verification and continuity |
| Handoff ownership and updates | Checkpoints |
