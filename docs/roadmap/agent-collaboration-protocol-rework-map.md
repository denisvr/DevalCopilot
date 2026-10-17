# Agent collaboration protocol rework map

WF-03 mapping of the long-form
[agent collaboration protocol](../architecture/agent-collaboration-protocol.md)
to focused companion documents; a coverage record, not a ledger or a contract.
It never replaces a destination: where they differ, the destination and Git
prevail. It holds twelve separately pinned records, WF-03A, WF-03B, WF-03C,
WF-03D, WF-03E, WF-03F, WF-03G, WF-03H, WF-03I, WF-03J, WF-03K and WF-03L;
each record's identifiers and coordinates refer only to its own baseline, and
each earlier record's statements stay as written, as history. WF-03 remains
incomplete. Future WF-03 moves remain unselected and have no destination here.

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

## WF-03D record

WF-03D moves the two manual read-only format repair sections. This record was
written with WF-03D's uncommitted working-tree diff, for independent review
before any GO; it describes that diff, not a published delivery. Publication,
if any, is evidenced by Git history and the checkpoints, not by this record.

- **Baseline.** Commit `fc14b5fd1caf3a910fc69921acf08306f75a7d71`, the
  published WF-03C result: `docs/architecture/agent-collaboration-protocol.md`
  blob `4fcf2df82f91f0bf2b1166d2622818407b4578d1`, SHA-256
  `3dee277cfa478ad3e84c01aa9a5859f0556f6a90e9b0ac0a2b35b57293d405e4`,
  230999 bytes, 2716 lines, 66 Markdown headings. Read it with
  `git show fc14b5fd:docs/architecture/agent-collaboration-protocol.md`.
- **Identifiers.** `DLstart-end` are WF-03D baseline line spans; `DPstart-end`
  are lines of the protocol after WF-03D; `DFstart-end` are lines of the
  [format repair](../architecture/agent-collaboration-format-repair.md)
  companion. Row IDs carry a `D-` prefix. No WF-03D identifier refers to a
  WF-03A, WF-03B or WF-03C row or coordinate. A section range spans its
  heading through the blank line before the next heading.
- **Dispositions and headings.** Retain and Move mean what they mean in
  WF-03A, with the same forwarder shape. All 66 baseline headings stay in the
  protocol in order with identical text and level, so every fragment,
  including the two new forwarding headings, resolves as before. The companion
  promotes the two moved headings from `###` to `##` with identical text, so
  their fragments are the same in both files. DF1-13 are the companion's title
  and introduction, and DF83 is the blank separator DL1899.

### WF-03D ranges

The four ranges cover DL1-2716 without gaps or overlap.

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| D-R-01 | DL1-1829 | Purpose through the run-scoped advisory Codex account-usage warning, including Structured output failure, the three companion routing paragraphs and the WF-03A, WF-03B and WF-03C forwarders | Retain; one routing insertion | DP1-500 (DL1-500), DP501-504 (insertion), DP505-1833 (DL501-1829) | Verbatim apart from the insertion: a three-line paragraph and a blank line immediately after the evidence inspection routing paragraph (DL496-500) and before `## Provider runtime configuration`, linking to the companion |
| D-M-01 | DL1830-1899 | One manual Codex Planner format repair | Move | DF14 heading, DF15-82 body (DL1831-1898); forwarder DP1834-1837; DP1838 (DL1899) | Body verbatim except one authorized link repair (DL1835, DF19) |
| D-M-02 | DL1900-2022 | One manual format repair of the remaining read-only stages | Move | DF84 heading, DF85-205 body (DL1901-2021); forwarder DP1839-1842; DP1843 (DL2022) | Body verbatim |
| D-R-02 | DL2023-2716 | Optional second challenge round and escalation through protocol evolution | Retain | DP1844-2537 | Verbatim |

### WF-03D fragments

| ID | Baseline fragment (protocol forwarder) | Companion fragment |
|---|---|---|
| D-M-01 | [`#one-manual-codex-planner-format-repair`](../architecture/agent-collaboration-protocol.md#one-manual-codex-planner-format-repair) | [`#one-manual-codex-planner-format-repair`](../architecture/agent-collaboration-format-repair.md#one-manual-codex-planner-format-repair) |
| D-M-02 | [`#one-manual-format-repair-of-the-remaining-read-only-stages`](../architecture/agent-collaboration-protocol.md#one-manual-format-repair-of-the-remaining-read-only-stages) | [`#one-manual-format-repair-of-the-remaining-read-only-stages`](../architecture/agent-collaboration-format-repair.md#one-manual-format-repair-of-the-remaining-read-only-stages) |

### WF-03D link and reference changes

- **Retargeted.** D-M-01's only link, `#agent-attempt-history-and-evidence-inspection`
  (DL1835, DF19), now points to
  `agent-collaboration-protocol.md#agent-attempt-history-and-evidence-inspection`,
  the protocol's retained forwarding heading, which forwards to the evidence
  inspection companion. The line is not rewrapped. No other moved text
  changed. This is WF-03C's retained CL2035 (CP1835) inbound reference.
- **Relative wording.** D-M-02's "Like the Planner's" and "The Planner's repair
  is unchanged" refer to D-M-01, which precedes it in the companion. D-M-02
  names the provider token stop, the two-round planning limit and the
  depth-two escalation without a link or "above"; those stay in the protocol.
  The moved sections contain no other link.
- **Inbound references.** At the baseline, outside the moved sections, the
  retained Structured output failure (DL462 and DL464, unchanged as DP462 and
  DP464) linked to both fragments; the product
  [cockpit specification](../product/run-cockpit-specification.md) (lines 970
  and 997) linked to both protocol fragments; and the frozen execution history
  snapshot (lines 2058 and 2970) linked to them. All still reach the
  protocol's retained forwarding headings, and none of those lines changed.
  The specification's own `### One manual Codex plan format repair` and
  `### One manual format repair of a critical review, challenge resolution, or code review`
  headings (lines 953 and 973) are separate product sections. The
  [engineering context](../engineering-context.md) routing row that names
  format repair under the protocol is unchanged; the protocol routes to the
  companion.
- **Provider contracts introduction.** The
  [provider contracts](../architecture/agent-collaboration-provider-contracts.md)
  companion's introduction no longer says provider runtime configuration keeps
  every other provider-runtime subject, which earlier extractions had made
  inaccurate: its line 6 became lines 6-7, saying it retains host runtime
  preflight and forwards extracted subjects through their original headings.
  The introduction grows by one line, so `## Claude Code critical-review CLI safety contract`
  moves from line 12 to 13, and everything from that heading through the end
  of that file is byte-identical. This is a separate introduction repair, not
  the moved-body link repair.

### WF-03D not moved

Structured output failure, the optional second challenge round and escalation,
escalated-plan authorization, verification failure diagnosis, review
correction, human guidance, provider token usage, model context limits,
account allowances, budgets, token and account controls, model and effort
controls, runtime preflight, manifests and every other subject not moved by
WF-03A to WF-03D stay in the protocol. The execution evidence and evidence
inspection companions are unchanged; the provider contracts companion changed
only in its introduction, as above. WF-03C's not-moved list named format
repair because it stayed at that move; WF-03D moves it. This is a relocation,
not a reconciliation: moved facts and wording are as written at the baseline,
except the one link repair above.

## WF-03E record

WF-03E moves the provider token-usage contracts and the Claude-reported model
context limits, and repairs two companion introductions that placed them in
the protocol. This record was written with WF-03E's uncommitted working-tree
diff, for independent review before any GO; it describes that diff, not a
published delivery. Publication, if any, is evidenced by Git history and the
checkpoints, not by this record.

- **Baseline.** Commit `022979790dca6327ce9844d1bbdb020236f594ed`, the
  published WF-03D result: `docs/architecture/agent-collaboration-protocol.md`
  blob `ba650cecb4da331665a482441ed936cd96deef52`, SHA-256
  `89970a47d952916aac3b8efbab005770915948080dd48db6320d909282748963`,
  212418 bytes, 2537 lines, 66 Markdown headings. Read it with
  `git show 02297979:docs/architecture/agent-collaboration-protocol.md`.
- **Identifiers.** `ELstart-end` are WF-03E baseline line spans; `EPstart-end`
  are lines of the protocol after WF-03E; `EUstart-end` are lines of the
  [usage evidence](../architecture/agent-collaboration-usage-evidence.md)
  companion. Row IDs carry an `E-` prefix. No WF-03E identifier refers to a
  WF-03A, WF-03B, WF-03C or WF-03D row or coordinate. A section range spans
  its heading through the blank line before the next heading.
- **Dispositions and headings.** Retain and Move mean what they mean in
  WF-03A, with the same forwarder shape. All 66 baseline headings stay in the
  protocol in order with identical text and level, so every fragment,
  including the two new forwarding headings, resolves as before. The companion
  promotes the two moved headings from `###` to `##` with identical text, so
  their fragments are the same in both files. EU1-15 are the companion's title
  and introduction, and EU162 is the blank separator EL1000.

### WF-03E ranges

The four ranges cover EL1-2537 without gaps or overlap.

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| E-R-01 | EL1-853 | Purpose through Agent-attempt history and evidence inspection, including Structured output failure, the four companion routing paragraphs and the WF-03A, WF-03B and WF-03C forwarders | Retain; one routing insertion | EP1-504 (EL1-504), EP505-509 (insertion), EP510-858 (EL505-853) | Verbatim apart from the insertion: a four-line paragraph and a blank line immediately after the format repair routing paragraph (EL501-504) and before `## Provider runtime configuration`, linking to the companion |
| E-M-01 | EL854-1000 | Provider token-usage contracts | Move | EU16 heading, EU17-161 body (EL855-999); forwarder EP859-862; EP863 (EL1000) | Body verbatim except one authorized reference repair (EL858, EU20); the trailing duplicate `budget for either provider.` line (EL999, EU161) is kept |
| E-M-02 | EL1001-1043 | Claude-reported model context limits | Move | EU163 heading, EU164-204 body (EL1002-1042); forwarder EP864-867; EP868 (EL1043) | Body verbatim |
| E-R-02 | EL1044-2537 | Provider account-allowance contracts through protocol evolution, including the WF-03D forwarders | Retain | EP869-2362 | Verbatim |

### WF-03E fragments

| ID | Baseline fragment (protocol forwarder) | Companion fragment |
|---|---|---|
| E-M-01 | [`#provider-token-usage-contracts`](../architecture/agent-collaboration-protocol.md#provider-token-usage-contracts) | [`#provider-token-usage-contracts`](../architecture/agent-collaboration-usage-evidence.md#provider-token-usage-contracts) |
| E-M-02 | [`#claude-reported-model-context-limits`](../architecture/agent-collaboration-protocol.md#claude-reported-model-context-limits) | [`#claude-reported-model-context-limits`](../architecture/agent-collaboration-usage-evidence.md#claude-reported-model-context-limits) |

### WF-03E link and reference changes

- **Repaired reference.** E-M-01's "the Claude Code CLI safety contract above"
  (EL858, EU20) is now the link
  `[the Claude Code CLI safety contract](agent-collaboration-provider-contracts.md#claude-code-critical-review-cli-safety-contract)`,
  the provider contracts companion's critical-review contract, which records
  the installed `claude.exe` embedded-string evidence method. At the baseline
  the phrase reached the protocol's retained WF-03A forwarding headings
  (EL544-553). The substitution drops "above"; the line is not rewrapped. No
  other moved text changed.
- **Relative wording.** E-M-01's `see "Claude-reported model context limits" below`
  (EL883, EU45) means E-M-02, and its "the run-wide summary below" (EL935,
  EU97) means its own later paragraph (EL947, EU109). E-M-02's "like the usage
  contract above" (EL1003, EU165) means E-M-01. All resolve in the companion.
  E-M-02's other links are two external documentation pages (EL1004-1005,
  EU166-167) and [ADR-0023](../decisions/0023-record-claude-reported-model-context-limits-in-historical-attempt-evidence.md)
  (EL1007, EU169), whose relative path resolves unchanged from the same
  directory. The moved sections contain no other link.
- **Retained references.** The protocol's account-allowance contracts' "the
  Codex per-attempt token-usage contract above" (EL1046-1047, EP871-872) and the
  Codex model catalog's "both contracts above" (EL1097-1098, EP922-923) still
  reach the retained usage forwarding heading (EP859) above them, beside the
  retained account-allowance contract; Token efficiency's `see "Provider token-usage contracts"`
  (EL2517, EP2342) names that heading. None of those lines changed.
- **Inbound references.** Links to the two protocol fragments from the
  [execution evidence](../architecture/agent-collaboration-execution-evidence.md)
  companion (lines 66 and 83, baseline 65 and 82), the
  [evidence inspection](../architecture/agent-collaboration-evidence-inspection.md)
  companion (line 175, baseline 174), the product
  [cockpit specification](../product/run-cockpit-specification.md) (line 1726)
  and the [documentation rework map](documentation-rework-map.md) (lines 165
  and 168) still reach the retained forwarding headings, as does the
  specification's unlinked "Provider token-usage contracts" reference (lines
  559-560). None of those lines changed; the two companion lines moved down
  one with the introduction repairs below. The WF-03B and WF-03C records above
  name the fragments as history. The frozen history snapshots link to neither.
- **Companion introductions.** At the baseline the execution evidence (line 10)
  and evidence inspection (line 11) introductions said provider token usage
  and model context limits stay in the protocol, which this move makes
  inaccurate. Each now routes them to the
  [usage evidence](../architecture/agent-collaboration-usage-evidence.md)
  companion; the execution evidence introduction keeps budgets in the
  protocol. Execution evidence line 10 became lines 10-11 and evidence
  inspection line 11 became lines 11-12, so each introduction grows by one
  line, and everything from each file's first `##` heading (baseline lines 13
  and 14, now 14 and 15) through its end is byte-identical. These are
  introduction repairs, not moved-body repairs; no body or existing contract
  link changed.

### WF-03E not moved

Account allowances, the Codex model and effort catalog, model, effort and
agentic-turn requests, token-activity warnings and stops, account-usage stops
and warnings, budgets, runtime preflight, manifests, structured output
failure, the challenge rounds and escalation, review correction and every other
subject not moved by WF-03A to WF-03E stay in the protocol. The provider
contracts and format repair companions are unchanged; the execution evidence
and evidence inspection companions changed only in their introductions, as
above. WF-03D's not-moved list named provider token usage
and model context limits because they stayed at that move; WF-03E moves them.
This is a relocation, not a reconciliation: moved facts and wording are as
written at the baseline, including the duplicate line, except the one reference
repair above.

## WF-03F record

WF-03F moves the per-provider run token-activity warnings and the per-provider
run token-activity stop at Agent claim, and repairs the usage evidence
companion introduction that placed them in the protocol. This record was
written with WF-03F's uncommitted working-tree diff, for independent review
before any GO; it describes that diff, not a published delivery. Publication,
if any, is evidenced by Git history and the checkpoints, not by this record.

- **Baseline.** Commit `5dd2e3a940a18e0ce44e2e60f89fbb66c1cded7e`, the
  published WF-03E result: `docs/architecture/agent-collaboration-protocol.md`
  blob `63f0315828c276e40cc5a29d5506cb94eb60eb60`, SHA-256
  `577bff8305f38c29e18661ac9531b055ad6369cc4a7d89b7bf2eef844c88136c`,
  198697 bytes, 2362 lines, 66 Markdown headings. Read it with
  `git show 5dd2e3a9:docs/architecture/agent-collaboration-protocol.md`.
- **Identifiers.** `FLstart-end` are WF-03F baseline line spans; `FPstart-end`
  are lines of the protocol after WF-03F; `FTstart-end` are lines of the
  [token-activity controls](../architecture/agent-collaboration-token-activity-controls.md)
  companion. Row IDs carry an `F-` prefix. No WF-03F identifier refers to a
  WF-03A, WF-03B, WF-03C, WF-03D or WF-03E row or coordinate. A section range
  spans its heading through the blank line before the next heading.
- **Dispositions and headings.** Retain and Move mean what they mean in
  WF-03A, with the same forwarder shape. All 66 baseline headings stay in the
  protocol in order with identical text and level, so every fragment,
  including the two new forwarding headings, resolves as before. The companion
  promotes the two moved headings from `###` to `##` with identical text, so
  their fragments are the same in both files. FT1-18 are the companion's title
  and introduction, and FT77 is the blank separator FL1473. Companion lines
  FT19-183 correspond one to one to FL1415-1579: the two headings are
  promoted and every other line is identical.

### WF-03F ranges

The four ranges cover FL1-2362 without gaps or overlap.

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| F-R-01 | FL1-1414 | Purpose through the optional Claude agentic-turn limit for mutation attempts, including the five companion routing paragraphs and the WF-03A, WF-03B, WF-03C and WF-03E forwarders | Retain; one routing insertion | FP1-509 (FL1-509), FP510-514 (insertion), FP515-1419 (FL510-1414) | Verbatim apart from the insertion: a four-line paragraph and a blank line immediately after the usage evidence routing paragraph (FL505-509) and before `## Provider runtime configuration`, linking to the companion |
| F-M-01 | FL1415-1473 | Per-provider run token-activity warnings | Move | FT19 heading, FT20-76 body (FL1416-1472); forwarder FP1420-1423; FP1424 (FL1473) | Body verbatim |
| F-M-02 | FL1474-1580 | Per-provider run token-activity stop at Agent claim | Move | FT78 heading, FT79-183 body (FL1475-1579); forwarder FP1425-1428; FP1429 (FL1580) | Body verbatim |
| F-R-02 | FL1581-2362 | Run-scoped Codex account-usage stop at claim and dispatch through protocol evolution, including the WF-03D forwarders | Retain | FP1430-2211 | Verbatim |

### WF-03F fragments

| ID | Baseline fragment (protocol forwarder) | Companion fragment |
|---|---|---|
| F-M-01 | [`#per-provider-run-token-activity-warnings`](../architecture/agent-collaboration-protocol.md#per-provider-run-token-activity-warnings) | [`#per-provider-run-token-activity-warnings`](../architecture/agent-collaboration-token-activity-controls.md#per-provider-run-token-activity-warnings) |
| F-M-02 | [`#per-provider-run-token-activity-stop-at-agent-claim`](../architecture/agent-collaboration-protocol.md#per-provider-run-token-activity-stop-at-agent-claim) | [`#per-provider-run-token-activity-stop-at-agent-claim`](../architecture/agent-collaboration-token-activity-controls.md#per-provider-run-token-activity-stop-at-agent-claim) |

### WF-03F link and reference changes

- **Moved text.** The moved sections contain no Markdown link or URL. No moved
  body line changed and no reference was repaired.
- **Relative wording.** F-M-02's "Unlike the advisory warning above" (FL1479,
  FT83) means F-M-01, which precedes it in the companion as at the baseline.
  Its other references to the warning, "the warning's existing" range
  (FL1489-1490, FT93-94), "the advisory warning columns deliberately are not"
  (FL1496, FT100) and "exactly as it does for the warning" (FL1569, FT173),
  carry no direction. Unlinked names of subjects that stay in the protocol,
  such as "the six Agent claim paths" (FL1422, FT26), "the 16-claim budget"
  (FL1537, FT141) and the "existing run-wide count and reserved-time budgets"
  (FL1543, FT147), carry no direction either and are kept as written.
- **Retained references.** The protocol's unlinked "separately from token
  activity" in the Codex account-usage stop (FL1621, FP1470) and "Codex token
  stop" and "Claude token stop" in explicit verification failure diagnosis
  (FL1925 and FL1929, FP1774 and FP1778) carry no direction; the retained
  forwarding headings (FP1420 and FP1425) keep both names in the protocol.
  None of those lines changed. The format repair companion's "token stop"
  (line 144) is unchanged.
- **Inbound references.** Links to the two protocol fragments from the product
  [cockpit specification](../product/run-cockpit-specification.md) (lines 827
  and 860), [current work](current-work.md) (Open risks, line 75, baseline
  73), the [documentation rework map](documentation-rework-map.md) (line 165)
  and the [delivery plan rework map](mvp-delivery-plan-rework-map.md) (line 70)
  still reach the retained forwarding headings; that row's protocol line
  numbers refer to its own earlier baseline. The frozen
  [execution history](current-work-history-ba1d4c2.md) links them at lines
  2777 and 3140. ADR-0025 (lines 8 and 75) and the
  [MVP delivery plan](mvp-delivery-plan.md) (line 194) name the stop without a
  link. None of those lines changed; current-work's link moved down two lines
  with the replaced delivery state above it. No earlier record of this map
  links either fragment; the WF-03E not-moved list names both subjects as
  history.
- **Usage evidence introduction.** At the baseline the
  [usage evidence](../architecture/agent-collaboration-usage-evidence.md)
  introduction (lines 12-13) said warnings and stops stay in the protocol,
  which this move makes inaccurate. Lines 12-13 became lines 12-16: they route
  token-activity warnings and stops to the companion and keep account
  allowances, account-usage stops and warnings, model and effort controls,
  budgets and every other unmoved subject in the protocol. Everything from its
  first `##` heading (baseline line 16, now 19) through its end is
  byte-identical. This is an introduction repair, not a moved-body repair.

### WF-03F not moved

The run-scoped Codex account-usage stop and advisory warning, account
allowances, the Codex model and effort catalog, model, effort and agentic-turn
requests, the run-wide claim count and reserved-time budgets, runtime
preflight, manifests, structured output failure, the challenge rounds and
escalation, review correction and every other subject not moved by WF-03A to
WF-03F stay in the protocol. The provider contracts, execution evidence,
evidence inspection and format repair companions are unchanged; the usage
evidence companion changed only in its introduction, as above. WF-03E's
not-moved list named token-activity warnings and stops because they stayed at
that move; WF-03F moves them. This is a relocation, not a reconciliation:
moved facts and wording are as written at the baseline.

## WF-03G record

WF-03G moves the provider account-allowance contracts, the run-scoped Codex
account-usage stop at claim and dispatch and the run-scoped advisory Codex
account-usage warning, and repairs the usage evidence and token-activity
controls companion introductions that placed them in the protocol. This record
was written with WF-03G's uncommitted working-tree diff, for independent review
before any GO; it describes that diff, not a published delivery. Publication,
if any, is evidenced by Git history and the checkpoints, not by this record.

- **Baseline.** Commit `bc649c50971b04161ae4d9ba3a8f91529fef4f0c`, the
  published WF-03F result: `docs/architecture/agent-collaboration-protocol.md`
  blob `a45ec4779f0ac5c1b7e62d69aee11bbb70b9db8f`, SHA-256
  `6925bae10e1966a38e050986de5a1776bae94a28ac3d782fdecefabf025fd3c1`,
  182906 bytes, 2211 lines, 66 Markdown headings. Read it with
  `git show bc649c50:docs/architecture/agent-collaboration-protocol.md`.
- **Identifiers.** `GLstart-end` are WF-03G baseline line spans; `GPstart-end`
  are lines of the protocol after WF-03G; `GAstart-end` are lines of the
  [account usage](../architecture/agent-collaboration-account-usage.md)
  companion. Row IDs carry a `G-` prefix. No WF-03G identifier refers to a
  WF-03A, WF-03B, WF-03C, WF-03D, WF-03E or WF-03F row or coordinate. A section
  range spans its heading through the blank line before the next heading.
- **Dispositions and headings.** Retain and Move mean what they mean in
  WF-03A, with the same forwarder shape. All 66 baseline headings stay in the
  protocol in order with identical text and level, so every fragment,
  including the three new forwarding headings, resolves as before. The
  companion promotes the three moved headings from `###` to `##` with
  identical text, so their fragments are the same in both files. GA1-23 are
  the companion's title and introduction; GA74 and GA118 are the blank
  separators GL924 and GL1473. Companion lines GA24-74 correspond one to one
  to GL874-924 and GA75-151 to GL1430-1506: the three headings are promoted,
  GA27 (GL877) carries the one moved-body reference repair, and every other
  line is identical.

### WF-03G ranges

The six ranges cover GL1-2211 without gaps or overlap.

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| G-R-01 | GL1-873 | Purpose through Claude-reported model context limits, including the six companion routing paragraphs and the WF-03A, WF-03B, WF-03C and WF-03E forwarders | Retain; one routing insertion | GP1-514 (GL1-514), GP515-520 (insertion), GP521-879 (GL515-873) | Verbatim apart from the insertion: a five-line paragraph and a blank line immediately after the token-activity controls routing paragraph (GL510-514) and before `## Provider runtime configuration`, linking to the companion |
| G-M-01 | GL874-924 | Provider account-allowance contracts | Move | GA24 heading, GA25-73 body (GL875-923); forwarder GP880-883; GP884 (GL924) | Body verbatim except one authorized reference repair (GL877, GA27) |
| G-R-02 | GL925-1429 | Codex model and reasoning-effort catalog contract through the WF-03F forwarders | Retain; two reference repairs | GP885-1389 | Verbatim except the catalog's two repaired references (GL928, GP888; GL945, GP905) |
| G-M-02 | GL1430-1473 | Run-scoped Codex account-usage stop at claim and dispatch | Move | GA75 heading, GA76-117 body (GL1431-1472); forwarder GP1390-1393; GP1394 (GL1473) | Body verbatim |
| G-M-03 | GL1474-1507 | Run-scoped advisory Codex account-usage warning | Move | GA119 heading, GA120-151 body (GL1475-1506); forwarder GP1395-1398; GP1399 (GL1507) | Body verbatim |
| G-R-03 | GL1508-2211 | One manual Codex Planner format repair through protocol evolution, including the WF-03D forwarders | Retain | GP1400-2103 | Verbatim |

### WF-03G fragments

| ID | Baseline fragment (protocol forwarder) | Companion fragment |
|---|---|---|
| G-M-01 | [`#provider-account-allowance-contracts`](../architecture/agent-collaboration-protocol.md#provider-account-allowance-contracts) | [`#provider-account-allowance-contracts`](../architecture/agent-collaboration-account-usage.md#provider-account-allowance-contracts) |
| G-M-02 | [`#run-scoped-codex-account-usage-stop-at-claim-and-dispatch`](../architecture/agent-collaboration-protocol.md#run-scoped-codex-account-usage-stop-at-claim-and-dispatch) | [`#run-scoped-codex-account-usage-stop-at-claim-and-dispatch`](../architecture/agent-collaboration-account-usage.md#run-scoped-codex-account-usage-stop-at-claim-and-dispatch) |
| G-M-03 | [`#run-scoped-advisory-codex-account-usage-warning`](../architecture/agent-collaboration-protocol.md#run-scoped-advisory-codex-account-usage-warning) | [`#run-scoped-advisory-codex-account-usage-warning`](../architecture/agent-collaboration-account-usage.md#run-scoped-advisory-codex-account-usage-warning) |

### WF-03G link and reference changes

- **Repaired moved reference.** G-M-01's "the Codex per-attempt token-usage
  contract above" (GL876-877, GA26-27) reached the retained usage evidence
  forwarding heading (GL864) above it. GL877 is now GA27, where "token-usage
  contract above" became the link
  `[token-usage contract](agent-collaboration-usage-evidence.md#provider-token-usage-contracts)`,
  the usage evidence companion's token-usage section, which holds the Codex
  per-attempt contract. The substitution drops "above"; the line is not
  rewrapped. No other moved text changed.
- **Repaired retained references.** The Codex model catalog's "distinct from
  both contracts above" (GL927-928, GP887-888) meant the two sections above it
  when it was written, the provider token-usage contracts and the provider
  account-allowance contracts; GL928 is now GP888, where "both contracts above"
  became "both the" followed by a link to each,
  `[provider token-usage contracts](agent-collaboration-usage-evidence.md#provider-token-usage-contracts)`
  and
  `[provider account-allowance contracts](agent-collaboration-account-usage.md#provider-account-allowance-contracts)`.
  Its "the account-allowance contract above" (GL945, GP905) became
  `the [account-allowance contract](agent-collaboration-account-usage.md#provider-account-allowance-contracts)`.
  Both substitutions drop "above" and neither line is rewrapped. The catalog's
  undirected "the allowance contract's limit id" (GL961, GP921) and "exactly
  like the allowance contract" (GL981, GP941) and the model requests' "the
  catalog above" (GL990, GP950), which stays above, are unchanged.
- **Relative wording.** G-M-02's "never over the display-only allowance
  projection" (GL1440-1441, GA85-86) and "separately from token activity and
  from the display-only allowance" (GL1470, GA115), and G-M-03's "reuses
  neither the stop's gate, facts or decisions nor the display-only allowance"
  (GL1500, GA145) and "beside the stop control" (GL1502, GA147) carry no
  direction; the named sections are in the companion. G-M-02 links
  [ADR-0025](../decisions/0025-stop-new-codex-attempts-at-an-explicit-account-usage-percentage.md)
  (GL1440, GA85) and G-M-03 links
  [ADR-0026](../decisions/0026-warn-explicitly-about-a-codex-account-usage-percentage.md)
  (GL1487, GA132); both relative paths resolve unchanged from the same
  directory. G-M-01's Codex App Server and documentation addresses are code
  spans, not links. The moved sections contain no other link.
- **Retained references.** The protocol's unlinked runtime preflight "account
  usage" (GL537, GP543), review correction's "Token and account-usage
  enforcement" (GL613, GP619) and "Context usage and account usage" (GL690,
  GP696), the "Provider account-usage snapshots" paragraph (GL705, GP711), the
  challenge rounds' "account-usage input" (GL1523, GP1415) and "Claude account
  allowance," (GL1638, GP1530) carry no direction and are kept as written,
  including their historical wording. None of those lines changed.
- **Inbound references.** Links to the protocol fragments from the product
  [cockpit specification](../product/run-cockpit-specification.md) (lines 617
  and 803) still reach the retained forwarding headings, as does the frozen
  [execution history](current-work-history-ba1d4c2.md) link at line 3983. No
  document links the stop's fragment. The
  [delivery plan rework map](mvp-delivery-plan-rework-map.md) (line 70) names
  the Codex account-usage stop and warning with protocol line numbers of its
  own earlier baseline, and the [engineering context](../engineering-context.md)
  (line 36) routes token and account controls to the protocol, whose new
  routing paragraph reaches the companion. None of those lines changed. The
  WF-03D, WF-03E and WF-03F records above name these subjects as history; the
  WF-03F record's "separately from token activity" (its FL1621, FP1470) is now
  GL1470 and GA115.
- **Companion introductions.** At the baseline the
  [usage evidence](../architecture/agent-collaboration-usage-evidence.md)
  (lines 14-16) and
  [token-activity controls](../architecture/agent-collaboration-token-activity-controls.md)
  (lines 14-16) introductions said account allowances and account-usage stops
  and warnings stay in the protocol, which this move makes inaccurate. Each
  now routes them to the companion and keeps model and effort controls,
  budgets and every other unmoved subject in the protocol. In each, lines
  14-16 became lines 14-17, and everything from its first `##` heading
  (baseline line 19, now 20) through its end is byte-identical. These are
  introduction repairs, not moved-body repairs.

### WF-03G not moved

The Codex model and effort catalog, model, effort and agentic-turn requests,
the run-wide claim count and reserved-time budgets, runtime preflight,
manifests, structured output failure, the challenge rounds and escalation,
review correction, diagnosis, local delivery and every other subject not moved
by WF-03A to WF-03G stay in the protocol. The provider contracts, execution
evidence, evidence inspection and format repair companions are unchanged; the
usage evidence and token-activity controls companions changed only in their
introductions, as above. WF-03F's not-moved list named the account-usage stop
and warning and account allowances because they stayed at that move; WF-03G
moves them. This is a relocation, not a reconciliation: moved facts and
wording are as written at the baseline, except the one reference repair above.

## WF-03H record

WF-03H moves the Codex model and reasoning-effort catalog contract, the
explicit Codex model and reasoning-effort requests, the explicit Claude
model-alias and effort requests and the optional Claude agentic-turn limit for
mutation attempts, and repairs the usage evidence, token-activity controls and
account usage companion introductions that placed model and effort controls in
the protocol. This record was written with WF-03H's uncommitted working-tree
diff, for independent review before any GO; it describes that diff, not a
published delivery. Publication, if any, is evidenced by Git history and the
checkpoints, not by this record.

- **Baseline.** Commit `de8f1a72e72e1a6839714764248cce56ce2fed1b`, the
  published WF-03G result: `docs/architecture/agent-collaboration-protocol.md`
  blob `8c7ab934d936012018cd0d1e0d47e1ca084d0999`, SHA-256
  `99e65cfebf891ad8ebc7652cf9a968cb8f9c1a6e4c8f196514e96a6a547590b9`,
  173122 bytes, 2103 lines, 66 Markdown headings. Read it with
  `git show de8f1a72:docs/architecture/agent-collaboration-protocol.md`.
- **Identifiers.** `HLstart-end` are WF-03H baseline line spans; `HPstart-end`
  are lines of the protocol after WF-03H; `HIstart-end` are lines of the
  [invocation configuration](../architecture/agent-collaboration-invocation-configuration.md)
  companion. Row IDs carry an `H-` prefix. No WF-03H identifier refers to a
  row or coordinate of WF-03A to WF-03G. A section range spans its heading
  through the blank line before the next heading.
- **Dispositions and headings.** Retain and Move mean what they mean in
  WF-03A, with the same forwarder shape. All 66 baseline headings stay in the
  protocol in order with identical text and level, so every fragment,
  including the five new forwarding headings, resolves as before. The
  companion promotes the five moved headings from `###` to `##` with identical
  text, so their fragments are the same in both files. HI1-25 are the
  companion's title and introduction. Companion lines HI26-519 correspond one
  to one to HL885-1378: the five headings (HI26, HI87, HI267, HI343 and HI418)
  are promoted, and every other line is identical, including the blank
  separators HI86, HI266, HI342 and HI417 (HL945, HL1125, HL1201 and HL1276).
  Those four separators and HL1379 are also the blank lines that close the
  five forwarders.

### WF-03H ranges

The seven ranges cover HL1-2103 without gaps or overlap.

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| H-R-01 | HL1-884 | Purpose through the provider account-allowance forwarder, including the seven companion routing paragraphs and the WF-03A, WF-03B, WF-03C, WF-03E and WF-03G forwarders | Retain; one routing insertion | HP1-520 (HL1-520), HP521-527 (insertion), HP528-891 (HL521-884) | Verbatim apart from the insertion: a six-line paragraph and a blank line immediately after the account usage routing paragraph (HL515-520) and before `## Provider runtime configuration`, linking to the companion |
| H-M-01 | HL885-945 | Codex model and reasoning-effort catalog contract | Move | HI26 heading, HI27-85 body (HL886-944), HI86 (HL945); forwarder HP892-895; HP896 (HL945) | Body verbatim |
| H-M-02 | HL946-1125 | Explicit Codex model and reasoning-effort requests | Move | HI87 heading, HI88-265 body (HL947-1124), HI266 (HL1125); forwarder HP897-900; HP901 (HL1125) | Body verbatim |
| H-M-03 | HL1126-1201 | Explicit Claude model-alias requests | Move | HI267 heading, HI268-341 body (HL1127-1200), HI342 (HL1201); forwarder HP902-905; HP906 (HL1201) | Body verbatim |
| H-M-04 | HL1202-1276 | Explicit Claude effort requests | Move | HI343 heading, HI344-416 body (HL1203-1275), HI417 (HL1276); forwarder HP907-910; HP911 (HL1276) | Body verbatim |
| H-M-05 | HL1277-1379 | Optional Claude agentic-turn limit for mutation attempts | Move | HI418 heading, HI419-519 body (HL1278-1378); forwarder HP912-915; HP916 (HL1379) | Body verbatim |
| H-R-02 | HL1380-2103 | Per-provider run token-activity warnings through protocol evolution, including the WF-03F, WF-03G and WF-03D forwarders | Retain | HP917-1640 | Verbatim |

### WF-03H fragments

| ID | Baseline fragment (protocol forwarder) | Companion fragment |
|---|---|---|
| H-M-01 | [`#codex-model-and-reasoning-effort-catalog-contract`](../architecture/agent-collaboration-protocol.md#codex-model-and-reasoning-effort-catalog-contract) | [`#codex-model-and-reasoning-effort-catalog-contract`](../architecture/agent-collaboration-invocation-configuration.md#codex-model-and-reasoning-effort-catalog-contract) |
| H-M-02 | [`#explicit-codex-model-and-reasoning-effort-requests`](../architecture/agent-collaboration-protocol.md#explicit-codex-model-and-reasoning-effort-requests) | [`#explicit-codex-model-and-reasoning-effort-requests`](../architecture/agent-collaboration-invocation-configuration.md#explicit-codex-model-and-reasoning-effort-requests) |
| H-M-03 | [`#explicit-claude-model-alias-requests`](../architecture/agent-collaboration-protocol.md#explicit-claude-model-alias-requests) | [`#explicit-claude-model-alias-requests`](../architecture/agent-collaboration-invocation-configuration.md#explicit-claude-model-alias-requests) |
| H-M-04 | [`#explicit-claude-effort-requests`](../architecture/agent-collaboration-protocol.md#explicit-claude-effort-requests) | [`#explicit-claude-effort-requests`](../architecture/agent-collaboration-invocation-configuration.md#explicit-claude-effort-requests) |
| H-M-05 | [`#optional-claude-agentic-turn-limit-for-mutation-attempts`](../architecture/agent-collaboration-protocol.md#optional-claude-agentic-turn-limit-for-mutation-attempts) | [`#optional-claude-agentic-turn-limit-for-mutation-attempts`](../architecture/agent-collaboration-invocation-configuration.md#optional-claude-agentic-turn-limit-for-mutation-attempts) |

### WF-03H link and reference changes

- **Moved text.** No moved body line changed; WF-03H has no reference repair.
- **Relative wording.** The catalog's two links to the usage evidence and
  account usage companions (HL888, HI29; HL905, HI46) resolve unchanged from
  the same directory. Its undirected "the allowance contract's limit id"
  (HL921, HI62) and "exactly like the allowance contract" (HL941, HI82) carry
  no link; the companion introduction routes them to the account usage
  companion's allowance contract. H-M-02's "the catalog above" (HL950, HI91)
  reaches H-M-01; its "factories below" and "further below" (HL958-959,
  HI99-100) and "(see above)" (HL1002-1003, HI143-144) stay within H-M-02.
  H-M-03's "the concurrency tokens below" (HL1155, HI296) stays within H-M-03,
  and its "the later effort extension below" (HL1177, HI318) reaches H-M-04;
  H-M-04's "The model-alias request above" (HL1204, HI345) reaches H-M-03.
  H-M-05's "above 100" (HL1295, HI436; HL1325, HI466) is a numeric bound, not
  a direction.
- **In-page links.** H-M-02's `#explicit-claude-model-alias-requests` link
  (HL1124, HI265) and H-M-03's `#explicit-claude-effort-requests` link
  (HL1191, HI332) are same-document fragments; at the baseline they reached
  the protocol sections, and they now reach H-M-03 and H-M-04 in the
  companion, unchanged. H-M-02 links
  [ADR-0009](../decisions/0009-separate-agent-roles-effects-and-provider-assignments.md)
  (HL979, HI120); the relative path resolves unchanged from the same
  directory. Every other link in the moved sections is an absolute `https`
  address; the catalog's App Server documentation address (HL889, HI30) is a
  code span, not a link.
- **Retained references.** Review correction's quoted title "Optional Claude
  agentic-turn limit for mutation attempts" (HL691, HP698) is not a link and
  carries no direction; it names the retained forwarding heading. Its
  following "no model or effort is requested" (HL692, HP699) predates the
  Claude model-alias, effort and turn-limit requests and is kept as written.
  Diagnosis's "the current requested Codex model and effort" (HL1667, HP1204)
  and "the current Claude model, effort, and turn-limit requests" (HL1670,
  HP1207) carry no direction. The account-usage wording the WF-03G review
  deferred is here HL619, HL696 and HL711-713 (HP626, HP703 and HP718-720).
  None of those lines changed, and the relocation reconciles or endorses none
  of them.
- **Inbound references.** Links to the five protocol fragments from the
  product [cockpit specification](../product/run-cockpit-specification.md)
  (lines 649, 671, 695, 729 and 759), the
  [provider contracts](../architecture/agent-collaboration-provider-contracts.md)
  companion (line 167), the
  [documentation rework map](documentation-rework-map.md) (line 160), the
  [delivery plan rework map](mvp-delivery-plan-rework-map.md) (line 71) and the
  frozen [execution history](current-work-history-ba1d4c2.md) (lines 2218,
  3087, 3194, 3471 and 3837) still reach the retained forwarding headings, and
  each forwarder reaches the same fragment in the companion. The two rework
  maps keep their own earlier coordinates. The WF-03A record's fragments
  (lines 63-64 of this map) are code spans, as history. None of those lines
  changed.
- **Companion introductions.** At the baseline the
  [usage evidence](../architecture/agent-collaboration-usage-evidence.md)
  (lines 15-17),
  [token-activity controls](../architecture/agent-collaboration-token-activity-controls.md)
  (lines 15-17) and
  [account usage](../architecture/agent-collaboration-account-usage.md)
  (lines 20-21) introductions said model and effort controls stay in the
  protocol, which this move makes inaccurate. Each now routes model and effort
  controls and the Claude agentic-turn limit to the companion and keeps
  budgets and every other unmoved subject in the protocol. In usage evidence
  and token-activity controls, line 15 is unchanged and lines 16-17 became
  lines 16-19; in account usage, lines 20-21 became lines 20-24. Everything
  from each first `##` heading (baseline lines 20, 20 and 24, now 22, 22 and
  27) through its end is byte-identical. These are introduction repairs, not
  moved-body repairs.

### WF-03H not moved

Runtime preflight, the run-wide claim count and reserved-time budgets,
manifests, structured output failure, the challenge rounds and escalation,
review correction and its human guidance, diagnosis, local delivery and every
other subject not moved by WF-03A to WF-03H stay in the protocol. The provider
contracts, execution evidence, evidence inspection and format repair
companions are unchanged; the usage evidence, token-activity controls and
account usage companions changed only in their introductions, as above.
WF-03G's not-moved list named the Codex model and effort catalog and the
model, effort and agentic-turn requests because they stayed at that move;
WF-03H moves them. This is a relocation, not a reconciliation: moved facts and
wording are as written at the baseline.

## WF-03I record

WF-03I moves bounded human guidance for one authorized review correction,
optional direct human guidance for mutation requests, the optional second
challenge round and escalation, explicit human authorization of one
escalated-plan implementation and explicit verification failure diagnosis,
including its diagnosis-origin correction guidance, and repairs the format
repair companion introduction that placed the challenge rounds and escalation
in the protocol. This record was written with WF-03I's uncommitted
working-tree diff, for independent review before any GO; it describes that
diff, not a published delivery. Publication, if any, is evidenced by Git
history and the checkpoints, not by this record.

- **Baseline.** Commit `b81f761173f52e160acd9141df612b6c2a9c2812`, the
  published planning-policy result after WF-03H, which left the protocol
  unchanged: `docs/architecture/agent-collaboration-protocol.md` blob
  `f6f17d090811404bcaa1a89ae7adecd4c09f9a08`, SHA-256
  `47d11f5fd984f8d46dcdbf3cd2a70d9787fa773aa3e260c0d5521505099caefe`,
  135584 bytes, 1640 lines, 66 Markdown headings. Read it with
  `git show b81f7611:docs/architecture/agent-collaboration-protocol.md`.
- **Identifiers.** `ILstart-end` are WF-03I baseline line spans; `IPstart-end`
  are lines of the protocol after WF-03I; `IGstart-end` are lines of the
  [guidance and escalation](../architecture/agent-collaboration-guidance-and-escalation.md)
  companion. Row IDs carry an `I-` prefix. No WF-03I identifier refers to a
  row or coordinate of WF-03A to WF-03H. A section range spans its heading
  through the blank line before the next heading.
- **Dispositions and headings.** Retain and Move mean what they mean in
  WF-03A, with the same forwarder shape. All 66 baseline headings stay in the
  protocol in order with identical text and level, so every fragment,
  including the five new forwarding headings, resolves as before. The
  companion promotes the five moved headings from `###` to `##` with identical
  text, so their fragments are the same in both files. IG1-32 are the
  companion's title and introduction. Companion lines IG33-162 correspond one
  to one to IL722-851, and IG163-450 to IL947-1234: the five headings (IG33,
  IG94, IG163, IG287 and IG376) are promoted, IG173 (IL957) carries the one
  authorized reference repair, and every other line is identical, including
  the blank separators IG93, IG162, IG286 and IG375 (IL782, IL851, IL1070 and
  IL1159). Those four separators and IL1235 are also the blank lines that
  close the five forwarders.

### WF-03I ranges

The eight ranges cover IL1-1640 without gaps or overlap.

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| I-R-01 | IL1-721 | Purpose through review correction, including the eight companion routing paragraphs and the WF-03A forwarders | Retain; one routing insertion | IP1-527 (IL1-527), IP528-535 (insertion), IP536-729 (IL528-721) | Verbatim apart from the insertion: a seven-line paragraph and a blank line immediately after the invocation configuration routing paragraph (IL521-527) and before `## Provider runtime configuration`, linking to the companion |
| I-M-01 | IL722-782 | Bounded human guidance for one authorized review correction | Move | IG33 heading, IG34-92 body (IL723-781), IG93 (IL782); forwarder IP730-733; IP734 (IL782) | Body verbatim |
| I-M-02 | IL783-851 | Optional direct human guidance for mutation requests | Move | IG94 heading, IG95-161 body (IL784-850), IG162 (IL851); forwarder IP735-738; IP739 (IL851) | Body verbatim |
| I-R-02 | IL852-946 | Execution evidence versus semantic outcome through the remaining read-only stages' format repair, all forwarders (WF-03B, WF-03C, WF-03E, WF-03G, WF-03H, WF-03F, WF-03G and WF-03D, in that order) | Retain | IP740-834 | Verbatim |
| I-M-03 | IL947-1070 | Optional second challenge round and escalation | Move | IG163 heading, IG164-285 body (IL948-1069), IG286 (IL1070); forwarder IP835-838; IP839 (IL1070) | Body verbatim except the IG173 (IL957) reference repair |
| I-M-04 | IL1071-1159 | Explicit human authorization of one escalated-plan implementation | Move | IG287 heading, IG288-374 body (IL1072-1158), IG375 (IL1159); forwarder IP840-843; IP844 (IL1159) | Body verbatim |
| I-M-05 | IL1160-1235 | Explicit verification failure diagnosis, including its correction guidance | Move | IG376 heading, IG377-450 body (IL1161-1234); forwarder IP845-848; IP849 (IL1235) | Body verbatim |
| I-R-03 | IL1236-1640 | Bounded untracked-file previews in Agent manifests through protocol evolution | Retain | IP850-1254 | Verbatim |

### WF-03I fragments

| ID | Baseline fragment (protocol forwarder) | Companion fragment |
|---|---|---|
| I-M-01 | [`#bounded-human-guidance-for-one-authorized-review-correction`](../architecture/agent-collaboration-protocol.md#bounded-human-guidance-for-one-authorized-review-correction) | [`#bounded-human-guidance-for-one-authorized-review-correction`](../architecture/agent-collaboration-guidance-and-escalation.md#bounded-human-guidance-for-one-authorized-review-correction) |
| I-M-02 | [`#optional-direct-human-guidance-for-mutation-requests`](../architecture/agent-collaboration-protocol.md#optional-direct-human-guidance-for-mutation-requests) | [`#optional-direct-human-guidance-for-mutation-requests`](../architecture/agent-collaboration-guidance-and-escalation.md#optional-direct-human-guidance-for-mutation-requests) |
| I-M-03 | [`#optional-second-challenge-round-and-escalation`](../architecture/agent-collaboration-protocol.md#optional-second-challenge-round-and-escalation) | [`#optional-second-challenge-round-and-escalation`](../architecture/agent-collaboration-guidance-and-escalation.md#optional-second-challenge-round-and-escalation) |
| I-M-04 | [`#explicit-human-authorization-of-one-escalated-plan-implementation`](../architecture/agent-collaboration-protocol.md#explicit-human-authorization-of-one-escalated-plan-implementation) | [`#explicit-human-authorization-of-one-escalated-plan-implementation`](../architecture/agent-collaboration-guidance-and-escalation.md#explicit-human-authorization-of-one-escalated-plan-implementation) |
| I-M-05 | [`#explicit-verification-failure-diagnosis`](../architecture/agent-collaboration-protocol.md#explicit-verification-failure-diagnosis) | [`#explicit-verification-failure-diagnosis`](../architecture/agent-collaboration-guidance-and-escalation.md#explicit-verification-failure-diagnosis) |

### WF-03I link and reference changes

- **Moved text.** One moved body line changed. At IL957 (IG173) "the
  role-first rule above" became
  `[role-first rule](agent-collaboration-protocol.md#role-first-message-authorization-and-eligibility)`,
  because the rule (IL39, IP39) stays in the protocol. The line was not
  rewrapped, and the replacement drops the article, so the link directly
  follows "per". No other moved line changed.
- **Relative wording.** I-M-01's "the same resolver a claim uses (below)"
  (IL753, IG64) stays within I-M-01. I-M-02's "the authorization guidance
  above" (IL790, IG101) reaches I-M-01, its unlinked "described under
  "Explicit verification failure diagnosis"" (IL787, IG98) names I-M-05, and
  its "the three states above" (IL843, IG154) stays within I-M-02. I-M-03's
  "the rules above" (IL1060, IG276) stays within I-M-03, and its "the one
  explicit authorization described next" (IL1067, IG283) reaches I-M-04.
  I-M-05's "(see "Correction" below)" (IL1173, IG389) and "the idempotent
  escalation above" (IL1222, IG438) stay within I-M-05. Temporal "earlier",
  "later", "previous" and "before" wording carries no section direction.
- **In-page and relative links.** I-M-03's two
  `#explicit-human-authorization-of-one-escalated-plan-implementation` links
  (IL1027, IG243; IL1042, IG258) are same-document fragments; at the baseline
  they reached the protocol section, and they now reach I-M-04 in the
  companion, unchanged. The workflow model link (IL990, IG206), the cockpit
  specification link (IL1065, IG281) and the ADR-0015 to ADR-0020 links
  (IL786, IG97; IL788, IG99; IL1024, IG240; IL1074, IG290; IL1089, IG305;
  IL1154, IG370; IL1162, IG378; IL1214, IG430; IL1215, IG431) resolve
  unchanged from the same directory. The moved sections have no absolute
  `https` address.
- **Unlinked references.** "The run-wide count and reserved-time budgets"
  (IL769, IG80), "the ordinary correction budget" (IL813, IG124), "ordinary
  correction" (IL1149, IG365) and "The existing `ReviewCorrection` contract"
  (IL1205, IG421) name retained protocol content; the companion introduction
  routes them to the protocol's Review correction section. "Its format
  repair" (IL1152, IG368), the Codex and Claude token stops (IL1203, IG419;
  IL1207, IG423), "the current requested Codex model and effort" (IL1204,
  IG420) and "the current Claude model, effort, and turn-limit requests"
  (IL1207, IG423) carry no link; the introduction routes them to the format
  repair, token-activity controls and invocation configuration companions.
  "As the ordinary correction" (IL1219, IG435) compares with the ordinary
  correction's direct guidance, which is I-M-02 in the companion. The WF-03H
  record cites the diagnosis wording of IL1204 and IL1207 as retained HL1667
  and HL1670; WF-03I moves it unchanged, and that record keeps its statements
  as history.
- **Retained references.** The protocol's in-page links to the five
  fragments (IL86, IL319, IL324, IL373 and IL439; unchanged as IP86, IP319,
  IP324, IP373 and IP439) reach the retained forwarding headings. Review
  correction's escalation at exhaustion and its one additional human
  authorization (IL618-625, IP626-633), its quoted title "Optional Claude
  agentic-turn limit for mutation attempts" and "no model or effort is
  requested" (IL698-699, IP706-707), and the manifest sections' mentions of
  diagnosis and diagnosis-origin correction (IL1243, IL1246, IL1551-1552 and
  IL1585; IP857, IP860, IP1165-1166 and IP1199) carry no link or section
  direction. None of those lines changed, and the relocation reconciles or
  endorses none of them.
- **Inbound references.** Links to the five protocol fragments from the
  [workflow model](../architecture/workflow-model.md) (line 369),
  [ADR-0015](../decisions/0015-add-direct-human-guidance-to-explicit-mutation-requests.md)
  (line 10),
  [ADR-0016](../decisions/0016-add-explicit-human-authorization-of-one-escalated-plan-implementation.md)
  (line 9), the product
  [cockpit specification](../product/run-cockpit-specification.md) (lines 867,
  1040, 1079 and 1124), the
  [documentation rework map](documentation-rework-map.md) (lines 161, 162 and
  164), the [delivery plan rework map](mvp-delivery-plan-rework-map.md) (lines
  72 and 78) and the frozen
  [execution history](current-work-history-ba1d4c2.md) (lines 2643 and 2888)
  still reach the retained forwarding headings, and each forwarder reaches the
  same fragment in the companion. The two rework maps keep their own earlier
  coordinates. A test comment quoting the first moved title is not a link.
  None of those lines changed.
- **Companion introduction.** At the baseline the
  [format repair](../architecture/agent-collaboration-format-repair.md)
  introduction (lines 10-11) said the challenge rounds and escalation stay in
  the protocol, which this move makes inaccurate. It now routes them to the
  companion and keeps structured output failure and the base review-correction
  contract in the protocol; lines 10-11 became lines 10-14, and the mapping
  line 12 is now line 15. Everything from its first `##` heading (baseline line
  14, now 17) through its end is byte-identical. This is an introduction
  repair, not a moved-body repair.

### WF-03I not moved

Review correction (IL582-721) stays whole in the protocol, because it mixes
the base correction loop with the run-wide claim-count and reserved-time
budgets and provider configuration observations. Structured output failure,
runtime preflight and the rest of provider runtime configuration, manifest
capture, the local-delivery contracts and every other subject not moved by
WF-03A to WF-03I stay in the protocol. The provider contracts, execution
evidence, evidence inspection, usage evidence, token-activity controls,
account usage and invocation configuration companions are unchanged; the
format repair companion changed only in its introduction. WF-03H's not-moved
list named the challenge rounds and escalation, review correction's human
guidance and diagnosis because they stayed at that move; WF-03I moves them.
This is a relocation, not a reconciliation: moved facts and wording are as
written at the baseline, apart from the one authorized reference repair.

## WF-03J record

WF-03J removes the mixed Review correction body from the protocol: the
correction loop with its budget and escalation, the run-wide claim-count and
reserved-time budgets and their manual-intake choice move to a new correction
and budgets companion, and the provider configuration observations that ended
the same section move to the invocation configuration companion. It repairs
the introductions of the invocation configuration, guidance and escalation,
format repair, execution evidence, account usage, usage evidence and
token-activity controls companions that placed review correction or budgets in
the protocol. This record was written with
WF-03J's uncommitted working-tree diff, for independent review before any GO;
it describes that diff, not a published delivery. Publication, if any, is
evidenced by Git history and the checkpoints, not by this record.

- **Baseline.** Commit `578c83d3b9e6550d22ca7785ee76c6e15d66607d`, the
  published WF-03I result: `docs/architecture/agent-collaboration-protocol.md`
  blob `745f9c564e59a62a6f3f94332a49be9f09eb82dc`, SHA-256
  `ec0fb085e8e696d86a8c53ba269691965d3e24ffcb0db1c39000d8f53f232cd1`,
  91697 bytes, 1254 lines, 66 Markdown headings. Read it with
  `git show 578c83d3:docs/architecture/agent-collaboration-protocol.md`.
- **Identifiers.** `JLstart-end` are WF-03J baseline line spans; `JPstart-end`
  are lines of the protocol after WF-03J; `JCstart-end` are lines of the
  [correction and budgets](../architecture/agent-collaboration-correction-and-budgets.md)
  companion; `JVstart-end` are lines of the
  [invocation configuration](../architecture/agent-collaboration-invocation-configuration.md)
  companion after WF-03J. Row IDs carry a `J-` prefix. No WF-03J identifier
  refers to a row or coordinate of WF-03A to WF-03I. A section range spans its
  heading through the blank line before the next heading.
- **Dispositions and headings.** Retain and Move mean what they mean in
  WF-03A, except that the one forwarder names two destinations: its body is a
  blank line and a five-line paragraph linking to both moved parts. All 66
  baseline headings stay in the protocol in order with identical text and
  level, so every fragment, including the `#review-correction` forwarder,
  resolves as before. The correction and budgets companion promotes the Review
  correction heading from `###` to `##` with identical text, so its fragment is
  the same in both files. The invocation configuration companion's new
  `## Provider configuration observations` heading (JV526) has no baseline
  counterpart; its fragment is new. JC1-30 are the correction and budgets
  companion's title and introduction, and JC31-126 correspond one to one to
  JL590-685: the heading (JC31) is promoted and every other line is
  identical. JV543-585 correspond one to one to JL686-728, and JV570 (JL713)
  carries the one authorized substitution. JV525-542 are the new section's
  blank separator, heading, blank line and framing paragraph, which says that
  the section is relocated baseline wording.

### WF-03J ranges

The four ranges cover JL1-1254 without gaps or overlap.

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| J-R-01 | JL1-589 | Purpose through the deferred Gemini CLI forwarder, including the nine companion routing paragraphs and runtime preflight | Retain; one routing insertion | JP1-535 (JL1-535), JP536-544 (insertion), JP545-598 (JL536-589) | Verbatim apart from the insertion: an eight-line paragraph and a blank line after the guidance and escalation routing paragraph (JL528-534) and its blank line (JL535) and before `## Provider runtime configuration`, linking to both destinations |
| J-M-01 | JL590-685 | Review correction: the correction loop, its budget and escalation, the run-wide claim-count and reserved-time budgets and their manual-intake choice | Move | JC31 heading, JC32 (JL591), JC33-126 body (JL592-685); forwarder JP599-605 | Body verbatim |
| J-M-02 | JL686-729 | Provider configuration observations, with the blank line before them and the section's closing blank line | Move | JV543 (JL686), JV544-585 body (JL687-728); JP606 (JL729) | Body verbatim except the JV570 (JL713) substitution |
| J-R-02 | JL730-1254 | Bounded human guidance forwarder through protocol evolution, including every later forwarder | Retain | JP607-1131 | Verbatim |

### WF-03J fragments

| ID | Baseline fragment (protocol forwarder) | Companion fragment |
|---|---|---|
| J-M-01 | [`#review-correction`](../architecture/agent-collaboration-protocol.md#review-correction) | [`#review-correction`](../architecture/agent-collaboration-correction-and-budgets.md#review-correction) |
| J-M-02 | [`#review-correction`](../architecture/agent-collaboration-protocol.md#review-correction) (the same forwarder) | [`#provider-configuration-observations`](../architecture/agent-collaboration-invocation-configuration.md#provider-configuration-observations) (new) |

### WF-03J link and reference changes

- **Moved text.** One moved body line changed. At JL713 (JV570)
  "host-runtime-preflight projection above" became
  `[host-runtime-preflight projection](agent-collaboration-protocol.md#provider-runtime-configuration)`,
  because that projection, the runtime preflight stages under
  `## Provider runtime configuration` (JL536-573, JP545-582), stays in the
  protocol, whose retained heading the link reaches. The line was not
  rewrapped; the rest of it is unchanged. The new framing paragraph
  (JV528-542) describes this change. No other moved line changed.
- **Relative wording.** J-M-01's "the run-wide Agent invocation-*time*
  reservation budget described immediately below" (JL636, JC77) reaches the
  reserved-time paragraph (JL655-674, JC96-115), its "the narrower
  review-correction budget above" (JL646, JC87) reaches the correction budget
  paragraph (JL626-640, JC67-81) and its "the 16-claim count budget above"
  (JL658, JC99) reaches the claim-count paragraph (JL642-653, JC83-94); all
  three stay within J-M-01, which keeps the correction and both run-wide
  budgets adjacent in their original order. "A ceiling below a role's
  configured timeout" (JL683, JC124) and "above 16 claims" (JL684, JC125) are
  comparisons, not section directions. In J-M-02, "the next eligible attempt"
  (JL721, JV578) is temporal, and the quoted title "Optional Claude
  agentic-turn limit for mutation attempts" (JL706, JV563) now names a
  section of the same companion (JV423).
- **Relative links.** The ADR-0013 links (JL638, JC79; JL674, JC115), the
  ADR-0012 link (JL653, JC94) and the ADR-0028 link (JL677, JC118) resolve
  unchanged from the same directory. Apart from the JV570 repair link, the
  moved text gained no link; it has no in-page link and no absolute `https`
  address.
- **Retained wording.** The moved statements are as written at the baseline,
  and the relocation reconciles or endorses none of them, including the six
  Agent-claiming paths (JL643, JC84; JL659, JC100) beside the eight Agent
  claim handlers (JL681, JC122); token and account-usage enforcement and
  measured wall-clock duration remaining deferred (JL634-635, JC75-76); the
  requested and observed model and effort that remain null (JL706-709,
  JV563-566); context and account usage not recorded or read as tracked or
  enforced (JL711-714, JV568-571); and the configured hard threshold (JL726-728,
  JV583-585). Both new introductions say so.
- **Retained references.** The protocol's "additional review-correction
  claim" (JL317, JP317) carries no link or section direction. Provider
  runtime configuration's preflight body (JL536-573, JP545-582) is unchanged.
  The provider contracts companion's link to `#provider-runtime-configuration`
  (line 5) is unchanged. Earlier records' coordinates for Review correction,
  such as WF-03I's IL582-721 and IP626-633, keep their statements as history.
- **Inbound references.** Links to the protocol's `#review-correction` from
  the [delivery plan](mvp-delivery-plan.md) (line 185) and the
  [delivery plan rework map](mvp-delivery-plan-rework-map.md) (line 67) still
  reach the retained forwarding heading, which reaches both destinations; that
  rework map keeps its own earlier coordinates. Neither line changed. The
  guidance and escalation introduction's link to it (baseline line 19) is
  repaired, as described below.
- **New links.** The protocol routing paragraph (JP539 and JP542) and the
  forwarder (JP601 and JP604) link to the two destinations; the correction and
  budgets introduction links to the protocol, the execution evidence,
  token-activity controls, account usage, guidance and escalation and
  invocation configuration companions and this map; the framing paragraph
  links to the correction and budgets companion's `#review-correction` and the
  account usage and token-activity controls companions; the repaired JV570
  links to the protocol's `#provider-runtime-configuration`; the seven repaired
  introductions link to the correction and budgets companion.
- **Companion introductions.** Each repair replaces only routing wording; from
  each companion's first `##` heading through its baseline end, the text is
  byte-identical. The
  [invocation configuration](../architecture/agent-collaboration-invocation-configuration.md)
  introduction (lines 16-23) said claim and time budgets stay in the protocol
  and called every section verbatim; lines 16-28 now route review correction
  and the budgets to the new companion and describe the last section's one
  wording change. Its first `##` heading moved from line 26 to 31, and its
  baseline lines 24-519 are JV29-524, followed by the appended section
  (JV525-585). The
  [guidance and escalation](../architecture/agent-collaboration-guidance-and-escalation.md)
  introduction (lines 17-22) linked to the protocol's Review correction
  section and kept the budgets there; lines 17-23 now link to the companion's
  `#review-correction`, and its first `##` heading moved from line 33 to 34.
  The [format repair](../architecture/agent-collaboration-format-repair.md)
  introduction (lines 10-14) kept the base review-correction contract in the
  protocol; lines 10-15 route it to the new companion, and its first `##`
  heading moved from line 17 to 18. The
  [execution evidence](../architecture/agent-collaboration-execution-evidence.md)
  introduction (line 11) said budgets stay in the protocol; lines 11-13 route
  the claim-count and reserved-time budgets to the new companion, and its
  first `##` heading moved from line 14 to 16. The
  [account usage](../architecture/agent-collaboration-account-usage.md)
  introduction (lines 23-24) kept claim and time budgets in the protocol;
  lines 23-26 route them to the new companion, and its first `##` heading moved
  from line 27 to 29. The
  [usage evidence](../architecture/agent-collaboration-usage-evidence.md)
  introduction (lines 18-19) said budgets stay in the protocol, and the
  [token-activity controls](../architecture/agent-collaboration-token-activity-controls.md)
  introduction (lines 18-19) kept claim and time budgets there; in each, lines
  18-21 now route the claim-count and reserved-time budgets to the new
  companion, and the first `##` heading moved from line 22 to 24. These are
  introduction repairs, not moved-body repairs.
- **Index.** The [architecture index](../architecture/README.md) adds one
  entry for the new companion after the guidance and escalation entry, and the
  invocation configuration entry's last two lines (37-38) now also name the
  relocated provider configuration observations.

### WF-03J not moved

Runtime preflight and the rest of provider runtime configuration, structured
output failure, manifest capture, the local-delivery contracts and every other
subject not moved by WF-03A to WF-03J stay in the protocol. The provider
contracts and evidence inspection companions are unchanged; the usage evidence
and token-activity controls companions changed only in their introductions.
WF-03I's not-moved
paragraph named Review correction because it stayed whole at that move; WF-03J
moves it. This is a relocation, not a reconciliation: moved facts and wording
are as written at the baseline, apart from the one authorized substitution.

## WF-03K record

WF-03K moves the seven remaining source-evidence and local-delivery contract
bodies out of the provider-runtime container: the bounded untracked-file
previews, bounded tracked-hunk evidence, attested tracked-change text,
attested checkpoint comparison and project instruction context sections move
to a new source evidence companion, and the complete-set Human approval and
recorded local-delivery receipt sections move to a new local delivery
companion. Host runtime preflight and the dialogue and context core stay in
the protocol. No existing companion changes. This record was written with
WF-03K's uncommitted working-tree diff, for independent review before any GO;
it describes that diff, not a published delivery. Publication, if any, is
evidenced by Git history and the checkpoints, not by this record.

- **Baseline.** Commit `0de51c01983be9e996d7c59bde44145be781a254`, the
  published WF-03J result: `docs/architecture/agent-collaboration-protocol.md`
  blob `3e95f9a41fe5c2cd5c93ef357b7f779e29beb31c`, SHA-256
  `c7028c274ef1c8a0dbea0e246c924cbce99f8adb8c87bdb21917baa7b125696c`,
  83364 bytes, 1131 lines, 66 Markdown headings. Read it with
  `git show 0de51c01:docs/architecture/agent-collaboration-protocol.md`.
- **Identifiers.** `KLstart-end` are WF-03K baseline line spans; `KPstart-end`
  are lines of the protocol after WF-03K; `KSstart-end` are lines of the
  [source evidence](../architecture/agent-collaboration-source-evidence.md)
  companion; `KDstart-end` are lines of the
  [local delivery](../architecture/agent-collaboration-local-delivery.md)
  companion. Row IDs carry a `K-` prefix. No WF-03K identifier refers to a row
  or coordinate of WF-03A to WF-03J. A section range spans its heading through
  the blank line before the next heading.
- **Dispositions and headings.** Retain and Move mean what they mean in
  WF-03A. All 66 baseline headings stay in the protocol in order with
  identical text and level, so every fragment resolves as before. Each moved
  section keeps its heading and closing blank line in the protocol as a
  forwarder: the heading, a blank line, a two-line link to the destination
  fragment and the closing blank line. Each companion promotes its headings
  from `###` to `##` with identical text, so each fragment is the same in the
  protocol and its companion. KS1-23 are the source evidence companion's title,
  introduction and blank line; KS24-282 correspond one to one to KL727-985 and
  KS284-344 to KL1030-1090, with the five headings (KS24, KS88, KS166, KS253,
  KS284) promoted and every other line identical. KS283 is a new blank
  separator, and the blank separators KS87, KS165 and KS252 stand in the
  positions of KL790, KL868 and KL955, whose blank lines also stay in the
  protocol as forwarder closing lines. KD1-24 are the local delivery
  companion's title, introduction and blank line; KD25-66 correspond one to one
  to KL987-1028, with the two headings (KD25, KD49) promoted, and the blank
  separator KD48 stands in the position of KL1010. Neither companion ends with
  a blank line.

### WF-03K ranges

The nine ranges cover KL1-1131 without gaps or overlap.

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| K-R-01 | KL1-726 | Purpose through the explicit verification failure diagnosis forwarder, including the ten companion routing paragraphs and runtime preflight | Retain; one routing insertion | KP1-544 (KL1-544), KP545-557 (insertion), KP558-739 (KL545-726) | Verbatim apart from the insertion: two routing paragraphs, each followed by a blank line, after the correction and budgets routing paragraph (KL536-543) and its blank line (KL544) and before `## Provider runtime configuration` |
| K-M-01 | KL727-790 | Bounded untracked-file previews in Agent manifests | Move | KS24 heading, KS25-86 body (KL728-789); forwarder KP740-744 | Body verbatim |
| K-M-02 | KL791-868 | Bounded tracked-hunk evidence in Agent manifests | Move | KS88 heading, KS89-164 body (KL792-867); forwarder KP745-749 | Body verbatim |
| K-M-03 | KL869-955 | Attested tracked-change text in Agent manifests | Move | KS166 heading, KS167-251 body (KL870-954); forwarder KP750-754 | Body verbatim |
| K-M-04 | KL956-986 | Attested checkpoint comparison for human inspection | Move | KS253 heading, KS254-282 body (KL957-985); forwarder KP755-759 | Body verbatim |
| K-M-05 | KL987-1010 | Complete-set Human approval for local delivery | Move | KD25 heading, KD26-47 body (KL988-1009); forwarder KP760-764 | Body verbatim |
| K-M-06 | KL1011-1029 | Recorded local-delivery receipt | Move | KD49 heading, KD50-66 body (KL1012-1028); forwarder KP765-769 | Body verbatim |
| K-M-07 | KL1030-1091 | Project instruction context in Agent manifests | Move | KS284 heading, KS285-344 body (KL1031-1090); forwarder KP770-774 | Body verbatim |
| K-R-02 | KL1092-1131 | Token efficiency through protocol evolution | Retain | KP775-814 | Verbatim |

In each forwarder the first line is the retained heading (KL727, KL791,
KL869, KL956, KL987, KL1011, KL1030) and the last is the retained closing
blank line (KL790, KL868, KL955, KL986, KL1010, KL1029, KL1091); the three
lines between them are new.

### WF-03K fragments

| ID | Baseline fragment (protocol forwarder) | Companion fragment |
|---|---|---|
| K-M-01 | [`#bounded-untracked-file-previews-in-agent-manifests`](../architecture/agent-collaboration-protocol.md#bounded-untracked-file-previews-in-agent-manifests) | [`#bounded-untracked-file-previews-in-agent-manifests`](../architecture/agent-collaboration-source-evidence.md#bounded-untracked-file-previews-in-agent-manifests) |
| K-M-02 | [`#bounded-tracked-hunk-evidence-in-agent-manifests`](../architecture/agent-collaboration-protocol.md#bounded-tracked-hunk-evidence-in-agent-manifests) | [`#bounded-tracked-hunk-evidence-in-agent-manifests`](../architecture/agent-collaboration-source-evidence.md#bounded-tracked-hunk-evidence-in-agent-manifests) |
| K-M-03 | [`#attested-tracked-change-text-in-agent-manifests`](../architecture/agent-collaboration-protocol.md#attested-tracked-change-text-in-agent-manifests) | [`#attested-tracked-change-text-in-agent-manifests`](../architecture/agent-collaboration-source-evidence.md#attested-tracked-change-text-in-agent-manifests) |
| K-M-04 | [`#attested-checkpoint-comparison-for-human-inspection`](../architecture/agent-collaboration-protocol.md#attested-checkpoint-comparison-for-human-inspection) | [`#attested-checkpoint-comparison-for-human-inspection`](../architecture/agent-collaboration-source-evidence.md#attested-checkpoint-comparison-for-human-inspection) |
| K-M-05 | [`#complete-set-human-approval-for-local-delivery`](../architecture/agent-collaboration-protocol.md#complete-set-human-approval-for-local-delivery) | [`#complete-set-human-approval-for-local-delivery`](../architecture/agent-collaboration-local-delivery.md#complete-set-human-approval-for-local-delivery) |
| K-M-06 | [`#recorded-local-delivery-receipt`](../architecture/agent-collaboration-protocol.md#recorded-local-delivery-receipt) | [`#recorded-local-delivery-receipt`](../architecture/agent-collaboration-local-delivery.md#recorded-local-delivery-receipt) |
| K-M-07 | [`#project-instruction-context-in-agent-manifests`](../architecture/agent-collaboration-protocol.md#project-instruction-context-in-agent-manifests) | [`#project-instruction-context-in-agent-manifests`](../architecture/agent-collaboration-source-evidence.md#project-instruction-context-in-agent-manifests) |

### WF-03K link and reference changes

- **Moved text.** No moved line changed; there is no substitution.
- **Directional wording.** The moved source-evidence sections keep their
  baseline order, so their references to each other by quoted title name
  sections of the same companion: "Bounded tracked-hunk evidence in Agent
  manifests" (KL764, KS61) names KS88; "Attested tracked-change text in Agent
  manifests" (KL784-785, KS81-82; KL794, KS91) names KS166; "Attested
  checkpoint comparison for human inspection" (KL877-878, KS174-175) names
  KS253. "Described below" (KL795, KS92) and "the omission metadata below"
  (KL809, KS106) stay within K-M-02, and "the next file" (KL929, KS226) is the
  next path in ordinal order within K-M-03. Other words such as "previous",
  "later" and "earlier" in the moved text are temporal, and "this section"
  (KL1054, KS308) means the manifest's instruction context section, not a
  document section. The local-delivery sections have no directional reference.
  No directional reference was broken by the relocation.
- **Relative links.** The moved text's eleven links all reach ADRs and resolve
  unchanged from the same directory: ADR-0022 (KL757, KS54), ADR-0024 (KL785,
  KS82; KL793, KS90; KL875, KS172), ADR-0027 (KL877, KS174; KL961, KS258) and
  ADR-0021 (KL1034, KS288) in the source evidence companion, and ADR-0029
  (KL989, KD27; KL1013, KD51), ADR-0030 (KL991, KD29) and ADR-0032 (KL1014,
  KD52) in the local delivery companion. The moved text has no in-page link
  and no absolute `https` address.
- **Retained wording.** The moved statements are as written at the baseline,
  and the relocation reconciles or endorses none of them, including the five
  Agent manifests that carry `changeEvidence` (KL731, KS28; KL799, KS96)
  beside six manifest families (KL734, KS31); "Seven of the eight claim
  handlers" (KL736, KS33) and the eight claim handlers (KL886, KS183; KL1041,
  KS295); the Windows-only physical proofs and other-host omissions (KL744,
  KS41; KL761-762, KS58-59; KL897-898, KS194-195; KL952, KS249;
  KL982-983, KS279-280; KL1051-1052, KS305-306); the stated limits of the raw
  Git observation (KL781-786, KS78-83; KL950, KS247; KL1052-1053, KS306-307);
  the process doubles that do not establish real-provider reliability (KL952,
  KS249); the Human decisions that still block local delivery (KL1008-1009,
  KD46-47); and the receipt saying nothing about current workspace state or
  remote publication (KL1027-1028, KD65-66). Both new introductions say so.
- **Retained references.** The protocol's context assembly list names
  "Project instruction context in Agent manifests" (KL472-473, KP472-473),
  "Bounded tracked-hunk evidence in Agent manifests" and "Bounded
  untracked-file previews in Agent manifests" (KL477-478, KP477-478); token
  efficiency names the untracked previews (KL1103, KP786); untrusted content
  names the instruction context (KL1122, KP805). These quoted titles carry no
  link and reach the retained forwarding headings. Provider runtime
  configuration's preflight body (KL545-582, KP558-595) is unchanged. Earlier
  records' coordinates for these sections, such as WF-03J's JP607-1131 and
  WF-03I's IL1236-1640, keep their statements as history.
- **Inbound references.** Tracked links to the seven baseline fragments:
  ADR-0022 (line 9) and ADR-0024 (line 8); the
  [documentation rework map](documentation-rework-map.md) (lines 128, 130,
  157, 158, 159 with two links, and 166); the
  [delivery plan rework map](mvp-delivery-plan-rework-map.md) (lines 73 to
  76); and the frozen
  [execution history](current-work-history-ba1d4c2.md) (lines 2372, 2435 and
  2534). All sixteen links still reach the retained forwarding headings, which
  link to the destination fragments; none changed, and those maps keep their
  own earlier coordinates. ADR-0021's prose mention of the protocol's untracked
  previews and tracked-hunk evidence (line 16) and the
  [engineering context](../engineering-context.md) rows that route manifests
  and the approval set and receipt to the protocol (lines 36 and 38) carry no
  fragment and reach the protocol, whose routing paragraphs and forwarders
  lead on; they are unchanged.
- **New links.** The protocol routing paragraphs (KP549 and KP554) and the
  seven forwarders (KP742, KP747, KP752, KP757, KP762, KP767 and KP772) link to
  the two companions. The source evidence introduction links to the protocol,
  the local delivery companion and this map; the local delivery introduction
  links to the protocol, ADR-0029, the source evidence companion,
  [GitHub and CI integration](../architecture/github-ci-integration.md), the
  [data and recovery](../architecture/data-and-recovery.md) local commit
  recovery section and this map.
- **Index.** The [architecture index](../architecture/README.md) adds two
  entries, for the source evidence and local delivery companions, after the
  correction and budgets entry.

### WF-03K not moved

Host runtime preflight and the rest of provider runtime configuration, the
dialogue and context core (message envelope through context assembly), token
efficiency, untrusted content, protocol evolution and every other subject not
moved by WF-03A to WF-03K stay in the protocol. No existing companion changes.
WF-03J's not-moved paragraph named manifest capture and the local-delivery
contracts because they stayed in the protocol at that move; WF-03K moves them.
This is a relocation, not a reconciliation: moved facts and wording are as
written at the baseline.

## WF-03L record

WF-03L separates attempt-input preparation and host runtime preflight from the
protocol's dialogue, ledger and role core: the context assembly, token
efficiency and untrusted content bodies move to a new context companion, and
the provider runtime configuration body, host runtime preflight, moves to the
provider contracts companion before its CLI contracts. The companion directory
stays in the protocol under the context assembly heading. Run execution mode
and isolation and protocol evolution stay in the protocol. This record was
written with WF-03L's uncommitted working-tree diff, for independent review
before any GO; it describes that diff, not a published delivery. Publication,
if any, is evidenced by Git history and the checkpoints, not by this record.

- **Baseline.** Commit `340d5e9d0148c32e21796a96504c283f3da025db`, the
  published WF-03K result: `docs/architecture/agent-collaboration-protocol.md`
  blob `8fd022bbf88a1dff2ef8d221dd34174f8285fd37`, SHA-256
  `34ae1f9dac248f8742e1fa69782cc2f5b8a9fae3c77bfedb26b4c4e478cbc537`,
  45868 bytes, 814 lines, 66 Markdown headings. Read it with
  `git show 340d5e9d:docs/architecture/agent-collaboration-protocol.md`.
- **Identifiers.** `LLstart-end` are WF-03L baseline line spans; `LPstart-end`
  are lines of the protocol after WF-03L; `LCstart-end` are lines of the
  [context](../architecture/agent-collaboration-context.md) companion;
  `LVstart-end` are lines of the
  [provider contracts](../architecture/agent-collaboration-provider-contracts.md)
  companion after WF-03L. Row IDs carry an `L-` prefix. No WF-03L identifier
  refers to a row or coordinate of WF-03A to WF-03K.
- **Dispositions and headings.** Retain means kept verbatim at the original
  path. Move here means that a section's body lines move verbatim while its
  heading, the blank line after the heading and the blank line after the body
  stay in the protocol; two new forwarder lines, a link to the destination
  fragment and the companion name, replace the body. All 66 baseline headings
  stay in the protocol in order with identical text and level, so every
  fragment resolves as before. Each destination section has a new `##`
  heading with the same text and level as its protocol heading, so each
  fragment is the same in the protocol and its companion. LC1-27 are the
  context companion's title, introduction and blank line; the headings LC28,
  LC47 and LC74 are each followed by a blank line, and the blank separators
  LC46 and LC73 are new. LV1-16 are the provider contracts companion's title,
  introduction and blank line: LV3-11 replace the five baseline introduction
  lines 3-7, and LV12-15 are its baseline lines 8-11, unchanged. LV17 is the
  new heading, LV18 and LV55 are new blank lines, and LV56-231 are its
  baseline lines 13-188, unchanged. The context companion does not end with a
  blank line.

### WF-03L ranges

The nine ranges cover LL1-814 without gaps or overlap.

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| L-R-01 | LL1-468 | Purpose through the context assembly heading and its blank line | Retain; one Purpose routing insertion | LP1-12 (LL1-12), LP13-19 (insertion), LP20-475 (LL13-468) | Verbatim apart from the insertion: one six-line routing paragraph and a blank line after the Purpose paragraphs (LL5-11) and their blank line (LL12), before `## Message envelope` |
| L-M-01 | LL469-484 | Context assembly body | Move | LC30-45 under heading LC28; forwarder LP476-477 | Body verbatim |
| L-R-02 | LL485-559 | Blank line after the context assembly body, the companion directory (LL486-556), its blank line, and the provider runtime configuration heading and its blank line | Retain; one directory framing insertion | LP478 (LL485), LP479-482 (insertion), LP483-556 (LL486-559) | Verbatim apart from the insertion: one three-line framing paragraph and a blank line immediately before the directory; the directory LL486-556 is LP483-553, byte-identical |
| L-M-02 | LL560-595 | Provider runtime configuration body: host runtime preflight | Move | LV19-54 under heading LV17; forwarder LP557-558 | Body verbatim |
| L-R-03 | LL596-776 | Blank line, the 35 subsection forwarders from the Claude Code critical-review CLI safety contract through project instruction context in Agent manifests, and the token efficiency heading and its blank line | Retain | LP559-739 | Verbatim |
| L-M-03 | LL777-800 | Token efficiency body | Move | LC49-72 under heading LC47; forwarder LP740-741 | Body verbatim |
| L-R-04 | LL801-803 | Blank line, the untrusted content heading and its blank line | Retain | LP742-744 | Verbatim |
| L-M-04 | LL804-808 | Untrusted content body | Move | LC76-80 under heading LC74; forwarder LP745-746 | Body verbatim |
| L-R-05 | LL809-814 | Blank line and protocol evolution | Retain | LP747-752 | Verbatim |

Each forwarder sits between its retained heading and blank line (LL467-468,
LL558-559, LL775-776, LL802-803; LP474-475, LP555-556, LP738-739, LP743-744)
and its retained closing blank line (LL485, LL596, LL801, LL809; LP478,
LP559, LP742, LP747). The protocol's inverse is exact: removing LP13-19 and
LP479-482 and replacing each forwarder's two lines with its destination body
restores the 45868-byte baseline.

### WF-03L fragments

| ID | Baseline fragment (protocol forwarder) | Companion fragment |
|---|---|---|
| L-M-01 | [`#context-assembly`](../architecture/agent-collaboration-protocol.md#context-assembly) | [`#context-assembly`](../architecture/agent-collaboration-context.md#context-assembly) |
| L-M-02 | [`#provider-runtime-configuration`](../architecture/agent-collaboration-protocol.md#provider-runtime-configuration) | [`#provider-runtime-configuration`](../architecture/agent-collaboration-provider-contracts.md#provider-runtime-configuration) |
| L-M-03 | [`#token-efficiency`](../architecture/agent-collaboration-protocol.md#token-efficiency) | [`#token-efficiency`](../architecture/agent-collaboration-context.md#token-efficiency) |
| L-M-04 | [`#untrusted-content`](../architecture/agent-collaboration-protocol.md#untrusted-content) | [`#untrusted-content`](../architecture/agent-collaboration-context.md#untrusted-content) |

### WF-03L link and reference changes

- **Moved text.** No moved line changed; there is no substitution. The four
  moved bodies have no link and no absolute `https` address.
- **Directional wording.** The moved bodies contain no "above", "below",
  "preceding", "following" or "next". In the preflight body "later" (LL582,
  LV41) is temporal, and "this discovery contract" (LL587, LV46) and "this
  narrower dispatch-time read" (LL595, LV54) refer to the section itself. In
  the provider contracts companion the preflight section precedes the CLI
  contracts, so the implementation contract's "above" references (LV144,
  LV147, LV167, LV201, LV206) still mean the preceding critical-review
  contract (LV56) and text within it, as the retained introduction sentence
  (LV13-15) says. No directional reference was broken by the relocation.
- **Quoted titles.** The moved text names other sections by quoted title
  without a link: "Project instruction context in Agent manifests" (LL472-473,
  LC33-34; LL805, LC77), "Bounded tracked-hunk evidence in Agent manifests"
  (LL477, LC38), "Bounded untracked-file previews in Agent manifests" (LL478,
  LC39; LL786, LC58) and "Provider token-usage contracts" (LL794, LC66). Each
  title is a retained protocol forwarding heading (LP733, LP708, LP703 and
  LP618), and the context introduction links each destination directly
  (LC17, LC18, LC20 and LC23) in the source evidence and usage evidence
  companions.
- **Retained wording.** The moved statements are as written at the baseline,
  and the relocation reconciles or endorses none of them, including the token
  usage implementation statement (LL791-796, LC63-68), the Increment 2 and
  Increment 4 discovery wording (LL566-567, LV25-26), the `Unknown`
  authentication and invocation readiness (LL579-584, LV38-43) and the one
  manual observation of a Codex desktop installation layout (LL586-595,
  LV45-54). Both affected introductions say so.
- **Retained references.** The companion directory (LL486-556, LP483-553) is
  unchanged; its "original headings below forward there" still names the
  retained subsection forwarders after it. The new framing paragraph
  (LP479-481) says the directory is not part of context assembly. The
  protocol's retained text has no link to the four fragments and does not
  name the four sections by quoted title. Earlier records' coordinates for
  these sections, such as WF-03K's KP558-595 and KP775-814, keep their
  statements as history.
- **Inbound references.** Before WF-03L, two tracked links reached any of the
  four baseline fragments: the provider contracts introduction (baseline line
  5, now replaced by LV5) and the invocation configuration companion's
  provider configuration observations (baseline line 570, now line 574), both
  to `#provider-runtime-configuration`. The second still reaches the retained
  heading LP555, whose forwarder LP557 links to LV17; that section's opening
  paragraph (baseline lines 528-542, now lines 532-546) keeps its WF-03J
  statement that the projection stays in the protocol, and the invocation
  configuration introduction (its lines 27-32) routes it to the provider
  contracts companion. The WF-03J record names that link and fragment only in
  code spans (lines 1096, 1132 and 1149). Two product comments name "Token
  efficiency" and "Context assembly" in the protocol by quoted title; they
  reach the retained headings and are unchanged.
- **Introductions.** The provider contracts introduction (LV3-11) now states
  its host runtime preflight ownership and keeps its explanation of the
  implementation contract's "above" references (LV13-15). The source evidence
  introduction (its lines 14-15 and 20-22) routes quoted titles through the
  context introduction and names the context and provider contracts
  companions; the invocation configuration introduction (its lines 27-32)
  routes runtime preflight. Their bodies are unchanged.
- **New links.** The protocol Purpose routing (LP14 and LP16) and the four
  forwarders (LP476, LP557, LP740 and LP745) link to the two companions. The
  context introduction links to the protocol, the provider contracts, source
  evidence and usage evidence companions and this map.
- **Index.** The [architecture index](../architecture/README.md) adds one
  entry, for the context companion, after the protocol entry, and updates the
  provider contracts entry for host runtime preflight.

### WF-03L not moved

The companion directory, run execution mode and isolation, explicit
abandonment, the message envelope, role-first authorization, the durable
ledger boundary, message types, reply semantics, default interaction, the
authority matrix, structured output failure, protocol evolution and every
other subject not moved by WF-03A to WF-03L stay in the protocol. No other
existing companion changes. WF-03K's not-moved paragraph named host runtime
preflight, context assembly, token efficiency and untrusted content because
they stayed in the protocol at that move; WF-03L moves their bodies. This is
a relocation, not a reconciliation: moved facts and wording are as written at
the baseline.
