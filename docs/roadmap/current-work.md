# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery. Procedure:
[development workflow](development-workflow.md). Git and code prevail; compare
branch, `HEAD`, `origin/main` and staged/unstaged/untracked state first.

## Active slice: WF-03J correction, budgets and configuration separation

- **Status: correction 1 after review 1 NO-GO; UNSTAGED, UNCOMMITTED and
  UNPUSHED for Codex review; no GO.** Preflight matched: `main`; `HEAD` and
  `origin/main` `578c83d3`; 0/0; empty index. Planner checkpoint (13506 bytes,
  `2d78447a...a52f`) preserved byte-identically. Live main: dispatch
  observation retained; the c1 final run's own lookup is in `runs.log`.
- **Paths.** Modified: [protocol](../architecture/agent-collaboration-protocol.md)
  (one forwarder, one router), [index](../architecture/README.md), the
  invocation configuration companion (introduction and appended provider
  configuration observations), the guidance and escalation, format repair,
  execution evidence, account usage, usage evidence and token-activity
  controls introductions,
  [rework map](agent-collaboration-protocol-rework-map.md) (WF-03J record),
  this file. New:
  [correction and budgets](../architecture/agent-collaboration-correction-and-budgets.md).
  Ignored: `logs/executor/wf-03j/`.
- **Findings.** WF-03J-R1/EX-J01: JL713 now carries the authorized link
  form; the review-1 plain form and its checker oracle were wrong, and
  review-1 final-01 and negative runs are retained as evidence for that wrong
  oracle. WF-03J-R2/EX-J02: the two added introductions are repaired, bodies
  exact. EX-J03 accepted (no trim).
- **Checks and limits.** `logs/executor/wf-03j/runs.log` and its README
  (correction 1 appended). Conservation is exact apart from the one authorized
  JL713 link. Link and sensitive scans are lexical; Git/disk checks have no
  negative control; retained six/eight-path, deferred-enforcement and null
  model/effort wording is unreconciled.
- **Next action:** independent Codex review. No commit or push.

## Documentation priority

Documentation first; functional work, tests and P9 stay stopped. Codex plans
and reviews; Claude implements; challenge, roles and explicit GO stay mandatory
([AGENTS.md](../../AGENTS.md)). WF-03 stays incomplete; later moves unselected.
Constraints: [planner-handoff.md](planner-handoff.md).

## Prior published delivery

WF-03I `578c83d3`, the planning policy `b81f7611` (workflow and slice
template), WF-03H `8a4223d5`, WF-03G `de8f1a72`, WF-03F `bc649c50`, WF-03E
`5dd2e3a9`, WF-03D `02297979`, WF-03C `fc14b5fd`, WF-03B `a40d95bb`, WF-03A
`c1189f1a` and WF-02B `d1a7b457` (each the sole parent of the one before) are
published per `logs/planner-review/<slice>/publication-verification.json`
(planning policy: `wf-planning-policy`); WF-02A `a593009` (four documents) per
`logs/planner-review/wf-02a/`. Pre-publication wording is historical; do not
reopen or republish them. WF-03I limits
(`logs/planner-review/wf-03i/review1/decision.md`): the planner's verify-01
Git ENOBUFS failure is retained (verify-02 passed); generate-01 output files
are gone (hashes, tool and streams survive) and helper console output exists
only in the executor chat; controls inspected, not rerun; reconstruction alone
cannot catch an omitted repair.
Planning-policy limits: planner-authored, lexical
link checks, no negative controls; publisher phases inspected, not rerun; the
fresh live stdout is only in the planner transcript. WF-03H limits
(`logs/planner-review/wf-03h/review1/decision.md`): the dev-01 C10
absolute-offset checker defect was fixed without a document change;
generate-01 output files were overwritten (hashes, tool and streams survive);
controls inspected, not rerun; the planner's freeze-01 wrong-path failure is
retained; EX-H03 HL691-692 and prior G wording stay deferred and unreconciled.
WF-03G limits
(`logs/planner-review/wf-03g/review1/decision.md`): negative-01 N23 survived a
checker weakness (fixed in negative-02, retargeted in negative-03); the
planner's first link scan misread a code span (retained); controls inspected,
not rerun; scans lexical; Git/disk/live checks lack negative controls; EX-G03
GL613/690/705-707 wording unreconciled. WF-03F limits:
`logs/planner-review/wf-03f/review1/decision.md` (N01 no-op, controls not
rerun) and its `publication-verification.json` (publisher source hash only).
WF-03E limits:
`logs/planner-review/wf-03e/review2/decision.md` and `review1/decision.md`.
WF-03D limits (`logs/planner-review/wf-03d/review1/decision.md`): lexical
scans; no Git/disk mutants; executor controls inspected, not rerun; N05, not
N06, is the broken-fragment control. WF-03D and WF-03G planner parent console
output is only in transcripts.
WF-03C limits (`logs/planner-review/wf-03c/review2/decision.md`):
negative-01's failed baseline invalidates its kills; the recovered dev-01
checker matches its hash but is no original snapshot; the aborted write is an
executor note, not an original stream. WF-02A's evidence-loss deviation stands:
`logs/executor/wf-02a/correction1/development-runs-transcribed.md` is a partly
abridged derivative chat transcription, not recovered or independently
authenticated output; original failed runs, checker and timings are
unavailable; run B's exit status is unknown. Other evidence is in this file at
their commits; retained, not rerun. Product, test, browser, generation and
qualification evidence keeps its original scope; none was run. Earlier
deliveries (TEST-48-01 `d60d718` and before) remain in this file at `a593009`
and before; their excluded-artifact reads grant no retrospective authorization
or new read.

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
