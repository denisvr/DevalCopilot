# Agent collaboration protocol acceptance

Candidate acceptance evidence for the WF-03 rework of the
[agent collaboration protocol](../architecture/agent-collaboration-protocol.md),
prepared with WF-03N for independent planner review. It is not an acceptance
decision: it does not mark WF-03 or the documentation rework complete and
grants no authority. The companions, the
[rework map](agent-collaboration-protocol-rework-map.md), the checkpoints and
Git prevail over this summary. Every check named here is lexical (text, links,
fragments, hashes); none proves semantic equivalence.

## Criteria and results

| Criterion | Result | Evidence |
|---|---|---|
| All 66 headings of the WF-03A baseline (`d1a7b457`) stay in the core in order, with identical text, level and fragment | Met: 66 of 66 | Fresh |
| Each heading is core-owned or forwards directly to existing, unique destination headings | Met: 3 core-owned (title, Purpose, Protocol evolution), 63 forwarded to 15 companions; one split, Review correction | Fresh |
| No forwarder target changed after the slice that first forwarded it | Met at every A-M commit | Fresh |
| The core routes all 15 companions from one directory, Purpose points to it, and the startup table links all 15 with the replaced row's ADR coverage | Met in the WF-03N checks | Fresh |
| WF-03N changes only the two routing regions: the pre-slice core reconstructs exactly, and all 15 companions equal `b409983e` | Met: exact 17983-byte reconstruction | Fresh |
| Each A-M publication: sole-parent chain, manifest equals the commit's changed paths and committed mode, blob, bytes and SHA-256, and the map record's stated baseline matches Git | Met for all 13 | Fresh |
| Each A-M move conserved its moved and retained text | Recorded as passing by each slice's retained final; not rerun | Retained |

The WF-03N ledger is `logs/executor/wf-03n/runs.log`: history-02 produced the
inventory and history ledger, and the final matrix re-checks this report's
inputs on stable bytes. These are ignored local files, not tracked records.

## Subject ownership

| Owner | Original headings | First forwarded |
|---|---|---|
| Core (protocol) | Title, Purpose, Protocol evolution; it also holds the companion directory under Context assembly | Not moved |
| [Dialogue](../architecture/agent-collaboration-dialogue.md) | 21: envelope, role-first rule, message types, reply semantics, default interaction, authority matrix, structured output failure | WF-03M |
| [Ledger](../architecture/agent-collaboration-ledger.md) | 3: durable ledger, execution mode and isolation, explicit abandonment | WF-03M |
| [Context](../architecture/agent-collaboration-context.md) | 3: context assembly, token efficiency, untrusted content | WF-03L |
| [Provider contracts](../architecture/agent-collaboration-provider-contracts.md) | 4: provider runtime configuration and three CLI contracts | WF-03A; runtime configuration WF-03L |
| [Execution evidence](../architecture/agent-collaboration-execution-evidence.md) | 2 | WF-03B |
| [Evidence inspection](../architecture/agent-collaboration-evidence-inspection.md) | 3 | WF-03C |
| [Format repair](../architecture/agent-collaboration-format-repair.md) | 2 | WF-03D |
| [Usage evidence](../architecture/agent-collaboration-usage-evidence.md) | 2 | WF-03E |
| [Token-activity controls](../architecture/agent-collaboration-token-activity-controls.md) | 2 | WF-03F |
| [Account usage](../architecture/agent-collaboration-account-usage.md) | 3 | WF-03G |
| [Invocation configuration](../architecture/agent-collaboration-invocation-configuration.md) | 5, and Review correction's second destination, provider configuration observations | WF-03H; observations WF-03J |
| [Guidance and escalation](../architecture/agent-collaboration-guidance-and-escalation.md) | 5 | WF-03I |
| [Correction and budgets](../architecture/agent-collaboration-correction-and-budgets.md) | 1: Review correction (first destination) | WF-03J |
| [Source evidence](../architecture/agent-collaboration-source-evidence.md) | 5 | WF-03K |
| [Local delivery](../architecture/agent-collaboration-local-delivery.md) | 2 | WF-03K |

The per-heading inventory, with original and current lines, fragments,
owners, destinations, the split and the first forwarding slice, is
`logs/executor/wf-03n/history-02/inventory.json`; the per-slice ledger is
`history-ledger.json` beside it.

## Fresh, retained and not-run evidence

- **Fresh (WF-03N).** The inventory and the history ledger above, read from
  Git history and retained files only; the WF-03N boundary, routing, link,
  protected-content, hygiene and sensitive checks; and their negative controls
  on in-memory copies.
- **Retained, inspected and not rerun.** Each A-M slice's final checker
  results, negative controls, publication verification and review decision.
  WF-03N reads their recorded pass values and the input hashes they record.
  For every slice, the last retained final records the SHA-256 of every
  published executor document. These records keep their own claims and
  limits; reading them does not authenticate them historically.
- **Not run.** No old checker program, no new whole-original byte
  reconstruction, no recreation of missing output, and no product, browser,
  provider, build, test or qualification suite.

## Residual issues and limits

- WF-03A's retained final records a composition check of the core and
  per-section verbatim moved bodies, not a named inverse reconstruction.
  WF-03B to WF-03M record named reconstructions, WF-03E's in its
  pre-correction final, which its introduction-only correction carried with
  a contract-suffix check. B, C, E and J each have a correction final after a
  first final that recorded only part of the published hashes.
- Earlier evidence limits stand as recorded in
  [current work](current-work.md#prior-published-delivery) and the
  [planner handoff](planner-handoff.md#retained-review-and-evidence-limits):
  for example WF-03C's failed negative-01 baseline, overwritten or missing
  generator outputs, derivative transcriptions and chat-only console output.
- Moved wording keeps its baseline statements, unreconciled and unendorsed:
  stage ordinals, counts, "real today" and companion bodies that still say a
  subject stays in the protocol (EX-L01). Some routes still reach subjects
  through the core's retained headings rather than the owner (EX-K01, EX-L02,
  EX-M02); they resolve.
- The [documentation rework map](documentation-rework-map.md) still links a
  missing `planner-handoff.md#verified-transition-baseline` fragment. It is
  pre-existing and out of scope, so a repository-wide link scan is not clean.
- Git, disk and live checks have no negative controls; ownership is derived
  from forwarder links, not from reading the contracts.

## Records

The [rework map](agent-collaboration-protocol-rework-map.md) holds the A-N
records; the [development workflow](development-workflow.md),
[current work](current-work.md) and [planner handoff](planner-handoff.md) hold
the procedure, delivery facts and review decisions. Retained per-slice
evidence is under the ignored `logs/executor/wf-03<slice>/` and
`logs/planner-review/wf-03<slice>/` directories.
