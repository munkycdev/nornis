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

    // Puts the message at the top of Talk it through's list (feature 41), so a long answer reads
    // down from the question. The list scrolls on its own; the page is left where it is. Room is
    // made below a short answer, or the question couldn't reach the top.
    scrollToMessage(list, index) {
        const message = list && list.querySelector(`[data-talk-index="${index}"]`);
        if (!message) {
            return;
        }

        list.style.paddingBottom = '';
        const below = list.scrollHeight - (message.getBoundingClientRect().top - list.getBoundingClientRect().top + list.scrollTop);
        if (below < list.clientHeight) {
            list.style.paddingBottom = `${list.clientHeight - below}px`;
        }

        list.scrollTop += message.getBoundingClientRect().top - list.getBoundingClientRect().top;
    },
};

document.addEventListener('keydown', e => {
    if (e.target instanceof HTMLTextAreaElement
        && e.target.closest('[data-submit-on-enter]')
        && window.nornisCompose.sendsOnEnter(e)) {
        e.preventDefault();
    }
}, true);
