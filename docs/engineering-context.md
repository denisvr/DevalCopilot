# Engineering context

- Standards revision: `805439c3316ed64b0a56c933103156e9fb853a2b`
- Root namespace: `DevalCopilot`
- Topology: modular monolith with a thin desktop shell and one local host
- Security profile: S2, tailored for a privileged local developer tool
- Backend: .NET 10 and ASP.NET Core MVC
- Frontend: React and TypeScript hosted in Tauri 2
- Persistence: Entity Framework Core 10 with file-backed SQLite
- Generated clients: NSwag TypeScript for the MVC API
- Live transport: SignalR notifications with cursor-based durable replay
- Initial operating system: Windows-first, with cross-platform boundaries
  preserved where they do not compromise the MVP
- Deployment: local desktop only; no production service deployment is planned
  for the MVP

## Project decisions

- [ADR-0001](decisions/0001-build-a-local-first-supervised-orchestrator.md):
  Build a local-first supervised orchestrator.
- [ADR-0002](decisions/0002-use-tauri-react-and-dotnet.md): Use Tauri,
  React, and .NET 10 with one authoritative .NET host.
- [ADR-0003](decisions/0003-use-a-durable-sqlite-event-journal.md): Use a
  durable SQLite event journal with materialized state.
- [ADR-0004](decisions/0004-use-a-structured-agent-collaboration-protocol.md):
  Use a structured and bounded agent collaboration protocol.
- [ADR-0005](decisions/0005-use-git-worktrees-and-a-policy-controlled-github-loop.md):
  Use Git worktrees and a policy-controlled GitHub/CI loop.
- [ADR-0006](decisions/0006-support-bounded-concurrent-runs-across-projects.md):
  Support bounded concurrent runs across projects.
- [ADR-0007](decisions/0007-registration-time-repository-identity-is-non-authoritative-for-mutation-exclusion.md):
  Registration-time repository identity is non-authoritative for mutation
  exclusion.
- [ADR-0008](decisions/0008-physical-repository-identity-and-tool-owned-worktree-ownership.md):
  Physical repository identity and tool-owned worktree ownership.
- [ADR-0009](decisions/0009-separate-agent-roles-effects-and-provider-assignments.md):
  Separate agent roles, effects, and provider assignments.
- [ADR-0010](decisions/0010-add-review-correction-response-contract.md): Add a
  role-scoped review-correction response contract.

## Product-specific architecture

- The .NET host is the only authority for workflow state, policy, persistence,
  process execution, Git mutation, and remote publication.
- Tauri owns window lifecycle, sidecar lifecycle, packaging, and narrowly
  scoped native capabilities. It contains no workflow or domain policy.
- React renders current state and submits intentions. It never executes Git,
  agent, shell, or GitHub commands directly.
- MVC operations own commands and queries. SignalR carries notifications only;
  it is never the source of durable state and does not dispatch business
  commands.
- Long-running agent or GitHub operations never execute inside an EF Core
  transaction. Application commands persist intent in short transactions, and
  supervised background processes perform external work.
- SQLite writes are serialized through an application-owned ingestion boundary.
- The run scheduler permits bounded concurrency across distinct repositories,
  while enforcing one mutating run per canonical repository and one writer per
  worktree.
- Event order is defined by a monotonic integer sequence, not wall-clock time.
- Large logs, screenshots, patches, and generated reports are stored as hashed
  filesystem artifacts with metadata in SQLite.
- Agent context is assembled progressively from the smallest sufficient set of
  durable records and evidence. Complete transcripts and unchanged repository
  content are not replayed by default.
- Token usage is a visible, enforceable budget at attempt, stage, and run level
  when provider data is available.
- Git, Codex, Claude Code, and GitHub integrations are replaceable
  Infrastructure adapters behind narrow Application ports.
- The MVP uses existing local CLI authentication and never copies provider
  credentials into the application database.

## Security tailoring

S2 is selected because the application executes code, edits repositories,
interacts with privileged developer credentials, and may publish remote Git
changes. The application is local-only, but compromise could materially affect
source repositories and connected systems.

- The local API binds only to loopback on an ephemeral port.
- A per-launch secret authenticates the bundled frontend and remains in memory.
- Browser storage does not contain credentials or provider tokens.
- The frontend has no generic shell capability.
- Approved project roots constrain filesystem access.
- Repository text, tool output, generated content, and CI logs are untrusted.
- Push, pull request, merge, release, deployment, destructive Git, and workflow
  actions follow explicit risk policies.

## Testing tailoring

- SQLite integration tests use disposable file-backed databases because SQLite
  is the production provider. EF Core InMemory is not provider evidence.
- MVC integration tests use `WebApplicationFactory` and a disposable SQLite
  database.
- Agent, Git, GitHub, and local process adapters use deterministic executable
  test doubles for contract and failure-path tests. Automated tests do not
  call real providers.
- Browser workflows use Playwright against a test host and test adapters.
- Docker or Testcontainers are used only when the real boundary under test
  requires a containerized provider.

## Package adoption

Adopt only released `Devalente.Shared.*` packages whose responsibilities are
needed by a current slice. The likely initial set is CQRS abstractions and
mediator, Results, FluentValidation integration, EF Core integration, MVC
Problem Details, and NSwag generation. Auditing, soft deletion, and security
packages remain opt-in according to actual entity and host requirements.

## Approved deviations

None. Local-only tailoring and file-backed SQLite integration tests are project
decisions within the shared standards rather than exceptions to them.
