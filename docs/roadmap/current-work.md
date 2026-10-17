# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery. Procedure:
[development workflow](development-workflow.md). Git and code prevail; compare
branch, `HEAD`, `origin/main` and staged/unstaged/untracked state first.

## Active slice: WF-05 documentation workflow finishing

- **Status: implemented; UNSTAGED, UNCOMMITTED and UNPUSHED for Codex review;
  no GO.** Preflight matched: `main`, `HEAD`/`origin/main`/live main
  `e614c795`, 0/0, clean.
- **Paths.** New exact `e614c795` checkpoint copies (History); modified this
  file, [planner](planner-handoff.md), [workflow](development-workflow.md) and
  [rework map](documentation-rework-map.md) row CW-A.
- **Findings.** EX-05-01: the dispatched CW-A target (`d60d718` GO heading)
  is pre-publication wording; the row's original fragment, at map commit
  `a593009`, records TEST-48-01 as published. The owner chose that pinned
  link; Codex reviews it.
- **Checks.** `logs/executor/wf-05/runs.log`: snapshot, scope, mapping,
  link, continuity, byte and hygiene checks; final 12/12. No product suites,
  old checkers or mutation controls.
- **Next action:** independent Codex review. No commit or push.

## Documentation priority

WF-01–WF-04 are published and the selected rework accepted at `e614c795`;
functional work, tests and P9 stay stopped. Roles, challenge and explicit GO:
[AGENTS.md](../../AGENTS.md); constraints: [planner-handoff.md](planner-handoff.md).

## Prior published delivery

WF-04 `e614c795` (sole parent `0a57216c`, five mode-100644 paths) passed
independent publication verification 5/5 (`logs/planner-review/wf-04/`); its
[pre-GO record](current-work-history-e614c79.md#active-slice-wf-04-checkpoint-history-consolidation)
is historical. WF-03N back to WF-02A:
[deliveries](current-work-history-e614c79.md#prior-published-delivery),
[per-slice limits](current-work-history-0a57216.md#prior-published-delivery);
TEST-48-01 `d60d718` and earlier: this file at `a593009` and before.
Excluded-artifact reads grant no retrospective authorization or new read; do
not reopen or republish delivered slices.

## Active evidence limits

Archiving and progressive reading resolve or waive none of these:
- **WF-02A evidence loss.**
  `logs/executor/wf-02a/correction1/development-runs-transcribed.md` is a
  partly abridged derivative chat transcription, not recovered or
  independently authenticated output; original failed runs, checker and
  timings are unavailable; run B's exit status is unknown. Never reconstruct.
- **Failed, missing and derivative evidence stays as recorded**, including C's
  invalid negative-01 kills, recovered checker and aborted-write note, F N01
  and G N23, H/I generator output, B/I ENOBUFS, M publisher and RV-N01 stream
  gaps, WF-04 dev-01, invalid negative-01 and RV-04-01, failed planner
  verifications, checker defects and chat-only output
  ([detail](current-work-history-e614c79.md#active-evidence-limits)).
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
entries are in Git history. Evidence, not current status or approval.

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
