import { useState } from 'react'
import type { FormEvent } from 'react'
import { useRegisterProject } from '../hooks/useRegisterProject'

interface AddProjectFormProps {
  onRegistered: () => void
}

/**
 * The approved cockpit's "Add project" affordance. Collapsed to a single button until opened;
 * only ever renders the fixed, safe error text the API itself returned — never a raw exception.
 */
export function AddProjectForm({ onRegistered }: AddProjectFormProps) {
  const [open, setOpen] = useState(false)
  const [name, setName] = useState('')
  const [path, setPath] = useState('')

  const { registering, error, register } = useRegisterProject(() => {
    setOpen(false)
    setName('')
    setPath('')
    onRegistered()
  })

  if (!open) {
    return (
      <button type="button" className="dc-button" onClick={() => setOpen(true)}>
        Add project
      </button>
    )
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    await register(name, path)
  }

  return (
    <form className="dc-add-project-form" onSubmit={handleSubmit} aria-label="Add project">
      <input
        type="text"
        placeholder="Name"
        value={name}
        onChange={(event) => setName(event.target.value)}
        aria-label="Project name"
      />
      <input
        type="text"
        placeholder={String.raw`C:\path\to\repo`}
        value={path}
        onChange={(event) => setPath(event.target.value)}
        aria-label="Repository path"
      />
      <button type="submit" className="dc-button" data-variant="primary" disabled={registering}>
        {registering ? 'Registering…' : 'Register'}
      </button>
      <button
        type="button"
        className="dc-button"
        disabled={registering}
        onClick={() => {
          setOpen(false)
          setName('')
          setPath('')
        }}
      >
        Cancel
      </button>
      {error ? (
        <p className="dc-empty-state" role="alert">
          {error}
        </p>
      ) : null}
    </form>
  )
}
