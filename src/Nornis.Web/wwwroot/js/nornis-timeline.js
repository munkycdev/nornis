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
        const getUnnest = () => document.getElementById(elementId + '-unnest');
        const labels = () => {
            const root = getRoot();
            return root ? [...root.querySelectorAll('.nornis-timeline-label[data-storyline-id]')] : [];
        };

        // The un-nest zone is only a live target while it's active (revealed for a drag of a
        // storyline that has a parent). Hit-test by geometry, not DOM events — it's pointer-none.
        function overUnnest(x, y) {
            const zone = getUnnest();
            if (!zone || !zone.classList.contains('nornis-timeline-unnest-active')) return false;
            const r = zone.getBoundingClientRect();
            return x >= r.left && x <= r.right && y >= r.top && y <= r.bottom;
        }

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
            getUnnest()?.classList.remove('nornis-timeline-unnest-over');
        }

        function cleanupDrag() {
            document.removeEventListener('pointermove', state.onMove);
            document.removeEventListener('pointerup', state.onUp);
            document.removeEventListener('keydown', state.onKey, true);
            clearHighlight();
            getUnnest()?.classList.remove('nornis-timeline-unnest-active');
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
                // Only a nested storyline can be un-nested, so reveal the zone only then.
                if (drag.hasParent) {
                    getUnnest()?.classList.add('nornis-timeline-unnest-active');
                }
                drag.ghost = document.createElement('div');
                drag.ghost.className = 'nornis-timeline-ghost';
                drag.ghost.textContent = drag.name;
                document.body.appendChild(drag.ghost);
            }

            drag.ghost.style.left = (e.clientX + 12) + 'px';
            drag.ghost.style.top = (e.clientY + 8) + 'px';

            clearHighlight();
            // The un-nest zone wins over a row when the pointer is over it, so a drag that
            // crosses rows on its way up to the bar doesn't flicker between the two intents.
            if (drag.hasParent && overUnnest(e.clientX, e.clientY)) {
                getUnnest()?.classList.add('nornis-timeline-unnest-over');
            } else {
                const target = findTarget(e.clientX, e.clientY, drag.sourceId);
                if (target) {
                    target.classList.add('nornis-timeline-droptarget');
                }
            }
        };

        state.onUp = e => {
            const drag = state.drag;
            if (!drag) return;

            const started = drag.started;
            const unnest = started && drag.hasParent && overUnnest(e.clientX, e.clientY);
            const target = started && !unnest ? findTarget(e.clientX, e.clientY, drag.sourceId) : null;
            cleanupDrag();

            if (started) {
                swallowNextClick();
                if (unnest) {
                    state.dotnetRef.invokeMethodAsync('OnJsUnnest', drag.sourceId);
                } else if (target) {
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
                hasParent: label.dataset.hasParent === 'true',
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
        document.getElementById(elementId + '-unnest')
            ?.classList.remove('nornis-timeline-unnest-active', 'nornis-timeline-unnest-over');
        document.body.style.cursor = '';
        instances.delete(elementId);
    }

    window.nornisTimeline = { init, destroy };

    // ------------------------------------------------------------- fast tooltips --
    // Custom tooltip for [data-nornis-tip] elements (SVG or HTML). Native <title>
    // tooltips carry a fixed ~1s browser delay and can't be styled; this shows after
    // a short delay (instantly when moving between tipped elements, like native
    // menus), follows the pointer, and renders multi-line content. Delegated on the
    // document and self-initializing, so Blazor re-renders never detach it.
    const TIP_DELAY_MS = 120;
    const TIP_WARM_MS = 400; // moving between tips within this window skips the delay

    let tipEl = null;
    let tipTimer = 0;
    let tipTarget = null;
    let lastHiddenAt = 0;

    function ensureTipEl() {
        if (!tipEl) {
            tipEl = document.createElement('div');
            tipEl.className = 'nornis-tip-bubble';
            tipEl.setAttribute('role', 'tooltip');
            document.body.appendChild(tipEl);
        }
        return tipEl;
    }

    function positionTip(x, y) {
        const el = ensureTipEl();
        const pad = 12;
        const rect = el.getBoundingClientRect();
        let left = x + pad;
        let top = y + pad + 4;
        if (left + rect.width > window.innerWidth - 8) {
            left = x - rect.width - pad;
        }
        if (top + rect.height > window.innerHeight - 8) {
            top = y - rect.height - pad;
        }
        el.style.left = Math.max(8, left) + 'px';
        el.style.top = Math.max(8, top) + 'px';
    }

    function showTip(target, x, y) {
        const text = target.getAttribute('data-nornis-tip');
        if (!text) return;
        const el = ensureTipEl();
        el.textContent = text;
        el.classList.add('nornis-tip-visible');
        positionTip(x, y);
    }

    function hideTip() {
        clearTimeout(tipTimer);
        tipTimer = 0;
        if (tipTarget) {
            lastHiddenAt = performance.now();
        }
        tipTarget = null;
        if (tipEl) {
            tipEl.classList.remove('nornis-tip-visible');
        }
    }

    document.addEventListener('pointerover', e => {
        const target = e.target.closest?.('[data-nornis-tip]');
        if (!target || target === tipTarget) return;

        hideTip();
        tipTarget = target;
        const { clientX, clientY } = e;
        const warm = performance.now() - lastHiddenAt < TIP_WARM_MS;
        tipTimer = setTimeout(() => showTip(target, clientX, clientY), warm ? 0 : TIP_DELAY_MS);
    });

    document.addEventListener('pointerout', e => {
        if (tipTarget && !tipTarget.contains(e.relatedTarget)) {
            hideTip();
        }
    });

    document.addEventListener('pointermove', e => {
        if (tipTarget && tipEl?.classList.contains('nornis-tip-visible')) {
            positionTip(e.clientX, e.clientY);
        }
    });

    // Clicks navigate and scrolls shift the layout under the pointer — drop the tip.
    document.addEventListener('pointerdown', hideTip, true);
    document.addEventListener('scroll', hideTip, true);
})();
