# ADR-0072: Adopt one Windows execution profile

Status: Accepted (verification profile extended by
[ADR-0073](0073-add-an-owned-nuget-base-to-the-windows-verification-profile.md)
(`windows-verification-v2`) and then by
[ADR-0074](0074-disable-sdk-first-run-persistent-side-effects-in-the-windows-verification-profile.md):
new executions record `windows-verification-v3`; the version-1 decision below
stays as recorded)

## Context

Planner-selected slice WIN-EXEC-PROFILE-01 (planner handoff, findings REV-02,
REV-03, REV-07, F-02, F-03, F-04, F-06, F-07 and F-11; it improves the
diagnosis of REV-01/F-01 without claiming their historical cause) groups one
execution boundary that previously failed open in several places:

- The three Claude role adapters read the narrative `result` text of an
  undocumented envelope shape and collapsed every failure into one opaque
  provider failure, so a person could not tell an authentication report from a
  malformed envelope.
- The shared child-process adapter awaited stdout and stderr to end of file
  with no bound. A descendant that inherited a pipe could hold an execution open
  indefinitely after its invoked process had exited, and exit code zero was the
  only success signal even when output had not been finalized.
- Verification ran with an empty environment, so ordinary toolchains could not
  locate their system prerequisites, and nothing recorded which environment an
  execution used.
- Host startup ignored every recovery `Result` and payload, and the desktop
  shell could not tell a failed startup from a host that never answered.

This decision is the planner-selected architecture; the executor implemented it
for review. It changes no approval, commit, publication or ownership-release
authority.

## Decision

### Versioned Claude contracts and one shared envelope reader

New attempts are claimed as `claude-critical-review-v2`,
`claude-implementation-v3` and `claude-review-correction-v3`. Versions 2 and 3
of the mutation paths carry exactly the same turn-limit and direct-guidance
options (ADR-0015, ADR-0028); the CriticalReviewer keeps its one turn. Every
other argument, the tool and permission restrictions, the authentication
environment, stdin delivery, sessions and model/effort requests are unchanged,
except that `--prompt-suggestions false` is no longer passed. Each adapter
dispatches only its current version: an attempt claimed under a historical
version fails closed before any process starts and is never upgraded or
redispatched. Historical versions, manifests and evidence stay as recorded and
classifiable. A historical v1 critical review that ended as invalid structured
output may still be the source of one repair (ADR-0004 format repair); the
repair runs under the current contract.

One pure, internal reader classifies the documented single result envelope of
`--print --output-format json --json-schema` (code.claude.com "Run Claude Code
programmatically" and the Agent SDK result message): `type: "result"`, a
`subtype` of `success` or one of the four documented `error_*` values, a boolean
`is_error`, the narrative `result` on the success arm, an `errors` array on the
error arm, an optional `api_error_status` and, for a valid structured run,
`structured_output`. Semantic success requires an observed exit code zero,
complete I/O (below), untruncated stdout, exactly one occurrence of every root
property, coherent metadata, `is_error` false, subtype `success`, no errors or
API error status, and a `structured_output` JSON object without duplicate
property names; its object, never the narrative text, is what the role's own
schema parser then validates. JSON is bounded in size and depth. Different
narrative text is not a contradiction. The roles keep their own adapters,
schemas and authorization; only this reader is shared.

### Closed, persisted invocation diagnostics

Every Claude invocation that does not reach semantic success records one closed
category, version `agent-invocation-diagnostic-v1`, atomically with its failed
result: InvocationUnavailable, EnvironmentUnavailable, TimedOut, Cancelled,
CaptureDeadlineExceeded, CaptureFault, AuthenticationRequired,
ProviderLimitReached, ProviderReportedError, NonZeroExit, InvalidEnvelope,
StructuredOutputMissing or InvalidStructuredOutput. A failure envelope is
interpreted even on a non-zero exit, and nothing on a non-zero exit is success.
The only specific provider categories come from the documented
`api_error_status` field (401 AuthenticationRequired, 429
ProviderLimitReached); every other report stays generic. A category describes
what was observed or reported, never a verified cause. No matched text,
payload, exception, environment value, credential, account identifier or path
is stored or shown. Usage and model-limit facts stay independently validated.
The diagnostic is write-once, only beside a failure of a dispatched Claude
attempt, and never beside a semantic success. The existing Agent attempt
evidence projection (current run and run history) shows it with fixed copy;
historical attempts show nothing (not recorded). No endpoint, authentication
probe or retry authority is added.

### Bounded process I/O completion

The shared adapter records stdout and stderr completion (Complete,
DeadlineExceeded, Faulted, Unknown) and stdin delivery (the same plus
NotRedirected) separately from the process outcome, physical termination,
duration and byte-cap truncation. Complete means end of file and finalization,
not retention of every byte. The execution timeout and the existing five-second
post-kill wait are unchanged; then the adapter allows at most five seconds for
all I/O to complete and at most one second to cancel the actual reads and the
input write, close its own pipe ends and close each writer under its write gate.
A close request first freezes the returned output and truncation fact as an
immutable snapshot and cancels the writer's sink I/O (the sink is opened for
asynchronous, unbuffered I/O, so the cancellation reaches the operating system);
no chunk read afterwards reaches the buffer or the sink. A writer is proved
closed only when its gate and its sink's disposal both finish within the
release window; otherwise its stream is Unknown, and a held writer finalizes
the sink itself once its I/O returns without changing the reported completion.
An incomplete stream never releases its redaction tail. A captured stream's
partial file is sealed only when its writer was proved closed (Complete,
DeadlineExceeded or Faulted); an Unknown or absent fact leaves the partial file
neither sealed nor deleted, in every Agent, Process and verification
supervisor. These are managed deadlines that assume a responsive operating
system; an expired window never proves a descendant stopped.

Agent semantic success (every clean-exit-required outcome), a successful Process
attempt and a verification Passed now also require explicitly complete I/O. An
observed exit code zero with incomplete or unknown I/O is recorded unchanged
and is a failure. Every consumer of the shared adapter that parses output or
accepts exit zero (Codex, Git, local commit, discovery and the GitHub
observations) treats incomplete I/O as a failure without any change to its
command, environment or authority.

### Additive persistence

One additive migration adds nullable text facts: the three I/O facts and the
diagnostic with its version on attempts (Agent and Process), the three I/O
facts, the environment profile version and a closed verification diagnostic on
verification executions, and a capture-completion classification on Agent,
Process and verification output artifacts. Historical rows keep null, which is
never read as complete. A downgrade is refused, before any change, once any new
fact was recorded. The exact-shape readers of ADR-0061 and ADR-0069 accept a
Failed verification with an actual Exited/0 tuple only beside recorded
incomplete I/O, and keep every historical shape coherent. ADR-0065/0066 exit
proof and release semantics are unchanged: their guards are status-independent
and Exited remains proof about the invoked process only.

### One fixed Windows verification environment

Every dispatched verification receives the fixed profile
`windows-verification-v1` and nothing from the host's ambient environment:
`SystemRoot` derived from the system directory, `ComSpec` (the system
`cmd.exe`, the Windows script shell approved npm scripts use), a `PATH` of the
validated directory of the selected executable and the system directory only,
and owned per-execution `TEMP`, `TMP`, `USERPROFILE`, `APPDATA` and
`LOCALAPPDATA`; for `dotnet.exe` also `DOTNET_ROOT`, owned `DOTNET_CLI_HOME`
and `NUGET_PACKAGES`, `DOTNET_CLI_TELEMETRY_OPTOUT=1` and `DOTNET_NOLOGO=1`; for
`node.exe` running exactly its own bundled `npm-cli.js`, owned
`npm_config_cache` and empty owned `npm_config_userconfig` and
`npm_config_globalconfig` files. A direct `npm.cmd` launch, an unvalidated
executable or a missing prerequisite completes the execution as Failed with
EnvironmentUnavailable instead of leaving it pending. No credential, profile
configuration or cache is copied and there is no fallback. The typed recipes,
source and dispatch gates are unchanged, and standalone Process attempts keep
their empty environment. Owned scratch is removed only after a proved exit with
complete I/O; anything unresolved keeps it. Because the dispatch marker is
durable before the environment is prepared, a preparation that fails (an I/O
failure while creating the owned children, or any unexpected exception) is
recorded once as Failed with EnvironmentUnavailable, no profile version and no
process; the partially created scratch, which no process ever used, is removed,
and nothing is redispatched. An adapter that returns no result leaves any
partial output file in place.

### Fatal startup and its guidance

Startup recovery keeps its order, idempotency, committed work and
side-effect-free compositions but now runs through one checked runner. A
database, schema, query, transaction or commit fault, a failed or incoherent
safety-critical reconciliation, or any unclassified exception is fatal: no later
step, API, Ready line or hosted dispatch follows. A coherent success that records
NeedsAttention, Interrupted or held ownership is the existing state, not a
failure. Per-item optional historical output that could not be sealed or
imported, deferred removal of a proved-redundant partial and unclosed previous
supervision observations are the only warnings, and only because every
transaction and safety-critical step was proven; they create no evidence,
readiness or release. The same typed rule applies to all three historical
output families (Agent, Process and verification): only a file-system fault on
one output item (a file that exists but cannot be read, described or sealed) is
an optional-output warning, and its files stay in place; a truly absent file is
nothing to import; every database query and commit, and any other exception,
remains fatal. Startup removes a partial output file only when an artifact of
the same purpose is already recorded for that attempt or execution, so an
unsealed or failed-import partial survives every later restart; verification
partials are never promoted and are no longer deleted.

A fatal failure writes one bounded, versioned frame,
`DEVALCOPILOT_SIDECAR_FAILED {"version":1,"stage":...,"reason":...,"action":...}`,
on the existing sidecar stdout protocol channel and the same safe diagnostic
(`DCSF1-<stage>-<reason>-<action>`) on stderr, then exits with code 3. The shell
validates the frame against closed lists, a 512-byte bound and the
reason-to-action rule; a fragment, an oversized or malformed record is ignored.
The failure is terminal for that launch, and a late Ready never restores a
session. `get_launch_session` returns the closed `startup-failed` error, and the
WebView's existing failed-startup screen shows fixed guidance and the copyable
diagnostic without any API or session: storage unavailability means restore
access or free space and reopen once, persistent failure needs reviewed repair;
incoherent or unknown state means preserve data and worktrees, copy the
diagnostic and request reviewed repair. Nothing advises database edits, reset,
deletion, force-release or blind restarts, and nothing promises self-healing.
Nonfatal warnings travel as closed names on the Ready line and appear in one
app-shell summary. No native command, permission, endpoint or raw stderr display
is added.

### Dependency preparation for the local toolchain proof

The real toolchain proof prepares dependencies only from exact lockfile
identities, copying exact `.nupkg` archives and lockfile-addressed npm tarball
content into owned, fresh staging with a locked, offline, local-only restore.
Only the lockfiles committed at `HEAD` are authority (their committed bytes,
never an untracked, ignored or modified working copy). Before any write the
staging root must not equal, contain, sit inside or alias (through a link or
junction) the repository or either source cache, physically and
case-insensitively on Windows. Every NuGet archive and npm content file is read
once through one bounded reader (a regular file, never a link, physically
inside its root, within the size bound), verified in memory, and exactly those
bytes are written to staging; the sources stay read-only.
It is proof preparation, not a product feature, and it needs an identified
existing outbound-isolation boundary before it may run; without one the proof
is Blocked (OutboundIsolationUnavailable). Missing certificate or revocation
evidence is Blocked (CertificateValidationUnavailable), never relaxed.

## Narrow extensions

- ADR-0004/ADR-0009: the Claude envelope contract and the closed diagnostic
  beside a failed result; role schemas and authority are unchanged.
- ADR-0002/ADR-0006: the sidecar stdout protocol gains the failure frame and
  warning names; the bootstrap secret, loopback binding and session lifetime are
  unchanged.
- ADR-0023: diagnostic and I/O facts join, never replace, attempt evidence.
- ADR-0060/ADR-0055/ADR-0056: their startup steps are now checked; order and
  semantics are unchanged.
- ADR-0061/ADR-0069: one additional coherent Failed tuple, only with recorded
  incomplete I/O.

## Consequences

- An installed Claude Code version's compatibility with this documented
  contract remains unqualified until a separately authorized qualification.
- Verification recipes that relied on inherited configuration, caches or
  credentials now fail truthfully; a recipe must be self-contained under the
  fixed profile. Each execution gets its own empty package caches, so a .NET
  recipe must restore and build in one execution, and SDK recipes should
  disable build servers so no persistent node inherits the captured pipes.
- Under the `windows-verification-v1` keys, NuGet restore inside the .NET SDK
  throws, because NuGet derives its machine-wide settings path from
  `ProgramFiles(x86)` or `ProgramFiles`, which version 1 omits (review challenge
  WEP-R4). Resolved for new executions by
  [ADR-0073](0073-add-an-owned-nuget-base-to-the-windows-verification-profile.md)
  (`windows-verification-v2`: one owned, empty `ProgramFiles(x86)` for
  `dotnet.exe` only); executions that recorded version 1 keep it. Every real SDK
  execution was also found to run the SDK's first-run experience, which appends
  a tools directory to the persistent user `PATH` and generates an ASP.NET
  development certificate (finding WEP-F6); resolved for new executions by
  [ADR-0074](0074-disable-sdk-first-run-persistent-side-effects-in-the-windows-verification-profile.md)
  (`windows-verification-v3`: two literal-`false` flags for `dotnet.exe` only);
  executions that recorded version 1 or 2 keep what they recorded.
- Timing bounds are managed deadlines; an unresponsive operating system can
  still delay them, and they never prove descendant shutdown.
- General host-loss recovery, Job Objects, force-release, reservation recovery,
  resume, retry, takeover, publication, draft pull requests and CI orchestration
  remain separate decisions. ADR-0050 remains Proposed.
