export const theme = {
  radius: '2px',
  fonts: {
    sans: "'Geist Sans', ui-sans-serif, system-ui, sans-serif",
    mono: "'Geist Mono', ui-monospace, monospace",
  },
  tracks: {
    cargo: 'var(--color-track-cargo)',
    tanker: 'var(--color-track-tanker)',
    passenger: 'var(--color-track-passenger)',
    aircraft: 'var(--color-track-aircraft)',
    unknown: 'var(--color-track-unknown)',
  },
  classification: {
    official: { bg: '#16a34a', text: '#ffffff', label: 'OFFICIAL' },
    officialSensitive: { bg: '#d97706', text: '#ffffff', label: 'OFFICIAL-SENSITIVE' },
    secret: { bg: '#dc2626', text: '#ffffff', label: 'SECRET' },
  },
} as const
