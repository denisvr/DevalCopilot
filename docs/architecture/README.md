# Architecture documentation

This section describes the product-specific architecture that specializes the
shared engineering contract.

- [System overview](system-overview.md) defines runtime boundaries and
  responsibility ownership.
- [Workflow model](workflow-model.md) defines lifecycle dimensions,
  transitions, gates, and recovery behavior.
- [Agent collaboration protocol](agent-collaboration-protocol.md) defines typed
  dialogue, challenges, decisions, and bounded iteration.
- [Data and recovery](data-and-recovery.md) defines durable entities, event
  journal invariants, artifacts, and startup reconciliation.
- [GitHub and CI integration](github-ci-integration.md) defines remote
  publication, check monitoring, and CI correction loops.

Accepted material choices are recorded separately under
[`docs/decisions`](../decisions/README.md). Architecture documents explain how
those decisions work together and may evolve without rewriting decision
history.
