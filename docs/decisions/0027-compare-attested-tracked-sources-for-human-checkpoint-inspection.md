# ADR-0027: Compare attested tracked sources for human checkpoint inspection

Status: Accepted

## Context

[ADR-0024](0024-deliver-new-tracked-change-text-only-from-attested-snapshots.md) closed the route by which Git's raw working-path
patch (`git diff HEAD`) delivered the changed text of a file that another name reaches into new Agent manifests, and said explicitly
that the ordinary checkpoint diff query was left unchanged. That query is the protected route a human uses to inspect a checkpoint
before reviewing or approving it. It still captured the ordinary evidence and returned its raw patch as `CompleteDiff`, and the
inspection panel rendered it. A tracked file replaced by a hard link to a file outside the owned worktree therefore put the outside
file's changed text into the response and the rendered page; a disposable real-Git reproduction of the mechanism showed the outside
text in `git diff HEAD`. The response and hook also called the text "complete" although the host had never proven it so.

The raw observation (`status`, the full `diff`, the per-path `hash-object` of untracked files) is still what the checkpoint
fingerprint is computed from, so it cannot be replaced without changing fingerprints, serialization and sealed history.

## Decision

**An explicit inspection capture.** `IGitWorkspaceEvidenceReader.CaptureForCheckpointInspectionAsync` is the only capture the
checkpoint inspection query asks for. It is the same coherent capture bracket and the same fingerprint as every other capture, with
the ADR-0024 attestation of tracked changed paths (held-handle physical proof, exact captured-HEAD blob, repeated observation and the
immutable `GitWorkspaceTrackedFile` facts and bounds), and it returns neither the raw patch (`CompleteDiff` is null), nor instruction
context, nor untracked previews. An implementation without attestation support returns the plain capture with the patch removed and
no facts, which the query reports as `not_attested` omissions and never as a patch. `CaptureAsync`, checkpoint persistence and
serialization, the fingerprint bytes, the raw internal observation and every sealed artifact are unchanged.

**One protected query, unchanged guards.** The existing protected `GET` and its single mediator query keep project and checkpoint
membership, a Ready workspace, an active lease, a fresh coherent capture and exact fingerprint agreement. A stale or changing capture
remains a safe refusal. Inspection persists no source text and creates no event, checkpoint, attempt or artifact.

**Shared proof and comparison, separate delivery policies.** The responsibilities that must evolve together move under Projects
ownership: the admission of attested facts and their re-derivation (`AttestedTrackedComparison`) and the deterministic host comparison
(`TrackedComparison`, unchanged algorithm). The Agent manifest fitting, the two reserved instruction names' controlled section and the
manifest reduction ladder stay in Runs; the inspection response fitting stays in the query. Every consumer states a
`TrackedSourcePurpose` (no default): `AgentDelivery` reserves `AGENTS.md` and `CLAUDE.md` to their controlled instruction section, so
their tracked text is neither read nor delivered as before; `HumanInspection` treats a physically proven tracked root instruction file
as inert displayed source text, imports nothing from it and grants no authority. An unsafe root file is an omission like any other.
Capture reads no instruction file merely to inspect a checkpoint.

**Replacing the misleading contract.** The response carries the comparison text, `isComplete`, tracked and compared path counts, the
fixed host-comparison limitation and a list of tracked paths without text, each with a fixed reason, in ordinal path order. Every
tracked changed path is in the text or in the list exactly once; untracked files stay in the unchanged changed-files list and are
never fabricated as tracked comparisons. `isComplete` means no tracked path was omitted: it describes displayed tracked-content
coverage only, not Git metadata, modes, renames, review applicability or approval. A capture with no tracked change is complete with
empty text; an all-omitted capture is not. Omission details are fixed reason codes: no exception, arbitrary Git or provider message,
outside bytes or filesystem path.

**A bounded response.** The comparison text is at most 512 KiB of UTF-8, in addition to the existing 128 changed paths, 256 KiB and
8192 lines per source and 512 KiB retained per observation. Whole file blocks are fitted in ordinal order; a block that does not fit
is omitted whole with the fixed reason `comparison_limit` (a later, smaller block may still fit) and is never cut into apparently
complete text. There is no sampling, paging, optimizer or larger bound.

**The interface.** The generated client changes with the contract. The inspection hook keeps ownership by project and checkpoint and
the ordering of overlapping reads; the panel states a partial or all-omitted result truthfully, lists the omitted paths and the
limitation, renders names and text as plain text, and shows "No tracked diff." only for a complete capture with no tracked change.

## What this does not decide

This closes the human-inspection route that delivers the text of a tracked file through HTTP and the UI. It does not harden the raw
Git observation behind the fingerprint (`status`, `diff`, per-path hashing), which reads named paths and can still read the content of
a file another name links to; it changes no fingerprint, checkpoint persistence, historical artifact, Agent manifest or replay,
provider argument, budget, account control, claim, authorization, review applicability, lifecycle, lease or supervisor routing. The
host comparison is not Git's minimal or filter-normalized patch and does not compare modes or renames. Files and link topology can
change after observation. Windows is the only host with the physical proof; elsewhere every tracked path is an omission. Alias
enumeration, a deeper parent chain for deletions, a minimal-diff optimizer and a generic file reader remain unselected.

## Consequences

An inspected checkpoint shows the host comparison of every tracked file that can be proven and an explicit reason for every one that
cannot, so the unsafe path is visible rather than silently quoted or hidden, and the healthy siblings stay inspectable. The costs are
false refusals (a legitimately multiply linked tracked file is omitted), a more verbose one-hunk comparison where a file has scattered
edits, and the loss of Git's own patch in this view. A human who needs the raw patch must obtain it outside this application.
