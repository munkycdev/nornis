// Back that prefers real browser history — so Timeline → artifact → back returns to
// the Timeline (with its view/filter query intact) rather than a hardcoded page.
// The fallback covers direct deep links opened in a fresh tab, where there is no
// history entry to go back to.
window.nornisNav = {
    back(fallback) {
        if (window.history.length > 1) {
            window.history.back();
        } else {
            window.location.assign(fallback);
        }
    },

    // Tab visibility, for the nav's activity poll. A background tab has nobody to show a badge
    // to, but it kept polling forever — and a browser left open overnight is the normal case, not
    // the exceptional one. Each poll is an API call, a database read, and enough traffic to hold
    // a scaled-to-zero container awake.
    //
    // Handlers are kept by token rather than assuming one subscriber: a Blazor Server circuit can
    // re-render the layout, and a listener left attached to a disposed component would keep
    // calling into it.
    _visibilityHandlers: new Map(),
    _visibilityToken: 0,

    watchVisibility(dotNetRef) {
        const handler = () => {
            dotNetRef.invokeMethodAsync('OnTabVisibilityChanged', !document.hidden);
        };

        document.addEventListener('visibilitychange', handler);

        const token = ++this._visibilityToken;
        this._visibilityHandlers.set(token, handler);
        return token;
    },

    unwatchVisibility(token) {
        const handler = this._visibilityHandlers.get(token);
        if (handler) {
            document.removeEventListener('visibilitychange', handler);
            this._visibilityHandlers.delete(token);
        }
    },

    // Read once at startup: a tab can already be in the background by the time the circuit is
    // live — restored sessions and ctrl-clicked links both open hidden — and `visibilitychange`
    // only fires on a change, so nothing would tell us until the user came back.
    isTabVisible() {
        return !document.hidden;
    },
};
