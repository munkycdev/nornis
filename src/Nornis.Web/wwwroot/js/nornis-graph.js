// Cytoscape wrapper for the artifact graph. Blazor hands over the full world graph
// once; focus/depth filtering happens here. Node taps call back into .NET.
window.nornisGraph = (function () {
    let cy = null;

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
        if (cy) { cy.destroy(); cy = null; }

        const sub = neighborhood(nodes, edges, focusId, depth);

        cy = cytoscape({
            container: el,
            elements: [
                ...sub.nodes.map(n => ({
                    data: { id: n.id, label: n.name, type: n.type, status: n.status }
                })),
                ...sub.edges.map(e => ({
                    data: { id: e.id, source: e.sourceId, target: e.targetId, label: e.type }
                }))
            ],
            style: [
                {
                    selector: "node",
                    style: {
                        "background-color": ele => typeColors[ele.data("type")] ?? "#9AA5B1",
                        "label": "data(label)",
                        "font-size": "10px",
                        "color": "#2A3B4C",
                        "text-valign": "bottom",
                        "text-margin-y": "4px",
                        "width": 22,
                        "height": 22,
                        "text-wrap": "ellipsis",
                        "text-max-width": "110px",
                        "opacity": ele => ele.data("status") === "Archived" ? 0.35 : 1
                    }
                },
                {
                    selector: `node[id = "${focusId}"]`,
                    style: {
                        "width": 38,
                        "height": 38,
                        "border-width": 3,
                        "border-color": "#C98A4B",
                        "font-weight": "bold"
                    }
                },
                {
                    selector: "edge",
                    style: {
                        "width": 1.5,
                        "line-color": "#C6CFD8",
                        "curve-style": "bezier",
                        "label": "data(label)",
                        "font-size": "7px",
                        "color": "#8A97A5",
                        "text-rotation": "autorotate"
                    }
                },
                {
                    selector: "node:selected",
                    style: { "border-width": 3, "border-color": "#2A3B4C" }
                }
            ],
            layout: {
                name: "cose",
                animate: false,
                padding: 30,
                nodeRepulsion: 8000,
                idealEdgeLength: 90
            },
            wheelSensitivity: 0.3
        });

        cy.on("tap", "node", evt => {
            const d = evt.target.data();
            dotnetRef.invokeMethodAsync("OnNodeSelected", d.id, d.label, d.type, d.status);
        });

        return sub.nodes.length;
    }

    function destroy() {
        if (cy) { cy.destroy(); cy = null; }
    }

    return { render, destroy };
})();
