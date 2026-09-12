# DevalCopilot.Frontend

React and TypeScript cockpit frontend for DevalCopilot, built with Vite and
hosted in a Tauri shell in later increments. It renders durable run state
retrieved through the generated MVC API client and never executes Git, agent,
shell, or GitHub commands directly.

## Current scaffold status

This project currently contains the Vite/React/TypeScript scaffold only. The
run cockpit, the generated API client, and the authenticated session boundary
are added incrementally against the approved
[run cockpit specification](../../../docs/product/run-cockpit-specification.md).

## Commands

```bash
npm ci
npm run dev
npm run lint
npm run typecheck
npm run build
```
