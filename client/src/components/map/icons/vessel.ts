/**
 * Vessel icon as an SVG data URL.
 * Simple ship silhouette: pointed bow (top), wider stern (bottom).
 * 24×32 pixels, white fill — coloured at render time via SDF icon-color.
 */
export const VESSEL_ICON_SVG = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 32" width="24" height="32">
  <polygon points="12,1 22,28 12,22 2,28" fill="white"/>
</svg>`

export const VESSEL_ICON_DATA_URL = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(VESSEL_ICON_SVG)}`
