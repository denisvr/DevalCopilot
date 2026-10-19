# Planner/reviewer handoff

[AGENTS.md](../../AGENTS.md) and [workflow](development-workflow.md) apply.
This is current planning state; historical GO and pending wording in snapshots
is evidence, never renewed authority. Git and code prevail over summaries.

## Priority and verified baseline

Codex is planner/reviewer; Claude is executor. The owner explicitly requested
this new planner chat after HIST-INPC-01 publication; the outgoing chat reported
63.4% context and selected no successor. Functional roadmap development remains
authorized; experimental authority is not renewed.

Selection baseline checked on 2026-10-19: main; HEAD, local origin/main and
fresh live main 459344d3b0f3311d1d0603a0d26187e22959b256; empty index, clean
tracked tree and no nonignored untracked paths. HIST-INPC-01 is published and
independently verified. Committed checkpoints describe preparation phases;
their old pending wording reopens neither that slice nor earlier deliveries.
No separate closure commit is required.

Selection added the planner-owned ADR-0054, decision-index update and this
handoff. Claude has returned HIST-OUT-01; the review GO below accepts only its
fixed final identity, including planner evidence-wording edits. Work remains
unstaged/uncommitted/unpushed until the executor acts on that GO. Keep publication
and any corrections in the existing HIST-OUT-01 executor chat; start no competing
work. Current context usage in this planner chat is not available; do not reuse
the outgoing chat's percentage as a current value.

WF-01–WF-06 remain published and verified. Earlier priority/review routes:
this file at commit 459344d and its linked historical snapshots.

## Latest verified publication

HIST-INPC-01 commit 459344d3b0f3311d1d0603a0d26187e22959b256, tree
5988448e298cf7dede4c8803e68a74416ee62595, sole parent
80a2ec228e8ed97a55bd0f76ccbd03c3f57c22e4: independent verification of the
fixed GO identity and exact 66 approved paths/statuses without rename detection
(28 M, three D, 35 A) is retained in
logs/planner-review/hist-inpc-01/publication-verification.json and .log.
This startup confirms commit/tree/sole parent, path counts, GO SHA-256
7c61532d2058c6ca860bcfb33abf839ff5ed1dafe83bdf151094ee040771398f, clean Git
state and fresh live main. Detailed review/GO: this file at commit 459344d and
logs/planner-review/hist-inpc-01/review-01.md and publication-go.json.
Independent review checks: 178 Application, 52 API/auth, 328 frontend and
164 architecture passed. Executor whole frontend 5009, builds, lint, NSwag,
strict e2e and journey were inspected, with their actual scope and limits.
No product tests were repeated for this unchanged-content startup.
The original two EF1002 failures have no retained output: never reconstruct
or authenticate them from later passes. Failed runs, expectation/escape fixes
and controls stay retained. Journey counts of 45 duplicate-key and three
negotiation errors lack a HEAD comparison; cause and count equivalence remain
unproved. Existing consumer/store/journey limits and all six frozen snapshots
remain unchanged.

HIST-VER-01 `80a2ec228e8ed97a55bd0f76ccbd03c3f57c22e4`, tree
`1f5cfa0631ef5ec94272eba7e6d950f3b93f87ec`, sole parent
`26c3f24a5165f9735b6fe50ee6cfc112a70b88bc`: independently compared with
the fixed GO tree and exact 74 approved paths/statuses without rename detection
(12 M, 62 A), including the owner-requested AGENTS/CLAUDE/workflow rules.
Fresh live main, HEAD and local origin/main agree, 0/0; index/tree clean and
no nonignored untracked files. Evidence:
`logs/planner-review/hist-ver-01/publication-verification.json` and `.log`;
fixed GO: `publication-go.json`; full review: this file at `80a2ec2` and
`review-01.md`/`review-02.md` in that ignored directory. HVR-R1/R2/R3 resolved
after correction round 1. Independent corrected-input checks: 150 Application,
58 API/auth, 317 frontend and the original five-case lifetime reproduction
all passed. Executor 4852-test whole frontend, architecture 161, strict e2e,
build/lint/client reproducibility and the extended collaboration journey were
inspected, not independently rerun in that round. The missing original
TypeScript failure output remains lost. Journey console counts (154 duplicate
keys, 14 negotiations) lack a HEAD comparison/comparable prior capture, so
cause and count equivalence are unproved. Retained identity/store/other-journey
scopes and failed controls remain as recorded. No publication-time product
retest or closure-document commit was needed.

HIST-INP-01 `26c3f24a5165f9735b6fe50ee6cfc112a70b88bc`, tree
`2db3fbe53bc2ad5a6ecd35a63a9edb02ae5e6095`, sole parent
`96a21d737777980724cc7351d8d7a2c10b0e6e2d`: independently compared with
the fixed GO tree and exact 49 approved paths/statuses without rename detection
(16 M, one D, 32 A). Fresh live main, HEAD and local origin/main agree, 0/0;
index/tree clean and no nonignored untracked files. Evidence:
`logs/planner-review/hist-inp-01/publication-verification.log`; exact GO:
`publication-go.json` in that directory. Full review and selected prompt:
this file at `26c3f24`. Independent review checks: 183 Application, 30 API and
255 frontend tests passed. The first independent Application testhost timed
out; the same binaries passed outside the sandbox, without proving the cause.
The 4621-test whole-frontend result remains pre-fixture-correction evidence,
followed by the corrected file's 18 tests and focused review, not a later full
rerun. Three journeys and strict e2e typecheck are retained same-slice evidence
on unchanged functional inputs. All recorded failures, surviving controls and
browser coverage limits remain in current-work and the ignored evidence.
Publisher preflight helper recursion, stderr-contaminated comparison and
sandbox network failure are preserved in `logs/executor/hist-inp-01/publication/`;
no approved content changed. No publication-time product retest was needed.

HIST-MET-01 `96a21d737777980724cc7351d8d7a2c10b0e6e2d`, tree
`206f8a387ad459fe11f905e95f3f1086d0cda9f9`, sole parent
`5e588a44b5ebc0e633cf1f2e1dbb3af442515a2b`: independently compared with
the GO tree, sole parent and exact 16 paths (10 M, six A); fresh live main,
HEAD and local origin/main agree, 0/0, clean index/tree, no nonignored untracked
paths. Evidence: `logs/planner-review/hist-met-01/publication-verification.log`.
HMT-R1 resolved after two corrections; immutable identity, raw timestamp and
section-isolation evidence, accepted executor challenges and limits: this file
at `96a21d7` and `logs/planner-review/hist-met-01/`. Independent final focused
checks: 247 passed. The 4471-test full-suite pass remains prior-input evidence,
not a correction-2 full-suite rerun. Known token counts are not browser-proven.
Nested timestamp/other-operation parsing, duplicate React keys and compiler
locks retain their stated limits. No publication-time product retest needed.

ART-WIN-01 `5e588a44b5ebc0e633cf1f2e1dbb3af442515a2b`, tree
`420617062a7bb2ebcdec87ac5bca314c5df03d35`, sole parent
`75372b18d3d669f744010a3b9a628a408acaed4c`: independently compared with
the GO tree, sole parent and exact 19 paths (14 M, five A); fresh live main,
HEAD and local origin/main agree, 0/0, clean index/tree and no nonignored
untracked paths. Evidence: `logs/planner-review/art-win-01/publication-verification.log`.
No blocking findings; independent focused tests: 179 passed. The broad frontend
run remains 4307 passes/two invalid-fixture failures, then the corrected file
43/43, not a later full-suite pass. Detailed GO and accepted executor challenges:
this file at `5e588a4` and `logs/planner-review/art-win-01/`. Browser proof is
short ASCII/empty artifacts; other permutations have narrower test evidence.

HIST-ATT-01 `75372b18d3d669f744010a3b9a628a408acaed4c`, tree
`d49a3eac47b8d8a9d19c5b27fd6c6c622f695d40`, sole parent
`4b52e1cfc3dbfbcb20908da5e87459b6e7c3e4f7`: independently compared with
the GO tree, sole parent and exact 26 paths (18 M, eight A); fresh live main,
HEAD and local origin/main agree, 0/0, clean index/tree and no nonignored
untracked paths. Evidence: `logs/planner-review/hist-att-01/publication-verification.log`.
HATT-R1 resolved; review detail: this file at `75372b1` and
`logs/planner-review/hist-att-01/`. Independent correction tests: 104 passed;
executor final matrices and authenticated journey inspected, retained scopes
unchanged. No publication-time product retest was needed.

WF-06 `4b52e1cfc3dbfbcb20908da5e87459b6e7c3e4f7`, tree
`e888fe69ac5b790aec793822f84865381a6bb8ec`, sole parent
`e55709fe80898b6c977b39d09b85b9733f0c6f4c`: independent verification of
the GO identity, exact five approved paths and live remote is recorded in
the final section of `logs/planner-review/wf-06/review.log`. WF06-R1/R2
resolved. The committed checkpoints describe pre-publication phases; neither
requires reopening or republishing WF-06. Its former functional pause is
superseded by the owner's resumption above.

WF-05 `e55709fe`, sole parent `e614c795`, exactly the six manifest paths:
Codex GO `logs/planner-review/wf-05/review-01.json` with
`publication-manifest.json`; EX-05-01 accepted; independent publication
verification complete (`logs/planner-review/wf-05/publication-verification.json`).
Publisher phases inspected, not rerun or historically authenticated; the
executor's failing dev-01 pre-fix checker was not retained or reconstructed.
WF-04 `e614c795` verification 5/5, its findings and the documentation/workflow
rework acceptance at `e614c795`
(`logs/planner-review/documentation-rework-acceptance.json`) stand as recorded
in this file at `e55709f`; WF-03 technical acceptance stands. WF-03N detail:
[exact record](planner-handoff-history-e614c79.md#latest-accepted-delivery).

## HIST-OUT-01 review: publication GO

Codex accepts the substantive implementation with no actionable product finding
after code review and independent checks. GO covers only the fixed reviewed
identity and approved paths recorded outside the tracked tree in
logs/planner-review/hist-out-01/publication-go.json and the complete publication
prompt. A mismatch stops publication; it never broadens GO. Publication and
independent publication verification have not happened yet. No next slice is
selected or dispatched.

**Review basis:** the bounded scalar proof selects by message-level AttemptId
before judging the whole association set, preserves the existing identity and
role-author policies and admits no text before the proof. The member read
repeats it, constrains run/attempt/message and rechecks exact header, provenance
and actor through unchanged guarded collaboration content admission. API-owned
graphs preserve Int64/time strings. Raw facades and whole-answer decoders keep
closed tuples and selected-reference binding; queued numbered visits and
opening-owned disclosure use unchanged lifetime primitives. The journey's new
oracle locates actual Resolver outputs by their own message-level association,
compares a Decision and revised Proposal and preserves all prior input assertions.

**Executor points accepted:** the separate Int64 wire-test class is proportional
isolation for SQLite's saturated sequence counter and is included in the final
and independent API filters. Frontend provider-observed provenance and exact
actor matching enforce the selected attempt's relationship contract; they do
not add a new content policy or change accepted architecture.

**Independent evidence:** 129 Application, 23 API/auth/Int64, 421 frontend across
ten transport/decoder/hook/component/composition files and 172 architecture tests
passed. Strict e2e TypeScript passed with an explicitly expanded file list and
retained arguments. The first strict command passed literal wildcards through
PowerShell and failed TS6053 before checking the files; it is retained. The
first frontend run retained ENOENT temporary-module failures (19 tests passed,
nine suites failed); the same filter passed with workspace TEMP/TMP. The first
Application testhost connection timed out/aborted after 90 seconds; the same
rebuilt binaries passed outside the sandbox, without proving the timeout's
cause. Commands and complete independent outputs: logs/planner-review/hist-out-01/.
All 63 non-document files match the executor's final input hashes; between its
before/after evidence baselines only the two test fixtures changed. The client
matches the inspected reproducible blob. The planner handoff was independently
compared to its authored selection and was unchanged by the executor.

**Evidence wording finding HOUT-R1: resolved by planner documentation edits.**
Current-work now distinguishes ordinary failure diagnostics from abridged
negative-control summaries, records the parameter-assertion and toggle-list
failures, removes the blanket no-product-failure claim and identifies the
unretained M2 compiler diagnostic and original strict-e2e invocation. The
executor evidence.md claim of raw output for every command is overbroad; keep
that original report as evidence, not authority. Controls were inspected as
named failure/count/restoration summaries, not complete mutation/build/test
histories or independent historical authentication. Do not reconstruct missing
originals or use later passes to authenticate them. This finding needs no
product-code change or new mutation campaign.

**Inspected, not independently rerun:** the executor's 5299-test whole frontend,
lint/build, generator reproducibility and authenticated journey, with their
recorded ordinary failures, corrections and actual limits. The journey's 44
duplicate-key and three negotiation errors lack a HEAD comparison; cause and
count equivalence remain unproved. Empty/large/corrupt sets and replacement
lifetimes remain lower-boundary proof, not browser coverage. Unchanged inputs,
history, attribution, verification, artifact/store and other journeys retain
their earlier scopes. Existing failures, restrictions and all six snapshots
remain unchanged. Planner-only evidence wording and this review decision change
no product/test input and require no product retest.

Claude publishes only the fixed identity in the same HIST-OUT-01 chat after
verifying the complete prompt's preconditions, staged/committed identity, sole
parent and exact paths. Normal main push only; Codex independently verifies
publication before the next selection and checks current context usage then.

## Selected slice: HIST-OUT-01 recorded outputs and selected content

**Decision:** implement accepted
[ADR-0054](../decisions/0054-inspect-recorded-outputs-of-a-selected-agent-attempt.md)
as one complete read-only vertical inspection. Claude executes; Codex retains
architecture, review and GO. Acceptance of this contract is not product delivery.
The prior HIST-INPC-01 selection/review is preserved in this file at commit 459344d.

**Code and roadmap basis:** AgentAttemptDetail mounts recorded inputs and
verification, but no output-message section. CollaborationMessage.AttemptId
and its existing index already identify outputs; RecordChallengeResolutionResult
records multiple Decisions plus a revised Proposal, and correction records
multiple RevisionResponses plus an ExecutionReport. The inverse ADR-0037
attribution exists but does not locate all messages of a selected attempt.
Host-constructed escalations have null message-level AttemptId; event attribution
is not a substitute. This advances Increment 4's visible collaboration and
Increment 5's historical inspection, without declaring either complete.

**Sizing and alternatives:** bundle all bounded references, one explicitly
selected body, both MVC queries, raw-wire validation, opening/visit ownership
and the authenticated older-run journey. List-only or body-only delivery leaves
half of this same access gap. The existing input journey already records a
multi-output Resolver, so the complete outcome needs no invented provider or
new test harness. Full linked replay requires graph traversal/navigation policy;
automatic stage coordination requires durable dispatch/recovery/authority
contracts beyond the current explicit per-stage commands. Pause/resume would
also change lifecycle admission, result/restart handling and claim/dispatch
serialization across operations; the current enum has no Paused state. These
are concrete separate dependency and validation boundaries, not arbitrary
file-count limits. Provider gaps and product GitHub execution remain constrained;
ADR-0050 cannot be treated as accepted. Historical inspection does not complete
the local delivery loop.

**Resolved contract:** two new protected GETs, outputs and outputs/{messageId}
under the exact run/attempt route; new feature-owned output-set proof shared
only by these operations. Reuse unchanged scalar attempt identity and role
author policy. Select by AttemptId, inspect at most 64 of 65 ordered scalar rows,
reject any foreign/malformed association before returning references or reading
text. Empty is valid for any admitted role; nonempty requires dispatch and known
version/type, ProviderObserved provenance and exact Agent actor role/provider.
No outcome/cardinality reconstruction. The list returns the complete admitted
set; the UI reveals it locally in tens. The content operation proves the whole
set again, reads only one member through unchanged guarded
CollaborationHistoryRows, and rechecks the association, header, provenance and
actor. Whole-set Unavailable precedes nonmember 404; unavailable content remains
distinct. Exact closed graphs, string Int64/time, errors and read-now limitations
are in ADR-0054.

**Frontend:** independent collapsed Recorded collaboration outputs in the
selected-attempt detail. One explicit content selection, Close and failure-only
Retry, no content prefetch. Validate raw JSON whole, bind immutable attempt and
selected reference, and preserve raw timestamps. Reuse unchanged owned-lifetime
primitives and content admission. Opening/local disclosure and queued member
visits own callbacks/results across close/reopen, A-B-A, same-batch reselect,
parent reload and unmount. No chain navigation, live/mutation control, URL or
browser persistence, export, linkification or content logging.

**Scope/exclusions:** the two Application queries/owned output policy, two MVC
operations and DTO graphs, generated client and scoped raw facades, selected
output decoders/hooks/views and proportional tests. Preserve all existing
input, history, reply, attribution, verification and artifact behavior. No
Domain, persistence/migration/index, claim/dispatch/result, source capture,
artifact-store, provider, dependency, shared lifetime or shared harness/cache
change. If the existing index cannot support the bounded relationship query,
or unchanged projection/admission cannot satisfy the reread proof, stop with
evidence and a bounded alternative rather than broaden architecture.

**Acceptance/final matrix:**

- Application: file-backed SQLite for both new operations; zero/one/multiple/
  11/64/65 rows, last-row corruption, all ownership/provenance/actor/type and
  dispatch refusals, unchanged identity/content policy, exact large Int64/time,
  bounded indexed scalar reads, no text before whole proof, one selected text
  read, reread association/header/actor drift, faults/cancellation and no effects.
  Final filters: GetAgentAttemptOutputs and GetAgentAttemptOutputMessage.
- API: both new endpoint classes plus launch-auth inventory, representative
  404/Unavailable/content-unavailable/Recorded wire tuples, raw sequence/time,
  closed owned OpenAPI graphs and reproducible NSwag. No need to repeat every
  policy permutation at MVC.
- Frontend: new transport/decoder/hook/component files and selected-attempt
  history/composition regressions; all-member validation, local 11/64 disclosure,
  no body prefetch and complete queued/retained-callback ownership. Lint/build;
  one final whole-frontend pass is justified by selected-attempt composition and
  client factories. Strict e2e typecheck remains separate.
- Journey: extend only historical-attempt-inputs.journey.ts, preserve its input
  oracle and real lineage. With the newer objective selected, inspect the older
  Resolver's complete output references, then explicitly a Decision and revised
  Proposal, comparing to exact stored rows. Bound GETs and prove no automatic
  body/artifact reads, additional invocations, source or durable writes. Empty/
  large/corrupt output sets stay below the browser. Build the stable host and
  run this journey once serially; unchanged other journeys retain prior scope.
- Architecture, changed-document links/inbound fragments, whitespace/diff and
  applicable sensitive-data checks stay fresh. Update the inspection/cockpit
  contracts and current-work as delivery-for-review; leave planner ADR/index/
  handoff substantive decisions unchanged.

Run focused checks while developing, then this final matrix once on stable
inputs, retaining commands, filters, timings and original failures in one concise
log. Retain old input/history/verification/store evidence only if its inputs stay
unchanged; a changed shared policy/projection/primitive requires planner review
and consumer regressions. Broaden for concrete uncertainty or failures, not by
routine. No new shared snapshots or mutation campaign. Return the complete diff
unstaged, uncommitted and unpushed; no next-slice choice or publication before
explicit Codex GO for its final reviewed identity.

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
  tests are authorized only for HIST-OUT-01 under ADR-0054. No dependency, experiment,
  capture-limit or standards edits; the old documentation-only production/test
  exclusion is superseded, not the other restrictions.

All six frozen snapshots ([registry](development-workflow.md#historical-snapshots))
remain untouched.
