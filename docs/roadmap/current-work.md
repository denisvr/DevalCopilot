# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery. Procedure:
[development workflow](development-workflow.md). Git and code prevail; compare
branch, `HEAD`, `origin/main` and staged/unstaged/untracked state first.

## Active slice: WF-02A engineering context and checkpoint reduction

- **Status: UNSTAGED, UNCOMMITTED and UNPUSHED; correction R1 of the Codex
  NO-GO returned for independent review; no GO.** Dispatch and correction
  preflights matched: `main`; `HEAD`, local `origin/main` and live main
  `d60d718ccf5265f80d26ea06004dc0ad20d359e9`; 0/0; empty index; the planner
  checkpoint (now 6920 bytes, `fe95a672...16bb`) preserved byte-identically.
- **Paths.** Modified: `docs/engineering-context.md` (77934 to about 8 KiB)
  and this file. New: [documentation-rework-map.md](documentation-rework-map.md)
  (every baseline section and item of both files; corrections CR-01 to CR-05).
  No architecture document changed: each removed detail already had an
  authoritative ADR or architecture/protocol section. Ignored:
  `logs/executor/wf-02a/` (checker, configuration and results).
- **Development failures.** The first full checker run exited 1 with 11
  failures: 13 of 515 claim phrases unmatched because destinations used other
  wording (each reworded only after the equivalent sentence was read; one regex
  also tripped on a stripped `*`) and one false positive, the standards
  revision, an EngineeringStandards object. A later run failed one claim after
  the context was reworded. One final matrix passed and was repeated after a
  map sentence was corrected. The checker rewrites its results, so no original
  failed-run file survives; the outputs are transcribed from the executor chat
  in `logs/executor/wf-02a/correction1/development-runs-transcribed.md`, without
  timings, with the later run's exit status unknown and only run A's
  configuration retained.
- **Final checks, once on stable input** (`logs/executor/wf-02a/results/final-matrix.log`,
  every step exit 0): 138 map rows matching every baseline span, 518 of 518
  claims, all links, fragments and inbound references resolved, strict UTF-8
  without CR or BOM, frozen snapshot blobs unchanged, secret patterns matching
  only Git object IDs, `git diff --check` clean. R1 rechecked this file with an
  adapted checker copy under `correction1/`.
- **Limits.** Claim matching is lexical: it shows each phrase is stated, not
  full equivalence. The decision index still files ADR-0001 and ADR-0050 under
  "Accepted decisions"; its categorization is deferred to WF-02B planning.
- **Retained.** All product, test, browser, generation and qualification
  evidence with its original scope; none was run.
- **Next action:** independent Codex review. No commit or push.

## Documentation priority

Documentation rework is the owner's priority; functional work, including test
improvements and P9 investigation, stays stopped. Codex plans and reviews;
Claude implements; reciprocal challenge, role ownership and explicit GO stay
mandatory ([AGENTS.md](../../AGENTS.md)). WF-02A completes only the
context and checkpoint part of WF-02. WF-02B (roadmap reduction) and WF-03
(protocol restructuring) are unselected and incomplete. Selection and decision
constraints: [planner-handoff.md](planner-handoff.md).

## Prior published delivery

TEST-48-01 `d60d718ccf5265f80d26ea06004dc0ad20d359e9` (sole parent
`f7e42c1c7d574a9724ee8755684e4b39179bc53f`, `test: retain bounded approval
replay diagnostics`; both checkpoints and two approval tests under
`tests/DevalCopilot.Api.IntegrationTests/Features/Runs/LocalCommit/`) is
published, per the planner's verified transition baseline. Its committed
"UNSTAGED, UNCOMMITTED and UNPUSHED" wording was pre-publication; do not reopen
or republish it. Its change, evidence, failures and checks remain in both
checkpoints at that commit, `logs/executor/test-48-01/` and
`logs/planner-review/test-48-01/`; retained, not rerun.

DOC-49-01 `f7e42c1`, WF-COL-01 `cab3756`, P9-SEM-01 `b48738f`, P9-EV-01
`465e60c`, WF-01 `4c9102c` and earlier facts remain in this file at `d60d718`
and before; their excluded-artifact reads grant no retrospective authorization
or new read.

## History

[Execution history snapshot](current-work-history-ba1d4c2.md): exact blob
`ebb007040fa73ab0252511180e53e2e64f5285df` of this file at `ba1d4c2`, with
every earlier entry, evidence bullet and open risk as written then. Later
entries are in this file's Git history (through `d60d718`). Links from other
documents to this file's earlier evidence resolve there. It is evidence, not
current status or approval.

## Open risks

- **Account usage.** [ADR-0025](../decisions/0025-stop-new-codex-attempts-at-an-explicit-account-usage-percentage.md)
  and `CodexAccountUsageStopGate` enforce an optional run-scoped Codex stop at
  a provider-reported percentage, at claim and before dispatch; below-threshold
  evidence is not remaining quota. The
  [ADR-0026](../decisions/0026-warn-explicitly-about-a-codex-account-usage-percentage.md)
  warning is advisory and separate, as is the run-scoped
  [token-activity stop](../architecture/agent-collaboration-protocol.md#per-provider-run-token-activity-stop-at-agent-claim).
  No account-usage threshold applies to Claude; Claude account and session
  limits stay open.
- Provider-session resume, live context capacity, manual compaction and other
  provider controls stay open under
  [Increment 4](mvp-delivery-plan.md#increment-4-real-codex-and-claude-code-collaboration).
- [ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md)
  and [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)
  bound claim count and reserved time, not actual wall time, provider usage or
  account availability, and neither has a human override. Gemini stays disabled
  under [ADR-0011](../decisions/0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md).
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
