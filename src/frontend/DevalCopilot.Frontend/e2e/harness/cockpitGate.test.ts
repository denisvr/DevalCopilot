import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { dirname, join } from 'node:path'
import { describe, it } from 'node:test'
import { fileURLToPath } from 'node:url'
import { CockpitGate, provideCockpitGate } from './cockpitGate.ts'
import type { GatePage, GateResponse, GateRoute } from './cockpitGate.ts'

// Deterministic regressions for the project-selection spec's gate lifecycle. A fake page/context models exactly the Playwright
// behavior that matters, taken from the installed Playwright (1.63): a response fetched by a route handler is disposed together
// with the context; a route settles only when its handler returned AND the route itself was handled (a successful `fulfill`, or
// `fallback`), while a handler that throws settles it at once; and `unrouteAll({ behavior: 'wait' })` waits, for the routes that
// had not already thrown when it was called, for that whole settlement, not merely for the handler callback. (An earlier fake
// modelled only the callback, which could not show a handler that returned after a failed fulfill leaving shutdown pending.)
// Every ordering below is driven explicitly: no sleeps, no stress timing; a would-be hang is detected by a bounded count of
// event-loop turns and by the per-test timeout. The same behavior against real Chromium is at the end of this file.

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
  /** Settles when the route was handled (true) or handed on (false), as Playwright's own handling promise does. */
  readonly handling = deferred<boolean>()
  /** Set when a handling call (here `fulfill`) threw, as Playwright marks the route. */
  didThrow = false
  fetchFailure: Error | null = null
  /** A genuine rejection of `fulfill` that is not a closed context (for example invalid arguments). */
  fulfillFailure: Error | null = null

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
    try {
      if (this.context.disposed) {
        throw new Error('route.fulfill: Target page, context or browser has been closed')
      }
      if (this.fulfillFailure) {
        throw this.fulfillFailure
      }
    } catch (error) {
      this.didThrow = true // Playwright marks the route and leaves its handling promise pending
      throw error
    }
    this.fulfilled.push({ status: options.status })
    this.handling.resolve(true)
  }
}

class FakePage implements GatePage {
  readonly context = new FakeContext()
  handlers: ((route: GateRoute) => Promise<void> | void)[] = []
  private readonly invocations = new Set<{ route: FakeRoute; complete: ReturnType<typeof deferred<void>> }>()

  async route(_url: string, handler: (route: GateRoute) => Promise<void> | void) {
    this.handlers.push(handler)
  }

  async unrouteAll(options?: { behavior?: string }) {
    this.handlers = []
    if (options?.behavior === 'wait') {
      // As Playwright does: wait for the whole settlement of each running route, except one that had already thrown.
      const waiting = [...this.invocations].filter((invocation) => !invocation.route.didThrow)
      await Promise.all(waiting.map((invocation) => invocation.complete.promise))
    }
  }

  newRoute() {
    return new FakeRoute(this.context)
  }

  /** The page issues a request: the registered handler starts running (as Playwright would do). */
  dispatch(route: FakeRoute) {
    const handler = this.handlers[0]
    assert.ok(handler, 'no handler is registered')
    const invocation = { route, complete: deferred() }
    this.invocations.add(invocation)
    // Playwright awaits the handler AND the route's handling promise together; a throwing handler settles it at once.
    const settled = Promise.all([route.handling.promise, Promise.resolve(handler(route))])
      .then(([handled]) => handled)
      .finally(() => {
        invocation.complete.resolve()
        this.invocations.delete(invocation)
      })
    settled.catch(() => {}) // Playwright drops this promise too; the tests below await it when they assert on it
    return settled
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

  it('a fulfill failure after shutdown starts settles teardown with that failure and keeps it recorded', TIMEOUT, async () => {
    const page = new FakePage()
    const gate = new CockpitGate()
    await gate.attach(page)
    const route = holdingRoute(page, gate)
    route.fulfillFailure = new Error('route.fulfill: Can specify either body or json parameters')
    const handler = page.dispatch(route)
    await until(() => gate.heldCount(RUN) === 1)

    // Shutdown starts while the answer is held: it releases the hold and unroutes, and only then does the fulfill reject.
    let outcome = 'pending'
    void gate.shutdown(page).then(
      () => {
        outcome = 'drained'
      },
      (error: Error) => {
        outcome = `rejected: ${error.message}`
      },
    )
    await until(() => outcome !== 'pending') // a gate that swallows the failure leaves Playwright's wait pending, so this never holds

    assert.match(outcome, /^rejected: route\.fulfill: Can specify either body or json parameters$/)
    assert.equal(gate.errors.length, 1, 'the genuine failure is recorded, not swallowed')
    assert.equal(gate.heldCount(RUN), 0)
    assert.deepEqual(route.fulfilled, [])
    await assert.rejects(handler, /Can specify either body or json parameters/) // and Playwright's own handling sees it too
  })

  it('a fulfill failure of an injected answer stays observable and cannot strand teardown either', TIMEOUT, async () => {
    const page = new FakePage()
    const gate = new CockpitGate()
    await gate.attach(page)
    gate.modes.set(RUN, 'fail')
    const route = page.newRoute()
    route.fulfillFailure = new Error('route.fulfill: Can specify either body or json parameters')

    await assert.rejects(page.dispatch(route), /Can specify either body or json parameters/)

    assert.equal(gate.errors.length, 1)
    await assert.rejects(gate.shutdown(page), /Can specify either body or json parameters/)
  })

  it('a successful held delivery records nothing and leaves nothing for teardown to report', TIMEOUT, async () => {
    const page = new FakePage()
    const gate = new CockpitGate()
    await gate.attach(page)
    const route = holdingRoute(page, gate)
    const handler = page.dispatch(route)
    await until(() => gate.heldCount(RUN) === 1)

    await gate.shutdown(page) // resolves only once the route itself was handled, not merely once the callback returned
    page.close()

    assert.equal(await handler, true)
    assert.deepEqual(route.fulfilled, [{ status: 200 }])
    assert.deepEqual(gate.errors, [])
  })
})

// The same lifecycle against real Chromium and the installed Playwright, one scenario per child process (see gateRealScenario.ts).
// The child runs with `--unhandled-rejections=warn`, a Node option, so that Playwright's own report of a rejected route handler
// (it drops the handler's promise) is visible on stderr instead of ending the process before the outcome is printed.
describe('the cockpit gate against real Chromium', () => {
  const scenarioPath = join(dirname(fileURLToPath(import.meta.url)), 'gateRealScenario.ts')

  function run(scenario: string) {
    const child = spawnSync(process.execPath, ['--unhandled-rejections=warn', scenarioPath, scenario], { encoding: 'utf8', timeout: 90_000 })
    assert.equal(child.status, 0, `the scenario failed to run: ${child.stderr.slice(0, 400)}`)
    const line = child.stdout.split('\n').filter((entry) => entry.startsWith('{')).at(-1)
    assert.ok(line, 'the scenario printed no result')
    return { result: JSON.parse(line) as Record<string, unknown>, stderr: child.stderr }
  }

  it('delivers a held answer when shutdown releases it and drains without any recorded failure', { timeout: 120_000 }, () => {
    const { result } = run('delivery')

    assert.deepEqual(result, {
      scenario: 'delivery',
      outcome: 'drained',
      fulfillError: null,
      gateErrors: 0,
      held: 0,
      observed: '{"answer":"from the host"}',
    })
  })

  it('drains a held answer whose request the page already cancelled, with no recorded failure', { timeout: 120_000 }, () => {
    const { result } = run('cancelled')

    assert.deepEqual(result, { scenario: 'cancelled', outcome: 'drained', fulfillError: null, gateErrors: 0, held: 0, observed: 'aborted' })
  })

  it('settles teardown with a genuine fulfill failure that begins after shutdown starts, and keeps it observable', { timeout: 120_000 }, () => {
    const { result, stderr } = run('fulfill-failure')

    assert.match(String(result.outcome), /^error:.*Can specify either body or json parameters/, 'teardown settled, with the failure')
    assert.match(String(result.fulfillError), /Can specify either body or json parameters/)
    assert.equal(result.gateErrors, 1, 'the failure is recorded by the gate')
    assert.equal(result.held, 0)
    assert.match(stderr, /Can specify either body or json parameters/, 'Playwright itself reported the rejected handler')
  })
})
