# ADR-0006: Support bounded concurrent runs across projects

## Status

Accepted

## Context

ADR-0001 limited the initial product to one active workflow. The approved run
cockpit and intended personal workflow require DevalCopilot to supervise work
for several registered projects without making the user wait for unrelated CI,
review, or agent activity to finish.

Unrestricted concurrency would increase the risk of two runs modifying the same
repository, oversubscribing provider allowances, or bypassing SQLite's intended
single-writer discipline. The product needs concurrency between independent
projects while retaining exclusive mutation ownership inside each project.

## Decision

Supersede only ADR-0001's one-active-workflow constraint with bounded
multi-project concurrency:

- allow active runs for distinct canonical repositories concurrently;
- default to at most two active mutating runs globally, with a configurable
  limit that can be reduced to one;
- permit at most one mutating run per canonical repository;
- retain one writer per tool-owned worktree and one owner per external attempt;
- queue conflicting runs with a visible reason rather than racing them;
- serialize SQLite state changes through the existing ingestion boundary;
- enforce run budgets and provider account-usage guardrails independently;
- treat CI observation and human-approval waits as non-mutating activity that
  does not consume an agent execution slot.

All other ADR-0001 decisions remain accepted, including one local developer,
one authoritative orchestrator, supervised autonomy, and local-only operation.

## Consequences

- The project switcher can truthfully show several runs progressing at once.
- Run commands, notifications, leases, and UI projections must always carry an
  explicit run and project identity.
- The scheduler needs global, repository, worktree, and provider eligibility
  checks before claiming an attempt.
- A pause, stop, failure, or exhausted budget for one run does not implicitly
  stop unrelated projects.
- Integration tests must prove both safe concurrency across repositories and
  exclusion within one repository.
- Resource pressure remains bounded by a conservative default and explicit user
  configuration.

## Alternatives considered

### Keep one global active run for the MVP

Rejected because an unrelated wait for CI, review, or a provider would block all
other managed projects and contradict the approved cockpit model.

### Allow unrestricted concurrent runs

Rejected because it would make provider consumption and local resource use
unpredictable and would require conflict handling that the MVP does not need.

### Allow multiple writers in one repository

Rejected because separate worktrees do not eliminate shared Git metadata,
publication, policy, and human-review conflicts. Repository-level mutation
exclusion is the safer MVP boundary.
