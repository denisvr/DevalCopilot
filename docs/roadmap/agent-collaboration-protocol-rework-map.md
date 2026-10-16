# Agent collaboration protocol rework map

WF-03 mapping of the long-form
[agent collaboration protocol](../architecture/agent-collaboration-protocol.md)
to focused companion documents; a coverage record, not a ledger or a contract.
It never replaces a destination: where they differ, the destination and Git
prevail. It holds six separately pinned records, WF-03A, WF-03B, WF-03C,
WF-03D, WF-03E and WF-03F; each record's identifiers and coordinates refer only
to its own baseline, and each earlier record's statements stay as written, as
history. WF-03 remains incomplete. Future WF-03 moves remain unselected, and
this map names no destination for them.

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
