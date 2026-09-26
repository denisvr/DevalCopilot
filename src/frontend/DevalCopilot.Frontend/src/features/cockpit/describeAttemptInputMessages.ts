import { typeLabelFor } from './collaborationMessageTypeLabel'
import type { CollaborationTimelineCard } from './types'

/** One recorded collaboration input the producing Agent attempt was launched against.
 * Structurally matches the generated `AttemptInputMessageEvidenceResponse`. Carries no message
 * content of its own — only `collaborationMessageId` cross-references the caller's own already-
 * loaded timeline, and presence there is never assumed. */
export interface AttemptInputMessageEvidenceView {
  sequence?: number
  collaborationMessageId?: string
  type?: string
  collaborationMessageSequence?: number
  occurredAtUtc?: Date
}

/** The bounded, honest evidence this drill-down shows for one attempt's recorded collaboration-
 * input set. Structurally matches the relevant subset of the generated
 * `CollaborationMessageEvidenceResponse`. */
export interface AttemptInputMessagesEvidenceView {
  inputMessagesStatus?: string
  inputMessages?: AttemptInputMessageEvidenceView[]
  inputMessagesOmitted?: boolean
  inputMessageTotalCount?: number
}

/**
 * Describes one recorded input's own line: its ordered position and type, plus — only when the
 * referenced message is present in the caller's own currently loaded timeline window — that
 * message's already-displayed summary. A reference this caller cannot currently resolve is
 * described as such, using the same "not present in the currently loaded timeline" phrasing as the
 * reply-parent verification elsewhere in this cockpit (see `AgentCollaboration.tsx`), never
 * silently rendered as if it were visible.
 */
export function describeAttemptInputMessage(
  entry: AttemptInputMessageEvidenceView,
  cardsById: ReadonlyMap<string, CollaborationTimelineCard>,
): string {
  const position = `${(entry.sequence ?? 0) + 1}. ${typeLabelFor(entry.type ?? '')}`

  if (!entry.collaborationMessageId) {
    return `${position}: no referenced message identity.`
  }

  const card = cardsById.get(entry.collaborationMessageId)
  if (!card) {
    return `${position}: not present in the currently loaded timeline; the relationship is not verified here.`
  }

  return `${position}: ${card.summary}`
}

/**
 * Describes the non-`Recorded` states honestly, or returns `null` for `Recorded` (whose ordered
 * entries are rendered individually instead). Never claims a bounded-cap omission for `Empty` — a
 * legitimate attempt with no recorded inputs (e.g. a Planner) is not the same as an unverifiable
 * set, and neither state is ever confused with the other.
 */
export function describeAttemptInputMessagesStatus(status: string | null | undefined): string | null {
  switch (status) {
    case 'Empty':
      return 'No recorded collaboration inputs — this attempt started from none.'
    case 'Invalid':
      return "This attempt's recorded collaboration inputs could not be verified and are not shown."
    case 'Recorded':
      return null
    default:
      return 'Recorded collaboration inputs unavailable.'
  }
}
