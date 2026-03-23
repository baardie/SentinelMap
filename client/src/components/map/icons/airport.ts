/**
 * Airport icon as an SVG data URL.
 * Simplified runway cross/airport symbol.
 * 24×24 pixels, white fill — coloured at render time via SDF icon-color.
 */
const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24">
  <circle cx="12" cy="12" r="10" fill="none" stroke="white" stroke-width="1.5"/>
  <line x1="4" y1="12" x2="20" y2="12" stroke="white" stroke-width="2"/>
  <line x1="12" y1="6" x2="12" y2="18" stroke="white" stroke-width="1.5"/>
</svg>`

export const AIRPORT_ICON_DATA_URL = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
