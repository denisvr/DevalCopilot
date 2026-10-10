import type { GetProjectRunHistoryResponse } from '../../api/clients'
import type { ReceiptSource } from './localDeliveryReceipt'

/** The page size this region asks for; the host accepts 1 to 20 and never clamps. */
export const RUN_HISTORY_PAGE_SIZE = 10

export type RunHistoryLifecycle = 'Created' | 'Running' | 'Completed' | 'Failed' | 'Interrupted' | 'Abandoned' | 'Unrecognized'
export type RunHistoryStage = 'Intake' | 'Plan' | 'Critique' | 'Resolution' | 'Execute' | 'Completed' | 'Unrecognized'
export type RunHistoryExecutionMode = 'Legacy' | 'Simulated' | 'ManualAgent' | 'Unrecognized'

/** One recorded run of a project, already proven coherent for the page that carried it: the recorded values only, nothing derived. */
export interface RunHistoryEntry {
  projectId: string
  runId: string
  executionNumber: number
  objective: string
  lifecycle: RunHistoryLifecycle
  stage: RunHistoryStage
  executionMode: RunHistoryExecutionMode
  createdAtUtc: Date
  lastAdvancedAtUtc: Date
  /** Locates the run's own recorded local-delivery receipt; it does not certify that the receipt is available. Null: no source here. */
  receiptSource: ReceiptSource | null
}

export interface RunHistoryPage {
  entries: readonly RunHistoryEntry[]
  hasMore: boolean
  nextBeforeExecutionNumber: number | null
}

/** What a page was requested with: the project, the exclusive cursor (null: the first page) and the page bound. */
export interface RunHistoryRequest {
  projectId: string
  beforeExecutionNumber: number | null
  limit: number
}

const LIFECYCLES: readonly string[] = ['Created', 'Running', 'Completed', 'Failed', 'Interrupted', 'Abandoned', 'Unrecognized']
const STAGES: readonly string[] = ['Intake', 'Plan', 'Critique', 'Resolution', 'Execute', 'Completed', 'Unrecognized']
const MODES: readonly string[] = ['Legacy', 'Simulated', 'ManualAgent', 'Unrecognized']
const OBJECT_ID = /^[0-9a-f]{40}$/

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isPositiveInteger = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0
const isInstant = (value: unknown): value is Date => value instanceof Date && !Number.isNaN(value.getTime())

function toSource(runId: string, source: unknown): ReceiptSource | null | undefined {
  if (source === null || source === undefined) {
    return null
  }

  if (typeof source !== 'object') {
    return undefined
  }

  const candidate = source as Record<string, unknown>
  return candidate.runId === runId
    && isText(candidate.operationId)
    && typeof candidate.commitSha === 'string'
    && OBJECT_ID.test(candidate.commitSha)
    && isText(candidate.checkpointId)
    && isPositiveInteger(candidate.checkpointNumber)
    ? {
      runId,
      operationId: candidate.operationId,
      commitSha: candidate.commitSha,
      checkpointId: candidate.checkpointId,
      checkpointNumber: candidate.checkpointNumber,
    }
    : undefined
}

function toEntry(entry: unknown, projectId: string): RunHistoryEntry | null {
  if (!entry || typeof entry !== 'object') {
    return null
  }

  const candidate = entry as Record<string, unknown>
  if (
    candidate.projectId !== projectId
    || !isText(candidate.runId)
    || !isPositiveInteger(candidate.executionNumber)
    || !isText(candidate.objective)
    || typeof candidate.lifecycle !== 'string'
    || !LIFECYCLES.includes(candidate.lifecycle)
    || typeof candidate.stage !== 'string'
    || !STAGES.includes(candidate.stage)
    || typeof candidate.executionMode !== 'string'
    || !MODES.includes(candidate.executionMode)
    || !isInstant(candidate.createdAtUtc)
    || !isInstant(candidate.lastAdvancedAtUtc)
  ) {
    return null
  }

  const source = toSource(candidate.runId, candidate.receiptSource)
  // A source exists only for a run the host recorded as Completed at the Completed stage; any other claim is not coherent.
  if (source === undefined || (source !== null && (candidate.lifecycle !== 'Completed' || candidate.stage !== 'Completed'))) {
    return null
  }

  return {
    projectId,
    runId: candidate.runId,
    executionNumber: candidate.executionNumber,
    objective: candidate.objective,
    lifecycle: candidate.lifecycle as RunHistoryLifecycle,
    stage: candidate.stage as RunHistoryStage,
    executionMode: candidate.executionMode as RunHistoryExecutionMode,
    createdAtUtc: candidate.createdAtUtc,
    lastAdvancedAtUtc: candidate.lastAdvancedAtUtc,
    receiptSource: source,
  }
}

/**
 * Accepts a host page only when the whole of it is coherent for exactly `request` and for the rows already `retained`: the answered
 * project, a page no larger than requested, unique run identities and execution numbers that are safe positive integers in strictly
 * descending order, every one older than the exclusive cursor and than the last retained row, only known (or fixed Unrecognized)
 * lifecycle, stage and mode disclosures, valid instants, a source only for a Completed run and naming that run, and a continuation
 * that is a full page whose next cursor is its last number (or none). Anything else is null and is never repaired, trimmed or
 * partially accepted.
 */
export function toRunHistoryPage(
  response: GetProjectRunHistoryResponse | null | undefined,
  request: RunHistoryRequest,
  retained: readonly RunHistoryEntry[] = [],
): RunHistoryPage | null {
  if (!response || typeof response !== 'object' || response.projectId !== request.projectId || typeof response.hasMore !== 'boolean') {
    return null
  }

  const raw = response.entries
  if (!Array.isArray(raw) || raw.length > request.limit) {
    return null
  }

  const runIds = new Set(retained.map((entry) => entry.runId))
  const numbers = new Set(retained.map((entry) => entry.executionNumber))
  let ceiling = Math.min(
    request.beforeExecutionNumber ?? Number.POSITIVE_INFINITY,
    retained.length > 0 ? retained[retained.length - 1].executionNumber : Number.POSITIVE_INFINITY,
  )
  const entries: RunHistoryEntry[] = []
  for (const item of raw) {
    const entry = toEntry(item, request.projectId)
    if (!entry || runIds.has(entry.runId) || numbers.has(entry.executionNumber) || entry.executionNumber >= ceiling) {
      return null
    }

    runIds.add(entry.runId)
    numbers.add(entry.executionNumber)
    ceiling = entry.executionNumber
    entries.push(entry)
  }

  const next = response.nextBeforeExecutionNumber
  if (!response.hasMore) {
    return next === null || next === undefined ? { entries, hasMore: false, nextBeforeExecutionNumber: null } : null
  }

  return entries.length === request.limit && isPositiveInteger(next) && next === entries[entries.length - 1].executionNumber
    ? { entries, hasMore: true, nextBeforeExecutionNumber: next }
    : null
}

/** The disclosure of a lifecycle or stage the host recorded; a value this version does not recognize is never worded as a known one. */
export const lifecycleLabel = (lifecycle: string): string => (lifecycle === 'Unrecognized' ? 'Unrecognized lifecycle' : lifecycle)
export const stageLabel = (stage: string): string => (stage === 'Unrecognized' ? 'Unrecognized stage' : stage)
