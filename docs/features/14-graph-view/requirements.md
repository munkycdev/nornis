# Requirements Document

## Introduction

The relationship structure of a world (345+ edges) is only navigable one artifact at a
time. A graph view makes the connective tissue visible.

## Requirements

1. A world graph endpoint SHALL return the caller-visible artifacts as nodes (id, name,
   type, status) and caller-visible relationships between visible artifacts as edges
   (endpoints, type).
2. THE graph page SHALL render a neighborhood around a focus artifact (selectable via
   search; also reachable from artifact detail) at depth 1 or 2, or the whole world —
   defaulting to depth-2 neighborhood, never the full hairball by default.
3. Nodes SHALL be colored by artifact type with a legend; clicking a node SHALL show its
   name/type/status and offer opening its detail page; clicking also refocuses the
   neighborhood on that node.
4. Rendering SHALL use a locally vendored force-graph (d3-force physics; no CDN at
   runtime) with a live simulation — dragging nodes stretches connections elastically.
