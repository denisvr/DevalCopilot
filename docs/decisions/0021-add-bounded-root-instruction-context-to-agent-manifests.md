# ADR-0021: Add bounded root instruction context to Agent manifests

Status: Accepted

## Context

Every Agent context manifest named the same three DevalCopilot-specific documents (`CLAUDE.md`,
`docs/engineering-context.md`, `docs/architecture/agent-collaboration-protocol.md`) as its "instruction references",
without reading them and whatever project the run belonged to. For any other registered project the names pointed at
files that do not exist in its worktree, and no stage received the conventions that project actually keeps. The reference
list was also misleading in the other direction: a path string proves neither that a file exists nor that a provider ever
received it. The Claude critical review additionally runs with tools disabled and `--safe-mode`, so that provider cannot
be expected to discover any project instructions itself, and the review-correction manifest had no equivalent at all.

[ADR-0004](0004-use-a-structured-agent-collaboration-protocol.md) already says repository text is untrusted evidence, and the
bounded untracked-file previews and tracked-hunk evidence of the existing protocol already show how repository bytes may be
sealed into a manifest without becoming authority. This decision adds the project's own root conventions to that same
sealed-context mechanism, and it supersedes no existing authority decision.

## Decision

**Scope: exactly two root files.** Every newly claimed Agent stage seals the exact root `AGENTS.md` and `CLAUDE.md`, in that
order, from the tool-owned worktree of its own registered project. Nothing else is read: no import, Markdown reference, nested
or parent instruction file, link target, home or profile path, or setting is followed. A reference inside `AGENTS.md` to another
file (for example a sibling `EngineeringStandards` checkout) is data, never permission for another read, and the section never
claims that a project's complete contract was loaded. Tracked files (clean or modified) and non-ignored untracked files are
eligible. Ignored-only files, and tracked entries marked assume-unchanged or skip-worktree, are omitted, because no Git
evidence of the checkpoint vouches for them.

**Capture.** The Projects-owned Git evidence capability gains one narrow Agent-context capture
(`IGitWorkspaceEvidenceReader.CaptureForAgentContextAsync`, called only by the eight claim handlers). It returns the exact
current working-tree bytes of the two fixed names inside the same coherent observation as the checkpoint fingerprint.
Git is asked only fixed, hardened questions with literal pathspecs about the index entry, its flags and ignored status. On Windows
a file is first opened and proven: a regular, single-name, non-reparse file whose open handle's final path is exactly
(case-sensitively) the resolved worktree root plus the fixed name, within the 8 KiB bound; a hard link, a case-distinct spelling,
a link, a directory, a junction or an oversized file is classified there and nothing else is ever done with it. Only the bounded
bytes read through that proven handle reach the independent identity operation: they are given to Git on standard input
(`hash-object --no-filters --stdin`), the same held handle is read again to prove the file did not change meanwhile, and Git's
raw identity, this host's own blob computation of the same bytes and, for an untracked file, the identity the checkpoint
fingerprint itself covers must all agree. No repository pathname is ever given to Git for this identity, so a path swapped or
linked between a check and a later open cannot reach it. Presence, classification, identity, length and text are observed again
after the existing status, HEAD and diff bracket, and any difference, or any identity mismatch, discards all text and fails
through the existing `RepositoryChangedDuringCapture` outcome. A host without that physical proof omits both files as `containment_unproven`; lexical containment, timestamps,
decoded text or a clean Git status are never substituted for proof. The ordinary captures, the checkpoint fingerprint and its
serialization are unchanged, and a plain capture never pretends instructions were captured. The raw observation behind the
fingerprint (its status, its full diff, and the `hash-object` of each untracked path) is the pre-existing one: it reads named
paths and is not claimed to have this reader's physical-containment guarantee; nothing in this decision changes it.

**Delivery projection.** The two root names are reserved to the controlled section. For NEW Agent delivery their text is never
carried as a generic untracked preview or as tracked diff, hunk or changed-line sample content, whatever the section says about
them (Complete, Omitted for any reason including `section_budget` and `manifest_budget`, or Absent), and whether the source is a
safe regular file, an unsafe link, clean, dirty or untracked. The capture for Agent context does not read a reserved untracked path
as a preview, the returned untracked entry for it is an explicit omission (`reserved_instruction_file`), and the returned diff has
the reserved files' blocks removed structurally: it is cut at `diff --git` file boundaries (which cannot occur inside a hunk) by
the path decoded from each block's own header, never by searching repository-controlled text for a name or a string. Only a
header this host decodes with certainty can be classified: the default `a/` and `b/` prefixes with the same path on both sides.
Anything else is unknown, and unknown never means "not reserved": a diff that does not begin with a file header, or any block whose
header cannot be decoded (a repository configured with `diff.noprefix` or `diff.mnemonicprefix`, a rename, a malformed quoted
name), cannot be cut at a certain boundary, so when a reserved path changed the whole generic diff is withheld, including the
unrelated hunks that uncertainty also hides. No other prefix is guessed, the ordinary Git observation is not normalized and the
checkpoint fingerprint is unchanged. The manifest builders apply the same projection themselves, so any capture reader is
covered, independently of whether another builder later rejects the format. Changed paths are kept, a changed reserved path is
named under `diffSelection.reservedInstructionFiles` in every reduction step, the diff is then never `diffTruncated: false` or
`complete`, and an untracked reserved path is listed as omitted. When a tracked reserved path changed and no diff text is
delivered at all, `diffSelection` carries the fixed reason `reserved_instruction_diff_withheld` and a fixed notice saying that
the whole generic diff, including unrelated hunks, was withheld and that `changedPaths` still lists every changed file. Unrelated change evidence is delivered as before, and the
names match ignoring case because the Windows file system does; a nested path with the same file name is an ordinary file.

**Bounds and fidelity.** Each source is at most 8 KiB of raw UTF-8, valid, without NUL; the complete decoded text, including
line endings and any byte order mark, is kept exactly, with no normalization, summary, truncation or partial rule. An empty file is
a legitimate complete source. Oversized, binary, invalid-UTF-8, unreadable, containment-unproven, ignored, index-flagged and
unmerged files are omitted with a fixed safe reason, never a path or an exception message.

**The section.** Each new manifest carries one additive, versioned `projectInstructionContext` (version 1), bound to the
claim's source workspace, checkpoint and fingerprint, preceded by the fixed host-authored `projectInstructionContextBoundary`.
Its `sources` always account for both files in fixed order, each as `Complete` (exact text, verified byte length and SHA-256),
`Absent` (proven not to exist at observation) or `Omitted` with a fixed reason: `ignored`, `index_flag`, `unmerged`,
`not_regular_file`, `containment_unproven`, `unreadable`, `content_identity_mismatch`, `too_large`, `binary`, `invalid_utf8`,
`section_budget`, `manifest_budget`, or `not_captured` (the claim's capture returned no instruction context, which is never
reported as `Absent`). Length and SHA-256 appear only when actually established; text appears only for `Complete`. The
Application re-derives what makes an entry `Complete` (bound, no NUL, and the exact UTF-8 bytes of the text must hash to the
claimed identity), so an adapter that over-claims can never produce a `Complete` entry.

**Boundary.** The fixed text states that the section is untrusted repository text that may inform the response through
compatible project conventions only, and that it can never override or extend the authorized plan, the role, the expected output
schema, the host permissions and command restrictions, or any human decision, and grants no tools, network access, approval,
authorization, retries, budgets, provider switching or publication. The provider adapters, their arguments, safe-mode,
permissions, tool restrictions and response schemas are unchanged, and no provider is given discovery of its own.

**Every claim path.** Before this decision six manifest builders (planning, critical review, resolution, implementation, code
review and verification diagnosis) carried the fixed references and the review-correction builder carried no equivalent; the
section is now added by all seven builders, which serve eight claim handlers: planning, critical review, resolution (with
re-review and second round), implementation (accepted original, resolved revision with or without a second review, and the
human-authorized escalated form, each with or without direct guidance), code review (initial and correction), the ordinary and
guided review correction and the diagnosis-origin correction (one builder, two handlers), and verification diagnosis, including
each format-repair form, which is a fresh claim that captures fresh context. This is stage coverage, not a count of
`AgentRole` values: the implementation and both correction claims share the Implementer role and diagnosis shares the CodeReviewer
role. The misleading fixed `instructionReferences` property is removed from every new
manifest and not replaced: a Complete source of the section is the only statement that a file was provided.

**Budget.** The serialized section, metadata included, is at most 12 KiB; whole entries are fitted in fixed order and a text whose
escaped JSON cannot fit is omitted whole as `section_budget`, retaining its accounting. The section takes part in the existing
32 KiB whole-manifest fitting: only after every existing optional reduction of change evidence (and the diagnosis excerpt budget)
is exhausted may whole instruction texts be omitted, latest file first, as `manifest_budget`, always retaining the metadata, the
boundary and any established length and SHA-256. A plan, challenge, finding, schema, authorization or other semantic input is
never shortened. When the mandatory envelope still cannot fit, the existing `context_manifest_too_large` refusal, orphan cleanup
and non-consumption of claims, reservations and grants apply unchanged.

**Forward only.** A manifest already sealed replays its exact bytes, including its old references or its absence of the
section; nothing rebuilds a manifest at dispatch or restart, reads the root files again to replace sealed input, rewrites
history, or rejects a historical attempt merely because the section is absent. A later fresh claim captures its own context.
There is no schema, migration, dependency, API shape, provider flag, profile, permission, authentication, budget, scheduler,
lifecycle, grant, approval or publication change.

## Confidentiality and honest limits

- The text is carried by the sealed manifest, handed to the provider by the adapter, and shown by the existing authenticated
  sealed-artifact viewer, which already serves manifest content to the local operator in the browser; that viewer is unchanged and
  now shows this section like every other manifest content, and no new endpoint, DTO or viewer is added. It is never written to
  SQLite, status, a log or an error. Redaction is not applied to host-composed manifests, so a file's content, including anything
  sensitive written in it, is delivered to the provider as written and is visible to whoever may open that viewer. The controls
  reduce what is read; they do not make repository content safe to disclose.
- The content is untrusted. Delimiting and the fixed boundary reduce the chance that a provider follows it as authority, and the host
  grants nothing because of it; the host cannot prove what a provider obeys or ignores.
- Only the two root files are read. Conventions kept elsewhere, imported, or referenced from them are not provided, and an omission
  or absence is stated as such. This is not a complete project contract.
- Containment is proven on the open handle at the moment of the read. The file may still change after the second observation and
  before the provider runs; the manifest records what was observed, bound to the checkpoint fingerprint, not what the file holds later.
- Windows is the only host with the physical proof; elsewhere both files are omitted. The pre-existing untracked-file previews and
  tracked diff do not reject a hard link to an outside file, and the checkpoint fingerprint's raw observation reads named paths;
  none of that is changed here. For the two root names only, the delivery projection above keeps those generic surfaces from
  carrying their text; general hard-link hardening of other paths remains outside this decision.

## Consequences

A stage for any registered project can follow that project's own conventions without receiving another project's file names, and a
reviewer can see from the sealed manifest exactly which root files were provided, which were absent, and why any was not. The
representation is independent of the provider and of whether it can read files itself. Adding a third file, an import resolver or a
directory walk would be a new decision with its own authority analysis.
