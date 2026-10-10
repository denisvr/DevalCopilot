# Manual provider qualification playbook

This is the opt-in procedure for the one real-provider acceptance session the owner authorized on 2026-10-09: one real Codex
Planner Proposal, then one real Claude CriticalReviewer Acceptance **or** Challenge replying to that exact persisted Proposal,
through the production host, adapters, parsers and result writers, in a new disposable repository and data root. A genuine
Challenge is a valid result; nothing forces an Acceptance. The result is version-specific compatibility evidence, not provider
obedience, general reliability, account capacity or increment completion. See [current-work.md](current-work.md) for the
recorded outcome of the session, and [AGENTS.md](../../AGENTS.md) for the planner/executor roles.

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
- The ledger is the directory `devalcopilot-manual-qualification-ledger` directly under the OS temp directory (one
  `planner.allowance` and one `reviewer.allowance` file, created exclusively and never rewritten). If either exists, the launcher
  starts nothing. Resetting it is a manual act that needs a **new owner decision**; the launcher never does it.
- A refusal before any POST (opt-in, readiness, targets, setup) spends nothing and is not proof of a provider invocation.

## Preconditions

The host's own discovery must resolve both providers as installed targets; the launcher never installs, updates, logs in,
copies credentials, edits user configuration or changes a model or effort default.

- Codex: a direct `codex.exe` on `PATH` or in `%APPDATA%\npm`, or the global npm package `@openai/codex` (run through Node).
- Claude Code: a direct `claude.exe`, or the global npm package `@anthropic-ai/claude-code` (its native `bin/claude.exe`).
- Both already authenticated for the Windows user. Git on `PATH`.

If discovery reports either as not observed, or as anything but a real installed provider (inside the disposable root or the
launcher output, a `ProviderFixture` file beside it, an unexpected name or package, an unparseable version), the session stops
as **Blocked** before anything is spent. Unsupported installed arguments or launch shapes are blockers, not things to patch.

## Run it once

Build, then start the launcher from the repository root with both halves of the opt-in:

```powershell
dotnet build DevalCopilot.slnx -p:UseSharedCompilation=false -m:1
$env:DEVALCOPILOT_MANUAL_QUALIFICATION_CONFIRM = 'one-codex-planner-and-one-claude-critical-review'
& .\tests\DevalCopilot.Api.IntegrationTests\ManualQualification\Launcher\bin\Debug\net10.0\ManualQualification.exe --run-real-provider-session
Remove-Item Env:\DEVALCOPILOT_MANUAL_QUALIFICATION_CONFIRM
```

Without the exact flag as the only argument and the exact phrase, it prints a refusal and exits 2 with no host, no provider, no
directory and no ledger entry.

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

| Exit | Meaning |
| ---- | ------- |
| 0 | Qualified: one Proposal and one Acceptance or Challenge replying to it, every control satisfied. |
| 2 | Refused: the opt-in is missing or inexact. Nothing started. |
| 3 | The ledger shows a spent allowance. Nothing started. |
| 4 | Blocked before any allowance was spent (readiness, targets, setup, source change during setup). |
| 5 | An allowance was spent and the session did not reach a valid qualified result. |
| 6 | Qualified, but shutdown or cleanup was unsafe, so the root was preserved for attention. |
| 7 | The launcher stopped unexpectedly (only the exception type is printed). |

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
