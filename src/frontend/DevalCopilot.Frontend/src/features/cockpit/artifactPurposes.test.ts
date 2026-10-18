import { describe, expect, it } from 'vitest'
import { AgentAttemptArtifactMetadataResponse } from '../../api/generated/api-client'
import { artifactPurposeRouteSegment, availableArtifactPurposes, isAllowlistedArtifactPurpose } from './artifactPurposes'

const metadata = (purpose: unknown) =>
  new AgentAttemptArtifactMetadataResponse({ purpose: purpose as string, byteLength: 1, truncated: false, captureOutcome: 'Captured' })

const PROTOTYPE_SHAPED = ['toString', 'constructor', '__proto__', 'hasOwnProperty', 'valueOf', 'isPrototypeOf']

describe('artifact purposes', () => {
  it('maps each recorded purpose to the closed route segment both sealed-window routes accept', () => {
    expect(artifactPurposeRouteSegment('AgentContextManifest')).toBe('context-manifest')
    expect(artifactPurposeRouteSegment('AgentStandardOutput')).toBe('stdout')
    expect(artifactPurposeRouteSegment('AgentStandardError')).toBe('stderr')
    expect(artifactPurposeRouteSegment('AgentFinalResponse')).toBe('final-response')
  })

  it.each([
    'ProcessStandardOutput',
    'ProcessStandardError',
    'Unknown',
    '',
    'stderr',
    'agentstandarderror',
    ...PROTOTYPE_SHAPED,
  ])('maps no segment for %j', (purpose) => {
    expect(artifactPurposeRouteSegment(purpose)).toBeNull()
    expect(isAllowlistedArtifactPurpose(purpose)).toBe(false)
  })

  it('offers only the distinct own allowlisted purposes present in the metadata, in its order', () => {
    const artifacts = [
      metadata('AgentFinalResponse'),
      metadata('toString'),
      metadata('ProcessStandardOutput'),
      metadata('AgentStandardError'),
      metadata('AgentFinalResponse'),
      metadata(undefined),
      metadata(null),
      metadata(7),
      metadata('__proto__'),
      metadata('AgentContextManifest'),
    ]

    expect(availableArtifactPurposes(artifacts)).toEqual(['AgentFinalResponse', 'AgentStandardError', 'AgentContextManifest'])
  })

  it('offers nothing for metadata that names only unknown, Process or prototype-shaped purposes', () => {
    expect(availableArtifactPurposes([...PROTOTYPE_SHAPED, 'ProcessStandardError', 'Unknown'].map(metadata))).toEqual([])
  })
})
