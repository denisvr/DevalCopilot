# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md), the
[roadmap](mvp-delivery-plan.md), and accepted [ADRs](../decisions/README.md).
Verify against Git and code; previous selections and reviews remain in Git.

## Current selection (2026-10-01): human authorization of one escalated-plan implementation

### Current review decision: GO (2026-10-01)

- GO authorizes exactly the reviewed substantive slice: 34 modified tracked files
  (including this planner record and current-work.md) and 49 untracked files, 83 total.
  Independently verified main, HEAD, local origin/main and live refs/heads/main at
  `3fe5f08cb648f3726e385d14be554ae4e1a4fda0`, nothing staged. The substantive
  commit must have that parent; preserve this GO record and the reviewed files.
  No next slice is selected. A material change after GO requires re-review.
- R1 is resolved: the authorized report/correction chain carries ImplementedPlan
  separately from OriginalProposal. Initial review, format repair and correction
  re-review receive the final Proposal's identifier and substantive content; the
  actual Planner root remains the historical lineage and ADR-0010 reply identity.
  Hosted tests capture what the review adapter receives, with different root,
  first-revision and final plans. Earlier forms retain their target and bytes.
- R2 is resolved: committed identity lifetimes and read ids do not recur after a
  run/source round trip. Earlier Available, Consumed and error records do not
  reappear while the replacement read is pending; hook and real cockpit regressions
  cover A-B-A, failures and obsolete completions. Same-identity refresh retains
  existing display state while loading so the open form keeps its draft.
- R3 is resolved: the authorized claim's armed post-seal exception scope and failure
  branches use the existing independent durability probe with a non-cancelled token.
  Definitively orphaned manifests are deleted; committed or unprovable ones are
  retained. Real SQLite regressions cover cancellation around begin/save/commit,
  failed rollback, raw failure, unresolved probing and exactly one spent grant
  after a committed claim. Production DI supplies the existing probe registration.
- Independent final-tree checks: solution build 0 warnings/0 errors; Application
  authorization/claim/query/downstream/manifest plus CodeReview and correction-status
  filter 207/207; Api authorization endpoint/hosted filter 20/20; Infrastructure
  migration/actual-adapter filter 19/19; Domain full 903/903; Architecture full 9/9;
  frontend 108 files, 1470/1470; typecheck clean; lint 12 existing warnings, no errors;
  harness 19/19; authenticated real-host Chromium 5/5, reuseExistingServer false.
  No skips in these reviewer runs and no devalcopilot-e2e-* root left afterward.
  Client SHA-256 remains
  `1f8ef46cc50871f0494d25838b63c0f37e6131ec4f6bef7aa44a126eeeee5bbb`.
  All 83 changed files are free of NUL/trailing whitespace; 159 local documentation
  links resolve; git diff --check is clean apart from Git's CRLF notices.
- Executor evidence on the corrected tree: full Application 2881, Infrastructure
  943 passed plus 3 existing skips, Api 714; production frontend build, regeneration,
  audits and formatter comparison (153 findings/17 files, the recorded baseline).
  These are distinct from the reviewer runs above. No real-provider reliability or
  formatter cleanliness is claimed. Ordinary depth-one review still targets the
  root under its historical contract; that separate issue remains outside this slice.
- Publish through the one instruction in the planner chat: reviewed substantive
  commit, normal fast-forward push, live-remote verification and post-publication
  checks, then current-work.md-only factual closure. Stop on push failure/divergence;
  no force-push or history reconciliation. Do not alter implementation, choose
  another slice or add unrelated documentation before publication is verified.

### Verified baseline and candidate judgment

- Independently verified `main`, `HEAD`, local `origin/main` and live `refs/heads/main`
  at `3fe5f08cb648f3726e385d14be554ae4e1a4fda0`, initially clean with nothing staged,
  unstaged or untracked. Published direct-guidance substantive commit:
  `c57439ebec2463de900fa6d4fb66831952b4254c`, parent
  `371c3a6b81ddc65bc8de7057d8bd8ce2b388296e`, exactly 116 files. The closure has
  that substantive parent and changes only current-work.md. Client SHA-256 remains
  `44afe84a4aa14ce6f9307f7a248dab4e5b31977eecd34b812da47ebf8475f44b`.
  Post-publication tests are executor evidence in the delivery entry; this planning
  turn verified Git, code and documentation without repeating those tests.
  After this planner edit: same branch/HEAD/refs, nothing staged, only this handoff
  modified and unstaged, nothing untracked. This is the exact executor preflight.
- Select exactly one Increment 4 outcome: a human may explicitly authorize one
  initial implementation claim for the current depth-two plan after the second
  challenge-resolution escalation. Authorization records a real human decision;
  requesting implementation remains a separate operation. No third review or
  resolution becomes possible and no budget or execution gate is overridden.
- Benefit: the existing final revision and complete decisions become usable without
  spending another planning lineage merely to restate a plan the owner accepts.
  PlanningEscalation asks for a human decision, but CreateImplementationAttempt,
  PlanningLineage and ImplementerExecutionReportEligibility currently reject depth
  two unconditionally. Close that whole input/implementation/downstream evidence
  chain, rather than adding a decision button that later review cannot consume.
- Alternatives: further context sampling has lower value after tracked/untracked
  previews; manual-run terminalization, coordination, stop/takeover, commits and
  publication belong to Increment 5/6 and need separate lifecycle authority.
  Provider controls remain unselected: the current Codex observer reads only quota
  snapshots, without binding account and bucket applicability to the invocation.
  The current official [app-server contract](https://learn.chatgpt.com/docs/app-server)
  describes account/rateLimits/read and its quota windows; that is not an invocation
  reservation. The current Claude adapters disable session persistence, which the
  official [CLI contract](https://code.claude.com/docs/en/cli-reference) says prevents
  resume. Neither allowance observation nor a session identifier proves eligibility.
  Claude account allowance and provider-session resume remain unproven here.

### Selected authority, persistence and operations

- Add ADR-0016 for this explicit human exception to the current protocol's unconditional
  depth-two implementation refusal. Preserve accepted ADRs 0004/0005/0009/0010/0012/
  0013/0014/0015. The automated lineage still ends at depth two; review/resolution caps
  stay unchanged. Never raise MaximumImplementableDepth globally to permit unapproved
  plans. This selection authorizes the specified architecture, not publication.
- Add one protected MVC authorization command plus a source-scoped read operation.
  Use POST/GET /api/runs/{runId}/planning-escalations/{escalationMessageId}/implementation-authorization;
  the POST body contains only rationale. Derive the final Proposal from the same-run escalation,
  never from human text or client-supplied authority fields. Require a nonblank bounded
  rationale using the existing compatible normalized-text policy (at most 600 UTF-16
  units); the instruction itself is fixed host text. No free-form instruction, replacement
  Proposal, extra attempt request or provider setting is accepted by this operation.
- Validate the full completed coherent depth-two lineage, all ordered Challenge/Decision
  identities, exactly one canonical HostConstructed Orchestrator-to-Human escalation
  replying to its final Proposal, and current run/mode/workspace/active lease/checkpoint/
  fresh fingerprint. Reject a source superseded by a newer independent Planner Proposal.
  Unknown, foreign, unreadable, forged or ambiguous evidence fails closed without echo.
  Historical escalation messages are not rewritten or treated as prior authorizations.
- Persist one planning-implementation authorization relation binding run, escalation,
  final Proposal and its exact workspace/checkpoint/fingerprint to one HumanInstruction
  message. The message is HumanSubmitted, attemptless, Human-to-Orchestrator, replying
  to that escalation, with fixed purpose and canonical bounded rationale. Keep it
  distinct from review-correction authorizations and direct advisory guidance; preserve
  the existing HumanInstruction protocol shape and existing factories' semantics.
  The relation owns one nullable consumed-by-attempt link; it is the only authoritative
  grant/consumption location, with database uniqueness and concurrency backstops.
  Do not duplicate the rationale or introduce a generic approval subsystem.
- No grant exists for historical rows: additive migration, no backfill or inferred consent.
  Keep authorization facts and messages as durable history with no new deletion operation.
  Identical retries return the same authorization without duplicate message/event; a
  different rationale for the same source conflicts. Retries never revive stale or consumed grants.
  At most one grant per escalation,
  with no renewal, revocation or second grant in this slice.
- Authorization makes no Agent claim, consumes no budget, seals no provider manifest and
  starts no provider. Bounded Git evidence capture stays outside transactions. After it,
  fresh untracked authority reads and the short authorization commit are one serialized
  unit; tracked entities cannot confer stale authority. Read states distinguish absent,
  available, consumed, stale and invalid recorded facts without claiming provider readiness.

### Implementation and durable evidence chain

- Extend the existing initial implementation request to recognize this explicitly authorized
  final Proposal, retaining its current request body. Validate it before external work and
  again inside a short transaction after capture/sealing; consume the one grant atomically
  with Attempt, exact inputs, artifact and ordinary budget reservation. A lost race/refusal
  consumes nothing and deletes any orphaned sealed manifest. Failed/interrupted/undispatched
  claims still spend the grant once, just as they spend their ordinary claim budgets; no refund.
- This branch's exact ordered AttemptInputMessage set is: final Proposal, every Decision
  of its second resolution in collaboration order, then the authorization HumanInstruction.
  Validate the entire two-round lineage but never promote an earlier challenged revision,
  invent a CriticalReviewer Acceptance, or flatten the human message into a provider Decision.
  Earlier plan forms keep their exact inputs and manifest bytes.
- Add a distinct bounded resolutionEvidence form for the human-authorized escalated plan,
  carrying final plan, complete leaf decisions and exact human authorization identifiers/
  rationale, framed by fixed host text. Preserve the 32-KiB ceiling, direct guidance and
  existing evidence-fitting rules; never truncate authority evidence or human text.
  Internal invocation expectations may carry the bounded authorization solely for sealed
  manifest consistency. No provider CLI flag, permission, schema, contract-version or tools change.
- Both eligibility feed and fresh final dispatch fail closed on missing/mismatched grant,
  source, consumption owner or inputs. The actual implementation adapter refuses a sealed
  authorization-form disagreement before any process. Sealed replay uses the same consumed
  grant and immutable input, never current UI state or a newly reconstructed grant.
- Make the successful ExecutionReport chain valid through local verification, CodeReviewer,
  ordinary correction and re-review under the existing contracts. Validate historical human
  authority against that implementation's starting checkpoint and exact consuming attempt,
  not the later result checkpoint. Resolve the actual Planner root across both revisions;
  do not mistake the depth-one parent for the root. Preserve ADR-0010's correction input set,
  findings/revision replies, exact verification selection and extra-correction authorization.
- Preserve all existing mode, lifecycle, active-attempt, writer, workspace, lease, checkpoint,
  duplicate, count/time budget, token-stop, model, turn-limit and mutation-recovery gates.
  Human authorization supplies only this plan-decision prerequisite, never invocation eligibility.

### Cockpit and exclusions

- Add an explicit authorization panel for the current planning escalation, showing its final
  plan/decision identity and the one-claim consequence. Offer implementation separately only
  against that exact authorized Proposal; existing provider review actions remain withheld
  for depth two. Render available/consumed/stale/invalid facts and the recorded human reason
  truthfully; budgets and provider checks stay separately visible. Do not optimistically
  infer an authorization from a successful click without current server facts.
- Own form, rationale version, handlers and async continuations by run/escalation/Proposal/
  mounted lifetime, including A-B-A, source replacement/return, unmount, duplicates and
  edits during API/refresh. Late accepted operations remain real server facts, but cannot
  update another interaction or clear a newer draft. Preserve project selection/isolation.
- Update protocol, workflow, cockpit, ADR index and engineering context as affected; regenerate
  the client through build. New shared policies/contracts belong in explicit Policies/Ports/
  Errors/Contracts ownership, not an operation-bearing feature root; no unrelated reorganization.
- Exclude general approvals/chat, arbitrary HumanInstruction submission, plan editing, root/
  depth-one overrides, third challenge rounds, automatic stages, scheduler, terminalization,
  pause/stop/takeover, budget overrides, fallback/Gemini, provider resume/compaction, account
  allowance, new dependencies, Git/CI/publication actions and more context sampling.

### Stop gates, acceptance and executor handoff

- Stop on Git drift, authority outside this one source/claim, changes to accepted budget or
  correction semantics, protocol/output-schema or provider-contract changes, destructive
  migration, auth bypass, real-provider tests, unsafe cleanup or unbounded harness expansion.
- Prove parent-schema migration with historical messages/inputs and no grants fabricated.
  Cover full source/provenance/canonical-content validation, foreign/stale/duplicate roots,
  malformed/unreadable evidence, idempotency and conflicting rationale, concurrency, and
  populated-context authority changes committed by another connection before BEGIN.
- Prove single consumption and rollback/orphan cleanup at both authorization and claim seams;
  every existing hard gate still refuses, retaining the unconsumed grant when no claim commits.
  Wrong consumption owner, tampered inputs/grant and sealed-form mismatch invoke no provider;
  healthy siblings and every historical plan/correction form remain valid. Depth-two review/
  resolution remains refused with or without a human grant.
- Hosted deterministic doubles prove the whole successful second-resolution -> human
  authorization -> explicit implementation -> checkpoint/report -> verification -> code review
  chain, plus ordinary correction/re-review of that report, exact input/reply/root identities,
  budgets and captured provider stdin. Restart replay of an undispatched claim neither renews
  consent nor consumes twice. Do not infer real-provider reliability from doubles.
- Prove actual regenerated-client request/response behavior over authenticated real HTTP and
  a bounded Chromium authorization interaction using owned metadata fixtures and no provider,
  alongside existing intake/simulation/wire smoke. Tests select their own project/run and
  are order-independent; retain verified-root cleanup and credential sanitization.
  Frontend controlled promises cover all ownership cases and truthful refresh failure.
- Obtain focused red/green or restored mutations for source freshness, authorization required,
  exact consumption/input binding, third-round refusal, root resolution and UI ownership.
  Final validation: solution build; sequential full Domain/Application/Infrastructure/Api/
  Architecture suites; full frontend/typecheck/lint/build, harness and Chromium; client
  regeneration stability, dependencies/secrets, formatter versus parent, doc links, tracked/
  untracked whitespace and NUL checks. Distinguish actual runs, retained evidence and skips.
- Continue in the same Claude executor chat. The current GO above covers the reviewed diff;
  do not edit this planner record. The English publication instruction is in the planner chat.
  That one instruction covers substantive commit, normal fast-forward
  push, live-remote verification and current-work.md-only factual closure. Re-review material
  post-GO changes; stop on push failure/divergence without force or history reconciliation.
