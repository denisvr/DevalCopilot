# DevalCopilot

DevalCopilot is a local-first, supervised development orchestrator that enables
Codex and Claude Code to plan, challenge, implement, review, verify, and improve
software through one durable workflow.

The product is not a chat wrapper. It is a control plane for agent-assisted
software delivery in which:

- Codex acts primarily as architect and reviewer;
- Claude Code acts primarily as critical executor;
- either agent may challenge assumptions and propose a better course;
- Git and CI provide objective evidence;
- DevalCopilot owns workflow state, process supervision, policy, and recovery;
- the human remains the final authority for consequential actions.

## Current status

The repository has an accepted product, architecture, and security baseline,
and the backend and frontend project scaffolds are in place. Cockpit behavior,
persistence, and agent collaboration are implemented incrementally against the
approved [run cockpit specification](docs/product/run-cockpit-specification.md).
The latest delivered slice and the next required action are recorded in the
[cross-chat handoff](docs/roadmap/current-work.md); verify it against Git before
resuming work in a new chat.

The first release is intentionally local-only. It must be complete enough for a
stable version of DevalCopilot to coordinate development of its next version
without manual message transfer between Codex, Claude Code, and GitHub.

## MVP success statement

The MVP succeeds when a stable DevalCopilot instance can:

1. accept a change request for the DevalCopilot repository;
2. create an isolated branch and Git worktree;
3. coordinate a bounded planning and challenge exchange between Codex and
   Claude Code;
4. supervise implementation, local verification, review, and correction;
5. present the complete run visually and allow human intervention;
6. publish an approved branch and draft pull request;
7. monitor GitHub CI and feed relevant failures back into the agent loop;
8. produce a green, reviewable pull request or a clear human escalation;
9. recover durable history after application restart.

The MVP provides self-hosted development, not in-place self-modification. The
running stable version controls a separate candidate worktree and never
overwrites its own executable or active checkout.

## Technology stack

- Tauri 2 as the thin desktop shell;
- React, TypeScript, and Vite for the user interface;
- .NET 10 and ASP.NET Core as the authoritative local host;
- MVC Controllers for commands and queries;
- SignalR for live notifications, backed by durable event replay;
- Entity Framework Core 10 with file-backed SQLite;
- filesystem storage for large and raw artifacts;
- Git, Codex, Claude Code, and GitHub CLI integrations behind typed adapters.

## Documentation

- [Engineering context](docs/engineering-context.md)
- [Product vision](docs/product/vision.md)
- [MVP scope](docs/product/mvp-scope.md)
- [User experience](docs/product/user-experience.md)
- [Run cockpit functional specification](docs/product/run-cockpit-specification.md)
- [Architecture overview](docs/architecture/system-overview.md)
- [Workflow model](docs/architecture/workflow-model.md)
- [Agent collaboration protocol](docs/architecture/agent-collaboration-protocol.md)
- [Data and recovery](docs/architecture/data-and-recovery.md)
- [GitHub and CI integration](docs/architecture/github-ci-integration.md)
- [Security plan](docs/security/security-plan.md)
- [Security threat model](docs/security/threat-model.md)
- [MVP delivery plan](docs/roadmap/mvp-delivery-plan.md)
- [Current work and cross-chat handoff](docs/roadmap/current-work.md)
- [Architecture decisions](docs/decisions/README.md)
- [Glossary](docs/glossary.md)

## Engineering contract

This project adopts the sibling `EngineeringStandards` repository through
`AGENTS.md`, `CLAUDE.md`, and `docs/engineering-context.md`. Shared standards
are referenced rather than copied so updates remain deliberate and reviewable.

All source code, committed documentation, identifiers, and canonical product
copy are written in English. Product localization may be added through explicit
locale resources later.
