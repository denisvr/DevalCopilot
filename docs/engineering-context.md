# Engineering context

Project-owned startup context for the shared engineering contract: what an
agent cannot safely rediscover, routed to each subject's authoritative contract
for progressive reading. Accepted ADRs and routed contracts prevail over this
summary; Git and code prevail over every document. An accepted target is not
delivery, and a Proposed ADR authorizes nothing. The
[rework map](roadmap/documentation-rework-map.md) traces the former long form.

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

The [decision index](decisions/README.md) lists every ADR with its narrow
extensions and supersessions; each record is authoritative, and its own status
line prevails. ADR-0001 is partially superseded by ADR-0006. ADR-0050 is
Proposed, documentation only: it selects no mechanism and authorizes no push.

| Subject | Authoritative contracts |
|---|---|
| Scope and cockpit | [MVP scope](product/mvp-scope.md), [cockpit specification](product/run-cockpit-specification.md) |
| Containers, ownership, bootstrap secret, notifications, background work, scheduling | [System overview](architecture/system-overview.md); ADR-0002, ADR-0006 to ADR-0008 |
| Lifecycle, gates, verification, approval and publication state, recovery | [Workflow model](architecture/workflow-model.md) |
| Collaboration core: purpose, companion directory, protocol evolution | [Collaboration protocol](architecture/agent-collaboration-protocol.md); ADR-0004 |
| Messages, roles, reply semantics, authority matrix, structured output failure | [Dialogue](architecture/agent-collaboration-dialogue.md); ADR-0009 |
| Durable ledger, execution-mode isolation, abandonment claim paths | [Ledger](architecture/agent-collaboration-ledger.md); ADR-0014 |
| Attempt input preparation, runtime preflight, provider CLI safety | [Context](architecture/agent-collaboration-context.md), [provider contracts](architecture/agent-collaboration-provider-contracts.md); ADR-0011 |
| Attempt evidence, token and account controls (evidence, not approval or readiness) | [Execution evidence](architecture/agent-collaboration-execution-evidence.md), [evidence inspection](architecture/agent-collaboration-evidence-inspection.md), [usage evidence](architecture/agent-collaboration-usage-evidence.md), [token-activity controls](architecture/agent-collaboration-token-activity-controls.md), [account usage](architecture/agent-collaboration-account-usage.md); ADR-0023, ADR-0025, ADR-0026 |
| Invocation configuration, format repair | [Invocation configuration](architecture/agent-collaboration-invocation-configuration.md), [format repair](architecture/agent-collaboration-format-repair.md); ADR-0004 |
| Guidance, escalation, diagnosis | [Guidance and escalation](architecture/agent-collaboration-guidance-and-escalation.md); ADR-0015, ADR-0016, ADR-0018 to ADR-0020 |
| Review correction, claim and time budgets (not usage) | [Correction and budgets](architecture/agent-collaboration-correction-and-budgets.md); ADR-0010, ADR-0012, ADR-0013, ADR-0017, ADR-0028 |
| Manifest source evidence, checkpoint comparison, local delivery (not remote publication) | [Source evidence](architecture/agent-collaboration-source-evidence.md), [local delivery](architecture/agent-collaboration-local-delivery.md); ADR-0021, ADR-0022, ADR-0024, ADR-0027 |
| Journal, artifacts, secrets, leases, reconciliation, local commit, abandonment | [Data and recovery](architecture/data-and-recovery.md); ADR-0003, ADR-0029, ADR-0031 |
| Approval set, receipt, run, event and collaboration history | ADR-0030 to ADR-0037; protocol |
| GitHub observation, binding, checks, branch approval, permission | [GitHub and CI](architecture/github-ci-integration.md); ADR-0005, ADR-0038 to ADR-0049; ADR-0050 Proposed; publication evidence notes ([index](architecture/README.md)) are not contracts |
| Security | [Security plan](security/security-plan.md), [threat model](security/threat-model.md) |
| Validation and evidence | [Validation policy](roadmap/validation-policy.md) |
| Increments and status | [MVP delivery plan](roadmap/mvp-delivery-plan.md) |
| Procedure, checkpoints, risks | [Workflow](roadmap/development-workflow.md), [current work](roadmap/current-work.md), [planner handoff](roadmap/planner-handoff.md) |

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
- Git, Codex, Claude Code, and GitHub integrations are replaceable
  Infrastructure adapters behind narrow Application ports.
- The MVP uses existing local CLI authentication and never copies provider
  credentials into the application database.

Implemented today: the explicit local commit and the Human-requested surfaces of
ADR-0030 to 0045 and 0047 to 0049. GitHub readings are point-in-time,
never readiness, permission to push or publication authority; installed `gh`
and schema compatibility are unverified. No push, pull request, CI routing or
mutating orchestration exists; ADR-0048 records consent only. The one narrow
exception to explicit per-stage requests is ADR-0055: a single explicit
authorization may arm one read-only planning sequence (Planner,
CriticalReviewer, Resolver; at most five attempts) that the host advances to
Accepted planning or the existing Human escalation. It never starts
implementation, verification, commit or publication. PLAN-COORD-01's review
and publication provenance are recorded in the roadmap checkpoints and Git.
ADR-0060 / LOCAL-COORD-01 (implemented for review, unpublished until its own GO) adds the second narrow exception: one explicit,
single-use, Human-confirmed authorization per ManualAgent run arms one local execution sequence for one Accepted (or separately
Human-authorized depth-two) plan. The host composes the existing implementation, whole pinned verification passes, ordinary review or
eligible failure diagnosis and at most two shared corrections through the same claim gates, ending at an approved review awaiting the
Human's own approval, a Human escalation or an attention state; it never approves, commits or publishes. A linked dispatched operation
without coherent host Exited evidence stays an effective execution owner (no supported release exists), and a restart never resumes,
redispatches or adopts. ADR-0061 / LOCAL-INSPECT-01 (implemented for review, unpublished until its own GO) adds only read-only recorded
inspection of that cycle: three bodyless GETs (index, selected step, one verification stream window) and one collapsed region in the
current run and run history; it grants, changes and dispatches nothing. Increments 4 to 6 stay incomplete.
ADR-0062 / LOCAL-QUEUE-01 (implemented for review, unpublished until its own GO) adds only an explicit, separately confirmed queued intake
mode for that same cycle: the cycle waits as a durable pending admission and the host, never the page, reserves the oldest eligible one
under the write lock before external preparation; it adds no priority, executor lease, retry, takeover or process-aware recovery and
changes no approval, commit or publication authority. Increments 4 to 6 stay incomplete.
ADR-0063 / LOCAL-QUEUE-INSPECT-01 (implemented for review, unpublished until its own GO) adds only a fourth bodyless GET and one collapsed
region inside that inspector: the recorded admission ledger of one queued cycle, including a wait retired without a step, proved from one
database statement, with an explicit action that reuses the existing selected-step evidence; it grants, changes and dispatches nothing.
Increments 4 to 6 stay incomplete.
ADR-0064 / LOCAL-QUEUE-OVERVIEW-01 (implemented for review, unpublished until its own GO) adds only a fifth bodyless GET and one collapsed
global region beside the execution-capacity control: the recorded Waiting, Reserved and Unresolved admissions of every queued cycle across
projects, proved from one database statement, with an explicit action that reuses the existing per-run inspector for the exact run; it
reads without polling, grants, changes and dispatches nothing, and an empty answer is not an idle host. Increments 4 to 6 stay incomplete.
ADR-0056 adds only operational evidence for all seven Agent response contracts: a
durable observation of what the host's own observer last reported (phase, last
report, absolute deadline, first attention) for the current attempt, an independent
bounded sweep and the startup closure of a previous host's observations. It is not
provider progress, process liveness or an execution lease, retries, cancels or
takes over nothing, and is never an eligibility input. ADR-0057 /
WORKSPACE-EXCL-01 makes one shared Application policy the single reading of who
may execute in a project's owned workspace: an unresolved Agent attempt of any
run (all seven contracts, repairs and coordinated planning claims), an
unresolved verification execution, or ADR-0029's local-commit reservation. A
claim is admitted only into an Idle workspace and a single-use dispatch gate
only beside no owner other than its exact operation; conflicting or
untrustworthy ownership fails closed and nothing is terminalized, cancelled,
retried or released. It adds no table, queue, slot or scheduler and no
standalone Process ownership. ADR-0058 / GLOBAL-ADMIT-01 bounds recorded potentially mutating owners per authoritative database (existing Agent, verification and local-commit rows only; no queue, lease or release) and withholds new mutating starts in both cockpit regions. ADR-0059 / GLOBAL-LIMIT-01 makes that bound an explicit, durable operator choice of one or two (default two; immutable configuration revisions in one additive table; a lower limit is refused while more owners are recorded) read fresh at every admission and shown by one global control. Increments 4 to 6 stay incomplete; no MVP
completion is implied. Text-delivery controls
(ADR-0021, 0022, 0024, 0027) close named routes, not every filesystem read;
their physical proofs are Windows-only. Token and account-usage controls are
local guards, not account, cost or capacity budgets. Execution limits:
[open risks](roadmap/current-work.md#open-risks).

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
  database. Under ADR-0046 and the
  [validation policy](roadmap/validation-policy.md#class-owned-sealed-sqlite-snapshot-adr-0046),
  one class needing a real delivery lineage for many independent facts may seed
  it once per class execution and give each fact its own SQLite backup.
  ADR-0047 extends that narrowly to its own endpoint class, through
  feature-owned composition of the same producer, its own fixture instance and
  its observer in the sealed-scene refusal inventory; it migrates no other
  class. No other shared test data or cross-run cache is approved.
- Agent, Git, GitHub, and local process adapters use deterministic executable
  test doubles for contract and failure-path tests. Automated tests do not
  call real providers.
- Browser workflows use Playwright against a test host and test adapters.
- Docker or Testcontainers are used only when the real boundary under test
  requires a containerized provider.

## Package adoption

Adopt only released `Devalente.Shared.*` packages whose responsibilities a
current slice needs. Adopted at 0.2.0 (`Directory.Packages.props`): CQRS
abstractions and mediator, Results, FluentValidation integration, EF Core
integration, MVC Problem Details, Security and NSwag generation. Auditing and
soft deletion remain opt-in by actual entity and host requirements.

## Approved deviations

None. Local-only tailoring and file-backed SQLite integration tests are project
decisions within the shared standards rather than exceptions to them.
