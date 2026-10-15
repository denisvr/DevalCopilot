# GitHub publication source-write denial (ADR-0050 P9): feasibility evidence

> **Status: evidence for a pending planner decision. This is not an accepted contract, not an ADR and not an approval to
> implement.** It selects no mechanism, designs no broker, alternate user, service or credential export, and weakens neither
> the P9 requirement nor any other part of [ADR-0050](../decisions/0050-define-the-first-approved-github-branch-publication-execution-contract.md),
> which remains Proposed. Every "candidate" below is only one of the two the assignment named. Mechanism selection,
> architecture and any owner escalation belong to Codex. No probe, test program, helper, credential, token, ACL or
> AppContainer-profile operation was run or changed, and no real `gh`, GCM or provider call was made.

- Repository baseline: `main` at `58c188f48e6894d4d2d1084336c268d86f51dc8b` (verified equal to local and live `origin/main`).
- Evidence and documentation access date: 2026-10-15. Delivery handoff: [current work](../roadmap/current-work.md).
- Related published evidence (unchanged): [execution-evidence note](github-branch-publication-execution-evidence.md).
- Revision: correction round 1 (Codex review items R1 to R3): mechanism facts are separated from compatibility assumptions, the
  two privileges are classified separately, and the proof claim is bounded. No source was added; the twelve retained pages are
  unchanged.

## 1. Question and bounded conclusion

P9 in ADR-0050 (decisions 5.2 and 7.1, "Proof obligations") requires that the push child and its descendants be **unable to
write** the user's active checkout or the source object store, while they read the source objects (alternates) and write only a
host-owned per-attempt scratch. Two candidates were compared, nothing more: **A**, a `WRITE_RESTRICTED` token with host-owned
writable scratch; **B**, AppContainer or Less-Privileged AppContainer (LPAC) with explicit resource grants.

This is a summary of the evidence, not a recommendation and not a selection.

1. **The sources read do not establish an implementation that meets P9 for either candidate.** The twelve pages describe the
   mechanisms (two access checks; a package-SID principal) and their stated goals, and leave the unknowns listed in section 7
   open. That is an inconclusive result, not a finding for or against either candidate. Documentation can support an argument
   about a *specified* mechanism, and owned local adversarial tests (section 9.1, proposed and not run) could support such a
   specification together with its documented access-control model. Finite passing tests would not prove that all writes or
   escapes are impossible, and no mechanism is specified here.
2. **A (`WRITE_RESTRICTED`).** Documented: restricting SIDs are "considered only when evaluating write access", and access needs
   both checks to pass (S01, S02). Not documented: which rights count as "write" (for example `DELETE`, `WRITE_DAC`,
   `WRITE_OWNER`, `PROCESS_DUP_HANDLE`, `PROCESS_CREATE_PROCESS`, token duplication) and how a missing DACL behaves in the
   second check. The sources establish nothing about successful credential retrieval, decryption, Credential Manager access or
   GCM compatibility under this token; those stay unknown. Microsoft states that a restricted-token application "should" run on a
   non-default desktop to prevent `SendMessage`/`PostMessage` attacks on unrestricted applications (S01, S02). No owner-repository
   ACL change was identified for A; scratch grants are host-owned.
3. **B (AppContainer/LPAC).** Documented: a dual-principal model in which an AppContainer must be explicitly granted access and
   the permitted access is the intersection of the user's and the package's (S11), with credential, file, process and window
   isolation stated as goals (S04). Two items are **unresolved and conditionally stop its dependent design (section 5)**. First,
   *if* the package principal lacks effective access to the source object store, reading it through alternates would need a grant
   on the owner's repository (an ACL change outside scratch); no repository ACL or capability state was measured here, so whether
   the condition holds is unknown. Second, S04 states a credential-isolation goal; the sources do not establish which credential
   paths the installed GCM has under a container, so neither feasibility nor impossibility is inferred. B's dependent design stays
   conservatively paused for planner review; no workaround is designed or evaluated.
4. **Both** need a new native process-creation path (CODE: the executor uses `System.Diagnostics.Process`; no token,
   attribute-list or handle-list code exists in `src`), and **neither** is shown to be compatible with an already-installed,
   explicitly enabled, noninteractive GCM. Windows jobs, alternates, restricted command arguments and post-run comparisons
   remain non-denial mechanisms and were not re-litigated. ADR-0050 still has no selected, proven mechanism and P9 stays unproven.

## 2. Evidence classes

| Class | Meaning |
|---|---|
| **DOC** | A statement on a Microsoft Learn page listed in section 8 (S01 to S12), read as tag-stripped page text on 2026-10-15. The pages describe the current documentation, not the Windows build of the host (Windows 11 build 10.0.26200 was not tested against any claim). |
| **CODE** | Anchored to a file and lines at the baseline. |
| **INFERENCE** | Reasoning from DOC or CODE, not stated by a page and not observed. |
| **UNKNOWN** | Not established by any source read. Fails closed in any design that depends on it. |
| **CONTRADICTION** | Two statements in the sources read that cannot both hold as written. |

There is no LOCAL class here: nothing was executed.

## 3. Minimal boundary diagram

```text
 Owner's source repository (must NOT be writable by the push)      Host-owned per-attempt scratch (writable)
 +----------------------------------------------+                 +----------------------------------+
 | active checkout, linked worktrees            |                 | bare transport repository        |
 | .git common object store  ---- read path ----+-- alternates -->| scratch HOME, TEMP, GCM state    |
 +----------------------------------------------+                 +----------------------------------+
        ^ P9: no write path for the child tree                              ^ the only intended write grant
        |                                                                   |
 host process (full user token, owns handles to the DB, logs, source locks)
   |  creates (new native path, not the current Process.Start)
   v
 push child tree:  git.exe -> git-remote-https -> GCM helper -> any descendant
   token: A = WRITE_RESTRICTED (user SIDs kept) | B = AppContainer/LPAC (package SID, user SIDs intersected)
   escape surfaces to cover: inherited writable handles, parent-process and token access, impersonation,
                             default interactive desktop messages, handles duplicated from the host
 Windows job: process lifetime and accounting only (P10); not a filesystem or security boundary.
```

## 4. Candidate comparison

Each cell carries its class. "Grant" means an access-control change; **scratch grants** are on host-owned, per-attempt paths,
**owner-repository changes** are on the user's source repository, and the two are never merged.

| Topic | A. `WRITE_RESTRICTED` token + host-owned scratch | B. AppContainer / LPAC + explicit grants |
|---|---|---|
| Process and token boundary | New primary token from `CreateRestrictedToken` with restricting SIDs and `WRITE_RESTRICTED`, used by `CreateProcessAsUser`. **Caller privileges (two different privileges):** a restricted version of the caller's own primary token needs no `SE_ASSIGNPRIMARYTOKEN_NAME` (DOC S01, S02, S06); S06 separately says the caller "Typically" must have `SE_INCREASE_QUOTA_NAME` and "may require" `SE_ASSIGNPRIMARYTOKEN_NAME` if the token is not assignable. Whether the host holds the quota privilege is UNKNOWN (not measured). **Privileges the child token retains** are a separate matter: `CreateRestrictedToken` can delete privileges or disable all but `SeChangeNotifyPrivilege` (DOC S01); which the child keeps is not decided here. `CreateProcess` from an impersonating thread always inherits the process's primary token (DOC S07), which supports descendants keeping the token (INFERENCE for the non-impersonating case) | Process created with `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES`; LPAC adds `PROC_THREAD_ATTRIBUTE_ALL_APPLICATION_PACKAGES_POLICY` with the opt-out value (DOC S11 example; S05 has no text on that attribute); the container attribute itself is DOC S05, S11. A child created by an AppContainer process "typically inherits the parent's token" (DOC S12) |
| Filesystem write boundary | Two checks; write access must pass both, so a DACL that names no restricting SID denies write (DOC S01, S02, S03). The restricting check is not applied to non-write access (DOC S01: restricting SIDs "considered only when evaluating write access"). What counts as "write" and how an object without a DACL behaves in the restricting check: UNKNOWN (S03 says an object without a DACL grants access, without saying if that applies to the restricting check) | Access needs both user and package principals in the DACL; an AppContainer "must be explicitly granted access" and gets the intersection of the two (DOC S11). S04 says read-write "can be granted to specific persistent files", and "Read-only access is less restricted" without defining the default for a user-profile directory (AMBIGUOUS, section 7) |
| Creation, deletion, rename, ACL and ownership changes | UNKNOWN which of the rights behind each operation are "write" for the restricting check (S01, S02 do not enumerate them). A source DACL that names none of the restricting SIDs would deny any right it is evaluated for (INFERENCE from S03) | Anything not granted to the package principal is denied (DOC S11; goal statement in S04). A read-only grant on the source store would not itself grant create, delete, rename or ACL rights (INFERENCE; ACE inheritance flags and virtualization are not described, UNKNOWN) |
| Credential boundary | The documented behavior is only the restricting-SID rule (DOC S01, S02). Credential retrieval, decryption, Credential Manager access, the effect of `store` and `erase`, and GCM compatibility are not established by any source read (UNKNOWN); no credential boundary is claimed or denied | S04 states a goal: AppContainer "prevents the use of user credentials", with credentials "unique to each user/application pairing" so the application "cannot impersonate the user" (DOC). How that bears on an installed GCM, and which credential paths it has under a container, is UNKNOWN; neither reachability nor unreachability is inferred |
| Writable inherited handles and descendants | `bInheritHandles` TRUE passes every inheritable handle, and inherited handles keep their value and access rights (DOC S06); `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` restricts that list (DOC S05, S06). A token cannot narrow an already granted handle (INFERENCE: access checks at open time are not stated on the pages). Whether the executor's handles are inheritable is UNKNOWN (`Process` internals not read; CODE shows no handle list). Descendants inherit the token (see first row) | Same handle rule (DOC S06, INFERENCE). Descendants keep the container (DOC S12) |
| Escape: parent process, tokens, impersonation | `PROC_THREAD_ATTRIBUTE_PARENT_PROCESS` lets a creator use another process as parent, inheriting its handles and token, if it holds `PROCESS_CREATE_PROCESS` on it (DOC S05, S08). A holder of `PROCESS_DUP_HANDLE` can reach maximum access via a pseudo-handle (DOC S08 warning). Whether the write-restricted check blocks opening the host or its token with those or `TOKEN_DUPLICATE` rights is UNKNOWN; impersonation levels are described (DOC S07) but not which tokens a restricted process may impersonate (UNKNOWN) | The process DACL and token DACL of the host do not name the package SID unless granted (INFERENCE from S11); process isolation is a stated goal (DOC S04). Not enumerated (UNKNOWN) |
| Escape: interactive desktop | Documented risk: attack by `SendMessage`/`PostMessage` to unrestricted applications on the default desktop; run on another desktop (DOC S01, S02). A separate desktop object and its DACL are a setup requirement (DOC S09; S06 describes `lpDesktop`). **CONTRADICTION** in S06 on which desktop a `CreateProcessAsUser` child gets by default (section 7) | Window isolation stated as a goal (DOC S04); not detailed (UNKNOWN). Low integrity level always applies in a container (DOC S11, S12) |
| Privileges and setup | No profile or persistent object. Per attempt: token creation, a scratch DACL (grant the restricting SID write), likely a desktop, and the unresolved caller-privilege question (`SE_INCREASE_QUOTA_NAME`). Owner-repository change: none identified (the restricting check is not applied to non-write access, DOC S01; no repository ACL was measured) | `CreateAppContainerProfile` creates a per-user, per-app profile (folders and registry storage) and may return `E_ACCESSDENIED`; `DeleteAppContainerProfile` is its counterpart; an unpackaged app must do this itself (DOC S10, S12). That is **persistent per-user state**. Scratch grants (the package SID on the scratch directory) are host-owned. **Conditional owner-repository ACL change:** a grant on the source store is needed only if the package principal lacks effective access to it (DOC S11 describes the explicit-grant model; no repository ACL or capability state was measured, so the condition is UNKNOWN). LPAC also needs explicit grants for resources a regular container gets for free, such as the registry and COM (DOC S11), plausibly the Git installation and certificate stores (UNKNOWN, system-level) |
| Descendant compatibility (Git, Schannel, GCM) | UNKNOWN, no source read | UNKNOWN; network needs a capability such as `internetClient` (DOC S11 example). Whether Git for Windows runs under LPAC at all is not documented |
| GCM `get`, `store`, `erase`, noninteractive | UNKNOWN. No source read states how Credential Manager, DPAPI-protected data or GCM state and log writes behave under the restricting check | UNKNOWN. The credential-isolation goal (S04) is not evidence of a block or of a path |
| Source-repository ACL change required? | Not identified | **Conditional on missing effective access (unmeasured); the dependent design stays paused** |

## 5. Conditions that pause a dependent design for review

The assignment says to identify, not design around, any need for a broker, an alternate user or service, a credential export
or a source-repository ACL change. Nothing below is established; each item is a condition that, if it held, would put the
dependent design in excluded territory, and B's design is therefore kept paused. No feasibility or impossibility is inferred and
no workaround is designed.

1. **B: a grant on the owner's repository, conditional on missing effective access.** S11 states that an AppContainer must be
   explicitly granted access and that the permitted access is the intersection of the user's and the package's principals; S04
   is ambiguous about read-only access. *If* the package principal has no effective access to the source object store (for
   example no ACE usable by the package SID on its location), the read path of ADR-0050 decision 5.2 would need an ACL change on
   the owner's repository, which [ADR-0008](../decisions/0008-physical-repository-identity-and-tool-owned-worktree-ownership.md)
   does not list among the additive changes workspace preparation may make in the active checkout. No repository ACL or
   capability state was measured, so whether the condition holds is unknown.
2. **B: the credential path is not established.** S04 states a credential-isolation goal. The sources read do not establish
   which credential paths the already-installed GCM has under a container, so they support neither reachability nor
   unreachability. Any arrangement beyond the installed GCM (a broker, an alternate user or service, a credential export) is
   excluded by the assignment and was not evaluated.
3. **B (LPAC): grants outside scratch and outside the repository** may be needed for resources a regular container reaches by
   default (S11), possibly system-level; which ones, if any, the Git and GCM tool set needs is unknown.
4. **A: no pausing condition was identified**, but its open items (the set of "write" rights, the NULL-DACL behavior of the
   second check, access to the host's process and token, the desktop, the inherited handles) are the cases that would decide
   whether A can meet P9. They are listed as proposed proof cases (section 9), not answered here.

## 6. Claim, source and limitation matrix

| # | Claim | Class | Source | Limitation |
|---|---|---|---|---|
| C1 | A restricted token can have deny-only SIDs, deleted privileges and restricting SIDs; two access checks must both allow | DOC | S01, S02 | Describes the algorithm, not any operation's outcome on the host |
| C2 | `WRITE_RESTRICTED` makes restricting SIDs "considered only when evaluating write access" | DOC | S01 | The set of rights treated as write is not given. Establishes nothing about credential retrieval, decryption, Credential Manager access or GCM compatibility |
| C3 | A restricted version of the caller's own primary token needs no `SE_ASSIGNPRIMARYTOKEN_NAME` | DOC | S01, S02, S06 | A different privilege from `SE_INCREASE_QUOTA_NAME`, which S06 says the caller "Typically" must have (UNKNOWN whether the host holds it); privileges retained by the child token are a separate choice (S01 describes deleting them) |
| C4 | Restricted-token applications should run on a non-default desktop to prevent message attacks | DOC | S01, S02 | Conditional on the restricted process having windows or message access; the exact exposure of a console child is not stated |
| C5 | A missing DACL grants access; otherwise ACEs are walked in order | DOC | S03 | Silent on the restricting-SID check |
| C6 | AppContainer access needs both principals; explicit grants required; intersection of access | DOC | S11 | Does not state the default DACL of a user-profile directory |
| C7 | AppContainer prevents use of user credentials and impersonation of the user | DOC | S04 | A goal statement; not a statement about GCM, DPAPI or Credential Manager, and no reachability or unreachability follows from it |
| C8 | LPAC opts out of all application packages and needs explicit capabilities for the registry, COM and others | DOC | S11 | Which file resources need grants is not listed |
| C9 | An AppContainer child typically inherits the parent's token | DOC | S12 | "Typically"; no breakaway exception is described |
| C10 | `CreateAppContainerProfile` creates a per-user, per-app persistent profile, can fail with `E_ACCESSDENIED` | DOC | S10, S12 | Failure conditions beyond the listed code unknown |
| C11 | Inheritable handles are inherited when `bInheritHandles` is TRUE and keep their rights; a handle list narrows the set | DOC | S05, S06 | Whether a held handle survives a token difference is INFERENCE |
| C12 | `PROC_THREAD_ATTRIBUTE_PARENT_PROCESS` inherits the token and handles of the named process; needs `PROCESS_CREATE_PROCESS` | DOC | S05, S08 | Whether either candidate denies that right to the host is UNKNOWN |
| C13 | Rights such as `PROCESS_DUP_HANDLE` can be escalated to others via pseudo-handles | DOC | S08 | Warning only |
| C14 | Impersonation levels exist; `CreateProcess` from an impersonating thread inherits the primary token | DOC | S07 | Which tokens a restricted process may open or impersonate is UNKNOWN |
| C15 | A desktop has its own DACL and access rights | DOC | S09 | The exposure of a console child on the default desktop is UNKNOWN |
| C16 | The executor creates children with `System.Diagnostics.Process` and a cleared environment; its approved-root check is a lexical path check | CODE | `ChildProcessExecutionAdapter.cs` lines 24-59, 243-279; `ProcessExecutionRequest.cs` lines 41-60 | The check does not restrict what the child writes |
| C17 | `src` has no job object, token, AppContainer, handle-list, security-descriptor or `CreateProcess*` code | CODE | Two text searches of `src/**/*.cs` (`logs/executor/p9-feasibility/code-search-20261015.txt`); only `kernel32` handle and path helpers use `DllImport` | A text search of the baseline; framework internals such as how `Process` marks handles were not read |
| C18 | Tree kill is `Process.Kill(entireProcessTree: true)` with a best-effort 5 s wait | CODE | `ProcessTreeTermination.cs` line 18; adapter lines 94-105 | P10, not P9 |
| C19 | The ownership proof cross-validates the Git common directory of a linked workspace, which is the main repository's store (the user's active checkout) | CODE and INFERENCE | [ADR-0008](../decisions/0008-physical-repository-identity-and-tool-owned-worktree-ownership.md), `LocalCommitGit.ProveOwnershipAsync` lines 95-120; the layout inference is in section 3.3 of the [execution-evidence note](github-branch-publication-execution-evidence.md) | Common-directory validation is a point-in-time read; no worktree layout page was read here |

## 7. Contradictions, ambiguities and unknowns

**Contradictions and ambiguities in the sources read**

- **S06, desktop default (CONTRADICTION).** One passage says that a null `lpDesktop` "inherits the desktop and window station of
  its parent process". A remark says that by default `CreateProcessAsUser` creates the process on a noninteractive window
  station with a desktop that is not visible. The page does not reconcile them; either would matter for the escape-surface
  analysis of A.
- **Caller privileges (not a contradiction; classified in correction round 1).** `SE_INCREASE_QUOTA_NAME` and
  `SE_ASSIGNPRIMARYTOKEN_NAME` are different privileges. S01, S02 and S06 say a restricted version of the caller's own primary
  token needs no `SE_ASSIGNPRIMARYTOKEN_NAME`; that exception does not speak to `SE_INCREASE_QUOTA_NAME`, which S06 says the
  caller "Typically" must have (a qualified statement, preserved as such). Whether the host holds the quota privilege is not
  stated on any page and was not measured. Privileges the *child* token retains are a separate matter from the caller's.
- **S11/S04 versus S12, deny versus redirect (AMBIGUOUS).** S11 and S04 describe denial of anything not granted; S12 describes
  file system and registry **virtualization** (redirected writes to a private data folder) for packaged apps. Which applies to
  an unpackaged process created with a security-capabilities attribute is not stated, so a write to a source path might be
  denied or redirected. Either would leave the source unwritten, but the child's observed behavior would differ.
- **S04, read access (AMBIGUOUS).** "Read-only access is less restricted" against S11's "must be explicitly granted access".

**Unknowns (not resolved by the pages; each is a proof case or an owner/planner question)**

1. Which access rights the restricting check treats as write (`DELETE`, `WRITE_DAC`, `WRITE_OWNER`, `FILE_DELETE_CHILD`,
   `PROCESS_DUP_HANDLE`, `PROCESS_CREATE_PROCESS`, `TOKEN_DUPLICATE`), and how an owner's implicit rights behave.
2. The restricting-check behavior for an object without a DACL, for a permissive DACL (a world-writable directory), and for
   restricting SIDs that appear in many DACLs.
3. Whether the executor's current handles (database, logs, index-lock receipts) are inheritable (CODE shows no handle list).
4. Whether either token can open, duplicate or impersonate the host's token or open the host process.
5. Windows 11 build behavior for any of the above (not tested; no page names a build).
6. Git for Windows, Schannel and certificate-store behavior under either token (not documented in the pages read).
7. GCM behavior (get, store, erase, noninteractive error, state and trace writes, helper environment) under either token. GCM
   documentation was read in the [published addendum](github-branch-publication-execution-evidence.md) (section 8) only as DOC
   for the unrestricted case and was **not re-read here** (the page cap was used by the Windows pages), so it is not a source
   for this note.
8. Whether alternates reads need any write access to the source (lock files, pack indexes) in the installed Git (K4 of ADR-0050).
9. Whether the host's token holds `SE_INCREASE_QUOTA_NAME`, and which privileges a child token should retain (two separate
   questions; neither measured).
10. The effective access and capability state of a package principal on the source object store, and on the Git installation
    for LPAC (no repository, Program Files or capability ACL was inspected).
11. The S06 default-desktop contradiction above stays unresolved; no experiment or alternate launch design is requested.

## 8. Source-reading provenance

Twelve Microsoft Learn pages were read directly (the assignment's maximum). Each was fetched once with `fetch` through a local
reading aid (retained as `fetchtext-reading-aid.js`, see below) that keeps the page's `<main>` element and strips tags;
the first 40 lines of each retained file are navigation and are not content. Nothing was executed from any page. One further URL
(`secauthz/lpac`) was probed for existence and returned HTTP 404; it was not read and is not counted. Before reading, a status
check of the 13 URLs produced no content.

| Id | Page (`https://learn.microsoft.com/en-us/windows/win32/` + path) | "Last updated" on page | SHA-256 of the retained text (first 16 hex) | Read |
|---|---|---|---|---|
| S01 | `api/securitybaseapi/nf-securitybaseapi-createrestrictedtoken` | 2021-10-13 | `1d540f9b4aa3bca0` | Whole |
| S02 | `secauthz/restricted-tokens` | 2021-01-07 | `6d839a79b8f9723b` | Whole |
| S03 | `secauthz/how-dacls-control-access-to-an-object` | 2021-01-07 | `cb66990bf45150f1` | Whole |
| S04 | `secauthz/appcontainer-isolation` | 2025-07-08 | `8c092c1a80d8e809` | Whole |
| S05 | `api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute` | 2022-11-01 | `9f4025751702eced` | Attribute descriptions for `HANDLE_LIST`, `PARENT_PROCESS`, `SECURITY_CAPABILITIES`; a keyword search of the rest (no `ALL_APPLICATION_PACKAGES` text on this page) |
| S06 | `api/processthreadsapi/nf-processthreadsapi-createprocessasuserw` | 2023-02-09 | `9f4256728def00fc` | Description, `hToken`, `bInheritHandles`, `lpDesktop`, the remarks on desktop, handle inheritance and token; not every remark |
| S07 | `secauthz/impersonation-levels` | 2021-01-07 | `de00d129f6afcd52` | Whole |
| S08 | `procthread/process-security-and-access-rights` | 2025-07-14 | `e42e9532558bf749` | Whole |
| S09 | `winstation/desktop-security-and-access-rights` | 2020-08-19 | `a1fc52adcbb969d6` | Whole |
| S10 | `api/userenv/nf-userenv-createappcontainerprofile` | 2024-11-27 | `915ead9cddaee375` | Whole |
| S11 | `secauthz/implementing-an-appcontainer` | 2025-09-03 | `ac95696a83bc94c2` | Whole (code samples included) |
| S12 | `secauthz/appcontainer-for-legacy-applications-` | 2023-07-25 | `f73f46afabd22edc` | Whole |

Retained, ignored by Git, under `logs/executor/p9-feasibility/`: `sources/S01` to `S12`
(tag-stripped text with the URL, HTTP status, UTC time and HTML size in each header), `sources.sha256` (full digests),
`head-status-20261015.txt` (the existence check, including the 404), `fetch-20261015.log`, `fetchtext-reading-aid.js` and
`code-search-20261015.txt`.
Limits: the tag-stripped text can lose table cell boundaries and code formatting; the pages report no Windows build; a
"Last updated" date is the page's, not a version of the API.

## 9. Proposed future proof cases (not run; each needs its own bounded decision)

### 9.1 Owned local proof (no real GCM, `gh`, provider, credential or network)

**Proposed and unexecuted.** PL1 to PL7 describe tests that a later, separately bounded and reviewed slice could run against
a *specified* mechanism; none was run, and no test program or probe exists. Owned adversarial tests of that kind could support
the specification together with its documented access-control model. A finite set of passing tests would not prove that all
writes or escapes are impossible: a failure would refute the specification, a pass would be bounded evidence only.

Boundary: a Windows host test with synthetic fixtures in throwaway directories (a fixture "source repository" with a read-only
object store, a fixture "active checkout", a host-owned scratch), an owned test host process, and real local Git and a local
bare repository. Never the owner's real repository. Each case needs a positive control (the same operation succeeds inside the
granted scratch and as the unrestricted user) so a denial is not an artifact of a broken launch.

| Id | Case | Applies to | What a denial must show |
|---|---|---|---|
| PL1 | The child tree attempts create, overwrite, append, truncate, delete, rename, hard link, junction, alternate data stream, delete-on-close and DACL and owner change on the fixture source and checkout, directly and from a grandchild | A, B | Every write form refused (or, for B, shown redirected away from the fixture, see S12) |
| PL2 | The restricting-check behavior for permissive DACLs, an object with no DACL and an owner's implicit rights | A | The unknowns 1 and 2 of section 7 |
| PL3 | An inheritable writable handle opened by the host to a fixture file and the effect of `bInheritHandles` with and without a handle list | A, B | That an inherited writable handle is, or is not, a write path, and what the handle list removes |
| PL4 | The child opens the host process (each right of S08), the host token, duplicates a host handle, impersonates, and creates a child with `PARENT_PROCESS` | A, B | Which of them succeed; unknown 4 |
| PL5 | The child posts and sends window messages to a host-owned window on the same and on a separate desktop | A | The S01/S02 risk and the desktop requirement |
| PL6 | Real Git pushes to a local bare repository with an alternates path to the fixture source, under each token, with a descendant | A, B | The push succeeds, the fixture source is unwritten, and a deliberately injected write is refused (assertions on denial, not a post-run comparison standing in for one) |
| PL7 | The scratch grant and cleanup lifecycle, and (B) profile creation, deletion and failure of `CreateAppContainerProfile` | A, B | Persistent state added and removed, and what a failure leaves |

These cases follow the ADR-0050 Boundary L pattern (owned local tests with no real provider). ADR-0050 still has no selected,
proven mechanism for P9; this note does not propose amending its P9 row, and any such change is Codex's decision.

### 9.2 Real Git and GCM compatibility qualification (separate, unstarted)

Not part of 9.1 and not authorized by this note: GCM `get`, `store` and `erase` under the chosen token, noninteractive error
behavior, helper state and trace writes, environment needs, the account selected, and GitHub's behavior. It is Boundary Q of
ADR-0050, needs its own explicit decision, a disposable repository and the real installed GCM and Git, and stays separate from
the Agent qualification ledger (`Blocked`/`TargetsNotReal`, both allowances unused).

## 10. What this does not establish, and a challenge to the assignment

- It does not show that either candidate meets, or fails, P9. It does not select, design or recommend a mechanism.
- It does not cover job objects (P10), alternates semantics, the installed Git's behavior, Windows build differences, GCM
  behavior or any real provider behavior.
- **Result.** Inconclusive, and a valid result: the twelve pages read do not establish an implementation that meets P9 for
  either candidate. Which decisive facts the pages leave open (the write-right mapping, handle and process access, redirect
  versus deny, effective access of a package principal, GCM credential paths) are listed in section 7. Whether more
  documentation, a specified mechanism with owned adversarial tests (section 9.1), or neither is the next step is for Codex.
- **Challenge to the assignment (for Codex, no disagreement pending).** None is raised against the correction round: its
  readings of the privilege separation, the unproven compatibility assumptions and the proof claim are consistent with the
  pages, and the earlier "probable" wording and the exhaustive credential statement were not supported by the sources read.
- Conditional observation for the planner, not a design: if a source grant were ever needed (section 5, item 1), it would
  arise from the alternates read path of decision 5.2. This note does not redesign that path.
- Agent qualification stays `Blocked`/`TargetsNotReal`, both allowances unused; Git/GitHub qualification is separate and
  unstarted. The historical concurrent-replay 409, target-test 500, disposal `NullReferenceException` and the unnamed Api
  2067/2068 failure remain separately unexplained.

## 11. Experimental results: candidate A local viability (owned harness; one bounded run)

> **Experimental evidence, not a production mechanism and not P9 acceptance.** This section was appended in a later slice.
> The status block and sections 1 to 10 describe the documentation-only round and are preserved unchanged; their statements
> that nothing was executed apply to that round. ADR-0050 remains Proposed, P9 remains unproven, B remains paused, and no
> configuration was changed to force a result.
>
> **Read first (correction round 1): the executed harness violated the dispatched stop gate.** It did not stop at the first
> source-mutation counterexample (the restricted deletion) and went on to further probes and a grandchild; this is a harness
> control-flow defect and a protocol deviation, described in section 11.7. Only the restricted child's read, scratch write and
> source create, truncate and delete results (through the first counterexample) are in-gate results; the restricted DACL,
> H5, H6 and grandchild observations were obtained after the required stop point, are reported as factual observations and are
> not acceptance evidence under the dispatched gate. The executed binary is frozen and not approved for reuse.

### 11.1 What was run

A dependency-free C harness (`p9probe-v1.c`, built with the installed MSVC 19.51 and Windows SDK 10.0.26100.0, static CRT, no
package download) on Windows `10.0.26200.9711`. It creates a new run root under ignored `logs/executor/p9-token-gate/`,
verifies canonical containment and the absence of reparse points from the gate directory down, then runs **one unrestricted
control child and one restricted child**, each of which spawns **one** grandchild. No command shell, no Git, GCM, helper,
network, credential, account, service or AppContainer profile; no elevation. The real repository's ACLs, the default desktop and
every other process were neither inspected nor modified. The harness's own processes, one private desktop, a private job per child launch and
newly created fixture paths were the only targets. Native signatures and constants were verified by compiling against the
installed SDK headers (`environment-and-header-verification-20261015.txt`) and against S01, S05 and S06.

| Item | Exact configuration (as run) |
|---|---|
| Token | The harness's own unelevated primary token (elevation type Limited, integrity `0x2000`, five privileges, none of them `SE_INCREASE_QUOTA_NAME` or `SE_ASSIGNPRIMARYTOKEN_NAME`); `CreateRestrictedToken` flags `DISABLE_MAX_PRIVILEGE` (0x1) plus `WRITE_RESTRICTED` (0x8); no disabled SIDs |
| Restricting SID | One fresh synthetic SID (`S-1-5-21-` plus three random sub-authorities and RID `0x7E570000`), created in process, no account |
| Effective restricted token (recorded by host and child) | `IsTokenRestricted` 1; 1 restricting SID; 1 privilege (`SeChangeNotifyPrivilege`); same 15 groups and integrity as the parent; `TokenAccessInformation.Flags` `0x2a18` against `0x2a00` for the parent (the difference `0x18` is consistent with the ntifs `TOKEN_IS_RESTRICTED` and `TOKEN_WRITE_RESTRICTED` bits; that mapping is not verified in the installed headers). Real user and logon SIDs are redacted in the logs |
| Launch | **Restricted child: `CreateProcessAsUserW`; unrestricted control child: `CreateProcessW`** (the private desktop, handle-list, flags, environment and job setup below were shared). `bInheritHandles` TRUE with `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` = [anonymous pipe write end, NUL stdin]; flags `EXTENDED_STARTUPINFO_PRESENT`, `CREATE_SUSPENDED`, `CREATE_UNICODE_ENVIRONMENT`, `DETACHED_PROCESS`; explicit private desktop; environment only `SystemRoot`, `TEMP`, `TMP` (scratch); working directory scratch; fixed executable path |
| Containment | Private unnamed job with kill-on-close and an active-process limit of 2, no breakaway; the suspended child was assigned before `ResumeThread` |
| Grants | Synthetic SID `FILE_ALL_ACCESS` (container and object inherit) on the **new scratch directory only**; the private desktop's DACL names the current user, the synthetic SID and SYSTEM. No ACE was added to any source fixture by the host |
| Fixtures | New files per label under `fixture/source/<label>/` inheriting the run root's ordinary-user access (**not** made unwritable), a granted `fixture/scratch/`, and two host-held writable fixture handles per child, explicitly noninheritable |
| Bounds | 60 s child, 20 s grandchild, 64 KiB captured output; none reached |

### 11.2 Controls and raw outcomes

The unrestricted control child used the same desktop, handle-list, job and environment setup as the restricted child, with the
ordinary token through `CreateProcessW` (the restricted child used `CreateProcessAsUserW`). The host validated that every
required control operation succeeded (`CONTROL-VALIDATION: control valid`) before the restricted launch. Raw Win32 codes: 0
success, 5 `ERROR_ACCESS_DENIED`, 6 `ERROR_INVALID_HANDLE`. The "Gate" column classifies each restricted result against the
dispatched stop gate (section 11.7): **in-gate** results precede or are the first source-mutation counterexample; **post-stop**
results were obtained after the point at which the run should have stopped. Raw output: `runs/run-20261015T191251Z-27828/out/` and
`matrix-run-20261015T191251Z.txt`.

| Family | Operation | Gate | Unrestricted control | Restricted |
|---|---|---|---|---|
| 1 and 2 | Launch, containment, privileges (child) | in-gate | created, assigned, resumed; exit 0 | created and run without elevation or the host holding `SE_INCREASE_QUOTA_NAME` or `SE_ASSIGNPRIMARYTOKEN_NAME`; assigned and resumed; exit 0 |
| 2 | R1: read the synthetic source file (child) | in-gate | succeeded | **succeeded**, content matched |
| 2 | W1: create, write and read back a granted scratch file (child) | in-gate | succeeded | **succeeded**, content matched |
| 3 | S1: create a new file in the source directory (child) | in-gate | succeeded | **denied (5)** |
| 3 | S2: open-truncate an existing source file (child) | in-gate | succeeded | **denied (5)** |
| 3 | S3: delete a named source file (child) | in-gate: **the first source-mutation counterexample** | succeeded | **SUCCEEDED (0)**, confirmed on disk |
| 3 | S4: read the file's DACL (child) | post-stop | succeeded | succeeded |
| 3 | S4: set the file's DACL, add one ACE (child) | post-stop | succeeded | denied (5) |
| 5 | H5: the host's noninheritable writable fixture handle value, probed in the child | post-stop | value invalid (6) | value invalid (6) |
| 6 | H6: `OpenProcess(PROCESS_DUP_HANDLE)` on this experiment's own host (child) | post-stop | succeeded; the known fixture handle was duplicated and written through | denied (5); no duplication was attempted |
| 4 | Grandchild: R1, W1, S1 to S4 | post-stop | as the control child | read and scratch write succeeded, create and truncate denied (5), **delete succeeded (0)**, DACL read succeeded, DACL set denied (5); raw `IsTokenRestricted` 1 and `TokenAccessInformation.Flags` `0x2a18` |

### 11.3 Observed fixture changes (host snapshots, SHA-256 per file, before and after each child)

- **Control:** `new.txt` created, `overwrite.txt` changed, `delete.txt` removed, `dacl.txt` gained one ACE (8 to 9 ACEs, marker SID
  present) and `h6.txt` changed through the duplicated handle, for the child and (apart from H6) the grandchild; `h5.txt` unchanged.
- **Restricted:** `read-me.txt`, `overwrite.txt`, `dacl.txt` (still 8 ACEs, no marker), `h5.txt` and `h6.txt` byte-identical;
  no `new.txt`; **`delete.txt` absent in both the child's and the grandchild's source directory (5 entries after, 6 before)**; scratch
  gained `res-scratch.txt` and `res-gc-scratch.txt`. The child directory's snapshot corroborates the in-gate deletion and shows
  the aftermath of the whole child tree: it was taken after the child and its grandchild had exited, so the unchanged
  `dacl.txt`, `h5.txt` and `h6.txt` reflect post-stop probes as well. The grandchild directory's snapshot corroborates only the
  post-stop grandchild deletion.
- Job accounting after each child: `TotalProcesses` 2 and `ActiveProcesses` 0, observed after exit, so termination of these two
  processes was observed (it says nothing about other runs or hosts).

### 11.4 Findings

1. **In-gate prerequisites met on this host and token:** an unelevated restricted-token launch (`CreateProcessAsUserW`) with the
   explicit handle list, private desktop and pre-resume job containment worked, and the restricted child **read the source and
   wrote granted scratch**. Separately, and not as a confinement result, the restricted child also **deleted a source file**
   (finding 2).
2. **Counterexample to the denial claim for deletion (in-gate, first source-mutation counterexample).** With this exact
   configuration the restricted child **deleted a named fixture file** (confirmed on disk), while creating a file and truncating a
   file were denied. By itself this means the configuration does not deny every write-class effect on a source it can otherwise
   read. Why `DeleteFileW` succeeded was **not investigated**; a plausible but **untested** hypothesis is that the delete right is
   not among the rights the write-restricted check treats as write (see unknown 1 of section 7). The unrestricted control proves
   only that the operation itself works. The configuration was not altered, and this result is not generalized to other
   restricted-token configurations.
3. **Post-stop observations (factual, not acceptance evidence under the dispatched gate):** the restricted child's DACL read
   succeeded and DACL set was denied; the noninheritable host handle value was invalid in the child (H5); `PROCESS_DUP_HANDLE` on
   the owned host was denied (H6); the restricted grandchild showed the same read, scratch-write, create, truncate, delete and DACL
   outcomes and reported `IsTokenRestricted` 1 with raw `TokenAccessInformation.Flags` `0x2a18`. The grandchild's token claim is
   bounded to those observed values together with the separately identified reasoning that a child created by a restricted
   process inherits the token (S07, S12; not independently verified here); full token equality between child and grandchild was
   not measured.
4. **Handle-list isolation (observed case only, post-stop):** a noninheritable host handle value was invalid in the child (6) in
   both the restricted child and the unrestricted control, and both were launched with the same handle list and the handle's
   inheritable flag cleared. That is consistent with the flag and the list excluding the handle but does not independently prove
   the list's causal effect: the positive case (an inheritable writable handle in the list) and the inheritable-but-omitted case
   were not run.
5. **Caller privilege (in-gate, this token and Windows build only):** the host token held neither
   `SE_INCREASE_QUOTA_NAME` nor `SE_ASSIGNPRIMARYTOKEN_NAME` and `CreateProcessAsUserW` with a restricted version of its own token
   succeeded (partial answer to unknown 9 of section 7).

### 11.5 Coverage limits and what the passing cases do not establish

- One run, one Windows build, one token, one synthetic restricting SID, files only. Not run: append, rename, hard link, junction,
  alternate data stream, delete-on-close, directory creation and deletion, `FILE_DELETE_CHILD`, owner change, other process rights
  (`PROCESS_CREATE_PROCESS`, `PROCESS_VM_WRITE`, token open, duplicate or impersonation), parent-process spoofing, window
  messages (the children never loaded `user32`, so the private desktop was configured but not exercised), a positive
  inherited-handle case, and any permissive or absent DACL. After H6 was denied at `OpenProcess`, its later steps were not reached.
- Passing cases (read, scratch write, create denial, truncate denial; and, post-stop, DACL-set denial and the `PROCESS_DUP_HANDLE`
  denial) are bounded observations. They do not establish P9, do not prove that every write or escape is impossible, and say
  nothing about Git, Schannel, GCM compatibility, credential retrieval or the real repository's ACLs.
- A job-accounting observation covers only the processes of this run. The real repository and the source object store were not
  targets, so nothing here measures their effective access.
- **Harness defect and protocol deviation (section 11.7):** the executed binary did not stop at the first source-mutation
  counterexample. This is a recorded defect, not "none observed".
- Build and lookup record, none repeated: the retained successful build `build-v1-a3` emitted warning C4189 (`waited` is
  initialized but not referenced, `p9probe-v1.c` line 550), which was left as is. Failed build attempts are retained: `build-v1`
  (an empty directory from a script abort on a missing `vswhere` path), `build-v1-a2` (a quoting error, `cl` "missing source
  filename") and the two `.failed` build scripts; the only executed binary is `build-v1-a3`. The header-constant check first looked
  for `BCRYPT_USE_SYSTEM_PREFERRED_RNG` in `um/bcrypt.h`, which does not exist in the installed SDK (`No such file or directory`),
  and the corrected lookup found it in `shared/bcrypt.h` as `0x00000002`; both appear in
  `environment-and-header-verification-20261015.txt`. The command capture `cmd-run1-20261015T201251Z.txt` is named with local time
  marked as `Z` (the run root's UTC name is `run-20261015T191251Z-27828`); the file holds only `exit=0`.
- The deletion counterexample is a candidate result and a decision input: the control validated the same operation, and the
  deletion occurred at the first source-mutation counterexample, before the defect let the run continue. Whether to specify a
  different configuration or another candidate is for Codex.

### 11.6 Provenance

Retained under ignored `logs/executor/p9-token-gate/` (never staged): `src/p9probe-v1.c` (SHA-256 `03789575...c5cc`),
`build-v1-a3/p9probe.exe` (`5ff1a0fc...387f`), the build scripts and logs, `runs/run-20261015T191251Z-27828/` (the fixture tree
and `out/host-results.jsonl` with `child-ctl.out.jsonl` and `child-res.out.jsonl`), `matrix-run-20261015T191251Z.txt`,
`derive-matrix.sh`, `environment-and-header-verification-20261015.txt`, `header-verification-20261015-pre.txt` and
`manifest-20261015T191344Z.sha256` (42 entries; its own digest `c2b05831...6ea`). Commands: `./build-p9probe.ps1 v1 a3`, then
`build-v1-a3\p9probe.exe host`, run once. Fixture roots were preserved; nothing was cleaned up. Correction round 1 changed only
this section and the handoff entry; it ran no harness, build or probe, and its outputs are under
`logs/executor/p9-token-gate/correction1/`. The 42-entry evidence manifest is unchanged.

### 11.7 Protocol deviation and harness defect

The dispatch required the run to stop on the first configuration counterexample. In the executed source (`p9probe-v1.c`),
`run_ops` stops only when the read (R1) or granted-scratch write (W1) fails (`STOP-GATE`, then `return FALSE`). After a source
operation that succeeds where denial was expected, such as the restricted S3 deletion, it continues to S4 and returns `TRUE`
(line 171); `role_child` then runs H5, H6 and spawns the grandchild, which runs the same operations again. There is no stop on a
successful source mutation. This is a **harness control-flow defect** and a **protocol deviation**.

Classification of the restricted results: R1, W1, S1, S2 and S3 (child) precede or are the first counterexample and are in-gate;
the restricted child's S4, H5 and H6 and every grandchild observation were obtained after the required stop point. Their
factual results are kept in sections 11.2 to 11.4 and are not presented as acceptance evidence under the dispatched gate. The
frozen harness was not repaired, rerun or approved for reuse, and no evidence was removed. Host snapshots and job accounting still
corroborate the in-gate deletion and the observed aftermath.

## 12. Diagnostic: file DELETE versus parent FILE_DELETE_CHILD under an ordinary and a restricted token (owned harness; one bounded run)

> **Diagnostic evidence, not a production configuration and not P9 acceptance.** This section was appended in a later slice
> (baseline `main` at `8ed77baea6f5f90e78636d25b8684c3a092d7b8d`, verified equal to local and live `origin/main`). Sections 1 to 11
> are preserved unchanged, including section 11's protocol deviation, its stop-gate defect and its frozen binary, which was not
> run, repaired or reused here. It selects no mechanism and no workaround. ADR-0050 remains Proposed, P9 remains unproven,
> candidate B remains paused, and an inconclusive reading of any item below is valid.

### 12.1 Question and design

Section 11 observed that the restricted child deleted a named source fixture file and did not investigate why. DeleteFileW is
documented to need either delete permission on the file or delete-child permission in the parent directory (DOC D01). This
slice isolates those two routes with a closed 2 x 4 matrix: an ordinary-token control and a restricted-token case for each of
four independent fixtures that differ **only** in two permission bits.

| Case | File DELETE | Parent FILE_DELETE_CHILD |
|---|---|---|
| Neither | absent | absent |
| FileOnly | granted | absent |
| ParentOnly | absent | granted |
| Both | granted | granted |

**Harness.** A new dependency-free C program, `p9delete-v1.c`, built once with the installed MSVC 19.51 (`_MSC_FULL_VER`
195136260), Windows SDK 10.0.26100.0, static CRT, no package download, on Windows `10.0.26200.9711`, run once on an NTFS volume.
The frozen v1 harness and binary of section 11 were neither run nor modified. The host creates eight fresh fixtures and verifies
every descriptor by read-back before any child exists, then runs at most eight sequential children (four ordinary, then four
restricted), with no grandchildren, no command shell, no Git, GCM, helper, network, credential, account, service or AppContainer
profile and no elevation. Nothing outside the new ignored run root was inspected or changed except the read-only inspection in
section 12.6.

**Child control flow (inspected before the run).** The child role is straight-line code: validate that the target path is
under the run's `cases` directory, `SetLastError(0)`, **one** `DeleteFileW` on the designated new file, emit one JSON line with the
return value and the raw Win32 error, return 0. It has no loop, no continuation, no DACL, handle-duplication, token, process or
other file operation, and no process creation. The source contains exactly one `DeleteFileW` call; the host never deletes. The
host reads the effective token of the created, still suspended child from outside (`OpenProcessToken` on the process handle)
before resume, so the child does not probe its own token.

**Launch APIs (not equivalent).** The ordinary controls used `CreateProcessW` with no token argument; the host-observed
child token showed the same privilege, group and integrity values as the host's own token. The restricted cases used `CreateProcessAsUserW` with a restricted primary token. Both used the same explicit
handle list (the pipe write end and a NUL stdin), flags `EXTENDED_STARTUPINFO_PRESENT`, `CREATE_SUSPENDED`,
`CREATE_UNICODE_ENVIRONMENT` and `DETACHED_PROCESS`, a fixed executable path, an environment of only `SystemRoot`, `TEMP` and
`TMP` (the case's scratch), the scratch as working directory, a private desktop per child and a private job (kill-on-close,
active-process limit 1, no breakaway) to which the suspended child was assigned before `ResumeThread`. The two launches
therefore differ in the token, the launch function, the private desktop's DACL (the restricted case's synthetic SID was added),
and the restricted case's synthetic grant on its own scratch; no ordinary-token `CreateProcessAsUserW` control exists.

**Restricted token.** `CreateRestrictedToken` over the harness's own unelevated primary token (Limited, integrity `0x2000`) with
`DISABLE_MAX_PRIVILEGE` plus `WRITE_RESTRICTED` and one fresh synthetic restricting SID per restricted case (created in process,
no account). The host-observed child token had `IsTokenRestricted` 1, one restricting SID, one privilege
(`SeChangeNotifyPrivilege`) against five for the ordinary child, the same 15 groups and integrity, and
`TokenAccessInformation.Flags` raw `0x2a18` against `0x2a00` (the same values as section 11). The synthetic SID was granted
`FILE_ALL_ACCESS` (container and object inherit) **only** on that case's new scratch directory and on the private desktop; it
was not granted any access to a source fixture file or parent directory.

### 12.2 Fixtures and descriptors

Each case has its own new directory under `runs/run-20261015T194158Z-18820/cases/<ord|res>-<Case>/` (the harness's run root is
under the ignored `logs/executor/p9-delete-routes/`) holding `parent/target.txt` (identical 24-byte content, SHA-256
`28263af2...67ae`) and `scratch/`. Canonical containment under the run root and the absence of reparse points from the gate
directory down were verified. For every case, **both** the file and the parent directory carry a **protected** DACL that was set
by the host and read back before launch and again immediately before launch (no drift) and after the child:

- the DACL contains exactly **one** `ACCESS_ALLOWED` ACE with flags `0x0` (no inheritance), for the current user (redacted as
  `<current-user>`; the file's owner and primary group also read back as `<current-user>`). Read-back control flags
  were `dacl_present=1`, `dacl_protected=1` and `dacl_auto_inherited=1` (the last set by the system, not requested). No other
  ACE exists, so no inherited or other ACE can supply an omitted right;
- file mask `0x00120089` (`FILE_READ_DATA`, `FILE_READ_EA`, `FILE_READ_ATTRIBUTES`, `READ_CONTROL`, `SYNCHRONIZE`), plus `DELETE`
  (`0x00130089`) for FileOnly and Both;
- parent mask `0x001200a9` (`FILE_LIST_DIRECTORY`, `FILE_READ_EA`, `FILE_TRAVERSE`, `FILE_READ_ATTRIBUTES`, `READ_CONTROL`,
  `SYNCHRONIZE`), plus `FILE_DELETE_CHILD` (`0x001200e9`) for ParentOnly and Both.

Before launch the host also recorded each file's attributes (`0x20`), volume serial, file index, link count (1) and content hash,
and each parent's identity, and confirmed that every fixture I/O handle was closed (the host's open handle count was 137 to
140 at the launches). As a descriptor sanity check only, a host-side `AccessCheck` with a duplicate of the ordinary token
granted `DELETE` on the file only for FileOnly and Both and `FILE_DELETE_CHILD` on the parent only for ParentOnly and Both
(not run for the restricted token; it is not a deletion).

### 12.3 Raw outcomes

Raw Win32 codes: 0 success, 5 `ERROR_ACCESS_DENIED`, 2 `ERROR_FILE_NOT_FOUND`. "Present" is the host's observation after the
child had exited and its job reported 0 active processes (one poll was enough in every case; absent cases read raw error 2).
Output: `runs/run-20261015T194158Z-18820/out/` and `matrix-run-20261015T194158Z.txt`.

| Case | Ordinary token: `DeleteFileW` | Ordinary: host observation | Restricted token: `DeleteFileW` | Restricted: host observation |
|---|---|---|---|---|
| Neither | failed, 5 | file present, identity and hash unchanged | failed, 5 | file present, identity and hash unchanged |
| FileOnly | succeeded | file absent | **failed, 5** | file present, identity and hash unchanged |
| ParentOnly | succeeded | file absent | **succeeded** | file absent |
| Both | succeeded | file absent | succeeded | file absent |

All eight children exited 0, produced exactly one well-formed result line, were launched suspended, contained and assigned to
their job before resume, and were observed with `TotalProcesses` 1 and `ActiveProcesses` 0 (each ran in under 10 ms). Every
descriptor and parent identity was unchanged after every child, and every parent listing showed only the expected remaining
entry (`target.txt` present, or none after a deletion). All four ordinary controls matched their expectation before any
restricted case ran. No setup, descriptor, control, capture, containment, authority or termination prerequisite failed, so
no stop occurred, and the restricted outcomes were planned measurements.

### 12.4 Evidence classes

**Observed (this host, token, build, one run each).**

1. Ordinary token: DeleteFileW was denied only in Neither and succeeded when either the file's `DELETE` or the parent's
   `FILE_DELETE_CHILD` was granted. These controls agree with the documented either-or rule below.
2. Restricted token (candidate A configuration): Neither was denied and unchanged; **FileOnly was denied (5) and the file was
   unchanged although the file's DACL granted the current user `DELETE`**; **ParentOnly succeeded and the file was deleted
   although the file's DACL granted the current user no `DELETE`**; Both succeeded. With the synthetic restricting SID absent
   from every source descriptor, the outcome therefore differed between the two routes: it depended on which of the two
   permissions the current user held.
3. In the restricted matrix, granting the current user `FILE_DELETE_CHILD` on the parent was the only variation that allowed the
   deletion; granting `DELETE` on the file alone did not.

**Documented rules (Microsoft Learn, retained as `sources/D01` to `D24`, read 2026-10-15).**

- D01: to delete or rename a file "you must have either delete permission on the file, or delete child permission in the parent
  directory"; `DeleteFile` marks the file for deletion on close; a read-only file fails with `ERROR_ACCESS_DENIED`. D03 lists
  `FILE_DELETE_CHILD` (`0x40`) as the right, for a directory, to delete a directory and the files it contains, and
  D02 lists `DELETE` among the standard rights valid for files and directories.
- D19 (`CreateRestrictedToken`) and D18 (restricted tokens): restricting SIDs are "considered only when evaluating write access"
  under `WRITE_RESTRICTED`, and access requires both the ordinary and the restricting check to pass (carried from S01 and S02).
  Neither page defines which rights are write access.
- D05, D24: `SetNamedSecurityInfo` with `PROTECTED_DACL_SECURITY_INFORMATION` sets a protected DACL (the required right is
  `WRITE_DAC`) and propagates inheritable ACEs to existing children; the ACEs here are non-inheritable. D09 and D17: an object
  without a DACL grants full access and a DACL with no matching allowing ACE denies, which is why the fixtures carry one explicit
  ACE and no other.
- D13 (`CreateProcessAsUserW`) and D14 (`CreateProcessW`) are the pages of the two creation functions used; this slice did not
  read them for a statement that they are equivalent for this purpose, and no equivalence is claimed.
- Pages read in the relevant part for the statements above: D01, D02, D03, D05, D09, D18, D19 and D24; the others (D04, D06 to
  D08, D10 to D17, D20 to D22) are retained as the references for the native calls the harness uses and are not cited for a
  claim.

**Inference (not observed, not established by a page).**

- Observation 2 is consistent with the restricting check being applied to the file-level `DELETE` access that `DeleteFileW`
  needs when the user's `DELETE` is the only route, and with the parent-level `FILE_DELETE_CHILD` route not being blocked by the
  restricting SIDs. It is **not** consistent with the simple hypothesis offered in section 11.4 that `DELETE` on the file is
  outside the rights the write-restricted check treats as write: under that hypothesis FileOnly would have succeeded. The
  experiment does not show which access check, which requested rights, or which internal path produced either outcome, and it
  does not show that the parent route is evaluated against the restricting SIDs at all.
- Section 11's source fixtures inherited the run root's ordinary-user access. A present-day read-only inspection (12.6) of the
  surviving old fixture directories shows inherited ACEs granting the current user full control, which would include both
  permissions of this matrix. That is **not** a reconstruction of the deleted file's historical descriptor, which was not
  recorded; it is consistent with section 11's deletion being a Both-like case, and no cause of the historical result is inferred
  beyond that consistency.

**Unknown.**

- Why the two routes differ under this token, and which Windows component or check decides each one; nothing here reaches
  internals.
- Whether a different restricting SID set, flag set, integrity level, launch function or token (including an ordinary token
  launched through `CreateProcessAsUserW`) changes any cell; the launch function and the token were varied together.
- Behavior of other write-class operations (rename, which D01 groups with deletion, append, links, streams, delete-on-close,
  directory deletion, `WRITE_DAC` and `WRITE_OWNER`), another file system, other Windows builds, a repeat of this run,
  or a real repository's ACLs (none was inspected).
- Whether any P9-relevant mechanism could avoid the parent route; no design was evaluated.

### 12.5 Limits and what this does not establish

- One run, one Windows build, one volume, files only, one restricting-SID shape (synthetic, absent from the source descriptors),
  one ACE per descriptor, no repetition; each cell is a single observation.
- The child's token was read from the host after creation and before resume, not by the child; the child's DeleteFileW result
  and the host's later observation are the only deletion evidence. The ordinary controls validate that the operation works and
  that the descriptors behave as documented for the ordinary token; they do not validate the restricted launch path.
- This does not establish P9, the denial or the permission of any other operation, a credential boundary, Git, Schannel or
  GCM compatibility, handle or process isolation, or desktop isolation, and it does not select, rank or recommend a
  mechanism, a configuration or a workaround. Whether the observation changes the candidate comparison, requires a
  specified mechanism, or ends candidate A is for Codex.
- Section 11's retained limitations stand: the stop-gate defect and protocol deviation of v1, warning C4189 in that build,
  its failed build attempts, and the header lookup and command-capture naming records are unchanged. This slice's own record is
  below.

### 12.6 Present-day read-only inspection of surviving v1 fixtures (labelled present-day evidence)

`icacls` was run read-only on the surviving v1 directories `res`, `res-gc` and `ctl` and on two files of `res`. Each showed
inherited ACEs (`(I)`), including the current user with full control and several other principals with modify; the output is
retained with SIDs and the account name redacted as `old-fixture-readonly-icacls-20261015.txt`. Nothing was mutated, and the
deleted file's historical descriptor was not reconstructed from this or from assumptions. The captured output file was redacted in place; the original
unredacted capture was not retained. The retained text is a redacted derivative, not an unmodified raw capture.

### 12.7 Provenance

Retained, uncleaned and ignored by Git under `logs/executor/p9-delete-routes/`: `src/p9delete-v1.c` (SHA-256 `57194b87...92f3`),
`build-p9delete.ps1` (`6b06bbbe...c073`), `build-v1-a1/` (`p9delete.exe` `9e85dd17...97c9`, `build.log`, object file),
`runs/run-20261015T194158Z-18820/` (the eight fixtures and `out/host-results.jsonl` `59a7dac3...8b19` with the eight child
outputs), `cmd-run1-20261015T194158Z.txt` and its exit record, `derive-matrix.sh`, `matrix-run-20261015T194158Z.txt`,
`old-fixture-readonly-icacls-20261015.txt`, the 24 retained documentation pages under `sources/` (`D23`, `getprocesstimes`, was
fetched and is not cited), and `manifest-20261015.sha256` (46 entries; its own digest `d54e38a6...4421`). Commands, each once:
`build-p9delete.ps1 v1 a1`, then `build-v1-a1/p9delete.exe host`. The build printed a `vswhere.exe is not recognized` line from the
Visual Studio environment script (this build script, unlike v1's, did not extend `PATH`), compiled with `cl exit: 0` and no compiler
warning at `/W4`. No build, run, case or harness defect required a rerun, so no failed attempt exists in this slice. Fixture
roots were preserved with their protected DACLs (eight parent directories and three surviving files: ordinary Neither,
restricted Neither and restricted FileOnly). Future cleanup and any required permission changes were not evaluated and are not
authorized by this slice; no cleanup was attempted.

## 13. Viability gate: full restriction (DISABLE_MAX_PRIVILEGE only, one synthetic restricting SID) with detached scratch (owned harness; one bounded run)

> **Experimental evidence, not a production configuration and not P9 acceptance.** This section was appended in a later slice
> (baseline `main` at `e2510c55112ec55f1fb8a030c1f2e005822926ca`, verified equal to local and live `origin/main`). Sections 1 to 12
> are preserved byte for byte, including section 11's protocol deviation and stop-gate defect and section 12's diagnosis. It
> selects no mechanism and no workaround. ADR-0050 remains Proposed, P9 remains unproven, candidate B remains paused, and the
> negative or inconclusive reading below is valid. A detached scratch fixture is not a Git object-copy implementation, and
> ADR-0050's source-object design is unchanged.

### 13.1 Question and closed configuration

Can one exact full-restriction configuration (1) start without changing permissions outside new owned experiment resources,
(2) read and write a detached scratch fixture, and (3) refuse a source-fixture read and both deletion routes measured in
section 12? The configuration was closed before the run: `CreateRestrictedToken` over the harness's own unelevated primary
token with `DISABLE_MAX_PRIVILEGE` only, no SIDs to disable, no privileges to delete, exactly **one** fresh synthetic
restricting SID, and **no** `WRITE_RESTRICTED`, `SANDBOX_INERT`, `LUA_TOKEN`, further restricting SID, privilege enabling,
elevation or fallback. The host's own token was recorded first and matched the prerequisite: primary, elevation type Limited
(3), not elevated, `IsTokenRestricted` 0, integrity `0x2000`, `TokenAccessInformation.Flags` raw `0x2a00`, five privileges,
15 groups. The restricted token as obtained was verified before any launch to have exactly one restricting SID (the synthetic
one, attributes `0x7`), exactly one privilege (`SeChangeNotifyPrivilege`, attributes `0x3`), `IsTokenRestricted` 1, the same
15 groups and integrity, and raw flags `0x2a10`. Sections 11 and 12 recorded `0x2a18` with `WRITE_RESTRICTED`; the difference
of `0x08` is consistent with that flag being absent, but the bit mapping is not verified here.

### 13.2 Harness, build and static review

Two new dependency-free C programs under ignored `logs/executor/p9-full-restriction-gate/src/`: a host
(`p9gate-host-v1.c`, imports `KERNEL32` and `ADVAPI32`; `user32` and `bcrypt` are loaded dynamically in the host only) and a
separate child (`p9gate-child-v1.c`, imports `KERNEL32.dll` only, recorded with `dumpbin`). Built with the installed MSVC
(compiler banner 19.51.36260, linker 14.51.36260.0, `_MSC_FULL_VER` 195136260; `vcvars64.bat` selected Windows SDK
10.0.28000.0, read from its environment; an SDK 10.0.26100.0 is also installed and earlier sections name it), `/W4 /MT /O2
/GS /guard:cf`, static CRT, no package download. The run was on Windows `10.0.26200.9711`, NTFS. The previous harnesses were
neither run nor modified.

The child is straight-line: fixed argv, a canonical-prefix check, then five operations in fixed order, each followed
immediately by a check that returns on the first unexpected result: (1) `SCRATCH_READ`, (2) `SCRATCH_WRITE` (the only
truncating open, on the designated detached scratch file), (3) `SOURCE_SENTINEL_READ` (open for data read; nothing is read in
restricted mode), (4) `FILEONLY_DELETE`, (5) `PARENTONLY_DELETE` (two `DeleteFileW` call sites). It contains no process
creation, token, DACL, handle-duplication, directory, move or link call. The host never deletes or rewrites a fixture after
setup; its only `SetNamedSecurityInfoW` calls are setup-time protected fixture descriptors and the two synthetic grants below.
The review was done on the sources before the build and again after the build fix, and is retained as
`static-review-v1.txt` and `static-review-v1-a2.txt` (forbidden-call counts of 0 in the child, the call-site listings and a
written control-flow reading). No DACL-changing, token-escape, host-handle, parent-spoofing or additional write probe exists.

### 13.3 Fixtures, descriptors and grants

New canonical non-reparse run root `runs/run-20261015T203020Z-24964/` (containment, absence of reparse points and NTFS were
verified before any fixture). Independent fresh fixtures for each child, `ctl` (ordinary) and `res` (restricted), each with
`bin/` (a byte-identical copy of the child executable, SHA-256 `7ac6c60f...1fa2`, created by the host so it inherits the
directory's ACEs), `source/sentinel.txt`, `fileonly/target.txt`, `parentonly/target.txt` and `scratch/` (`copy.txt` and `tmp/`;
`TEMP` and `TMP` point at `tmp/`). The sentinel and the detached scratch copy hold the same fixed fictitious 37 bytes (SHA-256
`cf19bab5...3785`); each deletion target holds fixed 34 fictitious bytes (`c6b0ddff...d16d`). Fixture I/O handles were closed
before each launch (host open-handle count 140 and 143).

As in section 12, the six source descriptors per case carry a **protected** DACL set by the host and read back (control flags
`0x9404`, exactly one `ACCESS_ALLOWED` ACE, flags `0x0`, the current user redacted as `<current-user>`), re-read immediately
before each launch (no drift): sentinel file `0x00120089` and its parent `0x001200a9`; FileOnly file `0x00130089` (`DELETE`) and
parent `0x001200a9` (no `FILE_DELETE_CHILD`); ParentOnly file `0x00120089` (no `DELETE`) and parent `0x001200e9`
(`FILE_DELETE_CHILD`).

The synthetic SID received exactly two grants, both in `res`: read/execute `0x001200a9` (container and object inherit,
ACE flags `0x3`) on `res/bin`, so the executable carries it inherited (`0x10`); and read/write `0x0012019f` (flags `0x3`) on
`res/scratch`, inherited by `copy.txt` and `tmp/`. No other grant to the synthetic SID, no grant on any source fixture, the
`ctl` tree, an old root, a system or user directory or the repository, and no broad SID, null DACL, capability, profile,
service or ambient fallback. A walk of the whole run root (26 objects before the launches, 28 after) listed every ACE naming
the synthetic SID: exactly five (`bin`, the executable, `scratch`, `copy.txt`, `tmp`), none elsewhere. The private desktop's DACL
names the current user, SYSTEM and, for the restricted child, the synthetic SID. The `bin` and `scratch` directories also carry
the ACEs they inherit from the repository tree (nine ACEs each: the synthetic grant plus eight others, including the current
user and other ordinary principals); those pre-exist, were not changed by the host and are not synthetic-SID grants. Output is captured through inherited anonymous
pipes, so no output path was granted.

### 13.4 Raw outcomes

Both children used fixed executable and argv, `STARTF_USESTDHANDLES` with an explicit handle list (stdout pipe, stderr pipe, NUL
stdin), a private desktop and a private non-breakaway kill-on-close job (active-process limit 1, so no grandchild) to which the
suspended child was assigned before `ResumeThread`, `TEMP`-only environment (`SystemRoot`, `TEMP`, `TMP`), a 30 s bound and a
64 KiB cap per stream. The control used `CreateProcessW` and the restricted child `CreateProcessAsUserW`; the launch function,
the token, the private desktop's DACL and the synthetic grants therefore differ, and no ordinary-token `CreateProcessAsUserW`
control exists, so the token is not shown to be the only variable. The harness set its own error mode so a hard-error dialog
could not stall a child.

| Child | Result |
|---|---|
| Ordinary control (`CreateProcessW`) | **Valid.** Exit 0 in 28 ms; seven well-formed named lines, all expected: `SCRATCH_READ`, `SCRATCH_WRITE` (36 bytes written and read back), `SOURCE_SENTINEL_READ` (content matched), `FILEONLY_DELETE` and `PARENTONLY_DELETE` succeeded, `DONE`. Host observation agreed: sentinel unchanged (bytes, identity and descriptor), both deletion targets absent, scratch copy rewritten to the fixed new bytes. Job query succeeded, 1 total and 0 active processes; stderr 0 bytes. |
| Restricted (`CreateProcessAsUserW`) | **Process created; no child result captured.** The token was created and verified, the process was created suspended, assigned to the job before resume, its token read from the host (same values as the created token), and resumed. It exited **`0xc0000022` (`STATUS_ACCESS_DENIED`)** in 25 ms **with no captured line, including `CHILD_START`**: zero stdout bytes and zero stderr bytes. Job query succeeded, 1 total and 0 active processes; no timeout, so no termination was needed and no `TerminateJobObject` call was made. |

The restricted gate order (scratch read and write, then denied sentinel read, FileOnly deletion and ParentOnly deletion) has
**no captured result for any check.** Whether child entry or an ordered operation was reached is not established by silence. The host's observations after the restricted child are
the only outcome data: the sentinel, both deletion targets and the scratch copy were byte-identical to their before snapshots
(SHA-256 and file identity equal), every protected descriptor and parent descriptor equal to setup, every parent listing
unchanged, and the scratch copy was **not** rewritten. The host classified the case as "child exited without emitting a result
(start or loader failure)", stopped the matrix and returned. That classifier label does not identify a proven failing stage; no ordered gate outcome was established.

### 13.5 Evidence classes

**Observed (this host, token, build, one run).**

1. The control is valid and shows the fixtures, grants and capture path work for the ordinary token.
2. With exactly this closed token, `CreateRestrictedToken`, `CreateProcessAsUserW`, pre-resume job assignment and `ResumeThread`
   all succeeded for the unelevated user with no privilege widening, and **the child process terminated with
   `STATUS_ACCESS_DENIED` with no captured stdout or stderr.** Process creation succeeded. This is not evidence of denial
   of a source operation or proof that child entry was never reached. The child emitter ignores `WriteFile`'s result, so absent output alone cannot prove absent execution. No diagnosis was run.
3. Host before/after snapshots show matching source-fixture bytes, identities and descriptors; no intervening-write history was measured. The host performed no source mutation after setup.

**Inference (not observed, not established by a page).**

- A required image, DLL, section or other resource might have failed the second restricting-SID access check if it lacked an
  effective grant. No system-resource descriptor or loader access was measured here. This hypothesis is **consistent with** the
  observed failure and **is not shown** to be its cause: the experiment does not identify which object, which check or which
  loader stage returned the status (the image, a system DLL or `KnownDlls` section, the working directory, the desktop or
  another resource). No diagnosis probe was run.

**Not established.**

- Whether the sentinel read, the FileOnly deletion and the ParentOnly deletion would be refused under this token: they were
  not evidenced by captured operation results; whether they were attempted is not established. This section is **neither a pass nor a deletion counterexample.**
- Whether a child can reach and complete its ordered gate under one restricting SID with only the grants dispatched here; any configuration that adds
  restricting SIDs or widens grants on system directories is a different configuration and was not run.
- Everything section 12.4 and section 12.5 list as unknown for other tokens, builds, operations and file systems, and Git, Schannel
  or GCM compatibility, handle or process isolation, desktop isolation and credential behavior.

### 13.6 Limits, defects and what this does not establish

- One run, one build, one volume, one synthetic SID, one launch function per token; each result is a single observation.
- **Capture defects (recorded, nothing reconstructed).** The command-capture wrapper `cmd-run1-20261015T203020Z.*` expanded
  `%ERRORLEVEL%` at parse time, so `cmd-run1-20261015T203020Z.exit.txt` reads `exit=0` and is **not** the host's exit code, which
  was not measured (by code inspection the restricted-gate stop path returns 35). The host's own `STOP` and `RUN exit` records and
  the child result lines are the evidence. The first toolchain capture (`toolchain-identity-20261015.txt`) printed unexpanded
  variables for the same reason, the second (`-b`) failed because the batch file was invoked without a path, and the third (`-c`)
  is the valid one; all three are retained. The host `CHILD-RESULT` records for the `CHILD_START` and `DONE` lines print
  `observed=failure` only because those two lines carry no `observed` field; their verdicts are `expected`.
- **Build.** `build-v1-a1` failed for the child: `_countof` was used without `<stdlib.h>` (warning C4013 then link error LNK2019); the host
  compiled in that build. One include line was added to the child only and the pair was rebuilt as `build-v1-a2` (0 warnings at
  `/W4`, build exit 0). Both build directories are retained; only the `a2` executables were run. The failed attempt did not run
  anything.
- The launch-function difference stands as a limitation; no causal claim that the token or its flag is the only variable.
- No grandchild, DACL-change, token-escape, host-handle, parent-spoofing or additional write probe was run. The restricted
  private desktop, the handle list and the job were configured and used by the host; whether the child ever reached `user32` is
  unknown.
- This does not establish P9, source-copy correctness, an object-supply design, escape resistance, a credential boundary, or
  Git, Schannel or GCM compatibility, and it does not select, rank or recommend a mechanism or a changed configuration. Whether
  the result ends the single-restricting-SID line, calls for a different bounded configuration, or something else is for Codex.

### 13.7 Provenance

Retained, uncleaned and ignored by Git under `logs/executor/p9-full-restriction-gate/`: `src/p9gate-host-v1.c` (SHA-256
`ef204f1d...7774`), `src/p9gate-child-v1.c` (`4d8ed6ae...1306`; the `a1` version `c79b4573...5b5f` is recorded in
`static-review-v1.txt`), `build-p9gate.ps1`, `build-v1-a1/` (failed) and `build-v1-a2/` (`p9gate-host.exe` `8f238420...8418`,
`p9gate-child.exe` `7ac6c60f...1fa2`, `build.log`, `imports-child.txt`, `imports-host.txt`), the two static reviews, the three
toolchain captures and `toolchain-identity.bat`, `runs/run-20261015T203020Z-24964/` (the two fixture trees and
`out/host-results.jsonl` with `child-ctl.stdout.jsonl`, `child-res.stdout.jsonl` and the two empty stderr captures), the command
capture files, and `manifest-20261015.sha256` (37 entries; its own digest `b7ea3d14...8276`). Commands, each once: `build-p9gate.ps1 v1 a1`
(failed), `build-p9gate.ps1 v1 a2`, then `build-v1-a2\p9gate-host.exe host` through `cmd.exe /c` with output redirected to the
command-capture files. The only runs of this experiment are the one control and the one restricted child. Fixture roots were
preserved with their protected DACLs (`ctl` retains its sentinel and rewritten scratch copy; `res` retains all four
fixtures and the unmodified scratch copy); cleanup and any permission reset were not evaluated and are not authorized by this
slice. Agent qualification stays `Blocked`/`TargetsNotReal`, both allowances unused; Git/GitHub qualification is separate and
unstarted. The historical concurrent-replay 409, target-test 500, disposal `NullReferenceException` and unnamed Api 2067/2068
failure remain separately unexplained, and no later pass establishes flakiness or irrelevance.
