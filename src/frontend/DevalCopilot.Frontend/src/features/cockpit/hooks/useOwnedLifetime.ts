import { useCallback, useLayoutEffect, useMemo, useState } from 'react'

/**
 * One mounted lifetime of one owner (a run, a project, a checkpoint, an output stream). Every
 * committed change of the owner key creates a new instance, and returning to an earlier key is a
 * new lifetime too: an obsolete read or operation can never be mistaken for the current one. The
 * instance is inert until a layout effect activates it, so an abandoned render owns nothing, and
 * it ends in the cleanup of the commit that replaces it, before any later continuation can run.
 */
export class OwnedLifetime {
  readonly key: string | null
  private active = false
  private readonly sequences = new Map<string, number>()

  constructor(key: string | null) {
    this.key = key
  }

  activate() {
    this.active = true
  }

  end() {
    this.active = false
  }

  isActive = () => this.active

  /**
   * Starts a read or operation on `channel` and returns its ownership check. The check stays true
   * only while this lifetime is active and no newer start on the same channel happened, so an
   * older overlapping response can neither overwrite a newer result nor clear its pending flag.
   */
  begin = (channel = 'default'): (() => boolean) => {
    const mine = (this.sequences.get(channel) ?? 0) + 1
    this.sequences.set(channel, mine)
    return () => this.active && this.sequences.get(channel) === mine
  }
}

/** Owns the lifetime of `key` (null: nothing is selected, so nothing can be owned) for one hook or component. */
export function useOwnedLifetime(key: string | null): OwnedLifetime {
  const lifetime = useMemo(() => new OwnedLifetime(key), [key])

  useLayoutEffect(() => {
    lifetime.activate()
    return () => {
      lifetime.end()
    }
  }, [lifetime])

  return lifetime
}

interface Frame<T> {
  owner: OwnedLifetime | null
  value: T
}

/**
 * State that belongs to `owner`. A frame written by any other lifetime (the previous selection,
 * or an earlier visit to the same one) is never exposed: the committed render of a new owner
 * derives the owner's `initial` value immediately instead of waiting for an effect to clear it.
 * `commit` writes only while its owner is still active, so a continuation of a replaced owner
 * cannot touch the replacement's state.
 */
export function useOwnedState<T>(
  owner: OwnedLifetime,
  createInitial: (key: string | null) => T,
): [T, (update: (previous: T) => T) => void] {
  const initial = useMemo(() => createInitial(owner.key), [owner, createInitial])
  const [frame, setFrame] = useState<Frame<T>>({ owner: null, value: initial })

  const commit = useCallback(
    (update: (previous: T) => T) => {
      setFrame((previous) =>
        owner.isActive() ? { owner, value: update(previous.owner === owner ? previous.value : initial) } : previous,
      )
    },
    [owner, initial],
  )

  return [frame.owner === owner ? frame.value : initial, commit]
}
