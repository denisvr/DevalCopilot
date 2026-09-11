# ADR-0001: Build a local-first supervised orchestrator

## Status

Accepted

## Context

The initial problem is manual coordination between Codex, Claude Code, local
development tools, Git, and GitHub. The project owner currently transfers plans,
implementation reports, failures, and review findings between tools. This loses
structure, makes recovery difficult, and prevents a complete view of how a
change evolved.

The immediate goal is personal local use. There is no current requirement for a
hosted production service, multi-user collaboration, tenant isolation, or
remote workers. The MVP must nevertheless control privileged local execution
and remote source publication safely.

## Decision

Build DevalCopilot as a local-first desktop application with one authoritative
orchestrator and supervised autonomy.

- The application runs on the developer workstation.
- One developer and one active workflow are supported initially.
- The orchestrator owns state, process execution, policies, recovery, Git, and
  GitHub actions.
- Codex and Claude Code are participants without direct authority to bypass the
  orchestrator.
- The human remains the final authority for material disagreements and
  consequential external actions.
- The MVP targets self-hosted development: a stable DevalCopilot version can
  develop a candidate version in an isolated worktree.
- The MVP does not modify its running executable or active checkout in place.
- Containers are optional project capabilities, not mandatory application
  infrastructure.

## Consequences

- Authentication, collaboration, cloud deployment, and distributed consistency
  remain outside the MVP.
- Local privilege and prompt-injection risks remain material and require an S2
  security profile despite the lack of public exposure.
- Process lifecycle, worktree ownership, durable events, and startup
  reconciliation are core product behavior rather than operational extras.
- The system may later add remote execution behind new adapters without moving
  current workflow authority into agent or UI processes.

## Alternatives considered

### Hosted multi-user platform first

Rejected because it adds identity, tenancy, deployment, billing, and operations
before proving the core orchestration model.

### Command-line tool without a durable visual application

Rejected because observation, intervention, history, and visible product
evolution are explicit MVP goals.

### Fully autonomous operation

Rejected for the MVP because unrestricted source and remote authority would
make failures difficult to bound while the workflow is still being proven.
