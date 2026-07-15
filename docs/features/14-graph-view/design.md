# Design Document

API: `GET /api/worlds/{id}/artifacts/graph` → `{ nodes: [...], edges: [...] }` via
`ArtifactService.GetGraphAsync`: visibility-filtered artifacts (existing allowed-scope
logic) + `IArtifactRelationshipRepository.ListByArtifactIdsAsync` over the visible ids
with the same scopes; edges whose endpoints are both visible. One payload for the whole
world (~50KB at current scale) so focus/depth changes are client-side.

Web: `/graph` page (nav item after Artifacts) + `/graph/{artifactId:guid}` deep link
(button on artifact detail). Blazor loads the graph JSON once per world, then drives
`wwwroot/js/nornis-graph.js` via JS interop: `nornisGraph.render(el, nodes, edges,
focusId, depth, dotnetRef)` — Cytoscape `cose` layout, type-colored nodes, focus node
emphasized, `tap` callbacks into .NET for the selection card and refocus. Depth control
(1 / 2 / All) and focus autocomplete in a toolbar.
