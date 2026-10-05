# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md),
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Review decision (2026-10-05): GO, attested checkpoint comparison

Codex accepts the complete corrected snapshot for publication. R1 is resolved:
coverage is admitted only from explicit safe non-negative integer counts,
coherent compared/omitted/total accounting, a matching completeness flag,
distinct non-empty omission paths and reasons, and text present exactly when
at least one path was compared. Source text is never parsed. A refused body
clears the inspection and shows the fixed safe message; valid empty, complete,
partial and all-omitted responses retain their truthful presentation. No
backend contract or generic decoder was added in the correction.

Verified preflight: `main`, HEAD, local `origin/main` and live
`refs/heads/main` all `7552cbe2772a6103d0f5c7caf464f2b0a15e8b6f`, real index
empty. The reviewed inventory INCLUDING this planner record is 47 paths:
27 modified, one deleted by the comparison-algorithm move and 19 added.
The executor's 46 excludes this record. The executor preserved the NO-GO
record byte-for-byte at SHA-256
`57239c90efd786678dbd1458d4490e2a58e882ecd65bd458b6fe7c4f25caa75a`
until this intentional decision edit. Generated client SHA-256 remains
`e1540273dab05124e8e7ca8f62863171efd6944c8c1fab0e53d6295074cc89bc`.
An external raw-file manifest freezes the final approved bytes, statuses and
sizes, including the required absence of the deleted source path.

Codex reviewed the capture-purpose split, shared attested-source policy,
namespace-only comparison move, response fit, authenticated API, frontend,
ADR and tests. Earlier independent checks remain evidence on the unchanged
backend: solution build zero warnings/errors, selected Application 215/215,
Infrastructure 88/88, Api 12/12 and full Architecture 36/36, no selected skips
or failures. On the corrected frontend Codex independently ran the four
panel/ownership test files: 89/89, and `npm run typecheck`: exit 0. The
executor's fresh logs show the 21 failing-first R1 regressions, full Vitest
2090/2090, typecheck, lint (nine baseline warnings), build, strict e2e tsc,
harness 87/87 and one canonical Chromium 12/12 plus journeys 3/3. Codex
inspected those logs and did not repeat the browser runs. Full backend counts,
formatter baseline and audits are retained, not newly run after R1.

The accounting, text/compared and safe-integer mutations fail their tests.
Removing only `typeof isComplete` survives because the subsequent strict
comparison with a boolean already rejects non-booleans; this is an equivalent
redundant guard, not missing behavioral coverage. The earlier Infrastructure
cancellation-test failure remains disclosed with cause unproven and unchanged
adapter/process code; repeated passes are not proof of its elimination.

Publication authorization applies only to the frozen reviewed snapshot.
Require the exact base, branch, empty index, 47-path status inventory and every
raw hash/size before staging only those paths. The deletion must remain absent.
Require the complete cached diff check, then one substantive commit, a normal
fast-forward push of `main`, fetch and independent live-remote verification,
and sequential checks on that commit with fresh full output. A discrepancy,
material change, remote divergence or failed check stops publication/closure
and returns to Codex; no force push, history reconciliation or unchanged rerun
for green is authorized. After successful checks, only this slice's entry of
current-work.md may receive factual publication closure in a second commit and
normal push, with the same live-ref/clean-tree verification and no embedded
closure SHA. Preserve failures, fresh-versus-retained distinctions, the two
older owned e2e roots and the documented physical-proof/raw-observation limits.
No next slice is selected and Increment 4 completion is not claimed.

## Selected slice (2026-10-05): Attested checkpoint comparison for human inspection

Codex selects exactly one bounded Increment 4 evidence-safety outcome: a human
inspecting a checkpoint sees a bounded host comparison of physically proven
tracked sources, together with explicit omissions, instead of Git's raw
working-path patch. Claude remains the executor. This selection authorizes
implementation only; there is no commit/push GO or Increment 4 completion.

### Verified publication and executor preflight

The preceding warning slice is published. Substantive commit
`167f212fbd1714bc9e7cca3e5cd70de80263bc89` has parent
`8d18f72314ac32598788d37027f679a59cfa535b`. Its 81 published paths equal the
approved external manifest; Codex verified the manifest hash and all 80 current
raw files other than current-work.md against their approved hashes and sizes.
Closure `7552cbe2772a6103d0f5c7caf464f2b0a15e8b6f` has that substantive parent
and changes current-work.md only (two insertions, one deletion). No implementation
discrepancy was found. The executor's post-publication counts are recorded in
current-work.md; Codex did not rerun those suites during selection.

Before this planning edit, branch `main`, HEAD, local `origin/main` and live
`refs/heads/main` independently matched
`7552cbe2772a6103d0f5c7caf464f2b0a15e8b6f`, with nothing staged, unstaged or
untracked. After this edit the expected state is exactly one unstaged modified
file, `docs/roadmap/planner-handoff.md`, with nothing staged or untracked.
Generated client SHA-256:
`3e4ae6cc92e09761006146432e55e221fe522eec9c23b3be66d8c80b5321b447`.
The executor must verify these facts once before editing and preserve this
planner record byte-for-byte.

### Architectural judgment and comparison of candidates

`GetGitCheckpointDiffQueryHandler` still calls `CaptureAsync` and returns
`current.CompleteDiff`. `GetGitCheckpointDiffResponse` calls it CompleteDiff,
`useProjectGitEvidence` stores it, and WorkspaceEvidencePanel renders it, using
"No tracked diff." for empty text. By contrast, ADR-0024 already closes new
Agent delivery with attested source facts and a deterministic host comparison.
ADR-0024 explicitly leaves this authenticated human-inspection route open.

Codex reproduced the raw Git mechanism in a disposable repository with a
fictitious sentinel: replacing a committed tracked file with a hard link to a
file outside the worktree put the outside text in `git diff HEAD`. The temporary
repository was removed. This is mechanism evidence, not an HTTP reproduction;
the executor must prove the complete query/HTTP/browser boundary in this slice.

This closes a concrete disclosure route in the human evidence used before
review/approval. It does not expand context sampling or seek another manifest
format. It provides one end-to-end outcome across the reader, query, HTTP,
generated client and inspection UI without adding execution authority.

Other candidates were compared and remain unselected:

- Claude account allowance/thresholds: current official usage documentation
  describes `/usage` and last-known displays, not a proven bounded headless
  account-observation contract for this host. No credential inspection,
  undocumented endpoint, UI scraping or session totals may stand in for it.
- Session resume/manual compaction: the official Codex docs expose session-ID
  resume and App Server thread compaction, but the current adapters deliberately
  use ephemeral execution or no session persistence. Session ownership,
  retained-history isolation and preservation of each role/schema/permission
  envelope remain unproven. Existence of commands is insufficient.
- Another context sampler or limit display has less value than this concrete
  human-facing disclosure boundary. No Unknown-only scaffolding is selected.
- Stage coordination, run completion, commits and publication are Increment 5/6
  authority changes; they are not appended to this evidence slice.

References checked during selection: [Claude usage](https://code.claude.com/docs/en/costs),
[Claude CLI](https://code.claude.com/docs/en/cli-reference),
[Codex non-interactive sessions](https://learn.chatgpt.com/docs/non-interactive-mode),
and [Codex App Server](https://learn.chatgpt.com/docs/app-server).
No real provider was invoked. This slice needs no new provider capability.

Read ADR-0008 for owned-workspace evidence and ADR-0021/0022/0024 for containment,
reserved Agent instruction delivery, immutable facts and replay. Add ADR-0027
for the human-inspection comparison, explicitly advancing ADR-0024's deferred
ordinary-query boundary without rewriting its historical decision.

### Objective and exact boundaries

1. Keep the existing protected checkpoint-diff GET and one mediator query.
   Retain project/checkpoint membership, Ready workspace, active lease, fresh
   coherent capture and exact checkpoint-fingerprint agreement. A stale or
   changing capture remains a safe refusal. Source text is never persisted by
   inspection and the GET creates no event, checkpoint, attempt or artifact.
2. Give checkpoint inspection an explicit reader operation returning attested
   tracked facts in the existing coherent capture bracket, without instruction
   context or untracked previews. Reuse the held-handle proof, exact captured
   HEAD blob verification, repeated observation, immutable facts and existing
   source/path/time bounds of ADR-0024. Strip CompleteDiff from the returned
   inspection capture. An implementation with no attestation yields omissions,
   never a fallback to its raw patch. CaptureAsync, checkpoint serialization,
   fingerprint bytes, raw internal observation and old sealed replay stay intact.
3. Derive every delivered comparison from attested facts, rechecking changed-path
   membership, status, absent sides, duplicate/incoherent facts and bounds.
   Reuse the existing deterministic comparison algorithm; do not copy raw Git
   headers, metadata, filters or textconv. Share only the source admission and
   comparison responsibilities intended to evolve together, under Projects
   ownership. Keep Agent reservation/manifest fitting and query response fitting
   as explicit separate policies, not a generic delivery framework.
4. Human inspection may compare physically proven tracked root AGENTS.md and
   CLAUDE.md like other tracked files. This is inert displayed source text; it
   imports nothing and grants no authority. Agent capture and manifest delivery
   must STILL reserve both root names to their controlled instruction section.
   Purpose separation must be explicit and tested, with no default that broadens
   Agent delivery. Unsafe roots are omissions; no instruction reader is invoked
   merely to inspect a checkpoint.
5. Replace the misleading CompleteDiff response contract with operation-owned
   comparison text, completeness/coverage and a bounded list of path + fixed
   omission reason, plus the fixed host-comparison limitation. Account every
   tracked changed path exactly once as compared or omitted, in ordinal order.
   Untracked files remain in the unchanged changed-files list, never fabricated
   as tracked comparisons. No full paths, outside bytes, exceptions or arbitrary
   provider/Git messages enter omission details.
6. Bound delivered comparison text to 512 KiB UTF-8, in addition to the existing
   128-path, per-source and retained-source limits. Fit whole file blocks in
   ordinal order; an over-budget block is omitted with a fixed reason, never
   cut into apparently complete text. Completeness concerns displayed tracked
   content coverage only, not Git metadata, review applicability or approval.
   Unsupported/binary/mode-only changes and all-omitted captures remain visible
   as accounted omissions. No sampling, paging, diff optimizer or higher bounds.
7. Regenerate the TypeScript client through the normal build. Update
   useProjectGitEvidence and WorkspaceEvidencePanel to show the host comparison,
   its limitation and per-path omissions. An incomplete or all-omitted response
   must never render "No tracked diff." or imply the omitted files are clean.
   A genuine zero-tracked-change capture may show the empty state. Render names
   and content as plain text. Preserve explicit inspection, project/checkpoint
   lifetime ownership, overlapping-read ordering and safe failure clearing.
   No polling, remount workaround, browser storage or background inspection.
8. Add the ADR and narrow engineering-context/index/protocol/cockpit/roadmap
   updates, with a commit-ready current-work.md entry. Record that raw Git
   fingerprint hashing still reads named paths and that this slice closes the
   HTTP/UI text-delivery route, not every filesystem read or after-read race.
   The host comparison is not Git's minimal/filter-normalized patch and does
   not compare modes/renames. Windows-only proof and conservative omissions
   remain truthful limitations; no real-provider reliability claim.

### Exclusions and stop gates

Do not alter checkpoint fingerprints or persistence, historical artifacts,
Agent manifests/replay, budgets, account-warning/stop controls, claims,
authorizations, verification/review applicability, leases, lifecycle,
supervisor routing or provider argv. No migration, dependency, raw-observation
redesign, alias enumeration, directory-deletion proof extension, sandbox or
cleanup authority is selected. Preserve the two older owned e2e roots.

Stop and report if safe delivery requires changing a fingerprint/history,
weakening physical proof or any of these authorities, enlarging bounds,
creating another diff algorithm, or replacing normal authenticated host
composition. A preflight discrepancy or unexplained browser failure is a stop
gate, not permission to reconcile history or rerun unchanged for green.
A shared-code refactor must preserve the Agent contract and demonstrate that
preservation; material inability to do so returns to the planner.

### Acceptance evidence and review return

- Write regressions first. Through real Git, real Windows hard links, production
  capture and authenticated HTTP, an outside sentinel for root and nested
  tracked paths must be absent from the ENTIRE response; unsafe paths are
  omissions beside a healthy comparison. Cover inside second names, no physical
  proof, binary/unsupported sources, absent attestation and incoherent facts.
  Reuse existing detailed proof tests rather than duplicating every permutation.
- Prove safe edits/additions/deletions, root instruction comparison for humans,
  unchanged Agent root reservation and sealed replay; test repository diff
  prefix/attribute settings, stale fingerprint and change-during-capture refusal.
  Response-budget coverage must show whole-block omission and honest metadata.
- Add rendered component/hook evidence for partial/all-omitted/empty states,
  project and checkpoint replacement, A->B->A, late and overlapping answers,
  failure clearing and retained callbacks, without relying on parent keys.
- Add one real authenticated Chromium inspection case with a run-owned fixture:
  the UI shows a safe sibling and an unsafe-path omission and never the outside
  sentinel. Use the generated client over real HTTP and the established owned
  root/harness. Keep the existing journeys and all supervisors enabled.
- Targeted restored mutations must detect raw-patch fallback, lost omission
  accounting and a weakened proof/delivery boundary. Rebuild mutations so stale
  binaries cannot count as evidence. Disclose any surviving mutation precisely.
- Run affected checks first, then a fresh solution/client build, full affected
  Application/Infrastructure/Api suites, full Architecture, frontend Vitest,
  typecheck, lint, production build, strict e2e TypeScript check and harness.
  Only after the harness passes run one canonical test:e2e:all with fresh full
  output. Run formatter/audits as applicable; report baseline findings/skips,
  exact commands, failures and retained evidence without claiming them fresh.
- Check tracked AND new files for NUL/trailing whitespace/blank EOF, local
  Markdown links and the full diff. No new-file gap hidden by git diff --check.
  Return the complete unstaged, uncommitted, unpushed diff, inventory, risks and
  commit-ready current-work entry for Codex GO/NO-GO. Preserve this planner file.

Publication remains a later decision. After a future GO, one instruction will
cover the frozen substantive commit, normal fast-forward push, live-remote
verification, checks on that commit and a tightly bounded current-work-only
factual closure. Material changes after GO require re-review. No next executor
chat begins before this slice is reviewed, published and verified.
