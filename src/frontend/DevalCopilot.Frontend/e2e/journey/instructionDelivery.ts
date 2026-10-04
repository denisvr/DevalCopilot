import { createHash } from 'node:crypto'
import type { InstructionFile, InvocationEntry } from './journeyEnv.ts'
import { sha256OfText } from './journeyEnv.ts'

// The judge of "did every stage receive its own project's root instructions": pure, so a regression test can prove it detects a
// missing section, another project's files, an altered boundary, wrong or unverified identity, an altered text, and a stage that
// is missing. It reads two independent kinds of evidence — the doubles' allowlisted facts about what they were handed, and the
// sealed context manifests read back from the owned artifact root — and compares both with the files the journey itself wrote to
// the repository. It never needs, and never prints, a production value.

export interface SealedManifest {
  attemptId: string
  /** The sealed manifest exactly as stored. */
  text: string
  /** What the host's artifact row says about it. */
  byteLength: number
  contentHash: string
}

export interface InstructionExpectation {
  /** The files the journey committed to its own repository, in the fixed order. */
  files: readonly InstructionFile[]
  /** The workspace the stages ran in. */
  workspaceId: string
  /** Another repository's files: nothing of them may appear in this journey's evidence. */
  foreignFiles?: readonly InstructionFile[]
}

const same = (left: string | undefined | null, right: string) => left != null && left.toLowerCase() === right.toLowerCase()
const bare = (hash: string) => hash.replace(/^sha256:/i, '').toLowerCase()
const bytesOf = (text: string) => Buffer.byteLength(text, 'utf8')
/** The first line of a file: it names the repository, so it never occurs in another repository's files. */
const distinctiveLine = (file: InstructionFile) => file.text.split(/\r?\n/)[0]

/** The member immediately before the section must be the fixed-boundary member, and nothing legacy may remain. */
function judgeManifest(manifest: SealedManifest, expected: InstructionExpectation, label: string, problems: string[]) {
  const actualBytes = Buffer.from(manifest.text, 'utf8')
  if (actualBytes.length !== manifest.byteLength) {
    problems.push(`${label}: the sealed manifest has ${actualBytes.length} bytes but the host recorded ${manifest.byteLength}.`)
  }
  if (bare(createHash('sha256').update(actualBytes).digest('hex')) !== bare(manifest.contentHash)) {
    problems.push(`${label}: the sealed manifest does not hash to the host's recorded content hash.`)
  }
  let root: Record<string, unknown>
  try {
    root = JSON.parse(manifest.text) as Record<string, unknown>
  } catch {
    problems.push(`${label}: the sealed manifest is not JSON.`)
    return
  }
  const members = Object.keys(root)
  if (members.includes('instructionReferences')) {
    problems.push(`${label}: the manifest still carries the legacy fixed documentation references.`)
  }
  const sectionIndex = members.indexOf('projectInstructionContext')
  if (sectionIndex < 1 || members[sectionIndex - 1] !== 'projectInstructionContextBoundary') {
    problems.push(`${label}: the instruction section is missing or is not directly preceded by its fixed boundary.`)
    return
  }
  const boundary = root.projectInstructionContextBoundary
  if (typeof boundary !== 'string' || !boundary.includes('untrusted repository text') || !boundary.includes('never override or extend')) {
    problems.push(`${label}: the boundary does not state that the section is untrusted and cannot override or extend host authority.`)
  }
  const section = root.projectInstructionContext as Record<string, unknown>
  if (section.version !== 1) {
    problems.push(`${label}: the section version is not 1.`)
  }
  if (!same(section.sourceGitWorkspaceId as string | undefined, expected.workspaceId)) {
    problems.push(`${label}: the section is not bound to the journey's own workspace.`)
  }
  const ownWorkspace = (root.gitWorkspaceId ?? root.workspaceId) as string | undefined
  if (!same(section.sourceGitWorkspaceId as string | undefined, ownWorkspace ?? '')) {
    problems.push(`${label}: the section binding differs from the manifest's own workspace.`)
  }
  if (Buffer.byteLength(JSON.stringify(section), 'utf8') > 12 * 1024) {
    problems.push(`${label}: the serialized section exceeds 12 KiB.`)
  }
  const sources = Array.isArray(section.sources) ? (section.sources as Record<string, unknown>[]) : []
  if (sources.length !== expected.files.length) {
    problems.push(`${label}: the section accounts for ${sources.length} files, not ${expected.files.length}.`)
    return
  }
  expected.files.forEach((file, index) => {
    const source = sources[index]
    if (source.fileName !== file.fileName || source.status !== 'Complete' || source.reason !== null) {
      problems.push(`${label}: ${file.fileName} is not reported as the Complete source in its fixed position.`)
    }
    if (source.text !== file.text) {
      problems.push(`${label}: ${file.fileName} text differs from the bytes the journey committed.`)
    }
    if (source.byteLength !== bytesOf(file.text) || source.sha256 !== sha256OfText(file.text)) {
      problems.push(`${label}: ${file.fileName} length or SHA-256 differs from the committed bytes.`)
    }
  })
  for (const foreign of expected.foreignFiles ?? []) {
    if (manifest.text.includes(distinctiveLine(foreign))) {
      problems.push(`${label}: text of another project's ${foreign.fileName} appears in the manifest.`)
    }
  }
}

/**
 * Returns the problems found; an empty list means every one of the expected stages (one entry per contract, in order) reached its
 * double with a section the double itself verified, and its sealed manifest carries exactly the committed files, bound to this
 * journey's workspace and to nothing from any other repository.
 */
export function instructionDeliveryProblems(
  stageEntries: readonly InvocationEntry[],
  sealedManifests: readonly SealedManifest[],
  expected: InstructionExpectation,
  contractsInOrder: readonly string[],
  logText: string,
): string[] {
  const problems: string[] = []
  if (stageEntries.length !== contractsInOrder.length) {
    problems.push(`The doubles saw ${stageEntries.length} agent stages, not the ${contractsInOrder.length} that were claimed.`)
  }
  if (sealedManifests.length !== contractsInOrder.length) {
    problems.push(`${sealedManifests.length} sealed manifests were found, not ${contractsInOrder.length}.`)
  }
  const names = expected.files.map((file) => file.fileName).join(',')
  const statuses = expected.files.map(() => 'Complete').join(',')
  const lengths = expected.files.map((file) => String(bytesOf(file.text))).join(',')
  const hashes = expected.files.map((file) => sha256OfText(file.text)).join(',')

  contractsInOrder.forEach((contract, index) => {
    const label = `stage ${index + 1} (${contract})`
    const entry = stageEntries[index]
    if (!entry) {
      problems.push(`${label}: the doubles logged no invocation.`)
    } else {
      if ((entry.contract ?? '') !== contract) {
        problems.push(`${label}: the double served ${entry.contract ?? 'no contract'}.`)
      }
      const checks: [string, string | undefined, string][] = [
        ['instructionSection', entry.instructionSection, 'present'],
        ['instructionOrder', entry.instructionOrder, 'boundary-before-section'],
        ['instructionBoundary', entry.instructionBoundary, 'fixed'],
        ['instructionBinding', entry.instructionBinding, 'matches'],
        ['instructionFiles', entry.instructionFiles, names],
        ['instructionStatuses', entry.instructionStatuses, statuses],
        ['instructionByteLengths', entry.instructionByteLengths, lengths],
        ['instructionSha256', entry.instructionSha256, hashes],
        ['instructionTextsVerified', entry.instructionTextsVerified, 'true'],
        ['instructionReferences', entry.instructionReferences, 'absent'],
      ]
      for (const [field, actual, wanted] of checks) {
        if (actual !== wanted) {
          problems.push(`${label}: the double observed ${field} = ${actual ?? 'nothing'}, not ${wanted}.`)
        }
      }
    }
    const manifest = sealedManifests[index]
    if (manifest) {
      judgeManifest(manifest, expected, label, problems)
    }
  })

  // Neither the doubles' log nor any manifest may carry another repository's identity; the log may never carry any instruction text.
  for (const foreign of expected.foreignFiles ?? []) {
    if (logText.includes(sha256OfText(foreign.text))) {
      problems.push(`The doubles' log carries the identity of another project's ${foreign.fileName}.`)
    }
  }
  for (const file of expected.files) {
    const marker = file.text.split('\n').find((line) => line.startsWith('CONVENTIONS-OF-'))
    if (marker && logText.includes(marker)) {
      problems.push(`The doubles' log carries text of ${file.fileName}.`)
    }
    if (logText.includes(file.text.slice(0, 24))) {
      problems.push(`The doubles' log carries the beginning of ${file.fileName}.`)
    }
  }
  return problems
}
