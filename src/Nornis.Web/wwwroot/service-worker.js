// Notification delivery only. This deliberately does NOT cache anything: Nornis is a Blazor
// Server app whose UI arrives over a live circuit, and a service worker that served stale
// assets would break the app in ways that are miserable to diagnose. Its whole job is to be
// awake when the tab is not, so a push can become a notification.

self.addEventListener('install', () => {
    // Take over immediately rather than waiting for every tab to close — a user who just
    // granted permission should not have to restart the browser for it to work.
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(self.clients.claim());
});

self.addEventListener('push', (event) => {
    if (!event.data) {
        return;
    }

    let payload;
    try {
        payload = event.data.json();
    } catch {
        // Never show a raw or malformed payload; silence beats gibberish on a lock screen.
        return;
    }

    const title = payload.title || 'Nornis';
    const options = {
        body: payload.body || '',
        icon: '/favicon.png',
        badge: '/favicon.png',
        // Same tag replaces rather than stacks: a note reporting twice should not leave two.
        tag: payload.tag || 'nornis',
        renotify: false,
        data: { url: payload.url || '/' }
    };

    event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener('notificationclick', (event) => {
    event.notification.close();

    const target = (event.notification.data && event.notification.data.url) || '/';

    // Focus an existing tab if one is already open rather than piling up windows — someone who
    // left Nornis open and walked away should come back to the tab they left.
    event.waitUntil(
        self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
            for (const client of clientList) {
                if ('focus' in client) {
                    if ('navigate' in client) {
                        return client.navigate(target).then((c) => c && c.focus());
                    }
                    return client.focus();
                }
            }
            return self.clients.openWindow(target);
        })
    );
});
