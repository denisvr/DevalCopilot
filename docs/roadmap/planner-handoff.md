# Planner/reviewer handoff

Planner-owned scope, rationale and review decisions; not a delivery ledger or
approval log. It replaces its current state on each selection or review
decision. Procedure: [development workflow](development-workflow.md). Git and
code prevail over this summary.

## Active slice: DOC-49-01 (GO for the reviewed documentation diff)

- **Decision (2026-10-16): GO.** Publish exactly the engineering context,
  MVP plan, execution checkpoint and this planner record at the frozen
  manifest identities. Codex remains planner/reviewer; Claude remains
  executor. No closure edit or additional substantive change is authorized.
- **Accepted result.** Exactly two stale ADR-0049 labels now say accepted,
  consistent with the accepted ADR and decision index. Surrounding contract
  text and all other statuses remain unchanged. The correction establishes
  no real CLI compatibility, branch-push permission or increment completion.
- **R1 - resolved.** The execution checkpoint now records the original
  checker exit 1 and its two pre-existing false positives: assignment-secret
  at engineering-context line 192 on ordinary prose, and email at line 311
  on the fictitious remote. It also records resolved links/fragments and the
  passing diff check. Neither baseline line nor the checker was changed.
  Original failed output remains retained and classified, never a clean run.
- **Executor concern addressed.** Codex agreed with the executor's concern
  that the durable checkpoint omitted the nonzero outcome. The correction
  carries that qualification without requiring the chat or ignored logs.
  The Paths bullet's 9240-byte planner identity records the initial return;
  it is retained as instructed, not used as the final publication identity.
- **Independent review.** Review 1 inspected the complete four-path diff,
  executor scripts and final evidence; exact byte proofs established each
  intended substitution, unchanged baseline findings, planner preservation
  and the unchanged P9 evidence/History/Open risks tail. Its checker exit 1
  with two classified findings is retained. Review 2 inspected the R1 delta
  script and output, confirmed the saved return against its reviewed hash,
  and independently proved one 309-byte insertion with no deletion (45 ms).
  The corrected checkpoint checker passed with zero findings (40 ms);
  `git diff --check` passed. The substantive-file, checker and original
  failed-output hashes still match. No blocking finding remains.
- **Evidence and proportionality.** Review records are
  `logs/planner-review/doc-49-01/review1/independent-review.json` and
  `review2/independent-review.json`. Prior publication and product evidence
  retain their original scope and limitations. No product build, suite,
  generation, provider invocation, native work, qualification, security-state
  query, historical snapshot, P9 input inspection or cleanup was needed.

## Publication boundary

Review preflight: `main`; HEAD, local `origin/main` and live main all
`cab3756c24e58524f25ef3ba607236d63fa9426e`; 0/0; empty index;
exactly the four slice paths unstaged; no untracked paths.

`logs/planner-review/doc-49-01/publication-manifest.json` freezes the four
reviewed paths, modes, Git blobs, working-byte lengths/SHA-256 values and
parent entries. Verify it before staging, stage only those four paths and
commit once with sole parent `cab3756c24e58524f25ef3ba607236d63fa9426e`,
subject `docs: align ADR-0049 references with accepted status`. Push `main`
normally to `origin/main` and verify the live ref in the same executor turn.
Expected final checkout: clean, empty index, no untracked paths and 0/0.
A manifest discrepancy or material change returns for review. A failed push
or diverged remote stops publication; no force-push, reset, cleanup or history
reconciliation. Preserve pre-publication checkpoint wording; no closure edit.
This GO authorizes only this repository's reviewed documentation publication,
not product runtime publication, a next slice or renewed experimental work.

## Verified baseline and continuity

WF-COL-01 remains published at `cab3756c24e58524f25ef3ba607236d63fa9426e`,
sole parent `b48738f0f160336df945ad3a6ad1802a6876458b`, subject
`docs: clarify reciprocal critical collaboration`. Its four-path frozen
manifest SHA-256 is
`75749f58fcb55d655795bfce74423d2e556871e262cf85d53bd160faa9e2662d`.
The prior independent verification is retained at
`logs/planner-review/wf-col-01/publication-verification.json`; this review
rechecked local/live ref equality, not the complete prior publication matrix.
No closure edit or repeat push follows. ADR-0021 delivery of the new root
text was not exercised; existing sealed manifests remain unchanged.

P9-SEM-01 remains published at `b48738f0f160336df945ad3a6ad1802a6876458b`,
sole parent `465e60cfc31d02961fc3970d44e65bb4cfe5ee29`, with R1-R3 and its
architecture-index entry accepted. Its publication manifest SHA-256 is
`27a8e04c8e8c6cf9d9ccce9d22e19924a7441a7e7a5509b313ded13f60e0a6f5`;
its independent verification is retained, not rerun.

Review and publication stay in this planner chat and the same DOC-49-01
executor chat. Codex independently verifies publication before the next
continuity decision. No observed context-usage percentage is asserted;
no next slice or chat transition is dispatched by this GO.
WF-02/WF-03 remain unselected. Increment 4's provider contracts and Agent
qualification, Increment 5 coordination/recovery and Increment 6 publication
and CI orchestration remain incomplete. ADR-0049 reports repository access
only; ADR-0050 remains Proposed, P9 unproven and no mechanism selected.
No native, experimental, credential/helper/provider or cleanup authority follows.

## Preserved evidence and uncertainty

The [semantics note](../architecture/github-publication-native-io-semantics.md)
narrows only H37/H40 and part of static-audit U2. H40's failed-query output
is undocumented; query and wait rights differ. H37's stdout argument is
conditional on suffix-only loss, with possible consumption-on-failure open.
K1-K4 remain open; future work cannot validate the historical stderr capture
or the unchecked canonical exit query. No saved web-page copies exist.

P9-SEM-01's `wc -lc` deviation read the full contents of three excluded audit
reports for counts. No content was displayed or used in the analysis; review 1
identified their manifest memberships without reopening them. The correction
did not reopen them. This is not retrospective permission or a new read grant.
Preserve the failed grep and the recorded pre-final wording mismatch.

P9-EV-01 remains verified at `465e60c`, sole parent `4c9102c`; the
[capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md)
establishes retained-byte consistency only. The malformed receipt remains a
named failure and the earlier summary a derivative. Correction protection
hashing read excluded ctl/app fixtures: 69 equal saved hashes comprised
11 reader artifacts, 57 gate files and the planner; 12/56 was wrong. The
reviewer compared saved records without reopening fixtures. Preserve that
read-boundary deviation and prior parser/checker failures; no metadata proof
or another sweep follows. Original evidence and publication records stay frozen.

## Standing decision constraints

- [ADR-0050](../decisions/0050-define-the-first-approved-github-branch-publication-execution-contract.md)
  remains Proposed; P9 remains unproven. No mechanism is selected.
- Frozen patches A/B/C, their chain and C's added assertions remain unapplied
  and unaccepted. The write-ahead and sticky-flag alternatives stay unaccepted.
- The one-profile/two-child experimental authority is spent. No renewed native
  build, launch, profile query, reuse, creation/deletion, security operation,
  credential, helper or provider operation is authorized by this selection.
- Retained profiles, fixtures, protected DACLs, labels, grants and ignored
  evidence stay untouched; no cleanup, permission reset or fixture sweep.
  File hashes do not seal live ACL, label, grant or profile metadata.
- Agent qualification stays `Blocked`/`TargetsNotReal`, both allowances unused.
  Git/GitHub qualification is separate and unstarted; supported GCM versions
  and real compatibility remain unqualified.
- Owner GCM/ancestry-profile and residual name-to-ID risk acceptance was
  design-only. Human rearm/resolution remains absent; no runtime authority.
- Preserve the v1 defect/protocol deviation, inconclusive A2 result, native
  error/capture limitations, lost/pre-redaction output and live-state
  uncertainty in the [feasibility note](../architecture/github-publication-source-write-denial-feasibility.md).
  First-push claims requiring source re-reading remain in the
  [execution evidence](../architecture/github-branch-publication-execution-evidence.md).
  Reported repository access is not branch-push permission.
- Concurrent-replay 409, target-test 500, disposal `NullReferenceException`
  and unnamed Api 2067/2068 remain separately unexplained. Later passes prove
  neither flakiness nor irrelevance; no increment or MVP completion is inferred.

Execution-facing open risks remain in
[current-work.md](current-work.md#open-risks). WF-01 and earlier facts remain
at their published baselines and the frozen
[execution history](current-work-history-ba1d4c2.md) and
[planner history](planner-handoff-history-ba1d4c2.md); neither snapshot is edited.
