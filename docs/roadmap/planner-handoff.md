# Planner/reviewer handoff

This is the current decision checkpoint, not a delivery ledger or approval log.
Read [AGENTS.md](../../AGENTS.md) for the standing review and publication rules,
[current-work.md](current-work.md) for delivery facts, and the
[roadmap](mvp-delivery-plan.md) and accepted [ADRs](../decisions/README.md)
for product and architecture decisions. Verify this checkpoint against Git and
code before relying on it; older decision detail remains in Git.

## Current decision (2026-09-27)

- Codex owns planning, architecture, slice selection, review, and acceptance;
  Claude is the bounded executor. Routine slice selection needs no owner
  approval; committing and pushing an executor diff still requires Codex GO.
- Verified planning baseline: `main` and `HEAD` are
  `94b2cc3b6128df7d4bac22ae7f47c40a5532d2cc`; local `origin/main` and
  live `origin/main` match, and staged, unstaged, and untracked state is empty.
  Selected next bounded Increment 4 slice: disclose **provider-session resume
  eligibility for the current Claude Implementer attempt** as `Ineligible`
  only when its immutable assignment is coherent (`ClaudeCode`, `Implementer`,
  `WorkspaceEditOnly`, `claude-implementation-v1`). The existing adapter passes
  `--no-session-persistence`; the [Claude CLI reference](https://code.claude.com/docs/en/cli-reference)
  explicitly says sessions under this flag cannot be resumed. No attempt or a
  valid historical/mismatched assignment yields `Unknown`/`null`; invalid
  assignment preserves the existing fail-closed error. This is a configured
  attempt fact, not a host-wide capability or provider-observed result.
  Restrict work to the existing implementation status/API, generated client,
  implementation action, focused tests, and cockpit specification. Exclude
  CLI arguments, invocation, persistence schema, session identifiers and
  resume/open/fork actions, claim/dispatch/authorization policy, preflight,
  other roles, models, effort, context, compaction, and usage limits. Stop if
  assignment coherence or the fixed flag cannot be proved, or if disclosure
  requires a policy or invocation change. Acceptance requires positive,
  no-attempt, historical/mismatch, and invalid-assignment cases; strict API
  disclosure checks; generated-client drift verification; relevant backend
  and frontend tests, typecheck, lint, production build, and diff check.
  Claude must present the complete uncommitted, unpushed diff, including
  `current-work.md`, for Codex GO/NO-GO. No commit/push GO is granted.
- Review decision (2026-09-27): **NO-GO**, documentation correction within
  this same slice. The implementation and focused evidence match the selected
  behavior; Codex independently passed focused Application 8/8, API 9/9,
  frontend 19/19, typecheck, and `git diff --check`. However,
  `current-work.md` still describes this slice as pending, uncommitted, and
  awaiting GO, which would be false when committed with the substantive slice.
  Make that handoff concise and commit-ready: describe the slice as the current
  delivery based on parent `94b2cc3b6128df7d4bac22ae7f47c40a5532d2cc`,
  record actual checks and open risks, and name post-publication verification
  without asserting it happened or embedding the future commit SHA. Preserve
  code and tests. Review local links and run `git diff --check`; earlier test
  results remain applicable to a documentation-only correction. Return the
  whole uncommitted, unpushed diff for re-review. No commit/push GO.
- Correction re-review (2026-09-27): **GO** for the reviewed substantive
  resume-eligibility diff, including the corrected, commit-ready
  `current-work.md` and this review record. Codex verified `main`, `HEAD`,
  local `origin/main`, and live `origin/main` at
  `94b2cc3b6128df7d4bac22ae7f47c40a5532d2cc`, with no staged or
  untracked files; inspected the complete code, test, generated-client, and
  documentation diff; independently passed focused Application 8/8, API
  9/9, frontend 19/19, typecheck, and `git diff --check`; and confirmed the
  touched local documentation links resolve. The correction changed only
  `current-work.md`, so the earlier executor-reported full suites, lint, and
  build remain applicable. Claude may commit this reviewed slice on `main`,
  including `current-work.md` and `planner-handoff.md` in the same commit,
  push normally to `origin/main`, verify that the live remote points to the
  delivered commit, and report the result. A material change after GO
  requires re-review; a failed or divergent push must stop without force-push
  or reconciliation. No next product slice is selected.
- The configured Claude Implementer session-persistence slice was accepted
  after two correction rounds and published as `1d9f6b4876b43b0585ed8422d4de653966e8e8dc`.
  Codex independently passed API 307/307, focused Application 8/8, focused
  frontend 19/19, typecheck, and diff check before GO. The remote was verified;
  broader executor-reported checks are in the shared handoff. This does not
  close Increment 4.
- Previously selected and delivered Increment 4 slice: disclose the Claude
  Implementer adapter's configured permission-prompt handling on the existing
  implementation attempt status and action. For a coherent current assignment (`ClaudeCode`,
  `Implementer`, `WorkspaceEditOnly`, `claude-implementation-v1`), show the fixed
  `--permission-prompts none` configuration as `None`; show `Unknown`/`null` for
  no attempt or a valid historical/mismatched assignment, and preserve the
  current fail-closed invalid-assignment error. This is configuration evidence,
  not an observed provider outcome or authority to invoke. The current adapter
  already passes the flag; the [Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference)
  documents `none` as denying prompts in print mode. The local `claude` command
  was unavailable during planning, so installed-version behavior was not
  observed here.
- Scope is the existing implementation status projection/API contract, generated
  frontend client, implementation action, focused tests, and cockpit
  specification. Exclude adapter arguments, provider preflight, claim/dispatch
  and authorization policy, model/effort discovery, context or compaction,
  session resume, usage limits, persistence schema, and other roles. Stop if
  the fixed argument or assignment coherence cannot be proven, or if a safe
  disclosure requires changing invocation or security policy. Acceptance needs
  positive/current, no-attempt, historical/mismatch, and invalid-assignment
  cases; strict API disclosure guards; generated-client drift check; relevant
  backend/frontend tests, typecheck, lint, production build, and diff check.
  Claude must return the complete uncommitted, unpushed diff including
  `current-work.md` for Codex GO/NO-GO. No commit/push GO has been granted.
- Review decision (2026-09-27): **NO-GO**, correction of this same slice.
  The status projection and UI match the selected behavior, and Codex
  independently passed focused API 4/4, Application 8/8, frontend 19/19, and
  `git diff --check`. The new API test helper that exempts the single
  root-level `configuredPermissionPrompts` fact from its broad `prompt` leak
  scan has no negative tests. Add focused cases proving that a nested property,
  duplicate occurrence, unexpected value, and actual leaked prompt remain
  detectable by that guard; retain the coherent root-level `None`/`null`
  allowance. Keep the diff uncommitted and unpushed, run affected checks and
  relevant full validation, and return it for review. No commit/push GO.
- Correction re-review (2026-09-27): the five focused disclosure-guard cases
  close the prior finding; Codex independently passed the full API suite
  312/312 and `git diff --check`. **NO-GO remains for the delivery handoff only**:
  `current-work.md` still labels this slice pending, uncommitted, and awaiting
  GO, and still names the previous slice as the latest substantive delivery.
  Those statements would be false in the substantive commit. Claude should
  make that page concise and commit-ready: describe this slice as the current
  delivery based on parent `c2b0155f26f07bbe09ca67a9c5234228e274a703`,
  record actual checks, remaining risks, and the post-publication verification
  action, without embedding the new commit's unknown SHA. Preserve the code
  and corrected tests. A documentation-only correction needs `git diff --check`
  and link review; earlier test results remain applicable. Return the whole
  uncommitted diff for GO/NO-GO. No commit/push GO yet.
- Final review (2026-09-27): **GO** for this reviewed substantive diff,
  including the commit-ready `current-work.md`. Codex verified `main`, HEAD,
  and local `origin/main` at `c2b0155f26f07bbe09ca67a9c5234228e274a703`,
  reviewed the full diff and corrected guard tests, independently passed API
  312/312, focused Application 8/8 and frontend 19/19 across review rounds,
  confirmed local handoff links resolve, and passed `git diff --check`.
  Claude may commit this reviewed slice on `main`, push normally to
  `origin/main`, verify the remote points to the delivered commit, and report
  the result. A material change after GO requires re-review; a failed or
  divergent push must stop without force-push or reconciliation.
- Publication and handoff review (2026-09-27): the permission-confirmations
  slice was published as `01f1777af877dd0374d7e3d7e461127a02f40d73`;
  Codex independently verified live `origin/main` at that commit and a clean
  checkout before the handoff edit. **GO** for the documentation-only
  `current-work.md` closure recording that delivery and replacing its stale
  next action. Local links and `git diff --check` passed. Claude may commit
  this reviewed documentation checkpoint and push `main` normally; no next
  product slice is selected.
- Provider account-allowance evidence remains closed without delivery: neither
  local CLI offered a proven safe, authoritative, machine-readable observation
  contract. Do not add `Unknown`-only scaffolding or direct authenticated API
  access without a new accepted decision.
