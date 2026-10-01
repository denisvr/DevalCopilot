# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md), the
[roadmap](mvp-delivery-plan.md), and accepted [ADRs](../decisions/README.md).
Verify this checkpoint against Git and code; older decisions and reviews remain in Git.

## Current selection (2026-09-30): manual Agent run intake with durable execution-mode isolation

- Verified publication: `main`; `HEAD`, local `origin/main`, and live `refs/heads/main` all equal
  `325316a00d31be0bd05dfacba7a6c073ff327251`; staged, unstaged, and untracked state empty.
  The run-isolation substantive commit is `9d583d57a025f034f4fd2715f2c712a8f24f0072`, parent
  `ad66cfe1a42223e8dab0c1a1f7b5bb9d2c8793f6`, with the reviewed 48-file inventory. The closure
  has that substantive parent and changes only `current-work.md`. The generated-client SHA-256
  remains `4ac246f0f8fb259563d0985d2ac4035dca5d2cf39d9e5463854633485fa96386`.
  These Git facts were independently checked. Post-publication build/frontend results remain
  executor evidence; this planning turn did not repeat tests or implement production changes.
  After this planner edit: same branch/HEAD/refs, nothing staged, only this handoff modified
  and unstaged, nothing untracked. The executor must verify that exact preflight.
- Select exactly one Increment 4 outcome: a registered project can record a user-written objective
  as a durable manual Agent run, then use the existing explicitly requested collaboration stages.
  Creating or reloading that run must never start the simulation, invoke an Agent, or consume an
  Agent claim/time reservation. This makes the delivered stages reachable through normal intake.
- Evidence and priority: `App.tsx` currently sends only the fixed walking-skeleton objective to
  `StartSimulatedRun`, and offers creation only when the selected project has no run. That handler
  is the only production caller of `Run.RecordIntent`. Runs have no execution-mode discriminator;
  the simulation feed and claim accept every `Created` run. Adding only a manual-objective endpoint
  would race the simulator. Expanding browser coverage alone would still leave normal intake absent.
  Further context sampling is lower priority than this missing entry into the existing MVP.
  Provider allowance, resume, and compaction lack safe proven contracts and are not substitutes.

### Planner-owned architecture and boundaries

- Add immutable `RunExecutionMode` with `Legacy = 0`, `Simulated = 1`, `ManualAgent = 2`.
  Persist it with a migration. Existing rows become `Legacy`; do not classify them from attempts,
  provider defaults, lifecycle, or event payloads. Both public creation operations assign their
  explicit positive mode. No public mode parameter, setter, conversion, or historical rewrite.
  Legacy retains historical admission behavior, including legacy simulation and Process support;
  disclose that its mode was not recorded. This compatibility value supports actual new behavior,
  rather than an Unknown-only capability surface. Undefined modes never authorize execution.
- Add protected MVC `POST /api/runs/manual`, operation `CreateManualRun`, accepting only project ID
  and objective. Use one mediator command, existing Results/Problem Details, and the existing
  nonblank/2,000-character objective boundary. Persist the run in `Created`/`Intake`, monotonic
  project execution number, existing default run budgets, and an orchestrator-authored intent event
  in one short transaction. The response follows the existing creation shape (run ID/execution number).
  No workspace preparation, checkpoint capture, readiness probe, manifest, attempt, provider process,
  lease acquisition, automatic claim, or budget consumption is part of creation. Existing claim gates
  remain responsible for workspace, checkpoint, provider, policy, and budget eligibility.
- For both manual and simulated creation, atomically refuse when any run for that project is
  `Created` or `Running`; only no run or entirely terminal `Completed`/`Failed`/`Interrupted` history
  permits another intent. An unrecognized existing lifecycle also blocks creation. Return a safe
  conflict with no run/event/counter change. Serialize check and reservation; concurrent manual/manual
  and manual/simulation requests must not both succeed. Preserve all old rows and evidence.
  This is conservative manual intake admission, not ADR-0006's deferred scheduler or queue.
  Do not add a retroactive uniqueness rule that breaks databases with multiple historical active runs.
  Budget exhaustion alone does not make a run terminal or authorize replacing it.
- Admission table: simulation accepts `Simulated` or `Legacy`; the six existing Agent claim paths
  (including their four repair variants) accept `ManualAgent` or `Legacy`; standalone Process claims
  remain `Legacy` only. Apply mode checks before external work, at the authoritative claim commit
  seam with fresh reads, in eligibility feeds, and in final dispatch gates. UI/feed filtering alone
  is insufficient. Preserve current role/provider/contract, source, authorization, budget and sealed
  replay gates. Incompatible claimed attempts must not invoke adapters; do not invent recovery authority.
  Keep Process changes limited to its mode admission/feed/dispatch boundary, not its execution contract.
- Simulation recording/completion must require compatible run mode and `AttemptKind.Simulated`,
  with matching run/attempt identity. `CompleteSimulatedRun` currently lacks the kind check: close
  that seam so it cannot terminalize a manual Agent run. Guard any equivalent generic simulated-message
  recording boundary; preserve legitimate human/orchestrator messages and historical provenance.
- Project summaries and the cockpit expose recognized execution mode plus a truthful creation
  availability hint based on all project runs. Re-check eligibility in creation regardless of the hint.
  The new objective form is available for a first run and after terminal history, with a fixed visible
  reason when blocked. Simulation remains an explicitly labelled demo. Manual `Created` means waiting
  for an explicit planning request, never autonomous execution or proven provider readiness.
  Keep history intact and reuse the six existing stage actions; no new automatic chain or run selector.
  Hide/refuse Agent request actions for explicitly simulated/invalid modes. Legacy is visibly unclassified.
- Own the form's pending/error/draft/success by project and committed interaction lifetime, including
  A -> B -> A and unmount. Reject stale handlers and duplicate submissions synchronously. Protect
  post-request/refresh draft continuations from later edits. An accepted stale request remains real
  server state; do not undo or resubmit it. Reuse the preceding slice's lifecycle principles without
  broad read-fetching changes. Adjust directly related budget copy if needed so it does not promise
  immediate replacement of a still-active run.
- Document this additive decision in ADR-0014 and its index, reflecting this selection rather than
  delegating architecture to the executor. Retain ADR-0002/0003/0004/0005/0006/0008/0009/0010/0012/0013.
  Update affected cockpit/protocol/workflow intake documentation and the engineering context ADR index
  only as needed. Regenerate the TypeScript client through the build; never hand-edit it.
- Exclude coordinator/scheduler/queues, automatic stage advancement, pause/stop/retry/takeover, manual
  terminalization, replacing an active run, new approval/budget overrides, provider fallback, CLI flags,
  account allowance, provider-session resume/compaction, context sampling, Git/publication controls,
  verification-recipe management, broad frontend fetching, new dependencies and production test hooks.
  A manual run can remain nonterminal after its stages or budget exhaustion; lifecycle completion
  and replacement authority remain deferred. Do not claim Increment 4 is complete.

### Stop gates and acceptance

- Stop for planner judgment if isolation/admission requires a scheduler, active-run recovery, destructive
  migration, unsupported historical classification, provider-contract change, ADR reversal, relaxed
  authentication/ownership gates, or a production test bypass. Stop on unexplained baseline drift,
  real-provider invocation during automated tests, unsafe test cleanup, or unbounded harness expansion.
- Prove file-backed SQLite fresh creation and migration from the actual parent schema, preserving
  legacy lifecycle/events/attempts/budgets/evidence. Cover the admission table, undefined mode, terminal
  history, active/nonterminal refusal, missing project, invalid objective, transaction rollback, concurrent
  creation, and monotonic execution numbers. Prove direct simulation completion cannot complete an Agent.
- Cover all six Agent claims and six feeds, final Agent dispatch, simulation claim/feed/record/complete,
  and the narrow Process boundary. Include a kept-alive tracked context with a competing mode change
  before claim/dispatch commit: stale tracking must not confer authority. Refusals leave no claim-side
  records, reservation or consumed authorization; clean any orphan manifest. Healthy/legacy siblings work.
- Hosted proof: create through the protected HTTP operation with the simulator actually registered;
  observe several poll cycles and a host restart with the manual run still `Created`, no attempts or
  semantic Agent messages, and zero provider calls. A separate fixture project's explicit simulation
  must progress as a positive control. Then explicitly request existing planning in an
  eligible owned-workspace fixture; a deterministic adapter produces one validated durable Proposal,
  with correct budgets and no simulated attempt. Do not seed the run, attempt or proposal being proved.
- Browser proof: use the existing Playwright real-host harness with isolated disposable repository/data
  fixtures and normal authentication. Register the fixture through the public operation, create a typed
  objective through the UI, observe manual waiting state, reload and verify the same objective/run/mode
  and absence of simulated progress. Also retain a passing explicitly simulated demo smoke test.
  No mocked create/status response, real provider, paid invocation, developer database, auth bypass,
  credential-bearing trace/screenshot/storage/URL/log, or cleanup outside owned fixture roots.
  The official [Playwright web-server contract](https://playwright.dev/docs/test-webserver) supports
  the existing separate API/frontend servers; keep `reuseExistingServer: false`.
  Browser proof need not execute the entire six-stage/provider workflow.
- Include frontend controlled-promise cases for project switch/return/unmount, duplicates, unchanged
  current success/refusal, draft edits during a request/refresh, and explicit mode/blocked-intake copy.
  Obtain targeted red/green or mutation evidence that removing simulator isolation, final dispatch
  admission, or creation serialization fails meaningful regressions; restore every mutation.
- Final tree: solution build, sequential Domain/Application/Infrastructure/Api/Architecture suites;
  full frontend tests, typecheck, lint, production build and Chromium smoke; dependency/secret checks,
  formatter comparison against parent, generated-client regeneration stability, documentation links,
  and tracked/untracked whitespace checks. Report exact commands/results/skips, baseline diagnostics,
  actual versus retained evidence, and fixture cleanup. Never equate doubles with provider reliability.
- One new Claude executor chat implements this selection and keeps its corrections. Return the entire
  unstaged, uncommitted, unpushed diff, a commit-ready `current-work.md`, inventory, actual checks
  and limits for Codex GO/NO-GO. The executor does not edit this planner record or select another slice.
  The English execution prompt is in the planner chat, not this handoff. Selection grants no publication GO.
  After future GO, one publication instruction covers the reviewed substantive commit, normal
  fast-forward push, live-remote verification, and bounded factual `current-work.md` closure.
  Material changes after GO require re-review; push failure/divergence stops without force or reconciliation.

## Current review (2026-10-01): GO for the corrected manual-intake slice

- Independently verified `main`, `HEAD`, local `origin/main`, and live `refs/heads/main` at
  `325316a00d31be0bd05dfacba7a6c073ff327251`. The reviewed inventory is 66 modified tracked
  files (including this planner-owned record) and 50 untracked files, with nothing staged:
  116 files in the substantive delivery. Count untracked files, not collapsed status directories.
- The five NO-GO items are resolved: the existing exact-storage mapping now protects the mode
  field without changing its INTEGER/default-0 schema or classifying historical rows; admission,
  concurrency guards, feeds and projections use that stored form. Unknown lifecycles block intake
  without poisoning summaries. Draft clearing requires the submitted edit version and current
  interaction. Authorized browser reads sanitize failures, and deletion verifies a fresh owned
  root, marker/token and database containment with no working-directory fallback. The delivery
  entry is commit-ready and distinguishes final-tree evidence from retained results.
- Independent corrected-tree validation: solution build 0 warnings/errors; sequential full .NET
  suites Domain 839/839, Application 2,597/2,597, Infrastructure 872 passed/3 environment-gated
  skips, Api 646/646, Architecture 9/9; frontend 1,337/1,337; harness 15/15; production build
  including typecheck; lint 12 baseline warnings, no errors; Chromium 2/2 with the real hosts.
  The first sandboxed browser attempt could not initialize Windows Event Log and was terminated;
  the successful retry used approved execution outside the sandbox. Both attempts' owned roots
  were cleaned. A separate real Playwright transport-failure probe returned a sanitized exception
  with no synthetic credential or raw cause. No production implementation was changed by Codex.
- Independently checked generated-client SHA-256:
  `7253732aa5f7bd4c98198f31e2bb701c8c4b74ca2368c1d2c24d4ee78e67f2c8`.
  Tracked/untracked whitespace and NUL checks passed; 147 local documentation paths resolved.
  `dotnet format --verify-no-changes --no-restore` remains non-clean: 153 diagnostics in 17
  untouched files. The inconclusive parent-checkout count comparison is not acceptance evidence.
  Executor mutation/dependency-audit results remain attributed evidence, not independently repeated.
- GO authorizes publication of this reviewed substantive diff, including `current-work.md` and
  this planner record, through one normal fast-forward push, live-remote verification, selected
  post-publication checks, and a separate tightly bounded factual `current-work.md` closure.
  The complete English publication instruction is in the planner chat. No material changes after
  GO without re-review; push failure/divergence stops without force or history reconciliation.
  No next slice is selected. Preserve the manual nonterminal limitation, legacy compatibility,
  process-double/provider limits and normal host probes/scratch-directory caveat in the delivery.

## Prior decision and remaining limits

The run-isolation slice was reviewed GO and published as verified above. Its independent final
review checks were frontend 1,285/1,285, build 0 errors/0 warnings, production frontend build/typecheck,
lint 12 baseline warnings, audit 0 vulnerabilities, unchanged generated client, documentation paths
and whitespace/NUL checks. Post-publication results are recorded in `current-work.md`.
Ownership remains per hook/component instance; read-only fetching is unchanged; jsdom does not prove
browser layout-effect timing or provider behavior. Older detailed correction/review records remain in Git.
Claude account allowance and provider-session resume remain unproven without safe contracts.
Allowance observation never constitutes threshold enforcement or invocation eligibility.
