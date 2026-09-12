import { existsSync, unlinkSync } from 'node:fs'
import { join } from 'node:path'

// Runs as the first step of the API's webServer command in playwright.config.ts, before
// dotnet starts (with cwd already set to the API project directory), so this process is the
// only one that could possibly hold the file open at this point: there is no race with the
// API process this same webServer entry is about to start, and Playwright starts webServer
// entries without waiting for globalSetup to finish, so a globalSetup-based deletion cannot
// reliably run before this process opens the file.
for (const suffix of ['', '-wal', '-shm']) {
  const path = join(process.cwd(), `playwright-smoke.db${suffix}`)
  if (existsSync(path)) {
    unlinkSync(path)
  }
}
