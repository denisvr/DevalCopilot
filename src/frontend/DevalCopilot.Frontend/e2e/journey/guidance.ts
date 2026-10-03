import { createHash } from 'node:crypto'

/** The host's normalization of direct guidance (ADR-0015): Unicode form C, line endings to a line feed, surrounding whitespace trimmed.
 * The journey derives the value the host must have sealed from the text a person typed, to compare it with the host's own records. */
export function normalizeGuidance(raw: string): string {
  return raw.normalize('NFC').replace(/\r\n?/g, '\n').trim()
}

export function sha256Hex(text: string): string {
  return createHash('sha256').update(text, 'utf8').digest('hex')
}
