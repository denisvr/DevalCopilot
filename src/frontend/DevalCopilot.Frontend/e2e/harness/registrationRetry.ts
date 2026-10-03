import { isGitUnavailableRefusal } from './readinessRetry.ts'

/** The host's answer to the one registration request a submission produced. */
export interface RegistrationAnswer {
  status: number
  body: string
}

/** The rendered controls the registration goes through; the retry decision never touches the page itself. */
export interface RegistrationDriver {
  /** Opens the form, enters the values and submits ONCE; resolves with the host's answer to that request. */
  submit(name: string, path: string): Promise<RegistrationAnswer>
  /** One bounded wait for the registered project to be rendered. */
  confirmRendered(name: string): Promise<void>
  /** Leaves the form after a refusal, so the next attempt starts from a clean control. */
  dismiss(): Promise<void>
}

export interface RegistrationRetryOptions {
  /** Total submissions, the first included; finite so a permanently unavailable host fails the run instead of hanging it. */
  attempts: number
  delayMs: number
  sleep?: (ms: number) => Promise<void>
}

/**
 * Registers a project through the rendered form. Only the host's Git readiness refusal (HTTP 409 whose first error is exactly
 * `projects.git_unavailable`, the one transient condition at startup) is submitted again, with the same name and path. An accepted
 * registration is never submitted again, however late it renders; every other refusal, transport failure and rendering failure
 * is surfaced at once. Messages are fixed: neither a response body, a request nor a page text can reach them.
 */
export async function registerWithReadinessRetry(
  driver: RegistrationDriver,
  name: string,
  path: string,
  options: RegistrationRetryOptions,
): Promise<void> {
  const sleep = options.sleep ?? ((ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms)))
  for (let attempt = 1; ; attempt += 1) {
    let answer: RegistrationAnswer
    try {
      answer = await driver.submit(name, path)
    } catch {
      throw new Error('The registration request did not complete.')
    }

    if (answer.status >= 200 && answer.status < 300) {
      try {
        await driver.confirmRendered(name)
      } catch {
        throw new Error('The accepted registration was not rendered.')
      }
      return
    }

    if (!isGitUnavailableRefusal({ status: answer.status, response: answer.body })) {
      throw new Error(`The registration was refused (status ${answer.status}).`)
    }
    if (attempt >= options.attempts) {
      throw new Error(`Git stayed unavailable after ${options.attempts} registration attempts.`)
    }

    try {
      await driver.dismiss()
    } catch {
      throw new Error('The registration form could not be closed after a refusal.')
    }
    await sleep(options.delayMs)
  }
}
