import { useEffect, useState } from 'react'

export type CockpitTheme = 'dark-navy' | 'light'

const STORAGE_KEY = 'devalcopilot.theme'

function readStoredTheme(): CockpitTheme {
  // Theme is a non-sensitive UI preference; run state and credentials are never
  // stored this way.
  const stored = window.localStorage.getItem(STORAGE_KEY)
  return stored === 'light' ? 'light' : 'dark-navy'
}

export function useTheme(): [CockpitTheme, (theme: CockpitTheme) => void] {
  const [theme, setTheme] = useState<CockpitTheme>(readStoredTheme)

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    window.localStorage.setItem(STORAGE_KEY, theme)
  }, [theme])

  return [theme, setTheme]
}
