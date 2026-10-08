# ADR-0029: Deliver an explicit local commit before closing provider contract gaps

Status: Accepted

## Context

The owner authorized this sequencing change on 2026-10-06. The manual collaboration journey can implement, verify and review a
change, but cannot yet deliver a local commit. Remaining Increment 4 contracts for Claude account usage, provider-session resume,
compaction and additional permission profiles are unproven. More historical observations or context samples would not establish
those contracts. The order in [ADR-0009](0009-separate-agent-roles-effects-and-provider-assignments.md) would otherwise hold all
Increment 5 delivery work behind them.

The host already owns Git mutation under [ADR-0005](0005-use-git-worktrees-and-a-policy-controlled-github-loop.md). Its physical
repository lease, worktree registration and marker are governed by
[ADR-0008](0008-physical-repository-identity-and-tool-owned-worktree-ownership.md). Current startup reconciliation expects the
worktree HEAD to remain the preparation source commit. A legitimate host commit therefore needs durable authority and bounded
recovery as part of the same outcome. Git refs, the worktree index and SQLite cannot be committed in one atomic transaction.

## Decision

### Sequencing, without changing completion criteria

Permit the bounded explicit local-commit slice below before Increment 4 is complete. Its remaining requirements stay open and
unavailable where safe contracts are absent; they are neither removed nor represented by scaffolding. This narrowly supersedes
ADR-0009's increment ordering, not its role, effect, assignment, permission or concurrency rules. It grants no subsequent slice,
autonomy, remote publication or project-repository commit/push GO.

### One explicit operation

An authenticated, body-bearing `POST /api/runs/{runId}/local-commit` sends one command through the mediator. It accepts an operation
UUID, checkpoint UUID, approved CodeReviewer attempt UUID, approved human checkpoint-review UUID and a human-written commit
message. The message is trimmed, admits LF line endings, is at most 2 KiB of UTF-8, has a nonempty subject and rejects other control
characters; the request body is at most 8 KiB. The host adds one fixed operation-identity trailer. It derives every path, ref, SHA,
lease, verification member and Git argument; callers supply none of those. A read-only status query exposes eligibility, the
operation and safe outcome facts. An eligible status is advisory; the command decides again from fresh authority.

Only an exactly admitted `ManualAgent`, Running run may use this operation. There is at most one durably admitted local-commit
operation per run. Repeating the same operation UUID with identical normalized inputs returns its recorded operation without
another Git execution; changed inputs or a competing UUID conflict. Pre-admission refusals consume nothing. This is not an Agent
attempt and consumes no Agent budget, grant or provider observation. Existing Agent stops remain intact.

### Exact approval and exclusion gates

Admission and the final execution decision require fresh, untracked reads of the run, project, current workspace/checkpoint,
physical lease and marker, implementation lineage, latest applicable CodeReviewer attempt and its actual approval message,
human review and relational evidence, and enabled verification recipes with their latest executions. A successful review must
refer to the exact implemented report/plan and current checkpoint. A bare `FutureAgent` checkpoint-review row is insufficient.
Every enabled recipe must have a current Passed execution with exactly matching command snapshot and completion fingerprint.
The execution membership must equal the Agent review's ordered claimed membership and the selected human approval's membership;
overlap, stale configurations, empty verification, malformed or foreign evidence do not qualify.

The selected human row must be Approved for that exact checkpoint. All human decision rows for the same checkpoint must also be
Approved and coherent; a Pending or Rejected fact blocks this first commit feature. It never infers supersession from wall-clock
time or UUID order. Resolving mixed decisions requires a new checkpoint and new applicable evidence and approvals; this slice
adds no review override or chronology migration. The complete authority identity is pinned, including the human decision set,
and compared again, not merely its counts. A newer relevant implementation/review or any authority change refuses execution.

There must be no active Agent or verification execution in the workspace and no competing repository writer. The durable intent
reserves the owned workspace for this operation. Add an explicit Committing state, preserving existing enum values, and serialize
the reservation against Agent/verification claims, checkpoint and review recording, and verification-recipe mutation at their
write seams. These operations cannot race past a preliminary Ready check. Unrelated repositories retain their behavior. Git and
filesystem work remain outside short SQLite transactions; the locked transaction revalidates authority before recording intent
and again before recording a single-use execution marker. No generic scheduler or new global concurrency policy is introduced.

### Commit only immutable, physically admitted bytes

Create a complete bounded snapshot whose ordinary observation matches the approved checkpoint. Current added/modified files must
be read from held handles proven to be exact-path, regular, single-name sources inside the owned worktree, before reading and
again afterwards. Deletions need proven absence. Reuse the physical proof, not a preview, truncated text or Git working-path
patch. Windows is the only admitted host until equivalent proof exists. Admit at most 128 changed paths, 256 KiB per current file
and 4 MiB total current bytes. Binary bytes are allowed within those bounds. Unsupported path spellings, links, submodules,
conflicts, sparse-index/worktree forms, mode changes and incomplete observations refuse the whole commit. Existing executable
mode is preserved; new regular files use 100644. Empty changes are refused.

Use an isolated index initialized from the exact parent tree, raw blobs written with `hash-object --no-filters --stdin`, explicit
cache entries and `write-tree`. Unchanged entries inherit that parent; deletions remove only the recorded entries. The owned
worktree's real index must initially match the parent with no staged or unmerged changes. Do not use `git add`, `git commit`,
`reset --hard`, working-path hashing or checkout to construct the commit. A later external source edit cannot enter the tree.

Attribute decisions must use the immutable proposed tree, not a mutable working-tree `.gitattributes`. Suppress global/system
attributes and require repository `info/attributes` to be absent. Reject clean/process filters, working-tree encoding and ident
conversion before any converter could execute. Built-in text/EOL behavior is supported only when conversion of the admitted bytes
under that immutable attribute source is an identity: compare the canonical blob with the raw blob. A conversion such as CRLF to
LF refuses; normalization must precede a new checkpoint, verification and approvals. Never silently transform reviewed bytes.
All relevant attribute sources must be covered by the snapshot or proven absent; an unrepresented applicable source refuses.

Use fixed argument lists, bounded I/O and timeouts, a controlled Git environment, an owned proven-empty hooks directory for every
Git invocation, disabled signing and no credentials, network, fsmonitor, external diff/textconv, filter or repository executable.
Disable the `reference-transaction` hook as well as commit hooks. Pin a valid configured local author identity and UTC metadata;
do not invent the owner's identity. `commit-tree` creates one unsigned commit with the exact tree, expected parent, message and
operation trailer. Pre-admission object writes may leave unreachable objects; they grant no branch mutation authority.

### Durable intent, compare-and-swap and recovery

Persist immutable operation facts before branch mutation: normalized request identity, authority snapshot/membership, workspace,
lease and exact owned branch, parent/tree/proposed commit OIDs, author/date/message, and real-index preimage and prepared-index
identity. Keep the prepared index in a physically owned, bounded artifact. Journal admission, execution and terminal outcomes with
normal run event sequences. Introduce a focused entity/migration and ports, not a nullable Agent result or generic recovery framework.

Only the recorded owned branch may advance, using `update-ref` with the exact expected parent. Verify HEAD's binding to that branch
and ownership immediately around promotion. Synchronize only that worktree's administrative index with the recorded prepared tree,
under an exclusively created owned index lock and exact preimage checks; never overwrite source files or another process's lock.
If the binding, index or ownership changes, do not guess. The main checkout's HEAD, ref, index and files remain untouched.

A confirmed ref, commit object and index matching the recorded tree completes the operation and uses the existing Run completion
transition. The workspace returns to Ready only when ownership and the post-commit source observation are also consistent; an
external edit after the last observation never changes the delivered commit and requires attention instead of a clean label.
A definitely unpromoted failure, with unchanged parent/index and proven ownership, records Failed and fails the run; a startup
interruption proven unpromoted records Interrupted and interrupts it. Neither result authorizes another commit operation on that
run. An ambiguous result keeps the workspace reserved or NeedsAttention and the run nonterminal until exact reconciliation; it
is never a retry, a success or an invented failure fact. A persistence failure after promotion must also fail closed in-process.

Recover local-commit operations before ordinary workspace reconciliation or any supervisor dispatch. Exact recorded commit/tree/
parent/trailer and index evidence can complete a promoted operation without another commit or ref update. An owned pending index
promotion may be finished only with its exact persisted preimage/prepared artifact and exclusive lock proof. Unknown locks, refs,
objects, ownership or artifacts require attention; do not inspect a reflog for a plausible winner or retry Git mutation blindly.
Cleanup removes only artifacts/locks whose operation ownership is proved. A completed operation provides the narrowly extended
expected HEAD for ADR-0008 reconciliation; the original SourceCommitSha remains immutable. Across later runs in that workspace,
derive the expected tip only from the unique coherent chain of completed recorded parent/commit edges rooted at SourceCommitSha,
not a timestamp, UUID or arbitrary descendant. A broken or ambiguous chain refuses further delivery. Unrecorded advances still
fail closed, and pre-existing NeedsAttention states are not repaired by this feature.

### Cockpit and evidence

Expose a local-commit form only from settled, successful status for the selected run/checkpoint and approvals. Explain unsigned,
hook-free, local-only execution, irreversible operation admission and exact-byte conversion refusals. Pending or failed refresh
withholds submission. Bind message drafts, pending guards and errors to that committed identity and a draft version; obsolete
callbacks and completions cannot act across replacement, A to B to A, unmount or a newer request. A request with an unknown outcome
is reconciled by read-only operation status, never automatically resubmitted. Persisted status and history expose the local SHA,
parent/tree, branch, checkpoint and outcome after reload; they never imply a push, clean workspace or provider reliability.

## Acceptance and stop gates

Require real Git and file-backed SQLite through the protected API for exact tree/parent/index, additions/deletions and binary
controls, every gate, populated-tracker and commit-seam changes, concurrent request/claim/configuration races, and persistence or
process interruption at every external-effect boundary. Test restart before and after ref/index promotion and terminal recording,
including external ref/index/HEAD/marker changes. Sentinel hooks/filters must never run; linked sources must be refused before
content reaches Git; LF identity and CRLF conversion need controls. Check the active checkout and unrelated run remain unaffected.
Exercise a production-written browser journey with normal authentication, normal host composition, all supervisors and provider
process doubles, ending in explicit human approval, real local commit and reload. No raw-SQL approval fixture substitutes for it.

Stop if exact tree construction, exclusion, converter suppression, owned index promotion or recovery cannot be proved within this
contract. Report the concrete blocker rather than falling back to working-path staging, weak gates, automatic retry or recovery
authority. Include relevant full validation, meaningful mutation checks, generated-client regeneration, and fresh versus retained
evidence. Process doubles do not establish real-provider reliability.

## Boundaries and consequences

This is one end-to-end manual local-delivery outcome, including its required persistence, hosted execution, UI and recovery. It
does not implement autonomous coordination, scheduling, pause/resume, retry/takeover, run replacement, provider sessions, Claude
account enforcement, push, pull request, CI, merge, branch/worktree deletion or generic filesystem hardening. It changes neither
raw checkpoint identity nor existing preview/inspection delivery. A multi-linked file, unsupported conversion or conflicting human
decision can block this conservative first feature. It does not claim completion of Increment 4, Increment 5 or the MVP.

Official contracts checked on 2026-10-06:
[commit-tree](https://git-scm.com/docs/git-commit-tree), [update-ref](https://git-scm.com/docs/git-update-ref),
[hash-object](https://git-scm.com/docs/git-hash-object), [check-attr](https://git-scm.com/docs/git-check-attr),
[Git attribute-source option](https://git-scm.com/docs/git), [attributes](https://git-scm.com/docs/gitattributes),
[hooks](https://git-scm.com/docs/githooks) and [update-index](https://git-scm.com/docs/git-update-index).
An independent disposable Git 2.53.0.windows.1 probe confirmed isolated-index construction, immutable attribute-source selection,
LF identity versus CRLF conversion, exact snapshot retention after an external edit, and stale-parent CAS refusal. It did not
prove the application's containment, transactions, index promotion, startup or browser behavior; those remain acceptance work.
