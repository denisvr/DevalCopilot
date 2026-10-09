# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md),
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Current decision (2026-10-09): GO for outcome-aware preparation proofs

Codex grants publication GO for the reviewed test/support-only slice: prove
actual concurrent local-commit preparation outcomes, exact artifact identity,
completed cleanup and restart isolation. Claude remains executor. GO applies
only to the final frozen manifest supplied with this decision, including this
planner edit and the two factual ledger wording corrections. A material change
returns for review. No next slice, production fix or increment/MVP completion
is authorized.

### Independently verified state and scope

Branch main; HEAD, local origin/main and independently queried live
refs/heads/main are c12c7dd5b9c53064be66bd998d966659fdbd9822. Index empty;
18 status paths: 10 modified and 8 untracked. All execution changes are under
tests/DevalCopilot.Api.IntegrationTests/Features/Runs/LocalCommit; the other
paths are current-work.md and this planner record. No production, port,
migration, API, generated client, frontend, package, Git/native/lock,
authorization, reservation, artifact-layout, cleanup-policy or recovery
implementation changed. ADR-0029/0030/0031 remain unchanged. Client SHA-256:
22074a066c91a1fb1dfc0afc602573f14e72f9c6ffd8047633b2640c20e20b64.

The prior abandonment publication was verified before selection: substantive
f381c0c4af00b726c0d4ef7bbe6fbf2af32447c8, closure c12c7dd (current-work.md
only). Its full Api result of 1378 passed and 1 failed stays recorded; neither
the historical cause nor the event order was established.

### Architectural judgment and remaining production finding

The reviewed tests establish two actual Prepared results with distinct
captured artifact paths before releasing admission. They pin the admitted
result's own commit/tree/index and database facts, observe real cleanup after
return with operation/artifact/result identity, and prove only its recorded
leaf is removed. The other successful artifact remains byte-identical after
execution and startup recovery. Separate induced CheckpointNotCurrent
controls prove one or two refusals grant no artifact or admission authority.
Blocking-fake regressions distinguish method entry, outstanding work,
refusal, exception and successful completion; exact captured sequences own
the bounded, failure-safe admission gates. Existing seam/restart/lifecycle
assertions remain; the callback adaptations are mechanical.

The executor reported a separate first-use production race: concurrent
check-then-write initialization of empty.gitconfig can throw IOException and
be surfaced as HooksDirectoryNotEmpty. Codex inspected the unchanged storage
method and the fixture's explicit initialization. The executor's fresh-root
probe (12 false results in 300 trials) remains executor evidence; Codex did not
rerun that probe and it does not reconstruct the historical Api failure.

Codex accepts sequential initialization as an explicit precondition of these
two-success proofs. It initializes only this fixture's owned storage using the
production method; the two preparations still run concurrently. It neither
manufactures successful results, retries refusals, relaxes artifact assertions,
suppresses hooks checks nor changes production behavior. This is conditional
coverage of initialized storage, not a fix or proof of concurrent first use.
The known availability race remains open and fails closed. Correcting it is a
follow-up candidate, not selected work or permission to expand this slice.

### Acceptance evidence

Codex inspected all changed/new source and the ledger, confirmed the exact
18-path scope and unchanged client, and independently ran sequentially:

- Serial solution build --no-restore -p:UseSharedCompilation=false -m:1:
  0 warnings, 0 errors.
- The five affected competition/refusal/preparer/gate/cleanup classes:
  25 passed, 0 failed, 0 skipped.
- Full Api --no-build --no-restore: 1402 passed, 0 failed, 0 skipped
  (5 minutes 30 seconds).
- Full Architecture --no-build --no-restore: 47 passed, 0 failed, 0 skipped.

No reviewer run failed or was repeated. git diff --check and the 18-path
NUL/trailing-whitespace/blank-final-line scan passed. This GO does not claim
fresh full Domain/Application/Infrastructure, frontend/browser, audits or
repository-wide formatter runs by Codex. The ledger separates executor-fresh
results and ten rebuilt, restored mutations from retained delivery evidence.
The real-Git integration cases cannot independently detect premature cleanup
signals because real cleanup is synchronous; the blocking unit controls do.

### One publication instruction

Verify main, the exact baseline above, local/live remote, empty index, exact
18-path inventory and every raw SHA-256/size/status against the frozen reviewed
manifest; do not regenerate it or edit reviewed bytes. Stage only those paths,
check the exact 10 M/8 A staged inventory with --no-renames and cached whitespace,
then commit the substantive slice with current-work.md and this GO record.
Push main to origin/main normally, without amend, force or history repair;
fetch and independently query the live ref, requiring all three refs equal
the substantive SHA and a clean checkout.

Against that published commit, use fresh logs and run sequentially once:
the serial solution build, full Api (1402), and full Architecture (47).
Check exit codes, failures/skips, client hash and clean Git state. Preserve
any failure and stop without an unchanged rerun or closure. No additional
browser/provider, frontend or unrelated full-suite execution is required for
this test/support-only publication; retain earlier results with their scope.

Only after all required checks pass, make one tightly bounded factual closure
inside this slice's current-work.md entry: substantive SHA, verified publication,
actual commands/counts/skips and fresh-versus-retained evidence. Mark historical
planner hashes/status wording accurately; preserve all failures, the first-use
race and every remaining limit. Change no other path and embed no closure SHA.
Commit/push that one-file closure normally and verify local/live equality and
clean checkout again. Report both full SHAs/parents and evidence. Stop for
planner selection; no next slice is authorized.
