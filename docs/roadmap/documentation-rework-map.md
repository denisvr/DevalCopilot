# Documentation rework map

WF-02A mapping of the long-form startup documents to their concise
replacements; a coverage record, not a ledger or a contract. It never replaces
a destination: where they differ, the destination and Git prevail.

- **Baselines.** Commit `d60d718ccf5265f80d26ea06004dc0ad20d359e9`:
  `docs/engineering-context.md` blob `d48f4a82671d9d6aca2e313c06adea34de746584`
  (649 lines, 77934 bytes) and `docs/roadmap/current-work.md` blob
  `429148e4e4b79b6281f5dc144a27c8c67598712b` (172 lines, 11122 bytes). Read
  them with `git show d60d718:<path>`.
- **Identifiers.** `Lstart-end` are baseline line spans. A section row (`S-`)
  spans its heading through the line before the next heading. An item is one
  top-level bullet with its indented continuation, or one paragraph, numbered
  in order within its section: `HDR` header, `PD` project decisions (`PD-nn`
  summarizes ADR-00nn), `PSA` product-specific architecture, `SEC`, `TST`,
  `PKG`, `DEV`; `CW` rows cover current-work (`CW-Rnn` are Open risks bullets).
- **Dispositions.** Retain: kept, verbatim or condensed, in the destination.
  Deduplicate: removed because the destination already states it
  authoritatively. Relocate: moved into the destination. Correct: replaced by
  a corrected statement (see corrections).
- **Destinations.** Paths are relative to this file; `ADR-NNNN` means that
  record under `docs/decisions/`. Accepted ADRs are active contracts, not
  history.
- **Evidence.** Destinations were inspected by targeted reads, and the WF-02A
  checker located every configured claim (the bounds, exceptions, restrictions
  and incomplete-status phrases of the item) in the destination text; claims
  whose wording differs were reworded only after the equivalent destination
  sentence was read. A token pass also found every code span, quoted label and
  unit bound of PSA-02 to PSA-20 in its ADR; 14 tokens in nine items differ
  only in spelling (such as `<constant>` placeholders), each read in the ADR.
  PSA-01's two unmatched tokens are the publication pointer CR-03 replaces. Claim
  matching is lexical: it shows each phrase is stated, not full equivalence.
  Class, route and cockpit names in those narratives stay authoritative in the
  ADRs and source.

## Corrections

| ID | Baseline | Finding and evidence | Correction |
|---|---|---|---|
| CR-01 | PD-01 | ADR-0001's status line is "Partially superseded by ADR-0006"; the summary omitted it and the decision index files the record under "Accepted decisions". | The context states the partial supersession. The index is outside WF-02A paths and is unchanged (planner decision). |
| CR-02 | PKG-01 | "Likely initial set ... security packages remain opt-in" predates adoption: `Directory.Packages.props` pins eight `Devalente.Shared.*` packages at 0.2.0, including `AspNetCore.Security` (referenced by the Api). | The actual set is recorded; auditing and soft deletion remain opt-in. |
| CR-03 | PSA-01 | ADR-0029 delivery evidence "in `docs/roadmap/current-work.md`" leaves that file on replacement; it is preserved in the history snapshot and Git history. | Status is routed to the context's implemented-status paragraph and the MVP plan; contract details to ADR-0029 and data-and-recovery. |
| CR-04 | CW-A | TEST-48-01 was recorded as unstaged and unpushed; it is published as `d60d718` (sole parent `f7e42c1`). | Recorded as the prior published delivery; detail stays pinned at `d60d718`. |
| CR-05 | CW-R05 | The `FilesystemArtifactStore` remark routes its non-Windows lexical-only containment gap to current-work risks, which named only Windows-specific proof. | The open risk names the non-Windows omission and lexical-only containment. |

Not corrected (outside WF-02A paths): the decision index lists ADR-0050 under
"Accepted decisions" although its entry and record say Proposed; links in the
frozen snapshots stay unchanged by rule.

## Engineering context sections

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| S-HDR | L1-16 | Title and stack header | Retain | [context](../engineering-context.md) | Header bullets HDR-01 to 11 kept verbatim |
| S-PD | L17-167 | Project decisions (ADR list) | Deduplicate | [context](../engineering-context.md#project-decisions); [index](../decisions/README.md#accepted-decisions) | PD-01 to 49; heading kept for routing |
| S-PSA | L168-602 | Product-specific architecture | Retain | [context](../engineering-context.md#product-specific-architecture) | Boundaries kept; PSA-01 corrected; PSA-02 to 20 and 31 to 42 deduplicated |
| S-SEC | L603-618 | Security tailoring | Retain | [context](../engineering-context.md#security-tailoring) | Verbatim |
| S-TST | L619-637 | Testing tailoring | Retain | [context](../engineering-context.md#testing-tailoring) | TST-02 condensed |
| S-PKG | L638-645 | Package adoption | Correct | [context](../engineering-context.md#package-adoption) | CR-02 |
| S-DEV | L646-649 | Approved deviations | Retain | [context](../engineering-context.md#approved-deviations) | Verbatim |

## Engineering context items

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| HDR-01 | L3-3 | Standards revision | Retain | [context](../engineering-context.md) | Verbatim |
| HDR-02 | L4-4 | Root namespace | Retain | [context](../engineering-context.md) | Verbatim |
| HDR-03 | L5-5 | Topology | Retain | [context](../engineering-context.md) | Verbatim |
| HDR-04 | L6-6 | Security profile | Retain | [context](../engineering-context.md) | Verbatim |
| HDR-05 | L7-7 | Backend | Retain | [context](../engineering-context.md) | Verbatim |
| HDR-06 | L8-8 | Frontend | Retain | [context](../engineering-context.md) | Verbatim |
| HDR-07 | L9-9 | Persistence | Retain | [context](../engineering-context.md) | Verbatim |
| HDR-08 | L10-10 | Generated clients | Retain | [context](../engineering-context.md) | Verbatim |
| HDR-09 | L11-11 | Live transport | Retain | [context](../engineering-context.md) | Verbatim |
| HDR-10 | L12-13 | Operating system | Retain | [context](../engineering-context.md) | Verbatim |
| HDR-11 | L14-15 | Deployment | Retain | [context](../engineering-context.md) | Verbatim |
| PD-01 | L19-20 | ADR-0001 summary | Correct | ADR-0001; [index](../decisions/README.md#accepted-decisions); [context](../engineering-context.md#project-decisions) | CR-01; index entry; ADR status |
| PD-02 | L21-22 | ADR-0002 summary | Deduplicate | ADR-0002; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-03 | L23-24 | ADR-0003 summary | Deduplicate | ADR-0003; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-04 | L25-26 | ADR-0004 summary | Deduplicate | ADR-0004; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-05 | L27-28 | ADR-0005 summary | Deduplicate | ADR-0005; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-06 | L29-30 | ADR-0006 summary | Deduplicate | ADR-0006; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-07 | L31-33 | ADR-0007 summary | Deduplicate | ADR-0007; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-08 | L34-35 | ADR-0008 summary | Deduplicate | ADR-0008; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-09 | L36-37 | ADR-0009 summary | Deduplicate | ADR-0009; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-10 | L38-39 | ADR-0010 summary | Deduplicate | ADR-0010; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-11 | L40-41 | ADR-0011 summary | Deduplicate | ADR-0011; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-12 | L42-43 | ADR-0012 summary | Deduplicate | ADR-0012; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-13 | L44-45 | ADR-0013 summary | Deduplicate | ADR-0013; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-14 | L46-47 | ADR-0014 summary | Deduplicate | ADR-0014; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-15 | L48-50 | ADR-0015 summary | Deduplicate | ADR-0015; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-16 | L51-54 | ADR-0016 summary | Deduplicate | ADR-0016; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-17 | L55-56 | ADR-0017 summary | Deduplicate | ADR-0017; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-18 | L57-58 | ADR-0018 summary | Deduplicate | ADR-0018; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-19 | L59-61 | ADR-0019 summary | Deduplicate | ADR-0019; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-20 | L62-64 | ADR-0020 summary | Deduplicate | ADR-0020; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-21 | L65-66 | ADR-0021 summary | Deduplicate | ADR-0021; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-22 | L67-69 | ADR-0022 summary | Deduplicate | ADR-0022; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-23 | L70-72 | ADR-0023 summary | Deduplicate | ADR-0023; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-24 | L73-75 | ADR-0024 summary | Deduplicate | ADR-0024; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-25 | L76-78 | ADR-0025 summary | Deduplicate | ADR-0025; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-26 | L79-81 | ADR-0026 summary | Deduplicate | ADR-0026; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-27 | L82-84 | ADR-0027 summary | Deduplicate | ADR-0027; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-28 | L85-88 | ADR-0028 summary | Deduplicate | ADR-0028; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-29 | L89-91 | ADR-0029 summary | Deduplicate | ADR-0029; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-30 | L92-94 | ADR-0030 summary | Deduplicate | ADR-0030; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-31 | L95-97 | ADR-0031 summary | Deduplicate | ADR-0031; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-32 | L98-100 | ADR-0032 summary | Deduplicate | ADR-0032; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-33 | L101-104 | ADR-0033 summary | Deduplicate | ADR-0033; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-34 | L105-108 | ADR-0034 summary | Deduplicate | ADR-0034; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-35 | L109-112 | ADR-0035 summary | Deduplicate | ADR-0035; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-36 | L113-116 | ADR-0036 summary | Deduplicate | ADR-0036; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-37 | L117-120 | ADR-0037 summary | Deduplicate | ADR-0037; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-38 | L121-123 | ADR-0038 summary | Deduplicate | ADR-0038; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-39 | L124-126 | ADR-0039 summary | Deduplicate | ADR-0039; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-40 | L127-130 | ADR-0040 summary | Deduplicate | ADR-0040; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-41 | L131-134 | ADR-0041 summary | Deduplicate | ADR-0041; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-42 | L135-138 | ADR-0042 summary | Deduplicate | ADR-0042; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-43 | L139-142 | ADR-0043 summary | Deduplicate | ADR-0043; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-44 | L143-146 | ADR-0044 summary | Deduplicate | ADR-0044; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-45 | L147-151 | ADR-0045 summary | Deduplicate | ADR-0045; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-46 | L152-154 | ADR-0046 summary | Deduplicate | ADR-0046; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-47 | L155-159 | ADR-0047 summary | Deduplicate | ADR-0047; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-48 | L160-163 | ADR-0048 summary | Deduplicate | ADR-0048; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PD-49 | L164-166 | ADR-0049 summary | Deduplicate | ADR-0049; [index](../decisions/README.md#accepted-decisions) | Index entry and qualifier; ADR status |
| PSA-01 | L170-175 | ADR-0029 narrative | Correct | ADR-0029; [data-and-recovery](../architecture/data-and-recovery.md#explicit-local-commit-recovery); [context](../engineering-context.md#product-specific-architecture) | CR-03 |
| PSA-02 | L176-184 | ADR-0030 narrative | Deduplicate | ADR-0030; [protocol](../architecture/agent-collaboration-protocol.md#complete-set-human-approval-for-local-delivery) | ADR decision; protocol section |
| PSA-03 | L185-198 | ADR-0031 narrative | Deduplicate | ADR-0031; [data-and-recovery](../architecture/data-and-recovery.md#explicit-abandonment-of-an-inactive-manual-run); [protocol](../architecture/agent-collaboration-protocol.md#explicit-abandonment-and-the-claim-paths) | ADR decision; recovery and claim sections |
| PSA-04 | L199-208 | ADR-0032 narrative | Deduplicate | ADR-0032; [protocol](../architecture/agent-collaboration-protocol.md#recorded-local-delivery-receipt) | ADR decision; protocol section |
| PSA-05 | L209-219 | ADR-0033 narrative | Deduplicate | ADR-0033 | ADR decision |
| PSA-06 | L220-235 | ADR-0034 narrative | Deduplicate | ADR-0034 | ADR decision; incomplete replay status |
| PSA-07 | L236-251 | ADR-0035 narrative | Deduplicate | ADR-0035 | ADR decision; incomplete replay status |
| PSA-08 | L252-266 | ADR-0036 narrative | Deduplicate | ADR-0036 | ADR decision; incomplete traversal status |
| PSA-09 | L267-285 | ADR-0037 narrative | Deduplicate | ADR-0037 | ADR decision; incomplete lineage status |
| PSA-10 | L286-298 | ADR-0038 narrative | Deduplicate | ADR-0038 | ADR decision; unverified selector status |
| PSA-11 | L299-315 | ADR-0039 narrative | Deduplicate | ADR-0039; [github-ci](../architecture/github-ci-integration.md#repository-local-origin-observation) | ADR decision; GitHub section |
| PSA-12 | L316-332 | ADR-0040 narrative | Deduplicate | ADR-0040; [github-ci](../architecture/github-ci-integration.md#recorded-observation-scope) | ADR decision; GitHub section |
| PSA-13 | L333-346 | ADR-0041 narrative | Deduplicate | ADR-0041; [github-ci](../architecture/github-ci-integration.md#repository-identity-observation) | ADR decision; GitHub section |
| PSA-14 | L347-360 | ADR-0042 narrative | Deduplicate | ADR-0042; [github-ci](../architecture/github-ci-integration.md#remote-branch-tip-observation) | ADR decision; GitHub section |
| PSA-15 | L361-372 | ADR-0043 narrative | Deduplicate | ADR-0043; [github-ci](../architecture/github-ci-integration.md#durable-repository-identity-binding) | ADR decision; GitHub section |
| PSA-16 | L373-387 | ADR-0044 narrative | Deduplicate | ADR-0044; [github-ci](../architecture/github-ci-integration.md#explicit-commit-check-observation) | ADR decision; GitHub section; unverified CLI status |
| PSA-17 | L388-406 | ADR-0045 narrative | Deduplicate | ADR-0045; [github-ci](../architecture/github-ci-integration.md#explicit-check-context-inspection) | ADR decision; GitHub section; unverified CLI status |
| PSA-18 | L407-424 | ADR-0047 narrative | Deduplicate | ADR-0047; [github-ci](../architecture/github-ci-integration.md#explicit-check-run-output-inspection) | ADR decision; GitHub section; unverified CLI status |
| PSA-19 | L425-442 | ADR-0048 narrative | Deduplicate | ADR-0048; [github-ci](../architecture/github-ci-integration.md#explicit-branch-publication-approval) | ADR decision; GitHub section |
| PSA-20 | L443-459 | ADR-0049 narrative | Deduplicate | ADR-0049; [github-ci](../architecture/github-ci-integration.md#explicit-repository-permission-observation) | ADR decision; GitHub section; unverified CLI status |
| PSA-21 | L461-462 | Host is the only authority | Retain | [context](../engineering-context.md#product-specific-architecture) | Verbatim |
| PSA-22 | L463-464 | Tauri ownership | Retain | [context](../engineering-context.md#product-specific-architecture) | Verbatim |
| PSA-23 | L465-466 | React ownership | Retain | [context](../engineering-context.md#product-specific-architecture) | Verbatim |
| PSA-24 | L467-469 | MVC and SignalR | Retain | [context](../engineering-context.md#product-specific-architecture) | Verbatim |
| PSA-25 | L470-472 | No external work in EF transactions | Retain | [context](../engineering-context.md#product-specific-architecture) | Verbatim |
| PSA-26 | L473-473 | Serialized SQLite ingestion | Retain | [context](../engineering-context.md#product-specific-architecture) | Verbatim |
| PSA-27 | L474-476 | Bounded scheduling | Retain | [context](../engineering-context.md#product-specific-architecture) | Verbatim |
| PSA-28 | L477-477 | Event order | Retain | [context](../engineering-context.md#product-specific-architecture) | Verbatim |
| PSA-29 | L478-479 | Hashed artifacts | Retain | [context](../engineering-context.md#product-specific-architecture) | Verbatim |
| PSA-30 | L480-482 | Progressive agent context | Retain | [context](../engineering-context.md#product-specific-architecture) | Verbatim |
| PSA-31 | L483-493 | ADR-0022 untracked previews | Deduplicate | ADR-0022; [protocol](../architecture/agent-collaboration-protocol.md#bounded-untracked-file-previews-in-agent-manifests); [context](../engineering-context.md#product-specific-architecture) | ADR; protocol; limitation kept |
| PSA-32 | L494-503 | ADR-0021 instruction context | Deduplicate | ADR-0021; [protocol](../architecture/agent-collaboration-protocol.md#project-instruction-context-in-agent-manifests) | ADR; protocol |
| PSA-33 | L504-518 | ADR-0024 tracked evidence | Deduplicate | ADR-0024; [protocol](../architecture/agent-collaboration-protocol.md#bounded-tracked-hunk-evidence-in-agent-manifests); [protocol](../architecture/agent-collaboration-protocol.md#attested-tracked-change-text-in-agent-manifests) | ADR; two protocol sections |
| PSA-34 | L519-524 | Claude agentic-turn limit | Deduplicate | [protocol](../architecture/agent-collaboration-protocol.md#optional-claude-agentic-turn-limit-for-mutation-attempts) | Protocol section |
| PSA-35 | L525-530 | Direct human guidance | Deduplicate | ADR-0015; ADR-0019; [protocol](../architecture/agent-collaboration-protocol.md#optional-direct-human-guidance-for-mutation-requests) | ADRs; protocol |
| PSA-36 | L531-540 | Escalated-plan authorization | Deduplicate | ADR-0016; ADR-0020; [protocol](../architecture/agent-collaboration-protocol.md#explicit-human-authorization-of-one-escalated-plan-implementation) | ADRs; protocol |
| PSA-37 | L541-545 | ADR-0017 review target | Deduplicate | ADR-0017; [workflow-model](../architecture/workflow-model.md#current-agent-implementation-boundary) | ADR; workflow section |
| PSA-38 | L546-552 | Verification failure diagnosis | Deduplicate | ADR-0018; ADR-0019; [protocol](../architecture/agent-collaboration-protocol.md#explicit-verification-failure-diagnosis) | ADRs; protocol |
| PSA-39 | L553-570 | Token and account-usage controls | Deduplicate | ADR-0025; ADR-0026; [protocol](../architecture/agent-collaboration-protocol.md#provider-token-usage-contracts); [protocol](../architecture/agent-collaboration-protocol.md#per-provider-run-token-activity-stop-at-agent-claim); [open risks](current-work.md#open-risks); [context](../engineering-context.md#product-specific-architecture) | ADRs; protocol; open risks |
| PSA-40 | L571-580 | ADR-0027 checkpoint comparison | Deduplicate | ADR-0027; [protocol](../architecture/agent-collaboration-protocol.md#attested-checkpoint-comparison-for-human-inspection) | ADR; protocol |
| PSA-41 | L581-587 | ADR-0028 run budgets | Deduplicate | ADR-0028 | ADR decision |
| PSA-42 | L588-597 | ADR-0023 model limits | Deduplicate | ADR-0023; [protocol](../architecture/agent-collaboration-protocol.md#claude-reported-model-context-limits) | ADR; protocol |
| PSA-43 | L598-599 | Replaceable adapters | Retain | [context](../engineering-context.md#product-specific-architecture) | Verbatim |
| PSA-44 | L600-601 | Local CLI authentication | Retain | [context](../engineering-context.md#product-specific-architecture) | Verbatim |
| SEC-01 | L605-608 | S2 rationale | Retain | [context](../engineering-context.md#security-tailoring) | Verbatim |
| SEC-02 | L610-610 | Loopback binding | Retain | [context](../engineering-context.md#security-tailoring) | Verbatim |
| SEC-03 | L611-611 | Per-launch secret | Retain | [context](../engineering-context.md#security-tailoring) | Verbatim |
| SEC-04 | L612-612 | No browser credentials | Retain | [context](../engineering-context.md#security-tailoring) | Verbatim |
| SEC-05 | L613-613 | No generic shell | Retain | [context](../engineering-context.md#security-tailoring) | Verbatim |
| SEC-06 | L614-614 | Approved roots | Retain | [context](../engineering-context.md#security-tailoring) | Verbatim |
| SEC-07 | L615-615 | Untrusted content | Retain | [context](../engineering-context.md#security-tailoring) | Verbatim |
| SEC-08 | L616-617 | Risk policies | Retain | [context](../engineering-context.md#security-tailoring) | Verbatim |
| TST-01 | L621-622 | File-backed SQLite fidelity | Retain | [context](../engineering-context.md#testing-tailoring) | Verbatim |
| TST-02 | L623-630 | MVC tests; ADR-0046/0047 snapshot | Retain | [context](../engineering-context.md#testing-tailoring) | Condensed; validation-policy fragment |
| TST-03 | L631-633 | Deterministic test doubles | Retain | [context](../engineering-context.md#testing-tailoring) | Verbatim |
| TST-04 | L634-634 | Playwright | Retain | [context](../engineering-context.md#testing-tailoring) | Verbatim |
| TST-05 | L635-636 | Containers only when required | Retain | [context](../engineering-context.md#testing-tailoring) | Verbatim |
| PKG-01 | L640-644 | Package adoption | Correct | [context](../engineering-context.md#package-adoption) | CR-02 |
| DEV-01 | L648-649 | Approved deviations | Retain | [context](../engineering-context.md#approved-deviations) | Verbatim |

## Current work

| ID | Lines | Subject | Disposition | Destination | Evidence |
|---|---|---|---|---|---|
| CW-I | L3-6 | Checkpoint purpose and Git precedence | Retain | [current-work](current-work.md) | Verbatim |
| CW-A | L8-66 | TEST-48-01 active-slice record | Correct | [current-work](current-work.md#prior-published-delivery); [handoff at a593009](https://github.com/denisvr/DevalCopilot/blob/a59300972aa2519c1df5ed0cc4f8d63c238d0f26/docs/roadmap/planner-handoff.md#verified-transition-baseline) | CR-04; detail pinned at d60d718 |
| CW-P | L68-117 | Earlier published deliveries | Deduplicate | [current-work](current-work.md#prior-published-delivery); [handoff](planner-handoff.md#standing-decision-constraints) | Pinned at d60d718; constraints in handoff |
| CW-H | L119-125 | History snapshot | Retain | [current-work](current-work.md#history) | Frozen link kept |
| CW-R01 | L129-137 | Account usage | Retain | [current-work](current-work.md#open-risks) | Condensed; past-correction sentence pinned at d60d718 |
| CW-R02 | L138-139 | Provider session and context controls | Retain | [current-work](current-work.md#open-risks) | Increment 4 fragment added |
| CW-R03 | L140-144 | Claim/time budgets; Gemini | Retain | [current-work](current-work.md#open-risks) | Condensed |
| CW-R04 | L145-145 | Raw linked artifacts | Retain | [current-work](current-work.md#open-risks) | Verbatim |
| CW-R05 | L146-148 | Raw checkpoint Git reads | Correct | [current-work](current-work.md#open-risks) | CR-05 non-Windows limit |
| CW-R06 | L149-156 | Unexplained failures | Retain | [current-work](current-work.md#open-risks) | Condensed |
| CW-R07 | L157-170 | P9 evidence limits | Retain | [current-work](current-work.md#open-risks) | Condensed; section fragments added |
| CW-R08 | L171-172 | Decision-constraint routing | Retain | [current-work](current-work.md#open-risks) | Verbatim |

## Verification

The ignored WF-02A checker (`logs/executor/wf-02a/wf02a-check.mjs` with
`map-config.mjs`) re-derives every identifier and span above from the two
baseline blobs, requires exactly one row per section and item with the
configured disposition, resolves every destination path and fragment, confirms
every Verbatim row word for word, locates
each claim, checks each `PD` row's index entry and ADR status line, and checks
links, inbound fragments, strict UTF-8, sizes, frozen-snapshot identities and
a bounded secret-pattern scan. Results are under
`logs/executor/wf-02a/results/`; the delivery record in
[current-work.md](current-work.md) reports the run.
