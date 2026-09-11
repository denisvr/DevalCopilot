# ADR-0002: Use Tauri, React, and .NET

## Status

Accepted

## Context

DevalCopilot needs a high-density developer interface containing timelines,
agent conversations, terminal output, diffs, test results, CI status, artifacts,
and approvals. It also needs reliable process supervision, workflow state,
transactions, recovery, and integration with the existing Devalente engineering
standards and shared packages.

A single owner must control workflow and persistence. Splitting policy between
Rust, JavaScript, Python, and .NET would create competing sources of truth.

## Decision

Use:

- Tauri 2 as a thin desktop shell;
- React, TypeScript, and Vite for the frontend;
- .NET 10 as the authoritative application host;
- ASP.NET Core MVC Controllers for commands and queries;
- SignalR for post-commit live notifications;
- NSwag for the generated TypeScript MVC client.

The Tauri shell starts and observes a packaged self-contained .NET sidecar and
owns only desktop lifecycle, packaging, and narrowly scoped native capabilities.
The React frontend owns presentation. All domain behavior, workflow state,
policy, persistence, process execution, Git mutation, and remote integration
remain in the .NET host.

The backend follows Clean Architecture, CQRS, vertical slices, and the modular
monolith profile from the shared engineering contract.

## Consequences

- The project uses .NET, Node, and Rust build toolchains, but only .NET contains
  product authority.
- React can use mature editor, terminal, diff, and interaction libraries.
- The local loopback API requires per-launch authentication and strict origin
  configuration even though it is not internet-facing.
- SignalR disconnection cannot lose durable state because clients catch up by
  event sequence through a query.
- Packaging produces platform-specific Tauri bundles and .NET sidecars.
- Native AOT is deferred until compatibility and packaging value are proven.

## Alternatives considered

### Avalonia with an all-.NET UI

Rejected as the primary choice because the product is dominated by web-mature
developer controls and rapid visual iteration. It remains a credible fallback
if reducing toolchains becomes more important than frontend ecosystem breadth.

### Python and FastAPI orchestration core

Rejected because the MVP integrates provider CLIs rather than Python-specific
AI libraries. Python would duplicate backend architecture and distribution
without enough benefit.

### Rust orchestration core inside Tauri

Rejected because it would move the largest product responsibility into a new
engineering stack and discard the existing .NET standards and packages.

### Electron and Node orchestration

Rejected because it provides no decisive benefit over the selected React UI
while increasing the JavaScript process's privileged responsibility and desktop
runtime footprint.
