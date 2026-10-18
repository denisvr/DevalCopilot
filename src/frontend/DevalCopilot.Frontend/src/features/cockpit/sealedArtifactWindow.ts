import type { SealedAgentArtifactWindowResponse } from '../../api/clients'

/** What one window read asked for, and the sealed length an earlier accepted window of the same lifetime established (if any). */
export interface SealedWindowRequestFacts {
  fromOffset: number
  maxBytes: number
  sealedTotal: number | null
}

/** One host answer, decided whole: an accepted verified window, a closed non-text status, or an answer that is not coherent. */
export type SealedWindowAnswer =
  | { kind: 'ok'; text: string; nextOffset: number; totalLength: number; truncated: boolean | null }
  // NoAgentEvidence / AttemptLinkBroken / PurposeNotAllowlisted / ArtifactNotFound.
  | { kind: 'unavailable' }
  | { kind: 'missing' }
  | { kind: 'integrity-mismatch' }
  | { kind: 'identity-invalid' }
  | { kind: 'incoherent' }

const INCOHERENT: SealedWindowAnswer = { kind: 'incoherent' }

const isByteCursor = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0

/**
 * Decides one sealed-window answer of either route (both return the shared reader's statuses and the artifact store's byte cursor). A
 * closed non-`Ok` status is mapped to its disclosure and nothing else of it is used: its envelope carries no text, and an integrity
 * mismatch legitimately reports the observed file length, so no field of it is required or displayed. An unrecognized status is
 * incoherent.
 *
 * An `Ok` window is accepted only whole and only when coherent for exactly the request:
 * - `text` is a string and `truncated` is a boolean or null;
 * - `nextOffset` and `totalLengthSoFar` are present, nonnegative safe integers with `fromOffset <= nextOffset <= totalLengthSoFar`;
 * - the cursor advances by at most the requested byte cap, and by at least one byte while bytes remain (an empty file, or a read at the
 *   end of the file, legitimately advances by zero);
 * - the sealed length equals the one an earlier accepted window of the same lifetime established.
 *
 * Cursors are UTF-8 byte offsets of the sealed file, and the host decodes malformed UTF-8 with replacement and defers a split
 * codepoint to the next window, so the text is never re-encoded to recompute or require the advance. Only two facts that hold for every
 * UTF-8 decoding are checked: a positive advance decodes to some text and a zero advance to none, and no decoded UTF-16 code unit is
 * produced without consuming at least one byte (`text.length <= advance`).
 */
export function decideSealedWindowAnswer(
  response: SealedAgentArtifactWindowResponse | null | undefined,
  request: SealedWindowRequestFacts,
): SealedWindowAnswer {
  switch (response?.status) {
    case 'Ok':
      return acceptOkWindow(response, request)
    case 'Missing':
      return { kind: 'missing' }
    case 'IntegrityMismatch':
      return { kind: 'integrity-mismatch' }
    case 'AttemptIdentityInvalid':
      return { kind: 'identity-invalid' }
    case 'NoAgentEvidence':
    case 'AttemptLinkBroken':
    case 'PurposeNotAllowlisted':
    case 'ArtifactNotFound':
      return { kind: 'unavailable' }
    default:
      return INCOHERENT
  }
}

function acceptOkWindow(response: SealedAgentArtifactWindowResponse, { fromOffset, maxBytes, sealedTotal }: SealedWindowRequestFacts) {
  // Read as unknown: the generated model only describes the expected wire shape, it does not check it.
  const fields: Partial<Record<'text' | 'truncated' | 'nextOffset' | 'totalLengthSoFar', unknown>> = response
  const { text, truncated, nextOffset, totalLengthSoFar } = fields
  if (
    typeof text !== 'string'
    || (truncated !== null && typeof truncated !== 'boolean')
    || !isByteCursor(nextOffset)
    || !isByteCursor(totalLengthSoFar)
    || nextOffset < fromOffset
    || nextOffset > totalLengthSoFar
    || (sealedTotal !== null && totalLengthSoFar !== sealedTotal)
  ) {
    return INCOHERENT
  }

  const advance = nextOffset - fromOffset
  const bytesRemained = fromOffset < totalLengthSoFar
  if (advance > maxBytes || (bytesRemained && advance === 0) || (advance === 0) !== (text === '') || text.length > advance) {
    return INCOHERENT
  }

  return { kind: 'ok', text, nextOffset, totalLength: totalLengthSoFar, truncated } as const
}
