# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery. Procedure:
[development workflow](development-workflow.md). Git and code prevail; compare
branch, `HEAD`, `origin/main` and staged/unstaged/untracked state first.

## Active slice: P9-SEM-01 native exit and pipe-read semantics

- **Status: UNSTAGED, UNCOMMITTED and UNPUSHED; review 1 NO-GO corrections
  (R1-R3) returned for Codex review.** First preflight: `main`; `HEAD`, local
  `origin/main` and live main `465e60cfc31d02961fc3970d44e65bb4cfe5ee29`;
  0/0; empty index; only the planner file unstaged (8241 bytes,
  `1658df91...4d07`). Correction preflight: same refs, 0/0, empty index; five
  tracked files unstaged, the note untracked; planner file now 8441 bytes
  (`c2735ffd...468b`), preserved byte-identically.
- **Paths.** New: the
  [semantics note](../architecture/github-publication-native-io-semantics.md).
  Modified: this file, [architecture index](../architecture/README.md) (one
  evidence-only link, unchanged by the correction). Ignored: hash,
  membership, documentation-read and check records under
  `logs/executor/p9-native-io-semantics/`, correction records in its
  `correction1/`.
- **Instruction edits (not this slice).** `AGENTS.md` (`cff3b663...1c4f`)
  and `CLAUDE.md` (`d94da915...2453`) changed during the first pass. Codex
  made them in the planner chat at the owner's explicit request; they match
  the executor's discrepancy record. They are separate authorized owner work,
  kept byte-identical and unstaged, and excluded from this slice's commit.
- **Inputs.** The frozen host, three manifests and four static-audit reports
  matched the dispatched hashes before and after use; the five source/report
  entries match their manifests. Eleven Microsoft Learn pages read
  2026-10-16 (URLs, sections and dates in the note).
- **Result (documentation and source only).** H40: the return is ignored;
  `ec` after a failed query is undocumented, so "failure leaves 0" is an
  inference; the all-access `CreateProcessW` handle rules out a rights cause;
  no other cause is documented or excluded. H37: `drain` collapses empty
  poll, poll/read failure and zero-byte read; whether buffered bytes survive
  the writers' close for the final drain is undocumented. Stdout: only if
  loss is an unread suffix does the eight-line check fail closed (unless the
  loss is wholly after the eighth line); whether a failed read can remove
  bytes is unknown (K4), so stdout-loss detection is not established. Stderr
  reads empty only if every byte went unread, under any loss mode. U2
  narrowed for these two parts only.
- **Deviations.** One `wc -lc` glob also counted three non-allowlisted audit
  reports that are audit-manifest members (per review 1). `wc` read their
  full contents to count; only counts were displayed, and the contents were
  not viewed or used. Not reopened; no retrospective authorization. The
  first membership check failed (`grep` aborted); record kept, `awk` retry
  matched.
- **R1 (corrected).** Conclusion, section 4 implications, matrix H37-c and
  K4 now state the suffix-only assumption behind the eight-line argument and
  keep an unread suffix distinct from possible consumption on failure.
- **R2 (corrected).** Provenance limits "no other listed member" to the
  membership/hash checks and records the `wc` content reads as a deviation.
- **R3 (corrected).** The blanket K1-K5 sentence is replaced: API questions
  that documentation or separate investigation might narrow; controls that
  could improve future measurements; historical uncertainty (K5 and the
  canonical exit values) that no new work can remove. Nothing is selected.
- **Checks.** First pass: final documentation matrix on the three paths
  (links/fragments, UTF-8/LF/final newline, whitespace, sensitive patterns,
  complete diff, `git diff --check` with the note), all clean; eight-fault
  checker control met. Correction: the same checker, `git diff --check`,
  the note's whitespace check, diff inspection and planner/instruction-file
  hashes, recorded in `correction1/`; outcomes are in the executor report.
- **Retained.** Original input hashes and memberships, the source and
  documentation assessment and the checker control: inputs unchanged, not
  rerun. No new frozen-input or web read in the correction.
- **Not run.** Build, suite, generation, native harness, API probe, fault
  injection, capture/fixture/binary read, live profile/ACL/token state,
  qualification.
- **Next authorized action:** Codex review of this diff. No commit or push.

## Prior published delivery

P9-EV-01 `465e60cfc31d02961fc3970d44e65bb4cfe5ee29` (sole parent
`4c9102c523e2d8bd345f390332a6af470afb9cec`) published the
[capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md),
its index link and checkpoints; the planner verified it independently. R1's
byte-count wording and the index addition were accepted. Its committed
"UNCOMMITTED and UNPUSHED" wording was the pre-publication state. Its
correction protection hashing read the excluded `ctl`/`app` fixtures: saved
records hold 69 equal hashes (11 reader artifacts, 57 gate files, the
planner); the reported 12/56 split was wrong. The reviewer compared saved
records without reopening fixtures; this grants no retrospective
authorization or further sweep. Its checks are retained, not rerun here.
WF-01 `4c9102c` and earlier facts remain at their published baselines.

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
