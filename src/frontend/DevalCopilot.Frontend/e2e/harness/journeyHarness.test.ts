import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { after, describe, it } from 'node:test'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { createJourneyRepository, repositorySnapshot } from '../journey/journeyEnv.ts'
import { planIdentityProblems } from '../journey/planIdentity.ts'
import { registerWithReadinessRetry } from './registrationRetry.ts'
import type { RegistrationAnswer, RegistrationDriver } from './registrationRetry.ts'
import type { InvocationEntry } from '../journey/journeyEnv.ts'
import { createOwnedRoot, MARKER_FILE, ROOT_ENV, ROOT_PREFIX, TOKEN_ENV } from './ownedRoot.ts'

// Regressions for the browser journey's own support code: the plan-identity judge (a Planner-root substitution for the revised
// Proposal must be detected), the cleanup of the owned root after a FAILED run, and the disposable source repository. Every
// directory is created under the OS temp directory and removed in `after`.

const here = dirname(fileURLToPath(import.meta.url))
const cleanupModule = join(here, 'journeyRootCleanup.ts')
const created: string[] = []
after(() => {
  for (const path of created) {
    rmSync(path, { recursive: true, force: true })
  }
})

const ROOT = '11111111-1111-1111-1111-111111111111'
const REVISED = '22222222-2222-2222-2222-222222222222'
const plan = { rootId: ROOT, revisedId: REVISED }

function stage(contract: string, planMessageId: string, planMarker = 'REVISED-PLAN'): InvocationEntry {
  return { role: 'codex', kind: 'exec', contract, planMessageId, planMarker }
}

const healthy: InvocationEntry[] = [
  stage('ImplementationReport', REVISED.toUpperCase()),
  stage('VerificationDiagnosis', REVISED),
  stage('ImplementationReview', REVISED),
]

describe('the plan-identity judge', () => {
  it('accepts diagnosis and review evidence that saw the revised Proposal, whatever the GUID casing', () => {
    assert.deepEqual(planIdentityProblems(healthy, plan), [])
  })

  it('detects the Planner root substituted for the revised Proposal in any plan-bearing stage', () => {
    for (const index of [0, 1, 2]) {
      const substituted = healthy.map((entry, position) => (position === index ? { ...entry, planMessageId: ROOT } : entry))
      const problems = planIdentityProblems(substituted, plan)
      assert.equal(problems.length, 1, `stage ${index}: ${problems.join(' | ')}`)
      assert.match(problems[0], /Planner root instead of the revised Proposal/)
    }
  })

  it('detects an unknown plan, a plan whose content is not the revised one, and a missing stage', () => {
    const unknown = healthy.map((entry, position) => (position === 1 ? { ...entry, planMessageId: '33333333-3333-3333-3333-333333333333' } : entry))
    assert.match(planIdentityProblems(unknown, plan).join(' '), /neither the revised Proposal nor the Planner root/)

    const rootContent = healthy.map((entry, position) => (position === 2 ? { ...entry, planMarker: 'ROOT-PLAN' } : entry))
    assert.match(planIdentityProblems(rootContent, plan).join(' '), /did not see the revised plan's content/)

    assert.match(planIdentityProblems(healthy.slice(0, 2), plan).join(' '), /No ImplementationReview stage reached the doubles/)
  })

  it('refuses to discriminate when the root and the revised Proposal are the same message', () => {
    assert.match(planIdentityProblems(healthy, { rootId: REVISED, revisedId: REVISED }).join(' '), /cannot discriminate/)
  })
})

describe('the owned-root cleanup after a failing run', () => {
  function runChild(root: string, token: string, exitCode: number) {
    // A real child process stands in for a Playwright run that fails: it registers the exit cleanup and exits non-zero.
    const directory = mkdtempSync(join(tmpdir(), 'journey-cleanup-child-'))
    created.push(directory)
    const script = join(directory, 'child.mjs')
    writeFileSync(
      script,
      [
        `import { registerExitCleanup } from ${JSON.stringify(pathToFileURL(cleanupModule).href)}`,
        'registerExitCleanup(process.argv[2], process.argv[3])',
        'process.exit(Number(process.argv[4]))',
      ].join('\n'),
    )
    return spawnSync(process.execPath, [script, root, token, String(exitCode)], { encoding: 'utf8' })
  }

  it('removes the verified owned root even though the run itself failed', () => {
    const owned = createOwnedRoot()
    created.push(owned.root)
    writeFileSync(join(owned.root, 'leftover.txt'), 'x')

    const child = runChild(owned.root, owned.token, 1)

    assert.equal(child.status, 1, 'the failing exit code is preserved')
    assert.equal(existsSync(owned.root), false)
  })

  it('never removes a root that does not carry this run token, or a sibling directory with a matching prefix', () => {
    const owned = createOwnedRoot()
    created.push(owned.root)
    const sibling = mkdtempSync(join(tmpdir(), ROOT_PREFIX))
    created.push(sibling)
    writeFileSync(join(sibling, MARKER_FILE), 'some other run token')
    writeFileSync(join(sibling, 'keep.txt'), 'keep')

    runChild(owned.root, 'not-this-runs-token', 1)
    runChild(sibling, owned.token, 1)

    assert.equal(existsSync(join(owned.root, MARKER_FILE)), true)
    assert.equal(existsSync(join(sibling, 'keep.txt')), true)
  })
})

describe('the disposable source repository', () => {
  it('commits a stub candidate file with line-ending conversion disabled, and snapshots it without changing it', () => {
    const owned = createOwnedRoot()
    created.push(owned.root)
    const previous = { root: process.env[ROOT_ENV], token: process.env[TOKEN_ENV] }
    process.env[ROOT_ENV] = owned.root
    process.env[TOKEN_ENV] = owned.token
    try {
      mkdirSync(join(owned.root, 'repos'), { recursive: true })
      const repository = createJourneyRepository('regression-source')

      const snapshot = repositorySnapshot(repository.path)
      assert.equal(snapshot.head, repository.baselineCommit)
      assert.equal(snapshot.status, '')
      assert.match(snapshot.candidate, /return 0;/)
      assert.deepEqual(repositorySnapshot(repository.path), snapshot)
      const config = spawnSync('git', ['-C', repository.path, 'config', '--local', '--get', 'core.autocrlf'], { encoding: 'utf8' })
      assert.equal(config.stdout.trim(), 'false')
    } finally {
      process.env[ROOT_ENV] = previous.root
      process.env[TOKEN_ENV] = previous.token
    }
  })
})

describe('the registration retry', () => {
  const GIT_UNAVAILABLE = { status: 409, body: JSON.stringify({ errors: [{ code: 'projects.git_unavailable', detail: 'Git is not currently available on this host.' }] }) }
  const ACCEPTED = { status: 200, body: '{"projectId":"p"}' }
  const options = { attempts: 4, delayMs: 7 }

  // A scripted driver: every submission is recorded, so a repeated mutation is visible, and the form state is never real.
  function scripted(answers: (RegistrationAnswer | Error)[], confirm: () => Promise<void> = () => Promise.resolve()) {
    const log = { submits: [] as [string, string][], dismissals: 0, confirmations: 0, sleeps: [] as number[] }
    const driver: RegistrationDriver = {
      submit: (name, path) => {
        log.submits.push([name, path])
        const next = answers.shift()
        return next instanceof Error ? Promise.reject(next) : Promise.resolve(next ?? ACCEPTED)
      },
      confirmRendered: () => {
        log.confirmations += 1
        return confirm()
      },
      dismiss: () => {
        log.dismissals += 1
        return Promise.resolve()
      },
    }
    const sleep = (ms: number) => {
      log.sleeps.push(ms)
      return Promise.resolve()
    }
    return { log, run: () => registerWithReadinessRetry(driver, 'Fixture', String.raw`C:\root\repos\source`, { ...options, sleep }) }
  }

  it('registers once and reads the result without any further submission when the host accepts at once', async () => {
    const { log, run } = scripted([ACCEPTED])
    await run()
    assert.equal(log.submits.length, 1)
    assert.equal(log.confirmations, 1)
    assert.equal(log.dismissals, 0)
  })

  it('retries only the known Git readiness refusal, with the same repository path, and then succeeds', async () => {
    const { log, run } = scripted([GIT_UNAVAILABLE, GIT_UNAVAILABLE, ACCEPTED])
    await run()
    assert.equal(log.submits.length, 3)
    assert.deepEqual(new Set(log.submits.map(([name, path]) => `${name}|${path}`)).size, 1)
    assert.equal(log.dismissals, 2)
    assert.deepEqual(log.sleeps, [7, 7])
    assert.equal(log.confirmations, 1)
  })

  it('surfaces an unrelated refusal immediately, without another mutation, and without echoing the response body', async () => {
    const refusals: RegistrationAnswer[] = [
      { status: 409, body: JSON.stringify({ errors: [{ code: 'projects.duplicate_path', detail: 'SENSITIVE-DETAIL' }] }) },
      { status: 409, body: JSON.stringify({ errors: [{ code: 'projects.other' }, { code: 'projects.git_unavailable' }] }) },
      { status: 409, body: 'not json SENSITIVE-DETAIL' },
      { status: 400, body: '{"errors":[]}' },
      { status: 500, body: 'SENSITIVE-DETAIL' },
    ]
    for (const refusal of refusals) {
      const { log, run } = scripted([refusal, ACCEPTED])
      await assert.rejects(run, (error: Error) => {
        assert.equal(error.message, `The registration was refused (status ${refusal.status}).`)
        assert.doesNotMatch(error.message, /SENSITIVE/)
        return true
      })
      assert.equal(log.submits.length, 1, `${refusal.status}: no second mutation`)
      assert.equal(log.confirmations, 0)
    }
  })

  it('surfaces a transport failure once with a fixed message that carries none of the failure', async () => {
    const { log, run } = scripted([new Error('connect refused; Authorization: Bearer SENSITIVE-SECRET')])
    await assert.rejects(run, (error: Error) => {
      assert.equal(error.message, 'The registration request did not complete.')
      assert.equal(error.cause, undefined)
      return true
    })
    assert.equal(log.submits.length, 1)
    assert.equal(log.dismissals, 0)
  })

  it('never resubmits an accepted registration whose rendering is delayed, and does not retry a rendering that never happens', async () => {
    let released!: () => void
    const delayed = new Promise<void>((resolve) => {
      released = resolve
    })
    const slow = scripted([ACCEPTED], () => delayed)
    const pending = slow.run()
    await new Promise<void>((resolve) => setImmediate(resolve))
    assert.equal(slow.log.submits.length, 1)
    released()
    await pending
    assert.equal(slow.log.submits.length, 1)

    const never = scripted([ACCEPTED, ACCEPTED], () => Promise.reject(new Error('locator resolved to nothing; SENSITIVE-PAGE-TEXT')))
    await assert.rejects(never.run, (error: Error) => {
      assert.equal(error.message, 'The accepted registration was not rendered.')
      return true
    })
    assert.equal(never.log.submits.length, 1)
    assert.equal(never.log.dismissals, 0)
  })

  it('fails with a fixed message once the finite budget is spent, after exactly that many submissions', async () => {
    const { log, run } = scripted([GIT_UNAVAILABLE, GIT_UNAVAILABLE, GIT_UNAVAILABLE, GIT_UNAVAILABLE, ACCEPTED])
    await assert.rejects(run, { message: 'Git stayed unavailable after 4 registration attempts.' })
    assert.equal(log.submits.length, 4)
    assert.equal(log.sleeps.length, 3)
    assert.equal(log.confirmations, 0)
  })
})
