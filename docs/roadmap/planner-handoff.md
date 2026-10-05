# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md),
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Review decision (2026-10-05): GO for the corrected warning slice

R1 and R2 are resolved. Codex approves publication of the reviewed explicit
advisory Codex account-usage warning snapshot, including this planner decision
and the commit-ready current-work entry. This GO is limited to the external
raw-file inventory issued with the publication instruction; a material change
requires re-review. No next slice or Increment 4 completion is authorized.

Verified base: `main`, HEAD, local `origin/main` and live `refs/heads/main` all
`8d18f72314ac32598788d37027f679a59cfa535b`; real index empty. Full reviewed
inventory: 81 paths, 35 modified tracked files and 46 new files (the executor's
80-path count excludes this planner record). The generated client remains
`3e4ae6cc92e09761006146432e55e221fe522eec9c23b3be66d8c80b5321b447`.
The executor preserved the NO-GO planner bytes (`0176aba8...3a1f`) until this
intentional planner-owned replacement with the final GO decision.

R1 now retains the exact freshly read, untracked execution-mode representation
and requires unchanged, still-admitted mode after the observer call. Both
admitted-mode replacements yield Unavailable/ConfigurationChanged with no
windows or timestamp. The change is local to the warning query and changes no
shared mode helper, claim, stop gate, supervisor or persistence authority. Codex
inspected the new real-SQLite controls and the red log (4 failed, 34 passed),
the corrected full Application/Api/Architecture logs (4431/1144/33 passed), and
the account-usage journey's single development run on the correction (1 passed).
R2 documents the cockpit control and separates the implemented explicit,
null-by-default Codex warning/stop from future Claude/default/waiting/resumption
policy. ADR-0026 and the protocol agree with the exact-mode check.

Independent correction checks: solution build zero warnings/errors; warning
Application 89/89 and Api 46/46; full Architecture 33/33, no selected skips or
failures. Earlier independent Domain 47/47, Infrastructure 24/24 and five
frontend files 115/115 remain evidence on their unchanged code. The original
canonical browser log (Chromium 11/11, journeys 3/3, exit zero) predates R1 and is
retained, not claimed as a corrected-handler run. No browser suite was repeated
by Codex merely to obtain green.

The feature remains one explicit advisory host-account observation at an
instant, not run-attributed usage, remaining capacity or invocation eligibility.
It never changes claim/dispatch authority and is independent of the stop. Only
owned process doubles have run; real-provider reliability, Claude account
usage, provider-session resume and manual compaction are not established.

One publication instruction covers the frozen substantive commit on this base,
a normal fast-forward push, live-remote verification, sequential checks on that
commit and a tightly bounded current-work-only factual closure. Stop on a
preflight discrepancy, push divergence or failed check; preserve its output and
do not amend, force-push, reconcile history or rerun an unexplained failure for
green. Preserve prior failure evidence and fresh-versus-retained distinctions.

## Selected slice (2026-10-05): Explicit Codex account-usage warning

Codex selects exactly one bounded Increment 4 outcome: a human can save an
optional advisory account-usage percentage warning for a run, explicitly check
it against one strict Codex account observation, and see which reported windows
reached it before the separate stop guard refuses work. Claude remains the
executor. Selection grants implementation authority only, not commit/push GO.

### Verified preceding delivery and executor preflight

- Substantive commit `ebaffc018021ac0a8880fec34b9646b738ea67b6`, parent
  `1c169de350f59f3bc5622cc9f92925b3376d29d4`: exactly the reviewed 168 paths
  (77 modified, 91 added). Codex compared the published inventory with the
  approved manifest and verified the other 167 working files against its raw
  hashes/sizes and their published Git blobs; current-work.md has the subsequent
  bounded closure. No implementation discrepancy was found.
- Closure `8d18f72314ac32598788d37027f679a59cfa535b`, parent substantive above,
  changes only current-work.md within this entry (seven insertions, one removal).
  Before this planning edit, branch main, HEAD, local origin/main and live
  refs/heads/main all matched that full closure SHA; the index and checkout were
  clean. After this edit, only this planner record is modified, with nothing
  staged or untracked. That is the new executor's exact expected state.
- Generated client SHA-256 remains
  `9f1e5825b0f233d6de0802001b6d2a98c31ceddeda1ff1ba2f4b2324655b1417`.
- Codex inspected the fresh post-publication summary and the full canonical
  browser output: build zero warnings/errors; focused Domain 102, Application
  117, Infrastructure 114, Api 47 plus 20 hosted, Architecture 27; Vitest 1938,
  harness 82, typecheck/lint/build successful; Chromium 11 and journeys three.
  Those execution results were inspected, not rerun by Codex. The stale first
  output-file launch is explicitly excluded as evidence. The earlier route
  failure and the existing SignalR negotiation console noise remain recorded.
- This publication is complete. Historical account-stop GO/preflight details
  remain in Git and current-work.md; they are not instructions for this slice.

### Architectural judgment and candidate comparison

The roadmap explicitly requires separate account-usage warning thresholds.
The current UsageEvidenceRail displays the host-scoped partial allowance and
can configure the Codex stop, but has no advisory account warning. This is a
complete user outcome with existing safe read capability, persistence, API,
React ownership and browser evidence; it does not require another execution
framework or modifications to all the role handlers.

Compared candidates were not selected:

- Claude account allowance: the official CLI/cost pages describe interactive
  `/usage`, but establish no bounded machine-readable account-observation
  contract for this host. Do not read credentials, undocumented endpoints or
  local session totals to manufacture account usage.
- Session resume and manual compaction: official Codex documentation exposes
  resume and App Server compaction operations, but this repository has not
  proved session ownership, retained history isolation, or preservation of the
  exact role/schema/sandbox/settings envelope. Its adapters intentionally use
  fresh isolated execution. Existence of a flag is not the required contract.
- Further context sampling: bounded root instructions, untracked previews and
  attested tracked text already have delivered outcomes. Another expansion has
  less immediate value than the explicit warning gap and remains unselected.
- Stage coordination, completion and publication: these are substantive
  Increment 5/6 authority changes, not additions to this Increment 4 slice.

Current official references consulted: [Codex App Server](https://learn.chatgpt.com/docs/app-server)
(account/rateLimits/read, percentages, multi-bucket view),
[non-interactive Codex](https://learn.chatgpt.com/docs/non-interactive-mode),
[Claude CLI](https://code.claude.com/docs/en/cli-reference) and
[Claude usage documentation](https://code.claude.com/docs/en/costs).
No real provider was invoked. Reuse the installed strict read contract proven
by [ADR-0025](../decisions/0025-stop-new-codex-attempts-at-an-explicit-account-usage-percentage.md);
no new RPC method is needed. [ADR-0004](../decisions/0004-use-a-structured-agent-collaboration-protocol.md),
[ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md)
and [ADR-0014](../decisions/0014-add-manual-agent-run-intake-with-durable-execution-mode-isolation.md)
retain all workflow, assignment and execution-mode authority.

### Exact outcome and boundaries

1. Add one optional run-scoped warning percentage, integer 1 through 100;
   null clears it. Historical runs keep null, with no default or backfill.
   Persist the setting and its human change event atomically. Set/clear admits
   only Agent-admitting Created/Running runs, uses fresh execution-mode authority
   and the existing lifecycle/concurrency/error contract, and contacts no provider.
   A same-value request remains lifecycle-guarded. A malformed stored value is
   Invalid, never coerced/disabled, and can be repaired by set/clear. Use the
   existing exact-storage integer mapping; unrelated saves preserve malformed
   storage without throwing. The advisory column is NOT a claim concurrency
   token: changing it must not invalidate a claim or dispatch.
2. Add a protected MVC POST `/api/runs/{runId}/codex-account-usage-warning` with
   strict `{ "percent": integer | null }` and the established 8 KiB body bound.
   One mediator command per operation. Add the saved setting to the cockpit
   through an API-owned DTO; ordinary cockpit reads perform no provider work.
3. Add a separate protected MVC GET at the same route for one explicit warning
   check, through an operation-owned mediator query. It reads the stored setting
   and vetted Codex launch afresh, uses the existing provider-neutral
   `IAccountUsageObserver` for exactly one bounded read outside all transactions,
   and re-reads the setting/mode and launch after it. A replaced configuration or
   launch cannot produce a supposedly applicable below-threshold result. No
   setting, invalid setting or unadmitted run causes no observer call. No caller
   supplies a threshold, executable, provider identity or prior observation.
4. The warning evaluator is advisory operation behavior, not the stop policy or
   a forged attempt decision. Reuse only the strict immutable observation
   contract/parser, not dispatch facts, permission evaluations, historical stop
   decisions or the partial display allowance. Evaluate every bucket/window;
   equality reaches the warning and a provider-reported reached state also warns.
   Malformed/unavailable evidence, retrieval outside its read interval, future
   dating, age over 30 seconds at response evaluation, or a passed reset means
   Unavailable. No valid subset, credit override, sum or inferred zero. Carry
   bounded safe window identifiers/percentages and host retrieval time only.
5. Keep the two thresholds independent, with no required ordering. A warning
   does not disable requests, spend budgets/grants, stop a process or alter the
   published account stop. No Attempt column, attempt warning snapshot, persisted
   provider-observation history, event for a read, manifest change or cache.
   Preserve the display-only allowance operation and its existing semantics.
6. In Usage & Evidence, show the setting and explicit "Check Codex account
   warning" action beside the distinct stop control. No warning read on mount,
   configuration save, cockpit catch-up, selection, polling or background work.
   A cleared warning adds no read. Show pending, not checked, unavailable, and
   dated last-observation below/reached states with fixed safe English copy.
   Do not label an account eligible, ready, remaining capacity or live usage.
   Warning observations concern the host's Codex account, not usage attributable
   to this run. A persisted setting survives reload; its last observation does
   not. No automatic retry, toast system or outbound notification.
7. Own drafts, pending/errors, duplicate protection, continuations and observation
   results by the committed run plus authoritative warning setting identity.
   A->B->A starts a new lifetime. Retained callbacks are inert after replacement,
   and late/overlapping responses cannot write/refresh a newer owner. During a
   read hide any apparently current classification; failure clears it. Accepted
   setting writes remain real even when their local lifetime ends. Test without
   relying on parent keys and use existing ownership utilities.
8. Add ADR-0026 for this additive advisory outcome, with no supersession of the
   stop's authority; narrow protocol, cockpit, engineering-context/index/roadmap
   updates and a commit-ready current-work entry. Keep source facts separate from
   enforcement and state that Claude usage, resume and compaction remain open.
   Use named operation contracts, explicit responsibility folders, one top-level
   type per file and the existing generated-client build pipeline.

### Exclusions and stop gates

Do not change claim handlers, eligibility feeds, dispatch gates, supervisors,
stop recording/reconciliation, invocation arguments, budgets, leases, permissions,
provider sessions or context. An architecture test may narrowly admit the new
query as an observer consumer, without weakening the stop boundary tests. No
shared warning/stop framework, new provider, dependency upgrade, account login,
reset consumption, provider nudge/email, credential read, schema generation
redesign, raw Git hardening or harness timing repair.

Stop and report if the strict existing observation is insufficient, implementation
requires a new RPC/provider contract or modifies execution authority, an advisory
write blocks claims, safe malformed-storage repair is unprovable, or normal
browser composition needs a bypass. Do not broaden or silently reduce scope.

### Acceptance evidence

- Representative failing-first feature evidence; do not count compilation errors
  as red. Bounds, equality, malformed/partial/expired observations, provider
  reached state and independent warning/stop settings at their narrowest boundary.
- Real SQLite migration up/down/historical null, storage-class tampering and
  repair, atomic value/event, terminal/lifecycle races and same-value guard.
  Concurrent warning writes have truthful atomic value/events. Representative
  Codex and Claude claims remain unaffected by a concurrent advisory change and
  malformed warning storage; no warning reference in invocation/gate paths.
- Query observer call counts (zero/one), fresh populated-tracker authority,
  setting/launch replacement during read, bounded DTO disclosure and no database
  writes/attempt consumption from checks. Protected HTTP/strict body tests,
  including the 8 KiB bound through real Kestrel and generated-client wire use.
- React frame/ownership regressions: run/setting replacement, A->B->A, stale
  handlers/completions, overlapping reads, unmount, draft edits and duplicate
  activation. Pending/failure cannot show an authoritative cached classification.
- Extend the owned account-usage browser journey with normal authentication,
  Program composition and all supervisors: save, manual below/equal warning,
  reached warning while an otherwise eligible claim still proceeds under the
  separate stop, reload of setting, clear without extra read, and the existing
  stop refusal remains unchanged. No raw SQL for the new setting or observation.
- Detect equality changed, partial/display evidence substituted, automatic read,
  lost lifetime ownership, and warning affecting claim/gate authority. Restore
  exact source and rebuild after mutations; discard stale-binary evidence.
- A final sequential solution build, relevant full .NET suites, full Vitest,
  typecheck/strict e2e tsc, lint/build, harness, then one canonical test:e2e:all
  with a unique fresh log and complete counts. Regenerate the client after
  deletion and prove identical generated bytes. Verify existing formatter
  baseline, audits, local Markdown links and tracked/untracked hygiene including
  blank lines at EOF. Preserve failures; do not rerun browsers merely for green.
  Keep the two older owned roots untouched and clean only this run's own roots.

### Delivery boundary

Use one new Claude executor chat for this slice and its corrections. Return the
complete unstaged, uncommitted and unpushed diff, exact inventory, commit-ready
current-work entry and checks actually run for Codex GO/NO-GO. Do not edit this
planner record or select another slice. The review decision above governs publication. After
GO, one instruction will cover the reviewed substantive commit, normal
fast-forward push, live verification and tightly bounded current-work-only factual
closure; any material post-GO change returns for review. Increment 4 is not
claimed complete by this selection.
