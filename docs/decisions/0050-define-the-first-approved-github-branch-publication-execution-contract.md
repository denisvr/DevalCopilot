# ADR-0050: Define the first approved GitHub branch publication execution contract

Status: Proposed

> **This ADR is a proposal for Codex review. It is documentation only.** It is not Accepted, it activates nothing, it authorizes no
> runtime implementation, no real Git, GitHub or credential-helper call, no credential access, installation, login, commit or push, and it
> does not qualify any provider. Nothing here is present permission to execute any Git flag. Every statement of what a future executor
> "does" describes the contract that a later, separately bounded and separately reviewed implementation must satisfy.

## Context

[ADR-0005](0005-use-git-worktrees-and-a-policy-controlled-github-loop.md) makes the orchestrator, not an Agent, the executor of pushes, requires existing Git authentication,
forbids force-push, history rewriting and branch deletion in the MVP, and requires a remote lookup before a push or pull-request timeout is retried.
[ADR-0048](0048-approve-one-future-github-branch-push-of-a-bound-recorded-local-commit-on-explicit-request.md) records the Human's immutable consent to ONE future normal push of the exact
recorded local commit to its exact owned branch in the repository named by a coherent [ADR-0040](0040-record-an-immutable-github-observation-scope-for-a-completed-local-delivery.md)
target and [ADR-0043](0043-bind-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md) binding, and states that a future executor needs its own explicit request, its
own review and fresh checks. [ADR-0049](0049-observe-the-github-repository-permission-of-a-bound-recorded-target-on-explicit-request.md) observes the repository permission GitHub reports and
states that it is not branch-push permission. No component sends anything to a remote.

The published [execution-evidence note](../architecture/github-branch-publication-execution-evidence.md) established, from code, primary documentation and synthetic local probes only, what a
remote push needs and what the existing local-commit runner and recovery cannot supply. This ADR turns that evidence and the planner's design direction into one proposed contract for the first
approved branch publication. It does not repeat the evidence; section "Factual basis and limits" states what each claim rests on.

### Accepted owner scope

The owner has explicitly permitted contract preparation within one restricted profile, and nothing more:

- An **already installed, explicitly enabled, noninteractive Git Credential Manager** (GCM) as the only credential source.
- The recorded commit's **parent confirmed as the tip of an existing remote branch**; this ADR narrows that to the remote default branch (decision 2.4).
- The **residual name-to-ID risk is documented** (see "Residual-risk disclosure"): a fresh identity read, a fixed destination, refused redirects and a later observation do not atomically prevent a
  repository replacement by name and do not undo a wrong effect. The acceptance is design-only and numerical risk is not measured.

This is not permission for real calls, credential access, installation, login, commit or push. The acceptance of these two decisions is not asked for again and is not runtime risk acceptance.

### Out of scope

A pull request, a correction push, a rerun, a force overwrite, branch deletion, a default-branch push, another SHA, ref or repository, an Agent role in publication, automatic consent, any other credential
helper, any ambient configuration import, arbitrary unpublished ancestry, a fetch, a human rearm or resolution operation, and the API, wire, schema and cockpit designs (identified in "Implementation boundaries").
Real Agent qualification stays `Blocked`/`TargetsNotReal` with both allowances unused. Future Git/GitHub compatibility qualification is separate, unstarted and needs its own explicit decision.

## Factual basis and limits

Evidence classes: **CODE** (anchored to the repository at `e2e89110f9db44d3e96cea21f2d5989abc255af5`), **DOC** (what a primary page states; read 2026-10-15; the installed versions of Git and
GCM were not inspected and the pages describe the latest or `main` documentation), **LOCAL** (the synthetic probes of the evidence note; a local double, never GitHub or a real helper),
**INFERENCE** and **UNVERIFIED**. Documentation is never real compatibility evidence; every DOC statement below that a design depends on is also a proof obligation (section "Proof obligations").

| Fact the proposal relies on | Class and source |
|---|---|
| An empty expectation `--force-with-lease=<ref>:` means the named ref must not already exist; the flag otherwise overrides the fast-forward restriction when its expectation matches | DOC, [git push](https://git-scm.com/docs/git-push), re-read directly. LOCAL P4 U2, P4b U2r, U2t: an existing **equal** ref was reported up to date (nothing sent); an existing **ancestor** or **unrelated** ref was refused (client side, before sending); the wire command was identical to a plain create |
| `git push --porcelain` writes one tab-separated status line per ref (`*` new, `=` up to date, `!` rejected or failed) and `rejected` means Git did not try to send the ref, `remote rejected` means the remote refused, `remote failure` means the remote did not report success | DOC, git push, re-read directly |
| `--no-recurse-submodules`, `--[no-]signed`, `--push-option`, `--no-follow-tags` and `push.followTags` exist; `--no-follow-tags` suppressed a tag push under an explicit refspec | DOC, git push (flags re-read directly); LOCAL P2 H3 (`--no-follow-tags` only) |
| `protocol.allow` and `protocol.<name>.allow` set per-protocol policy for clone, fetch and push; known-safe protocols default to `always` | DOC, [git config](https://git-scm.com/docs/git-config), re-read directly. The exact pin spelling for `https` is unprobed |
| `credential.helper` set to the empty string resets the helper list; a value that is an absolute path is used verbatim; helpers are tried in turn; with no helper Git tries `GIT_ASKPASS`, `core.askPass`, `SSH_ASKPASS`, then the terminal | DOC, [gitcredentials](https://git-scm.com/docs/gitcredentials), re-read directly. LOCAL P3 A2, A4 |
| `GIT_CONFIG_GLOBAL`, `GIT_CONFIG_SYSTEM`, `GIT_CONFIG_NOSYSTEM` replace or skip the global and system files; the local `$GIT_DIR/config` and `config.worktree` are separate files | DOC, git config, re-read directly. LOCAL P1, P3 (repository-local helpers, rewrites and hooks took effect) |
| GCM can be configured through Git configuration; it can be told not to interact, in which case "an error is returned" if interaction is required (`credential.interactive`, `GCM_INTERACTIVE` set to `false` or `0`); the Windows default credential store is `wincredman` | DOC, GCM `docs/configuration.md`, `docs/environment.md`, `docs/credstores.md` on the `main` branch, read directly 2026-10-15. Not the installed GCM version |
| A credential helper is invoked per operation (`get`, `store`, `erase`) and may answer, or not, differently on later calls | DOC gitcredentials (re-read); LOCAL P3 A3-A5 (`store` after success, `erase` after failed authentication) on sentinel helpers only |
| `git ls-remote --symref` shows the symref `HEAD` and the object it points to; `--exit-code` exits 2 for no matching refs; `--get-url` shows only the `insteadOf` expansion | DOC, [git ls-remote](https://git-scm.com/docs/git-ls-remote), read directly. LOCAL P1c, P5 |
| `Repository.defaultBranchRef` is a nullable `Ref` ("The Ref associated with the repository's default branch") in GitHub's published GraphQL schema | DOC, `https://docs.github.com/public/fpt/schema.docs.graphql`, read directly 2026-10-15. No existing observer queries it |
| `git clone`, `fetch` and `push` at a renamed or transferred repository's previous location keep working through redirects; creating a repository at the original name stops them | DOC, GitHub renaming and transferring pages, re-read directly |
| One "Repository not found" error covers a nonexistent repository and one the credential cannot access, including on a push | DOC, GitHub troubleshooting page, re-read directly |
| A repository can borrow objects through `objects/info/alternates`; new objects are stored in the borrowing repository's own object store; an absolute path works locally | DOC, [gitrepository-layout](https://git-scm.com/docs/gitrepository-layout), read directly; the write behavior of a push is INFERENCE and a proof obligation. Alternates is an object-lookup mechanism, not an access restriction |
| Windows jobs: with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, "all processes associated with the job" terminate when the last handle to the job is closed; child processes created after association are associated by default unless breakaway is allowed; accounting reports `TotalProcesses` (associated during the job's lifetime) and `ActiveProcesses`; a completion-port message `JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO` exists; nested jobs need Windows 8 or Server 2012. The pages treat job limits and accounting (process lifetime) separately from "Job Object Security and Access Rights" and process security (access to the job and to processes); **no restriction of which files a job's processes may write was found in the pages read** | DOC, Microsoft Learn job-object pages, read directly 2026-10-15. Whether a process spawned before association escapes the job, and whether the existing executor can start a process suspended, is INFERENCE or UNVERIFIED |
| The existing executor has no job object, no process identifier in its result, no way to say that a process was never created, and a best-effort 5 s post-kill wait | CODE, [`ChildProcessExecutionAdapter`](../../src/backend/DevalCopilot.Infrastructure/Features/Processes/ChildProcessExecutionAdapter.cs), [`ProcessExecutionResult`](../../src/backend/DevalCopilot.Application/Features/Processes/Ports/ProcessExecutionResult.cs) |

## Decision (proposed)

### 1. Authority and exact scope

1. A publication is performed only on a **separate explicit execution request** that is distinct from the ADR-0048 consent. The request acknowledges, as complete values, the exact source (run, project,
   local-commit operation, workspace, the 40-character commit and the owned branch), the ADR-0040 target, the ADR-0043 binding and the ADR-0048 approval, including the opaque repository node ID.
   The wire form, the identifiers and the operation shape are an implementation contract (boundary B1), not decided here. **The request only records an explicit intent** (decision 6); it never claims and never launches.
2. The executor requires a **coherent ADR-0048 consent** for the run and the **exact ADR-0040/0043 scope** that consent snapshots. It reads the scope and the approval through the existing readers over fresh,
   untracked pinned rows, compares **complete values** (not identifiers) between the initial reading and the reading inside each short write-locked transaction, and refuses an incoherent, orphaned,
   drifted or incomplete record. A coherent approval is never recreated or modified, and consent is not reset by any outcome.
3. Authority is exactly one push of the one approved SHA to the one approved owned `refs/heads/<branch>` in the one bound repository. There is no Agent authority, no implicit or automatic consent, no pull
   request, no correction push, no rerun, no deletion and no expansion to another SHA, ref or repository. A claim (decision 6) **consumes** the consent of **that approved operation**: no second claim is
   permitted for it, whatever the outcome. **Duplicate prevention is scoped to the approved operation** (its run, approval, target, binding and owned branch). The same SHA approved in an unrelated repository or
   scope is a different approved operation and is not restricted by this contract. The approval row stays immutable (ADR-0048), so consumption is a separate fact (boundary B2).

### 2. Authentication profile and ancestry

1. The only supported credential source is an **already installed, explicitly enabled, noninteractive Git Credential Manager**. "Explicitly enabled" is an opt-in recorded outside the repository under review and
   outside any request body; its location and shape are boundary B2. "Noninteractive" means GCM is configured, through its documented settings, to return an error rather than show a prompt, and the child is
   started with prompts disabled (`GIT_TERMINAL_PROMPT=0`) and no askpass.
2. **No other helper, no ambient or global helper or configuration import, no `gh`-as-helper, no token or secret extraction or copying, no login, setup or installation.** The executor generates a host-owned
   configuration file that resets the helper list and names exactly the one pinned GCM, and it reads no credential value. A pinned GCM is identified by a recorded supported-version identity (path, version and file
   digest) checked fresh before each attempt; the set of supported versions is empty until a separately authorized qualification fills it (P1). A GCM that is not enabled, not configured noninteractive, not
   identifiable or not in the supported set is a **configuration or eligibility defect known before launch** and refuses before launch.
3. The credential lifecycle belongs to the helper: GCM may read, store, erase and refresh credentials, select an account, and contact its own endpoints. **This ADR does not promise zero helper side effects** and does
   not claim that GCM works under the cleared environment and generated configuration. Installed-version support, noninteractive behavior, environment needs and side effects are proof obligations (P1 to P5).
   **A helper error, or an interaction the helper turns out to require, that is discovered only after the push was launched is a post-launch observation** (decision 8), never a pre-launch refusal.
4. **Base and ancestry for the first profile.** The remote default branch is the base: its freshly admitted tip must **equal the recorded single parent** of the approved commit (a commit with other than one parent
   refuses). A missing or empty default ref refuses, and an owned branch equal to the default branch refuses. No unpublished ancestor is published and nothing is fetched; the push sends only objects the remote lacks
   relative to the advertised default tip. This narrows the owner's "parent confirmed as the tip of an existing remote branch" to the default branch and grants no wider ancestry.

### 3. Create-only transport

1. The transport plan is exactly one `git push` of one **full 40-character approved SHA** to one **exact, fully qualified owned `refs/heads/<branch>`** at the fixed destination `https://github.com/<owner>/<repository>.git`
   (host fixed, owner and repository from the ADR-0040 target under its name policy), guarded by the **exact explicit empty expectation** `--force-with-lease=refs/heads/<branch>:`. Candidate argv, with each element's status:

   | Element | Status |
   |---|---|
   | `-c core.hooksPath=<proven-empty hooks directory>` | LOCAL P2 H1 |
   | `-c credential.helper=` then `-c credential.helper=<the pinned GCM>` | the reset is LOCAL P3 A4; the pinned entry is a proof obligation (P3, quoting of an absolute path with spaces) |
   | `-c http.followRedirects=false` | LOCAL P3 R2 (on a double) |
   | `-c protocol.allow=never -c protocol.https.allow=always` | DOC only; unprobed (P8) |
   | `--git-dir=<per-attempt transport repository>` | the form is LOCAL P1 F; the location is decision 5 |
   | `push --porcelain --no-follow-tags --no-recurse-submodules --no-signed` | `--porcelain` and `--no-follow-tags` LOCAL; the other two DOC only (P8) |
   | `--force-with-lease=refs/heads/<branch>:` | LOCAL P4, P4b on a double; GitHub's behavior UNVERIFIED (P6) |
   | `https://github.com/<owner>/<repository>.git` | never probed against the real host |
   | `<40-hex SHA>:refs/heads/<branch>` | LOCAL P2 H4 (an unqualified destination is refused; an abbreviated source is accepted, so only 40-hex is ever passed) |

   Not part of the plan and refused if present anywhere in it: a plain or implicit lease, a nonempty expectation, a leading-plus refspec, `--force`, `--mirror`, `--all`, `--tags`, `--follow-tags`, `--delete`, a
   recursive submodule mode other than none, a push option, a signed push, a receive-pack override, a second refspec, a second URL, a named remote and an unpinned environment variable. The complete environment
   is a candidate set (a replaced empty home, Git's own directories on `PATH`, `GIT_CONFIG_NOSYSTEM=1`, the generated global file, `GIT_TERMINAL_PROMPT=0`, the GCM noninteractive setting); the complete list is
   settled by the transport implementation contract (B4) after P2 and P4.
2. **Semantics.** Before launch the executor observes the owned ref, **after every selected execution-eligibility gate of decision 4 has passed, including the base check of decision 2.4**. **Absent** proceeds.
   **Existing equal SHA** is reported as `AlreadyPresent`, an observation, **without a send** (no process is launched). **Existing different SHA** refuses. There is no gate bypass for `AlreadyPresent`: an equal
   ref in a repository whose default tip no longer equals the recorded parent refuses like any other attempt. If the ref appears between the observation and the push, the empty expectation makes Git refuse before
   sending or the server reject the create (LOCAL on a double); neither is promoted to a success.
3. **Narrow non-overwrite interpretation and ADR-0005.** Git documents the flag as one that can override the fast-forward restriction when its expectation matches. With the **empty** expectation the match means the
   ref does not exist, so there is nothing to overwrite, replace or rewrite; on the double an equal ref was reported up to date and an ancestor or unrelated ref was refused. This ADR records that reading
   **only** for this one form. It is **not** a general reversal of ADR-0005's prohibition of force-push and history rewriting, which stands for every other form, every overwrite, every deletion and every
   default-branch push. The flag's name is the only reason this needs recording. Whether GitHub's server enforces the same semantics is UNVERIFIED (P6), so this ADR is not present permission to execute the flag, and
   the compare-and-swap observed on a double is not assumed of GitHub. A compare-and-swap does not make repeated creates harmless in general: a ref deleted between two creates could let a second create succeed again
   (INFERENCE, a reasoned interleaving not an experiment). That residual is not removed by the flag; it is limited only by the single claim (decision 6) and stays a retained unknown.

### 4. Fresh gates and the identity limitation

*Every gate below is a selected execution-eligibility gate. All of them apply to every path, including the `AlreadyPresent` path of decision 3.2.*

1. **Local gates, fresh immediately before the claim and again before launch:** the owned local branch and its checked-out `HEAD` each equal the approved commit; fresh physical ownership proof of the **source**
   workspace (registered worktree, administrative directory, marker, physical identity, lease) and readiness (ADR-0008, ADR-0029 expected-HEAD chain); a coherent whole-value scope and approval; the local commit
   operation `Completed`; the commit object present with exactly one parent equal to the base (decision 2.4).
2. **Remote gates, fresh and bracketing the effect:** the repository identity (opaque node ID) equal to the binding and the reported names equal to the recorded names ignoring only ASCII case; the **default branch
   ref exists** and its tip equals the recorded parent; the **owned ref** state (absent, equal or different); the owned branch is not the default branch. The channel used for each read is an implementation choice (B5);
   any two channels used must agree or the attempt refuses, each channel's limits are those of the evidence note (a read can succeed anonymously and proves neither the push's principal nor write permission;
   "not found" is never absence), and an unrecognized, partial or unauthenticated answer refuses. Redirects are refused (`http.followRedirects=false`); a redirect refusal comes from a launched process and is a
   post-launch observation, not a pre-launch refusal.
3. **Repository permission** observations (ADR-0049) are, at most, conservative input screening: a reported insufficient or absent role may refuse; a reported role never admits. They prove neither Git's principal nor
   branch-write permission. **A server rejection remains possible after every gate passes**, and a protected-branch, ruleset or hook refusal is an observation (decision 8).
4. **Bracketing.** The identity and ref observations are repeated after the process has ended (or been resolved) and recorded as observations. They detect some changes after the fact; they cannot gate an effect that
   already happened and they cannot undo it.
5. **Identity limitation, stated plainly.** Git is addressed by name. No source read documents an ID-addressed Git transport or an identity echoed by the server. Therefore this contract does **not** claim atomic
   identity binding, prevention of every wrong effect or a measured probability (see "Residual-risk disclosure").

### 5. Isolation, execution location and the publication fence

1. **Per-attempt transport scratch.** Each attempt uses a newly created, host-owned bare repository under a host-owned root that is outside the active checkout and outside every `GitWorkspace`, with a unique
   attempt identity and an ownership marker, whose own configuration contains nothing the host did not write. Configuration is isolated from the source repository, the user's global and system configuration
   and every include: a host-generated global file, `GIT_CONFIG_NOSYSTEM=1` and `GIT_CONFIG_GLOBAL`, with no `include` or `includeIf` and no ambient `GIT_*` variable.
2. **Read-only path to the source objects, and the source-write-denial requirement (P9, UNPROVEN).** The scratch reaches the approved commit's objects through a path to the source repository's **common object
   store**, which the host must have **physically proven** to be that store (the cross-validated Git common directory of ADR-0008, the proven worktree registration, and a stable physical identity of the object
   directory), and the requirement is that the push and its descendants are **unable to write** the active checkout or the source object store. **Nothing in this contract establishes that denial.** An alternates entry makes an
   object resolve; it is not an ownership proof and not an access restriction. A Windows job governs process lifetime and accounting; it is not a filesystem or security restriction (DOC; decision 7). **No mechanism
   (an access-control, token, sandbox or other boundary) is selected or designed here, and the requirement is not weakened.** Detection after the fact (an observed difference in the source store or the active checkout)
   can show that a write occurred but cannot deny it; it does not meet the requirement as stated. Until a mechanism is separately designed, reviewed and proven, the requirement is unmet and the attempt refuses (a
   feasibility question returned to the planner, see "Proof obligations").
3. **Narrow proposed extension of ADR-0008 (execution location).** ADR-0008 requires every repository-mutating feature, pushes included, to hold the lease and "operate only inside a `Ready` `GitWorkspace`". This ADR
   proposes one additive extension, for publication only: a host-owned transport scratch **outside** the workspace is an admitted execution location **provided that** (a) the source workspace's physical ownership,
   lease and readiness proofs are retained and fresh, (b) the scratch has its own ownership marker, bounded lifetime and cleanup proof, (c) it is never inside the active checkout, a workspace or the source object store,
   and (d) it reads the source objects only through the proven read-only path of decision 5.2. The extension changes no lease, marker, reconciliation or active-checkout guarantee by itself, grants no other
   execution location, and takes effect only if this ADR is accepted.
4. **The publication fence (selected conceptual mechanism; not a schema or an implementation).** Publication exclusion is an **independent durable publication fence keyed by the physical repository identity**
   (ADR-0008), **acquired atomically with the claim** (decision 6.2). **Every mutating admission consults it**, including workspace preparation, local commit, Agent and verification work, and the operations,
   approvals and publications of other runs. A held fence refuses or queues them (ADR-0006: queue with a visible reason, never race). **Lease ownership proof and publication exclusion are separate facts: an `Active`
   lease alone is not sufficient publication exclusion.** Existing lease supersession and workspace reconciliation may **record lost ownership trust**, but they **cannot remove or bypass the fence and cannot permit
   a new mutator**. The fence is released only on the proofs of decision 8. **Unknown and remote-rejected attempts retain the fence** under this contract. **A conclusive human resolution or rearm contract is
   required before runtime activation**; no generic reset, cleanup or unrecorded escape is authorized, designed or implied here. The fence is a separate implementation contract (B3).

### 6. Durable dispatch

1. **The request records an explicit intent.** After admission (fresh gates, whole-scope reread) one short write-locked transaction records the intent for the approved operation, with the identity of the **live host
   session** that recorded it and one journal event, and the request returns the recorded state. The request launches nothing, claims nothing and holds no fence. An intent alone permits no send and no process.
2. **A supervisor may claim that explicit intent ONCE and perform the initial launch in the same live host session.** The claim is one short write-locked transaction that rereads the complete scope and approval,
   **acquires the publication fence atomically** (decision 5.4) and records the single-use claim. **The claim is the durable, conservative "may have started" boundary**: once it commits, the attempt is treated as
   possibly launched whatever happens next. The initial launch follows, in the same live session, outside any transaction. A separate third launch marker is **optional** phase detail and not required; if one is
   added, a marker committed before launch proves "may have started" only and never proves a launch happened. If the fence cannot be acquired no claim is recorded and nothing is launched.
3. **What the supervisor may not do.** It cannot invent an intent, reclaim, relaunch, resend, or automatically arm an intent recorded by another host session or an old intent after a restart. After a restart it may
   only perform the read-only resolution of decision 8 for an already claimed attempt.
4. **Replay and conflict.** An **identical** request (same request identity and the same complete values) **always returns the recorded state** (intent, claim or outcome) **without triggering any execution**, in
   every state, including after a claim. A **different request identity or different values** for the same approved operation **conflicts** (a fixed refusal). Neither a replay nor a conflict arms, claims or
   launches anything.
5. **Pre-claim restart leaves the intent unarmed.** An intent whose recording host session is gone (a restart, or any other host session) is **dormant**: it holds no fence, nothing is sent, and **neither a replay nor a
   supervisor arms it**. A later **human rearm or resolution path is a separately reviewed activation prerequisite**; no API or operation for it is designed or permitted here.
6. **No database transaction spans external work**: no process, network or helper call runs inside a transaction (ADR-0003). No implicit publication after restart, no automatic resend, no consent reset. **A claim
   without a recorded result stays uncertain** (`NeedsAttention`/`MayHaveStarted`).

### 7. Process lifetime, bounds and evidence retention

1. **Containment before any external mutation.** The push child (and its descendants) must be contained in a Windows job configured to terminate them when the host's last handle closes, **before** the push can send
   anything. The contract requires that no descendant can run outside the job (the assignment race of a process that has already started is a proof obligation, P10) and that breakaway is not permitted. The existing
   executor provides none of this (CODE), so the process-containment change is part of the implementation contract B3. **Containment is a process-lifetime boundary only; it is not a filesystem or security boundary (P9).**
2. **Conclusive local termination** is required before disposing any resource the child could still use (the transport scratch, its alternates entry and the captures): the host must observe, through the job, that no
   process remains (for example zero active processes through job accounting or a completion message) and that its own handles are released. **A process identifier, a deadline, a post-kill wait or an exit of the direct
   child alone is insufficient.** Whether a restarted host can rely on kill-on-close after host loss, without having observed termination, is unresolved (P10); until resolved, a claim found after host loss stays
   `NeedsAttention` with the fence retained.
3. **Local termination never proves a remote non-effect.**
4. **Candidate bounds for later executable validation** (not measured transfer capacity, not requirements until validated): remote reads 15 seconds; send deadline 120 seconds; captured output 32 KiB per stream and
   64 KiB combined. A bound that is too low produces more `Unclassified` outcomes; one that is too high delays resolution.
5. **Bounded evidence retention.** Retained evidence is a closed, bounded, admitted record: fixed outcome and observation codes, the full SHAs, the ref name, host-generated times, the exit status, byte counts and, if
   needed, a digest of the capture. **No raw credential, helper, provider or Git output** is placed in a journal entry, a persisted field or an ordinary response; captured text is untrusted, grammar-admitted and
   redacted before any admitted line is kept, and an unrecognized line is counted, never stored. Where and how long the bounded evidence is stored is boundary B2.

### 8. Outcomes, recovery and releases

Vocabulary (kinds never merged):

- **`NotLaunched`** (proven): **durable evidence specifically proving that no push process was started.** Two kinds can qualify: (a) a refusal durably recorded **before any launch call**, after which the launch is
  unreachable; (b) a start failure for which the executor itself yields durable evidence that **process creation did not happen**. **A generic exception reported by an executor is insufficient**: it may occur after
  a process exists. The current executor cannot distinguish the two (CODE); until B3 supplies such evidence, only (a) classifies, and every other start failure is `MayHaveStarted`. `NotLaunched` makes **no claim**
  about earlier read-only calls or about helper effects.
- **`AlreadyPresent`** (observation): the owned ref equals the approved SHA at the pre-launch observation (no process launched, recorded before any launch call), or a launched process reported `=` for the exact ref
  (a post-launch observation of equality with unknown causality). Never "DevalCopilot pushed it".
- **Observed acknowledgement** (`*` for exactly the approved SHA and ref, exit 0, complete admitted grammar, and a ref read showing the SHA): admitted server evidence, not exactly-once and not proof of who else acted.
- **Observed rejection**: `[rejected]` for the exact ref (Git did not try to send it, DOC) or `[remote rejected]` (the server refused). A **final rejection followed by an absent or different ref cannot exclude an earlier
  applied-and-moved effect during a transport-internal resend** (LOCAL P4b U3: two receive-pack POSTs from one client invocation, cause not established), nor a repeated effect after a deletion between creates.
- **Post-launch observations that are never pre-launch refusals:** a helper error or required interaction, a redirect refusal, a `To` line different from the plan, an unrecognized, truncated or oversized output, a
  `remote failure`, a non-zero exit without a recognized status, a timeout, a cancellation, a kill, a lost response, an extra POST, a host stop.
- **`NeedsAttention` / `MayHaveStarted`**: the attempt after a claim with no conclusive classification. This is the default; a later read never moves it to success or to "failed".

Rules: **no exactly-once claim**; **ref equality never proves causality**; **read-only reconciliation and append-only observations never relaunch and never erase uncertainty**; absence is never `NotLaunched`.

**Separate proofs for separate releases (none implies another):**

The fence-release classes below require a complete, coherent classification of this exact authorized invocation, conclusive local termination where a push process started, and valid final bound-identity/ref checks. An unresolved earlier timeout, lost response, truncated or contradictory output, identity mismatch or other `NeedsAttention` condition takes precedence: a later `=`, rejection, acknowledgement or ref equality does not erase it. For a launched `=` or client-side `[rejected]`, the supported Git contract must establish the no-update semantics for the whole invocation; recognizing a status string alone is insufficient. Until the local and real-compatibility proof obligations establish that interpretation, these classes do not authorize releasing a fence. They never prove who published an equal SHA.

| Release | Required proof | Not sufficient |
|---|---|---|
| **Transport resources** (scratch, alternates entry, captures) | Conclusive local termination (decision 7.2); not needed when `NotLaunched` proves no process was started | A deadline, a PID, an observation of the remote |
| **The publication fence** | Either `NotLaunched` (durable evidence that no push process started; this includes an `AlreadyPresent` recorded before any launch call), **or** conclusive local termination **together with** one of: an observed acknowledgement with the ref read showing the SHA; a post-launch `=` with the ref read showing the SHA (equality, no causality claimed); a complete admitted client-side `[rejected]` for the exact ref (one authorized invocation, Git did not try to send it; subject to P7) | `[remote rejected]`, any other post-launch observation, an absent ref, an unknown outcome, an elapsed time, a restart, a lease change |
| **A remote conclusion** (what the remote holds) | Only append-only observations with their time and channel | Anything about causality or about the whole attempt |

**Unresolved cases retain the fence.** A `[remote rejected]`, any `NeedsAttention`, any claim whose termination is not conclusively proven and any unknown outcome retain the fence. **A conclusive human resolution or rearm
contract is a prerequisite to runtime activation and is not defined here (K5)**; no generic reset, cleanup or escape exists. Until a later decision defines it, the repository stays fenced.

## Changed-ADR map

No existing ADR is marked superseded by this proposal, and no accepted ADR is edited by it.

| ADR | Relation | Note |
|---|---|---|
| [0003](0003-use-a-durable-sqlite-event-journal.md) | Applied unchanged | Durable intent, short transactions, no external work inside one, startup reconciliation, notification as a hint |
| [0005](0005-use-git-worktrees-and-a-policy-controlled-github-loop.md) | **Clarified, not superseded** | The empty-expectation form is recorded as non-overwrite for this one form (decision 3.3). Force-push, history rewriting, branch deletion, default-branch push and merge remain outside the MVP. "Existing Git authentication" is narrowed to the GCM profile. "Remote lookup before retry" is implemented as read-only reconciliation and no retry |
| [0006](0006-support-bounded-concurrent-runs-across-projects.md) | **Applied; admission extended (proposed)** | "At most one mutating run per canonical repository" is realized for publication by the publication fence, which every mutating admission consults (decision 5.4) |
| [0008](0008-physical-repository-identity-and-tool-owned-worktree-ownership.md) | **Two proposed narrow extensions** | (1) **Execution location**: a host-owned transport scratch outside the workspace for publication only (decision 5.3). (2) **Reconciliation and admission compatibility**: existing reconciliation may still record lost ownership trust and supersede a lease, but admissions (workspace preparation, local commit, Agent, verification, other runs, approvals and publications) consult the independent fence, and supersession cannot remove or bypass it or permit a new mutator. No lease, marker or active-checkout guarantee is otherwise changed |
| [0029](0029-deliver-an-explicit-local-commit-before-closing-provider-contract-gaps.md) | Unchanged | Its runner stays network-disabled and is not reused as the remote executor; `Committing` is a precedent for a reservation. Its admission seams are among those that must consult the fence under the ADR-0006/0008 extension |
| [0040](0040-record-an-immutable-github-observation-scope-for-a-completed-local-delivery.md), [0041](0041-observe-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md), [0042](0042-observe-the-recorded-github-branch-tip-of-a-recorded-observation-target-on-explicit-request.md), [0043](0043-bind-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md) | Unchanged | Inputs and read-only observers. None observes the default branch (the schema field exists but no observer queries it), so a default-branch read is a new read (B5) |
| [0048](0048-approve-one-future-github-branch-push-of-a-bound-recorded-local-commit-on-explicit-request.md) | Unchanged | The consent is the precondition; its row stays immutable; consumption is a separate fact |
| [0049](0049-observe-the-github-repository-permission-of-a-bound-recorded-target-on-explicit-request.md) | Unchanged | Screening input only |

On acceptance the engineering context, the architecture documents and the ADR-0006/0008 text would be updated as separate, reviewed changes. This slice changes none of them.

## Transition and invariant table

*Conceptual. "Durable facts" names what must be recorded, not a schema. A "forbidden inference" applies to all later rows. "Supervisor" means the supervisor of the host session that recorded the intent.*

| # | Event or boundary | Durable facts after | Required evidence | Forbidden inferences | Late result owner | Replay or restart behavior |
|---|---|---|---|---|---|---|
| X1 | Execution request, no intent for the approved operation | None yet | The request's complete values equal the whole fresh scope and approval reading; every gate of decision 4 passes | Consent means readiness, permission or a current remote state | None | Admission, then X7 |
| X2 | Admission refused (before intent) | None | A fixed reason from the closed refusal set | A refusal is evidence about the remote | None | No intent exists; nothing can be sent later |
| X3 | **Identical request**, in any state (no claim, intent, claim, outcome) | None new | Same request identity and the same complete values | A replay is a new authority; a replay arms, claims or launches | The recorded intent, claim or outcome | **Returns the recorded state and never triggers execution**, including after a claim (I11) |
| X4 | **Different request identity or values** for the same approved operation, in any state | None | A mismatch with the recorded request | A different request is a retry, a rearm or a resend | The recorded intent or claim | Fixed conflict; no process, no claim |
| X5 | Competing identical requests (one host) | One intent | The write lock and a unique key per approved operation | Both may proceed | The winner's intent | The loser is X3 |
| X6 | A mutating admission from another run or approval, workspace preparation, a local commit, Agent or verification work, or another publication, for the same physical repository | None | The publication fence is held (decision 5.4) | The second may use the repository; an `Active` lease or its supersession lets it in | The fence holder | Refused or queued (ADR-0006) |
| X7 | Intent recorded | Intent with the recording host session | Fresh gates and whole-scope equality under the write lock | The intent is a send, a claim, a fence or proof the gates still hold | The intent | Only the supervisor of that live session may claim it (X10) |
| X8 | Crash before the intent commits | None | Nothing | Anything | None | The request is simply absent; a new request is a new admission |
| X9 | Restart, or any new host session, with an unclaimed intent | The intent, unchanged and now dormant | The recorded session differs from the live one | That a replay, a supervisor or time arms it; that anything was sent | The intent | **Unarmed.** It holds no fence and nothing is sent. A replay (X3) returns it and does not arm it. A later human rearm or resolution path is a separately reviewed activation prerequisite and is not defined here |
| X10 | Supervisor claim and fence acquisition (one short write-locked transaction) | The claim (may have started) and the fence for the physical repository | Complete scope and approval reread under the write lock; the intent was recorded by this live session and is unclaimed; no other fence for the physical repository; every gate fresh | The claim means a push occurred; a second claim is possible | The claim | Atomic: both or neither. If the fence is held, no claim is recorded and nothing is launched |
| X11 | Crash during the claim transaction | Either nothing or both | The database's atomic commit | A partial claim | The claim | Nothing recorded means X7 and X9 apply; recorded means X15 |
| X12 | Gate refusal after the claim and before the launch call (stale observation, identity, default tip, owned ref different, GCM identity or configuration defect, containment, scratch or read-only path not established, a plan outside the closed set) | `NotLaunched` with the fixed reason, **committed before any launch call** | The recorded refusal | That the remote is unchanged; any conclusion about earlier reads or helper effects; any conclusion if the record did not commit | The claim | If the record did not commit (crash) the attempt is `MayHaveStarted` (X15) |
| X13 | `AlreadyPresent` before launch (the owned ref equals the SHA after every gate passed) | `AlreadyPresent`, recorded before any launch call | A fresh read and every selected gate | That DevalCopilot pushed it | The observation | No process; the fence is released (decision 8) |
| X14 | Initial launch, by the supervisor in the same live session | Nothing beyond the claim | The executor started, or reported a start failure | A reported start failure proves no process started | The attempt | A start failure is `NotLaunched` only with evidence that process creation did not happen (decision 8); otherwise `MayHaveStarted` |
| X15 | Crash after the claim, before any recorded result | The claim and the fence | Nothing | That the process never started or never finished | The attempt (possibly an orphan) | Startup recovery: `NeedsAttention`, read-only reconciliation, fence retained, no relaunch, no reclaim |
| X16 | Process running | The claim | One contained child tree, one deadline, bounded captures | A quiet process is dead | The attempt | Nothing else may start; the fence stays |
| X17 | A helper error or required interaction, a redirect refusal, a `To` line different from the plan, or an unrecognized, truncated or oversized output, observed after launch | `NeedsAttention` with the observation | The bounded admitted evidence | That nothing was sent; that it was a pre-launch refusal | The attempt | Observation only; fence retained |
| X18 | `[rejected]` for the exact ref in complete admitted grammar | Observed rejection (client side) | Complete grammar and exit status (P7) | That the rest of the attempt was clean if the grammar is incomplete | The attempt | No retry; the fence is released only with termination proof (decision 8) |
| X19 | `=` observed from the process | `AlreadyPresent` (post-launch equality, unknown causality) | Complete grammar and a ref read | A successful push by this host | The attempt | The fence is released only with termination proof and the ref read (decision 8) |
| X20 | Acknowledgement `*` for the exact ref | Observed acknowledgement | Complete admitted grammar, exit 0, a ref read showing the SHA | Exactly-once; nobody else pushed this SHA | The attempt | The claim is spent; the fence is released with termination proof |
| X21 | `[remote rejected]` | Observed rejection (server side) plus a ref read | Complete grammar and a read | That nothing was applied: a transport resend may have applied an earlier request | The attempt | `NeedsAttention`; **fence retained** |
| X22 | Timeout, cancellation, host shutdown, kill | `NeedsAttention` | The executor outcome or recorded stop; no exit code for a killed process (CODE) | The remote is unchanged because the client died (LOCAL P4b U4: read absent at 1.0 s and 2.3 s, present at 4.5 s; P4b U5: present immediately) | The attempt | Reconciliation appends observations; absence is never `NotLaunched`; fence retained |
| X23 | Lost response, **a repeated wire request** (transport-internal resend, an extra POST from one invocation) | `NeedsAttention` | A non-zero exit without a recognized status, or two requests observed | The push failed; the second answer describes the first; a repeated create is harmless (a deletion between creates could let it succeed again) | The attempt | Observation only; never a host resend; fence retained |
| X24 | A late child output, a late remote effect, or a deletion and re-creation of the ref after the attempt was classified | Appended observation | The read and its time and channel | A late read rewrites history, changes consent or concludes causality | None (observation) | Never relaunches; never erases uncertainty |
| X25 | Crash after the result is recorded, before the notification | The result | The recorded result | The notification failure invalidates the result | The attempt | The notification is a hint (ADR-0003) |
| X26 | Restart recovery of a **claimed** attempt | As recorded | Fresh identity and ref reads (observations), the containment evidence available after host loss | Any observation proves causality or principal; the supervisor may reclaim or relaunch | The attempt | `NeedsAttention` unless a conclusive classification is already recorded; fence retained; no relaunch; no implicit publication; consent not reset. Publication recovery completes (and the fence is consulted) before any dispatch, workspace reconciliation or admission, see "Proof obligations" |
| X27 | **Lease supersession** or workspace reconciliation (lost ownership trust) while a fence is held | The lease and workspace state change as ADR-0008 records; the fence unchanged | The fence is a separate durable fact keyed by physical identity | An `Active` lease, or its supersession, ends or bypasses exclusion; a new mutator may start | The fence holder | Supersession may record lost trust but cannot remove the fence or admit a mutator; admissions still refuse (X6) |
| X28 | Post-attempt bracket observation (identity, refs) | Appended observation | A fresh read | A changed identity can be undone; an unchanged one proves the effect went to the bound repository | None | Mismatch is attention, never a retry |
| X29 | Release of transport resources | Release recorded | Conclusive local termination (decision 7.2), or `NotLaunched` | An elapsed deadline, a PID or an observation of the remote | The attempt | Retained otherwise |
| X30 | Release of the fence | Release recorded | The proofs of the decision 8 table | A remote rejection, absence, an unknown outcome, time, restart or a lease change | The attempt | Retained otherwise; no generic reset, cleanup or escape; a human resolution or rearm contract is required before activation (K5) |
| X31 | Any later request for the approved operation after a claim | None | The consumed claim | Consent can be reset or reused | None | Identical: X3. Different: X4. No new claim is possible. An unrelated approved operation, even with the same SHA, is unaffected |
| X32 | Competing hosts, or a second host session, on the same database | The recorded state | The database's write lock and the fence | A second host may claim, arm or launch | The recorded attempt | Same rules as X3, X9, X10, X15, X26; separate databases for the same physical repository are not covered (K9) |

Invariants:

- **I1** One approved operation permits at most one claim; a claim is never reused, reset or replayed into a process. Duplicate prevention is scoped to the approved operation, not to a SHA.
- **I2** No process, network or helper call runs inside a database transaction.
- **I3** The request records intent only. Only the supervisor of the host session that recorded the intent may claim it, once; only a committed claim, with the fence and containment in place, permits the launch call, and the initial launch happens in that live session.
- **I4** After the claim the attempt is `MayHaveStarted` until a conclusive classification is recorded under decision 8; absence, equality alone, a final remote rejection or an elapsed time never conclude. The named client-side no-update classes require the complete-invocation proofs and precedence rules of decision 8, not merely a status string.
- **I5** The host dispatches at most once: no relaunch, resend or reclaim after a failure, timeout, restart, supervisor tick or replay; no old or foreign-session intent is armed automatically.
- **I6** Reconciliation reads are read-only, append-only and never send.
- **I7** The publication fence for the physical repository is acquired atomically with the claim and held until its release proof; lease supersession, reconciliation or any other path cannot remove or bypass it; an `Active` lease alone is not exclusion.
- **I8** Resources are disposed only after conclusive local termination (or proven `NotLaunched`); local termination proves nothing about the remote.
- **I9** The only credential source is the pinned GCM; no credential value is read, copied, logged or retained by the host.
- **I10** The host performs **one authorized invocation of one closed create-only plan and no second host dispatch.** Transport-internal resend and repeated remote effects (for example after a deletion between creates) remain possible; **no exactly-once guarantee** is made.
- **I11** A replay of an identical request returns the recorded state and never triggers execution, in every state.
- **I12** `NotLaunched` is recorded only on durable evidence specifically proving that no push process started; a generic executor exception never qualifies.
- **I13** Unknown and remote-rejected attempts retain the fence. A conclusive human resolution or rearm contract is required before runtime activation; none is defined or authorized here.

## Static walkthrough (consistency check of this draft; nothing was executed)

| Scenario | Governing transitions | Governing invariants | Result under this draft |
|---|---|---|---|
| Initial dispatch | X1, X7, X10, X14, X16, then X20 or X21 or X22 | I3, I5, I7, I10 | The request records the intent (X7); the same live session's supervisor claims once with the fence (X10) and launches (X14); the outcome is an observation |
| Identical replay | X3 (X31 after a claim) | I11, I1 | Returns the recorded state in every state; never triggers execution; never arms |
| Competing request | X4 (different values), X5 (concurrent identical), X6 (other operation or run), X32 (another host) | I1, I7 | A different request conflicts; concurrent identical requests resolve to one intent; another mutator is refused by the fence; the same SHA in another approved scope is unaffected |
| Pre-claim restart | X9 (X8 if before the intent commits) | I3, I5 | The intent is dormant and unarmed; replay returns it and does not arm it; the human rearm path is a separately reviewed activation prerequisite |
| Post-claim restart | X15, X26 | I4, I5, I6, I7 | `NeedsAttention`; read-only observations; no reclaim or relaunch; the fence stays |
| Post-launch helper error | X17 | I4, I12 | A post-launch observation, never a pre-launch refusal; `MayHaveStarted`; fence retained |
| Repeated wire request | X23 | I4, I10 | Observation only; no host resend; the guarantee is not exactly-once |
| Lease supersession | X27, X6 | I7 | The lease may record lost trust; the fence stays and admissions still refuse |
| Refusal before launch | X12, X13 | I12 | `NotLaunched` or `AlreadyPresent` recorded before any launch call; the fence is released |

## Compatibility limits and closed refusals

Compatibility is limited to: Windows; an installed, pinned and explicitly enabled GCM in noninteractive mode; HTTPS to `github.com`; a repository whose recorded commit has one parent equal to the remote default tip; no
proxy or private CA requirement; no redirect; a repository identity equal to the binding.

**Refusals before a launch** are **configuration or eligibility defects known before launch**; they record `NotLaunched` (decision 8, only when the durable evidence exists) and send nothing: not Windows; GCM absent, not
enabled, not configured noninteractive, not identifiable or not in the supported set; the GCM or Git identity changed since admission; any destination other than `https://github.com/<owner>/<repository>.git`; a known
proxy, private CA or SSH requirement; an identity or names different from the binding; a missing or empty default ref; a default tip different from the parent; a parent count other than one; the owned branch equal to
the default branch; the owned ref different from the SHA; an incoherent, orphaned, drifted or unproven scope, consent, ownership, lease or readiness; the local owned branch or `HEAD` different from the approved
commit; containment, scratch or the read-only object path not proven (P9); the fence unavailable; any channel disagreement; a plan outside the closed set of decision 3.1. These apply to the `AlreadyPresent` path as well.

**Detections after a launch** (never refusals; the attempt is `NeedsAttention` unless a conclusive classification is admitted): a helper error or required interaction, a redirect refusal, a `To` line different from the
plan, an unrecognized, truncated or oversized output, a `remote failure`, a non-zero exit without a recognized status, a timeout, a cancellation, a kill, a lost response, an extra POST.

## Proof obligations and retained unknowns

Obligations are grouped by the cheapest valid boundary that can prove them. **A local proof does not require a real-provider invocation**, and a real qualification does not replace a local proof.

### Boundary L: owned local implementation tests (real Git, local bare repositories, owned doubles, real file-backed SQLite, a real Windows job; no real provider, helper or credential)

- **L1** The closed plan and environment (every pin of decision 3.1 and its effect with real Git against local repositories and an owned HTTP double; refusal of anything outside the plan; no ambient variable); includes **P8**.
- **L2** Claim, fence and dispatch behavior: the atomic claim and fence, replay and conflict, the dormant intent after a host-session change, competing requests, no second dispatch, the consumed claim scoped to the approved operation.
- **L3** The fence against lease supersession and every mutating admission (workspace preparation, local commit, Agent, verification, other runs, approvals and publications). **Startup ordering:** the fence is consulted from the first admission after startup; publication recovery of claimed attempts (read-only, never relaunching) completes before any supervisor dispatch, workspace reconciliation or new admission and cannot be preempted by cleanup of transport scratch; its order relative to local-commit recovery is proven. **Competing hosts:** two host sessions on one database serialize at the claim and the fence; a second host starting while a claimed attempt or an orphan child may exist is held by the fence. Hosts on separate databases for one physical repository are not covered by a database fence (K9).
- **L4** Process lifetime (**P10**): job containment before launch, no descendant outside the job including a process that started before association, kill-on-close, the accounting evidence used for termination, host-loss simulation, and the executor evidence that distinguishes "process creation did not happen" for `NotLaunched` (decision 8).
- **L5** Output grammar and classification over retained and synthetic fixtures (including the extra-POST and truncated cases), bounded evidence retention and the absence of any secret in the database, events, logs and captured text.
- **L6** Transport scratch lifecycle and cleanup after conclusive termination; the read-only object path's identification (decision 5.2, the identification half of **P9**).
- **L7** The candidate bounds' behavior under test conditions (**P13**, locally only; adequacy for a real transfer belongs to Q).

### Boundary Q: separately authorized real qualification (actual GCM and GitHub; needs its own explicit decision, is separate from the Agent ledger and is unstarted)

- **Q1 (P1)** The installed GCM version and file identity and the supported set (empty until qualified). **Q2 (P2)** Noninteractive operation under the cleared environment and generated configuration. **Q3 (P3)** The absolute-path `credential.helper` entry on Windows and GCM's reading of its settings through the isolated configuration. **Q4 (P4)** What profile or environment variables GCM needs to reach its store. **Q5 (P5)** The helper-owned lifecycle (`get`, `store`, `erase`, refresh), any contact with other endpoints and the account selected.
- **Q6 (P6)** GitHub's behavior for a create with the empty expectation (a ref appearing after the advertisement; existing equal, ancestor and unrelated refs; protected or rule-governed names). **Q7 (P7)** GitHub's rejection forms, the `remote:` text, the exact porcelain grammar and the locale behavior on the real transport. **Q8 (P11)** Whether a transport-internal resend occurs against GitHub and what the second request answers. **Q9 (P12)** The default-branch and owned-ref observation channels against GitHub: agreement, anonymous-success and redirect behavior. **Q10 (P13)** Adequacy of the candidate bounds for a realistic push.

### P9: source-write denial (UNPROVEN, not weakened, no mechanism selected)

The requirement that the push and its descendants cannot write the active checkout or the source object store is **visibly unproven**. Alternates resolves an object; a Windows job governs process lifetime and
accounting; neither is an access restriction (DOC; decisions 5.2 and 7.1). No access-control, token, sandbox or other boundary is designed or selected here. **Feasibility question for planner discussion:** whether
the requirement as stated can be met at all within the accepted constraints (host-owned scratch, no new dependency, no ambient configuration) by any mechanism that is not designed in this slice. Detection after the
fact (a before-and-after observation of the source store and checkout) proves at most that a write happened; it does not deny one. If only detection is available, the requirement as phrased cannot be met and the
attempt refuses.

**Retained unknowns:** **K1** GitHub's server semantics and rate behavior; **K2** that `gh` and the helper share a principal (unrelated unless shown); **K3** an ID-addressed Git transport (none documented);
**K4** what the installed Git does that the latest manual does not describe; **K5** the conclusive human resolution or rearm contract for dormant intents, `NeedsAttention` and observed server rejection, and how it
releases the fence; **K6** whether the fence can be released after host loss without a human; **K7** the supported GCM version set; **K8** the mechanism for P9; **K9** exclusion across separate databases or
machines for one physical repository.

## Residual-risk disclosure

The owner has accepted, **for contract preparation only and without a numerical measurement**, the following risk. Git addresses a repository by name. A fresh identity read, a fixed destination
(`github.com`), refused redirects and a later observation **do not atomically prevent** the name from addressing a different repository at the moment of the push, for example after a rename followed by the original
name being reused (documented to stop the redirect) or after a transfer, and **do not undo** a wrong effect. A wrong effect would be one create of the approved branch name with the approved commit in a repository
the owner did not intend; the identity observation after the attempt can detect some such cases and cannot reverse them.

This ADR does not claim atomic identity binding, prevention of every wrong effect or any probability. The acceptance does not authorize runtime activation, credential access, a real Git, GitHub or helper call,
installation, login, commit or push, does not infer runtime risk acceptance and does not accept this ADR. Further risks that remain, each disclosed above: helper-owned credential effects; transport-internal resend
and repeated effects after a deletion between creates; a server rejection after all gates; a process that cannot be proven terminated after host loss; unproven source-write denial (P9); and the consequence of
retaining the fence for unresolved attempts.

## Validation matrix and implementation boundaries

### Proportional future validation (for the separately bounded implementation contracts; none is run by this slice)

| Risk | Cheapest proving boundary | Broaden when |
|---|---|---|
| Plan and environment closure, refusals (L1) | A pure plan builder and the adapter against an owned process double; one test per closed refusal; a sentinel secret never in any captured value | The executor, the Git capability snapshot or shared environment code changes |
| Real Git behavior of each pin (L1) | Infrastructure integration against real Git and local bare repositories plus an owned HTTP double (the retained probe scenarios) | The Git version or the helper arrangement changes |
| Observation and output classification (L5) | Pure parser tests over retained porcelain fixtures including the extra-POST and truncated cases | Any new status, locale or channel |
| Request, replay, conflict, claim, session arming, scope drift (L2) | Application handler tests on file-backed SQLite plus one route test | Shared transaction, mediator or admission behavior changes |
| Fence across runs, approvals and lease supersession; startup ordering; competing hosts (L3) | Application tests on file-backed SQLite with two runs and approvals for one physical repository, a lease supersession while a claim is unresolved, a second host composition on the same database and a restart | Lease, reservation, workspace-status or startup-ordering code changes |
| Crash boundaries X8 to X15, X25, X26 | Deterministic fault injection at each durable boundary with an owned runner fake and a second host composition on the same database | The state or event model changes |
| Reconciliation and release proofs | A pure policy theory over (state x observation x termination evidence) | A new observation source |
| Containment, termination and `NotLaunched` evidence (L4) | An executor integration test with a contained child tree that spawns descendants, kill-on-close, a host-loss simulation and a start failure before and after process creation | The executor changes |
| Scratch lifecycle and the object path (L6); P9 | A real-filesystem test of the scratch and its identification. **P9 has no proving boundary until a mechanism exists** | The scratch layout changes |
| No secret leakage (L5) | A sentinel credential through a helper double; assert absence in the database, events, logs and captured text | The helper arrangement changes |
| Real compatibility (Q1 to Q10) | **Not provable by a double.** A separately authorized qualification run against a disposable repository, separate from the Agent ledger | Never automatic |
| Mutation (bounded, serial) | Skip the claim; launch before the claim; arm an old intent on replay; let a replay launch; treat a generic start exception as `NotLaunched`; treat a timeout as `NotLaunched` or as failure; treat equality as success; drop one pin; release the fence on a deadline, on lease supersession or on `[remote rejected]`; dispose resources on a PID exit; trust the approval without the reread | |

### Implementation boundaries (each separately bounded and separately reviewed; none is designed here)

- **B1** The execution request and response contract (API, wire, validation, errors).
- **B2** Persistence: the intent with its host-session identity, the claim, consumption, the opt-in, observations and bounded evidence; schema, migration and journal events.
- **B3** The publication fence and its consultation by every mutating admission, the ADR-0006/0008 admission and reconciliation compatibility extension, the ADR-0008 execution-location extension, and the process-containment change to the executor (job, termination evidence, process identity, creation evidence).
- **B4** The transport adapter: the Git and GCM profile, the transport scratch, the plan builder and the output grammar.
- **B5** The remote observation channels, including the default-branch read.
- **B6** Dispatch, supervisor behavior, recovery and reconciliation, including startup ordering.
- **B7** The cockpit surface.
- **B8** Git and GitHub compatibility qualification (separate, unstarted, needing its own explicit decision and not part of the Agent qualification ledger).
- **B9** The human rearm and resolution contract (K5), a prerequisite to runtime activation.

## Consequences

- A first approved publication has a closed, reviewable contract that confines the host's action to one authorized invocation of one create-only plan under one credential profile, with no ambient configuration and no automatic retry.
- Unresolved attempts retain the fence; until a human-resolution contract exists (K5, B9), a `NeedsAttention` attempt blocks the repository, and a dormant intent stays dormant.
- The profile is narrow: it serves only repositories whose recorded parent equals the default tip and a Windows host with a qualified GCM. Other cases refuse.
- Acceptance would require the ADR-0006/0008 extensions, a publication fence, a process-containment change, a source-write-denial mechanism (P9), a human-resolution contract and a separately authorized qualification
  run; none exists. This ADR does not complete Increment 6, does not accept a risk beyond the owner's design-only acceptance and does not change any existing contract.
- The Api 2067/2068 failure and every other recorded limitation stand.

## Alternatives considered

- **A plain explicit-SHA push.** Rejected for this profile: it can fast-forward a branch another actor created before Git's advertisement (LOCAL P4 U2). It remains the comparison point in the evidence note.
- **Ambient or system configuration, or another helper.** Rejected: contradicts "no ambient import" and cannot be audited without reading credential configuration.
- **Pushing from the owned workspace's Git directory.** Not selected: it requires a complete inventory of repository-local keys and leaves a configuration-change window; the isolated scratch needs the ADR-0008 extension instead.
- **A wider ancestry or a fetch.** Not selected: it would publish unpublished history; the owner's profile is a confirmed remote parent.
- **A mandatory third launch marker.** Not selected: the claim is already the conservative boundary.
- **An `Active` lease as the only exclusion.** Not selected: reconciliation can supersede a lease on lost ownership trust, which would end exclusivity while a remote effect is unresolved; the independent fence is selected.
- **Letting a replay or a restarted supervisor rearm an old intent.** Not selected: it would be an implicit publication after restart.
