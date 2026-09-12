/** The durable event payload is bounded JSON; today it carries only a plain-text summary. */
export function parseEventSummary(payloadJson: string): string {
  try {
    const parsed = JSON.parse(payloadJson) as { summary?: string; objective?: string }
    return parsed.summary ?? parsed.objective ?? payloadJson
  } catch {
    return payloadJson
  }
}
