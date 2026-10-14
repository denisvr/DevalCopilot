# Slice validation policy

This policy makes local slice validation proportional to changed behavior. It
implements the engineering contract's narrowest-boundary rule; it does not
weaken required CI jobs, security controls, provider fidelity or release and
qualification gates. Explicit acceptance checks of an already dispatched slice
remain applicable unless the planner revises them with evidence. The current
ADR-0043 acceptance matrix remains unchanged.

## Plan validation before implementation

Each planner prompt specifies a small validation matrix: changed boundary, exact
focused tests or file filters, final regression set, broadening trigger and
retained evidence. Selecting every suite is not the default. Tests outside the
matrix require a changed dependency, failure or unresolved concern that makes
them relevant. The executor proposes evidence-based adjustments to the planner
when the actual diff changes that analysis.

During development, compile the affected projects incrementally and run the
smallest tests that prove the current behavior. Red-first runs and mutation
runs use the relevant named regressions, not whole suites. Mutations are
bounded by selected risks, run serially, and restore verified source hashes;
compile failures and no-op mutations are not counted as kills.

After implementation stabilizes, run the selected final matrix once. Reuse
passing results while their covered code, configuration, fixtures, generated
artifacts and dependencies remain unchanged. Documentation-only edits do not
invalidate binary or browser evidence. A correction reruns affected tests first
and then the final checks that its changes could invalidate; it does not reset
every earlier result. Record the reasoning for retained results.

## Choose the smallest sufficient final boundary

| Change | Default final evidence | When to broaden |
|---|---|---|
| Documentation only | Changed-document links, diff and applicable secret checks | Changed commands/contracts also alter executable behavior |
| One pure policy or isolated adapter/parser | Relevant policy/adapter classes and existing negative controls; appropriate build/analyzers | Shared execution, filesystem, environment or policy behavior changes |
| One application use case | Its decision/handler tests and consumers of changed policies | Shared admission, mediator, transaction, budget or lifecycle behavior changes; affected consumers cannot be enumerated reliably |
| One MVC operation | Its complete auth/validation/error/wire matrix plus directly related operation regressions and MVC authorization inventory | Host/authentication/global binding, serialization, error mapping or request pipeline changes; wider composition impact |
| Persistence or migration | Real file-backed SQLite feature tests, upgrade/reopen/constraints/atomicity, and all relevant migration compatibility tests | Shared DbContext conventions, transaction behavior, existing tables or cross-feature constraints change; impact is uncertain |
| One frontend feature | Its decoder/hook/component tests, lint and TypeScript/build checks as applicable | Shared ownership hooks, API bootstrap/client configuration, routing or cockpit-wide state changes |
| One end-to-end workflow | The affected authenticated journey after the binaries are stable | Shared harness, authentication, transport, shell or multiple workflows change; require the canonical chain |

A new additive table does not automatically require every unrelated process,
Git and API test. Migration compatibility and affected real-provider behavior
are still mandatory. Architecture checks are normally retained as a cheap
cross-cutting suite for code changes. Broad shared changes use the relevant full
projects; uncertainty is a reason to broaden, never to guess that impact is
absent. Release/self-hosting/real qualification and mandatory CI retain their
own complete checks. Selective local validation is not release qualification.

## Avoid repeated preparation

Build once before a group of .NET tests and use `--no-build --no-restore` only
against binaries built from the current source. Rebuild after a mutation or
source change before testing it. A normal incremental build is the default;
clean serial `--no-incremental` builds and build-server shutdowns require a
specific reason, such as cache uncertainty, build/generator/toolchain changes
or an explicit final/release gate. Do not delete the generated client routinely.
Regenerate after a successful API build when the public contract changes;
prove reproducibility and inspect drift. Retain client-generation evidence
when its inputs are unchanged, without weakening the CI drift check.

If `npm run build` already runs the same `tsc -b` as `npm run typecheck`, record
that included typecheck instead of running it twice on unchanged input. Strict
e2e TypeScript checks remain separate when those files are outside the product
configuration. Standalone harness checks are relevant when the harness or its
contract changes; do not replay them automatically for every frontend edit.

There must be only one build/generator/mutation writer to a shared output tree
at a time. Never run source mutations while another build or validation reads
those sources or outputs. After a stable build, at most two independent check
commands may run concurrently when their databases, roots, ports, processes,
environment and outputs are demonstrably isolated. Otherwise run serially.
Browser journeys and their shared harness chain remain serialized. Do not turn
on global test parallelism without an independently reviewed isolation proof.

## Design new tests without unnecessary setup

Detailed input and policy permutations belong at the cheapest boundary that
proves them. API tests prove MVC, authorization, transport and representative
use-case wiring; they do not repeat every pure-policy permutation.

Use the least setup that reaches the boundary under test. An unauthorized,
invalid-JSON, oversized-body or pre-dispatch-refusal case usually needs no
completed Git delivery. Do not execute an entire delivery merely to test a
request that cannot reach that delivery's handler. Verify that assumption in
the test, including absence of dispatch and external effects where relevant.

Tests of pinned delivery admission still need coherent real-provider rows.
Use existing feature-owned builders where appropriate and keep at least the
representative production-command/journey proof of the complete lineage. Do
not replace file-backed SQLite with mocked DbSets or EF InMemory, weaken
authentication, skip constraints, remove independent assertions, share mutable
data across tests or bypass safety invariants for speed. Fixture caching,
database cloning or broader test-infrastructure changes require a separately
bounded implementation and isolation evidence; this policy implements none
beyond the one narrow form below.

### Class-owned sealed SQLite snapshot (ADR-0046)

[ADR-0046](../decisions/0046-prepare-delivery-lineage-api-tests-from-a-class-owned-sealed-sqlite-snapshot.md)
approves one form of reuse, for one endpoint class at a time. Extending it to
another class is a separately selected slice. A class that uses it must:

- seed once per execution of that class, through the unchanged production
  lineage, in a class fixture; cache nothing between class executions or
  processes and use no global or assembly-wide state;
- stop every seed host and writer before sealing; seal with SQLite's backup
  facility, never by copying a live database or WAL; never open the sealed source
  for writing again;
- give each fact its own physical database copy, host and dependency container,
  observers and counters, clock, clients and writable roots, copying scalar
  identities and capturing any collection the seed hands out (a read-only
  interface is not immutability) as an immutable copy, never a mutable seed
  object; refresh time-dependent eligibility
  (such as CLI snapshot evidence) per fact instead of caching it;
- keep the shared Git scene read-only: no supervisor, provider or Git writer runs
  against it, and its fingerprint and the sealed files' hashes are verified when
  the class ends;
- keep every fact, its name and every independent assertion, plus a
  representative proof that the seed executed the production lineage;
- ship named isolation regressions (distinct databases and writable resources,
  change containment, a clean later clone, independent observers and clocks,
  disposal order) and kill compiling shared-database and shared-observer mutants
  with them; and
- report a before and after wall time of the unchanged filter, including seed,
  backup, host startup and disposal, with setup and observation cost reported
  separately.

Facts that must mutate the scene or run a supervisor or provider keep the
per-test lineage.

## Measure and retain evidence

Record the exact command/filter, source revision, result and elapsed time for
each validation phase. Distinguish focused, full, retained and not-run checks.
Keep complete logs and named results for every failure, including failed
development attempts; report aborted runs and active tests without inventing a
cause. No blind rerun, log deletion, silent skip or flaky-test label is a speed
optimization. Preserve the unexplained Api 2067/2068 failure and existing limits.

Target a focused edit/test cycle of about one minute or less when the boundary
permits it; this is a profiling target, not a timeout or permission to skip
tests. When a focused class exceeds that target, report setup versus execution
cost and propose a bounded optimization. Do not promise a speedup before an
actual before/after measurement.

The reviewer independently reruns the smallest set that tests the material
risk and inspects the executor's complete final evidence. It does not duplicate
the entire final matrix by routine. Broaden independent checks for discrepancies,
unexplained failures or unresolved concerns. Planning/review/publication rules,
real-provider restrictions, retained roots, ledger and artifact protections are
unchanged.
