# User experience

## Experience goal

DevalCopilot should make complex automation feel inspectable and controlled.
The user should understand within seconds:

- what objective is active;
- what stage the run has reached;
- which participant is acting or waiting;
- what changed;
- what evidence passed or failed;
- what disagreement remains unresolved;
- whether the application needs human action.

The primary interface is a development cockpit, not a general-purpose chat.
Conversation is shown in the context of workflow, evidence, and decisions.

## Information architecture

The MVP has three top-level destinations:

- **Projects**: repositories, capabilities, readiness, and recent runs;
- **Run Cockpit**: live and historical execution detail;
- **Settings**: application defaults, tool discovery, policies, budgets, and
  storage.

Artifacts, diffs, logs, approvals, and CI details open inside the current run
rather than becoming independent navigation silos.

## Projects view

```text
+--------------------------------------------------------------------------+
| DevalCopilot                                      Tools: 5 ready, 1 optional |
+--------------------------------------------------------------------------+
| Projects                                                                  |
|                                                                           |
|  DevalCopilot                         main @ a42fc91       Ready           |
|  C:\...\DevalCopilot                  GitHub connected     12 runs         |
|                                                                           |
|  AnotherProject                       feature/x @ a29f11   Needs attention |
|  C:\...\AnotherProject                Claude unavailable   3 runs          |
|                                                                           |
|                                      [Add project] [Start new run]        |
+--------------------------------------------------------------------------+
```

Each project card shows:

- canonical path and friendly name;
- current branch, abbreviated HEAD, and working-tree state;
- remote provider and authentication readiness;
- Codex, Claude Code, Git, required runtime, and optional Docker readiness;
- active run or recent terminal outcome;
- the most important blocking condition.

Readiness uses explicit states: `Ready`, `Degraded`, `Needs attention`, or
`Unavailable`. Color never carries the meaning alone.

## Run cockpit

```text
+--------------------------------------------------------------------------------------+
| Run #42  Add CI panel      RUNNING · REVIEW        branch @ a42fc91   [Pause] [Stop] |
+----------------------+--------------------------------------+------------------------+
| Workflow             | Agent collaboration                  | Evidence               |
|                      |                                      |                        |
| ✓ Intake             | CODEX · Proposal                     | Changes                |
| ✓ Plan               | Add check polling by exact SHA...    | 8 files  +231 -44      |
| ✓ Critical review    |                                      | [Open complete diff]   |
| ● Implementation     | CLAUDE · Challenge                   |                        |
| ○ Local verification | Polling must survive restart...      | Local verification     |
| ○ Review             |                                      | ✓ build   42s          |
| ○ Publication        | CODEX · Decision                     | ✗ tests   1 failed     |
| ○ CI                 | Accepted; persist cursor first...    | [Open failure]         |
|                      |                                      |                        |
| Budgets              | CLAUDE · Execution                   | GitHub CI              |
| Review loops  1 / 2  | Editing GitHubRunMonitor...          | ○ backend pending      |
| CI loops      0 / 2  |                                      | ○ frontend queued      |
| Tokens      18k / 60k|                                      |                        |
+----------------------+--------------------------------------+------------------------+
| Live output · Claude Code                                             [Inject input] |
+--------------------------------------------------------------------------------------+
```

### Header

The header always shows:

- objective and run identifier;
- run lifecycle and active stage;
- branch and exact abbreviated HEAD;
- connection freshness;
- pause, stop, retry, or takeover actions when applicable.

### Workflow rail

The left rail shows ordered stages, blocking reasons, current budgets, and
checkpoints. Selecting a stage filters the timeline and evidence without hiding
the overall run state.

Token usage distinguishes input, output, and cached tokens when providers make
that information available. The UI shows current-stage and whole-run budgets
without encouraging optimization that compromises correctness.

### Collaboration timeline

The center uses semantic cards and participant swimlanes. Card types have stable
icons, labels, and structures for proposals, acceptances, challenges, decisions,
execution reports, findings, revision responses, commands, events, and human
interventions.

Collapsed cards show the decision-relevant summary. Expanding a card reveals
reasoning, evidence links, protocol metadata, and raw artifact access. Raw
transcripts never replace the structured view.

### Evidence panel

The right panel has contextual tabs:

- Changes;
- Local verification;
- Review findings;
- GitHub CI;
- Artifacts;
- Approvals.

Every status links to the Git fingerprint or remote head SHA it proves. A stale
result is visibly marked and cannot appear green for the current source.

### Live output drawer

The bottom drawer shows bounded live stdout and stderr for the active attempt.
It uses a terminal renderer for readability but remains a view, not an
unrestricted interactive shell. Input is enabled only when the active adapter
owns an explicit interactive protocol.

## Human attention

The application differentiates three conditions:

- **Informational**: work continues without input;
- **Decision required**: automation is blocked on an explicit choice;
- **Safety intervention**: external state is ambiguous or policy was violated.

A required decision appears in the header, workflow rail, and timeline. The
decision surface presents:

- the exact question;
- why it matters now;
- options and consequences;
- recommended option, if any;
- evidence links;
- the scope and lifetime of resulting approval.

There is no generic `Approve all` control.

## Diff and review

- Use a Monaco-based unified or split diff view.
- Show complete changed-file inventory before individual file navigation.
- Distinguish user baseline changes from tool-owned changes.
- Link findings to exact file and line when possible.
- Mark generated, binary, deleted, and oversized files explicitly.
- Show the reviewed commit and invalidate the visual approval state after a
  source change.

## CI experience

The CI panel groups required and optional checks and shows:

- queued, running, passed, failed, skipped, cancelled, stale, or unknown state;
- workflow, job, and failed-step hierarchy;
- attempt, duration, exact head SHA, and provider URL;
- bounded failure excerpt and complete log artifact;
- the diagnosis, corrective decision, and resulting commit.

A subsequent push keeps the previous failure visible in history while resetting
the current verification state.

## Visible self-hosting evolution

Runs targeting DevalCopilot show:

- controller version and commit;
- candidate branch, commit, and build identity;
- frontend screenshot artifacts from browser tests;
- features and files changed since the controller version;
- local and CI evidence for the candidate;
- the point at which the candidate became ready for human adoption.

The MVP does not install the candidate automatically. A future isolated preview
or signed updater must preserve the stable-controller rollback path.

## Empty, loading, disconnected, and recovery states

Every primary view defines:

- first-use empty state with the next valid action;
- loading state that does not imply absence;
- disconnected state with last applied event sequence;
- reconnecting state that prevents unsafe stale actions;
- stale-state banner until durable catch-up completes;
- recovery state explaining observed drift and available actions;
- terminal state with evidence summary and replay.

## Accessibility and visual quality

- Keyboard navigation covers primary run and decision actions.
- Focus remains visible and predictable as live events arrive.
- Live updates use appropriate announcements without reading every log line.
- Status never depends on color alone.
- Dense panels support zoom and resizable boundaries.
- Text, icons, and code views meet appropriate contrast targets.
- Motion indicates transition but respects reduced-motion preferences.
- The interface remains usable at a practical minimum desktop width; mobile is
  outside the MVP.

## MVP design acceptance

- A new user can identify the active stage and actor without opening a log.
- A material challenge and its resolution are visually connected.
- A review or CI result always exposes the source fingerprint it applies to.
- A human approval explains its exact scope before submission.
- A restart reconstructs the same cockpit from durable state.
- A self-hosting run presents candidate screenshots and final evidence inside
  the stable controller.
