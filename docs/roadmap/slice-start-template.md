# Executor slice-start template

Use one executor chat for each selected slice and keep its corrections there.
The planner replaces every placeholder with verified facts and sends only the
slice details; standing rules live in [AGENTS.md](../../AGENTS.md) and the
[development workflow](development-workflow.md), not in this prompt.

```text
Implement the bounded slice [slice ID] selected by Codex.

Preflight: expected branch [branch], HEAD [full SHA], local and live
origin/main [full SHA]; expected staged [state], unstaged [state] and
untracked [state]. Verify once before editing. Stop and report any material
discrepancy.

Objective: [one observable result].
Sizing rationale: [why this is the largest coherent safe outcome; name the
concrete constraint if related work must be split].
Scope: [specific paths/roles/operations and allowed changes].
Exclusions: [nearby behavior and files that must not change].
Stop gates: [conditions requiring a blocker report rather than scope expansion].
Acceptance evidence: [focused cases, relevant full checks, generated artifacts,
documentation and diff checks].
Validation matrix: [boundary -> focused command/filter -> final regression
set -> broadening trigger -> retained evidence]. Run focused checks during
development and the final matrix once on stable input; record elapsed time.

Challenge a flawed request or finding with evidence and a bounded alternative
before doing dependent work. Agreement does not waive the engineering
contract or authorize unaccepted scope or publication.

Update docs/roadmap/current-work.md. Return the complete diff unstaged,
uncommitted and unpushed with changed files, exact checks and outcomes,
fresh versus retained versus not-run evidence, risks and short failure
excerpts for Codex GO/NO-GO. Keep corrections and finding IDs in this chat.
Do not select the next slice. This prompt grants no commit or push.
```

After explicit GO, the same executor chat commits the reviewed diff, pushes
`main` normally to `origin/main`, verifies the live remote and reports the
SHA; a material post-GO change returns for review. The planner then
recommends `CONTINUE_THIS_CHAT` or `START_NEW_CHAT` as the workflow defines.

A `START_NEW_CHAT` transition prompt instead gives the receiving planner the
verified published baseline, expected Git state, current decisions and
constraints, authoritative references and remaining objective, preserves the
current authority boundary and leaves next-slice selection to that planner.

Use [the slice validation policy](validation-policy.md) when filling the
matrix; choose checks for the boundaries the diff affects and preserve
applicable CI, security, real-provider and release gates.
