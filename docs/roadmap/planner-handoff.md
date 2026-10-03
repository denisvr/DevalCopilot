# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md), [current-work.md](current-work.md), the
[roadmap](mvp-delivery-plan.md), [engineering context](../engineering-context.md)
and accepted [ADRs](../decisions/README.md). Git and code prevail over summaries.

## Selected slice (2026-10-03): direct guidance for diagnosis-origin corrections

### Current review decision (2026-10-03): GO for the complete reviewed diff

- Codex accepts R1 and gives publication GO for the complete diagnosis-correction
  direct-guidance slice: 36 modified tracked files and eight untracked files,
  exactly 44 files including this planner record and current-work.md. The earlier
  NO-GO below is resolved; it is retained as the preceding review checkpoint.
  Claude publishes in the same executor chat. No next slice is selected.
- Independently verified main, HEAD, local origin/main and live refs/heads/main at
  681b7509da28aa91825fe7ca209096234ddf9804; nothing staged. The implementation,
  tests and generated client agree with the previous review. R1 changed only the
  cockpit specification and current-work.md, correctly recording the newly added
  8-KiB endpoint body limit and retained validation. Codex made one factual wording
  correction in the cockpit specification: the form sends the raw draft, and the
  server normalizes accepted text. No executable behavior was changed.
- Accept the documented failed-diagnosis-read behavior: it removes the editor and
  unsent draft; a successful applicable read restores an empty editor. Pending
  refresh preserves the draft with submission disabled. The guided action remains
  bounded by the exact source and shared allowance; the exhausted action is
  unguided escalation only, with no additional authorization.
- Previous independent checks remain applicable without another code/test change:
  build and NSwag 0 warnings/0 errors; Application 247/247; Infrastructure 55/55;
  Api 91/91; Architecture 9/9; Vitest 1707/1707; typecheck/build clean; lint nine
  warnings/zero errors; harness 40/40; Chromium 9/9 and guided journey 1/1 using
  the unchanged canonical command outside the sandbox after its startup refusal.
  Full backend, audits and formatter results remain separately identified executor
  evidence. R1 is documentation-only, so these tests were not rerun for it.
- Fresh correction review: inventory/index/refs, code diff, client and planner
  hashes, changed-file NUL/trailing-whitespace, diff whitespace and documentation
  links. The publication prompt supplies the final planner hash. Current-work.md
  SHA-256 is 01f1fad672110da32bff0aa9084b9d2279e1ab299d16a90385a2e18ba50c6173;
  generated api-client.ts is 1caa1d42862910d711a93198c8a74f3388a678a051fef2e58c8be8cab4aa0ef2.
- Publication authorization is one bounded sequence: verify this baseline, empty
  index, exact 44-file inventory and supplied hashes; stage only the reviewed
  substantive files including current-work and this GO record; inspect the staged
  diff/hygiene; commit with parent 681b750; push main normally to origin/main;
  fetch and verify HEAD, local origin/main and live refs/heads/main all equal the
  delivered SHA with a clean tree. Preserve this GO record byte-for-byte.
- Run the publication prompt's post-publication checks sequentially on that SHA.
  Stop on failure, unexpected files/refs, push failure or material change. Preserve
  the failure evidence; no force push, amend, history reconciliation or change
  after GO is authorized. Material changes return for review before publication.
- After successful checks, make a separate factual closure touching only
  current-work.md: delivered substantive SHA, verified publication, actual commands
  and results, retained evidence and remaining limits. Preserve earlier failure
  evidence and do not embed the closure's own SHA. Push it normally and verify all
  three refs and clean tree again. Report both SHAs and parents. Real-provider
  reliability, recovery authority and Increment 4 completion are not claimed.

### Review decision (2026-10-03): NO-GO, documentation correction only

- Independently verified main, HEAD, local origin/main and live refs/heads/main
  still at 681b7509da28aa91825fe7ca209096234ddf9804. Empty index; 35 modified
  tracked files and eight untracked files, including this record. The executor
  preserved the selected record at SHA-256 5b8dd893feb36a6b6bdac92b7d1a848a2d8a3a0f5759c0691a914a13ee4796d1
  before this review edit. No implementation or delivery-document edit by Codex.
- No functional blocking finding was found in the reviewed implementation.
  ADR-0019's bounded extension, normalized snapshot, existing dispatch/adapter
  protection, both exhaustion refusals, safe projections and owned editor agree
  with the selected outcome. Publication remains unauthorized until the complete
  corrected diff is reviewed. Continue in the same Claude executor chat.
- R1 (P2): update docs/product/run-cockpit-specification.md. Its diagnosis section
  omits the guided action, and its direct-guidance section enumerates only plan
  and ordinary-review sources/status blocks. It also promises that refresh never
  wipes a newer draft without stating this implementation's failed-diagnosis-read
  exception: the editor unmounts and loses an unsent draft. Document the exact
  diagnosis action/form, run plus diagnosis ownership, disabled pending refresh,
  failed-read removal/recovery, correctionDirectGuidance states, and unguided-only
  exhaustion with no authorization. Preserve the other controls' contracts.
- In the same documentation correction, fix the new current-work entry's
  "existing 8 KiB body cap" wording: RequestSizeLimit is newly added to this
  diagnosis-correction endpoint by this slice, matching the other guided requests.
  Keep the inventory, correction evidence and actual-versus-retained checks factual.
- Independently repeated: solution build/NSwag 0 warnings/0 errors; Application
  filter 247/247; Infrastructure DirectHumanGuidanceAdapterTests 55/55; Api diagnosis
  correction/hosted/provider-fixture filter 91/91; Architecture 9/9; frontend Vitest
  1707/1707 in 126 files, typecheck and production build clean, lint 9 warnings and
  zero errors, harness 40/40. No skips in these runs. Full backend/audit/formatter
  results in current-work remain executor evidence, not independent full reruns.
- Canonical test:e2e:all initially failed to start the host in the sandbox because
  Windows Event Log writes were denied; no browser test ran, and the blocked
  command was interrupted. The identical command outside the sandbox, without a
  composition change, passed Chromium 9/9 and the guided journey 1/1 after the
  harness passed. Existing SignalR negotiation console lines remain. Successful
  runs removed their roots. The interrupted startup left devalcopilot-e2e-2Jzi46
  (2026-10-03); its original in-memory ownership token was not recovered, so no
  marker-derived cleanup was performed. The older devalcopilot-e2e-ok3LGr root
  was also left untouched. Neither residue is part of the Git inventory.
- Generated api-client.ts SHA-256 is
  1caa1d42862910d711a93198c8a74f3388a678a051fef2e58c8be8cab4aa0ef2,
  unchanged by the independent build. Before this review edit current-work.md
  SHA-256 was 0f8263970d230dc3274c4bbee70e20a0b39a27525461e8288b297330476a4044.
  All 43 files passed NUL/trailing-whitespace checks, tracked diff whitespace
  and relative Markdown file-link checks; anchor verification remains executor
  evidence. Preserve this updated planner record byte-for-byte.
- Return only the bounded documentation correction inside the full unstaged,
  uncommitted, unpushed slice. Recheck documentation links, hygiene, inventory,
  refs and unchanged code/client hashes. No need to repeat backend/frontend/browser
  suites for documentation-only edits; label their evidence retained. A material
  code/test/contract change requires affected checks and re-review. No commit,
  push, next slice, history rewrite or publication closure is authorized.

### Verified baseline and decision

- Codex independently verified main, HEAD, local origin/main and live
  refs/heads/main at 681b7509da28aa91825fe7ca209096234ddf9804, with a clean tree
  and empty index before this planner edit. The published substantive commit is
  8c4ea7ace0a077df3dcdc1147938883c920d578e, parent 4748b83a618f9be761c2c1f47352364d2f9471c1;
  closure 681b750 changes only current-work.md. Publication and inventory agree
  with Git. The prior GO is spent on that delivery, not on the selected work.
- Select exactly one bounded Increment 4 outcome: optional advisory direct human
  guidance on the existing explicit verification-diagnosis correction request,
  from cockpit submission through immutable claim/seal, guarded dispatch and
  visible evidence. Claude implements in one new executor chat; Codex retains
  architectural judgment and GO/NO-GO. Selection grants no commit or push GO.
- The current diagnosis correction already uses the ReviewCorrection contract,
  Claude adapter, direct-guidance-capable attempt factory and manifest builder,
  but passes null for guidance. ADR-0015 supplies the existing text/snapshot/
  dispatch policy; ADR-0018 supplies diagnosis authority and shared allowance.
  This completes a useful human-instruction gap on the newly proven journey.
- Compared alternatives: the manual checkpoint-review HTTP 201/client 200
  mismatch is real but a separate, narrower contract fix. More context sampling
  adds less value here. Account observation is not account-threshold enforcement
  or invocation eligibility; Claude allowance, provider resume and compaction
  still need safe capability and authority contracts. Lifecycle completion,
  replacement and generic recovery belong to separate decisions. None is bundled.

### Objective and accepted boundaries

- Extend the existing protected diagnosis correction POST with optional guidance;
  absent/null retains the previous claim and exhaustion behavior. Reuse the
  existing normalization, valid-Unicode/600-UTF-16 bound, secret-screening policy,
  error contract and bounded request body. Supplied invalid/blank guidance is
  refused before reads or external work. Do not create another guidance policy.
- Record the normalized text in the existing immutable attempt snapshot and
  existing directHumanGuidance manifest envelope, exactly once before untrusted
  evidence. Preserve the implemented plan, ordered source inputs, diagnosis source
  notice, all source/applicability checks and the 32-KiB manifest ceiling. Guidance
  is never truncated or converted into authorization. Unguided seals and restart
  replay remain unchanged, including historical already-sealed attempts.
- Reuse the feed, fresh final dispatch snapshot guard and sealed adapter agreement;
  prove they protect diagnosis-origin corrections too. Source changes, malformed
  storage and snapshot/seal disagreement fail closed before provider invocation.
  No new provider flags, schema, effect, permission, correction slot or authority.
- A valid guided request at shared allowance exhaustion returns the existing
  direct-guidance-unavailable conflict after the existing authority gates, with no
  claim, escalation or authorization change. Apply this both before sealing and
  at the locked claim seam; remove an unused seal on seam refusal. Unguided
  exhaustion retains its existing idempotent durable escalation, including races.
- Add correctionDirectGuidance to diagnosis status using the existing fact/response
  semantics: null when no correction exists; the correction's own NotRecorded,
  Provided or Unknown fact otherwise. Keep read-only diagnosis and extra-claim
  authorization separate. History/evidence/latest-attempt projections must agree.
- Reuse the existing owned direct-guidance editor beside the unguided correction
  action, only when a correction is eligible within allowance. Bind submissions,
  pending/errors and drafts to run plus diagnosis source, with edit-version and
  newer-flow protection. Show recorded guidance as supplied context, never proof
  of provider compliance. Preserve explicit evidence refresh and its partial-
  failure guards. The exhausted escalation action sends no guidance.
- Extend the existing browser journey to submit harmless guidance through the
  rendered form and prove the native double receives the exact normalized sealed
  value/boundary. Keep all normal supervisors, authentication, adapters and owned
  cleanup, seven claims, Failed then Passed verification and sole final reload.
  Do not replace workflow steps with raw-SQL writes or log guidance payloads.
- Add ADR-0019 narrowly extending ADR-0015's request scope and ADR-0018's diagnosis
  correction request. Preserve accepted historical ADR bodies; update their index
  relationships and applicable protocol, engineering-context, cockpit and roadmap
  descriptions. No migration, dependency or unrelated endpoint repair is selected.

### Stop gates and acceptance evidence

- Stop and report if the existing snapshot/manifest contract cannot represent this
  safely without a migration, changed CLI contract or permissions; if source or
  shared-budget authority would need to expand; or if baseline/remote state differs.
  Do not infer support from CLI defaults or claim real-provider reliability.
- Real file-backed SQLite tests must cover exact normalized persistence and ordered
  inputs; invalid guidance causing no external work; absent/null compatibility;
  exhaustion and claim-seam races with no unintended escalation/claim, rollback and
  orphan cleanup; existing unguided escalation; safe projections; malformed/tampered
  guidance rejection at feed/dispatch/adapter; and immutable restart replay.
- Endpoint/generated-client tests must prove protected real HTTP request/response
  and Problem Details behavior. Frontend tests must cover current success, failed
  refresh, run/source replacement and A-to-B-to-A, unmount, retained callbacks,
  overlapping requests and edited/restored identical drafts. Preserve legitimate
  ordinary review targeting and no mutation from evidence refresh.
- Run affected checks first, then build/normal NSwag generation, sequential full
  backend suites, Architecture, frontend Vitest/typecheck/lint/build, harness and
  test:e2e:all after harness passes. Report actual commands, outcomes, skips,
  generated-client hash and hygiene/link checks. Separate retained evidence from
  checks run on this tree; preserve failures rather than rerunning to erase them.
- Return the entire unstaged, uncommitted, unpushed diff, including a commit-ready
  current-work entry and the preserved planner record, for Codex GO/NO-GO. No
  publication instruction is issued now. A future GO will cover the reviewed
  substantive commit, normal fast-forward push, live verification and a separate
  tightly bounded current-work-only factual closure. Material changes require
  another review. Increment 4 remains open.

### Initial executor preflight

Expected branch main and HEAD 681b7509da28aa91825fe7ca209096234ddf9804.
After this selection edit: no staged changes, only docs/roadmap/planner-handoff.md
modified, no untracked files. Verify local and live origin/main still equal HEAD.
Generated api-client.ts baseline SHA-256:
8234339faa80672512ab3e81c28ccd2166342716fed4d8435304424a83771bd6.
Preserve this planner-owned record byte-for-byte; the English execution prompt is
provided in chat, not duplicated here.
