# ADR-0007: Registration-time repository identity is non-authoritative for mutation exclusion

## Status

Accepted

## Context

Increment 3's first slice lets a user register a local Git repository and see a
trustworthy baseline (canonical root, HEAD, branch/detached/unborn state, dirty
state). Registration needs a duplicate-detection key so the same repository
cannot be registered twice under a different spelling, and the roadmap's later
writer-lease and worktree-ownership work (ADR-0005, ADR-0006) will eventually
need to key "one mutating run per canonical repository" off *some* durable
repository identity.

A true physical-identity signal for a local Windows directory (resistant to
`subst` drives, hardlinks, and ancestor junctions/symlinks) requires resolving
the underlying volume serial number and file reference number — real Win32
API surface (`CreateFile` with backup semantics plus
`GetFileInformationByHandle`), with no direct BCL equivalent for directories.
Building and testing that now, before any feature actually depends on
exclusivity, would expand this narrow first slice well beyond "register a
repository and prove it's real."

Repository baselines also need a way to determine which observation is
"current" as more are captured over time (future re-validation, and
eventually per-run precondition snapshots per ADR-0005's "fingerprints before
and after mutating stages"), without relying on wall-clock timestamps that a
clock adjustment or concurrent write could make ambiguous.

Finally, a Git-inspecting boundary that runs multiple separate `git`
invocations against a filesystem another process could be concurrently
modifying needs an explicit rule for what happens when the observation is
caught mid-change.

## Decision

- A registered project's duplicate-detection key
  (`Project.RegistrationIdentityKey`) is the case-insensitive, lexically
  normalized form of its canonical path — computed by the same rule for every
  registration, never re-derived ad hoc.
- This key is **registration-only**. It must not be used, alone, to key any
  future repository-mutation-exclusion mechanism: a writer lease, a
  worktree-ownership marker, or ADR-0006's "one mutating run per canonical
  repository" guarantee. A future physical-identity capture (Windows volume
  serial number + file reference number) must be introduced, and every
  existing registration backfilled with it, before any such mechanism may
  rely on repository identity for correctness.
- A registration root that is itself a reparse point (a directory symlink or
  junction) is rejected — not for identity purposes, but because it could be
  silently repointed later, breaking the registration's own referential
  stability. A linked Git worktree is also rejected, reserving "a worktree is
  something DevalCopilot itself creates for a run" ahead of any worktree
  feature actually being built. Neither rejection attempts to resolve or
  detect aliasing through an *ancestor* reparse point or a `subst`-mapped
  drive letter — an accepted consequence of the identity decision above, not
  a separate gap.
- UNC and other network-root paths are rejected outright for this local,
  single-workstation MVP (ADR-0001).
- Repository baselines are append-only and ordered by a per-project monotonic
  `BaselineNumber`, allocated by `Project` itself via `ReserveBaselineNumber()`
  — the same in-aggregate-counter pattern already used for
  `NextExecutionNumber`, backed by a unique `(ProjectId, BaselineNumber)`
  index. "Current" is always the greatest `BaselineNumber`; `ObservedAtUtc` is
  display metadata only and never used for ordering or selection.
- Git inspection for a baseline brackets its HEAD/branch/dirty observation
  with a before/after HEAD-signature check around the one call whose cost
  scales with working-tree size (`status`). A detected mismatch is retried
  once; a second mismatch fails closed with a typed
  `RepositoryChangedDuringInspection` outcome rather than persisting a torn,
  internally-inconsistent observation.

## Consequences

- The writer-lease/worktree-ownership deliverable later in Increment 3 has an
  explicit, named prerequisite it cannot silently skip: it must introduce
  physical-identity capture and backfill every existing registration with it
  before claiming repository-mutation exclusivity.
- Until that prerequisite lands, two distinct registered paths can secretly
  reference the same physical repository via `subst`, a hardlink, or an
  ancestor junction — an accepted, bounded, and now permanently recorded risk
  rather than a silent one.
- Baseline "current" selection is unambiguous and race-free under concurrent
  registration or a future revalidation command, reusing an already-proven
  concurrency argument rather than inventing a new one.
- A repository that changes while being inspected never produces a baseline
  that mixes evidence from two different moments; the caller sees a typed,
  retryable failure instead.

## Non-goals

- Does not implement physical-identity resolution (Option A) now.
- Does not implement worktree creation, branch mutation, a writer lease, or
  any mutation-exclusion enforcement.
- Does not add retry as a user-facing feature, GitHub integration, or
  multi-repository scheduling.
- Does not add UNC/network-path support.

## Alternatives considered

### Resolve physical identity now (Option A)

Rejected for this slice: correctly handling `subst` drives, hardlinks, and
cross-provider volume identity on Windows is real, novel platform work that
belongs with the feature that actually needs exclusivity guarantees, not with
a registration slice whose job is only to prove a repository is real and let
the user see it.

### Leave the identity gap as an undocumented limitation

Rejected: a future session implementing the writer-lease/worktree-ownership
slice could easily assume the existing `RegistrationIdentityKey` is already a
safe exclusivity key. Recording the constraint here, rather than leaving it
as a comment or a vague caveat, makes it something that must be consulted
before that assumption is made.

### Select the current baseline by latest `ObservedAtUtc`

Rejected: wall-clock timestamps are not guaranteed monotonic across a clock
adjustment, and a future revalidation path run concurrently with another
write could make "latest by timestamp" ambiguous in a way a per-project
monotonic counter, backed by a unique index, cannot be.
