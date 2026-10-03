# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md), the
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Selected slice (2026-10-03): an escalated final plan reaches a reviewed candidate

### Final review decision: GO for the reviewed 37-path snapshot

- R1, R2 and R3 are accepted within this same selected slice. GO authorizes the
  reviewed substantive commit and normal fast-forward publication on main, then
  a separately bounded current-work-only factual closure after required checks.
  It is not next-slice selection or Increment 4 completion. Material changes
  after this GO return for review before commit/push.
- Independently verified main, HEAD, local origin/main and live refs/heads/main at
  7cc361886eaf7bd6fad07292da401b98ff1c91a4, empty index, 28 modified tracked
  files and 9 untracked files (37 paths). The executor preserved the R3 review
  record at db9ebde79c70adf2ae30358082799cab022634463ce9bc377a6f54daf67ae83a.
  The generated client remains at the baseline hash below. No publication has
  occurred. A separate temporary SHA-256 manifest binds this GO to all 37 paths,
  including this final planner edit; the publication prompt supplies its path.
- The current escalation accurately describes durable claim consumption, accepts
  only its corrected current form or the unchanged whole legacy serialization,
  and leaves authority, budgets and persistence behavior intact. The new policy
  and enum follow the required ownership/file organization. The native journey
  carries the distinct final plan through explicit authorization, implementation,
  failed verification, guided diagnosis correction, passed verification and
  separate Agent/Human approvals, preserving ordinary and historical paths.
- R3 removes the fulfill catch. Genuine failures are recorded, reach Playwright
  and reject shutdown. The fake now models both callback and route settlement.
  Actual Chromium regressions prove normal/cancelled delivery and the deliberately
  injected failure. The diagnostic child uses Node's warning mode to retain the
  intentionally rejected handler's stderr while printing its outcome; the parent
  asserts that error, recorded count and rejected shutdown. This isolated
  expected-failure experiment changes no normal fixture, parent runner, canonical
  browser command, authentication or host composition, and adds no global observer.
- Independent R3-tree checks: harness 58/58 including all 12 gate cases;
  npm typecheck clean; strict standalone tsc over 12 changed/new e2e sources plus
  the two relevant existing specs clean; lint 9 baseline warnings, no errors;
  one canonical test:e2e:all execution after the harness passed: Chromium 11/11
  then journeys 2/2, normal authentication/composition and all supervisors,
  no failures, skips or reruns. The first reviewer standalone tsc invocation
  lacked TypeScript 6's ignoreConfig option and returned TS5112; correcting that
  command, with no repository change, produced the reported successful check.
- Independent unchanged-backend evidence from the preceding re-review remains
  applicable: build 0 warnings/0 errors, Application planning/authorization
  197/197, hosted/fixture API 113/113 and Architecture 9/9. Executor full suites,
  frontend Vitest/build, audits and formatter comparison remain separately
  attributed in current-work.md; they were not rerun merely for R3.
  Tracked diff and complete 37-path NUL/trailing-whitespace checks are clean;
  209 local Markdown file links resolve (the reviewer did not recheck anchors).
  The new browser run removed its own roots; only the two known older roots remain.
- The original teardown timeout's historical cause/event order remains unknown;
  its failed run and unchanged green rerun are preserved. Correcting a reproduced
  mechanism and passing these checks does not promise the timeout cannot recur.
  Doubles prove local contract agreement, not real-provider reliability. The
  serial invocation interval, fixture visibility and displayed-summary limits
  remain as documented. No historical row or sealed artifact is rewritten.
- Publication is bound to the reviewed snapshot: verify refs, empty index, exact
  inventory and raw hashes before staging; stage only the 37 approved paths;
  commit with parent 7cc361886eaf7bd6fad07292da401b98ff1c91a4; push main normally;
  fetch and verify local/live remote equality and clean checkout. Run the prompted
  post-publication checks once. On divergence, push failure, material change or
  failed check, stop and report rather than retrying into green or reconciling
  history. Do not force-push, amend or rewrite published history.
- Only after successful substantive publication and required checks, update this
  slice's current-work.md entry with the delivered SHA, observed results and
  fresh/retained evidence, preserving failures and limits. Commit that file alone
  without embedding its own SHA, push normally and verify live remote equality
  and a clean checkout again. Preserve this planner record during publication.

### Decision and verified baseline

- Select exactly one outcome: after two challenge rounds, the human can understand
  the escalation, authorize one implementation of its exact final Proposal, and
  carry that plan through verification, diagnosis, guided correction, ordinary
  review and a separate manual checkpoint approval using the rendered cockpit.
  Claude implements in one new executor chat; Codex owns architecture and review.
  This selection is not commit/push GO and does not complete Increment 4.
- Independently verified main, HEAD, local origin/main and live refs/heads/main at
  7cc361886eaf7bd6fad07292da401b98ff1c91a4, with nothing staged, unstaged or
  untracked before this edit. Substantive 4e58c031660602fb257b068ba1d7988b5643125e
  has parent 4ec68bd9143d426ccb2a746732db4a6fa22e1b1d and exactly the 35 approved
  manifest paths. All files outside the closure match the reviewed raw hashes;
  the substantive current-work text also matches its approved hash. Closure
  7cc36188 has parent 4e58c031 and changes current-work.md alone. Publication is
  verified; the previous GO is spent and must not be replayed. Post-publication
  checks are reported in current-work.md; no additional suite was rerun merely
  for this selection.
- ADR-0016 already permits one explicit implementation after the second round;
  ADR-0017 preserves the exact implemented plan through correction and review;
  ADR-0018/0019 permit diagnosis and optional guided correction. Backend hosted
  tests cover authorized plans, but the existing authorization browser fixtures
  insert lineage rows with SQL and never request implementation. The native
  browser journey exercises only an ordinary first revision.
- PlanningEscalation still writes "not implementable through this lineage" and
  recommends a new planning request. PlanningImplementationAuthorizationEvidence
  requires that whole canonical serialized content exactly. Changing only the
  writer would invalidate historical escalations, grants and downstream chains.
- This outcome closes a consequential human-decision branch and its misleading
  product explanation together. Further context sampling has lower marginal
  value here. Account thresholds need a separate policy for account binding,
  freshness, missing evidence and invocation enforcement; the existing Codex
  observation grants none of those. Current official App Server documentation
  confirms the read method, not this host's guard:
  [Codex App Server](https://learn.chatgpt.com/docs/app-server).
  A documented [Claude resume flag](https://code.claude.com/docs/en/cli-reference)
  is not proof of safe continuity under this host's stateless restricted profiles.
  Claude allowance, provider-session resume and compaction remain unproven.
  Increment 5 lifecycle, scheduling and publication authority are excluded.

### Product and compatibility boundaries

- Preserve the separate protected authorization and implementation operations,
  their request/response shapes, required bounded rationale, fixed HumanInstruction,
  single permanent grant consumption, exact ordered inputs, all fresh authority
  gates, immutable manifests and every existing budget and permission.
- New escalations retain protocol 1.0, summary, participant/provenance/reply facts,
  content field names and identifier-derived evidence. Their fixed options and
  consequences must explain that a human may separately authorize exactly one
  implementation of this exact final plan and then explicitly request it, or
  request a new plan; the escalation itself chooses nothing and approves nothing.
  A third critical review or resolution remains unavailable. Recommendation:
  inspect the final Proposal and all second-round Decisions before choosing.
- Establish one bounded compatibility rule at the existing Application-owned
  escalation/source policy: recompute the entire legacy canonical serialization
  and the entire new canonical serialization for the same verified identifiers
  and ordered challenges, and accept only ordinal equality with one whole form.
  Retain the legacy constants exactly. No semantic JSON relaxation, mixed form,
  fuzzy text, arbitrary version, caller-selected source, or inference from time,
  text, a CLI default or a successful click. All other source and grant checks
  remain mandatory and unchanged.
- Add ADR-0020 narrowly superseding ADR-0016's single-source-text-form requirement
  with those two exact forms. Do not edit accepted ADR history. Update the index,
  engineering context, workflow/protocol/cockpit contracts and the stale roadmap
  paragraph that still describes every depth-two plan as unimplementable.
  No historical message, grant, attempt, approval or sealed artifact is rewritten;
  new records use only the new form. No schema, migration or dependency change.
- Ordinary root/first-revision manifests and already sealed authorized manifests
  keep their bytes and replay behavior. This is a source-text compatibility rule,
  not a new grant, refund, renewal, revocation, extra correction or approval.

### Complete browser journey and fixture boundaries

- Add a second native-double browser journey under the existing journey harness:
  manual intake and owned workspace/checkpoint; Planner root; first Challenge
  and resolution; explicit second critical review with a materially distinct
  second Challenge; second resolution and exactly one host escalation; a required
  human reason and authorization; a separate implementation request; Failed
  local verification; Codex diagnosis; one explicitly guided Claude correction;
  Passed verification; ordinary Codex approval; explicit Human checkpoint
  approval; one final persistence reload. No intermediate reload or auto-advance.
- Produce three distinct Proposal identities and distinguishable substantive
  content. Both resolutions decide exactly their own challenges in order.
  Extend the existing fixture's closed manifest-derived response cases to
  recognize root, first revision and final revision; no general scenario engine,
  environment-driven provider bypass or workflow SQL seed. Unsupported plan/stage
  combinations fail loudly. Preserve the original ordinary journey and its claims.
- Strengthen the native fixture's authorized implementation case to validate the
  fixed human-plan boundary, exact authorized form, final plan identity, required
  authorization/instruction/source identifiers and complete ordered second-round
  Decisions. Check before any file edit, output or successful invocation log.
  This fixture proves what the production adapter supplied, never independently
  grants authority. Keep current argv, schemas, confinement and alias checks.
- Log only bounded allowlisted identity/count/hash facts needed to prove the
  final plan and human authorization reached the adapter; never the rationale,
  guidance, manifest, secret or transcript. The implementation, diagnosis and
  ordinary review must show the final Proposal's identity and substantive content,
  not the root or intermediate revision; correction reports still reply to the
  Planner root and exact findings under the accepted correction contract.
- Before authorization, show no implementation action for the final plan.
  Authorization adds one HumanInstruction/relation and no claim, reservation,
  provider invocation or approval. Its implementation consumes exactly that grant
  once, and it remains Consumed through correction/review and the final reload.
  No third round, grant reuse or implicit run completion is offered.
- Expected successful sequence: nine Agent claims, slots 1 through 9, one shared
  correction claim, 110 minutes reserved within the unchanged 120-minute ceiling
  and 16-claim limit; two verification executions Failed then Passed; checkpoints
  1, 2 and 3; distinct Agent and Human approvals of checkpoint 3.
  Authorization and explicit evidence refreshes create none of those claims.
  These counts derive from current AgentClaimPathPolicy, not modified budgets.
- Browser workflow mutations use rendered controls and generated clients only;
  read-only SQLite/log inspections supplement visible assertions. Make journey
  evidence scoped to its own project/run/workspace and invocation interval.
  Both journeys must coexist and pass in either order and alone without a shared
  database reset or filename-order assumption. Keep normal Program composition,
  authentication, all supervisors, verified native double destinations,
  reuseExistingServer: false and current owned-root cleanup.
- Production scope is the exact escalation writer/source compatibility policy
  and its explanations. No new API or broad UI-hook sweep. If the complete
  journey exposes a production behavior blocker beyond that scope, preserve the
  reproduction and return it before making a workaround or unrelated fix.

### Acceptance evidence and stop gates

- Write focused regressions before the compatibility change and show red/green.
  Pin the old serialization independently of the changed writer. Prove both
  canonical forms authorize and consume once when otherwise eligible; legacy
  unconsumed and consumed grants remain valid; historical authorized manifests
  replay exact sealed bytes after restart and remain valid through downstream
  chains. Reject edited text, mixed forms, reordered/missing/foreign identifiers,
  duplicate JSON members, forged authorship/provenance, extra escalations and
  stale context without new claims, reservations or lost manifests.
  Reuse existing detailed seam/race/budget tests rather than duplicating them.
- Fixture contract tests must refuse missing/changed authorization boundaries or
  facts and root/intermediate substitution before effects. Harness assertions
  must detect root and intermediate plan substitution independently of the
  doubles, missing stages and claim/authorization count changes. Mutation checks
  target legacy support, exact-form matching and final-plan identity.
- Run affected checks first. On the final production tree run build/NSwag,
  sequential full backend suites and Architecture; frontend Vitest, typecheck,
  lint/build, harness, then canonical test:e2e:all. Verify generated client stays
  at its baseline hash. Run audits and formatter comparison; preserve the known
  formatter baseline rather than claiming cleanliness. Check links, complete
  tracked/untracked whitespace and NUL hygiene, inventories and owned cleanup.
  Retain unchanged evidence only when its exact code/test/dependency inputs
  remain unchanged, and label fresh versus retained evidence and all failures.
- Stop on preflight discrepancy, a need for a third round or wider grant, schema/
  migration, provider flags/profiles/schemas, permission, budget, scheduler,
  lifecycle/recovery/publication change, real provider call or new external
  capability. Do not disable a supervisor, seed the browser lineage, add a reload,
  hide an error, retry into green or loosen historical validation to pass.
- Return the complete unstaged, uncommitted, unpushed diff, a commit-ready
  current-work.md entry, exact inventory and checks actually run for Codex
  GO/NO-GO. Preserve this planner-owned record byte-for-byte. Select no next slice.
  Only a future explicit GO will authorize one publication instruction covering
  the reviewed substantive commit, normal fast-forward push, live verification
  and a separate current-work-only factual closure. Material changes after GO
  return for review.

### Executor preflight after this planner edit

Expected branch main and HEAD 7cc361886eaf7bd6fad07292da401b98ff1c91a4.
Local origin/main and live refs/heads/main must match. Nothing staged;
only docs/roadmap/planner-handoff.md modified; no untracked files.
Generated api-client.ts SHA-256:
13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a.
The complete English execution prompt is provided in chat, not duplicated here.
