# Executor slice-start template

Use one new Claude chat for each selected slice. Keep its implementation and
review corrections in that chat. The planner replaces every placeholder with
verified facts and sends only the relevant slice details; standing rules live
in [AGENTS.md](../../AGENTS.md), not in this prompt.

```text
Implement the bounded slice selected by Codex.

Preflight: expected branch [branch], HEAD [full SHA]; expected staged [state],
unstaged [state], and untracked [state]. Verify once before editing. Stop and
report any material discrepancy.

Objective: [one observable result].
Scope: [specific path/role/operation and allowed changes].
Exclusions: [nearby behavior and files that must not change].
Stop gates: [conditions requiring a blocker report rather than scope expansion].
Acceptance evidence: [focused cases, relevant full checks, generated artifacts,
documentation, and diff checks].

If a request or finding is flawed, explain the evidence and propose a bounded
alternative for discussion before doing dependent work. Agreement does not
waive the engineering contract or authorize unaccepted scope or publication.

Update docs/roadmap/current-work.md with actual checks and remaining risks.
Present the complete diff, changed-file summary, concise check outcomes, and
short failure excerpts, uncommitted and unpushed, for Codex GO/NO-GO. Keep
corrections in this chat. Do not select the next slice.
```

After Codex GO, use the same chat to commit only the reviewed substantive diff,
push `main` normally to `origin/main`, verify the remote commit, and report it.
A material post-GO change returns for review. These publication rules are
defined in AGENTS.md; this template does not grant GO.

The planner includes the complete resolved prompt directly after its analysis;
a file link alone is insufficient. Use the evidence-based discussion rules in
AGENTS.md when the executor challenges a proposal or review finding.
