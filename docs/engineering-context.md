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
- [ADR-0011](decisions/0011-require-administrator-provisioned-policy-before-gemini-cli-execution.md):
  Require administrator-provisioned policy before Gemini CLI execution.
- [ADR-0012](decisions/0012-add-a-durable-run-wide-agent-claim-budget.md): Add
  a durable run-wide Agent claim budget.
- [ADR-0013](decisions/0013-add-a-durable-run-wide-agent-invocation-time-budget.md):
  Add a durable run-wide Agent invocation-time budget.
- [ADR-0014](decisions/0014-add-manual-agent-run-intake-with-durable-execution-mode-isolation.md):
  Add manual Agent run intake with durable execution-mode isolation.
- [ADR-0015](decisions/0015-add-direct-human-guidance-to-explicit-mutation-requests.md):
  Add direct human guidance to explicit mutation requests.
- [ADR-0016](decisions/0016-add-explicit-human-authorization-of-one-escalated-plan-implementation.md):
  Add explicit human authorization of one escalated-plan implementation (its preservation of the ordinary
  first-revision review target is narrowly superseded by ADR-0017).
- [ADR-0017](decisions/0017-review-the-implemented-plan-through-correction.md):
  Review the implemented plan through correction.

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
- Agent stages that receive bounded Git change evidence also receive bounded, identity-verified text previews of
  eligible untracked files, each explicitly marked complete, shortened, or omitted. A preview is admitted only
  from a handle proven physically inside the approved worktree (Windows) whose bytes match the fingerprint's
  raw-content identity; other hosts omit it. Its text exists only in the sealed manifest and provider input (see
  [the protocol](architecture/agent-collaboration-protocol.md#bounded-untracked-file-previews-in-agent-manifests)).
- The tracked side of that evidence is a deterministic selection of complete file headers and hunks under an 8 KiB
  UTF-8 bound, fitted with the untracked previews inside the 32 KiB manifest ceiling; omitted tracked material is
  accounted for with fixed reasons and the selection is never described as a complete or applyable patch. A valid
  text hunk too large to select whole adds only a separate, at most 4 KiB, explicitly incomplete sample of its changed
  lines outside `diff` (see
  [the protocol](architecture/agent-collaboration-protocol.md#bounded-tracked-hunk-evidence-in-agent-manifests)).
- The two Claude paths that can edit the worktree (initial implementation and review correction) may carry one
  owner-requested, run-scoped agentic-turn limit, snapshotted immutably on each claimed attempt and passed as the
  provider's documented `--max-turns` argument under the `claude-implementation-v2` and `claude-review-correction-v2`
  contracts. It is a provider-loop request beside the host timeout, never a measured count, a token, cost, or account
  ceiling, or a host-enforced limit (see
  [the protocol](architecture/agent-collaboration-protocol.md#optional-claude-agentic-turn-limit-for-mutation-attempts)).
- The two explicit mutation requests (initial implementation and ordinary review correction) may carry optional bounded
  direct human guidance, snapshotted immutably on the exact claimed attempt and sealed into its context beside a fixed
  advisory boundary. It is advisory clarification only, available within the ordinary correction budget, and a fact of what
  the host supplied, never of provider compliance (see
  [the protocol](architecture/agent-collaboration-protocol.md#optional-direct-human-guidance-for-mutation-requests)).
- A completed second challenge round ends a proposal lineage in a human escalation. A human may explicitly authorize exactly
  one initial implementation claim for that final plan with a required, bounded reason; authorizing and requesting the
  implementation are separate operations, the grant is consumed atomically by the ordinary explicit claim (and stays spent
  even if the attempt fails), and no automatic review or resolution cap, budget, or execution gate is raised or bypassed (see
  [ADR-0016](decisions/0016-add-explicit-human-authorization-of-one-escalated-plan-implementation.md) and
  [the protocol](architecture/agent-collaboration-protocol.md#explicit-human-authorization-of-one-escalated-plan-implementation)).
- Every newly claimed code review, its manual format repair, and re-reviews throughout the ordinary correction chain judge
  the exact Proposal the initial implementation consumed (the root, the revision, or the authorized final plan), while the
  Planner root remains the lineage identity and correction reply target. The change is forward only: a review sealed earlier
  replays its existing bytes, and history is never rewritten (see
  [ADR-0017](decisions/0017-review-the-implemented-plan-through-correction.md)).
- Token usage is a visible, best-effort measurement at attempt and run level
  when provider data is available. It is not an account or cost budget and no
  account-usage threshold is enforced anywhere in the system. The one
  enforced token control is an owner-configured, run-scoped, provider-separated
  stop on locally recorded token activity that refuses the next Agent claim for
  that provider once reached or unprovable; it is a retrospective local
  guardrail, not an account allowance or a per-attempt cap, and the advisory
  warning is separate (see the open risks in `docs/roadmap/current-work.md`).
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
