import type { MapFeatureData } from '../../types'

const ATON_TYPES: Record<number, { name: string; category: string }> = {
  0: { name: 'Not Specified', category: 'General' },
  1: { name: 'Reference Point', category: 'General' },
  2: { name: 'RACON (Radar Beacon)', category: 'General' },
  3: { name: 'Fixed Structure (Platform/Wind Farm)', category: 'General' },
  4: { name: 'Emergency Wreck Marking Buoy', category: 'General' },
  5: { name: 'Light (No Sectors)', category: 'Fixed AtoN' },
  6: { name: 'Light (With Sectors)', category: 'Fixed AtoN' },
  7: { name: 'Leading Light Front', category: 'Fixed AtoN' },
  8: { name: 'Leading Light Rear', category: 'Fixed AtoN' },
  9: { name: 'Beacon, Cardinal North', category: 'Fixed AtoN' },
  10: { name: 'Beacon, Cardinal East', category: 'Fixed AtoN' },
  11: { name: 'Beacon, Cardinal South', category: 'Fixed AtoN' },
  12: { name: 'Beacon, Cardinal West', category: 'Fixed AtoN' },
  13: { name: 'Beacon, Port Hand', category: 'Fixed AtoN' },
  14: { name: 'Beacon, Starboard Hand', category: 'Fixed AtoN' },
  15: { name: 'Beacon, Preferred Channel Port', category: 'Fixed AtoN' },
  16: { name: 'Beacon, Preferred Channel Starboard', category: 'Fixed AtoN' },
  17: { name: 'Beacon, Isolated Danger', category: 'Fixed AtoN' },
  18: { name: 'Beacon, Safe Water', category: 'Fixed AtoN' },
  19: { name: 'Beacon, Special Mark', category: 'Fixed AtoN' },
  20: { name: 'Cardinal Mark North', category: 'Floating AtoN' },
  21: { name: 'Cardinal Mark East', category: 'Floating AtoN' },
  22: { name: 'Cardinal Mark South', category: 'Floating AtoN' },
  23: { name: 'Cardinal Mark West', category: 'Floating AtoN' },
  24: { name: 'Port Hand Mark', category: 'Floating AtoN' },
  25: { name: 'Starboard Hand Mark', category: 'Floating AtoN' },
  26: { name: 'Preferred Channel Port Hand', category: 'Floating AtoN' },
  27: { name: 'Preferred Channel Starboard Hand', category: 'Floating AtoN' },
  28: { name: 'Isolated Danger', category: 'Floating AtoN' },
  29: { name: 'Safe Water', category: 'Floating AtoN' },
  30: { name: 'Special Mark', category: 'Floating AtoN' },
  31: { name: 'Light Vessel / LANBY / Rig', category: 'Floating AtoN' },
}

interface FeatureDetailPanelProps {
  feature: MapFeatureData
  onClose: () => void
  onEdit?: () => void
}

const FEATURE_TYPE_INFO: Record<string, { label: string; description: string; category: string }> = {
  AisBaseStation: {
    label: 'AIS BASE STATION',
    description: 'Fixed shore-based AIS transceiver providing vessel traffic monitoring and navigation safety services. Receives and rebroadcasts AIS messages from vessels within range.',
    category: 'MARITIME INFRASTRUCTURE',
  },
  AidToNavigation: {
    label: 'AID TO NAVIGATION',
    description: 'Buoy, lighthouse, or beacon broadcasting AIS signals to assist vessels with safe navigation. Provides fixed reference points for channel marking, hazard warning, and port approach guidance.',
    category: 'MARITIME INFRASTRUCTURE',
  },
  Airport: {
    label: 'AIRPORT',
    description: 'Civil or mixed-use aerodrome with scheduled or chartered flight operations. ADS-B equipped aircraft operating from this facility will appear in the tracking overlay.',
    category: 'AVIATION INFRASTRUCTURE',
  },
  MilitaryBase: {
    label: 'MILITARY INSTALLATION',
    description: 'Armed forces facility including naval bases, RAF stations, army garrisons, and defence research establishments. May have associated restricted airspace or exclusion zones.',
    category: 'DEFENCE INFRASTRUCTURE',
  },
  CustomStructure: {
    label: 'CUSTOM STRUCTURE',
    description: 'User-placed point of interest or operational marker. Can represent command posts, observation points, checkpoints, relay stations, or any analyst-defined location.',
    category: 'USER DEFINED',
  },
}

function getTypeInfo(featureType: string) {
  return FEATURE_TYPE_INFO[featureType] ?? {
    label: featureType.toUpperCase(),
    description: 'Map feature.',
    category: 'OTHER',
  }
}

function getTypeColor(featureType: string): string {
  switch (featureType) {
    case 'AisBaseStation': return '#8b5cf6'
    case 'AidToNavigation': return '#06b6d4'
    case 'Airport': return '#f97316'
    case 'MilitaryBase': return '#ef4444'
    case 'CustomStructure': return '#f59e0b'
    default: return '#94a3b8'
  }
}

export function FeatureDetailPanel({ feature, onClose, onEdit }: FeatureDetailPanelProps) {
  const info = getTypeInfo(feature.featureType)
  const typeColor = feature.color ?? getTypeColor(feature.featureType)

  // Parse details JSON if present
  let detailsObj: Record<string, string> | null = null
  if (feature.details) {
    try {
      detailsObj = JSON.parse(feature.details)
    } catch {
      // details is a plain string, not JSON
    }
  }

  return (
    <div
      className="absolute top-0 right-0 h-full w-80 bg-slate-900 border-l border-slate-700 flex flex-col z-20"
      style={{ borderRadius: '2px' }}
    >
      {/* Header with colour accent */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-slate-700">
        <div className="flex items-center gap-2">
          <div className="w-3 h-3 flex-shrink-0" style={{ backgroundColor: typeColor, borderRadius: '2px' }} />
          <span className="text-slate-100 text-sm font-semibold tracking-wide uppercase font-mono">
            {info.label}
          </span>
        </div>
        <button
          onClick={onClose}
          className="text-slate-400 hover:text-slate-100 text-lg leading-none"
          aria-label="Close"
        >
          ×
        </button>
      </div>

      {/* Body */}
      <div className="flex flex-col gap-4 px-4 py-4 flex-1 overflow-y-auto">
        {/* Name */}
        <div className="flex flex-col gap-1">
          <label className="text-slate-500 text-xs font-mono uppercase tracking-widest">NAME</label>
          <span className="text-slate-100 text-sm font-mono">{feature.name}</span>
        </div>

        {/* Category */}
        <div className="flex flex-col gap-1">
          <label className="text-slate-500 text-xs font-mono uppercase tracking-widest">CATEGORY</label>
          <span className="text-slate-400 text-xs font-mono tracking-wider">{info.category}</span>
        </div>

        {/* Description */}
        <div className="flex flex-col gap-1">
          <label className="text-slate-500 text-xs font-mono uppercase tracking-widest">DESCRIPTION</label>
          <p className="text-slate-300 text-xs leading-relaxed">{info.description}</p>
        </div>

        {/* Position */}
        <div className="flex flex-col gap-1">
          <label className="text-slate-500 text-xs font-mono uppercase tracking-widest">POSITION</label>
          <div className="bg-slate-800 border border-slate-700 text-slate-300 text-xs font-mono px-3 py-2" style={{ borderRadius: '2px' }}>
            {feature.latitude.toFixed(6)}°N, {feature.longitude.toFixed(6)}°
            {feature.longitude >= 0 ? 'E' : 'W'}
          </div>
        </div>

        {/* Source */}
        <div className="flex flex-col gap-1">
          <label className="text-slate-500 text-xs font-mono uppercase tracking-widest">SOURCE</label>
          <span className="text-slate-400 text-xs font-mono tracking-wider">
            {feature.source === 'user' ? 'USER PLACED' : feature.source === 'static' ? 'STATIC DATASET' : feature.source === 'ais' ? 'AIS LIVE FEED' : feature.source.toUpperCase()}
          </span>
        </div>

        {/* Details (if present) */}
        {feature.details && (
          <div className="flex flex-col gap-1">
            <label className="text-slate-500 text-xs font-mono uppercase tracking-widest">DETAILS</label>
            {detailsObj ? (
              <div className="flex flex-col gap-1">
                {Object.entries(detailsObj).map(([key, val]) => (
                  <div key={key} className="flex items-start gap-2">
                    <span className="text-slate-500 text-xs font-mono uppercase tracking-wider min-w-[4rem]">{key}:</span>
                    <span className="text-slate-300 text-xs font-mono">{String(val)}</span>
                  </div>
                ))}
              </div>
            ) : (
              <p className="text-slate-300 text-xs font-mono bg-slate-800 border border-slate-700 px-3 py-2" style={{ borderRadius: '2px' }}>
                {feature.details}
              </p>
            )}
          </div>
        )}

        {/* Feature-type specific info */}
        {feature.featureType === 'Airport' && (
          <div className="flex flex-col gap-1">
            <label className="text-slate-500 text-xs font-mono uppercase tracking-widest">OPERATIONS</label>
            <p className="text-slate-400 text-xs leading-relaxed">
              Active aerodrome — expect ADS-B traffic in approach/departure corridors.
              Check NOTAM for temporary restrictions.
            </p>
          </div>
        )}

        {feature.featureType === 'MilitaryBase' && (
          <div className="flex flex-col gap-1">
            <label className="text-slate-500 text-xs font-mono uppercase tracking-widest">OPERATIONS</label>
            <p className="text-slate-400 text-xs leading-relaxed">
              Active military facility — associated restricted airspace may apply.
              Military aircraft may not appear on ADS-B.
            </p>
          </div>
        )}

        {feature.featureType === 'AisBaseStation' && (
          <>
            {detailsObj?.mmsi && (
              <div className="flex flex-col gap-1">
                <label className="text-slate-500 text-xs font-mono uppercase tracking-widest">MMSI</label>
                <span className="text-slate-300 text-xs font-mono">{detailsObj.mmsi}</span>
              </div>
            )}
            <div className="flex flex-col gap-1">
              <label className="text-slate-500 text-xs font-mono uppercase tracking-widest">COVERAGE</label>
              <p className="text-slate-400 text-xs leading-relaxed">
                Typical VHF range: 20-40 nautical miles. Station provides
                real-time vessel tracking within its coverage area.
              </p>
            </div>
          </>
        )}

        {feature.featureType === 'AidToNavigation' && (() => {
          const aidType = detailsObj?.aidType != null ? Number(detailsObj.aidType) : null
          const atonInfo = aidType != null ? ATON_TYPES[aidType] : null
          return (
            <>
              {atonInfo && (
                <div className="flex flex-col gap-1">
                  <label className="text-slate-500 text-xs font-mono uppercase tracking-widest">AID TYPE</label>
                  <span className="text-slate-100 text-xs font-mono">{atonInfo.name}</span>
                  <span className="text-slate-500 text-xs font-mono">{atonInfo.category}</span>
                </div>
              )}
              {detailsObj?.mmsi && (
                <div className="flex flex-col gap-1">
                  <label className="text-slate-500 text-xs font-mono uppercase tracking-widest">MMSI</label>
                  <span className="text-slate-300 text-xs font-mono">{detailsObj.mmsi}</span>
                </div>
              )}
              <div className="flex flex-col gap-1">
                <label className="text-slate-500 text-xs font-mono uppercase tracking-widest">PURPOSE</label>
                <p className="text-slate-400 text-xs leading-relaxed">
                  {atonInfo?.category === 'Floating AtoN'
                    ? 'Floating navigation aid — buoy or light vessel marking channels, hazards, or safe water. Position may shift with tide and weather.'
                    : atonInfo?.category === 'Fixed AtoN'
                    ? 'Fixed navigation aid — beacon or light structure permanently positioned to mark channels, cardinal directions, or isolated dangers.'
                    : 'Navigation marker — indicates channel boundaries, hazards, anchorage areas, or port approach waypoints.'}
                </p>
              </div>
            </>
          )
        })()}
      </div>

      {/* Footer */}
      <div className="flex flex-col gap-2 px-4 py-4 border-t border-slate-700">
        {feature.source === 'user' && onEdit && (
          <button
            onClick={onEdit}
            className="w-full py-2 text-xs font-mono tracking-widest uppercase bg-slate-700 border border-slate-500 text-slate-100 hover:bg-slate-600 transition-colors"
            style={{ borderRadius: '2px' }}
          >
            EDIT
          </button>
        )}
        <button
          onClick={onClose}
          className="w-full py-2 text-xs font-mono tracking-widest uppercase bg-slate-800 border border-slate-700 text-slate-400 hover:text-slate-200 transition-colors"
          style={{ borderRadius: '2px' }}
        >
          CLOSE
        </button>
      </div>
    </div>
  )
}
