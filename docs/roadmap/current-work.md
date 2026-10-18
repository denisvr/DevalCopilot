# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: HIST-VER-01 recorded verification evidence of a selected Agent attempt

This document records the implementation delivered for review. Consult
[planner-handoff.md](planner-handoff.md) for the review decision. Reconcile
publication with Git and the slice's independent publication-verification
record, when available. This record alone grants no publication authority.

- **Prepared at** completion of NO-GO correction round 1 (HVR-R1–R3), unstaged, uncommitted and unpushed; no finding is
  closed and no GO exists until review. Correction preflight matched the planner's: `main`, HEAD/local origin/main `26c3f24a`,
  divergence 0/0, empty index, nine modified tracked paths and 59 untracked files; the live remote was not rechecked. During the
  correction `AGENTS.md`, `CLAUDE.md` and `development-workflow.md` changed on disk outside this executor (22:56–22:57, workflow
  report/continuity rules); they are untouched and not part of this correction. The planner-owned ADR-0052, decision index and
  planner handoff were not edited. The original delivery began from the same HEAD with only the planner's two modified documents
  and new ADR-0052. HIST-INP-01 was not reopened.
- **HVR-R1 correction.** The SQL exit-code guard keeps the same-typed `% int.MaxValue` exactness check and now compares all three
  Int32 values that are not their own remainder directly (`int.MinValue`, `-int.MaxValue`, `int.MaxValue`); INTEGER affinity
  stores an integral REAL as INTEGER, so a direct comparison only matches the exact integer and no wider modulus or CAST returns.
  New file-backed SQLite regressions: the evidence handler records a failed diagnosis member with each of `int.MinValue`,
  `-int.MaxValue`, `-int.MaxValue + 1`, -2, -1, 1, 2, `int.MaxValue - 1` and `int.MaxValue` exactly, with its Recorded stdout
  descriptor and the sibling member; the window handler serves that member's stream for seven boundaries with the exact store
  call. Storage controls stay refused (last-of-32 and window, before any store call): REAL -2147483646.5, -2147483647.5 and
  2147483646.5, 2147483648, -2147483649, BLOB and TEXT, beside the existing 1.5, 4294967297 and text defects. The API carries the
  four extreme codes as exact raw JSON numbers with the Recorded stream and serves their windows. Membership and ownership checks
  are unchanged.
- **HVR-R2 correction.** Within an opening, every member/stream choice is a numbered visit decided against the queued choice:
  choosing after Hide or after another stream starts a new visit (even for the same stream in one batch), choosing the current
  stream keeps it. `clearSelection` (Hide) belongs to the visit it was rendered for and clears only while that visit is still the
  queued choice, so a retained Hide of A cannot end B (separately or queued before it in one batch) or a later A visit (A-B-A). The
  selection exposes `visit`, and the window lifetime key is `(opening, run, attempt, member, stream, visit)`, so a same-batch
  Hide/reselection derives a fresh idle frame before the successor's first frame, its next manual load starts at offset zero, it
  reads nothing by itself, and a late answer or retained `load` of the ended visit is inert. Feature-owned only: the shared
  `useOwnedLifetime`/`useOwnedState` primitives, manual paging, overlap guard, decoders and the 256 KiB budget are unchanged; no
  forced remount. Regressions: seven evidence-hook visit tests (including the current Hide, an equal visit number in a later
  opening, parent reload and unmount), two window-hook tests for a new visit of the same tuple, six composed two-hook tests
  (each scenario of the review plus the current Hide) and one rendered-panel test (Hide then View after a pending load).
- **HVR-R3 correction.** `SeededVerificationMember`, `SeededVerificationScene` (Application tests) and `SeededVerificationLineage`
  (API tests) moved unchanged, with the same visibility and comments, into files of their own names; no fixture redesign.
- **Docs.** The inspection contract and cockpit specification now name the visit (Hide then reselection starts from zero; an earlier
  Hide never hides a later choice).
- **Correction checks.** Complete outputs: `logs/executor/hist-ver-01/correction-01/` (`c01`–`c19`, each with its command line).
  Pre-fix controls (the new `-int.MaxValue` comparison temporarily replaced by a duplicate `int.MaxValue` comparison, restored by
  copy and SHA-256 `c2ca2032…`): Application focused 2 failed (exactly `-2147483647` in each handler)/62 passed (`c01`); API focused 2
  failed (same value, metadata and window)/6 passed (`c04`). Fixed: Application focused 64 passed (`c02`), API focused 8 (`c03`).
  Frontend regressions against the unfixed hooks: 11 failed/92 passed (`c05`); fixed 103 passed (`c06`). The planner's two
  retained lifetime reproductions, copied unchanged into the hooks folder and removed afterwards, now pass 5/5 and 4/4
  (`c07`, `c08`). On stable corrected inputs: both Application handler classes 150 passed (`c09`); both endpoint classes plus
  launch-auth inventory 58 (`c10`); architecture 161 (`c11`); affected frontend decoder, contract, hooks, panel and existing
  attempt/history files 7 files/317 (`c12`); `npm run lint` 0 errors with the same nine warnings in unchanged files (`c13`);
  `npm run build` passed (`c14`); whole frontend once, 243 files/4852 passed (`c15`); strict e2e `tsc` (recorded flag set) exit 0
  (`c16`); journey-host build 0 warnings/0 errors, no compiler lock (`c17`); one serialized extended `collaboration.journey.ts`
  passed in 1.6 min (`c18`); `dotnet build` of the API `--no-incremental` left the generated client at `07d48b80…` (`c19`).
  Commands: `dotnet test tests/DevalCopilot.Application.Tests --no-restore -m:1 --filter
  "FullyQualifiedName~GetAgentAttemptVerificationEvidenceQueryHandlerTests|FullyQualifiedName~GetAgentAttemptVerificationOutputWindowQueryHandlerTests"`;
  `dotnet test tests/DevalCopilot.Api.IntegrationTests --no-restore -m:1 --filter
  "FullyQualifiedName~AgentAttemptVerificationEvidenceEndpointTests|FullyQualifiedName~AgentAttemptVerificationOutputWindowEndpointTests|FullyQualifiedName~LaunchSessionAuthenticationTests"`;
  `dotnet test tests/DevalCopilot.Architecture.Tests --no-restore -m:1`; from the frontend folder `npx vitest run <seven files>`,
  `npm run lint`, `npm run build`, `npx vitest run`, the strict `npx tsc --ignoreConfig …` line, `dotnet build
  ../../../tests/DevalCopilot.Api.IntegrationTests/BrowserJourney/Host/BrowserJourneyHost.csproj` and `npx playwright test -c
  playwright.journey.config.ts e2e/collaboration.journey.ts`. The journey log records 154 duplicate-React-key and 14 connection
  negotiation console errors from the first browser output, before the section is used; the first delivery's journey log captured
  no console lines, so the counts are not compared, and no HEAD-frontend comparison was run in this round.
- **Outcome.** A selected CodeReviewer attempt of `ImplementationReview` or
  `VerificationDiagnosis` has a collapsed "Recorded verification evidence"
  section, independent of evidence, inputs, Agent artifacts and any produced
  message. It shows the recorded checkpoint, every member in order (snapshot
  command name, Passed/Failed, exit code, raw UTC times) and truthful stdout/stderr
  descriptors (NotRecorded, Unavailable, Recorded with false/true/unknown truncation);
  a Recorded stream is chosen, loaded and paged manually, with a 256 KiB local view limit
  disclosed as such. Rules:
  [inspection contract](../architecture/agent-collaboration-evidence-inspection.md#agent-attempt-history-and-evidence-inspection),
  [cockpit specification](../product/run-cockpit-specification.md#agent-attempt-history-and-evidence),
  [ADR-0052](../decisions/0052-inspect-recorded-verification-evidence-of-a-selected-agent-attempt.md).
- **Backend.** Two protected, bodyless GETs and mediator queries
  (`GetAgentAttemptVerificationEvidence`, `GetAgentAttemptVerificationOutputWindow`), one MVC class each and
  API-owned response graphs. Both use one feature-owned read policy under
  `Policies/AgentAttemptVerification` that applies the unchanged `ScalarAgentAttemptIdentity`, reads at most 33
  memberships by the attempt/sequence index (32 inspected), proves the whole set and the exact historical
  workspace/checkpoint/fingerprint ownership by recorded identity, and classifies enums and guards numbers and strings
  in SQL before conversion. The list reads no path, hash or byte; a window reproves the membership, resolves only its
  stream and passes the admitted path and `sha256:`-plus-64-lowercase-hex hash unchanged to
  `IArtifactStore.VerifyAndReadSealedAsync`. Offsets outside 0..2^53-1, caps outside 64..65536 and an offset past the
  recorded length are refused, never clamped. Strict model binders keep rejected raw values out of the response. No
  Domain, persistence, migration, claim/dispatch, snapshot/digest, recipe, artifact-store, existing-reader or shared
  test-infrastructure change; the old verification output endpoint is untouched. The NSwag client is generated, additive
  (+493 lines) and reproducible.
- **Frontend.** Operation-scoped wire facades keep the parsed bodies; local closed-shape decoders validate the whole
  answer and each window against the requested identities, selected descriptor and byte cursor without re-encoding
  text. Two new hooks own the opening/selection and the member/stream window with the existing owned lifetimes. The
  existing history panel mounts the section only for the two supported contracts.
- **First-delivery checks (pre-correction inputs; rerun scopes above supersede them).** Complete outputs:
  `logs/executor/hist-ver-01/` (`dev-*`, `final-*`). Application: evidence handler
  82 passed (including an EXPLAIN QUERY PLAN proof), window handler 40 passed (file-backed SQLite, real store over disposable files). API: 46 passed (both
  endpoint classes plus the actual OpenAPI document). Architecture: 161 passed (eight new). Frontend: 215 new tests
  in five files; whole frontend 243 files / 4836 passed (`final-01`, once, justified by the new factories and
  composition); lint unchanged at the same nine warnings in unchanged files; `npm run build` (includes `tsc -b`) passed.
  Strict e2e typecheck exit 0; journey-host build passed (no compiler lock this time). API build `--no-incremental` and the generated client hash are unchanged before/after
  (`07d48b80…`). The extended `collaboration.journey.ts` passed once (`dev-22`); a negative control that changes the
  journey's expected passed stdout failed at that assertion (`neg-01`), then the file was restored by hash.
- **Retained, unrerun evidence.** The 183 ADR-0037/0051 consumer tests (the shared identity is unchanged), the artifact
  store containment proofs (store unchanged) and the historical-agent-attempts, sealed-artifact and
  historical-attempt-inputs journeys. The latter three use Planner/Critic/Resolver attempts, which do not show the new
  section; they were not rerun. The correction changes none of their inputs (shared identity, store and those journeys'
  files); the collaboration journey file is unchanged by it. The first delivery's journey negative control (`neg-01`) was not
  repeated.
- **Journey.** After the newer objective, the older run's diagnosis (attempt 5, two Failed members) and ordinary review
  (attempt 7, two Passed members) are opened once each through the rendered controls. The wire answer and the page equal
  the stored membership, checkpoint and stream rows; every recorded stream is loaded explicitly and its returned and
  rendered text equals the sealed file on disk and the executable's own output. The section's requests were bodyless
  GETs for the older run only (two membership reads, one window per loaded stream), with no inputs, Agent artifact
  window, provider invocation, durable change or source change. Malformed, oversized, 33-member and tampered cases stay
  below the browser. **Pre-existing stale expectation corrected:** the journey's historical-run button list
  predated HIST-ATT-01's "Show Agent attempt history" toggle and failed at that assertion on the first run
  (`dev-21`) independently of this slice; it now lists the toggle.
- **Failures retained.** `dev-01` (test compile error), `dev-02` (seven failures) and `dev-03` (one): three of
  `dev-02` were one real production defect (a wider modulus constant made EF cast both sides to INTEGER, so REAL execution
  numbers, checkpoint numbers and exit codes were accepted; fixed with a same-typed modulus), the rest were test defects (NUL
  text bound C-terminated by the SQLite provider, a duplicate position, a table that is only joined, seed numbering).
  `dev-07`/`dev-08` architecture assertions and `dev-13`/`dev-15` frontend tests failed on test defects. TypeScript errors found by the first build of the new tests were printed to the console and not retained;
  that output remains lost and no later pass recovers or stands in for it. The first delivery reported its browser console
  errors (duplicate React keys, connection negotiation) as the same pre-existing ones. The planner's independent
  reproductions exposed HVR-R1 and HVR-R2 that the first delivery's tests missed. Correction-round failures are only the
  intentional pre-fix controls `c01`, `c04` and `c05` above; no other correction run failed.
- **Not run.** Whole backend/API suites, other journeys, mutation campaign, real providers or P9 experiments. No
  dependency, schema, capture/store, shared identity, claim/dispatch/result, snapshot, recipe, harness or shared
  database-fixture change. Increments 4/5/6 remain incomplete, ADR-0050 stays Proposed, P9 unproven and AI Memory
  deferred.

## Functional development

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
resumed functional development, superseding the documentation-only exclusion
of production and test work; experimental authority is not renewed. Roles,
challenge and explicit GO: [AGENTS.md](../../AGENTS.md); constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

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
  HIST-MET-01, HIST-INP-01, HIST-VER-01).** All read bounded views only: no replay, export,
  session resume or complete history. HIST-VER-01 (unpublished, under review) shows recorded verification membership and
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
