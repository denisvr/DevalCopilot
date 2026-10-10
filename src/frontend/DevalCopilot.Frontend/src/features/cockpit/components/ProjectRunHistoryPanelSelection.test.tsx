import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { projectRunHistoryClient } from '../../../api/clients'
import { entryFor, pageOf } from '../projectRunHistoryFixture'
import { ProjectRunHistoryPanel } from './ProjectRunHistoryPanel'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectRunHistoryClient: vi.fn(),
}))

// The detail is replaced by a stub that retains every close callback the panel ever handed a selected run, so a callback the
// panel wiring gave to an earlier selection can be invoked later, exactly as a retained handler of an earlier render would be.
const handed: Array<{ runId: string; close: () => void }> = []
vi.mock('./ProjectRunHistoryDetail', () => ({
  ProjectRunHistoryDetail: ({ entry, onClose }: { entry: { runId: string }; onClose: () => void }) => {
    if (!handed.some((item) => item.close === onClose)) {
      handed.push({ runId: entry.runId, close: onClose })
    }

    return (
      <section aria-label="Selected run">
        <p>{`detail ${entry.runId}`}</p>
        <button type="button" onClick={onClose}>
          Close selected run
        </button>
      </section>
    )
  },
}))

const open = () => fireEvent.click(screen.getByRole('button', { name: 'Show run history' }))
const inspect = (number: number) => fireEvent.click(screen.getByRole('button', { name: `Inspect run #${number}` }))
const closeHanded = (index: number) => act(() => handed[index].close())

describe('the selected run of the Run history panel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    handed.length = 0
    const getProjectRunHistory = vi.fn(() => Promise.resolve(pageOf([], { entries: [entryFor(3), entryFor(2), entryFor(1)] })))
    vi.mocked(projectRunHistoryClient).mockReturnValue({ getProjectRunHistory } as unknown as ReturnType<typeof projectRunHistoryClient>)
  })

  async function opened() {
    render(<ProjectRunHistoryPanel projectId="project-1" />)
    open()
    await screen.findByRole('button', { name: 'Inspect run #3' })
  }

  it('closes the current detail through its own close callback', async () => {
    await opened()
    inspect(3)
    expect(screen.getByText('detail run-3')).toBeInTheDocument()

    closeHanded(0)

    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
    expect(screen.getByText('Objective 3')).toBeInTheDocument()
  })

  it('closes the current detail through its rendered control', async () => {
    await opened()
    inspect(2)

    fireEvent.click(screen.getByRole('button', { name: 'Close selected run' }))

    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
  })

  it('ignores a retained close of detail A once detail B is selected', async () => {
    await opened()
    inspect(3)
    const closeA = handed[0]
    inspect(2)
    expect(screen.getByText('detail run-2')).toBeInTheDocument()

    act(() => closeA.close())

    expect(screen.getByText('detail run-2')).toBeInTheDocument()
    expect(screen.queryByText('detail run-3')).toBeNull()
  })

  it('ignores a retained close of the first A once A is selected again after B (A to B to A)', async () => {
    await opened()
    inspect(3)
    const firstA = handed[0]
    inspect(2)
    inspect(3)
    expect(screen.getByText('detail run-3')).toBeInTheDocument()

    act(() => firstA.close())

    expect(screen.getByText('detail run-3')).toBeInTheDocument()
    closeHanded(handed.length - 1)
    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
  })

  it('ignores a retained close after the selection was closed and a new one opened', async () => {
    await opened()
    inspect(3)
    const closeA = handed[0]
    fireEvent.click(screen.getByRole('button', { name: 'Close selected run' }))
    inspect(3)

    act(() => closeA.close())

    expect(screen.getByText('detail run-3')).toBeInTheDocument()
  })

  it('keeps the same detail when the same run is inspected again', async () => {
    await opened()
    inspect(2)
    const first = handed[0]

    inspect(2)

    expect(screen.getByText('detail run-2')).toBeInTheDocument()
    act(() => first.close())
    expect(screen.queryByRole('region', { name: 'Selected run' })).toBeNull()
  })
})
