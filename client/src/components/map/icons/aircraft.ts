/**
 * Aircraft icon as an SVG data URL.
 * Simplified top-down silhouette: pointed fuselage, swept wings, small tail.
 * 24×32 pixels, white fill — coloured at render time via SDF icon-color.
 */
const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 32" width="24" height="32">
  <path d="M12 1 L14 12 L23 15 L14 17 L15 28 L12 26 L9 28 L10 17 L1 15 L10 12 Z" fill="white"/>
</svg>`

export const AIRCRAFT_ICON_DATA_URL = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
