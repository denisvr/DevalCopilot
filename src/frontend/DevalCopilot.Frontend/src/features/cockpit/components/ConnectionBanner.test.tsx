import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ConnectionBanner } from './ConnectionBanner'

describe('ConnectionBanner', () => {
  it('renders nothing while live', () => {
    const { container } = render(<ConnectionBanner state="live" />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing while first connecting', () => {
    const { container } = render(<ConnectionBanner state="connecting" />)
    expect(container).toBeEmptyDOMElement()
  })

  it('shows a stale-state banner while reconnecting', () => {
    render(<ConnectionBanner state="reconnecting" />)
    expect(screen.getByRole('status')).toHaveTextContent(/reconnecting/i)
  })

  it('shows a stale-state banner while disconnected', () => {
    render(<ConnectionBanner state="disconnected" />)
    expect(screen.getByRole('status')).toHaveTextContent(/disconnected/i)
  })

  it('shows the specific sync-error reason instead of the generic disconnected message', () => {
    render(<ConnectionBanner state="disconnected" syncError="cockpit query unavailable" />)
    expect(screen.getByRole('status')).toHaveTextContent('cockpit query unavailable')
  })

  it('prefers the sync-error reason even while nominally live', () => {
    render(<ConnectionBanner state="live" syncError="cockpit query unavailable" />)
    expect(screen.getByRole('status')).toHaveTextContent('cockpit query unavailable')
  })
})
