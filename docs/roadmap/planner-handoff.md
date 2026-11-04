# Planner/reviewer handoff

## Current decision: QUALIFICATION-B-CHALLENGE-SET-01 review 4, GO for exact-content publication

Verified `main`, HEAD, local `origin/main` and the live ref at
`793a4a53b7a030d9f83df3e9c34110369ea8cf26`. The index is empty, with
29 modified tracked paths including this record and six untracked files.

Close QBC-R4. The reader observes the closed facts before the message
loop. A stage that observes drift cannot reuse agreement or ask for more
message evidence, including after a return to the original facts.
Stable retries, failures, disagreements and cancellation retain their
behavior. The memory tests prove drift at the unit boundary. The
production composition exercises the normal and unchanged-retry paths
with owned doubles. Close QBC-S1. The facts and memory are separate types
in separate files. Accept QBC-R1 through QBC-R3 as previously reviewed.

Independent focused build and tests passed 200/200, including the
production-host rehearsal and the memory, verifier, session and summary
controls. Retain the executor's 328/328 focused and 973/973 matrix
results, the earlier matrices and the separate fixture results. The first
return's intermittent teardown failure remains unexplained.

The GO covers the 35 approved paths and the exact tree in the publication
instruction. Real execution requires a separate owner decision after
independent publication verification.

## QUALIFICATION-B-CHALLENGE-SET-01 review 3, NO-GO

Verified `main`, HEAD, local `origin/main` and the live ref at
`793a4a53b7a030d9f83df3e9c34110369ea8cf26`. The index is empty, with
29 modified tracked paths including this record and five untracked files.

**QBC-R4 remains open.** The comparison now captures the exact facts,
including ordered inputs, and blocks an agreement when the current
observation differs. Drift only sets the flag that stops new requests,
however. A later observation that reverts to the first facts can reuse
the old agreements. The changed-facts test explicitly expects this. If a
read fails after observing drift and the session retries, the failed
stage can qualify. Make observed drift terminal for agreement as well as
for requests. Observe terminal facts once per read before the message
loop, even with an empty listing or a zero request ceiling. Preserve
unchanged retries and the separate handling of route failure and
cancellation. Cover the original, changed, original sequence and assert
that it does not qualify or issue another request. Preserve prior failed
and disagreeing results.

**QBC-S1, open.** The engineering contract requires one top-level C#
type per file. Split `MessageEvidenceFacts` and
`MessageEvidenceMemory`, or make the facts a nested type.

The ordered-facts equality and production reader wiring are accepted.
The memory-level changed-facts control is an appropriate boundary and
does not require editing host database rows. Retain 324/324 focused and
969/969 matrix as executor evidence. Run the focused checks, then the
affected ManualQualification matrix once after correction. Preserve
QBC-R1 through QBC-R3, the unchanged fixture evidence, the successor
identities and all authorization boundaries.

## QUALIFICATION-B-CHALLENGE-SET-01 review 2, NO-GO

Verified `main`, HEAD, local `origin/main` and the live ref at
`793a4a53b7a030d9f83df3e9c34110369ea8cf26`. The index is empty, with
29 modified tracked paths including this record and three untracked files.

QBC-R1's request ceiling and failure path, QBC-R2's summary and set
comparison, and QBC-R3's input envelope checks are accepted on inspection.
The optional HTTP handler is confined to the qualification launcher and
allows offline production-composition fault tests. Accept it in scope.

**QBC-R4, open.** The correction remembers only the prior comparison enum
by message ID. A retry reads the attempt and ordered input rows again, but
returns the old agreement without checking the observation it was compared
against. For example, a route can agree with a prior input B before a
post-comparison read fails. A retry can see the expected Proposal A in
the stored inputs and reuse the earlier agreement for B. The verifier can
then combine fresh input A with route evidence that named B. Bind reused
agreement to the same attempt and comparison facts, including the complete
ordered inputs, or re-evaluate captured closed evidence against the fresh
observation. Fail closed on drift and keep the stage-wide request ceiling.
Prove stable retry success and drift refusal without another route request.

Retain the executor's 349/349 focused, 23/23 composition and 954/954
ManualQualification results, and the earlier 936/937 and isolated 8/8.
The teardown failure did not recur and remains unexplained. Retain fixture
evidence for the unchanged fixture. Check the focused paths first and run
the affected ManualQualification matrix once after the correction.
Keep the successor identities and all execution boundaries.

## QUALIFICATION-B-CHALLENGE-SET-01 review 1, NO-GO

Verified `main`, HEAD, local `origin/main` and the live ref at
`793a4a53b7a030d9f83df3e9c34110369ea8cf26`. The index is empty, with
26 modified tracked paths including this record and three untracked files.

- **QBC-R1, open.** The message-evidence request ceiling is enforced per
  read, while the stage retries a thrown read. A failed second route request
  can cause the first request to run again. Enforce the role and outcome
  ceiling for the stage, including errors and retries. Keep the existing
  polling and deadlines. A failed or unavailable reply comparison must
  fail the stage with a closed reason. Exercise the reader and session
  together with a fault on a later reply and prove the total request count.
- **QBC-R2, open.** Schema 4 can say a message belongs to the accepted
  attempt by comparing it with the observed attempt even when the stage
  identifies that observation as foreign. It can also report all replies
  linked to the Proposal when only a truncated prefix was inspected.
  Use the actual accepted ID and require a complete, non-empty listing
  for a positive whole-set lineage claim. When both sets were read,
  distinguish an empty timeline that disagrees with non-empty stored
  rows from a comparison that was never attempted. Add focused controls
  for these cases.
- **QBC-R3, open.** The per-message route comparison checks the input ID
  sequence but ignores the route's input status, omitted flag and total.
  Require a coherent complete envelope as well as the exact IDs. A
  Planner's legitimate empty input and a reviewer's one recorded Proposal
  must be distinguishable from invalid, omitted or contradictory inputs.
  Exercise a malformed envelope on a later reply.

The positive two-Challenge rehearsal and the authorization rotation are
in scope. Retain the executor's 936/937 matrix and isolated 8/8 deadline
class results. The teardown failure is unexplained. The prior slice
recorded the same symptom. Repeat the affected matrix once after the
correction, and compare with the exact baseline under equivalent
conditions if the failure recurs. Lot A, product code, shared deadline
policy, prior ledgers and retained evidence remain outside this slice.

## Selection: QUALIFICATION-B-CHALLENGE-SET-01 for offline preparation

Verified clean `main`, HEAD, local `origin/main` and the live ref at
`793a4a53b7a030d9f83df3e9c34110369ea8cf26` before this planner edit.

One authorized review-schema Lot B session
`2cb35392157b4bbaa3420c333983a9eb` produced one accepted Codex Proposal.
The Claude v3 invocation exited zero with complete I/O and a completed
`Challenged` outcome. The timeline listed two messages for that attempt.
The launcher's singleton reader projected neither message and did not
query either message-evidence route. Its verifier returned
`ReviewerMessageNotRecorded` (exit 5). The sealed input named the exact
Proposal. The summary could not establish per-reply evidence agreement.
The repository stayed unchanged. Both allowances are spent, and the
owned root was retained because descendant shutdown was unproven.

Correct the qualification reader, verifier and closed summary together.
One Planner still needs one Proposal. One CriticalReviewer attempt can
record one Acceptance or a complete set of one to five Challenges.
Check each reply against the exact Proposal and accepted attempt, expected
type, author, provenance and public message-evidence route. Check the
complete distinct ID set against the stored attempt's message metadata.
Reject missing, excessive, duplicate, mixed or incoherent replies and any
unproven evidence. Bound the number of evidence requests by the applicable
role and outcome. Keep attempt-level, sealed-input and source controls.
Version the safe summary as 4 with a bounded closed message list and
per-message comparison facts. Retain historical summaries as recorded.

Prepare one offline successor and retire the review-schema entry and
ledger name alongside earlier entries.

- Flag `--run-qualification-profile-01-challenge-set-exchange`
- Confirmation `qualification-profile-01-one-challenge-set-codex-planner-and-claude-reviewer`
- Authorization `owner-qualification-profile-01-lot-b-challenge-set`
- Ledger leaf `devalcopilot-qualification-profile-01-lot-b-challenge-set-ledger`

The composition rehearsal must cover multiple persisted Challenges from
one owned Claude double response. Preserve the one Planner and one
CriticalReviewer invocation, all process admission limits, 35-minute
bound and the launch-context checks. Product code, Lot A and past
evidence are outside scope. Return unstaged, uncommitted and unpushed.
Real execution and publication require separate decisions.

## CLAUDE-REVIEW-SCHEMA-01 review 2, GO for exact-content publication

Verified `main`, HEAD, local `origin/main` and the live ref at
`241bbb2ca181445267d1d51c390b484aea0b0dca`. The index was empty, with 29
modified tracked paths (including this record) and one untracked test.

- **CRS-R1, closed.** The CLI, both manifest forms and the fixture share
  the flat v3 schema. The three unsupported bounds are absent at every
  depth, `minItems: 1` remains, and the existing host parser and Domain
  policy enforce the original union and limits. Confirmed the parser and
  policy are byte-unchanged from HEAD. The historical version boundary and
  successor Lot B retirement and admission scope remain as reviewed.
- **CRS-D1, closed.** The ADR-0072 extension preserves the original decision.
  The provider, repair, cockpit and qualification records reflect the v3
  boundary and historical status behavior.
- **CRS-V1, validation accepted.** The corrected API matrix passed 1310/1310
  and none of the three earlier failures recurred. Retain 1301/1304 and
  76/76 as separate evidence. The earlier failures remain unexplained and
  the conditional baseline comparison was unnecessary in this round.

Independent review rebuilt the affected Application and Infrastructure test
projects and passed 139/139 focused schema, parser and repair cases and
53/53 adapter cases. The initial restricted test run aborted because the
runner could not connect to its test host. The rerun with local test-host
communication succeeded. The executor's logs also record 227/227 fixture
and 232/232 architecture cases.

Implementation and review-correction still pass bounded schemas to the
Claude CLI. Record their compatibility as unverified for separate review.
The CriticalReviewer correction covers only its own request and the fixed
Lot B successor. The reviewed fixture and doubles cannot establish live
provider acceptance. This GO covers only the 30 approved paths and the
exact tree in the publication instruction. Real execution requires a
separate owner decision after independent publication verification.

## CLAUDE-REVIEW-SCHEMA-01 review 1, NO-GO

Verified `main`, HEAD, local `origin/main` and the live ref at
`241bbb2ca181445267d1d51c390b484aea0b0dca`. The index was empty, with
26 modified tracked paths (including this planner record) and one new test.

- **CRS-R1, open.** The v3 schema removes the root union but still sends
  `minLength`, `maxLength` and `maxItems` to Claude. The [Anthropic structured
  output limits](https://platform.claude.com/docs/en/build-with-claude/structured-outputs#json-schema-limitations)
  exclude string length keywords and array constraints beyond `minItems` of
  zero or one. The adapter serializes this document directly into the CLI
  argument. No evidence establishes that the CLI strips these keywords. Keep
  the limits in the descriptions and the existing host validator. Keep
  `minItems: 1`. Cover absence of the unsupported keywords at every schema
  depth and preserve the manifest, CLI and fixture parity checks. Keep the
  parser controls for blank, overlong, oversized, mixed and unsafe payloads.
- **CRS-D1, open.** Record the narrow v3 extension in ADR-0072 with a link
  to the current provider contract. Preserve the original decision text.
  Reconcile the current contract lines in the qualification plan and the
  cockpit's CriticalReviewer facts, which recognize v1, v2 and v3 for
  disclosure. The executor's engineering-context correction and the fixture
  version update are in scope. The extension changes the transport contract
  within the existing architecture.
- **CRS-V1, evidence retained.** The executor's API matrix was 1301/1304;
  the three affected classes passed 76/76 alone. Logs confirm an unexpected
  Lot A exit, a file still open during Lot B teardown, and a Lot A test that
  observed empty output and a file still open during teardown. These are
  separate from the schema compatibility finding. Keep the results and
  rerun the affected API matrix once after the correction. If a failure
  repeats, compare its class or coupled filter against the exact baseline in
  a separate checkout under equivalent conditions. Preserve unexplained
  failures for review. Changes to Lot A or shared deadline behavior require
  separate scope.

The version boundary, historical repair-source policy and fixed successor
identities match the selected scope on inspection. No publication or real
run is authorized. Return the same slice for review after correction.

## Selection: CLAUDE-REVIEW-SCHEMA-01 for offline preparation

Baseline verified on 2026-11-04. Branch `main`, HEAD, local `origin/main`
and the live ref all equal `241bbb2ca181445267d1d51c390b484aea0b0dca`.
The index and working tree were clean before this planner entry.

The Proposal-guidance Lot B used both role allowances. Codex produced one
accepted Proposal. The Claude CriticalReviewer exited 1 with complete I/O,
`ProviderReportedError` and no review. The verified sealed stdout reported
HTTP 400 and a tool input schema restriction on root `oneOf`, `anyOf` and
`allOf`. Stderr was empty and both hashes matched. The application's
CriticalReviewer schema has root `oneOf`. The retained root and spent ledger
remain evidence. The failed attempt is outside the manual format-repair gate.

Use a closed, flat object for the Claude CLI schema and for the ordinary and
format-repair manifests. Require `decision` and `summary`, enumerate `accept`
and `challenge`, and describe the existing exclusive branch rules for the
optional `rationale` and `challenges` properties. Describe the existing
bounds in the schema. Keep the exact semantic union and all content and
length checks in the current parser and Domain policy. The emitted payload
keys, envelope reader, role restrictions and dialogue protocol remain the
same. The transport schema cannot establish that a live request will pass.

Version new CriticalReviewer attempts and repairs as
`claude-critical-review-v3`. Reject earlier versions at dispatch without
upgrading them or starting a process. Keep historical v1 and v2 attempts
classifiable and preserve their existing eligibility as format-repair sources
under the exact current outcome, clean-exit and lineage checks. A new repair
requests the current schema. Preserve the historical attempts and manifests.

Prepare one offline Lot B successor with the following fixed identities.
Retire the proposal-guidance flag, phrase and ledger alongside the older
entries. Do not inspect old ledgers or roots. Keep the launch context, two
roles, deadline, admission inventory and process ceilings. Lot A is outside
this slice.

- Flag `--run-qualification-profile-01-review-schema-exchange`
- Confirmation `qualification-profile-01-one-review-schema-codex-planner-and-claude-reviewer`
- Authorization `owner-qualification-profile-01-lot-b-review-schema`
- Ledger leaf `devalcopilot-qualification-profile-01-lot-b-review-schema-ledger`

Use offline tests to prove CLI and manifest schema parity, both accepted
forms, rejection of mixed, incomplete and unsafe payloads, historical
version behavior, retired-entry refusal and production composition with
owned provider doubles. Update the provider contract, format-repair note,
qualification playbook and implementation record. Return for review with
no staging, commit or push. A real Lot B still requires a separate owner
decision after independent publication verification.

## CODEX-PROPOSAL-GUIDANCE-01 accepted for exact-content publication

Review 1 confirmed `main`, HEAD, local origin/main and live origin/main at
`831cc1de9aab67f42984441db6cf9493dcd810d6`. The index was empty, with
11 modified tracked paths and three untracked paths.

Accept the fixed guidance in both planning manifest forms. It is rendered
from the same eight-literal vocabulary the Domain filter reads. The compiled
list exposes a read-only collection. The filter retains its rejection
semantics, and the schema, parser and role arguments stay as reviewed.
Fabricated controls show that the existing marker is still refused and a
safe paraphrase is accepted verbatim. The guidance precedes the untrusted
instruction boundary and remains inside the existing manifest budget. It
does not establish that a real provider follows it.

Accept the successor Lot B opt-in and the retirement guards. The planner
clarified the protocol wording to describe guidance rendered from fixed data.
Independent builds of the launcher and three test projects passed with zero
warnings and errors. In short isolated temp storage, 94/94 Domain, 345/345
Application and 99/99 API and launcher cases passed, including the affected
composition rehearsal with owned provider doubles. Evidence is in ignored
`logs/planner-review/codex-proposal-guidance-01-review-01/`. Retain the
executor's passing evidence and its earlier Git exit 128 failures in the
deep temp directory. Their exact cause was not isolated.

The separate GO covers only the exact 14 reviewed paths and tree.
Independently verify publication before preparing the real run decision.
The prior evidence and Defender action remain preserved.

## Offline preparation decision and evidence

The published baseline is `831cc1de9aab67f42984441db6cf9493dcd810d6` on
`main`, independently verified against the approved parent, tree, 15 paths
and live origin/main.

The Windows-root Lot B passed launch identity and process admission. Its
one native Codex Planner exited 0 with complete I/O and a sealed final
response. The application returned InvalidStructuredOutput and persisted
no proposal. The Claude Reviewer did not run. Source and worktree checks
passed, and the root was retained because descendant shutdown was unproven.
The Windows-root authorization is spent.

The authorized final-response inspection verified length and hash. The
JSON object contained all six expected string fields, with no extra,
duplicate, blank or over-limit fields. The existing content filter
matched the literal marker `environment` in `verificationPlan`. The
published parser refused the response. No response text is retained here.
Defender flagged the PowerShell command line used by the initial
diagnostic. Preserve the Defender action. That diagnostic routine remains
stopped and is outside the next work.

Prepare a small offline correction. Give ordinary and manual format-repair
Codex planning manifests a fixed host-authored instruction aligned with
the existing Proposal content policy. Put it beside the schema and before
the untrusted instruction boundary. Use the same existing marker
vocabulary for the policy and guidance. Include safe paraphrasing of
ordinary concepts so a provider can avoid blocked literals. Do not relax
the filter, modify the schema, rewrite provider output or change another
role. Historic manifests and attempts remain as recorded.

Prepare one fixed successor Lot B entry and retire the spent Windows-root
opt-in alongside the earlier ones. Preserve prior ledgers and roots.
Tests use fabricated proposals and owned doubles. Real execution and
publication require separate decisions.

## CODEX-WINDOWS-ROOT-01 accepted for exact-content publication

Review 1 confirmed `main`, HEAD, local origin/main and live origin/main at
`f979b5889eed3febc8a79b4b83ae7270e5e4a85b`. The index was empty with
15 modified tracked paths and no untracked paths.

Accept the shared invoker change. Direct native Windows role launches get
the same derived SystemRoot prerequisite as Node script launches, with an
early closed refusal if it cannot be derived. The controlled tests prove
the exact environment, refusal order and non-Windows behavior. The
qualification inventory still permits SystemRoot by name. The production
invoker is the guarantee that it is present. No discovery or App Server
scope extension is selected. The provider contract records the changed
native rule and the curl control's evidence limit.

Accept the fixed successor opt-in and separation from the two spent Lot B
ledgers. The reused retirement message does not distinguish a role
allowance from process slots. In the first Lot B only Git slots were
observed. In the context-bound Lot B the Planner allowance was consumed
and the Reviewer allowance was unused. Use the closed evidence for that
distinction.

Independent builds of the launcher and both affected test projects passed
with zero warnings and errors. In isolated test temp storage the reviewer
passed 239/239 invoker, adapter and related tests and 68/68 launcher and
rehearsal tests. The rehearsal used owned provider doubles and its
disclosed local-tool boundary. Evidence is in ignored
`logs/planner-review/codex-windows-root-01-review-01/`. Retain both of the
executor's full-family outcomes, including the Lot A failure and the
839/839 pass. This review does not establish that failure's exact cause.

The separate GO covers only the exact 15 reviewed paths and tree.
Independently verify publication before preparing the real run request.
The successor Lot B is pending a distinct owner execution decision.

## Offline preparation decision and evidence

The published baseline is clean `main` at
`f979b5889eed3febc8a79b4b83ae7270e5e4a85b`. The planner verified the prior
publication against its parent, tree, 20 approved paths and live main.

The context-bound Lot B resolved all seven pinned launch identities and
admitted every request. One direct native Codex Planner process exited 1
after 13 seconds with complete I/O and no proposal. The Claude reviewer
was not invoked. The host stopped, source and worktree checks passed, and
the root was preserved because descendant shutdown was unproven. That
authorization is spent. Preserve its ledger and evidence, including the
empty SQLite WAL sidecar created during the initial read-only inspection.

The authorized sealed stdout and stderr hashes matched. The closed
projection records WebSocket reconnects, fallback to HTTP, and a request
send failure against the ChatGPT service. No raw provider text was kept.
An anonymous HTTPS control with the same Windows curl executable reached
chatgpt.com in the ordinary environment (HTTP 403), failed name resolution
in the direct Codex environment (curl 6), and reached the server again
when only SystemRoot was added (HTTP 403). The 403 responses establish
transport, not service authorization. This control establishes a missing
OS prerequisite for the probe and supports the narrowly scoped repair.
It does not establish the Codex client's exact internal cause or that a
future model invocation succeeds.

Prepare the repair offline in the shared Codex role invoker. On Windows
derive and add only SystemRoot with the existing WindowsProcessSystemRoot
policy for native and script launches. Refuse if it cannot be derived
before scratch, sinks, or dispatch. Keep discovery and the Codex App
Server session at their existing scopes. Update the provider contract
documentation explicitly because its prior direct-native rule is replaced
for the shared role invoker only.

Prepare one fixed successor Lot B authorization for a future separately
approved run, reusing the existing context and runtime admission gates.
Retire the spent context-bound opt-in without opening, migrating or
rearming its ledger. Keep Lot A unchanged. Real execution and publication
remain pending.

## QUALIFICATION-B-CONTEXT-01 accepted for exact-content publication

Review 1 confirmed `main`, HEAD, local origin/main and live origin/main at
`646b62e7d2f6bcec12c568c60858b38bab8131e1`. The index was empty, with
12 modified tracked paths and eight untracked paths, all in the reviewed
launcher, tests or roadmap documents.

The context check uses the production resolver before any fixture, process
or marker. Accept the narrow, fail-closed reflection binding and its shape
sentinel. A resolved unapproved capability refuses before preparation because
runtime admission would refuse its probe. An ambiguous resolution also
refuses, as directed. Accept the retired opt-in, fixed replacement identity,
and destination alias protection at the documented point-in-time scope.

Independent launcher and test builds passed with zero warnings and errors.
Eight focused classes passed 112/112 in isolated temp storage. This included
the context and alias controls, persistence tests and the affected composition
rehearsal with owned provider doubles and its disclosed local-tool boundaries.
Evidence is in ignored `logs/planner-review/qualification-b-context-01-review-01/`.

The broad executor evidence remains intermittent at its recorded scope.
Its unchanged Lot B teardown failure reproduced on the baseline. For the
Lot A exit-5 discrepancy, the reviewer verified the relevant Lot A source
and tests match the baseline, then reproduced a specific mechanism in
isolation. The existing shared-temp cleanup helper can remove another
test's new status root, leaving two successful authentication observations
but unproven cleanup and exit 5. The control used an isolated temp directory,
owned doubles and exactly one owned root. This establishes a test isolation
defect and does not identify the cause of every historical failure. The
temporary control source was removed. Keep its source and TRX, the passing
focused results, and all reported failures. The test-isolation issue does
not block this Lot B change. Leave Lot A outside the approved diff.

Publication authority is limited to the parent, tree and 20 paths in the
separate exact-content GO. Independently verify publication before preparing
the actual launch context and manifest for an owner decision. Real execution
remains pending.

## Offline preparation decision and evidence

The published baseline is `646b62e7d2f6bcec12c568c60858b38bab8131e1` on
`main`, confirmed against live origin/main. The post-login Lot A reported
BothAuthenticated, complete I/O for both observations, and exit 0.

The first Lot B stopped during discovery with ProcessAdmissionRefused.
Its approved closed summary records Unlisted as the first refusal, 11 Git
admissions, and neither role attempted. The later SessionSpent result reflects
those existing process slots. Preserve this authorization's ledger and retained
root. The summary does not identify the exact refused request.

A read-only resolution control reproduced a difference between launch
contexts. In the Codex shell the production resolver selected the direct
Codex executable pinned in the manifest. With the configured Windows machine
and user PATH, the same resolver selected a Node-hosted Codex script. This is
sufficient to refuse against the direct pin, although the original process
environment was not captured. Match the manifest against the actual launch
context before the launcher creates a fixture, starts a child, or consumes a
marker.

Prepare one fixed replacement Lot B authorization offline. Retire the spent
opt-in and preserve the existing admission inventory, deadlines, role vectors,
evidence rules and production behavior. The existing runtime gates still
check every request. Use the real production target resolver without running
its probes. Keep this check inside bounded preparation. A mismatch needs a
closed, capability-specific reason without printing paths or environment
values. Real execution and publication require separate decisions.

## QUALIFICATION-A-POST-LOGIN-01 review 3 accepted for exact-content publication

Review preflight confirmed `main`, HEAD, local origin/main and live
origin/main at `79e6b8f22347711261042a60ffd02600e6542099`, an empty index,
nine modified tracked paths and no untracked paths.

**PAL-R2 closed.** Initial and pre-enumeration metadata checks run in bounded
preparation. Each checks expiry before using its answer. The held-check tests
return exit 8 and show no process, slot or root after release. PAL-R1 remains
accepted at its documented destination and point-in-time scope. PAL-V1 remains
closed.

Independent launcher and test builds passed with zero warnings or errors.
Five focused classes passed 97/97 in an isolated test temp directory, including
the held-read, alias, deadline, persistence and finalization cases. Evidence
is in ignored `logs/planner-review/qualification-a-post-login-01-review-03/`.
Retain the executor's 315/315 and the earlier results at their recorded scopes,
including the unresolved Lot B teardown failure from review 2.

The separate exact-content GO identifies the approved parent, tree and nine
paths. After publication, verify the live ref and published content
independently. The real post-login comparison requires a separate concrete
owner approval. Lot B remains pending.

## Review 2: correction 1 assessed

The review preflight confirmed `main`, HEAD, local origin/main and live
origin/main at `79e6b8f22347711261042a60ffd02600e6542099`, an empty index,
nine modified tracked paths and no untracked paths.

- **PAL-R1 alias behavior accepted at the destination check scope.** The
  correction refuses a junction to the retired ledger before enumerating its
  markers and checks the destination before normal and expiry saves. All five
  new regression cases pass independently. This covers the destination itself
  at the time of each check. It does not prove ancestor identity or exclude
  concurrent path replacement between the check and use.
- **PAL-R2 P2 open for the bounded caller.** The new initial call to
  `IsAliasedLedger` in `RunAsync` performs synchronous `File.GetAttributes`
  before the preparation task is started and the waiter can observe expiry.
  This makes the caller wait for filesystem I/O on the very path whose outer
  bound was previously independent of blocking preparation. This is a source
  finding. Keep destination metadata work inside the bounded worker or
  emission paths, with the check before marker enumeration and summary writes
  retained. Verify a held initial metadata check cannot prevent bounded return
  or allow late preparation to start anything.
- **PAL-V1 closed.** The reserve test samples the work window before the whole
  window and retains its work-expired/outer-open check. The independent run
  passed it. Keep the historical executor failure and the baseline reproduction.

Independent builds passed with zero warnings/errors. The same focused suite
in an isolated test temp directory returned 311/312. All post-login entry and
reserve cases passed. The sole failure was unchanged Lot B test
`LauncherOuterDeadlineTests.A_synchronous_first_snapshot_inside_the_session_cannot_hold_the_entry_past_its_bound`,
whose teardown could not delete a summary file that was still open. This
review has not isolated the cause on a baseline build. Preserve both the
executor's 312/312 and this outcome. Evidence is in ignored
`logs/planner-review/qualification-a-post-login-01-review-02/`.

Correction 2 is authorized offline in the same executor chat, limited to the
bounded metadata check and its focused evidence. The unrelated teardown
failure grants no Lot B change. Real execution and publication remain pending.

## Review 1: first return assessed

The first Lot A completed with BothLoggedOut and consumed both process slots.
The owner subsequently reported successful Claude Code login. The post-login
comparison needs its own one-shot authorization. Lot B remains pending.

Review preflight verified `main`, HEAD, local origin/main and live origin/main
at `79e6b8f22347711261042a60ffd02600e6542099`. The executor returned seven
modified tracked paths, with an empty index and no untracked files. The
reviewer's handoff update adds one tracked modification.

- **PAL-R1, P2 open.** The new fixed opt-in, distinct authorization and ledger
  name, retirement of the original flag and two-start envelope are in scope.
  The retired ledger guard compares only the last path segment. If the new
  ledger path is a junction to the retired one, `AnyConsumed` enumerates the
  target before the alias check in slot admission. The independent control
  used a junction and a fabricated original marker. It returned spent (exit 3)
  rather than refusing the alias (exit 2). The expiry path also passes the
  unvalidated destination to summary persistence. Guard the post-login
  destination before enumerating markers or creating any summary, including
  an expiry summary. Reject aliases without following them, and keep the
  existing deadline and normal-path reporting guarantees. Keep this correction
  local to Lot A.
- **PAL-V1, baseline test defect established.** The independent test and launcher
  builds passed with zero warnings/errors. All 307 existing affected cases
  passed in the reviewer context, plus one failing reviewer control. The
  executor's 306/307 remains recorded. The deadline test and its implementation
  match the published commit. A standalone control compiling that commit's
  EntryDeadline and using the exact consecutive-read expression exceeded
  300 ms in 42 of 128 samples. The two clock reads can explain the failure.
  Correct the unstable assertion in the same offline patch while retaining
  the checks that work expires before the outer budget. Avoid broad reruns.

Evidence is in ignored `logs/planner-review/qualification-a-post-login-01-review-01/`,
including the build logs, review TRX, exact junction control and published
deadline control. The reviewer removed the temporary test source. The
controls used owned doubles and fabricated records.

Correction 1 is authorized offline in the same post-login executor chat.
Real execution and publication remain pending.

## Previous decision: QUALIFICATION-PROFILE-01 review 7 accepted for exact-content publication

Planner selection, 2026-11-02: prioritize a supported, diagnosable first real
Codex Planner / Claude CriticalReviewer exchange. The complete scope,
call/time/usage envelope, validation matrix, assessment dispositions and
pending boundaries are in [the qualification plan](real-provider-qualification-plan.md).
The owner subsequently authorized offline preparation, which the executor has
returned for review. Review 7 closes QP-R3 and retains the other dispositions
at their recorded scopes. Publication authority applies only to the parent,
tree and path set named in the separate exact-content GO. Both real lots and
selection of the next slice remain pending.

## Verified delivered baseline

- Branch `main`; local `HEAD`, local `origin/main` and a fresh live remote read:
  `38fc3847f347599430cc036849044d75d6e8bb8a`.
- Tree `37ed3def51be675263508c4bf4d5114b2f69ab20`; sole parent
  `e880c4da311f65d7688196105364f239b4f116f0`; 326 changed paths.
- Initial index, working tree and untracked set were empty. This planning action
  changes only this handoff and adds the linked plan; no stage/commit/push.
- The outgoing planner independently verified WIN-EXEC-PROFILE-01 publication
  against its approved identity. This chat rechecked local identities/count
  and the live ref, without reopening preserved publication evidence. An initial
  remote read failed; the later read succeeded.

The exact previous review record, including WEP-R1 through R5, WEP-V1/V2,
WEP-F1 through F6 and the exact-content GO, is
[this handoff at the delivered baseline](https://github.com/denisvr/DevalCopilot/blob/38fc3847f347599430cc036849044d75d6e8bb8a/docs/roadmap/planner-handoff.md).
Those findings are closed at their recorded scopes. The implementation and
validation record remains in [current-work.md](current-work.md); its prepared
phase wording does not override Git or authorize repeat publication.

ADR-0072/0073/0074 are delivered: documented Claude structured output and
closed diagnostics, bounded I/O completion, checked startup and the fixed
Windows verification environment. New executions use `windows-verification-v3`;
historical v1/v2/null records are never relabeled, replayed or redispatched.
Acceptance F passed on the fourth authorized attempt at its bounded offline
Windows toolchain boundary; this is not installed-provider qualification,
fresh external vulnerability/signature/revocation qualification, CI or
self-hosting success. Earlier unexplained failures remain unexplained.

## Planning rationale and gates

The next outcome should test the uncertain external boundary. Group the
operator, fresh authorization-scoped allowances, current diagnostic/I/O evidence
projection and the first exchange in one future slice. Two live gates remain:

- A: at most two Claude authentication-status observations comparing an ordinary
  owner-launched terminal context with the production-filtered map; no model.
- B: only after reviewed prerequisites, at most one Codex Proposal and one
  Claude Acceptance or Challenge for that exact Proposal through production.

The existing launcher summary omits ADR-0072 contract-version, I/O and diagnostic
facts. Its fixed historical ledger cannot be silently reused. Production
readiness probes all seven capabilities and refreshes at five-minute intervals;
those process requests must be counted explicitly. See the plan for proposed
caps and the outstanding local-Git command inventory required before B approval.

Offline preparation is implemented and under review. After the correction and
independent acceptance, obtain separate concrete execution decisions for A and
B. Authentication/environment repair discovered by A remains a reviewed
dependency, not automatic permission to fix it.

## Review 7: correction 6 accepted (2026-11-03)

The planner verified `main`, HEAD, local origin/main and live origin/main at
`38fc3847f347599430cc036849044d75d6e8bb8a`. The index was empty, with
27 modified tracked paths and 66 untracked paths. The reviewed set contains
93 paths in qualification support, tests and roadmap documentation.

- **QP-R3 closed.** The observer checks the absolute monotonic deadline when
  reading the wait result. A wait that reports timeout or returns a completion
  index after expiry refuses. Both failed-start fallbacks explicitly report
  incomplete I/O. The late-success and failed-start regressions pass along with
  the in-time and overflow-with-held-pipe controls. The deadline is an
  acceptance check at the decision point. It cannot prevent OS pre-emption.
- **QP-R1, QP-R2, QP-R4 and QP-V1 retain their recorded closure scopes.** Keep
  the historical executor and independent reviewer evidence, including the
  unexplained native-watch discrepancies and default NSwag failure.

Independent evidence is in ignored
`logs/planner-review/qualification-profile-01-review-07/`. The test and
launcher builds passed with zero warnings/errors using no restore, serial
builds, UseSharedCompilation=false, SkipNSwag=True and both first-run flags
false. The same eight affected test classes as the executor passed
**209/209** in 1 m 6 s. This included owned doubles and the disclosed local-tool
rehearsal. The broad matrix was retained at its prior scopes. The review ran
no real lot, installed-provider inspection or provider/authentication/account
call and made no `src/` change.

Publication follows the exact-content tree scheme. The planner's separate
GO identifies the reviewed parent, tree and explicit approved path list.
After publication, the planner independently verifies the live ref and the
published content before planning another action. Lot A and Lot B require
separate explicit execution approval.

## Review 6: correction 5 assessed (2026-11-03)

Preflight confirmed `main`, HEAD/local origin/main and a fresh live main at
`38fc3847f347599430cc036849044d75d6e8bb8a`, an empty index, 27 modified tracked
paths, 66 untracked paths and no `src/` change.

- **QP-R3 accepted in part.** Correction 5 preserves the timeout result when the
  wait returns -1 and skips the wait when its initial remaining time is zero.
  The owned test for overflow with another pipe held open now reports both
  truncation and unproven termination. Keep these fixes and their tests.
- **QP-R3 P2 open for the absolute deadline.** The successful wait branch still
  never checks the monotonic clock again. The observer passes a relative timeout
  calculated before the call. A caller descheduled between that calculation and
  the wait can receive a success index outside the acceptance deadline. A reviewer
  control uses the existing seam to delay past the supplied remaining time and
  return the completed task index. The observer returned `TimedOut: false`.
  Require the success decision to observe the absolute deadline as still open,
  while preserving the terminal timeout result from the wait.
- **QP-R3 P2 open for failed-start I/O evidence.** Both fallback constructors
  inherit `IoComplete: true` even if the executable never starts. A reviewer
  control with a nonexistent executable confirms this. Set the I/O fact
  explicitly on failure paths and align the observation comments with the facts
  taken before and after cleanup. The failed start already refuses at the gate.

The independent build passed with zero warnings and errors (11.26 s) under the
established no-restore, serial, UseSharedCompilation=false, SkipNSwag=True and
first-run settings. The observer run passed all 21 existing cases and failed
both added controls at their intended assertions. See
`logs/planner-review/qualification-profile-01-review-06/` for the build,
observer log/TRX and exact control source. The temporary test source was removed
afterward. Rebuild before further source-bound validation. Retain the executor
207/207 and earlier evidence at their recorded scopes. This review ran only
owned observer doubles. QP-R1, QP-R2, QP-R4 and QP-V1 remain closed as recorded.

Correction 6 is authorized offline in the same executor chat. Limit the patch
to the final deadline predicate, explicit failure facts, their focused
regressions and the executor records. Keep the established process budget and
qualification boundaries. Neither real lot nor publication is authorized.

## Review 5: correction 4 assessed (2026-11-03)

The returned Correction 4 is present; no replay of Correction 4 is needed.
Preflight confirmed `main`, HEAD/local origin/main and a fresh live main at
`38fc3847f347599430cc036849044d75d6e8bb8a`; empty index; 27 modified tracked
paths and 66 untracked paths, no `src/` change. The two additional tracked
paths are the existing test process fixture and qualification test base. The
four added files are the three separated types and the observer regressions.

- **QP-R3, accepted:** the file layout, real refusal of the previous 4 KiB stderr
  control, bounded byte reads and normal/incomplete-stream cases. Accept the
  documented cap of 512 bytes per stream and the separate 250 ms best-effort
  termination confirmation. The five seconds remain the observation acceptance
  deadline. The version minimum, exact Git, single admission and inventory stay.
- **QP-R3, P2 open: deadline decision.** The result of `Task.WaitAny` is discarded.
  If the wait times out and the tasks complete before the subsequent status
  reads, or no time remains before entering the wait but the tasks have finished
  by inspection, the success branch can accept the observation. This is a source
  finding, not a reproduced timing failure. Keep expiry terminal and require
  successful observation before the deadline before permitting fixture Git.
- **QP-R3, P2 open: independent failure facts.** The `!wasOversized` guard
  suppresses unproven termination even when the root exited and the other pipe
  is still held open. An independent executable control emitted 4 KiB into
  stderr from an owned descendant after the root exited. The descendant was
  confirmed alive and held stdout open; the gate refused the observation, but
  `TerminationUnproven` was false. Preserve the overflow and the unproven
  cleanup facts independently. `Task.IsCompleted` alone also cannot establish
  successful I/O: the bounded reader returns `Ended: false` on read failure.
  Only successful EOF on both streams supports `IoComplete`.
- **QP-R1, QP-R2, QP-R4, QP-V1:** closed at the previously recorded scopes.
  Retain the prior evidence and the unexplained native-watch and default NSwag
  outcomes. The new fixture mode is selected only by `--version` plus the
  owned sidecar; no broad fixture-consumer rerun is required for that addition.

Independent evidence: ignored `logs/planner-review/qualification-profile-01-review-05/`.
`build.txt` and `control-build.txt` show zero warnings/errors (1.49 s and 10.53 s)
with the established no-restore, serial, UseSharedCompilation=false,
SkipNSwag=True and both ADR-0074 flags false settings. `focused.txt`/`focused.trx`
show **200/200 existing cases passed** in 55 s, including the real observer
against owned doubles and the disclosed local-tool rehearsal. The separate
`control-inherited-handle.txt`/`.trx` fail at the unproven-termination assertion
after confirming exit, truncation, incomplete I/O, refusal and a live owned
descendant. The initial helper did not deliver oversized stderr and failed its
setup assertion; `control.txt`/`.trx` and the initial helper source are retained.
The final helper writes directly to the inherited error handle; its source and
the exact reviewer test are in the same ignored directory. The reviewer cleaned
up the owned child and removed only the temporary test source. Rebuild before
further source-bound tests. No installed provider was inspected or called, and
no real authentication/account call, old-ledger access, install, download or
guest/cache proof was made.

Correction 5 is authorized offline now in the same executor chat, limited to
these two QP-R3 observer paths, permanent focused regressions and the executor
record/playbook. Cover a timeout followed by completion at the decision point
deterministically, and overflow with the other pipe held open using an owned
executable double. Keep the earlier passing behavior and retain unchanged
evidence. Return unstaged, uncommitted and unpushed. Neither real lot, installed
provider preflight nor publication is authorized; no next slice is selected.

## Review 4: correction 3 assessed (2026-11-02)

Preflight confirmed `main`, HEAD/local origin/main and a fresh live main at
`38fc3847f347599430cc036849044d75d6e8bb8a`; empty index; 25 modified tracked
paths and 62 untracked paths, no `src/` change. The planner inspected the five
new correction files, both entry finalizers, the composition and updated
records, rebuilt and independently exercised the same 13 affected suites.

| ID | Review 4 disposition | Basis / remaining work |
|---|---|---|
| QP-R1 | Closed at the reviewed finalization/persistence scope | Create-new persistence now checks write, device flush and disposal. Both owned-directory-at-the-filename controls return 9, and existing files are preserved. Exit 9 replaces 0/6 only; specific non-success exits remain. An uncommitted finalization follows exit 8. The affected 302 existing tests pass independently. The recorded in-flight sink and operating-system pre-emption limitations remain; this is no filesystem crash-atomicity claim. |
| QP-R2 | Closed, retained | No new admission-boundary input changed; affected admission/entry controls pass. |
| QP-R3 | P2 Open only for the real Git observer's bounded observation and mandatory type layout | The exact approved Git is admitted before fixture Git, through discovery.git with the unchanged eight/179 ceilings, and the minimum/closed parser/rehearsal negatives pass. However ProcessGitVersionObserver starts an unbounded, unawaited StandardError.ReadToEndAsync, limits stdout in characters rather than bytes, and gives stdout a second full bound after waiting for process exit. A real owned executable emitting a valid version plus 4,096 stderr bytes is accepted by Evaluate (null), contrary to the stated 512-output-byte refusal contract. Bound both streams in bytes, require their completion, and use one monotonic observation deadline including exit and I/O; refuse oversized/incomplete/failed observations without raw retention, fallback, retry or fixture Git. Keep cleanup honest about unproven termination. GitVersionGate.cs also declares four public top-level types: split GitVersionObservation, IGitVersionObserver and GitCompatibilityException into their own named files, retaining GitVersionGate separately, as ENGINEERING.md requires. No other newly added C# file was found to group top-level public/internal types. |
| QP-R4 | Closed, retained | No typed-status input changed; the affected status-entry controls pass. |
| QP-V1 | Closed for the previously recorded 670-test feature matrix; focused evidence refreshed | Reviewer independently passed all 302 existing correction-3 cases. The new oversized-stderr counterexample fails and is the QP-R3 correction gate, not grounds to rerun unrelated matrices. Preserve executor 524/526, 637/639, 668/670, independent 670/670 and their differing contexts; neither the native-watch discrepancies nor default NSwag exit 9009 is explained by this review. |

Independent evidence is in the new ignored directory
`logs/planner-review/qualification-profile-01-review-04/`:

- `build.txt` and `controls-build.txt`: zero warnings/errors, 13.43 s and
  11.84 s; no restore, serial, UseSharedCompilation=false, SkipNSwag=True,
  both ADR-0074 flags false and the short process-only PATH.
- `focused.txt`/`focused.trx`: all 302 existing cases passed (1 m 3 s).
  Two added reviewer controls failed on a reviewer setup error (the owned
  scratch directory had not been created), not on product behavior.
- After correcting that setup, `controls.txt`/`controls.trx` and
  `isolated-pipes.txt`/`isolated-pipes.trx`: each has one pass and one failure.
  The valid-version-plus-4-KiB-stderr control fails Assert.NotNull because
  Evaluate accepts it. The descendant-pipe control refuses; it does not prove
  that stderr completion is checked. The latter run changed only the owned
  helper's stdout handle inheritance, not implementation source.
- `reviewer-controls.cs` and `GitObservationDouble.cs` preserve the final
  controls/helper; their earlier setup/helper forms are also retained. The
  helper was compiled locally with the Windows .NET Framework C# compiler;
  its only effects are owned test files, bounded output and a short owned
  child, whose PID is recorded and cleaned up. No installed provider was
  read or run. The rehearsal retains its disclosed real local-tool boundary.

The reviewer removed only the temporary reviewer test source after running it;
rebuild before any further source-bound tests. No full matrix, architecture,
environment, native-watch or default NSwag rerun was performed. No preserved
qualification root or old ledger was opened; no provider/authentication/account
call, install, download, guest/cache proof or `src/` change was made.

Correction 4 is authorized offline now, in the same executor chat, limited to
QP-R3's observer and file layout, its permanent tests and the executor's
record/playbook. Keep the Git syntax minimum and all previously closed
findings. Validate the real observer with owned executable doubles (normal,
oversized stdout/stderr, delayed exit and independently unfinished pipes),
then the affected Git gate/composition/admission cases once on final source.
Retain passing persistence/entry and unchanged broad evidence; broaden only
for a material changed dependency, failure or unresolved concern. The
five-second bound and 512-byte cap must be mechanically enforced, not merely
weakened in documentation. No new activation confirmation, real lot, installed
provider preflight, commit or push is authorized. Return unstaged, uncommitted
and unpushed for the exact-content GO decision.

## Review 3: correction 2 assessed (2026-11-02)

Preflight confirmed `main`, HEAD/local origin/main and a fresh live main at
`38fc3847f347599430cc036849044d75d6e8bb8a`; empty index; 25 modified tracked
paths and 57 untracked paths, no `src/` change. The planner read correction 2,
the updated code/tests and the executor's complete-filter output/TRX.

| ID | Review 3 disposition | Basis / remaining work |
|---|---|---|
| QP-R1 | P1 Open only for authoritative persistence outcome | The nonblocking progress snapshot, repeated expiry checks, reserve inside the stated budget and close-before-report order address the prior controls, which now pass. However both save helpers swallow IOException/UnauthorizedAccessException and return no outcome; each entry still returns 0 after logging that its summary could not be saved. Two independent controls put an owned directory at the destination summary filename during printing: both lots reported the save failure and returned exit 0. Make persistence/commit success a typed, checked part of finalization, including flush/disposal; failed or unproven save must produce a closed non-success/attention result without refund or retry. Keep already observed exchange/authentication facts separate. Never return success from an uncommitted finalization branch. Preserve create-new/no-overwrite semantics and bounded return; no generic reporting framework is needed. |
| QP-R2 | Closed at the reviewed dispatch-boundary scope | Expiry after identity validation, after durable marker work and at ConfirmOpen is checked; markers remain consumed without a start. Snapshot reads no longer wait for hashing/marker I/O. All current controls pass in the independent full feature run. This is no claim of OS pre-emption or descendant containment. |
| QP-R3 | P1 Open only for the supported Git configuration prerequisite | Ten-minute role limits, exact schema bytes/run/attempt, fixed production scratch, attempt-bound sinks and the launcher Git's empty/pinned environment pass independent controls and the complete rehearsal. The executor correctly raised that the new Git environment controls need a sufficiently new Git. A digest identifies a file but establishes no flag support; the current manifest has no compatibility proof before its first fixture mutation. Require a bounded, closed Git version admission check for the exact approved executable before fixture Git mutation. Select Git 2.32.0 or newer as a syntax prerequisite for this profile, not universal compatibility/security qualification. Unknown/older/failed/incomplete observations refuse; never fall back or ignore flags. Use the already budgeted discovery.git version-probe class, keep the 179 maximum and no new live authority; update actual rehearsal counts honestly. |
| QP-R4 | Closed, retained | The complete independent feature run includes the current typed-status controls; installed Claude behavior remains unqualified. |
| QP-V1 | Closed for the current existing 670-test feature matrix in the recorded reviewer context | Reviewer built current source (including the final BOM-literal edit), then ran all 670 existing feature cases plus two new counterexamples once. All 670 existing cases passed, including all six native-watch tests; only the two new save-failure controls failed. This is independent passing evidence, not an explanation or erasure of executor 668/670, 637/639 or 524/526. Preserve all those outcomes. Retain native/unchanged evidence for correction 3 and run focused affected checks; do not repeat the full matrix merely to reproduce unexplained context differences. Broaden for new dependencies/failures/uncertainty. |

Git prerequisite decision: the
[Git 2.32 manual](https://git-scm.com/docs/git/2.32.0)
documents GIT_CONFIG_GLOBAL/SYSTEM, while the
[Git 2.31 release notes](https://raw.githubusercontent.com/git/git/v2.31.0/Documentation/RelNotes/2.31.0.txt)
record new environment-fed configuration mechanisms. The planner also compared
the primary tagged config.c sources for 2.31 and 2.32: the latter handles
GIT_CONFIG_GLOBAL; the former does not. This supports the conservative minimum
syntax gate, not a claim that any installed version has been qualified. Resolve
this engineering prerequisite without asking the owner to choose a version or
accept silently ignored controls. No real probe is run during preparation.

Correction 3 validation: focused finalization/save-failure controls for both
lots, the existing deadline/entry suites, new Git compatibility negatives and
the affected production-composition rehearsal with doubles. Keep the supported
normal result, failed/missing/partial durable output, refusal/no second start,
unchanged source, no overwrite and spent-slot invariants. Reuse the passing
native-watch, architecture, environment and other unchanged results with their
actual scopes. No broad matrix rerun without a material trigger.

Independent evidence: new ignored
`logs/planner-review/qualification-profile-01-review-03/` contains `build.txt`
(zero warnings/errors, 12.84 s, no restore, serial, UseSharedCompilation=false,
SkipNSwag=True, both ADR-0074 flags false and short process-only PATH),
`full-filter.txt`/`full-filter.trx` (672 cases: 670 existing passed, two new
save-failure controls failed, 1 m 30 s), and `reviewer-controls.cs` (exact temporary
controls). The reviewer removed only that added source afterwards; rebuild
before further source-bound tests. The feature run used provider doubles and
the specifically admitted local-tool boundary only; no installed provider
identity read, real provider/authentication/account call or old-ledger access.
Executor evidence read was limited to
`logs/executor/qualification-profile-01-correction-02/full-filter.txt` and its
TRX. No preserved qualification root was opened.

Correction 3 is authorized offline now, in the same executor chat. No new
activation confirmation, real lot, provider preflight, commit or push is
authorized. Preserve finding IDs and this planner-owned record; update the
executor record/playbook and return unstaged, uncommitted and unpushed.

## Review 2: correction 1 assessed (historical review, 2026-11-02)

Preflight: `main`; HEAD/local origin/main and a fresh live main remain
`38fc3847f347599430cc036849044d75d6e8bb8a`; empty index; 25 modified tracked
paths and 55 untracked paths, no `src/` changes. The planner read the correction
report, changed implementation, tests and executor full-filter output/TRX.

| ID | Review 2 disposition | Evidence / remaining correction |
|---|---|---|
| QP-R1 | P1 Open; synchronous-entry counterexamples fixed, finalization and waiter still unsafe | A shared monotonic deadline and off-thread preparation address the original synchronous prefix. But `ExpireAsync` reads `AdmissionFacts` synchronously; `Facts` takes the same lock held during hashing and durable marker I/O, so the deadline waiter can block on the work it was meant to bound. An independent control held revalidation and showed the facts read did not return. A second control used a writer that actually appends after release: exit 8 was followed by late success JSON. A held-disposal control left a durable `result: Qualified` despite exit 8. Existing HeldWriter only waits and never writes, missing this. Publish a nonblocking coherent progress snapshot, finalize closure before authoritative success, and make pending/in-flight output explicitly non-final. Include all failure/refusal reporting and emission reserve in the stated outer budget; two post-budget 3 s emissions cannot silently extend it. |
| QP-R2 | P1 Open; refusal latching fixed, expiry during validation not fixed | The first refusal now terminally closes admission, and the existing terminal-gate controls pass. However expiry is checked only before revalidation: an independent 40 ms deadline/200 ms successful-validation control still admitted and consumed a slot after expiry. Recheck after potentially blocking identity/durable work and at the dispatch handoff; preserve consumed markers without starting an expired request. Keep summary reads and closure independent of the slow-validation lock. |
| QP-R3 | P1 Open; approved launch identities and full vectors substantially improved | Exact executable/script hashes, shared absolute Git and complete role vectors fix the original first-argument counterexamples. Independent controls still admitted an arbitrary external `schema.json` and a 15-minute role request. The plan and production `AgentClaimPathPolicy` specify 10 minutes for these two roles; the new inventory instead allows 20. Bind schema location and exact contract bytes to this attempt, not its basename. Explicitly bind the known production scratch location used by discovery/schema preparation rather than accepting arbitrary working directories. The launcher's Git inherits all ambient environment/configuration outside its shape, permitting unreviewed helper/hook behavior; close that influence in the qualification support without changing production policy. No hostile-executable isolation claim is added. |
| QP-R4 | Closed at its typed-projection scope | Known present mistyped/null/duplicate members now reject the observation; optional absence remains distinct. Independent projector and terminal-admission set passed, and source/tests cover the second-start refusal. This establishes no installed Claude compatibility. |
| QP-V1 | Open / final evidence pending | Full executor result verified from its original output and TRX: 639 total, 637 passed, the same two native-watch failures, still unexplained. Keep 524/526 and all earlier failures. Review 2 did not rerun the native tests or the full feature set. Correct the conflicting evidence synopsis: correction 1 says no installed Git/Node/other tool was inspected or hashed, then reports hashing real Git/.NET/Node/Docker. Distinguish provider doubles from actual local-tool boundaries. Obtain final proportional evidence after the remaining correction, without blind retries, skipped native assertions or invented causes. |

### Clarifications and executor challenges resolved by the planner

- The previous blanket wording about suppressing every late sink write was too
  absolute. A write already inside an uncooperative external sink cannot be
  revoked. Keep bounded caller return and prevent detached work from initiating
  new effects; treat any in-flight bytes as unproven/non-final. Do not persist
  or present whole-entry success before required closure/finalization, and do
  not claim that a cancellation-ignoring sink was physically stopped. This
  narrows the mechanism claim, not the acceptance of a misleading final success.
- Existing production scratch is a legitimate dependency, not permission to
  move production files in this slice. The qualification support may recognize
  that exact known host-owned scratch boundary and the role's exact schema
  content through bounded proof. It must not accept every absolute schema path
  or discovery working directory. No arbitrary linked imports or new product
  context policy is selected.
- The all-owned-file wording was too broad for the already-required rehearsal
  through real local Git and existing tool discovery. Prospectively permit only
  bounded regular-file identity reads of the exact Git/.NET/Node/Docker files
  selected by that controlled rehearsal's existing local-tool resolution.
  Codex, Claude and gh remain owned doubles; installed provider identity reads
  and calls are still forbidden. No new machine/profile search, credentials,
  account access or live qualification follows. This is an explicit validation
  scope revision, not a claim that the earlier wording was followed. Correct
  the evidence record and do not describe real tool files as owned stand-ins.
- Keep the existing accepted observer refusals, fixed reflection call, per-class
  caps and support-build SkipNSwag decision. A periodic cap is not evidence of
  the whole refresh timeline; no live timeline experiment is selected.

### Review 2 independent evidence

New ignored directory: `logs/planner-review/qualification-profile-01-review-02/`.

- `build.txt`: affected API test project, no restore, warnings/analyzers,
  UseSharedCompilation=false, SkipNSwag=True, serial, both ADR-0074 flags false
  and short process-only PATH; zero warnings/errors, 10.76 s.
- `controls.txt` and `controls.trx`: 65 cases, 59 passing projector/terminal
  admission cases and six new failing counterexamples (expiry during validation,
  blocked facts, late success output, qualified durable summary before disposal,
  arbitrary schema and over-budget role). The run used only owned files and
  doubles, no installed-tool hashing or provider calls.
- `reviewer-controls.cs`: exact temporary counterexamples; the planner removed
  only that added source file afterwards. Rebuild before the next source-bound
  validation; current test binaries include the removed reviewer class.
- Executor evidence read: only
  `logs/executor/qualification-profile-01-correction-01/full-filter.txt` and
  `full-filter.trx`, not old qualification roots, credentials or ledgers.

Correction 2 is authorized now, offline, in the same executor chat, without a
new owner activation question. Keep QP-R1/R2/R3 and QP-V1 until review closes
them and QP-R4 closed unless a change invalidates its evidence. No live lot,
provider identity preflight or commit/push is authorized. Return unstaged,
uncommitted and unpushed; preserve this planner-owned review.

## Review 1 findings and decisions (historical review, 2026-11-02)

Preflight confirmed `main`, HEAD/local origin/main and a fresh live main at
the delivered baseline above, empty index, 25 modified tracked paths and 43
untracked files (including the two existing planner records). No `src/` change.
The initial restricted remote read failed; the subsequent live read succeeded.

| ID | Priority / disposition | Evidence and required correction |
|---|---|---|
| QP-R1 | P1 Open: whole-entry deadlines are not whole-entry bounds | A starts its outer wait after binary verification, root creation, filtered-map preparation and the synchronous prefix of the runner. B starts it after the synchronous prefix of `session.RunAsync`; summary saving and final disposal are outside it. Independent controls blocked only filtered-map preparation or the first source snapshot for 400 ms with a 40 ms outer budget: both returned exit 0, not 8. Establish one monotonic entry deadline including preparation/report/closure, a terminal admission gate, bounded return under the responsive-OS assumption, truthful uncertainty and no later starts/cleanup/report rewrite from detached continuation. A's deadline summary must not label an already admitted observation NotStarted. Rehearse synchronous preparation, held sink/disposal and late continuation, not just an awaited submission. |
| QP-R2 | P1 Open: refusal does not stop later process admission | `ProcessAdmission.Admit` records a refusal but the next valid request still consumes a slot and is admitted. Supervisors can continue until the session's next check. Independent control refused `unknown.exe` then admitted `git.exe --version`. Close the live admission gate atomically on the first refusal or expiry, preserving the original reason and consumed slots. Subsequent calls must start nothing, including background refresh; required final observation that is consequently unavailable remains Unproven. Test interleavings and never clean an unproved root. |
| QP-R3 | P1 Open: reviewed executable/command identity is not enforced | `JudgedTargets.Identity.Matches` accepts every tail after `exec`/`--print`; independent controls admitted Codex with `--dangerously-bypass-approvals-and-sandbox` and Claude with `--tools Bash --max-turns 99`. Discovery accepts a leaf filename or package-path substring, and launcher Git uses PATH search. Bind the complete approved role vectors and dynamic members, environment/deadline constraints, discovery launch tuples and fixture/worktree destinations. Pin explicit file identities for both providers and any Node/script components and use the same approved absolute Git identity for launcher and host; revalidate before dispatch and stop on drift. No claim of protection against malicious same-user executables. All implementation/tests use owned files/doubles; actual installed identities remain a future approved preflight, never a current discovery grant. |
| QP-R4 | P2 Open: mistyped known status members become authenticated | The projector ignores non-string `authMethod` and `configDirectory`. Both independent inputs with `loggedIn:true` plus either field as number 123 returned ReportedAuthenticated. Reject malformed present known members; absence of optional fields remains distinct. Preserve fixed methods and comparison-only directory disclosure. Add permanent cases for wrong types/duplicates and verify the second observation never starts after an incoherent first. |
| QP-V1 | Final evidence pending; earlier failures preserved | Executor's full feature result remains 524/526, with two native-watch failures and no clean-baseline comparison; architecture 232/232 used SkipNSwag after an unexplained exit 9009. Reviewer native-watch rerun passed all six under its recorded execution context. This does not explain or erase earlier failures. After corrections, rebuild once and run the affected feature matrix once; preserve original outputs, compare/disclose context and report blockers instead of attributing or suppressing failures. |

Decisions on executor challenges:

- Accept the qualification-only refusal of all three Codex app-server observers
  for this run without a usage stop. Source confirms the dispatch guard does
  not call its observer when the stop is NotConfigured; independent success,
  failed-no-message and observer-refusal controls passed. It qualifies no
  observer/account/model-catalog behavior. Any unexpected call must terminally
  close admission under QP-R2, never silently restore a production observer.
- Accept one fixed reflective call to the actual internal Claude environment
  builder, with fail-closed shape resolution and focused tests. No production
  visibility or environment-policy change is needed for this slice.
- Keep the per-class ceilings without arbitrary headroom; 179 is a maximum
  inventory, not the number actually observed in the short rehearsal. The
  periodic count cap is enforced but the full refresh timeline was not run;
  no additional live experiment or five-minute waits are required to assert
  that cap. A changed closed inventory needs evidence and review.
- Require executable identity binding under QP-R3. This is an engineering
  resolution of the existing plan, not another owner choice about PATH or
  unsupported binaries. The owner only decides future live authority and
  supplies/confirms otherwise unavailable launch-context information.
- Accept SkipNSwag for these support/test project builds: their existing API
  references explicitly request it and no API/schema/client input changed.
  Do not claim that the failed default NSwag invocation passed or explain 9009
  without evidence. No full frontend/Rust/journey replay is selected.

Independent evidence is retained only in the new ignored review directory
`logs/planner-review/qualification-profile-01-review-01/`:

- `build.txt`: affected API test project, no restore, warnings/analyzers,
  UseSharedCompilation=false, SkipNSwag=True, serial; 0 warnings/errors, 9.61 s.
  Both ADR-0074 first-run flags were false and a short process-only PATH was used.
- `controls-and-native.txt`: the initial restricted test invocation produced
  no test results and was interrupted. No cause or completed result is claimed.
- `controls-and-native-unrestricted.txt` and its TRX: 13 cases, six native-watch
  passes and seven reviewer counterexamples failing at the expected assertions
  (two role vectors, refusal continuation, two entry deadlines, two field types).
- `rehearsal.txt` and its TRX: three passing selected production-composition
  tests with provider doubles (current evidence, failed Claude without a message,
  and all three refused app-server observers), 17 s test duration.
- `reviewer-controls.cs`: exact temporary counterexample source, copied before
  execution. The reviewer removed only that added test file from the source
  tree afterwards. Current test binaries include it; rebuild before the next
  source-bound validation. No product code was changed by the reviewer.

No real provider/version/help/authentication/account call, legacy ledger or
preserved qualification-root read, credential access or unrelated cleanup occurred.
Return corrections unstaged/uncommitted/unpushed, update current-work.md and
the playbook honestly, and preserve these finding IDs. Do not edit this
planner-owned review or rewrite historical records.

## Standing decisions and limits

Retain the core architecture and simplify delivery overhead. Preserve REV-01
through REV-10 and F-01 through F-14 with their original meanings and limits;
the linked plan carries their consolidated dispositions. F-14 concerns duplicated
architectural comments; general recovery remains REV-04/F-05/F-13.

The owner accepted supported Git/GCM as trusted but fallible for supervised
publication, with exact authorization, restricted configuration, durable evidence
and uncertain-outcome recovery. Do not ask that decision again. ADR-0050 stays
Proposed/P9 unsatisfied until contract resolution; no publication implementation,
draft PR or CI authority follows. Job Objects do not replace durable identity,
authorization or filesystem isolation. The successful exit still needs a green
draft PR; escalation is required failure behavior.

The same offline slice permits its bounded launcher/test/document corrections
and focused local validation. No product-policy edits, real provider/version/help/
authentication/account calls, credentials or preserved qualification-evidence
reads, old ledger/reset, installation/download or unrelated cleanup are authorized.
The fresh live Git read above is repository reconciliation, not a product probe.
Keep Windows Sandbox enabled until the owner confirms it is no longer needed;
all four F attempts are consumed. No new guest/cache read is authorized. The
completed limited PATH repair is unchanged; do not inspect its protected backup,
key/certificate stores or clean any other PATH/scratch.

`CONTINUE_THIS_CHAT` for review and corrections in the existing executor chat.
This new planner chat was an explicit
owner-directed transition. Current context percentage is unavailable; no value
is inferred. Any future implementation uses a new executor chat and returns
unstaged, uncommitted and unpushed for GO/NO-GO under the
[development workflow](development-workflow.md).
