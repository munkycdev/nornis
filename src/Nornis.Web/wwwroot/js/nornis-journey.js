// window.nornisJourney — pointer-drag for the journey scrubber's playhead.
// Native <input type=range> can't sit its thumb on non-uniform (calendar-dated)
// session ticks, so the playhead is a custom element dragged with pointer events —
// the same approach the storyline timeline uses. On drag the fraction across the
// rail (0..1) is handed back to .NET, which snaps it to the nearest dated stop.
//
// Listeners are resolved by rail id on every event so Blazor re-rendering the
// scrubber never detaches the drag behavior.
(function () {
    'use strict';

    const instances = new Map();

    function init(railId, dotnetRef) {
        destroy(railId);

        const state = { railId, dotnetRef, dragging: false, onDown: null, onMove: null, onUp: null };
        const getRail = () => document.getElementById(railId);

        function reportFrac(clientX) {
            const rail = getRail();
            if (!rail) return;
            const r = rail.getBoundingClientRect();
            if (r.width <= 0) return;
            const frac = Math.min(1, Math.max(0, (clientX - r.left) / r.width));
            state.dotnetRef.invokeMethodAsync('OnScrubFraction', frac);
        }

        state.onMove = e => {
            if (!state.dragging) return;
            reportFrac(e.clientX);
        };

        state.onUp = () => {
            if (!state.dragging) return;
            state.dragging = false;
            document.body.style.userSelect = '';
            document.removeEventListener('pointermove', state.onMove);
            document.removeEventListener('pointerup', state.onUp);
        };

        state.onDown = e => {
            if (e.button !== 0) return;
            const rail = getRail();
            if (!rail) return;
            // A click on a session tick is handled by Blazor; dragging starts from the
            // handle or from empty rail track.
            if (e.target.closest('.nornis-journey-node')) return;
            if (!e.target.closest('.nornis-journey-rail')) return;

            state.dragging = true;
            document.body.style.userSelect = 'none';
            document.addEventListener('pointermove', state.onMove);
            document.addEventListener('pointerup', state.onUp);
            reportFrac(e.clientX);
            e.preventDefault();
        };

        const rail = getRail();
        if (rail) {
            rail.addEventListener('pointerdown', state.onDown);
        }
        instances.set(railId, state);
    }

    function destroy(railId) {
        const state = instances.get(railId);
        if (!state) return;
        const rail = document.getElementById(railId);
        if (rail && state.onDown) {
            rail.removeEventListener('pointerdown', state.onDown);
        }
        document.removeEventListener('pointermove', state.onMove);
        document.removeEventListener('pointerup', state.onUp);
        document.body.style.userSelect = '';
        instances.delete(railId);
    }

    window.nornisJourney = { init, destroy };
})();
