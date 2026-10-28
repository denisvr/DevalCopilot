# Planner/reviewer handoff

## Current action: COCKPIT-IDENTITY-01

**Review 1 GO for exact-content publication; publication pending.** Correct sibling component identity in the current
cockpit, preserving existing run-owned forms and recorded inspection lifetimes.
This repairs ADR-0031, ADR-0061, ADR-0067, ADR-0068 and ADR-0070's existing
contracts without changing them; no new ADR is needed. Planner-owned inputs
are this handoff and the MVP delivery plan. Preserve both during execution.

### Review 1 acceptance and publication boundary

Planner confirmed main, HEAD, local origin/main and fresh live main at
`a9e06ac634927b097d49f36a7064bd94f0cc46db`, empty index and exactly eight
pending paths (six modified, two new). Both planner inputs matched dispatch
hashes before the deliberate reviewer edits to this handoff and the roadmap.
The substantive implementation is exactly five stable feature-qualified run
keys in the two selected composition owners; no hook, eligibility, transport,
backend or shared harness changed. The complete new tests and journey diff
were reviewed. No blocking implementation finding remains.

Independent frontend **31/31**, four files: both new composition files and the
two unchanged originally failing surface files. Independent strict e2e checking
passed over **54 files**, with exact flags/inventory retained. Evidence:
`logs/planner-review/cockpit-identity-01/review-01/`. The executor's final
frontend **1054/1054**, lint, build/typecheck, red control and headed journey
are accepted at their recorded scopes. The original strict-e2e log is empty
and its summary abbreviates the flags; the independent command provides the
explicitly retained strict check, not a reconstruction of that invocation.

Accept the executor's second-collision challenge: the original-key control
shows duplicated/retained Local commit UI in the composition harness, beyond
a diagnostic alone. Its mocked hooks and synthetic projection changes prove
the composition property, not a reachable Abandoned-to-Running transition or
a reproduced real-user incident. App's outer run key remains unchanged.

The headed journey need not be repeated on original keys: component red
controls establish sensitivity at the changed boundary; browser sensitivity
is not independently demonstrated. Explicit account/model refresh invokes
existing capability observations. Clarify the invocation acceptance to mean
no additional Agent-stage invocation, command, claim or named durable change;
the existing probe/account observations are allowed by those requested reads.
This grants no new provider operation or mutation authority. The journey's
zero duplicate-key result does not explain every past console occurrence.

Reviewer qualified current-work's binary claim: successful build and later
matching outputs support the fresh journey, but changed hashes do not prove
earlier source mismatch or its cause. The before-build equality is reported,
not independently retained in a before-build hash record. CS2012 names
possible lock owners, not established causation. Preserve the failed build,
aborted control, test-helper failure, all red outputs, journey 404/409/SignalR
diagnostics and every earlier limit. No backend/whole-suite/provider/recovery
result or historical process authentication is claimed.

Publish only the eight paths, expected tree and sole parent in the external
review approval and complete GO. These reviewer document edits are included
in that identity. No further source/document edit, build, generation, cleanup
or closure commit is authorized during publication. Any identity mismatch,
material change, failed push or divergence returns to Codex; no recomputation,
broader staging, force-push or history reconciliation. Planner independently
verifies publication afterwards and then checks current context usage; no
current usage percentage has been observed in this review.

The original selection and acceptance matrix below remain historical context;
the review dispositions above resolve their reporting and scope qualifications.

### Verified baseline and selection evidence

On 2026-10-28, main, HEAD, local origin/main and a fresh permitted live main
matched `a9e06ac634927b097d49f36a7064bd94f0cc46db`; index, working tree and
untracked inventory were clean before planning. The restricted live read
failed with `getaddrinfo() thread failed to start`; the permitted read succeeded.
No reconciliation, repeat publication or closure commit is needed. Dispatch
preflight adds only the two unstaged planner documents above, with empty index
and no untracked files; verify their selection hashes before execution.

Evidence: `logs/planner-review/cockpit-identity-01/`. Fresh unchanged-source
LocalExecutionAdmissionSurfaces and LocalExecutionReleaseInspectionSurfaces:
10 failed / 10 passed, 12.51 s, precisely the ten current-run cases seen in the
previous slice; historical counterparts pass. UsageEvidenceRail assigns the
same sibling `key={runId}` to AgentAttemptHistoryPanel,
LocalExecutionInspectionPanel and PlanningSequenceInspectionPanel. A serial
control changed only these three keys to stable component-qualified run keys:
the unchanged 20 tests passed, 4.34 s, with no duplicate-key diagnostic. Source
was restored byte-identical: SHA-256
`862f764c9f3902fc1897bca431357414e84859cb16c9aa6d8a933b14a8b4e31e`.
Raw logs: baseline-focused.log, distinct-keys-focused.log; control-identity.txt
records candidate/original/restored hashes. First invocation never started
Vitest because its output directory resolved from the wrong working directory;
that tool failure remains in the planner transcript, not a test result or a
reconstructed original log. One documentation-edit attempt stopped before any
write on an exact-text guard because its expected newline did not match.

RunCockpitView also gives sibling LocalCommitPanel and AbandonRunPanel the
same run key. This is source-established; no behavioral failure or historical
browser cause is claimed for this second collision. React requires keys unique
among siblings and ties state to identity ([keys](https://react.dev/learn/rendering-lists),
[state](https://react.dev/learn/preserving-and-resetting-state)).

### Selected outcome and boundary

Repair both identified groups together in UsageEvidenceRail.tsx and
RunCockpitView.tsx with stable feature-qualified keys retaining the existing
run and deliberate version identities. Inspect sibling groups in those two
owners; change only demonstrated collisions. No random/time keys, removing run
reset, remount on ordinary refresh, console suppression, panel movement/reorder,
condition/eligibility change, hook replacement or repository-wide key cleanup.
This is one composition defect; splitting the inspectors from the command
panels leaves the same identified violation in this cockpit. A cross-stage
summary/replay adds independent proof contracts; scheduling/recovery and
provider/publication work cross separate authority. None is selected.

Allowed implementation: the two sources, focused feature-owned component and
composition tests (new CockpitIdentity-named test if useful), minimal existing
feature-owned fixture setup, a small extension of
`e2e/local-execution-queue.journey.ts`, and current-work.md. The two planner
inputs remain unchanged in the final diff. No shared test setup/harness,
global console policy, hook, decoder, transport, generated client, backend,
schema, dependency, native/provider experiment or authority change. If a
sufficient correction requires any excluded source/contract, stop and present
evidence and a bounded alternative before dependent work.

### Acceptance and proportional validation

1. Run the two original failing surface files unchanged first; all 20 cases
   must pass after correction. Preserve their original expectations and real
   transport coverage. The planner red/control evidence is retained, not a
   final executor result.
2. Add composition regressions for all three rail inspectors and both
   local-commit/abandonment panels. Deferred and resolved unrelated account/model
   observations and stable-run parent refresh must preserve open/selected
   inspection and eligible local drafts, with exactly one expected instance,
   no duplicate-key diagnostic and no added inspection read or command. Include
   conditional sibling presence changes where the existing contracts permit.
   Use a local console observer that preserves other diagnostics.
3. Preserve run replacement, A-B-A, collapse/reopen and unmount boundaries:
   relevant visit/draft ends, successor opens cold and stale callbacks/results
   cannot act on it. Reuse sufficient existing tests; add only missing
   composition cases. No command submission merely to test component identity.
4. Final stable-source frontend filters, once:
   `UsageEvidenceRail RunCockpitView RunCockpitSelectionOwnership RunEvidenceRefreshComposition LocalExecutionAdmissionSurfaces LocalExecutionReleaseInspectionSurfaces PlanningSequenceInspectionSurfaces AgentAttemptHistoryPanel LocalExecutionInspectionPanel PlanningSequenceInspectionPanel LocalCommitPanel AbandonRunPanel LocalCommitInspectionSurfaces layoutBoundary App.test.tsx App.queueInspection CockpitIdentity`.
   Include every new test regardless of filename. Run lint, product build
   (includes tsc), and the existing separate strict e2e TypeScript check over
   current e2e/config inventory. No whole Vitest/backend suite or client
   regeneration by default; retained evidence keeps unchanged-input scope.
5. Extend the existing local-execution-queue journey at its settled current-run
   inspection: an ordinary nonmutating account/model refresh or parent update
   preserves selected ledger/step content without extra inspection GETs,
   commands or invocations. Reuse existing request/durable oracles. Observe
   duplicate-key console diagnostics from before navigation and require zero
   throughout this journey; preserve other diagnostics and queue/history
   assertions. Run only this journey once after stable binaries, announced and
   automatically headed; trace/video/screenshots off, no Inspector/debug/UI,
   PWDEBUG or manual Resume. Verify host provenance; incrementally build the
   journey host serially if needed. Normal authentication and deterministic
   executables stay. No redundant journey or general recovery/provider claim.
6. Broaden only for changed consumers/dependencies, failures or unresolved
   impact; source expansion requires planner resolution. Retain one concise
   command/result/elapsed/source log and original failures. Check changed-doc
   links/inbound fragments, full diff and sensitive data. No mutation campaign
   or new validation framework. Update current-work as implementation facts;
   challenge material flaws before dependent work. Return unstaged, uncommitted
   and unpushed for independent GO/NO-GO; no commit/push authority exists.

### Delivered baseline and preserved qualifications

LOCAL-COMMIT-INSPECT-01 is independently verified published at
`a9e06ac634927b097d49f36a7064bd94f0cc46db`, tree
`b38fe71009923bffff533b0382d0ec1a67bb4bba`, sole parent
`e7bd118b65dba6c9fde2a3f07e1fda773bc25b08`, exactly 70 paths (53 added,
17 modified), approved path-list SHA-256
`ef71523254a774eac182d4d57d630713e371cd3867f155e733a5bd1fea19c49f`.
Records: `logs/planner-review/local-commit-inspect-01/publication-verification.json`,
`continuity-after-publication.json`, `review-02/approval.json` and
`review-02/approved-paths.txt`. Full prior selection/recovery/review dispositions
remain in this file at `a9e06ac` and current-work. Preparation-phase wording
is not pending delivery and authorizes no repeat review, publication or closure.

LCMI-R1 is resolved. Independent frontend 94/94 and backend 73/73, executor
corrected frontend 1078, Application 269/API 139/Architecture 225, format,
strict e2e 54-file and client reproducibility retain actual scopes. Client
SHA-256: `c601a846c31d5d3440abfbc924ca111814498627cd3db08cc73c15afe62d880d`.
The recovery headed journey predates the decoder correction; no corrected
frontend browser or whole Application/API/Infrastructure/Domain/Vitest run is
claimed. Its fingerprint covers named fields/counts, not all-row/file byte
identity; page reload is not host restart.

The new three-key control establishes sensitivity for ten current focused
failures only. It does not authenticate old runs or explain every past console
occurrence. Preserve first-journey 404 and unproved stale-host cause,
unauthenticated authoring session, supplementary comparison/exit-255 report
with unestablished causes, duplicate-key/SignalR observations, all development
and control failures, reviewer ENOENT suite-load failure and interrupted
restricted Application attempt. Current on-disk DLL equality does not
authenticate past loaded images. Earlier inspector/release/provider limits
remain; no missing evidence is rebuilt. ADR-0069 verification history has no
RunId; time or checkpoint coincidence establishes no run association.

The outgoing chat's observed 68.6924% required this new planner chat; it is not
this chat's usage. Current usage is unavailable; selection is no new push
verification or continuity event. No competing next slice was dispatched.

## Retained review and evidence limits

Do not rewrite the published GLOBAL-LIMIT-01 record. Its final independent
frontend evidence is 958/958 plus lint, build/typecheck and strict e2e checking;
two reviewer compiler-invocation failures precede the corrected pass. Full
Application 7061/API 3050 retain unchanged backend-source scope. Persistence 468
retains its first 466/468 failure and accepted table-list repairs. Whole Vitest
6835 is pre-correction evidence, not a fresh whole-suite run. Both corrected
journeys passed serially; the volunteered 1602-hook run has no separate log.

The original Application red run is 21 failures/55 passes: 18 behavioral and
three invalid rowid-insertion setup failures. The original third control's
unused-variable attempt did not compile; its corrected-hook control compiled
and killed its named regression. Preserve their distinct scopes and all
warnings/skips, bounded control sensitivity and source-versus-binary limits.

Detailed prior review and constraints: this file at `7c78c84`,
[current-work](current-work.md#active-evidence-limits),
[WF-04-era limits](planner-handoff-history-e614c79.md#retained-review-and-evidence-limits),
[earlier limits](planner-handoff-history-0a57216.md#retained-review-and-evidence-limits),
and ignored per-slice planner/executor logs. WEX's initial API mutant-provenance
uncertainty and overwritten correction failure log remain. Earlier lost,
derivative, partly retained and chat-only output, invalid/no-op controls,
measurement limits and unexplained failures stay qualified; later passes
reconstruct, authenticate or explain none of them. Frozen snapshots remain
untouched; no historical result is reconstructed by this selection.

## Standing decision constraints

- ADR-0050 stays Proposed; P9 unproven; AI Memory deferred; no mechanism selected. Frozen patches
  A/B/C, C's assertions, write-ahead and sticky-flag alternatives stay
  unapplied/unaccepted. Owner GCM/ancestry-profile and residual name-to-ID risk
  acceptance was design-only; human rearm/resolution is absent.
- One-profile/two-child authority is spent. No native build, launch, profile,
  security, helper, credential or provider operation is renewed. Retained
  profiles, fixtures, DACLs, labels, grants and ignored evidence stay untouched;
  no cleanup, sweep or permission reset. Hashes do not seal live metadata.
- Qualification: `Blocked`/`TargetsNotReal`, both allowances unused; Git/GitHub
  separate/unstarted; GCM unqualified. Repository access grants no branch push.
- Preserve defects, deviations, lost/pre-redaction output and uncertainty in the
  [feasibility note](../architecture/github-publication-source-write-denial-feasibility.md),
  [capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md),
  [native semantics note](../architecture/github-publication-native-io-semantics.md)
  and [execution evidence](../architecture/github-branch-publication-execution-evidence.md).
  Earlier excluded-artifact reads grant no new reads. Original/failed evidence
  stays frozen, including the 69-file 11/57 correction.
- Concurrent-replay 409, target-test 500, disposal `NullReferenceException`,
  unnamed Api 2067/2068 and interrupted reviewer synthetic run stay unexplained.
  Later passes prove neither flakiness nor irrelevance. TEST-48-01 cannot cover
  hung submissions; retention beside disposal failure is unproven.
- Increments 4/5/6 incomplete. Account/context, raw Git read, non-Windows limits:
  [current-work](current-work.md#open-risks). Production code and necessary tests
  are selected only for COCKPIT-IDENTITY-01 under the existing cockpit
  component-lifetime contracts; no new capability or authority is selected. ADR-0065/0066 retain their delivered release authority.
  PLAN-COORD-01, AGENT-SUP-01, WORKSPACE-EXCL-01, GLOBAL-ADMIT-01,
  GLOBAL-LIMIT-01, LOCAL-COORD-01, LOCAL-INSPECT-01, LOCAL-QUEUE-01,
  LOCAL-QUEUE-INSPECT-01, LOCAL-QUEUE-OVERVIEW-01,
  LOCAL-QUARANTINE-RELEASE-01, LOCAL-RELEASE-INSPECT-01, PLAN-INSPECT-01, PROJECT-VER-INSPECT-01 and LOCAL-COMMIT-INSPECT-01 are independently verified
  published. Inspection grants no mutation, scheduling,
  executor lease, operation retry/resume, quarantine release, recovery, approval,
  commit or publication authority. ADR-0065/0066 alone own the separately owner-authorized
  terminal-operation release; it renews none of the frozen experiments/grants.
  No dependency, experiment, capture-limit or standards edit is selected.
