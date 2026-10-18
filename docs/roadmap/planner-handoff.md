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
and fresh live main `75372b18d3d669f744010a3b9a628a408acaed4c`; divergence
0/0; empty index; clean tracked tree and no nonignored untracked paths.
HIST-ATT-01 is published and independently verified. The committed checkpoints
record earlier phases; do not reopen or republish it. ART-WIN-01 is now delivered
and reviewed below: HEAD unchanged, empty index, 14 modified tracked paths and
five new frontend paths, all unstaged/uncommitted/unpushed. Publication is
pending under the exact GO identity; no next slice is selected.

WF-05 baseline and GO: this file at `e55709f`; earlier:
[priority](planner-handoff-history-e614c79.md#priority-and-verified-baseline),
[WF-04 GO and review](planner-handoff-history-e614c79.md#selected-slice-wf-04-publication-go).

## Latest verified publication

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

## Selected slice: ART-WIN-01 reliable sealed text inspection from collaboration

**Decision and evidence:** restore the message-linked artifact text path and
make window acceptance coherent across both existing readers. The message
hook currently sends recorded names such as `AgentStandardError`, but its
endpoint accepts `stderr` (and the other three closed route segments), so
legitimate reads fail with 404. `availableArtifactPurposes` uses `in`, admitting
prototype keys; `useSealedArtifactWindow` defaults missing success cursors and
accepts backward/nonprogressing or unsafe values. These are concrete functional
inspection defects retained by HIST-ATT-01, not an authorization gap for P9.

**Roadmap and size:** one outcome in Increment 4's evidence inspection and
Increment 5's historical access: the user can manually inspect a message's
sealed artifact text, with truthful paging in both routes. Include the route
repair, own-property purpose allowlist, shared window decoder, both consumers'
regressions and authenticated browser proof as one safe reviewable unit.
No new backend route, schema, artifact type or provider capability; no complete
replay or completion of Increments 4/5/6. Autonomous stages and remote
publication remain independently bounded and are not selected.

**Scope:** feature-owned `artifactPurposes`, both sealed-window route hooks,
shared window state/decoding and affected viewer tests; minimal viewer changes
only if necessary for this outcome. Reuse existing generated clients and owned
lifetimes. A small shared mapping of the same four purposes/segments is allowed
because both readers intentionally change together; no generic artifact or
history framework. Contract: the
[inspection views](../architecture/agent-collaboration-evidence-inspection.md);
compare the existing MVC endpoints and filesystem read semantics before editing.
Backend implementation, generated clients, persistence and dependencies remain
unchanged. Update the inspection contract, relevant cockpit copy and execution
checkpoint only as necessary; preserve prior evidence and open risks.

**Acceptance:** only the four own allowlisted recorded purposes present in the
attempt's metadata are offered; unknown/prototype names start no request.
Both route hooks send their exact existing closed segments. Accept an Ok window
only whole: text is a string, truncation is boolean or null, cursors/total are
nonnegative safe integers; requested offset <= next <= total, delta <= requested
byte cap; progress is positive while data remains (an empty file is valid).
A sealed total stays identical across accepted pages. Reject absent, fractional,
unsafe, negative, backward, nonprogressing, overshooting or contradictory fields
before appending text or advancing the cursor. Preserve validated earlier text
internally and retry the same failed offset, never display rejected text or use
its cursor. Do not infer cursor bytes from JavaScript string length or require
TextEncoder equality: the host decodes malformed UTF-8 with replacement, and
UTF-8 cursor/EOF semantics must remain compatible. Closed non-Ok statuses never
show text; preserve their distinct disclosures without inventing an incompatible
backend envelope rule. No auto paging/retry, logging, storage, mutation or live
tail; keep sensitivity caveats, literal rendering and lifetime isolation.

**Validation:** pure purpose/window-decoder permutations, both route-hook and
viewer regressions, history composition and direct shared consumers; frontend
lint/build and strict journey typecheck as applicable. One focused authenticated
`sealed-artifact-inspection.journey.ts` must create a provider-observed message
through existing deterministic-provider commands, open its evidence in the
production UI and compare loaded text with its actual sealed artifact. Run the
existing historical-agent-attempts journey serially as the other route's
regression. No raw-SQL lineage fabrication. Prove inspection starts no provider
invocation or workflow/Git mutation. Retain unchanged architecture evidence
unless inputs change. Broaden for shared ownership/harness changes, failures or
uncertain impact; no routine full backend/API suite or client generation.
Preserve complete original check/failure output with commands/timings in one
concise indexed evidence directory; do not reconstruct earlier losses.

**Prior review and evidence:** HATT-R1 remains resolved. The accepted missing
original output for HIST-ATT-01 runs 02/13, unretained interim pass and Codex's
first ENOENT runner output remain limited as recorded in this file at `75372b1`
and current-work. Duplicate React keys and compiler/runner failures stay
unexplained. The three artifact defects above are now selected work, not fixed
in anticipation. No prior gate or evidence is retroactively strengthened.

**Continuity:** `CONTINUE_THIS_CHAT` for Codex: constraints and review evidence
are available, no observed or owner-reported context percentage. Start one new
Claude chat for ART-WIN-01 after the independently verified HIST-ATT-01
publication; keep all its corrections there. The publication decision follows.

## ART-WIN-01 review: GO

Codex reviewed the complete substantive diff and executor evidence; no blocking
finding. GO includes this planner review edit. The exact tree, expected sole
parent and 19 approved paths are supplied in the publication prompt and
`logs/planner-review/art-win-01/publication-go.log`; publication and independent
verification remain pending. Publish in the same Claude chat. No next slice.

- **Independent verification:** all 179 tests in eight affected decoder,
  purpose, hook, viewer and composed test files pass; original output retained
  in `logs/planner-review/art-win-01/focused-01.log`. Both route mappings agree
  with their existing MVC endpoints; offset/replacement compatibility was
  compared with `FilesystemArtifactStore.ReadWindowAsync`/`Utf8Boundary`.
  Fresh live main, HEAD and local origin/main still equal `75372b18`; index
  empty; diff whitespace clean. Backend, generated clients and dependencies
  unchanged; no new provider or publication capability.
- **Executor evidence:** inspected r01-r18 and their failure dispositions.
  r10 passes 136; the broad r11 run has 4307 passes and two invalid-fixture
  failures, not a full-suite pass. The corrected file passes all 43 in r12;
  the later test-only type fix passes its 15 tests in r16. Passing unaffected
  suites are retained, not relabeled as a later full run. Lint/build, strict
  e2e typecheck and both authenticated journeys pass. Red-first results and
  equal restored hashes were inspected, not independently rerun. r02's bad
  invocation, r04's unexplained compiler lock and r14's corrected test type
  error remain preserved; later passes do not explain the lock.
- **Executor challenges resolved:** accept `text.length <= cursor advance`:
  each UTF-16 code unit produced by this host's UTF-8 decoder consumes at
  least one byte, including replacement fallback; this is an inequality,
  not re-encoding or byte equality. Only sealed total stability was required;
  accepting each boolean/null truncation fact without cross-page equality is
  within this slice. The same fixed error for transport failure and malformed
  windows is accepted: both keep the validated cursor/text and allow the same
  explicit retry; distinct closed non-Ok disclosures remain unchanged.
- **Scope and limits:** the empty-file disclosure and coherent fixture repairs
  are accepted. Browser proof covers four short ASCII artifacts, including an
  empty stderr; multi-window/non-ASCII/malformed behavior is tested at pure,
  hook and viewer boundaries, not claimed as browser proof. No cryptographic
  verification is added in the browser. Duplicate React keys and compiler
  locks remain unexplained; earlier missing evidence and P9 constraints stand.

No additional implementation changes or routine test reruns are required for
this diff. Material post-GO changes return for review. The publication GO is
only for our development delivery, never product remote-publication authority.

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
  tests are authorized only for ART-WIN-01. No dependency, experiment,
  capture-limit or standards edits; the old documentation-only production/test
  exclusion is superseded, not the other restrictions.

All six frozen snapshots ([registry](development-workflow.md#historical-snapshots))
remain untouched.
