import { randomBytes } from 'node:crypto'
import { defineConfig } from '@playwright/test'
import { registerExitCleanup } from './e2e/harness/journeyRootCleanup'
import { resolveRunRoot, ROOT_ENV, TOKEN_ENV } from './e2e/harness/ownedRoot'

// The browser-driven local collaboration proof: the production frontend, generated client, MVC authentication, mediator,
// SQLite storage, supervisors, provider adapters, process executor, Git/worktree evidence and artifact stores, with only the
// external provider executables replaced by deterministic doubles (tests/.../BrowserJourney). This configuration is separate
// from playwright.config.ts on purpose: it does not import it, so it never creates a second root or secret, and the existing
// real-host suite is unchanged. The two run sequentially (see the test:e2e:all script); both reuse the local ports one after
// the other and neither broadens CORS.

export const JOURNEY_API_URL = 'http://127.0.0.1:5099'
export const JOURNEY_ORIGIN = 'http://127.0.0.1:5173'

// Generated fresh and kept in memory and in this run's process environment only: never a file, an argument, a URL, browser
// storage, a trace, or a provider child (the host clears the environment of every child it starts).
process.env.DEVALCOPILOT_TEST_LAUNCH_SECRET ??= randomBytes(32).toString('hex')

const isWorker = process.env.TEST_WORKER_INDEX !== undefined
const ownedRoot = resolveRunRoot(process.env, isWorker)
process.env[ROOT_ENV] = ownedRoot.root
process.env[TOKEN_ENV] = ownedRoot.token

const CLEANUP = Symbol.for('devalcopilot.e2e.journey.cleanup-registered')
if (!isWorker && !(globalThis as Record<symbol, unknown>)[CLEANUP]) {
  ;(globalThis as Record<symbol, unknown>)[CLEANUP] = true
  // Runs once, after Playwright has stopped the servers that held the root open and also after a failed run; nothing outside the
  // verified owned root is ever deleted.
  registerExitCleanup(ownedRoot.root, ownedRoot.token)
}

// The owned bin directory goes first on the host's own PATH, so discovery resolves the doubles and never an installed
// provider. On Windows the variable may be spelled Path; reuse the existing spelling so the child sees one variable.
const pathKey = Object.keys(process.env).find((key) => key.toUpperCase() === 'PATH') ?? 'PATH'
const hostPath = `${ownedRoot.root}\\bin;${process.env[pathKey] ?? ''}`

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.journey.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 360_000,
  reporter: 'list',
  use: {
    baseURL: JOURNEY_ORIGIN,
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
  webServer: [
    {
      command: '.\\BrowserJourneyHost.exe',
      cwd: '../../../tests/DevalCopilot.Api.IntegrationTests/BrowserJourney/Host/bin/Debug/net10.0',
      url: `${JOURNEY_API_URL}/health`,
      reuseExistingServer: false,
      timeout: 90_000,
      env: {
        [pathKey]: hostPath,
        [ROOT_ENV]: ownedRoot.root,
        [TOKEN_ENV]: ownedRoot.token,
        DEVALCOPILOT_TEST_LAUNCH_SECRET: process.env.DEVALCOPILOT_TEST_LAUNCH_SECRET ?? '',
        DEVALCOPILOT_JOURNEY_API_URL: JOURNEY_API_URL,
        DEVALCOPILOT_JOURNEY_ORIGIN: JOURNEY_ORIGIN,
      },
    },
    {
      command: 'npm run dev -- --port 5173 --strictPort --host 127.0.0.1',
      url: JOURNEY_ORIGIN,
      reuseExistingServer: false,
      timeout: 30_000,
    },
  ],
})
