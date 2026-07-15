# Requirements Document

## Introduction

Post-import scale (360 artifacts, 1,000+ facts) made the flat Artifacts grid and the
flat Canon truth-state lists unbrowsable.

## Requirements

1. THE Artifacts page SHALL offer a tree projection (type → status → name, collapsible)
   alongside the existing card grid, plus a name/summary search that applies to both views.
2. THE Canon page SHALL group entries by artifact within each truth-state section,
   sections SHALL be collapsible with counts, and a text search SHALL filter across
   artifact names, labels, and values (expanding sections while searching).
3. Both SHALL be pure client-side projections over the existing endpoints — no API change.
