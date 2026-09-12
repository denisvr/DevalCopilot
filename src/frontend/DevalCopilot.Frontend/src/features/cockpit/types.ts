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
