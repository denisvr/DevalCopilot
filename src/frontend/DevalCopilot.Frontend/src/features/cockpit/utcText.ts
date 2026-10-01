/**
 * The generated client revives timestamps as Date objects while a test fixture may supply the wire
 * string; both become the same ISO text, and a missing or invalid value becomes the empty string.
 */
export function toUtcText(value: unknown): string {
  if (value instanceof Date) {
    return Number.isNaN(value.getTime()) ? '' : value.toISOString()
  }
  return typeof value === 'string' ? value : ''
}
