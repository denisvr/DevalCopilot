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
and fresh live main `4b52e1cfc3dbfbcb20908da5e87459b6e7c3e4f7`; divergence
0/0; empty index; initially clean tracked tree and no nonignored untracked
paths. After this selection, only this handoff is modified, unstaged; HEAD
is unchanged. No implementation or publication GO has been given.

WF-05 baseline and GO: this file at `e55709f`; earlier:
[priority](planner-handoff-history-e614c79.md#priority-and-verified-baseline),
[WF-04 GO and review](planner-handoff-history-e614c79.md#selected-slice-wf-04-publication-go).

## Latest verified publication

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

## Selected slice: HIST-ATT-01 historical run Agent-attempt inspection

**Decision:** expose the existing read-only Agent-attempt history, evidence
metadata and sealed artifact-window viewer inside the selected historical
run. This is one functional access path, including attempts that emitted no
collaboration message; it does not extend ADR-0037's attribution inspector.

**Evidence and roadmap:** `ProjectRunHistoryDetail` currently mounts only
the receipt, event headers and collaboration history. `AgentAttemptHistoryPanel`
is mounted in production only by `UsageEvidenceRail` for the current run.
The existing protected history/evidence/window operations already resolve an
explicit run and attempt (see the
[inspection contract](../architecture/agent-collaboration-evidence-inspection.md#agent-attempt-history-and-evidence-inspection)).
ADR-0033's historical selection ownership remains mandatory; ADR-0037's
minimal attribution stays unchanged. This advances Increment 5's historical
evidence access, not complete replay. Increments 4/5/6 remain incomplete.

**Sizing:** include navigation, necessary state ownership and response-coherence
guards, focused tests and one authenticated browser journey as one reviewable
outcome. Autonomous coordination needs separate dispatch/recovery authority;
remote publication remains blocked by Proposed ADR-0050/P9. Neither is needed
for these existing read operations. No new backend contract, schema, provider
capability, generic history framework or architecture reversal is selected.

**Scope and acceptance:** reuse the existing generated clients and attempt
viewer; open history, select an attempt, then explicitly select/load sealed
text windows. No eager evidence/artifact request, polling or automatic retry.
Run/project replacement, A-to-B-to-A, closing/reopening and parent reload end
every child selection/read/text lifetime before replacement is displayed;
obsolete results and retained callbacks cannot affect a successor. Page
ordering, continuation and selected evidence identity must be checked before
display. Failed later pages retain only valid rows and retry the same cursor.
Unknown/incoherent identity offers no evidence; unavailable/missing/integrity
failures disclose no text. Preserve literal-text rendering, purpose allowlist,
manual paging and existing sensitivity caveats. Mount no live/mutation control
and change neither current run selection nor workflow state.

**Validation:** focused frontend tests for the composed history and its
modified hooks/decoders, existing attempt/artifact and project-selection
regressions, frontend lint/build (includes typecheck), and one new
`historical-agent-attempts.journey.ts` through the existing authenticated
deterministic-provider journey host. Prove old-run inspection after a newer
objective, including a no-message failed/interrupted attempt, and no new
provider invocation or product mutation during inspection. Build the unchanged
journey host once as needed; do not infer native/provider qualification.
Run architecture tests, applicable dependency/secret checks, changed-document
links and diff hygiene. Broaden only for changed shared ownership/window code,
failures or uncertain impact; no routine complete backend/API suite or client
regeneration with unchanged inputs. Retain one concise executor log and all
original failures. Executor updates current-work at delivery, including the
resumption and outstanding risks; this planner edit belongs to the review diff.

**Continuity:** keep Codex in this chat; context usage is unknown. Start one
Claude chat for HIST-ATT-01 after the completed WF-06 slice, retaining its
corrections there. Return unstaged/uncommitted/unpushed for explicit review;
no second slice and no commit/push authorization.

## HIST-ATT-01 review: GO after correction 1

Codex independently reviewed the final production diff, decoder, owned lifetimes,
composed tests, browser journey and final evidence. GO for this reviewed slice,
including this review edit; publication identity, expected sole parent and exact
approved paths are supplied in the publication prompt and ignored review record.
Publication and its independent verification remain pending, not implied by GO.
Keep publication in the same Claude chat. No next slice is selected.

- **HATT-R1 (P2), resolved in correction 1:** the first delivery's
  `agentAttemptHistory.ts` accepted a verified history
  row with arbitrary nonblank status/role/provider and an absent or incompatible
  response contract. Its evidence decoder accepts the same unsupported identity
  and binds only ID/number, allowing a different role/provider/contract from the
  selected row. A direct execution of the transpiled production decoder accepted
  UnknownStatus/UnknownRole/UnknownProvider/UnknownContract as both a page and
  success evidence. The correction now requires recognized supported identity
  combinations, compared directly with `AgentAttemptIdentity` and
  `AgentAttemptContract`, and binds verified evidence to the accepted row's
  immutable role/provider/contract. Recorded status/times remain fresh
  observations, not equality to the page. Identity-invalid behavior is retained;
  unsupported/contradictory responses are rejected whole. Pure, hook and composed
  regressions cover unknown members, unsupported combinations, contradiction and
  valid status progression. Codex independently reran all 104 tests in the three
  correction-focused files: passed, complete output in
  `logs/planner-review/hist-att-01/correction1-focused.log`.
- **Independent checks:** focused frontend filter (history decoder/hooks,
  composed historical inspection and both artifact viewers): 7 files, 135 tests
  passed; full output in `logs/planner-review/hist-att-01/focused-02.log`.
  First attempt: 45 tests passed, six suites failed to load temporary module
  files (ENOENT); output is in the Codex tool transcript, with truncation, not a
  complete retained log. Second attempt used an ignored workspace TEMP/TMP root;
  the underlying first failure is not explained. `git diff --check` passed.
- **Final evidence:** inspected executor correction c01-c09 outputs and the
  retained initial matrix. Correction c03/c04/c05 pass 104/170/199 tests (overlap,
  not additive), lint/build pass with unchanged warnings, authenticated journey
  passes. The binding mutant produces six assertion failures, not a compilation
  kill, and records equal restored hashes; inspected, not independently rerun.
  Architecture, strict journey typecheck and unchanged shared-window consumer
  evidence are retained for their unchanged inputs. The unretained interim
  four-fixture passing run is not relied on. Fresh live main still equals
  `4b52e1cfc3dbfbcb20908da5e87459b6e7c3e4f7`; index empty.
- **Scope dispositions:** cockpit-spec documentation and the attempt route's
  purpose mapping are accepted as necessary parts of this outcome. The
  message-linked route mapping, prototype-key allowlist and window-offset
  validation are pre-existing limits, retained without claiming resolution;
  they do not require a second slice or expansion of this correction. Duplicate
  React-key controls support pre-existence, not cause or harmlessness.
- **Evidence disposition:** executor run 02 and run 13 complete output was
  not retained, contrary to the validation policy. Accept this delivery's
  explicitly recorded loss as a limitation: the reported fixture failures and
  compiler failure are not authenticated by their later passes. Do not recreate
  originals or rerun solely to simulate lost evidence. Retain complete original
  output for every correction check and every new failure prospectively.

No further implementation correction is required for this diff. The publication
GO authorizes only its exact approved content and paths, with a normal
fast-forward push of main and independent publication verification afterward.
Material post-GO changes return for review; no next-slice authority.

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
  tests are authorized only for HIST-ATT-01. No dependency, experiment,
  capture-limit or standards edits; the old documentation-only production/test
  exclusion is superseded, not the other restrictions.

All six frozen snapshots ([registry](development-workflow.md#historical-snapshots))
remain untouched.
