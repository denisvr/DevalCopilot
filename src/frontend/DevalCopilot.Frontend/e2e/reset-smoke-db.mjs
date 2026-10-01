import { removeOwnedDatabase, ROOT_ENV, TOKEN_ENV } from './harness/ownedRoot.ts'

// Runs as the first step of the API webServer command in playwright.config.ts, before dotnet starts. It removes a stale
// smoke database (and its WAL and SHM files) only from the disposable root this test run owns: the root must be verified
// (under the temp directory, harness prefix, and a marker holding this run's token) and the database must be the
// smoke database directly inside it. There is no fallback location; anything else refuses and fails the run.
try {
  removeOwnedDatabase(process.env[ROOT_ENV], process.env[TOKEN_ENV], process.env.DEVALCOPILOT_SMOKE_DB_PATH)
} catch (error) {
  console.error(`Refusing to reset the smoke database: ${error instanceof Error ? error.message : 'unknown failure'}`)
  process.exit(1)
}
