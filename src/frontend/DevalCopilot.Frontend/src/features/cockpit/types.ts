export type ConnectionState = 'connecting' | 'live' | 'reconnecting' | 'disconnected'

export interface CollaborationCard {
  sequence: number
  id: string
  attemptId: string | null
  eventType: string
  actor: string
  summary: string
  occurredAtUtc: string
}

export interface CollaborationTimelineCard {
  sequence: number
  id: string
  attemptId: string | null
  actor: string
  recipient: string
  type: string
  inReplyToMessageId: string | null
  summary: string
  details: string[]
  provenance: string
  occurredAtUtc: string
}
