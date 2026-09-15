# ADR-0008: Physical repository identity and tool-owned worktree ownership

## Status

Accepted

## Context

ADR-0007 registered a repository under a lexically normalized identity key and explicitly
declared that key non-authoritative for any future mutation-exclusion mechanism: a writer lease,
a worktree-ownership marker, or ADR-0006's "one mutating run per canonical repository." It named
a concrete prerequisite — capture a real Windows physical identity, and backfill every existing
registration with it — that this slice must satisfy before repository-mutation exclusivity can
be claimed.

ADR-0005 and ADR-0006 further require that the running application develop its next version
(and, eventually, run agent work) without ever touching the user's active checkout, using an
isolated, tool-owned Git worktree with a durable ownership marker, one writer per worktree, and
one mutating run per canonical repository.

This slice is the first to actually create a worktree. It must therefore also decide: how a
directory's physical identity is resolved on Windows; how a mutation lease enforces
"at most one active workspace per physical repository" without losing lease history; how a
worktree's own Git administrative directory is discovered and validated rather than assumed by
path convention; where and how the ownership marker is written so it can never be mistaken for
part of either working tree; and exactly what a restarted host can safely conclude about a
workspace it did not itself just create.

## Decision

- **Physical identity.** A directory's physical identity is the pair (Windows volume serial
  number, 128-bit file ID), resolved via `CreateFileW` (with `FILE_FLAG_BACKUP_SEMANTICS`,
  required to open a directory handle at all) followed by `GetFileInformationByHandleEx`'s
  `FileIdInfo` class — the one place in the codebase that P/Invokes this, since no BCL API
  exposes it for directories. Only NTFS and ReFS support `FileIdInfo`; any other filesystem is
  reported as a closed `UnsupportedFilesystem` outcome, never a fabricated identity.
  `CreateFileW` resolves through the real NT object manager, so a `subst` drive mapping or an
  ancestor junction/symlink is followed transparently — two lexically different paths reaching
  the same physical directory resolve to the identical tuple, closing the aliasing gap ADR-0007
  recorded without any new detection logic.
- **Persistence and recovery.** `Project` gains a nullable `(PhysicalVolumeSerialNumber,
  PhysicalFileId)` pair, a `PhysicalIdentityStatus` (`Unresolved` | `Resolved` | `Unavailable`),
  and a closed `PhysicalIdentityFailureReason` (`None` | `UnsupportedFilesystem` |
  `PathInaccessible`) — a safe, persisted reason code, never a raw Win32 error, exception
  message, or path, sufficient for the UI to render fixed, truthful copy. Every existing
  registration starts `Unresolved`. Resolution is never automatic or scheduled: a dedicated
  recheck action, and an equivalent one-shot inline recheck at the start of workspace
  preparation, are the only two paths that ever change it, and both share one Application-level
  routine. A transient failure to verify an already-`Resolved` project never demotes it, and a
  successful resolution that disagrees with an already-`Resolved` project's stored tuple is
  surfaced as its own typed conflict rather than silently overwriting the stored identity — a
  directory silently replaced at the same path is a fact for a human to see, not to paper over.
- **Mutation lease.** `RepositoryMutationLease` is a durable workspace-ownership lock keyed by
  physical identity — explicitly not the future Increment 5 executor lease. It has no expiry,
  heartbeat, renewal, or automatic sweep in this slice: it is `Active` from creation until an
  explicit, evidenced `Released` (an in-request compensated failure) or `Superseded` (a
  reconciliation-detected loss of trust), both terminal. Exclusivity is enforced by a SQLite
  partial unique index on `(PhysicalVolumeSerialNumber, PhysicalFileId)` filtered to `Status =
  'Active'` rows — at most one active lease per physical repository, while every released or
  superseded row remains fully retained and queryable, so lease history is never lost to make
  exclusivity work. It carries no `Run` correlation field in this slice: workspace preparation is
  a standalone action a user takes on a registered project, deliberately decoupled from any
  future run/objective concept.
- **Workspace creation.** `GitWorkspace` records are created only from a fresh, non-dirty,
  non-unborn baseline captured immediately before creation, at an exact resolved commit SHA
  (never `HEAD` or a branch name) — eliminating any TOCTOU gap between baseline capture and
  worktree creation. The lease and the workspace (`Preparing`) are inserted together and durably
  committed — genuinely written to disk, not merely staged inside a still-open transaction —
  before any external Git or filesystem side effect is attempted; that commit is the actual
  exclusivity-enforcing moment. This durability guarantee depends on a specific, load-bearing
  mechanism, not merely on the handler calling `SaveChangesAsync`: `PrepareRepositoryWorkspaceCommand`
  is declared `IManualTransactionCommand<TResult>`, not `ICommand<TResult>`, so
  `AddDevalenteEfCoreTransactions`'s automatic per-command transaction wrap never opens an
  ambient transaction around this handler. Were it declared as an ordinary `ICommand<TResult>`,
  that automatic wrap would keep the handler's first `SaveChangesAsync` uncommitted for the
  entire duration of the external Git/marker I/O that follows it — silently reintroducing the
  exact crash-unsafe window this ADR exists to close, even though the handler's own code would
  look unchanged. The handler instead owns two independent, separately committed transactions —
  one for durable intent, one for the final `Ready`/compensating outcome — with no EF transaction
  open across the external I/O between them. An exception or cancellation in that window leaves
  the already-committed `Preparing`/`Active` state exactly as it stood, for
  `ReconcileWorkspacesCommandHandler` to resolve at the next startup, never rolled back and never
  papered over with an invented terminal state. Deterministic, never-reused naming
  (`devalcopilot/workspace/<ProjectId>/<WorkspaceNumber>` and a matching path under
  `%LocalAppData%\DevalCopilot\workspaces\`) comes from a per-project monotonic
  `ReserveWorkspaceNumber()`, the same in-aggregate-counter pattern already proven by
  `ReserveBaselineNumber`. Exactly one Git command ever mutates anything: `git worktree add -b
  <branch> <path> <sha>`, fixed, argument-list-only, and reusing the same hardening discipline
  (`core.fsmonitor`/`core.untrackedCache` forced off, remote protocols disabled) as the existing
  read-only Git inspector. Before that command is even considered, the computed workspace path
  is checked, by a path-segment-aware comparison (never a raw string-prefix check), against the
  main repository's own path: equal, nested beneath it, or an ancestor of it are all rejected as
  a closed `WorkspaceOverlapsMainRepository` outcome, with neither path ever included in the
  resulting safe error text.
- **Administrative directory and marker.** The workspace's own Git administrative directory is
  never assumed to be `<main-repo>\.git\worktrees\<name>` by path convention. After a successful
  `worktree add`, a fixed, read-only `git rev-parse --git-dir`/`--git-common-dir` pair, run from
  inside the new workspace, discovers it, and its reported common directory is cross-validated
  against the one the main repository itself reports the same way — only once both agree is the
  directory trusted. The ownership marker is then written there, atomically: a uniquely named
  temporary file is written, flushed, and closed, then renamed over the fixed final name
  (`devalcopilot-ownership.json`) in one atomic same-volume move — never observable partially
  written, and never written into either working tree. Marker validation is typed-field
  comparison against the durable database record (workspace, project, and lease identifiers,
  and the physical-identity tuple) — never a raw byte or hash comparison, so an incidental
  formatting difference is never mistaken for tampering.
- **Reconciliation.** Startup reconciliation runs once, before any new preparation request is
  accepted, for every still-`Active` lease. It promotes a `Preparing` workspace to `Ready` only
  when Git's own worktree registration, the marker's typed fields, and the workspace's own
  current `HEAD` all agree with the database's recorded intent — any inconsistency, however
  small, fails closed to `FailedToPrepare` instead of being resolved by assumption. A `Ready`
  workspace found missing on disk becomes `MissingExternally`; one whose marker is absent or
  mismatched becomes `AlteredExternally` (both terminal, both superseding the lease); one whose
  marker is valid but whose `HEAD` no longer matches the recorded source commit becomes
  `NeedsAttention` (ownership stands, only content trust is flagged; the lease remains `Active`).
  This slice never attempts to reverse a `NeedsAttention` flag back to `Ready`. Reconciliation is
  declared `IManualTransactionCommand<TResult>` for the same reason workspace preparation is:
  each lease's own transition (if any) is committed via its own explicit, independent
  `SaveChangesAsync` immediately after that lease's evidence is decided — never inside one
  ambient transaction spanning every still-`Active` lease's external Git/marker reads, and never
  one where a later lease's failure could roll back an earlier, already-decided lease's
  transition. A lease found unchanged is never written at all.
- **Active-checkout guarantee.** Workspace preparation never modifies any tracked file,
  untracked working-tree file, `HEAD`, the current branch ref, or index content in the main
  checkout. Git necessarily adds new, additive administrative metadata under
  `.git/worktrees/<name>/` and a new branch ref/reflog under `.git/refs/heads/...` /
  `.git/logs/refs/heads/...` — bounded to exactly those new paths, never touching a pre-existing
  one.

## Consequences

- Every future repository-mutating feature (agent execution, commits, pushes) must acquire this
  lease and operate only inside a `Ready` `GitWorkspace`, never the registered canonical path
  directly.
- Startup reconciliation gains five evidence-driven outcomes it must handle before scheduling
  any further work involving a workspace.
- Existing registrations remain fully usable for read-only baseline display but cannot
  participate in workspace preparation until their physical identity is resolved — surfaced to
  the user as a bounded, explicit "recheck identity" action, never silently blocked forever.
- A volume that is not NTFS or ReFS can never host a mutating workspace — an accepted, visible
  limitation for the Windows-first MVP.
- Lease history is fully preserved (every `Released`/`Superseded` row remains queryable), so a
  future audit or diagnostic view of "what happened to this repository's workspaces over time"
  is never blocked by the exclusivity mechanism itself.

## Non-goals

- Does not implement agent execution, source editing, commit, push, pull request, or CI.
- Does not implement lease renewal, heartbeat, expiry enforcement, or any automatic sweep — all
  remain deferred to the Increment 5 executor-lease work that builds on this slice's lease and
  ownership marker.
- Does not correlate a workspace or lease with a `Run`/objective.
- Does not implement automatic or destructive worktree/branch cleanup, a retry UI, or a
  scheduler.
- Does not extend physical-identity support to network or UNC paths.
- Does not check working-tree dirtiness inside an already-prepared candidate workspace — that
  remains a future consumer's responsibility once real work happens inside one.
- Does not attempt to automatically reverse a `NeedsAttention` workspace back to `Ready`.

## Alternatives considered

### Key the mutation lease off `Project.RegistrationIdentityKey`

Rejected: this is exactly what ADR-0007 named as unsafe — a lexical key cannot distinguish two
differently spelled paths that alias the same physical directory via `subst`, a hardlink, or an
ancestor junction, which would let two "different" registrations both claim exclusive mutation
rights over the same real repository.

### A single (non-partial) unique index on physical identity

Rejected: it would forbid ever preparing a second workspace for a repository after the first
lease is released, since every row — regardless of status — would compete for the same unique
slot. A partial index scoped to `Status = 'Active'` rows is the smallest change that preserves
both guarantees.

### A DB-only ownership claim, no on-disk marker

Rejected: a marker inside Git's own worktree administrative directory gives a second,
independent, tamper-evident source of truth that survives a corrupted or rolled-back database in
a way a database-only claim cannot, at the cost of one small atomic file write.

### Assume the administrative directory by path convention

Rejected: `<main-repo>\.git\worktrees\<name>` is a reasonable guess but not a Git-guaranteed
contract. Discovering and cross-validating it via `rev-parse` costs two cheap, read-only Git
calls and removes an entire class of "wrote the marker somewhere Git doesn't actually own" bugs.
