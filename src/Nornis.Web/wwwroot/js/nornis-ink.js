// window.nornisInk — infinite vertical ink canvas for handwritten session notes.
// Strokes live in world coordinates (y grows downward forever); the canvas is a
// viewport panned by touch/wheel. Pen (or mouse) draws; a single finger scrolls —
// the OneNote model. Stroke data is plain JSON: the true source persisted to blob
// storage; PNG tiles are rendered on demand for vision transcription.
(function () {
    'use strict';

    const instances = new Map();

    class InkSurface {
        constructor(host, dotnetRef, readOnly) {
            this.host = host;
            this.dotnetRef = dotnetRef;
            this.readOnly = readOnly;
            this.strokes = [];
            this.undoStack = []; // {kind:'add'|'erase', stroke, index}
            this.current = null;
            this.scrollY = 0;
            this.tool = 'pen';
            this.penSize = 2.5;
            this.color = '#1a2333';
            this.dirtyTimer = null;

            this.canvas = document.createElement('canvas');
            this.canvas.style.cssText = 'display:block;width:100%;height:100%;touch-action:none;';
            host.appendChild(this.canvas);
            this.ctx = this.canvas.getContext('2d');

            this.resizeObserver = new ResizeObserver(() => this.resize());
            this.resizeObserver.observe(host);
            this.resize();

            if (!readOnly) {
                this.canvas.addEventListener('pointerdown', e => this.onDown(e));
                this.canvas.addEventListener('pointermove', e => this.onMove(e));
                this.canvas.addEventListener('pointerup', e => this.onUp(e));
                this.canvas.addEventListener('pointercancel', e => this.onUp(e));
            }
            this.canvas.addEventListener('wheel', e => {
                e.preventDefault();
                this.pan(e.deltaY);
            }, { passive: false });
        }

        resize() {
            const dpr = window.devicePixelRatio || 1;
            const rect = this.host.getBoundingClientRect();
            this.cssWidth = Math.max(1, rect.width);
            this.cssHeight = Math.max(1, rect.height);
            this.canvas.width = Math.round(this.cssWidth * dpr);
            this.canvas.height = Math.round(this.cssHeight * dpr);
            this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            this.redraw();
        }

        // ------------------------------------------------------------- input --

        isDrawingPointer(e) {
            return e.pointerType === 'pen' || e.pointerType === 'mouse';
        }

        toWorld(e) {
            const rect = this.canvas.getBoundingClientRect();
            return [
                +(e.clientX - rect.left).toFixed(1),
                +(e.clientY - rect.top + this.scrollY).toFixed(1),
                +(e.pressure || 0.5).toFixed(2),
            ];
        }

        onDown(e) {
            if (this.isDrawingPointer(e)) {
                if (e.buttons === 32 || this.tool === 'eraser') {
                    // Barrel-button erase or eraser tool: delete the stroke under the pointer.
                    this.eraseAt(this.toWorld(e));
                    this.erasing = true;
                } else {
                    this.current = { points: [this.toWorld(e)], size: this.penSize, color: this.color };
                }
                try { this.canvas.setPointerCapture(e.pointerId); } catch { /* synthetic pointers can't be captured */ }
                e.preventDefault();
            } else if (e.pointerType === 'touch') {
                this.touchPan = { y: e.clientY, id: e.pointerId };
            }
        }

        onMove(e) {
            if (this.erasing && this.isDrawingPointer(e)) {
                this.eraseAt(this.toWorld(e));
                return;
            }
            if (this.current && this.isDrawingPointer(e)) {
                if (e.buttons === 0) { return; } // mouse hover, not drawing
                // Coalesced events give smoother curves, but the list can be empty
                // (synthetic events, some browsers between frames) — fall back to e.
                let events = e.getCoalescedEvents ? e.getCoalescedEvents() : [];
                if (events.length === 0) { events = [e]; }
                for (const ev of events) {
                    this.current.points.push(this.toWorld(ev));
                }
                this.redraw();
                e.preventDefault();
            } else if (this.touchPan && e.pointerId === this.touchPan.id) {
                this.pan(this.touchPan.y - e.clientY);
                this.touchPan.y = e.clientY;
                e.preventDefault();
            }
        }

        onUp(e) {
            if (this.current) {
                if (this.current.points.length > 1) {
                    this.strokes.push(this.current);
                    this.undoStack.push({ kind: 'add' });
                    this.markDirty();
                }
                this.current = null;
                this.redraw();
            }
            this.erasing = false;
            this.touchPan = null;
        }

        pan(dy) {
            this.scrollY = Math.max(0, this.scrollY + dy);
            this.redraw();
        }

        eraseAt(world) {
            const threshold = 12;
            for (let i = this.strokes.length - 1; i >= 0; i--) {
                const stroke = this.strokes[i];
                if (stroke.points.some(p =>
                    Math.abs(p[0] - world[0]) < threshold && Math.abs(p[1] - world[1]) < threshold)) {
                    this.undoStack.push({ kind: 'erase', stroke, index: i });
                    this.strokes.splice(i, 1);
                    this.markDirty();
                    this.redraw();
                    return;
                }
            }
        }

        undo() {
            const op = this.undoStack.pop();
            if (!op) { return; }
            if (op.kind === 'add') {
                this.strokes.pop();
            } else {
                this.strokes.splice(op.index, 0, op.stroke);
            }
            this.markDirty();
            this.redraw();
        }

        markDirty() {
            if (!this.dotnetRef) { return; }
            clearTimeout(this.dirtyTimer);
            // Debounce so a writing burst produces one autosave, not dozens.
            this.dirtyTimer = setTimeout(() => {
                this.dotnetRef.invokeMethodAsync('OnInkChanged').catch(() => { });
            }, 3000);
        }

        // ------------------------------------------------------------ drawing --

        redraw() {
            const ctx = this.ctx;
            ctx.clearRect(0, 0, this.cssWidth, this.cssHeight);

            // Faint ruled lines give the page a notebook feel and a scroll anchor.
            ctx.strokeStyle = 'rgba(26,35,51,0.07)';
            ctx.lineWidth = 1;
            const spacing = 36;
            const first = spacing - (this.scrollY % spacing);
            for (let y = first; y < this.cssHeight; y += spacing) {
                ctx.beginPath();
                ctx.moveTo(0, y);
                ctx.lineTo(this.cssWidth, y);
                ctx.stroke();
            }

            const top = this.scrollY - 50;
            const bottom = this.scrollY + this.cssHeight + 50;
            for (const stroke of this.strokes) {
                if (stroke.points[0][1] > bottom || stroke.points[stroke.points.length - 1][1] < top) {
                    continue;
                }
                this.drawStroke(ctx, stroke, 0, -this.scrollY);
            }
            if (this.current) {
                this.drawStroke(ctx, this.current, 0, -this.scrollY);
            }
        }

        // Midpoint quadratic smoothing with pressure-scaled width per segment.
        drawStroke(ctx, stroke, dx, dy) {
            const pts = stroke.points;
            ctx.strokeStyle = stroke.color || '#1a2333';
            ctx.lineCap = 'round';
            ctx.lineJoin = 'round';

            if (pts.length === 1) {
                ctx.beginPath();
                ctx.arc(pts[0][0] + dx, pts[0][1] + dy, (stroke.size || 2.5) / 2, 0, Math.PI * 2);
                ctx.fillStyle = ctx.strokeStyle;
                ctx.fill();
                return;
            }

            for (let i = 1; i < pts.length; i++) {
                const p0 = pts[i - 1];
                const p1 = pts[i];
                const mid = [(p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2];
                ctx.beginPath();
                ctx.lineWidth = Math.max(0.75, (stroke.size || 2.5) * (0.5 + (p1[2] ?? 0.5)));
                if (i === 1) {
                    ctx.moveTo(p0[0] + dx, p0[1] + dy);
                    ctx.lineTo(mid[0] + dx, mid[1] + dy);
                } else {
                    const prevMid = [(pts[i - 2][0] + p0[0]) / 2, (pts[i - 2][1] + p0[1]) / 2];
                    ctx.moveTo(prevMid[0] + dx, prevMid[1] + dy);
                    ctx.quadraticCurveTo(p0[0] + dx, p0[1] + dy, mid[0] + dx, mid[1] + dy);
                }
                ctx.stroke();
            }
        }

        // ------------------------------------------------------- persistence --

        getInkJson() {
            return JSON.stringify({ version: 1, strokes: this.strokes });
        }

        loadInk(json) {
            try {
                const doc = JSON.parse(json);
                this.strokes = Array.isArray(doc?.strokes) ? doc.strokes : [];
            } catch {
                this.strokes = [];
            }
            this.undoStack = [];
            this.redraw();
        }

        hasInk() {
            return this.strokes.length > 0;
        }

        /// Renders the written extent to PNG tiles (data URLs) for transcription.
        renderTiles(tileWidth, tileHeight) {
            if (this.strokes.length === 0) { return []; }

            let maxY = 0;
            for (const stroke of this.strokes) {
                for (const p of stroke.points) {
                    if (p[1] > maxY) { maxY = p[1]; }
                }
            }

            const scale = tileWidth / this.cssWidth;
            const worldTileHeight = tileHeight / scale;
            const tileCount = Math.max(1, Math.ceil((maxY + 40) / worldTileHeight));
            const tiles = [];

            for (let t = 0; t < tileCount; t++) {
                const tile = document.createElement('canvas');
                tile.width = tileWidth;
                tile.height = tileHeight;
                const tctx = tile.getContext('2d');
                tctx.fillStyle = '#ffffff';
                tctx.fillRect(0, 0, tileWidth, tileHeight);
                tctx.setTransform(scale, 0, 0, scale, 0, 0);

                const offset = -t * worldTileHeight;
                for (const stroke of this.strokes) {
                    this.drawStroke(tctx, stroke, 0, offset);
                }
                tiles.push(tile.toDataURL('image/png'));
            }

            return tiles;
        }

        destroy() {
            clearTimeout(this.dirtyTimer);
            this.resizeObserver.disconnect();
            this.canvas.remove();
        }
    }

    window.nornisInk = {
        init(elementId, inkJson, dotnetRef, readOnly) {
            const host = document.getElementById(elementId);
            if (!host) { return false; }
            this.destroy(elementId);
            const surface = new InkSurface(host, dotnetRef, !!readOnly);
            if (inkJson) { surface.loadInk(inkJson); }
            instances.set(elementId, surface);
            return true;
        },

        /// Fetches the ink JSON from a (SAS) URL and loads it — avoids pushing large
        /// documents through the Blazor circuit.
        async initFromUrl(elementId, url, dotnetRef, readOnly) {
            const ok = this.init(elementId, null, dotnetRef, readOnly);
            if (!ok) { return false; }
            try {
                const response = await fetch(url);
                if (response.ok) {
                    instances.get(elementId)?.loadInk(await response.text());
                }
            } catch { /* empty canvas is the fallback */ }
            return true;
        },

        // NOTE: bulk data (ink JSON, rendered tiles) must NEVER return through JS
        // interop — Blazor Server's SignalR receive limit (32 KB) kills the circuit.
        // JS PUTs straight to blob SAS URLs; only booleans and counts cross.

        hasInk(elementId) { return instances.get(elementId)?.hasInk() ?? false; },
        inkSize(elementId) { return instances.get(elementId)?.getInkJson()?.length ?? 0; },
        setTool(elementId, tool) { const s = instances.get(elementId); if (s) { s.tool = tool; } },
        undo(elementId) { instances.get(elementId)?.undo(); },

        /// PUTs the current ink JSON straight to a blob SAS URL.
        async saveInk(elementId, sasUrl) {
            const surface = instances.get(elementId);
            if (!surface) { return false; }
            try {
                const response = await fetch(sasUrl, {
                    method: 'PUT',
                    headers: { 'x-ms-blob-type': 'BlockBlob', 'Content-Type': 'application/json' },
                    body: surface.getInkJson(),
                });
                return response.ok;
            } catch {
                return false;
            }
        },

        /// Renders the written extent to PNG tiles, kept in JS. Returns the tile count.
        renderTileCount(elementId, w, h) {
            const surface = instances.get(elementId);
            if (!surface) { return 0; }
            surface.pendingTiles = surface.renderTiles(w, h);
            return surface.pendingTiles.length;
        },

        /// PUTs a previously rendered tile straight to a blob SAS URL.
        async putTile(elementId, index, sasUrl) {
            const tile = instances.get(elementId)?.pendingTiles?.[index];
            if (!tile) { return false; }
            try {
                const blob = await (await fetch(tile)).blob();
                const response = await fetch(sasUrl, {
                    method: 'PUT',
                    headers: { 'x-ms-blob-type': 'BlockBlob', 'Content-Type': 'image/png' },
                    body: blob,
                });
                return response.ok;
            } catch {
                return false;
            }
        },

        clearTiles(elementId) {
            const surface = instances.get(elementId);
            if (surface) { surface.pendingTiles = null; }
        },

        destroy(elementId) {
            instances.get(elementId)?.destroy();
            instances.delete(elementId);
        },
    };
})();
