// Browser side of push notifications. Kept in one place so the Blazor component can stay
// declarative and never touch the Push API directly.

window.nornisPush = (function () {
    function urlBase64ToUint8Array(base64String) {
        // VAPID keys travel as URL-safe base64; the Push API wants raw bytes.
        const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
        const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
        const raw = window.atob(base64);
        const output = new Uint8Array(raw.length);
        for (let i = 0; i < raw.length; ++i) {
            output[i] = raw.charCodeAt(i);
        }
        return output;
    }

    function browserLabel() {
        const ua = navigator.userAgent;
        const name =
            /Edg\//.test(ua) ? 'Edge' :
            /OPR\//.test(ua) ? 'Opera' :
            /Firefox\//.test(ua) ? 'Firefox' :
            /Chrome\//.test(ua) ? 'Chrome' :
            /Safari\//.test(ua) ? 'Safari' : 'Browser';
        const platform =
            /Windows/.test(ua) ? 'Windows' :
            /Android/.test(ua) ? 'Android' :
            /iPhone|iPad/.test(ua) ? 'iOS' :
            /Mac OS X/.test(ua) ? 'macOS' :
            /Linux/.test(ua) ? 'Linux' : '';
        return platform ? `${name} on ${platform}` : name;
    }

    return {
        // Whether this browser could do it at all — old browsers and non-HTTPS origins cannot.
        isSupported: function () {
            return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
        },

        // 'granted' | 'denied' | 'default'. Denied is terminal until the user changes it in
        // browser settings, so the UI has to say so rather than offer the button again.
        permission: function () {
            return 'Notification' in window ? Notification.permission : 'unsupported';
        },

        // Already subscribed on this browser? Used to show the right state on load without
        // prompting for anything.
        currentEndpoint: async function () {
            if (!this.isSupported()) {
                return null;
            }
            const reg = await navigator.serviceWorker.getRegistration('/service-worker.js');
            if (!reg) {
                return null;
            }
            const sub = await reg.pushManager.getSubscription();
            return sub ? sub.endpoint : null;
        },

        // Registers the worker, asks permission, subscribes, and hands the keys back for the
        // server to store. Only ever called from an explicit click — a permission prompt on
        // page load is how sites get themselves permanently blocked.
        subscribe: async function (vapidPublicKey) {
            if (!this.isSupported()) {
                return { ok: false, reason: 'unsupported' };
            }

            const permission = await Notification.requestPermission();
            if (permission !== 'granted') {
                return { ok: false, reason: permission };
            }

            const reg = await navigator.serviceWorker.register('/service-worker.js');
            await navigator.serviceWorker.ready;

            let sub = await reg.pushManager.getSubscription();
            if (!sub) {
                sub = await reg.pushManager.subscribe({
                    userVisibleOnly: true,
                    applicationServerKey: urlBase64ToUint8Array(vapidPublicKey)
                });
            }

            const json = sub.toJSON();
            return {
                ok: true,
                endpoint: sub.endpoint,
                p256dh: json.keys.p256dh,
                auth: json.keys.auth,
                label: browserLabel()
            };
        },

        // Unsubscribes locally and returns the endpoint so the server can forget it too.
        unsubscribe: async function () {
            if (!this.isSupported()) {
                return null;
            }
            const reg = await navigator.serviceWorker.getRegistration('/service-worker.js');
            if (!reg) {
                return null;
            }
            const sub = await reg.pushManager.getSubscription();
            if (!sub) {
                return null;
            }
            const endpoint = sub.endpoint;
            await sub.unsubscribe();
            return endpoint;
        }
    };
})();
