# ADR-0022: Admit generic untracked-file previews only from physically proven single-name files

Status: Accepted

## Context

Agent context manifests carry bounded, identity-verified text previews of eligible untracked files, so a review or correction
stage can see a new file that `git diff HEAD` does not print. The preview reader
([the protocol](../architecture/agent-collaboration-protocol.md#bounded-untracked-file-previews-in-agent-manifests)) admitted a file
when three things held: it opened as a regular file, the operating system reported that the open handle's final path was exactly
the resolved worktree root plus the Git-reported relative path, and the bytes read hashed to the raw-content identity the
checkpoint fingerprint covers.

Those three checks describe one name. A file with a second name does not fail any of them: an ordinary untracked path that is a hard
link to a file outside the worktree opens as the exact reported name, its handle reports that name as its final path, and its bytes
hash to the identity that `git hash-object --no-filters` computes for the very same path. The reader therefore returned the outside
file's text as an ordinary preview, which was independently reproduced on a real Windows hard link with the published reader. The
reader already knew a stronger question, answered by the operating system for the open handle and not for a pathname: the handle's
attributes and its number of links, which the root instruction reader of
[ADR-0021](0021-add-bounded-root-instruction-context-to-agent-manifests.md) asks through `WindowsHandleFileFacts`. ADR-0021 protects
only the two reserved root names and says that general hard-link hardening of other paths remained outside it. That left every
other untracked filename on a disclosure route that all seven preview-carrying Agent claims share.

## Decision

**Admission.** A generic untracked-file preview is admitted only from a regular, non-reparse, non-device file with exactly one name,
proven on the held handle. The existing lexical refusal of rooted, drive, stream, dot and empty-segment paths, the refusal of
directories and trailing-slash paths unopened, the case-sensitive exact final-path proof against the resolved worktree root (a
legitimately redirected root is still accepted because the root is resolved with the same call), the 64 KiB verification bound, the
fingerprint's blob-identity comparison and the strict UTF-8 and NUL rules are unchanged. Before any byte or any length is read, the
reader asks the operating system for the open handle's attributes and link count through the existing `WindowsHandleFileFacts`
(no new interop, no generic filesystem service, no Application-level file access). The preview is admitted only when that answer is
available, reports no reparse point, no directory or device attribute, and exactly one link.

**Recheck.** After the bounded read, and before the preview is accepted, the same question is asked again of the same held handle.
Any change (a second name that appeared while the bytes were read, a reparse point, facts the system can no longer give) discards
the preview and its size, whatever the read had concluded. The repository pathname is never opened again to read content and
pathname metadata is never used as proof.

**Refusals.** Unavailable facts, a link count other than one and a reparse point are omitted as `containment_unproven`, with no
text and no size that was not proven. A file with several links is refused even when every known name is inside the worktree:
the host does not enumerate aliases, an enumeration would be neither required nor sufficient, and a second name that happens to be
inside the worktree today can gain an outside name tomorrow. A directory or device keeps the existing `not_regular_file`
classification and every other existing reason (`missing`, `unreadable`, `content_identity_mismatch`, `too_large`, `binary`,
`invalid_utf8`, `aggregate_limit`) keeps its meaning. Another host still omits every file as `containment_unproven` and lexical
containment is never a substitute.

**Everywhere previews are requested.** The rule lives in the one Projects-owned reader that both preview entry points use
(`CaptureWithUntrackedPreviewsAsync` and `CaptureForAgentContextAsync` with previews requested; the seven preview-requesting claim
handlers reach it through the second). It adds no file discovery, requests no previews for a path that requested none (planning
still requests none), and changes no HTTP contract, DTO, generated client, schema, migration, dependency, provider argument, profile,
schema or permission, authentication, budget, grant, scheduler or lifecycle behavior.

**Budgets and siblings.** An omission carries no text and spends none of the 4 KiB per-file or 16 KiB aggregate preview budget, so a
refused file never hides a healthy sibling, the ordinal ordering is unchanged, and the existing whole-manifest fitting accounts for it
as the fixed omission it is. Every untracked path of a capture is still accounted for exactly once.

**Forward only.** A newly sealed manifest carries the truthful omission. A manifest already sealed replays its exact bytes, even when
it holds a preview the corrected reader would now refuse; nothing rebuilds a manifest at dispatch or restart, reseals, rewrites
history or recaptures at dispatch, and no artifact access authority is added. Content admitted normally remains unredacted and is
delivered to the provider and shown by the existing authenticated sealed-artifact viewer exactly as before.

## What this does not decide

This closes generic untracked-preview delivery, not every filesystem read. Raw Git still reads named paths: the per-path
`hash-object` that feeds the checkpoint fingerprint, `git status`, and the tracked diff, hunk and changed-line sample selection can
read the content of a path that has another name outside the worktree, and a tracked file that is a hard link to an outside file can
have its changed content appear as tracked diff. The ordinary capture, the fingerprint and its serialization are unchanged, and
hardening those surfaces is a separate capability decision, because Git reopens the path after any check the host could make and a
filename check alone cannot make patch generation safe. ADR-0021's root instruction contract, its reservation of the two root names
and its own physical proof are unchanged and remain accepted. Files and link topology can change after observation, and a file whose
content is admitted is still repository text: untrusted evidence, never authority.

## Consequences

For every Agent stage that receives previews, a path that names a file with any other name is delivered as an explicit
`containment_unproven` omission, and the unrelated safe previews around it are unchanged. The cost is false refusals: a legitimately
multiply linked file inside the worktree (for example a build output linked from a cache) is omitted from the preview, which is the
honest and conservative result because the host cannot establish where its other names point. A reviewer still sees the path and the
reason, never an unproven size, and can read the file itself. Adding alias enumeration, a tracked-diff containment rule or any broader
filesystem read would be a new decision with its own authority analysis.
