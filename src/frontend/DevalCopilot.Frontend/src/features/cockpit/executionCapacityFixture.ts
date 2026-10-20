import { toExecutionCapacityReading } from './executionCapacity'
import type { ExecutionCapacityReading, ExecutionCapacityState, ExecutionCapacityStatus } from './executionCapacity'

// Test fixtures for the execution capacity read (ADR-0058): complete host answers exactly as the wire carries them, each coherent for the
// status it builds. A test overrides one member to prove that the decoder refuses the incoherent form.

export type CapacityWireBody = Record<string, unknown>

/** The one coherent answer of `status`; Available records `owners` (0 or 1), AtCapacity `owners` (2 or more), Unavailable none. */
export function capacityWireFor(status: ExecutionCapacityStatus, overrides: CapacityWireBody = {}, owners?: number): CapacityWireBody {
  return {
    limit: 2,
    recordedOwners: status === 'Unavailable' ? null : (owners ?? (status === 'Available' ? 0 : 2)),
    status,
    reasonCode: status === 'Available' ? null : status === 'AtCapacity' ? 'execution_capacity_limit_reached' : 'execution_capacity_unavailable',
    ...overrides,
  }
}

export const availableCapacityWire = (owners = 0): CapacityWireBody => capacityWireFor('Available', {}, owners)

export function capacityReadingOf(status: ExecutionCapacityStatus, owners?: number): ExecutionCapacityReading {
  const reading = toExecutionCapacityReading(capacityWireFor(status, {}, owners))
  if (reading === null) {
    throw new Error('The execution capacity fixture is not a coherent answer.')
  }

  return reading
}

/** What `useExecutionCapacity` exposes for a validated reading of `status` (or for no reading at all). */
export function capacityStateFor(
  status: ExecutionCapacityStatus | null,
  overrides: Partial<ExecutionCapacityState> = {},
): ExecutionCapacityState {
  return {
    reading: status === null ? null : capacityReadingOf(status),
    loading: false,
    failure: null,
    refresh: () => undefined,
    ...overrides,
  }
}

/**
 * A validated Available state in the form existing region tests need. It is explicit test data for a region composition, never a default
 * of the components: they require the state and withhold new mutating starts without it.
 */
export const availableCapacityState = (): ExecutionCapacityState => capacityStateFor('Available')
