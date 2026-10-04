# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md), the
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Selected slice (2026-10-04): Claude-reported model context limits in attempt evidence

Codex selects exactly one bounded Increment 4 outcome: after a Claude attempt
concludes, its existing historical evidence view shows the model identifiers
listed in the provider result and each model's reported context-window and
maximum-output token limits. The result is a durable observation for that
attempt, available after restart, rather than a live capacity or eligibility
claim. Claude began implementation in one executor chat. The owner subsequently assigned
Codex completion of this slice after Claude credit exhaustion. That reassignment
authorizes this correction, not implementation of the next selected slice.

### Historical review decision (2026-10-04): NO-GO, corrected below

Independent review confirms main, HEAD, local origin/main and live
refs/heads/main at 122d4ebaa81f4d5899cf5e69e916de33bc67318c. The index is empty;
the submitted inventory is exactly 78 paths (47 modified, 31 untracked).
The executor preserved the selection record at SHA-256
6d68f53e6bf4f3e234daec8342b6e4f999878c55563f142ecf3281eecaddadd5 before this
planner review edit. The regenerated client is
d283da056d20b9f1ea9e14cd6ac452988a3f13226fb8c8f8c6bb90e5790b0819.
This is a correction of the selected outcome in the same executor chat,
not a new slice or publication permission.

Three findings must be corrected:

1. R1, immutable evidence and carriage. AgentModelContextLimitsEvidence.Models
   exposes the actual List returned by Order through IReadOnlyList. Both
   Create and FromPersisted permit a caller to cast it to IList and replace
   entries after validation. Serialize and the Attempt completion backstop
   then record those changed values. A reviewer probe changed a validated
   window from 200000 to 777 and recorded 777; it also replaced a restored
   entry with window -1/output 0, recorded terminal completion and
   stored that invalid snapshot, which the read side subsequently hid.
   AgentModelContextLimits similarly retains the caller's mutable collection.
   Give both boundaries their own immutable snapshot with no writable
   collection exposed or shared backing storage. Preserve ordering, whole-map
   admission, 16 entries and the 4 KiB bound. Prove source-collection mutation
   and mutation through the public surface cannot change reported values or
   serialization, including FromPersisted and the completion transitions.
   Merely exposing IReadOnlyList or wrapping a caller-owned list is insufficient.
2. R2, bounded non-throwing recording validation. AgentModelContextLimitsRecording
   dereferences and materializes limits.Models before checking shape/count.
   The reviewer probe reproduced ArgumentNullException for a null collection,
   NullReferenceException for a null entry, and enumeration of a 17-entry
   collection before its count could be refused. Check the collection and
   count before traversal/allocation, safely refuse null entries, and return
   the existing fixed invalid-evidence error without producing Domain evidence
   or mutating the completion. Do not replace this with a blanket exception
   catch or relax the parser's all-or-unknown contract. Add regressions for
   these inputs and the three recording handlers' unchanged state on refusal.
   Coordinate construction and validation with R1 so freezing does not
   introduce an unbounded copy before the admission check.
3. R3, preserve the older test fixtures' facts. HistoricalAgentAttemptRow now
   inserts only a small subset of an Attempt that the previous EF seed
   persisted in full. A reviewer probe invoked the modified SeedFreshAsync
   on the latest migrated SQLite schema: workspace, checkpoint, fingerprint,
   manifest artifact, protocol, permission profile, adapter version, both
   capture bounds and budget slot were all NULL despite the claimed Attempt
   supplying them. The same reduced helper replaces two historical seeds.
   Keep ordinary EF insertion for a latest-schema seed; for an older schema
   write the existing applicable scalar facts faithfully, omitting only
   columns that do not exist there. Assert their preservation through upgrade,
   alongside the existing messages, inputs and authorizations. Do not weaken
   assertions or fabricate values. The two additive down-migration column
   expectation updates are appropriate and are not a separate finding.

The Claude-only detail section and the closed ASCII identifier admission remain
within the selected contract. No request to broaden either is made. Preserve
the nullable additive API, invocation behavior, observation-only meaning,
current schema boundary and all prior risk disclosures.

Independent checks on the submitted tree: solution/NSwag build 0 warnings and
0 errors; Domain model-limit filter 96/96; Application model-limit filter 35/35;
Infrastructure adapters/new migration/affected older migration filter 157/157;
Api model-limit endpoint filter 12/12; the three actual hosted supervisor classes
99/99; Architecture 13/13; focused Vitest display/detail tests 16/16.
No test in these selections was skipped. The external reviewer probes above
exercise gaps those green suites do not cover. The client regenerated unchanged.
The reviewer did not repeat the full suites or browser runs after establishing
the blockers; the executor's reported full validation remains submitted evidence,
not independent proof of these invariants.

Write the focused regressions first against the submitted behavior and retain
their red evidence. Correct only R1-R3, update the existing current-work.md entry
with actual fresh/retained checks, and return the entire unstaged, uncommitted,
unpushed diff for re-review. Run affected checks first, then the relevant full
backend and frontend validation for the final code; harness must pass before one
canonical test:e2e:all. Retain unexplained failures instead of rerunning an
unchanged tree until green. Preserve this planner-owned record byte-for-byte.
No stage, commit, push, next-slice selection or Increment 4 completion is
authorized. Any future GO will be bound to a newly reviewed snapshot.

### Final review decision (2026-10-04): GO for the corrected 80-path snapshot

The owner explicitly assigned Codex current-slice execution after Claude credit
exhaustion. Codex completed the bounded correction and final review; it does not
claim an independent review of its own edits. R1-R3 above are resolved: owned
ImmutableArray snapshots, count admission before entry reads (zero reads for an
excessive counted collection; at most 17 for an uncounted sequence), fixed
malformed-input refusals with unchanged completion state, and faithful historical
fixtures with ordinary EF latest-schema seeds and non-vacuous fact comparisons.

The reviewed tree is main on 122d4ebaa81f4d5899cf5e69e916de33bc67318c, with an
empty index and exactly 80 paths (48 modified, 32 new). The client SHA-256 is
d283da056d20b9f1ea9e14cd6ac452988a3f13226fb8c8f8c6bb90e5790b0819.
current-work.md records the fresh full suites: build 0 warnings/errors; Domain
1056, Application 3954, Infrastructure 1149 + 4 existing environment skips,
Api 997, Architecture 13; Vitest 1802, typecheck/strict e2e/build clean, lint
9 baseline warnings, harness 69, then one canonical Chromium 11 + journeys 2.
It preserves the frontend synchronization failure and sandbox scratch-directory
refusal, their bounded/environment corrections, retained audits and mutations,
and the 153-finding formatter baseline. Observation-only scope and all limits
remain unchanged. No real-provider reliability or Increment 4 completion is proven.

Publication instruction: bind all 80 reviewed paths to a raw hash/size inventory;
verify that snapshot, empty index and unchanged local/live baseline before staging.
Commit exactly that substantive snapshot including current-work.md and this GO,
then normally fast-forward push main to origin/main. Fetch and independently
verify HEAD, local origin/main and live refs/heads/main equal the delivered SHA
and the checkout is clean. Run the solution build, affected Domain/Application/
Infrastructure/Api selections and full Architecture, frontend checks, harness,
then one canonical test:e2e:all after harness success. Record actual counts and
skips separately from retained full-suite evidence. Only after these succeed,
make one current-work.md-only factual closure within this slice entry, normally
push it and verify all three refs and a clean tree again. No amend, force push,
history reconciliation, silent failed-check retry or unrelated edit. Any material
change after this GO requires review again. The next slice is selected only after
verified closure; this GO authorizes no next-slice implementation.

### Independently verified publication and new baseline

Branch main; HEAD, local origin/main and live refs/heads/main:
122d4ebaa81f4d5899cf5e69e916de33bc67318c. Empty index and clean checkout before
this planner edit. Substantive ba5797a0be898a7a50c7a400056ed891ce3c47d5 has parent
5c9dace0f976025ad71dfad11bb73c9468343236 and exactly the approved 16 paths.
All 15 files outside the closure match the approved raw SHA-256 and size.
The substantive current-work.md Git blob matches the approved raw SHA-256
9c3da1e373077f501f08d57222cd09145a99eaf1708b407e1d6b3a1ecf21bcc5.
Closure 122d4eba has parent ba5797a0 and changes only that delivery entry
(+5/-1). The approved inventory manifest itself matches SHA-256
71105170a67fe76e9e8505a144a1331de6b59a39830ad901ed06f8db99e504a8.
The prior publication is verified and its snapshot-specific GO is spent.
No implementation suite was repeated merely to verify an identical publication.

Reported post-publication build and filtered Infrastructure/Application/Api/
Architecture checks, harness and canonical browser suites match the review
instruction, including the two explicit file-symlink skips; current-work.md
distinguishes these from retained full suites, mutations and other checks.
Generated api-client.ts remains
13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a.

Expected executor preflight after this edit: the same branch and full HEAD,
nothing staged, only docs/roadmap/planner-handoff.md modified, no untracked
paths. Preserve this planner-owned record byte-for-byte.

### Evidence and candidate comparison

The three Claude adapters already parse one bounded JSON result envelope from
--print --output-format json. ClaudeCliTokenUsage deliberately ignores modelUsage.
The existing attempt-evidence GET and history detail are suitable bounded,
authenticated inspection surfaces; neither needs a new provider call.

The official [programmatic CLI guide](https://code.claude.com/docs/en/headless)
identifies print mode as the CLI form of the Agent SDK. Its
[TypeScript reference](https://code.claude.com/docs/en/agent-sdk/typescript)
declares the modelUsage map and the contextWindow and maxOutputTokens members.
Codex fetched the official Markdown reference when the web reader rejected
the oversized HTML, and independently inspected only public schema strings
from installed @anthropic-ai/claude-code 2.1.276: the result envelope includes
modelUsage, and its entry schema declares both limits as integers. No
authenticated invocation, credential read, package installation or CLI-default
inference was used. These establish a reported-field contract, not the accuracy
of a provider's reported values or future invocation capacity.

This advances a remaining Increment 4 context-visibility gap with a complete
adapter-to-database-to-HTTP-to-browser outcome. It does not extend sampling.
Raw Git hashing and tracked-diff hard-link disclosure remain a real, explicitly
open security risk. Closing them requires a separate design that derives
delivered evidence from proven bytes; checking names before Git reopens them
would be unsound. A broader rewrite of checkpoint/diff capabilities is not
included here and remains necessary before claiming comprehensive containment.
Account stops, safe resume and manual compaction remain unselected: allowance
observations do not bind an account to an eligible CLI invocation, and a
contextWindow field does not establish a compaction/resume contract.
The Increment 5 coordinator, lifecycle and publication authorities are excluded.

### Precise implementation boundary

- Parse modelUsage in all three existing Claude adapters: critical review,
  initial implementation, and review correction, including the existing
  repair/guided/authorized/diagnosis-origin variants through those adapters.
  Use the same structurally valid, clean-exit, untruncated stdout envelope
  boundary as existing token evidence. A valid is_error envelope may retain
  this independent evidence. Non-zero exit, incomplete capture or an invalid
  outer envelope yields no new evidence.
- Retain only each map key as modelId, contextWindow as contextWindowTokens,
  and maxOutputTokens. Do not interpret a map entry as the main model, fallback,
  requested alias, authentication state or permission capability. Ignore cost,
  usage totals, canonicalModel, provider-routing fields and all other metadata.
  Do not modify existing AgentObservedModel/effort or token accounting.
- All-or-unknown bounded admission: one unique modelUsage object, 1 to 16 unique
  ordinal keys, each 1 to 128 ASCII characters matching
  [A-Za-z0-9][A-Za-z0-9._-]*; no normalization or alias substitution.
  Every entry must be an object with exactly one of each required numeric
  member, positive JSON integers fitting Int32, and maxOutputTokens no greater
  than contextWindowTokens. Reject duplicate required members, malformed or
  empty maps, unsupported key shapes, overflow and over-bound data as absent
  optional evidence. Do not take the first valid subset. Preserve existing
  business outcomes, valid token usage and process evidence independently.
- Use a provider-neutral, project-owned immutable evidence value across ports
  and completion recording. Persist one nullable, versioned project-owned
  snapshot on Attempt in one new nullable TEXT column, not the raw envelope
  or a second assignment authority. At most 4 KiB UTF-8 serialized, with entries
  ordered ordinally by modelId; a fixed source tag identifies the Claude
  parsing contract. Keep serialization provider-independent. Domain validates
  the project shape; Infrastructure alone parses provider field names.
  No child-table/catalog framework or new dependency is required.
- Record the snapshot once in the existing completion transaction, including
  unsuccessful semantic outcomes where the admitted invocation evidence exists.
  Preserve atomic outcome/artifact/evidence recording and no premature writes.
  Old rows remain NULL; never backfill by reparsing artifacts. Malformed,
  excessive, unknown-version or wrong-provider persisted data projects absent
  without throwing or poisoning healthy sibling attempts. A Running,
  undispatched, non-Agent or incoherent attempt exposes none.
- Extend only the existing GET /runs/{runId}/agent-attempts/{attemptId}/evidence
  response additively with nullable modelContextLimits and API-owned nested DTOs.
  Reads use stored facts only, never an artifact read or provider probe.
  Reuse its identity, authentication and cross-run non-disclosure rules.
  Other role-status/cockpit-summary/history-list contracts stay unchanged.
  Regenerate the client through NSwag; do not hand-edit it.
- Show the nullable evidence in the existing selected historical attempt detail:
  "Claude-reported model limits", modelId, context-window tokens, maximum output
  tokens, and fixed text explaining that remaining context and next-invocation
  capacity were not measured. Call entries models listed by Claude, not models
  independently proven used. Missing evidence is "Not recorded", never zero.
  No fullness percentage, arithmetic from cumulative usage, progress meter,
  live readiness claim, new global panel, polling, probe or action.
  Preserve run/attempt selection ownership and render identifiers as text.
- Add additive ADR-0023 for the observation/persistence/display boundary,
  narrowly update its index, engineering context, protocol, cockpit contract
  and roadmap gap description, and add a commit-ready current-work.md entry.
  Preserve historical entries and all containment-risk disclosures.

### Stop gates and exclusions

Stop and report if the existing envelope contract cannot carry these fields
without changing invocation/authentication/permission/session behavior, if the
evidence cannot be bounded and persisted independently of semantic outcome,
or if the existing authenticated detail cannot render it without a broader
authority or state redesign. Do not substitute Unknown-only scaffolding.
Stop unexplained build/test/browser failures and retain their evidence rather
than rerunning an unchanged tree until green.

No provider calls in automated tests, SDK installation, new discovery/probe,
account allowance/threshold/eligibility, token stop change, model/effort selection,
resume, compaction, session persistence, provider fallback, scheduling,
autonomous verification, lifecycle/commit/push authority, manifest recapture,
sealed replay rewrite, filesystem/diff/fingerprint change, native-shell work,
dependency change or unrelated hook migration. No Increment 4 completion claim.

### Acceptance evidence and checks

1. Write regressions first proving the parent discards a concrete reported
   model map; then prove single/multiple model observations through every
   Claude adapter over process doubles with invocation arguments unchanged.
   Cover malformed/duplicate/oversized/non-integer/contradictory maps,
   truncation, invalid envelopes, clean is_error, and semantic invalid output.
   Missing model limits must preserve ordinary output and existing token usage;
   missing usage must not discard otherwise admitted model limits.
2. Prove nullable upgrade of a populated prior SQLite schema, exact round trip,
   bounded versioned snapshot, immutable recording, rollback and replay/restart.
   Exercise tampered stored shape/provider/source/status and healthy siblings.
   No artifact reparse or extra provider invocation may be needed on reads.
3. At the real MVC boundary prove the additive success/absence contract,
   authorization, foreign-run refusal, malformed persisted evidence and
   OpenAPI/client shape. A hosted completion flow must use actual Claude
   adapters/process doubles and production recording, not only SQL-seeded data.
4. Prove the historical detail's recorded and absent states, multiple entries,
   text-only model labels and run/attempt replacement behavior. Extend an
   existing native-double journey to inspect production-recorded limits through
   the generated client and rendered history detail, including persistence after
   reload; do not build another journey framework or use raw SQL as that proof.
5. Mutation evidence must detect lost completion propagation, admitted duplicate
   numeric fields and replacement of provider values with requested/default
   capacities. Restore every mutation byte-identically.
6. Affected checks first; then one final solution/NSwag build, sequential full
   Domain/Application/Infrastructure/Api suites and Architecture. Run frontend
   Vitest, typecheck, lint and production build, strict e2e typecheck, harness,
   then one canonical test:e2e:all with normal authentication, Program
   composition and all supervisors. Distinguish explicit environment skips.
   Recheck formatter against the baseline and all changed/new file whitespace,
   NUL and Markdown file/anchor links. Audits may be explicitly retained if
   dependencies do not change. No test-output/build/database files in the diff.

Return the complete unstaged, uncommitted, unpushed diff (tracked and untracked),
commit-ready current-work.md, exact inventory, client hash, actual commands and
results, fresh versus retained evidence, failures/skips and residual risks for
Codex GO/NO-GO. Keep corrections in the same executor chat. A future GO will
supply one snapshot-bound publication instruction covering substantive commit,
normal fast-forward push/live verification and tightly bounded factual closure.
Any material change after that GO returns for review. No GO is given here.
