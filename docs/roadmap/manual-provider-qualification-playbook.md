# Manual provider qualification playbook

This is the opt-in procedure for the one real-provider acceptance session the owner authorized on 2026-10-09: one real Codex
Planner Proposal, then one real Claude CriticalReviewer Acceptance **or** Challenge replying to that exact persisted Proposal,
through the production host, adapters, parsers and result writers, in a new disposable repository and data root. A genuine
Challenge is a valid result; nothing forces an Acceptance. The result is version-specific compatibility evidence, not provider
obedience, general reliability, account capacity or increment completion. Three launches are recorded:

- The first, on 2026-10-09, ended `Blocked` / `TargetsNotReal` before any allowance was spent.
- The second, published on 2026-10-31, ended `Failed` (exit 5) at the CriticalReviewer invocation. It spent both allowances and
  qualified nothing.
- The third followed a reset of those allowances by a new owner decision; a 2026-10-31 sequence before it had stopped at its
  build step, with no reset and no launch. The third ended `Failed` (exit 5) at the CriticalReviewer invocation again. It spent
  both new allowances and qualified nothing.

No further sequence under those authorizations is possible. See [the recorded outcomes](#recorded-outcome-of-the-only-launch) below.
A new, separately authorized procedure, [QUALIFICATION-PROFILE-01](#qualification-profile-01-prepared-offline-not-authorized-for-execution),
has been prepared offline only: it is not authorized for execution, and the launcher no longer starts the earlier session. See
[AGENTS.md](../../AGENTS.md) for the planner/executor roles.

## Recorded outcome of the only launch

This heading keeps its original name so existing links still resolve; it now records both launches. The first is the dated
2026-10-09 outcome directly below, unchanged; the second follows it.

The launcher has been run for real once, on 2026-10-09, with the opt-in. It ended `Blocked` with code `TargetsNotReal` (exit 4)
before any allowance was spent, so no qualification took place. The historical record is the bullet "Actual real-session
result" in the section "Manual provider qualification (2026-10-09)" of the frozen execution-history snapshot
[current-work-history-ba1d4c2.md](current-work-history-ba1d4c2.md); `current-work.md` does not carry it. The owner's
authorization is recorded under "Selected slice and owner authorization" in
[planner-handoff-history-ba1d4c2.md](planner-handoff-history-ba1d4c2.md).

As observed on 2026-10-09: the host's readiness supervisor reported Codex `NotObservedExecutableNotFound` (no `codex.exe` on
`PATH` or in `%APPDATA%\npm`, and no `@openai/codex` package), Claude Code as a real installed `DirectExecutable` target (observed
version `2.1.276`) and Git `2.53.0`. Both allowances stayed unused, no POST was made and no inference ran; the only provider
activity was the host's own `--version` discovery probes. That is a dated observation, not the current state of this or any
machine: whether Codex is discoverable now has not been re-observed here. A later launch is a new run that needs the planner's
or the owner's separate decision after the environment is prepared. This record changes none of the authority, allowance or
no-retry boundaries below, and it is not provider qualification.

**The failed session (published 2026-10-31).** A later, separately authorized single session through the unchanged launcher
ended `Failed` with code `ReviewerAttemptFailedProviderInvocationFailed` (exit 5). The real Codex Planner attempt recorded a
coherent `Proposal` (process exited 0) with agreeing sealed evidence and an unchanged source and worktree. The real Claude
CriticalReviewer attempt for that exact Proposal was dispatched and its process exited 1 after 129 ms with outcome
`ProviderInvocationFailed`; no valid reply was recorded. Both allowances are spent: the unchanged launcher refuses with exit 3
while either marker exists, and no reset has been selected. The child-process proof ended `OriginUnresolved`, so the session's
owned root was preserved (`ShutdownUnproven`). Nothing was retried. Providers are not qualified. Its execution record is the
historical QUALIFICATION-REAL-SESSION-01 section of [current-work.md](current-work.md); its summary is schema 1 and is not
rewritten (see [the safe summary](#the-safe-summary)).

Afterwards the planner, under a bounded owner authorization, read only that Reviewer attempt's sealed streams: stdout was empty
and a vocabulary-restricted projection of stderr reported that the `SystemRoot` environment variable was not set. The Claude print
adapters now carry that Windows prerequisite and the summary moved to schema 2; see
[Windows Claude print-mode launch environment](../architecture/agent-collaboration-provider-contracts.md#windows-claude-print-mode-launch-environment).
That diagnosis names one observed missing prerequisite, not the CLI's internal cause or the absence of others, and the repair
is proven only by controlled tests with fakes. Neither is a runtime requalification: no later real session is recorded, and one
would need new owner authority and spendable allowances.

**The stopped reset-and-session sequence (2026-10-31, after the repair).** The owner authorized an auditable archive of the two
spent markers inside the original ledger, followed by one new session. The single physical sequence passed its preflight, then
stopped 1.9 s after its start, when the planner-prepared build step failed before starting any build (more than one `dotnet.exe`
resolved in the executor's context). The reset never ran and the launcher was not started. Only ledger existence/reparse metadata
was checked; marker contents were not read or changed, no new summary or root was created, and no provider was invoked.
Both original allowances remain spent at their recorded scope, and providers are not qualified. Nothing was retried.
The planner's final review closed this physical sequence at its mandatory stop: another physical sequence requires a new
explicit owner decision, even though this one invoked no provider. Its execution record is the
QUALIFICATION-REAL-SESSION-02 section of [current-work.md](current-work.md).

**The prepared procedure and its one authorized sequence (2026-10-31).** QUALIFICATION-REAL-SESSION-03 prepared one corrected
operator procedure as an ignored, one-use package. It resolves a single first `dotnet.exe` from PATH, records its inputs as
hash-verified data and runs one ordered, deadline-bounded sequence with bounded output capture. The package was exercised offline,
corrected after each review and accepted. The owner then explicitly authorized one physical sequence, which ran once:

- The preflight, a local build with 0 warnings and 0 errors, and the source check passed.
- The reset archived the two known old markers without overwriting anything, and the receipt was validated.
- The unchanged launcher ran one session, and its whole sequence ended at elapsed 53.7 s.
- The real Codex Planner attempt recorded one coherent `Proposal` (exit 0) with agreeing sealed evidence, and the source stayed
  unchanged.
- The real Claude CriticalReviewer attempt for that Proposal was dispatched. Its process exited 1 after 535 ms with
  `ProviderInvocationFailed` and no message.
- The launcher exited 5 (`Failed`, `ReviewerAttemptFailedProviderInvocationFailed`). Its child-process proof ended
  `OriginUnresolved`, so the new root was preserved (`ShutdownUnproven`).
- The sequence then stopped at its exit gate, without the ledger comparison.

Both new allowances are spent, so the launcher again refuses with exit 3 while they exist. Nothing was retried, and providers are
not qualified. The earlier `SystemRoot` repair did not make the reviewer invocation succeed. The cause of this failure was not observed,
because the provider streams were not inspected. Its record is the QUALIFICATION-REAL-SESSION-03 section of
[current-work.md](current-work.md).

**Separately authorized post-session diagnosis (2026-10-31).** A bounded planner inspection of only the known session-03
Reviewer's sealed streams verified empty stderr and 1,174 stdout bytes. Its closed projection reported authentication-related
text; raw output was not exported. The copied-database metadata bound the two known attempts, and source database bytes stayed
stable across the reads; no WAL was present. This does not prove account state or a specific authentication cause. No login,
provider call, ledger/reset or cleanup was performed. Providers remain unqualified. Any authentication action or further real
session needs its own authority; the completed inspection grants none. See the CLAUDE-QUALIFICATION-DIAGNOSIS-02 record in
[current-work.md](current-work.md).

**One authorized authentication-status observation (2026-10-31).** Under a new owner decision, a Claude executor ran one
planner-accepted, one-use helper once. It verified the pinned native Claude identity and started `claude auth status` with the
child environment cleared and only the production policy's entries supplied (key names `SystemRoot` and `USERPROFILE`). The CLI
exited 1 with 0 stderr bytes. Total helper elapsed time was 1.084 s, including preflight/compilation; exact native duration was
not retained. The closed projection was `ReportedLoggedOut` with method `none`. Output stayed
in bounded memory and was neither displayed nor persisted. Nothing was retried, and no login or other account command, model call
or configuration change followed. This is fresh evidence about the current executor-hosted helper context only. It does not
reproduce the session-03 child environment, prove a credential or quota state, or explain the session-03 failure. Providers remain
unqualified, and any authentication action or further real session needs its own authority. See the
CLAUDE-AUTHENTICATION-STATUS-01 record in [current-work.md](current-work.md).

## QUALIFICATION-PROFILE-01 (prepared offline; not authorized for execution)

This section is the complete proposed procedure for one bounded, diagnosable first real Codex Planner / Claude CriticalReviewer
exchange, in two lots that each need a **separate concrete execution approval** after independent preparation review. Offline
preparation ran no real provider, version, help, authentication or account call, login, credential or preserved-evidence
inspection, and accessed no old ledger. Every historical allowance remains consumed. The plan is
[real-provider-qualification-plan.md](real-provider-qualification-plan.md); the implementation record is in
[current-work.md](current-work.md). The text below describes the preparation **after correction 2** of the planner's reviews 1 and 2
(QP-R1 whole-entry deadlines and finalization, QP-R2 terminal admission and expiry during validation, QP-R3 reviewed request and
executable identities, QP-R4 typed status projection); the correction is returned for review and is not accepted.

### Fresh authorization identities and allowance locations

| Lot | Authorization identity | Exclusive directory (under the OS temp directory) | Opt-in |
| --- | --- | --- | --- |
| A: two `claude auth status` observations after the owner's login | `owner-qualification-profile-01-lot-a-post-login` | `devalcopilot-qualification-profile-01-lot-a-post-login-ledger` | `--run-claude-auth-status-after-login --claude-executable <absolute path> --claude-sha256 <digest>` and `DEVALCOPILOT_MANUAL_QUALIFICATION_CONFIRM=qualification-profile-01-two-claude-auth-status-observations-after-login` |
| B: one Planner and one CriticalReviewer, bound to the launch context, Windows root carried by every Codex launch, fixed Proposal content guidance in the planning manifest, flat transport schema for the Claude reviewer | `owner-qualification-profile-01-lot-b-review-schema` | `devalcopilot-qualification-profile-01-lot-b-review-schema-ledger` | `--run-qualification-profile-01-review-schema-exchange --approved-launches <absolute path of the owner's approved-launch manifest>` (nothing else) and `DEVALCOPILOT_MANUAL_QUALIFICATION_CONFIRM=qualification-profile-01-one-review-schema-codex-planner-and-claude-reviewer` |

Each directory is created by its own lot and holds that authorization's durable slot files only. The launcher never names, reads,
enumerates, resets or archives the earlier ledger directory, except to refuse it at construction; no argument or switch selects an
authorization or a directory, and the earlier session flag and phrase are no longer recognized. The original lot A opt-in (flag
`--run-claude-auth-status-comparison`, authorization `owner-qualification-profile-01-lot-a-two-claude-auth-status-observations`,
directory `devalcopilot-qualification-profile-01-lot-a-ledger`, phrase `qualification-profile-01-two-claude-auth-status-observations`)
is retired the same way: its flag is refused with a fixed message and either phrase before any directory, ledger or process exists,
its phrase no longer confirms the post-login entry, and a ledger path whose leaf name is its directory is refused with no file
access. Its ledger and evidence are preserved and are never opened, reset, archived or migrated. The first lot B opt-in (flag
`--run-qualification-profile-01-exchange`, authorization `owner-qualification-profile-01-lot-b-one-planner-one-critical-reviewer`,
directory `devalcopilot-qualification-profile-01-lot-b-ledger`, phrase
`qualification-profile-01-one-codex-planner-and-one-claude-critical-review`) is retired the same way: it stopped during discovery with
`ProcessAdmissionRefused` (first refusal `Unlisted`, 11 Git admissions, neither role attempted; its closed summary does not identify
the exact refused request), and the planner's later `SessionSpent` result reflects those existing process slots. Its flag is refused with a fixed message (naming the replacement flag) and exit 2 before any
environment, directory, ledger or process exists, its phrase no longer confirms the replacement entry, and a ledger path whose leaf
name is its directory is refused at construction with no file access. That ledger and the root that run retained are never read,
enumerated, reset, archived, migrated or cleaned by the launcher. The context-bound lot B opt-in (flag
`--run-qualification-profile-01-context-bound-exchange`, authorization `owner-qualification-profile-01-lot-b-context-bound`, directory
`devalcopilot-qualification-profile-01-lot-b-context-bound-ledger`, phrase
`qualification-profile-01-one-context-bound-codex-planner-and-claude-reviewer`) is retired the same way. It resolved all seven pinned
launch identities and admitted every request, then one direct native Codex Planner process exited 1 after 13 seconds with complete I/O
and no proposal; the Claude reviewer was never invoked, the host stopped, and its root was preserved because descendant shutdown was
unproven. Its authorization is spent. Its flag is refused with the same fixed message (naming the successor flag) and exit 2 before any
environment, directory, ledger or process exists, its phrase no longer confirms the successor entry, and a ledger path whose leaf name is
its directory (compared case-insensitively, with or without a trailing separator) is refused at construction with no file access. That
ledger, its evidence (including the empty SQLite WAL sidecar created by an earlier read-only inspection) and the retained root are never
read, enumerated, reset, archived, migrated or cleaned by the launcher, and each session's owned root has a fresh random name, so no
retained root is reachable from the successor entry. The Windows-root lot B opt-in (flag
`--run-qualification-profile-01-windows-root-exchange`, authorization `owner-qualification-profile-01-lot-b-windows-root`, directory
`devalcopilot-qualification-profile-01-lot-b-windows-root-ledger`, phrase
`qualification-profile-01-one-windows-root-codex-planner-and-claude-reviewer`) is retired the same way. It passed launch identity and
process admission, and its one native Codex Planner exited 0 with complete I/O and a sealed final response; the application returned
`InvalidStructuredOutput` and persisted no proposal, the Claude reviewer was never invoked, and the root was retained because
descendant shutdown was unproven. Its authorization is spent. Its flag is refused with the same fixed message (naming the successor
flag) and exit 2 before any environment, directory, ledger or process exists, its phrase no longer confirms the successor entry, and a
ledger path whose leaf name is its directory (case-insensitive, with or without a trailing separator) is refused at construction with
no file access. That ledger, its evidence, the sealed response and the retained root are never read, enumerated, reset, archived,
migrated or cleaned by the launcher. The proposal-guidance lot B opt-in (flag
`--run-qualification-profile-01-proposal-guidance-exchange`, authorization `owner-qualification-profile-01-lot-b-proposal-guidance`,
directory `devalcopilot-qualification-profile-01-lot-b-proposal-guidance-ledger`, phrase
`qualification-profile-01-one-proposal-guidance-codex-planner-and-claude-reviewer`) is retired the same way. Both of its role
allowances were used: the Codex Planner produced one accepted Proposal, and the Claude CriticalReviewer exited 1 with complete I/O, the
closed diagnostic `ProviderReportedError` and no review. The verified sealed stdout (hashes matched, stderr empty) reported HTTP 400 and
a tool input schema restriction on a root `oneOf`, `anyOf` or `allOf`; the application's review schema had a root `oneOf`. That failed
attempt is outside the manual format-repair gate (a provider-reported error with a non-zero exit is not `InvalidStructuredOutput`). Its
authorization is spent. Its flag is refused with the same fixed message (naming the successor flag) and exit 2 before any environment,
directory, ledger or process exists, its phrase no longer confirms the successor entry, and a ledger path whose leaf name is its
directory (case-insensitive, with or without a trailing separator) is refused at construction with no file access. That ledger, its
evidence and the retained root are never read, enumerated, reset, archived, migrated or cleaned by the launcher. The successor lot B
entry refuses a ledger destination that is a
reparse point the same way as the post-login entry (below): as its first step inside the bounded worker (fixed code
`Refused: LedgerDestinationAliased.`, exit 2, nothing created), again before the session enumerates any marker, and before every summary
save, including the outer-deadline verdict; each read of the destination runs in the worker or an emission, never on the deadline
waiter, so a held read cannot keep the entry past its bound. The post-login entry also refuses,
with a fixed message and exit 2 and before it enumerates markers or creates anything, a ledger destination that is itself a junction,
symbolic link or other reparse point (judged from its own attributes, never followed, so an alias to the retired ledger can no longer
read as "spent"); it checks the destination first thing in the bounded worker (before the binary verification), again immediately
before the first marker enumeration and before saving any summary, so an alias that appears mid-run gets no slot and no summary file,
whether the report is the normal one or the outer-deadline verdict. No metadata read of the destination runs on the caller: a held
read cannot keep the entry past its bound, and each read in the worker is followed by a clock decision, so a late release after the
bound enumerates, starts and consumes nothing. The
check covers the destination itself, not its ancestors, and is a decision point, not a lock. Every slot (each process start and
each of the two role allowances) is one file created exclusively and flushed **before** its process or POST. Any existing slot makes
the lot spent: there is no restart, refund, archive or rearm, and a second entry starts nothing. The earlier phrase, the other lot's
phrase and the other lot's flag are each refused.

### Lot A: two status observations after login (proposed entry; QUALIFICATION-A-POST-LOGIN-01)

The original lot A completed both observations with `BothLoggedOut` and consumed its two slots (planner/owner statement; not
re-read here). The owner then reported a successful Claude Code login. This entry is the one fixed replacement: the same comparison
implementation, identity checks, ordinary and production-filtered environments, at most two serial starts and 120-second bound, and
the same closed summary (`statusSchemaVersion` 1, whose `authorization` field now names the post-login authorization), under a fresh
authorization, directory, flag and phrase. Preparing it ran nothing real; running it needs its own owner decision.

Run by the owner in an **ordinary Windows terminal** (a helper started inside an executor does not establish equivalence with it):

```powershell
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'; $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build tests\DevalCopilot.Api.IntegrationTests\ManualQualification\Launcher\ManualQualification.csproj -warnaserror -p:UseSharedCompilation=false -m:1
$env:DEVALCOPILOT_MANUAL_QUALIFICATION_CONFIRM = 'qualification-profile-01-two-claude-auth-status-observations-after-login'
& .\tests\DevalCopilot.Api.IntegrationTests\ManualQualification\Launcher\bin\Debug\net10.0\ManualQualification.exe --run-claude-auth-status-after-login --claude-executable '<owner-approved absolute path of claude.exe>' --claude-sha256 '<64 hex digits>'
Remove-Item Env:\DEVALCOPILOT_MANUAL_QUALIFICATION_CONFIRM
```

- **Identity.** One executable, named `claude.exe`, an absolute normalized path to a real file (no alias), at most 1 GiB, whose
  SHA-256 equals the digest the owner supplies. The last recorded native identity (235,169,440 bytes, SHA-256
  `A863895D829A246D97C1B881FAE85593AAB97B5B7EA5436AD964798F5F48A022`) is retained history and was **not** re-verified here; the owner
  confirms the path and digest at approval. Nothing is searched, resolved through a profile or cache, or substituted. The identity
  check reads only that file's bytes.
- **Processes.** At most **2** child starts, serial: first `auth status` with the launching terminal's own environment (every
  variable except the operator's confirmation variable), then `auth status` with the map built by the **production** Claude environment
  builder itself (reached by one narrow reflective call pinned by a test; nothing is duplicated). Direct typed arguments, no shell, no
  terminal, no input, the shared bounded process adapter, one new disposable working directory. No model, version, help, login,
  logout, setup or other command exists in the inventory. If the production builder refuses, nothing starts and the stop is reported.
- **Deadlines.** 30 s execution per child, plus the adapter's unchanged 5 s post-kill wait, 5 s I/O window and 1 s release window;
  100 s for the serial sequence (a running child is then cancelled). One monotonic 120 s budget starts as the entry's first
  statement and covers the binary verification, the root, the filtered-map preparation, both observations, the closure of the root,
  the summary, its save and the reporting of any refusal, error or expiry. The last 5 s of it are a reserve held back for that
  reporting, so the work and its report are decided at 115 s and the entry returns no later than 120 s: no wait is added after the
  budget. The waiter never runs the work it waits for, and it reads the admission progress from a snapshot that never waits for work
  in flight, so a blocking preparation, a held output sink or a slow identity check cannot keep the entry from returning. At the
  decision instant the entry prints a fixed unproven verdict (exit 8) and starts nothing more: the admission gate closes by the
  clock at its next decision, after every blocking step and at the dispatch handoff, no continuation removes the root or rewrites
  the report, and an observation whose slot was consumed or is being consumed is reported `StartedUnproven`, never `NotStarted`.
  The summary is printed first, then committed, and only then saved, so a summary is never persisted after the bound decided
  otherwise. A sink call already in progress cannot be revoked: its bytes are non-final and unproven (the exit code and a fixed error
  line say so) and no physical suppression of that call is claimed. The bound is a decision point of the launcher under a responsive
  operating system: it does not pre-empt a blocked operating-system call and proves nothing about a descendant process.
- **Bound request and identity.** The status inventory binds the exact executable and `auth status`, the entry's own working
  directory and its approved root, the deadline (at most 30 s), no standard input and no sink. The filtered context may carry only
  `USERPROFILE`, `CLAUDE_CONFIG_DIR` and `SystemRoot`; the ordinary context is the launching terminal's own environment by design
  (its names are not constrained and its values are never read or kept). The claude.exe digest is verified again immediately before
  each of the two starts, so a replaced file is refused with a closed drift code.
- **Reads and writes.** Reads: the one claude.exe file (identity only); the launcher's own environment in memory. Writes: two
  slot files, one `status-summary-<session>.json` (created exclusively), one owned `devalcopilot-e2e-status-*` directory under the
  temp directory, removed only when every started child exited with complete I/O.
- **Closed projection.** Parsed in memory from at most 16 KiB per stream: the boolean `loggedIn` (it must agree with the documented
  exit code, 0 authenticated and 1 logged out), the method name mapped to `none`, `claudeAi`, `console`, `other` or `unreported`, and
  `configDirectory` reduced to a one-way fingerprint used only to derive `Equal`, `Different` or `Unknown`. A recognized member that
  is present with any other type (a number, `null`, a boolean, an array or an object) or duplicated makes the whole observation
  `Unknown`, so it cannot be reported as authenticated; only an absent optional member is distinct (`unreported`, fingerprint
  `Unknown`). Anything else, including other exit codes, truncation or incomplete I/O, is `Unknown`. Physical outcome, exit code,
  duration, I/O completion and the comparison flags are retained; raw output, e-mail, account or organization data, paths and
  environment names or values are never printed or stored. The summary reports only four closed booleans and a count per context
  (user profile, configuration-directory variable, any `ANTHROPIC_` variable, any `CLAUDE` variable).
- **Decision** (exactly the plan's table; none activates anything or claims matching credentials or model access): both authenticated
  supports preparing B; ordinary authenticated with filtered logged out is a context discrepancy to review before B; both logged out
  stops (owner-managed prerequisite); the reverse discrepancy, unknown, malformed, timeout or incomplete I/O stops with no second
  comparison. The sequence stops at the first observation that is not coherent.
- **Exit codes.** 0 both authenticated and root removed; 2 refused (opt-in, argument, retired original flag or ledger, or binary
  identity or an aliased ledger destination; nothing created); 3 spent;
  5 any other completed or stopped observation; 7 unexpected; 8 outer deadline; 9 the observations completed and were printed but the
  summary could not be persisted (see the summary paragraph below).

### Lot B: one read-only exchange, bound to the launch context, Windows root on every Codex launch, fixed Proposal content guidance, flat Claude review schema (proposed entry; CLAUDE-REVIEW-SCHEMA-01)

The first, the context-bound, the Windows-root and the proposal-guidance lot B entries are spent (see the retirement paragraph
above). This entry is their one fixed successor: the same exchange, the same process inventory, role vectors, deadlines and evidence
rules, the same **launch-context check** described below in bounded preparation, the same Windows `SystemRoot` carried by every Codex
launch (see [Windows native Codex role launch](../architecture/agent-collaboration-provider-contracts.md#windows-native-codex-role-launch))
and the same fixed `proposalContentGuidance` member in the Codex planning manifest (see
[Proposal content guidance](../architecture/agent-collaboration-protocol.md#proposal-content-guidance)), plus one production repair it
carries: the Claude CriticalReviewer now uses contract `claude-critical-review-v3`, whose one transport schema is a flat, closed object
(`decision` and `summary` required, `decision` enumerated as `accept` or `challenge`, `rationale` and `challenges` optional in the
schema, `minItems` 1 kept, and no `minLength`, `maxLength` or `maxItems` anywhere) with the branch exclusivity and the current bounds
described in the property text only, because the previous root `oneOf` was rejected by the provider with HTTP 400 and the tool input
does not accept those three keywords. The same document is the `--json-schema` argument and the `expectedOutputSchema` of the
ordinary and the repair manifest (see
[transport schema](../architecture/agent-collaboration-provider-contracts.md#claude-code-critical-review-cli-safety-contract)). The exact
union and every content and length rule stay in the parser and the Domain policy, the payload keys, envelope, role restrictions and
dialogue protocol are unchanged, and a v1 or v2 attempt is refused at dispatch before any process. The proposal-guidance run showed
the problem it answers. The schema is a transport description: offline tests cannot establish that the provider accepts it, and a real
exchange stays a separate decision. The 179-start ceiling, the 35-minute bound and the two-role sequence are unchanged. It is prepared
offline only and not authorized for execution.

```powershell
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'; $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build tests\DevalCopilot.Api.IntegrationTests\ManualQualification\Launcher\ManualQualification.csproj -warnaserror -p:UseSharedCompilation=false -m:1
$env:DEVALCOPILOT_MANUAL_QUALIFICATION_CONFIRM = 'qualification-profile-01-one-review-schema-codex-planner-and-claude-reviewer'
& .\tests\DevalCopilot.Api.IntegrationTests\ManualQualification\Launcher\bin\Debug\net10.0\ManualQualification.exe --run-qualification-profile-01-review-schema-exchange --approved-launches '<absolute path of the owner-prepared approved-launch manifest>'
Remove-Item Env:\DEVALCOPILOT_MANUAL_QUALIFICATION_CONFIRM
```

**Approved-launch manifest.** Without it the entry refuses (`Refused: ApprovedLaunchesRequired.`, exit 2) before a host, directory,
ledger entry or process exists. It is a closed JSON document, at most 32 KiB, at an absolute normalized path:
`{"approvedLaunches":[{"capability":"git","path":"<absolute file>","sha256":"<64 lowercase hex>"}, ...]}`. Each entry names exactly
`capability`, `path` and `sha256`, plus `scriptPath` and `scriptSha256` together for a Node-hosted script launch; an unknown, missing,
duplicated or mistyped member, a relative or non-normalized path, an uppercase digest or an alias file is refused with a closed code.
`git`, `codex` and `claude` are required; `node` is required exactly when Codex or Claude is a script launch (and then Codex's or
Claude's executable must be that same Node file and digest); `dotnet`, `gh` and `docker` are listed only if the owner approves them,
and an installed tool that is not listed is never probed (the launch-context check below refuses it as extra before anything starts; the
runtime admission refuses its first probe as unlisted if it ever got that far). Each listed file must be a
regular, non-alias file of at most 1 GiB whose SHA-256 equals its pin when the manifest is loaded, and again **immediately before
every start that launches it** (executable and script alike); a replaced or removed file is refused with a closed drift or
not-a-file code and stops the lot. The same approved absolute Git file is used by the launcher's own Git commands and by the host
(never a name searched through `PATH`). The host's own readiness evidence for Git, Codex and Claude must name exactly these approved
tuples, or the target judgement fails and no Git or role shape is ever listed. The manifest grants launch identities only, never a
command, argument or capability. It protects against a file that differs from what the owner pinned; it is no protection against a
malicious but approved executable, or against a file the same user alters between the check and the launch. The format's unit
tests use owned stand-in files only; the controlled rehearsal of the production composition additionally reads, in memory, the exact
regular files of the machine's real Git, .NET, Node and Docker that its existing local-tool resolution selects (real tool
boundaries; Codex, Claude and `gh` are owned doubles). **No installed Codex or Claude identity was read or hashed**, and the owner's
actual paths and digests remain a future approved preflight.

**Launch-context check.** The first lot B stopped on an unlisted request, and a read-only control showed that the production target
resolver selects different launches in different launch contexts: in one shell the direct Codex executable the manifest pinned, and
with the configured Windows machine and user `PATH` a Node-hosted Codex script. So, **after the manifest verifies and before any
fixture root, Git probe, child process, ledger entry or slot exists**, inside the entry's bounded preparation, the launcher runs the
actual production resolution step (the private `ToolDiscoveryAdapter` resolution, with the catalog's own descriptor, in the current
process environment) once for every catalog capability, without any probe, and compares each result with the manifest:

- A direct executable must be the approved executable of a manifest entry with no script; a Node-hosted script must be the approved
  Node executable **and** the approved script of an entry that lists both. So a direct pin never matches a script resolution, a script
  pin never matches a direct one, and both paths of a script launch must agree (paths compare case-insensitively, exactly as the
  runtime admission does).
- A resolution that is **missing** for an approved tool, **extra** (an installed tool the owner did not list, which the host would
  probe and the admission would refuse as unlisted), **ambiguous** (the production resolver found more than one distinct target, for
  any capability) or **different** from the approval refuses with a closed code `LaunchContext<Capability><Missing|Extra|Ambiguous|Mismatch>`
  (capability as the catalog names it, for example `LaunchContextCodexCliMismatch`), exit 2 and a single `Refused:` line. A resolver
  that cannot be bound or answered refuses with `LaunchContextResolverUnavailable`. No path, digest, file name or environment value is
  printed. The first refusal in the catalog's order (Git, Codex, Claude, GitHub, .NET, Node, Docker) is the answer.
- The resolver is internal to the production Infrastructure assembly, whose source is outside this lot, so the binding is by
  reflection against the exact expected shape (type names, parameter types, result members and the two target types). Any difference
  is a refusal; the launcher never carries a copy of the resolver's rules. A test pins that the binding finds the production
  resolver's shape.
- The deadline is asked before every resolution and again before its answer is used; after the bound nothing further is resolved,
  composed, probed or consumed. The resolution runs in the bounded worker, never on the deadline waiter.

The check is a decision at one moment in the launching process's environment, not a lock: the host resolves again at each probe, and
the **runtime admission checks, the 179-start inventory, the two role calls, the 35-minute outer bound and the evidence rules are
unchanged** and still refuse every request as before. A mismatch it catches is a refusal of the entry, not a provider result; a
match proves only that the launcher's resolution agrees with the approved manifest, not that any tool works or is authenticated.

The session is the one described under [What one session does](#what-one-session-does), with these preparation changes. Its adapter
contracts are pinned as `codex-planning-v1` and `claude-critical-review-v3` (the latter keeps its complete restrictions and
`max-turns=1`); `maximumAgentAttempts=2` and `maximumAgentInvocationMinutes=20` are unchanged. Model defaults, permission profiles,
safety flags and the allowlist are untouched, and no model-catalog call is made.

**Process inventory.** Every process request of the host (through its one production process adapter) and of the launcher passes a
qualification-only admission boundary. A class matches the **whole request**, not a name or a first argument: the approved
executable (and script), the complete argument vector with only the reviewed dynamic members constrained, the environment variable
**names** (never values), the deadline and every working directory, approved root, output sink and fixture or worktree destination,
which must lie inside the session's one owned root. It must match exactly one listed class; every approved file it launches must still
be exactly its pinned identity; and the class and the authorization total must still have a durable slot, which is consumed before the
process exists. An unlisted, ambiguous, drifted or exhausted request is refused with a closed code and creates no process.
**The gate is terminal.** The first refusal, or the whole-entry deadline, closes it atomically with the decision that caused it: the
first reason is kept (`closedBy` in the summary), consumed slots stay consumed, and no later request of any producer, including a
background refresh or a detached continuation, is ever admitted (they are counted as `AdmissionClosed`). The deadline is rechecked
after the identity verification, after the durable marker is written and at the dispatch handoff immediately before the process
would be created; a request whose marker was already consumed when the gate closed keeps the marker, never starts, and is counted as
`ExpiredBeforeStart`. Progress (`pending` classes in flight, admitted, refused, `closedBy`) is read from a snapshot that never takes
the admission lock. Because every later Git
command is then refused too, the final source inspection becomes unavailable, which is reported as unproven (`SourceUnreadable`) and
preserves the root; a closed gate always requires that inspection, even before any allowance was spent.
The three production observers that start a Codex `app-server` through their own process boundary (account allowance, model catalog,
account-usage dispatch guard) are answered as unavailable observations after a counted refusal: the guard calls its observer only
when a usage stop is configured, which this run never configures. Any call to one of them is itself an unlisted launch that closes the
gate; no production observer is ever silently restored. Git and role shapes are listed only after the target judgement has accepted
the host's readiness evidence as exactly the approved tuples.

**Reviewed role vectors.** Codex: the approved launch (a Node script, if approved, as the first argument) followed by exactly
`exec --json --output-schema <schema> --output-last-message <result> --sandbox read-only --cd <workspace> --ephemeral
--ignore-user-config -`, with the working directory and approved root equal to `<workspace>` (inside the owned workspaces
directory), standard input supplied, environment names within `USERPROFILE`, `CODEX_HOME` and `SystemRoot`, and a finite deadline of
at most **10 minutes** (the reviewed invocation limit of both roles, `AgentClaimPathPolicy`; the run's 20-minute
`maximumAgentInvocationMinutes` is a different, unchanged setting). `<result>` and both output sinks must be the
`final-response`, `stdout` and `stderr` files of ONE attempt of this session's run in the artifact layout
(`artifacts\runs\<run>\attempts\<attempt>\...partial`), and `<schema>` must be that same run's and attempt's file
`%LOCALAPPDATA%\DevalCopilot\agent-scratch\<run>\<attempt>\schema.json`, the exact existing production scratch location, whose raw
bytes are read at admission and must equal the serialized production proposal contract (a byte-order mark or any other difference
is a different contract). An arbitrary absolute file named `schema.json` never qualifies, and no production file is moved. There is
no model, effort, bypass, full-auto, sandbox or other member. Claude: the approved launch followed by exactly the production critical-review vector (`--print --input-format text
--output-format json --json-schema <the production critical-review schema> --safe-mode --restricted --disable-slash-commands
--no-chrome --permission-prompts none --tools "" --strict-mcp-config --permission-mode plan --no-session-persistence --session-id
<GUID> --max-turns 1`), with the same workspace, attempt-bound sink, input and ten-minute deadline rules and environment names within `USERPROFILE`,
`CLAUDE_CONFIG_DIR` and `SystemRoot`. Any added, removed or changed member (for example `--tools Bash --max-turns 99` or
`--dangerously-bypass-approvals-and-sandbox`) is unlisted. Host Git additionally requires the hardening prefix, environment names
within `GIT_TERMINAL_PROMPT`, `GIT_OPTIONAL_LOCKS` and `GIT_CONFIG_NOSYSTEM`, a deadline of at most 30 s and destinations inside the
owned root (the worktree path, a plain branch name and a 40-hex commit); the launcher's own Git requires a working directory inside the
owned root, a 60 s bound and its constrained environment. Discovery probes are `--version` of an approved tuple only, with at most
`SystemRoot` in the environment, a 5 s deadline, and the exact production tool-discovery scratch directory
(`%LOCALAPPDATA%\DevalCopilot\tool-discovery-scratch`) as both working directory and approved root; no other directory qualifies.

**The launcher's own Git environment.** Its child starts from an empty environment: no ambient Git variable (`GIT_DIR`,
`GIT_WORK_TREE`, `GIT_EXEC_PATH`, `GIT_ASKPASS`, `GIT_SSH_COMMAND`, `GIT_CONFIG_PARAMETERS`, ...), `HOME` or `PATH` reaches it. Only
`SystemRoot`, `GIT_OPTIONAL_LOCKS=0`, `GIT_TERMINAL_PROMPT=0`, `GIT_CONFIG_NOSYSTEM=1`, `GIT_CONFIG_GLOBAL=NUL` and the Git
configuration-count variables are set, pinning `core.hooksPath=NUL`, `core.fsmonitor=false`, `core.untrackedCache=false` and
`protocol.allow=never`; no argument of any counted command changes. The admission requires exactly those pinned variable names in the
launcher Git shape, so a hook, helper, global or system configuration cannot add behavior outside the counted procedure. The host's Git
environment is bound by name as before. No isolation from a hostile Git executable is claimed.

**Git version prerequisite.** `GIT_CONFIG_GLOBAL` and the configuration-count variables are honored from Git 2.32.0 (2.31 added the
environment-fed configuration mechanisms but does not handle `GIT_CONFIG_GLOBAL`), and a file digest identifies a file without proving
any flag support. So, **before the launcher's first fixture mutation** (`git init`), the launcher makes **one** bounded request of the
exact approved Git file: `--version`, no shell, an environment of at most `SystemRoot`, the exact production tool-discovery scratch
directory as working directory and approved root, **one monotonic 5 s deadline** covering the child's exit and the completion of
**both** redirected streams, and at most **512 bytes on each stream** (enforced while reading, in small blocks, with no unbounded
buffering: the first byte beyond the cap on either stream makes the observation oversized and stops it; standard output is returned to
the gate, standard error is only counted and discarded, and neither is persisted or logged). It is admitted as the already budgeted
`discovery.git` class (so it is one of that class' eight starts and counts toward the 179 maximum; with the host's own probes the class
is expected to show at least two starts in a normal run, and the five-minute refresh timeline still fits inside the ceiling). The
observation must be one coherent line `git version <major>.<minor>.<patch>[.suffix]` of **at least 2.32.0**. An unknown or malformed,
older, failed, timed-out, non-zero, truncated or incomplete observation refuses the session as Blocked before any fixture Git
command (closed codes `GitVersionUnknown`, `GitVersionTooOld`, `GitVersionObservationFailed`, `GitVersionIncomplete`,
`GitVersionTerminationUnproven`), with no fallback, retry or ignored flag, and spends nothing. A stream that has not ended when the
one deadline arrives (a pipe held open by a descendant, for instance) is refused as incomplete and, because cleanup cannot prove that
the descendant ended, as termination-unproven; a child that outlives the deadline is killed once and confirmed for at most 250 ms
(inside the cleanup only, never a second observation wait), and a child that cannot be confirmed is reported as unproven. Expiry is
terminal: the observation is accepted only when the one wait established that the child exited and both streams reached their end
without overflow **and** the absolute monotonic deadline was still open when that result was read, so a wait that timed out, had no
time left to start, or returned a success index after the deadline (a caller descheduled between computing the relative timeout and
waiting) refuses even if everything happened to finish before the next status read. A child that could not be started, or no process
returned, reports no completed I/O (it already refuses as a failed observation). The process facts (exit, code) are sampled at the
decision; the I/O and termination facts are measured after the one cleanup. The failure facts are independent: overflow (the excess is read and discarded,
never stored), unfinished I/O (true "I/O complete" only when both readers successfully reached the end of their stream) and
unproven termination (kill unconfirmed, or the root exited while a pipe never ended) are each reported on their own, so an overflow
on one stream does not hide a descendant still holding the other. The minimum is a syntax prerequisite for this profile only: it is neither
universal Git compatibility nor a security qualification, and the observer creates the production scratch directory if it does not
yet exist (the same directory production creates for its own probes). The preparation tested this only with doubles, plus the real
`git --version` the controlled rehearsal now performs as one more local-tool boundary.

| Class | Most starts | Exact shape |
| --- | ---: | --- |
| `discovery.git`, `.dotnet`, `.node`, `.gh`, `.docker`, `.codex`, `.claude` | 8 each, 56 in all | the `--version` probe of the owner-approved tuple of that capability (for an approved script launch the Node executable, the approved script, then `--version`); a capability the owner did not approve matches nothing; first probe plus one refresh every five minutes inside the 35-minute bound |
| `role.codex.exec`, `role.claude.print` | 1 each, 2 in all | the approved launch followed by exactly the reviewed vector above; no retry, repair, Resolver, fallback, resume or compaction |
| `launcher.git.init`, `.config`, `.add`, `.commit` | 1 each | the five-command fixture creation (its final `rev-parse HEAD` is counted in `.head`) |
| `launcher.git.head`, `.headref`, `.refs`, `.index` | 10, 9, 9, 9 | the nine complete read-only snapshots (original, baseline pair, three observations of the source and the worktree) |
| `git.revparse.gitdir`, `.bare`, `.toplevel`, `.commondir`, `git.symbolicref`, `git.revparse.head`, `git.status.porcelain` | 3, 2, 2, 4, 4, 4, 2 | registration, identity recheck and workspace preparation (read-only) |
| `git.worktree.add` | 1 | the one worktree creation |
| `git.evidence.revparse`, `.status`, `.diff` | 14 each | the bounded evidence capture around each role |
| `git.evidence.hashobject`, `.lsfiles.tracked`, `.lsfiles.others` | 8, 4, 4 | the root instruction files `AGENTS.md` and `CLAUDE.md` only |

That is 56 discovery, 2 role, 41 launcher Git and 80 host Git starts: **179** in total (a maximum inventory, not the number the short
rehearsal issued; discovery refreshes are bounded by the per-capability ceiling but the five-minute timeline was not run). These
counts were resolved offline by running the unchanged production composition against the journey's provider doubles
(Profile01RehearsalTests) and are pinned in a test; they carry no headroom, so any extra command stops the run, and a changed
inventory needs a reviewed correction with fresh evidence. No remote Git, helper, credential, authentication, account or
model-catalog command is listed, and none of them has any slot.

**Time.** 90 s readiness, 120 s per setup step, 60 s per POST, 20 s per read, 12 minutes per stage (beside the unchanged 10-minute
role deadlines), 32 minutes for the active session and 60 s host shutdown. One monotonic 35-minute deadline starts as the entry's
first statement and covers the manifest load and verification, the launch-context check, the environment's creation and preparation, the session, the final
observations, the summary, its save and the closure. The waiter never runs the work it waits for, so a blocking preparation, a held
report sink or a held disposal cannot keep the entry from returning at the bound. At 35 minutes the launcher prints a fixed unproven
verdict (exit 8) and the admission gate closes by the clock at its next decision, so nothing more is started, no allowance is
consumed, and the detached session stops, cleans and disposes nothing and rewrites no report; the session, host and any provider
process it started are reported as unproven. The last 5 s of the 35 minutes are an emission reserve **inside** the budget for that
verdict and for any refusal or error report: the work, its closure and its report are decided at 34 min 55 s, every emission is
bounded by the time left in the 35 minutes, and nothing is waited for after them. A normal end is ordered: the session finishes, the
entry claims its report, the environment is closed (disposed), the summary (which states `closure.environmentDisposed`) is printed,
the entry commits, and only then is the summary saved, so authoritative success is never printed or kept before the required closure
and a failed closure disqualifies a qualified result. Persistence is checked: the summary file is created exclusively, written,
flushed to the device and closed in one guarded step, and a failure (an obstructed or existing destination, an I/O error) turns a
successful or attention-only result into exit 9 with a fixed error-stream line; the printed facts, the spent allowances and every
existing file stay as they are, with no retry, refund or overwrite, and a finalization that did not commit never returns its own exit
code (it is the unproven verdict, exit 8). If the bound elapses while the closure or the print is still running, the
entry returns exit 8, saves the unproven verdict (`expiredDuring: ReportOrClosure`, `finalizationComplete: false`) and writes a fixed
line to the error stream; the report sink is not written by anyone else while a call of it is in flight. That call cannot be revoked:
if it later appends, its bytes are non-final and unproven, and no physical suppression of it is claimed. The late continuation neither
saves nor disposes again. The bound is a decision point of the launcher under a responsive operating system: it does not pre-empt a
blocked operating-system call, expiry never proves a process stopped, and no outer timeout authorizes killing an unrelated process.

**Evidence reads (read-only) and writes.** Reads: the public status, timeline, message-evidence and, new, **attempt-evidence** routes
and the disposable rows, for each stage; the sealed artifacts' lengths and hashes; Git snapshots of the source and the worktree after
every submitted stage and after shutdown. The attempt-evidence route is read for every terminal attempt, including a failed Claude
attempt with no message, and compared with the stored attempt on status, outcome, process, the three recorded I/O facts and the
closed diagnostic. Writes: two allowance files, at most 179 slot files, one `summary-<session>.json` (created exclusively), and the
owned `devalcopilot-e2e-manual-*` root under the temp directory.

**Acceptance and stop actions.** A stage qualifies only for the accepted attempt, with the pinned contract version, a clean exit 0,
explicitly complete required I/O (both outputs complete and input delivered), a coherent role response, exactly one attributed
message, exact ordered inputs and reply identity, agreeing protected evidence and sealed length and hash comparisons. A missing or
foreign attempt, a historical or missing contract version, or absent I/O facts fails. On a failed Claude invocation the recorded
closed diagnostic and its version are reported; an unknown or generic diagnosis is a valid failure report, never retried or
relabelled. Source and worktree are compared after every submitted stage and after shutdown (unreadable is unproven). The first
failed, refused, ambiguous, mismatched or expired step stops the lot: no second submission, context, model, binary or fallback.
The conservative process proof is unchanged (`OriginUnresolved` preserves the root and yields an attention result, exit 6 when
qualified); exchange compatibility, source integrity, invoked-process completion, descendant shutdown and cleanup stay separate
facts. Provider usage is reported only if observed, otherwise unknown; this is not a monetary, token or provider-internal request cap.

### Remaining decisions before either lot can be approved

1. Approve, or replace, the two authorization identities and confirmation phrases above.
2. Lot A: the owner supplies and confirms the `claude.exe` path and SHA-256.
3. Lot B: the owner supplies, at a future approved preflight, the approved-launch manifest: exact absolute paths and SHA-256 digests of
   Git, the Codex and Claude launches (and the Node and script components of a script launch) and any of `dotnet`, `gh` and `docker`
   that are installed. This is launch-context information, not a decision about `PATH`, unsupported binaries or a new
   authorization. The offline preparation reads no installed file; the actual identities are not known here. The manifest must
   describe what the production resolver selects **in the shell, user and search-path context the entry is launched from**: the
   launch-context check refuses a manifest from another context before anything starts.
4. If a hard monetary or token ceiling is required, activation stays blocked: none can be enforced here.

Decided by the planner at review 1 (recorded, not reopened): the qualification-only counted refusal of all three `app-server`
observers is accepted for this run (any call closes the gate); the single fixed reflective call to the production Claude environment
builder is accepted with fail-closed resolution; the per-class ceilings keep no headroom and are recalculated only when a reviewed
correction changes the request inventory; and executable identity binding is required engineering work, now implemented as the
approved-launch manifest above.

## It is not a test

The launcher is a separate executable, `ManualQualification` (source under
`tests/DevalCopilot.Api.IntegrationTests/ManualQualification/Launcher`, part of the solution so it is built). It is never run by
`dotnet test`, Vitest, the harness, either canonical Playwright configuration, `test:e2e:all` or CI. The existing
`BrowserJourneyHost`, which installs only deterministic doubles, is unchanged and cannot be given a real provider.

## Authority and allowances

- At most **one** real Planner invocation and **one** real CriticalReviewer invocation, with no automatic retry, repair,
  Resolver, implementation, correction, fallback, resume, compaction or Git delivery. Provider-internal HTTP requests are not a
  separately controlled cap, and this is not a monetary or token limit.
- Each allowance is consumed, durably, **before** its single POST. A timeout, an ambiguous or refused answer, a crash, a restart
  or a second launch never makes it spendable again. The reviewer allowance can be taken only by the session that holds the
  planner allowance, and only after one coherent recorded Proposal and an unchanged source.
- The ledger of the current authorization is the directory `devalcopilot-qualification-profile-01-lot-b-review-schema-ledger` directly under the OS
  temp directory (one `planner.allowance` and one `reviewer.allowance` file, created exclusively and never rewritten, beside the
  durable process slots of [QUALIFICATION-PROFILE-01](#qualification-profile-01-prepared-offline-not-authorized-for-execution)). If any
  allowance or process slot exists, the launcher starts nothing. The earlier authorizations' directory is never named, read, reset or
  archived by the launcher; a new authorization is the only way to a new ledger, and it needs a **new owner decision**.
- A refusal before any POST (opt-in, readiness, targets, setup) spends nothing and is not proof of a provider invocation.

## Preconditions

The host's own discovery must resolve both providers as installed targets; the launcher never installs, updates, logs in,
copies credentials, edits user configuration or changes a model or effort default.

- Codex: a direct `codex.exe` on `PATH` or in `%APPDATA%\npm`, or the global npm package `@openai/codex` (run through Node).
- Claude Code: a direct `claude.exe`, or the global npm package `@anthropic-ai/claude-code` (its native `bin/claude.exe`).
- Both already authenticated for the Windows user. Git as an approved absolute file (the launcher never resolves it through `PATH`).
- The host's discovery must name exactly the files of the owner's approved-launch manifest; anything else is a blocker, never a
  substitution.

If discovery reports either as not observed, or as anything but a real installed provider (inside the disposable root or the
launcher output, a `ProviderFixture` file beside it, an unexpected name or package, an unparseable version), the session stops
as **Blocked** before anything is spent. Unsupported installed arguments or launch shapes are blockers, not things to patch.

## Run it once

The exact proposed entries of both lots, with their identities, counts and deadlines, are under
[QUALIFICATION-PROFILE-01](#qualification-profile-01-prepared-offline-not-authorized-for-execution). Without the exact flag (lot B,
optionally followed by exactly one `--approved-launches <path>`) and the exact phrase, the launcher prints a refusal and exits 2 with
no host, no provider, no directory and no ledger entry; without a valid approved-launch manifest it refuses the same way after the
opt-in. The earlier session flag (`--run-real-provider-session`) and its phrase are no longer recognized.

This shows the opt-in shape, not current authority. Any further real sequence (reset, build and launch) follows only a separately
reviewed procedure, a new explicit owner decision and a complete planner dispatch. The QUALIFICATION-REAL-SESSION-03 package used
its single authorization; it cannot be rerun under that decision.

## What one session does

1. Stops if the ledger already shows a spent allowance (exit 3).
2. Creates a new owned root under the temp directory (`devalcopilot-e2e-manual-*`, with an ownership marker) holding a tiny
   fictitious Git repository (one stub source file and benign root instructions) and the host's disposable database, workspace
   and artifact storage.
3. Starts the production Program composition on loopback (real Kestrel authentication with an in-memory launch secret,
   migrations, reconciliation, all supervisors, real discovery and process executor); only the storage roots and the secret
   differ. Host logging is silenced so nothing it logs reaches the output.
4. Waits, bounded, for the normal readiness evidence and judges both launch targets (above). Only `--version`-style discovery runs.
5. Registers the project, rechecks identity, prepares the workspace, captures a checkpoint and records a ManualAgent run with
   `maximumAgentAttempts=2` and `maximumAgentInvocationMinutes=20` through the existing protected routes.
6. Snapshots the source repository and the prepared worktree (HEAD reference and commit, every reference, the staged index
   entries, and the bytes of every file), consumes the planner allowance, POSTs once and requires a durable attempt identity in
   the answer, then polls the existing read routes within a finite deadline. Only the attempt that POST was accepted for can
   qualify the stage: an observation of any other attempt is foreign evidence, ends the stage, and is never substituted. A
   coherent recorded Proposal of that attempt, and an unchanged source and worktree, are required before the next step.
7. Only then consumes the reviewer allowance, POSTs once with the exact Proposal identity, binds the stage to the attempt that
   POST was accepted for, and verifies a reply of the matching type whose `InReplyTo` is that Proposal, whose recorded input set
   is exactly that Proposal, and whose sealed manifest names it. Exactly the two permitted attempts must exist.
8. Stops the host and its children within the shutdown bound, then observes both repositories once more after every submitted
   stage however it ended (failed, refused, ambiguous, timed out or cancelled), each read independently so that one unreadable
   repository never hides the other. An unreadable observation is **unproven**, never unchanged, and a qualified result that this
   final observation contradicts is not qualified. The first failure code is always kept; further source facts are only added.
9. Proves that no process the host started is still running (below) and removes only this session's proven-owned root. A root
   that is unsafe to clean, whose shutdown is unproven, or that holds evidence of a failure after a provider may have run, is
   **preserved** and the summary says why.

Every wait is bounded (readiness 90 s, setup steps 120 s, each POST 60 s, each read 20 s, each stage 12 minutes beside the
unchanged ten-minute role timeouts, shutdown 60 s, the whole session 32 minutes). After a failed, refused, ambiguous or
timed-out submission nothing is resubmitted and no other session is created.

### The child-process proof

Before the host starts the launcher records every process already running, and while it runs it samples the process table. A
process is identified by its id together with its creation time. Each new process is classified by walking its ancestry:
**owned** when the chain reaches the launcher through new, older processes, whatever the executable is called (a helper, a shell
or a provider process); **unrelated** only when the chain positively reaches a pre-existing process whose creation time is known
and unchanged, and the new process's own creation time is known and later than it; otherwise **unknown**: an orphan whose parent
is gone, a parent that is younger than its child, a missing creation time or ordering on either side, or a pre-existing parent
id whose identity is unreadable, changed or cannot be verified. A missing creation time is never positive proof of anything: it
is neither a match nor a positively different identity. Only a classification that was positively established is remembered as
unrelated; an uncertain one is re-evaluated and is never trusted later. A remembered descendant whose id is still present but
whose creation time cannot be compared is not declared gone.

A pre-existing process (one recorded at the start) is exempt on its own account when its identity matches positively, or when
its creation time is unreadable but its id, parent id and name are unchanged (protected system processes). That exemption covers
only that process: it never proves a newly observed child of it unrelated, and it does not apply when the parent id or name
changed, because the id may then have been reused.

The shutdown is proven only when no remembered or new descendant is alive, no remembered id has an unreadable identity, no new
process of unknown origin is alive (all within the bounded grace), and no table read failed or was partial (only the documented
end-of-table answer ends a read). Otherwise it is unproven with one closed reason (`LeftoverAlive`, `IdentityUnavailable`,
`OriginUnresolved` or `EnumerationFailed`) and the root is preserved. An unknown process is neither declared owned nor stopped:
the launcher never stops or adopts a process, so a preserved root needs the owner's inspection. This is deliberately
conservative: an unrelated new process whose parent exited between two samples (about 250 ms), or whose own or ancestor's
creation time cannot be read (some system services), is also unresolved. The proof is not confinement of any provider and is not
a claim of ownership.

## The safe summary

The launcher prints one JSON summary and keeps an identical copy as `summary-<session>.json` in the ledger directory. It is
composed only of closed names, numbers, flags, plain version tokens and opaque identities; a drive path or any forbidden value
(the launch secret, the root, the temp or the user-profile path) replaces the whole text with a fixed refusal. It reports the
observed CLI versions and, for each role separately: whether its allowance was consumed before the POST, whether the POST was
attempted, the attempt identity the POST was accepted for next to the attempt identity actually observed (and whether they
match), whether the attempt was dispatched, whether an execution was **observed**, and a closed invocation state
(`NotAttempted`, `RefusedBeforeDispatch`, `Unknown`, `NotDispatched`, `DispatchedNoExecutionEvidence` or `ExecutionObserved`).
Dispatch is not execution: the host commits its dispatch marker before it invokes the adapter and can then record a failure
without any child-process result, so an observed execution is reported only from host-measured process evidence (an exit with
its code, or a measured timeout) of the accepted attempt, and missing evidence is not proven, never proof that nothing ran. The
conservative handling that a submitted stage may have run, and the no-retry rule, do not depend on it. It also reports each
stage's role, provider, status, outcome and process facts, the message type and `InReplyTo`, whether the replying attempt's
recorded input and sealed manifest name that Proposal, sealed byte and hash agreement, whether the source and worktree are
**proven** and unchanged (an unobserved or unreadable repository is neither), the child-process proof with its reason, and the
cleanup facts. It never carries a user or executable path, a provider session identifier, a credential, a manifest or any
provider output.

The exchange summary is now `schemaVersion` 3 (described after this paragraph); schema 2, the previous version, is described here and
every schema-1 and schema-2 field is kept in schema 3. For each available stage it adds `attemptsInRun` and `dispatchedAttemptsInRun` (the
run's attempt rows and those with a dispatch marker when the stage was read), `messageCount` and `evidenceRouteComparison`, and
keeps every schema-1 field, including the derived `evidenceRouteAgrees` boolean. `messageCount` is `null` when the timeline was
not enumerated (a still running attempt) and otherwise the actual number of messages the accepted attempt produced: 0, 1 or
more. The public message-evidence route is compared only for exactly one produced message, so `evidenceRouteComparison` is
`NotAttempted` without one and `Agrees` or `Disagrees` only after an actual comparison; `evidenceRouteAgrees` is true only for
`Agrees`, so a false value alone never means a disagreement. An unavailable stage stays `null`. A stage state the reader cannot
produce (an undefined comparison value, a negative count, or a performed comparison without exactly one known selected message)
replaces the whole text with the same fixed refusal, now carrying `schemaVersion` 2. A stage still qualifies only with exactly one
message of its own and an agreeing performed comparison; the closed failure codes and the first-failure order are unchanged.
Summaries written under schema 1, including the failed session's, are historical and are not rewritten: in them a `false`
`evidenceRouteAgrees` with no selected message did not distinguish an absent comparison from a disagreement, and the count was
not reported. Schema 2 reports only what the host recorded; it adds no provider output, failure classification or diagnosis.

**Schema 3 (QUALIFICATION-PROFILE-01).** It records the authorization identity of the lot and, for each available stage,
`adapterContractVersion` (the recorded ADR-0072 adapter contract), `ioCompletion` (the three recorded I/O facts and the derived
`requiredComplete`), `invocationDiagnostic` (its state, closed category and vocabulary version, never a verified cause) and
`attemptEvidenceRouteAgrees` with `attemptEvidenceRouteComparison`, read through the attempt-evidence route whether or not the attempt
produced a message. A failed attempt with no message therefore still reports these facts. A top-level `processAdmission` section
reports the closed counts of admitted starts per class and refused requests per closed code and, if the gate closed, its first reason
(`closedBy`), never a path or an argument. The fixed
refusal now carries `schemaVersion` 3, and the outer-deadline verdict (exit 8) carries only the identity, the admission counts and the
unproven shutdown and cleanup facts. A summary is created exclusively and never overwrites another; schema-1 and schema-2 summaries
are never read or rewritten. Lot A prints its own independent closed summary (`statusSchemaVersion` 1).

| Exit | Meaning |
| ---- | ------- |
| 0 | Qualified: one Proposal and one Acceptance or Challenge replying to it, every control satisfied. |
| 2 | Refused: the opt-in is missing or inexact (the retired first lot B flag included), the approved-launch manifest is missing or invalid, the launch context does not match it (`Refused: LaunchContext...`), or the ledger destination is an alias (`LedgerDestinationAliased`). Nothing started. |
| 3 | The ledger shows a spent allowance. Nothing started. |
| 4 | Blocked before any allowance was spent (readiness, targets, setup, source change during setup). |
| 5 | An allowance was spent and the session did not reach a valid qualified result. |
| 6 | Qualified, but shutdown or cleanup was unsafe, so the root was preserved for attention. |
| 7 | The launcher stopped unexpectedly (only the exception type is printed). |
| 8 | The entry's one monotonic bound (35 minutes for lot B, 5 s of it an emission reserve, from its first statement and covering preparation, the session, the final observations, the closure, the report and any refusal or error reporting) elapsed: a fixed unproven verdict was saved and printed (or, if the report or closure was already in flight, saved with a fixed error-stream line saying report and closure are unproven), the admission gate closed, nothing was stopped or cleaned and nothing later may start, consume or persist. Bytes of a sink call already in flight are non-final. |
| 9 | The session's exchange or observation facts were printed (and, for an allowance, spent) but the summary file could not be created exclusively, written, flushed and closed, so a successful or attention-only result is replaced by this closed attention exit; a more specific non-success exit (3, 4, 5, 8) is kept. Nothing is retried, refunded or overwritten, and an existing file is never touched. |

## Inspecting or removing a preserved root

A preserved `devalcopilot-e2e-manual-*` directory under the temp directory is local, disposable and contains only the fictitious
repository, its database and the sealed artifacts. Inspect it only through authenticated local access; delete it by hand once it
is no longer needed. Delete nothing else: other `devalcopilot-*` directories are earlier fixtures that belong to other work.

## Limits

- The host's own scratch directories stay at their production location under the user's local application data, exactly as in
  the browser journeys; only storage roots and the launch secret are replaced.
- Read-only adapters and profiles are the production ones; this does not claim complete provider-side confinement, and a
  provider can still read what its own permissions allow.
- The offline regressions rehearse this whole flow, including the real composition, against the journey's deterministic doubles
  and prove each control with scripted substitutes. Those are never a provider result and cannot be supplied to the launcher:
  its entry point always uses the installed-provider judge and the native process table.
- The rehearsal proves the cleanup decision both ways with a scripted, read-only process table that only offline tests supply: a
  complete observation removes only the owned root, and an unknown-origin observation preserves it (`OriginUnresolved`, exit 6).
  A scripted table is never native shutdown evidence. A rehearsal on the native table asserts only the safe outcomes the policy
  allows (proven and removed, or `OriginUnresolved` or `IdentityUnavailable` and preserved); which one occurs depends on the
  processes running on the machine, and a known leftover or a failed table read is never accepted. A native rehearsal that ends
  unproven leaves its exact owned root preserved and never removes it; the owner deletes it by hand. The only offline teardown
  acts on the exact root and token the environment generated at creation (never a discovered root or a token read from a
  marker), for the scripted case only, and only after the host stopped and both doubles were measured to have exited.
