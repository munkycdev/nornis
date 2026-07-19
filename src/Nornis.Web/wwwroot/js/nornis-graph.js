// force-graph wrapper for the artifact graph: a live d3-force simulation, so edges
// behave like springs — drag a node and the web stretches and relaxes. Blazor hands
// over the full world graph once; focus/depth filtering happens here. Node taps call
// back into .NET. Instances are keyed by element id so multiple graphs can coexist.
window.nornisGraph = (function () {
    const instances = new Map();

    const typeColors = {
        Character: "#C98A4B",
        Location: "#4B8AC9",
        Item: "#8A6FC9",
        Faction: "#C94B6E",
        Event: "#4BC98A",
        Storyline: "#C9B44B",
        Concept: "#6EC9C0",
        Document: "#9AA5B1"
    };

    // Distinct, readable hues for coding highlighted edges by relationship type. Assigned
    // in sorted order of the types present so the mapping is stable within a graph.
    const relationshipPalette = [
        "#C94B6E", "#4B8AC9", "#4BC98A", "#C98A4B", "#8A6FC9",
        "#6EC9C0", "#C9B44B", "#B5642E", "#4BA0C9", "#9A6EC9"
    ];

    const BASE_LINK = "#C6CFD8";              // normal, nothing selected
    const DIM_LINK = "rgba(198,207,216,0.12)"; // faded when a selection is active
    const DIM_NODE_ALPHA = 0.15;
    const HIGHLIGHT_DEPTH = 3;                 // hops of edges to light up from the clicked node

    function linkEndId(end) {
        return typeof end === "object" ? end.id : end;
    }

    function neighborhood(nodes, edges, focusId, depth) {
        if (!focusId) return { nodes, edges };
        const adj = new Map();
        for (const e of edges) {
            (adj.get(e.sourceId) ?? adj.set(e.sourceId, []).get(e.sourceId)).push(e.targetId);
            (adj.get(e.targetId) ?? adj.set(e.targetId, []).get(e.targetId)).push(e.sourceId);
        }
        const keep = new Set([focusId]);
        let frontier = [focusId];
        for (let d = 0; d < depth; d++) {
            const next = [];
            for (const id of frontier) {
                for (const n of adj.get(id) ?? []) {
                    if (!keep.has(n)) { keep.add(n); next.push(n); }
                }
            }
            frontier = next;
        }
        return {
            nodes: nodes.filter(n => keep.has(n.id)),
            edges: edges.filter(e => keep.has(e.sourceId) && keep.has(e.targetId))
        };
    }

    function render(elementId, nodes, edges, focusId, depth, dotnetRef) {
        const el = document.getElementById(elementId);
        if (!el) return 0;
        destroy(elementId);

        const sub = neighborhood(nodes, edges, focusId, depth);

        // Stable relationship-type → color map for this render.
        const relColors = new Map();
        [...new Set(sub.edges.map(e => e.type).filter(Boolean))].sort()
            .forEach((t, i) => relColors.set(t, relationshipPalette[i % relationshipPalette.length]));
        const relColor = type => relColors.get(type) ?? "#6B7A88";

        // Click-highlight state: the selected node plus the edges/nodes within HIGHLIGHT_DEPTH.
        const hl = { selectedId: null, links: new Set(), nodes: new Set() };
        let zoomedOnce = false;

        const linkColorFor = l =>
            hl.links.size === 0 ? BASE_LINK : (hl.links.has(l) ? relColor(l.type) : DIM_LINK);
        const linkWidthFor = l => hl.links.has(l) ? 2.5 : 1;

        function setHighlight(nodeId) {
            const data = fg.graphData();
            const adj = new Map();
            for (const l of data.links) {
                const s = linkEndId(l.source);
                const t = linkEndId(l.target);
                (adj.get(s) ?? adj.set(s, []).get(s)).push({ link: l, other: t });
                (adj.get(t) ?? adj.set(t, []).get(t)).push({ link: l, other: s });
            }

            const links = new Set();
            const visited = new Set([nodeId]);
            let frontier = [nodeId];
            for (let d = 0; d < HIGHLIGHT_DEPTH; d++) {
                const next = [];
                for (const id of frontier) {
                    for (const { link, other } of adj.get(id) ?? []) {
                        links.add(link);
                        if (!visited.has(other)) { visited.add(other); next.push(other); }
                    }
                }
                frontier = next;
            }

            hl.selectedId = nodeId;
            hl.links = links;
            hl.nodes = visited;
        }

        function clearHighlight() {
            hl.selectedId = null;
            hl.links = new Set();
            hl.nodes = new Set();
        }

        // Re-setting the style accessors with fresh closures forces a repaint so the new
        // highlight shows immediately.
        function refresh() {
            fg.linkColor(l => linkColorFor(l)).linkWidth(l => linkWidthFor(l));
        }

        function notifyLegend() {
            if (hl.links.size === 0) {
                dotnetRef.invokeMethodAsync("OnLinksHighlighted", null, []);
                return;
            }
            const types = [...new Set([...hl.links].map(l => l.type).filter(Boolean))].sort();
            const items = types.map(t => ({ type: t, color: relColor(t) }));
            const name = fg.graphData().nodes.find(n => n.id === hl.selectedId)?.name ?? null;
            dotnetRef.invokeMethodAsync("OnLinksHighlighted", name, items);
        }

        const fg = ForceGraph()(el)
            .width(el.clientWidth)
            .height(el.clientHeight)
            .graphData({
                nodes: sub.nodes.map(n => ({ ...n })),
                links: sub.edges.map(e => ({ source: e.sourceId, target: e.targetId, type: e.type }))
            })
            .nodeId("id")
            .nodeLabel(n => `${n.name} — ${n.type} · ${n.status}`)
            // Keep repainting so click-highlight state changes show without a follow-up
            // interaction; artifact graphs are bounded so the cost is modest.
            .autoPauseRedraw(false)
            .nodeCanvasObject((node, ctx, scale) => {
                const isFocus = node.id === focusId || node.id === hl.selectedId;
                const dimmed = hl.links.size > 0 && !hl.nodes.has(node.id);
                const r = isFocus ? 9 : 5;
                const color = typeColors[node.type] ?? "#9AA5B1";

                ctx.globalAlpha = dimmed ? DIM_NODE_ALPHA : (node.status === "Archived" ? 0.35 : 1);
                ctx.beginPath();
                ctx.arc(node.x, node.y, r, 0, 2 * Math.PI);
                ctx.fillStyle = color;
                ctx.fill();
                if (isFocus) {
                    ctx.lineWidth = 2;
                    ctx.strokeStyle = "#2A3B4C";
                    ctx.stroke();
                }

                // Labels only when zoomed in enough to read them (or on the focus node)
                if (scale > 1.2 || isFocus) {
                    const fontSize = Math.max(10 / scale, 2.5);
                    ctx.font = `${isFocus ? "bold " : ""}${fontSize}px Inter, sans-serif`;
                    ctx.textAlign = "center";
                    ctx.textBaseline = "top";
                    ctx.fillStyle = "#2A3B4C";
                    ctx.fillText(node.name, node.x, node.y + r + 2);
                }
                ctx.globalAlpha = 1;
            })
            .nodePointerAreaPaint((node, color, ctx) => {
                ctx.beginPath();
                ctx.arc(node.x, node.y, 10, 0, 2 * Math.PI);
                ctx.fillStyle = color;
                ctx.fill();
            })
            .linkColor(l => linkColorFor(l))
            .linkWidth(l => linkWidthFor(l))
            .linkLabel(l => l.type)
            .d3VelocityDecay(0.25)
            .cooldownTime(8000)
            .onNodeClick(node => {
                // Toggle: re-clicking the selected node clears the highlight.
                if (hl.selectedId === node.id) {
                    clearHighlight();
                } else {
                    setHighlight(node.id);
                }
                refresh();
                notifyLegend();
                dotnetRef.invokeMethodAsync("OnNodeSelected", node.id, node.name, node.type, node.status);
            })
            .onBackgroundClick(() => {
                if (hl.selectedId !== null) {
                    clearHighlight();
                    refresh();
                    notifyLegend();
                }
            })
            .onNodeDragEnd(n => {
                // Release the node so the springs pull the web back into shape —
                // this is the elastic feel; pinning (fx/fy) would freeze it.
                n.fx = undefined;
                n.fy = undefined;
            })
            .onEngineStop(() => {
                if (!zoomedOnce) {
                    zoomedOnce = true;
                    fg.zoomToFit(400, 40);
                }
            });

        const resizeObserver = new ResizeObserver(() => {
            fg.width(el.clientWidth).height(el.clientHeight);
        });
        resizeObserver.observe(el);

        instances.set(elementId, { fg, resizeObserver });

        return sub.nodes.length;
    }

    function destroy(elementId) {
        const instance = instances.get(elementId);
        if (!instance) return;
        instance.resizeObserver.disconnect();
        instance.fg._destructor?.();
        instances.delete(elementId);
    }

    return { render, destroy };
})();
