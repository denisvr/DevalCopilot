# ADR-0024: Deliver new tracked-change text only from attested snapshots

Status: Accepted

## Context

Every Agent manifest that carries change evidence used to deliver tracked-file text from the raw patch of `git diff HEAD` as captured
for the checkpoint ([the protocol](../architecture/agent-collaboration-protocol.md#bounded-tracked-hunk-evidence-in-agent-manifests)).
Git generates that patch by reopening the repository pathnames, so a tracked file that has become a hard link to a file outside the
worktree, or that has any second name, puts the other file's changed text into the patch. The text then flowed, as whole hunks and as
changed-line samples, into the sealed manifest of every role, the provider's standard input and the authenticated sealed-artifact
viewer. [ADR-0022](0022-admit-generic-untracked-previews-only-from-physically-proven-single-name-files.md) closed that route for
generic untracked previews and said so explicitly: the tracked diff, the hunk selection and the sample selection were left on the
unproven raw observation, because a check of a name before Git reopens it cannot make patch generation safe.
[ADR-0021](0021-add-bounded-root-instruction-context-to-agent-manifests.md) protected only the two root instruction names, and did
so by decoding the headers of the raw patch, which the repository's own diff prefix settings could make undecodable.

The unchanged raw observation (`status`, the full `diff`, the per-path `hash-object` of untracked files) is what the checkpoint
fingerprint is computed from, so it cannot be hardened or replaced without changing fingerprints, serialization and the replay of
sealed history.

## Decision

**Attested facts replace the raw patch for new delivery.** `CaptureForAgentContextAsync` still observes the raw state for the
fingerprint, but the capture it returns no longer carries the raw patch (`CompleteDiff` is null). It carries
`GitWorkspaceEvidenceResult.TrackedFiles`: one immutable, provider-neutral `GitWorkspaceTrackedFile` for every tracked changed path,
in ordinal order, each either the attested before and after text of the file or a fixed omission. The collection that crosses the
Projects boundary is an owned immutable snapshot (`GitWorkspaceTrackedFiles`): the result copies whatever collection it is given, at
construction or by a `with` replacement, and returns a read-only list that is neither an array nor a mutable list, so neither the
caller's collection nor a cast of the returned one can replace attested text after the physical proof. Ordinary captures
(`CaptureAsync`, `CaptureWithUntrackedPreviewsAsync`), the checkpoint fingerprint and its serialization, the ordinary checkpoint diff
query and every sealed historical manifest are unchanged.

**Current side: a held, proven handle.** On Windows the current text of a tracked path is acquired only through one open handle.
Before any length or byte is read, the operating system must report that the handle's final path is exactly (case-sensitively) the
resolved owned worktree root plus the Git-reported relative path and that it is a regular, non-device, non-reparse file with exactly
one link. The length is then bounded, the bytes are read, and the same facts are asked again of the same handle. The bytes are given to
Git on standard input for an independent raw identity (`hash-object --no-filters --stdin`), the handle is read again and proven once
more, and the identity, this host's own blob computation and the read bytes must agree. The path is never given to Git for this. The
whole observation is repeated inside the existing capture bracket and again after it; any difference, or bytes that did not hold,
discards the capture as `RepositoryChangedDuringCapture`. A deletion is claimed only when the immediate parent directory is
physically proven to be the owned directory before and after a failed open and nothing stands at the name; an access failure is never
absence. Other hosts omit every file as `containment_unproven`.

**Baseline side: the exact blob of the captured HEAD.** The old text comes from the blob the captured HEAD records for the literal
path, found with fixed typed plumbing (`ls-tree -z -l` with literal pathspecs and the full commit identity, then `cat-file blob <id>`),
with replacement objects and promisor lazy fetching disabled so a missing local object is an omission and never a network fetch, no
filter, textconv or shell, and no object written. The recorded type, mode and size are checked before content is read; the content is
accepted only when its re-encoded bytes equal the recorded size and hash to the object's identity, because a decoded process string is
not raw-byte proof by itself.

**A deterministic host comparison.** The host, not Git, writes the patch, from the two owned snapshots alone: a linear
common-prefix scan, a linear common-suffix scan, one complete replacement hunk for the remaining middle and at most three unchanged
context lines at each edge. Line terminators and the final-newline state are exact. Paths are written in the canonical quoted form,
hunk ranges are numbers, and no raw header, function text or metadata is copied; the repository's diff prefix, filter, textconv and
end-of-line settings cannot influence it. This is not Git's minimal or filter-normalized patch: unchanged lines inside the replaced
middle can appear as removed and added, line-ending-only differences remain visible, and file modes and renames are not compared. The
manifest says so in a fixed `trackedComparison` statement. No diff optimizer, temporary source file or raw-patch fallback exists.

**What is supported and what is omitted.** Ordinary edits, staged additions, deletions and empty files are compared. Unmerged paths,
types other than regular files (symbolic links, submodules, other modes), type changes, intent-to-add, a path that is also untracked,
binary content, invalid UTF-8, paths that cannot be encoded, unproven or unreadable sources, and files outside the bounds are explicit
omissions with a fixed reason. Each source is at most 256 KiB and 8192 lines, an observation retains at most 512 KiB of source text
(the delivered facts and the baseline cache together: a baseline is kept in the cache only for an admitted file, whose facts already
hold the same text, so a path omitted after its baseline was read, for example for no content difference, leaves no source text alive),
files are processed in ordinal path order, and nothing is truncated into apparently complete text. The existing 128 changed paths,
512 KiB raw capture and 10-second Git timeout are unchanged.

**Builders re-derive; every path is accounted for.** The facts cross the Projects Application boundary, and the manifest builders
re-derive what may be delivered for any reader: a capture with no facts delivers no tracked text and counts every tracked path as
`not_attested`, a fact for a path the capture did not report, a duplicate, a fact that contradicts the porcelain state, text outside the
bounds or a spent budget is never admitted, and the two reserved instruction names are always an omission. Every tracked path is in the
composed text or in a fixed-reason omission exactly once. The omissions are merged into the existing whole-hunk selection, counts and
changed-line samples, so the 32 KiB ceiling, the reduction ladder and the explicitly incomplete samples are unchanged, and a
per-reason count (`diffSelection.omissionReasons`) survives every reduction step. The manifest is never described as complete while any
path is omitted.

**Forward only.** A manifest already sealed replays its exact bytes after a restart, whatever shape it has; nothing rebuilds or
filters a manifest at dispatch.

## What this does not decide

The raw Git observation behind the fingerprint (`status`, `diff`, the per-path `hash-object`) and the ordinary checkpoint diff still
read named paths and can read the content of a file another name links to; this decision does not claim to harden them. Committed blob
content is not confidential by inference, and a file's content that is admitted is unredacted repository text in the provider input and
the sealed-artifact viewer. Files and link topology can change after observation. Windows is the only host with the physical proof. A
process double does not establish the reliability of a real provider. Alias enumeration, sampling beyond the existing contract, a
minimal-diff optimizer, parent chains for deletions and any broader filesystem read would be new decisions.

## Consequences

For every Agent stage that carries change evidence, a tracked path that any other name reaches, or whose sources cannot be proven, is a
fixed omission, and the healthy siblings are delivered as before. The cost is false refusals (a legitimately multiply linked tracked
file is omitted) and a more verbose comparison where a file has several scattered edits, which the manifest states. A reviewer still
sees every changed path and the reason. The projection that withheld the whole generic diff when a reserved path changed under an
undecodable header format is no longer needed for new delivery: that case is now one omission beside the delivered siblings.
