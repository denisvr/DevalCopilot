import { execFileSync } from 'node:child_process'
import { linkSync, mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import {
  CaptureGitWorkspaceCheckpointEndpointClient,
  PrepareRepositoryWorkspaceEndpointClient,
  RegisterProjectEndpointClient,
  RegisterProjectRequest,
} from '../src/api/generated/api-client'
import { API_BASE_URL, E2E_ROOT } from '../playwright.config'
import { retryOnGitUnavailable } from './harness/readinessRetry'
import { authorizedHttp } from './planningAuthorizationFixture'

// OWNED FIXTURE for the inspection of a checkpoint whose tracked file is a second name of a file OUTSIDE the isolated workspace.
// Everything is created beneath the root owned by this run: a disposable repository, a fictitious outside file, and the
// project, workspace and checkpoint through the PUBLIC operations of the real host (generated client, normal authentication).
// Only the workspace files are edited directly, because no public operation changes source: an edit, and a committed file
// replaced by a real hard link to the outside file. No provider and no verification process is ever started.

/** Clearly fictitious text. It must never appear in any response of the host or anywhere in the rendered page. */
export const OUTSIDE_SENTINEL = 'OUTSIDE-SENTINEL-e2e-4c1d: fictitious bytes that live outside the owned workspace.'

export interface CheckpointInspectionFixture {
  projectName: string
  projectId: string
  checkpointId: string
  workspacePath: string
  safePath: string
  unsafePath: string
}

function git(repository: string, ...args: string[]): void {
  execFileSync('git', ['-C', repository, ...args], { stdio: 'pipe' })
}

export async function createCheckpointInspectionFixture(name: string): Promise<CheckpointInspectionFixture> {
  const repository = join(E2E_ROOT, 'repos', name)
  mkdirSync(join(repository, 'src'), { recursive: true })
  writeFileSync(join(repository, 'src', 'safe.txt'), 'alpha\nbeta\ngamma\n')
  writeFileSync(join(repository, 'src', 'linked.txt'), 'committed text of the linked file\n')
  git(repository, 'init', '--initial-branch', 'main')
  git(repository, 'config', 'core.autocrlf', 'false')
  git(repository, 'add', '-A')
  git(repository, '-c', 'user.name=Fixture Author', '-c', 'user.email=fixture@example.invalid', 'commit', '-m', 'Initial commit')

  const projectName = `${name} fixture`
  const projectId = (
    await retryOnGitUnavailable(
      () => new RegisterProjectEndpointClient(API_BASE_URL, authorizedHttp).registerProject(new RegisterProjectRequest({ name: projectName, path: repository })),
      { attempts: 45, delayMs: 1_000 },
    )
  ).projectId!
  const workspacePath = (
    await new PrepareRepositoryWorkspaceEndpointClient(API_BASE_URL, authorizedHttp).prepareRepositoryWorkspace(projectId)
  ).workspacePath!

  const outsidePath = join(E2E_ROOT, 'outside', `${name}.txt`)
  mkdirSync(dirname(outsidePath), { recursive: true })
  writeFileSync(outsidePath, OUTSIDE_SENTINEL)
  writeFileSync(join(workspacePath, 'src', 'safe.txt'), 'alpha\nBETA EDITED IN THE WORKSPACE\ngamma\n')
  rmSync(join(workspacePath, 'src', 'linked.txt'))
  linkSync(outsidePath, join(workspacePath, 'src', 'linked.txt'))

  const checkpoint = await new CaptureGitWorkspaceCheckpointEndpointClient(API_BASE_URL, authorizedHttp).captureGitWorkspaceCheckpoint(projectId)
  return {
    projectName,
    projectId,
    checkpointId: checkpoint.checkpointId!,
    workspacePath,
    safePath: 'src/safe.txt',
    unsafePath: 'src/linked.txt',
  }
}
