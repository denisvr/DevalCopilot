# Planner/reviewer handoff

[AGENTS.md](../../AGENTS.md) and [workflow](development-workflow.md) apply.
This is current planning state; historical GO and pending wording in snapshots
is evidence, never renewed authority. Git and code prevail over summaries.

## Priority and verified baseline

WF-01–WF-06 are published and independently verified. On 2026-10-18 the owner
explicitly resumed functional roadmap development, superseding the
documentation-first pause and its runtime/test exclusions, without renewing
experimental authority. Codex plans/reviews. Claude began HIST-INP-01; the
owner explicitly transferred its execution completion to Codex when Claude's
credits ran out. The owner explicitly requested this planner chat transition.

Selection baseline independently checked: `main`; HEAD, local origin/main
and fresh live main `96a21d737777980724cc7351d8d7a2c10b0e6e2d`; divergence
0/0; empty index; clean tree and no nonignored untracked paths. HIST-MET-01
is published and independently verified. Checkpoints describe prepared phases;
do not reopen or republish it. After this selection, the handoff and decision
index were modified unstaged, and ADR-0051 was new/untracked; no runtime change
existed at selection. Review now covers the delivered implementation below:
HEAD remains the selection baseline, the real index is empty, and the diff
contains 16 modified paths, one deletion and 32 new files. Publication and its
independent verification remain pending under the exact GO identity.

WF-05 baseline and GO: this file at `e55709f`; earlier:
[priority](planner-handoff-history-e614c79.md#priority-and-verified-baseline),
[WF-04 GO and review](planner-handoff-history-e614c79.md#selected-slice-wf-04-publication-go).

## Latest verified publication

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

## Selected slice: HIST-INP-01 recorded inputs of a selected Agent attempt

**Decision:** implement accepted
[ADR-0051](../decisions/0051-inspect-recorded-inputs-of-a-selected-agent-attempt.md)
as one vertical slice: protected read query, bounded SQLite projections,
API-owned response/generated client, and an on-demand recorded-input view in
the selected attempt, including failed/interrupted attempts without an output
message. Planner owns the resolved contract; the executor implements, not
selects it.

**Evidence and roadmap:** the existing message-linked input reader starts from
an output message; `AgentAttemptEvidenceBody` has no input inspector. Durable
`AttemptInputMessage` rows already hold these references. The bounded scalar
identity approach is delivered under ADR-0037; reuse its rules, not full entity
materialization or its message actor assumptions. This advances Increment 4's
progressive-context inspection and Increment 5's historical evidence access.
It is neither complete context/replay nor increment completion. Autonomy would
need a separate recorded decision about ADR-0014's manual authority and durable
dispatch/recovery; publication remains bounded by Proposed ADR-0050/P9.

**Sizing:** query, identity/set coherence, wire/decoder, selection ownership and
one authenticated browser proof form one outcome. A metadata-only UI without
the new query cannot inspect an attempt lacking a producing message. Do not
split successful and unavailable/empty cases. Content navigation, input chains,
provider sessions, autonomous advancement and raw filesystem access are not
required and are excluded.

**Resolved boundaries:** one bodyless GET
`/api/runs/{runId}/agent-attempts/{attemptId}/inputs`; exact selected Agent
attempt only. Shared Problem Details 404 `agent_attempt_inputs.not_found` for
unknown/foreign/non-Agent sources. Closed `Unavailable`/`Empty`/`Recorded`
shapes and disclosure exactly as ADR-0051; no new persistence, mutation or
invocation. Read at most 65 input rows, inspect at most 64, display first 10
only after proving the entire inspected set; a 65th yields Unavailable without
count or partial data, not a declaration that the stored set is invalid.
SQL-classify stored enums/version and guard assignment storage as in ADR-0037;
keep Domain identity/assignment rules unchanged. Exact Int64 message sequences
are canonical strings; occurred time is an invariant UTC round-trip string,
validated before display, not a generated Date property. No summary/content,
artifact, prompt, session or chain reads. Existing routes retain behavior.

**Frontend:** collapsed input control inside the selected attempt, independent
of its facts/artifact controls. Explicit open/read/retry only; reuse owned
lifetimes and validate the whole answer against the selected immutable header.
Preserve Unknown/Unavailable versus genuine Planner Empty; source refs are
recorded inputs, never complete prompt/context/session. No page draining,
content navigation, storage, logging or mutation. New timestamp text may reuse
the proven instant parser; do not broaden the existing wire facade.

**Scope:** new GetAgentAttemptInputs query/endpoint and operation-owned models,
feature-local frontend hook/decoder/view, necessary generated client and tests.
A narrow extraction of identical scalar attempt projection/identity evaluation
is permitted with ADR-0037 equivalence regressions; its actor comparison stays
unchanged. No generic reader/repository/framework, raw SQL in Application,
new dependency, migration, claim/dispatch change or new snapshot fixture reuse.
Update the inspection/cockpit contract and current-work at delivery. Preserve
these planner-written selection/decision files in the review diff; the accepted
ADR is a contract, not already delivered functionality.

**Validation:** file-backed SQLite new handler/identity/set refusal tests,
endpoint full auth/404/status/wire matrix and MVC inventory; exact sequences
above 2^53 and omission/bounds; source query inspection proving no full text or
aggregate reads. New frontend decoder/hook/composition tests plus selected
attempt and existing recorded-input regressions. Normal API build/NSwag
reproducibility, focused project builds/analyzers, architecture tests,
frontend lint/build and strict e2e typecheck. One new authenticated
`historical-attempt-inputs.journey.ts` using existing production commands and
deterministic providers must inspect a nonempty input set after a newer
objective, compare references with stored rows, and start no invocation,
mutation or artifact-window read. Extend the existing no-message historical
Planner journey for the genuine Empty case; non-Planner empty/corrupt/oversized
cases belong to SQLite/API tests, not fabricated browser lineage. Retain other
unchanged journey evidence. Broaden for shared projection/identity changes,
failures or uncertainty, not a routine full backend/API suite. No mutation
campaign, new test infrastructure or real-provider experiment.

**Continuity at selection:** Codex recommended continuing its planner chat and
one new Claude executor chat. The owner's subsequent Codex execution recovery
and explicit planner transition supersede that chat placement, not the slice
or its authority boundaries. No observed or owner-reported context percentage.
No second slice is selected while publication remains pending.

## HIST-INP-01 review: GO

Codex independently reviewed all 49 changed/new/deleted paths against ADR-0051
and the resolved selection above, including scalar SQL, the ADR-0037 extraction,
API-owned mapping, generated output, raw-body facade, decoder, ownership,
composition, journeys, documentation and the executor's evidence. No blocking
finding remains. This GO includes the planner's final checkpoint and decision
index edits. The expected sole parent, exact approved paths and expected tree
are recorded in `logs/planner-review/hist-inp-01/publication-go.json` and the
complete publication prompt; no document embeds its own future commit SHA.

- **Independent checks:** 183 Application tests (new query, ADR-0037 and old
  input-reader regressions), 30 API tests (new/related routes and launch
  authentication), and 255 tests across nine frontend decoder, wire, hook,
  composition and existing input-view files passed. Exact commands, complete
  outputs, exits and elapsed times are in `logs/planner-review/hist-inp-01/`.
  The first Application run built successfully but aborted when testhost did
  not connect within 90 seconds; the same binaries passed outside the sandbox.
  Preserve that failure without claiming its cause. Fresh live main agrees
  with HEAD/local origin/main at `96a21d73`; index empty, divergence 0/0.
- **Review evidence:** the guarded attempt projection and assignment/identity
  evaluation are the previous operation's same expressions and rules; its
  actor comparison remains local. The new indexed input read stops at 65,
  resolves at most 64 same-run message identities and validates every inspected
  reference before returning ten. The role-aware Empty/Unavailable distinction,
  exact Int64/time strings, authentication, fixed 404, fault propagation and
  absence of writes/content/chain reads are preserved. The raw-body facade
  keeps explicit nulls and wrong types for whole-answer refusal without
  changing generated URL/auth/error processing. Existing owned lifetimes and
  serial guards isolate selections, openings, requests and retained callbacks.
- **Retained evidence and limits:** inspected executor final outputs support
  153 architecture tests, API/NSwag reproducibility, lint/build and hygiene;
  the full frontend result is 4621 tests before only the required fixture
  `traceId` correction, followed by that file's 18 tests and the fresh 255-test
  review set, not a later full-suite rerun. The strict e2e check, journey-host
  build and three authenticated deterministic-provider journeys remain
  retained evidence on unchanged functional inputs. Browser proof covers
  ordinary small nonempty sets and a stopped-before-dispatch Planner with no
  output message; corrupt/oversized sets and values above 2^53 have narrower
  SQLite/API/frontend evidence. No real-provider or P9 experiment was run.
  Initial failures, the surviving settle control and later overlap coverage,
  duplicate keys, negotiation errors and every older evidence limit remain.
- **Accepted implementation choices:** the operation-specific parsed-body
  facade is necessary because generated models erase required null/falsy
  distinctions. The malformed-position SQL guard prevents SQLite conversion
  from silently turning damaged storage into a valid position; it changes no
  write rule. These are within the resolved contract, not new architecture.

GO authorizes only the exact reviewed development commit and normal push of
`main` to `origin/main`, by a bounded Codex executor. It grants no product
publication capability. A content mismatch, remote divergence, failed push or
material post-GO change stops publication and returns for review. No routine
product retest or implementation change is required before publication.

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
  tests are authorized only for HIST-INP-01 under ADR-0051. No dependency, experiment,
  capture-limit or standards edits; the old documentation-only production/test
  exclusion is superseded, not the other restrictions.

All six frozen snapshots ([registry](development-workflow.md#historical-snapshots))
remain untouched.
