// window.nornisUpload — direct-to-blob upload for Library documents.
// Blazor Server must not stream file bytes over the SignalR circuit, so the browser
// PUTs the file straight to the short-lived SAS URL the API minted (Chronicis pattern).
(function () {
    'use strict';

    window.nornisUpload = {
        /// Returns the selected file's {name, size, type} or null.
        getFileInfo(inputId) {
            const input = document.getElementById(inputId);
            const file = input?.files?.[0];
            return file ? { name: file.name, size: file.size, type: file.type || '' } : null;
        },

        /// Returns all selected files' {name, size, type} for a multi-file input.
        getFileInfos(inputId) {
            const input = document.getElementById(inputId);
            return Array.from(input?.files ?? []).map(f => ({ name: f.name, size: f.size, type: f.type || '' }));
        },

        /// PUTs the selected file to the SAS URL. Reports progress (0-100) via
        /// dotnetRef.OnUploadProgress and resolves true/false for success.
        send(inputId, sasUrl, contentType, dotnetRef) {
            return this.sendAt(inputId, 0, sasUrl, contentType, dotnetRef);
        },

        /// PUTs the file at the given index of a (multi-file) input to the SAS URL.
        sendAt(inputId, index, sasUrl, contentType, dotnetRef) {
            return new Promise(resolve => {
                const input = document.getElementById(inputId);
                const file = input?.files?.[index];
                if (!file) {
                    resolve(false);
                    return;
                }

                // XMLHttpRequest instead of fetch: upload progress events.
                const xhr = new XMLHttpRequest();
                xhr.open('PUT', sasUrl, true);
                xhr.setRequestHeader('x-ms-blob-type', 'BlockBlob');
                xhr.setRequestHeader('Content-Type', contentType || file.type || 'application/octet-stream');

                xhr.upload.onprogress = e => {
                    if (e.lengthComputable && dotnetRef) {
                        dotnetRef.invokeMethodAsync('OnUploadProgress', Math.round(e.loaded / e.total * 100));
                    }
                };
                xhr.onload = () => resolve(xhr.status >= 200 && xhr.status < 300);
                xhr.onerror = () => resolve(false);

                xhr.send(file);
            });
        },

        open(url) {
            window.open(url, '_blank', 'noopener');
        },
    };
})();
