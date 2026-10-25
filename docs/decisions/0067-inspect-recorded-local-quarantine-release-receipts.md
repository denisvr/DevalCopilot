# ADR-0067: Inspect recorded local quarantine release receipts

Status: Accepted

## Context and authority

LOCAL-QUARANTINE-RELEASE-01 is independently verified published as
`7ab7a15eab9ce5682578517f1bb106ca7557db21`. ADR-0065/0066 own its original-host
proof, exact Human confirmation and effective-ownership semantics. Its current-run
control shows a small receipt after release. ADR-0061's current and historical
step inspector shows the operation but cannot inspect that Human decision and
its supporting termination facts. Event headers alone do not prove the chain.

Select LOCAL-RELEASE-INSPECT-01 under the existing functional-development and
read-only inspection authority. This extends recorded inspection, not the release
grant. No new owner recovery or execution decision is needed. Ship the proved
receipt, supporting facts, protected query and both inspector consumers together.
General recovery needs separate authority; complete run replay needs a separately
resolved temporal and cross-stage contract. Neither belongs in this outcome.

## Decision

### One protected recorded query

Add `GetLocalExecutionReleaseInspectionQuery` and one authorized, bodyless,
queryless MVC GET through `IApplicationMediator`:

`/api/runs/{runId}/local-execution-sequence/inspection/steps/{stepId}/ownership-release`

Reuse ADR-0061's strict body/query guard and fixed, non-echoing inspection 400/404
errors. An absent run, sequence or foreign/missing step is the same fixed 404.
Use an API-owned response graph and reproducible NSwag generation. No POST or
other mutation route is added or changed.

Read exactly one bounded, provider-neutral, untracked database statement, reusing
the existing feature-owned `LocalExecutionReleaseStatement` and
`LocalExecutionReleaseProof`. Preserve their root/step/recipe bounds, guarded
projection, full associations, concrete terminal-result/snapshot equality,
chronology, exact lifetime, actual Human event and digest validation. This is
shared release-proof ownership, not a generic inspection framework. The query
must not call the candidate query, mint a confirmation, use a clock, start a
transaction or probe current readiness, ownership, Git, files, processes,
providers, accounts or artifacts. No table, migration, event, worker or dependency.

The closed response is `runId`, `stepId`, `status` and nullable `receipt`.
Statuses are Recorded, NotRecorded and Unavailable; only Recorded has a receipt.
Determine status from the same statement's rows and proof:

- A fully proved existing release (`Released`) is Recorded.
- NotRecorded requires both receipt associations (step-keyed and witness-keyed)
  to be absent and a supported proof result of Available or NotEligible. It
  means only that this read proved no receipt for this step; it says nothing
  about quarantine, termination, current eligibility or whether release is possible.
- A present receipt that fails any proof is Unavailable, including a receipt
  beside an unlinked/revoked step. Never map NotEligible blindly to absence.
- All other unreadable, incomplete, ambiguous or unsupported evidence is
  Unavailable with no partial payload. In particular, missing producer evidence
  may make an old unreleased operation Unavailable rather than NotRecorded.

Do not weaken or change the existing candidate, replay, release command,
ownership expression or SQL guards to implement this mapping. If a bounded
translation or necessary proof cannot be reused without changing their behavior,
raise a challenge before dependent work. Normal cancellation/database failures
retain the normal pipeline; any stored-value materialization handling stays
narrow and justified, never a catch-all converting query defects into absence.

### Receipt and supporting facts

The API-owned receipt graph contains only:

- `releaseId`, `releasedAtUtc`, `eventSequence`;
- `sequenceId`, `sequenceState`, `admissionMode`;
- `stepKind`, `ordinal`, `passNumber`, `operationFamily`, `operationId`;
- `sourceCheckpointId`, `sourceFingerprintSha256`;
- `operationStatus`, `processOutcome`, nullable `semanticExitCode`,
  `processDurationTicks`;
- `witnessId`, `observationId`, `dispatchedAtUtc`, `recordedAtUtc`,
  `physicalExitCode`, `observedElapsedTicks`.

These are a bounded disclosure of already proved facts, not a copy of every
internal snapshot column. The complete snapshot, concrete Agent outcome or
verification completion fingerprint and all undisclosed identities still must
be proved. The selected operation's existing inspector owns further evidence.
No nonce, launch identity, ticket, PID, path, executable, argument, output,
confirmation version/digest/expiry or action candidate is returned.

Keep the established closed vocabularies, nonempty UUIDs, lowercase SHA-256,
safe bounded ordinal/pass values and canonical UTC round-trip strings.
`eventSequence` is a canonical positive Int64 string; durations are canonical
nonnegative Int64 strings, including zero; exit codes are exact Int32 values.
Enforce family/kind/status/process/nullability relationships, not merely shapes.
Decode raw string times before any unchecked generated-client date revival.

### Current and historical cockpit integration

Inside ADR-0061's selected step detail, provide one collapsed **Inspect recorded
quarantine release** region for a proved linked Agent or verification operation.
The same component serves the current run and the selected run-history detail.
Selecting the step or opening another inspector does not read this receipt.
Explicit open reads it once; explicit reload replaces the read and its result.
No polling, retry, cross-visit cache, prefetch, export or automatic content read.

Use the existing owned-lifetime primitives. Session/run, parent inspector/index
visit, step/operation visit, opening and reload own callbacks, pending state and
answers. Closing, parent reload, step retry/replacement, A-B-A, project/run change,
unmount, stale callbacks and overlapping/late answers cannot populate or clear a
successor's state or start a successor's request. Carry a real AbortSignal and a
ten-second request bound, abort on replacement/unmount and release only that
request's single-flight guard. No shared transport/lifetime redesign is selected.

The decoder accepts the complete answer or none and verifies run/step identity;
Recorded also matches the selected sequence, mode, step kind/ordinal/pass,
operation family/id and pinned source. Invalid/failing answers clear old success.
Render fixed product copy and literal values. Keep internal identity details
secondary to the Human decision, original failed result, distinct physical
termination facts and their times. Mount no release control, candidate query or
confirmation handler in either inspector.

Explain that the record concerns a past Human release of that operation's retained
quarantine; it does not change its failed result or ended cycle, establish current
workspace/capacity availability, approve work or start anything. Physical evidence
concerns the invoked process only. NotRecorded and Unavailable promise neither a
held nor a released owner; authoritative live readers keep that responsibility.
In particular ADR-0066's checksum-only mismatch remains Unavailable here while
the exact relational grant can still release ownership. Do not infer or repair it.

The receipt is coherent within its own statement. Its separately read parent
index, operation evidence and current live status are not one atomic replay.
Recorded consistency is unsigned relational consistency, not authenticated
history; coordinated matching database rewrites remain a declared limit.

## Acceptance and consequences

Use real disposable file-backed SQLite for both families, all six local step
kinds, both admission modes, restart/reopen and an older selected run after a
newer run exists. Cover exact receipt absence versus unavailable evidence,
step-keyed/witness-keyed inconsistencies, unsupported/malformed/foreign rows,
snapshot drift, wrong Human event and checksum-only mismatch. Prove one statement,
no writes/events and no external effects; unchanged ownership is independently
asserted for representative valid, snapshot-drift and checksum-only cases.

API tests own authentication, strict body/query handling, non-disclosing errors,
closed wire and representative production-handler wiring. Frontend tests own
exact decoding, every nested lifetime, actual abort/timeout, single flight and
current/history integration without mutation. Keep permutations at their cheapest
boundary. Two bounded compiling controls remove a selected receipt-identity guard
and an ended-visit guard; named permanent regressions must fail. Independent
defenses may mask a control; report that scope and challenge an alternative.

Extend the existing authenticated owned-fixture quarantine-release journey to
inspect the recorded decision from the current and run-history surfaces, then
after a page reload. Retain its exact POST inventory and original invariants;
the inspection segment sends no business POST, candidate GET or artifact read,
adds no release/event and changes no result/cycle. A browser reload is not a host
restart; file-backed tests separately prove host/container reopen behavior.

The handoff supplies the proportional final matrix and broadening triggers.
This completes no increment and authorizes no real-run recovery, cleanup,
qualification, provider experiment, cancellation, retry/resume/takeover,
reservation release, approval, commit or publication. ADR-0050 remains Proposed,
P9 unproven, AI Memory deferred and Increments 4/5/6 incomplete.
