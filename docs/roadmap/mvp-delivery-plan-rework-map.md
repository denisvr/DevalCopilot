# MVP delivery plan rework map

WF-02B mapping of the long-form [MVP delivery plan](mvp-delivery-plan.md) to
its concise replacement; a coverage record, not a ledger or a contract. It
never replaces a destination: where they differ, the destination and Git
prevail.

- **Baseline.** Commit `a59300972aa2519c1df5ed0cc4f8d63c238d0f26`:
  `docs/roadmap/mvp-delivery-plan.md` blob
  `e3e3dda700cba45ca1be0da800a893bce5d2582d` (512 lines, 34031 bytes). Read it
  with `git show a593009:docs/roadmap/mvp-delivery-plan.md`.
- **Identifiers.** `Lstart-end` are baseline line spans; `Rstart-end` are lines
  of the replacement. A section row (`S-`) spans its heading through the line
  before the next heading. An item row covers one removed or condensed
  paragraph or bullet: `I4-Pnn` the Increment 4 narrative paragraphs after its
  exit criteria, `I4-D14` the fourteenth Increment 4 deliverable, `I5-Nnn` the
  sentence groups of the one-paragraph Increment 5 narrative, one per ADR.
- **Dispositions.** Retain: kept verbatim. Condense: kept, shortened, in the
  replacement. Deduplicate: detail removed because the destination states it
  authoritatively; the item's status and open work stay in the replacement.
  Correct: replaced by a corrected statement (see corrections).
- **Destinations.** Paths are relative to this file; `ADR-NNNN` means that
  record under `../decisions/`; `protocol` is
  [agent-collaboration-protocol.md](../architecture/agent-collaboration-protocol.md)
  with its section fragment and baseline line span (the protocol is unchanged).
  Accepted ADRs are active contracts, not history.
- **Headings.** Every baseline heading is kept in order with identical text,
  so every fragment, including the repeated `outcome`, `deliverables` and
  `exit-criteria` series (`outcome` to `outcome-7`, `deliverables` to
  `deliverables-6`, `exit-criteria` to `exit-criteria-7`) and the inbound
  `increment-4-...` and `increment-5-...` fragments, resolves as before.

## Corrections

| ID | Baseline | Finding and evidence | Correction |
|---|---|---|---|
| CR-01 | I5-N01 | The owner's 2026-10-06 sequencing authorization was routed to the planner handoff, whose current state no longer records it; ADR-0029 states it (Context, L7) and bounds it under "Sequencing, without changing completion criteria". | Routed to that ADR-0029 section. |

## Sections

| ID | Lines | Subject | Disposition | Replacement | Evidence |
|---|---|---|---|---|---|
| S-TTL | L1-2 | Title | Retain | R1-2 | Verbatim |
| S-DS | L3-12 | Delivery strategy | Retain | R3-12 | Verbatim |
| S-I0 | L13-34 | Increment 0 outcome, deliverables, exit criteria | Retain | R13-34 | Verbatim |
| S-I1 | L35-89 | Increment 1 outcome, deliverables, exit criteria | Retain | R35-89 | Verbatim |
| S-I2 | L90-113 | Increment 2 outcome, deliverables, exit criteria | Retain | R90-113 | Verbatim |
| S-I3 | L114-138 | Increment 3 outcome, deliverables, exit criteria | Retain | R114-138 | Verbatim |
| S-I4-O | L139-145 | Increment 4 heading and outcome | Retain | R139-145 | Verbatim |
| S-I4-D | L146-169 | Increment 4 deliverables | Retain; I4-D14 condensed | R146-169 | Thirteen bullets verbatim |
| S-I4-X | L170-305 | Increment 4 exit criteria and control narratives | Retain criteria; condense narratives | R170-210 | Criteria L172-182 verbatim (R172-182); I4-P01 to P12 summarized in R184-209 |
| S-I5-N | L306-381 | Increment 5 heading and ADR narrative | Retain heading; condense narrative; CR-01 | R211-244 | I5-N01 to N21; heading verbatim |
| S-I5 | L382-418 | Increment 5 outcome, deliverables, exit criteria | Retain | R245-281 | Verbatim |
| S-I6 | L419-445 | Increment 6 outcome, deliverables, exit criteria | Retain | R282-308 | Verbatim |
| S-I7 | L446-474 | Increment 7 outcome, demonstration, exit criteria | Retain | R309-337 | Verbatim |
| S-CC | L475-489 | Cross-cutting completion checks | Retain | R338-352 | Verbatim |
| S-DR | L490-512 | Deferred roadmap | Retain | R353-375 | Verbatim, including the closing evidence-and-decision rule |

## Increment 4 items

Each row names the material bounds, exceptions and limitations; "R" marks
those the replacement keeps, every other one is stated by the destination.

| ID | Lines | Subject | Disposition | Replacement | Destination | Bounds and limitations |
|---|---|---|---|---|---|---|
| I4-D14 | L164-166 | Context-window deliverable | Condense | R164-166 | ADR-0023 | Inline ADR link replaced by its number; remaining context, live capacity and compaction open (R) |
| I4-P01 | L184-189 | Review-correction control | Deduplicate | R188-189 | [protocol](../architecture/agent-collaboration-protocol.md#review-correction) L738-752; ADR-0010 | Default two claims, escalation, one human-authorized claim (R); failed or interrupted claims consume; no pause/resume, fallback, Gemini or automatic orchestration |
| I4-P02 | L191-198 | Run-wide claim budget | Deduplicate | R190-191 | ADR-0012; protocol L754-765 | Default 16 and no human override (R); all six claiming paths; failed or interrupted claims consume; Simulated and Process exempt; hard stop |
| I4-P03 | L200-212 | Invocation-time budget | Deduplicate | R191-192 | ADR-0013 (Decision; non-goals L100-111); protocol L767-786 | Default 120 minutes, reserved not measured (R); alongside the count budget; configured timeout reserved; historical runs `NULL`; no account limit or lifetime guarantee |
| I4-P04 | L214-232 | Token-activity stop; Codex account-usage stop and warning | Deduplicate | R194-196, R204-207 | [protocol](../architecture/agent-collaboration-protocol.md#per-provider-run-token-activity-stop-at-agent-claim) L2153-2165; ADR-0025 with protocol L2260-2303; ADR-0026 with protocol L2304-2337 | Exit criterion met for Codex only; Claude account usage and remaining controls open (R); retrospective, unprovable-below refuses, claimed attempts unaffected, not an allowance; stop at claim and dispatch, not eligibility; warning advisory, explicit and never refusing |
| I4-P05 | L234-240 | Claude agentic-turn limit | Deduplicate | R197 | [protocol](../architecture/agent-collaboration-protocol.md#optional-claude-agentic-turn-limit-for-mutation-attempts) L1991-2093 | 1 through 100, immutable per attempt, `--max-turns`; not a measured count, budget, stop or eligibility claim |
| I4-P06 | L242-248 | Guidance for diagnosis-origin corrections | Deduplicate | R197-198 | ADR-0019, ADR-0015, ADR-0018; [protocol](../architecture/agent-collaboration-protocol.md#optional-direct-human-guidance-for-mutation-requests) L903-971 and L2744-2819 | Advisory (R); same normalization and bounds, one sealed snapshot, within the correction allowance; grants nothing; recorded text is not compliance |
| I4-P07 | L250-256 | Root instruction context | Deduplicate | R199, R204 | ADR-0021; [protocol](../architecture/agent-collaboration-protocol.md#project-instruction-context-in-agent-manifests) L3123-3184 | Resume and compaction open (R); only the two root files, Complete/Absent/Omitted, sealed replay unchanged, untrusted, no imports or provider discovery |
| I4-P08 | L258-265 | Physically proven untracked previews | Deduplicate | R199-200, R207-209 | ADR-0022; [protocol](../architecture/agent-collaboration-protocol.md#bounded-untracked-file-previews-in-agent-manifests) L2820-2883; [open risks](current-work.md#open-risks) | Raw Git observation can still read linked content, hardening open (R); final-path and one-link checks; omission beside delivered siblings |
| I4-P09 | L267-273 | Attested tracked-change text | Deduplicate | R199-200 | ADR-0024; [protocol](../architecture/agent-collaboration-protocol.md#attested-tracked-change-text-in-agent-manifests) L2962-3048 | HEAD blob versus proven handle, one-hunk comparison, omissions, sealed replay, raw observation and fingerprint unchanged |
| I4-P10 | L275-281 | Attested checkpoint comparison | Deduplicate | R199-200, R207-209 | ADR-0027 (L46-60); [protocol](../architecture/agent-collaboration-protocol.md#attested-checkpoint-comparison-for-human-inspection) L3049-3079 | Not every filesystem read (R, as raw Git observation); 512 KiB whole-file bound, per-path omissions, inert root files, partial or all-omitted shown truthfully |
| I4-P11 | L283-288 | Owner-chosen run budgets | Deduplicate | R192-193 | ADR-0028; protocol L788-797 | Smaller immutable values at manual intake (R); 1 through 16 and 1 through 120; consumption after failure; ceiling below a timeout refuses; existing runs and defaults unchanged |
| I4-P12 | L290-304 | Plan-challenge cap and escalation | Deduplicate | R201-202, R207-208 | ADR-0016, ADR-0020; [protocol](../architecture/agent-collaboration-protocol.md#optional-second-challenge-round-and-escalation) L2531-2654 and L2655-2743; [workflow model](../architecture/workflow-model.md#bounded-loops) L365-368 | Per-lineage cap (R); other loop exhaustion, such as CI rounds, later (R); one second review and resolution, atomic escalation, depth-two plan only by one authorization then a separate request; more conservative than per-issue wording |

## Increment 5 items

All rows except I5-N01 and I5-N21 are Deduplicate with their own ADR as the
destination; every ADR's index entry and qualifier are unchanged. The
replacement keeps their status (partial delivery, not completion) and groups
their open work in R238-243 under Increment 5 or 6.

| ID | Lines | ADR | Replacement | Bounds and limitations kept in the replacement (R) or the ADR |
|---|---|---|---|---|
| I5-N01 | L308-312 | ADR-0029 | R213-220 | Condense and CR-01: date, bounded slice, exact verified, reviewed and approved checkpoint, durable intent and restart recovery, delivered on `origin/main`, completes neither increment (R); narrow supersession of ADR-0009 ordering, no later slice, autonomy or publication (ADR-0029 Sequencing) |
| I5-N02 | L313-314 | ADR-0030 | R225-226 | Complete-set approval (R); several recipes |
| I5-N03 | L315-317 | ADR-0031 | R225-226 | Metadata-only, inactive manual run (R); cancels, repairs and deletes nothing |
| I5-N04 | L318-320 | ADR-0032 | R225-226 | Historical receipt (R); pinned rows, no persistence change, local delivery only |
| I5-N05 | L321-323 | ADR-0033 | R227-229 | Read-only (R); cursor-paged, locates receipts; full replay open (R) |
| I5-N06 | L324-326 | ADR-0034 | R227-229 | Headers only, no payload or current-state claim; not complete replay (R) |
| I5-N07 | L327-330 | ADR-0035 | R227-229 | Content only through the existing policy; full linked replay open (R) |
| I5-N08 | L331-334 | ADR-0036 | R227-229 | Closed None/Unavailable/Available, no chain traversal |
| I5-N09 | L335-338 | ADR-0037 | R227-229 | Minimal recorded header, closed status, no outcome or evidence claim |
| I5-N10 | L339-342 | ADR-0038 | R230-236 | Observation, not permission or publication authority (R); fixed privacy-filtered command, no credential or environment-token observation |
| I5-N11 | L343-346 | ADR-0039 | R230-236 | Local `origin` input only; no remote identity, binding or permission |
| I5-N12 | L347-350 | ADR-0040 | R230-236 | Immutable local scope record (R); verifies nothing remote |
| I5-N13 | L351-353 | ADR-0041 | R230-236 | Unpersisted (R); no branch, commit, permission or CI proof |
| I5-N14 | L354-356 | ADR-0042 | R230-236 | Point-in-time, unpersisted (R); no binding or CI result |
| I5-N15 | L357-358 | ADR-0043 | R230-236 | Identity binding record (R); historical evidence, not permission or publication |
| I5-N16 | L359-362 | ADR-0044 | R230-243 | Required-check policy and monitoring open (R); shown only under binding and exact-branch equality |
| I5-N17 | L363-366 | ADR-0045 | R230-243 | Enumeration and diagnosis open (R); at most 50 entries |
| I5-N18 | L367-370 | ADR-0047 | R230-243 | Logs, diagnosis, correction routing and rerun open (R); untrusted post-redaction text, never persisted or sent to an Agent |
| I5-N19 | L371-374 | ADR-0048 | R230-243 | Consent only; push, pull request, revocation, scheduling and reconciliation open (R) |
| I5-N20 | L375-378 | ADR-0049 | R230-243 | Reported access is not branch-push permission; rulesets and protection open (R); ADR-0048 neither required nor changed |
| I5-N21 | L379-380 | Open provider controls | R204-207, R242-243 | Retain: Claude account usage, resume, compaction and safe permission profiles open and unavailable while unproven; deliverables and exit criteria unchanged (R) |

R234-236 also keeps ADR-0050 as Proposed and authorizing nothing (decision
index and ADR-0050 status line).

## Evidence

- **Mechanical.** The WF-02B checker (`logs/executor/wf-02b/`, ignored)
  compares every `Retain` span byte for byte with the replacement, checks
  that the sections and items cover every baseline line once, resolves every
  destination path and fragment, and finds each configured bound phrase in the
  replacement or destination. Phrase matching is lexical: it shows each phrase
  is stated, not equivalence.
- **Semantic.** Each Deduplicate row's destination span was read before its
  detail was removed. That reading is executor evidence for independent
  review, not a finding of equivalence.
