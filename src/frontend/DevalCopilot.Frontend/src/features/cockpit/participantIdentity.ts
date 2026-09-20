import type { ParticipantIdentityResponse } from '../../api/clients'
import type { ParticipantIdentityView } from './types'

export function toParticipantIdentity(identity: ParticipantIdentityResponse | undefined): ParticipantIdentityView {
  return {
    kind: identity?.kind ?? 'None',
    role: identity?.role ?? null,
    provider: identity?.provider ?? null,
  }
}

function providerLabel(provider: string | null): string | null {
  if (provider === 'ClaudeCode') {
    return 'Claude Code'
  }
  return provider
}

export function formatParticipantIdentity(identity: ParticipantIdentityView): string {
  if (identity.kind !== 'Agent') {
    return identity.kind
  }

  const primary = identity.role ?? 'Agent'
  const provider = providerLabel(identity.provider)
  return provider ? `${primary} · ${provider}` : primary
}
