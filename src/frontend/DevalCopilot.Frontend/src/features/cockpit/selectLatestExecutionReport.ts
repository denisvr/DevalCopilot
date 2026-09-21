/** Selects only the report identity proven by the backend read model. Timeline cards are display
 * evidence and may lag the durable correction result, so they are deliberately ignored here. */
export function selectLatestExecutionReportMessageId(
  reviewableExecutionReportMessageId: string | null | undefined,
  statusLoading: boolean,
  statusError: string | null | undefined,
): string | null {
  if (statusLoading || statusError) {
    return null
  }
  return reviewableExecutionReportMessageId ?? null
}
