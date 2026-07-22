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
};
