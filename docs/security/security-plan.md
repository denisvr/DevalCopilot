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
| Git | Existing Git and SSH configuration | Policy-authorized repository operations; an explicit read-only observation of the project's own repository-local `origin` URL ([ADR-0039](../decisions/0039-observe-the-project-local-github-origin-on-explicit-request.md)) that reads no global, system, included or worktree value, no credential and no network and grants no authority; and an explicit Human selection of an immutable GitHub owner and repository for a completed local delivery ([ADR-0040](../decisions/0040-record-an-immutable-github-observation-scope-for-a-completed-local-delivery.md)) that is persisted locally only, invokes no Git, process or network operation and verifies and authorizes nothing | Git/SSH tooling and user | Provider policy |
| GitHub CLI | Existing CLI-managed session | Policy-authorized GitHub operations; an explicit read-only stored-authentication observation ([ADR-0038](../decisions/0038-observe-stored-github-cli-authentication-on-explicit-request.md)) that never extracts the token and grants no authority; and an explicit read-only repository identity observation ([ADR-0041](../decisions/0041-observe-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md)) through one fixed GraphQL query with the stored default authentication only (no ambient token, host or configuration), which proves no branch, commit, permission or CI result and persists nothing; and an explicit read-only remote branch tip observation ([ADR-0042](../decisions/0042-observe-the-recorded-github-branch-tip-of-a-recorded-observation-target-on-explicit-request.md)) with the same boundary, which reports a point-in-time branch tip and proves no CI result, permission, durable binding or publication approval and persists nothing; and an explicit Human binding of the observed opaque repository ID ([ADR-0043](../decisions/0043-bind-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md)) that re-runs the ADR-0041 observation inside the host, is persisted locally as historical identity evidence only and grants no permission, branch or commit proof, CI approval or publication authority; and an explicit read-only commit check observation ([ADR-0044](../decisions/0044-observe-the-check-summary-of-a-bound-recorded-local-commit-on-explicit-request.md)) with the same boundary through one fixed GraphQL query, which reports GitHub's point-in-time aggregate check state for the recorded commit only when the observed repository ID equals the immutable binding and the exact recorded branch points at that commit, persists nothing and is not complete check or workflow evidence, a required-check approval, a permission or publication authority; and an explicit read-only check-context inspection ([ADR-0045](../decisions/0045-inspect-the-check-contexts-of-a-bound-recorded-local-commit-on-explicit-request.md)) with the same boundary through one fixed GraphQL query and, for that one adapter only, a 128 KiB per-stream output cap, which reports the bounded collection (at most 50) of check-run and commit-status metadata GitHub returns for the recorded commit only under the same identity and exact-branch conditions, treats every label as inert untrusted text under a strict text contract and reapplies the existing fixed secret-shape redaction to it after decoding, persists nothing and is not complete check or workflow evidence, a required-check approval, a permission or publication authority; and an explicit read-only check run output inspection ([ADR-0047](../decisions/0047-inspect-the-output-of-one-github-check-run-of-a-bound-recorded-local-commit-on-explicit-request.md)) in two fixed GraphQL phases with the same boundary (association metadata first, with the small 8 KiB cap, and the output only after the host proved that the check belongs to the bound repository and the recorded commit and re-admitted the CLI snapshot; the output phase alone has the 256 KiB per-stream cap), which treats the check name, title, summary and text as untrusted provider text: decoded once, held to an explicit Unicode contract and byte bounds, redacted once more with the existing fixed redaction (five supported secret shapes only; not necessarily the provider's original text), shown only as inert plain text and never truncated, persisted, retained, rendered as Markdown or HTML, acted on or sent to an Agent, and is not a diagnosis, complete check or workflow evidence, a required-check approval, a permission or publication authority | GitHub CLI and user | GitHub policy |

An explicit Human approval of ONE future normal push of a completed local commit to its recorded owned branch in the repository named by a coherent recorded target and binding
([ADR-0048](../decisions/0048-approve-one-future-github-branch-push-of-a-bound-recorded-local-commit-on-explicit-request.md)) is a local, immutable authority input only. The protected write
accepts exactly seven strict string members under an 8 KiB bound (query keys, unknown, duplicate, differently cased and escaped members are refused before dispatch), requires every
acknowledgement to equal the host-resolved pinned scope, decides under one short write-locked transaction that compares the complete scope by value, and saves one immutable record (update and
delete triggers, restrictive keys) and one Human event atomically; the host, action and policy version are fixed project constants and the client supplies no URL, refspec, credential, message
or timestamp. It invokes no process, Git, `gh`, network, Agent or run transition, is never persisted ahead of or without its event (an orphan or incoherent record is unavailable, never a
success), and is not a permission, readiness, a pushed commit or an instruction to any Agent; any future executor must re-check ownership, the commit, the remote identity, the reference,
authentication, permission and ambiguity under its own explicit request and review.

An explicit, read-only observation of the repository permission GitHub reports for the stored default authentication on a bound recorded repository
([ADR-0049](../decisions/0049-observe-the-github-repository-permission-of-a-bound-recorded-target-on-explicit-request.md)) is ephemeral reported access and grants no authority. The protected,
bodyless route refuses any query key or request body before dispatch with fixed non-echoing errors; the host reads the recorded scope and binding itself, invokes one fixed `gh api graphql` query
through the existing process executor only for a current, successful, direct-executable `GitHubCli` snapshot (no discovery, shell, retry, credential read, ambient token, host or configuration, no
output sink, 15 seconds, 8 KiB per stream, one non-queued in-flight slot), refuses any backslash, extra or unknown member and any literal outside the six GitHub defines, compares the whole scope by
value after the call and stamps the host clock after revalidation. It writes nothing, never reports a role for a repository whose ID differs from the immutable binding, and is neither permission to
push the branch nor an input to any approval, claim or Agent; no ADR-0048 approval is required, consumed or changed by it, and installed-CLI and schema compatibility remain unverified.

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
| Local quarantine release | Loopback MVC GET and POST, same bearer policy | Human-only, exact versioned confirmation with a two-minute expiry; original-host termination evidence required | Closed 512-byte request, write-locked revalidation, exact replay, one append-only release and one event |
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
| Approved-root path containment | S2 | Canonical path policy | Unit and integration tests: `ChildProcessExecutionAdapterTests` rejects a working directory outside the approved root, including filesystem-root edge cases | Implemented |
| Typed process arguments | S2 | Process adapter boundary | Injection test suite: `ChildProcessExecutionAdapterTests.Arguments_containing_shell_metacharacters_reach_the_child_literally` | Implemented |
| Single worktree writer | S2 | Ownership lease and Git policy | Concurrency and recovery tests | Planned |
| Scoped approval and invalidation | S2 | Application policy | State-transition tests | Planned |
| Exact-SHA CI evidence | S2 | CI correlation policy | Adapter contract tests | Planned |
| No stored provider credentials | S2 | CLI-owned authentication | Persistence and artifact scan | Planned |
| Bounded output and iteration | S2 | Attempt and run budgets | Failure-path tests: `ChildProcessExecutionAdapterTests.Output_beyond_the_capture_caps_is_discarded_and_flagged_truncated` | Implemented |
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
