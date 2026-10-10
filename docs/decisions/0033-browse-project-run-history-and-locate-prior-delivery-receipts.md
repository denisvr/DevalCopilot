# ADR-0033: Browse project run history and locate prior delivery receipts

Status: Accepted

## Context

[ADR-0014](0014-add-manual-agent-run-intake-with-durable-execution-mode-isolation.md) and
[ADR-0031](0031-abandon-an-inactive-manual-run-through-an-explicit-human-decision.md) let a project record a subsequent objective once
its earlier runs are terminal, and [ADR-0003](0003-use-a-durable-sqlite-event-journal.md) keeps every run's rows durable.
[ADR-0032](0032-show-a-recorded-local-delivery-receipt-from-pinned-evidence.md) reads the receipt of a completed local commit by its
run identity. The project summary nevertheless selects one most-relevant run per project, so the cockpit can open only that run. After
another objective replaces it, an owner has no normal route to an earlier objective or to the receipt of an earlier delivery, although
every fact is still recorded and immutable. This is a read problem, not a replay or timeline problem.

## Decision

**One protected, read-only page of a project's runs.** `GET /api/projects/{projectId}/run-history` (`GetProjectRunHistoryQuery` through
`IApplicationMediator`, one MVC endpoint, bodyless, protected like every project operation) answers one page of that project's runs
in descending `ExecutionNumber`. An unknown project is a safe 404 (`projects.not_found`); an existing project with no run is a
successful empty page. The transport types are API-owned records and the generated TypeScript client is regenerated reproducibly from
the OpenAPI document.

**Cursor paging, never clamping.** `beforeExecutionNumber` is an exclusive cursor, positive when supplied; `limit` defaults to 10 and
accepts 1 to 20. A value outside those bounds is a 400 and is never clamped, defaulted or ignored. The public parameters stay numeric
(`int?`), but a feature-local model binder binds an absent or empty value as absent and a value that is not a whole 32-bit integer
(text, a fraction, an overflow) as a value no validator accepts, without adding the framework's own model-state error, so every refusal
is the shared Problem Details contract produced through the mediator's validation: `ApiProblemDetails` with its `errors` array
(`validation.invalid_limit` or `validation.invalid_cursor`, a fixed detail, a JSON pointer such as `#/limit`) and the trace identifier,
and never the rejected value. The operation declares `ApiProblemDetails` for 400 and 404 so the OpenAPI document and the generated
client carry the typed refusal; no global MVC behavior and no other route changes. The page is read through the existing unique
`(ProjectId, ExecutionNumber)` index with one extra row, so `HasMore` is exact without a count; `NextBeforeExecutionNumber` is the last
admitted execution number and is present only when older runs remain. There is no offset, timestamp ordering, total-count scan or
automatic draining of pages. A newer run created between two reads can neither repeat nor skip an older one.

**What an entry contains.** Only the project and run identities, the execution number, the recorded objective, the lifecycle and stage
(a known name, or the fixed value `Unrecognized`), the exact stored execution mode (`Legacy`, `Simulated`, `ManualAgent` or
`Unrecognized`, through the one existing exact storage reading), the creation and last-advance UTC times, and an optional receipt
source. Entries are minimal untracked projections of the `runs` row, never the `Run` aggregate. The database classifies the lifecycle
and stage columns, so a stored value this version does not recognize keeps its row, is disclosed only as `Unrecognized`, is never
exposed, never becomes a known state and never prevents a healthy neighboring row from loading. No mapping, stored value or schema
changes.

**The receipt source only locates a receipt.** For a run recorded as `Completed` at the `Completed` stage that owns exactly one
local-commit operation recorded as `Completed` (classified by the database) with a completion time, the entry carries that operation's
own run, operation, commit and checkpoint identities and the checkpoint number, provided the operation belongs to the same project and
run and those fields are structurally valid (a 40-character lowercase object id, a nonempty checkpoint identity and a positive number).
It is resolved only by the run's own operation, never by the latest operation, the current checkpoint, current eligibility or another
run. It does not certify that the receipt is available or that anything was approved: the existing receipt endpoint remains the only
authority and may still answer `Unavailable`. When no coherent completed source can be identified the source is `null`, which says only
that no completed delivery source is available in this view; it does not show that no operation exists, that a delivery failed or that
one succeeded. The query reconstructs no receipt and no digest.

**Read-only by construction.** The query reads persisted rows untracked, writes nothing and invokes no Git, filesystem, provider,
process, readiness refresh, lease, recovery or mutation action. It needs no migration or persistence change, and changes no ADR-0029
authority, reservation, recovery or approval decision, no ADR-0030 approval rule and no ADR-0032 digest or reconstruction.

**Cockpit.** The selected project shows a **Run history** region, collapsed by default. Opening it requests one page; older pages and a
reload of the first page are explicit actions, and nothing is polled, timed, retried automatically or stored in the browser. Selecting a
row shows its recorded metadata beside a fixed note that it is a snapshot, not live progress, not the current workspace or current
verification and not a remote publication, and, only when the host located a source, the existing **Local delivery receipt** component,
hook and normalizer for exactly that source, with ADR-0032's exact source checks, read-once behavior, unavailable and error states and
explicit retry unchanged. A selected run without a source shows the fixed disclosure above and requests no receipt. The region mounts no
live cockpit, eligibility, configuration or mutation hook and offers no mutation control; the live cockpit, current project and run
selection and intake are unchanged.

**Ownership.** The reads and selections belong to committed lifetimes built on the shared owned-lifetime hooks: the committed project
(collapsed for every project; another project, a return to an earlier one or unmounting ends it), each open or reload of it (a fresh
first-page read with no rows and no selection; closing and reopening end it too) and the selected row (whose receipt read belongs to its
own source lifetime). The selected row's actions, including closing the detail, belong to its committed selection: choosing another
row, returning to an earlier one (A to B to A) or choosing it again after a close each begin a new selection, so the close callback
of an earlier selection can never close the one that replaced it. A replacement derives its own empty first frame in the same render, an older or replaced read can neither
overwrite nor settle a newer one, and the callbacks of an ended lifetime start nothing. A small normalizer accepts a page only whole and
only when it is coherent for exactly its request and the rows already retained: the answered project, no more entries than requested,
unique run identities and execution numbers that are safe positive integers in strictly descending order and older than the cursor and
the last retained row, only known (or fixed `Unrecognized`) disclosures, valid instants, a source only for a `Completed` run that names
that run with structurally valid fields, and a continuation that is a full page whose next cursor is its last number (or none).
Anything else is an explicit invalid-page failure, never a plausible partial page; a failed older page keeps only the valid rows of its
own lifetime and retries the same cursor. All values are rendered as text.

## Consequences

- An owner can browse a project's earlier objectives and inspect an older recorded delivery after another objective replaced it, also
  after a reload or restart, without any mutation or live work.
- The history is as good as the recorded rows: an unrecognized stored value is disclosed, not repaired, and a source that cannot be
  coherently identified is simply absent.
- The contract is additive. It is not a replay, a timeline, an artifact viewer, an export, a retention or cleanup mechanism, a remote
  publication, an autonomous coordinator or evidence of provider reliability, and it does not complete Increment 4 or 7.
- Full event replay, receipts for failed, interrupted or ambiguous operations, other historical views and generic history
  infrastructure remain separate decisions.
