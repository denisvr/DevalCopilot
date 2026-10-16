# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery. Procedure:
[development workflow](development-workflow.md). Git and code prevail; compare
branch, `HEAD`, `origin/main` and staged/unstaged/untracked state first.

## Active slice: DOC-49-01 accepted-status reference correction

- **Status: UNSTAGED, UNCOMMITTED and UNPUSHED; returned for Codex review.**
  Preflight matched the dispatch: `main`; `HEAD`, local `origin/main` and
  live main `cab3756c24e58524f25ef3ba607236d63fa9426e`; 0/0; empty index;
  exactly the planner file unstaged; no untracked paths.
- **Paths.** Modified: [engineering context](../engineering-context.md),
  [MVP plan](mvp-delivery-plan.md), this file and
  [planner-handoff.md](planner-handoff.md) (9240 bytes, `624a277a...45e1`,
  planner-owned, preserved byte-identically). Ignored: check records under
  `logs/executor/doc-49-01/`.
- **Change.** The engineering context's ADR-0049 decision-list label
  `proposed, additive` is now `accepted, additive`, and the MVP plan's
  `(proposed) adds` is now `(accepted) adds`.
  [ADR-0049](../decisions/0049-observe-the-github-repository-permission-of-a-bound-recorded-target-on-explicit-request.md)
  records `Status: Accepted` and the decision index lists it as accepted, so
  no stop gate applied. Surrounding text, links, every other status
  (including Proposed ADR-0050) and product behavior are unchanged. The
  correction concerns decision status only; it establishes no real CLI
  compatibility, branch-push permission or increment completion.
- **Checks.** Focused: each substantive file equals its HEAD blob after
  exactly one substitution (one occurrence at HEAD, none remaining, equal
  length); both ADR-0049 links resolve to the accepted record the index
  names. Final, once on stable input: the reused P9-SEM-01 documentation
  checker (inspected, unchanged: 4173 bytes, `da16f21e...ff4b`) on the four
  paths, the complete four-path diff, planner length and SHA-256, and
  `git diff --check`. The checker exited 1 with two pre-existing false
  positives in the engineering context, on lines unchanged from `HEAD`:
  `assignment-secret` at line 192 on ordinary token prose and `email` at
  line 311 on the fictitious remote example; local links and fragments
  resolved and `git diff --check` passed.
  Commands, outcomes and times are in the executor report
  and `logs/executor/doc-49-01/`.
- **Retained.** WF-COL-01 publication and planner verification, the
  permission-observation feature at
  `810b99b8e4f9c5e4d1b92266d8039a34f5c7c39e` (confirmed an ancestor of
  `HEAD`, not reinspected) and earlier evidence, each with its original scope
  and limitations.
- **Not run.** Build, suite, generation, provider invocation, native
  experiment, qualification, security-state query, cleanup or evidence
  sweep; no historical snapshot or ignored P9 evidence was reopened.
- **Next authorized action:** Codex review of this diff. No commit or push.

## Prior published delivery

WF-COL-01 `cab3756c24e58524f25ef3ba607236d63fa9426e` (sole parent
`b48738f0f160336df945ad3a6ad1802a6876458b`) published the reciprocal
critical-collaboration text in `AGENTS.md` and `CLAUDE.md` with both
checkpoints (four paths, mode 100644); the planner independently verified
the commit, parent, paths, blobs and hashes against its frozen manifest and
local/live ref equality, and the retained verification establishes normal
fast-forward publication. Its committed "UNSTAGED, UNCOMMITTED and UNPUSHED"
wording was the pre-publication state. ADR-0021 delivery of the new root
text was not exercised; existing sealed manifests replay unchanged. Its
non-blocking notes stand: `CLAUDE.md` paraphrases the shared section
heading, neither root names a discussion channel, and executor handling of
review findings stays in the workflow's findings rules. Its checks are
retained, not rerun.

P9-SEM-01 `b48738f0f160336df945ad3a6ad1802a6876458b` (sole parent
`465e60cfc31d02961fc3970d44e65bb4cfe5ee29`) published the
[semantics note](../architecture/github-publication-native-io-semantics.md),
its index link and checkpoints; the planner independently verified the four
paths, blobs, bytes, fast-forward push and live ref and accepted R1-R3 and the
index entry. Its committed "UNSTAGED, UNCOMMITTED and UNPUSHED" wording was
the pre-publication state. Its `wc -lc` deviation read the full contents of
three excluded audit reports for counts only; this grants no retrospective
authorization or new read. The failed membership `grep` and the recorded
pre-final wording mismatch are preserved (see
[planner-handoff.md](planner-handoff.md)). Its checks are retained, not rerun.

P9-EV-01 `465e60c` (sole parent `4c9102c`) published the
[capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md);
its correction hashing read the excluded `ctl`/`app` fixtures (69 equal saved
hashes; the reported 12/56 split was wrong), with no retrospective
authorization or further sweep. WF-01 `4c9102c` and earlier facts remain at
their published baselines.

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
  metadata. The [capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md)
  found the retained captures consistent; it cannot show that no output was
  lost or prove the child exit codes. The
  [semantics note](../architecture/github-publication-native-io-semantics.md)
  leaves open the exit value after a failed query, any failure cause for the
  child handle and buffered-byte availability for the final drain (K1-K5).
- Decision constraints (ADR-0050, P9, qualification, experimental authority,
  retained artifacts) are in [planner-handoff.md](planner-handoff.md).
