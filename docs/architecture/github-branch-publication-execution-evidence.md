# GitHub branch publication execution: implementation-readiness evidence

> **Status: evidence for a pending planner decision. This is not an accepted execution contract, not an ADR and not an
> approval to implement.** Every "proposed" or "candidate" boundary, state and choice below is for Codex review. Nothing here
> reserves or accepts an execution ADR, adds a table, port, API, adapter, queue or supervisor, or grants any publication,
> pull request, correction push, rerun, force mode or automatic-execution authority. [ADR-0048](../decisions/0048-approve-one-future-github-branch-push-of-a-bound-recorded-local-commit-on-explicit-request.md)
> remains recorded consent only and [ADR-0049](../decisions/0049-observe-the-github-repository-permission-of-a-bound-recorded-target-on-explicit-request.md)
> remains ephemeral reported access only. Future Git/GitHub compatibility qualification is a separate gate from the existing
> Agent qualification ledger (`Blocked`/`TargetsNotReal`, both allowances unused); this document relaxes and authorizes neither.

- Repository baseline: `main` at `810b99b8e4f9c5e4d1b92266d8039a34f5c7c39e` (verified equal to local and live `origin/main`).
- Evidence date and documentation access date: 2026-10-15. Revision: first correction round (Codex review items R1 to R5).
- Source of the planner constraints and of the review: [planner handoff](../roadmap/planner-handoff.md), "ADR-0049 publication verified; first-push
  execution evidence selected" and "First-push execution evidence review".
- Delivery handoff: [current work](../roadmap/current-work.md).

## How to read this document

Each claim carries one evidence class. A claim without a class is a statement of scope, not evidence.

| Class | Meaning |
|---|---|
| **CODE** | Anchored to a file and line range at the baseline above. |
| **DOC** | What a primary Git or GitHub page states. The Git manual pages report 2.55.0 as their latest version (`git-push` "last updated in 2.55.0"); the installed Git used for every probe is `2.53.0.windows.1`, and **no 2.53.0 manual text was read**, so a DOC statement is not assumed true of 2.53.0 unless a LOCAL probe shows it. Sections marked "re-read directly" were read as tag-stripped page text on 2026-10-15 (`logs/executor/first-push-evidence/r2-sources/`); every other DOC statement was read only through a summarizing fetch tool and must be re-read at the source before it becomes a contract. |
| **LOCAL** | Observed in a synthetic probe against local bare repositories or an owned `127.0.0.1` double (section 7). It shows what Git 2.53.0.windows.1 and `git http-backend` did there. It is never evidence about GitHub, `gh`, the host's process executor or a real credential helper. |
| **INFERENCE** | Reasoning from the evidence above, not itself observed. Labelled where it carries weight; an interleaving that was reasoned and not run says so. |
| **UNVERIFIED** | Not established. Fails closed in any design that depends on it. |

## 1. Bounded conclusion

This is a summary of the evidence, not a recommendation and not a selection.

1. **Reuse.** The existing process executor, the ADR-0048 readers, the read-only GitHub observers and the Application pattern
   (durable intent, a single-use claim committed before the effect, no transaction across external work, read-only reconciliation) are
   reusable ideas or primitives. The local-commit *Git* code and its *recovery policy* are not reusable as a remote executor (section 2).
2. **Authentication is unresolved, not impossible.** The existing local-commit profile (`GIT_CONFIG_NOSYSTEM`, an empty global file, an allowlisted
   environment) provides no credential helper through system or global configuration, and in the tested synthetic arrangement (no helper, no askpass, prompts disabled, a
   cleared environment) a push refused (LOCAL P3 A1). That is one tested arrangement, not a proof that every way to reuse existing Git authentication is unsafe or unavailable.
   The same profile does **not** remove repository-local configuration: a helper named only in repository configuration executed (LOCAL P3 A3). Which credential source
   a push may use, and whether a real helper works under any boundary, are unresolved (U1, U13). Forwarding the home directory is not offered as a solution.
3. **Whole-attempt certainty after a launch is not available from client output or later reads.** A killed client, a lost acknowledgement and an extra POST
   from one client invocation were each observed on a local double (section 5). Evidence about one exchange (an acknowledgement, a rejection, an equal SHA) is recorded as
   observed evidence; it does not establish what the whole attempt did, and a final rejection followed by an absent or different ref cannot exclude an earlier applied-and-moved
   effect. A matching remote SHA proves an observation, never who pushed it.
4. **No atomic guarantee ties the Git transport to the bound opaque repository ID** in any source read. Names address the transport; the ID is a GraphQL fact (section 4, U6).
5. **Repository mutation exclusion is already required** by accepted [ADR-0006](../decisions/0006-support-bounded-concurrent-runs-across-projects.md) and
   [ADR-0008](../decisions/0008-physical-repository-identity-and-tool-owned-worktree-ownership.md) for pushes. Uniqueness per approval is additional, never a substitute (section 2.1).
6. **Proposals that need discussion** (section 6.1): plain push versus an explicit empty-expectation lease (a candidate, not a selected requirement); an isolated transport repository
   (a proposed execution-location extension of ADR-0008, not an accepted design); and what whole-attempt statements any observation may support.

Bounded reading, pending Codex review: the evidence found nothing that makes a first normal push infeasible, and nothing that proves a safe executor exists. A narrow design appears
expressible only if the credential source, the execution location, the exclusion mechanism and the open questions of section 6.2 are decided. Restoring ambient Git configuration and pushing is
not supported by any evidence here. Real GitHub and real credential-helper compatibility are not shown (section 7.3).

## 2. A. Code-anchored reuse and gap map

### 2.1 Reuse and gap table

| Concern | Anchor (CODE) | Reusable for a remote push | Gap |
|---|---|---|---|
| Argv-only child, environment allowlist, bounded capture, timeout, tree kill | [`ChildProcessExecutionAdapter`](../../src/backend/DevalCopilot.Infrastructure/Features/Processes/ChildProcessExecutionAdapter.cs) lines 38-49 (arguments, cleared environment), 52-56 (caps), 64-106 (timeout, cancellation, tree kill, 5 s post-kill wait) | Yes: the same primitive the read-only GitHub adapters use | The result type has no process identifier, no "still running" flag and no progress information ([`ProcessExecutionResult`](../../src/backend/DevalCopilot.Application/Features/Processes/Ports/ProcessExecutionResult.cs)). After a kill the 5 s wait is best-effort and "proceeds" (lines 101-105), so a timed-out result can describe a tree that is not yet gone. A killed process's exit code is withheld by design, so a timeout is data about the host, not about the remote. No job object is created, so a host crash can leave a launched child behind. |
| Hardened Git invocation | [`LocalCommitGitRunner`](../../src/backend/DevalCopilot.Infrastructure/Features/Runs/LocalCommitGitRunner.cs) lines 45-59 (fixed `-c` set), 75-99 (environment), 109-145 (execute and map) | The fixed `-c` set (hooks directory, signing off, no pager, no fsmonitor) and the proven-empty hooks directory (line 53) are reusable ideas | Line 51 sets `protocol.allow=never` **deliberately**: no network protocol can run, which is why its repository-local configuration is harmless today. Lines 79 and 86 set `GIT_CONFIG_NOSYSTEM=1` and an empty global file: no helper can come from system or global configuration (this does not remove a helper named in repository-local, worktree or included configuration, which the runner never neutralizes). Lines 84-99 forward `USERPROFILE`, `HOME`, `HOMEDRIVE`, `HOMEPATH`, `XDG_CONFIG_HOME` and `APPDATA` for author identity only; reusing that switch for a remote child would expose user configuration. Cancellation is thrown (line 128) and a timeout drops all output (line 129), which would discard the porcelain lines a push result depends on. |
| Interactive local child | [`LocalCommitRefTransaction`](../../src/backend/DevalCopilot.Infrastructure/Features/Runs/LocalCommitRefTransaction.cs) lines 7-16, 60-89 | The idea of explicit phases and independent deadline, phase and cleanup budgets | The local protocol (`update-ref --stdin` start, update, prepare, then `commit`) lets the host hold a prepared lock and *choose* when to send. `git push` exposes no prepare/commit handshake to its caller; once `send-pack` starts the controls are before it (hooks, expectation) and after it (status). The class also forwards `PATH`, `SystemRoot` and `ComSpec` (lines 80-82). |
| Git executable resolution | [`LocalCommitGit.ResolveGit`](../../src/backend/DevalCopilot.Infrastructure/Features/Runs/LocalCommitGit.cs) lines 43-47 and [`HostExecutableResolver`](../../src/backend/DevalCopilot.Infrastructure/Features/EnvironmentReadiness/HostExecutableResolver.cs) lines 18-37 | A fresh, successful, direct-executable `Git` capability snapshot gate exists as a pattern (ADR-0038/0041) | Resolution searches the **ambient** `PATH` first and then fixed fallbacks, on every call, with no recorded file identity or version for a send. |
| Physical ownership proof | [`LocalCommitGit.ProveOwnershipAsync`](../../src/backend/DevalCopilot.Infrastructure/Features/Runs/LocalCommitGit.cs) lines 97-120 (registered worktree, administrative directory, marker with workspace, project, lease and physical identity) and [ADR-0008](../decisions/0008-physical-repository-identity-and-tool-owned-worktree-ownership.md) | Yes, as the fresh ownership read before any send | It is a point-in-time read, not a held capability. The local flow holds index-lock handles in process memory (line 33) that a restart loses. Source workspace ownership, the lease and the applicable readiness proofs stay required whatever the transport location. |
| Durable intent, single-use claim, outcome | [`ExecuteLocalCommitCommandHandler`](../../src/backend/DevalCopilot.Application/Features/Runs/Commands/ExecuteLocalCommit/ExecuteLocalCommitCommandHandler.cs) lines 31-35 (a non-`Prepared` operation is never executed again), 256-300 (one write-locked transaction: `Prepared` to `Executing`, reread of the complete authority, one journal event), 89-148 (external effect outside any transaction, outcome recorded afterwards, persistence failure after the effect fails closed) | **The pattern**, not the types | [`LocalCommitOperation`](../../src/backend/DevalCopilot.Domain/Features/Runs/LocalCommitOperation.cs) is local-specific (unique per run, index-lock receipts, ref and index recovery fields, lines 212-233). The ADR-0048 approval row is immutable by trigger, so "consumed" cannot live on it. A push needs its own record. |
| Startup recovery | [`RecoverLocalCommitOperationsCommandHandler`](../../src/backend/DevalCopilot.Application/Features/Runs/Commands/RecoverLocalCommitOperations/RecoverLocalCommitOperationsCommandHandler.cs) lines 18-100 and [`LocalCommitRecoveryPolicy`](../../src/backend/DevalCopilot.Application/Features/Runs/Policies/LocalCommit/LocalCommitRecoveryPolicy.cs) lines 31-107 | The structure "decide from recorded facts and an exact inspection; everything else is attention; never retry" | The local policy proves "never promoted" by reading the single authoritative ref and its lock files (lines 64-76). A remote ref cannot do that (section 5). Recovery would also need network reads at startup (new failure modes, bounded and non-blocking). |
| Supervisor | [`LocalCommitSupervisor`](../../src/backend/DevalCopilot.Api/HostedServices/LocalCommitSupervisor.cs) lines 13-45 (250 ms poll, a query proposes candidates, the command decides, one at a time) | The "query proposes, command decides" shape | Whether an intent is executed by the request or by a poller is a design choice (U8). A poller that executes any `Prepared` push intent is broader automatic execution unless an intent exists only after an explicit request. |
| **Repository mutation exclusion (accepted requirement)** | [ADR-0006](../decisions/0006-support-bounded-concurrent-runs-across-projects.md) ("at most one mutating run per canonical repository"; queue conflicting runs rather than race them); [ADR-0008](../decisions/0008-physical-repository-identity-and-tool-owned-worktree-ownership.md), Consequences: "Every future repository-mutating feature (agent execution, commits, pushes) must acquire this lease and operate only inside a `Ready` `GitWorkspace`"; the lease is keyed by physical identity with a partial unique index (one `Active` lease per physical repository). [ADR-0005](../decisions/0005-use-git-worktrees-and-a-policy-controlled-github-loop.md): one writer per worktree, the orchestrator executes pushes. Existing mechanism for commits: [`WorkspaceStatus.Committing`](../../src/backend/DevalCopilot.Domain/Features/Projects/WorkspaceStatus.cs) lines 19-21, [`GitWorkspace`](../../src/backend/DevalCopilot.Domain/Features/Projects/GitWorkspace.cs) lines 162-190, `CommitReservation` | The requirement and the lease; the reservation mechanism is one available model | A push must hold repository exclusion that also excludes **other runs and other approvals** for the same physical repository, from before the process may start until the outcome is conclusively resolved. A unique key per approval (or per run) does not do that and is not a substitute. The exact reservation mechanism (a reservation like `Committing`, a new status, lease-only) is a future design detail; the requirement is not optional (U10). |
| Authority inputs | [`GitHubBranchPublicationScopeReader`](../../src/backend/DevalCopilot.Application/Features/Runs/Policies/GitHubBranchPublicationApproval/GitHubBranchPublicationScopeReader.cs), [`GitHubBranchPublicationApprovalReader`](../../src/backend/DevalCopilot.Application/Features/Runs/Policies/GitHubBranchPublicationApproval/GitHubBranchPublicationApprovalReader.cs), the ADR-0040/0043 readers | Yes: complete coherent scope and approval snapshots, compared as whole values before and after external work and under the write lock | The approval carries no consumption or dispatch fact. |
| Read-only remote observers | [`GitHubRepositoryIdentityAdapter`](../../src/backend/DevalCopilot.Infrastructure/Features/Runs/GitHubRepositoryIdentityAdapter.cs), [`GitHubBranchObservationAdapter`](../../src/backend/DevalCopilot.Infrastructure/Features/Runs/GitHubBranchObservationAdapter.cs) (query at line 42; the returned ref is proven exactly), [`GitHubRepositoryPermissionObservationAdapter`](../../src/backend/DevalCopilot.Infrastructure/Features/Runs/GitHubRepositoryPermissionObservationAdapter.cs) (allowlist lines 41-53, single-flight lines 70 and 97) | Yes as fresh read-only checks and as a reconciliation read | They run `gh` with the **`gh` stored default authentication** (forwarding `USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, lines 41-42). Nothing shows that principal equals the one a Git credential helper supplies (ADR-0049). They discard standard error unread. |
| Output admission | The observers' whole-response admission (for example the backslash refusal in the permission adapter) | The principle: admit exactly, refuse the rest | A push has a line grammar (a `To` line, one status line per ref, `Done`, plus server `remote:` text on stderr and hints). A closed parser is new work, and anything it does not recognize is an observation that the attempt is unclassified, never a statement that nothing was sent. |

### 2.2 Why the local-commit runner and recovery cannot simply become a remote executor

1. **Disabled network is the contract.** `protocol.allow=never`, an empty global file and `GIT_CONFIG_NOSYSTEM` make the runner's safety argument ("no network, credential, filter
   or repository executable") true for local work. Removing them to push reopens those surfaces, and the profile never neutralized repository-local configuration (LOCAL P3 A3), so
   restoring ambient configuration is not a bounded change (CODE: runner lines 51, 79, 86).
2. **The local effect is single-writer and re-readable; the remote effect is neither.** The recorded commit object is created by this host under a unique trailer on an owned branch, so
   "the ref equals the recorded commit" proves delivery (recovery policy lines 80-95). Anyone with access can push the same SHA (it is also a local branch in the user's own checkout),
   and the host cannot read the server's locks or in-flight state.
3. **"Proven not promoted" has no remote counterpart** except the host's own durable record about what it did (section 5). The local `Interrupt` and `Failed` outcomes (recorder lines 41-63)
   rest on reading the ref; a remote equivalent would be a false inference.
4. **No prepare phase** (section 2.1, interactive child).
5. **The executor cannot tell the host what happened to the process** (no identifier, no still-running flag, no partial output through the runner on cancellation, no job object).

## 3. B. Transport and authentication feasibility

All LOCAL observations: `logs/executor/first-push-evidence/` (ignored; manifests and exact commands in its README files), Git 2.53.0.windows.1. No real credential configuration, store or
helper was read or invoked; every Git call used a replaced home, `GIT_CONFIG_NOSYSTEM=1` and an empty global file inside a new sandbox.

### 3.1 Boundaries, evidence and incompatibilities

| Boundary | What can change the destination, credential or effect | Evidence | Candidate control (proposal, pending Codex review) | Incompatibility or residual |
|---|---|---|---|---|
| Executable and helper trust | Git resolves `git-remote-https`, `git-credential-*` and `git-<command>` from its exec path and `PATH`; a `credential.helper` value is a name, an absolute path or a `!` shell snippet | DOC gitcredentials (re-read directly: the value is executed by the shell; `git credential-<name>` form). LOCAL: `git --exec-path` of this install lists `git-remote-http.exe`, `git-remote-https.exe`, `git-credential-wincred.exe`, `git-http-backend.exe` (a directory listing only; no helper invoked) | Send only from a fresh, successful, absolute `Git` snapshot; record its path, version and file digest in the durable intent and compare at send; a child `PATH` limited to Git's own directories (the probes used `cmd`, `mingw64/bin` and `usr/bin`); refuse on any mismatch | `HostExecutableResolver` searches ambient `PATH` first today (CODE). Where a supported helper binary lives is UNVERIFIED |
| System and global configuration | Helper, `url.*`, `http.*`, `core.sshCommand`, proxy, TLS, `push.*` | DOC git-config (re-read directly: `GIT_CONFIG_GLOBAL`, `GIT_CONFIG_SYSTEM`, `GIT_CONFIG_NOSYSTEM` choose or skip the global and system files; the pages say nothing about local configuration being skipped). LOCAL P3 A1: the tested synthetic arrangement (no helper, no askpass, `GIT_TERMINAL_PROMPT=0`, cleared environment) **refused** (`could not read Username ... terminal prompts disabled`, exit 128; the double saw one 401 request) | Pin `GIT_CONFIG_NOSYSTEM=1` and `GIT_CONFIG_GLOBAL` to a **host-generated** file; ambient system or global configuration is not passed | Disabling these two scopes does **not** remove repository-local helpers (LOCAL P3 A3 executed one). P3 A1 shows only that the tested arrangement has no credential source; it is not evidence about any other arrangement. The credential source for a real push is unresolved (U1) |
| Local, worktree and included configuration | `$GIT_DIR/config`, `config.worktree` (with `extensions.worktreeConfig`), `include.path`, `includeIf` | DOC git-config (re-read directly: the files and their order; `config.worktree` is read only with `extensions.worktreeConfig`). LOCAL P1 A: an include outside the repository is read and listed with scope `local`; P1b: `includeIf hasconfig:remote.*.url` includes a file when written as a config section (P1 A's CLI-set variant was inconclusive and is kept); P1 E: `config.worktree` rewrote the destination of a push from a linked worktree; P3 A3: a `credential.helper` named only in repository configuration **executes**; P2 H1: repository `core.hooksPath` selects hooks | Do not let a push read an untrusted repository's local configuration. Two candidates are compared in section 3.3 (isolated transport repository; pre-validation of the owned Git directory). Neither is accepted | No environment variable disables a repository's local configuration, and `-c` cannot clear a multi-valued `url.*.insteadOf` (no reset form found in the sections read; not probed). `-c credential.helper=` does reset the helper list (DOC re-read directly; LOCAL P3 A4) |
| `insteadOf`, `pushInsteadOf`, `pushurl`, redirects | A rewrite changes the destination host or repository silently | DOC git-config (`url.<base>.insteadOf`, `pushInsteadOf`, `remote.<name>.pushurl`). LOCAL P1 B and C: both **rewrote an explicit URL argument** (the porcelain `To` line showed the rewritten target); P1 D: `remote.origin.pushurl` was **not** used for an explicit URL but was for the named remote; P1c: `git ls-remote --get-url` shows the `insteadOf` expansion only, **not** `pushInsteadOf` | Explicit URL built by the host from the recorded owner and repository at the fixed host `github.com`, with no rewrite section readable by the push. After the launch, a `To` line that differs from the intended URL is an observation about an attempt that **may already have sent** (section 5.2), not a pre-send refusal | A pre-send `--get-url` check is insufficient (it cannot see `pushInsteadOf`). The `To` comparison is a normalized, exact one (Git adds a trailing `/` in some messages) |
| HTTP redirects | A redirect moves the push to another path or host; credentials are then requested for the new context | DOC renaming and transferring a repository (re-read directly: `clone`, `fetch` and `push` at the old location keep working; reusing the old name stops the redirect). DOC git-config (`http.followRedirects` default `initial`). LOCAL P3 R1: with the default an initial 301 from `127.0.0.1` to `localhost` **was followed** and the helper was asked for the new host's credential and stored it; R2 (`false`): the push ended with `returned error: 301` (the double saw only the first GET); R3 (`true`): followed | Candidate pin `-c http.followRedirects=false` | After a rename or transfer the push is refused until the recorded target is replaced (ADR-0040/0043 are immutable): acceptable fail-closed behavior, but an incompatibility to disclose. The refusal arrives from a launched process; it is an observation (section 5.2) |
| Proxy, TLS, extra headers, credential scoping | `http.proxy`, `http.sslVerify`, `http.sslCAInfo`, `http.sslBackend`, `http.extraHeader`, `http.curloptResolve`, `GIT_SSL_NO_VERIFY`, proxy environment variables, `credential.<url>.*`, `credential.useHttpPath` | DOC git-config and git (summarized fetch; not re-read directly). LOCAL P3 A6: by default the helper received `protocol` and `host` only; `credential.useHttpPath=true` added `path` | An isolated configuration plus an environment holding none of these variables leaves the defaults. Pins are **unprobed** candidates, listed in 3.2 | An environment needing a proxy or a private CA fails closed. The Windows TLS backend is the platform's (the installed `curl` reports Schannel); trust and revocation settings outside Git configuration are UNVERIFIED. Credentials are scoped to the **host** by default (LOCAL P3 A6); the remote bounds what they can do |
| Prompts and askpass | `GIT_ASKPASS`, `core.askPass`, `SSH_ASKPASS`, terminal prompt, helper UI | DOC gitcredentials (re-read directly: with no helper, `GIT_ASKPASS`, then `core.askPass`, then `SSH_ASKPASS`, then the terminal). LOCAL P3 A2: with `GIT_TERMINAL_PROMPT=0` **an ambient `GIT_ASKPASS` still supplied username and password and the push succeeded** | The executor clears the child environment (CODE adapter lines 45-49); keep `GIT_TERMINAL_PROMPT=0` and set no askpass | A helper that opens a window is UNVERIFIED and not controlled by `GIT_TERMINAL_PROMPT`; its hang ends at the timeout with an unclassified outcome |
| Credential-helper side effects and repeated calls | A helper's `store` and `erase`; a helper's answer can differ from call to call | LOCAL P3 A3, A4: `store` was called with the credential after a successful push; A5: `erase` after a failed authentication. DOC gitcredentials: helpers are invoked per operation and may answer or not | Prefer a helper arrangement that ignores `store` and `erase` (DOC: helpers may ignore unsupported operations) | Whether the real helper persists on `store` is UNVERIFIED. INFERENCE: two calls (for example a read before the push and the push) can receive different credentials |
| Client hooks | `pre-push`; repository or global `core.hooksPath`; templates at repository creation | DOC githooks (re-read directly: `pre-push` is called by `git push` and a non-zero exit aborts without pushing; the page does not mention dry run). LOCAL P2 H1: `pre-push` fired for a real push **and for `--dry-run`**; `-c core.hooksPath=<empty owned directory>` and `--no-verify` each suppressed it; the `-c` value overrode a repository `core.hooksPath` pointing elsewhere. P3b: the hook started **after** the first network request of the same push (the advertisement GET) | `-c core.hooksPath=<existing proven-empty hooks directory>` (CODE: runner line 53) | A repository that supplies its own hooks is only controlled by that pin or by not using its Git directory |
| Server hooks and platform rules | `pre-receive`, `update`, protected-branch rules, rulesets, push restrictions | DOC githooks (summarized fetch), protected branches (summarized fetch; rulesets not read) | None possible client-side. A refusal arrives as `[remote rejected]` | LOCAL P2 H2: an `update`-hook rejection produced `[remote rejected] (hook declined)` with `remote:` text and exit 1 on the double. Real GitHub rejection text and codes are UNVERIFIED. DOC git-push (re-read directly): `--dry-run` "does everything except actually send the updates", so it cannot evaluate server rules |
| Extra refs, submodules, LFS | `push.followTags`, `--tags`, `--mirror`, `--all`, `push.recurseSubmodules`, `push.pushOption`, `push.gpgSign`, `remote.<n>.push`, `push.default` | DOC git-push (re-read directly: `push.followTags` "enable `--follow-tags` option by default", overridable with `--no-follow-tags`; explicit-refspec precedence from the summarized fetch). LOCAL P2 H3: with `-c push.followTags=true` an explicit single refspec **also pushed an annotated tag**; `--no-follow-tags` suppressed it; the configuration-file variant was uninformative (the tag already existed remotely) | Candidate `--no-follow-tags`, one refspec, an isolated configuration that sets none of these. `--no-recurse-submodules`, `--signed` and push-option controls are **unprobed** | The new commit cannot contain submodule or `filter` paths (ADR-0029 refuses them), but **ancestor commits are published too** (U5). LFS pointers in ancestry would be sent without objects; UNVERIFIED |
| Configuration-change races | Another process edits a file between the host's preflight and Git's read | LOCAL P3b: an edit of `http.extraHeader` made by a `pre-push` hook (after the first request) **landed in the file but was not applied to the later POST** (one key, one version; P3 C1/C2 did not prove the edit landed and are not relied on) | Host-owned configuration files; no file an outside party writes is read | An outside process running as the same OS user can alter host-owned files and the Git installation; ADR-0029 already accepts that boundary for local hardening, and it must be stated in any decision |
| Output, time, cancellation | Large or misleading output; a push that outlives its bound | CODE: the executor's caps and timeout (adapter lines 52-106); observers use 8 KiB and 16 KiB caps. LOCAL: a one-ref porcelain output was under 1 KiB | `--porcelain` on stdout, standard error bounded and treated as untrusted text, a locale pin to be verified (flags are language-independent; summaries may not be), a wall-clock bound (U9) | A bound too low turns a slow success into an unclassified outcome; too high delays resolution |

### 3.2 Candidate argv and environment (proposal; established versus unresolved)

*A candidate for review, not a contract. "Probed" means a LOCAL probe ran that exact form; every probe used `http://127.0.0.1:<port>/...`, `file:///...` or the unresolvable `github.invalid`
as destination, never `https://github.com/...`.*

Candidate argv, in order:

| Element | Status |
|---|---|
| `git` from a fresh absolute `Git` snapshot | Resolution rule is CODE; the identity comparison is proposed |
| `-c core.hooksPath=<proven-empty hooks directory>` | Probed (P2 H1) |
| `-c credential.helper=` then the single supported helper entry | The reset is probed (P3 A4); **the helper entry is UNRESOLVED (U1)**. Probed only with sentinel helpers, never a real one |
| `-c http.followRedirects=false` | Probed (P3 R2) |
| `--git-dir=<transport repository>` | The form is probed (P1 F, a scratch bare directory); **the location is UNRESOLVED (section 3.3, U14)** |
| `push --porcelain` | Probed (all pushes) |
| `--no-follow-tags` | Probed (P2 H3) |
| `--force-with-lease=refs/heads/<branch>:` | A candidate under discussion (U2); probed (P4, P4b) |
| `https://github.com/<owner>/<repository>.git` | Host, owner and repository rules are CODE (ADR-0040 name policy); **never probed against the real host** |
| `<full 40-hex SHA>:refs/heads/<branch>` | Probed (P2 H4: an unqualified destination is refused; an abbreviated source is accepted, so only the 40-hex form may be passed) |

Unprobed or unresolved argv elements (not part of any list above): `-c protocol.allow=never -c protocol.https.allow=always` (the per-protocol key spelling is from the summarized fetch only), `--no-recurse-submodules`,
`-c http.sslVerify=true`, a proxy pin, `-c push.followTags=false`, `--no-signed`, a push-option pin, a `--` separator before the URL, a locale pin, and which entries of the existing runner's fixed `-c` set
(CODE runner lines 45-59: `--no-pager --literal-pathspecs`, `core.fsmonitor`, `core.untrackedCache`, `core.attributesFile`, `commit.gpgsign`, `tag.gpgsign`, `i18n.commitEncoding`, `gc.auto`, `core.quotePath`) apply to a push.
The runner's `-c protocol.allow=never` must not be inherited.

Candidate environment:

| Element | Status |
|---|---|
| `SystemRoot`, `PATH` of Git's `cmd`, `mingw64/bin` and `usr/bin` directories | Probed (the combination every probe used) |
| `HOME`, `USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, `XDG_CONFIG_HOME`, `TEMP`, `TMP` pointing at a new **empty host-owned directory** | Probed (every probe replaced the home this way). **Running with no home variable at all was not probed**; whether a real helper needs profile variables is UNRESOLVED (U13) |
| `GIT_CONFIG_NOSYSTEM=1`, `GIT_CONFIG_GLOBAL=<empty or host-generated file>` | Probed |
| `GIT_TERMINAL_PROMPT=0` | Probed |
| `GIT_NO_REPLACE_OBJECTS=1`, `GIT_NO_LAZY_FETCH=1` | CODE (the existing runner sets them); not part of the push probes |
| A locale variable | UNRESOLVED |
| No `GIT_ASKPASS`, `SSH_ASKPASS`, SSH, proxy, trace or other ambient `GIT_*` variable | Follows from the cleared executor environment (CODE) and was probed by the allow-list; the sentinel askpass result (P3 A2) shows why |

### 3.3 Where the push runs (proposal; requires a decision, not an accepted design)

Local, worktree and included configuration can change the destination, execute a helper and select hooks (section 3.1). ADR-0008 says every repository-mutating feature, pushes included, operates "only inside a
`Ready` `GitWorkspace`, never the registered canonical path directly". Candidates, none accepted:

| Candidate | What the evidence shows | Open |
|---|---|---|
| **A. Isolated host-owned bare transport repository** whose objects come from the source through `objects/info/alternates` | LOCAL P1 F: the commit resolved through alternates (`cat-file -t` succeeded), the scratch repository's whole configuration was five `core` lines, the source's rewrite did not apply, and the explicit destination was used. The scratch repository was a new bare directory beside the source repository in a sandbox | **It is an execution location outside the `GitWorkspace`, so it needs an explicit extension of ADR-0008** (execution location, ownership marker, lifetime, cleanup). That alternates resolved one object does not establish physical safety: INFERENCE from Git's worktree layout and ADR-0008's common-directory validation: a linked worktree's objects live in the main repository's common object store, so the alternates path reads the user's active checkout's object store; whether read-only access to it is acceptable, and how its path is proven, are decisions. P1 F did not use a linked worktree |
| **B. Push from the owned workspace's proven administrative directory** (as the runner already does with `--git-dir` and `--work-tree`, runner lines 60-66) | Not probed as a push. P1/P3 show what local configuration can do, so the directory's configuration, worktree configuration and includes would have to be validated and refused when they contain a destination, helper, hook, extra-ref or transport key | A pre-validation read followed by the push leaves a configuration-change window the isolated repository avoids; a complete key inventory is unresolved |
| C. Other | Not examined | |

Under every candidate the source workspace ownership proof, the lease and the applicable readiness proofs remain required, and repository exclusion (section 2.1) is held.

### 3.4 Incompatibilities the candidate boundary creates

- A user whose authentication is available only through configuration the boundary does not carry (user-global helper, system helper, SSH keys, a proxy, a private CA) is refused, not served by ambient fallback.
- A renamed or transferred repository is refused until a new target and binding are recorded.
- A helper that needs interactive UI, or profile variables to find its store, may not work under a cleared environment; that cannot be established without invoking a real helper (not authorized here).

## 4. C. Remote identity and reference boundary

### 4.1 What can and cannot tie the transport to the bound repository ID

| Question | Finding |
|---|---|
| Is there a documented way to push by opaque ID or to receive the repository ID from the Git transport? | **UNVERIFIED**: no source read documents an ID-addressed Git URL or an identity echoed by `receive-pack`. The binding (ADR-0043) is a GraphQL fact; Git is addressed by name. |
| Rename | DOC (re-read directly): after a rename, `git clone`, `git fetch` and `git push` targeting the previous location "continue to function as if made on the new location". With `http.followRedirects=false` (LOCAL P3 R2) the push ended with the 301 error instead. |
| Transfer | DOC (re-read directly): the same redirects; creating a repository or fork at the previous location permanently deletes them. The retired-name statement is from the summarized fetch only. |
| Name replacement (reuse) | DOC (renaming page, re-read directly): reusing the original name of a renamed repository stops the redirects. The old URL then addresses a **different repository**, and nothing in Git distinguishes it. A fresh ID read before the send detects a replacement that already happened; it cannot detect one between that read and the send, and a later reread does not undo an earlier wrong effect. |
| Absent versus inaccessible | DOC (troubleshooting page, re-read directly): "Repository not found" means "the repository does not exist or you do not have permission to access it", and the page says the same error results from pushing to a repository that does not exist. So that error is `Unknown` for GitHub, never absence. LOCAL P5: the double distinguishes a missing ref (exit 0 and no lines; 2 with `--exit-code`), a missing repository (128, `not found`) and a missing credential (128). ADR-0042 already treats `RefNotReturned` as no proof. |
| Default branch | DOC (summarized fetch): changing the default needs another existing branch, so the default is an existing ref. LOCAL P5: `git ls-remote --symref URL HEAD refs/heads/<owned>` returned the default-branch symref and the tip in one read on the double. |
| What a Git-native read proves | **Not the future push's principal and not write permission.** A read uses whatever candidate transport boundary it runs under; on a repository readable anonymously it can succeed with no authentication (INFERENCE: my P5 double required credentials for everything, so anonymous success was not exercised), and a helper may supply different credentials to a later call (INFERENCE). It gives ref and default-branch evidence from the transport it ran on, no identity and no push permission. |
| Dry run, role, later identity reread | A dry run contacts the remote and fires `pre-push` (LOCAL P2 H1) but sends no update and cannot evaluate server rules; a repository role (ADR-0049) is not branch-push permission; a later identity reread observes a later state. None erases an effect that already occurred on the wrong repository. |

### 4.2 Existing refs (LOCAL: Git 2.53.0 behind `git http-backend`; GitHub's server is UNVERIFIED)

The comparison below is a **proposal comparison**, not a selected requirement: a plain explicit-SHA push, and an explicit empty-expectation lease `--force-with-lease=<ref>:` (DOC, re-read directly: "If `<expect>` is the empty string, then the named ref must not already exist"; the same text describes the flag as one that "overrides" the fast-forward restriction when the expectation matches, so it
is a force mode only in form; with an empty expectation the observed existing refs with a different SHA were refused, while an equal SHA was an up-to-date no-op, as the control below shows). Codex has recognized the empty-expectation form as a meaningful non-overwrite candidate; this grants no force mode and no implementation.

| Remote ref before Git's advertisement | Plain push of the recorded SHA (full 40-hex source, qualified destination) | Empty-expectation lease |
|---|---|---|
| Absent | Created (`*`); the update command's old value is all zeroes (P2 H5, P4b U2t) | Created; the wire command is identical (P4b U2t) |
| Exists, **same** SHA | `[up to date]` (`=`), exit 0, nothing sent (P4 U2 same): the **existing-equal-SHA no-op control** | `[up to date]`, exit 0 (same control) |
| Exists, an **ancestor** of the SHA | **Fast-forwarded** (`old..new`): another actor's branch advanced (P4 U2 ancestor) | `[rejected] (stale info)` before sending, no POST in the double's log (P4 U2 ancestor) |
| Exists, **unrelated** | `[rejected] (non-fast-forward)` (P4 U2 other) | `[rejected] (stale info)` before sending |

| Ref created by a competitor **after** the advertisement and before the update is applied | Plain push | Empty-expectation lease |
|---|---|---|
| Same, ancestor or unrelated SHA | `[remote rejected] (reference already exists)`, exit 1; the server kept the competitor's value (P4b U1) | identical (P4b U2r) |

Observation and inference: in these scenarios the server's answer is consistent with a compare-and-swap against the command's old value (zero for a create), which Git sends in both forms. That holds for the
double's `receive-pack`; GitHub's behavior is UNVERIFIED. **The compare-and-swap does not make repeated creates harmless in general (INFERENCE; a reasoned interleaving, not an experiment performed):** if a ref were
**deleted between two creates** of the same ref (by another actor with delete rights, or by a platform mechanism), the second create could succeed again and the effect would be duplicated rather than rejected.
The empty-expectation form additionally refuses, before sending, a ref already present when Git reads the advertisement; it does not address the deletion interleaving either.

### 4.3 Documented guarantees and residual risks

| Item | Class | Statement |
|---|---|---|
| Refspec semantics (`<src>:<dst>`, qualification) | DOC (summarized fetch) | A fully qualified destination is used as-is. LOCAL P2 H4 confirms an unqualified destination for a SHA source is refused |
| A client-side rejection is not sent; a remote rejection is the server's | DOC (re-read directly: `rejected` "Git did not try to send the ref at all"; `remote rejected` "The remote end refused the update"; `remote failure` "did not report the successful update ... a break in the network connection, or other transient error") | The three are distinct statuses; `remote failure` is an explicitly unknown remote effect |
| Name-to-ID window | **Residual (U6)** | Between the last GraphQL identity read and the Git push the name may start addressing another repository. No source read closes it |
| Default-branch or ref change after a read | Residual | In the observed cases, a create command carrying an all-zero old value was refused when a ref appeared after advertisement. This is not a guarantee that a plain push never updates an existing ref: it fast-forwarded an ancestor already present at advertisement (section 4.2). What a first push to an empty repository does to the default branch is not stated on the page read |
| `gh` principal versus Git principal | **Residual (U1)** | Permission, identity and ref observations by `gh` do not prove the Git credential can push; a Git-native read does not either (section 4.1) |
| Server rules (protection, rulesets, hooks) | DOC (summarized fetch) | Unknown until the send |
| Ancestor publication | **Residual (U5)** | A push sends every reachable object the remote lacks, not only the approved head |
| Atomic identity-and-ref guarantee | **Not established** | None is claimed |

## 5. D. Crash, replay and recovery state table

*Conceptual and proposed, pending Codex review. It adds no table, port, API or supervisor; names are placeholders. "Intent", "claim" and any marker may be columns of one entity.*

### 5.1 Conceptual ordering

1. Explicit **execution request**, distinct from ADR-0048 consent, acknowledging the exact source, target, binding, approval, owned branch and SHA. No other authority.
2. Admission: fresh, untracked reads of scope and approval (existing readers); fresh physical ownership, lease, workspace readiness and local commit evidence of the **source** workspace; fresh `Git` snapshot;
   fresh remote identity equal to the binding; optionally a Git-native read of the default branch and ref (an observation only, section 4.1). Remote reads are not effects.
3. **Intent** committed (short write-locked transaction, one journal event).
4. **Claim and repository exclusion** committed in one short write-locked transaction that rereads the whole scope: the claim is single use, and the exclusion (section 5.5) is acquired at or before it. **The claim itself can serve as
   the durable, conservative "may have started" boundary**: once it is committed, the attempt is treated as possibly launched.
5. *Optional:* a separate **launch marker** committed immediately before the process is started. It adds phase information (a crash before it proves the host never reached the launch statement) but is **not proven mandatory**.
   A marker committed before a launch cannot prove the launch happened (the host can stop between the commit and the start); it proves "may have started" only.
6. Launch the pinned push (outside any transaction), bounded.
7. Record **observations** (5.2) and run read-only reconciliation reads.
8. Release the repository exclusion only on conclusive evidence (5.5).

### 5.2 Observations versus whole-attempt certainty

Three kinds of fact, never merged:

**(a) Proven pre-launch refusal.** The host decided to refuse and durably recorded the refusal **before** any call that could start the process, or the process could not start (a launch exception the executor reports). Examples: stale snapshot,
ownership unprovable, identity differs, object absent, no credential source, a planned URL or argv outside the closed set. This is the only kind of "nothing was sent" the host itself can support. If the host stops before it records the refusal, the attempt is
`MayHaveStarted` (conservative; it was claimed).

**(b) Evidence observed during or after a launch** (each describes one exchange or one report; each may follow an effect): a complete porcelain status for the exact ref (`*` created, `=` up to date, `!` with `[rejected]` or `[remote rejected]`, or an unrecognized line), the `To` URL,
the exit status, an executor `TimedOut` or `Cancelled`, a redirect refusal, an unrecognized or truncated output, `remote:` text, a later Git-native or `gh` ref read (absent, equal, different, not found, unauthenticated). **A mismatched `To` line or an unrecognized grammar is a detection after launch, not a refusal before it.**

**(c) Whole-attempt uncertainty.** From the claim (or marker) onward the attempt is `MayHaveStarted` with observations attached. The evidence supports no stronger whole-attempt statement after a launch than the observations themselves: a final server rejection followed by an absent or different ref
cannot exclude an earlier applied-and-moved effect during a transport resend; `Acknowledged` is the server's report for the exact ref, not exactly-once; a matching SHA does not say who pushed. Whether any observation justifies a stronger label (for example for a client-side rejection, which DOC says is never sent) is a decision for Codex (U3, U7).

Vocabulary used below: `NotLaunched` (kind a), `MayHaveStarted` (kind c), and observations (kind b): `AckObserved`, `UpToDateObserved`, `ClientRejectionObserved`, `RemoteRejectionObserved`, `Unclassified` (timeout, cancellation, kill, lost response, unrecognized or truncated output, a non-zero exit without a recognized status), `RefStateObserved(absent | equal | different | notFoundOrDenied | unauthenticated)`.

### 5.3 State table

| # | Transition or boundary | Required evidence | Forbidden inferences | Owner of a late result | How reconciliation prevents a second send or a false success |
|---|---|---|---|---|---|
| T1 | Request received, nothing durable | The request equals the whole fresh scope and approval reading (ADR-0048 readers) | Approval implies permission, readiness or a current remote state; a browser claim or an old observation | None | Nothing sent. A refusal consumes nothing |
| T2 | Replay or conflict | Same request identifier and identical acknowledgement replays the recorded intent or outcome; any other request for an approval that has an intent conflicts (the approval is consent to **one** push) | Treating a replay as permission to send again | The original intent | Replay never starts a process; the conflict is a fixed refusal |
| T3 | Admission refused (before intent) | A named, fixed reason | A refusal is evidence about the remote beyond that read | None | No intent exists, so nothing can be sent later |
| T4 | Intent committed | Whole-scope equality under the write lock | Intent is a send, a reservation of the remote, or proof the checks still hold | The intent | Unique per approval (conceptual key); a competing request or host conflicts at the key |
| T5 | Claim and repository exclusion committed (single use) | Second reread of scope and approval under the write lock; the intent was still `Intent`; **no other run or approval holds exclusion for the same physical repository** | The claim proves a push happened | The claim | A claim that is not `Intent` is refused (same shape as `ExecuteLocalCommitCommandHandler` lines 31-35); an exclusion held by another run or approval refuses or queues this one (ADR-0006) |
| T6 | Post-claim check refuses before the launch | A fixed reason **durably recorded before any launch call** | That the remote is unchanged; any statement if the record was not committed (crash) | The claim | Kind (a), `NotLaunched`, only when the record exists. The claim stays spent; the exclusion is released because the host can show nothing was launched. A new execution needs new authority (none exists in ADR-0048) |
| T7 | Optional launch marker committed, then crash or launch failure | The marker row; a recorded launch exception when it occurred | That an absent remote ref means the process never started; that the marker proves a launch | The attempt | A crash after the marker is `MayHaveStarted`. A recorded launch exception is kind (a). The conservative cost of committing before the launch is that "never started" is not provable after a crash |
| T8 | Process running | One child, one deadline, bounded captures, exclusion held | Progress implies success; a quiet process is dead | The attempt | Nothing else may start; the exclusion stays held |
| T9 | `[rejected]` for the exact ref in complete porcelain (DOC: Git did not try to send the ref) | Complete grammar and exit status | That the rest of the attempt is clean if the grammar is incomplete | The attempt | `ClientRejectionObserved`; no retry. Corroborated for the empty-expectation and non-fast-forward cases by LOCAL P4 U2 (no POST in the double's log) |
| T10 | `[remote rejected]` for the exact ref | Complete grammar, exit status, and a reconciliation read | That nothing was applied. LOCAL P4b U3: one client invocation produced **two** receive-pack POSTs after a dropped connection while the first had applied; the second response's size (73 bytes) equals the "reference already exists" rejection of P4b U1, but the double dropped that body and it was not captured (so it is not shown what the second said, nor why the client resent). A reported `reference already exists` can describe a ref holding **our** SHA, and a final rejection followed by an absent or different ref cannot exclude an earlier applied-and-moved effect | The attempt | `RemoteRejectionObserved` plus the ref read; the attempt stays `MayHaveStarted`. No whole-attempt "failed" is concluded |
| T11 | Acknowledged creation | Complete porcelain `*` for the exact ref, exit 0, and a ref read that shows the SHA on the exact ref | "Exactly once"; "nobody else pushed this SHA" | The attempt | `AckObserved`; the claim is spent so no second process can start |
| T12 | Up to date | `=` for the exact ref, or the pre-send read already showed the SHA | That DevalCopilot pushed it | The attempt (or none) | `UpToDateObserved` (pre-existing equality when the pre-send read showed it); never labelled a successful push |
| T13 | Timeout, cancellation, host shutdown (client killed) | The executor outcome or a recorded stop; no exit code for a killed process (CODE) | The remote is unchanged because the client died: LOCAL P4b U4 (the double applied the buffered push 3.0 s after the client was killed at 1.0 s) read **absent at about 1.0 s and 2.3 s and present at about 4.5 s**; P4b U5 (killed before the acknowledgement) read **present immediately**. The late apply in U4 is the double's configured behavior; real servers are UNVERIFIED | The attempt | `Unclassified` and `MayHaveStarted`. Read-only reconciliation may append observations; absence is never promoted to "not launched" |
| T14 | Lost response | A non-zero exit with no recognized status line | The push failed | The attempt | LOCAL P4b U3: the ref was present after the failure. `Unclassified`, then observations; never a retry |
| T15 | Child possibly still running (timeout kill not confirmed, host crash, restart) | No process identifier, start time or job object is recorded by the executor (CODE) | **That an elapsed deadline proves the child stopped, or that waiting permits releasing exclusion or disposing resources the child may still use** | The attempt (possibly an orphan) | The attempt stays `MayHaveStarted`; the exclusion and the isolated transport repository (if used) are retained. Read-only reads may continue but release nothing. Release needs conclusive evidence of termination (a recorded identity and job-object evidence, or an explicit human decision); neither exists today (U12) |
| T16 | Crash after the claim (or marker) and before the outcome is recorded | The claim; no outcome | Anything | The attempt | Startup recovery: `MayHaveStarted`, then read-only reconciliation (never a relaunch); exclusion retained |
| T17 | Crash after the outcome, before the notification | The recorded outcome | The notification failed so the outcome is not real | The attempt | The notification is a hint (ADR-0003, ADR-0040) |
| T18 | Restart reconciliation | Fresh identity equal to the binding; a Git-native or `gh` ref read (observations only) | Any observation proves causality or principal | The attempt | See 5.4 |
| T19 | A late observation after the attempt was classified | The read result and its time | A later read rewrites history | None | Observations are append-only; a later absence never turns an unresolved attempt into `NotLaunched`, and a later read never turns it into "failed" |

### 5.4 Reconciliation observation matrix (all reads read-only; no read can cause a send)

| Whole-attempt state | Remote read | Recorded observation | Not concluded |
|---|---|---|---|
| `NotLaunched` (kind a) | any | Append only | Remote state |
| `MayHaveStarted` | Ref absent | `RefStateObserved(absent)`. Re-read later; still not `NotLaunched` | Failure, or that nothing was sent |
| `MayHaveStarted` | Ref equals the recorded SHA | `RefStateObserved(equal)`: **equality of unknown causality**. Pre-existing equality is claimable only if a pre-send read already showed the SHA; an acknowledged creation needs a recorded complete `*` | That this host pushed it, or that someone else did |
| `MayHaveStarted` | Ref holds another SHA | `RefStateObserved(different)` (rejected, never applied, or applied and moved by another actor) | That our push failed |
| `MayHaveStarted` | Not found, 401, 403, identity differs from the binding | `RefStateObserved(notFoundOrDenied)` or identity change (attention). "Not found" is never absence (DOC) | Anything about the ref |
| Outcome recorded | any | Append the observation | |

### 5.5 Conceptual bounds and resource ownership (no new components)

- **Repository exclusion (required, ADR-0006/0008).** The push holds exclusion for the physical repository, covering **other runs and other approvals**, from the claim until conclusive resolution. A per-approval unique key is additional and
  prevents a second request for the same approval. The mechanism (a `Committing`-like reservation, a new workspace state, lease-only) is a future design detail.
- **Release is evidence-based, not time-based.** `NotLaunched` (kind a) or an outcome the host can show conclusive allows release. A deadline, a post-kill wait or a quiet period does **not** (T15). Uncertain outcomes stay conservative: the exclusion remains and the attempt needs
  attention until evidence or a human decision resolves it.
- **Resources owned by the attempt:** under candidate A the transport repository directory and its alternates file (created before the claim boundary is relied on, removable only after the child is known stopped or the attempt is resolved; deleting it under a child that may be running can corrupt or change what is sent),
  the bounded captured output (never persisted raw; untrusted text), and the one child process. Credentials are never written by the host; a helper's `store` side effect is outside the host's ownership.
- **Bounds:** one process, one refspec, a wall-clock bound (U9), capture caps comparable to the observers (8 KiB per stream, 16 KiB combined), a post-kill wait.
- **Competing requests and hosts:** two requests on one database serialize at the write lock and the claim; two hosts on separate databases are not covered by exclusion and are limited only by the server's answers and the equal-SHA no-op.
- **No second send by construction:** a single-use claim; reconciliation uses only read-only commands; no automatic retry exists. LOCAL P4b U3 shows that **one client invocation can send more than one POST**, so "no blind retry" cannot rest on host logic alone; what limits a duplicate is the server's answer
  (observed on the double, section 4.2) and the interleaving caveat there.
- **No claim of exactly-once remote execution** is made or supportable.

## 6. E. Constraints, proposals, unresolved choices and acceptance matrix

### 6.1 Planner constraints versus executor proposals, and points for discussion

| # | Item | Status | Evidence and discussion |
|---|---|---|---|
| 1 | Planner constraint: no force and no force-with-lease | Constraint as dispatched; Codex has since recognized the empty-expectation form as a meaningful non-overwrite candidate, **granting no force mode and no implementation** | Section 4.2 compares a plain explicit-SHA push with the empty-expectation lease, with the existing-equal-SHA no-op control. Create-only is **not** a selected product requirement. The comparison is input to Codex (U2) |
| 2 | Planner constraint: reuse existing Git authentication, no setup, no ambient fallback | Constraint | Under the isolation the same constraints ask for, a credential source must come from somewhere (U1). The tested no-helper arrangement refused (P3 A1); that is not a proof that no safe arrangement exists |
| 3 | Planner constraint: fresh remote checks | Constraint | The existing observers use `gh`; a Git-native read is an extra observation with the limits of section 4.1 (anonymous success possible; no principal; no write permission) |
| 4 | Planner constraint: no blind retry | Constraint | Not contradicted. One client invocation can send more than one POST (P4b U3) |
| 5 | Planner constraint: durable intent and a single-use claim before any effect | Constraint | A conservative "may have started" boundary must precede the launch; **the claim itself can be that boundary**; an additional launch marker is optional phase detail, not proven mandatory, and cannot prove a launch happened (5.1) |
| 6 | Planner constraint: repository exclusion | Accepted requirement (ADR-0006/0008), restored in 2.1, 5.1 and 5.5 | Not a proposal and not optional |
| 7 | Proposal: isolated host-owned bare transport repository | **Proposal, requires an explicit ADR-0008 execution-location extension** (3.3) | Alternates resolution is not physical safety |

### 6.2 Unresolved questions

*Classification is a proposal for Codex. No row is an approval, and no row asks the owner to adopt a design. Codex decides which are resolved by the contract and which reach the owner.*

**Authority or risk by nature (candidates for the owner, if Codex agrees):**

| Id | Question | Alternatives and consequences |
|---|---|---|
| U1 | Source of authentication for a push under the chosen boundary | (a) Closed refusal unless the owner designates a helper in host configuration (a new persisted setting). (b) The host reads only the credential keys of the user's global Git configuration for `github.com` (reads credential configuration). (c) Pass system and global configuration through (broad; contradicts "no ambient fallback"). (d) Make `gh` the Git credential helper via `gh auth setup-git` (a configuration change; principal becomes `gh`'s; excluded by the constraints). Each changes what the product stores or reads |
| U5 | Ancestors: a push publishes every reachable object the remote lacks, not only the approved head | (a) Accept. (b) Require the commit's parent to equal an observed remote tip. (c) Preview the object count (needs remote refs fetched, a new network step). (a) may publish unpublished local history; (b) refuses legitimate ancestry; (c) adds a fetch |
| U6 | The name-to-ID window | (a) Accept and disclose. (b) Bracket the push with identity reads and treat a change as an attention observation (detects some changes after the fact; cannot undo). Neither closes it |
| U11 / U13 | Real-provider and real-helper compatibility | The doubles prove Git behavior only. Verifying a real helper, the `gh`/Git principal relation, GitHub's rejection forms and its behavior needs an owner-authorized run against a disposable repository. This is a **separate gate from the existing Agent qualification ledger**; neither is relaxed or authorized here |

**Technical questions for Codex:**

| Id | Question | Alternatives and consequences |
|---|---|---|
| U2 | Plain push versus empty-expectation lease | Section 4.2. The lease refuses, before sending, a ref present at advertisement; a plain push can fast-forward an ancestor ref created by another actor. Neither addresses a delete between creates |
| U3 / U7 | Which observations (if any) may support a stronger whole-attempt statement, and what authority exists after `MayHaveStarted` | ADR-0048 consent is for ONE push, so no retry authority exists. A resend after review would need a new decision |
| U4 | Require the local owned branch tip to equal the SHA at send time, or only that the commit object exists | Strict refuses after any later local change; relaxed publishes the approved SHA regardless of the local branch |
| U8 | Executor trigger | The request runs the claim and send inline, or a poller claims a `Prepared` intent; intent existing only after an explicit request is the safety property |
| U9 | Wall-clock and size bounds | Larger delays resolution; smaller produces more unclassified outcomes. Transfer size depends on U5 |
| U10 | **Reservation mechanism** for the required exclusion (not whether to exclude) | Reuse a `Committing`-like workspace reservation, add a state, or lease-only; consequence: Agent work is blocked for the transfer and while an attempt is `MayHaveStarted` |
| U12 | Termination evidence for a child | A Windows job object with kill-on-close plus a recorded process identifier and start time would give conclusive evidence; it requires changing the process executor. Without it, an uncertain outcome keeps exclusion until a human decision |
| U14 | Execution location (3.3) | Candidate A needs an ADR-0008 execution-location extension (marker, lifetime, cleanup, alternates path proof); candidate B needs a complete configuration-key inventory and accepts a validation window |

### 6.3 Future implementation acceptance matrix

Cheapest proving boundary first; broaden only on the trigger in the last column.

| Risk | Cheapest proving boundary | Focused check | Broaden when |
|---|---|---|---|
| Exact argv, environment and pins; refusals; no ambient variable reaches the child | A pure builder of the push plan plus the adapter against an owned process-executor double (the existing observer pattern) | Argument and environment equality, one test per proven pre-launch refusal, a sentinel secret never appears in any captured value | The executor, the Git capability snapshot or shared environment code changes |
| Real Git behavior of every pin (hooks off, no rewrite, redirect refused, no extra refs, the chosen lease form, porcelain grammar, extra POST) | Infrastructure integration against real Git and local bare repositories, with an owned HTTP double for redirect, authentication and drop cases (the probes under `logs/executor/first-push-evidence/scripts` are the scenarios) | One test per row of 3.1 that rests on LOCAL evidence | The Git version or the helper arrangement changes |
| Observation classification | Pure parser tests over retained porcelain and `remote:` fixtures, including the extra-POST and truncated cases | Each observation of 5.2 and the rule that an unrecognized line is `Unclassified` | Any new status or locale behavior |
| Request, replay, conflict, claim, whole-scope rereads | Application handler tests on file-backed SQLite (the ADR-0048 test base) plus one Api test of the route | Admission refusals, replay, conflict, concurrent identical and competing requests, drift under the lock | Shared transaction, mediator or admission behavior changes |
| **Repository exclusion across runs and approvals** | Application tests on file-backed SQLite with two runs or approvals for one physical repository (the existing lease and reservation tests are the pattern) | The second is refused or queued; exclusion is not released by a deadline; it is released only on the conclusive evidence of 5.5 | Lease, reservation or workspace-status code changes |
| Crash boundaries T4 to T17 | Deterministic fault injection at each durable boundary with an owned runner fake and a second host composition on the same database (the existing command-interceptor pattern) | A runner invocation counter proves no second send after every boundary and restart; state equals the table | The state or event model changes |
| Reconciliation matrix | A pure policy theory over (whole-attempt state x observation) | Every row of 5.4, including "absent never becomes `NotLaunched`" | A new observation source |
| Authority separation | Architecture test: only the push runner composes network-capable Git arguments | One inventory test | A second Git-network component appears |
| No secret leakage | A sentinel credential through a helper double; assert absence in the database, events, logs and captured process text | One test | The helper arrangement changes |
| Real compatibility (helper, GitHub, principal, rejection text, server behavior) | **Not provable by a double.** An owner-authorized qualification run against a disposable repository, separate from the Agent ledger | Complete retained logs | Never automatic; a release gate (U11/U13) |
| UI (only if a request surface ships) | Decoder, hook and panel tests; one journey at the end | Existing frontend pattern | Shared cockpit state changes |
| Final regression | Focused families above, full Architecture, `dotnet format` on changed files, client reproducibility only if an API shipped | Run once on stable inputs | Per the [validation policy](../roadmap/validation-policy.md) |
| Mutation (bounded, serial) | Hash-verified compiling mutants | Skip the claim; launch before the claim; treat a timeout as a failure or as `NotLaunched`; treat equality as success; drop one pin; trust the approval without the reread; release exclusion on a deadline | |

## 7. Sources and probes

### 7.1 Primary documentation (accessed 2026-10-15)

"Re-read directly" means the tag-stripped text of the page was read in full for the named sections, with the page-version limits stated in the conventions above; "summarized" means the page was read only through a summarizing fetch tool.

| Source | Used for | Reading |
|---|---|---|
| [git push](https://git-scm.com/docs/git-push) | `--force-with-lease` forms and the empty expectation, `--dry-run`, `--porcelain`, status flags and the `rejected` / `remote rejected` / `remote failure` summaries, `push.followTags` | Re-read directly; other options (`--atomic`, `--receive-pack`, `--recurse-submodules`, `--tags`, `--mirror`, `--delete`) and explicit-refspec precedence: summarized |
| [git config](https://git-scm.com/docs/git-config) | `GIT_CONFIG_GLOBAL`, `GIT_CONFIG_SYSTEM`, `GIT_CONFIG_NOSYSTEM`, the files list, `config.worktree` | Re-read directly; includes, `url.*`, `http.*`, `protocol.*`, `core.*`, `push.*`, protected configuration: summarized |
| [gitcredentials](https://git-scm.com/docs/gitcredentials) | Helper forms, the empty-string reset, askpass order, `useHttpPath` | Re-read directly for the cited statements; the rest summarized |
| [git (environment)](https://git-scm.com/docs/git) | `GIT_TERMINAL_PROMPT`, `GIT_ASKPASS`, `GIT_ALLOW_PROTOCOL`, `HOME` on Windows | Summarized, and only partly read (sections on `GIT_EXEC_PATH`, `GIT_PROXY_COMMAND`, `GIT_HTTP_*` not reached) |
| [githooks](https://git-scm.com/docs/githooks) | `pre-push` and its abort, `core.hooksPath` | Re-read directly for `pre-push`; server hooks summarized |
| [Renaming a repository](https://docs.github.com/en/repositories/creating-and-managing-repositories/renaming-a-repository) | Old-location `clone`, `fetch` and `push` continue to function; reusing the original name stops redirects | Re-read directly |
| [Transferring a repository](https://docs.github.com/en/repositories/creating-and-managing-repositories/transferring-a-repository) | Redirects after a transfer; a repository or fork created at the previous location permanently deletes them | Re-read directly for those statements; retired names summarized |
| [Troubleshooting cloning errors](https://docs.github.com/en/repositories/creating-and-managing-repositories/troubleshooting-cloning-errors) | One "Repository not found" for a nonexistent and an inaccessible repository, including a push to a repository that does not exist | Re-read directly |
| [About protected branches](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches) | Server-enforced rejections; rulesets are separate and were not read | Summarized |
| [Changing the default branch](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-branches-in-your-repository/changing-the-default-branch) | Changing the default needs another existing branch; nothing on a newly pushed repository | Summarized |
| [Caching your credentials in Git](https://docs.github.com/en/get-started/git-basics/caching-your-github-credentials-in-git) | `gh` can store credentials for Git at login; the Git Credential Manager ships with Git for Windows; no statement that Git and `gh` share one principal | Summarized |
| [gh auth setup-git](https://cli.github.com/manual/gh_auth_setup-git) | Registers `gh` as a Git credential helper (a configuration change) | Summarized |

### 7.2 Probes (LOCAL; synthetic)

Retained under `logs/executor/first-push-evidence/` (ignored by Git): `scripts/`, `sandbox-r1-20261015T1320Z/out/`, `README-r1-20261015T1320Z.md` and `manifest-r1-20261015T1320Z.sha256` (original, unmodified), and the
correction-round files `r2-sources/`, `r2-sources.sha256`, `README-r2-corrections.md` and `manifest-r2-corrections.sha256`. Every call used a replaced home, `GIT_CONFIG_NOSYSTEM=1`, an empty global file inside a new sandbox and `env -i`; no real credential
configuration, store or helper was read or invoked; no external endpoint was contacted (`github.invalid` and `example.invalid` never resolve; the only listener was an owned `127.0.0.1` Node double in front of `git http-backend`); this checkout and its origin were not a target.

| Probe | What it showed | Elapsed (script-measured where stated) |
|---|---|---|
| P1, P1b, P1c | Layered configuration, includes, `-c` and `GIT_CONFIG_COUNT` precedence, rewrite of an explicit URL by `insteadOf` and `pushInsteadOf`, `pushurl` for a named remote only, `config.worktree`, an isolated repository through alternates, `--get-url` shows only the `insteadOf` expansion | 2.5 s (P1 total) |
| P2 | `pre-push` fires on dry run and is suppressed by an empty `core.hooksPath` or `--no-verify`; server hook output and rejection; `push.followTags` extra tag; full-SHA create, repeat, fast-forward, non-fast-forward, unqualified destination refused, abbreviated source accepted; wire command old value | 4.2 s |
| P3, P3b | A helper from repository configuration executes; reset and host helper; ambient askpass despite `GIT_TERMINAL_PROMPT=0`; `store` and `erase`; helper context; redirect following and refusal; one key not re-read after launch | 16.2 s (P3) |
| P4 (defective), P4b | Race and existing-ref matrices, the empty expectation, the wire command, a lost acknowledgement with an extra POST, a kill before apply (late apply), a kill before the acknowledgement | 25.7 s (P4), 16.7 s (P4b) |
| P5 | Git-native read-only observation: default-branch symref, absent ref, missing repository, no credential, bad credential (the double required credentials for everything) | not timed |

**Capture limits (what the retained files do and do not contain).** The retained `p*.log` files are the scripts' combined standard output and error, and the scripts themselves truncate or filter some of it. Complete originals exist where stated; nothing has been reconstructed.

- `scripts/p4_...sh`, `scripts/p4b_...sh` and `scripts/p5_...sh`: the seeding pushes were piped through `tail -1`, so only their last line is retained (their other lines were not kept anywhere).
- `scripts/p4_...sh` and `scripts/p4b_...sh`: the `killrun.js` output is piped through `head -12`, and `scripts/killrun.js` itself keeps at most 4000 characters of each captured stream. In every killed run (P4 U4 and U5 in their defective form, P4b U4 and U5) the output shown is what the client had written before the kill; **partial child output that was not yet flushed or read at the kill is unavailable**, and in the retained P4b U4 and U5 runs the captured stdout and stderr are empty, which does not show what Git would have printed.
- `scripts/p2_...sh` prints the packet-trace lines through `head -3`; the complete trace files are retained (`out/p2-trace-create.txt`, `out/p2-trace-update.txt`, `out/p4b-trace-plain.txt`, `out/p4b-trace-lease.txt`). `scripts/p1c_...sh` pipes its last command through `head -3`.
- The displayed "double request log" in P3, P4 and P4b omits each file's first line (`sed -n '2,$p'`, the `listening` event) and the timestamps; the complete per-scenario `double.log` files are retained under `out/`. The double's `backendStderr` field is cut to 400 characters (`httpdouble.js`).
- The P3 helper-call and askpass logs and the P2 hook log are **reset at the start of each scenario**, so per-scenario contents survive only as printed in the corresponding `p*.log`; the files left on disk hold the last scenario.
- The HTTP trace of the lease create in P4 (`out/p4-trace-lease-create.txt`) matched nothing in the greps (the transport helper writes those lines); the wire evidence for the empty expectation is the file-transport trace of P4b.
- The dropped response body in P4 U3 and P4b U3 was never captured (the double destroyed the socket), so what the second POST answered is inferred only from its length.

Failed attempts kept (not relied on): P1 section A (an includeIf case that did not match), P3 C1 and C2 (the hook write was unverified), P4 U1 and the lease race (the competitor hook never ran), P4 U3 (observation after the double stopped), P4 U4 and U5 (`taskkill` missing under the minimal `PATH`; the client was never killed).
Each was repeated once in a corrected form (P1b, P3b, P4b). The scripts `httpdouble.js` and `killrun.js` were edited between runs (documented in the retained README).

### 7.3 What this evidence does not establish

- GitHub's `receive-pack`, rulesets, rate limits, rejection text, atomicity, retries or default-branch behavior; `gh` behavior of any version; the real credential helper (its `store` behavior, interactivity, environment needs); Git and `gh` principal equality; an ID-addressed transport; the effect of a proxy or private CA; LFS and submodule ancestry beyond the documents; Git 2.53.0 manual text.
- Any property of the host's real process executor under a Windows job object or orphan conditions (not run: no build was authorized).
- Why the client sent a second POST in the lost-response scenario; what that second POST answered.
- Exactly-once remote execution, an atomic identity-and-ref guarantee, or the absence of a residual race.
- Real Git/GitHub compatibility qualification (not started and separate from the Agent ledger). Agent qualification stays `Blocked`/`TargetsNotReal` with both allowances unused. The historical concurrent-replay 409, the older target-test 500, the disposal `NullReferenceException` and the unnamed Api 2067/2068 failure remain separately unexplained.

## 8. Decision addendum (2026-10-15): owner scope and Proposed ADR-0050

*An addendum. Sections 1 to 7 are the historical evidence and limitations as published and are not amended by it; where they say "unresolved", the addendum records only what the owner and Codex have since decided for design purposes.*

**Owner decision.** The owner explicitly accepted advancing only the *preparation* of an execution contract within a restricted profile: an already installed, explicitly enabled, noninteractive Git Credential Manager; the recorded
commit's parent confirmed as the tip of an existing remote branch; and the residual name-to-ID risk documented, not measured. It authorizes no real call, credential access, installation, login, commit or push, and it
does not accept any ADR.

**What the decision resolves, for design purposes only, and what it does not.**

| Open item of section 6.2 | Status after the decision |
|---|---|
| U1 credential source | Narrowed to the restricted GCM profile for the first contract. Installed-version support, noninteractive behavior, helper side effects and environment needs remain **unverified** and are proof obligations in [ADR-0050](../decisions/0050-define-the-first-approved-github-branch-publication-execution-contract.md) (Proposed) |
| U5 ancestor publication | Narrowed: the first profile requires the single recorded parent to equal the freshly admitted remote **default**-branch tip; no other unpublished ancestry and no fetch |
| U6 name-to-ID window | Documented, accepted for design only; not atomically closed, not measured, not an authorization (ADR-0050, "Residual-risk disclosure") |
| U2 lease form | The empty-expectation lease is the **proposed** transport guard (not permission to execute it; GitHub's behavior unverified) |
| U10 reservation, U14 execution location | Exclusion is retained as an **independent durable publication fence** keyed by physical repository identity and acquired atomically with the claim (a selected conceptual mechanism; an `Active` lease alone is not exclusion and lease supersession cannot remove the fence); the transport scratch is a **proposed** narrow ADR-0008 extension; schema and implementation are separately bounded contracts |
| U11/U13 real-helper and provider compatibility | **Unresolved.** Separate, unstarted, needing its own explicit decision; separate from the Agent qualification ledger |
| U3/U7, U4, U8, U9, U12 | Addressed as proposed contract text or proof obligations in ADR-0050; none is accepted. For U8 the proposal is that a request only records an explicit intent and the supervisor of the original live host session claims it once; a restart leaves it dormant, and the human rearm and resolution path is a separately reviewed activation prerequisite. The source-write-denial requirement stays visibly unproven (ADR-0050 P9) |

Sources read for the proposal on 2026-10-15 (page text kept as reading aids under ignored `logs/executor/first-push-evidence/r3-sources/`): GCM `docs/configuration.md`, `docs/environment.md`, `docs/credstores.md` and
`docs/faq.md` (`main` branch, not the installed version), git-ls-remote and gitrepository-layout, GitHub's published GraphQL schema (`Repository.defaultBranchRef`), and Microsoft Learn's job-object, accounting, limit,
completion-port and `AssignProcessToJobObject` pages. These are documentation, not compatibility evidence. No probe, real helper or provider call was made for this addendum or for ADR-0050, and the unchanged
executable evidence of sections 3 to 5 is retained.
