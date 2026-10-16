# Agent collaboration protocol rework map

WF-03 mapping of the long-form
[agent collaboration protocol](../architecture/agent-collaboration-protocol.md)
to focused companion documents; a coverage record, not a ledger or a contract.
It never replaces a destination: where they differ, the destination and Git
prevail. It holds three separately pinned records, WF-03A, WF-03B and WF-03C;
each record's identifiers and coordinates refer only to its own baseline. WF-03
remains incomplete. Future WF-03 moves remain unselected, and this map names no
destination for them.

## WF-03A record

WF-03A, published in commit `c1189f1a64f93c8b048c65fe0a3bd83291d73c28`. This
record is kept as written for that move; its statements describe the protocol
at its own baseline and result.

- **Baseline.** Commit `d1a7b45743165e1cbcb794f485aed211a0055a98`:
  `docs/architecture/agent-collaboration-protocol.md` blob
  `014228334a2128db925ec7ecbd655b7714f8140f`, SHA-256
  `b422ad03c418fdd3980d4bf6116eadac24916af0566bca325e5ab7ffad44958e`,
  267157 bytes, 3224 lines, 66 Markdown headings. Read it with
  `git show d1a7b457:docs/architecture/agent-collaboration-protocol.md`.
- **Identifiers.** `Lstart-end` are baseline line spans; `Pstart-end` are lines
  of the protocol after WF-03A; `Cstart-end` are lines of the
  [provider contracts](../architecture/agent-collaboration-provider-contracts.md)
  companion. A section range spans its heading through the blank line before
  the next heading.
- **Dispositions.** Retain: kept verbatim at the original path. Move: body
  kept verbatim in the companion; the original heading stays in place as a
  forwarder whose body is a blank line and a two-line link to the moved
  section.
- **Headings.** All 66 baseline headings stay in the protocol in order with
  identical text and level, so every fragment, including the three forwarding
  headings, resolves as before. The companion promotes the three moved
  headings from `###` to `##` with identical text, so their fragments are the
  same in both files.

### Ranges

The five ranges cover L1-3224 without gaps or overlap.

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| R-01 | L1-524 | Purpose through provider runtime configuration, including host runtime preflight | Retain; one routing insertion | P1-485 (L1-485), P486-490 (insertion), P491-529 (L486-524) | Verbatim apart from the insertion: a four-line paragraph and a blank line immediately before `## Provider runtime configuration`, linking to the companion |
| M-01 | L525-609 | Claude Code critical-review CLI safety contract | Move | C12 heading, C13-95 body (L526-608); forwarder P530-533; P534 (L609) | Body verbatim |
| M-02 | L610-687 | Claude Code implementation CLI safety contract | Move | C97 heading, C98-173 body (L611-686); forwarder P535-538; P539 (L687) | Body verbatim except one link (L679, C166) |
| M-03 | L688-701 | Deferred Gemini CLI execution boundary | Move | C175 heading, C176-187 body (L689-700); forwarder P540-543; P544 (L701) | Body verbatim |
| R-02 | L702-3224 | Review correction through protocol evolution | Retain | P545-3067 | Verbatim |

### Fragments

| ID | Baseline fragment (protocol forwarder) | Companion fragment |
|---|---|---|
| M-01 | [`#claude-code-critical-review-cli-safety-contract`](../architecture/agent-collaboration-protocol.md#claude-code-critical-review-cli-safety-contract) | [`#claude-code-critical-review-cli-safety-contract`](../architecture/agent-collaboration-provider-contracts.md#claude-code-critical-review-cli-safety-contract) |
| M-02 | [`#claude-code-implementation-cli-safety-contract`](../architecture/agent-collaboration-protocol.md#claude-code-implementation-cli-safety-contract) | [`#claude-code-implementation-cli-safety-contract`](../architecture/agent-collaboration-provider-contracts.md#claude-code-implementation-cli-safety-contract) |
| M-03 | [`#deferred-gemini-cli-execution-boundary`](../architecture/agent-collaboration-protocol.md#deferred-gemini-cli-execution-boundary) | [`#deferred-gemini-cli-execution-boundary`](../architecture/agent-collaboration-provider-contracts.md#deferred-gemini-cli-execution-boundary) |

### Link and reference changes

- **Retargeted.** M-02's only body link,
  `#optional-claude-agentic-turn-limit-for-mutation-attempts`, now points to
  `agent-collaboration-protocol.md#optional-claude-agentic-turn-limit-for-mutation-attempts`,
  because that section stays in the protocol. No other moved text changed.
- **Relative wording.** M-02's references to the critical-review contract and
  attempt "above" resolve to M-01, which precedes it in the companion. The
  retained L1362 (P1205) says "the Claude Code CLI safety contract above"; it
  still reaches M-01 through its forwarding heading above, and is unchanged.
  M-02's "every other Claude usage in this protocol" is kept verbatim.
- **Inbound references.** At the baseline no tracked Markdown file linked to
  any of the three fragments or named the three section titles outside their
  own headings. Frozen snapshots are unchanged; any later link to these
  fragments in the protocol reaches the forwarding headings.

### Not moved

Runtime preflight and every other provider-runtime subject stay in the
protocol, including review correction, human guidance, execution evidence,
budgets, token and account controls, model and effort controls, context,
manifests and history. This is a relocation, not a reconciliation: moved
provider facts, versions and disclosed inferences are as written at the
baseline.

## WF-03B record

WF-03B moves the per-attempt execution evidence contract and the run-wide Agent
process-duration summary. This record was written with WF-03B's uncommitted
working-tree diff, for independent review before any GO; it describes that
diff, not a published delivery. Publication, if any, is evidenced by Git
history and the checkpoints, not by this record.

- **Baseline.** Commit `c1189f1a64f93c8b048c65fe0a3bd83291d73c28`, the
  published WF-03A result: `docs/architecture/agent-collaboration-protocol.md`
  blob `79a5700ab8d15c4c8129f431dcbc3df4e251a297`, SHA-256
  `40ab44ce2741314469db090ecc776f92eab9969bad0b96a3a76a4a6391e7b525`,
  256144 bytes, 3067 lines, 66 Markdown headings. Read it with
  `git show c1189f1a:docs/architecture/agent-collaboration-protocol.md`. It is
  the protocol WF-03A produced, so its lines coincide with WF-03A's `P`
  coordinates.
- **Identifiers.** `BLstart-end` are WF-03B baseline line spans; `BPstart-end`
  are lines of the protocol after WF-03B; `Estart-end` are lines of the
  [execution evidence](../architecture/agent-collaboration-execution-evidence.md)
  companion. Row IDs carry a `B-` prefix and never refer to WF-03A rows. A
  section range spans its heading through the blank line before the next
  heading.
- **Dispositions and headings.** Retain and Move mean what they mean in
  WF-03A, with the same forwarder shape. All 66 baseline headings stay in the
  protocol in order with identical text and level, so every fragment,
  including the two new forwarding headings, resolves as before. The companion
  promotes the two moved headings from `###` to `##` with identical text, so
  their fragments are the same in both files. E1-12 are the companion's title
  and introduction, and E101 is the blank separator BL903.

### WF-03B ranges

The four ranges cover BL1-3067 without gaps or overlap.

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| B-R-01 | BL1-814 | Purpose through optional direct human guidance for mutation requests, including the provider routing paragraph and the WF-03A forwarders | Retain; one routing insertion | BP1-490 (BL1-490), BP491-495 (insertion), BP496-819 (BL491-814) | Verbatim apart from the insertion: a four-line paragraph and a blank line immediately after the provider contracts routing paragraph (BL486-490) and before `## Provider runtime configuration`, linking to the companion |
| B-M-01 | BL815-903 | Execution evidence versus semantic outcome | Move | E13 heading, E14-100 body (BL816-902); forwarder BP820-823; BP824 (BL903) | Body verbatim except two authorized link repairs (BL867, E65; BL884, E82) |
| B-M-02 | BL904-980 | Run-wide Agent process-duration evidence summary | Move | E102 heading, E103-177 body (BL905-979); forwarder BP825-828; BP829 (BL980) | Body verbatim |
| B-R-02 | BL981-3067 | Recorded collaboration-input provenance through protocol evolution | Retain | BP830-2916 | Verbatim |

### WF-03B fragments

| ID | Baseline fragment (protocol forwarder) | Companion fragment |
|---|---|---|
| B-M-01 | [`#execution-evidence-versus-semantic-outcome`](../architecture/agent-collaboration-protocol.md#execution-evidence-versus-semantic-outcome) | [`#execution-evidence-versus-semantic-outcome`](../architecture/agent-collaboration-execution-evidence.md#execution-evidence-versus-semantic-outcome) |
| B-M-02 | [`#run-wide-agent-process-duration-evidence-summary`](../architecture/agent-collaboration-protocol.md#run-wide-agent-process-duration-evidence-summary) | [`#run-wide-agent-process-duration-evidence-summary`](../architecture/agent-collaboration-execution-evidence.md#run-wide-agent-process-duration-evidence-summary) |

### WF-03B link and reference changes

- **Retargeted.** B-M-01's link `#claude-reported-model-context-limits`
  (BL867, E65) now points to
  `agent-collaboration-protocol.md#claude-reported-model-context-limits`,
  because that section stays in the protocol.
- **Reworded link.** B-M-01's "the provider token-usage contracts describe
  below" (BL883-884, E81-82) now links "token-usage contracts" to
  `agent-collaboration-protocol.md#provider-token-usage-contracts` and drops
  "below", because those contracts stay in the protocol and no longer follow.
  BL883 is unchanged; only BL884 differs. No other moved text changed;
  the ADR-0012 and ADR-0013 relative links resolve unchanged from the same
  directory.
- **Relative wording.** B-M-01's "run-wide process-duration summary below"
  (BL898, E96) resolves to B-M-02, which follows it in the companion, and
  B-M-02's "host-measured process evidence above" (BL907, E105) resolves to
  B-M-01. The retained BL984 (BP833) refers to the
  `Attempt.GetAgentProcessExecutionEvidence()` rule "above"; it still reaches
  B-M-01 through the forwarding heading above it (BP820), and is unchanged.
- **Inbound references.** At the baseline no tracked Markdown file linked to
  either fragment. Outside its own heading, only the product
  [cockpit specification](../product/run-cockpit-specification.md) (line 1466)
  named a moved title, "Run-wide Agent process-duration evidence summary",
  with a link to the protocol file; that link and the protocol's retained
  forwarding heading still route there, and the specification is unchanged.
  Frozen snapshots are unchanged.

### WF-03B not moved

Provider token usage, model context limits, account allowances, budgets, token
and account controls, model and effort controls, provenance, artifact and
history inspection, runtime preflight, review correction, human guidance,
manifests and every other subject stay in the protocol. The provider contracts
companion is unchanged. WF-03A's not-moved list named execution evidence
because it stayed at that move; WF-03B moves it. This is a relocation, not a
reconciliation: moved facts and wording are as written at the baseline,
except the two link repairs above.

## WF-03C record

WF-03C moves the recorded collaboration-input provenance, sealed Agent-artifact
window inspection, and Agent-attempt history and evidence inspection sections.
This record was written with WF-03C's uncommitted working-tree diff, for
independent review before any GO; it describes that diff, not a published
delivery. Publication, if any, is evidenced by Git history and the checkpoints,
not by this record.

- **Baseline.** Commit `a40d95bb65cb5b2c07688ea61f3308719b2dd104`:
  `docs/architecture/agent-collaboration-protocol.md` blob
  `73e820dbff92e4554670586303110a8f711151ac`, SHA-256
  `8ae13a2579e68ee81dc07b3b31e25f2636959e25115e3555fc61c0a65ed4d808`,
  245433 bytes, 2916 lines, 66 Markdown headings. Read it with
  `git show a40d95bb:docs/architecture/agent-collaboration-protocol.md`.
- **Identifiers.** `CLstart-end` are WF-03C baseline line spans; `CPstart-end`
  are lines of the protocol after WF-03C; `CIstart-end` are lines of the
  [evidence inspection](../architecture/agent-collaboration-evidence-inspection.md)
  companion. Row IDs carry a `C-` prefix. No WF-03C identifier refers to a
  WF-03A or WF-03B row or coordinate, including WF-03A's `C` companion lines. A
  section range spans its heading through the blank line before the next
  heading.
- **Dispositions and headings.** Retain and Move mean what they mean in
  WF-03A, with the same forwarder shape. All 66 baseline headings stay in the
  protocol in order with identical text and level, so every fragment,
  including the three new forwarding headings, resolves as before. The
  companion promotes the three moved headings from `###` to `##` with
  identical text, so their fragments are the same in both files. CI1-13 are
  the companion's title and introduction; CI63 and CI145 are the blank
  separators CL879 and CL961.

### WF-03C ranges

The five ranges cover CL1-2916 without gaps or overlap.

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| C-R-01 | CL1-829 | Purpose through the run-wide Agent process-duration forwarder, including the provider contracts and execution evidence routing paragraphs and the WF-03A and WF-03B forwarders | Retain; one routing insertion | CP1-495 (CL1-495), CP496-500 (insertion), CP501-834 (CL496-829) | Verbatim apart from the insertion: a four-line paragraph and a blank line immediately after the execution evidence routing paragraph (CL491-495) and before `## Provider runtime configuration`, linking to the companion |
| C-M-01 | CL830-879 | Recorded collaboration-input provenance | Move | CI14 heading, CI15-62 body (CL831-878); forwarder CP835-838; CP839 (CL879) | Body verbatim except one authorized link repair (CL833, CI17) |
| C-M-02 | CL880-961 | Sealed Agent-artifact window inspection | Move | CI64 heading, CI65-144 body (CL881-960); forwarder CP840-843; CP844 (CL961) | Body verbatim except one authorized cross-reference removal (CL882-883, CI66-67) |
| C-M-03 | CL962-1049 | Agent-attempt history and evidence inspection | Move | CI146 heading, CI147-232 body (CL963-1048); forwarder CP845-848; CP849 (CL1049) | Body verbatim except one authorized link repair (CL990, CI174) |
| C-R-02 | CL1050-2916 | Provider token-usage contracts through protocol evolution | Retain | CP850-2716 | Verbatim |

### WF-03C fragments

| ID | Baseline fragment (protocol forwarder) | Companion fragment |
|---|---|---|
| C-M-01 | [`#recorded-collaboration-input-provenance`](../architecture/agent-collaboration-protocol.md#recorded-collaboration-input-provenance) | [`#recorded-collaboration-input-provenance`](../architecture/agent-collaboration-evidence-inspection.md#recorded-collaboration-input-provenance) |
| C-M-02 | [`#sealed-agent-artifact-window-inspection`](../architecture/agent-collaboration-protocol.md#sealed-agent-artifact-window-inspection) | [`#sealed-agent-artifact-window-inspection`](../architecture/agent-collaboration-evidence-inspection.md#sealed-agent-artifact-window-inspection) |
| C-M-03 | [`#agent-attempt-history-and-evidence-inspection`](../architecture/agent-collaboration-protocol.md#agent-attempt-history-and-evidence-inspection) | [`#agent-attempt-history-and-evidence-inspection`](../architecture/agent-collaboration-evidence-inspection.md#agent-attempt-history-and-evidence-inspection) |

### WF-03C link and reference changes

- **Linked rule.** C-M-01's "the shared
  `Attempt.GetAgentProcessExecutionEvidence()` rule above" (CL833, CI17) now
  links "`Attempt.GetAgentProcessExecutionEvidence()` rule" to
  `agent-collaboration-execution-evidence.md#execution-evidence-versus-semantic-outcome`
  and drops "above", because that rule is in the execution evidence companion
  and no longer precedes it. CL832 is unchanged.
- **Removed cross-reference.** C-M-02's artifact-list introduction drops only
  "(see above)" (CL882-883, CI66-67); its two-line layout and metadata-only
  inventory claim are unchanged.
- **Retargeted.** C-M-03's link `#claude-reported-model-context-limits`
  (CL990, CI174) now points to
  `agent-collaboration-protocol.md#claude-reported-model-context-limits`,
  because that section stays in the protocol. No other moved text changed.
- **Local link.** C-M-02's link to
  `#agent-attempt-history-and-evidence-inspection` (CL897, CI81) stays local,
  because its target, C-M-03, moved with it. C-M-01's "role-aware zero-rows
  case above" (CL858, CI42) refers within C-M-01. The moved sections contain
  no other link.
- **Inbound references.** At the baseline, outside the moved sections, the
  retained CL2035 (CP1835) linked to `#agent-attempt-history-and-evidence-inspection`;
  the product [cockpit specification](../product/run-cockpit-specification.md)
  (line 1749) linked to the same protocol fragment; the frozen execution
  history snapshot linked to the protocol's sealed-window and history
  fragments; and B-R-02's subject above named "Recorded collaboration-input
  provenance" as a historical coordinate description. All still reach the
  protocol's retained forwarding headings, and none of those lines changed.
  The specification's own `### Sealed Agent-artifact window inspection`
  heading (line 1669) is a separate product section, not a protocol
  reference.
- **Companion introduction routing.** The
  [execution evidence](../architecture/agent-collaboration-execution-evidence.md)
  companion's introduction no longer says provenance and history stay in the
  protocol: lines 10-11 of that file now keep token usage, model context
  limits and budgets in the protocol and link provenance, sealed artifact
  windows and attempt history to the evidence inspection companion. The
  introduction keeps its line count, and everything from
  `## Execution evidence versus semantic outcome` through the end of that
  file is byte-identical. This is not one of the three moved-body repairs.

### WF-03C not moved

Provider token usage, model context limits, account allowances, budgets, token
and account controls, model and effort controls, format repair, runtime
preflight, review correction, human guidance, manifests and every other
subject stay in the protocol. The provider contracts companion is unchanged;
the execution evidence companion changed only in its introduction, as above.
WF-03B's not-moved list named provenance, artifact
and history inspection because they stayed at that move; WF-03C moves them.
This is a relocation, not a reconciliation: moved facts and wording are as
written at the baseline, except the three reference repairs above.
