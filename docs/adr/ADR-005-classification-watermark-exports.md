# ADR-005: Classification Watermark on Exports

**Status:** Accepted
**Date:** 2026-03-18

## Context

When analysts export data (GeoJSON, KML, CSV), the exported file leaves the system boundary. In defence contexts, the classification marking must travel with the data — it should be inseparable from the content.

## Decision

All exported files include a classification watermark: metadata field in GeoJSON, header row in CSV, document description in KML. The watermark reflects the highest classification of data in the export.

## Consequences

- **Positive:** Mirrors how defence systems handle data extraction. An exported file shared outside the system carries its classification. Reviewers from defence backgrounds will expect this.
- **Negative:** Marginal additional complexity in export logic. Users must understand that the watermark is informational (mock classification system).
