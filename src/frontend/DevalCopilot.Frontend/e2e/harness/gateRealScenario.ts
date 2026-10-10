import http from 'node:http'
import { chromium } from 'playwright'
import type { Page } from 'playwright'
import { CockpitGate, COCKPIT_PATTERN } from './cockpitGate.ts'

// One scenario of the cockpit gate against REAL Chromium and the installed Playwright, run in its own process by cockpitGate.test.ts
// (which starts it with `--unhandled-rejections=warn` so Playwright's own report of a failed route handler cannot end the process
// before the outcome is printed). The page talks to a local HTTP server, never to the application host. It prints one JSON line:
// how `gate.shutdown(page)` ended within a bounded wait, what the gate recorded, and what the page observed.
//
// `concurrent` is the explicitly ordered reproduction of the shutdown race: two cockpit requests are in flight, one held and one
// waiting for the host; shutdown starts; the held one is released and finishes first; only then does the host answer the other. It
// also reports the order of those events and whether the BROWSER's own copy of the second request reached the host, which happens
// only when Playwright itself continued that route.
//
// Usage: node --unhandled-rejections=warn gateRealScenario.ts <delivery|cancelled|fulfill-failure|concurrent>

const RUN = '11111111-1111-1111-1111-111111111111'
const SLOW_RUN = '22222222-2222-2222-2222-222222222222'
const BOUND_MS = 4_000
const scenario = process.argv[2]
if (scenario !== 'delivery' && scenario !== 'cancelled' && scenario !== 'fulfill-failure' && scenario !== 'concurrent') {
  throw new Error('Unknown scenario.')
}

// The host answer of SLOW_RUN waits for `openSlowHost`. A request sent by the browser itself carries Fetch metadata; the gate's own
// Node-side fetch does not, so the two can be told apart.
const apiHits: Record<string, number> = {}
const browserHits: Record<string, number> = {}
let openSlowHost!: () => void
const slowHostOpen = new Promise<void>((resolve) => {
  openSlowHost = resolve
})
const server = http.createServer(async (request, response) => {
  const cockpit = request.url?.includes('/cockpit') ?? false
  if (cockpit) {
    const run = request.url!.split('/')[3]
    const hits = request.headers['sec-fetch-mode'] === undefined ? apiHits : browserHits
    hits[run] = (hits[run] ?? 0) + 1
    if (run === SLOW_RUN) {
      await slowHostOpen
    }
  }
  response.writeHead(200, { 'Content-Type': cockpit ? 'application/json' : 'text/html' })
  response.end(cockpit ? '{"answer":"from the host"}' : '<html><body>local gate scenario</body></html>')
})

function turn() {
  return new Promise<void>((resolve) => setImmediate(resolve))
}

async function until(condition: () => boolean) {
  const deadline = Date.now() + BOUND_MS
  while (!condition() && Date.now() < deadline) {
    await turn()
  }
  if (!condition()) {
    throw new Error('The awaited state was not reached.')
  }
}

async function runConcurrent(page: Page, gate: CockpitGate, events: string[]) {
  await page.evaluate(
    ([held, slow]) => {
      const w = window as unknown as { pending: Promise<string>[] }
      w.pending = [held, slow].map((run) =>
        fetch(`/api/runs/${run}/cockpit`).then(
          (response) => response.text(),
          () => 'aborted',
        ),
      )
    },
    [RUN, SLOW_RUN],
  )
  await until(() => gate.heldCount(RUN) === 1 && (apiHits[SLOW_RUN] ?? 0) === 1)
  events.push('both-in-flight')

  let outcome = 'pending'
  const shutdown = gate.shutdown(page).then(
    () => {
      outcome = 'drained'
    },
    (error: unknown) => {
      outcome = `error:${(error as Error).message.split('\n')[0]}`
    },
  )
  // The held request finishes first, as its own fulfill completes; one event-loop turn lets Playwright run what follows its handler,
  // and the round trip to the browser is ordered after anything Playwright sent meanwhile.
  await until(() => events.includes('fulfilled:held'))
  await turn()
  await page.evaluate(() => 0)
  events.push('held-finished')
  openSlowHost()

  await Promise.race([shutdown, new Promise<void>((resolve) => setTimeout(resolve, BOUND_MS))])
  const observed = outcome === 'drained' ? await page.evaluate(() => Promise.all((window as unknown as { pending: Promise<string>[] }).pending)) : null
  console.log(
    JSON.stringify({
      scenario,
      outcome,
      events,
      gateErrors: gate.errors.length,
      browserHitsOfSlowRun: browserHits[SLOW_RUN] ?? 0,
      hostHitsOfSlowRun: apiHits[SLOW_RUN] ?? 0,
      observed,
    }),
  )
}

await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
const browser = await chromium.launch({ headless: true })
try {
  const page = await browser.newPage()
  await page.goto(`http://127.0.0.1:${(server.address() as { port: number }).port}/`)

  const gate = new CockpitGate()
  gate.modes.set(RUN, 'hold')
  let fulfillError: string | null = null
  const events: string[] = []
  await page.route(COCKPIT_PATTERN, (route) =>
    gate.handle({
      request: () => route.request(),
      fetch: () => route.fetch(),
      // The fulfill-failure scenario makes Playwright itself reject the call (body and json are mutually exclusive); the gate does
      // not decide what counts as a failure, it only sees the rejection.
      fulfill: async (options) => {
        try {
          const answer = { ...options, body: typeof options.body === 'string' ? options.body : Buffer.from(options.body) }
          await route.fulfill(scenario === 'fulfill-failure' ? { ...answer, json: {} } : answer)
          events.push(route.request().url().includes(SLOW_RUN) ? 'fulfilled:slow' : 'fulfilled:held')
        } catch (error) {
          fulfillError = (error as Error).message.split('\n')[0]
          events.push('fulfill-rejected')
          throw error
        }
      },
    }),
  )
  if (scenario === 'concurrent') {
    await runConcurrent(page, gate, events)
  } else {
    await page.evaluate((run) => {
      const w = window as unknown as { controller: AbortController; pending: Promise<string> }
      w.controller = new AbortController()
      w.pending = fetch(`/api/runs/${run}/cockpit`, { signal: w.controller.signal }).then(
        (response) => response.text(),
        () => 'aborted',
      )
    }, RUN)
    const deadline = Date.now() + BOUND_MS
    while (gate.heldCount(RUN) === 0 && Date.now() < deadline) {
      await new Promise<void>((resolve) => setImmediate(resolve))
    }
    if (gate.heldCount(RUN) === 0) {
      throw new Error('The request was never held.')
    }
    if (scenario === 'cancelled') {
      await page.evaluate(() => (window as unknown as { controller: AbortController }).controller.abort())
    }

    let timer: NodeJS.Timeout | undefined
    const outcome = await Promise.race([
      gate.shutdown(page).then(
        () => 'drained',
        (error: unknown) => `error:${(error as Error).message.split('\n')[0]}`,
      ),
      new Promise<string>((resolve) => {
        timer = setTimeout(() => resolve('pending'), BOUND_MS)
      }),
    ])
    clearTimeout(timer)
    const observed = outcome === 'drained' ? await page.evaluate(() => (window as unknown as { pending: Promise<string> }).pending) : null
    console.log(JSON.stringify({ scenario, outcome, fulfillError, gateErrors: gate.errors.length, held: gate.heldCount(RUN), observed }))
  }
} finally {
  await browser.close()
  await new Promise<void>((resolve) => server.close(() => resolve()))
}
process.exit(0)
