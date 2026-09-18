// window.nornisEditor — TipTap WYSIWYG wrapper for the capture editor.
// Ported from Chronicis's tipTapIntegration.js, minus wiki-link / external-reference /
// map / image plumbing. Instances are keyed by element id (same shape as nornisGraph).
// The editor edits HTML; Blazor pulls it via getHtml and converts to markdown in C#.
(function () {
    'use strict';

    const editors = new Map();
    const normalizeLocks = new Set();

    // ── Markdown → HTML (initial content back-compat; Capture usually starts empty) ──

    function isHtmlContent(content) {
        if (!content || content.trim() === '') return false;
        return /<(p|h[1-6]|ul|ol|li|strong|em|s|a|pre|code|blockquote|div|span|br|table|thead|tbody|tr|th|td)[^>]*>/i.test(content);
    }

    function markdownToHtml(markdown) {
        if (!markdown) return '<p></p>';

        let html = markdown;

        // Headers
        html = html.replace(/^######\s+(.+)$/gm, '<h6>$1</h6>');
        html = html.replace(/^#####\s+(.+)$/gm, '<h5>$1</h5>');
        html = html.replace(/^####\s+(.+)$/gm, '<h4>$1</h4>');
        html = html.replace(/^###\s+(.+)$/gm, '<h3>$1</h3>');
        html = html.replace(/^##\s+(.+)$/gm, '<h2>$1</h2>');
        html = html.replace(/^#\s+(.+)$/gm, '<h1>$1</h1>');

        // Horizontal rule (own line)
        html = html.replace(/^---+\s*$/gm, '<hr>');

        // Bold / italic / strikethrough
        html = html.replace(/\*\*\*(.+?)\*\*\*/g, '<strong><em>$1</em></strong>');
        html = html.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
        html = html.replace(/\*([^\*\n]+?)\*/g, '<em>$1</em>');
        html = html.replace(/~~(.+?)~~/g, '<s>$1</s>');

        // Links [text](url)
        html = html.replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2">$1</a>');

        // Code blocks then inline code
        html = html.replace(/```([\s\S]*?)```/g, '<pre><code>$1</code></pre>');
        html = html.replace(/`([^`]+)`/g, '<code>$1</code>');

        // Bullet lists — consecutive lines starting with * or -
        html = html.replace(/^([\*\-]\s+.+\n?)+/gm, match => {
            const items = match.trim().split('\n')
                .filter(line => line.trim())
                .map(line => `<li>${line.replace(/^[\*\-]\s+/, '')}</li>`)
                .join('');
            return `<ul>${items}</ul>`;
        });

        // Ordered lists — consecutive numbered lines
        html = html.replace(/^(\d+\.\s+.+\n?)+/gm, match => {
            const items = match.trim().split('\n')
                .filter(line => line.trim())
                .map(line => `<li>${line.replace(/^\d+\.\s+/, '')}</li>`)
                .join('');
            return `<ol>${items}</ol>`;
        });

        // Paragraph breaks and line breaks
        html = html.replace(/\n\n/g, '</p><p>');
        html = html.replace(/\n/g, '<br>');

        if (!html.match(/^<(h[1-6]|ul|ol|pre|blockquote|div|hr)/)) {
            html = '<p>' + html + '</p>';
        }

        return html || '<p></p>';
    }

    function ensureHtml(content) {
        if (!content || content.trim() === '') return '<p></p>';
        return isHtmlContent(content) ? content : markdownToHtml(content);
    }

    // ── Markdown pipe-table normalization (typed or pasted "| a | b |" rows become real tables) ──

    function parseMarkdownPipeRow(text) {
        const trimmed = (text || '').trim();
        if (!trimmed.startsWith('|') || !trimmed.endsWith('|')) return [];
        return trimmed.slice(1, -1).split('|').map(cell => cell.trim());
    }

    function isMarkdownPipeRow(text) {
        return parseMarkdownPipeRow(text).length > 0;
    }

    function isMarkdownSeparatorCell(cell) {
        return /^:?-{3,}:?$/.test((cell || '').trim());
    }

    function createTableElementFromMarkdownRows(headerCells, separatorCells, bodyRows) {
        const table = document.createElement('table');
        const thead = document.createElement('thead');
        const headerRow = document.createElement('tr');

        const columnCount = Math.max(
            headerCells.length,
            separatorCells.length,
            bodyRows.reduce((max, row) => Math.max(max, row.length), 0),
            1);

        const normalizeCells = cells => {
            const normalized = cells.slice(0, columnCount);
            while (normalized.length < columnCount) normalized.push('');
            return normalized;
        };

        normalizeCells(headerCells).forEach(cell => {
            const th = document.createElement('th');
            th.textContent = cell;
            headerRow.appendChild(th);
        });
        thead.appendChild(headerRow);
        table.appendChild(thead);

        const tbody = document.createElement('tbody');
        bodyRows.forEach(rowCells => {
            const tr = document.createElement('tr');
            normalizeCells(rowCells).forEach(cell => {
                const td = document.createElement('td');
                td.textContent = cell;
                tr.appendChild(td);
            });
            tbody.appendChild(tr);
        });
        table.appendChild(tbody);

        return table;
    }

    function extractLinesFromParagraph(paragraph) {
        return paragraph.innerHTML.split(/<br\s*\/?>/i)
            .map(part => {
                const temp = document.createElement('div');
                temp.innerHTML = part;
                return (temp.textContent || '').trim();
            })
            .filter(line => line.length > 0);
    }

    function convertMarkdownPipeTablesInHtml(html) {
        if (!html || html.indexOf('|') === -1) return html;

        const container = document.createElement('div');
        container.innerHTML = html;
        let changed = false;

        let i = 0;
        while (i < container.children.length) {
            const current = container.children[i];
            if (!current || current.tagName !== 'P') {
                i += 1;
                continue;
            }

            // Scenario A: one paragraph holding <br>-separated markdown table lines.
            if (current.innerHTML.toLowerCase().includes('<br')) {
                const lines = extractLinesFromParagraph(current);
                if (lines.length >= 2 && isMarkdownPipeRow(lines[0]) && isMarkdownPipeRow(lines[1])) {
                    const headerCells = parseMarkdownPipeRow(lines[0]);
                    const separatorCells = parseMarkdownPipeRow(lines[1]);
                    if (separatorCells.length > 0 && separatorCells.every(isMarkdownSeparatorCell)) {
                        const bodyRows = lines.slice(2).filter(isMarkdownPipeRow).map(parseMarkdownPipeRow);
                        const table = createTableElementFromMarkdownRows(headerCells, separatorCells, bodyRows);
                        current.before(table);
                        current.remove();
                        changed = true;
                        i += 1;
                        continue;
                    }
                }
            }

            // Scenario B: consecutive paragraphs, each a markdown table row.
            const rowNodes = [];
            let j = i;
            while (j < container.children.length) {
                const candidate = container.children[j];
                if (!candidate || candidate.tagName !== 'P') break;
                if (!isMarkdownPipeRow((candidate.textContent || '').trim())) break;
                rowNodes.push(candidate);
                j += 1;
            }

            if (rowNodes.length >= 2) {
                const headerCells = parseMarkdownPipeRow(rowNodes[0].textContent || '');
                const separatorCells = parseMarkdownPipeRow(rowNodes[1].textContent || '');
                if (separatorCells.length > 0 && separatorCells.every(isMarkdownSeparatorCell)) {
                    const bodyRows = rowNodes.slice(2)
                        .map(node => parseMarkdownPipeRow(node.textContent || ''))
                        .filter(row => row.length > 0);
                    const table = createTableElementFromMarkdownRows(headerCells, separatorCells, bodyRows);
                    rowNodes[0].before(table);
                    rowNodes.forEach(node => node.remove());
                    changed = true;
                    i += 1;
                    continue;
                }
            }

            i += 1;
        }

        return changed ? container.innerHTML : html;
    }

    function normalizeMarkdownTables(elementId, editor) {
        if (normalizeLocks.has(elementId)) return;

        const html = editor.getHTML();
        const normalized = convertMarkdownPipeTablesInHtml(html);
        if (normalized === html) return;

        normalizeLocks.add(elementId);
        try {
            editor.commands.setContent(normalized, false);
        } finally {
            normalizeLocks.delete(elementId);
        }
    }

    // ── Toolbar command dispatch ──

    const commands = {
        bold: c => c.toggleBold(),
        italic: c => c.toggleItalic(),
        strike: c => c.toggleStrike(),
        h1: c => c.toggleHeading({ level: 1 }),
        h2: c => c.toggleHeading({ level: 2 }),
        h3: c => c.toggleHeading({ level: 3 }),
        bulletList: c => c.toggleBulletList(),
        orderedList: c => c.toggleOrderedList(),
        blockquote: c => c.toggleBlockquote(),
        codeBlock: c => c.toggleCodeBlock(),
        hr: c => c.setHorizontalRule(),
        insertTable: c => c.insertTable({ rows: 3, cols: 3, withHeaderRow: true }),
        addRowAfter: c => c.addRowAfter(),
        addColumnAfter: c => c.addColumnAfter(),
        deleteRow: c => c.deleteRow(),
        deleteColumn: c => c.deleteColumn(),
        toggleHeaderRow: c => c.toggleHeaderRow(),
        deleteTable: c => c.deleteTable(),
    };

    function updateEmptyState(container, editor) {
        container.classList.toggle('nornis-editor-empty', editor.isEmpty);
    }

    // ── Backup of unsaved notes ──
    //
    // Kept in this browser's localStorage, written from here rather than from Blazor, so it
    // survives the one thing it exists for: the page going away — a mis-click on the sidebar, a
    // dropped circuit, a closed tab — before Save was pressed. Nothing here crosses the SignalR
    // connection. One entry per key; the key is the page's business (Capture uses one per world).

    const BACKUP_DEBOUNCE_MS = 600;
    // Well under any browser's per-origin quota, and past anything a person types in a
    // sitting. A pasted transcript beyond it is not backed up, and the console says so once.
    const BACKUP_MAX_CHARS = 1500000;
    const backups = new Map(); // elementId -> { key, timer }

    function readBackup(key) {
        try {
            const raw = window.localStorage.getItem(key);
            if (!raw) return null;
            const parsed = JSON.parse(raw);
            return parsed && typeof parsed.html === 'string' && parsed.html.trim() ? parsed : null;
        } catch {
            return null;
        }
    }

    function writeBackup(key, editor) {
        try {
            if (editor.isEmpty) {
                window.localStorage.removeItem(key);
                return;
            }
            const html = editor.getHTML();
            if (html.length > BACKUP_MAX_CHARS) {
                console.warn('nornisEditor: notes too large to back up locally', key, html.length);
                return;
            }
            window.localStorage.setItem(key, JSON.stringify({ html, savedAt: new Date().toISOString() }));
        } catch (e) {
            // Quota, private mode, or storage switched off: the editor keeps working, the
            // safety net does not. Said once per write attempt, not swallowed.
            console.warn('nornisEditor: could not back up notes', key, e);
        }
    }

    function scheduleBackup(elementId, editor) {
        const entry = backups.get(elementId);
        if (!entry) return;
        if (entry.timer) clearTimeout(entry.timer);
        entry.timer = setTimeout(() => {
            entry.timer = null;
            writeBackup(entry.key, editor);
        }, BACKUP_DEBOUNCE_MS);
    }

    // The last few hundred milliseconds of typing are still in the debounce when the page is
    // left; destroy() and pagehide both land them before the editor goes.
    function flushBackup(elementId) {
        const entry = backups.get(elementId);
        const editor = editors.get(elementId);
        if (!entry || !editor || !entry.timer) return;
        clearTimeout(entry.timer);
        entry.timer = null;
        writeBackup(entry.key, editor);
    }

    window.addEventListener('pagehide', () => {
        for (const elementId of backups.keys()) flushBackup(elementId);
    });

    // ── Public API ──

    window.nornisEditor = {
        // Returns the ISO time the restored notes were kept at when a backup was loaded, and
        // null otherwise — the page shows a banner from it. A backup is loaded in place of empty
        // initial content, or in place of any content when preferBackup is set: an edit page's
        // initial content is the stored body, and the kept copy is by definition newer edits on
        // top of it.
        init(elementId, initialContent, placeholder, editable = true, backupKey = null, preferBackup = false) {
            const container = document.getElementById(elementId);
            if (!container || !window.TipTap) {
                console.error('nornisEditor.init: missing container or TipTap bundle', elementId);
                return null;
            }

            this.destroy(elementId);

            if (placeholder && editable) {
                container.dataset.placeholder = placeholder;
            }

            let restoredAt = null;
            let startingContent = initialContent;
            if (backupKey && editable) {
                backups.set(elementId, { key: backupKey, timer: null });
                if (preferBackup || !initialContent || !initialContent.trim()) {
                    const kept = readBackup(backupKey);
                    if (kept) {
                        startingContent = kept.html;
                        restoredAt = kept.savedAt || null;
                    }
                }
            }

            const content = convertMarkdownPipeTablesInHtml(ensureHtml(startingContent));
            const editor = new window.TipTap.Editor({
                element: container,
                extensions: [
                    window.TipTap.StarterKit.configure({ heading: { levels: [1, 2, 3, 4, 5, 6] } }),
                    window.TipTap.Table.configure({ resizable: editable }),
                    window.TipTap.TableRow,
                    window.TipTap.TableHeader,
                    window.TipTap.TableCell,
                ],
                content: content,
                editable: editable,
                onCreate: ({ editor }) => { if (editable) updateEmptyState(container, editor); },
                onUpdate: ({ editor }) => {
                    normalizeMarkdownTables(elementId, editor);
                    if (editable) updateEmptyState(container, editor);
                    scheduleBackup(elementId, editor);
                },
            });

            // Alt+Left and Alt+Right are the browser's Back and Forward, one slip away from the
            // word-jump keys, and Back from the middle of a page of notes is how notes get lost.
            // Swallowed only while the text has focus; everywhere else the browser keeps them.
            if (editable) {
                container.addEventListener('keydown', e => {
                    if (e.altKey && !e.ctrlKey && !e.metaKey && (e.key === 'ArrowLeft' || e.key === 'ArrowRight')) {
                        e.preventDefault();
                    }
                });
            }

            editors.set(elementId, editor);
            return restoredAt;
        },

        // When notes were kept under a key, as an ISO time, or null. Lets a page say "you have
        // unsaved changes here" before any editor is on screen.
        peekBackup(backupKey) {
            const kept = readBackup(backupKey);
            return kept ? (kept.savedAt || null) : null;
        },

        // Forgets the kept notes for a key and cancels any write on its way — called when the
        // notes have been saved for real, or when the person chooses to start fresh.
        clearBackup(backupKey) {
            for (const entry of backups.values()) {
                if (entry.key === backupKey && entry.timer) {
                    clearTimeout(entry.timer);
                    entry.timer = null;
                }
            }
            try {
                window.localStorage.removeItem(backupKey);
            } catch {
                // Nothing to remove, or no storage to remove it from.
            }
        },

        getHtml(elementId) {
            const editor = editors.get(elementId);
            return editor ? editor.getHTML() : '';
        },

        setContent(elementId, markdown) {
            const editor = editors.get(elementId);
            if (editor) {
                editor.commands.setContent(convertMarkdownPipeTablesInHtml(ensureHtml(markdown)));
                // The replace emits an update, which schedules a backup write; cancel it. Every
                // caller has just cleared the kept copy and is putting canonical content back —
                // writing that as a "kept" copy would announce unsaved changes that are nothing
                // of the kind. The placeholder state is brought in line by hand for the same
                // reason: the update may or may not have fired, depending on the TipTap build.
                const entry = backups.get(elementId);
                if (entry && entry.timer) {
                    clearTimeout(entry.timer);
                    entry.timer = null;
                }
                const container = document.getElementById(elementId);
                if (container && editor.isEditable) updateEmptyState(container, editor);
            }
        },

        exec(elementId, command) {
            const editor = editors.get(elementId);
            const action = commands[command];
            if (editor && action) {
                action(editor.chain().focus()).run();
            }
        },

        // Puts the caret back where it was. Toggling the editor's maximised state re-renders
        // the wrapper, and a click on the toolbar button had already taken focus anyway.
        focus(elementId) {
            const editor = editors.get(elementId);
            if (editor) {
                editor.commands.focus();
            }
        },

        destroy(elementId) {
            flushBackup(elementId);
            backups.delete(elementId);
            const editor = editors.get(elementId);
            if (editor) {
                editor.destroy();
                editors.delete(elementId);
            }
        },
    };
})();
