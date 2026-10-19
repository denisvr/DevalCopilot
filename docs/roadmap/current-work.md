# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: HIST-INPC-01 complete bounded input references and selected content

This document records the implementation delivered for review. Consult
[planner-handoff.md](planner-handoff.md) for the review decision. Reconcile
publication with Git and the slice's independent publication-verification
record, when available. This record alone grants no publication authority.

- **Prepared at** completion of the first delivery, unstaged, uncommitted and unpushed; no GO exists. Preflight matched the
  planner's: `main`, HEAD/local origin/main/fresh live main `80a2ec22`, divergence 0/0, empty index, the planner's two modified
  documents (decision index, planner handoff) and new untracked ADR-0053, none edited here. HIST-VER-01 was not reopened.
- **Outcome.** Inside a selected historical Agent attempt, every reference of an admitted input set of at most 64 is reachable and
  the owner can read one referenced collaboration message at a time
  ([ADR-0053](../decisions/0053-inspect-the-content-of-recorded-inputs-of-a-selected-agent-attempt.md)). Rules:
  [inspection contract](../architecture/agent-collaboration-evidence-inspection.md#agent-attempt-history-and-evidence-inspection),
  [cockpit specification](../product/run-cockpit-specification.md#agent-attempt-history-and-evidence).
- **Backend.** The ADR-0051 set proof moved unchanged (same three bounded scalar reads, same statements) into the feature-owned
  `Policies/AgentAttemptInputs` reader with its three projection/row types, shared by exactly the two input queries; a test proves the
  content read's first three statements equal the reference inspection's. `GetAgentAttemptInputs` gains optional `limit` (default 10,
  1..64): a feature-owned strict binder (absent or one blank value is the default; any repeated value, even repeated blanks, and every
  non-canonical text bind a rejected value) and an Application validator give the shared `validation.invalid_limit` at `#/limit`
  without the value; nothing is clamped and the whole set is proved whatever the limit. New `GetAgentAttemptInputMessageQuery`,
  handler and one MVC endpoint: unknown run, missing/foreign/non-Agent attempt and nonmember share
  `agent_attempt_input_message.not_found`; an unprovable set is root Unavailable whichever message was asked; only a proved member is
  read, through the unchanged `CollaborationHistoryRows.Project`/`ToEntry`, and a reread identity/version/type/sequence/time
  (`EqualsExact`) that differs from the reference is Unavailable. API-owned closed DTO graph; both sequences Int64 strings, both times
  raw UTC round-trip strings. `ScalarAgentAttemptIdentity`, `CollaborationHistoryRows`, the message-linked reader and every other
  consumer are unchanged; no Domain, persistence, migration, dependency or shared harness change. The generated client is additive
  (+408/-2: the optional `limit` argument, the new operation and its models).
- **Frontend.** The inputs facade passes `limit`; the reference decoder binds prefix length and omission to the requested limit
  (default ten for old callers). The section requests `limit=64` once per opening, shows ten references and reveals loaded ones in
  local chunks of ten on request (labeled apart from host omission), with one "Inspect input N" action each into a single
  "Selected recorded input" inspector (Close, failure-only retry). A new facade keeps the parsed body; a new decoder checks exact keys
  at every level, the status tuple, the selected header and reference (raw Int64 and time text before any Date conversion), then
  reuses the unchanged history entry validation for participants, provenance and content schema. The opening owns the disclosure and
  numbered input visits (decided against the queued choice); the content hook is keyed by `(opening, run, attempt, message, visit)`.
  Shared `useOwnedLifetime`/`useOwnedState` are unchanged; no forced remount, prefetch, polling, automatic retry, link or storage.
- **Journey.** `historical-attempt-inputs.journey.ts`, after the newer objective, now also checks each opening requests
  `?limit=64`, then explicitly inspects the older run's resolution inputs 1 (Proposal) and 2 (Challenge): each answer's root, header,
  input and message (sequence, time, version, type, provenance, participants, summary, fields) and the rendered header, participants,
  summary and labeled fields equal the stored `collaboration_messages` row; the inspector is then closed. Exactly those two content
  GETs occurred, none before the explicit clicks; no non-GET, artifact-window read, provider invocation or durable/source change;
  the Planner's empty set is preserved. 11/64/65 and corrupt sets stay below the browser.
- **Checks.** Complete outputs: `logs/executor/hist-inpc-01/` (`dev-*`, `neg-*`, `final-*`, each with its command line and
  timing). Final matrix on stable inputs: API `--no-incremental` build ran NSwag and left the client at `657bc13c…` (blob
  `3559032f…`; the generator writes CRLF, Git normalizes) (`final-01`); Application both input handler classes 178 passed in 54 s
  (`final-03`); API metadata endpoint, Int64 wire, new endpoint and launch-auth 52 passed (`final-14`, after `final-13` rebuilt the
  escape fix below); architecture 164 passed (`final-05`); lint 0 errors with the same nine warnings in unchanged files
  (`final-10`); `npm run build` (includes `tsc -b`) passed (`final-11`); whole frontend once, 246 files/5009 passed (`final-12`).
  Strict e2e `tsc` (recorded flag set) exit 0 (`dev-09`), journey-host build 0/0 with no compiler lock (`dev-10`) and one serialized
  journey passed in 12.3 s (`dev-11`) ran on the final product inputs and are retained. Bounded negative controls, each restored
  by SHA-256: backend M1 limit ignored (6 failed), M2 reread time check removed (1), M3 membership ignored (19), M4 repeated limit
  admitted (2) (`neg-01`); frontend F1 Close not visit-scoped (1), F2 visit missing from the content key (1), F3 disclosure owned by
  the selection (2), F4 raw time comparison removed (1), F5 limit not sent (5) (`neg-02`); journey expected summary altered failed
  at that assertion (`neg-03`).
- **Failures retained.** The first Application test build failed with two EF1002 analyzer errors (interpolated raw SQL in two new test
  helpers; output printed to the console, not retained in a file); fixed by binding the statement to a local first. Development
  frontend runs `dev-05` and `dev-06` each failed one rendered test on test defects (input 12 was still behind the local disclosure;
  a first-frame check ran after the next answer settled). Final `final-08` whole frontend failed one existing composition test that
  pinned the request arguments without the new `64`; the expectation was updated, the file passed (`final-09`) and the cheap final
  frontend checks were rerun. The Edit tool wrote two test strings as raw `€`/`٥` instead of `\u` escapes; restored as ASCII escapes
  and the API filter rerun (`final-14`). No product-code failure occurred.
- **Retained, unrerun evidence.** `CollaborationHistoryRows`, `ScalarAgentAttemptIdentity` and the message-linked reader are
  byte-unchanged, so the GetCollaborationHistory/Reply and ADR-0037/0051 attribution/message-evidence consumers keep their prior
  evidence with its scope; verification handlers, the artifact store and the historical-agent-attempts, sealed-artifact and
  collaboration journeys are unaffected and not rerun. Journey console output shows the known 45 duplicate-React-key and 3 connection
  negotiation errors; no HEAD comparison was run, so cause and count equivalence remain unproved.
- **Not run.** Whole backend/API suites, other journeys, mutation campaign, real providers or P9 experiments. Increments 4/5/6
  remain incomplete, ADR-0050 stays Proposed, P9 unproven and AI Memory deferred.

## Functional development

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
resumed functional development, superseding the documentation-only exclusion
of production and test work; experimental authority is not renewed. Roles,
challenge and explicit GO: [AGENTS.md](../../AGENTS.md); constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

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
  HIST-MET-01, HIST-INP-01, HIST-VER-01, HIST-INPC-01).** All read bounded views only: no replay, export,
  session resume or complete history. HIST-INPC-01 (unpublished, under review) shows at most 64 input references and one stored
  message at a time as read now, never the exact prompt, all context or what an Agent saw; its set proof and content read are
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
