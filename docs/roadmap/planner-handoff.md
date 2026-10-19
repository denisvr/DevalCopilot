# Planner/reviewer handoff

[AGENTS.md](../../AGENTS.md) and the [workflow](development-workflow.md) apply.
Codex remains planner/reviewer; Claude remains executor. Codex performed planning
and judgment without delegation. AGENT-SUP-01 correction 1 is reviewed GO below.
Only the exact content identified in the accompanying publication prompt may be
committed and pushed from its current Claude executor chat. No other slice is
selected or dispatched.

## Verified baseline and prior publication

Startup on 2026-10-19 independently reconciled main; HEAD, local origin/main
and a fresh live refs/heads/main all equal
`a78e572c84e2e7a9a86ac90c1e1b286e6001973e`, tree
`2c62b12ffa69be55017dbe2b92a8fad224840420`, sole parent
`1f22a3738976f6b0df0ec59321016be8f5df6115`, divergence 0/0. The real index,
tracked working tree and nonignored untracked inventory were empty. The first
sandbox remote query failed with getaddrinfo() thread failed to start; a
permitted fresh query succeeded. No cause is inferred from that later success.

PLAN-COORD-01 is published and independently verified. Its retained
`logs/planner-review/plan-coord-01/publication-verification.json` and `.md`
record the exact 176 GO-approved paths (66 M, 110 A), parent and tree. Startup
inspected those records and independently confirmed the live baseline; it did
not rerun the historical review or product tests. Committed review-phase
checkpoint wording reopens nothing and requires no closure commit. PCO-R1
through R5 are resolved; PCO-C1 remains accepted/nonblocking. The review,
validation acceptance adjustment, original/lost/reported/retained provenance
and all prior restrictions remain as recorded in this file and current-work.md
at `a78e572` and ignored per-slice evidence. No missing output is authenticated
or unexplained failure attributed by this selection.

Current chat context percentage is unavailable. The prior publication record's
27.16% session-metadata ratio and older 61.0% report are historical, not current
usage. The owner explicitly continued this planner chat. No new publication
event or context-threshold transition is claimed.

Selection changes only this handoff, the decision index and new ADR-0056.
Executor preflight: the same main/HEAD/local/live baseline, empty index, these
two tracked documentation modifications and one untracked ADR, no other
nonignored paths. Preserve these planner documents as part of the slice's
review. Codex changed no source, test, dependency, commit or push.

## Remaining MVP critical path against code

Increments 4/5/6 remain incomplete. ADR-0050 stays Proposed, P9 unproven and AI
Memory deferred. No completion percentage or delivery date is defensible.

| Front | Actual baseline and remaining dependency |
|---|---|
| Planning | PlanningSequenceCoordinator and its three claim/result paths join the complete bounded read-only lineage under ADR-0055. Acceptance or Human escalation ends that grant; it selects no mutating successor. |
| Local execution | ImplementationSupervisor, VerificationExecutionSupervisor, ImplementationReviewSupervisor, ReviewCorrectionSupervisor and VerificationDiagnosisSupervisor execute existing explicit work. LocalCommitSupervisor has its separate exact-checkpoint authority/recovery. They do not form the full automatic delivery loop. |
| Current execution truth | Claims set Attempt.Running; MarkAgentAttemptDispatched records a single-use marker. AgentProcessExecutionEvidence arrives after the adapter returns. Seven Agent supervisors await their own work without a common durable current observer/heartbeat/phase record. PlanningSequencePolicy deadlines cover its own sequence only. RunHeader distinguishes coordinated lifecycle, but its ordinary Running treatment/ticking duration can still imply activity. ProviderRuntimePreflight reports version evidence and unknown controls, not current execution; the target runtime strip is not a delivered liveness surface. |
| Increment 5 autonomy | Mutation grants, verification-recipe/execution-set identity, correction routing, repository/global scheduling, finite execution leases, process-aware controls and ambiguous-write recovery remain separate unresolved contracts. Observation cannot authorize them. |
| Increment 4 gaps | Claude account usage, eligible session resume, live context capacity, manual compaction and other permission profiles need contracts/evidence. Deterministic doubles do not qualify real providers; experiments are unauthorized. Raw Git observation and non-Windows containment limits remain. |
| Increment 6 | GitHub observations/binding/consent establish neither branch permission nor push readiness. Source-write denial/P9 and separate Git/GitHub/GCM qualification block the proposed publication path. CI monitoring, diagnosis/correction, rerun and reconciliation remain. |
| Self-hosting | The stable packaged end-to-end demonstration depends on those capabilities and qualification; local tests/read-only coordination cannot establish it. |

## Selected slice: AGENT-SUP-01 current Agent supervision observations

Implement accepted [ADR-0056](../decisions/0056-observe-agent-supervision-without-granting-execution-authority.md).
The cockpit shows the phase the host last reported for its current Agent
attempt, the observer's last report and absolute deadline. A stale or hung
observation becomes attention independently of the blocked role worker. It
never represents provider progress or execution authority.

**Sizing judgment.** All seven Agent contracts, repairs and manual/coordinated
paths share Attempt identity and the same observation semantics. Splitting by
provider/role or shipping the backend without truthful cockpit composition
leaves the outcome incomplete. Verification, standalone Process and local commit
cross different identity/recovery lifecycles. Automatic implementation,
verification, correction and commit require broader mutation authority absent
here; provider work has experiment restrictions. These are concrete authority
and validation boundaries, not a file-count limit. Observation is a prerequisite
for safely judging future coordination, not a substitute for the missing loop.

**Resolved architecture.** One non-authoritative AgentSupervision receipt per
observed Agent attempt; immutable association/original host/observer, guarded
revision and short independent writes. Forward-only Preparing and RecordingResult
phases each get two minutes; AwaitingAdapterResult gets the reserved invocation
timeout plus two minutes. Five-second heartbeats, stale at thirty seconds,
independent bounded sweep and immediate effective-attention reads. A heartbeat
cannot extend a phase or clear first attention. Actual terminal Attempt facts
win current display while attention history remains. No process liveness,
execution lease, retry, cancel, takeover, ownership release or new eligibility.
Observation faults cannot suppress the original safety gates/result recorder.
The complete resolved contract is ADR-0056; challenge it with evidence rather
than silently redesigning it.

**Scope.** Feature-owned Domain receipt/policy; DbContext/EF configuration and
additive migration/constraints; internal commands/query/events; one small host
observation-lifetime collaborator across the seven existing Agent supervisors;
independent sweep and startup closure after existing attempt reconciliation.
One protected bodyless/queryless GET /api/runs/{runId}/agent-supervision with
API-owned DTOs, NSwag output, operation-scoped raw-wire validation, owned
hook/panel and current header/supervision/duration composition. Preserve the
version-only ProviderRuntimePreflight and historical collaboration cards; add
no target model/context/permission runtime controls or active-card machinery. Update
engineering-context, system-overview, data-and-recovery, cockpit specification,
MVP plan and executor current-work only for this exact partial capability.
Historical readers, invocation/process contracts, provider permissions,
artifacts/source admission, claim/dispatch/result rules and ADR-0055 authority
remain unchanged. No accepted historical ADR or frozen snapshot is rewritten.

## Proportional validation matrix

New .NET class/file names contain AgentSupervision for focused filters. Build
once per stable inputs, use --no-build --no-restore only against current binaries.
Retain original outputs, commands, source/diff identity, results and elapsed
times in concise ignored logs/executor/agent-sup-01 evidence. Separate fresh,
retained and not-run evidence; keep failures without reconstructing losses.

- Domain/Application: AgentSupervision filter, plus full
  GetRunEventHistoryQueryHandlerTests when constants change. Identity/replay,
  foreign/stale writers, forward phases, exact deadlines/staleness, overflow/
  clock refusal, hung phase with fresh heartbeat, effective/recorded attention,
  timely/late terminal outcomes, uncertainty/rollback. Full projects only if
  shared admission/dispatch/lifecycle/result semantics change, also a scope stop.
- Infrastructure: AgentSupervision OR Migration name filters; real file-backed
  SQLite empty/upgrade/legacy/reopen/constraints, independent-connection races,
  revision/identity protection and receipt/event atomicity. No fixture caching.
- API: new AgentSupervision plus existing AgentAttemptSupervisor,
  ClaudeCriticalReviewSupervisor, ChallengeResolutionSupervisor,
  SecondChallengeRoundSupervisor, ImplementationSupervisor,
  ImplementationReviewSupervisor, ReviewCorrectionSupervisor,
  VerificationDiagnosis, PlanningSequence and LaunchSessionAuthentication
  substrings; real MVC authorization inventory and affected startup/side-effect-
  free tests. Retain the expanded actual FullyQualifiedName filter. Hosted new
  cases exercise every production supervisor, repairs/coordinated paths, blocked
  preparation/adapter/recorder, observation faults/disposal/restart, unchanged
  provider cardinality/budgets/results and exclusion. Broaden to full API when
  shared host/pipeline effects exceed bounded registration/startup wiring or
  affected consumers cannot be enumerated; stop for authority changes.
- Frontend: focused decoder/hook/panel, RunHeader and cockpit/
  selection cases; final whole Vitest once, lint and production build including
  tsc -b because shared composition changes. Closed wire UTC/Int64/tuple rules,
  freshness, terminal/attention precedence, A-B-A/remount/pending/stale callbacks
  and no observation-based action grant. Reproducible NSwag after API build.
- Browser: extend planning-sequence.journey.ts through the authenticated host
  and existing deterministic executables; run it and collaboration.journey.ts
  serially on stable binaries. Prove one real persisted observation for the
  exact linked attempt and truthful terminal display; no need to catch every
  transient phase in a fast fixture. Hung/freshness permutations stay at hosted/
  UI boundaries. Strict e2e TypeScript uses explicitly expanded files. Shared
  harness/fixture invocation contract changes require planner reassessment.
- Full architecture; affected format/build/analyzers, existing dependency/
  security checks without dependency changes, links/inbound fragments, sensitive
  data and full diff. Three serial compiling controls: heartbeat masks deadline,
  foreign observer update, stale UI response; hash-verify source restoration.
  No real provider, P9, packaged-shell or release qualification is claimed.

Broaden only for changed dependencies, failures or unresolved impact under the
validation policy. Keep unaffected provider/process/Git/artifact/history and
other-journey evidence at its actual scope; rerun only invalidated checks after
correction. No product tests ran for this documentation-only planner selection.

Planner selection checks passed: 73 local Markdown paths/fragments across the
three selected documents, affected current inbound handoff fragments,
git diff --check and a selected-document sensitive-pattern scan (lexical only).
Historical missing-fragment statements remain historical. Final Git recheck
confirmed the same HEAD/local/live main, 0/0 and empty index, with only the
three expected planner paths changed. The unavailable WindowsApps Python
launcher rejected an initial handoff-write attempt before writing; the native
PowerShell write then succeeded. This is tooling history, not a product failure
or validation pass.

## AGENT-SUP-01 review 2: GO after correction 1

Codex independently reviewed the correction and decides **GO for the exact
reviewed substantive diff**. Review 1 was NO-GO; ASUP-R1 through R5 are now
resolved. The detailed first review and pre-update handoff remain under
`logs/planner-review/agent-sup-01/`, alongside the second review. This decision
accepts no new execution, provider, dependency or product-publication authority.
The only authorized repository action is publication of this reviewed slice
under the content identity in the accompanying executor prompt.

Review preflight independently confirmed main, HEAD/local origin/main/fresh
live main `a78e572c84e2e7a9a86ac90c1e1b286e6001973e`, divergence 0/0 and an empty
index. There are **141 distinct paths: 43 M and 98 new**, equivalent to 95
directory-grouped status entries. Eight new correction files account for the
increase from review 1's 133 files; no unrelated work was identified. The
approved path inventory is `logs/planner-review/agent-sup-01/review-02/reviewed-paths.txt`.
The expected parent and tree are external to the content they identify and
will be supplied in the publication prompt; never insert them into this file.

- **ASUP-R1 [P2], resolved.** Task.Run isolates synchronous provider work before
  the caller's two-second wait. Each pending operation owns its scope until
  settled. The observer serializes phase/closure writes and drops heartbeat
  ticks while a write is pending; the sweep similarly retains pending work
  without replacing it. The real SQLite registration/phase/closure/shutdown
  contention cases pass. An additional independent noncooperative async
  notifier probe also passes: bounded phase/disposal waits, one active write,
  no queued heartbeats across a real five-second tick, ordered eventual
  phases/closure, exactly one closure and an unchanged Running attempt. This
  closes the async evidence requirement without a production-code correction
  or a new executor dispatch.
- **ASUP-R2 [P2], resolved.** The operation-owned AbortSignal reaches the actual
  authenticated fetch through the existing generated operation. Timeout,
  selection and unmount abort it; the hook waits for actual transport
  settlement before starting another request of that lifetime. The real
  hook/wire/generated-client cancellation-aware transport tests pass, including
  A-B-A and a transport that ignores abort. Existing stale-owner fences remain.
- **ASUP-R3 [P2], resolved.** Current chronology is validated before historical
  terminal assessment; future attention, closure and completion are refused,
  never clamped. The three reviewer cases now return Unavailable. The decoder
  mirrors successful-answer chronology, while timely results read late, exact
  boundaries, terminal precedence and attention history remain covered.
- **ASUP-R4 [P2], resolved.** Registration, each phase entry and every actual
  closure now append bounded fixed-schema events atomically with the receipt.
  Heartbeats stay silent. Exact ordering, idempotence and SQLite-trigger
  rollback tests pass; all seven hosted compositions and the planning journey
  assert the material events instead of zero. The original executor choice
  remains rejected and is superseded by this correction.
- **ASUP-R5 [P3], resolved.** The four supporting types were split into matching
  feature-owned files; AgentSupervisionHostedSupport retains only its own type.
  The locus is the API integration test project. Compilation and the retained
  hosted behavior pass, with no fixture cache or isolation redesign.

**Independent validation.** Rebuilt Application feature plus complete event
history class: 201/201 (42 s test duration). Rebuilt API observation feature,
all three naming spellings and all seven production supervisors: 77/77 (32 s),
including real SQLite contention/startup/endpoint cases. Eight frontend
wire/decoder/hook/panel/header/event suites: 292/292 (4.24 s). The extra async
probe: 1/1 (10 s); its source was copied with matching SHA-256
`22029c8e2264edf321d54470fe4740a619a3d24a5b44fb9fa76a3ab51c051bc5`, removed
from the test project and the project rebuilt with zero warnings/errors.
No reviewer probe remains in returned sources or binaries. Codex implemented
no product correction. Exact commands/results and probe source are retained
under `logs/planner-review/agent-sup-01/review-02/`.

**Inspected executor evidence.** Correction final logs support Domain 40/40,
Application 201/201, API matrix 315/315, Infrastructure 290/290, Architecture
179/179, frontend focused 518/518 and full 5950/5950, lint/build (including
typecheck), strict e2e tsc and both serial journeys 1/1. The generated client's
current SHA-256 matches its retained generation proof. Initial red and
intermediate development failures remain retained, including the file-delete
race with an abandoned write before the test's settlement wait was added.
The e2e log summarizes the initial TS6053 tooling failure in its header; it
is not complete original failure output. Independent review tooling also
encountered a restricted remote lookup failure (the permitted live lookup
passed), an invalid PowerShell pipeline and an unexpanded rg path glob; none
is a product result or an inferred product cause.

**ASUP-C1, accepted nonblocking limitation.** The journey logs contain 136
and 244 duplicate-key warnings, versus the earlier 72 and 160, plus the
reported negotiation stops. Contrary to the correction record's singular
wording, collaboration has two distinct repeated keys, planning one. Source
inspection confirms LocalCommitPanel and AbandonRunPanel already have the same
sibling key={runId} in the published baseline. That is pre-existing UI debt;
it does not establish the cause of the increased counts. The selected
supervision regressions and journeys pass, with no demonstrated new
supervision failure. No warning-free journey claim, causal attribution,
regression-rate conclusion or unrelated UI fix is accepted by this GO.

**Retained evidence limits.** The negative controls are historical passes
against the first implementation, not fresh passes against this correction:
their source files changed, although the original guards are preserved and
fresh named regressions cover the correction. This clarifies current-work's
"targets did not change" wording. No routine repeat or broad campaign is
required. The original complete Application/API failures and isolated passes
remain unexplained; no clean complete rerun is claimed. Review 1's tooling
failures, control 3's aborted first run/restoration, earlier evidence losses,
PCO-C1 and the unchanged dev-only source-map-js advisory all remain. Mandatory
CI, security, real-provider fidelity and release gates are not waived.
Increments 4/5/6 remain incomplete, ADR-0050 Proposed, P9 unproven and AI Memory
deferred. No real-provider experiment, native-shell or release proof is added.

## Stop boundaries and next action

The same Claude executor chat may publish only the reviewed 141-path outcome,
including these review documents and the executor's review-phase current-work.
Verify the exact parent, path set and expected tree supplied in the GO prompt
before committing. Stage only approved paths, compare the staged and committed
content identity, commit and push main normally, then query the live remote.
A material diff, identity mismatch, changed baseline, failed push or diverged
remote stops publication; no force-push, history reconciliation, closure-document
edit or new slice is authorized. Codex independently verifies publication next;
executor-reported success does not establish that verification.

## Retained review and evidence limits

Active execution-evidence limits:
[current-work](current-work.md#active-evidence-limits). Detail as written:
[WF-04-era limits](planner-handoff-history-e614c79.md#retained-review-and-evidence-limits),
[earlier planner limits](planner-handoff-history-0a57216.md#retained-review-and-evidence-limits),
[execution limits](current-work-history-0a57216.md#prior-published-delivery),
this file at `e55709f`, and per-slice records under ignored
`logs/planner-review/<slice>/` and `logs/executor/<slice>/`. Progressive
reading and condensation waive none of them:
- WF-02A evidence loss remains: derivative, partly abridged chat transcription;
  original failed runs/checker/timings unavailable; run B exit unknown. No
  reconstruction or historical authentication of missing originals.
- Failed, derivative, missing, overwritten and chat-only evidence and all
  earlier stable findings/dispositions stay retained; later passes erase or
  explain none of them. Accepted dispositions (EX-N01, EX-N02, EX-04-02,
  EX-05-01, the RV-04-01 measurement limit, earlier legacy-route
  observations) and retained failures (RV-N01 child streams, planner N
  verify-01/02) stand as recorded.
- Scans/coordinates are lexical; Git/disk/live and independent checks have no
  negative controls. Retained controls and publication phases were inspected,
  not rerun or historically authenticated. Moved counts, ordinals, deferred controls,
  null-model observations and "real today" wording stay unreconciled.
- Records citing the formerly missing
  `planner-handoff.md#verified-transition-baseline` fragment stay historical;
  EX-05-01's CW-A repair is accepted. Archiving does not resolve any
  open issue.

## Historical review routes

Exact earlier review records: WF-05 in this file at `e55709f`;
[WF-04](planner-handoff-history-e614c79.md#selected-slice-wf-04-publication-go);
WF-03I to WF-03N through the
[route list](planner-handoff-history-e614c79.md#historical-review-routes).
Their old GO/pending statements are not current instructions.

## Standing decision constraints

- ADR-0050 stays Proposed; P9 unproven; no mechanism selected. Frozen patches
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
  [current-work](current-work.md#open-risks). Production code and necessary
  tests are selected only for AGENT-SUP-01 under ADR-0056; PLAN-COORD-01 is published.
  This adds observations only, not broader mutation authority. No dependency, experiment,
  capture-limit or standards edits; the old documentation-only production/test
  exclusion is superseded, not the other restrictions.

All six frozen snapshots ([registry](development-workflow.md#historical-snapshots))
remain untouched.
