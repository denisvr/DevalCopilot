# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: HIST-INP-01 recorded inputs of a selected Agent attempt

This document records the implementation delivered for review. Consult
[planner-handoff.md](planner-handoff.md) for the review decision. Reconcile
publication with Git and the slice's independent publication-verification
record, when available. This record alone grants no publication authority.

- **Prepared at** execution completion, unstaged, uncommitted and unpushed.
  Claude began from `main`, HEAD/local origin/main/live main `96a21d73`,
  divergence 0/0, empty index, only the planner's two modified documents and
  new ADR-0051. The owner then explicitly asked Codex to finish Claude's work
  after Claude's credits ran out. Codex recovered the same slice and HEAD,
  local origin/main 0/0, empty index and the in-progress implementation;
  no reset, cleanup, new slice or publication authority was introduced. The
  three planner-written files were unchanged at execution delivery; the planner
  subsequently records its review in the handoff and updates the decision index.
- **Outcome.** The selected attempt now has a collapsed "Recorded collaboration
  inputs" inspector, independent of evidence, artifacts and any message it
  produced. It shows plain references with exact sequence and timestamp text,
  an inspected count and a first-ten disclosure, or truthful Empty/Unavailable
  states. Each selection and opening owns its requests, results and callbacks;
  explicit retry is the only retry. Rules and copy:
  [inspection contract](../architecture/agent-collaboration-evidence-inspection.md#agent-attempt-history-and-evidence-inspection),
  [cockpit specification](../product/run-cockpit-specification.md#agent-attempt-history-and-evidence),
  [ADR-0051](../decisions/0051-inspect-recorded-inputs-of-a-selected-agent-attempt.md).
- **Backend.** One protected, bodyless GET and mediator query read scalar
  projections of exactly the attempt, at most 65 ordered input rows and at
  most 64 referenced message identities. A 65th row yields Unavailable without
  a count, not a declaration that the stored set is invalid. The entire
  inspected set is checked before the first ten are shown. The ADR-0037
  guarded attempt projection and identity evaluation were narrowly extracted;
  its source/actor comparison stays in its operation. No Domain rule, old
  message-input reader, persistence, migration, invocation or authority changed.
  The API owns its response graph; Int64 sequences and UTC round-trip times
  are strings. The NSwag addition is generated and reproducible. A handwritten
  facade preserves raw types and explicit nulls for whole-answer validation;
  URL, authentication and non-200 handling remain generated.
- **Fresh completion checks.** Complete outputs and exact commands/exits/times:
  `logs/executor/hist-inp-01/codex-completion/index.txt`. Application new-query,
  ADR-0037 and old input-reader regressions: 183 passed (c05); API new/related
  routes and launch authentication: 30 passed (c10); architecture: 153 passed
  (c11). Whole frontend: 238 files, 4621 passed (c04). Its only subsequent test
  correction supplies a required `traceId` in the hook's 404 fixture; that
  file then passed 18/18 (c08), without assertion removal. The full suite was
  not repeated after that fixture-only correction. Lint passed with the same
  nine warnings in unchanged files (c06); frontend build including `tsc -b`
  passed (c09; existing bundle-size warning). API build/NSwag passed (c02),
  with unchanged generated-client SHA256 before/after generation. Hygiene
  passed: 169 local links, missing-file/fragment and sensitive-data controls,
  added-line scan, whitespace and unchanged planner-file hashes (c12).
- **Retained same-slice evidence.** Claude's commands and complete output remain
  in `logs/executor/hist-inp-01/index.txt` and `output/`. The strict e2e
  typecheck (j01), journey-host build (j02), new historical-input journey
  (j03), extended no-message historical-attempt journey (j04), and sealed
  artifact journey (j05) passed. Their production behavior, journey and host
  inputs were unchanged during completion; Codex changed one test fixture,
  explanatory comments and delivery docs only. The new journey produces a
  plan, critical review and resolution through production controls, abandons
  that run, records a newer objective and compares the older attempts' input
  references/counts with SQLite after reload, with no invocation, mutation,
  artifact-window read or source change and the newer run still selected.
  The stopped-before-dispatch Planner proves Empty without an output message.
  Browser proof covers ordinary small sets; malformed/oversized sets and
  sequences above 2^53 are proven by SQLite, API and frontend tests.
- **Failures and controls retained.** Claude's b03 analyzer failure, b04/b05
  development failures, b08 architecture assertion failure, f02/f06 failures
  and all their output remain. b07 caught six malformed-position controls;
  f09's first settle control survived (28 passed), a missing overlap test was
  added and f11 caught it; panel/serial and journey controls failed as expected.
  Source restoration was hash-checked. Completion c01 failed at restore with
  no diagnostic beyond exit 1; c02 succeeded without restore, serially. c03
  aborted before executing tests when testhost did not connect within 90s;
  c05 passed outside the sandbox. These passes do not establish the causes.
  c07 found the missing test-fixture `traceId`; c08/c09 passed after its fix.
  No failure output was removed or reconstructed.
- **Not run.** Whole backend/API suites, unrelated journeys, real providers or
  P9 experiments. No dependency, schema, harness or shared database-fixture
  change. Existing duplicate React keys and connection-negotiation console
  errors are retained; they are not fixed or causally explained by this slice.

## Functional development

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
resumed functional development, superseding the documentation-only exclusion
of production and test work; experimental authority is not renewed. Roles,
challenge and explicit GO: [AGENTS.md](../../AGENTS.md); constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

HIST-MET-01 `96a21d73` (sole parent `5e588a44`, 16 paths) passed independent
publication verification (`logs/planner-review/hist-met-01/publication-verification.log`).
Its implementation, two HMT-R1 corrections, all failures and validation scopes
are recorded in this file at `96a21d7`; 247 independent focused tests passed.
The 4471-test full-suite result remains correction-1 input evidence, not a
correction-2 full rerun. Known token counts remain outside browser proof.

ART-WIN-01 `5e588a44` (sole parent `75372b18`, 19 paths) passed independent
publication verification
(`logs/planner-review/art-win-01/publication-verification.log`); its record,
committed with pre-review wording, is this file at `5e588a4`. HIST-ATT-01
`75372b18` (sole parent `4b52e1cf`, 26 paths) passed independent
publication verification
(`logs/planner-review/hist-att-01/publication-verification.log`); its record,
committed with pre-review wording, is this file at `75372b1`. WF-06
`4b52e1cf` (sole parent `e55709fe`, five paths) passed independent
publication verification (final section of
`logs/planner-review/wf-06/review.log`); its record, committed with
pre-publication wording, is this file at `4b52e1c`. WF-05 `e55709fe` (sole
parent `e614c795`, six mode-100644 paths) passed independent publication
verification (`logs/planner-review/wf-05/publication-verification.json`); its
record, committed with pre-review wording, is this file at `e55709f`. WF-04
`e614c795` (sole parent `0a57216c`) passed 5/5 (`logs/planner-review/wf-04/`);
its [pre-GO record](current-work-history-e614c79.md#active-slice-wf-04-checkpoint-history-consolidation)
is historical. WF-03N back to WF-02A:
[deliveries](current-work-history-e614c79.md#prior-published-delivery),
[per-slice limits](current-work-history-0a57216.md#prior-published-delivery);
TEST-48-01 `d60d718` and earlier: this file at `a593009` and before.
Excluded-artifact reads grant no retrospective authorization or new read; do
not reopen or republish delivered slices.

## Active evidence limits

Archiving, condensation and progressive reading resolve or waive none of these:
- **WF-02A evidence loss.**
  `logs/executor/wf-02a/correction1/development-runs-transcribed.md` is a
  partly abridged derivative chat transcription, not recovered or
  independently authenticated output; original failed runs, checker and
  timings are unavailable; run B's exit status is unknown. Never reconstruct.
- **Failed, missing and derivative evidence stays as recorded** in the
  per-slice records: invalid mutation kills, recovered or unretained
  checkers, surviving or no-op mutants, overwritten generator output,
  unrecorded streams, failed planner verifications, checker defects and
  chat-only output. Items through WF-03N:
  [detail](current-work-history-e614c79.md#active-evidence-limits); WF-04
  dev-01, invalid negative-01 and RV-04-01: this file at `e55709f`. WF-05
  dev-01's failing pre-fix checker was not retained or reconstructed.
  HIST-ATT-01's log keeps only filtered excerpts of its expected development
  failures (run 02) and of its first, failed journey-host build (run 13);
  their complete output was not retained. Codex accepted that loss as a
  limitation of this delivery; later passes neither authenticate nor explain
  those failures. Codex's first independent focused run failed to load six
  suites (temporary-module ENOENT); its retry with a workspace TEMP/TMP passed
  (`logs/planner-review/hist-att-01/focused-02.log`). That first failure is
  unexplained and not attributed to product code. ART-WIN-01 keeps complete
  output of every check, including its failures; its first journey-host build
  hit the same compiler file lock (r04, also naming Microsoft Defender) with
  only its own build processes running, and the retry passed. Its cause is
  unproven. HIST-MET-01 keeps complete output of every check too; its first
  journey-host build hit the lock again (r07, naming only `VBCSCompiler`), the
  retry passed, and the cause remains unproven.
- Scans are lexical; Git/disk/live checks lack negative controls; retained
  controls and publisher phases were inspected, not rerun or historically
  authenticated. Ordinals, counts, "real today", EX-G03, EX-H03 and WF-02B
  observations stay unreconciled. Earlier product, test, browser, generation
  and qualification evidence keeps its scope.

## History

[Registered](development-workflow.md#historical-snapshots) exact snapshots:
[ba1d4c2](current-work-history-ba1d4c2.md) (earliest),
[0a57216](current-work-history-0a57216.md) (WF-03N, earlier limits) and
[e614c79](current-work-history-e614c79.md) (WF-04, prior risk wording); later
entries, including WF-05, WF-06, HIST-ATT-01, ART-WIN-01 and HIST-MET-01, are in Git history. Evidence, not current status or
approval.

## Open risks

[Exact prior wording](current-work-history-e614c79.md#open-risks).
- **Account usage.** ADR-0025 and `CodexAccountUsageStopGate` enforce an
  optional run-scoped Codex stop at a provider-reported percentage, at claim
  and before dispatch; below-threshold evidence is not remaining quota. The
  ADR-0026 warning is advisory and separate, as is the
  [token-activity stop](../architecture/agent-collaboration-protocol.md#per-provider-run-token-activity-stop-at-agent-claim).
  No Claude account-usage threshold; Claude account/session limits open.
- Provider-session resume, live context capacity, manual compaction and other
  provider controls stay open under
  [Increment 4](mvp-delivery-plan.md#increment-4-real-codex-and-claude-code-collaboration).
- ADR-0012/0013 bound claim count and reserved time, not wall time, provider
  usage or account availability; no human override. Gemini stays disabled
  (ADR-0011).
- Raw linked artifact inspection beyond the existing bounded views is open.
- **Attempt and artifact inspection limits (HIST-ATT-01, ART-WIN-01,
  HIST-MET-01, HIST-INP-01).** All read bounded views only: no replay, export,
  session resume or complete history. The three window defects HIST-ATT-01
  found are addressed by the published ART-WIN-01; its window acceptance bounds
  the text by the cursor advance but cannot prove that the text is the decoding
  of those bytes; the host remains the authority for content. Published HIST-MET-01
  shows the selected attempt's recorded
  execution facts; the browser checks their shape and range only. Its
  evidence timestamps are judged raw by an operation-scoped facade (HMT-R1);
  other operations still rely on the generated client's unchecked revival,
  which this slice does not change. Within the same evidence answer, the
  generated conversion of other members is also unchanged: a malformed nested
  `accountUsageDecision.retrievedAtUtc` can still make the generated model throw.
  Planner review corrected the reported artifact failure mechanism: the generated
  model guards `artifacts` with `Array.isArray`; a non-array is left absent and
  the existing whole-answer decoder rejects it. These pre-existing whole-read
  refusals are not newly reachable or addressed here. Known token counts are not browser-proven
  (the Codex double reports no usage). A duplicate React key console error seen
  in the journeys (also with the HEAD frontend) and a recurring compiler file
  lock on the first journey-host build remain unexplained. The development
  build's second evidence read is inferred, not proven, to come from
  StrictMode.
- Raw checkpoint Git observation can read named linked content; ADR-0024/0027
  constrain delivered tracked text only. Physical proofs are Windows-only:
  elsewhere previews are omitted and `FilesystemArtifactStore` containment is
  lexical.
- Unexplained: concurrent-replay 409, target-test 500, disposal
  `NullReferenceException`, unnamed Api 2067/2068 and the interrupted reviewer
  synthetic run; later passes establish neither flakiness nor irrelevance.
  TEST-48-01 only bounds what a future failure retains; one pass attributes
  nothing, a hung submission yields no record, and retention beside a disposal
  fault was not exercised.
- **P9 evidence limits:** [feasibility note](../architecture/github-publication-source-write-denial-feasibility.md)
  sections 11-14, [capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md#5-unknowns-and-limits)
  and [semantics note](../architecture/github-publication-native-io-semantics.md#6-unknowns-and-consequences)
  (K1-K5): stop-gate defect and protocol deviation, inconclusive A2, capture
  and native-error limits, malformed original receipt, no pre-redaction
  originals, path-bearing derivatives, annotated transcript, unchecked child
  exit, limited profile state, no live ACL seal, no proof against lost output.
- Decision constraints: [planner-handoff.md](planner-handoff.md#standing-decision-constraints).
