import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import App from './App'
import * as useSessionStatusModule from './features/cockpit/hooks/useSessionStatus'

vi.mock('./features/cockpit/hooks/useSessionStatus')

const useSessionStatusMock = vi.mocked(useSessionStatusModule.useSessionStatus)

describe('App session states', () => {
  it('shows a starting state while the session is not yet resolved', () => {
    useSessionStatusMock.mockReturnValue('initializing')

    render(<App />)

    expect(screen.getByText(/starting the local host/i)).toBeInTheDocument()
  })

  it('shows a sidecar-failed state without ever showing the run cockpit', () => {
    useSessionStatusMock.mockReturnValue('failed')

    render(<App />)

    expect(screen.getByText(/no launch session is available/i)).toBeInTheDocument()
  })

  it('shows a distinct disconnected/recovery state, directing the user to reopen the app', () => {
    useSessionStatusMock.mockReturnValue('disconnected')

    render(<App />)

    expect(screen.getByText(/disconnected/i)).toBeInTheDocument()
    expect(screen.getByText(/close and reopen/i)).toBeInTheDocument()
  })
})
