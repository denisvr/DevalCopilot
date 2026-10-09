export const RUN_ABANDONED_EVENT_TYPE = 'run.abandoned'

/**
 * The durable event payload is bounded JSON; today it carries a plain-text summary or objective. The abandonment event carries only the
 * human reason, so it is worded from its event type: a reason is never taken from an arbitrary payload.
 */
export function parseEventSummary(payloadJson: string, eventType?: string): string {
  try {
    const parsed = JSON.parse(payloadJson) as { summary?: string; objective?: string; reason?: unknown }
    if (eventType === RUN_ABANDONED_EVENT_TYPE && typeof parsed.reason === 'string') {
      return `Run abandoned by the owner. Reason: ${parsed.reason}`
    }
    return parsed.summary ?? parsed.objective ?? payloadJson
  } catch {
    return payloadJson
  }
}
