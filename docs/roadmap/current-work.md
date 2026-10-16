# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery. Procedure:
[development workflow](development-workflow.md). Git and code prevail; compare
branch, `HEAD`, `origin/main` and staged/unstaged/untracked state first.

## Active slice: WF-03C evidence inspection extraction

- **Status: UNSTAGED, UNCOMMITTED and UNPUSHED; correction 1 for independent
  Codex review; no GO.** Preflight matched: `main`; `HEAD`, local `origin/main`
  and live main `a40d95bb`; 0/0; empty index; the review-1 planner checkpoint
  (6140 bytes, `ab9373e5...fd70`) preserved byte-identically.
- **WF-03C-R1 (NO-GO, review 1): corrected.** dev-01 failed C12 on a checker
  defect and C17 on a real checkpoint wording loss ("evidence keeps its
  original scope"), restored before dev-02. negative-01's unmutated baseline
  failed, so its kills are invalid; negative-02 superseded it.
- **EX-C01: resolved.** The execution evidence introduction links provenance
  and history to the new companion; its contract sections are unchanged.
- **Paths.** Modified: [protocol](../architecture/agent-collaboration-protocol.md)
  (three forwarders, one routing paragraph; 66 headings kept),
  [index](../architecture/README.md) (one entry),
  [rework map](agent-collaboration-protocol-rework-map.md) (WF-03C record;
  WF-03A/B kept),
  [execution evidence](../architecture/agent-collaboration-execution-evidence.md)
  (introduction only), this file. New:
  [evidence inspection](../architecture/agent-collaboration-evidence-inspection.md)
  (L830-1049; three authorized repairs). Ignored: `logs/executor/wf-03c/`.
- **Checks.** Before review: dev-02 PASS 19/19, negative-02 22/22 killed,
  `final-01/` PASS 20/20. Correction 1: `correction1/`; outcomes in the report.
- **Limits.** Conservation is exact; link and sensitive scans are lexical.
  Product, test, browser, generation and qualification evidence keeps its
  original scope; none was run.
- **Next action:** independent Codex review. No commit or push.

## Documentation priority

Documentation is the owner's priority; functional work, tests and P9 stay
stopped. Codex plans and reviews; Claude implements; challenge, role ownership
and explicit GO stay mandatory ([AGENTS.md](../../AGENTS.md)). WF-03C is the
third WF-03 slice; WF-03 stays incomplete and later moves unselected.
Constraints: [planner-handoff.md](planner-handoff.md).

## Prior published delivery

WF-03B `a40d95bb` (sole parent `c1189f1a`) is published per
`logs/planner-review/wf-03b/publication-verification.json`, WF-03A `c1189f1a`
per `logs/planner-review/wf-03a/publication-verification.json`, WF-02B
`d1a7b457` per `logs/planner-review/wf-02b/publication-verification.json` and
WF-02A `a593009` (four documents) per `logs/planner-review/wf-02a/`. Their
pre-publication wording is historical; do not reopen or republish them. WF-02A's evidence-loss deviation stands:
`logs/executor/wf-02a/correction1/development-runs-transcribed.md` is a partly
abridged derivative chat transcription, not recovered or independently
authenticated original output; the original failed runs, checker and timings
are unavailable, and run B's exit status is unknown. Their other evidence is
in this file at their commits; retained, not rerun. Earlier deliveries
(TEST-48-01 `d60d718` and before) remain in this file at `a593009` and before;
their excluded-artifact reads grant no retrospective authorization or new read.

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
