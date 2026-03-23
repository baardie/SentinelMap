/**
 * Structure icon as an SVG data URL.
 * Pin/marker for user-placed structures.
 * 24×32 pixels, white fill — coloured at render time via SDF icon-color.
 */
const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 32" width="24" height="32">
  <path d="M12 0C6 0 2 5 2 10c0 8 10 22 10 22s10-14 10-22C22 5 18 0 12 0zm0 14a4 4 0 110-8 4 4 0 010 8z" fill="white"/>
</svg>`

export const STRUCTURE_ICON_DATA_URL = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
