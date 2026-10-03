import { removeOwnedRoot } from './ownedRoot.ts'

/**
 * Registers the one-time removal of the journey's owned root at process exit — after Playwright has stopped the servers that held
 * it open, and also when the run failed (a failing test only changes the exit code). Removal goes through the ownership check, so
 * a root that does not carry this run's token, or any sibling directory with a matching prefix, is never touched, and a failed
 * verification or removal never changes the run's result.
 */
export function registerExitCleanup(
  root: string,
  token: string,
  processLike: { once(event: 'exit', listener: () => void): unknown } = process,
): void {
  processLike.once('exit', () => {
    try {
      removeOwnedRoot(root, token)
    } catch {
      // A leftover disposable directory under the temp root is harmless.
    }
  })
}
