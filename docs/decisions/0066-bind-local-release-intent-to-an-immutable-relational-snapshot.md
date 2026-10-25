# ADR-0066: Bind local release intent to an immutable relational snapshot

Status: Accepted

## Context and authority

This narrowly extends [ADR-0065](0065-release-terminal-local-quarantine-with-original-host-termination-evidence.md)
inside the same unpublished LOCAL-QUARANTINE-RELEASE-01 slice. The planner resolves
the executor's correction-1 translation challenge on 2026-10-25 under the owner's
existing 2026-10-23 design and selection authority. No new recovery, execution,
qualification or publication authority is granted.

SQLite has no selected SHA-256 capability. A provider-neutral EF ownership
predicate and a native live-fence trigger therefore cannot recompute the current
canonical confirmation digest. The correction's reader recomputes it, while the
ownership predicates compare only referenced facts. Calling that an equivalent
complete proof is insufficient: changing a legitimate Agent outcome or a
SourceChanged verification's completion fingerprint can preserve the current
coherence classification. The Human must confirm exact values, not a class of
otherwise legitimate results. The old canonical facts omit these concrete values.

A digest is neither a credential nor a signature of the local database. Adding
a SQLite hashing function, external capability, materialized eligibility flag
or background integrity collector is unnecessary for this bounded contract.
Instead, retain the exact intent as relational data that every consumer can
compare. This decision supersedes only ADR-0065's implicit equivalence between
post-commit confirmation-checksum readability and effective ownership; all
original-host, physical, terminal, identity, expiry and Human-event requirements
remain.

## Decision

### One receipt also stores the exact confirmed tuple

Keep three append-only facts. Extend the existing Human release row with a
versioned, immutable typed snapshot of its canonical confirmation facts, version
and exact expiry. Store typed columns in that same row, using its existing
identity/foreign-key columns where they already capture an intended value.
No fourth table, JSON authority document, generic snapshot framework, cached
eligibility flag or new independent source of process evidence is selected.

The snapshot contains every non-secret canonical fact currently named by
LocalExecutionOwnershipReleaseFacts, extended to include the concrete AgentOutcome
or verification CompletionFingerprintSha256 as appropriate, not their coherence
class. Include the full sequence/step/source/admission/operation identity and
state, witness and observation identities, actual process/physical facts and
recording times, confirmation version and exact expiry. Preserve the existing
privacy boundary: no host nonce/ticket, PID, path, argument or output is exposed
or copied into this intent snapshot.

Construct it only from the fresh complete proof under the Human command's final
write lock, after the candidate digest and expiry were validated. Append it in
the same receipt/event commit. Enforce required members, closed versions,
family-specific nullability and exact stored numeric constraints. Existing
witness/observation and receipt append-only, unique and restrictive associations
remain. Missing, foreign, duplicate or unsupported snapshots never release.

The in-memory proof, provider-neutral ownership expression and SQL live-fence
proof must each compare the current complete proved tuple against this stored
typed snapshot, field for field. Compare actual values, not only membership in
a valid vocabulary. A coherent-but-different outcome, completion fingerprint,
physical exit code, observed duration, sequence state, source or confirmation
expiry invalidates the old intent. It cannot replay, free an owner or pass a
guard. UUID equality and a matching checksum alone prove none of this.

The candidate digest must also include the concrete terminal-result values.
Changing one legitimate value after GET changes the candidate digest; the old
POST is stale and writes no receipt/event. After a release, the same drift
invalidates the relational snapshot independently of digest recomputation.

### Checksum integrity and ownership have distinct meanings

At initial release, the exact fresh digest remains mandatory. For a coherent
stored receipt, the reader and replay still recompute the digest from the exact
snapshot-proved facts, version and expiry. An unchanged receipt replays after
expiry; neither recomputation nor replay renews its grant.

There is one explicit post-commit distinction: a well-shaped substituted
ConfirmationSha256, with the entire typed snapshot, physical chain, exact expiry
and actual Human event otherwise intact, makes the release inspection
Unavailable and refuses replay. It does not retroactively reacquire ownership.
The persisted exact relational Human grant remains valid; the checksum is not
its credential or the only binding of its subject. This exception applies only
to checksum mismatch, never a missing/mismatched snapshot, identity, result,
physical observation, chronology, version, expiry or Human event.

Do not change or repair a checksum automatically. No confirmation-metadata
recovery command is selected. An Unavailable inspection cannot be interpreted
as proof that a hold remains: the existing authoritative live-control,
workspace and capacity readers decide ownership from the relational proof.
Keep the current closed HTTP graph and reason codes; use neutral fixed copy
that promises neither retained nor released ownership from an unreadable answer.
Do not optimistically update those shared readers from the release panel.

### Exact time and persistence boundaries

The maximum lifetime remains exactly two minutes at 100 ns tick precision.
A floating-point julianday approximation with a positive tolerance is not an
equivalent storage guard. Enforce the exact bound through feature-owned exact
storage constraints/proof; keep the Domain and locked-command checks. Test
120 seconds minus one tick, exactly 120 seconds, plus one tick and fractional
second boundaries. Do not introduce a global date-reader or clock framework.

Comparing supported host-written normalized UTC storage text for chronology is
accepted under the existing typed GUID/time and monotonic-clock conventions.
It grants no support for arbitrary offsets or malformed date storage. Preserve
that qualification and verify second/fraction/day-boundary ordering.

Extend only the still-unpublished additive migration/configuration/Designer/
snapshot. No historical migration edit, backfill, fabricated snapshot or new
grant for existing rows is authorized. A missing snapshot stays unsupported.
Used-feature Down refusal still happens before any change; empty-feature Down
restores the old guards. All unrelated queue, claim, result and source guards
remain.

## Required evidence and consequences

File-backed tests cover each intent field's independent drift, including both
operation families and modes, before confirmation and after release. Assert
reader, workspace, capacity, abandonment in its actual scope and direct SQL
write-seam results independently. Test malformed completion fingerprints and
the exact lifetime boundary. Add compiling controls of snapshot equality in
the reader, ownership expression and SQL trigger, retaining independent defenses.

One explicit checksum-only test must show Unavailable inspection, refused replay,
no second event/write and still-valid ownership release, with truthful UI copy.
In contrast, changing any authoritative snapshot fact must retain ownership.
Producer provenance, initial confirmation, immutable results/cycles, six kinds,
ReadOnly versus mutating effects, restart and the five selected journeys remain.

The extra typed columns intentionally duplicate intent data to make the exact
Human subject testable at every database write seam. They are not a replacement
for proving the referenced rows. No new lease, cancellation, retry, takeover,
reservation release, real-run recovery, native/provider experiment, approval,
commit or publication is selected.
