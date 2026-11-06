import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Box,
  Button,
  Checkbox,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControl,
  IconButton,
  InputLabel,
  MenuItem,
  Pagination,
  Paper,
  Select,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TableSortLabel,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import SaveIcon from '@mui/icons-material/Save';
import PrintIcon from '@mui/icons-material/Print';
import RefreshIcon from '@mui/icons-material/Refresh';
import TableViewIcon from '@mui/icons-material/TableView';
import { api, type Row } from '../api';
import { TABLES, display, parseInput, toWrite, type ColumnDef, type TableDef } from '../tables';
import { useRun, useToast } from '../ui/Toast';
import { downloadCsv, printRows } from '../ui/export';

/** Cells the user has typed into but not yet confirmed with the update button, keyed by row then column. */
type Edits = Record<string, Record<string, string>>;

export function CrudTab() {
  const [table, setTable] = useState<TableDef>(TABLES[0]);
  const [orderBy, setOrderBy] = useState(table.defaultOrderBy);
  const [desc, setDesc] = useState(table.defaultDesc);
  const [filterText, setFilterText] = useState('');
  const [filter, setFilter] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  const [rows, setRows] = useState<Row[]>([]);
  const [total, setTotal] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(false);

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [edits, setEdits] = useState<Edits>({});
  const [editingCell, setEditingCell] = useState<{ id: string; key: string } | null>(null);
  const [confirmUpdate, setConfirmUpdate] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [exporting, setExporting] = useState(false);

  // The create form keeps what was typed when it is closed or fails, separately per table.
  const draftsRef = useRef<Record<string, Record<string, string>>>({});

  const toast = useToast();
  const run = useRun();

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await api.list(table.path, { orderBy, desc, filter, page, pageSize });
      setRows(result.items);
      setTotal(result.total);
      setTotalPages(result.totalPages);
      // The server clamps a page past the end, so follow it rather than showing an empty table.
      if (result.page !== page) setPage(result.page);
      setSelected(new Set());
      setEdits({});
      // A reload replaces the rows underneath, so no cell should be left mid-edit against stale data.
      setEditingCell(null);
    } catch (error) {
      const failure = error as { message?: string; detail?: string };
      toast.error(failure.message ?? 'Could not load rows.', failure.detail);
      setRows([]);
      setTotal(0);
      setTotalPages(1);
    } finally {
      setLoading(false);
    }
  }, [table.path, orderBy, desc, filter, page, pageSize, toast]);

  useEffect(() => {
    void load();
  }, [load]);

  // Typing in the filter should not fire a request per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => {
      const trimmed = filterText.trim();
      setFilter((current) => {
        if (current === trimmed) return current;
        setPage(1);
        return trimmed;
      });
    }, 300);
    return () => clearTimeout(timer);
  }, [filterText]);

  function chooseTable(path: string) {
    const next = TABLES.find((candidate) => candidate.path === path);
    if (!next) return;
    setTable(next);
    setOrderBy(next.defaultOrderBy);
    setDesc(next.defaultDesc);
    setFilterText('');
    setFilter('');
    setPage(1);
    setSelected(new Set());
    setEdits({});
    setEditingCell(null);
  }

  function sortBy(key: string) {
    if (orderBy === key) {
      setDesc((value) => !value);
    } else {
      setOrderBy(key);
      setDesc(false);
    }
    setPage(1);
  }

  function setCell(rowId: string, column: ColumnDef, text: string, original: unknown) {
    setEdits((current) => {
      const forRow = { ...(current[rowId] ?? {}) };
      // Typing the original value back is not a change, so stop tracking it.
      if (text === display(original, column.kind)) delete forRow[column.key];
      else forRow[column.key] = text;

      const next = { ...current };
      if (Object.keys(forRow).length === 0) delete next[rowId];
      else next[rowId] = forRow;
      return next;
    });
  }

  const changedIds = Object.keys(edits);

  async function applyUpdates() {
    setBusy(true);
    const failures: string[] = [];
    let saved = 0;

    for (const rowId of changedIds) {
      const row = rows.find((candidate) => candidate.id === rowId);
      if (!row) continue;
      const merged: Record<string, unknown> = { ...row };
      let bad = false;
      for (const [key, text] of Object.entries(edits[rowId])) {
        const column = table.columns.find((candidate) => candidate.key === key);
        if (!column) continue;
        try {
          merged[key] = parseInput(text, column);
        } catch {
          failures.push(`"${text}" is not a valid ${column.label}.`);
          bad = true;
        }
      }
      if (bad) continue;

      try {
        await api.update(table.path, rowId, toWrite(table, merged));
        saved++;
      } catch (error) {
        const failure = error as { message?: string; detail?: string };
        failures.push(failure.message ?? 'Update failed.');
      }
    }

    setBusy(false);
    setConfirmUpdate(false);
    if (failures.length === 0) toast.success(saved === 1 ? `${table.label} updated.` : `${saved} rows updated.`);
    else toast.error(failures[0], failures.length > 1 ? `${failures.length - 1} more row(s) also failed.` : undefined);
    await load();
  }

  async function deleteSelected() {
    const ids = [...selected];
    setBusy(true);
    const ok = await run(
      () => api.remove(table.path, ids),
      ids.length === 1 ? `${table.label} deleted.` : `${ids.length} rows deleted.`,
    );
    setBusy(false);
    setConfirmDelete(false);
    if (ok) await load();
  }

  async function exportRows(mode: 'csv' | 'print') {
    setExporting(true);
    try {
      // Everything matching the current filter and sort, not just the page on screen.
      const all = await api.listAll(table.path, { orderBy, desc, filter });
      if (mode === 'csv') downloadCsv(table, all);
      else printRows(table, all);
    } catch (error) {
      const failure = error as { message?: string; detail?: string };
      toast.error(failure.message ?? 'Could not fetch the rows to export.', failure.detail);
    } finally {
      setExporting(false);
    }
  }

  const allOnPageSelected = rows.length > 0 && rows.every((row) => selected.has(row.id));
  const someOnPageSelected = rows.some((row) => selected.has(row.id));

  function toggleAll() {
    setSelected((current) => {
      const next = new Set(current);
      if (allOnPageSelected) rows.forEach((row) => next.delete(row.id));
      else rows.forEach((row) => next.add(row.id));
      return next;
    });
  }

  const rangeLabel = useMemo(() => {
    if (total === 0) return 'No rows';
    if (!table.big) return `${total} row${total === 1 ? '' : 's'}`;
    const first = (page - 1) * pageSize + 1;
    return `${first}\u2013${Math.min(first + rows.length - 1, total)} of ${total}`;
  }, [total, table.big, page, pageSize, rows.length]);

  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'flex-end' }}>
        <FormControl size="small" sx={{ minWidth: 240 }}>
          <InputLabel id="crud-table-label">Table</InputLabel>
          <Select
            labelId="crud-table-label"
            label="Table"
            value={table.path}
            onChange={(event) => chooseTable(event.target.value)}
          >
            {TABLES.map((candidate) => (
              <MenuItem key={candidate.path} value={candidate.path}>
                {candidate.label}
              </MenuItem>
            ))}
          </Select>
        </FormControl>

        <Box sx={{ flexGrow: 1 }} />

        <Stack direction="row" spacing={1}>
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreateOpen(true)}>
            Create
          </Button>
          <Button
            variant="contained"
            color="secondary"
            startIcon={<SaveIcon />}
            disabled={changedIds.length === 0}
            onClick={() => setConfirmUpdate(true)}
          >
            Update{changedIds.length > 0 ? ` (${changedIds.length})` : ''}
          </Button>
          <Button
            variant="contained"
            color="error"
            startIcon={<DeleteIcon />}
            disabled={selected.size === 0}
            onClick={() => setConfirmDelete(true)}
          >
            Delete{selected.size > 0 ? ` (${selected.size})` : ''}
          </Button>
        </Stack>
      </Stack>

      <Stack direction="row" sx={{ justifyContent: 'flex-end' }}>
        <Typography variant="h6">{table.label}</Typography>
      </Stack>

      <Paper variant="outlined">
        <Stack direction="row" spacing={1} useFlexGap sx={{ p: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
          {table.big ? (
            <FormControl size="small" sx={{ minWidth: 110 }}>
              <InputLabel id="crud-size-label">Rows</InputLabel>
              <Select
                labelId="crud-size-label"
                label="Rows"
                value={pageSize}
                onChange={(event) => {
                  setPageSize(Number(event.target.value));
                  setPage(1);
                }}
              >
                {[10, 20, 50].map((size) => (
                  <MenuItem key={size} value={size}>
                    {size}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>
          ) : (
            <Typography variant="body2" color="text.secondary">
              All rows
            </Typography>
          )}

          <Box sx={{ flexGrow: 1 }} />

          <Typography variant="body2" color="text.secondary">
            {loading ? 'Loading\u2026' : rangeLabel}
          </Typography>
          <TextField
            size="small"
            label="Filter"
            value={filterText}
            onChange={(event) => setFilterText(event.target.value)}
            sx={{ minWidth: 240 }}
          />
          <Tooltip title="Download every matching row as CSV">
            <span>
              <IconButton onClick={() => void exportRows('csv')} disabled={exporting || total === 0}>
                <TableViewIcon />
              </IconButton>
            </span>
          </Tooltip>
          <Tooltip title="Print every matching row">
            <span>
              <IconButton onClick={() => void exportRows('print')} disabled={exporting || total === 0}>
                <PrintIcon />
              </IconButton>
            </span>
          </Tooltip>
          <Tooltip title="Reload">
            <span>
              <IconButton onClick={() => void load()} disabled={loading}>
                <RefreshIcon />
              </IconButton>
            </span>
          </Tooltip>
        </Stack>

        <TableContainer sx={{ maxHeight: '60vh' }}>
          <Table size="small" stickyHeader>
            <TableHead>
              <TableRow>
                <TableCell padding="checkbox">
                  <Checkbox
                    checked={allOnPageSelected}
                    indeterminate={!allOnPageSelected && someOnPageSelected}
                    onChange={toggleAll}
                    disabled={rows.length === 0}
                  />
                </TableCell>
                {table.columns.map((column) => (
                  <TableCell
                    key={column.key}
                    sortDirection={orderBy === column.key ? (desc ? 'desc' : 'asc') : false}
                  >
                    <TableSortLabel
                      active={orderBy === column.key}
                      direction={orderBy === column.key && desc ? 'desc' : 'asc'}
                      onClick={() => sortBy(column.key)}
                    >
                      {column.label}
                    </TableSortLabel>
                  </TableCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.length === 0 && !loading && (
                <TableRow>
                  <TableCell colSpan={table.columns.length + 1}>
                    <Typography variant="body2" color="text.secondary" sx={{ py: 3, textAlign: 'center' }}>
                      Nothing to show.
                    </Typography>
                  </TableCell>
                </TableRow>
              )}
              {rows.map((row) => (
                <TableRow key={row.id} hover selected={selected.has(row.id)}>
                  <TableCell padding="checkbox">
                    <Checkbox
                      checked={selected.has(row.id)}
                      onChange={() =>
                        setSelected((current) => {
                          const next = new Set(current);
                          if (next.has(row.id)) next.delete(row.id);
                          else next.add(row.id);
                          return next;
                        })
                      }
                    />
                  </TableCell>
                  {table.columns.map((column) => (
                    <Cell
                      key={column.key}
                      row={row}
                      column={column}
                      edited={edits[row.id]?.[column.key]}
                      editing={editingCell?.id === row.id && editingCell.key === column.key}
                      onStart={() => column.editable && setEditingCell({ id: row.id, key: column.key })}
                      onStop={() => setEditingCell(null)}
                      onChange={(text) => setCell(row.id, column, text, row[column.key])}
                    />
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>

        {table.big && (
          <Stack direction="row" sx={{ p: 1.5, justifyContent: 'center' }}>
            <Pagination
              count={totalPages}
              page={page}
              onChange={(_, value) => setPage(value)}
              disabled={loading}
              showFirstButton
              showLastButton
            />
          </Stack>
        )}
      </Paper>

      <Dialog open={confirmUpdate} onClose={() => setConfirmUpdate(false)}>
        <DialogTitle>Save {changedIds.length === 1 ? 'this change' : `${changedIds.length} changed rows`}?</DialogTitle>
        <DialogContent>
          <DialogContentText component="div">
            {changedIds.map((rowId) => (
              <Box key={rowId} sx={{ mb: 1 }}>
                <code>{rowId.slice(0, 8)}</code>
                {Object.entries(edits[rowId]).map(([key, text]) => (
                  <div key={key}>
                    {table.columns.find((column) => column.key === key)?.label}: <b>{text || '(empty)'}</b>
                  </div>
                ))}
              </Box>
            ))}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmUpdate(false)}>Cancel</Button>
          <Button variant="contained" disabled={busy} onClick={() => void applyUpdates()}>
            {busy ? <CircularProgress size={20} /> : 'Save'}
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={confirmDelete} onClose={() => setConfirmDelete(false)}>
        <DialogTitle>Delete {selected.size === 1 ? 'this row' : `${selected.size} rows`}?</DialogTitle>
        <DialogContent>
          <DialogContentText>
            This cannot be undone. If any selected row is still referenced elsewhere, nothing is deleted.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmDelete(false)}>Cancel</Button>
          <Button color="error" variant="contained" disabled={busy} onClick={() => void deleteSelected()}>
            {busy ? <CircularProgress size={20} /> : 'Delete'}
          </Button>
        </DialogActions>
      </Dialog>

      <CreateDialog
        open={createOpen}
        table={table}
        drafts={draftsRef.current}
        onClose={() => setCreateOpen(false)}
        onCreated={() => {
          setCreateOpen(false);
          void load();
        }}
      />
    </Stack>
  );
}

interface CellProps {
  row: Row;
  column: ColumnDef;
  edited: string | undefined;
  editing: boolean;
  onStart: () => void;
  onStop: () => void;
  onChange: (text: string) => void;
}

function Cell({ row, column, edited, editing, onStart, onStop, onChange }: CellProps) {
  const original = display(row[column.key], column.kind);
  const shown = edited ?? original;

  if (editing) {
    return (
      <TableCell sx={{ p: 0.5 }}>
        <TextField
          autoFocus
          fullWidth
          size="small"
          variant="standard"
          value={shown}
          onChange={(event) => onChange(event.target.value)}
          onBlur={onStop}
          onKeyDown={(event) => {
            if (event.key === 'Enter') onStop();
            if (event.key === 'Escape') {
              onChange(original);
              onStop();
            }
          }}
        />
      </TableCell>
    );
  }

  if (column.kind === 'image') {
    return (
      <TableCell onClick={onStart} sx={{ cursor: 'text' }}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          {shown ? (
            <img
              src={`/files/${shown.replace(/^[\\/]+/, '').replace(/\\/g, '/')}`}
              alt=""
              width={28}
              height={28}
              style={{ borderRadius: '50%', objectFit: 'cover' }}
              onError={(event) => {
                event.currentTarget.style.visibility = 'hidden';
              }}
            />
          ) : null}
          <Typography variant="body2" noWrap sx={{ maxWidth: 200 }}>
            {shown}
          </Typography>
        </Stack>
      </TableCell>
    );
  }

  return (
    <TableCell
      onClick={onStart}
      title={column.editable ? 'Click to edit' : 'Primary keys cannot be changed'}
      sx={{
        cursor: column.editable ? 'text' : 'not-allowed',
        color: column.editable ? 'text.primary' : 'text.disabled',
        bgcolor: edited !== undefined ? 'warning.light' : undefined,
        maxWidth: 320,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
      }}
    >
      {shown}
    </TableCell>
  );
}

interface CreateDialogProps {
  open: boolean;
  table: TableDef;
  drafts: Record<string, Record<string, string>>;
  onClose: () => void;
  onCreated: () => void;
}

function CreateDialog({ open, table, drafts, onClose, onCreated }: CreateDialogProps) {
  const fields = useMemo(() => table.columns.filter((column) => column.editable), [table]);
  const [values, setValues] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const toast = useToast();

  // Whatever was typed survives a failure and a close, so nothing has to be retyped.
  useEffect(() => {
    if (open) {
      setValues(drafts[table.path] ?? {});
      setError(null);
    }
  }, [open, table.path, drafts]);

  function set(key: string, value: string) {
    setValues((current) => {
      const next = { ...current, [key]: value };
      drafts[table.path] = next;
      return next;
    });
  }

  async function submit() {
    const body: Record<string, unknown> = {};
    for (const field of fields) {
      try {
        body[field.key] = parseInput(values[field.key] ?? '', field);
      } catch {
        setError(`"${values[field.key]}" is not a valid ${field.label}.`);
        return;
      }
    }

    setBusy(true);
    try {
      await api.create(table.path, body);
      delete drafts[table.path];
      setValues({});
      setError(null);
      toast.success(`${table.label} created.`);
      onCreated();
    } catch (caught) {
      // The form stays open with everything still filled in so the error can be corrected.
      const failure = caught as { message?: string; detail?: string };
      setError(failure.message ?? 'Create failed.');
      toast.error(failure.message ?? 'Create failed.', failure.detail);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>New {table.label}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && (
            <Typography variant="body2" color="error">
              {error}
            </Typography>
          )}
          {fields.map((field) => (
            <TextField
              key={field.key}
              size="small"
              label={field.label + (field.nullable ? ' (optional)' : '')}
              value={values[field.key] ?? ''}
              onChange={(event) => set(field.key, event.target.value)}
              helperText={hintFor(field)}
            />
          ))}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Close</Button>
        <Button variant="contained" disabled={busy} onClick={() => void submit()}>
          {busy ? <CircularProgress size={20} /> : 'Create'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function hintFor(column: ColumnDef): string | undefined {
  switch (column.kind) {
    case 'guid':
      return 'ID of the related row';
    case 'snowflake':
      return 'Discord ID';
    case 'bool':
      return 'yes or no';
    case 'datetime':
      return 'e.g. 2024-05-01 18:30';
    case 'int':
      return 'whole number';
    default:
      return undefined;
  }
}
