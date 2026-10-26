import type { ProjectVerificationHistoryItem } from './projectVerificationInspection'

/** Test-only builders of the parsed recorded project verification answers the host writes (ADR-0069); every field can be replaced to build a contradiction. */
export const FINGERPRINT = 'a'.repeat(64)
export const OTHER_FINGERPRINT = 'b'.repeat(64)

export const guidOf = (kind: string, n: number): string => {
  const hex = (kind.charCodeAt(0) * 1000 + n).toString(16).padStart(12, '0')
  return `00000000-0000-4000-8000-${hex}`
}

export const PROJECT = guidOf('p', 1)
export const WORKSPACE = guidOf('w', 1)
export const CHECKPOINT = guidOf('c', 1)
export const COMMAND = guidOf('m', 1)

/** A fixed-width UTC round-trip instant `minute` minutes and `second` seconds after 09:00. */
export const instantAt = (minute: number, second = 0, ticks = 0): string =>
  `2026-10-20T09:${String(minute).padStart(2, '0')}:${String(second).padStart(2, '0')}.${String(ticks).padStart(7, '0')}Z`

export const executionIdFor = (number: number) => guidOf('e', number)
export const artifactIdFor = (number: number, stream: 'stdout' | 'stderr') => guidOf(stream === 'stdout' ? 'o' : 'r', number)

/** One listed execution of the history, claimed `number` minutes after 09:00 (so a larger number is a later claim). */
export const itemFor = (number: number, patch: Record<string, unknown> = {}) => ({
  executionId: executionIdFor(number),
  executionNumber: number,
  commandName: `Check ${number}`,
  claimedAtUtc: instantAt(Math.floor(number / 60), number % 60),
  ...patch,
})

/** The listed item as the decoder returns it, for expectations. */
export const indexedItemFor = (number: number, patch: Partial<ProjectVerificationHistoryItem> = {}): ProjectVerificationHistoryItem => ({
  ...(itemFor(number) as ProjectVerificationHistoryItem),
  ...patch,
})

/** A Recorded page of `count` items starting at `first` and descending (the host's order). */
export const pageFor = (first: number, count: number, patch: Record<string, unknown> = {}, projectId: string = PROJECT) => {
  const items = Array.from({ length: count }, (_, index) => itemFor(first - index))
  const last = first - count + 1
  const hasMore = count === 20 && last > 1
  return {
    projectId,
    status: 'Recorded',
    items,
    hasMore,
    nextBeforeExecutionNumber: hasMore ? last : null,
    ...patch,
  }
}

export const emptyPageFor = (projectId: string = PROJECT) => pageFor(1, 0, {}, projectId)

export const unavailablePageFor = (projectId: string = PROJECT) => ({
  projectId,
  status: 'Unavailable',
  items: [],
  hasMore: false,
  nextBeforeExecutionNumber: null,
})

export const recordedStreamFor = (number: number, stream: 'stdout' | 'stderr', patch: Record<string, unknown> = {}) => ({
  status: 'Recorded',
  artifactId: artifactIdFor(number, stream),
  byteLength: 12,
  captureOutcome: 'CapturedWithKnownTruncation',
  truncated: false,
  ...patch,
})

export const notRecordedStream = () => ({
  status: 'NotRecorded',
  artifactId: null,
  byteLength: null,
  captureOutcome: null,
  truncated: null,
})

export const unavailableStream = () => ({
  status: 'Unavailable',
  artifactId: null,
  byteLength: null,
  captureOutcome: null,
  truncated: null,
})

/** The recorded facts of the execution `number`: a Passed, Exited, exit code 0 manual verification by default. */
export const executionFor = (number: number, patch: Record<string, unknown> = {}) => {
  const item = itemFor(number)
  return {
    executionNumber: number,
    commandId: COMMAND,
    commandName: item.commandName,
    workspaceId: WORKSPACE,
    checkpointId: CHECKPOINT,
    checkpointNumber: 3,
    claimFingerprintSha256: FINGERPRINT,
    completionFingerprintSha256: FINGERPRINT,
    status: 'Passed',
    outcome: 'Exited',
    exitCode: 0,
    claimedAtUtc: item.claimedAtUtc,
    dispatchedAtUtc: instantAt(30, 1),
    completedAtUtc: instantAt(30, 9),
    standardOutput: recordedStreamFor(number, 'stdout'),
    standardError: recordedStreamFor(number, 'stderr', { byteLength: 5, truncated: true }),
    ...patch,
  }
}

export const inspectionFor = (
  number: number,
  patch: Record<string, unknown> = {},
  executionPatch: Record<string, unknown> = {},
  projectId: string = PROJECT,
) => ({
  projectId,
  executionId: executionIdFor(number),
  status: 'Recorded',
  execution: executionFor(number, executionPatch),
  ...patch,
})

export const unavailableInspectionFor = (number: number, projectId: string = PROJECT) => ({
  projectId,
  executionId: executionIdFor(number),
  status: 'Unavailable',
  execution: null,
})

/** The window answer of a Recorded stream of the execution `number` (the default 12 byte stdout, read whole). */
export const windowFor = (
  number: number,
  stream: 'stdout' | 'stderr',
  patch: Record<string, unknown> = {},
  projectId: string = PROJECT,
) => {
  const descriptor = stream === 'stdout' ? recordedStreamFor(number, 'stdout') : recordedStreamFor(number, 'stderr', { byteLength: 5, truncated: true })
  const text = stream === 'stdout' ? 'hello output' : 'oops!'
  return {
    projectId,
    executionId: executionIdFor(number),
    stream,
    status: 'Ok',
    artifact: descriptor,
    text,
    nextOffset: descriptor.byteLength,
    totalLength: descriptor.byteLength,
    ...patch,
  }
}

export const refusedWindowFor = (
  number: number,
  stream: 'stdout' | 'stderr',
  status: 'Unavailable' | 'NotRecorded' | 'Missing' | 'IntegrityMismatch',
  fromOffset = 0,
  projectId: string = PROJECT,
) => ({
  projectId,
  executionId: executionIdFor(number),
  stream,
  status,
  artifact: null,
  text: '',
  nextOffset: fromOffset,
  totalLength: 0,
})
