# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: HIST-MET-01 historical attempt execution and usage facts

This document records the implementation delivered for review. Consult
[planner-handoff.md](planner-handoff.md) for the review decision. Reconcile
publication with Git and the slice's independent publication-verification
record, when available. This record alone grants no publication authority.

- **Prepared at** correction 2 (HMT-R1) of the delivery for review.
  Correction 2 preflight: the same refs and 0/0, empty index, correction 1's
  diff (ten modified tracked paths, six untracked) plus Codex's correction-2
  NO-GO edit to the handoff. First
  delivery preflight: `main`, `HEAD`/`origin/main`/live main `5e588a44`, 0/0,
  empty index, only the planner's handoff selection modified, no nonignored
  untracked paths. Correction preflight: the same refs and 0/0, empty index,
  the delivered diff (nine modified tracked paths, three untracked) plus
  Codex's NO-GO edit to the handoff. Nothing is staged, committed or pushed.
- **Outcome.** The selected Agent attempt in the attempt history (the current
  run's rail and a run selected in Run history alike) now shows, from the
  evidence answer it already read, a "Recorded execution facts" section: the
  response contract; claim, dispatch and end times; the semantic result; the
  host-measured process result; the configured timeout; and the
  provider-reported token and cache counts, each a separate fact with a caveat
  that a clean exit is not success and that counts are not cost, quota,
  capacity, readiness or approval. Rules:
  [selected-attempt execution facts](../architecture/agent-collaboration-evidence-inspection.md#agent-attempt-history-and-evidence-inspection);
  copy: [cockpit specification](../product/run-cockpit-specification.md#agent-attempt-history-and-evidence).
- **Changes (frontend only).** The new pure `agentAttemptExecutionFacts.ts`
  admits each section on its own: an instant only as a valid `Date` (nothing
  else is parsed; a running attempt naming an end time is contradictory);
  dispatch and status first (undispatched, running, or an
  unreadable dispatch time expose no measured result or usage, whatever the
  answer carries); for a concluded dispatched attempt, all members absent is
  unknown; otherwise a closed process outcome, an int32 exit code exactly for
  `Exited` and a nonnegative safe-integer whole-millisecond duration; nonnegative
  int32 input/output and each cache count independently absent or one too;
  a positive safe-integer timeout independent of dispatch. Anything else makes
  only that section unavailable. The new `SelectedAttemptExecutionFacts.tsx`
  passes only admitted values to the unchanged `ProcessEvidenceLine` and
  `TokenUsageLine` (their wording reused) and renders the times, timeout and
  unavailable states itself; `AgentAttemptHistoryPanel.tsx` mounts it in place
  of its former claim/end line. No hook, request, shared presentation, latest-
  attempt, status-action, run-summary, backend, generated client, schema,
  dependency or harness change.
- **Correction 1 (HMT-R1, proposed resolved for Codex to verify).** A `Date`
  check after generated revival could not prove the wire value: the generated
  client turns raw `dispatchedAtUtc: 1` into 2001-01-01 and drops falsy values.
  The new handwritten `agentAttemptEvidenceWire.ts` (`AgentAttemptEvidenceWireClient`,
  extending the generated evidence operation; `src/api/clients.ts` changes only
  that factory) reads a 200 body once, judges the raw claim, dispatch and
  completion values, hands the same text to the unchanged generated processing
  and then sets each timestamp from its verdict: missing or null stays absent;
  exactly the `DateTimeOffset` wire text (`yyyy-MM-ddTHH:mm:ss`, 0-7 fractional
  digits, `Z` or `±HH:mm` within 14 hours) of a real calendar date and time
  within the `DateTimeOffset` range becomes the instant computed from its
  fields; anything else becomes an invalid `Date`, which the facts reader shows
  as unavailable (dispatch, and so process and usage) and the identity binding
  rejects for the mandatory claim time. Non-200 answers, URL, authentication
  and the single request stay generated. Comments and the contract no longer
  claim that post-revival checks suffice.
- **Correction 2 (HMT-R1 remainder, proposed resolved for Codex to verify).**
  Correction 1 still handed the original timestamp members to the generated
  conversion, so a raw value such as `{"toString": 0}` threw a `TypeError` and
  rejected the whole answer before its verdict applied. The facade now sets the
  generated operation's protected `jsonParseReviver` to a stateless
  `withoutRootTimestamps`, which removes the three root timestamp members while
  the generated processing parses the original text; the verdicts still come
  from the facade's own parse of that text, and only the root's three members
  are affected (the same names below the root and nested timestamps such as
  `accountUsageDecision.retrievedAtUtc` stay generated). A first draft that
  re-serialized the body instead was dropped before any test ran: `1e400`
  parses to `Infinity` and would have been written back as `null`, turning a
  malformed count into absence.
- **Checks** in `logs/executor/hist-met-01/` (`index.txt`, complete output in
  `output/`): development runs r01-r04 (existing panel/composition tests,
  then the new pure and composed tests, all passed); red-first of the 18 new
  composed tests against the HEAD panel (r05: all 18 failed; restored by hash)
  and of the extended sealed journey (r15: facts region not found; restored by
  hash); strict e2e `tsc` (r06, r10, r12 after each journey edit); journey host
  build (r07 failed with the recurring CS2012 Domain.dll compiler lock, r08
  retry passed); `sealed-artifact-inspection.journey.ts` (r09 failed on the
  journey's own evidence-read count, below; r11 and r13 passed) and
  `historical-agent-attempts.journey.ts` (r14 passed); the prescribed final
  regression set (r16: 11 files, 306 passed) and the three added/modified test
  files by exact path (r17: 151 passed); lint (r18, exit 0, the nine existing
  warnings, none in changed files; r22 on the final input); build with `tsc -b`
  (r19 failed on a type error in the new pure test, fixed; r20 passed; that
  file 77 passed, r21); hygiene (r23 failed only on its own over-strict new
  heading check, which flagged the routine slice-heading replacements; r24,
  with an inbound-anchor search for each removed heading instead, passed; r25
  after the last document edits, passed). Not run: the whole frontend suite
  (shared presentation and hooks unchanged, so no broadening trigger),
  architecture and backend/API tests and client generation (backend inputs
  unchanged).
- **Correction 1 checks** in `logs/executor/hist-met-01/correction1/`
  (`index.txt`, `output/`): new raw-wire and factory tests (c01, 60 passed) and
  the panel with its facade-composed cases (c02, 67 passed); red-first with the
  facade's override replaced by plain generated processing (c03: 32 of the new
  through-client, factory and composed tests failed, the 35 others, covering
  valid and absent values, status handling and malformed values the browser
  already revives as invalid, passed; restored by hash); raw-wire, facts and
  composed regressions (c04, 4 files, 214 passed); factory, session and hook
  checks (c05, 23 passed); the prescribed regression set (c06, 11 files, 313
  passed); the whole frontend suite, broadened because the client factory
  changed (c07, 234 files, 4471 passed); lint (c08, the nine existing warnings,
  none in changed files); build with `tsc -b` (c09); strict e2e `tsc` (c10, the
  journey comment changed); both journeys through the facade with real host
  timestamps (c11 sealed, c12 historical, passed); hygiene (c13 failed only
  because its lexical scan matched the clearly fictitious bearer value in the
  new factory test; renamed, that file passed again in c14, hygiene passed in
  c15, and lint and build were rerun on the final input, c16 and c17). Retained:
  the r08 journey-host build (backend unchanged). Not run: architecture and
  backend/API tests and client generation.
- **Correction 2 checks** in `logs/executor/hist-met-01/correction2/`
  (`index.txt`, `output/`): raw-JSON-through-facade cases with non-callable
  `toString` objects and arrays for dispatch, completion and claim, a
  parse-fidelity case (`1e400` stays `Infinity`, nested timestamps stay
  generated) and composed facade cases, which also assert that the evidence
  route was the only evidence request and that the artifact still loads
  explicitly (d01, 2 files, 143 passed); red-first with only the reviver line
  removed, i.e. correction 1's behavior (d02: 19 of 20 selected tests failed
  with the generated `TypeError`s; the passing one is the bare-generated-client
  contrast; restored by hash); wire, factory, facts, panel, historical
  composition and evidence-hook tests (d03, 6 files, 247 passed); the
  prescribed regression set (d04, 11 files, 316 passed); lint (d05, the nine
  existing warnings, none in changed files); build with `tsc -b` (d06); both
  journeys serially (d07 sealed, d08 historical, passed); hygiene (d09).
  Retained with their scope: correction 1's whole frontend suite (c07, 4471
  passed; its broadening trigger, the client factory, is unchanged since) and
  strict e2e `tsc` (c10; the journey files are unchanged since), and the r08
  journey-host build (backend unchanged). Not run: architecture and
  backend/API tests and client generation.
- **r09 failure.** All new fact assertions passed; the journey then expected
  exactly one evidence GET and saw two identical ones for the same run and
  attempt. The journey host serves the development build and `main.tsx` wraps
  the app in React `StrictMode`; a second run of the unchanged selection effect
  is the likely cause, inferred and not independently proven (no StrictMode
  on/off control was run; the composed tests, without StrictMode, show one
  read). The assertion now requires every evidence read to name exactly that
  run and attempt and no request for any attempt or the inspected run after the
  facts are shown.
- **Journey scope.** Production frontend, generated client, MVC
  authentication, SQLite and the deterministic doubles. The sealed journey
  abandons the planning run through its form, records a newer objective
  through the intake and, with the newer run still selected, inspects the
  planning attempt from Run history: the evidence answer equals the stored row
  (duration truncated from ticks, timeout, counts, dispatch time) and the page
  shows its contract, times, `Proposed`, a clean exit with that duration and
  the timeout, with no artifact window, provider invocation, non-GET request
  or durable change. The Codex double emits no terminal turn event, so this
  attempt's usage is recorded as null and the page shows "Token usage unknown";
  known token and cache counts are proven only by the pure and composed tests.
  The historical journey asserts the stopped-before-dispatch attempt's stored
  nulls, the host's null answer and the page's truthful absence ("not
  dispatched", "Process not started", "Token usage: none (provider not
  invoked)") beside its semantic result and configured timeout. Since
  correction 1 both read through the timestamp facade, so the host's real
  serialized claim, dispatch and end times are accepted there. Nonzero exits,
  timeouts, cancellations, zero values and malformed answers, including
  malformed timestamps, are not constructed in the browser; they are proven at
  the pure, raw-wire and composed boundaries. The pre-existing
  duplicate-React-key console error also appears in these runs.

## Functional development

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
resumed functional development, superseding the documentation-only exclusion
of production and test work; experimental authority is not renewed. Roles,
challenge and explicit GO: [AGENTS.md](../../AGENTS.md); constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

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
entries, including WF-05, WF-06, HIST-ATT-01 and ART-WIN-01, are in Git history. Evidence, not current status or
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
  HIST-MET-01).** All read existing bounded views only: no replay, export,
  session resume or complete history. The three window defects HIST-ATT-01
  found are addressed by the published ART-WIN-01; its window acceptance bounds
  the text by the cursor advance but cannot prove that the text is the decoding
  of those bytes; the host remains the authority for content. HIST-MET-01,
  delivered for review and not accepted, shows the selected attempt's recorded
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
