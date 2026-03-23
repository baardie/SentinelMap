/**
 * Buoy icon as an SVG data URL.
 * Diamond marker for aids to navigation.
 * 24×24 pixels, white fill — coloured at render time via SDF icon-color.
 */
const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24">
  <polygon points="12,2 22,12 12,22 2,12" fill="white"/>
</svg>`

export const BUOY_ICON_DATA_URL = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
