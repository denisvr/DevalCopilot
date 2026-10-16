# Current work and cross-chat handoff

Execution facts and delivery evidence; not a transcript or approval. It
replaces its current state on each delivery. Procedure:
[development workflow](development-workflow.md). Git and code prevail; compare
branch, `HEAD`, `origin/main` and staged/unstaged/untracked state first.

## Active slice: TEST-48-01 bounded concurrent-replay failure diagnostics

- **Status: UNSTAGED, UNCOMMITTED and UNPUSHED; returned for Codex review.**
  Preflight matched the dispatch: `main`; `HEAD`, local `origin/main` and
  live main `f7e42c1c7d574a9724ee8755684e4b39179bc53f`; 0/0; empty index;
  exactly the planner file unstaged (11228 bytes, `331c3345...de88`,
  planner-owned, preserved byte-identically); no untracked paths.
- **Paths.** Modified: `GitHubBranchPublicationApprovalDurabilityTests.cs`
  and this file. New: the sibling `GitHubBranchPublicationApprovalDiagnosticsTests.cs`
  (both under `tests/DevalCopilot.Api.IntegrationTests/Features/Runs/LocalCommit/`).
  Ignored: evidence under `logs/executor/test-48-01/`.
- **Change.** The concurrent identical-request fact keeps four hosts, sixteen
  identical requests submitted concurrently and every assertion. It now
  awaits them through a feature-owned helper that writes one bounded record
  per returned response to the xUnit test output in a `finally` block, so
  the records exist before any assertion runs and a faulted submission
  still propagates its own exception. The same text is the message of the
  all-200 status assertion, now `Assert.True` with unchanged pass/fail
  semantics (it replaces `Assert.All`, whose item formatting is not
  bounded). A record holds only the request ordinal (submission index), its
  host ordinal, the numeric status and the exact ADR-0048 code at
  `errors[0].code` (one of the six fixed codes) or a closed fallback:
  `empty`, `oversized` (above 8 KiB of UTF-8), `malformed` (invalid JSON,
  depth above 8, wrong kind or type), `missing`, `ambiguous` (duplicate
  members or several errors) or `unknown`. Ordinals identify submissions,
  not execution order. A success body has no code and records
  `fallback:missing`. No body, value, path, header or exception text is
  copied; records are at most 16 and each fits 256 UTF-8 bytes.
- **Synthetic diagnostic evidence (36 cases, no host or lineage).**
  Fabricated `unavailable` and `conflict` responses keep their code, request
  and host ordinals in the thrown status-assertion message. A faulted
  submission keeps its original exception while the returned responses are
  written. Malformed, wrong-type, duplicate, ambiguous, unknown, case- or
  space-altered, escaped, oversized and too-deep bodies get the expected
  fallback. Every result omits an inert fictitious sentinel and raw JSON.
  This is synthetic evidence only; it neither reproduces nor attributes the
  historical 409.
- **Development failures (retained).** Build 1 failed with CS9007 (a raw
  interpolated literal). The first synthetic run failed 1 of 36: an
  escaped lone surrogate compares unequal without throwing (`unknown`), so
  the unsupported `InvalidOperationException` catch was removed and only
  `JsonException` is caught.
- **Final checks, once on stable input.** The incremental serial build with
  client generation disabled exited 0. The dispatched filter exited 0:
  37 passed (36 synthetic and the real fact, which passed with sixteen
  200 records retained in the TRX). Its `Durability...Diagnostic_` clause
  matches nothing because the synthetic checks live in the sibling class.
  The source hashes were equal before and after. Formatting of both C#
  files passed, as did the reused documentation checker (4173 bytes,
  `da16f21e...ff4b`) and `git diff --check`. The sentinel occurs in the TRX
  only in three theory display names, never in output. Commands, results
  and times are in the executor report and `logs/executor/test-48-01/`.
- **Retained.** Product, deterministic snapshot, architecture, persistence,
  frontend, generated-client, browser and qualification evidence, and
  every other approval test, each with its original scope; none was rerun.
- **Not run.** Other suites, stress or repeat runs, mutation, generation,
  provider, native, qualification, security-state, cleanup or evidence
  sweep.
- **Next authorized action:** Codex review of this diff. No commit or push.

## Prior published delivery

DOC-49-01 `f7e42c1c7d574a9724ee8755684e4b39179bc53f` (sole parent
`cab3756c24e58524f25ef3ba607236d63fa9426e`, subject `docs: align ADR-0049
references with accepted status`) published the accepted-status labels of
ADR-0049 in the engineering context and MVP plan with both checkpoints (four
paths). The planner independently verified the publication; its retained
verification is `logs/planner-review/doc-49-01/publication-verification.json`
over the frozen manifest SHA-256
`f2833d7fc2d6374867697bfae7ee9a2e920409ad2479cb01c5e3ebd7db396a23`
(this slice rechecked that hash, the commit, its parent and its paths only).
R1 is resolved: the original checker exit 1 and its two pre-existing
engineering-context false positives remain recorded with the focused
passing correction. The planner identity in its Paths bullet describes the
initial executor return; the frozen manifest defines the publication. Its
committed "UNSTAGED, UNCOMMITTED and UNPUSHED" wording was the
pre-publication state. Its checks are retained, not rerun.

WF-COL-01 `cab3756c24e58524f25ef3ba607236d63fa9426e` (sole parent
`b48738f0f160336df945ad3a6ad1802a6876458b`) published the reciprocal
critical-collaboration text in `AGENTS.md` and `CLAUDE.md` with both
checkpoints (four paths, mode 100644); the planner independently verified
the commit, parent, paths, blobs and hashes against its frozen manifest and
local/live ref equality, and the retained verification establishes normal
fast-forward publication. Its committed "UNSTAGED, UNCOMMITTED and UNPUSHED"
wording was the pre-publication state. ADR-0021 delivery of the new root
text was not exercised; existing sealed manifests replay unchanged. Its
non-blocking notes stand: `CLAUDE.md` paraphrases the shared section
heading, neither root names a discussion channel, and executor handling of
review findings stays in the workflow's findings rules. Its checks are
retained, not rerun.

P9-SEM-01 `b48738f0f160336df945ad3a6ad1802a6876458b` (sole parent
`465e60cfc31d02961fc3970d44e65bb4cfe5ee29`) published the
[semantics note](../architecture/github-publication-native-io-semantics.md),
its index link and checkpoints; the planner independently verified the four
paths, blobs, bytes, fast-forward push and live ref and accepted R1-R3 and the
index entry. Its committed "UNSTAGED, UNCOMMITTED and UNPUSHED" wording was
the pre-publication state. Its `wc -lc` deviation read the full contents of
three excluded audit reports for counts only; this grants no retrospective
authorization or new read. The failed membership `grep` and the recorded
pre-final wording mismatch are preserved (see
[planner-handoff.md](planner-handoff.md)). Its checks are retained, not rerun.

P9-EV-01 `465e60c` (sole parent `4c9102c`) published the
[capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md);
its correction hashing read the excluded `ctl`/`app` fixtures (69 equal saved
hashes; the reported 12/56 split was wrong), with no retrospective
authorization or further sweep. WF-01 `4c9102c` and earlier facts remain at
their published baselines.

## History

[Execution history snapshot](current-work-history-ba1d4c2.md): exact blob
`ebb007040fa73ab0252511180e53e2e64f5285df` of this file at `ba1d4c2`, with
every earlier entry, evidence bullet and open risk as written then. Links
from other documents to this file's earlier evidence resolve there. It is
evidence, not current status or approval.

## Open risks

- **Account usage.** The old "provider account-usage limits remain
  unenforced" summary was overbroad. [ADR-0025](../decisions/0025-stop-new-codex-attempts-at-an-explicit-account-usage-percentage.md)
  and `CodexAccountUsageStopGate` enforce an optional, run-scoped Codex stop
  at a provider-reported account-usage percentage, at claim and before
  dispatch; below-threshold evidence is not remaining quota.
  [ADR-0026](../decisions/0026-warn-explicitly-about-a-codex-account-usage-percentage.md)'s
  warning is advisory and separate, as is the run-scoped token-activity stop.
  No account-usage threshold applies to Claude; Claude account and session
  limits stay open.
- Provider-session resume, live context capacity and manual compaction, and
  remaining provider controls stay open under Increment 4.
- [ADR-0012](../decisions/0012-add-a-durable-run-wide-agent-claim-budget.md)
  and [ADR-0013](../decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md)
  bound claim count and reserved time, not actual wall time, provider usage or
  account availability, and neither has a human override. Gemini remains disabled under
  [ADR-0011](../decisions/0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md).
- Raw linked artifact inspection beyond the existing bounded views is open.
- Raw checkpoint Git observation can still read named linked content; ADR-0024
  and ADR-0027 constrain delivered tracked text only, and the physical proof
  is Windows-specific.
- The concurrent-replay 409, target-test 500, disposal
  `NullReferenceException` and unnamed Api 2067/2068 remain separately
  unexplained; later passes establish neither flakiness nor irrelevance.
  TEST-48-01 only bounds what a future failure of the concurrent
  identical-request fact retains (ordinals, status, fixed code). The one
  passing run attributes nothing. A hung submission yields no record;
  records are written before host disposal, but their retention beside a
  disposal fault was not exercised.
- **P9 evidence limits** ([feasibility note](../architecture/github-publication-source-write-denial-feasibility.md)
  sections 11 to 14): the v1 stop-gate defect and protocol deviation; the
  inconclusive A2 no-child result; native-error and capture limitations; a
  malformed original receipt versus its derived summary (missing captures are
  not reconstructed); no pre-redaction originals, retained redacted
  derivatives and object files embedding local paths; an annotated host
  transcript versus valid JSONL captures; an unchecked child-exit measurement;
  limited profile-state evidence. File-hash manifests do not seal live ACL
  metadata. The [capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md)
  found the retained captures consistent; it cannot show that no output was
  lost or prove the child exit codes. The
  [semantics note](../architecture/github-publication-native-io-semantics.md)
  leaves open the exit value after a failed query, any failure cause for the
  child handle and buffered-byte availability for the final drain (K1-K5).
- Decision constraints (ADR-0050, P9, qualification, experimental authority,
  retained artifacts) are in [planner-handoff.md](planner-handoff.md).
