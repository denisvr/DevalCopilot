# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md), the
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Selected slice (2026-10-04): physically proven untracked-file previews

### Current review decision (2026-10-04): GO for the reviewed snapshot

Codex authorizes publication of this bounded generic untracked-preview slice
only. The complete diff has been inspected: the existing reader proves the
opened handle's attributes and exactly one link before any byte or length read,
then rechecks the same handle after the bounded read. Unsafe text and size are
omitted, healthy sibling previews remain available, the capture fingerprint and
root instruction contract stay unchanged, and sealed replay remains exact.
No blocking finding remains. No next slice is selected and Increment 4 is not
claimed complete.

Verified branch main; HEAD, local origin/main and live refs/heads/main:
5c9dace0f976025ad71dfad11bb73c9468343236. Empty index; exactly 16 reviewed paths
(11 tracked modifications and 5 new files), including this planner review edit
and the commit-ready current-work.md entry. Publication is bound to the exact
path/raw-SHA-256/size inventory issued with the publication instruction, not
counts alone. The generated client remains
13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a.

Independent fresh reviewer evidence: solution build 0 errors and 0 warnings;
Infrastructure preview/instruction filter 96 passed and 2 environment-gated
file-symlink skips; Api preview delivery plus prior instruction delivery/replay,
hosted chains and fixture filter 84 passed without skips; Application preview
manifest/instruction/projection filter 185 passed without skips; Architecture
full 13 passed; harness 65 passed; canonical test:e2e:all once, Chromium 11 and
native-double journeys 2 passed with normal authentication, normal host
composition and all supervisors. No browser failure or unchanged rerun occurred.
The known SignalR navigation negotiation console lines remain. Git diff check
and all 16 changed/new file NUL/trailing-whitespace/lone-CR/final-newline checks
passed; 190 local document file links resolved (anchors were not independently
rechecked). The historical delivery ledger is unchanged. The executor's full
suites, mutation runs, formatter comparison and 303 file/anchor link checks are
separately reported evidence in current-work.md, not independent reviewer runs.

Codex adjusted only two wording facts in the new current-work.md entry: its
planner hash describes the dispatch/executor-return state before this review
edit, and executor checks apply to the final implementation with delivery docs
finalized afterwards. No production or test file changed during review.

Accepted limits: Windows-only proof and explicit symlink privilege skips;
legitimate multiple links are conservatively omitted; files and link topology
can change after observation. Raw Git hashing and tracked diff can still read
outside hard links. Normally admitted content remains unredacted and visible
in the authenticated artifact viewer. No real-provider reliability is proven.
The two older e2e roots remain outside cleanup authority.

Use one publication instruction: verify every approved path/hash/size and the
empty index, commit exactly this substantive snapshot including current-work.md,
push main normally, verify the live ref, run the specified checks on that commit,
then make only the tightly bounded factual current-work.md closure and verify
its normal push. Stop on divergence, failed checks, inventory mismatch or any
material change. Do not force-push, amend, reconcile history, rerun an unexplained
failure into green or start another slice.

Approved inventory manifest (outside the repository):
`C:\Users\denis\AppData\Local\Temp\devalcopilot-untracked-preview-go-20261004-375abebe9e194de6ba4621606fc4beb6.json`

### Original selection decision and verified baseline (historical)

Codex selects one bounded Increment 4 security outcome: generic untracked-file
previews delivered to Agents admit bytes only from a physically proven, regular,
single-name file at the exact Git-reported path of the owned worktree. Unsafe
files retain explicit omission accounting; healthy sibling previews remain
available. Claude implements in one new executor chat. This is selection only,
with no implementation, commit/push GO or Increment 4 completion claim.

Independently verified main; HEAD, local origin/main and live refs/heads/main:
5c9dace0f976025ad71dfad11bb73c9468343236. Empty index and clean checkout before
this planner edit. Substantive 4f3e31ab70c2dda9220e48eded8c4b0920daf73a has parent
3b8450c48e247846944204ba8f2eab1b04bc635b and exactly the approved 84 paths. All
83 non-closure files match the approved raw SHA-256 and size; the substantive
current-work.md Git blob matches approved SHA-256
7271ac59f3d98e64ec160542655ea04fd622723d7e664457724257a27443b79b. Closure 5c9dace0
has parent 4f3e31ab and changes only the bounded current-work.md entry (+5/-1).
The prior publication is verified and its GO is spent. Its reported publication
checks and retained evidence remain in current-work.md; no suite was repeated
merely to verify an identical publication. Generated api-client.ts remains
13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a.

### Evidence and candidate comparison

UntrackedFilePreviewReader checks the opened handle's final path and the bytes'
Git blob identity, but does not ask WindowsHandleFileFacts for attributes and
link count. A hard link can name the same file both inside and outside the
worktree while passing both checks. Codex independently invoked the published
reader on a disposable real Windows hard link with a matching blob identity:
the outside sentinel was returned as preview text. This proves the reader gap;
it is not a claim that a real provider was invoked. The probe was removed and
the repository remained clean.

This closes a demonstrated disclosure route used by the shared Agent evidence
capability, rather than increasing context sampling. The existing root-only
instruction protection does not protect a different untracked filename.
Broader tracked-diff/fingerprint containment is a separate capability decision:
Git still reads named paths, and patch generation cannot be declared safe merely
by checking a filename before Git reopens it. That remaining risk stays visible.
Account stops, resume and compaction were compared but are not selected. The
[App Server reference](https://learn.chatgpt.com/docs/app-server) documents rate
limit reads and thread compaction; it does not establish this host's binding of
an observed account to a subsequent restricted CLI invocation. The
[Claude CLI reference](https://code.claude.com/docs/en/cli-reference) says
no-session-persistence prevents resume. Neither a CLI flag nor an observed
allowance proves safe invocation eligibility under the current contracts.
Lifecycle/commit orchestration belongs to Increment 5 and remains excluded.

### Precise implementation boundary

- Harden the existing Projects-owned UntrackedFilePreviewReader for every caller
  that requests generic untracked previews, including CaptureWithUntrackedPreviewsAsync
  and CaptureForAgentContextAsync. Do not enable previews for planning or any
  other path that currently requests none, and add no new file discovery.
- Keep lexical refusal of rooted/drive/stream/dot/empty-segment paths before an
  open, exact ordinal final-handle path matching against the resolved worktree
  root, and legitimate redirected-root behavior. Before reading bytes or their
  length, require successful handle facts proving a regular, non-reparse,
  non-device file with exactly one link. Reuse WindowsHandleFileFacts; no new
  interop, generic filesystem service or Application-level file access is needed.
- Refuse every multiple-link file, even when all known names are inside the
  worktree. Enumerating aliases is neither required nor sufficient. Failure to
  obtain proof is containment_unproven, with no text or unproven size. Preserve
  existing fixed classifications for a known non-regular file and other failures.
  Recheck the held handle's admission facts after its bounded read, before
  accepting the preview. Do not reopen the repository pathname to read content,
  use pathname metadata as proof, or weaken an unsupported-host omission.
- Preserve the existing 64 KiB full-byte verification limit, 4 KiB per-file and
  16 KiB aggregate preview limits, ordinal ordering, strict UTF-8/NUL handling,
  character-boundary cuts, fingerprint blob-identity comparison and whole-manifest
  32 KiB fitting. A new omission must not spend preview text budget or hide a
  healthy sibling. Preserve complete/shortened/omitted accounting and fixed reasons.
- Do not alter CaptureAsync, Git commands/raw observations, checkpoint identity
  serialization, tracked diff/hunk/sample selection, instruction-section capture,
  root-name reservation, claim budgets/eligibility or dispatch. Explicitly retain
  that raw Git hashing and tracked diff may still read outside hard links; this
  slice prevents their content being returned through generic untracked previews,
  not every filesystem read. Files and link topology can change after observation.
- Newly sealed manifests carry the truthful omission. Historical sealed artifacts
  replay byte-identically, even if they contain a preview that the new reader
  would refuse. No reseal, historical rewrite, dispatch-time recapture or new
  artifact access authority. Content admitted normally is still unredacted and
  visible through the authenticated sealed-artifact viewer.
- Add additive ADR-0022 for this generic-preview admission policy; ADR-0021's root
  instruction contract remains accepted and unchanged. Update its index,
  engineering context, the preview protocol passage and the relevant roadmap
  risk description. Correct the preview passage's outdated caller and viewer
  claims narrowly; do not rewrite the historical delivery ledger.

### Acceptance evidence and validation

1. Write failing regressions first against the parent: a real outside hard link
   at an ordinary root and nested untracked path must currently return a matching
   sentinel, then be omitted by the corrected reader. Use independent expected
   bytes/identities. Cover an in-worktree second hard link, a safe sibling and a
   normal single-name control. The hard-link regression must run on this host;
   do not replace it with a mock or an environment skip.
2. At the real Git/filesystem boundary cover both preview capture entry points,
   facts unavailable, non-regular/link/junction and unsupported-host refusals,
   exact path/redirected-root controls, identity mismatch, and a deterministic
   link-count change during the bounded read caught by the final proof. Reuse
   existing bounds/encoding/order tests rather than copying their full matrix.
   Establish that admission failure returns no preview bytes. A file-symlink
   privilege skip may remain explicit and separate from the hard-link evidence.
3. Through real claims, sealed artifacts and actual adapters over process doubles,
   prove the outside sentinel is absent from the entire returned preview evidence,
   entire manifest and stdin while an unrelated safe preview is present. Cover
   both providers and every existing preview-bearing builder/form using the
   narrowest shared coverage; do not assert only one JSON section or manufacture
   omissions in a fake reader as the security proof. Preserve claims that do not
   request previews, root reserved-name behavior and no-consumption on existing
   claim refusals. Prove old sealed replay and a later fresh claim's omission.
4. Mutation checks must detect bypassed link-count/facts proof and bypassed final
   admission recheck. Restore mutations byte-identically. Add no test-only
   production bypass; an internal deterministic seam may expose only the real
   acquisition/read boundary when necessary.
5. Run affected checks first. On the final tree run one solution build/NSwag,
   sequential full Infrastructure, Application and Api suites, and Architecture.
   Domain can remain explicitly retained if no Domain code/tests change. Keep the
   generated client byte-identical. Run formatter comparison and hygiene/link
   checks on every tracked/untracked changed file. No dependency changes are
   expected; label audit evidence retained rather than inventing a fresh run.
6. Run frontend typecheck/build, strict e2e typecheck and the harness, then one
   canonical test:e2e:all with normal authentication, host composition and all
   supervisors. No new browser journey, workflow SQL seed, extra claim or reload
   is required. Run full Vitest if frontend files change; otherwise identify its
   earlier result as retained. Clean only roots owned by this run; leave the two
   older e2e roots untouched. Stop and preserve an unexplained canonical failure.

### Exclusions, stop gates and delivery

No HTTP/DTO/generated-client, schema/migration/dependency, React production,
provider argv/profile/schema/permission, authentication, budget, grant, scheduler,
lifecycle, account threshold, session/resume/compaction or publication change.
No broad filesystem/artifact-store hardening, generic file browser, tracked-diff
regeneration, fingerprint redesign or expanded preview bounds.

Stop on preflight discrepancy, inability to establish the stated held-handle
proof, a required wider filesystem read or authority, a real-provider call,
external contract mismatch, historical rewrite or a production blocker outside
scope. Do not silently harden tracked diff or claim this resolves its risk.
Do not weaken assertions, hide failures, disable supervisors or rerun an unchanged
browser failure into green. Return a blocker with the narrow reproduction.

Return the complete unstaged, uncommitted, unpushed diff, including a commit-ready
current-work.md entry, exact changed-file inventory, commands actually run,
fresh/retained evidence and remaining limits, for Codex GO/NO-GO. Preserve this
planner-owned file byte-for-byte. Keep all corrections in the same executor chat
and select no next slice. A future GO will use one publication instruction for
the reviewed substantive commit, normal fast-forward push, live verification,
required checks and tightly bounded current-work-only factual closure. Any
material post-GO change requires re-review.

### Executor preflight after selection (historical)

Expected branch main; HEAD, local origin/main and live refs/heads/main:
5c9dace0f976025ad71dfad11bb73c9468343236.
Nothing staged; only docs/roadmap/planner-handoff.md modified; no untracked files.
Generated api-client.ts SHA-256:
13c9d116ecc792e05e2652f470ceff3c73bfc574c2fb348157dc7d5440dac44a.
The complete English execution prompt is provided in chat, not duplicated here.
