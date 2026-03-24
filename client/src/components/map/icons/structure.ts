/**
 * Structure icon as an SVG data URL.
 * Pin/marker for user-placed structures.
 * 24×32 pixels, white fill — coloured at render time via SDF icon-color.
 */
const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 32" width="24" height="32">
  <path d="M12 1 C7 1 3 5.5 3 10 C3 17 12 30 12 30 C12 30 21 17 21 10 C21 5.5 17 1 12 1 Z M12 7 C14.2 7 16 8.8 16 11 C16 13.2 14.2 15 12 15 C9.8 15 8 13.2 8 11 C8 8.8 9.8 7 12 7 Z" fill="white" fill-rule="evenodd"/>
</svg>`

export const STRUCTURE_ICON_DATA_URL = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
