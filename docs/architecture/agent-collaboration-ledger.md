# Agent collaboration ledger

This companion to the [agent collaboration protocol](agent-collaboration-protocol.md)
holds the protocol's durable state boundary: the current durable ledger of
validated envelopes and the message-producing stages that append to it, run
execution mode and isolation, and explicit abandonment and the claim paths.
The message envelope, message types, reply semantics and default interaction
are in the [dialogue](agent-collaboration-dialogue.md) companion. The sections
below are moved in their baseline order with their heading levels, and the
protocol's original headings forward here. Two directional references now link
to the dialogue companion instead: the critical-review path a revised Proposal
can re-enter, formerly "above", and the reply-semantics table, formerly
"below"; no other moved text changed. Their statements are as written at the
baseline, including the stage ordinals and the claim paths they name; the move
reconciles or endorses none of them. Mapping:
[protocol rework map](../roadmap/agent-collaboration-protocol-rework-map.md).

## Current durable ledger boundary

The current implementation persists a project-owned, append-only ledger of
validated `1.0` envelopes for one run, with an optional owning attempt and a
monotonic timeline sequence. It records bounded summaries and typed structured
details only; it does not persist a provider-native transcript, executable
path, environment value, credential, provider configuration, or raw output.
The deterministic walking-skeleton sequence writes envelopes marked
`Simulated`; this label is not evidence of provider observation, authentication,
or invocation.

Codex's Planning step, Claude Code's Critical review step, and Codex's
challenge-resolution step are the first three message-producing stages with a
real, live provider invocation. A durable Agent attempt of either provider
records a bounded context manifest, runs the provider's own CLI as a real
child process, and preserves its raw stdout, stderr, and final-response
content only as bounded sealed artifacts outside the database — never as
ledger or API content. A structurally valid Proposal, Acceptance, Challenge
set, or Decision-set-plus-revised-Proposal is appended to the ledger only
after passing the same protocol/schema validation as any other message; an
invalid, failed, or source-drifted attempt appends none. A Claude
critical-review attempt reviews exactly one already-recorded,
provider-observed Codex Proposal bound to the same run, isolated workspace,
checkpoint, and fresh Git fingerprint; it never edits the repository, and it
returns exactly one Acceptance or one to five Challenge messages, never both
and never neither.

A Codex challenge-resolution attempt is claimed only against one specific,
already-completed Challenged Claude critical-review attempt, and persists the
exact ordered input set that attempt is bound to as `AttemptInputMessage`
rows: the original Proposal at sequence 0, then every Challenge that review
raised, in the same timeline order the review itself recorded them — never a
caller-selected subset, never a different order. Given that fixed context, the
provider resolves every Challenge explicitly and proposes a revision; on
success, the attempt atomically appends one Decision per Challenge (each
replying to the Challenge it resolves) plus exactly one revised Proposal
(replying to the original Proposal) in a single transaction — never a partial
set, never a Decision left unresolved. Before that provider invocation ever
starts, and again in the same short transaction that commits the dispatch
marker, the run/workspace/lease/checkpoint eligibility is revalidated and the
attempt's own exact ordered input set is independently re-checked against
every other already-Resolved challenge-resolution attempt; a resolution of the
identical ordered Proposal-plus-Challenge-set already existing supersedes this
attempt with a truthful `InputAlreadyResolved` outcome before the provider is
ever invoked — never a duplicated resolution, and never silently retried. The
revised Proposal a resolution produces can re-enter the [critical-review path](agent-collaboration-dialogue.md#critical-review)
as an ordinary Proposal; Acceptance remains terminal for that review
cycle.

Claude Code's Implementer step is the fourth message-producing stage with a
real, live provider invocation: it durably implements exactly one
authoritative resolved plan, edited entirely inside the run's owned worktree.
Exactly two resolved-plan forms are eligible, identified only by the plan's
own Proposal message id, never reconstructed from display text: an original,
provider-observed Codex Proposal with a completed, successful Claude Accepted
review; or the provider-observed revised Proposal a completed, successful
Codex Resolver attempt itself produced. The attempt persists the exact
ordered input identity that form is bound to as `AttemptInputMessage` rows —
the Proposal at sequence 0, then either its Acceptance or its complete
ordered Decision set, never both. Claude never runs Git, a verification
command, a commit, a push, a package install, or any network operation during
this attempt; its only capability is reading and editing files inside the
worktree. Because Claude's implementation tool allowlist never includes Git
or any process tool, it can never move `HEAD` itself: after the process
exits, the orchestrator independently re-reads fresh Git evidence and first
checks whether `HEAD` itself still matches the immutable starting
checkpoint's own `HEAD` — a changed `HEAD` is proof of an external or
unauthorized mutation and is recorded as `ImplementationHeadChanged`, never
`Implemented`, regardless of what the report itself claims to have changed.
Only when `HEAD` is unchanged, the process succeeded, and a valid, schema-
conformant implementation report's self-reported changed-path set exactly
matches that independently observed evidence does the attempt record one new
immutable Git checkpoint plus its changed-file rows, mark itself
`Implemented`, and append exactly one Execution report replying to the
implemented Proposal — all atomically, after every external process has
already finished. Any other outcome — a failed invocation, an invalid or
untrustworthy report, a changed `HEAD`, or evidence that could not be
captured — is recorded truthfully instead (`NoChangesProduced`,
`InvalidStructuredOutput`, `ProviderInvocationFailed`,
`ImplementationHeadChanged`, or `CheckpointEvidenceUnavailable`), and the
filesystem is never rolled back or silently discarded; whenever the worktree
may have changed without a verified, trustworthy result, the workspace is
also flagged `NeedsAttention` for a human to inspect, and no further attempt
is auto-retried against it. Every boundary value a recording command
receives — the starting checkpoint reference, the completion `HEAD`/
fingerprint shapes, every independently observed changed path, the sealed
artifacts, and the implementation report itself (even one a caller
constructed by hand rather than through the schema parser) — is
independently re-validated before any mutation, through the same shared
validation both the parser and the recording command call, so the two can
never silently drift apart.
Before that provider invocation ever starts, and again in the same short
transaction that commits the dispatch marker, eligibility is revalidated and
this attempt's exact plan-and-starting-checkpoint identity is independently
re-checked against every other already-`Implemented` attempt; an
implementation of the identical plan and checkpoint already existing
supersedes this attempt with a truthful `InputAlreadyImplemented` outcome
before the provider is ever invoked. A host restart that finds a
dispatched-but-unrecorded implementation attempt never guesses its outcome:
reconciliation independently re-reads fresh Git evidence before marking the
attempt `Interrupted` and, if the fingerprint no longer matches (or that
evidence itself could not be captured), the workspace `NeedsAttention` as
well.

The durable Review-finding, Revision-response, and Codex implementation-review
envelopes are now explicit, bounded workflow facts. Production contains real
Codex and Claude provider adapters and supervisors for the implemented roles;
automated tests use deterministic adapters and doubles and never invoke a real
provider. The durable envelopes do not authorize automatic verification,
publication, fallback, or parallel execution.

The cockpit presents the bounded `Challenge`, `Decision`, `ReviewFinding`, and
`RevisionResponse` fields as typed cards, including `Decision.resolution`'s own
closed enum (`accepted`, `partiallyAccepted`, `rejected` — see
`ChallengeResolutionOutputSchema`); any other value fails closed rather than
rendering as if it were a valid protocol value. A reply is presented as a
verified parent relationship only when the parent message is both present in
the current run's currently loaded timeline and an earlier, protocol-compatible
parent for the reply's own type per the [reply-semantics table](agent-collaboration-dialogue.md#version-10-reply-semantics); a matching
id that fails either check is reported only as an observed reference, never
reconstructed into an asserted relationship. A parent absent from the loaded
timeline is reported only as not present in what is currently loaded — never
asserted to be outside the API's bounded window, since its absence does not
prove that. The presentation never adds affected paths to the durable
`ReviewFinding` ledger and never treats malformed or unknown structured content
as a semantic fact.

## Run execution mode and isolation

Every run carries an immutable execution mode ([ADR-0014](../decisions/0014-add-manual-agent-run-intake-with-durable-execution-mode-isolation.md)):
`Legacy` (0, recorded before modes existed), `Simulated` (1), or `ManualAgent` (2). The deterministic simulator, including
step recording, completion, and Simulated-provenance messages, operates only on `Simulated` and `Legacy` runs; the six Agent
claim paths (with their repair variants), their eligibility feeds, and the final dispatch gate operate only on `ManualAgent`
and `Legacy` runs; standalone Process attempts operate only on `Legacy` runs. The mode is read afresh at the claim, at the
commit seam, and at dispatch, so a stale tracked entity never confers authority, and a stored number outside the set admits
nothing. A manual run is created by recording an objective only; it creates no attempt, manifest, lease, or budget
consumption, and the explicit stage requests keep every existing workspace, checkpoint, provider, contract, source, budget,
and authorization gate. `CompleteSimulatedRun` accepts only a `Simulated` attempt, so it can never conclude an Agent
attempt or a manual run.

### Explicit abandonment and the claim paths

A human may end an inactive manual run as `Abandoned` ([ADR-0031](../decisions/0031-abandon-an-inactive-manual-run-through-an-explicit-human-decision.md)). The protected
`POST /api/runs/{runId}/abandon` decides only from fresh untracked reads under the write lock and is refused while any attempt of the
project is not `Completed`, `Failed` or `Interrupted` (including an unrecognized status), so no Agent claim can already be in flight
when it commits. The other direction is the claim's own guard: the three Claude claim paths (critical review, implementation, review
correction) and the dispatch marker commit through a Run update that includes the `Lifecycle` concurrency token and therefore lose to a
concurrent abandonment; the Codex planning, challenge-resolution and code-review claims, which can claim a `Running` run without
changing a Run column, read the lifecycle untracked inside their short claim transaction after their first guard write took the write
lock and refuse before any insert (the sealed manifest is removed and nothing is consumed), including their repair variants, with the code
each already uses for an ended run: `runs.not_active` for planning and `runs.not_running` for challenge resolution and code review; the verification-diagnosis and diagnosis-correction claims already re-read it there. No new claim path, budget slot,
authorization or provider invocation is created by an abandonment or by reading its status, and an abandoned run offers no Agent request.
