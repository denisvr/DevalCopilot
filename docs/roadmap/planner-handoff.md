# Planner/reviewer handoff

[AGENTS.md](../../AGENTS.md) and [workflow](development-workflow.md) apply.
This is current planning state; historical GO and pending wording in snapshots
is evidence, never renewed authority. Git and code prevail over summaries.

## Priority and verified baseline

WF-01–WF-06 are published and independently verified. The owner's 2026-10-18
resumption of functional roadmap development and explicit planner transition
stand; experimental authority is not renewed. Codex plans/reviews. Claude began
HIST-INP-01; the owner transferred its execution completion to Codex when
Claude's credits ran out. A bounded Codex executor published its exact reviewed
GO; the planner independently verified that publication before selecting below.

Selection baseline independently checked: `main`; HEAD, local origin/main and
fresh live main `80a2ec228e8ed97a55bd0f76ccbd03c3f57c22e4`; divergence 0/0;
empty index, clean tree and no nonignored untracked paths. HIST-VER-01 is
published and independently verified. Claude returned HIST-INPC-01 for review.
Final review confirms the same HEAD, local origin/main and fresh live main,
divergence 0/0, empty index, 28 modified and three deleted tracked paths plus
35 new/untracked files (66 approved paths without rename detection). GO below
covers only its fixed reviewed identity. Publication and independent publication
verification have not happened yet for HIST-INPC-01.
Committed checkpoints describe their preparation phase; do not reopen or
republish HIST-INP-01 or HIST-VER-01 because of their pre-publication wording.

WF-05 baseline and GO: this file at `e55709f`; earlier:
[priority](planner-handoff-history-e614c79.md#priority-and-verified-baseline),
[WF-04 GO and review](planner-handoff-history-e614c79.md#selected-slice-wf-04-publication-go).

## Latest verified publication

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

## HIST-INPC-01 review: publication GO

Codex accepts the first delivered substantive diff, with no actionable findings
after code review and independent checks. Claude may publish only its fixed
reviewed identity in the same HIST-INPC-01 executor chat. The exact tree,
expected sole parent and 66 approved paths/statuses are fixed outside the
tracked tree in `logs/planner-review/hist-inpc-01/publication-go.json` and the
complete publication prompt. A mismatch stops publication, never broadens GO.
No next slice is selected or dispatched.

**Review basis:** the three moved projection/row files differ only in namespace;
the feature-owned reader preserves the complete ADR-0051 proof before either
prefix or content access. Default-ten behavior and the optional 1..64 limit
remain separate from the 65-read/64-inspection bound. The new handler selects
only one admitted member's guarded content through unchanged
`CollaborationHistoryRows`, compares the reread header and withholds any
disagreement. The API owns the new graph and preserves sequence/time text.
The raw decoder checks every new object's keys before unchanged history
validation. Opening-owned disclosure and queued, numbered selection visits
keep old callbacks/results out of successors; shared identity, content
admission and lifetime primitives are unchanged.

**Independent evidence:** both Application classes 178 passed; both input
endpoints, metadata Int64 and launch-auth inventory 52 passed; nine frontend
files (the executor's eight affected files plus historical-attempt composition)
328 passed; architecture 164 passed. All were rebuilt/run from the reviewed
source. Local links across six changed/new documents: 176 checked, zero broken;
whitespace passed. ADR-0053 and the pre-review handoff match their planner
selection hashes; the generated client matches the inspected reproducible
identity. Commands and complete outputs: `logs/planner-review/hist-inpc-01/`.

**Executor evidence inspected, not independently rerun:** final whole frontend
5009/246 passed after its retained failed run and corrected request-argument
expectation; lint/builds, generated-client reproducibility, strict e2e typecheck
and the extended historical-attempt-inputs journey. The journey compares two
actual older inputs with stored rows after a newer objective and bounds the
explicit GETs, without new invocations, artifact reads or durable/source changes.
Large/corrupt sets and ownership replacements remain lower-boundary proof,
not browser coverage. Four backend controls ran through .NET build/test and
five frontend controls through Vitest, with inspected failure/restoration
evidence; no full TypeScript build is claimed for mutated inputs. Changing the journey's
expected summary is assertion-sensitivity evidence, not a production mutation.
This inspection does not independently authenticate historical runs.

**Executor points resolved:** rejecting repeated blanks follows ADR-0053's
single-blank rule; declaring metadata 400 is required for the new validation.
An unprovable set returns Unavailable even for a stranger ID because membership
cannot be decided. Header-consistent content changed between reads can be
returned as reread under the existing content policy; no snapshot is claimed.
The host-omission copy remains truthful defensive copy, although unreachable
for the UI's current limit=64 request.

**Retained limits:** the initial two EF1002 analyzer failures have no retained
file output. Accept that evidence loss as a delivery limitation, never
reconstruct it or treat later compilation as proof of the missing original.
The executor's statement that no product code failed is not an independent
historical conclusion. The two development test failures, first final frontend
failure, escape repair and all controls remain recorded in current-work and
ignored evidence. Journey console counts (45 duplicate keys, three connection
negotiations) lack a HEAD comparison; cause and count equivalence are unproved.
Unchanged history/reply/attribution/message-evidence consumers, verification,
store containment and other journeys retain only their recorded scopes.

GO includes exactly 28 M, three D and 35 A paths, including planner ADR/index/
handoff and current-work. No product inputs changed during review; the final
planner-only decision edit requires no product retest. Claude must verify the
fixed staged/committed tree, sole parent and exact paths, push main normally
and verify fresh live main. No closure-document edit, amend, force push,
identity recomputation or unrelated staging. Codex independently verifies
publication before any next-slice planning, then checks current context usage.

## Selected slice: HIST-INPC-01 complete bounded input references and selected content

**Decision:** implement accepted
[ADR-0053](../decisions/0053-inspect-the-content-of-recorded-inputs-of-a-selected-agent-attempt.md)
as one read-only vertical inspection. Codex plans/reviews; Claude executes in
one executor chat and keeps every correction there. Selection authorized
implementation only; the later review GO above is the publication decision.

**Code and roadmap basis:** `SelectedAttemptRecordedInputs` renders plain
references with no action, and `GetAgentAttemptInputsQueryHandler` proves up
to 64 inputs but always returns at most ten. `CollaborationHistoryRows` already
guards/adjudicates summary and closed fields for history and reply reads;
the standalone history browser still requires searching unrelated pages.
The existing historical-attempt-inputs journey provides real claim inputs and
an older run after a newer objective. This advances Increment 4's visible
collaboration and Increment 5's historical inspection, not full replay or
completion of either increment.

**Sizing:** accessible references plus selected content are one outcome. An
optional limit alone leaves the body inaccessible; a body-only endpoint leaves
admitted references after ten unreachable in this view. Group the limit,
complete bounded browser, one content query and ownership/tests. Full reply/
producing-attempt/artifact graph navigation needs a separate traversal contract;
automatic coordination needs authority/recovery contracts; provider gaps and
GitHub execution remain constrained. Do not use them to broaden this slice.

**Resolved contract:** preserve the existing metadata operation's default-ten
response and empty/unavailable rules; add optional canonical `limit` 1..64
(missing/single blank defaults to ten, invalid/repeated rejected by shared
validation in API/Application). The entire 65-read/64-inspection proof still
precedes every prefix. Selected-attempt UI requests 64 once per opening,
initially shows ten and manually reveals further local chunks of ten, without
another HTTP request or body prefetch. Host omission and local disclosure are
distinct; a 65-input set remains unavailable without partial data.

New bodyless protected GET
`/api/runs/{runId}/agent-attempts/{attemptId}/inputs/{messageId}` through
`GetAgentAttemptInputMessageQuery` and one MVC class re-proves the entire input
set before selecting that exact member's guarded content in that run. Same
fixed `agent_attempt_input_message.not_found` for unknown run/missing/foreign/
non-Agent attempt and a nonmember of a valid set; root 200 Unavailable for an
unprovable/malformed/oversized set. Reuse unchanged ADR-0035/0036 content
admission; no other input's text is selected. A mismatch between the admitted
reference and reread message header yields root Unavailable. Its closed root
and message/input graphs, content-unavailable distinction, Int64 strings and
raw UTC round-trip strings are fixed by ADR-0053; no raw JSON or chain pointers.

**Frontend:** one explicit Inspect input action per displayed reference,
one replaceable content inspector, explicit Close/failure Retry, safe fixed
states and literal fields in the existing schema order. Validate raw JSON
whole against selected attempt/reference and strict new shapes before any
display; make metadata counts request-limit aware without Date normalization.
Attempt, opening, every input visit and local disclosure have owned lifetimes,
including queued changes, retained callbacks, A-B-A, same-batch Close/reselect,
reload and unmount. Closing then reselecting starts fresh; selecting the current
input again is a no-op. Existing shared lifetime primitives stay unchanged.
No body prefetch, polling, auto retry, chain navigation, storage, links or export.

**Scope:** existing GetAgentAttemptInputs query/API/mapping and its raw client,
one new input-message query/API with owned DTOs, narrowly extracted feature-owned
input-set policy, generated client, selected-input decoder/hooks/view and
feature tests. Reuse unchanged ScalarAgentAttemptIdentity and
CollaborationHistoryRows; no broad move of history infrastructure or handler
composition. Existing history/reply and message-linked input behavior is
unchanged. Executor updates inspection/cockpit documentation and current-work;
planner ADR/index/handoff remain in the diff and are not executor-owned.
No Domain, persistence, migration, claim/dispatch/result, source/snapshot,
artifact-store/capture, recipe, provider, dependency or shared harness/cache
change. Challenge a concrete scope conflict before dependent work.

**Acceptance and final validation:**
- File-backed SQLite: default-ten equivalence, limits 1/10/64 and invalid
  direct requests; sets 0/1/10/11/64/65; last-member corruption, unique exact
  ownership despite newer runs/messages; no content projection before whole
  membership admission, bounded indexed scalar queries and no writes/external
  effects. New content case matrix covers recognized/unrecognized participants,
  provenance, all existing content schemas/refusals, secret/oversized/NUL-hidden
  storage, exact Int64 >2^53/time text, header-reread disagreement and normal
  faults/cancellation. Do not weaken the existing content policy.
- API: new endpoint plus extended metadata endpoint/Int64 cases, launch-auth
  inventory, fixed 404/shared 400/status/wire/OpenAPI and generated-client
  reproducibility. Representative transport cases, not every policy permutation.
- Frontend: raw transport and whole decoders; local 11/64-reference disclosure;
  no prefetch; per-input/status/participant/content tuples; opening/visit/queued
  close/reselect and retained-callback isolation in hooks, composition and
  rendered panel. Regress existing inputs, attempt/history and verification
  composition; lint/build and one final whole-frontend run for changed factories
  and shared selected-input composition.
- Extend only `historical-attempt-inputs.journey.ts`: after the newer objective,
  use actual recorded older references, explicitly inspect two different inputs
  and compare the safe body/header to the same stored rows; verify absence of
  automatic content reads, mutations, artifact reads and new invocations.
  Keep the empty no-message Planner path. Update expected explicit limit=64
  requests without weakening its oracle. Strict e2e typecheck and stable host
  build, one serialized journey; large/corrupt sets stay below the browser.
- Final Application filters cover GetAgentAttemptInputs and the new
  GetAgentAttemptInputMessage. Final API filters cover the metadata endpoint/
  Int64 cases, new content endpoint and launch-auth inventory. If shared
  content projection/admission is moved or changed, also rerun its
  GetCollaborationHistory/GetCollaborationHistoryReply consumers and transport;
  otherwise retain their existing evidence with its actual scope. Rerun
  GetCollaborationHistoryAttempt/GetCollaborationMessageEvidence and other
  ADR-0037/0051 consumers if shared identity or their inputs change; otherwise
  retain the prior 183-test evidence. Architecture, changed-document links,
  whitespace and sensitive-data hygiene remain fresh. Retain unchanged store/
  verification-handler proofs and other journeys unless their inputs change;
  broaden for failures or concrete uncertainty. No new shared SQLite snapshots
  or mutation campaign.

Run focused checks during development, then the proportional final matrix once
on stable inputs. Retain exact commands/outcomes/timings and original failures
in one concise executor evidence log; distinguish fresh/retained/not-run results.
Return unstaged, uncommitted and unpushed, with a short report. No next-slice
selection or publication before explicit Codex GO for the final reviewed diff.

**Continuity:** CONTINUE_THIS_CHAT for Codex. Current local token telemetry
observed 158255/475000 tokens (33.3%), source and timestamp retained in
`logs/planner-review/hist-ver-01/context-telemetry.json`; this is context, not
account quota. The greater-than-60% transition threshold was not met at that
check. Claude's HIST-VER-01 chat has completed publication; start one new
Claude executor chat for this newly selected slice, with no concurrent work.


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
  tests are authorized only for HIST-INPC-01 under ADR-0053. No dependency, experiment,
  capture-limit or standards edits; the old documentation-only production/test
  exclusion is superseded, not the other restrictions.

All six frozen snapshots ([registry](development-workflow.md#historical-snapshots))
remain untouched.
