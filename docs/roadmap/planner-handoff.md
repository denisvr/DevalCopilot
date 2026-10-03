# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md), the
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Current slice (2026-10-03): GO for the reviewed browser collaboration proof

### Decision and publication baseline

- Codex gives GO for the complete reviewed diff of the browser-driven local
  collaboration proof with Codex feed isolation, including R1-R5 and the completed
  R6, R6-A and R6-B corrections. Claude remains the executor in the same chat.
  This accepts the bounded implementation and authorizes publication of this
  exact substantive slice; it is not a claim that publication has occurred or
  that Increment 4 is complete. No next slice is selected.
- Independently verified main, HEAD, local origin/main and live refs/heads/main
  at 4748b83a618f9be761c2c1f47352364d2f9471c1. Nothing staged; 29 modified tracked
  files (including this planner record and current-work) and 48 untracked files:
  77 reviewed files. The earlier diagnosis substantive and fixture correction
  remain published and are not amended. Preserve the existing history.
- Before this GO edit, planner-handoff SHA-256 matched
  af441ab2d1aee1d874859520c19286e83b360375241cad76ec53786d5559d645;
  current-work SHA-256 is
  94fcc4c1bcdfda8783c37704eb35689250a67e0356f5d98990b520b43455dea9.
  Generated api-client.ts SHA-256 is
  8234339faa80672512ab3e81c28ccd2166342716fed4d8435304424a83771bd6.
  This GO edit changes only this record and preserves UTF-8 without BOM and CRLF;
  the publication prompt supplies its resulting hash. Inventory remains 77.

### Accepted outcome and boundaries

- One rendered, authenticated, explicitly requested local journey: registration,
  physical identity, real candidate worktree and checkpoint, verification recipe,
  manual objective, Codex root plan, Claude challenge, distinct Codex revision,
  Claude implementation, Failed verification, Codex diagnosis, Claude correction,
  fresh Passed verification and ordinary Codex approval. Seven Agent claims,
  one shared correction slot and unchanged budgets; approval is not lifecycle
  completion. Source checkout content/HEAD and source branches remain unchanged.
- A test-only WebApplicationFactory<Program>.UseKestrel host preserves normal
  MVC/authentication, CORS, SignalR, SQLite/migrations, readiness, all supervisors,
  mediator, real provider adapters, process executor, Git and artifact behavior.
  Existing real workspace/artifact implementations point at the verified owned
  temporary root. Only external provider executables are deterministic native
  doubles, selected through the owned PATH and verified before Agent requests.
  Fixed schema copies, closed argv/stdin contracts, bounded schema comparison,
  allowed candidate edits, allowlisted logs and reparse destination checks bound
  the doubles. Actual candidate content decides verification, not an invocation
  counter. The journey does not seed workflow rows with raw SQL.
- Two operation-owned routing predicates restore ADR-0009 partitioning: planning
  requires Codex/Planner/Proposal, resolution requires Codex/Resolver/
  ChallengeResolution. Other feeds and semantic gates are unchanged. Planning
  no longer consumes another Codex role using its Proposal schema.
- The verification claim's MVC response metadata and normal NSwag output agree
  with runtime Accepted/202; only that generated operation changed. Explicit
  Refresh evidence updates the three checkpoint consumers and the selected
  project's current run diagnosis/review GETs. No run event or sequence is
  invented, no eligibility is inferred from local verification rows, and no
  workflow stage is automatic. Pending/failed reads cannot offer ordinary review;
  a failed diagnosis read cannot revive an older fallback report. Legitimate
  fallback targeting and accepted operations remain intact.
- Project/run lifetimes own the refresh connection; code-review and diagnosis
  status reads use the existing ownership pattern. Replaced callbacks are inert,
  returning A is a new lifetime, newest reads win, and safe failure/recovery is
  explicit. Drafts and mounted subtrees survive refresh. Only the final persistence
  reload remains. No scheduler, lifecycle authority, new dependency/provider,
  permission expansion, account threshold or session-resume capability is added.

### Review evidence and accepted limitations

- Independently repeated on the final reviewed tree: normal solution build and
  NSwag generation, 0 warnings/0 errors and unchanged client hash; Application
  routing filter 12/12; Api fixture/host-destination/supervisor-routing filter
  65/65; full Architecture 9/9; full frontend Vitest 1684/1684 (125 files);
  typecheck and production build clean (existing chunk-size notice); lint
  9 warnings/0 errors; Node harness 38/38. No test skips in these runs.
- The first independent test:e2e:all attempt could not start the host because the
  sandbox denied Windows Event Log writes; no Chromium test ran in that attempt.
  The unchanged canonical command then ran outside the sandbox with normal host
  composition and authentication: existing Chromium suite 8/8, followed by journey
  1/1. No product or fixture bypass was introduced. Known SignalR negotiation
  console lines appeared on reload. No root from these runs remained; the old
  devalcopilot-e2e-ok3LGr root from 2026-10-02 was left untouched.
- Earlier independent review probes reproduced partial refresh failures and a
  replaced callback starting a GET for B. Permanent regressions and the corrected
  guards/owned hook now resolve these findings. Temporary probes were removed.
  Full Domain/Application/Infrastructure/Api results (952, 3475, 956 plus three
  existing skips, 809), audits and formatter baseline remain explicitly retained
  executor evidence; those full backend suites were not repeated in this review.
- Doubles prove local reachability and adapter agreement, not real-provider
  reliability, authentication, account allowance or session behavior. One chain
  is not a permutation matrix. Reparse checks do not prove hard-link isolation or
  concurrent alias replacement. Normal host scratch behavior remains unchanged.
- Manual RecordCheckpointReview still returns 201 while its client handles 200;
  this separate operation is explicitly deferred. Other run status hooks retain
  their existing rules and are not driven by Refresh evidence. Refresh is explicit,
  not automatic on verification completion. Neither this GO nor these tests close
  Increment 4, provide recovery authority or authorize another slice.

### Authorized publication and next action

- Before staging, verify the exact baseline, empty index, 77-file inventory and
  the three hashes supplied in the publication prompt. Preserve this GO record
  byte-for-byte. Stage only the reviewed substantive files, including current-work
  and this record; check the staged diff and hygiene before committing.
- Commit the reviewed substantive slice on main with parent 4748b83, then use a
  normal fast-forward push to origin/main. Fetch and verify HEAD, local origin/main
  and live refs/heads/main all equal the delivered SHA and the checkout is clean.
  Stop on any divergence, push failure, unexpected inventory or material change;
  no force push, amend, reconciliation or material post-GO edit is authorized.
- Run the publication prompt's specified post-publication checks on that commit.
  Stop on failure and preserve the factual evidence; no rerun-to-green or closure
  claim on an unresolved failure. Then make a separate factual closure changing
  only current-work: substantive SHA, verified publication, checks actually run,
  retained evidence and remaining limits. Do not embed the closure's own SHA.
  Push normally and verify all three refs and the clean tree again.
- Report both SHAs, parents, inventories, actual checks, remote state and limits.
  Codex owns next-slice selection after verified publication. No next slice starts
  under this GO. Any material change requires review again before publication.
