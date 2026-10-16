# Planner/reviewer handoff

Planner-owned scope; replace, never append. [AGENTS.md](../../AGENTS.md) and
[workflow](development-workflow.md) apply. Git/code prevail.

## Priority and remaining objective

Documentation is the owner's priority; functional work, tests and P9 stay
stopped until completion or owner reprioritization. Codex plans/reviews;
Claude implements; challenge, role ownership and explicit GO stay mandatory.

- **Delivered:** WF-01 `4c9102c`, WF-COL-01 `cab3756`, DOC-49-01 `f7e42c1`,
  TEST-48-01 `d60d718` and WF-02A `a593009`. Do not reopen or republish them.
- **Reviewed:** WF-02B has GO below; publication is not yet verified.
- **Unselected/incomplete:** WF-03 splits the protocol into a core/router and
  roles/messages, provider, review/correction,
  context/evidence, usage/budgets and history documents, with section mapping
  and inbound-link/fragment compatibility. No runtime, permission, protocol or
  architecture change. WF-02B alone cannot complete the documentation objective.

## Verified transition baseline

2026-10-16: `main`; HEAD, local `origin/main` and live `refs/heads/main`
equal `a59300972aa2519c1df5ed0cc4f8d63c238d0f26`; divergence 0/0;
empty index/tree and no untracked paths before selection. Sandboxed live-ref
queries failed; elevated read-only retries succeeded. No reconciliation ran.

WF-02A: `logs/planner-review/wf-02a/publication-verification.json`;
pre-publication wording is historical.

## Selected slice: WF-02B

**2026-10-16: GO for the frozen five-document diff; no blocking findings.**
Claude publishes in the same chat; Codex verifies. Scope: [roadmap](mvp-delivery-plan.md),
its [map](mvp-delivery-plan-rework-map.md), ADR index, current-work and this record.
Planning meaning and incomplete Increments 4 to 6 stay; no contract/runtime change.

Independent `node logs/planner-review/wf-02b/review1/check-review.mjs` passed
(4287 ms); checks and separate semantic acceptance:
`logs/planner-review/wf-02b/review1/`. Executor final-01 (13 checks) and
development/negative runs under `logs/executor/wf-02b/` are retained, not rerun;
historical commands/timings remain executor evidence. Product evidence unchanged.

Executor challenges resolved:

- **EX-01 accepted:** roadmap 16495 bytes (+111); retain precise links.
- **EX-02 accepted:** current-work 6661 (+517); retain risks/WF-02A evidence loss.
- **EX-03/CR-01 accepted:** ADR-0029 owns the sequencing exception.
- **EX-04 resolved:** WF-02A's map is historical; ADR-0012's control stays locatable.

Index-intro and six/eight-path observations: out of scope, unadjudicated.

## Publication boundary

Preflight: same branch/refs, 0/0, empty index; four tracked paths unstaged,
only the map untracked. Claude preserved the dispatch checkpoint; Codex replaced
it with this GO. Five final files are frozen in
`logs/planner-review/wf-02b/publication-manifest.json`.
Commit exactly those files once, sole parent `a593009`, subject
`docs: simplify MVP roadmap and correct ADR index categories`.
Verify bytes/hashes, modes and blobs; push `main` normally to `origin/main`
and verify live main in the same turn. No closure edit. Changed substance
returns for review; failed push/divergence stops, no force or reconciliation.

## Retained review and evidence limits

WF-02A R1 resolved: `logs/planner-review/wf-02a/review2/r1-checks.json`.
Accepted 8192-byte context/8186-byte checkpoint retain risks/evidence limits.
`logs/executor/wf-02a/correction1/development-runs-transcribed.md` is derivative,
partly abridged chat text, not independently authenticated original output.
Earlier checker outputs were overwritten; original failed runs, checker and
timings are unavailable; run B exit status is unknown. Keep this evidence-loss
deviation; never reconstruct it. Unchanged evidence is retained, not rerun.

## Standing decision constraints

- ADR-0050 stays Proposed, P9 unproven, no mechanism selected. Frozen patches
  A/B/C, C's assertions, write-ahead and sticky-flag alternatives remain
  unapplied/unaccepted. Owner GCM/ancestry-profile and residual name-to-ID risk
  acceptance was design-only; human rearm/resolution is absent.
- One-profile/two-child experimental authority is spent. No native build,
  launch, profile, security, helper, credential or provider operation is renewed.
  Retained profiles, fixtures, DACLs, labels, grants and ignored evidence stay
  untouched; no sweep, cleanup or permission reset. Hashes do not seal live
  ACL, label, grant or profile metadata.
- Qualification stays `Blocked`/`TargetsNotReal`, both allowances unused.
  Git/GitHub qualification is separate/unstarted; GCM versions and compatibility
  remain unqualified. Reported repository access is not branch-push permission.
- Preserve defects, deviations, lost/pre-redaction output and uncertainty in the
  [feasibility note](../architecture/github-publication-source-write-denial-feasibility.md),
  [capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md),
  [native semantics note](../architecture/github-publication-native-io-semantics.md)
  and [execution evidence](../architecture/github-branch-publication-execution-evidence.md).
  Earlier excluded-artifact reads grant no new reads. Original and failed
  evidence stays frozen, including the 69-file 11/57 correction.
- Concurrent-replay 409, target-test 500, disposal `NullReferenceException`,
  unnamed Api 2067/2068 and interrupted reviewer synthetic run stay unexplained.
  Later passes prove neither flakiness nor irrelevance. TEST-48-01 diagnostics
  cannot cover hung submissions; retention beside disposal failure is unproven.
- Increment 4 provider qualification, Increment 5 coordination/recovery and
  Increment 6 publication/CI orchestration remain incomplete. Account/context,
  raw Git read and other execution-facing limits stay in
  [current-work.md](current-work.md#open-risks).

Frozen [execution history](current-work-history-ba1d4c2.md) blob
`ebb007040fa73ab0252511180e53e2e64f5285df` and
[planner history](planner-handoff-history-ba1d4c2.md) blob
`dd3bb1d70459322db9147c99c8fe53a01829e38a` remain untouched.
