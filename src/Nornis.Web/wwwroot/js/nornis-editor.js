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

    // ── Public API ──

    window.nornisEditor = {
        init(elementId, initialMarkdown, placeholder) {
            const container = document.getElementById(elementId);
            if (!container || !window.TipTap) {
                console.error('nornisEditor.init: missing container or TipTap bundle', elementId);
                return;
            }

            this.destroy(elementId);

            if (placeholder) {
                container.dataset.placeholder = placeholder;
            }

            const content = convertMarkdownPipeTablesInHtml(ensureHtml(initialMarkdown));
            const editor = new window.TipTap.Editor({
                element: container,
                extensions: [
                    window.TipTap.StarterKit.configure({ heading: { levels: [1, 2, 3, 4, 5, 6] } }),
                    window.TipTap.Table.configure({ resizable: true }),
                    window.TipTap.TableRow,
                    window.TipTap.TableHeader,
                    window.TipTap.TableCell,
                ],
                content: content,
                editable: true,
                onCreate: ({ editor }) => updateEmptyState(container, editor),
                onUpdate: ({ editor }) => {
                    normalizeMarkdownTables(elementId, editor);
                    updateEmptyState(container, editor);
                },
            });

            editors.set(elementId, editor);
        },

        getHtml(elementId) {
            const editor = editors.get(elementId);
            return editor ? editor.getHTML() : '';
        },

        setContent(elementId, markdown) {
            const editor = editors.get(elementId);
            if (editor) {
                editor.commands.setContent(convertMarkdownPipeTablesInHtml(ensureHtml(markdown)));
            }
        },

        exec(elementId, command) {
            const editor = editors.get(elementId);
            const action = commands[command];
            if (editor && action) {
                action(editor.chain().focus()).run();
            }
        },

        destroy(elementId) {
            const editor = editors.get(elementId);
            if (editor) {
                editor.destroy();
                editors.delete(elementId);
            }
        },
    };
})();
