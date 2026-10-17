# Planner/reviewer handoff

[AGENTS.md](../../AGENTS.md) and [workflow](development-workflow.md) apply.

## Priority and verified baseline

Documentation first; functional work/tests/P9 stopped. Codex plans/reviews;
Claude implements. WF-03 incomplete. 2026-10-17: main; HEAD, origin/main and
independently checked live main `b81f761173f52e160acd9141df612b6c2a9c2812`;
0/0; empty index, clean tree, no nonignored untracked paths before this edit.
At dispatch only this unstaged checkpoint differed; the GO below supersedes it.

Planning policy published; sole parent `8a4223d50b3a1db0cf5683d39fbba059f59818b3`.
`logs/planner-review/wf-planning-policy/publication-verification.json`: 5/5;
original Git streams in publication-verification-01/; checker publication-verify.mjs.
Fresh separate live stdout in chat transcript only. Publisher pre 21/21,
stage 16/16, commit 19/19, push 6/6, post 13/13 inspected, not rerun;
all snapshots match publisher; no failed phase. Cosmetic loose-list spacing
accepted without closure edit. Largest-coherent-slice policy applies.

WF-03H `8a4223d5`, sole parent `de8f1a72`, independently verified per
`logs/planner-review/wf-03h/publication-verification.json` (5/5, original child
streams retained). Review: `logs/planner-review/wf-03h/review1/decision.md`;
independent 5/5, freeze-02 4/4; freeze-01 wrong-path failure retained.
Executor final 23/23, negative baseline 16/16, controls 22/22 inspected, not
rerun. Dev-01 C10 absolute-offset defect fixed without document change.
Generate-01 output files overwritten: only hashes/tool/streams survive.
EX-H01 accepted: current-work 7399 bytes, exact 3466-byte History/risks;
EX-H02 turn-limit routing accepted; EX-H03 stale HL691-692 and prior G wording
deferred, unchanged, unreconciled and not endorsed. No missing streams recreated.
G/F/E/D limits remain through their review decisions: G review1, F review1 and
publication verification (publisher source hash only), E review2/review1,
D review1 (N05, not N06, is the broken-fragment control), under
logs/planner-review/<slice>/. Scans/coordinates lexical; Git/disk/live have no
negative controls. Earlier product/provider/qualification evidence not rerun.
No reopening, republication or closure edit of delivered work.

## Selected slice: WF-03I publication GO

GO for seven documents frozen in
`logs/planner-review/wf-03i/publication-manifest.json`; sole parent
`b81f761173f52e160acd9141df612b6c2a9c2812`. Subject:
`docs: extract guidance and escalation contracts`. No next slice selected.
Five guidance/escalation/diagnosis sections moved; exactly one IL957 reference
repair. All 66 protocol headings retained; baseline reconstructed exactly
(135584 bytes). A-H map records, format-repair body, current-work History/risks
(3466 bytes) and earlier evidence-limit suffix are exact. Base correction,
budgets, manifests and local delivery remain in the protocol, unreconciled.

Review: `logs/planner-review/wf-03i/review1/decision.md`.
Independent verify-02: 5/5; original tools/results/stdout/stderr retained.
235 local links, five in-protocol and 14 other inbound links pass lexical
checks. Verify-01 was 4/5: Git ENOBUFS during a large historical baseline read;
verify-02 compares consumer Git identities instead. Parent failure retained;
child's buffered/truncated output was not separately retained or reconstructed.
Executor final 24/24, negative baseline 17/17 and controls 27/27 inspected,
not rerun. Current delivery inputs and all checker/tool snapshots match hashes.
Fresh independent live lookup matched b81f7611; original stdout only in chat
transcript. Git/disk/live have no negative controls; scans/coordinates lexical.
No product, browser, provider or qualification checks run.

EX-I01 resolved: dev-01 caught a real live-link defect in the I record;
corrected before dev-02 with unchanged checker; first-appended fragment and
failed evidence retained. EX-I02 accepted: article omission follows the exact
planner-authorized substitution, preserves meaning and target; no cosmetic
correction requested. EX-I03 accepted: current-work 8075 bytes (+1931 over
6 KiB, +676 over H), with exact risks and earlier limitations; no forced trim.
EX-I04 accepted limitation: reconstruction alone cannot catch an omitted repair;
C02/C17 and N05 cover it; independent R01 separately checks the precise line.
No blocking findings. Freeze-01's draft manifest had the prior slice label;
retained as publication-manifest-draft-01.json; freeze-02 corrects and checks
the metadata. Generate-01 outputs not retained, only hashes/tool/streams;
map-append, current-work splice and inline map-fix console output exist only
in executor chat, not original standalone streams. Missing streams not recreated.

This GO replaces dispatch checkpoint b8bf291f...4780 (8326 bytes); executor G02
checked that version only. Six executor documents retain reviewed bytes.
Publish exactly seven manifest paths, mode 100644, normal main fast-forward
push to origin/main and live verification. Same Claude chat publishes; no
closure edit or extra approval. Stop for material change, failed push or
divergence; no force/history reconciliation. Codex verifies publication.
WF-03 remains incomplete; later slices unselected.

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
