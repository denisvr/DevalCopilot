# ADR-0005: Use Git worktrees and a policy-controlled GitHub loop

## Status

Accepted

## Context

The running application must be able to develop its next version without
changing its active checkout. Agent work must be reviewable, attributable, and
isolated from user changes. Remote CI is part of the evidence loop, and failures
must return to the agents without manual message transfer.

Giving agents direct unrestricted GitHub access would bypass workflow gates and
make destructive or externally visible actions difficult to audit. A local
application also should not introduce custom token storage when the developer
already has authenticated Git and GitHub tooling.

## Decision

- Create one tool-owned branch and isolated Git worktree per active run.
- Permit one writer for a worktree.
- Record Git fingerprints before and after mutating stages.
- Bind review approval to the exact fingerprint and invalidate it after changes.
- Let the orchestrator, not either agent, execute commits, pushes, pull request
  operations, and CI actions.
- Use existing Git authentication for Git operations.
- Use the authenticated GitHub CLI as the initial GitHub Infrastructure adapter
  without extracting or persisting its token.
- Create draft pull requests and correlate checks by repository, branch, and
  exact head SHA.
- Observe CI automatically; gate remote mutation according to risk policy.
- Keep merge, force-push, release, deployment, branch deletion, and repository
  administration outside the MVP.

## Consequences

- A stable DevalCopilot build can safely coordinate a candidate build.
- The application must own worktree cleanup, stale ownership detection, and
  external-state reconciliation.
- GitHub CLI output must be requested in structured form where available and
  normalized behind Application ports.
- Polling with persisted state is required because the local MVP has no public
  webhook endpoint.
- Push or PR timeouts require remote lookup before retry to avoid duplicates.
- CI logs and pull request content are untrusted and require bounding and
  redaction before agent or remote use.

## Alternatives considered

### Modify the user's active checkout

Rejected because it risks uncommitted work, prevents stable self-hosting, and
couples execution to the current interactive branch.

### Give agents direct Git and GitHub authority

Rejected because provider prompts and tool output must not bypass policy,
approval, and audit boundaries.

### Implement GitHub OAuth or a GitHub App in the MVP

Rejected because it adds token lifecycle and callback complexity for a
single-user local application that can reuse an existing CLI session.

### Require inbound webhooks

Rejected because they require a reachable endpoint or tunnel. Resumable polling
is sufficient for one local user.
