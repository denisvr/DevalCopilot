# Planner/reviewer handoff

[AGENTS.md](../../AGENTS.md) and [workflow](development-workflow.md) apply.
This is current selection and review state. PLAN-COORD-01 is GO after correction 1.
Historical records retain their original phase; Git and code prevail.

## Priority and verified baseline

Codex remains planner/reviewer; Claude remains executor. Claude returned
PLAN-COORD-01, and the owner requested independent review of that uncommitted
diff. Corrections stay in the existing executor chat. No planning or judgment
was delegated. The exact-content publication GO follows the completed review below.

Startup reconciled on 2026-10-19: branch main; HEAD, local origin/main and a
fresh live refs/heads/main all 1f22a3738976f6b0df0ec59321016be8f5df6115;
empty index, clean tracked tree and no nonignored untracked paths. The first
sandbox remote read failed with getaddrinfo() thread failed to start; the
subsequent permitted read returned that SHA. This establishes no cause for
the first failure. No product tests were repeated for unchanged baseline code.

Review preflight also confirms main, HEAD/local origin/main/fresh live main at
that same SHA, 0/0 and an empty index. The first delivered diff had 171 distinct files
(62 modified tracked files and 109 nonignored untracked files), including the
three planner-prepared selection files. The executor's 112 count matches the
directory-grouped git status entries, not distinct files; the exact reviewed
list is logs/planner-review/plan-coord-01/reviewed-paths.txt. No unrelated-work
discrepancy was identified. Nothing was staged, committed or pushed.
Correction preflight reconciles 176 files (66 tracked modifications, 110 new files): four required routed documents and the new deadline regression class account for the five additional paths. The live remote remains at the same base. Planner review changes this handoff, factual status/provenance wording in three routed documents and ignored review evidence. Three
temporary reproduction test sources were removed after their outputs and
source copies were retained; no product correction was implemented by Codex.
Current context percentage is unavailable; the prior chat's owner-reported
61.0% transition observation is historical and is not this chat's usage.

## Latest verified publication

HIST-OUT-01 is published and independently verified, not reopened by its
committed pre-publication checkpoint wording. Exact commit
1f22a3738976f6b0df0ec59321016be8f5df6115, tree
dc859c2c443b52318b364fb9173100bc7dc298a9, sole parent
459344d3b0f3311d1d0603a0d26187e22959b256: 69 GO-approved paths without rename
detection (11 M, 58 A), GO SHA-256
f779d5543f74165236a8d73d234d1be7eef01ab808f28b7ddc8be70d5ef0a69b.
Prior independent verification and transition were inspected in
logs/planner-review/hist-out-01/publication-verification.json/.log,
independent-published-status.txt and planner-transition.md. This startup
confirms the clean Git/live-remote baseline, rather than inventing a second
historical review. Claude's HIST-OUT-01 chat completed publication; no closure
commit or competing executor work is required.

Independent review evidence: 129 Application, 23 API/auth/Int64, 421 frontend,
172 architecture and strict e2e TypeScript passed. Preserve the initial testhost
timeout, frontend ENOENT failures and literal-wildcard TS6053 failure; subsequent
passes do not prove their causes. HOUT-R1 is resolved by the planner's evidence
wording corrections before GO: negative controls are mainly abridged summaries,
the original M2 diagnostic and executor strict-e2e invocation are missing, and
the executor's blanket raw-output claim is overbroad. Whole frontend 5299,
build/lint/generation and journey remain inspected executor evidence with their
actual scope. Journey 44 duplicate-key/three negotiation errors have no HEAD
comparison; cause and count equivalence remain unproved. Prior HIST-INPC-01's
two lost EF1002 outputs and all earlier limits remain. Exact previous selection,
review and publication GO: this file at commit 1f22a37 and the ignored review
records. Earlier deliveries and decisions: that revision's Latest verified
publication and its explicit historical routes.

## PLAN-COORD-01 review: GO after correction 1

PCO-R1 through PCO-R5 are resolved. PCO-C1 remains an accepted nonblocking
testing-layer limitation. This supersedes review 1's NO-GO and correction
prompt, not ADR-0055 or the preserved evidence. The GO applies only to the
final reviewed substantive diff. Publication authority is bound to the exact
parent, tree and 176 approved paths in the accompanying planner GO record and
prompt under logs/planner-review/plan-coord-01/. Nothing has been committed or
pushed by the planner; publication still requires the executor's identity
checks, normal push and independent planner verification.

- **PCO-R1 [P1], resolved.** Completed results are judged by their own completion
  time before classification. At or after the deadline ends with attention;
  one tick before remains timely even when observed later. Terminal attempts
  and outputs remain recorded, and an already-won Stop keeps Stopped. The read
  projection also reports genuinely late completions before persistence. Seven
  permanent Application regressions cover Proposal, Acceptance, Resolution and
  Stop precedence; the independent PlanningSequence filter passes 128/128.
- **PCO-R2 [P2], resolved.** Observation owns inFlight only while it runs; the
  original reservation owns one separate preparation task keyed by step, with
  the same four preparation slots. A later poll expires a blocked preparation;
  its late return cannot claim. Hosted tests prove durable revocation while
  capture stays blocked and zero attempts/provider calls after release. Exact
  ticket checks and atomic linking remain in the existing claim transactions.
- **PCO-R3 [P2], resolved.** Context, workflow intake/correction wording, ledger,
  MVP partial delivery, protocol and cockpit specification state the narrow
  read-only capability. Intake stays inert, modes immutable, implementation
  separately authorized and increments 4/5/6 incomplete. Accepted historical
  ADRs were not rewritten.
- **PCO-R4 [P2], resolved with evidence loss retained.** Current-work separates
  original reports, retained outputs, fresh checks and not-run checks. The
  original whole Application run had one failure, followed only by its 93-test
  class; there is no full passing rerun on final inputs. Original broad run
  logs were not saved as files. A transcript location and retained control
  outputs are identified; the first surviving Application predecessor-mutant
  output was overwritten and remains lost. Later passes authenticate none of
  that history. Correction mutation/format claims without separate output logs
  remain executor-reported. See current-work for exact provenance.
- **PCO-R5 [P2], resolved.** Query State now equals the effective snapshot State;
  RecordedState remains separate. The closed frontend decoder was not relaxed.
  Independent real-query/endpoint exports of both expired preparation and
  expired running-result responses were serialized, admitted by the actual
  frontend decoder and rendered as attention before persistence, without Stop.
  These are direct endpoint-mapping checks, not an additional HTTP auth proof.

**PCO-C1, accepted nonblocking limit:** Application's helper duplicates claim
composition and misses a wrong Resolver predecessor in the production composer.
Production still passes ticket.PredecessorAttemptId and the consumer validates
it; independent hosted two/four/five-step production-mediator paths pass. No
additional mirror test or Resolver product correction is required. Retained
hosted mutant output supports its reported kill but does not recover the lost
surviving Application run or independently authenticate its history.

**Other reviewed boundaries retained:** all three step links are inside claim
transactions with independent uncertainty probes; early and durable reciprocal
manual/verification/local-commit/abandonment fences, eight DB guard inventory,
Stop drain, source binding and startup interruption after attempt recovery were
reviewed. Prior independent Infrastructure 15, frontend 546 and architecture
172 passes retain their scope; this is not every race for each guard. Local
commit early-refusal handler coverage remains unproved beyond inspection and
the direct DB backstop. Three MVC operations remain protected and individually
mediated; body, auth, wire and stale-action regressions pass.

**Independent correction verification:** Application PlanningSequence 128,
original failing event-history class 93, API PlanningSequence 39, frontend
three decoder/panel/stale-action files 237, and two actual backend response
exports plus two frontend admission/render cases passed. Full original
outputs, exact commands and source copies: review-02.md and adjacent logs in
logs/planner-review/plan-coord-01/. Two temporary reviewer test sources were
removed; only ignored source copies and exported fictitious responses remain.
The first sandbox Application correction run produced no output and was
interrupted; its cause is unknown, not explained by the subsequent pass.

**Validation acceptance adjustment:** accept the originally reported full
Application run with one pinned event-list failure, that class's independent
93-test pass and the current 128-test feature pass instead of requiring another
full Application rerun. The changed shared admission consumers were exercised
in the original broad run; the correction changes only the feature's deadline
policy/projection/coordinator and tests/docs. These consumers are enumerable,
with actual hosted paths and the repaired failed class verified independently.
The unchanged broad API/frontend/migration evidence is retained with its actual
reported/inspected provenance; it is not recast as a fresh final-input run.
Domain's additive deadline helper is covered by the Application boundary; its
older full-project count is not a new full Domain pass. No wider API rerun,
real provider, P9 or live-clock deadline proof is claimed. The two corrected
journeys and architecture 172 passes have executor logs; other original broad
results remain reported evidence. Journey duplicate-key/negotiation console
errors have no HEAD comparison and their cause/equivalence remain unproved.
This bounded local acceptance changes no CI or release gate.

## Selected slice: PLAN-COORD-01 bounded read-only planning coordination

**Decision:** implement accepted
[ADR-0055](../decisions/0055-coordinate-one-explicitly-authorized-read-only-planning-sequence.md).
One Human arming request for a pristine ManualAgent run coordinates Planner,
CriticalReviewer, Resolver, one further CriticalReviewer and Resolver as needed
(maximum five attempts), ending in Accepted planning or the existing depth-two
Human escalation. No subsequent per-stage click or message transfer is required
inside that sequence. This selection is not product delivery or publication GO.

**Code basis:** CreateManualRun is inert; the three planning supervisors poll
claimed attempts at 500 ms and do not claim successors. The existing three
Create*Attempt commands seal context before their commit; Codex claims use short
manual transactions, whereas ordinary Claude review currently relies on a
single SaveChanges boundary. Linking a step afterward would permit duplication
after an ambiguous response. PlanningLineage already validates depth 0/1/2 and
exact input/reply/provenance; RecordChallengeResolutionResult already records
the second-round escalation. Startup recovery completes artifact recovery and
read-only/mutating Agent reconciliation before hosted workers start. Reuse those
contracts rather than infer execution from the inspection API or loaded timeline.

**MVP critical path and sizing:** more inspection/replay does not join stages.
This sequence delivers the complete existing nonmutating planning dialogue,
including its bounded second round, together with durable authority, atomic
step claims, stop races, restart safety, deadlines and visible outcomes. A
plan-only coordinator or separate backend/UI dispatch would leave the same
outcome incomplete. Full plan/edit/verify/review/correct/commit automation adds
mutating grants, recipe/execution-set identity, correction routing, writer and
scheduler ownership and ambiguous-write recovery; those are concrete separate
policy/validation boundaries. General scheduling and pause/resume do not select
next protocol inputs. Provider-gap work remains required, but this sequence
uses already supported roles and adds no invocation capability.

**Resolved authority:** ADR-0055 narrowly supersedes ADR-0014's per-stage
request rule only for this explicit sequence and narrowly extends ADR-0009's
ordering only for read-only coordination. Immutable modes, inert intake,
ADR-0006 exclusion and all existing claim/dispatch/budget/source gates remain.
One sequence per run lifetime, exact pinned source, five unique step ordinals,
no historical adoption, retries, repairs, new root or rearm. The same transaction
must save each attempt, its step association, inputs, artifacts and budget
consumption. Reserve preparation once, fence stale workers and independently
resolve persistence uncertainty. A duplicate/failed tick cannot spend another
claim. Manual Agent/verification/commit/abandonment races are server-enforced.

**Control and visibility:** Stop after current step revokes an uncommitted
preparation or drains the already claimed step; it is not process cancellation.
Two-minute preparation and timeout-plus-two-minute result deadlines close
advancement with truthful attention. Expired/closed linked attempts cannot
start a provider; use the dedicated independently proved pre-dispatch refusal,
not WorkspaceNoLongerEligible. Already dispatched attempts keep their real
ownership and results. Startup interrupts every formerly advancing sequence,
including between steps, with no automatic resumption. Preserve existing
attempt reconciliation and side-effect-free host compositions. No heartbeat or
executor-lease completion is claimed: coordinated cockpit/header/runtime views
must distinguish recorded lifecycle, arming, dispatch and awaiting-result from
live execution, including while status is unavailable. Ordinary manual actions
return only when their existing gates and the sequence fence both admit them.

**Scope:** feature-owned Domain state, DbContext sets, EF configurations and an
additive migration; three protected start/read/stop MVC operations and complete
owned DTO graphs; internal step tickets on the three existing claim commands;
common claim/dispatch admission guards only where this sequence owns their
semantics; dedicated dispatch-refusal recorder and three supervisor branches;
coordinator/deadline sweep and ordered startup recovery; generated client,
scoped raw-wire validation, hooks/panel and coordinated header/runtime/manual-
action composition. Preserve provider adapters, process contracts, schemas,
source text/artifact admission, historical readers, shared UI lifetimes and
shared browser harness. Update ledger/workflow/cockpit/engineering-context/MVP
status only to reflect the exact partial capability; do not declare increments
complete. Planner-owned ADR/decision selection remain under Codex judgment.

**Stop gates:** inability to compose an atomic step/attempt claim with current
ports; any need for different provider permissions, real-provider experiments,
manual-run conversion, automatic mutation, new dependency, general scheduler,
shared harness/cache, weaker source/budget/recovery semantics or publication;
material preflight discrepancy or disagreement with this architecture. Report
evidence and a bounded alternative before dependent work; no silent expansion.

**Validation matrix** (record exact commands, names, source and elapsed time;
focused development checks, then one stable-input final matrix):

- New tests use PlanningSequence in their class/file names so focused .NET
  filters `FullyQualifiedName~PlanningSequence` enumerate the feature. Use
  independent file-backed SQLite connections for race/rollback/reopen and real
  migrations for empty/upgrade/legacy constraints. Exercise all two/four/five-
  attempt branches, complete exact input sets, duplicates/uncertain commit,
  both sides of stop/claim and arming/manual/verification/abandonment races,
  source/mode/lease drift, all restart boundaries and expired pre-dispatch
  versus already-dispatched results. Assert zero unauthorized external calls
  and independently verify per-step/budget/membership cardinality.
- Final Domain and Application projects in full: shared admission and lifecycle
  consumers are affected. Infrastructure filter
  `FullyQualifiedName~PlanningSequence|FullyQualifiedName~Migration` covers
  new persistence plus existing migration compatibility. Add feature constraints
  to that filter if their names do not contain Migration.
- Final API filter: new PlanningSequence classes plus
  AgentAttemptSupervisor, ClaudeCriticalReviewSupervisor,
  ChallengeResolutionSupervisor, SecondChallengeRoundSupervisor,
  ManualRunHostedTests, CreateManualRun, RequestCodexPlanning,
  RequestClaudeCriticalReview, RequestChallengeResolution, RequestReadOnlyFormatRepair,
  RequestCodeReview, RequestImplementation, ReviewCorrectionEndpoint,
  RequestVerificationDiagnosis, RequestDiagnosisCorrection,
  PlanningImplementationAuthorizationEndpoint, LocalCommitEndpoint,
  AbandonManualRunEndpoint, VerificationCommandsEndpoint,
  VerificationExecutionSupervisor and LaunchSessionAuthentication. Translate
  each name to FullyQualifiedName~name joined by | and retain the actual filter.
  Include a real MVC authorization inventory for new operations and startup/
  side-effect-free composition tests. Broaden to the full API project if shared
  host/pipeline behavior changes or affected consumers cannot be enumerated.
- Frontend: explicit new decoder/hook/panel and header/runtime/cockpit/manual-
  action files during development; final whole Vitest once because shared
  cockpit action gating/presentation changes, plus lint and production build
  (includes tsc -b). Test raw Int64/time/closed tuples, failed status, arming replay,
  late stop/result, A-B-A, remount and queued stale callbacks. No shared lifetime
  primitive changes. Reproducible NSwag after API build and inspect exact drift.
- One new planning-sequence.journey.ts arms once through the real authenticated
  UI/host with provider executables replaced by existing deterministic doubles,
  verifies exact durable step/result identities and reaches a terminal planning
  outcome without per-stage POSTs. Run it and collaboration.journey.ts serially;
  prove remaining branches/recovery at cheaper hosted boundaries. Strict e2e
  TypeScript uses an explicitly expanded file list. Shared harness modifications
  require planner reassessment and, if accepted, the canonical full harness chain.
- Full architecture suite; applicable incremental builds/analyzers, formatter,
  dependency/secret checks, changed links/inbound fragments and complete diff.
  Kill four compiling, restored controls: missing sequence claim fence, separate
  step link, wrong predecessor and stale UI action. Use named focused tests,
  retain originals and verify restoration; no broad mutation campaign.
- Retain unaffected provider-adapter, artifact/source, historical-inspection,
  other-journey and all previous failure evidence with its actual limitations.
  Corrections rerun only invalidated checks. No tests were run by the planner
  for this documentation-only selection; Claude produces implementation evidence.

## Remaining MVP work

No defensible completion percentage or delivery date follows from the number
of past slices. Increments 4/5/6 remain incomplete. After this first coordination
segment the unresolved fronts still include the mutating local loop and its
scheduler/control/recovery contracts, provider/account/context/session gaps,
GitHub publication qualification and CI correction/reconciliation, then the
packaged self-hosting demonstration. P9 remains unproven, ADR-0050 Proposed and
AI Memory deferred. None is accepted as a future dispatched slice here.

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
  tests are authorized only for PLAN-COORD-01 under ADR-0055. No dependency, experiment,
  capture-limit or standards edits; the old documentation-only production/test
  exclusion is superseded, not the other restrictions.

All six frozen snapshots ([registry](development-workflow.md#historical-snapshots))
remain untouched.
