import http from 'node:http'
import { chromium } from 'playwright'
import { CockpitGate, COCKPIT_PATTERN } from './cockpitGate.ts'

// One scenario of the cockpit gate against REAL Chromium and the installed Playwright, run in its own process by cockpitGate.test.ts
// (which starts it with `--unhandled-rejections=warn` so Playwright's own report of a failed route handler cannot end the process
// before the outcome is printed). The page talks to a local HTTP server, never to the application host. It prints one JSON line:
// how `gate.shutdown(page)` ended within a bounded wait, what the gate recorded, and what the page observed.
//
// Usage: node --unhandled-rejections=warn gateRealScenario.ts <delivery|cancelled|fulfill-failure>

const RUN = '11111111-1111-1111-1111-111111111111'
const BOUND_MS = 4_000
const scenario = process.argv[2]
if (scenario !== 'delivery' && scenario !== 'cancelled' && scenario !== 'fulfill-failure') {
  throw new Error('Unknown scenario.')
}

const server = http.createServer((request, response) => {
  const cockpit = request.url?.includes('/cockpit') ?? false
  response.writeHead(200, { 'Content-Type': cockpit ? 'application/json' : 'text/html' })
  response.end(cockpit ? '{"answer":"from the host"}' : '<html><body>local gate scenario</body></html>')
})
await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
const browser = await chromium.launch({ headless: true })
try {
  const page = await browser.newPage()
  await page.goto(`http://127.0.0.1:${(server.address() as { port: number }).port}/`)

  const gate = new CockpitGate()
  gate.modes.set(RUN, 'hold')
  let fulfillError: string | null = null
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
        } catch (error) {
          fulfillError = (error as Error).message.split('\n')[0]
          throw error
        }
      },
    }),
  )
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
} finally {
  await browser.close()
  await new Promise<void>((resolve) => server.close(() => resolve()))
}
process.exit(0)
