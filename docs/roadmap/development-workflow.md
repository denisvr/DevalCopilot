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
  context from the checkpoints and verify it against Git
  ([recovery](#recovery)).

## Chats

- One executor chat holds a selected slice's implementation and all its
  corrections. The next executor chat starts only after the prior slice is
  published and verified, except for a documented recovery transition.
- Codex and Claude continuity are assessed separately under
  [continuity](#publication-verification-and-continuity); by default an
  active review and its corrections stay in their current chats.

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
- Each consolidated actionable decision includes one complete inline
  copy-and-paste prompt: dispatch, correction, authorized publication,
  evidence gathering, owner decision or chat transition. A file link may
  supplement it but never replaces it, and the owner never assembles
  instructions from several responses. Progress updates, questions and
  technical discussion do not reissue an unchanged prompt. When a decision
  changes, the new complete prompt explicitly supersedes the earlier
  actionable version. The prompt actually dispatched preserves the verified
  preflight, objective, scope, acceptance and authority boundaries, and keeps
  a pending boundary instead of implying authority.
- A prompt for repository action states the planner-verified expected
  branch, `HEAD` and staged/unstaged/untracked state; the recipient verifies
  them once before acting. Prompts state the objective and boundaries without
  repeating standing rules or copying roadmap, ADR or handoff contents; see
  the [template](slice-start-template.md).
- Validation follows the [validation policy](validation-policy.md): focused
  checks, one proportional final matrix, broadening triggers and proportional,
  retained evidence, without every suite by default or repeated passes on
  unchanged inputs. Mandatory CI, security, real-provider integration and
  release gates stay. Dispatched acceptance requirements stay until the
  planner explicitly revises them with evidence.

## Execution and reports

- Return the completed slice unstaged, uncommitted and unpushed, with
  current-work.md updated as the implementation record for review
  ([recovery](#recovery)).
- Report changed and new files, exact check commands and outcomes, fresh
  versus retained versus not-run evidence, risks or blockers and short failure
  excerpts; complete logs or file contents only on request. No document embeds
  its own future commit SHA.

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

The publication GO names the approved-content identity in a scheme the
planner chooses: a manifest when its additional detail is justified or, for
routine bounded slices, the lightweight tree below; one suffices. Either way
the GO states the expected parent and approved paths; the tree scheme adds
the expected tree ID, the manifest scheme its path and approved hash.

- For the tree scheme, after reviewing the final diff, including any planner
  review edits, the planner computes the expected tree in an isolated
  temporary index from the verified parent and the explicit approved paths
  (`GIT_INDEX_FILE=<temp>`: `git read-tree <parent>`, `git add -- <paths>`,
  `git write-tree`), leaving the real index and unrelated work untouched.
  These values go in the GO prompt or external review record, never inside
  a file whose identity they determine.
- The executor verifies the preconditions, stages only the approved paths,
  verifies the staged content against the selected scheme (the staged tree
  from `git write-tree`, or the manifest's entries), commits, verifies the
  committed content against the same scheme and the expected sole parent,
  pushes normally and verifies the live remote ref.
- The planner's independent publication verification checks a fresh live
  ref and the final Git state, and compares the exact published commit with
  the GO identity: for the tree scheme, its tree and sole parent against the
  GO values and its changed paths against the approved set; for a manifest,
  its content and changed paths against the approved manifest and its sole
  parent against the expected parent. Executor-reported success alone
  establishes none of these comparisons.
- A mismatch stops publication: no silent recomputation or broader staging.
  Tree equality identifies tracked content, paths and modes only, not
  semantic correctness, approval, remote publication or independent
  verification.

## Publication verification and continuity

After independently verifying publication, the planner recommends exactly one
of `CONTINUE_THIS_CHAT` or `START_NEW_CHAT`, explains why and gives the
matching complete prompt below. This human-operated policy changes no product
behavior:

- Assess Codex and Claude continuity separately. Keep an active review and its
  corrections together by default and never restart one merely because of
  compaction; prefer transitions after independently verified publication.
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
  evidence (the phase it was prepared at, the prior delivered commit,
  uncommitted work, checks actually run and open risks); the executor updates
  it when delivering a slice.
- [planner-handoff.md](planner-handoff.md) holds selected scope, rationale and
  review decisions, distinguishing selected work from ideas and verified
  against Git, code, the roadmap and accepted ADRs; the planner updates it
  when selecting a slice or recording a review decision.
- Updates replace the current state instead of prepending entries, stay short,
  link authoritative contracts and ADRs, and follow durable events only, never
  conversation length or compaction. The planner carries delivered facts
  forward in its next selection record.

## Recovery

The implementation record, review decision, commit, remote publication and
independent publication verification are distinct; none implies the next.
A checkpoint records the phase at which it was prepared, and its wording
never requires repeating an action that Git shows as completed. Nothing is
called published in anticipation of a successful push, and no separate
closure-document commit records a later phase. The executor's committed
record therefore reads, in substance:

> This document records the implementation delivered for review. Consult
> planner-handoff.md for the review decision. Reconcile publication with Git
> and the slice's independent publication-verification record, when
> available. This record alone grants no publication authority.

Reconcile Git first, then the records:

- **Uncommitted changes:** reconcile them with the selected slice and its
  review decision. Without GO, review or correction continues; with GO,
  publish only the content the GO identifies.
- **Local commit ahead of the remote:** publication is incomplete; reconcile
  publication authority and the approved identity before retrying the push.
- **Commit present on the remote:** do not republish; look for independent
  verification of that exact commit.
- **Verification evidence missing:** "remote commit confirmed" is not "prior
  independent verification established"; perform the bounded verification
  needed instead of inventing a previous result.
- Missing local logs authorize neither reconstructing evidence nor
  automatically reopening the implementation.

## Historical snapshots

| Source commit | Execution snapshot | Planner snapshot |
|---|---|---|
| `ba1d4c2d9e9c5df285777267a7402e75f28e4f87` | [current-work-history-ba1d4c2.md](current-work-history-ba1d4c2.md) | [planner-handoff-history-ba1d4c2.md](planner-handoff-history-ba1d4c2.md) |
| `0a57216c2a8f7e6d00228e6d0a315d25f526a180` | [current-work-history-0a57216.md](current-work-history-0a57216.md) | [planner-handoff-history-0a57216.md](planner-handoff-history-0a57216.md) (WF-03N final GO) |
| `e614c79553c92e1964d57fa9a4d9d5dbbfadb2b2` | [current-work-history-e614c79.md](current-work-history-e614c79.md) | [planner-handoff-history-e614c79.md](planner-handoff-history-e614c79.md) (WF-04 final GO) |

Each snapshot is the exact blob of its checkpoint at the source commit;
compare `git hash-object` of the snapshot with
`git rev-parse <commit>:docs/roadmap/<checkpoint>.md`. Snapshots are
progressive references, not mandatory startup reading. They preserve
statements made at the time, including old GO, pending, uncommitted and
publication wording, as evidence, never current instructions, renewed
approval or current status. They are never edited; their links are frozen. A
reference to either checkpoint's earlier entries, from a snapshot or another
document, means that baseline content: within a snapshot, its own baseline;
elsewhere, the latest pair whose baseline contained the entry, unless the
reference names another commit. Entries recorded only between or after pinned
baselines are in the checkpoints' Git history; cite them explicitly, as "this
file at `<commit>`". A new snapshot needs a concrete access or preservation
benefit. Archiving an issue does not resolve it.

## Rule locations

| Baseline `AGENTS.md` topic | Now |
|---|---|
| Contract, routing, language, trust, ADRs | AGENTS.md |
| Roles, challenge, disagreement | AGENTS.md |
| Review and publication authority, Git discrepancies | AGENTS.md; detail here |
| Startup reading and reuse; chats | Startup routing; Chats |
| Actionable prompts, preflight, validation | Planning and dispatch |
| Reports; corrections; GO/NO-GO | Execution; Review and corrections |
| Commit, push, closure | Publication; Recovery |
| Next work | Publication verification and continuity |
| Handoff ownership and updates | Checkpoints |
