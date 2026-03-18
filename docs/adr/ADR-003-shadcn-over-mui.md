# ADR-003: shadcn/ui Over MUI

**Status:** Accepted
**Date:** 2026-03-18

## Context

The COP frontend needs a component library. Material UI (MUI) provides a comprehensive library but has a recognisable "Google Material" aesthetic. shadcn/ui provides pre-built components as source code that you own and fully control.

## Decision

Use shadcn/ui with Tailwind CSS and defence-specific theme overrides: 2px border radius (sharp, operational), Geist Mono for identifiers, slate/zinc palette, red reserved for alerts only.

## Consequences

- **Positive:** Full control over design system. No vendor visual identity. Components are source code — no version lock-in. Defence aesthetic achievable through theme overrides rather than fighting a library's defaults.
- **Negative:** Slightly more initial setup than MUI's out-of-box experience. No pre-built data grid (use custom table components).
- **Mitigated by:** shadcn CLI automates component installation. Radix UI primitives handle accessibility. 30 minutes of theming transforms the default look into purpose-built operational software.
