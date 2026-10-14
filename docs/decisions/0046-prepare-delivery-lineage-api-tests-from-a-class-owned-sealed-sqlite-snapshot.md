# ADR-0046: Prepare delivery-lineage API tests from a class-owned sealed SQLite snapshot

Status: Accepted

## Context

The check-context endpoint class (`GitHubCheckContextsObservationEndpointTests`, [ADR-0045](0045-inspect-the-check-contexts-of-a-bound-recorded-local-commit-on-explicit-request.md))
has 20 independently selectable facts. Each began by preparing its own real delivery lineage through the production host: a real Git scene and worktree, the Codex, Claude and
human lineage, the explicit local commit, the protected observation-target POST and the identity-binding POST. That setup was the dominant cost: the class took 120.2 s
(20 passed), and an earlier profile attributed 84.5 of 94.2 s to lineage preparation, while the observation each fact asserts takes milliseconds. The owner asked for shorter test
duration. The [slice validation policy](../roadmap/validation-policy.md) forbids replacing real file-backed SQLite with mocked DbSets or EF InMemory, weakening authentication,
skipping constraints, removing independent assertions or sharing mutable data across tests for speed, and it left fixture caching and database cloning to a separately bounded
implementation with isolation evidence. This ADR is that bounded implementation for one class. It is test infrastructure only: no production, frontend, public-contract or
client change, no dependency, and no product, qualification or release gate is closed.

## Decision

**One run-local, class-owned sealed snapshot, used only by the endpoint class.** `GitHubCheckContextsSealedEvidence` is an xUnit class fixture of that class alone, created when
the class starts and disposed when it ends. Nothing is cached between class executions, between processes or in a global location, and no other test class, collection or
assembly-wide state uses it. Migrating another class needs its own selected slice, its own isolation evidence and its own measurement; the existing per-test lineage helpers stay
unchanged and remain the default.

**The seed is the unchanged production lineage.** It runs the existing delivery helpers: a real Git scene, the production command chain and supervisor-driven explicit local
commit, the protected observation-target POST and the protected identity-binding POST, through production hosts over file-backed SQLite, real MVC authentication and real Git, as
every other lineage test does. Every row and canonical event is written by the host's own handlers; nothing is fabricated or edited and no persisted path is
rewritten. The seed keeps two images of that one lineage: the state with the target recorded and the state after the binding POST. The first serves the single fact that
requires no binding; every other fact uses the second.

**Sealing.** Every seed host is stopped before an image is taken, and the sealing step asserts that the stopped database has no WAL, shared-memory or journal sidecar. An image is
written with SQLite's consistent backup facility (never a copy of a live database or WAL), marked read-only, hashed and never opened for writing again. It is opened read-only
only as the source of backups. The identities the lineage recorded (project, run, workspace, checkpoint, operation, commit and target identifiers and the ordered execution
membership) are captured as an immutable snapshot: the scalars by value, and the membership, which the seed returns as a mutable list behind a read-only interface, copied into an
immutable array that preserves order, absence and emptiness. Every clone receives that immutable membership. No mutable collection, seed host, observer, counter or clock object
is shared with the seed or another clone, and the shared lineage identity type is unchanged.

**Each fact receives its own clone.** A fact gets its own physical SQLite file made from the sealed image by the backup facility, its own fresh production host and dependency
container, its own scripted observers and counters, its own clock, its own clients, and its own writable artifact, workspace and local-commit storage roots. Current CLI snapshot
evidence is recorded again in each clone through the existing domain method; eligibility is never cached. The clone is owned by the fact: disposing it stops its host before it
removes the files it created.

**The sealed scene is read-only evidence owned by the class.** The Git scene the lineage used stays on disk, shared by every clone's persisted paths, and is read-only to them. An
observation host runs no supervisor, and every agent-provider adapter, every other GitHub observer, the local-commit preparer, the mutating members of the local-commit
repository and worktree creation refuse any call and record it; the reads the production startup recovery and the observation legitimately make still reach the real adapters. The
scene and the sealed files are hashed when sealed and must be byte-for-byte unchanged when the class ends, and the class fails otherwise.

**Seams.** The shared test base gains an optional constructor that borrows a scene, a database file and a writable root from an owner, and the local-commit test host gains an
optional writable root; both default to the existing behavior, so every other caller is unchanged. The CLI-snapshot recorder and the database fingerprint gain static overloads for
an owner that holds no test instance. No production code changes.

**Required isolation evidence.** Named regressions prove that two simultaneously live clones have distinct databases and writable resources; that capability, binding and event
changes in one clone leave the other clone and the sealed source unchanged; that mutating the capture input or attempting to mutate a clone's execution membership alters no
sealed identity, other clone or later clone and keeps the original order and scalars; that a later clone starts clean; that observer answers, counters and clock state are independent; that
disposing one clone preserves the other and the sealed source until their owners finish; that the seed executed the production delivery and recorded coherent target and binding
evidence; and that an observation host cannot run a supervisor, a provider or a Git writer against the sealed scene. Compiling mutants (a shared clone database, a shared mutable observer and counter,
and the original mutable membership alias) must each be killed by named isolation regressions. The 20 facts, their names and every independent assertion remain, including the real-adapter safe
projection test.

## Consequences

- The class's setup moved from a full lineage per fact to one lineage per class execution plus a backup and a host start per fact. The measured before and after (wall time,
  setup versus observation) are recorded in the [current-work handoff](../roadmap/current-work.md); the gain is neither promised nor extended to any other class by this ADR.
- Only host startup remains per fact. A fact that must mutate the Git scene, run a supervisor or provider, or needs a different lineage cannot use the sealed scene and keeps the
  per-test lineage.
- Clones of one image share run, project and operation identifiers across separate databases. That is intended and is safe only because no clone shares a database, a writable
  root or a mutable object with another; the isolation regressions are part of the contract and must be kept when the helpers change.
- A future change to host startup that writes to the Git scene, or to a seeded schema, is detected by the scene and sealed-file fingerprints rather than by a silent
  cross-fact dependency.
- A sealed image is a snapshot of the schema and rows of the build that produced it, so it is rebuilt by every class execution; it is never a cached artifact and cannot go stale.
- No security, authentication, authorization, provider-fidelity, mandatory CI, real-provider, qualification or release control changes. Qualification remains blocked and no
  allowance is used.
