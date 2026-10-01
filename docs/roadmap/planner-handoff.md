# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md), the
[roadmap](mvp-delivery-plan.md), and accepted [ADRs](../decisions/README.md).
Verify this checkpoint against Git and code; older decisions and reviews remain in Git.

## Current selection (2026-10-01): direct human guidance for mutation requests

- Independently verified publication: `main`; `HEAD`, local `origin/main`, and live
  `refs/heads/main` all equal `371c3a6b81ddc65bc8de7057d8bd8ce2b388296e`, with nothing
  staged, unstaged, or untracked. Manual-intake substantive commit:
  `506653539727799f87e15a85dab7db4f7fcbf419`, parent
  `325316a00d31be0bd05dfacba7a6c073ff327251`, with the reviewed 116-file inventory.
  The closure has that substantive parent and changes only `current-work.md`.
  Generated-client SHA-256:
  `7253732aa5f7bd4c98198f31e2bb701c8c4b74ca2368c1d2c24d4ee78e67f2c8`.
  Post-publication tests are executor evidence recorded in `current-work.md`;
  this planning turn did not repeat tests or implement production changes.
  After this planner edit: same branch/HEAD/refs, nothing staged, only this handoff
  modified and unstaged, nothing untracked. Verify this exact executor preflight.
- Select exactly one Increment 4 outcome: optional short advisory guidance with an explicit
  initial implementation or ordinary review-correction request. The accepted text belongs
  to that exact claimed attempt, is visible as a durable human-input fact, and reaches its
  sealed context unchanged on restart. This enables clarification before correction-budget
  exhaustion without manual transfer, changing a resolved plan, or granting another attempt.
- Priority: both request DTOs currently contain only their source ID. Existing guidance
  applies only to authorization of an extra correction after exhaustion. Workspace preparation,
  checkpoints, verification recipes and manual intake already have public UI operations.
  More context sampling would not deliver this missing human control. Manual-run completion
  needs a separate decision about publication/CI and cannot turn local review into full
  delivery success. Account guardrails remain deferred: the Codex observer does not bind
  account/authentication and bucket applicability to the actual invocation. The official
  [app-server contract](https://learn.chatgpt.com/docs/app-server) describes
  `account/rateLimits/read` as quota-usage snapshots, not an invocation reservation.
  Observation alone is not threshold enforcement or invocation eligibility.

### Planner-owned architecture and boundaries

- Extend the existing two protected MVC request bodies and mediator commands with optional
  `Guidance`; retain routes, source-ID requirements and response shapes. Omitted/null preserves
  existing behavior. Supplied text normalizes deterministically (Unicode Form C, LF line endings,
  trim) to nonblank text of at most 600 UTF-16 code units, valid Unicode and no controls except LF.
  Apply the existing bounded summary content policy. Reject invalid supplied input with safe
  Problem Details before external work; never truncate, discard or echo rejected text.
  Share normalization only for intentionally shared policy; preserve the authorization
  rationale's reserved sentinel and canonical representation.
- Persist one nullable immutable direct-guidance text snapshot on `Attempt`, assigned only by
  the two mutation claims and committed with attempt, inputs, manifest metadata and existing
  budget reservation. Migrate old attempts to null without inferring/backfilling artifacts or
  HumanInstruction messages. Null means no direct guidance recorded, not proof of no historical
  human guidance. No mutable run-wide setting, draft entity, separate snapshot table, new
  collaboration-message type or generic instruction framework.
- Guidance clarifies work inside the authoritative plan or complete findings. It cannot change
  objective, inputs, schema, permissions, working directory, verification, Git/network restrictions,
  model, turn limits, budgets or approval. Preserve mode, lineage, workspace, lease, checkpoint,
  duplicate and budget gates, and exact ordered AttemptInputMessage rows under ADR-0010.
- Direct correction guidance is available within the ordinary correction budget only. At exhaustion,
  refuse any request containing direct guidance with a fixed conflict, even when extra authorization
  exists; consume nothing and create no escalation as a side effect of that refusal. Preserve
  existing no-guidance requests, escalation and both authorization operations. Never merge,
  replace or reinterpret the HumanInstruction authorization rationale.
- Add a distinct `directHumanGuidance` object with the accepted text exactly once and a fixed
  host-authored advisory boundary to both initial implementation manifest forms and ordinary
  correction manifests. Place the boundary before untrusted evidence. Existing authorized
  correction `humanGuidance` stays separate and unchanged. Absent direct guidance preserves
  existing manifest bytes, including authorized guidance. Retain the 32-KiB bound and existing
  evidence-fitting policy; guidance is never truncated.
- Feeds, fresh final dispatch and invocation consistency fail closed on malformed snapshots,
  incompatible role/provider/contract/profile, or disagreement between the fresh persisted
  snapshot and the exact sealed manifest. Preserve historical unguided/authorized attempts.
  Guard snapshot drift at the dispatch commit seam; an earlier projection cannot confer authority.
  Use the existing bounded sealed-artifact verification boundary, never regeneration from current
  settings. Internal invocation models may carry the immutable expected snapshot solely for
  consistency checking. No CLI flag, tool, environment, permission, session, output-schema or
  provider-version change is selected. No new automatic retry or recovery authority.
- Expose the bounded fact in the two statuses, attempt history and evidence, with truthful absence
  and safe malformed-state rendering. No entire manifest, transcript, absolute path or provider
  diagnostic. Label direct human submission separately from extra-correction authorization.
  A fact proves what the host supplied, not that a provider followed it.
- Add optional guidance editors to the two cockpit actions. Submit source ID and text together;
  retain no-guidance actions and separate extra-authorization UI. Own drafts, busy/error state,
  handlers and API/refresh continuations by run, source and committed interaction lifetime,
  including A -> B -> A and unmount. Every draft edit increments a version, including identical
  text edits. Clear only an unchanged submitted draft after current success. Reject obsolete
  handlers and synchronous duplicates; accepted stale operations remain real server state.
  UI availability never overrides a server gate. Render text normally, never as HTML.
- Record this additive decision in ADR-0015 and its index, documenting these selected semantics.
  Preserve accepted ADRs, especially ADR-0004/0005/0009/0010/0012/0013/0014. Update affected
  protocol, cockpit and workflow descriptions; regenerate the TypeScript client through build.
- Exclude other Agent stages, general chat/injection, read-fetching overhaul, scheduling,
  automatic advancement, terminalization/replacement, pause/stop/takeover, approval/budget overrides,
  provider selection/fallback, dependencies, account allowance, resume/compaction, context sampling,
  Git/publication and CI operations. Do not claim Increment 4 completion or provider reliability.

### Stop gates and acceptance evidence

- Stop for planner judgment on changes to accepted authority, authorization protocol, provider
  permissions/CLI contracts, output schemas, active-run lifecycle, recovery or ordered-input identity.
  Stop on unexplained Git drift, historical fabrication, destructive migration, auth bypass,
  real-provider invocation in automated tests, unsafe cleanup or unbounded harness expansion.
- Prove real SQLite migration from the parent schema and old-null/unrelated-save preservation.
  Cover both claims and implementation forms, ordinary correction, absent/null, normalization,
  invalid text, foreign/stale sources, duplicates/concurrency, exhaustion with/without authorization,
  rollback and orphan cleanup. Refusals leave no claim/input/artifact/reservation or consumed authorization.
- Prove both feeds, fresh final gate and actual Infrastructure adapters refuse malformed/mismatched
  guidance before any provider process. Keep a populated tracked context while another connection
  changes the snapshot before the dispatch transaction: drift must refuse. Healthy siblings and
  old unguided/authorized paths work. Projections never throw or display malformed text.
  Do not claim protection against every hostile write after the dispatch boundary.
- Hosted deterministic process doubles capture actual stdin for guided initial implementation
  and guided ordinary correction: exact accepted text once, unchanged source identities/tool arguments,
  validated result persistence and sealed replay of an undispatched claim after restart.
  Later requests/draft edits cannot change replay. Include authorized/unguided positive controls
  and zero process calls on mismatch. Extend owned disposable fixtures; no production test hooks.
- Frontend controlled promises cover same-run source replacement/return, run switch/return,
  unmount/remount, stale handlers, duplicates, current success/refusal, API/refresh draft edits
  including identical text, separate exhaustion/authorization UI and fact rendering. Exercise the
  regenerated client against real HTTP. Retain existing authenticated Chromium intake/simulation
  smoke tests; a full provider browser workflow is excluded.
- Obtain focused red/green or restored mutations for snapshot/manifest mismatch, exhaustion refusal,
  dispatch freshness and draft/source ownership. Final tree: solution build; sequential full
  Domain/Application/Infrastructure/Api/Architecture suites; full frontend tests, typecheck, lint,
  production build; harness and existing Chromium smoke; regeneration stability, dependency/secret
  checks, formatter comparison against parent, doc links, tracked/untracked whitespace and NUL checks.
  Report actual versus retained evidence, exact commands, skips and baseline diagnostics.
- One new Claude executor chat implements this selection and keeps its corrections. Return the
  entire unstaged, uncommitted, unpushed diff, commit-ready `current-work.md`, inventory, checks
  and limits for Codex GO/NO-GO. Do not edit this planner record or select another slice.
  The English prompt is in the planner chat. Selection grants no commit/push GO. After future GO,
  one publication instruction covers the reviewed substantive commit, normal fast-forward push,
  live-remote verification and bounded factual documentation closure. Material changes after GO
  require re-review; push failure/divergence stops without force or history reconciliation.

## Current review (2026-10-01): GO for the reviewed direct-guidance slice

- Independently verified `main`, `HEAD`, local `origin/main` and live `refs/heads/main`
  at `371c3a6b81ddc65bc8de7057d8bd8ce2b388296e`, nothing staged. Reviewed inventory
  after this planner edit: 81 modified tracked files including this record, plus
  35 untracked files (116 total). Generated-client SHA-256:
  `44afe84a4aa14ce6f9307f7a248dab4e5b31977eecd34b812da47ebf8475f44b`.
  Publication is authorized for this exact substantive diff, including current-work.md,
  this planner decision, ADR-0015, the migration and generated client. No publication has
  occurred yet. A material change requires another review before committing.
- R1-R6 are resolved. The fresh dispatch query reads the complete guidance coherence
  tuple without disturbing pending tracked writes; null snapshots use neutral NotRecorded;
  agreement requires a JSON object and valid guided boundaries; the response contract lives
  in Contracts. The two UI specifications select and assert their own fixture project after
  registration and reload. The wire fixture is built once before registration retries,
  which accept only the specific 409 Git-unavailable refusal, stop within a finite test
  budget and surface unexpected failures without transport details. The correction changed
  only e2e tests/support and documentation. Accept ADR-0015 as the additive selected decision;
  existing authority, ordinary budgets, authorization guidance and accepted ADRs are preserved.
- Independently repeated on the final tree: solution build 0 warnings/errors; frontend
  1,394/1,394 across 103 files and typecheck clean; Node harness 19/19; real authenticated
  Chromium normal order 3/3 and wire-before-manual-before-simulation order 3/3. The latter
  reruns the exact order that failed in the previous review. No provider calls or production
  hooks were introduced. The temporary wire copy was removed and no owned e2e root remained.
  Client hash stayed identical; tracked diff whitespace and all changed-file whitespace/NUL
  checks passed; 152 local documentation link targets resolved. SignalR negotiation messages
  remain disclosed. Browser runs used the existing host outside the sandbox because Windows
  Event Log access blocked sandboxed startup in the previous round; composition was unchanged.
- Retained independent evidence from the previous review: full Domain 881/881, focused
  Application direct guidance 99/99, Infrastructure guidance/migration 52/52, Api direct and
  authorized replay guidance 36/36, Architecture 9/9, with no skips in those runs. Full
  Application 2,729, Infrastructure 924 passed plus 3 environment skips, Api 694, frontend
  lint/build/audit, formatter comparison and vulnerable-package checks remain executor
  evidence, with their provenance recorded in current-work.md. They were not rerun by this
  reviewer during the test/support-only correction. Do not claim formatter cleanliness or
  real-provider reliability. Recorded guidance proves host input, not provider compliance.
- Next authorized action: use the single publication instruction in the planner chat to
  commit exactly the reviewed 116-file substantive slice, push main normally, verify the
  live remote, run the specified post-publication checks, then make one factual closure
  commit changing only current-work.md and push/verify it normally. Preserve this planner
  record. Any push failure, remote divergence, unexpected inventory or check failure stops
  publication/closure without force-push, history reconciliation or unreviewed fixes.
  The closure records the substantive SHA and checks actually run, never its own SHA in
  itself. No next slice is selected; selection follows verified publication.

## Remaining limits

Manual runs can remain nonterminal and block another objective; this slice grants no completion
or replacement authority. The host retains normal probes and scratch-directory behavior.
Existing formatter findings and process-double limits remain disclosed. Claude account allowance
and provider-session resume remain unproven until safe contracts are established.
