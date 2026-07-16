// window.nornisTimeline — pointer-based drag-to-nest for the storyline timeline.
// Replaces native HTML5 drag-and-drop, which is unreliable under Blazor Server
// (no dataTransfer.setData means Firefox never starts a drag; dragenter/leave
// flicker on child elements; drop targets limited to the small gutter labels).
// Pointer events work everywhere, and the drop target is the whole row band —
// releasing anywhere on a storyline's row nests onto that storyline.
//
// Listeners are document-delegated and the container is resolved by id on every
// event, so Blazor re-rendering (or briefly removing) the chart never detaches
// the drag behavior.
(function () {
    'use strict';

    const instances = new Map();
    const DRAG_THRESHOLD_PX = 6;

    function init(elementId, dotnetRef) {
        destroy(elementId);

        const state = { elementId, dotnetRef, drag: null, onPointerDown: null, onMove: null, onUp: null, onKey: null };

        const getRoot = () => document.getElementById(elementId);
        const labels = () => {
            const root = getRoot();
            return root ? [...root.querySelectorAll('.nornis-timeline-label[data-storyline-id]')] : [];
        };

        // A drop lands on the storyline whose row band contains the pointer's Y,
        // anywhere within the chart horizontally — the row is the target, not the label.
        function findTarget(x, y, sourceId) {
            const root = getRoot();
            if (!root) return null;
            const wrap = root.getBoundingClientRect();
            if (x < wrap.left || x > wrap.right || y < wrap.top || y > wrap.bottom) {
                return null;
            }
            return labels().find(el => {
                if (el.dataset.storylineId === sourceId) return false;
                const r = el.getBoundingClientRect();
                return y >= r.top && y <= r.bottom;
            }) || null;
        }

        function clearHighlight() {
            labels().forEach(el => el.classList.remove('nornis-timeline-droptarget'));
        }

        function cleanupDrag() {
            document.removeEventListener('pointermove', state.onMove);
            document.removeEventListener('pointerup', state.onUp);
            document.removeEventListener('keydown', state.onKey, true);
            clearHighlight();
            document.body.style.cursor = '';
            if (state.drag?.ghost) {
                state.drag.ghost.remove();
            }
            state.drag = null;
        }

        // A completed drag ends with pointerup on the source label, which the browser
        // follows with a click — swallow that one click so it doesn't navigate.
        function swallowNextClick() {
            const swallow = e => {
                e.preventDefault();
                e.stopPropagation();
            };
            document.addEventListener('click', swallow, { capture: true, once: true });
            setTimeout(() => document.removeEventListener('click', swallow, true), 150);
        }

        state.onMove = e => {
            const drag = state.drag;
            if (!drag) return;

            if (!drag.started) {
                const dx = e.clientX - drag.startX;
                const dy = e.clientY - drag.startY;
                if (Math.hypot(dx, dy) < DRAG_THRESHOLD_PX) return;

                drag.started = true;
                document.body.style.cursor = 'grabbing';
                drag.ghost = document.createElement('div');
                drag.ghost.className = 'nornis-timeline-ghost';
                drag.ghost.textContent = drag.name;
                document.body.appendChild(drag.ghost);
            }

            drag.ghost.style.left = (e.clientX + 12) + 'px';
            drag.ghost.style.top = (e.clientY + 8) + 'px';

            clearHighlight();
            const target = findTarget(e.clientX, e.clientY, drag.sourceId);
            if (target) {
                target.classList.add('nornis-timeline-droptarget');
            }
        };

        state.onUp = e => {
            const drag = state.drag;
            if (!drag) return;

            const started = drag.started;
            const target = started ? findTarget(e.clientX, e.clientY, drag.sourceId) : null;
            cleanupDrag();

            if (started) {
                swallowNextClick();
                if (target) {
                    state.dotnetRef.invokeMethodAsync('OnJsReparent', drag.sourceId, target.dataset.storylineId);
                }
            }
        };

        state.onKey = e => {
            if (e.key === 'Escape' && state.drag) {
                const started = state.drag.started;
                cleanupDrag();
                if (started) {
                    swallowNextClick();
                }
            }
        };

        state.onPointerDown = e => {
            if (e.button !== 0 || state.drag) return;
            const root = getRoot();
            if (!root) return;
            const label = e.target.closest('.nornis-timeline-label[data-storyline-id]');
            if (!label || !root.contains(label)) return;

            state.drag = {
                sourceId: label.dataset.storylineId,
                name: (label.textContent || '').trim(),
                startX: e.clientX,
                startY: e.clientY,
                started: false,
                ghost: null,
            };
            document.addEventListener('pointermove', state.onMove);
            document.addEventListener('pointerup', state.onUp);
            document.addEventListener('keydown', state.onKey, true);
        };

        document.addEventListener('pointerdown', state.onPointerDown);
        instances.set(elementId, state);
    }

    function destroy(elementId) {
        const state = instances.get(elementId);
        if (!state) return;
        document.removeEventListener('pointerdown', state.onPointerDown);
        document.removeEventListener('pointermove', state.onMove);
        document.removeEventListener('pointerup', state.onUp);
        document.removeEventListener('keydown', state.onKey, true);
        if (state.drag?.ghost) {
            state.drag.ghost.remove();
        }
        document.body.style.cursor = '';
        instances.delete(elementId);
    }

    window.nornisTimeline = { init, destroy };
})();
