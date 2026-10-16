# Agent collaboration protocol rework map

WF-03 mapping of the long-form
[agent collaboration protocol](../architecture/agent-collaboration-protocol.md)
to focused companion documents; a coverage record, not a ledger or a contract.
It never replaces a destination: where they differ, the destination and Git
prevail. WF-03A is the only move recorded here. Future WF-03 moves remain
unselected, and this map names no destination for them.

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

## Ranges

The five ranges cover L1-3224 without gaps or overlap.

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| R-01 | L1-524 | Purpose through provider runtime configuration, including host runtime preflight | Retain; one routing insertion | P1-485 (L1-485), P486-490 (insertion), P491-529 (L486-524) | Verbatim apart from the insertion: a four-line paragraph and a blank line immediately before `## Provider runtime configuration`, linking to the companion |
| M-01 | L525-609 | Claude Code critical-review CLI safety contract | Move | C12 heading, C13-95 body (L526-608); forwarder P530-533; P534 (L609) | Body verbatim |
| M-02 | L610-687 | Claude Code implementation CLI safety contract | Move | C97 heading, C98-173 body (L611-686); forwarder P535-538; P539 (L687) | Body verbatim except one link (L679, C166) |
| M-03 | L688-701 | Deferred Gemini CLI execution boundary | Move | C175 heading, C176-187 body (L689-700); forwarder P540-543; P544 (L701) | Body verbatim |
| R-02 | L702-3224 | Review correction through protocol evolution | Retain | P545-3067 | Verbatim |

## Fragments

| ID | Baseline fragment (protocol forwarder) | Companion fragment |
|---|---|---|
| M-01 | [`#claude-code-critical-review-cli-safety-contract`](../architecture/agent-collaboration-protocol.md#claude-code-critical-review-cli-safety-contract) | [`#claude-code-critical-review-cli-safety-contract`](../architecture/agent-collaboration-provider-contracts.md#claude-code-critical-review-cli-safety-contract) |
| M-02 | [`#claude-code-implementation-cli-safety-contract`](../architecture/agent-collaboration-protocol.md#claude-code-implementation-cli-safety-contract) | [`#claude-code-implementation-cli-safety-contract`](../architecture/agent-collaboration-provider-contracts.md#claude-code-implementation-cli-safety-contract) |
| M-03 | [`#deferred-gemini-cli-execution-boundary`](../architecture/agent-collaboration-protocol.md#deferred-gemini-cli-execution-boundary) | [`#deferred-gemini-cli-execution-boundary`](../architecture/agent-collaboration-provider-contracts.md#deferred-gemini-cli-execution-boundary) |

## Link and reference changes

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

## Not moved

Runtime preflight and every other provider-runtime subject stay in the
protocol, including review correction, human guidance, execution evidence,
budgets, token and account controls, model and effort controls, context,
manifests and history. This is a relocation, not a reconciliation: moved
provider facts, versions and disclosed inferences are as written at the
baseline.
