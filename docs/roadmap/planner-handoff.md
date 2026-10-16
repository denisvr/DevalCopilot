# Planner/reviewer handoff

[AGENTS.md](../../AGENTS.md) and [workflow](development-workflow.md) apply.

## Priority and remaining objective

Documentation priority; functional work, tests and P9 stay stopped.
Codex plans/reviews; Claude implements. Reciprocal challenge, role
ownership and explicit GO remain mandatory.

- **Delivered:** WF-01 `4c9102c`, WF-02A `a593009`, WF-02B `d1a7b457`;
  earlier deliveries in Git history. No reopening, republishing or closure edit.
- **Incomplete:** WF-03 must yield a concise protocol core/router and focused
  roles/messages, provider, review/correction, context/evidence, usage/budgets
  and history documents, with section mapping and link/fragment compatibility.
  WF-03A is only its first slice; later moves remain unselected. No protocol,
  runtime, permission or architectural change is authorized.

## Verified baseline and dispatch state

2026-10-16: `main`; HEAD, local `origin/main` and live main
`d1a7b45743165e1cbcb794f485aed211a0055a98`; divergence 0/0; empty index,
clean tree, no untracked paths. Sandboxed live-ref failed; elevated read-only
retry succeeded. No reconciliation.

WF-02B: `logs/planner-review/wf-02b/publication-verification.json` and its
`publication-manifest.json`.
WF-02A verification remains under `logs/planner-review/wf-02a/`.
Dispatch had only this file modified; Claude preserved its 6140-byte dispatch
identity. Codex now replaces selection with GO. Selection evidence:
`logs/planner-review/wf-03a/selection-preflight.json` (ignored).

## Selected slice: WF-03A

**2026-10-16: GO for frozen six-document diff; no blocking findings.**
Three CLI safety bodies extracted; original headings retained.
No contract/runtime/permission change. WF-03 incomplete; later moves unselected.

Independent `node logs/planner-review/wf-03a/review1/check-review.mjs review`:
PASS, 9 checks, 100 links, 547 ms; exact reconstruction. Review/dispositions:
`logs/planner-review/wf-03a/review1/`. Executor results/failures retained under
`logs/executor/wf-03a/`; timings retained.

Executor observations resolved:
- **WF-03A-EX-01 accepted:** current-work 6807 bytes (+663); retain risks and
  WF-02A evidence loss; 6 KiB is a target.
- **WF-03A-EX-02 accepted:** "in this protocol" includes its linked companion.
- **WF-03A-EX-03 accepted:** "CLI safety contract above" reaches its forwarder.

## Publication boundary

Claude publishes in the same chat. Preflight: main/HEAD/local and live main
`d1a7b457`; 0/0; empty index; four unstaged tracked paths, only provider contracts
and rework map untracked. Codex replaced this checkpoint after review; other five
files stay exact. Freeze: `logs/planner-review/wf-03a/publication-manifest.json`.
Commit exactly its six files once, sole parent `d1a7b457`, subject
`docs: extract provider CLI safety contracts`. Verify bytes/blobs/modes and staged
tree; push main normally to origin/main and verify live ref in the same turn.
No closure edit/new slice. Changed substance returns for review; failed push or
divergence stops without force/reconciliation. Codex verifies publication.

## Retained review and evidence limits

WF-02A R1 resolved, evidence-loss deviation retained:
`logs/executor/wf-02a/correction1/development-runs-transcribed.md` is derivative,
partly abridged chat text, not recovered or independently authenticated output.
Earlier checker outputs were overwritten; original failed runs, checker and
timings are unavailable; run B exit status is unknown. Never reconstruct them.

WF-02B resolved: `logs/planner-review/wf-02b/review1/`; planner freeze
false positive (obsolete footer), disclosure and corrected pass:
`freeze-publication-attempt01.mjs`, `freeze-attempt01.json`; not an executor
finding. Index-intro and six/eight-path observations remain unadjudicated/out of
scope. Evidence retained, not rerun.

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
