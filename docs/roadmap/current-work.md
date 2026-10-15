# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery. Procedure:
[development workflow](development-workflow.md). Git and code prevail; compare
branch, `HEAD`, `origin/main` and staged/unstaged/untracked state first.

## Active slice: WF-01 documentation workflow migration

- **Status: UNSTAGED, UNCOMMITTED and UNPUSHED; returned for Codex GO/NO-GO.**
  Documentation only. Preflight once before editing: `main`; `HEAD`, local
  `origin/main` and live `refs/heads/main` all
  `ba1d4c2d9e9c5df285777267a7402e75f28e4f87`; empty index; no unstaged or
  untracked path.
- **Paths.** Modified: `AGENTS.md`, this file, `planner-handoff.md`,
  `slice-start-template.md`. New: `development-workflow.md`,
  `current-work-history-ba1d4c2.md`, `planner-handoff-history-ba1d4c2.md`.
- **Checks (fresh, this slice; documentation only; exact commands, counts and
  timings are in the review report).** History: each snapshot equals its
  required blob by raw and path-filtered `git hash-object`, at 1,276,430 and
  499,010 bytes. Bounds and encoding: `AGENTS.md` at most 6 KiB, the workflow
  at most 8 KiB, each checkpoint at most 6 KiB; authored files are valid
  UTF-8 with LF, a final newline, no NUL and no trailing whitespace.
  References: authored local links and fragments, including reference-style,
  incoming links and snapshot links all resolve (fragment-aware scratch
  checker; the retained `check_links.js` was inspected and run unchanged).
  Hygiene: `git diff --check` clean; secret and personal-string scans of the
  authored files report no hits. Scope: empty index, exactly seven paths,
  unchanged `HEAD` and generated client `8d72cb7c...d104`.
- **Not run.** No build, product suite, journey, client regeneration,
  provider qualification, native harness or experiment.
- **Next authorized action:** Codex review of this diff. No commit or push.

## Prior published delivery

`ba1d4c2d9e9c5df285777267a7402e75f28e4f87` (parent
`69b2adb9b37675ef0d57b3ebe2e10ae9c3dde757`) published the static AppContainer
harness audit report on `origin/main`. Its entry's "UNCOMMITTED and UNPUSHED"
status was the pre-publication submission state; Git shows it published.
Its executor checks are retained evidence from that slice, not rerun or
independently verified here.

## History

[Execution history snapshot](current-work-history-ba1d4c2.md): exact blob
`ebb007040fa73ab0252511180e53e2e64f5285df` of this file at `ba1d4c2`, with
every earlier entry, evidence bullet and open risk as written then. Links
from other documents to this file's earlier evidence resolve there. It is
evidence, not current status or approval.

## Open risks

- **Account usage.** The old "provider account-usage limits remain
  unenforced" summary was overbroad. [ADR-0025](../decisions/0025-stop-new-codex-attempts-at-an-explicit-account-usage-percentage.md)
  and `CodexAccountUsageStopGate` enforce an optional, run-scoped Codex stop
  at a provider-reported account-usage percentage, at claim and before
  dispatch; below-threshold evidence is not remaining quota.
  [ADR-0026](../decisions/0026-warn-explicitly-about-a-codex-account-usage-percentage.md)'s
  warning is advisory and separate, as is the run-scoped token-activity stop.
  No account-usage threshold applies to Claude; Claude account and session
  limits stay open.
- Provider-session resume, live context capacity and manual compaction, and
  remaining provider controls stay open under Increment 4.
- [ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md)
  and [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)
  bound claim count and reserved time, not actual wall time, provider usage or
  account availability, and neither has a human override. Gemini remains disabled under
  [ADR-0011](../decisions/0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md).
- Raw linked artifact inspection beyond the existing bounded views is open.
- Raw checkpoint Git observation can still read named linked content; ADR-0024
  and ADR-0027 constrain delivered tracked text only, and the physical proof
  is Windows-specific.
- The concurrent-replay 409, target-test 500, disposal
  `NullReferenceException` and unnamed Api 2067/2068 remain separately
  unexplained; later passes establish neither flakiness nor irrelevance.
- **P9 evidence limits** ([feasibility note](../architecture/github-publication-source-write-denial-feasibility.md)
  sections 11 to 14): the v1 stop-gate defect and protocol deviation; the
  inconclusive A2 no-child result; native-error and capture limitations; a
  malformed original receipt versus its derived summary (missing captures are
  not reconstructed); no pre-redaction originals, retained redacted
  derivatives and object files embedding local paths; an annotated host
  transcript versus valid JSONL captures; an unchecked child-exit measurement;
  limited profile-state evidence. File-hash manifests do not seal live ACL
  metadata.
- Decision constraints (ADR-0050, P9, qualification, experimental authority,
  retained artifacts) are in [planner-handoff.md](planner-handoff.md).
