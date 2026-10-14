# ADR-0047: Inspect the output of one GitHub check run of a bound recorded local commit on explicit request

Status: Accepted

## Context

[ADR-0045](0045-inspect-the-check-contexts-of-a-bound-recorded-local-commit-on-explicit-request.md) shows which checks and statuses GitHub returned for the recorded commit, as
identifiers and literal states with no diagnostic text. A later failed-check diagnosis needs the text a check run itself carries. This ADR closes that one input gap: on explicit
request, show the provider-supplied title, summary and text of one CheckRun that GitHub associates with the recorded local commit of the bound repository, and nothing else. It
inspects and interprets nothing: it is what GitHub returned for one check run at one moment, shown as inert, untrusted, post-redaction plain text. It is not a diagnosis, a log, a
workflow, job or step history, a required-check or branch-protection decision, a permission, a publication approval or a publication performed by DevalCopilot, and it is never
persisted, retained as an artifact, sent to an Agent or acted on. Required-check policy, workflow enumeration, durable output, Agent diagnosis and correction routing, rerun, scoped
publication, push, pull request, reconciliation, automatic monitoring, the remaining Increment 4 contracts and qualification, and Increment 5 coordination stay separately gated,
and real Agent qualification is unaffected (Blocked/TargetsNotReal, both allowances unused). The ADR-0040 through ADR-0046 contracts, readers, ports, adapters, statuses, wire answers
and fixtures are unchanged except for the one narrow, named test-composition extension below.

GitHub's public schema documents the check-run and check-suite fields used here and `node(id:)` as a global lookup (checked on 2026-10-14:
<https://docs.github.com/en/graphql/reference/checks> and <https://docs.github.com/en/graphql/reference/meta#node>). That is documentation evidence, not installed-CLI, account or
schema qualification. A single combined node query would request the text before proving that the check belongs to the bound repository and recorded commit, so the host runs two
fixed phases: association metadata first, the output only after the host admitted the association.

## Decision

**One protected, bodyless, read-only operation.** `GET /api/runs/{runId}/github-check-run-output-observation?checkRunId=<id>` (`GetGitHubCheckRunOutputObservationQuery` through
`IApplicationMediator`, one MVC endpoint under the protected runs route, an operation-owned port, result and API-owned response types for the whole transport graph, the generated
client regenerated reproducibly and additively and never edited by hand) returns `{runId, checkRunId, status, source, target, binding, observation}`. The `source`, `target` and
`binding` are the existing ADR-0044/ADR-0045 fact shapes (the same API-owned response types), mapped from Application views. The only client input is `checkRunId`: exactly one query
key, spelled exactly `checkRunId`, with exactly one nonempty value of 1 to 256 ASCII letters, digits, `_`, `-`, `/`, `+` or `=`, never trimmed, normalized or decoded. A missing,
duplicate (in any letter case), extra or invalid query is refused before dispatch with the fixed, non-echoing 400 `github_check_run_output_observation.invalid_request`; a request
body (a positive declared length, any transfer encoding, including an empty chunked one, or a readable byte) is refused first with the fixed 400
`github_check_run_output_observation.body_not_allowed`, which never echoes it (a request that has both gets the body refusal); an unknown run is the fixed 404
`github_check_run_output_observation.not_found`. No client repository, SHA, branch, suite, query document, executable, credential, cursor or text is accepted. The operation writes no
row, event or lock, holds no transaction, persists and caches nothing and changes no schema; a database fault keeps the normal pipeline.

**Eleven closed statuses and nullability.** `source`, `target`, `binding` and `observation` are nullable and each status fixes exactly which are present.

| Status | source, target | binding | observation | `ref` / `checkRun` | Output invoked |
|---|---|---|---|---|---|
| `NotRecorded` (no target and no binding or event) | absent | absent | absent | n/a | nothing |
| `Unavailable` (any source, target, binding or event is incoherent or orphaned, or the scope or binding was lost or changed during the observation) | absent | absent | absent | n/a | no |
| `BindingNotRecorded` (coherent source and target; genuinely no binding row and no event) | present | absent | absent | n/a | nothing |
| `CliUnavailable` (the CLI snapshot was unusable before the metadata call, or unusable or on another resolved path before the output call) | present | present | absent | n/a | no second call |
| `Unknown` (every unusable outcome, never says why) | present | present | absent | n/a | not after a refusal |
| `IdentityChanged` (a whole valid answer with another opaque repository ID; the other ID is never returned and no time is stamped) | present | present | absent | n/a | no |
| `RefNotReturned` (valid null ref) | present | present | present | null / null | no |
| `CommitDifferent` (bound ID, exact branch, another valid commit) | present | present | present | present / null | no |
| `CheckRunUnavailable` (bound ID, exact branch, recorded commit, but the node is missing, not a check run, in a foreign repository, in a suite of another commit, or its association changed between the two phases) | present | present | present | present / null | no (or withheld) |
| `OutputNotReturned` (association admitted in both phases; title, summary and text all null) | present | present | present | present / present, `output` null | yes |
| `OutputReported` (association admitted in both phases; at least one nonnull field) | present | present | present | present / present with `output {title, summary, text}` | yes |

An `observation` is `{repositoryNodeId, owner, repository, observedAtUtc, ref, checkRun}` with `ref` `{branchName, commitSha}` and `checkRun`
`{id, checkSuiteId, commitSha, name, status, conclusion, output}`; `output` is `{title, summary, text}`, each nullable. The check run `id` equals the request, its `commitSha`
equals the recorded commit (and the returned ref's), `name` is the observed safe name, `status` is one of GitHub's six literals and `conclusion` one of its nine literals or null; they are
validated independently with no pairing rule, and no green verdict, approval, requiredness or "latest attempt" is computed or translated. The names in an observation are the observed names (equal to the
recorded ones only ignoring ASCII case), never rewritten as the target's. A null `title`, `summary` or `text` means only that GitHub returned no value; an empty or
whitespace-only string is returned text and stays distinct from null; an `OutputNotReturned` output (all three null) is `null`, never an object of nulls.

**Scope and binding from the unchanged readers, compared three times.** The handler reads the source, target and binding with the unchanged `GitHubObservationSourceReader`,
`GitHubObservationTargetReader` and `GitHubRepositoryIdentityBindingReader` (untracked, bounded) before any process, again after the metadata call and again after the output call.
Each later reading is compared with the first by complete value (the binding's values included, so a coherent replacement of the binding and its event is drift); any lost, changed or
incoherent fact makes the answer `Unavailable`, strips every scope and remote fact whatever the observer said, and takes precedence over every other outcome. No transaction, lock or
write is opened.

**Admission before and between the phases, no discovery.** The handler requires the same fresh, successful `GitHubCli` `DirectExecutable` snapshot as ADR-0038, ADR-0041, ADR-0042,
ADR-0044 and ADR-0045 before the metadata call, and shows `CliUnavailable` with nothing invoked otherwise. Between the phases it reads the snapshot again and requires another usable
snapshot with the same resolved executable path (ordinal); otherwise `CliUnavailable` and no second call. It never discovers, refreshes or installs a tool. The host clock must not
precede `binding.RecordedAtUtc` before the output call (otherwise `Unknown`, no second call, never clamped). Caller cancellation is checked before each external call and propagates
with unexpected faults.

**The two phases and the independent validation.** Application owns `IGitHubCheckRunOutputObserver` with narrow typed `ObserveMetadataAsync` and `ObserveOutputAsync` methods, each
returning a provider-neutral probe `{NodeId, Owner, Repository, Ref, Node}` or nothing. The adapter is stateless between phases and never decides whether the second may run; the handler
owns that decision, never caches another request's metadata and validates each complete probe independently, including output that will be withheld, before any classification:

- Root repository: the unchanged ID, owner and repository policies (names equal to the recorded scope ordinally ignoring only ASCII case; a rename, transfer, trimmed or
  `.git`-stripped name is never adopted); a ref that is absent or has exactly the recorded owned branch (ordinal) and a 40-character lowercase commit.
- Node: absent; a check run whose returned ID equals the requested ID ordinally, whose check-run repository equals its check-suite repository (a disagreement is internal
  incoherence, `Unknown`) and whose identifiers and suite commit are well formed (with its detail present exactly in the output phase); or another GraphQL type name with nothing else.
- Classification then follows a fixed order: the root identity (a different opaque ID is `IdentityChanged`), the ref (`RefNotReturned`), the commit (`CommitDifferent`), and only then
  the association: both repository IDs equal to the bound ID and the suite commit equal to the recorded commit, otherwise `CheckRunUnavailable`. A coherent foreign association or a
  suite of another commit never permits an output call. In the output phase the complete identity (check-run ID and repository, suite ID and repository, commit) must equal the first
  admitted identity; a coherent change is `CheckRunUnavailable` and withholds the check and the output, a wrong echoed ID or internal incoherence is `Unknown`.
- The observation is stamped from the host UTC clock after the last external call (the output call, or the metadata call for a status that stops there) and only for statuses that carry an
  observation; a time earlier than `binding.RecordedAtUtc` is `Unknown` without an observation.

**Operation-owned rules.** `GitHubCheckRunOutputPolicy` (in the operation folder, shared only by the adapter, the handler and the endpoint's identifier check; the repository node-ID policy
is neither changed nor promoted and the ADR-0045 policy is neither reused nor changed) holds the identifier alphabet, the check-name label contract, GitHub's six check-run
status literals and nine conclusion literals, the output text contract with its byte bounds, the association validity and the node shape. Names follow the ADR-0045 safe-label contract
(nonblank, well-formed, 1 to 256 UTF-8 bytes, no Control, Format, Surrogate, LineSeparator or ParagraphSeparator character, no U+FFFD).

**Output text contract.** Only the check name and the title, summary and text receive JSON escapes, decoded exactly once (an invalid surrogate sequence is a refusal of the whole
answer, never a replacement and never an unhandled fault). Null, empty, whitespace-only, tab, line feed and carriage return are preserved exactly. The text must be well-formed
Unicode with no Control character other than tab, line feed and carriage return, no Format, Surrogate, LineSeparator or ParagraphSeparator character and no U+FFFD. UTF-8 bounds:
title 256 bytes, summary 8 KiB, text 16 KiB and the trio 24 KiB together; nothing is ever truncated, so an invalid or oversized value refuses the whole answer. The bounds and characters
are checked after decoding and again after one application of the unchanged internal `StreamingOutputRedactor` to each decoded value, because the shared executor redacts only the raw
captured text, where an escaped character is invisible to it. No pattern is copied, nothing is decoded a second time, normalized, trimmed, linkified or rendered: **an admitted value is
safe post-redaction text, not necessarily the provider's original text**, and only the five supported secret shapes are detected (an unknown secret format is not). The browser and
.NET Unicode category tables may differ for newly assigned code points; the host's decision is the authoritative one and the decoder repeats it conservatively.

**Adapter.** `GitHubCheckRunOutputObservationAdapter` is composed on the existing `IProcessExecutionAdapter` inside the host scratch directory, with the ADR-0041 environment, scratch and
privacy boundaries, standard error discarded unread and no raw provider output logged or retained. It runs one invocation per phase and at most two per request, sequentially, with no
retry: 15 seconds each, at most 30 seconds of external execution. A singleton non-queued slot is acquired per process invocation and released in a `finally`: contention returns nothing
immediately (`Unknown`), and phases of different requests may interleave because nothing is shared between them. Each invocation is

`gh api graphql --hostname github.com --method POST -f query=<constant> -f owner=<stored owner> -f name=<stored repository> -f ref=refs/heads/<recorded branch> -f id=<selected check run ID>`

with one of two fixed read-only documents (the stored names, the bounded owned branch and the identifier travel only as `-f` raw-string variables, never `-F`, never interpolated):

- Metadata: `query($owner:String!,$name:String!,$ref:String!,$id:ID!){repository(owner:$owner,name:$name){id name nameWithOwner ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid}}}} node(id:$id){__typename ... on CheckRun{id repository{id} checkSuite{id repository{id} commit{oid}}}}}`
- Output: `query($owner:String!,$name:String!,$ref:String!,$id:ID!){repository(owner:$owner,name:$name){id name nameWithOwner ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid}}}} node(id:$id){__typename ... on CheckRun{id repository{id} checkSuite{id repository{id} commit{oid}} name status conclusion title summary text}}}`

The metadata phase captures at most 8 KiB per stream and 16 KiB combined; only the output phase captures 256 KiB per stream and 512 KiB combined (legal worst case: 24 KiB of decoded
text with every character escaped as a six-byte sequence plus a 256-character name and the envelope is about 150 KiB; the tests prove an answer of that size passes the real executor
intact, an answer of exactly 256 KiB is admitted, one byte more or a standard error over its cap is truncated and refused, and the metadata phase has the same exact-cap behavior at
8 KiB). The older observers keep their own caps, the executor's global limits are unchanged and the JSON depth bound is 8. The JSON is admitted whole or not at all: exactly
`data.repository` and `data.node`; the exact ADR-0041 repository members; a ref that is an explicit `null` or exactly `name`, `prefix` (`refs/heads/`), the ordinally equal recorded branch
and a Commit target with a lowercase 40-hex `oid`; and a node that is an explicit `null`, exactly the selected members of a check run (the first phase without any detail), or exactly one literal GraphQL type name
(`[A-Za-z_][A-Za-z0-9_]*`, at most 64, not `CheckRun`) and no other member. Every property name must be spelled literally (the raw token is inspected); errors, extensions, partial data,
missing, extra or duplicate members, escaped literal strings, unsupported literals, a fallback ref, a nonzero exit, a timeout, truncation and an oversized or malformed value refuse
the whole answer as `Unknown`. Only the executable-start failures ADR-0041 identifies become `Unknown`; cancellation and every other fault propagate.

**Cockpit.** Inside a `ContextsReported` reading of the ADR-0045 "GitHub check details" region, each `CheckRun` entry (a `StatusContext` entry never) offers its own collapsed region
"GitHub check output" with "Show GitHub check output" (which sends nothing), "Check GitHub check output" (then "Recheck") and "Close GitHub check output". It shows the exact recorded
branch, local commit, owner, repository and check run ID, the stored-authentication and two-step note and the fixed disclosure (untrusted text supplied by GitHub for one check run at one moment,
plain text only, not an instruction, a diagnosis, required-check approval, permission or publication authority, and never sent to an Agent) and the redaction notice (five supported shapes
only; not necessarily the provider's original text) before anything is checked. A preceding contexts response is not host authorization: the host answers from its own two-phase proof every
time. Ownership is the committed project, run, source and target, the selected check run ID, the parent contexts reading and each opening (the unchanged `useOwnedLifetime` and
`useOwnedState`); a parent recheck, close or scope change ends the child, another check or scope, a return to an earlier one (A-B-A), closing and reopening each start collapsed and
unchecked, a late completion of an ended or superseded lifetime is ignored and never clears the pending marker of its successor, a second activation in flight, two in one batch and any
activation after a close in the same batch send nothing, and pending and failure remove any earlier answer. A reading renders bounded facts as inert text and each output value as
preformatted plain text (never Markdown or HTML, never a link, copy, download, Agent, rerun or publication action); a null value, an empty string and a whitespace-only string are stated
with fixed text and are never conflated; a null conclusion reads "not returned". The decoder accepts the whole answer only for exactly the observed source, target and check run and only in
the documented status, nullability, ID, ref, commit, name, literal, text, bound and time combinations (it repeats the host's rules). There are no automatic reads, polling, effect or
timer work, retries, browser persistence, key, remount or reset-effect workaround.

**Test composition (the one narrow extension of ADR-0046).** The new endpoint test class composes the shipped ADR-0046 sealed producer and backup mechanism through feature-owned setup
instead of preparing a lineage per fact. It owns its own `IClassFixture` instance (a fresh seed of the unchanged production lineage per class execution), and each fact receives its own
clone made by the producer's backup facility, its own fresh production host and dependency container, its own scripted observer and counters, its own clock, immutable identity copies, writable roots,
source guards and disposal; no fixture instance, database, root or mutable object is shared with another class, fact or run. The new observer is added to the existing `SealedSceneGuard`
refusal inventory (so an observation host over the sealed scene refuses it by default), and only this operation's controlled test composition overrides its own observer after the guard.
The feature-owned composition also refuses, with the same refusing mechanism and named refusals in the clone's own queue, the two GitHub observers the shared guard leaves to
each class (repository identity and check contexts), and registers no permissive identity double: the output observer is the only GitHub observer the clone host enables.
Behavioral regressions resolve both refused ports from the effective clone host and prove the named refusal, no process start (a fail-on-use process executor composed into
those hosts refuses and counts every non-Git start, while the host's own read-only Git inspection passes through as on every other clone), the output observer still answering,
and the sealed images and scene unchanged.
No generic producer or clone refactor, no migration of another class and no change to ADR-0046's classes were made; the Architecture inventory tests of three earlier
GitHub operations were extended only to name this operation's adapter, handler and policy among the sources they enumerate (the unchanged production code of those operations is not touched). This class has no measured old baseline, so only its actual setup
and wall time and its isolation evidence are reported in the [current-work handoff](../roadmap/current-work.md); no speedup is claimed. A representative per-test production lineage fact and a
production-host safe-projection test through the real adapter and a scripted process engine are kept.

## Consequences

- An owner can see, on explicit request and without changing anything, the title, summary and text GitHub holds for one check run of the recorded commit of a repository whose
  identity was confirmed, as an input for later failed-check diagnosis; the text is data for a person to read, never an instruction.
- The answer is advisory and instantaneous: it can change immediately after it is read, never gates a claim, delivery or publication, and proves no required-check satisfaction, complete
  evidence, workflow history, permission, publication approval or publication performed by DevalCopilot. A later slice must define any diagnosis, durable retention, enumeration or requiredness.
- Output is shown only for a check run proved, in two phases and against the immutable binding, to belong to the bound repository and the recorded commit; a moved branch, a replaced
  repository, a foreign or re-associated check run or any lost binding yields no check and no text.
- A fixture double proves the process boundary only; the installed CLI's real formatting of a GraphQL answer, stored-authentication compatibility, GitHub's reliability and its actual field
  values remain unverified, and an older or incompatible binary is `Unknown`. Secrets of an unsupported shape in provider text are not detected, and browser and .NET Unicode tables may differ.
- The native journey keeps its fixed remote commit and therefore stays on the `CommitDifferent` path: it proves one explicit authenticated request costs exactly one new HTTP request and one
  metadata invocation, no output invocation, text or write, and no automatic work after a reload or close. The matching-commit inspection is proved at the process, production-host and UI
  boundaries; no recorded-SHA discovery was invented.
- Only the deterministic owned workspace branch form is supported; another branch name is never sent to a process. The contract is additive and changes no Domain, schema, migration, journal,
  admission, claim, scheduling, recovery, approval, budget or digest decision and adds no dependency, remote mutation, persistence, CI monitoring or publication. It does not complete
  Increment 6.
