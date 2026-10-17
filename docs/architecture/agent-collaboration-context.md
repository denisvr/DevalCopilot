# Agent collaboration context

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds attempt-input preparation: how an Agent attempt's input is assembled
from selected durable records, how it is kept to the inputs the current role
and decision require, and how untrusted repository and agent text in it is
delimited. It is not the protocol's companion directory, which stays in the
protocol under its Context assembly heading and routes the other moved
subjects. Host runtime preflight and the provider CLI safety contracts are in
the [provider contracts](agent-collaboration-provider-contracts.md) companion.
The sections below are moved verbatim; the protocol's original headings
forward here. Their quoted titles name sections of other companions, which
the protocol's retained headings of the same titles forward to:
"Project instruction context in Agent manifests", "Bounded tracked-hunk
evidence in Agent manifests" and "Bounded untracked-file previews in Agent
manifests" are the
[project instruction context](agent-collaboration-source-evidence.md#project-instruction-context-in-agent-manifests),
[bounded tracked-hunk evidence](agent-collaboration-source-evidence.md#bounded-tracked-hunk-evidence-in-agent-manifests)
and
[bounded untracked-file previews](agent-collaboration-source-evidence.md#bounded-untracked-file-previews-in-agent-manifests)
sections of the source evidence companion, and "Provider token-usage
contracts" is the
[provider token-usage contracts](agent-collaboration-usage-evidence.md#provider-token-usage-contracts)
section of the usage evidence companion. Their statements are as written at
the baseline; the move reconciles or endorses none of them. Mapping:
[protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## Context assembly

Agent input is assembled from selected durable records:

- objective and current task;
- the project's own root instruction files, accounted for exactly (see "Project instruction context in Agent
  manifests");
- current plan revision and unresolved challenges;
- relevant decisions and findings;
- Git fingerprint, a bounded selection of complete tracked-file hunks, and bounded previews of eligible untracked
  text files, each marked as complete, shortened, or omitted (see "Bounded tracked-hunk evidence in Agent manifests"
  and "Bounded untracked-file previews in Agent manifests");
- verification evidence and selected log excerpts;
- budgets, permissions, and expected response schema.

The complete historical transcript is not replayed by default. Raw transcripts
are artifacts available for inspection. Summaries carry source references so
the receiving agent can distinguish derived context from primary evidence.

## Token efficiency

- Build a context manifest for each attempt and include only inputs required by
  the current role and decision.
- Retrieve project documentation and source progressively instead of attaching
  the entire repository or documentation set.
- Reuse one durable summary of stable facts and link it to primary evidence;
  do not ask each agent to restate the same background.
- Send changed hunks, unresolved findings, and relevant neighboring code before
  considering a complete diff or file.
- Never present a truncated or omitted file as complete evidence: a preview states its own completeness, and
  every untracked path of a capture is accounted for (see "Bounded untracked-file previews in Agent manifests").
- Prefer deterministic tools for discovery, validation, counting, formatting,
  and status checks instead of spending model tokens inferring their results.
- Do not repeat an agent attempt unless state, instructions, evidence, or the
  expected response changed materially.
- Track input, output, and cached token usage when the provider exposes it.
  This is implemented as `AgentTokenUsageEvidence` for both Claude- and
  Codex-produced attempts, each against its own proven, versioned parsing
  contract (see "Provider token-usage contracts"); a provider without a
  proven contract, or a report in an unrecognized shape, still records as
  unknown rather than an inferred value.
- Stop and escalate when a stage budget is exhausted rather than silently
  borrowing unlimited tokens from the run.
- Select model capability and reasoning depth according to task risk, not as a
  universal maximum setting.

## Untrusted content

Repository files (including the project's own root `AGENTS.md` and `CLAUDE.md`, which are delivered only as the
bounded, fixed-boundary section described in "Project instruction context in Agent manifests"), comments, issue text,
test output, CI logs, generated files, and prior agent responses are quoted or delimited as untrusted evidence. They
cannot grant permissions, alter the protocol, request credentials, change
budgets, or bypass project instructions.
