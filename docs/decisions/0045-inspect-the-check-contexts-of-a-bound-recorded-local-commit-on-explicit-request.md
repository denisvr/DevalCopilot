# ADR-0045: Inspect the check contexts of a bound recorded local commit on explicit request

Status: Accepted

## Context

[ADR-0044](0044-observe-the-check-summary-of-a-bound-recorded-local-commit-on-explicit-request.md) shows GitHub's single aggregate check state for the recorded commit. It
cannot say which check or status produced that state, so it supplies nothing a later diagnosis or required-check policy could name. This closes one input gap: on explicit
request, show the bounded collection of check-run and commit-status metadata (kind, opaque ID, label, literal status, literal conclusion and check suite) that GitHub returns
for the recorded commit, but only when the freshly observed repository ID equals the immutable [ADR-0043](0043-bind-the-github-repository-identity-of-a-recorded-observation-target-on-explicit-request.md)
binding and the exact recorded branch currently points at the recorded commit. It is what GitHub returned for one commit at one moment (`Commit.statusCheckRollup.contexts`,
first 50) and nothing more: not complete check or workflow evidence, not a workflow-run, job, step, log or attempt history, not a required-check or branch-protection decision,
not a permission, not a publication approval and not a publication performed by DevalCopilot. Required-check policy, workflow enumeration, scoped publication, push, pull
request, reconciliation, automatic monitoring, the remaining Increment 4 contracts and qualification, and Increment 5 coordination stay separately gated, and real Agent
qualification is unaffected. The ADR-0040 through ADR-0044 contracts, readers, ports, adapters, statuses and wire answers are unchanged.

## Decision

**One protected, bodyless, read-only observation.** `GET /api/runs/{runId}/github-check-contexts-observation` (`GetGitHubCheckContextsObservationQuery` through
`IApplicationMediator`, one MVC endpoint under the protected runs route, API-owned response types for the whole transport graph, the generated client regenerated
reproducibly and additively and never edited by hand) returns `{runId, status, source, target, binding, observation}`. The `source` `{operationId, commitSha, branchName}`,
`target` `{id, eventId, host, owner, repository, recordedAtUtc}` and `binding` `{id, eventId, repositoryNodeId, owner, repository, observedAtUtc, recordedAtUtc}` are the existing
fact shapes (the same API-owned response types), mapped from Application views. The `observation` is
`{repositoryNodeId, owner, repository, observedAtUtc, ref: null | {branchName, commitSha}, totalCount: null | integer, contexts: null | [context]}` and each context is exactly
`{kind, id, name, status, conclusion, checkSuiteId}`. An unknown run is the fixed 404 `github_check_contexts_observation.not_found`. The route accepts no client repository, node
ID, branch, SHA, cursor, limit, executable, credential or query, and any request body (a positive declared length, any transfer encoding, including an empty chunked one, or a
readable byte) is refused before dispatch with the fixed 400 `github_check_contexts_observation.body_not_allowed`, which never echoes it. It writes no row, event or lock, holds
no transaction across external work, persists nothing, caches nothing and changes no schema; a database fault keeps the normal pipeline.

**Closed statuses and nullability.** Four fact groups are nullable (`source`, `target`, `binding`, `observation`) and each status fixes exactly which are present; inside an
observation `ref`, `totalCount` and `contexts` are fixed by the status too.

| Status | source, target | binding | observation | `ref` / `totalCount` / `contexts` | Invoked |
|---|---|---|---|---|---|
| `NotRecorded` (no target and no binding or event) | absent | absent | absent | n/a | nothing |
| `Unavailable` (any source, target, binding or event is incoherent or orphaned, or the scope or binding was lost or changed during the observation) | absent | absent | absent | n/a | nothing on the initial refusal |
| `BindingNotRecorded` (coherent source and target; genuinely no binding row and no event) | present | absent | absent | n/a | nothing |
| `CliUnavailable`, `Unknown` | present | present | absent | n/a | nothing for the first |
| `IdentityChanged` (a whole valid answer whose opaque ID differs ordinally from the binding; the other ID and any entry is never returned) | present | present | absent | n/a | one observation |
| `RefNotReturned` (valid null ref) | present | present | present | null / null / null | one observation |
| `CommitDifferent` (bound ID, exact branch, another valid commit) | present | present | present | present / null / null even if GitHub returned entries | one observation |
| `NoContextsReturned` (bound ID, exact branch, the recorded commit, GitHub returned a null rollup) | present | present | present | present / null / null | one observation |
| `LimitExceeded` (bound ID, exact branch, the recorded commit, `totalCount` above 50) | present | present | present | present / the count / null | one observation |
| `ContextsReported` (bound ID, exact branch, the recorded commit, `totalCount` 0 to 50) | present | present | present | present / the count, equal to `contexts.Count` / an array, possibly empty | one observation |

The names in an observation are the observed names (equal to the recorded ones only ignoring ASCII case), never rewritten as the target's. A null collection means only that no
rollup was exposed or that the bound was exceeded; it never means that no checks exist, and an empty array means only that GitHub returned zero entries (never CI success).
No status or text translates a state: `SUCCESS` is never called passed, approved, ready or green, no aggregate verdict, "latest attempt", requiredness or workflow relation is
computed, and the fifty-first entry is never requested (no second page, cursor or request).

**Scope and binding from the unchanged readers.** The handler reads the source, target and binding with the existing `GitHubObservationSourceReader`,
`GitHubObservationTargetReader` and `GitHubRepositoryIdentityBindingReader` (untracked, bounded) before any process, including binding and event orphan detection when the source
or target is absent: a binding or its event without a coherent target is `Unavailable`, never `NotRecorded` or `BindingNotRecorded`. No target and no binding or event is
`NotRecorded`; a coherent target with neither a binding row nor a binding event is `BindingNotRecorded`; anything else that is not one coherent bound scope is `Unavailable`.

**Admission before any process, no discovery.** The handler requires the same fresh, successful `GitHubCli` `DirectExecutable` snapshot as ADR-0038, ADR-0041, ADR-0042 and
ADR-0044 (no script, a nonblank absolute executable, an observed version and time, fresh under `HostCapabilityReadinessProjector.IsStale`); anything else is `CliUnavailable`
with nothing invoked. It never discovers, refreshes or installs a tool, and depends on no authentication-status, origin, identity, branch or commit-check observation and on no
answer another region displayed: the host reads the binding itself on every request.

**Observe, read again, validate, then compare.** The handler invokes the operation-owned, provider-neutral `IGitHubCheckContextsObserver` exactly once with no transaction and no
write lock held. After it returns it reads the source, target, binding and their canonical events again as one scope and compares the whole with the first reading; any lost,
changed or incoherent fact (including a changed or lost binding or binding event) makes the answer `Unavailable` and strips every scope and remote fact, whatever the observer
said, and this takes precedence over every other outcome. It then validates the entire result independently before any classification: an opaque node ID in the unchanged
`GitHubRepositoryNodeIdPolicy`, an owner and repository inside the unchanged conservative name policy and equal to the recorded scope ordinally ignoring only ASCII case (a
rename, transfer, trimmed or `.git`-stripped name is never adopted), a ref that is absent or has exactly the recorded owned branch name (`GitHubOwnedBranchNamePolicy`,
ordinal) and a 40-character lowercase hexadecimal commit, and a page that exists only beside a ref and passes `GitHubCheckContextsPolicy.IsPage` whatever it will be withheld for
(a foreign ID, another commit or more than 50 entries never excuses a malformed bounded page). A malformed answer is `Unknown`, never a partially admitted identity change,
commit difference or exceeded bound. Only then is the node ID compared with the binding ordinally (a mismatch is `IdentityChanged`) and the commit with the recorded commit
ordinally. The observation is stamped from the host UTC clock after the external work and only for statuses that carry an observation; a reading earlier than
`binding.RecordedAtUtc` is `Unknown` without an observation, never clamped, retried, reobserved or fabricated, and an identity change carries no observation time. Cancellation
and unexpected faults propagate.

**Operation-owned context rules.** `GitHubCheckContextsPolicy` (in the operation folder, shared only by the adapter and the handler; the repository node-ID policy is neither
changed nor promoted) holds the closed kinds `CheckRun` and `StatusContext`; an opaque identifier of 1 to 256 ASCII letters, digits, `_`, `-`, `/`, `+` or `=`, compared
ordinally and never decoded (the entry ID and the check suite ID); the literal check run statuses `COMPLETED`, `IN_PROGRESS`, `PENDING`, `QUEUED`, `REQUESTED`, `WAITING`; the
literal conclusions `ACTION_REQUIRED`, `CANCELLED`, `FAILURE`, `NEUTRAL`, `SKIPPED`, `STALE`, `STARTUP_FAILURE`, `SUCCESS`, `TIMED_OUT` or null; the literal status context states
`ERROR`, `EXPECTED`, `FAILURE`, `PENDING`, `SUCCESS`; and the label contract. A `CheckRun` maps `id`, `name`, `status`, `conclusion` and `checkSuite.id`; a `StatusContext` maps
`id`, `context` and `state` into `id`, `name` and `status`, with a null conclusion and check suite. Status and conclusion are preserved independently: no pairing rule is
invented, no success is inferred from a null conclusion and an unsupported literal makes the whole answer `Unknown`. A page holds exactly the lesser of 50 and the non-negative
`totalCount` entries in provider order; a duplicate entry identifier (ordinal) is refused, while repeated names and shared check suites are preserved and never deduplicated by
name, collapsed into a latest attempt or related to a workflow.

**Label text contract.** Only `CheckRun.name` and `StatusContext.context` carry display text: nonblank, well-formed Unicode of 1 to 256 UTF-8 bytes, with no Unicode Control,
Format, Surrogate, LineSeparator or ParagraphSeparator character and no U+FFFD. JSON string escapes in a label are decoded exactly once and the decoded text is held to that
contract; an invalid escape or surrogate sequence is a refusal of the whole answer, never a replacement and never an unhandled parsing fault, and the decoder's one
`InvalidOperationException` for an invalid surrogate is caught around that single call only. The shared process executor redacts supported secret shapes from the raw captured
text, where an escaped character is invisible to it, so the adapter reapplies the same existing fixed redaction (the unchanged internal `StreamingOutputRedactor`, no copied
pattern, no change to shared execution and no second decode) to the decoded label, holds the result to the contract again and exposes only that text: **an admitted label is safe
post-redaction text, not necessarily the provider's original text**, and a label shaped like any of the five supported secret classes reaches the contract as `[REDACTED]`
(only those fixed shapes: an unknown secret format is not detected). Legitimate escaped Unicode, quotes, backslashes and markup-looking text are preserved as inert text. Every
other string value stays literal: it must contain no backslash at all, as in ADR-0044. A malformed entry refuses the whole answer; a sibling is never silently omitted.

**Port and adapter.** Application owns `IGitHubCheckContextsObserver`, returning only `{NodeId, Owner, Repository, Ref, Page}` or nothing (a page carries `TotalCount` and the
validated first page). Infrastructure owns the GitHub CLI protocol in `GitHubCheckContextsObservationAdapter`, composed on the existing `IProcessExecutionAdapter` inside the host
scratch directory; the ADR-0041, ADR-0042 and ADR-0044 adapters are unchanged and no broad gateway is extracted. It validates the supported names and the bounded owned branch
before invoking, refuses anything else without a process, and runs exactly one invocation with separate literal arguments:

`gh api graphql --hostname github.com --method POST -f query=<constant> -f owner=<stored owner> -f name=<stored repository> -f ref=refs/heads/<recorded branch>`

with the one fixed read-only document
`query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid statusCheckRollup{contexts(first:50){totalCount pageInfo{hasNextPage} nodes{__typename ... on CheckRun{id name status conclusion checkSuite{id}} ... on StatusContext{id context state}}}}}}}}}`.
The HTTP `POST` only transports the query. The stored names and branch travel only as `-f` raw-string variables (never `-F`) and are never interpolated. There is no mutation,
shell, jq, pagination, cache, retry, fallback or second invocation, and nothing about workflow runs, jobs, steps, logs, annotations, pull requests or URLs is selected.

**Process boundary.** The ADR-0038 non-secret OS, profile and temp allowlist and the fixed controls disabling prompts, update notifications, telemetry, color and spinners, run in
the host scratch directory; no ambient token, custom configuration, host, debug, proxy, pager, browser or `PATH` override is forwarded and no credential is extracted; no output
sinks, logs or artifacts; 15 seconds; standard error is discarded unread. Only this new adapter captures at most 128 KiB per stream and 256 KiB combined (the ADR-0041, ADR-0042
and ADR-0044 observers keep their own 8 KiB and 16 KiB, and the executor's global limits and bounds are unchanged), because a bounded collection can legitimately exceed a single
aggregate; a planning sizing probe of 50 maximal identifiers and 256-byte labels escaped as six-byte Unicode sequences measured 119208 bytes, and the tests prove an answer of that
size passes the real executor intact, an answer of exactly 128 KiB is admitted and one byte more, or a standard error over its cap, is truncated and refused. The adapter is a
singleton with one in-flight slot: contention returns nothing immediately (`Unknown`), with no queue, and the slot is released in a `finally` after every outcome. Only the
executable-start failures ADR-0041 already identifies (the `Win32Exception`, and the executor's `ArgumentException` for the executable parameter when the vetted absolute path no
longer exists) become `Unknown`; cancellation and every other fault propagate.

**Strict whole-response admission.** The JSON is admitted whole or not at all, with at most 12 levels of nesting: exactly `data.repository` with exactly `id`, `name`,
`nameWithOwner` and `ref`, with the ADR-0041 name and ID rules and internal name coherence. `ref` must be an explicit `null` or exactly an object with `name`, `prefix` and a
`target`; the `prefix` must be exactly `refs/heads/`, the `name` ordinally equal to the recorded branch and the `target` exactly `{__typename: "Commit", oid: <40 lowercase
hexadecimal characters>, statusCheckRollup}` where `statusCheckRollup` is an explicit `null` or exactly `{contexts}`. `contexts` must be exactly `totalCount`, `pageInfo` and
`nodes`: `totalCount` a plain non-negative Int32 literal (no sign, fraction, exponent, leading zero, string or null), `pageInfo` exactly `{hasNextPage}` with a JSON boolean equal to
whether `totalCount` exceeds 50, and `nodes` an array of exactly the lesser of 50 and `totalCount` entries, each exactly the selected fields of its one known typename (`checkSuite`
exactly `{id}`). Every property name must be written literally: the raw token of each name is inspected (a decoded name proves nothing about its spelling) and an escaped name at any
level refuses the whole answer. Errors, extensions, partial data, missing, extra or duplicate properties at any level, null, partial or unknown entries, a null connection beside a
non-null rollup, unsupported or non-string literals, invalid or foreign names or IDs, a wrong prefix or name (including a tag or another branch returned by the fallback), a null or
non-Commit target, an invalid OID, a timeout, a nonzero exit and an oversized or truncated stream are refused whole as `Unknown`, never `RefNotReturned`, never a match. Literal
whitespace, member order and explicit nulls are admitted as specified. A valid null ref means only that GitHub returned no ref and a valid null rollup only that it returned no
rollup. The cited GraphQL references (the commit and checks objects, `StatusCheckRollup`, `CheckRun`, `StatusContext` and their enumerations) define the nullable rollup, the
mixed context union and the literal values; real installed-CLI, stored authentication and schema compatibility remain unverified.

**Cockpit.** Beside the ADR-0041 identity, ADR-0042 remote branch, ADR-0043 identity binding and ADR-0044 commit checks regions, inside an Available ADR-0040 target's result, a
separate collapsed "GitHub check details" region offers "Show GitHub check details" (which sends nothing), "Check GitHub check details" (then "Recheck") and "Close GitHub check
details". It shows the exact recorded branch, local commit, owner and repository, the stored-authentication and 50-entry notes and the fixed disclosure (this is the bounded
collection GitHub returned for this commit, not required-check approval, complete workflow history, permission or publication authority) before anything is checked, with no
dependency on a sibling region's loaded binding or earlier answer. A reading renders bounded facts as inert text: the bound repository ID, the observed repository facts, the
returned ref and commit, the UTC observation time and, for `ContextsReported`, each entry's kind, safe name, ID, literal status and conclusion (a null conclusion is "not
returned", never "passed") and, for a check run, its check suite ID; zero entries is stated as zero returned and never as success, and `LimitExceeded` shows the total, no entry and
the 50-entry explanation. There is never a link, copy or other action, a translated word, an aggregate verdict or an approval action. A different commit, a ref not returned, an
identity change and any drift expose no entry, count or label. Ownership follows ADR-0041, ADR-0042 and ADR-0044 with the unchanged `useOwnedLifetime` and `useOwnedState`: the
committed source and target and each opening own the region; another source or target, a return to an earlier one, closing and reopening each start collapsed and unchecked; a
late answer of an ended or superseded lifetime is ignored (including one that settles while its successor is still pending); close is serial-guarded; a second activation in
flight, two in one batch and any activation after a close in the same batch send nothing; pending and failure remove any earlier observation. The decoder accepts the whole answer
only for exactly the observed source and target and only in the documented status, nullability, ID, ref, commit, count, entry, label and time combinations (it repeats the host's
rules), and the open region discloses that a name may have been redacted by the host and is not necessarily the provider's original text. There are no automatic reads, polling, reconnect work, retries, browser persistence, key, remount or reset-effect workaround.

## Consequences

- An owner can see, on explicit request and without changing anything, which checks and statuses GitHub returned for the recorded commit of a repository whose identity was
  confirmed, as an input for later failed-check diagnosis and required-check policy.
- The answer is advisory and instantaneous: it can change immediately after it is read, never gates a claim, delivery or publication, and proves no required-check satisfaction,
  complete evidence, workflow history, permission, publication approval or publication performed by DevalCopilot. A later slice must define any requiredness, enumeration beyond
  50 entries, workflow relation or monitoring.
- A collection is shown only for the recorded commit; a moved branch, a replaced repository or any lost binding yields no entry, count or label.
- A fixture double proves the process boundary only; the installed CLI's real formatting of a GraphQL answer, stored-authentication compatibility, GitHub's reliability and its
  actual field values remain unverified, and an older or incompatible binary is `Unknown`. The observer never installs, upgrades or falls back to another command.
- The larger capture caps are local to one adapter. A secret-shaped label (supported shapes only), literal or escaped, reaches the contract as `[REDACTED]`; the cockpit says so with a fixed disclosure, and a label is never claimed to be the provider's original text.
- Only the deterministic owned workspace branch form is supported; another branch name is never sent to a process.
- The contract is additive and changes no Domain, schema, migration, journal, admission, claim, scheduling, recovery, approval, budget or digest decision and adds no dependency,
  remote mutation, persistence, CI monitoring or publication. It does not complete Increment 6.
