// window.nornisMapEdit — drag-to-adjust for map pins on the source detail page.
// Extraction's positions are approximate; dragging a pin is the manual correction.
// The drop hands normalized coordinates back to .NET, which persists them and
// returns false when the save failed — then the pin snaps back where it was.
//
// Listeners are resolved by viewer id on every event so Blazor re-rendering the
// map never detaches the drag behavior (same approach as nornis-journey.js).
(function () {
    'use strict';

    const instances = new Map();

    function init(viewerId, dotnetRef) {
        destroy(viewerId);

        const state = {
            viewerId, dotnetRef, pin: null, moved: false,
            startX: 0, startY: 0, offsetX: 0, offsetY: 0, origLeft: '', origTop: '',
            onDown: null, onMove: null, onUp: null
        };
        const getViewer = () => document.getElementById(viewerId);

        state.onMove = e => {
            if (!state.pin) return;
            // A small dead zone keeps plain clicks (pin → artifact page) working.
            if (!state.moved && Math.hypot(e.clientX - state.startX, e.clientY - state.startY) < 4) return;
            state.moved = true;
            const viewer = getViewer();
            if (!viewer) return;
            const r = viewer.getBoundingClientRect();
            if (r.width <= 0 || r.height <= 0) return;
            const x = Math.min(1, Math.max(0, (e.clientX - state.offsetX - r.left) / r.width));
            const y = Math.min(1, Math.max(0, (e.clientY - state.offsetY - r.top) / r.height));
            state.pin.style.left = (x * 100) + '%';
            state.pin.style.top = (y * 100) + '%';
        };

        state.onUp = () => {
            const pin = state.pin;
            if (!pin) return;
            const moved = state.moved;
            const orig = { left: state.origLeft, top: state.origTop };
            state.pin = null;
            state.moved = false;
            document.removeEventListener('pointermove', state.onMove);
            document.removeEventListener('pointerup', state.onUp);
            document.body.style.userSelect = '';
            if (!moved) return; // plain click — let the anchor navigate

            // A drag must not also navigate: swallow the click that follows pointerup.
            const swallow = ev => { ev.preventDefault(); ev.stopPropagation(); };
            pin.addEventListener('click', swallow, { capture: true, once: true });
            setTimeout(() => pin.removeEventListener('click', swallow, { capture: true }), 0);

            const x = parseFloat(pin.style.left) / 100;
            const y = parseFloat(pin.style.top) / 100;
            state.dotnetRef.invokeMethodAsync('OnPinDragged', pin.dataset.placemarkId, x, y)
                .then(ok => { if (!ok) { pin.style.left = orig.left; pin.style.top = orig.top; } })
                .catch(() => { pin.style.left = orig.left; pin.style.top = orig.top; });
        };

        state.onDown = e => {
            if (e.button !== 0) return;
            const pin = e.target.closest('.nornis-map-pin');
            if (!pin || !pin.dataset.placemarkId) return;
            const viewer = getViewer();
            if (!viewer || !viewer.contains(pin)) return;
            const r = viewer.getBoundingClientRect();
            if (r.width <= 0 || r.height <= 0) return;

            state.pin = pin;
            state.moved = false;
            state.startX = e.clientX;
            state.startY = e.clientY;
            state.origLeft = pin.style.left;
            state.origTop = pin.style.top;
            // Grab offset: keep the pin's anchor where it was relative to the pointer,
            // so the pin doesn't jump to the cursor on the first move.
            const anchorX = r.left + (parseFloat(pin.style.left) / 100) * r.width;
            const anchorY = r.top + (parseFloat(pin.style.top) / 100) * r.height;
            state.offsetX = e.clientX - anchorX;
            state.offsetY = e.clientY - anchorY;

            document.body.style.userSelect = 'none';
            document.addEventListener('pointermove', state.onMove);
            document.addEventListener('pointerup', state.onUp);
            e.preventDefault(); // no native link-drag ghost image
        };

        const viewer = getViewer();
        if (viewer) {
            viewer.addEventListener('pointerdown', state.onDown);
        }
        instances.set(viewerId, state);
    }

    function destroy(viewerId) {
        const state = instances.get(viewerId);
        if (!state) return;
        const viewer = document.getElementById(viewerId);
        if (viewer && state.onDown) {
            viewer.removeEventListener('pointerdown', state.onDown);
        }
        document.removeEventListener('pointermove', state.onMove);
        document.removeEventListener('pointerup', state.onUp);
        document.body.style.userSelect = '';
        instances.delete(viewerId);
    }

    window.nornisMapEdit = { init, destroy };
})();
