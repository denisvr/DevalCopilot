# ADR-0003: Use a durable SQLite event journal

## Status

Accepted

## Context

Agent, process, Git, and CI work is long-running and failure-prone. The
application must show a complete timeline, resume observation after restart,
distinguish retries, preserve decisions, and reconcile actions whose responses
may have been lost.

A transcript alone cannot represent current state or enforce transitions. Full
event sourcing would add replay, migration, and modeling cost that the MVP does
not require. A server database would contradict the local-first goal without
providing a current scaling benefit.

## Decision

Use Entity Framework Core 10 with a file-backed SQLite database and an
event-sourcing-lite model:

- append immutable, versioned events for every material workflow fact;
- maintain normalized current-state tables for operational queries;
- commit each event and its materialized state change atomically;
- define event order with a monotonic integer sequence;
- serialize application writes;
- keep transactions short and exclude external process or network duration;
- store large raw content in a hashed filesystem artifact store;
- use durable intent, immutable attempts, finite leases, and startup
  reconciliation for external work.

## Consequences

- The UI can replay run history and recover missed live notifications.
- Current-state queries do not require replaying the entire event history.
- SQLite concurrency, type, and migration limitations require explicit design,
  single-writer discipline, and recoverable migration backups.
- File-backed SQLite is the real provider in integration tests; EF Core InMemory
  is not valid provider evidence.
- Event and artifact retention require explicit deletion behavior.
- Moving to a server database later is a persistence migration, not an
  invisible provider swap.

## Alternatives considered

### Transcript files only

Rejected because they cannot safely enforce workflow transitions, approvals,
idempotency, queries, or recovery.

### Full event sourcing

Rejected because mandatory replay and event-only state would add complexity
without an MVP requirement.

### PostgreSQL

Rejected for the local MVP because it creates installation or container
requirements without a current concurrency or scale need.

### In-memory state with periodic snapshots

Rejected because a crash could lose authority-relevant transitions and make
external reconciliation ambiguous.
