import { randomBytes } from 'node:crypto'
import { defineConfig } from '@playwright/test'

// Fixed local ports for the smoke test's two real, separately started processes: the
// actual .NET API (not a stub) and the actual Vite dev server (not a mocked frontend).
export const API_BASE_URL = 'http://127.0.0.1:5099'
const FRONTEND_URL = 'http://127.0.0.1:5173'

// Generated fresh, in memory, for this test run only — never written to a file, never a
// literal in source. Handed to the API process as an environment variable local to that
// child process (never argv, never a URL), and to the browser page only through
// page.addInitScript in the spec, matching docs/architecture/system-overview.md's rule
// that the test-harness secret must never appear in source, URLs, browser storage, test
// snapshots, traces, or logs.
//
// Playwright loads this config module independently in its main orchestrator process and
// again in each worker process that runs the spec file — a plain module-level
// `randomBytes()` call would produce a different value in each, so the browser and the API
// (started from the main process) would end up with mismatched secrets. Stashing the value
// in `process.env` the first time it is generated means every process that inherited that
// environment (workers are spawned after the main process generates it) reads the same
// already-generated value instead of generating its own.
process.env.DEVALCOPILOT_TEST_LAUNCH_SECRET ??= randomBytes(32).toString('hex')
export const TEST_LAUNCH_SECRET = process.env.DEVALCOPILOT_TEST_LAUNCH_SECRET

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  timeout: 30_000,
  use: {
    baseURL: FRONTEND_URL,
  },
  webServer: [
    {
      // Deletes any stale file-backed SQLite database (including WAL/SHM side files) left
      // over from a previous run before dotnet ever opens it: see reset-smoke-db.mjs.
      command: 'node ../../frontend/DevalCopilot.Frontend/e2e/reset-smoke-db.mjs && dotnet run --no-build --no-launch-profile',
      cwd: '../../backend/DevalCopilot.Api',
      url: `${API_BASE_URL}/health`,
      reuseExistingServer: false,
      timeout: 60_000,
      env: {
        ASPNETCORE_ENVIRONMENT: 'PlaywrightSmoke',
        ASPNETCORE_URLS: API_BASE_URL,
        LaunchSession__Secret: TEST_LAUNCH_SECRET,
      },
    },
    {
      command: 'npm run dev -- --port 5173 --strictPort --host 127.0.0.1',
      url: FRONTEND_URL,
      reuseExistingServer: false,
      timeout: 30_000,
    },
  ],
})
