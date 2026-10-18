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
fresh live main `26c3f24a5165f9735b6fe50ee6cfc112a70b88bc`; divergence 0/0;
empty index, clean tree and no nonignored untracked paths. Claude has now returned
HIST-VER-01 correction round 1 for review. Final review preflight independently
confirms the same HEAD, local origin/main and fresh live main, divergence 0/0,
empty index, 12 modified paths and 62 new/untracked files (74 paths), including
the planner selection documents and owner-requested workflow instructions.
Publication GO is bounded by the fixed content identity below; publication
and independent publication verification have not happened yet. The
committed execution checkpoint describes its preparation phase, not pending
HIST-INP-01 publication; do not reopen or republish it.

WF-05 baseline and GO: this file at `e55709f`; earlier:
[priority](planner-handoff-history-e614c79.md#priority-and-verified-baseline),
[WF-04 GO and review](planner-handoff-history-e614c79.md#selected-slice-wf-04-publication-go).

## Latest verified publication

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

## HIST-VER-01 review: GO after correction round 1

Codex accepts the corrected substantive diff and authorizes Claude to publish
only its fixed reviewed identity in the same HIST-VER-01 executor chat. This
supersedes the initial NO-GO; no other slice is selected or dispatched. The
exact tree, expected sole parent and 74 approved paths are fixed outside the
tracked tree in `logs/planner-review/hist-ver-01/publication-go.json` and the
complete publication prompt. A mismatch stops publication, never broadens GO.

The owner's subsequent workflow amendment is included in the pending diff:
every completed planner analysis returns a prompt; after verified publication,
current context usage above 60% requires a new Codex planning chat. Claude's
critical discussion authority and short report after each completed action
are explicit in AGENTS.md, the workflow and CLAUDE.md. Codex made those edits
at the owner's request; the executor's reported discrepancy is reconciled.
They are reviewed and included in this GO, not executor scope expansion.

- **HVR-R1 (P2), resolved: valid negative Int32 exit code rejected.** In
  `RecordedVerificationProjection.ProjectExecution`, the original same-typed
  modulus guard special-cased Int32 minimum and maximum but omitted `-2147483647`.
  Its remainder modulo `2147483647` is zero, so a coherent failed diagnosis
  became Unavailable, including otherwise readable members and windows.
  An independent actual-handler/file-backed-SQLite sweep reproduced this
  while neighboring values and both extremes remained Recorded. The guard
  now compares all three exceptional values directly with same-typed Int32
  operands; the rest retain their own-remainder check. File-backed SQLite
  regressions cover the value, extremes/neighbors and REAL/TEXT/BLOB/out-of-range
  refusals, including the last of 32 members and no store call on refusal.
  Both handlers and the HTTP number/window representation are covered. The
  wider-modulus EF CAST defect is not restored.
- **HVR-R2 (P2), resolved: member/stream visit ownership incomplete.** The metadata
  hook's original `clearSelection` belonged to the opening, not the selected visit.
  Retaining A's Hide callback, selecting B, then invoking it cleared B; this
  also happened within one React batch and after A-B-A. A composed two-hook
  reproduction additionally loaded A, hid and reselected that same tuple in
  one batch: old text/cursor survived because the window key had no visit
  identity. The corrected choice frame numbers visits against queued state;
  Hide clears only its own visit and the window key includes that visit.
  Selecting the already selected tuple preserves its visit; Hide/reselect
  creates a fresh one even in one batch. Hook/composed/rendered regressions
  prove fresh idle/offset-zero state, no automatic read and inert earlier
  callbacks/results. Shared lifetime primitives and byte budgets are unchanged.
- **HVR-R3 (P3), resolved: top-level test types share files.**
  `SeededVerificationMember`, `SeededVerificationScene` and
  `SeededVerificationLineage` now have matching feature-owned files, with
  unchanged declarations/visibility and no seed or shared-fixture redesign.

Initial independent checks passed: 122 Application handler tests, 50 API tests
(the two endpoint classes plus launch-auth inventory), and 301 frontend tests
(five new files and the existing attempt panel/history composition). Those
passes did not cover R1/R2. The separate diagnostic numeric sweep exposed R1;
that round's final independent lifetime reproduction had four failing scenarios
and one passing current-Hide control. Temporary reproduction tests were removed
from the source tree; originals and full outputs remain under ignored
`logs/planner-review/hist-ver-01/` (`numeric-reproduction.log`,
`lifetime-reproduction-02.log`, `independent-*`). Commands are retained beside
the outputs. No product fix, stage, commit or push was performed by review.

Executor challenges resolved: a rooted path remains Unavailable before a store
call, as ADR-0052 requires a relative path. The journey's added existing
HIST-ATT-01 button is an accepted stale-expectation repair. The first test-build
TypeScript failure output is missing, as reported; accept the evidence loss
as a limitation, never reconstruct it or relabel later passes as its original
output. The three planner-document delivery hashes and the generated-client
hash were independently matched before this review edit. Reported full-suite,
architecture and browser results retain their actual scopes; the browser
oracle control is an assertion-sensitivity check, not a production mutation.

Correction review independently passed 150 Application tests, 58 API tests
(both endpoints plus launch-auth inventory), 317 frontend tests across the
same seven affected/regression files, and the unchanged five-case planner
reproduction that previously had four failures. Its temporary source copy was
removed. Complete commands/results: `logs/planner-review/hist-ver-01/correction-01/`.
Fresh live main also matches the expected parent. ADR/index and generated
client hashes still match their reviewed identities.

Executor final evidence inspected, not independently rerun in this round:
161 architecture tests, 4852 full-frontend tests across 243 files (one run),
lint with nine unchanged-file warnings, frontend/API/host builds, generated
client reproducibility, strict e2e typecheck and the serialized collaboration
journey. Retained pre-fix controls fail on the targeted defects and fixed
regressions pass; this is inspected evidence, not historical authentication.
The journey's 154 duplicate-key and 14 negotiation console errors begin before
the new section is used; no HEAD browser comparison or comparable first-delivery
console capture establishes their cause or whether their counts changed.
Accept this unresolved diagnostic limit without calling it proved pre-existing.
The missing first TypeScript failure output remains lost. Unchanged identity,
store containment and other journeys retain their recorded scope and limits.

GO includes the 12 modified and 62 added reviewed paths, including the owner's
three workflow instruction files, the planner ADR/index/handoff and the
executor's current-work record. No product retest is required for the final
planner-only decision edit. Claude must verify the fixed tree and exact paths,
commit with the expected sole parent, push main normally and verify the live
ref. A changed substantive diff, divergent remote or failed push stops the
flow. No amendments, force push, unrelated staging or closure-document edits.
Codex will independently verify publication before any next-slice planning
and then check current context usage under the owner's greater-than-60% rule.

## Selected slice: HIST-VER-01 historical verification membership and sealed output

**Decision:** implement accepted
[ADR-0052](../decisions/0052-inspect-recorded-verification-evidence-of-a-selected-agent-attempt.md)
as one vertical inspection: the selected CodeReviewer attempt's exact recorded
verification membership, its historical execution metadata and manual sealed
stdout/stderr windows. Include ordinary ImplementationReview and failed
VerificationDiagnosis, including attempts without an output message. The owner
explicitly clarified that Claude is the executor for HIST-VER-01; Codex remains
the planner/reviewer. This supersedes the earlier Codex executor assignment
only, without changing scope, acceptance or publication authority.

**Evidence and roadmap:** `AttemptVerificationEvidence` persists ordered
command/execution pairs at both claim paths. `AgentAttemptEvidenceBody` shows
neither this set nor its streams. `GetProjectVerificationExecutions` returns
only the latest 20 project executions; `useVerificationExecutionOutput` drains
windows on a timer. Neither is suitable for detached historical inspection.
The local receipt names only passing delivery members. Existing deterministic
`collaboration.journey.ts` already produces failed diagnosis and passing review
members, completes delivery, records a newer objective and opens the prior run.
This advances Increment 4's verification inputs and Increment 5's historical
evidence access under ADR-0003/0009/0018/0033/0051, not complete replay or either
increment. ADR-0032's receipt and all authority rules remain unchanged.

**Sizing and alternatives:** membership, exact historical ownership, truthful
stream metadata and manually bounded bytes are one useful evidence outcome;
shipping only IDs leaves the verification's evidence inaccessible. Include both
review and diagnosis, the two streams, missing/recovered capture and lifetime
refusals together. Reusing current-project readers would substitute latest
records and drain output; broadening the receipt would miss failed diagnoses
and couple historical access to completed delivery. Autonomous coordination
requires a separate ADR-0014 authority/dispatch/recovery decision, and remote
publication is constrained by Proposed ADR-0050/P9; neither is a dependency or
part of this slice. Session, account and compaction contracts remain unproven.

**Resolved read contract:** two bodyless protected GETs, API-owned graphs:
`.../agent-attempts/{attemptId}/verification-evidence` and
`.../verification-evidence/{executionId}/artifact-window/{stream}`, under the
exact run. ADR-0052 fixes fields, statuses, safe errors and bounds. Read at most
33 ordered scalar memberships, inspect at most 32, show all only after proving
the entire set and exact historical project/workspace/checkpoint/fingerprint
ownership. Zero/oversized/unprovable/unsupported sets are Unavailable, never an
invented Empty or partial set. Read execution snapshot names, not current
recipes; classify stored enums and prevent SQLite numeric conversion from
hiding damaged values. Reuse the unchanged scalar attempt identity evaluation.
No full entity/argument/session/source materialization, raw SQL in Application,
latest/current eligibility or invocation. New shared read policy, only where
the two operations deliberately co-evolve, belongs in an explicit Policies
subfolder; do not refactor existing feature-root types.

**Sealed windows:** at most the two allowlisted descriptors per member;
NotRecorded/Unavailable/Recorded with exact capture/truncation tuples. List
reads no paths, hashes or bytes. A window re-proves membership, resolves only
that execution/stream, then delegates unchanged to
`IArtifactStore.VerifyAndReadSealedAsync`; path/hash remain guarded and internal.
No partial file, capture change, other stream bytes or source read. Default
16 KiB, reject invalid cap/offset rather than clamp; cap 64..65536, safe integer
cursors and lengths. Preserve Missing versus IntegrityMismatch and truthful
unknown truncation. This reads membership and referenced rows as stored now;
it does not recompute the diagnosis snapshot or prove which bytes the Agent
saw, current eligibility, approval or success. Do not expose snapshot digests.

**Frontend:** collapsed selected-attempt section, independent of evidence,
collaboration inputs and Agent artifacts. Metadata open/read once; explicit
member/stream selection, Load, Load next and failure-only retry. No polling or
automatic paging. Maximum retained 256 KiB of source bytes per member/stream
selection, with an honest local-view limit before exceeding it. Whole-answer
raw validation binds exact requested IDs, immutable header, every member and
stream tuple, timestamp text and stable artifact/cursor facts. Reuse the proven
instant parser and sealed-window acceptance invariants without changing their
existing consumers. Owned lifetimes cover selection, opening and member/stream;
replacement's first frame, A-B-A, retained callbacks, overlap, reload and unmount
must isolate all results. Literal text only; no browser/URL persistence, logs,
download, export or mutation. An unavailable stream leaves valid siblings and
membership readable. Malformed membership shows none of that answer.

**Scope:** the two new query/endpoint folders, necessary feature-owned read
policy, API mapping/generated client and operation-specific raw facades,
frontend decoder/hooks/view, focused tests and the existing collaboration
journey extension. Update inspection/cockpit copy and current-work at delivery.
Keep planner selection/ADR/index changes in the review diff. No dependency,
migration, persistence, claim/dispatch/result, snapshot/digest, recipe,
artifact-store/capture, existing reader or test-harness change; no new shared
SQLite snapshot reuse. Stop and challenge if this contract cannot be proved
without one of those changes; do not substitute another source or widen it.

**Validation matrix:**
- Application: real file-backed SQLite tests for both new handlers, 1/32/33,
  malformed last member/positions/IDs/storage, exact ownership despite newer
  runs/checkpoints/executions and recipe edits, review/diagnosis terminal shapes,
  bounded indexed scalar SQL, no writes/files in list and exact admitted row
  passed to sealed verification in windows. Fault/cancellation propagation;
  guard unrecognized enum, huge/NUL-hidden strings and numeric coercion.
- API: both endpoint classes' complete launch-auth/404/validation/status/wire/
  OpenAPI matrices plus launch-auth inventory; generated client from the normal
  API build, reproducibility and owned response graph. Verify member/stream
  substitution starts no artifact read. Old verification output endpoint stays
  unchanged; inspect its diff and retain its existing evidence unless affected.
- Artifact boundary: feature tests with the real store over disposable owned
  files prove correct delegation, verified empty/missing/tampered text and
  multiwindow UTF-8. Retain existing unchanged store containment proofs; no
  new native/profile/provider experiment. Broaden only if the store changes,
  which first requires planner scope review.
- Frontend: new raw-wire/decoder/hook/composition tests; both capture outcomes,
  false/true/null truncation, malformed counts/times/descriptors, hostile text,
  paging/EOF/cap and every lifetime replacement. Existing attempt panel,
  historical run composition, inputs, execution facts and artifact-window
  regressions. Lint/build, including product TypeScript. The new factories and
  shared composition justify one final whole-frontend run, not repeated passes.
- Browser: extend `collaboration.journey.ts` after its newer objective to inspect
  the prior diagnosis's failed members and ordinary review's passing members,
  compare metadata to SQLite and requested stdout/stderr to sealed fixture
  bytes, with no extra stage, non-GET, automatic draining or source mutation.
  No fabricated lineage or new shared harness. Strict e2e typecheck, stable host
  build, serialize the journey. Keep corrupt/oversized cases below the browser.
- Architecture suite and changed-document links, whitespace and sensitive-data
  hygiene. Preserve complete failure output, commands and elapsed times.

Focused new test classes should be named after each query handler and endpoint;
run their filters first. Reuse unchanged prior evidence with its actual scope.
Rerun ADR-0037/0051 consumers if shared identity changes (otherwise retain their
183-test review evidence). Broaden only for changed shared behavior, a failure
or a concrete unresolved concern; no routine whole backend/API matrix or
mutation campaign. Run one final proportional matrix on stable inputs.

**Continuity:** `CONTINUE_THIS_CHAT` for the planner: constraints and evidence
remain available, with no observed or owner-reported context percentage. Start
one new Claude executor chat for HIST-VER-01 and keep its corrections there.
The prior publication executor has completed; no concurrent implementation is
dispatched. Return unstaged/uncommitted/unpushed for review. Selection grants
no commit or push; all standing and retained constraints below remain.

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
  tests are authorized only for HIST-VER-01 under ADR-0052. No dependency, experiment,
  capture-limit or standards edits; the old documentation-only production/test
  exclusion is superseded, not the other restrictions.

All six frozen snapshots ([registry](development-workflow.md#historical-snapshots))
remain untouched.
