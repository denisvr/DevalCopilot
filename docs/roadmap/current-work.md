# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: HIST-ATT-01 historical run Agent-attempt inspection

This document records the implementation delivered for review. Consult
[planner-handoff.md](planner-handoff.md) for the review decision. Reconcile
publication with Git and the slice's independent publication-verification
record, when available. This record alone grants no publication authority.

- **Prepared at** delivery of correction 1 (Codex NO-GO, HATT-R1) for review.
  First preflight: `main`, `HEAD`/`origin/main`/live main `4b52e1cf`, 0/0,
  empty index, only the planner's handoff selection modified. Correction 1
  preflight: same `HEAD`/`origin/main`, empty index, the delivered diff (15
  modified, eight untracked) with Codex's review record in the handoff.
  Nothing is staged, committed or pushed.
- **Correction 1, HATT-R1 (proposed resolution, for Codex to verify).** A
  verified history row or evidence answer now needs a recorded status and a
  supported role/provider pair whose response contract belongs to that role
  (restated from `AgentAttemptIdentity` and `AgentAttemptContract`). The
  accepted row's identity, number and immutable role, provider and contract
  travel to the evidence read (`toAgentAttemptIdentityExpectation`), and
  verified evidence must repeat all of them; status and times may advance.
  Unknown, unsupported or contradictory answers fail whole and expose no
  metadata, purpose or text; unverified rows and answers keep their
  disclosure. Test fixtures that omitted the contract or paired Codex with
  Implementer were corrected to supported identities.
- **Outcome.** Run history's selected run now mounts the existing collapsed
  Agent-attempt history for exactly that run: an owner can open an older run,
  browse its attempts manually, select one and read its evidence and sealed
  windows, including a failed attempt that emitted no message. The rail keeps
  the same component for the current run. ADR-0037's inspector is unchanged.
  Navigation, validation and lifetimes:
  [inspection contract](../architecture/agent-collaboration-evidence-inspection.md#agent-attempt-history-and-evidence-inspection);
  cockpit copy: [specification](../product/run-cockpit-specification.md#agent-attempts-of-a-historical-run).
- **Changes (frontend only).** A feature-local decoder accepts history pages
  and evidence only whole and only when coherent for their request and the
  selected row's identity, number, role, provider and response contract. The history, evidence and artifact viewer
  moved from key remounts and generation counters to the shared owned-lifetime
  hooks (run, opening, selected attempt, chosen purpose), so replacement,
  return to an earlier run or attempt, close, reload and unmount end every
  subordinate read, text and retained callback. The shared sealed-window hook
  follows the same rule; its consumers are this viewer and the message-linked
  viewer (`AttemptArtifactWindowViewer` in `CollaborationEvidenceDrilldown`).
  The attempt route's window request now maps each recorded purpose to the
  route's own segment: before, the client sent `AgentStandardError` etc.,
  which `GetAgentAttemptArtifactWindowEndpoint` answers with 404, so its
  windows could never load. No backend, schema, generated client, dependency,
  permission or provider change.
- **Functional resumption.** The owner resumed functional roadmap work on
  2026-10-18; this is the first functional slice after it. Experimental
  restrictions and P9 limits are unchanged.
- **Correction 1 checks (fresh, complete output)** indexed in
  `logs/executor/hist-att-01/correction1/index.txt`: the expected fixture
  failures after the decoder change (c01, 35 failed); a hash-guarded mutant
  without the evidence-to-row binding (c02, killed by six decoder, hook and
  composed tests, restored); decoder, evidence hook and composed detail
  (c03, 104 passed); the dispatched filter (c04, 170); the other slice tests
  and panel consumers (c05, 199); lint (c06, exit 0, nine warnings in
  untouched files); build (c07); the journey, rerun because its production
  path changed (c08, passed). One interim fixture run was not retained.
  Retained unchanged: architecture tests (backend inputs unchanged), strict
  e2e `tsc` (journey unchanged), shared window-hook consumer runs (window code
  unchanged). No red-first run of the pre-correction decoder: that file was
  untracked and no copy was kept; Codex's reproduction is the red evidence.
- **First-delivery checks** in `logs/executor/hist-att-01/runs.log`: the dispatched
  Vitest filter (8 files, 170 tests); every added or modified test file
  (8 files, 169); direct consumers of the shared window hook, rail, cockpit
  and run-history detail (12 files, 257); a red-first run of the new window
  tests against the original shared hook (3 of 7 fail) and the composed
  tests against the naive composition (4 of 10 fail), both restored by hash;
  `npm run lint` (exit 0, nine warnings in untouched files); `npm run build`
  (includes `tsc -b`); the recorded strict e2e `tsc` flag set; the journey
  host build (the first attempt failed with a compiler file lock, the retry
  passed); `historical-agent-attempts.journey.ts` (passed); architecture
  tests (148 passed); links, sensitive-data and diff hygiene. No backend/API
  suites or client regeneration (inputs unchanged).
- **Journey scope.** Production frontend, generated client, MVC
  authentication, SQLite and the deterministic doubles. The scene uses only
  existing behavior: the account-usage stop ends a claimed planning attempt
  before dispatch (no message), the run is abandoned through its form and a
  newer objective is recorded. After a reload, and again after a second
  reload, the older attempt and its exact sealed manifest bytes are inspected
  from Run history; each inspection adds no invocation, non-GET request,
  workflow row, artifact, Git or source change and keeps the newer run
  selected. Its browser console shows a duplicate-React-key error that the
  unchanged `abandon-run` journey also shows against the HEAD frontend (37
  times); it is pre-existing and unexplained.

## Functional development

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
resumed functional development, superseding the documentation-only exclusion
of production and test work; experimental authority is not renewed. Roles,
challenge and explicit GO: [AGENTS.md](../../AGENTS.md); constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

WF-06 `4b52e1cf` (sole parent `e55709fe`, five paths) passed independent
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
  unexplained and not attributed to product code.
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
entries, including WF-05 and WF-06, are in Git history. Evidence, not current status or
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
- **Historical attempt inspection limits (HIST-ATT-01).** It reads existing
  bounded views only: no replay, export, session resume or complete history.
  Found and not fixed, being outside the selected scope: the message-linked
  window viewer still sends recorded purpose names that
  `GetSealedAgentArtifactWindowEndpoint` refuses with 404, so its windows show
  the generic error; `availableArtifactPurposes` filters with `in`, which
  admits prototype keys (the attempt path is guarded by its decoder); window
  `Ok` offsets are defaulted, not validated. A duplicate React key console
  error seen in the journeys and a one-time compiler file lock while building
  the journey host remain unexplained.
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
