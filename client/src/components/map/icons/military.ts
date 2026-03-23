/**
 * Military base icon as an SVG data URL.
 * 5-pointed star symbol.
 * 24×24 pixels, white fill — coloured at render time via SDF icon-color.
 */
const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24">
  <polygon points="12,2 15,9 22,9 16.5,14 18.5,21 12,17 5.5,21 7.5,14 2,9 9,9" fill="white"/>
</svg>`

export const MILITARY_ICON_DATA_URL = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
