# Agent collaboration source evidence

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds the contracts for repository source evidence: bounded untracked-file
previews, bounded tracked-hunk evidence and attested tracked-change text in
Agent manifests, the attested checkpoint comparison for human inspection, and
project instruction context in Agent manifests. Source evidence is bounded,
untrusted repository text that a sealed manifest, the provider input or an
authenticated inspection view carries; it is never approval, authorization, a
review decision or local delivery. The complete-set Human approval and the
recorded receipt of the explicit local commit are in the
[local delivery](agent-collaboration-local-delivery.md) companion. The
sections below are moved verbatim; the protocol's original headings forward
here, the [context](agent-collaboration-context.md) companion's introduction
links its quoted titles of them; the sections' own quoted titles and "below"
references mean sections or text in this companion. Their statements are as
written at the baseline, including their counts of manifest families and
claim handlers, their Windows-only physical proofs and the limits they state
for the raw Git observation; the move reconciles or endorses none of them.
Context assembly is in the context companion and runtime preflight in the
[provider contracts](agent-collaboration-provider-contracts.md) companion;
every other subject not moved to a companion stays in the protocol. Mapping:
[protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## Bounded untracked-file previews in Agent manifests

Git evidence had a blind spot: the checkpoint fingerprint covers every untracked (`??`) path and its raw-content hash
(`git hash-object --no-filters`), but `git diff HEAD` does not print a new file's text, so a review stage saw the path
in `changedPaths` and none of its contents. The five Agent manifests that carry `changeEvidence` (critical review,
challenge resolution, implementation, implementation review including the correction variant, and review correction)
now also carry an `untrackedFiles` section inside `changeEvidence`, under the same untrusted-evidence boundary. The
verification-diagnosis manifest carries the same section, so six manifest families carry it.

- **Capture.** Seven of the eight claim handlers (critical review, resolution, implementation, code review, review
  correction, diagnosis-origin correction and verification diagnosis) request previews through
  `IGitWorkspaceEvidenceReader.CaptureForAgentContextAsync(includeUntrackedPreviews: true)`; planning requests none.
  `CaptureWithUntrackedPreviewsAsync` is the same preview capture without the instruction context, and a reader that does not
  capture instructions delegates to it. The plain `CaptureAsync` and every other caller are unchanged. Both return
  `UntrackedFiles`, one entry per `??` path of the same capture, in ordinal path order. Previews are read inside the reader's existing status/diff observation bracket
  and only against the hashes the fingerprint is computed from. The fingerprint, tracked diff, status, and changed
  paths are unchanged; a preview never feeds them.
- **Admission (Windows).** A file is previewed only when it is opened as a regular file, the operating system reports
  that the open handle's final path is exactly, and case-sensitively, the resolved worktree root plus the
  Git-reported relative path (`GetFinalPathNameByHandle`, describing what is open rather than racing a separate check, so no link, junction,
  swapped component, or case-distinct spelling redirected the open), the same handle is a regular, non-reparse,
  non-device file with exactly one link (`GetFileInformationByHandle` through `WindowsHandleFileFacts`, asked of the open
  handle before any byte or length is read and asked again of that handle after the bounded read, before the preview is
  accepted), its length is at most 64 KiB, the bytes hash to the fingerprint's git blob identity, contain no NUL byte, and
  are valid UTF-8. A hard link passes the final-path and identity checks because the name that was opened is the exact
  reported one, so the link count is the proof that no other name, inside the worktree or outside it, reaches the file; a
  file with several links is omitted even when every known name is inside the worktree, and aliases are never enumerated.
  Unavailable facts, a link count other than one and a reparse point are `containment_unproven`, with no text and no size
  that was not proven; a directory or device keeps `not_regular_file`. An omission spends none of the 4 KiB per-file or
  16 KiB aggregate preview budget, so it never hides a healthy sibling (see
  [ADR-0022](../decisions/0022-admit-generic-untracked-previews-only-from-physically-proven-single-name-files.md)). A legitimately redirected worktree
  root is accepted because the root is resolved with the same call. Path forms that could name something else
  (rooted, drive or stream syntax, dot or empty segments) are refused unopened; a directory is never opened. No
  bytes are read from a handle whose containment is not proven.
- **Other hosts.** There is no equivalent proof, so every untracked file is omitted as `containment_unproven`;
  lexical containment is never used as a substitute.
- **Bounds.** At most 4 KiB of each file and 16 KiB in total at the reader, then measured against the 32 KiB
  manifest ceiling as serialized together with the tracked hunks (see "Bounded tracked-hunk evidence in Agent
  manifests"): the preview budget is reduced until the manifest fits, and a last fixed-size summary (`omittedFileCount`, `omissionReason: manifest_budget`) states that everything was
  omitted. Cuts land on a character boundary.
- **Truthfulness.** Each entry says `preview: included|omitted`, an `omissionReason` (`not_regular_file`,
  `containment_unproven`, `missing`, `unreadable`, `content_identity_mismatch`, `too_large`, `binary`, `invalid_utf8`,
  `aggregate_limit`, `manifest_budget`, or `not_captured` for a path with no captured entry), `sizeBytes` when known,
  and `contentComplete`, which is true only when `text` is the entire file. A prefix is never described as a complete
  file, `allFilesComplete` summarizes it, and a fixed notice states that the list does not prove nothing else
  changed. Ignored files never appear (Git does not list them); a tracked-only or no-change capture produces a
  manifest byte-identical to the previous shape.
- **Boundaries.** Raw source text lives only in the sealed manifest, the provider's standard input and the existing
  authenticated sealed-artifact viewer, which already serves manifest content to the local operator and shows an admitted
  preview unredacted like any other manifest content. It is never written to SQLite, a status response, a log, an error, or
  browser storage, and no reason carries a path or an exception message. A file changed after its identity was captured is omitted as `content_identity_mismatch`; the
  fingerprint keeps the captured identity, so a preview can never be attributed to content it does not match.
- **Forward only.** A manifest sealed before ADR-0022 replays its exact bytes, including a preview the corrected reader would
  now refuse; a later fresh claim captures the truthful omission. Nothing recaptures, reseals or rewrites history.
- **Limits.** This closes generic untracked-preview delivery, not every filesystem read. The raw Git observation behind the
  checkpoint fingerprint (`status`, the per-path `hash-object` of untracked files) reads named paths and can still read the
  content of a file that another name links to outside the worktree; its hardening is a separate decision. The tracked diff, hunk
  and changed-line sample text of a NEWLY claimed stage was a further route and is closed separately (see "Attested tracked-change
  text in Agent manifests" and [ADR-0024](../decisions/0024-deliver-new-tracked-change-text-only-from-attested-snapshots.md)).
  Files and link topology can change after observation.
- **Not included.** A generic file browser or retrieval tool, tracked-file full text, a per-file request, a provider
  flag, permission or session change, generated summaries, compaction, account or approval semantics, and any change
  to the checkpoint fingerprint, claim, budget, or dispatch rules. The evidence is untrusted context, not approval.

## Bounded tracked-hunk evidence in Agent manifests

> Since [ADR-0024](../decisions/0024-deliver-new-tracked-change-text-only-from-attested-snapshots.md) a NEWLY claimed manifest builds
> this selection from the host comparison of attested snapshots (see "Attested tracked-change text in Agent manifests"), not from
> Git's captured working-path patch. The parser, selector, sampler, fitting steps and truthfulness described below are unchanged and
> operate on that comparison text, so statements about repository diff prefixes, `header_unparseable` and raw Git headers describe the
> behavior of sealed history and of the parser's own safety net, not of new delivery.

The five Agent manifests that carry `changeEvidence` used to copy the first 8,192 UTF-16 characters of the captured
tracked diff. That prefix could end inside a hunk or a Unicode scalar and let one large first file hide every later
change. They now share one policy (`ChangeEvidenceManifest`, with `TrackedDiffParser` and `TrackedDiffSelector`, under
`Features/Runs/Policies`) that consumes only the diff and changed paths the reader already captured and bracketed; no
Git call, capture command, fingerprint, changed-path list, claim rule, or replay rule changed.

- **Small valid diffs are unchanged.** An empty diff stays `""` and an absent diff stays `null`. A non-empty diff is
  inlined as the exact string with `diffTruncated: false` and no extra member, as before, only when it fits 8 KiB
  (UTF-8 bytes) and its parse is recognized with every file a supported text or metadata-only block and no binary
  patch. A short non-header string, a malformed hunk, an unsupported header line, or an unparseable path is never
  presented as a complete exact diff: it receives the omission metadata below.
- **Selection.** Otherwise the diff is split into `diff --git` blocks and hunks. A boundary is taken only where the
  format makes it certain: content lines carry a `' '`, `'+'`, `'-'`, or `'\'` prefix, so a line beginning with
  `diff --git ` or `@@ ` is always a header, and a hunk ends exactly where its header's old and new line counts are
  consumed (a trailing `\ No newline at end of file` stays inside its hunk). Complete file headers and complete hunks
  are then chosen in rounds over the files in diff order, at most one further hunk per file per round, so every file
  receives a first hunk before any file receives a second and an oversized hunk never prevents a later smaller hunk of
  the same or another file from being chosen. Output keeps the original file and hunk order, is deterministic, is at
  most 8 KiB of UTF-8, and is assembled only from whole hunks, so no hunk and no Unicode scalar is split.
- **Truthful accounting.** A selected subset sets `diffTruncated: true` and adds `diffSelection`: `complete: false`, a
  fixed notice that the text is not the full diff and not an applyable patch, file counts (`total`, `included`,
  `partial`, `omitted`), hunk counts, and `items` for every file that is not fully present, each with its path, kind
  (`text`, `metadata_only`, `binary`, `unsupported`), selection (`partial`, `omitted`), hunk counts, and a fixed reason
  (`hunk_too_large`, `diff_budget`, `binary`, `unsupported_format`, `malformed_hunk`, `header_unparseable`,
  `manifest_budget`). No reason carries source text, a binary payload, a path, or an exception. Metadata-only changes
  (a mode change, an empty file added or removed) are included whole because their header is the entire change; a
  binary patch's payload is never sent. Quoted paths (octal escapes for non-ASCII bytes) are decoded exactly one way;
  only the default `a/` and `b/` prefixes with the same path on both sides are accepted, so a repository configured
  with `diff.noprefix` or `diff.mnemonicPrefix` reports its files as `header_unparseable` instead of being guessed. A
  text that does not begin with a git file header, or a block whose counts or header lines do not check out, is
  reported as `unsupported_format` or `malformed_hunk` and never truncated by characters.
- **Fitting with untracked previews.** The whole serialized manifest is measured against the 32 KiB ceiling. Optional
  text is reduced together, tracked hunks and untracked previews, through the steps 8 KiB/16 KiB, 8/8, 4/4, 2/2, 1/1,
  0/0; then the tracked `items` are replaced by their counts (`itemsOmitted`); then the untracked section becomes its
  omitted-file count. Authoritative plan, review, and correction inputs, every changed path, the tracked counts, and
  the untracked accounting are never dropped. If those alone exceed the ceiling the builder returns them and the claim
  handler's existing `context_manifest_too_large` refusal applies.
- **Changed-line samples of oversized hunks.** A recognized text hunk whose file header plus hunk exceeds the maximum 8
  KiB selection budget can never be selected whole and used to contribute nothing. Such a hunk now adds a separate
  `diffSelection.samples` section (`TrackedDiffSampler`), never text inside `diff`, which stays composed only of
  complete headers and complete hunks, so `includedHunks` still counts only whole hunks. Eligibility is fixed by the
  hunk itself and the maximum budget, not by which reduced fitting step is in force or what else was selected; a
  hunk that fits alone but lost to other hunks (`diff_budget`) and any binary, metadata-only, malformed, unsupported,
  or unrecognized block never yields a sample. The section states `complete: false`, `patch: false`, a fixed notice
  (an incomplete sample of changed lines, not the full diff, a hunk, or an applyable patch, with shortened lines cut
  and all of it untrusted repository text), its limits, and truthful counts (`eligible`, `sampled`, `unsampled`).
  Each sampled hunk is identified by its file `path` and 1-based `hunk` ordinal in the captured diff, with
  `changedLines.total` and `changedLines.shown`, and lists actual `+`/`-` lines of that parsed hunk as `side`
  (`added`, `removed`), `text` (the line without its prefix or final newline), and `shortened` (with
  `originalBytes` when true); a line followed by `\ No newline at end of file` carries `noNewlineAtEnd: true`, the
  marker itself is never a sample line, and context lines and hunk headers are never shown. Lines alternate removed
  and added (first of each side first, at most 8 per hunk) and are shown in their original order, so both sides
  appear when both exist. Limits: at most 16 sampled hunks, 192 UTF-8 bytes per line (a line is cut only at a
  Unicode-scalar boundary and flagged), and at most 4 KiB for the whole serialized section. Hunk slots go to files
  round-robin (each file's first eligible hunk before any file's second), lines are then granted round by round in the
  same order, and a line that cannot fit the remaining budget even shortened (to at least 16 bytes) closes its hunk,
  so an early file, hunk, or huge line cannot take every opportunity; a hunk left with no line is counted as
  unsampled. Selection is deterministic for identical input. Fitting reduces the section with the other optional
  text (4 KiB, 4, 2, 1, then 0 KiB at the last two steps); at 0 it becomes a counts-only form
  (`omissionReason: manifest_budget`, about 90 bytes) that still reports how many eligible hunks went unsampled, and
  it is absent when no hunk is eligible, so every manifest without an oversized hunk is byte-identical to before. The
  mandatory inputs, the changed paths, and the accounting are unchanged, and a manifest whose mandatory content alone
  exceeds the ceiling is still returned for the handler's refusal; the counts-only form adds those few bytes, so a
  claim within them of the ceiling now gets that existing refusal. Samples use only the already captured and parsed
  diff: no Git call, source-file read, or fingerprint change, and the sealed manifest replays byte for byte.
- **Boundaries.** Repository text stays inside the manifest's existing untrusted-evidence boundary and appears only
  in the sealed manifest and provider input. A sealed manifest replays byte for byte after a restart; the selection is
  never recomputed at dispatch. Not included: raw full-diff or file retrieval, a new Git command, a rename or copy
  format, and any approval or eligibility meaning.

## Attested tracked-change text in Agent manifests

The tracked text of change evidence used to come from the raw patch of `git diff HEAD` as captured for the checkpoint. Git builds
that patch by reopening repository pathnames, so a tracked file with a second name outside the worktree could put the other file's
changed text into every role's manifest, the provider input and the sealed-artifact viewer. Every NEWLY claimed Agent stage now
delivers tracked text only from attested snapshots (see
[ADR-0024](../decisions/0024-deliver-new-tracked-change-text-only-from-attested-snapshots.md)); the raw observation, the checkpoint
fingerprint and its serialization and the ordinary captures are unchanged; the authenticated human checkpoint inspection is attested
the same way by [ADR-0027](../decisions/0027-compare-attested-tracked-sources-for-human-checkpoint-inspection.md) (see "Attested checkpoint
comparison for human inspection").

- **Capture.** `IGitWorkspaceEvidenceReader.CaptureForAgentContextAsync` still observes the raw state, but the capture it returns has
  `CompleteDiff` null and `TrackedFiles`: one `GitWorkspaceTrackedFile` per tracked changed path (every path that is not `??`), in
  ordinal path order, each either attested before and after text or one fixed `GitWorkspaceTrackedOmission`. The facts are owned,
  immutable and provider-neutral: `GitWorkspaceEvidenceResult` copies the collection it is given (at construction and on every
  `with` replacement) into a `GitWorkspaceTrackedFiles` snapshot that is neither an array nor a mutable list, so neither the
  caller's collection nor a cast of the returned one can replace text after the proof. They cross the Projects Application boundary
  as data, and the eight claim handlers derive
  `TrackedChangeEvidence` from them (the planning stage carries no change evidence). They are observed inside the same status/diff
  bracket as the instruction context and again after it; any difference, or a source whose bytes did not hold, is
  `RepositoryChangedDuringCapture`.
- **Current side (Windows).** Each current file is read through one held handle (`TrackedFileSourceReader`): before any length or
  byte, the operating system must report that the handle's final path is exactly, case-sensitively, the resolved worktree root plus
  the Git-reported relative path and that it is a regular, non-device, non-reparse file with exactly one link
  (`GetFinalPathNameByHandle`, `GetFileInformationByHandle`); the length is then bounded, the bytes are read and the same facts are
  asked again of the same handle. Git hashes exactly those bytes on standard input (`hash-object --no-filters --stdin`; no repository
  pathname is given to Git), the handle is read again and proven once more, and Git's identity, this host's own blob computation and
  the bytes must agree. A deletion is claimed only for a path that does not open, with its immediate parent directory physically
  proven to be the owned directory before and after the failed open; an access failure is never absence. Another host omits every
  file as `containment_unproven`.
- **Baseline side.** The old text is the blob the captured HEAD records for the literal path: `ls-tree -z -l <commit> -- <paths>`
  (batches of at most 32, literal pathspecs) for mode, type, object identity and size, then `cat-file blob <id>` only after the current
  side is acceptable. Both run with `--no-replace-objects`, `GIT_NO_REPLACE_OBJECTS=1` and `GIT_NO_LAZY_FETCH=1` (so a replacement
  object cannot change the baseline and a missing local object is an omission, never a fetch), the existing hardening and no filter,
  textconv, shell or object write. Only a regular-file blob (`100644`, `100755`) of at most 256 KiB is read, and its content is
  accepted only when the re-encoded bytes equal the recorded size and hash to the object identity (a decoded process string is not
  raw-byte proof by itself); the identity is checked on the first read. Baseline text is cached per object for the second
  observation only when its file was admitted (the facts already hold that text), so the 512 KiB retained-source budget bounds
  everything an observation keeps alive, and a baseline whose file is omitted afterwards is not kept.
- **The host comparison.** The manifest builder composes the patch itself from the two snapshots (`TrackedComparison`): a linear
  common-prefix scan, a linear common-suffix scan that never overlaps it, one complete replacement hunk for the remaining middle
  and at most three unchanged context lines at each edge. A line is its exact text including its terminator, so a different
  terminator or a missing final newline is a difference and is stated with the standard `\ No newline at end of file` marker.
  Headers are `diff --git a/<path> b/<path>` then `---` and `+++` (`/dev/null` for an addition or deletion), with paths in the
  canonical quoted form (bare when no character needs quoting, otherwise C-style quoted with octal escapes for control characters and
  every non-ASCII byte) and `@@ -start,count +start,count @@` numeric ranges; no raw header, function text, mode or index line is copied,
  and the repository's diff prefix, filter, textconv and end-of-line settings have no influence. An empty file added or removed is a
  header with no hunk. The fixed limitation is stated in every manifest that has a tracked changed path as
  `changeEvidence.trackedComparison` (`method` `host_prefix_suffix_v1` and a notice): this is not Git's minimal or filter-normalized
  patch, unchanged lines inside the replaced middle can appear as removed and added, line-ending-only differences remain visible, and
  file modes and renames are not compared (a shorter form of the same notice survives the counts-only reduction).
- **Supported and omitted.** Modified (`" M"`, `"M "`, `"MM"`), added to the index (`"A "`, `"AM"`) and deleted (`" D"`, `"D "`,
  `"MD"`) paths are compared. Fixed omission reasons: `reserved_instruction_file`, `unsupported_status` (a type change, an
  intent-to-add, an added-then-deleted path, a path that is also untracked, a duplicated porcelain entry), `unmerged`,
  `unsupported_mode`, `symbolic_link`, `submodule`, `not_regular_file`, `containment_unproven`, `unreadable`, `too_large`,
  `too_many_lines`, `binary`, `invalid_utf8`, `baseline_unavailable`, `baseline_unverified`, `unencodable_path`, `aggregate_limit`,
  `no_content_difference` (identical bytes, for example a mode-only change), and the two builder-derived reasons `not_attested`
  (no fact for the path) and `attestation_incoherent` (a duplicate, a fact that contradicts the porcelain state, text outside the
  bounds). An omission carries no text and no size.
- **Bounds.** At most 256 KiB and 8192 lines per source (either side), at most 512 KiB of source retained per observation (both sides
  of every attested file, spent in ordinal path order; the next file that would exceed it is `aggregate_limit`), the existing
  128 changed paths, 512 KiB raw capture and 10-second Git timeout; work is linear in those sizes and no source is ever truncated
  into apparently complete text. The 32 KiB manifest ceiling, the 8 KiB tracked selection and the 4 KiB sample section are unchanged.
- **Builders re-derive.** `TrackedChangeEvidence.Derive` is the only way text reaches `ChangeEvidenceManifest`: it accounts for every
  tracked changed path exactly once, ordinal, from the capture's own changed paths and the reader's facts. A reader that returned no
  facts delivers no tracked text and every tracked path is `not_attested`; a fact for a path that is not a tracked change is ignored;
  the porcelain state decides which sides may be absent (`Classify`, shared with the reader); bounds and the retained budget are
  checked again; the two reserved root names are always omitted. There is no raw-diff input to the builders, and a legacy raw patch
  can never be admitted by missing or incoherent facts.
- **Manifest accounting.** The composed text is parsed by the existing `TrackedDiffParser`; omitted paths are merged into the same
  file list as omitted files, so `diffSelection.files`, `hunks`, `items` (kind `unsupported`, the fixed reason), the whole-hunk
  selection and the incomplete samples work unchanged. A comparison with no omission that fits 8 KiB is inlined exactly
  (`diffTruncated: false`, no `diffSelection`); otherwise `diffTruncated: true`, `diffSelection.complete: false` and, whenever any path
  is omitted, `diffSelection.omissionReasons` (reason to count, ordinal), which is a few fixed bytes and so survives every reduction
  step even when the per-file items are replaced by their counts. A manifest with no tracked path has no `trackedComparison` member and
  keeps the historical empty-diff shape.
- **Boundaries and replay.** Source text lives only in the sealed manifest, the provider input and the existing authenticated
  sealed-artifact viewer, unredacted, and is never stored in SQLite, a log or an error. Dispatch and restart read the sealed artifact;
  nothing recaptures, rebuilds, filters or reseals it, and a manifest sealed before this contract (a raw patch under `diff`, no
  `trackedComparison`) replays its exact bytes. Architecture tests keep `CompleteDiff` out of the Runs feature (and, since ADR-0027, out of every production reader), allow the
  derivation only in the claim handlers and keep the attestation types out of supervisors, endpoints and adapters.
- **Limits.** The raw observation behind the fingerprint still reads named paths and is not claimed to
  have this containment guarantee; committed blob content is not confidential by inference; topology can change after observation;
  only Windows has the physical proof; the process doubles used in tests do not establish real-provider reliability. Not included: a
  minimal-diff optimizer, alias enumeration, a deeper parent chain for deletions, a generic file reader, and any provider flag,
  permission, budget, grant, scheduling or lifecycle change.

## Attested checkpoint comparison for human inspection

The protected checkpoint-diff route a human uses to inspect a checkpoint used to return the raw patch of `git diff HEAD` as
`CompleteDiff`, which Git builds by reopening repository pathnames: a tracked file with a second name outside the owned worktree put
the other file's changed text into the response and the rendered page. Since
[ADR-0027](../decisions/0027-compare-attested-tracked-sources-for-human-checkpoint-inspection.md) the same attestation as
ADR-0024 backs that route, with the Agent-only concerns left out.

- **Capture.** `IGitWorkspaceEvidenceReader.CaptureForCheckpointInspectionAsync` is the only capture the query asks for. It is the same
  coherent bracket and fingerprint, returns `TrackedFiles` (the immutable facts of every tracked changed path) and returns no raw
  patch, no instruction context and no untracked previews. The two root instruction names are not reserved for it, and no
  instruction file is read to inspect a checkpoint. A reader without attestation support yields the plain capture with the patch
  removed and no facts, so every tracked path is a `not_attested` omission.
- **Shared policy, separate delivery.** `AttestedTrackedComparison` and `TrackedComparison` live in Projects and are the one admission
  and comparison used by both consumers; every call states a `TrackedSourcePurpose`. `AgentDelivery` reserves `AGENTS.md` and
  `CLAUDE.md` to the controlled instruction section (unchanged); `HumanInspection` compares a physically proven tracked root
  instruction file as inert displayed text that imports nothing and grants no authority. Agent manifest fitting stays in Runs and
  the inspection response fitting stays in the query.
- **Response.** `comparisonText`, `isComplete`, `trackedPathCount`, `comparedPathCount`, the fixed `limitation` and
  `omissions` (path and fixed reason, ordinal): every tracked changed path is compared or omitted exactly once, untracked files stay
  in the changed-files route, and `isComplete` means only that no tracked path was omitted. The text is at most 512 KiB of UTF-8;
  whole file blocks are fitted in ordinal order and an over-budget block is omitted as `comparison_limit`, never cut. The existing
  128 changed paths, 256 KiB and 8192 lines per source and 512 KiB retained per observation still apply.
- **Interface.** The inspection hook keeps project and checkpoint ownership and overlapping-read ordering; the panel states a
  partial or all-omitted result, lists the omitted paths and the limitation as plain text, and shows "No tracked diff." only for a
  complete capture with no tracked change.
- **Limits.** The raw observation behind the fingerprint still reads named paths; topology can change after observation; only Windows
  has the physical proof; the comparison is not Git's minimal or filter-normalized patch and does not compare modes or renames. Not
  included: the raw patch, alias enumeration, a minimal-diff optimizer, paging and any change to fingerprints, checkpoints, Agent
  manifests or replay.

## Project instruction context in Agent manifests

Every manifest used to carry the same three fixed DevalCopilot documentation names as "instruction references" whatever the
project, without reading them. Every NEW Agent claim now seals the project's own root instructions instead, through one
additive section (see [ADR-0021](../decisions/0021-add-bounded-root-instruction-context-to-agent-manifests.md)). The fixed
references are removed from new manifests and not replaced; an old sealed manifest replays its own bytes, references included.

- **Which files.** Exactly the root `AGENTS.md` then `CLAUDE.md` of the claim's own tool-owned worktree. No import, Markdown
  reference, nested or parent file, link, home path or setting is followed, and a reference to a sibling checkout grants no
  read. Tracked (clean or modified) and non-ignored untracked files are eligible; ignored-only files and assume-unchanged or
  skip-worktree entries are omitted because no checkpoint evidence vouches for them.
- **Capture.** `IGitWorkspaceEvidenceReader.CaptureForAgentContextAsync` is called only by the eight claim handlers (planning,
  critical review, resolution, implementation, code review, review correction, diagnosis-origin correction, verification
  diagnosis), once per claim, beside the untracked previews where the stage has them; every other capture is unchanged and
  returns no instruction context. Git runs only fixed hardened builtin subcommands with literal pathspecs (`ls-files -v`, an
  ignored-others listing). On Windows a file is read only through an open handle whose final path is exactly the resolved
  worktree root plus the fixed name, which is a regular, single-name, non-reparse file within the 8 KiB bound; nothing is hashed
  or sent for a file that fails that proof. The independent raw identity is Git's `hash-object --no-filters --stdin` of exactly
  those bounded bytes (no repository pathname is ever given to Git for it), the same held handle is read again, and Git's
  identity, the host's own blob computation and (for an untracked file) the checkpoint fingerprint's identity must agree.
  Presence, classification, identity and text are observed inside the capture bracket and again after it; any difference or
  identity mismatch discards all text and fails as `RepositoryChangedDuringCapture`. Another host omits both files as
  `containment_unproven`. The raw observation behind the fingerprint (status, the full diff and the per-path `hash-object` of
  untracked files) is the unchanged, pre-existing one and is not claimed to have this containment guarantee.
- **Delivery projection.** The two root names are reserved to this section. In a NEW Agent manifest their text never appears as a
  generic untracked preview or as tracked diff, hunk or sample content, whatever the section says (Complete, any Omitted reason,
  Absent) and for a safe, unsafe, clean, dirty or untracked source. The Agent-context capture does not preview a reserved
  untracked path and returns it as omitted (`reserved_instruction_file`). A reserved TRACKED path is, since ADR-0024, an explicit
  `reserved_instruction_file` omission of the attested tracked evidence (it is never even read for it), counted in
  `diffSelection.omissionReasons` and listed in `diffSelection.items` beside the delivered siblings, whatever a reader's facts
  claim and in every builder; the earlier cut of the raw patch at `diff --git` boundaries, and its whole-diff withholding
  (`reserved_instruction_diff_withheld`) under an undecodable header format, applied only to the raw patch and are no longer part of
  new delivery. Changed paths stay; a changed reserved path is named in `diffSelection.reservedInstructionFiles` at every reduction
  step and the diff is then never `diffTruncated: false` or `complete`; unrelated evidence is delivered as before. Names match
  ignoring case; only the root path is reserved. Ordinary captures, the fingerprint and historical manifests are unchanged.
- **Section.** `projectInstructionContextBoundary` (fixed text) and `projectInstructionContext` (`version` 1, `notice`,
  `sourceGitWorkspaceId`, `sourceGitCheckpointId`, `sourceCheckpointFingerprintSha256`, `sources`). Each of the two `sources`
  has `fileName`, `status` (`Complete`, `Absent`, `Omitted`), `reason`, `byteLength`, `sha256` and `text`; text only for
  `Complete`, length and SHA-256 only when established (a file omitted for budget keeps the ones it has). Reasons: `ignored`,
  `index_flag`, `unmerged`, `not_regular_file`, `containment_unproven`, `unreadable`, `content_identity_mismatch`, `too_large`,
  `binary`, `invalid_utf8`, `section_budget`, `manifest_budget`, `not_captured`. A capture that returned no context is
  `not_captured`, never `Absent`. Placement: directly after `untrustedEvidenceBoundary` where the form has one (so a
  format-repair notice stays in the trusted part), and where the planning form carried the old references.
- **Bounds.** At most 8 KiB of raw UTF-8 per source, valid and without NUL; the complete decoded text is kept byte for byte
  (line endings and a byte order mark included). The serialized section is at most 12 KiB, fitted as whole entries in fixed
  order (`section_budget`). In the 32 KiB whole-manifest fitting the section is reduced last: only after change evidence (and
  the diagnosis failure excerpts) are reduced as far as they can be are whole texts omitted, latest first
  (`manifest_budget`). No plan, challenge, finding, schema or authorization is shortened, and a manifest whose mandatory
  content cannot fit keeps the existing `context_manifest_too_large` refusal with its orphan cleanup and without consuming a
  claim, reservation or grant.
- **Authority.** The fixed boundary states the section is untrusted repository text that may inform the response through
  compatible conventions only; it can never override or extend the authorized plan, the role, the output schema, permissions,
  command restrictions or any human decision and grants no tool, network, approval, authorization, retry, budget, provider
  switch or publication. Provider adapters, arguments, safe-mode and schemas are unchanged.
- **Replay and confidentiality.** Dispatch and restart read the sealed artifact; nothing recaptures or rebuilds it, and a
  manifest sealed before this contract is never rejected for lacking the section. The text is carried by the sealed manifest, the
  provider input and the existing authenticated sealed-artifact viewer, which already serves manifest content in the browser and
  now shows this section like any other manifest content (no new endpoint or viewer); it is never stored in SQLite, a log or an
  error, and is not redacted. Containment is proven at the read, and the file can still change after observation.
- **Not included.** Imports, a directory walk, a third file, provider-side discovery, redaction of repository content, a
  rewrite of historical manifests, or any change to authority, budgets or permissions.
