# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md),
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Selected slice (2026-10-05): Owner-selected immutable budgets at manual run intake

Codex selects exactly one bounded Increment 4 budget-control outcome: the owner
may choose smaller run-wide Agent claim and reserved-invocation-time ceilings
while recording a new manual run, and the existing claims enforce those exact
immutable policies. Claude remains the executor. This selection authorizes
implementation only, with no commit/push GO and no Increment 4 completion.

### Current review decision (2026-10-05): GO after R1

Codex accepts the complete corrected manual-intake budget slice. R1 is
resolved: the claim banner requires safe valid counts and agreement between
the exhaustion flag and used >= maximum before showing any count or
reached-limit assertion. Invalid evidence keeps the unknown/invalid note;
historical ceilings above 16 and coherent over-ceiling usage remain facts.
The previous NO-GO below is historical and superseded by this decision.

Verified state before this planner edit: main; HEAD, local origin/main and
live refs/heads/main all e0ab52676c70e8d9c9d0760343e5607ec4aed8f4; empty index;
35 paths (26 modified, 9 untracked), including this record and current-work.md.
The executor preserved the NO-GO record at 960f77f1...b3b. The generated client
is unchanged at c45dde6e9217a6bc1803c0993168c80dc571172120ef23e9b47cf35253d145db.
Only this planner record is changed by Codex for this GO; all implementation,
other documentation and test bytes remain the corrected submission.

Fresh independent correction review: four affected frontend files, 169/169;
changed-file hygiene over all 35 paths and 291 local Markdown links, clean.
Codex inspected r1-e2e-all-canonical.txt: Chromium 12/12, journeys 4/4, exit 0.
The first review's independent build (0 warnings/errors), Application 49/49,
Api 63/63 and Architecture 40/40 remain applicable to unchanged backend and
generated-client bytes. Full frontend 2162 and the other fresh correction
checks are executor evidence, not additional independent reruns. The accepted
factory-equivalence coverage and the disclosed provider/consumption limits
below stand. Formatter cleanliness and real-provider reliability are not
claimed; Increment 4 is not complete.

GO is limited to the frozen external 35-path reviewed publication manifest,
including this GO record and the commit-ready current-work.md. The publication
instruction supplied in chat defines its exact path and hash and covers one
substantive commit with the expected parent, normal fast-forward push,
fetch/live-ref verification, fresh sequential checks and a tightly bounded
current-work-only factual closure. No other edit, dependency, authority change,
amend, force push, history reconciliation or next slice is authorized.
Any material difference from the frozen snapshot returns to review.

### Historical review decision (2026-10-05): NO-GO, bounded correction R1

Codex independently verified main, HEAD, local origin/main and live
refs/heads/main at e0ab52676c70e8d9c9d0760343e5607ec4aed8f4. The index is
empty; the complete submitted tree has 34 paths (26 modified, 8 untracked),
including this planner record and current-work.md. The executor preserved the
selection record at raw SHA-256
866e6e551791ca1af35c5c91b17df496573789c5bab9b5b7ebb44fea5dd31db8.
The generated client remains
c45dde6e9217a6bc1803c0993168c80dc571172120ef23e9b47cf35253d145db.
This review edit changes only the planner record and does not authorize
staging, committing, pushing or selecting another slice.

R1 [P2]: AgentClaimBudgetBanner must validate the complete claim projection
before asserting exhaustion or displaying a count. Currently exhausted=true
bypasses coherence: maximum=4, used=0 renders a reached-limit alert with
0/4 used; a missing maximum also asserts exhaustion. Number.isInteger admits
9007199254740992 as a trustworthy count. Codex reproduced all three through
the actual component rendered with ReactDOM server after TypeScript
transpilation; the coherent 4/4 exhausted control remains correct. Existing
focused frontend tests pass because the invalid matrix covers only the
non-exhausted branch.

Write regressions first for both exhaustion-flag directions, missing or
invalid counts under exhausted=true, and unsafe/non-finite counts. Show the
existing distinct unknown/invalid note, with no count and no reached-limit
assertion, unless both counts and the boolean flag are coherent. Admit safe
positive ceiling and safe non-negative usage; exhaustion agrees with
used >= maximum. Keep valid historical ceilings above 16 and coherent
usage above the ceiling: these are display facts, not intake range rules.
Preserve the valid exhausted alert, quiet pre-exhaustion fact, and all existing
action-block derivation. No generic decoder, backend, contract or claim change
is required. Update the current-work entry with red/green evidence and fresh
versus retained checks; preserve this planner record after the review edit.

Codex acceptance assessment: the persisted-property equivalence bridge,
existing non-default count/time/equality/race/repair suites, unchanged claim
code and new real-host production-created planning scenes adequately connect
intake to existing enforcement. The eight-file Architecture audit alone is
not behavioral proof; it supplements that evidence. The disclosed lack of
new production-created failure/interruption scenes is accepted reuse of the
unchanged mechanism, not a claim that those scenes were newly run. No duplicate
claim framework or eight new end-to-end scenes are requested for R1.

Independent review checks on the submitted tree: solution build
--no-restore -p:UseSharedCompilation=false -m:1, 0 warnings/errors;
Application ManualRunBudgetCreationTests plus RunIntentCreationTests, 49/49;
Api CreateManualRunBudgetEndpointTests plus ManualRunHostedTests plus
CreateManualRunEndpointTests, 63/63; full Architecture, 40/40; four frontend
intake/budget-display files, 70/70; changed-file hygiene and 291 local Markdown
links, no issues; git diff --check clean apart from ordinary CRLF notices.
Codex inspected the executor's fresh canonical output: Chromium 12/12,
journeys 4/4, exit 0. The full suites, mutations and other reported checks
remain executor evidence, not independent reruns by Codex.

Return the complete corrected unstaged, uncommitted and unpushed diff in this
same executor chat. Run the affected tests first, then full frontend Vitest,
typecheck, lint and production build; harness before one canonical
test:e2e:all with fresh complete output. Do not rerun unchanged failures for
green. Backend results may be retained if backend/client/tests stay unchanged;
label them as retained. Include hygiene for tracked and new files and local
Markdown links. No publication GO exists.

### Verified publication and executor preflight

The checkpoint-comparison slice is published. Substantive commit
`af2a221c035c39f25e2e5642ddada2d8cf576230` has parent
`7552cbe2772a6103d0f5c7caf464f2b0a15e8b6f`; its 47 changed paths, with rename
detection disabled, equal the approved manifest. Codex compared the 46 current
unchanged/absent paths other than the closure-edited current-work.md against the
manifest's raw hashes and sizes. Closure
`e0ab52676c70e8d9c9d0760343e5607ec4aed8f4` has that substantive parent and
changes only the prior slice's current-work.md entry (two insertions, one
deletion). The closure preserves its failure evidence and remaining limits.
The executor reports the post-publication checks in that entry; Codex has not
repeated them after publication.

Before this selection edit, Codex independently verified `main`, HEAD, local
`origin/main` and live `refs/heads/main` all at
`e0ab52676c70e8d9c9d0760343e5607ec4aed8f4`, with an empty index and a clean
checkout. Expected executor state AFTER this edit: the same branch and all
three refs, nothing staged, ONLY docs/roadmap/planner-handoff.md modified,
and nothing untracked. Generated client SHA-256 remains
`e1540273dab05124e8e7ca8f62863171efd6944c8c1fab0e53d6295074cc89bc`.
Verify once before editing and preserve this planner record byte-for-byte.

### Architectural judgment and comparison of candidates

Run.RecordClassifiedIntent already accepts a maximum Agent-attempt count and
an invocation-time ceiling. RunIntentRecorder currently uses its defaults;
CreateManualRunRequest/Command and RunIntakeForm expose only the objective.
All eight Agent claim handlers read the persisted MaximumAgentAttempts and
MaximumAgentInvocationTime; the cockpit already projects the two independent
budgets. Therefore a creation-time choice can connect intake, persistence,
claims and truthful UI without a new guard, provider flag or mutable policy.

The benefit is an enforceable smaller commitment for a particular objective:
for example, four Agent claims and 40 minutes of reserved invocation time,
instead of accepting 16 and 120 automatically. These remain reservations and
permanent claims, not measured elapsed time, token/cost caps, provider account
allowance or assurance that an objective can finish within the chosen limits.
A ceiling below the first role's configured timeout is valid and prevents that
claim; do not adjust the timeout or promise initial eligibility.

Alternatives compared and NOT selected:

- Another context sampler or historical limit display adds less value than
  making existing enforced budgets configurable at their safe creation seam.
  Generic instruction, untracked and tracked delivery and human comparison
  have just been closed; raw fingerprint hashing remains a separate decision.
- Claude account-usage stop/warning: current official usage documentation
  describes /usage and last-known bars, but this review has not established a
  bounded headless observation contract for the host's authenticated account.
  No credentials, undocumented endpoints, UI scraping or session-cost proxy.
- Provider-session resume/manual compaction: Codex documents explicit session
  resume and App Server compaction, and Claude documents resume. Current
  adapters deliberately use ephemeral/no-session-persistence execution.
  The existence of those commands does not prove owned retained-history
  isolation, preservation of role/schema/permission envelopes or safe replay.
  Session correlation is already recorded; exposing an Unknown-only shell is
  not a meaningful substitute for proving resume.
- New stage coordination, terminalization, commit or publication authority
  belongs to Increment 5/6 and is not appended to this budget slice.

Official contracts checked during selection:
[Codex non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode),
[Codex App Server](https://learn.chatgpt.com/docs/app-server),
[Claude CLI](https://code.claude.com/docs/en/cli-reference) and
[Claude usage](https://code.claude.com/docs/en/costs).
No real provider ran. The selected behavior requires no new external capability.

Read ADR-0012/0013 for permanent consumption and historical policies and
ADR-0014 for serialized manual intake and mode isolation. Add ADR-0028 for
this choice: it narrowly advances their fixed-default creation behavior for
NEW manual runs only, preserving immutability, consumption and every existing
historical policy. Do not alter old ADRs silently or impose the new transport
range on historical/domain-created runs that legitimately exceed 16 claims.

### Objective and precise boundaries

1. Extend the existing protected POST /api/runs/manual and its one mediator
   command with optional nullable integer members maximumAgentAttempts and
   maximumAgentInvocationMinutes. Missing or null independently chooses the
   existing default (16 claims, 120 minutes). Explicit accepted ranges are
   1..16 claims and 1..120 whole minutes. Invalid numeric values are refused
   through model binding/validation, never clamped, rounded, ignored or treated
   as defaults. No other public creation or update operation gains these inputs.
2. Pass the effective values through the existing serialized intent creation
   to Run.RecordClassifiedIntent, persist them in the EXISTING columns and use
   exactly TimeSpan.FromMinutes for the validated whole-minute input. The run,
   intent event and project execution-number reservation remain one atomic
   creation. No extra event kind or storage location is needed. Preserve the
   objective, authorization, active-run admission, conflict mapping and event
   semantics. Shared creation code may carry explicit policy values but must
   leave simulated creation and its defaults unchanged.
3. Keep both ceilings immutable after creation. No setter, clearing, increase,
   reset, refund, override, policy version or attempt snapshot is selected.
   Claims keep their existing permanent consumption, timeout reservations,
   fail-closed evidence checks, race handling and correction-authorization
   semantics. No budget bypass or new claim algorithm. Audit all eight claim
   paths (including read-only repairs and diagnosis-origin correction) and
   prove their existing policy reads honor non-default persisted ceilings.
4. In the manual intake form add clearly labelled numeric drafts, initially
   16 and 120, with honest whole-integer validation and explanatory copy:
   claims count even if they fail or are interrupted; time is reserved from
   configured attempt timeouts, not measured elapsed time; ceilings cannot be
   changed later and a small ceiling may prevent the first claim. The demo
   action ignores these manual drafts and remains separately labelled.
5. Treat objective and both budget drafts as ONE project-owned submission
   snapshot. Every edit advances its version, including editing back to the
   same value. Accepted completion clears/resets only the unchanged submitted
   draft of its current project lifetime; an edit, A->B->A or unmount must not
   let an old completion overwrite drafts, notices, errors or a newer guard.
   Preserve same-tick duplicate protection across manual/demo submissions and
   accepted-server-operation semantics. Use existing ownership mechanisms;
   no storage, cache, global bus, polling or remount workaround.
6. Show the actual immutable claim ceiling/used count and reservation
   ceiling/reserved/remaining from the existing cockpit projections, including
   before count exhaustion, so the choice is confirmable after creation and
   reload. Preserve distinct unknown/invalid/exhausted states and the existing
   global block derivation. Positive remainder never proves role eligibility.
   Regenerate the client through the build, never edit it. Narrowly update the
   cockpit specification, protocol, engineering context, ADR index and roadmap.

### Exclusions and stop gates

No existing-run budget mutation, nulling historical time policy, migration,
backfill, dependency, provider invocation contract, session behavior, token or
account guard, model/effort policy, turn limit, correction allowance, context
manifest, fingerprint, lease, scheduler, stage coordinator, run completion,
replacement, retry/recovery or remote mutation is selected. Exhaustion still
neither finishes a run nor permits replacing it. Keep both older owned e2e
roots untouched. No real-provider run is needed.

Stop and report if the objective needs changing claim/dispatch authority,
adding mutable budgets, increasing a default ceiling, normalizing historical
policies, creating a second budget store or changing normal authenticated host
composition. Any preflight discrepancy or unexplained canonical browser
failure is a stop gate; do not reconcile history or rerun unchanged for green.
Report a discovered existing enforcement defect rather than broadening scope.

### Acceptance evidence and complete review return

- Write regressions first for ignored non-default choices. Prove absent/null
  defaults, either field alone, both boundaries, invalid/zero/negative/fractional/
  overflowing/string inputs, and atomic persistence on real file-backed SQLite.
  Refused intake or a competing creation leaves no run/event/counter advance.
  Preserve authentication and simulated/default request compatibility.
- On production-created manual runs, prove claim-count exhaustion and a
  reservation too small for the next configured timeout independently refuse
  before provider probes, Git capture, seals, claims or grants. Include equality
  acceptance at the reservation boundary, permanent consumption after failure/
  interruption, unaffected unrelated runs, and malformed reservation evidence.
  Use a focused eight-path/repair matrix with existing scene builders for the
  shared budgets; do not duplicate each path's entire eligibility suite.
- Prove persistence/reload and existing historical >16-count and null-time
  behavior remain unchanged. No migration or new per-attempt budget field.
- Render real form/hook composition for numeric validation, whole submitted
  draft ownership (including identical-value edits), pending project changes,
  A->B->A, accepted late operations and duplicate manual/demo activation.
  Check displayed immutable non-default budgets and refusal copy without
  treating a reservation or an advisory value as provider eligibility.
- Add one owned authenticated Chromium flow using the generated client over
  real HTTP: select smaller budgets through the form, read the recorded
  cockpit values, reload and confirm them; prove a too-small time budget
  refuses an explicit planning claim without invoking a provider. Keep normal
  composition and all supervisors; use the established bounded native-double
  fixture or its allowlisted invocation facts, not a production bypass or raw
  SQL substitute for creating the budgeted run. Existing journeys stay intact.
- Restored targeted mutations must detect defaults substituted for selected
  values, ignored transport range, and one budget draft omitted from the
  version/ownership check. Rebuild backend mutations to avoid stale binaries.
- Run affected tests first, then fresh solution/client generation, full affected
  Application and Api suites, full Architecture, relevant Domain/Infrastructure
  suites when changed, full frontend Vitest/typecheck/lint/build, strict e2e
  TypeScript, harness, then ONE canonical test:e2e:all with fresh full output.
  Run applicable formatter/audits/hygiene and local Markdown links. Report
  baseline findings/skips, exact commands, failures and retained evidence.
- Return the COMPLETE unstaged, uncommitted, unpushed diff with a commit-ready
  current-work.md entry, inventory, evidence and limits for Codex GO/NO-GO.
  Preserve this planner file. Keep corrections in the same new executor chat.
  No stage, commit, push or next-slice authorization is granted.

After a future GO, one publication instruction will cover the frozen reviewed
substantive commit, normal fast-forward push, independent live-ref verification,
checks and narrowly bounded current-work-only factual closure. Any material
post-GO change returns to review before publication.
