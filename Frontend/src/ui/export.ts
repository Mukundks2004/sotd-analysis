import type { Row } from '../api';
import { display, type TableDef } from '../tables';

/** Quotes a value for CSV, doubling any quotes inside it. */
function csvCell(value: string): string {
  return /[",\r\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"]/g, (character) =>
    character === '&' ? '&amp;' : character === '<' ? '&lt;' : character === '>' ? '&gt;' : '&quot;',
  );
}

function timestamp(): string {
  return new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
}

/** Writes every row given (the whole current filter and sort, not just the page on screen) to a CSV file. */
export function downloadCsv(table: TableDef, rows: Row[]): void {
  const header = table.columns.map((column) => csvCell(column.label)).join(',');
  const body = rows.map((row) =>
    table.columns.map((column) => csvCell(display(row[column.key], column.kind))).join(','),
  );
  // The BOM keeps Excel from mangling non-ASCII song titles.
  const blob = new Blob(['\uFEFF', [header, ...body].join('\r\n')], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `${table.path}-${timestamp()}.csv`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/** Opens the browser's print dialog on a plain table of every row given. */
export function printRows(table: TableDef, rows: Row[]): void {
  const head = table.columns.map((column) => `<th>${escapeHtml(column.label)}</th>`).join('');
  const body = rows
    .map(
      (row) =>
        `<tr>${table.columns
          .map((column) => `<td>${escapeHtml(display(row[column.key], column.kind))}</td>`)
          .join('')}</tr>`,
    )
    .join('');

  const html = `<!doctype html><html><head><meta charset="utf-8"><title>${escapeHtml(table.label)}</title>
<style>
body{font-family:system-ui,sans-serif;margin:24px}
h1{font-size:18px}
table{border-collapse:collapse;width:100%;font-size:11px}
th,td{border:1px solid #bbb;padding:4px 6px;text-align:left;vertical-align:top}
th{background:#eee}
tr{break-inside:avoid}
</style></head><body>
<h1>${escapeHtml(table.label)} \u2014 ${rows.length} row${rows.length === 1 ? '' : 's'}</h1>
<table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>
</body></html>`;

  // A hidden iframe prints without the pop-up blocker getting involved.
  const frame = document.createElement('iframe');
  frame.style.position = 'fixed';
  frame.style.right = '0';
  frame.style.bottom = '0';
  frame.style.width = '0';
  frame.style.height = '0';
  frame.style.border = '0';
  document.body.appendChild(frame);

  const doc = frame.contentDocument;
  if (!doc) {
    frame.remove();
    return;
  }
  doc.open();
  doc.write(html);
  doc.close();

  frame.contentWindow?.focus();
  frame.contentWindow?.print();
  // Removing it immediately would cancel the dialog in some browsers.
  setTimeout(() => frame.remove(), 60000);
}
