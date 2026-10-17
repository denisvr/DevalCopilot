# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery. Procedure:
[development workflow](development-workflow.md). Git and code prevail; compare
branch, `HEAD`, `origin/main` and staged/unstaged/untracked state first.

## Active slice: WF-03L context and runtime preparation extraction

- **Status: implemented; UNSTAGED, UNCOMMITTED and UNPUSHED for Codex review;
  no GO.** Preflight matched: `main`; `HEAD`, `origin/main` and live main
  `340d5e9d`; 0/0; empty index; only the planner checkpoint (21469 bytes,
  `343867fc...3ddd1`) modified, preserved byte-identically.
- **Paths.** Modified: [protocol](../architecture/agent-collaboration-protocol.md)
  (Purpose routing, four forwarders, directory framing),
  [provider contracts](../architecture/agent-collaboration-provider-contracts.md)
  (preflight section, introduction),
  [source evidence](../architecture/agent-collaboration-source-evidence.md) and
  [invocation configuration](../architecture/agent-collaboration-invocation-configuration.md)
  (introductions only), [index](../architecture/README.md) (one entry, provider
  entry), [rework map](agent-collaboration-protocol-rework-map.md)
  (introduction, WF-03L record), this file. New:
  [context](../architecture/agent-collaboration-context.md). Other companions
  unchanged. Ignored: `logs/executor/wf-03l/`.
- **Findings.** EX-L01 observation: the invocation configuration's provider
  configuration observations paragraph (lines 544-546) keeps its WF-03J
  statement that the host-runtime-preflight projection stays in the protocol;
  the body is preserved by scope, its link reaches the retained heading's
  forwarder and the introduction routes it. EX-L02 observation: the
  engineering context (line 36) and two product comments still route these
  subjects to the protocol, like EX-K01; the forwarders reach them. Both
  unchanged, outside the allowlist; no edit proposed for this slice.
- **Checks and limits.** `logs/executor/wf-03l/runs.log` and its README.
  Moved bodies exact, no substitution; the baseline reconstructs exactly.
  Link, coordinate, directional and sensitive scans are lexical and prove no
  semantic equivalence; Git/disk/live checks have no negative control;
  retained increment, token-usage and installation-layout wording is
  unreconciled.
- **Next action:** independent Codex review. No commit or push.

## Documentation priority

Documentation first; functional work, tests and P9 stay stopped. Codex plans
and reviews; Claude implements; challenge, roles and explicit GO stay mandatory
([AGENTS.md](../../AGENTS.md)). WF-03 stays incomplete; later moves unselected.
Constraints: [planner-handoff.md](planner-handoff.md).

## Prior published delivery

WF-03K `340d5e9d`, WF-03J `0de51c01`, WF-03I `578c83d3`, the planning policy `b81f7611`
(workflow and slice template), WF-03H `8a4223d5`, WF-03G `de8f1a72`, WF-03F `bc649c50`, WF-03E
`5dd2e3a9`, WF-03D `02297979`, WF-03C `fc14b5fd`, WF-03B `a40d95bb`, WF-03A
`c1189f1a` and WF-02B `d1a7b457` (each the sole parent of the one before) are
published per `logs/planner-review/<slice>/publication-verification.json`
(planning policy: `wf-planning-policy`); WF-02A `a593009` (four documents) per
`logs/planner-review/wf-02a/`. Pre-publication wording is historical; do not
reopen or republish them. WF-03K limits
(`logs/planner-review/wf-03k/review1/decision.md`): the planner's verify-01
3/5 (an inline-code example misread as a link; a failed network lookup) is
retained and verify-02 passed; the executor dev-01 C06 directional-oracle gap
and negative-01 N14 indentation-mutant defect are retained, fixed in dev-02
and negative-02; its evidence README's "three" generator runs is an accepted
summary-count typo for the four runs in its ledger; controls inspected, not
rerun; scans lexical; Git/disk/live checks lack negative controls. WF-03J limits
(`logs/planner-review/wf-03j/review2/decision.md`, review1 retained): the
initial negative-01 expected-message defects and the c1-dev-01 C17 false
positive are retained; the review-1 plain-form oracle evidence stays with its
correction; the planner's misdirected status write is retained in review2;
controls inspected, not rerun. WF-03I limits
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
