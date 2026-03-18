# Spec 03 — Entity Correlation & Identity Resolution

**Parent:** [PRD.md](../PRD.md)
**Status:** Draft

---

## 1. Overview

The correlation engine is the core differentiator of SentinelMap. Rather than displaying isolated data feeds, it fuses observations from multiple sources into unified **entities** — answering questions like "this vessel, these three news articles, and this Companies House filing all relate to the same real-world actor."

v1 uses deterministic, rule-based correlation. No ML. This is a deliberate design choice — rule-based systems are auditable, explainable, and predictable, which matters in defence contexts where analysts need to trust and verify the system's reasoning.

---

## 2. Entity Model

### 2.1 Entity Record

```csharp
public class Entity
{
    public Guid Id { get; set; }
    public string PrimaryName { get; set; }           // Best-known name
    public EntityType Type { get; set; }               // Vessel, Aircraft, Organisation, Person, Unknown
    public double CorrelationConfidence { get; set; }  // 0.0–1.0 overall confidence in the fused identity
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    
    // Linked identifiers from all sources
    public List<EntityIdentifier> Identifiers { get; set; }
    
    // Linked observations (tracks, articles, posts, filings)
    public List<Observation> Observations { get; set; }
    
    // Relationships to other entities
    public List<EntityRelationship> Relationships { get; set; }
}
```

### 2.2 Entity Identifiers

An entity can have multiple identifiers from different sources:

| Source | Identifier Type | Example |
|---|---|---|
| AIS | MMSI | 353136000 |
| AIS | IMO Number | 9811000 |
| ADS-B | ICAO24 Hex | 4CA529 |
| ADS-B | Callsign | RYR1234 |
| Companies House | Company Number | 12345678 |
| News | Canonical name | "Evergreen Marine Corp" |

### 2.3 Entity Relationships

```csharp
public class EntityRelationship
{
    public Guid SourceEntityId { get; set; }
    public Guid TargetEntityId { get; set; }
    public RelationshipType Type { get; set; }  // Owner, Operator, NearbyAt, MentionedWith, Subsidiary
    public double Confidence { get; set; }
    public string Evidence { get; set; }         // Human-readable reason for the link
    public DateTimeOffset EstablishedAt { get; set; }
}
```

---

## 3. Correlation Rules

The engine runs a pipeline of correlation rules, each producing candidate links with confidence scores. Links above the configured threshold (default: 0.6) are committed; links between 0.3–0.6 are flagged for analyst review.

### 3.1 Direct Identifier Match

**Confidence: 0.95**

The simplest and highest-confidence rule. If two observations share a verified identifier, they're the same entity.

| Match | Example |
|---|---|
| MMSI ↔ MMSI | Two AIS reports with the same MMSI |
| IMO ↔ IMO | AIS report IMO matches a news article mentioning the same IMO |
| ICAO24 ↔ ICAO24 | Two ADS-B reports with the same transponder hex |
| Company number ↔ Company number | Companies House filing matches a news article citing the same number |

### 3.2 Name Fuzzy Match

**Confidence: 0.5–0.85 (scaled by similarity)**

When no shared identifier exists, fuzzy name matching links entities:

```csharp
public class NameMatchRule : ICorrelationRule
{
    public CorrelationCandidate? Evaluate(Observation a, Observation b)
    {
        var similarity = ComputeSimilarity(a.ExtractedName, b.ExtractedName);
        
        if (similarity < 0.7) return null;  // Below threshold
        
        return new CorrelationCandidate
        {
            Confidence = 0.5 + (similarity - 0.7) * (0.35 / 0.3),  // Scale 0.7–1.0 → 0.5–0.85
            Rule = "NameFuzzyMatch",
            Evidence = $"Name similarity: {similarity:P0} between '{a.ExtractedName}' and '{b.ExtractedName}'"
        };
    }
}
```

**Similarity algorithm:** Jaro-Winkler distance (good for names, handles transpositions and prefix weighting). Implemented with a .NET library or hand-rolled — no external service dependency.

**Normalisation before comparison:**
1. Lowercase.
2. Strip legal suffixes ("Ltd", "LLC", "Corp", "PLC").
3. Strip common vessel prefixes ("MV", "MT", "HMS", "USS").
4. Collapse whitespace.
5. Transliterate non-ASCII to ASCII equivalents.

### 3.3 Spatio-Temporal Proximity

**Confidence: 0.3–0.7 (scaled by proximity)**

Two entities observed near each other in space and time are flagged as potentially related:

```sql
-- Find ADS-B tracks within 5nm of an AIS track at roughly the same time
SELECT ais.entity_id, adsb.entity_id,
       ST_Distance(ais.position::geography, adsb.position::geography) AS distance_m,
       ABS(EXTRACT(EPOCH FROM ais.timestamp - adsb.timestamp)) AS time_diff_s
FROM position_reports ais
JOIN position_reports adsb
  ON ST_DWithin(ais.position::geography, adsb.position::geography, 9260)  -- 5 nautical miles
  AND ABS(EXTRACT(EPOCH FROM ais.timestamp - adsb.timestamp)) < 300       -- 5 minutes
WHERE ais.source_type = 'Maritime'
  AND adsb.source_type = 'Aviation';
```

**Confidence scaling:**
- < 1nm and < 1min: 0.7 (strong co-location)
- 1–3nm and < 3min: 0.5
- 3–5nm and < 5min: 0.3
- Beyond: no link

**Relationship type:** `NearbyAt` — indicates proximity, not identity. This creates a relationship edge, not a merge.

### 3.4 News ↔ Entity Keyword Match

**Confidence: 0.4–0.75**

Match news articles to existing entities by searching article text for known entity identifiers and names:

1. For each new article, scan title + body for all known entity names, MMSI numbers, IMO numbers, company numbers.
2. Exact identifier match → 0.75 confidence.
3. Name substring match (entity name appears in article) → 0.6 confidence.
4. Keyword proximity (entity name appears within 50 characters of a relevant keyword like "vessel", "ship", "aircraft") → 0.4 confidence.

---

## 4. Correlation Pipeline

```
New Observation arrives
    │
    ▼
┌─────────────────────────────┐
│  1. Direct Identifier Match │  ← Check all existing entities for shared IDs
└──────────┬──────────────────┘
           │ (if no match)
           ▼
┌─────────────────────────────┐
│  2. Name Fuzzy Match        │  ← Compare extracted names against entity name index
└──────────┬──────────────────┘
           │ (if no match)
           ▼
┌─────────────────────────────┐
│  3. Spatio-Temporal Check   │  ← PostGIS proximity query (positional obs only)
└──────────┬──────────────────┘
           │ (if no match)
           ▼
┌─────────────────────────────┐
│  4. Create New Entity       │  ← No existing entity matches — create fresh
└──────────┬──────────────────┘
           │
           ▼
┌─────────────────────────────┐
│  5. Confidence Aggregation  │  ← If multiple rules fired, combine scores
└──────────┬──────────────────┘
           │
           ▼
┌─────────────────────────────┐
│  6. Emit to Alerting        │  ← Notify alerting engine of new/updated entity
└─────────────────────────────┘
```

### Confidence Aggregation

When multiple rules produce candidates linking to the same entity, their confidences are combined using **noisy-OR**:

```
P_combined = 1 - ∏(1 - P_i)
```

Example: Name match (0.6) + spatio-temporal proximity (0.4) → combined: 1 - (0.4 × 0.6) = 0.76.

This prevents double-counting while ensuring multiple weak signals reinforce each other.

---

## 5. Entity Merge & Split

### Merge

When two previously separate entities are linked above threshold:
1. The newer entity is merged into the older one (preserving the longer history).
2. All observations, identifiers, and relationships from the newer entity are transferred.
3. A merge audit event is logged with full provenance.
4. Any active alerts on either entity are preserved.

### Split (Analyst-Initiated)

If an analyst determines a merge was incorrect:
1. They can manually split an entity via the UI.
2. They select which observations belong to which side of the split.
3. The correlation engine re-runs on the split entities to validate.
4. A split audit event is logged.

### Merge Protection

Certain identifier types are treated as authoritative. If two entities share the same source type but have different authoritative IDs (e.g. two different MMSI numbers), they are **never** auto-merged regardless of other rule scores. The system flags this as a conflict for analyst review.

---

## 6. Identity Graph

The entity relationships form a graph. The frontend renders this as an interactive force-directed graph in the entity detail panel (using D3.js force simulation):

```
[Vessel: Ever Given] ──Owner──▶ [Company: Evergreen Marine]
         │                              │
     NearbyAt                      Subsidiary
         │                              │
         ▼                              ▼
[Aircraft: HSZ412] ──Operator──▶ [Company: Shoei Kisen]
```

**Graph API:**

```
GET /api/entities/{entityId}/graph?depth=2
```

Returns the entity, all direct relationships, and relationships of related entities up to the specified depth. Default depth: 1. Max depth: 3 (to prevent explosion).

---

## 7. Analyst Review Queue

Correlations between 0.3–0.6 confidence land in a review queue:

```
┌─────────────────────────────────────────────────┐
│  PENDING CORRELATIONS                    [3 new] │
├─────────────────────────────────────────────────┤
│  ⚠ 0.52  "MV Pacific Star" ↔ "Pacific Star Ltd"│
│          Rule: NameFuzzyMatch                    │
│          [✓ Confirm] [✗ Reject] [👁 Inspect]    │
│                                                  │
│  ⚠ 0.45  Aircraft N12345 ↔ Vessel MMSI:1234567 │
│          Rule: SpatioTemporal (2.1nm, 45s)      │
│          [✓ Confirm] [✗ Reject] [👁 Inspect]    │
└─────────────────────────────────────────────────┘
```

Analyst decisions feed back into the system as training signal for threshold tuning (in future ML versions).

---

## 8. Database Schema (Correlation-Specific)

```sql
CREATE TABLE entities (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    primary_name    TEXT NOT NULL,
    entity_type     TEXT NOT NULL CHECK (entity_type IN ('Vessel','Aircraft','Organisation','Person','Unknown')),
    correlation_confidence DOUBLE PRECISION DEFAULT 1.0,
    first_seen      TIMESTAMPTZ NOT NULL,
    last_seen       TIMESTAMPTZ NOT NULL,
    created_at      TIMESTAMPTZ DEFAULT NOW(),
    updated_at      TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE entity_identifiers (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    entity_id       UUID NOT NULL REFERENCES entities(id) ON DELETE CASCADE,
    identifier_type TEXT NOT NULL,    -- 'MMSI', 'IMO', 'ICAO24', 'CompanyNumber', 'Name'
    identifier_value TEXT NOT NULL,
    source_id       TEXT NOT NULL,    -- Which connector provided this
    confidence      DOUBLE PRECISION DEFAULT 1.0,
    UNIQUE(identifier_type, identifier_value)
);

CREATE TABLE entity_relationships (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source_entity_id UUID NOT NULL REFERENCES entities(id) ON DELETE CASCADE,
    target_entity_id UUID NOT NULL REFERENCES entities(id) ON DELETE CASCADE,
    relationship_type TEXT NOT NULL,
    confidence      DOUBLE PRECISION NOT NULL,
    evidence        TEXT,
    established_at  TIMESTAMPTZ DEFAULT NOW(),
    established_by  TEXT DEFAULT 'system'  -- 'system' or user ID for manual links
);

CREATE TABLE correlation_reviews (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    entity_a_id     UUID NOT NULL REFERENCES entities(id),
    entity_b_id     UUID NOT NULL REFERENCES entities(id),
    rule_name       TEXT NOT NULL,
    confidence      DOUBLE PRECISION NOT NULL,
    status          TEXT DEFAULT 'pending' CHECK (status IN ('pending','confirmed','rejected')),
    reviewed_by     UUID REFERENCES users(id),
    reviewed_at     TIMESTAMPTZ,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX idx_identifiers_lookup ON entity_identifiers(identifier_type, identifier_value);
CREATE INDEX idx_relationships_source ON entity_relationships(source_entity_id);
CREATE INDEX idx_relationships_target ON entity_relationships(target_entity_id);
CREATE INDEX idx_reviews_pending ON correlation_reviews(status) WHERE status = 'pending';
```

---

## 9. Testing Strategy

| Level | Scope |
|---|---|
| **Unit** | Each correlation rule tested with fixture pairs (should-match, should-not-match, edge cases) |
| **Integration** | Full pipeline: ingest two related observations → verify entity created with correct links |
| **Accuracy** | Curated test dataset of 100 known entity pairs with expected correlation outcomes. Measure precision/recall. Target: >90% precision, >80% recall at 0.6 threshold. |
| **Regression** | Analyst review decisions stored as test fixtures — system must produce the same result on re-run |
