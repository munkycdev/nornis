// window.nornisUpload — direct-to-blob upload for Library documents.
// Blazor Server must not stream file bytes over the SignalR circuit, so the browser
// PUTs the file straight to the short-lived SAS URL the API minted (Chronicis pattern).
(function () {
    'use strict';

    // Re-encoded stand-ins for the raw picked files, per input id. A phone camera hands over
    // a 12-megapixel HEIC; the API's allowlist has no .heic and no vision model accepts one,
    // so the page normalizes to JPEG before the SAS handshake asks for a ticket. Bounded by
    // the caller's own page cap, which it passes in — see prepareImages.
    const prepared = new Map();

    /// Decodes a picked file, honouring EXIF orientation. A phone photograph carries its
    /// rotation in metadata rather than in the pixels, and handwriting transcribed sideways
    /// reads far worse than handwriting transcribed upright.
    async function decode(file) {
        if (window.createImageBitmap) {
            try {
                return await createImageBitmap(file, { imageOrientation: 'from-image' });
            } catch {
                // Some Safari builds refuse HEIC through this path; the <img> fallback below
                // goes through the platform decoder, which does not.
            }
        }

        return await new Promise((resolve, reject) => {
            const url = URL.createObjectURL(file);
            const img = new Image();
            img.onload = () => { URL.revokeObjectURL(url); resolve(img); };
            img.onerror = () => { URL.revokeObjectURL(url); reject(new Error('decode failed')); };
            img.src = url;
        });
    }

    function jpegName(name) {
        const base = (name || '').replace(/\.[^./\\]*$/, '');
        return (base || 'photo') + '.jpg';
    }

    function describe(file) {
        return { name: file.name, size: file.size, type: file.type || '' };
    }

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
            return Array.from(input?.files ?? []).map(describe);
        },

        /// Re-encodes the input's images as JPEG, no longer than maxEdge on the long side,
        /// and returns the descriptors the SAS handshake should be given — so the ticket is
        /// requested for the bytes that will actually be PUT, not the ones the camera made.
        /// sendAt then prefers the re-encoded blob.
        ///
        /// Downscaling is not only about upload size: the same pixels are billed as vision
        /// input, and a page of handwriting is legible far below what a phone camera shoots.
        ///
        /// A file that will not decode keeps its original bytes and descriptor. The server
        /// owns the list of what it accepts and will say so; guessing here would put that
        /// list in two places.
        async prepareImages(inputId, maxFiles, maxEdge, quality) {
            const input = document.getElementById(inputId);
            const files = Array.from(input?.files ?? []).slice(0, maxFiles);
            const blobs = [];
            const infos = [];

            for (const file of files) {
                let blob = null;
                try {
                    const bitmap = await decode(file);
                    const scale = Math.min(1, maxEdge / Math.max(bitmap.width, bitmap.height));
                    const canvas = document.createElement('canvas');
                    canvas.width = Math.round(bitmap.width * scale);
                    canvas.height = Math.round(bitmap.height * scale);
                    canvas.getContext('2d').drawImage(bitmap, 0, 0, canvas.width, canvas.height);
                    if (bitmap.close) {
                        bitmap.close();
                    }
                    blob = await new Promise(r => canvas.toBlob(r, 'image/jpeg', quality));
                } catch {
                    blob = null;
                }

                blobs.push(blob);
                infos.push(blob
                    ? { name: jpegName(file.name), size: blob.size, type: 'image/jpeg' }
                    : describe(file));
            }

            prepared.set(inputId, blobs);
            return infos;
        },

        /// Drops an input's re-encoded blobs. Called when the component goes away, so a long
        /// circuit does not accumulate a page of JPEGs per visit.
        release(inputId) {
            prepared.delete(inputId);
        },

        /// Opens an input's picker, asking the OS for the camera when useCamera is set.
        /// One input driven two ways rather than two inputs, so the upload path keeps a
        /// single file list to index. iOS honours `capture` by opening the camera directly;
        /// a browser that ignores it shows its ordinary picker, which offers the camera one
        /// tap further in — worth having either way, because the alternative is asking
        /// someone at a table to find their notes in a file dialog.
        pick(inputId, useCamera) {
            const input = document.getElementById(inputId);
            if (!input) {
                return;
            }

            if (useCamera) {
                input.setAttribute('capture', 'environment');
            } else {
                input.removeAttribute('capture');
            }

            input.click();
        },

        /// PUTs the selected file to the SAS URL. Reports progress (0-100) via
        /// dotnetRef.OnUploadProgress and resolves true/false for success.
        send(inputId, sasUrl, contentType, dotnetRef) {
            return this.sendAt(inputId, 0, sasUrl, contentType, dotnetRef);
        },

        /// PUTs the file at the given index of a (multi-file) input to the SAS URL, or the
        /// blob prepareImages re-encoded in its place.
        sendAt(inputId, index, sasUrl, contentType, dotnetRef) {
            return new Promise(resolve => {
                const input = document.getElementById(inputId);
                const file = prepared.get(inputId)?.[index] ?? input?.files?.[index];
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
