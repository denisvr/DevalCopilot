# ADR-0023: Record Claude-reported model context limits in historical attempt evidence

Status: Accepted

## Context

A Claude attempt's provider result is one `--print --output-format json` stdout envelope. The three Claude adapters (critical
review, implementation and review correction, including the repair, guided, authorized and diagnosis-origin variants that run
through them) already read its `is_error`, `result`, `session_id` and `usage` members. They deliberately ignored `modelUsage`, so the
host kept no record of which model identifiers Claude listed for an attempt or of the context-window and maximum-output limits it
reported for each. A person inspecting a past attempt could not see them, and the roadmap's context-visibility gap stayed open.

The reported-field contract is documented and observable without calling a provider. The official programmatic CLI guide
([code.claude.com/docs/en/headless](https://code.claude.com/docs/en/headless)) identifies print mode as the CLI form of the Agent
SDK, and the SDK's TypeScript reference ([code.claude.com/docs/en/agent-sdk/typescript](https://code.claude.com/docs/en/agent-sdk/typescript))
declares a `modelUsage` map whose entries carry `contextWindow` and `maxOutputTokens`. A read-only inspection of the public result
schema of the installed `@anthropic-ai/claude-code` 2.1.276 shows the same envelope member and declares both limits as integers. No
authenticated invocation, credential read, package installation, model catalog, CLI default or alias was used. This establishes what
Claude reports, not that a reported value is accurate, and not what a later invocation can accept.

## Decision

**Observation.** After a Claude attempt's provider process exits cleanly with a structurally valid, untruncated envelope, the
adapters read the optional `modelUsage` map and retain only each map key as the model identifier and the entry's `contextWindow` and
`maxOutputTokens` as its two limits. A valid `is_error` envelope may still carry them, because they are independent of the business
result. A non-zero exit, a timeout or cancellation, an incomplete capture, an invalid envelope and an invocation that produced no
process result supply none. Costs, usage totals, `canonicalModel`, routing and every other member are ignored, no entry is
interpreted as the main model, a fallback, a requested alias, authentication state or a permission, and the existing observed model
and effort, token usage and its accounting are unchanged.

**Admission is all-or-unknown.** The envelope must hold exactly one `modelUsage` object of 1 to 16 unique identifiers (ordinal
comparison, no normalization) of 1 to 128 ASCII characters matching `[A-Za-z0-9][A-Za-z0-9._-]*`. Every entry must be an object with
exactly one occurrence of each required member, and both members must be positive JSON integers that fit a 32-bit signed integer,
with the output limit no greater than the window. The shape is judged by one Domain rule that the parser and the recording policy
share. A missing, empty, duplicated, malformed, excessive or unsupported map becomes absent optional evidence: the adapter never
keeps a valid subset and never lets the evidence invalidate an otherwise valid result, process evidence or token usage. Missing limits
preserve valid usage, and missing usage preserves valid limits.

**Provider-neutral carriage and one durable snapshot.** An immutable provider-neutral value (the source tag of the parsing contract
and the ordered entries) travels through the three invocation results, the supervisors and the three recording commands, and the
existing completion transaction records it once, together with the outcome, artifacts and other evidence, including for unsuccessful
semantic outcomes. It is stored as one nullable `TEXT` column on `Attempt`: the canonical project-owned snapshot
`{"version":1,"source":"claude-cli-model-usage-v1","models":[{"modelId":…,"contextWindowTokens":…,"maxOutputTokens":…}]}`, entries
ordered ordinally, at most 4 KiB of UTF-8, written compactly with fixed member names and order. The Domain validates and serializes
that project shape and never sees a provider field name; only Infrastructure parses `modelUsage`. Recording is write-once and bound to
a dispatched attempt of the proven provider and to an outcome that is not a pre-invocation classification (the closed set that
process and token evidence already share), and a violation fails the command before any mutation. There is no child table, catalog
or dependency.

**Reading is strict and forward only.** Stored text is accepted only when it parses and re-serializes to exactly itself as valid
evidence for the attempt's provider and the one proven source. Malformed, oversized, reordered, extended, wrong-provider or
unknown-version text projects as absent without throwing and without affecting a healthy sibling. A running, undispatched, non-Agent
or identity-incoherent attempt exposes none. Rows that existed before the column remain `NULL`; nothing backfills them or reparses an
artifact, and a read never calls a provider or reads an artifact.

**Exposure and display.** The existing authenticated `GET /runs/{runId}/agent-attempts/{attemptId}/evidence` gains one additive,
nullable `modelContextLimits` member with API-owned nested contracts (`models`, each with `modelId`, `contextWindowTokens` and
`maxOutputTokens`); the parsing-contract source and snapshot version are never exposed. Identity, authentication and cross-run rules
are those of the route. No role-status, cockpit-summary or history-list contract changes, and the TypeScript client is regenerated,
not edited. The selected historical attempt detail shows, for a Claude attempt, a "Claude-reported model limits" section with each
identifier as plain text, its context-window tokens and its maximum output tokens, describes the entries as models listed by Claude
and not independently proven used, and states that remaining context and next-invocation capacity were not measured. An attempt with
none shows "Not recorded", never zero. There is no percentage, fullness meter, estimate derived from token totals, global panel,
polling, probe or action.

## What this does not decide

This is a historical observation of what a provider reported. It is not remaining context, compaction, resume or session
persistence; not account allowance or a threshold; not a live capability, model or effort selection, or invocation eligibility; and
not a token-stop, fallback, scheduler, lifecycle or publication authority. It does not make a reported number true, does not say which
listed model served which turn, and does not change any provider argument, authentication or permission behavior. It changes no
filesystem, Git fingerprint, tracked-diff, manifest or sealed-replay behavior, and the open hard-link disclosure risk recorded in
[ADR-0022](0022-admit-generic-untracked-previews-only-from-physically-proven-single-name-files.md) and
[the roadmap handoff](../roadmap/current-work.md) is unchanged. Codex attempts record none, and a future provider contract would
need its own proven source tag.

## Consequences

A person can see, for any concluded Claude attempt, which models Claude listed and the limits it reported, and the record survives a
restart. The fact stays honest about its limits: an unproven or oversized report shows as "Not recorded" rather than a guess, and a
real-provider shape this contract does not recognize (for example an identifier outside the closed ASCII shape) is also absent until
a new, evidenced contract version admits it. The cost is one nullable column, one more parameter on three completion paths, and a
parsing contract that must change together with the Claude envelope contract it shares with the existing usage reader.
