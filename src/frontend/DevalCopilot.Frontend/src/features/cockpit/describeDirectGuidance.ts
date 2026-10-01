import type { DirectHumanGuidanceResponse } from '../../api/clients'

/** Mirrors the server's bound on accepted direct guidance (UTF-16 code units, after normalization). */
export const DIRECT_GUIDANCE_MAX_LENGTH = 600

export type DirectGuidanceDisplay =
  | { kind: 'provided'; text: string }
  | { kind: 'notRecorded' }
  | { kind: 'unknown' }

/**
 * Presentation of the host-supplied direct-guidance fact of one mutation attempt, or `null` when the
 * response carries no fact (no attempt, or outside the mutation paths). The fact states only what the
 * host put into the attempt's sealed context, never that a provider followed it. `Provided` without
 * acceptable text, and any unrecognised state, is `unknown` and never shows text; `NotRecorded` says only that
 * no direct guidance was recorded, for historical and new unguided attempts alike, never that none was submitted.
 */
export function describeDirectGuidance(fact: DirectHumanGuidanceResponse | null | undefined): DirectGuidanceDisplay | null {
  if (!fact) {
    return null
  }

  switch (fact.state) {
    case 'Provided':
      return typeof fact.text === 'string' && fact.text.trim().length > 0 && fact.text.length <= DIRECT_GUIDANCE_MAX_LENGTH
        ? { kind: 'provided', text: fact.text }
        : { kind: 'unknown' }
    case 'NotRecorded':
      return { kind: 'notRecorded' }
    default:
      return { kind: 'unknown' }
  }
}

export type DirectGuidanceDraftCheck = { ok: true } | { ok: false; reason: 'blank' | 'tooLong' | 'control' }

/** Every C0/C1 control character except newline (a lone carriage return is normalized to a newline first). */
function hasForbiddenControl(text: string): boolean {
  for (let index = 0; index < text.length; index += 1) {
    const code = text.charCodeAt(index)
    if (code !== 0x0a && (code <= 0x1f || (code >= 0x7f && code <= 0x9f))) {
      return true
    }
  }
  return false
}

/**
 * Local feedback consistent with the server's normalization and bound (Unicode form C, LF line
 * endings, trimmed; at most 600 UTF-16 code units; no control characters except a newline). The server
 * stays authoritative; this only prevents sending a blank or visibly invalid draft.
 */
export function checkDirectGuidanceDraft(draft: string): DirectGuidanceDraftCheck {
  const normalized = draft.normalize('NFC').replace(/\r\n?/g, '\n').trim()
  if (normalized.length === 0) {
    return { ok: false, reason: 'blank' }
  }
  if (hasForbiddenControl(normalized)) {
    return { ok: false, reason: 'control' }
  }
  if (normalized.length > DIRECT_GUIDANCE_MAX_LENGTH) {
    return { ok: false, reason: 'tooLong' }
  }
  return { ok: true }
}

/** The length the server counts for a draft (UTF-16 code units of the normalized text). */
export function directGuidanceLength(draft: string): number {
  return draft.normalize('NFC').replace(/\r\n?/g, '\n').trim().length
}
