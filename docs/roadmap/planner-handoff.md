# Planner/reviewer handoff

[AGENTS.md](../../AGENTS.md) and [workflow](development-workflow.md) apply.

## Priority and verified baseline

Documentation first; functional work/tests/P9 stopped. Codex plans/reviews;
Claude implements. WF-03 remains incomplete; later extraction slices unselected.
WF-03H independently verified published on 2026-10-17:
main; HEAD, origin/main and live refs/heads/main all
`8a4223d50b3a1db0cf5683d39fbba059f59818b3`; sole parent
`de8f1a72e72e1a6839714764248cce56ce2fed1b`; divergence 0/0; empty index,
clean tree, no nonignored untracked paths before this planner policy edit.
Evidence: `logs/planner-review/wf-03h/publication-verification.json`, five
passing groups; original child Git streams in `publication-verification-01/`;
checker `publication-verify.mjs`. Separate fresh live lookup matched; its
original stdout remains only in the chat tool transcript. Manifest: 2570 bytes,
SHA-256 d65fd1890428c6a4ec09aef2186a3fccc0c0a1db0da0a589dd1d71e4b3275882.
Publisher inspected, not rerun: pre 41/41, stage 30/30, commit 33/33,
push 8/8, post 21/21; snapshots match publisher SHA-256
10d99acd40d20711585fd710f9ae3d92078fb428fe405450b99761ca4b186a42.
No failed publisher phase. No H closure edit, reopening or republication.

H review: `logs/planner-review/wf-03h/review1/decision.md`; independent 5/5,
freeze-02 4/4. Freeze-01 used a wrong baseline path; failure retained.
Executor final 23/23 and negative baseline 16/16, controls 22/22 inspected,
not rerun. Dev-01 C10 absolute-offset checker defect fixed with no document
change. Generate-01 output files overwritten; hashes/tool/streams survive,
not original output documents. No missing streams reconstructed. Scans and
coordinates lexical; Git/disk/live lack negative controls. EX-H01 accepted:
7399-byte current-work, exact 3466-byte History/risks. EX-H02 accepted:
turn-limit routing. EX-H03 deferred: HL691-692 and prior GL613/690/705-707
wording remains unchanged, unreconciled and not endorsed.
Earlier G/F/E/D limits remain through `logs/planner-review/wf-03g/review1/decision.md`,
`logs/planner-review/wf-03f/review1/decision.md` and its publication verification
(publisher source hash only), E review2/review1 decisions and D review1 decision
(N05, not N06, is the broken-fragment control). No product/provider checks.

## Owner-requested planning policy: publication GO

Owner explicitly requested that the planner always try the largest coherent
slice to reduce Claude/Codex relays and routine owner involvement. Planner-owned
policy is now specified in development-workflow.md; slice-start-template.md
requires a sizing rationale and a concrete constraint for necessary splits.
These are human workflow documents only: no product orchestration, architecture,
role transfer or automatic messaging is introduced. Explicit GO stays mandatory.

GO for exactly development-workflow.md, slice-start-template.md and this
planner-handoff.md under docs/roadmap/, at the hashes in
`logs/planner-review/wf-planning-policy/publication-manifest.json`.
Subject: `docs: prefer the largest coherent planning slice`.
Sole parent: `8a4223d50b3a1db0cf5683d39fbba059f59818b3`.
The planner authored the owner-requested policy and its checkpoint; the executor
only publishes the reviewed files. No execution delivery or current-work closure
edit is requested. All other tracked files remain unchanged. Normal main push,
live verification, stop on divergence/push failure/material change; no force.

CONTINUE_THIS_CHAT: the immediate follow-up is this bounded workflow policy
publication and its verification; no context-usage measurement is claimed.
After that, assess the largest coherent remaining documentation outcome.
WF-03 is unfinished; no next extraction is selected by this policy change.

## Retained review and evidence limits

WF-03C-R1 resolved: dev-01 C12 checker defect versus real C17 retained-scope
wording loss, restored before dev-02; negative-01 baseline failed, invalidating
its kills; negative-02 supersedes it. EX-C01 resolved; original companion-path
omission was the planner's. Review: logs/planner-review/wf-03c/review2/decision.md.
Correction evidence inspected, not rerun; original conservation retained.
Recovered dev-01 checker matches its hash, not an original snapshot or historical
authentication. Aborted write is an executor note, not an original stream.
C's 6969-byte current-work accepted; no trim/closure edit. Scans lexical;
Git/disk no mutants. The 6 KiB target does not waive retention.

WF-02A R1 resolved; evidence-loss deviation remains:
`logs/executor/wf-02a/correction1/development-runs-transcribed.md` is derivative,
partly abridged chat text, not recovered or independently authenticated output.
Earlier checker outputs were overwritten; original failed runs, checker and
timings are unavailable; run B exit status is unknown. Never reconstruct them.

WF-02B: `logs/planner-review/wf-02b/review1/`; obsolete-footer false positive
corrected without executor edits; index-intro and six/eight-path observations
unadjudicated/out of scope. WF-03B R1/EX-01 resolved, EX-02/03 accepted;
newline/status false positives and ENOBUFS defect/deviation:
`logs/planner-review/wf-03b/review1/decision.md`. ENOBUFS note is derivative
tool-output transcription; no original standalone stream/timing retained or
reconstructed. These are planner defects. Failed evidence stays retained.
Prior product/test/browser/generation/qualification evidence keeps its original
scope, not rerun. Pre-existing missing
`planner-handoff.md#verified-transition-baseline` reference in
documentation-rework-map.md stays unchanged/out of scope.

## Standing decision constraints

- ADR-0050 stays Proposed; P9 unproven; no mechanism selected. Frozen patches
  A/B/C, C's assertions, write-ahead and sticky-flag alternatives stay
  unapplied/unaccepted. Owner GCM/ancestry-profile and residual name-to-ID risk
  acceptance was design-only; human rearm/resolution is absent.
- One-profile/two-child authority is spent. No native build, launch, profile,
  security, helper, credential or provider operation is renewed. Retained
  profiles, fixtures, DACLs, labels, grants and ignored evidence stay untouched;
  no cleanup, sweep or permission reset. Hashes do not seal live metadata.
- Qualification: `Blocked`/`TargetsNotReal`, both allowances unused; Git/GitHub
  separate/unstarted; GCM unqualified. Repository access grants no branch push.
- Preserve defects, deviations, lost/pre-redaction output and uncertainty in the
  [feasibility note](../architecture/github-publication-source-write-denial-feasibility.md),
  [capture cross-check](../architecture/github-publication-retained-capture-crosscheck.md),
  [native semantics note](../architecture/github-publication-native-io-semantics.md)
  and [execution evidence](../architecture/github-branch-publication-execution-evidence.md).
  Earlier excluded-artifact reads grant no new reads. Original/failed evidence
  stays frozen, including the 69-file 11/57 correction.
- Concurrent-replay 409, target-test 500, disposal `NullReferenceException`,
  unnamed Api 2067/2068 and interrupted reviewer synthetic run stay unexplained.
  Later passes prove neither flakiness nor irrelevance. TEST-48-01 cannot cover
  hung submissions; retention beside disposal failure is unproven.
- Increments 4/5/6 incomplete. Account/context, raw Git read, non-Windows limits:
  [current-work](current-work.md#open-risks). No production, tests, dependencies,
  experiments, capture-limit or standards edits.

Frozen [execution history](current-work-history-ba1d4c2.md) blob
`ebb007040fa73ab0252511180e53e2e64f5285df` and
[planner history](planner-handoff-history-ba1d4c2.md) blob
`dd3bb1d70459322db9147c99c8fe53a01829e38a` remain untouched.
