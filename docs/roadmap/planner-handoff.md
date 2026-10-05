# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md),
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Review decision (2026-10-05): GO for the corrected account-usage stop

Claude remains the executor. GO covers only the exact reviewed 168-path snapshot,
including the planner-owned decision and the factual review edits described in
current-work.md. No next slice is selected. Nothing has been staged, committed or
pushed by the reviewer. Freeze and verify every path's raw hash, size and status
in the external reviewed manifest before publication; any material change requires
re-review. Use the single publication instruction supplied with this GO.

### Independently verified state and acceptance

- Branch `main`; HEAD, local `origin/main` and live `refs/heads/main` all
  `1c169de350f59f3bc5622cc9f92925b3376d29d4`. Empty index; 77 modified tracked
  files and 91 untracked files, including this record and current-work.md.
- Generated client SHA-256:
  `9f1e5825b0f233d6de0802001b6d2a98c31ceddeda1ff1ba2f4b2324655b1417`.
- R1-R5 are closed: fresh write-locked terminal recording with one save/commit;
  fail-closed refusal handling in all four supervisors; decision/snapshot
  coherence; run-plus-setting frontend ownership; real-Kestrel 8 KiB body bound;
  and the provider-neutral port, responsibility folders and split types.
- Independently rebuilt with zero warnings/errors. Focused sequential tests:
  Domain CodexAccountUsage 102; Application AccountUsage plus startup read-only
  reconciliation 117; Infrastructure CodexAccountUsage 114; Api AccountUsage 47
  plus a separate 20 hosted tests across all four supervisors; Architecture 27.
  Four focused frontend files passed 115. No failures/skips in these checks.
- A temporary real-SQLite hosted probe passed with a new service provider and
  empty in-memory block list after failed terminal recording. The existing
  startup recovery/reconciliation interrupted the attempt before the planning
  supervisor, with two account observations total and zero Agent invocations.
  The probe was removed and its source restored byte-for-byte; the Api test
  project rebuilt. This is fresh-container seam evidence, not a new full Program
  browser run. Restarting only a supervisor reuses the host's guard singleton;
  restarting the normal host applies existing interruption authority. Neither
  creates an account-usage decision that never committed.
- Executor final full-suite evidence: Domain 1162, Application 4314,
  Infrastructure 1347 passed plus four existing skips, Api 1098, Architecture 27;
  Vitest 1938, harness 82, typecheck, strict e2e tsc, lint nine baseline warnings,
  and production build. Codex inspected the corrected canonical browser output:
  Chromium 11 passed and native-double journeys three passed on its first run.
  Those full suites/browser runs were not independently repeated by Codex.
- Reviewer edits are factual documentation, one test comment and test line-ending
  normalization only; executable production/test behavior remains the corrected
  executor implementation.

### Renewed publication GO after the EOF correction

The executor stopped before commit when the staged check found one extra blank
line at EOF in the new setter hook test. Its index was cleared and no publication
occurred. Codex verified every original approved file, removed only the final
extra LF byte and updated current-work.md and this record. No executable behavior
changed; previous validation remains applicable. The renewed GO replaces the
previous manifest and requires the refreshed exact hashes. The complete snapshot
is checked in a separate temporary index, including all new files; the real index
remains empty. The 77 modified plus 91 untracked inventory and baseline are unchanged.

### Publication boundaries and remaining limits

Commit only the frozen reviewed inventory, including current-work.md and this
record, then make a normal fast-forward push of `main` and verify the live remote.
Run the specified post-publication checks sequentially with full browser output.
If a check fails, preserve the failure and stop before factual closure; do not
retry to obtain green evidence or change the reviewed implementation after GO.
Once successful, a separate closure may change only this slice's current-work.md
entry with the delivered SHA, verified publication, actual checks and retained
limits. Never embed the closure commit's own SHA. Verify that push too.

The local percentage guard reserves no quota and cannot prevent usage changing
in another client or after observation. Real-provider reliability remains
unproven. A durable-recording failure keeps the attempt undispatched and blocked
in that process; a normal host restart interrupts it through existing authority,
without refund, automatic resume or new authorization. The earlier project-
selection route failure remains preserved with unproven cause/event order; a
passing run does not establish it cannot recur. Formatter baseline and existing
environment skips remain disclosed. Increment 4 is not claimed complete.

## Selected slice (2026-10-04): Explicit Codex account-usage stop at claim and dispatch

Codex selects exactly one bounded Increment 4 outcome: a human can configure an
optional run-scoped percentage stop for new Codex attempts, and the host enforces
it at claim and again before invocation. Configuration, refusal, terminal
pre-dispatch outcomes and historical evidence are visible through the real API
and cockpit. Claude remains the executor. Selection is not commit/push GO.

### Verified preceding publication and executor preflight

Codex independently verified substantive 65ab7fe156f4b7d91337b764ebfbfdf4ce885ada
on 1d61092e981e714e7a3bc5ac139423b587247bec: exactly the approved 69 paths,
matching the reviewed raw hashes and sizes (allowing Git's recorded line-ending
normalization when inspecting the historical current-work blob). Closure
1c169de350f59f3bc5622cc9f92925b3376d29d4 has that substantive parent and changes
only current-work within its entry. Branch main, HEAD, local origin/main and live
refs/heads/main equal the full closure SHA; the index and checkout were clean.
The canonical test:e2e:all script chains Chromium and journeys with &&, so its
reported exit 0 supports success of both commands. The missing captured Chromium
count remains a documented evidence limit, not an independently verified 11/11
post-publication count. Do not relabel retained checks or erase prior failures.

Expected branch main; exact HEAD 1c169de350f59f3bc5622cc9f92925b3376d29d4.
After this planner edit: nothing staged, only docs/roadmap/planner-handoff.md
modified, no untracked paths. Preserve this file byte-for-byte. Generated client
baseline SHA-256: d283da056d20b9f1ea9e14cd6ac452988a3f13226fb8c8f8c6bb90e5790b0819.
Its hash may change only through normal reproducible NSwag generation for the
selected transport changes.

### Judgment and authoritative contract

The delivered browser journeys already exercise the manual collaboration loop.
Another sampling expansion or provider metadata panel has less value than an
actual stop control still required by Increment 4. Ordinary raw Git reads remain
an explicitly recorded risk; hardening the fingerprint is a separate identity
and compatibility decision. Increment 5 lifecycle/coordinator/publication work
is not bundled. Claude account allowance, provider resume and compaction remain
unproven and unselected; add no Unknown-only controls for them.

The [official App Server contract](https://learn.chatgpt.com/docs/app-server#6-rate-limits-chatgpt)
was fetched during selection. It documents account/rateLimits/read, percentage
usage per window, a legacy view and a multi-bucket view. This supplies observation,
not an invocation entitlement, quota reservation or atomic account lock. The
existing CodexAccountAllowanceAdapter is deliberately display-only: it can omit a
malformed window while showing a healthy one. Its partial projection must never
be treated as enforceable evidence. Implement a distinct strict, bounded
observation for the selected policy, reusing the existing vetted-target and
CodexAppServerSession transport. Preserve the existing display query's contract.

Add ADR-0025 for this additive authority decision. Respect ADR-0004/0009,
0012/0013/0014 and all existing execution, budget and provenance gates. Record
that this advances only the Codex account-stop requirement; Claude and other
remaining Increment 4 requirements stay open.

### Policy, persistence and API boundary

- One optional Run setting: integer used-percent threshold 1..100; null disables
  it. No default threshold, bucket/model selector, advisory-warning control,
  override or provider-general configuration. Editable only for a recognized
  Created/Running run admitting Agent work. Set and clear use one protected MVC
  operation, mediator, bounded request, stable safe errors and atomic event.
- Snapshot the setting immutably on each new Codex attempt, including planning,
  resolution, ordinary/re-review CodeReviewer and verification diagnosis, and
  their existing format repairs. A later setting change applies to later claims,
  not a previously claimed attempt. Existing attempts and runs migrate to null;
  no historical policy is invented. Claude, Simulated and Process are unaffected.
- Use the established exact stored-integer reading for the two new numeric
  columns. Malformed stored configuration or attempt snapshots fail closed
  without coercion or materialization exceptions; valid set/clear repairs the
  Run configuration. Concurrent settings/lifecycle changes cannot be overwritten
  by stale tracked Run state or allow a claim on a different setting.
- Add one nullable canonical, versioned, bounded (at most 8 KiB UTF-8) dispatch
  decision snapshot on Attempt, recorded once: claimed threshold when valid, host retrieval
  instant, fixed decision/reason and only the validated bucket/window facts used.
  No raw payload, account identifier, credential, path, credit data or labels.
  Failed canonical/provenance reads expose an unavailable fact without throwing.
  Historical evidence remains absent, never a guessed below-threshold decision.
- Extend the existing cockpit projection with configuration and the existing
  selected-attempt evidence with its threshold and recorded dispatch decision.
  Stage status/history must describe the new failure outcomes truthfully; do not
  duplicate the decision into a competing persistence location or every feed.

### Strict observation and decision

- The neutral Application port owns immutable copied facts, not wire models or
  mutable collections. The Codex adapter uses only the existing read-only stdio
  handshake plus account/rateLimits/read through the vetted CLI. Keep finite
  timeout, capture bounds, cancellation and process-tree cleanup; no credential
  reads, extra RPC methods, inference calls, reset consumption or experimental
  flags. Tests use local process doubles only.
- At most 16 distinct bounded identifier buckets and two windows per bucket;
  retain the existing 64-character safe identifier bound. Prefer the multi-bucket
  view when present; never merge it with legacy or fall back from an invalid or
  empty map. Require at least one usable window in every retained bucket.
- Evaluate every reported bucket and window conservatively. Any usedPercent at
  or above the threshold stops. Below-threshold facts only satisfy this one local
  guard; they never prove account access, provider readiness or sufficient quota.
- Null/absent windows may be absent according to the documented shape. A present
  malformed window, invalid percentage, duplicate relevant property/key, unusable
  bucket, truncation, excess cardinality or failed exchange invalidates the whole
  observation. Do not admit a valid subset. Known optional duration/reset fields
  must also be valid when present; do not normalize invalid values into absence.
  A non-null provider-classified reached-limit state also stops even if percentages
  are low; unknown classification text is never displayed or used to permit.
  Credits cannot override this policy. Do not sum windows or map them to models.
- A guard observation belongs to this one check and exact vetted launch tuple;
  never reuse the rail's display result, prior attempt, another check or shared
  cache. Host-retrieved evidence must lie within the current read interval, be no
  more than 30 seconds old at its commit seam, and not be future-dated. A known
  reset that has passed before that seam makes the evidence unavailable, not zero.
  These are host freshness limits, not proof of the age of the provider's data.

### Claim and dispatch seams

- All four Codex claim handlers enforce the snapshotted setting, including their
  repair branches, after existing cheap admission/budget checks and before the
  durable claim. Observation occurs outside any EF transaction. Inside the short
  claim transaction re-read authority untracked, compare the setting and launch
  tuple, validate freshness and preserve existing reservation/input/workspace
  guards. A refusal commits no attempt, number, slot, time reservation or consumed
  authorization; remove any orphan manifest as existing claims do.
- Each Codex supervisor performs a separate fresh guard read after its normal
  pre-dispatch Git capture and before MarkAgentAttemptDispatched. Bind that read
  to the exact attempt, threshold snapshot and launch tuple actually passed to
  its adapter. MarkAgentAttemptDispatched independently validates the owned facts
  and fresh stored authority; missing, malformed, expired or mismatched guard
  facts for a threshold-bearing Codex attempt can never commit the dispatch marker.
  No external work inside the gate's automatic transaction. No caller-visible API
  accepts these internal guard facts.
- A reached/unavailable dispatch guard resolves the claimed, undispatched attempt
  once to a distinct truthful terminal failed outcome through a dedicated command
  that independently validates the persisted attempt and decision. Persist the
  bounded decision and completion event atomically. No dispatch marker, provider
  invocation, semantic collaboration message or checkpoint review. The original
  claim's budgets and consumed grants remain spent. An invalid stored attempt threshold
  is an evidence-unavailable refusal, never a guessed number or raw stored text;
  the dedicated recording path must also resolve it without invoking anything. No 500ms polling retry, silent
  stranding, automatic resume, format repair or refund. A subsequent human request
  remains governed by the existing role eligibility and budgets.
- Preserve every existing dispatch/source/contract/replay check. A disabled
  setting adds no allowance RPC; historical/null snapshots dispatch exactly as
  before. An already dispatched attempt is neither rechecked nor cancelled.

### Cockpit and observable benefit

Offer the control in the selected run's Usage & Evidence surface, separately
from token activity and the existing host-scoped allowance observation. Show the
configured threshold or not configured, fixed refusal copy, and historical
attempt decisions with retrieval time. Make the next-claim-only setting boundary
explicit. No UI label says eligible, safe to invoke, account remaining or live
capacity based on this guard. No automatic observation polling or request retry.

Own drafts/pending/errors/refresh by committed run plus authoritative setting
identity, using the established lifetime/flow patterns without relying on parent
keys. A stale completion cannot write or refresh the replacement selection. Keep
accepted server operations real. Failures display fixed safe copy; cached/display
allowance must never enable or authorize an action.

### Acceptance evidence and stop gates

Write representative failing-first regressions before production fixes. Detailed
shape/policy cases belong at their narrowest boundary; do not multiply every case
across all layers. Required evidence:

- Domain bounds/null/snapshot immutability and percentage equality; strict adapter
  real-stdio doubles for multi-bucket, legacy, one malformed sibling, duplicate,
  reached-state, timeout/truncation and cleanup. Returned-facts mutation must not
  change a decision. Existing display observation behavior stays unchanged.
- Real file-backed SQLite migration up/down/historical-null and storage-class
  tampering, repair and unrelated saves; all claim variants; threshold/launch
  changes at the seam with a populated tracker; orphan cleanup and zero consumption.
- Hosted coverage of all four Codex supervisors, with the real adapters and process
  executor over doubles: below at claim then reached/unavailable at dispatch gives
  zero Agent invocations, one terminal outcome/event, no redispatch after restart;
  healthy allowed path runs once. Claude and a separate unconfigured run continue.
  Direct gate callers omitting facts and changing a fact cannot bypass the stop.
- One browser scenario through normal authenticated Program composition and all
  supervisors: configure through the generated client, refuse a threshold-reaching
  request, display a terminal pre-dispatch stop, and complete a later allowed
  request. Owned closed-contract doubles and ordinary API setup; no raw SQL
  shortcut for the configured policy or production/test permission bypass.
- Detect targeted mutations: valid subset accepted, equality changed, final guard
  skipped, policy/launch seam check removed, historical/display evidence substituted.
  Restore exact source and rebuild to prevent stale mutated binaries.
- Sequential solution build and relevant full .NET suites; Vitest, typecheck,
  lint, production build, harness, then one canonical test:e2e:all with full output
  captured so each suite's counts are visible. Reproduce generated-client bytes
  after deletion/rebuild. Format baseline, audits, links and tracked/untracked
  hygiene; label retained evidence honestly and preserve failures/cleanup limits.

Stop and report rather than broaden if the official/local contract cannot support
strict observation, a gate can be bypassed, external work requires an automatic
transaction, terminal refusal cannot be recorded safely, or an existing invariant
must be weakened. No automatic account login/reset, scheduler, lifecycle/lease
release, generic workflow framework, session resume, compaction, Claude allowance,
permission changes, context expansion, dependency upgrade or raw Git redesign.
Account usage can change after observation or in another client; the provider can
reject a below-threshold invocation, and an invocation can itself cross the
threshold. This slice reserves no quota and proves no real-provider reliability.

### Delivery boundary

Return the complete unstaged, uncommitted and unpushed diff, a commit-ready
current-work entry, exact changed-file inventory, commands/results and limits for
Codex GO/NO-GO. Do not edit this planner record or select the next slice.
After a future explicit GO, one publication instruction covers the exact reviewed
substantive commit, normal fast-forward push, live verification, sequential
post-publication checks and a separate tightly bounded current-work-only factual
closure. Any material post-GO change requires re-review. The review decision above
authorizes only its exact corrected snapshot; selection itself was not publication
authorization. Increment 4 is not complete.
