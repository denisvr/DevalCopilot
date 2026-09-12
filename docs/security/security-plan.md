# DevalCopilot security plan

## Profile

- Selected profile: S2, tailored for a privileged local developer tool
- Decision record: [ADR-0001](../decisions/0001-build-a-local-first-supervised-orchestrator.md)
- Owner: project owner
- Last review: 2026-09-11
- Next review: before the self-hosting MVP demonstration or any material change
  to exposure, identities, execution, or remote authority
- Exposure and worst-case impact: local loopback application with child-process,
  repository, and GitHub authority; compromise could disclose credentials,
  corrupt source history, publish malicious changes, or execute code as the
  developer

## Assets and data

| Asset or data | Confidentiality | Integrity | Availability | Retention owner |
|---|---|---|---|---|
| Local source repositories | High | High | High | Repository owner |
| Git and GitHub credentials | Critical | Critical | Medium | CLI and OS credential store owner |
| Agent provider sessions | High | High | Medium | Provider CLI and user |
| Workflow database | Medium | High | High | DevalCopilot |
| Logs and transcript artifacts | High | High | Medium | DevalCopilot run retention policy |
| Decisions, approvals, and findings | Medium | Critical | High | DevalCopilot |
| Candidate application builds | Medium | Critical | Medium | Build and release workflow |

## Identities and privileges

| Identity | Authentication | Allowed capabilities | Credential owner | Rotation |
|---|---|---|---|---|
| Local developer | Operating-system session | Configure, approve, intervene | Operating system and user | Platform policy |
| React frontend | Random per-launch session secret | Local MVC and notification boundary | .NET host memory | Every launch |
| .NET host | Tauri-launched process identity | Orchestration and configured adapters | Operating-system user | Process lifetime |
| Codex CLI | Existing provider-managed session | Scoped agent attempt | Codex CLI and user | Provider policy |
| Claude Code CLI | Existing provider-managed session | Scoped agent attempt | Claude Code and user | Provider policy |
| Git | Existing Git and SSH configuration | Policy-authorized repository operations | Git/SSH tooling and user | Provider policy |
| GitHub CLI | Existing CLI-managed session | Policy-authorized GitHub operations | GitHub CLI and user | GitHub policy |

No agent response is an identity or authorization decision.

## Trust boundaries and dependencies

| Boundary | Data exchanged | Authentication | Authorization | Failure owner |
|---|---|---|---|---|
| React to local API | Intentions, queries, event cursor | Per-launch session | MVC operation and application policy | DevalCopilot host |
| Host to repository | Files, Git metadata, commands | OS identity | Approved root and worktree policy | Project/run |
| Host to agent CLI | Prompt context and structured output | Existing CLI session | Attempt scope and capability policy | Agent adapter |
| Host to project tools | Typed arguments, stdout/stderr | OS identity | Configured verification command | Project configuration |
| Host to GitHub CLI | Repository and PR/check operations | Existing CLI session | Remote-action policy and approval | GitHub adapter |
| Host to SQLite/artifacts | State, events, evidence | OS filesystem ACL | Application ownership | Persistence layer |

## Entry points

| Entry point | Exposure | Policy | Abuse control |
|---|---|---|---|
| MVC API | Loopback only | `Authorization: Bearer <session-secret>` per launch; explicit operation policy | Request limits, cancellation, idempotency |
| SignalR hub | Loopback only | Same bearer secret over LongPolling only; no `access_token` query string | Message and connection limits; durable catch-up |
| Tauri commands | Bundled webview only | Minimal named capabilities | No generic shell or broad filesystem scope |
| Project registration | Local user | Canonical approved roots | Path validation and explicit confirmation |
| Agent output | Child-process stream | Untrusted evidence | Size, schema, rate, and artifact limits |
| CI and PR content | GitHub CLI response | Untrusted evidence | Exact-SHA correlation, bounding, redaction |

## Secrets and keys

Secret values are never recorded here or in application persistence.

| Purpose | Location | Scope | Owner | Revocation |
|---|---|---|---|---|---|
| Local frontend session | .NET host and webview memory | One host launch | DevalCopilot | Process exit |
| Codex authentication | Provider CLI credential storage | Provider account | User and provider | Provider CLI/account |
| Claude Code authentication | Provider CLI credential storage | Provider account | User and provider | Provider CLI/account |
| GitHub authentication | GitHub CLI credential storage | Configured repositories | User and GitHub | `gh` and GitHub settings |
| Git/SSH authentication | Git credential manager or SSH agent | Configured remotes | User | Git provider and local agent |

The application must not call credential-export commands or copy tokens from
provider configuration into prompts, logs, SQLite, or artifacts.

## External controls

| Control | Owner | Environment | Evidence | Status |
|---|---|---|---|---|
| Workstation account and filesystem protection | User and operating system | Local | Platform configuration review | External |
| Repository branch protection | Repository owner | GitHub | Repository ruleset inspection | Planned before remote MVP |
| GitHub authentication scope | User | GitHub CLI | Auth status and permission test without token output | Planned |
| Provider CLI authentication scope | User | Local/provider | Capability health checks | Planned |
| Dependency and secret scanning | Repository workflow | Local and CI | Required checks | Planned |
| Trusted release artifact | Future release workflow | Local/GitHub | Checksums and signing evidence | Deferred beyond MVP |

## Evidence register

| Control | Profile | Implementation | Evidence | Status |
|---|---|---|---|---|
| Loopback-only API | S2 | Host binding configuration | API integration test | Planned |
| Per-launch API authentication | S2 | Stdin-delivered bootstrap secret (never in argv/env); Bearer scheme; shell/host each tear down on the other's exit | API integration test: `401` for absent and incorrect credential, success for correct credential, `access_token` query-string auth rejected | Planned |
| Bootstrap secret browser hygiene | S2 | React holds the secret in module memory only | Frontend/browser test: bootstrap context never enters browser storage or the URL | Planned |
| Bootstrap secret log hygiene | S2 | Secret never written to stdout, stderr, or structured logs | Log-capture integration test: the known test credential never appears in captured host logs | Planned |
| Packaged bundle secret hygiene | S2 | No development authentication endpoint in the production host; production Vite build | Build/source scan: packaged frontend bundle contains no known bootstrap credential or test-authentication bypass | Planned |
| Explicit MVC authorization | S2 | Default deny and operation declarations | Endpoint inventory test | Planned |
| No frontend generic shell | S2 | Tauri capability configuration | Configuration and browser test | Planned |
| Approved-root path containment | S2 | Canonical path policy | Unit and integration tests | Planned |
| Typed process arguments | S2 | Process adapter boundary | Injection test suite | Planned |
| Single worktree writer | S2 | Ownership lease and Git policy | Concurrency and recovery tests | Planned |
| Scoped approval and invalidation | S2 | Application policy | State-transition tests | Planned |
| Exact-SHA CI evidence | S2 | CI correlation policy | Adapter contract tests | Planned |
| No stored provider credentials | S2 | CLI-owned authentication | Persistence and artifact scan | Planned |
| Bounded output and iteration | S2 | Attempt and run budgets | Failure-path tests | Planned |
| Self-hosting isolation | S2 | Stable controller and candidate worktree | MVP demonstration review | Planned |

## Exceptions and residual risks

The current residual-risk register is maintained in the
[threat model](threat-model.md#residual-risks). No permanent security exception
is accepted by this document. A new exception must include an owner, expiry,
compensating control, and review date.

## Incident and recovery

- Private reporting channel: direct report to the project owner; a repository
  security policy will define the channel before external collaboration.
- Alert and escalation owner: project owner.
- Credential revocation: stop DevalCopilot, terminate child processes, revoke or
  refresh affected provider/GitHub/Git credentials through their owning tools,
  then inspect persisted artifacts for exposure.
- Trusted rebuild: restore source from an independently verified commit, clear
  untrusted build output, restore dependencies from locked manifests, run the
  complete validation suite, and produce a new package through the approved
  build path.
- Database recovery: preserve the affected database and artifacts for diagnosis,
  restore from the most recent verified local backup, and reconcile GitHub and
  repository state before resuming automation.
- Notification owner: project owner; external notification obligations are not
  currently applicable but must be reassessed if user or regulated data enters
  scope.
