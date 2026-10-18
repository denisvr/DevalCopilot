# Planner/reviewer handoff

[AGENTS.md](../../AGENTS.md) and [workflow](development-workflow.md) apply.
This is current planning state; historical GO and pending wording in snapshots
is evidence, never renewed authority. Git and code prevail over summaries.

## Priority and verified baseline

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
explicitly resumed functional roadmap development, superseding the
documentation-first pause and its runtime/test exclusions, without renewing
experimental authority. Codex plans/reviews; Claude implements.

Selection baseline independently checked: `main`; HEAD, local origin/main
and fresh live main `5e588a44b5ebc0e633cf1f2e1dbb3af442515a2b`; divergence
0/0; empty index; clean tree and no nonignored untracked paths. ART-WIN-01
is published and independently verified. Checkpoints describe their prepared
phases; do not reopen or republish it. After this selection only this handoff
is modified, unstaged, with HEAD unchanged. No implementation or publication
GO was granted at selection; HIST-MET-01 is now reviewed below after correction
2. HEAD is unchanged, the real index is empty and the reviewed diff has ten
modified tracked paths and six new sources/tests. Publication is pending under
the exact GO identity; no next slice is selected.

WF-05 baseline and GO: this file at `e55709f`; earlier:
[priority](planner-handoff-history-e614c79.md#priority-and-verified-baseline),
[WF-04 GO and review](planner-handoff-history-e614c79.md#selected-slice-wf-04-publication-go).

## Latest verified publication

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

## Selected slice: HIST-MET-01 historical attempt execution and usage facts

**Decision and evidence:** show the selected Agent attempt's existing recorded
process and token evidence beside its semantic result, with its response
contract and recorded dispatch time. `GetAgentAttemptEvidence` already returns
`processExecution` and `tokenUsage` through the Domain's fail-closed getters;
`AgentAttemptEvidenceBody` currently omits both, unlike the latest-attempt view.
The run-level inspector serves both the current rail and a selected historical
run, including attempts with no collaboration message. No new read or backend
capability is needed.

**Roadmap and size:** one complete selected-attempt fact view in Increment 4's
execution evidence and Increment 5's historical access, not complete replay or
increment completion. Include process, configured timeout, token/cache counts,
truthful absence, numeric/date admission and composed/browser proof together.
Do not split known values from absence or safety cases. Automatic advancement
would require its own recorded decision about ADR-0014's explicit manual stage
authority and durable dispatch/recovery; it is not inferred from the roadmap.
P9/remote publication and unproven provider contracts are not dependencies.

**Scope:** feature-local selected-attempt fact presentation and normalization,
`agentAttemptHistory`/evidence admission only where needed, the existing panel,
focused tests and the two existing artifact/history journeys. Reuse
`ProcessEvidenceLine`/`TokenUsageLine` and their established wording where
suitable, admitting safe facts before passing them to shared presentation.
Do not broaden the latest-attempt, status-action or run-summary behavior;
shared presentation changes require evidence of necessity and direct-consumer
regressions. No backend/client/schema/dependency or provider-parser change.
Contracts: [execution evidence](../architecture/agent-collaboration-execution-evidence.md),
[usage evidence](../architecture/agent-collaboration-usage-evidence.md),
[inspection](../architecture/agent-collaboration-evidence-inspection.md), ADR-0023
and ADR-0033. Domain rules and API projection prevail over UI assumptions.

**Acceptance:** display the selected response contract and valid recorded UTC
dispatch/claim/completion facts; validate newly used optional timestamps before
display. Separate semantic result, host-measured process outcome/exit/duration,
configured timeout and provider-reported token/cache counts. A clean exit does
not assert semantic success. No aggregate, cost, quota, capacity or readiness
claim. Still-running or undispatched attempts expose no measured result or
known usage even if their answer contains plausible values. Preserve terminal
unknown/legacy/interrupted evidence as unknown, never zero; valid zero duration
and zero counts remain known. Configured timeout is independent and may be
shown when measured evidence is absent.

Admit only the actual wire ranges: nonnegative safe-integer whole-millisecond
duration, positive safe-integer configured timeout, int32 exit code only for
Exited, and nonnegative int32 input/output/cache counts. Null/missing usage is
unknown, not a zero default; caches may be independently absent under the
provider contract. Negative/fractional/nonfinite/unsafe/wrong-type/contradictory
optional evidence is unavailable in that section, never clamped or invented;
valid independent metadata and artifact access stay usable. Use the existing
host trust boundaries; do not invent provider-schema proof from browser fields.
Preserve exact selected ID/number/role/provider/contract binding, replacement
lifetimes, literal rendering and no extra requests, logging, storage or mutation.

**Validation:** pure optional-fact normalization cases plus the real composed
panel: exit zero with failed semantic result; nonzero exit, timeout/cancellation;
known/unknown/zero process and token facts; independent cache absence; invalid
numbers/dates; running/undispatched injection; and A-to-B replacement with
contrasting facts. Retain artifact paging and ownership regressions. Frontend
lint/build, focused changed-file filters and the relevant existing process/token
fact/attempt-display tests; broaden only if shared consumers change or failures
make impact uncertain. Extend the authenticated sealed-artifact journey to
inspect the planning attempt's recorded facts from Run history after a newer
objective, using existing production commands and deterministic fixtures.
Extend the historical-agent-attempts journey to assert no measured/known usage
for its stopped-before-dispatch attempt. Run serially; strict e2e typecheck.
Compare values with real recorded evidence; no SQL lineage fabrication. Inspecting
facts adds no provider call, mutation or artifact-window read before requested.
Retain unchanged architecture evidence; no routine backend/API suite or client
generation. Update current-work and the relevant inspection/cockpit copy only
as necessary. Preserve full original check/failure output with commands/timings.

**Prior limits:** HATT-R1 remains resolved; ART-WIN-01 is accepted/published.
Earlier missing output, limited browser coverage, duplicate React keys,
compiler/runner failures, unexplained 409/500 and all P9 restrictions remain
as recorded. No new experiment, profile, credential or real-provider authority.

**Continuity:** `CONTINUE_THIS_CHAT` for Codex: current constraints/evidence
remain available; no observed or owner-reported usage percentage. Start one new
Claude chat for HIST-MET-01 after verified ART-WIN-01 publication, keeping its
corrections there. Return unstaged/uncommitted/unpushed; no commit/push GO.

## HIST-MET-01 review: GO after correction 2

Codex independently reviewed the final facts presentation, raw-wire facade,
factory wiring, composed tests, journey changes and executor evidence. GO
includes these planner review edits and the factual correction in current-work.
Expected sole parent, tree and exact 16 approved paths are supplied in the
publication prompt and `logs/planner-review/hist-met-01/publication-go.log`.
Publication and independent verification remain pending. Keep publication in
the same Claude chat; no next slice is selected.

- **HMT-R1 (P2), resolved:** the first delivery trusted Date objects produced
  by permissive generated conversion: raw dispatch `1` became 2001-01-01 and
  admitted known process/usage. Correction 1 judged raw fields but still let
  `{"toString":0}` reach generated conversion, throwing and hiding valid
  sibling metadata/artifacts. Both reproductions remain under
  `logs/planner-review/hist-met-01/`. Correction 2 removes the three root
  timestamp members during generated JSON parsing through the protected reviver
  seam, then applies the raw absent/valid/invalid verdicts. Invalid optional
  timestamps never reach generated conversion; invalid claim still rejects the
  answer, and invalid dispatch withholds measured process/known usage while
  independent metadata and artifact access remain usable. The calendar/offset
  parser, exact identity binding, URL/auth/non-200 handling and single-request
  behavior remain intact. No generated source or backend change.
- **Independent checks:** six affected wire/factory/facts/panel/historical/hook
  files pass all 247 tests; complete output in
  `logs/planner-review/hist-met-01/correction2-focused.log`. The tests cover
  non-callable toString objects and nested arrays, numeric/falsy/calendar
  coercion, valid formats and absence, malformed claim rejection, invalid
  optional timestamps with usable siblings, preserved Infinity and nested
  conversion, status/auth/errors and composed artifact access. Final newline,
  CR and Unicode separator probes in correction 1 were rejected; no format
  finding. Diff hygiene passes; fresh live main, HEAD and local origin/main
  remain `5e588a44`; index empty. No planner commit or push.
- **Executor evidence:** correction 2 d03/d04 pass 247/316 tests (overlapping),
  lint/build and both authenticated journeys pass. The no-reviver control has
  19 assertion/runtime failures from the intended generated conversion across
  20 selected tests, no compilation kill, and records an equal restored hash;
  inspected, not independently rerun. Correction 1's 4471-test full-suite pass
  is retained as prior-input evidence, not a final full-suite rerun; its factory
  wiring is unchanged and correction 2 directly covers the modified operation.
  Strict e2e evidence and host build are retained for unchanged inputs.
  Preserve r07/r09/r19/r23/c13 failures, previous evidence loss and limitations.
- **Executor challenges and scope:** accept the operation-specific handwritten
  facade and original-text reviver; reserialization is not used, so Infinity
  is not silently turned into null. Known-token browser proof is absent because
  the fixture reports unknown usage; pure/composed known-count proof plus the
  real unknown-usage journey is proportional. Existing duplicate semantic
  summary is accepted for compatibility. StrictMode causation is now explicitly
  an inference; no on/off control or shared lifetime change is claimed.
- **Remaining parsing limits:** unchecked nested timestamps and other operations
  are outside these three root timestamp fields. A malformed nested retrieval
  timestamp can still fail the whole read; do not extend this slice to it.
  The executor's claim that generated code loops over non-array artifacts was
  inaccurate: direct inspection found an Array.isArray guard, followed by the
  existing evidence decoder's whole-answer refusal when the list is absent.
  Codex corrected that mechanism in current-work without changing behavior.
  These existing failure paths are not evidence of newly resolved general
  schema validation. Browser proof covers known process and unknown/undispatched
  usage, not known token counts or every malformed wire permutation. Duplicate
  React keys and compiler locks remain unexplained; all P9 restrictions stand.

No further correction or routine retest is required for this reviewed diff.
Material post-GO changes return for review. The GO authorizes only our exact
reviewed development publication, never product remote-publication capability.

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
  [current-work](current-work.md#open-risks). Production frontend and necessary
  tests are authorized only for HIST-MET-01. No dependency, experiment,
  capture-limit or standards edits; the old documentation-only production/test
  exclusion is superseded, not the other restrictions.

All six frozen snapshots ([registry](development-workflow.md#historical-snapshots))
remain untouched.
