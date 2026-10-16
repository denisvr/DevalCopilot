# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery. Procedure:
[development workflow](development-workflow.md). Git and code prevail; compare
branch, `HEAD`, `origin/main` and staged/unstaged/untracked state first.

## Active slice: WF-COL-01 reciprocal critical collaboration instructions

- **Status: UNSTAGED, UNCOMMITTED and UNPUSHED; returned for Codex review.**
  Preflight matched the dispatch: `main`; `HEAD`, local `origin/main` and
  live main `b48738f0f160336df945ad3a6ad1802a6876458b`; 0/0; empty index;
  exactly `AGENTS.md`, `CLAUDE.md` and the planner file unstaged; no
  untracked paths.
- **Paths.** Modified: `AGENTS.md` (4513 bytes, `cff3b663...1c4f`),
  `CLAUDE.md` (703 bytes, `d94da915...2453`), this file and
  [planner-handoff.md](planner-handoff.md) (8013 bytes, `7f74fee3...a759`,
  planner-owned, preserved byte-identically). Ignored: check records under
  `logs/executor/wf-col-01/`.
- **Provenance.** Codex made both root edits in the planner chat at the
  owner's explicit request during P9-SEM-01, which deliberately excluded
  them. The executor did not author or change them; their bytes are kept.
- **Assessment (evidence for Codex, not acceptance).** The `AGENTS.md`
  challenge section makes analysis, discussion and brainstorming reciprocal,
  asks both roles to raise material concerns instead of silently following a
  request believed flawed, requires the planner to address material executor
  challenges with a recorded decision and rationale, and keeps brainstormed
  ideas as proposals. The roles, the unchanged disagreement paragraph,
  GO/NO-GO and publication authority, the startup routing link and the
  `CLAUDE.md` `@AGENTS.md` import are intact; no messaging, orchestration,
  tool or provider permission is introduced. The workflow's rule-location
  table and the playbook's role reference still resolve. No material concern
  was found, so the root text is retained unchanged.
- **ADR-0021.** Both roots stay below the 8192-byte raw bound and remain the
  only captured sources; `CLAUDE.md` names an `AGENTS.md` section that the
  same manifest carries and imports nothing else. In a sealed product
  manifest the new text stays untrusted advisory context beneath the fixed
  boundary; existing sealed manifests replay unchanged.
- **Non-blocking notes.** `CLAUDE.md` paraphrases the shared section, so a
  later rename or rewrite of that heading must update it. "Initiate
  discussion with Codex" relies on the human-operated workflow for the
  channel; neither root names a mechanism. Only the planner's duty to answer
  challenges is explicit; executor handling of review findings stays in the
  workflow's findings rules.
- **Checks.** Focused: complete root diffs, reciprocal and authority
  wording, import, routing link and section reference. Final, once on stable
  input: the reused P9-SEM-01 documentation checker (inspected, unchanged:
  4173 bytes, `da16f21e...ff4b`) on the four paths, root and planner hashes,
  raw-byte bounds and `git diff --check`. Commands, outcomes and times are in
  the executor report and `logs/executor/wf-col-01/`.
- **Retained.** P9-SEM-01 publication and review evidence with its original
  limitations; not reread beyond the planner record.
- **Not run.** Build, suite, generation, provider invocation, native
  experiment, qualification, cleanup or evidence sweep. No manifest was
  sealed, so ADR-0021 delivery of the new text is not exercised.
- **Next authorized action:** Codex review of this diff. No commit or push.

## Prior published delivery

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
