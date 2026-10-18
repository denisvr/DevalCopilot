import { attemptIdFor } from './agentAttemptHistoryFixture'
import type { VerificationWindowRequestFacts } from './agentAttemptVerification'

/** Test-only builders of the parsed recorded-verification answers the host writes (ADR-0052); every field can be replaced to build a contradiction. */
export type VerificationContract = 'ImplementationReview' | 'VerificationDiagnosis'

export const commandIdFor = (runId: string, attemptNumber: number, position: number) => `${runId}-attempt-${attemptNumber}-command-${position}`
export const executionIdFor = (runId: string, attemptNumber: number, position: number) => `${runId}-attempt-${attemptNumber}-execution-${position}`
export const artifactIdFor = (runId: string, attemptNumber: number, position: number, stream: 'stdout' | 'stderr') =>
  `${runId}-attempt-${attemptNumber}-artifact-${position}-${stream}`

export const notRecordedStream = () => ({ status: 'NotRecorded', artifactId: null, byteLength: null, captureOutcome: null, truncated: null })
export const unavailableStream = () => ({ status: 'Unavailable', artifactId: null, byteLength: null, captureOutcome: null, truncated: null })
export const recordedStream = (artifactId: string, byteLength: number, truncated: boolean | null = false) => ({
  status: 'Recorded',
  artifactId,
  byteLength,
  captureOutcome: truncated === null ? 'RecoveredAfterHostInterruption' : 'CapturedWithKnownTruncation',
  truncated,
})

export const verificationMemberFor = (
  runId: string,
  attemptNumber: number,
  position: number,
  status: 'Passed' | 'Failed' = 'Passed',
  patch: Record<string, unknown> = {},
) => ({
  sequence: position,
  commandId: commandIdFor(runId, attemptNumber, position),
  executionId: executionIdFor(runId, attemptNumber, position),
  executionNumber: position + 1,
  commandName: `Command ${position + 1}`,
  status,
  exitCode: status === 'Passed' ? 0 : 1,
  claimedAtUtc: `2026-10-09T10:${String(position % 60).padStart(2, '0')}:00.1234567Z`,
  dispatchedAtUtc: `2026-10-09T10:${String(position % 60).padStart(2, '0')}:01.1234567Z`,
  completedAtUtc: `2026-10-09T10:${String(position % 60).padStart(2, '0')}:02.1234567Z`,
  standardOutput: recordedStream(artifactIdFor(runId, attemptNumber, position, 'stdout'), 120),
  standardError: notRecordedStream(),
  ...patch,
})

/** The selected CodeReviewer attempt the answer is checked against. */
export const verificationHeaderFor = (
  runId: string,
  attemptNumber: number,
  contract: VerificationContract = 'ImplementationReview',
  patch: Record<string, unknown> = {},
) => ({
  id: attemptIdFor(runId, attemptNumber),
  attemptNumber,
  role: 'CodeReviewer',
  provider: 'Codex',
  responseContract: contract,
  ...patch,
})

/** A Recorded answer with one member per entry of `statuses` (default: one Passed member of an ordinary review). */
export const recordedVerificationFor = (
  runId: string,
  attemptNumber: number,
  statuses: readonly ('Passed' | 'Failed')[] = ['Passed'],
  contract: VerificationContract = statuses.includes('Failed') ? 'VerificationDiagnosis' : 'ImplementationReview',
  patch: Record<string, unknown> = {},
) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  status: 'Recorded',
  attempt: verificationHeaderFor(runId, attemptNumber, contract),
  checkpoint: { id: `${runId}-checkpoint`, checkpointNumber: 3 },
  memberTotalCount: statuses.length,
  members: statuses.map((status, position) => verificationMemberFor(runId, attemptNumber, position, status)),
  ...patch,
})

export const unavailableVerificationFor = (runId: string, attemptNumber: number, patch: Record<string, unknown> = {}) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  status: 'Unavailable',
  attempt: null,
  checkpoint: null,
  memberTotalCount: null,
  members: [],
  ...patch,
})

/** What a history-fixture CodeReviewer attempt's answer is checked against. */
export const verificationExpectationFor = (runId: string, attemptNumber: number, contract: VerificationContract = 'ImplementationReview') => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  attemptNumber,
  role: 'CodeReviewer' as const,
  provider: 'Codex' as const,
  responseContract: contract,
})

export const verificationWindowRequest = (
  patch: Partial<VerificationWindowRequestFacts> = {},
): VerificationWindowRequestFacts => ({
  runId: 'run-1',
  attemptId: attemptIdFor('run-1', 2),
  executionId: executionIdFor('run-1', 2, 0),
  stream: 'stdout',
  descriptor: recordedStream(artifactIdFor('run-1', 2, 0, 'stdout'), 120) as VerificationWindowRequestFacts['descriptor'],
  fromOffset: 0,
  maxBytes: 16384,
  ...patch,
})

/** A verified window answer of ASCII `text` read from `fromOffset` of an output of `total` bytes (characters equal bytes). */
export const verificationWindowAnswer = (
  request: Pick<VerificationWindowRequestFacts, 'runId' | 'attemptId' | 'executionId' | 'stream' | 'descriptor'>,
  text: string,
  fromOffset = 0,
  patch: Record<string, unknown> = {},
) => ({
  runId: request.runId,
  attemptId: request.attemptId,
  executionId: request.executionId,
  stream: request.stream,
  status: 'Ok',
  artifact: { ...request.descriptor },
  text,
  nextOffset: fromOffset + text.length,
  totalLength: request.descriptor.byteLength,
  ...patch,
})

export const refusedVerificationWindow = (
  request: Pick<VerificationWindowRequestFacts, 'runId' | 'attemptId' | 'executionId' | 'stream'>,
  status: 'Unavailable' | 'NotRecorded' | 'Missing' | 'IntegrityMismatch',
  fromOffset = 0,
  patch: Record<string, unknown> = {},
) => ({
  runId: request.runId,
  attemptId: request.attemptId,
  executionId: request.executionId,
  stream: request.stream,
  status,
  artifact: null,
  text: '',
  nextOffset: fromOffset,
  totalLength: 0,
  ...patch,
})
