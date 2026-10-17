# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery. Procedure:
[development workflow](development-workflow.md). Git and code prevail; compare
branch, `HEAD`, `origin/main` and staged/unstaged/untracked state first.

## Active slice: WF-04 checkpoint history consolidation

- **Status: implemented; UNSTAGED, UNCOMMITTED and UNPUSHED for Codex review;
  no GO.** Preflight matched: `main`; `HEAD`, `origin/main` and live main
  `0a57216c`; 0/0; empty index; only the planner checkpoint (12358 bytes,
  `04a5b8de...29eb6a`) modified, preserved byte-identically.
- **Paths.** New exact `0a57216` copies:
  [execution snapshot](current-work-history-0a57216.md) and
  [planner snapshot](planner-handoff-history-0a57216.md). Modified: this
  file before History and the workflow's
  [Historical snapshots](development-workflow.md#historical-snapshots). The
  planner checkpoint as found. Ignored: `logs/executor/wf-04/`.
- **Findings.** EX-04-01 observation: the planner checkpoint's replaced
  "Priority and verified baseline" body (`b409983e` selection baseline,
  workflow patch identity, WF-03L publication summary) has no explicit
  historical link; it is reachable through the same fragment in the planner
  snapshot under the workflow routing rule. Not edited (planner-owned).
- **Checks and limits.** `logs/executor/wf-04/runs.log` and its README:
  preflight 11/11, snapshot 2/2, final 21/21, negative baseline 17/17 with
  25/25 killed (negative-01 invalid: failed baseline). Startup bytes,
  `0a57216` to now: current-work 11601 to 8757 (above the ~6 KiB target; no
  limit dropped), planner checkpoint 25586 to 12358, workflow 9375 to 10137.
  Scans lexical; Git/disk/live checks have no negative control; no product
  suites or old checkers run.
- **Next action:** independent Codex review. No commit or push.

## Documentation priority

Documentation first; functional work, tests and P9 stay stopped. Codex plans
and reviews; Claude implements; challenge, roles and explicit GO stay mandatory
([AGENTS.md](../../AGENTS.md)). WF-03 is accepted and published; the wider
documentation/workflow objective stays open. Constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

WF-03N `0a57216c` (sole parent `b409983e`, seven mode-100644 paths) is
published per `logs/planner-review/wf-03n/publication-verification.json`
(independent 5/5; read, not rerun); review
`logs/planner-review/wf-03n/decision.md`. WF-03 technical acceptance is
effective. EX-N01 and EX-N02 are accepted; RV-N01 is an accepted evidence
limit. The record's unpushed, pre-GO wording is historical:
[WF-03N execution record](current-work-history-0a57216.md#active-slice-wf-03n-integrated-routing-and-acceptance).

Earlier slices, WF-03M `b409983e` back to WF-02A `a593009`: commits, review
record paths, publication evidence and limits as carried at `0a57216` are in
the [execution snapshot](current-work-history-0a57216.md#prior-published-delivery);
WF-03I to WF-03N review bodies and the retained B, C, WF-02B and WF-02A
dispositions are in the
[planner snapshot](planner-handoff-history-0a57216.md#retained-review-and-evidence-limits)
and the sections above it. Every stable finding and disposition stays in its
slice's `logs/planner-review/<slice>/` and `logs/executor/<slice>/` records;
earlier checkpoint bodies are in Git history. TEST-48-01 `d60d718` and earlier
are in this file at `a593009` and before; their excluded-artifact reads grant
no retrospective authorization or new read. Do not reopen or republish them.

## Active evidence limits

Archiving and progressive reading resolve or waive none of these:
- **WF-02A evidence loss.**
  `logs/executor/wf-02a/correction1/development-runs-transcribed.md` is a
  partly abridged derivative chat transcription, not recovered or
  independently authenticated output; original failed runs, checker and
  timings are unavailable; run B's exit status is unknown. Never reconstruct.
- **Failed, missing and derivative evidence stays as recorded:** C's failed
  negative-01 baseline (its kills invalid), recovered (not original) dev-01
  checker and aborted-write note; no-op or surviving mutants (F N01, G N23);
  overwritten or missing generator output (H, I); B and I ENOBUFS records;
  M publisher `git show` reads without stream recording; RV-N01 child Git
  reads without individual streams; failed planner verifications, executor
  checker defects, excluded throws and chat-only console output in the
  per-slice records. Later passes explain or erase none.
- **Scope.** Link, ownership, coordinate and sensitive scans are lexical and
  prove no semantic equivalence; Git/disk/live and independent checks have no
  negative controls; retained controls and publisher phases were inspected,
  not rerun or historically authenticated; no new whole-original inverse is
  claimed. Ordinals, counts, "real today", EX-G03 and EX-H03 wording and the
  WF-02B observations stay unreconciled or unadjudicated. Product, test,
  browser, generation and qualification evidence keeps its original scope;
  none was run.
- The documentation rework map's known missing
  `planner-handoff.md#verified-transition-baseline` link is unchanged.

## History

[Execution history snapshot](current-work-history-ba1d4c2.md): exact blob
`ebb007040fa73ab0252511180e53e2e64f5285df` of this file at `ba1d4c2`, with
every earlier entry, evidence bullet and open risk as written then. Later
entries are in this file's Git history. Links from other documents to this
file's earlier evidence resolve there. It is evidence, not current status or
approval.

## Open risks

- **Account usage.** ADR-0025 and `CodexAccountUsageStopGate` enforce an
  optional run-scoped Codex stop at a provider-reported percentage, at claim
  and before dispatch; below-threshold evidence is not remaining quota. The
  ADR-0026 warning is advisory and separate, as is the run-scoped
  [token-activity stop](../architecture/agent-collaboration-protocol.md#per-provider-run-token-activity-stop-at-agent-claim).
  No account-usage threshold applies to Claude; Claude account and session
  limits stay open.
- Provider-session resume, live context capacity, manual compaction and other
  provider controls stay open under
  [Increment 4](mvp-delivery-plan.md#increment-4-real-codex-and-claude-code-collaboration).
- ADR-0012 and ADR-0013 bound claim count and reserved time, not actual wall
  time, provider usage or account availability, and neither has a human
  override. Gemini stays disabled under ADR-0011.
- Raw linked artifact inspection beyond the existing bounded views is open.
- Raw checkpoint Git observation can still read named linked content; ADR-0024
  and ADR-0027 constrain delivered tracked text only. Physical proofs are
  Windows-specific: elsewhere, previews are omitted and `FilesystemArtifactStore`
  checks containment lexically only.
- The concurrent-replay 409, target-test 500, disposal
  `NullReferenceException` and unnamed Api 2067/2068 remain unexplained, as
  does the interrupted reviewer synthetic run the planner checkpoint records;
  later passes establish neither flakiness nor irrelevance. TEST-48-01 only bounds
  what a future failure of the concurrent identical-request fact retains
  (ordinals, status, fixed code); its one passing run attributes nothing, a hung
  submission yields no record, and retention beside a disposal fault was not
  exercised.
- **P9 evidence limits** ([feasibility note](../architecture/github-publication-source-write-denial-feasibility.md)
  sections 11 to 14): the v1 stop-gate defect and protocol deviation; the
  inconclusive A2 no-child result; native-error and capture limits; a malformed
  original receipt versus its derived summary (missing captures are not
  reconstructed); no pre-redaction originals; retained redacted derivatives and
  object files embedding local paths; an annotated host transcript versus valid
  JSONL captures; an unchecked child-exit measurement; limited profile-state
  evidence. File-hash manifests do not seal live ACL metadata. The
  [capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md#5-unknowns-and-limits)
  found the retained captures consistent but cannot show that no output was
  lost or prove child exit codes; the
  [semantics note](../architecture/github-publication-native-io-semantics.md#6-unknowns-and-consequences)
  leaves open the exit value after a failed query, the child-handle failure
  cause and final-drain buffered bytes (K1-K5).
- Decision constraints (ADR-0050, P9, qualification, experimental authority,
  retained artifacts) are in [planner-handoff.md](planner-handoff.md).
