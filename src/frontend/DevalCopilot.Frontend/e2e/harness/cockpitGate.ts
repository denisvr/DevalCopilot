// The delivery gate of the project-selection spec, with an explicit shutdown lifecycle. The spec forwards the page's own
// authenticated cockpit request to the real host and only delays or replaces the delivery of its answer. Because a held answer
// is a pending route handler, the gate must end that work while the browser context is still alive: Playwright disposes a
// route's fetched response with the context, so a handler still reading `response.body()` afterwards fails with "Response has
// been disposed". `shutdown` therefore (1) stops new holds, (2) releases every existing hold, (3) unregisters interception with
// `unrouteAll({ behavior: 'wait' })` — https://playwright.dev/docs/api/class-page#page-unroute-all — and (4) awaits every
// handler this gate started, all before fixture teardown closes the context. Only the Playwright types are structural here so the
// lifecycle can be tested deterministically with fakes (see harness.test.ts); the spec passes the real Page and Route.

export type CockpitMode = 'pass' | 'hold' | 'fail'

export interface GateResponse {
  status(): number
  headers(): Record<string, string>
  body(): Promise<Buffer>
}

export interface GateRoute {
  request(): { url(): string }
  fetch(): Promise<GateResponse>
  fulfill(options: { status: number; contentType?: string; headers?: Record<string, string>; body: Buffer | string }): Promise<void>
}

export interface GatePage {
  route(url: string, handler: (route: GateRoute) => Promise<void> | void): Promise<unknown>
  unrouteAll(options?: { behavior?: 'wait' | 'ignoreErrors' | 'default' }): Promise<void>
}

export const COCKPIT_PATTERN = '**/api/runs/*/cockpit'

export class CockpitGate {
  readonly modes = new Map<string, CockpitMode>()
  readonly held = new Map<string, (() => void)[]>()
  /** Every failure of a forwarding handler, in order. A genuine failure is never swallowed: `shutdown` rethrows them. */
  readonly errors: unknown[] = []

  private closing = false
  private readonly active = new Set<Promise<void>>()

  get isClosing() {
    return this.closing
  }

  /** Starts intercepting the cockpit requests of the page. */
  async attach(page: GatePage) {
    await page.route(COCKPIT_PATTERN, (route) => this.handle(route))
  }

  /** The route handler: tracked so shutdown can await it; its failures stay observable. */
  handle(route: GateRoute): Promise<void> {
    const work = this.forward(route).catch((error: unknown) => {
      this.errors.push(error)
      throw error
    })
    this.active.add(work)
    const forget = () => this.active.delete(work)
    work.then(forget, forget)
    return work
  }

  private async forward(route: GateRoute) {
    const runId = new URL(route.request().url()).pathname.split('/')[3]
    const mode = this.closing ? 'pass' : (this.modes.get(runId) ?? 'pass')
    if (mode === 'fail') {
      await route.fulfill({ status: 500, contentType: 'application/json', body: '{"errors":[{"detail":"Injected cockpit failure."}]}' })
      return
    }
    // The real, authenticated answer of the host; only its delivery can wait.
    const response = await route.fetch()
    const body = await response.body()
    // Checked after the awaits and registered synchronously: shutdown either sees this hold (and releases it) or this handler
    // sees the shutdown (and never holds), so no hold can be registered after the release and wait forever.
    if (mode === 'hold' && !this.closing) {
      await new Promise<void>((release) => {
        const waiting = this.held.get(runId) ?? []
        waiting.push(release)
        this.held.set(runId, waiting)
      })
    }
    // No catch here, on purpose. Playwright settles a route only when its handler returned AND the route was handled, and
    // `unrouteAll({ behavior: 'wait' })` waits for both, except for a handler that throws, which settles at once. A fulfill that
    // fails and is swallowed would return normally with the route still unhandled and leave shutdown pending forever. The failure
    // therefore propagates: `handle` records it, Playwright's wait settles, and `shutdown` rethrows it. A request the page
    // abandoned (cancelled, reloaded, navigated away or closed) is not such a failure: the installed Playwright fulfills it
    // without error (see the real-Chromium scenarios in cockpitGate.test.ts), so no error class is excluded.
    await route.fulfill({ status: response.status(), headers: response.headers(), body })
  }

  release(runId: string) {
    this.held.get(runId)?.splice(0).forEach((release) => release())
  }

  heldCount(runId: string) {
    return this.held.get(runId)?.length ?? 0
  }

  /** Ends all gate work while the page is still alive; throws the first recorded forwarding failure, if any. */
  async shutdown(page: GatePage) {
    this.closing = true
    for (const runId of [...this.held.keys()]) {
      this.release(runId)
    }
    await page.unrouteAll({ behavior: 'wait' })
    await Promise.allSettled([...this.active])
    if (this.errors.length > 0) {
      throw this.errors.length === 1 ? this.errors[0] : new AggregateError(this.errors, 'The cockpit gate recorded forwarding failures.')
    }
  }
}

/**
 * The fixture lifecycle of the gate: it hands a fresh gate to the test and, once the test has finished — passed or failed —
 * shuts it down while the page is still alive. Playwright runs the remainder of a fixture before it tears down the fixtures it
 * depends on, so this runs before the page and its context are closed. A shutdown failure is rethrown so it stays observable.
 */
export async function provideCockpitGate(page: GatePage, provide: (gate: CockpitGate) => Promise<void>): Promise<void> {
  const gate = new CockpitGate()
  try {
    await provide(gate)
  } finally {
    await gate.shutdown(page)
  }
}
