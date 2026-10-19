# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: HIST-OUT-01 recorded outputs and selected content of a historical Agent attempt

This document records the implementation delivered for review. Consult
[planner-handoff.md](planner-handoff.md) for the review decision. Reconcile
publication with Git and the slice's independent publication-verification
record, when available. This record alone grants no publication authority.

- **Prepared at** completion of the first delivery, unstaged, uncommitted and unpushed; no GO exists. Preflight matched the
  planner's: `main`, HEAD/local origin/main/fresh live main `459344d3`, divergence 0/0, empty index, the planner's two modified
  documents (decision index, planner handoff) and new untracked ADR-0054, none edited here. HIST-INPC-01 was not reopened.
- **Outcome.** From a selected historical Agent attempt the owner can discover every message of its admitted bounded output set (at
  most 64) and explicitly read one message at a time, without searching unrelated collaboration-history pages
  ([ADR-0054](../decisions/0054-inspect-recorded-outputs-of-a-selected-agent-attempt.md)). Rules:
  [inspection contract](../architecture/agent-collaboration-evidence-inspection.md#agent-attempt-history-and-evidence-inspection),
  [cockpit specification](../product/run-cockpit-specification.md#agent-attempt-history-and-evidence).
- **Backend.** Two protected bodyless MVC GETs through `IApplicationMediator`, `GetAgentAttemptOutputsQuery` and
  `GetAgentAttemptOutputMessageQuery`, share one feature-owned `Policies/AgentAttemptOutputs` reader. It reuses the unchanged
  `ScalarAgentAttemptIdentity` and `CollaborationMessageAuthorPolicy`, selects at most 65 ordered scalar message headers by the message's
  own `AttemptId` (before the run is judged, through the existing attempt-reference index), inspects 64 and judges the whole set before any
  reference or text is returned: selected run, unique nonempty ID, strictly increasing positive sequence, protocol 1.0, a type the role may
  author, ProviderObserved provenance, an Agent actor equal to the attempt's role/provider and, for a nonempty set, a recorded dispatch
  time. Nothing is filtered; zero rows is `Empty` for any admitted role. The member query repeats that proof, then reads only the
  admitted selected message constrained to its run and `AttemptId` through the unchanged `CollaborationHistoryRows.Project`/`ToEntry` and
  rechecks ID, sequence, `EqualsExact` time, version, type, provenance and actor; the fixed 404s are `agent_attempt_outputs.not_found` and
  `agent_attempt_output_message.not_found`. API-owned closed graphs with canonical Int64 and UTC round-trip strings. No Domain,
  persistence, migration, index, claim/dispatch/result, artifact-store, provider, dependency, shared lifetime or shared harness change;
  `ScalarAgentAttemptIdentity`, `CollaborationHistoryRows` and every input, history, reply, attribution and verification reader are
  byte-unchanged. The generated client is additive (+623/-0, two operations and their models).
- **Frontend.** Operation-scoped raw-JSON facades (`AgentAttemptOutputsWireClient`, `AgentAttemptOutputMessageWireClient`) and whole-answer
  decoders check exact keys at every level, the status tuples, the selected immutable header, every position, unique ID, strictly
  increasing exact Int64 text, canonical time text and the role/type table, then selected-member equality (provenance provider-observed,
  actor exactly the attempt) over the unchanged history content admission, with raw sequence/time text kept as the host wrote it. New
  `useAgentAttemptOutputs`/`useAgentAttemptOutputMessage` use the unchanged `useOwnedLifetime`/`useOwnedState`: the opening owns a local
  disclosure (ten, then groups of ten, no request) and numbered visits decided against the queued choice; the content hook is keyed by
  `(opening, run, attempt, message, visit)`. The independent collapsed "Recorded collaboration outputs" section (beside inputs and
  verification) reads once on opening, offers "Inspect output N" per visible reference into one replaceable inspector with Close and
  failure-only Retry, and renders everything as literal text with the read-now limitations. No prefetch, polling, automatic retry, link,
  storage or mutation.
- **Journey.** `historical-attempt-inputs.journey.ts` keeps every input assertion and, after them with the newer objective still selected,
  opens the older Resolver's outputs once (no query), compares the complete host answer and the rendered references to the stored
  messages that record that attempt (count, order, identities, types, sequences, times, exact keys), then explicitly inspects a Decision
  and the revised Proposal and compares each answer and rendered body to its stored row. Exactly one outputs GET and those two content GETs
  occurred, none before the explicit clicks, none for the newer run; no non-GET, artifact-window read, provider invocation or durable/source
  change. Empty, oversized, 11/64/65 and corrupt output sets stay below the browser.
- **Checks.** Execution evidence: `logs/executor/hist-out-01/` (`dev-*`, `neg-*`, `final-*`). Ordinary failed runs retain diagnostics;
  the negative-control logs mostly retain abridged named failures/counts and restoration hashes, not complete command/mutation/output
  records. The first M2 compiler diagnostic and the original strict-e2e invocation are not retained; neither can be reconstructed or
  authenticated from later passes. See the planner review for independent checks and the accepted evidence limits. Final matrix on stable
  inputs (only two test-only fixture files changed after the input-hash baseline `final-00`, listed in `final-10`): API `--no-incremental`
  build ran NSwag and reproduced the client byte-for-byte (blob `a18c1e36…`, the generator writes CRLF, Git normalizes) (`final-01`);
  Application `GetAgentAttemptOutputs`+`GetAgentAttemptOutputMessage` 129 passed in 33 s (`final-03`); API endpoint classes plus launch
  authentication plus the Int64 wire class 23 passed (`final-04`); architecture 172 passed (`final-05`); lint 0 errors with the same nine
  warnings in unchanged files (`final-06`); `npm run build` (includes `tsc -b`) passed (`final-08`); whole frontend once, 253 files/5299
  passed (`final-09`). Strict e2e `tsc` (recorded flag set) exit 0 (`dev-11`), journey-host build 0/0 (`dev-12`) and one serialized
  journey passed in 11.6 s (`dev-13`) ran on the final product and e2e inputs and are retained (later changes touched only test fixtures
  and documents). Bounded negative controls, each restored by SHA-256: backend M1 foreign run not refused (3 failed), M2b undispatched
  nonempty admitted (3), M3 65th row not observed (3), M4 role/type policy ignored (4), M5 reread association dropped (3), M6 reread
  actor dropped (6), M7 reread time not exact (1) (`neg-01`, `neg-01b`); frontend F1 actor not bound (1), F2 type table ignored (6), F3
  sequence order unchecked (3), F4 Close not visit-scoped (1), F5 visit missing from the content key (1), F6 disclosure starts at eleven
  (6), F7 selection not decided against the queued choice (7) (`neg-02`); journey altered expected Decision summary failed at that
  assertion (`neg-03`).
- **Failures retained.** First Application run (`dev-02`, 26 failed) and first API run (`dev-05`, 1 failed) exposed defects of the new
  tests: a run holds one running attempt (`UNIQUE constraint failed: attempts.RunId`) and the tests seeded a second one; an Application
  parameter assertion also failed to recognize an Int32 limit parameter. The seeding now completes the earlier attempt first and
  the parameter assertion accepts its actual numeric type; `dev-03`/`dev-06` passed. The first component run (`dev-10`) also failed an
  existing exact-toggle-list expectation, corrected to include the new outputs toggle. The first backend mutant M2 did not compile
  (only its failure summary is retained, not the compiler diagnostic) and is not counted as a kill; M2b replaced it (`neg-01b`). The first final
  `npm run build` (`final-07`) failed with three `tsc` type errors in new tests whose fixture typed the output type as a three-member
  literal union; the fixture field was widened and `final-08`/`final-09` passed. The retained ordinary failure diagnostics identify test
  defects; they do not establish a blanket historical absence of product failures or authenticate missing/abridged originals.
- **Retained, unrerun evidence.** `CollaborationHistoryRows`, `ScalarAgentAttemptIdentity`, the message-linked reader, the input,
  verification and attribution readers and every other journey are byte-unchanged and keep their prior evidence with its scope; they were
  not rerun. Journey console output shows 44 duplicate-React-key and 3 connection-negotiation errors; no HEAD comparison was run, so
  cause and count equivalence remain unproved, as for the earlier journeys.
- **Not run.** Whole backend/API suites, other journeys, a mutation campaign beyond the bounded controls above, real providers and P9
  experiments. Increments 4/5/6 remain incomplete, ADR-0050 stays Proposed, P9 unproven and AI Memory deferred.

## Functional development

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
resumed functional development, superseding the documentation-only exclusion
of production and test work; experimental authority is not renewed. Roles,
challenge and explicit GO: [AGENTS.md](../../AGENTS.md); constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

HIST-INPC-01 `459344d3` (sole parent `80a2ec22`, 66 paths) passed independent publication verification
(`logs/planner-review/hist-inpc-01/publication-verification.json` and `.log`); its record, committed with pre-review wording, is
this file at `459344d`.

HIST-VER-01 `80a2ec22` (sole parent `26c3f24a`, 74 paths) passed independent publication verification
(`logs/planner-review/hist-ver-01/publication-verification.json` and `.log`); its record, committed with pre-review wording, is
this file at `80a2ec2`.

HIST-INP-01 `26c3f24a` (sole parent `96a21d73`, 49 paths) passed independent publication verification
(`logs/planner-review/hist-inp-01/publication-verification.log`); its record, committed with pre-review wording, is this file
at `26c3f24`.

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
- **HIST-INPC-01 (published).** The two original EF1002 analyzer failures of its first Application test build have no retained
  output, and later passes neither reconstruct nor authenticate them; its development, final and negative-control failures stay
  as recorded in this file at `459344d` and its `logs/executor/hist-inpc-01/`. The journey console counts of that slice (45
  duplicate React keys, three connection negotiations) and those of HIST-OUT-01 (44 and three) have no HEAD comparison, so cause and
  count equivalence stay unproved.
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
  HIST-MET-01, HIST-INP-01, HIST-VER-01, HIST-INPC-01, HIST-OUT-01).** All read bounded views only: no replay, export,
  session resume or complete history. HIST-OUT-01 (unpublished, under review) shows at most 64 stored collaboration messages that
  record the attempt as their own attempt and one of them at a time, as read now: not the complete provider response, proof of what was
  generated or seen, a current approval or execution success. A host-constructed escalation has no message-level attempt reference
  and is not found; no outcome, event or reply is used to infer an expected output, and an empty set says only that none was recorded
  in that read. Its set proof and content read are separate reads without an atomic snapshot. Published HIST-INPC-01 shows at most 64
  input references and one stored message at a time as read now, never the exact prompt, all context or what an Agent saw; its
  set proof and content read are
  separate reads without an atomic snapshot. Published HIST-VER-01 shows recorded verification membership and
  sealed output as read now, never which bytes an Agent saw or a current approval; at most 32 members are inspected and a
  view keeps at most 256 KiB of one output. The three window defects HIST-ATT-01
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
