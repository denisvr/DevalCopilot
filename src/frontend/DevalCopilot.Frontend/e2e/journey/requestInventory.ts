// The POST inventory of one browser journey. The cockpit holds a SignalR connection to `/hubs/run` that the host maps with LongPolling only,
// so the browser keeps issuing POSTs to that path (the negotiate request and every long-poll message) at moments no business action
// controls. They carry notifications, never commands (RunNotificationHub is authorized and broadcast-only with no business method).
// A journey that wants "exactly the release POST and nothing else" therefore must not count them, but it must also never let any OTHER
// write disappear: the notification transport is the one explicitly named exception, everything else is a business POST. The raw record
// stays available, and every index is taken from the business list, so transport traffic can never shift which request is "the release".

/** The notification transport: the hub path itself and anything beneath it (negotiate, long-poll messages). Case-insensitive like the host routing. */
const NOTIFICATION_TRANSPORT = /^\/hubs\/run(\/|$)/i

export const isNotificationTransportPath = (pathname: string): boolean => NOTIFICATION_TRANSPORT.test(pathname)

/** Every POST that is not the notification transport: an unexpected path is a business POST on purpose, so it fails loudly. */
export const isBusinessPost = (pathname: string): boolean => !isNotificationTransportPath(pathname)

export class PostInventory {
  private readonly raw: string[] = []

  /** Records the pathname (no origin, no query) of one POST the page issued. */
  record(pathname: string): void {
    this.raw.push(pathname)
  }

  /** Every POST in order, transport included. */
  all(): readonly string[] {
    return [...this.raw]
  }

  /** Every business POST in order. */
  business(): string[] {
    return this.raw.filter(isBusinessPost)
  }

  /** A position in the raw record; pass it to `businessSince` later. */
  mark(): number {
    return this.raw.length
  }

  /** The business POSTs recorded after `mark`, in order; transport messages never appear and never shift the order. */
  businessSince(mark: number): string[] {
    return this.raw.slice(mark).filter(isBusinessPost)
  }

  /** The transport POSTs recorded after `mark` (evidence that the filter had traffic to ignore, never an assertion subject). */
  transportSince(mark: number): string[] {
    return this.raw.slice(mark).filter(isNotificationTransportPath)
  }
}
