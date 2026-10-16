# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery. Procedure:
[development workflow](development-workflow.md). Git and code prevail; compare
branch, `HEAD`, `origin/main` and staged/unstaged/untracked state first.

## Active slice: P9-EV-01 retained-capture consistency cross-check

- **Status: UNSTAGED, UNCOMMITTED and UNPUSHED; NO-GO corrections returned
  for Codex review.** Both preflights: `main`; `HEAD`, local `origin/main`
  and live main `4c9102c523e2d8bd345f390332a6af470afb9cec`; 0/0; empty index.
  First: only the planner file unstaged (6095 bytes, `1b309df9...d486`).
  Correction: this file and the planner file unstaged, note untracked; planner
  file (6037 bytes, `8251c1b3...c39a`) preserved byte-identically.
- **Paths.** New: the
  [cross-check note](../architecture/github-publication-retained-capture-crosscheck.md).
  Modified: this file, [architecture index](../architecture/README.md).
  Ignored: reader, reports, manifests and `correction1/` evidence under
  `logs/executor/p9-appcontainer-evidence-crosscheck/`.
- **R1 (resolved, wording only).** Section 3: 112,425 bytes = 112,138 bytes
  of LF-terminated transcript host lines + 287 bytes from LF to CRLF; a
  read-only byte count agrees. No contradiction.
- **Index (authorized).** One link: consistency evidence, not a contract or
  P9 proof.
- **Result (recorded bytes).** 4 manifests and 22 inputs matched before and
  after use. Capture lengths, child JSONL, eight steps per child, embedded
  segments, host summaries, CRLF wrapper stream and completion ordering agree;
  the receipt stays a named parse failure; the derived summary matches its
  sources. No contradiction; refinements and unknowns are in the note.
- **Checks.** `--self-test` met 8/8 controls (about 10 ms); `--verify`, one
  pass, met every expectation (about 21 ms). A first self-test failed its
  positive control (parser bug); report retained, reader fixed before verify.
  Doc links, fragments, encoding, whitespace, secret and personal scans and
  `git diff --check` clean; input and planner hashes unchanged.
- **Correction checks.** Reader not rerun. Changed docs: links, UTF-8, LF,
  whitespace, sensitive shapes and `git diff --check` (with the note) clean.
  Before/after hashes of reader artifacts, gate root and planner file match;
  this read, unchanged, the `ctl`/`app` fixtures the first pass left unopened.
- **Not run.** Build, suite, regeneration, qualification, native harness,
  profile or ACL access, fault injection, experiment.
- **Next authorized action:** Codex review of this diff. No commit or push.

## Prior published delivery

WF-01 `4c9102c523e2d8bd345f390332a6af470afb9cec` (parent
`ba1d4c2d9e9c5df285777267a7402e75f28e4f87`) published the bounded development
workflow and the archived history snapshots on `origin/main`. Its
"UNCOMMITTED and UNPUSHED" status was the pre-publication submission state.
Its checks are retained evidence from that slice, not rerun here.

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
  lost or prove the child exit codes.
- Decision constraints (ADR-0050, P9, qualification, experimental authority,
  retained artifacts) are in [planner-handoff.md](planner-handoff.md).
