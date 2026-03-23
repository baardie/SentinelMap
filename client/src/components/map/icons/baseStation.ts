/**
 * Base station icon as an SVG data URL.
 * Antenna/tower symbol.
 * 24×24 pixels, white fill — coloured at render time via SDF icon-color.
 */
const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24">
  <line x1="12" y1="2" x2="12" y2="22" stroke="white" stroke-width="2"/>
  <line x1="6" y1="8" x2="18" y2="8" stroke="white" stroke-width="1.5"/>
  <line x1="8" y1="14" x2="16" y2="14" stroke="white" stroke-width="1.5"/>
  <circle cx="12" cy="4" r="2" fill="white"/>
</svg>`

export const BASE_STATION_ICON_DATA_URL = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
