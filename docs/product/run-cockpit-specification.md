# Run cockpit functional specification

## Status

Approved MVP design baseline. The interactive reference is
[`prototypes/cockpit-v1`](../../prototypes/cockpit-v1/index.html).

The prototype demonstrates intended behavior and information hierarchy. It is
not an implementation contract for literal sample values, hard-coded provider
models, simulated events, or visual dimensions.

## Purpose

The run cockpit is the primary supervision surface for a DevalCopilot run. It
must answer five questions without requiring the user to open a raw log:

1. Which project and objective are active?
2. Which agent or tool is working now?
3. What workflow state has been reached, including repeated attempts?
4. What budget, provider allowance, or human decision can stop progress?
5. What evidence supports the current status?

The cockpit is not a shared free-form chat client. It is a structured view of a
durable workflow whose conversation, actions, and evidence are owned by the
DevalCopilot orchestrator.

## Information hierarchy

The desktop layout has four levels:

1. a collapsible global navigation rail;
2. a project and run switcher across the top;
3. a compact run header with objective, state, autonomous time, and primary
   controls;
4. a three-panel workspace with collapsible Workflow and Usage & Evidence
   rails around the responsive Agent Collaboration surface.

The collaboration surface is the visual focus. Collapsing either contextual
rail gives the released width to collaboration cards rather than leaving empty
space. At the supported minimum desktop width, contextual rails become drawers
and collaboration remains usable.

## Project and run switcher

The top switcher shows registered projects with their most relevant active run.
Each item contains only:

- project name;
- concise state such as `Running`, `Review`, `Waiting`, or `Paused`;
- execution number and current stage.

Selecting an item changes the entire cockpit atomically to that run. Data from
two runs must never be visually combined. A loading or stale state remains
visible until the selected run projection catches up.

`Execution 18` identifies the eighteenth durable run for that project. It is
not a message, interaction, attempt, or event count.

Multiple projects may have active runs concurrently. The UI must distinguish:

- actively consuming an execution slot;
- waiting for an agent, command, CI, approval, or concurrency slot;
- paused by the user or a provider guardrail;
- terminal.

## Run header

The primary header shows:

- project name and objective;
- execution number;
- lifecycle and active stage;
- accumulated autonomous session time;
- context-sensitive `Start`, `Pause` or `Resume`, and `Stop` controls.

Autonomous session time measures time during which the run is allowed to
advance. It excludes time while the run is paused or terminal. Detailed Git
fingerprints, event sequence numbers, provider session identifiers, and attempt
metadata belong in Evidence rather than the primary header.

`Pause` prevents a new external attempt from starting. `Stop` requires
confirmation and becomes terminal only after active external state is
reconciled. Controls must never imply immediate cancellation when the provider
cannot prove it.

## Agent runtime strip

Codex and Claude each have one compact, single-line runtime control. The line
contains:

- provider identity;
- `Working`, `Waiting`, `Paused`, `Blocked`, `Unavailable`, or `Stopped`;
- selected model and effort;
- context-window percentage when available.

The active participant receives a high-contrast border, a visible working
label, and restrained motion. Status never depends on animation or color alone,
and reduced-motion preferences disable nonessential animation.

Selecting the model summary opens provider-specific settings for model, effort,
and permission mode. Options are discovered through the provider adapter and
must not be hard-coded as a permanent product catalog. A change applies at the
next safe attempt boundary and never silently changes an active invocation.

Provider permission modes are subordinate to DevalCopilot policy. Selecting a
more permissive provider mode cannot grant filesystem, process, Git, GitHub, or
publication authority that the run policy does not already allow.

Selecting the context indicator opens:

- consumed and available context when reported;
- automatic compaction policy;
- a manual `Compact` action when supported;
- freshness and an explicit `Unknown` state when unsupported.

Manual compaction occurs only between turns. It preserves the durable run
record and creates a new context-manifest revision referencing decisions and
evidence that remain authoritative. It does not rewrite run history.

## Collaboration timeline

Codex cards are aligned to the left and Claude cards to the right. Orchestrator,
tool, CI, and human events are centered. The alternating layout expresses the
exchange without pretending that both providers share one native chat.

Supported card types include:

- proposal and plan revision;
- acceptance or material challenge;
- decision and challenge resolution;
- execution report and active work;
- review finding and revision response;
- verification, Git, CI, approval, and human-intervention events.

Collapsed cards show a decision-relevant summary. Challenge, Decision,
ReviewFinding, and RevisionResponse cards additionally expose their bounded
structured fields with readable labels. A reply is described as verified only
when its parent message is both present in the currently loaded timeline and
an earlier, protocol-compatible parent for the reply's own type; a matching id
that fails either check is described only as an observed reference, never as a
verified relationship, and a parent absent from the loaded timeline is never
asserted to be outside the API's bounded window — only that it is not present
in what is currently loaded. The active card is visually tied to the
participant marked `Working`.

Expanded cards are intended to later expose linked evidence and raw artifacts;
the cockpit does not yet implement that expand interaction, only the bounded
legacy-details disclosure described above.

The timeline is reconstructed from durable events. It does not rely on the
continued availability of either provider's native conversation history.

## Provider sessions

A DevalCopilot run may use several Codex threads and Claude Code sessions. Each
agent attempt records its provider session identifier when one exists so the
adapter can continue an eligible session and the UI can offer provider-native
open or resume actions when supported.

Provider-native sessions are secondary views:

- Codex contains only Codex-side prompts, responses, and actions;
- Claude Code contains only Claude-side prompts, responses, and actions;
- DevalCopilot contains the complete interleaved collaboration, workflow,
  decisions, and evidence.

Provider UI discovery is a convenience, not a recovery dependency. The product
must not read or mutate undocumented provider storage to manufacture shared
history.

## Workflow rail

The Workflow rail is a projection of persisted stages, tasks, gates, attempts,
and checkpoints. It is not extracted from a Markdown checklist.

The rail shows the ordered stage map and the current state of each stage. A
stage can contain multiple immutable attempts and can be revisited by a bounded
correction loop. Re-entry adds an attempt and event; it does not duplicate the
stage or erase prior outcomes.

When collapsed, the rail retains a compact progress and attention indicator.
Selecting a stage filters collaboration and evidence while preserving visible
awareness of the whole run.

No loop is infinite. Loop count, elapsed time, token budget, provider allowance,
and policy can stop advancement and produce a precise escalation.

## Usage and budget controls

Usage controls occupy the top of the right rail, above evidence. Codex and
Claude are always displayed separately because provider accounting and reset
windows are not interchangeable.

### Run budgets

Run budgets are DevalCopilot-owned enforcement limits. The UI shows observed
input, output, and cached tokens per provider when available, plus stage and run
limits. Estimated values must be labeled as estimates.

Crossing a warning threshold raises visible attention. Exhausting a hard budget
prevents a new attempt and creates a durable escalation; it never borrows
silently from another provider or run.

### Provider account usage guardrails

Account usage is a provider-reported, time-windowed allowance snapshot. Each
provider and window records value, reset time, retrieval time, and confidence.
Unavailable data is shown as `Unknown`, never as zero.

The user can configure warning and stop thresholds independently for Codex and
Claude. The MVP default is an 80% warning and a 90% stop threshold for every
reported window. When any hard threshold is reached:

- no new attempt starts for the affected provider;
- the active attempt may finish by default, unless a stricter cancellation
  policy is configured and supported safely;
- affected runs enter a provider-guardrail waiting state;
- another provider may continue only when the workflow has independent eligible
  work and policy permits it;
- resumption requires a fresh allowance snapshot below the stop threshold or an
  explicit, scoped human override.

Provider account usage does not replace run budgets. A run may have budget left
while its provider allowance is exhausted, or the reverse.

## Evidence surface

The right rail provides contextual evidence categories:

- Changes;
- Local verification;
- Review findings;
- GitHub CI;
- Artifacts;
- Approvals.

Technical data deliberately omitted from the primary header lives here,
including branch, commit, Git fingerprint, event sequence, attempt identifiers,
provider session links, timestamps, and freshness.

Every success or approval identifies the exact source fingerprint it proves.
Stale evidence remains visible as history but cannot appear valid for current
source. When the rail is collapsed, it retains badges for failures, pending
approvals, and unverified changes.

## Live output

The bottom drawer streams bounded stdout and stderr for the active attempt. It
identifies the provider or tool and attempt number. Complete output is retained
according to artifact limits; the live surface may truncate without losing the
artifact reference.

The drawer is not a general shell. Input is exposed only for a typed adapter
interaction that explicitly supports it.

## Concurrent run behavior

The MVP supports bounded concurrent runs across distinct approved projects.
The default global limit is two active mutating runs and is configurable down to
one. Waiting on CI or human approval does not consume an agent execution slot.

Concurrency invariants are:

- at most one mutating run for a canonical repository at a time;
- exactly one writer for a run worktree;
- provider and command processes are owned by one immutable attempt;
- SQLite state changes continue through the serialized ingestion boundary;
- project switching never changes execution state;
- pause, stop, budget, and provider-allowance decisions are scoped to the
  intended run or provider and do not silently affect unrelated projects.

A conflicting run is queued with an explicit reason. Read-only history,
evidence inspection, and external CI observation remain available.

## Themes, density, and persistence

Dark Navy and Light are supported themes. Theme choice and panel layout may be
stored as non-sensitive local UI preferences. Run state, approvals, provider
credentials, and session secrets must never be stored as UI preferences.

Both themes use a vivid orange for Claude, warnings, and active attention with
contrast sufficient against their backgrounds. Meaning is reinforced through
text, shape, and iconography.

Navigation and contextual rails are collapsible, keyboard operable, and
resizable where practical. The collaboration column grows responsively. The
application preserves the user's layout preferences without changing the
durable run projection.

## Required read models

The frontend consumes generated contracts for these conceptual projections:

- `ProjectRunSummary`: project identity, execution number, stage, lifecycle,
  wait reason, and freshness;
- `RunCockpit`: objective, autonomous duration, active participant, stage map,
  available commands, and latest sequence;
- `AgentRuntime`: provider health, active attempt, model, effort, permission
  mode, provider session reference, context usage, and compaction capability;
- `BudgetStatus`: provider-specific stage and run usage, limits, confidence,
  and enforcement state;
- `ProviderAllowanceStatus`: provider, window, consumed percentage, reset time,
  thresholds, freshness, and availability;
- `CollaborationCard`: typed summary, actor, related stage and attempt, evidence
  references, and expansion capability;
- `EvidenceSummary`: changes, checks, findings, artifacts, approvals, Git
  fingerprint, and staleness;
- `LiveAttemptOutput`: bounded chunks, cursor, stream, attempt, and truncation
  state.

SignalR only announces that durable state advanced. Initial load, project
switching, reconnection, and missed-event recovery query authoritative read
models and journal cursors through the local API.

## MVP acceptance criteria

- The selected project, objective, lifecycle, stage, and active participant are
  understandable within seconds.
- Workflow and Evidence can collapse independently, and collaboration uses the
  released space.
- A real challenge and its resolution are visually connected.
- Model, effort, and permission-mode changes apply only at safe boundaries.
- Context usage and compaction never replace or erase durable history.
- Codex and Claude run budgets and account allowances remain separate.
- Reaching a configured provider stop threshold prevents a new invocation.
- Two different projects can progress concurrently without creating concurrent
  writers for one repository or worktree.
- Evidence exposes the exact source fingerprint and marks stale results.
- Restart and reconnect reconstruct the same cockpit from durable state.
- Dark Navy and Light themes retain readable status and attention contrast.
- The complete self-hosting demonstration can be supervised without opening a
  provider UI or copying a message manually.
