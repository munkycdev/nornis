// force-graph wrapper for the artifact graph: a live d3-force simulation, so edges
// behave like springs — drag a node and the web stretches and relaxes. Blazor hands
// over the full world graph once; focus/depth filtering happens here. Node taps call
// back into .NET.
window.nornisGraph = (function () {
    let fg = null;
    let resizeObserver = null;

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
        destroy();

        const sub = neighborhood(nodes, edges, focusId, depth);
        let zoomedOnce = false;

        fg = ForceGraph()(el)
            .width(el.clientWidth)
            .height(el.clientHeight)
            .graphData({
                nodes: sub.nodes.map(n => ({ ...n })),
                links: sub.edges.map(e => ({ source: e.sourceId, target: e.targetId, type: e.type }))
            })
            .nodeId("id")
            .nodeLabel(n => `${n.name} — ${n.type} · ${n.status}`)
            .nodeCanvasObject((node, ctx, scale) => {
                const isFocus = node.id === focusId;
                const r = isFocus ? 9 : 5;
                const color = typeColors[node.type] ?? "#9AA5B1";

                ctx.globalAlpha = node.status === "Archived" ? 0.35 : 1;
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
            .linkColor(() => "#C6CFD8")
            .linkWidth(1)
            .linkLabel(l => l.type)
            .d3VelocityDecay(0.25)
            .cooldownTime(8000)
            .onNodeClick(n => dotnetRef.invokeMethodAsync("OnNodeSelected", n.id, n.name, n.type, n.status))
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

        resizeObserver = new ResizeObserver(() => {
            if (fg) {
                fg.width(el.clientWidth).height(el.clientHeight);
            }
        });
        resizeObserver.observe(el);

        return sub.nodes.length;
    }

    function destroy() {
        resizeObserver?.disconnect();
        resizeObserver = null;
        if (fg) {
            fg._destructor?.();
            fg = null;
        }
    }

    return { render, destroy };
})();
