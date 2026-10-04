import type { SealedManifest } from './instructionDelivery.ts'

// The judge of "did every stage receive the attested tracked-file comparison of its own worktree" (ADR-0024): pure, so a regression
// test can prove it detects a raw Git patch (an `index` line, a function-text hunk header), a missing or altered comparison
// statement, a comparison that does not rebuild the real file, a stage that saw the wrong state, and a manifest that was changed
// after sealing. It reads the sealed manifests read back from the owned artifact root and compares them with the files the
// journey itself committed and the doubles edited; it applies the sealed patch to the committed baseline with its own small
// applier, written independently of the host, and never needs a production value.

export interface TrackedExpectation {
  /** The one tracked path the doubles edit. */
  path: string
  /** The exact committed text (the HEAD blob) of that path, as the journey wrote it. */
  baseline: string
  /**
   * The exact current text each claimed stage must have been handed, by contract name; null means the file was unchanged when
   * that stage was claimed, so no tracked change may be delivered for it.
   */
  currentByContract: Readonly<Record<string, string | null>>
  /** The contract whose sealed patch must rebuild the file exactly as the journey reads it from the worktree at the end. */
  finalContract: string
  /** The file's text as the journey reads it from the worktree at the end. */
  finalWorktreeText: string
}

/** The stage that carries no change evidence at all (the planner's first claim). */
const NO_CHANGE_EVIDENCE_CONTRACT = 'Proposal'

const COMPARISON_METHOD = 'host_prefix_suffix_v1'

const lines = (text: string): string[] => text.split(/(?<=\n)/).filter((line) => line.length > 0)

/**
 * Applies the single-file unified patch the host writes to `before`, checking every context and removed line, and returns the
 * result. Throws a readable error on any mismatch. Only what the host's comparison writes is supported: explicit numeric ranges and
 * the "no newline at end of file" marker.
 */
export function applyUnifiedPatch(patch: string, before: string): string {
  const raw = lines(patch)
  let index = 0
  while (index < raw.length && !raw[index].startsWith('@@ ')) {
    index++
  }
  const source = lines(before)
  const output: string[] = []
  let cursor = 0
  while (index < raw.length) {
    const header = /^@@ -(\d+),(\d+) \+(\d+),(\d+) @@\n$/.exec(raw[index])
    if (!header) {
      throw new Error(`not a numeric hunk header: ${JSON.stringify(raw[index])}`)
    }
    index++
    const [oldStart, oldCount, newCount] = [Number(header[1]), Number(header[2]), Number(header[4])]
    const skipTo = oldCount === 0 ? oldStart : oldStart - 1
    if (skipTo < cursor || skipTo > source.length) {
      throw new Error('hunks are out of order or outside the baseline')
    }
    while (cursor < skipTo) {
      output.push(source[cursor++])
    }
    let seenOld = 0
    let seenNew = 0
    while (seenOld < oldCount || seenNew < newCount) {
      const line = raw[index++]
      const marker = index < raw.length && raw[index].startsWith('\\')
      let content = line.slice(1)
      if (marker) {
        if (raw[index] !== '\\ No newline at end of file\n' || !content.endsWith('\n')) {
          throw new Error('malformed no-newline marker')
        }
        content = content.slice(0, -1)
        index++
      }
      if (line[0] === ' ' || line[0] === '-') {
        if (source[cursor] !== content) {
          throw new Error(`a ${line[0] === ' ' ? 'context' : 'removed'} line differs from the baseline: ${JSON.stringify(content)}`)
        }
        if (line[0] === ' ') {
          output.push(source[cursor])
          seenNew++
        }
        cursor++
        seenOld++
      } else if (line[0] === '+') {
        output.push(content)
        seenNew++
      } else {
        throw new Error(`unknown line prefix in ${JSON.stringify(line)}`)
      }
    }
  }
  while (cursor < source.length) {
    output.push(source[cursor++])
  }
  return output.join('')
}

function judgeEvidence(
  manifest: SealedManifest,
  contract: string,
  expected: TrackedExpectation,
  label: string,
  problems: string[],
) {
  let root: Record<string, unknown>
  try {
    root = JSON.parse(manifest.text) as Record<string, unknown>
  } catch {
    problems.push(`${label}: the sealed manifest is not JSON.`)
    return
  }
  if (contract === NO_CHANGE_EVIDENCE_CONTRACT) {
    if ('changeEvidence' in root) {
      problems.push(`${label}: the planning manifest unexpectedly carries change evidence.`)
    }
    return
  }
  const evidence = root.changeEvidence as Record<string, unknown> | undefined
  if (!evidence) {
    problems.push(`${label}: the manifest carries no change evidence.`)
    return
  }
  const wanted = expected.currentByContract[contract]
  if (wanted === undefined) {
    problems.push(`${label}: the journey has no expectation for contract ${contract}.`)
    return
  }
  const changedPaths = (evidence.changedPaths as { Path: string; IndexStatus: string; WorkTreeStatus: string }[] | undefined) ?? []
  const diff = evidence.diff
  if (wanted === null) {
    if (diff !== '' || evidence.diffTruncated !== false || 'diffSelection' in evidence || 'trackedComparison' in evidence) {
      problems.push(`${label}: the file was unchanged at claim time, yet tracked change evidence was delivered.`)
    }
    if (changedPaths.length !== 0) {
      problems.push(`${label}: the checkpoint lists ${changedPaths.length} changed paths, not none.`)
    }
    return
  }

  const comparison = evidence.trackedComparison as { method?: string; notice?: string } | undefined
  if (comparison?.method !== COMPARISON_METHOD) {
    problems.push(`${label}: the manifest does not name the host comparison method.`)
  }
  if (typeof comparison?.notice !== 'string' || !comparison.notice.includes("not Git's minimal or filter-normalized patch")) {
    problems.push(`${label}: the manifest does not state that the comparison is not Git's minimal or filter-normalized patch.`)
  }
  if (changedPaths.length !== 1 || changedPaths[0].Path !== expected.path || changedPaths[0].IndexStatus !== ' ' || changedPaths[0].WorkTreeStatus !== 'M') {
    problems.push(`${label}: the checkpoint does not list exactly the one modified tracked path ${expected.path}.`)
  }
  if (evidence.diffTruncated !== false || 'diffSelection' in evidence) {
    problems.push(`${label}: a single small tracked change is not delivered as an exact, complete comparison.`)
  }
  if (typeof diff !== 'string') {
    problems.push(`${label}: the comparison text is missing.`)
    return
  }
  const header = `diff --git a/${expected.path} b/${expected.path}\n--- a/${expected.path}\n+++ b/${expected.path}\n`
  if (!diff.startsWith(header) || diff.split('\ndiff --git ').length !== 1) {
    problems.push(`${label}: the comparison is not exactly one host-written file block for ${expected.path}.`)
    return
  }
  const body = diff.slice(header.length)
  const bodyLines = lines(body)
  const hunkHeaders = bodyLines.filter((line) => line.startsWith('@@'))
  if (hunkHeaders.length !== 1 || !/^@@ -\d+,\d+ \+\d+,\d+ @@\n$/.test(hunkHeaders[0])) {
    problems.push(`${label}: the hunk header is not a bare numeric range (raw Git function text or metadata leaked in).`)
    return
  }
  if (/^(index |old mode|new mode|similarity)/m.test(diff)) {
    problems.push(`${label}: the comparison carries raw Git patch metadata.`)
  }
  let rebuilt: string
  try {
    rebuilt = applyUnifiedPatch(diff, expected.baseline)
  } catch (error) {
    problems.push(`${label}: the sealed comparison does not apply to the committed baseline: ${(error as Error).message}`)
    return
  }
  if (rebuilt !== wanted) {
    problems.push(`${label}: the sealed comparison rebuilds ${JSON.stringify(rebuilt)}, not the file the stage was handed.`)
  }
  if (contract === expected.finalContract && rebuilt !== expected.finalWorktreeText) {
    problems.push(`${label}: the final stage's comparison does not rebuild the file as it is in the worktree.`)
  }
}

/**
 * Returns the problems found; an empty list means every stage's sealed manifest carries exactly the attested comparison of the one
 * tracked file the doubles edited (none before the implementation, the defect after it, the correction after that), rebuilt from
 * the committed baseline with an independent applier, and the last stage's rebuilds the worktree's own bytes.
 */
export function trackedDeliveryProblems(
  sealedManifests: readonly SealedManifest[],
  contractsInOrder: readonly string[],
  expected: TrackedExpectation,
): string[] {
  const problems: string[] = []
  if (sealedManifests.length !== contractsInOrder.length) {
    problems.push(`${sealedManifests.length} sealed manifests were found, not ${contractsInOrder.length}.`)
  }
  if (!contractsInOrder.includes(expected.finalContract)) {
    problems.push(`The final contract ${expected.finalContract} was never claimed.`)
  }
  contractsInOrder.forEach((contract, index) => {
    const manifest = sealedManifests[index]
    if (manifest) {
      judgeEvidence(manifest, contract, expected, `stage ${index + 1} (${contract})`, problems)
    }
  })
  return problems
}
