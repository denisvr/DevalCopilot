# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery and records the phase at which it
was prepared ([recovery](development-workflow.md#recovery)). Git and code
prevail; compare branch, `HEAD`, `origin/main` and staged/unstaged/untracked
state first.

## Active slice: ART-WIN-01 reliable sealed text inspection from collaboration

This document records the implementation delivered for review. Consult
[planner-handoff.md](planner-handoff.md) for the review decision. Reconcile
publication with Git and the slice's independent publication-verification
record, when available. This record alone grants no publication authority.

- **Prepared at** first delivery for review. Preflight: `main`,
  `HEAD`/`origin/main`/live main `75372b18`, 0/0, empty index, only the
  planner's handoff selection modified, no untracked paths. Nothing is
  staged, committed or pushed.
- **Outcome.** An owner can open a collaboration message's attempt evidence
  and manually load its sealed artifact text: the message-linked request now
  sends the route's closed segment instead of the recorded purpose name, which
  `GetSealedAgentArtifactWindowEndpoint` answered with 404. Both readers (that
  viewer and the attempt-history viewer) accept a window only whole and
  coherent and keep text and paging intact when it is not. Rules:
  [window requests and acceptance](../architecture/agent-collaboration-evidence-inspection.md#sealed-agent-artifact-window-inspection);
  copy: [cockpit specification](../product/run-cockpit-specification.md#sealed-agent-artifact-window-inspection).
- **Changes (frontend only).** `artifactPurposes.ts` offers only distinct own
  allowlisted purposes (no inherited names such as `toString`) and owns the one
  purpose-to-segment mapping both route hooks use; anything else starts no
  request. The new pure `sealedArtifactWindow.ts` decides each answer whole
  (string text, boolean-or-null truncation, present nonnegative safe-integer
  cursors, offset <= next <= total, advance within the byte cap and positive
  while bytes remain, zero advance exactly when the text is empty, no more
  UTF-16 units than bytes, and the sealed total of the lifetime's first
  accepted window); closed non-`Ok` statuses keep their disclosures and use
  nothing else of their envelope. The shared hook keeps an accepted cursor and
  sealed total per lifetime, advances only on an accepted window, and retries
  the same offset after a failure or rejection. The viewer says when a sealed
  file is empty. The two `AgentAttemptHistoryPanel` fixtures the stricter rule
  rejected (no `truncated`; 68 characters claiming 60 bytes) were corrected,
  assertions unchanged. No backend, generated client, schema, dependency or
  harness change.
- **Checks** in `logs/executor/art-win-01/` (`index.txt`, complete output in
  `output/`): red-first of the new and updated tests against the HEAD
  production files (r01: 42 failed, 53 passed; restored by hash) and of the new
  journey against the HEAD frontend (r08: 404 at the first Load; restored by
  hash); focused slice tests (r10, 7 files, 136 passed); the whole frontend
  suite, broadened because shared window and purpose code changed (r11: 2
  fixture failures above, 4307 passed; that file then 43 passed, r12); lint
  (r13, exit 0, the nine existing warnings, none in changed files); build with
  `tsc -b` (r14 failed on a type error in a new test, fixed; r15 passed); strict
  e2e `tsc` (r03, then r17 after the journey changed; r02 was an invocation
  error); journey host build (r04 failed with a compiler file lock, r05 retry
  passed); `sealed-artifact-inspection.journey.ts` (r06, then r07 with the
  scene pinned) and `historical-agent-attempts.journey.ts` (r09), both passed;
  hygiene (see the index). Not run: architecture and backend/API tests and
  client generation (backend inputs unchanged).
- **Journey scope.** Production frontend, generated client, MVC
  authentication, SQLite and the deterministic doubles. One Codex planning
  attempt through its rendered control produces the provider-observed
  proposal; its attempt sealed the four purposes, standard error empty. From
  the live timeline the journey opens the message evidence, sees each purpose
  offered once, loads each through its segment for exactly that run and
  message, and compares the shown text with the integrity-checked sealed bytes
  (the empty one shown as empty); closing and reopening discards the text. The
  inspection adds no provider invocation, non-GET request, workflow row,
  artifact, Git or source change. The scene does not construct multi-window,
  non-ASCII or malformed answers; those are proven only by the decoder, hook
  and viewer tests. The pre-existing duplicate-React-key console error also
  appears here (12-14 times), as with the HEAD frontend (13 times, r08).

## Functional development

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
resumed functional development, superseding the documentation-only exclusion
of production and test work; experimental authority is not renewed. Roles,
challenge and explicit GO: [AGENTS.md](../../AGENTS.md); constraints:
[planner-handoff.md](planner-handoff.md).

## Prior published delivery

HIST-ATT-01 `75372b18` (sole parent `4b52e1cf`, 26 paths) passed independent
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
  unproven.
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
entries, including WF-05, WF-06 and HIST-ATT-01, are in Git history. Evidence, not current status or
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
- **Attempt and artifact inspection limits (HIST-ATT-01, ART-WIN-01).** Both
  read existing bounded views only: no replay, export, session resume or
  complete history. The three window defects HIST-ATT-01 found (message-route
  purpose names refused with 404, `in` admitting prototype purposes, defaulted
  `Ok` cursors) are addressed by ART-WIN-01, delivered for review and not
  accepted. Its window acceptance bounds the text by the cursor advance but
  cannot prove that the text is the decoding of those bytes; the host remains
  the authority for content. A duplicate React key console error seen in the
  journeys (also with the HEAD frontend) and a recurring compiler file lock on
  the first journey-host build remain unexplained.
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
