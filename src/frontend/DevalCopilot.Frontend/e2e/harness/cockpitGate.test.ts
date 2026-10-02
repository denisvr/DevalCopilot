import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { CockpitGate, provideCockpitGate } from './cockpitGate.ts'
import type { GatePage, GateResponse, GateRoute } from './cockpitGate.ts'

// Deterministic regressions for the project-selection spec's gate lifecycle. A fake page/context models exactly the Playwright
// behavior that matters: a response fetched by a route handler is disposed together with the context, and `unrouteAll({
// behavior: 'wait' })` waits for the handlers that are running. Every ordering below is driven explicitly — no sleeps, no
// stress timing; a would-be hang is bounded by the per-test timeout.

const RUN = '11111111-1111-1111-1111-111111111111'
const TIMEOUT = { timeout: 3_000 }

function deferred<T = void>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

async function until(condition: () => boolean) {
  for (let turn = 0; turn < 200 && !condition(); turn += 1) {
    await new Promise<void>((resolve) => setImmediate(resolve))
  }
  assert.ok(condition(), 'the awaited state was not reached')
}

class FakeContext {
  disposed = false
}

class FakeRoute implements GateRoute {
  readonly fetched = deferred<void>()
  readonly bodyReady = deferred<void>()
  readonly fulfilled: { status: number }[] = []
  fetchFailure: Error | null = null

  private readonly context: FakeContext

  constructor(context: FakeContext) {
    this.context = context
  }

  request() {
    return { url: () => `http://127.0.0.1/api/runs/${RUN}/cockpit` }
  }

  async fetch(): Promise<GateResponse> {
    await this.fetched.promise
    if (this.fetchFailure) {
      throw this.fetchFailure
    }
    return {
      status: () => 200,
      headers: () => ({}),
      body: async () => {
        await this.bodyReady.promise
        if (this.context.disposed) {
          throw new Error('apiResponse.body: Response has been disposed')
        }
        return Buffer.from('{}')
      },
    }
  }

  async fulfill(options: { status: number }) {
    if (this.context.disposed) {
      throw new Error('route.fulfill: Target page, context or browser has been closed')
    }
    this.fulfilled.push({ status: options.status })
  }
}

class FakePage implements GatePage {
  readonly context = new FakeContext()
  handlers: ((route: GateRoute) => Promise<void> | void)[] = []
  private readonly running: Promise<unknown>[] = []

  async route(_url: string, handler: (route: GateRoute) => Promise<void> | void) {
    this.handlers.push(handler)
  }

  async unrouteAll(options?: { behavior?: string }) {
    this.handlers = []
    if (options?.behavior === 'wait') {
      await Promise.allSettled(this.running)
    }
  }

  newRoute() {
    return new FakeRoute(this.context)
  }

  /** The page issues a request: the registered handler starts running (as Playwright would do). */
  dispatch(route: FakeRoute) {
    const handler = this.handlers[0]
    assert.ok(handler, 'no handler is registered');
    const running = Promise.resolve(handler(route))
    this.running.push(running)
    return running
  }

  close() {
    this.context.disposed = true
  }
}

function holdingRoute(page: FakePage, gate: CockpitGate) {
  gate.modes.set(RUN, 'hold')
  const route = page.newRoute()
  route.fetched.resolve()
  route.bodyReady.resolve()
  return route
}

describe('the cockpit gate lifecycle', () => {
  it('shutdown releases work that is already held, before the context closes', TIMEOUT, async () => {
    const page = new FakePage()
    const gate = new CockpitGate()
    await gate.attach(page)
    const route = holdingRoute(page, gate)
    const handler = page.dispatch(route)
    await until(() => gate.heldCount(RUN) === 1)

    await gate.shutdown(page)
    page.close()

    await handler
    assert.equal(gate.heldCount(RUN), 0)
    assert.deepEqual(route.fulfilled, [{ status: 200 }])
    assert.equal(page.handlers.length, 0, 'interception is unregistered')
    assert.deepEqual(gate.errors, [])
  })

  it('shutdown during fetch/body work never lets a late hold wait forever', TIMEOUT, async () => {
    const page = new FakePage()
    const gate = new CockpitGate()
    await gate.attach(page)
    gate.modes.set(RUN, 'hold')
    const route = page.newRoute()
    const handler = page.dispatch(route)

    // Shutdown begins while the host answer is still being fetched; only afterwards do the fetch and body complete, so the
    // handler would register a hold after the existing holds were released.
    const shutdown = gate.shutdown(page)
    route.fetched.resolve()
    route.bodyReady.resolve()
    await shutdown
    page.close()

    await handler
    assert.equal(gate.heldCount(RUN), 0)
    assert.deepEqual(route.fulfilled, [{ status: 200 }])
    assert.deepEqual(gate.errors, [])
  })

  it('closing the context first reproduces the disposed-response failure; shutting down first does not', TIMEOUT, async () => {
    const legacyPage = new FakePage()
    const legacy = new CockpitGate()
    await legacy.attach(legacyPage)
    legacy.modes.set(RUN, 'pass')
    const legacyRoute = legacyPage.newRoute()
    const legacyHandler = legacyPage.dispatch(legacyRoute)
    legacyRoute.fetched.resolve()
    legacyPage.close() // the old ordering: the fixture teardown closes the context while the handler still reads the body
    legacyRoute.bodyReady.resolve()
    await assert.rejects(legacyHandler, /Response has been disposed/)
    assert.equal(legacy.errors.length, 1, 'the failure is recorded, not hidden')

    const page = new FakePage()
    const gate = new CockpitGate()
    await gate.attach(page)
    gate.modes.set(RUN, 'pass')
    const route = page.newRoute()
    const handler = page.dispatch(route)
    route.fetched.resolve()
    const shutdown = gate.shutdown(page) // the new ordering: drain first, close afterwards
    route.bodyReady.resolve()
    await shutdown
    page.close()
    await handler
    assert.deepEqual(route.fulfilled, [{ status: 200 }])
    assert.deepEqual(gate.errors, [])
  })

  it('cleans up when the test body fails an assertion', TIMEOUT, async () => {
    const page = new FakePage()
    let route!: FakeRoute
    let used!: CockpitGate
    await assert.rejects(
      provideCockpitGate(page, async (gate) => {
        used = gate
        await gate.attach(page)
        route = holdingRoute(page, gate)
        page.dispatch(route)
        await until(() => gate.heldCount(RUN) === 1)
        assert.equal(1, 2, 'an assertion of the test fails here')
      }),
      /an assertion of the test fails here/,
    )

    assert.equal(used.heldCount(RUN), 0, 'existing holds were released')
    assert.equal(page.handlers.length, 0, 'interception was unregistered')
    assert.deepEqual(route.fulfilled, [{ status: 200 }], 'the held handler finished while the context was alive')
    assert.equal(used.isClosing, true)
  })

  it('keeps a genuine live forwarding failure observable', TIMEOUT, async () => {
    const page = new FakePage()
    const gate = new CockpitGate()
    await gate.attach(page)
    gate.modes.set(RUN, 'pass')
    const route = page.newRoute()
    route.fetchFailure = new Error('connect ECONNREFUSED 127.0.0.1:1')
    route.fetched.resolve()

    await assert.rejects(page.dispatch(route), /ECONNREFUSED/)

    assert.equal(gate.errors.length, 1)
    assert.deepEqual(route.fulfilled, [])
    await assert.rejects(gate.shutdown(page), /ECONNREFUSED/)
  })

  it('does not turn the held-answer injection modes into silent passes during normal operation', TIMEOUT, async () => {
    const page = new FakePage()
    const gate = new CockpitGate()
    await gate.attach(page)
    gate.modes.set(RUN, 'fail')
    const route = page.newRoute()

    await page.dispatch(route)

    assert.deepEqual(route.fulfilled, [{ status: 500 }])
    await gate.shutdown(page)
  })
})
