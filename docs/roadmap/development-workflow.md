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
- Codex and Claude continuity are assessed separately. By default an active
  review and its corrections stay in their current chats; transitions are
  preferred after independently verified publication. Around 60% observed or
  owner-reported context usage triggers reassessment, not a mandatory
  restart; below it, a planner transition needs a concrete documented reason.
  A different slice, transcript length, unknown capacity or compaction alone
  is not a reason; never restart an active review merely because of
  compaction. Detail: [continuity](#publication-verification-and-continuity).

## Planning and dispatch

- The planner must always try to select the largest coherent slice that can
  be safely implemented, validated and reviewed as one bounded outcome.
  Group related work with a shared objective, compatible dependencies and
  explicit acceptance criteria to reduce executor round trips and owner
  copy-and-paste between chats. A slice is not limited by an arbitrary file,
  section or task count. Do not split a coherent outcome merely to make each
  dispatch smaller; when a smaller slice is necessary, state the concrete
  dependency, uncertainty, authority, validation, rollback or context constraint.
  Size never overrides scope, safety, role ownership or review quality.
- Minimize routine owner intervention. Resolve ordinary planning and execution
  choices from the contract and available evidence, inspect repository evidence
  directly, and bundle related instructions or corrections into one actionable
  prompt. Ask the owner only for a material unresolved choice, missing authority
  or information that cannot be obtained independently. This does not add
  automatic chat coordination or remove explicit GO and publication checks.

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
of `CONTINUE_THIS_CHAT` or `START_NEW_CHAT`, explains why and gives the
matching complete prompt below. This human-operated policy changes no product
behavior:

- Assess Codex and Claude continuity separately. Keep an active review and its
  corrections together by default; prefer transitions after independently
  verified publication.
- Around 60% observed or owner-reported context usage triggers reassessment.
  It is neither a mandatory restart nor the only reason to transition. Above
  about 60%, assess whether a transition would help; do not restart
  automatically.
- A planner transition below 60% requires a concrete documented reason, such
  as repeated loss of governing constraints after targeted recovery, or a
  deliberate owner instruction. Transcript length, unknown capacity, a
  different slice or compaction alone is insufficient.
- Cite context usage only when observed or owner-reported, naming the source.
  Never invent a percentage, extrapolate an earlier report, or confuse context
  usage with account quota or model window size.

Each recommendation carries its prompt:

- `CONTINUE_THIS_CHAT`: assess the next work, then select and dispatch one safe
  bounded slice once, with the complete executor prompt; or state the specific
  blocker with a bounded evidence or owner-decision prompt.
- `START_NEW_CHAT`: give a complete planner opening prompt with the verified
  published baseline, expected Git state, current decisions and constraints,
  authoritative references and remaining objective. The receiving planner
  selects the next slice; the outgoing chat dispatches nothing, so no
  competing work exists.

Executor continuity is assessed separately under the one executor chat per
slice rule in [Chats](#chats); a planner transition does not move an active
executor. A recovery transition keeps the same slice, finding identifiers,
exact Git state, evidence, unresolved work and authority boundaries; it is not
a new grant or a different slice.

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
`dd3bb1d70459322db9147c99c8fe53a01829e38a`). Exact baseline blobs of commit
`0a57216c2a8f7e6d00228e6d0a315d25f526a180`:
[current-work-history-0a57216.md](current-work-history-0a57216.md) (blob
`7276c03eefd9a01273c8d746a825239c405a73a2`, 11601 bytes, SHA-256
`926e175b1e09514e4efc6e5d12e5cff2a733364527a082b6f46f008fd0b207bc`) and
[planner-handoff-history-0a57216.md](planner-handoff-history-0a57216.md) (blob
`59c70e1add64aa4cbeeae789b8c0f9a9f9ed9532`, 25586 bytes, SHA-256
`7dc1ae7dac4678f01a40b18f419472589f217a21aa5b9f97d320b26e413e7032`; the
WF-03N final GO checkpoint). Exact baseline blobs of commit
`e614c79553c92e1964d57fa9a4d9d5dbbfadb2b2`:
[current-work-history-e614c79.md](current-work-history-e614c79.md) (blob
`84b61b2800b238f49390ba308577d2c19841de71`, 8757 bytes, SHA-256
`2b8e861daa62f61b60fdb8f804aeff2cbc4d8032aafe4215cce0742993ded53a`) and
[planner-handoff-history-e614c79.md](planner-handoff-history-e614c79.md) (blob
`f3b520dd7ae5a8015c58a43653408fba0e083350`, 11301 bytes, SHA-256
`a7815594ce4ef465aee9cf25919e4e6015ec9d99f1767098557f55f3fdf0a297`; the
WF-04 final GO checkpoint). Snapshots are progressive references, not
mandatory startup reading. They preserve statements made at
the time, including old GO, pending, uncommitted and publication wording, as
evidence, never current instructions, renewed approval or current status.
They are never edited; their links are frozen. A reference to either
checkpoint's earlier entries, from a snapshot or another document, means that
baseline content: within a snapshot, its own baseline; elsewhere, the latest
pair whose baseline contained the entry, unless the reference names another
commit. Entries recorded only between pinned baselines are in the
checkpoints' Git history. Archiving an issue does not resolve it.

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
