// Enter sends in a message box marked data-submit-on-enter (Prep's Talk it through). Blazor can't
// cancel a key's default conditionally, so this stops the newline in the browser and the
// component's own key handler sends. Shift+Enter, or Enter while an IME is composing, keeps the
// textarea's usual behaviour.
//
// One listener on the document rather than one per box, so a box that appears later (the panel
// re-rendering, a conversation cleared from full) needs no setup. Capture phase, so it runs
// before anything inside the box can act on the key; preventDefault doesn't stop Blazor seeing it.
window.nornisCompose = {
    // Mirrors AdventureConversationPanel.SendsOnEnter; SubmitOnEnterMirrorTests holds the two together.
    sendsOnEnter(e) {
        return e.key === 'Enter' && !e.shiftKey && !e.ctrlKey && !e.altKey && !e.metaKey && !e.isComposing;
    },
};

document.addEventListener('keydown', e => {
    if (e.target instanceof HTMLTextAreaElement
        && e.target.closest('[data-submit-on-enter]')
        && window.nornisCompose.sendsOnEnter(e)) {
        e.preventDefault();
    }
}, true);
