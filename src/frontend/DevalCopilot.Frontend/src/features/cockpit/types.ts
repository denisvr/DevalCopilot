export type ConnectionState = 'connecting' | 'live' | 'reconnecting' | 'disconnected'

export interface ParticipantIdentityView {
  kind: string
  role: string | null
  provider: string | null
}

export interface CollaborationCard {
  sequence: number
  id: string
  attemptId: string | null
  eventType: string
  actor: ParticipantIdentityView
  summary: string
  occurredAtUtc: string
}

export interface CollaborationTimelineCard {
  sequence: number
  id: string
  attemptId: string | null
  actor: ParticipantIdentityView
  recipient: ParticipantIdentityView
  type: string
  inReplyToMessageId: string | null
  summary: string
  details: string[]
  structuredContentJson?: string
  provenance: string
  occurredAtUtc: string
}
