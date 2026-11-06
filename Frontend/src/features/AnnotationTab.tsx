import { useCallback, useEffect, useRef, useState } from 'react';
import {
  Box,
  Chip,
  CircularProgress,
  ClickAwayListener,
  FormControl,
  InputLabel,
  List,
  ListItem,
  ListItemButton,
  MenuItem,
  Pagination,
  Paper,
  Popper,
  Select,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import { api, type AnnotationRow, type Tag } from '../api';
import { useToast } from '../ui/Toast';

interface Relationship {
  key: string;
  label: string;
  owner: string;
  tag: string;
  ordered: boolean;
  big: boolean;
}

export function AnnotationTab() {
  const [relationships, setRelationships] = useState<Relationship[]>([]);
  const [current, setCurrent] = useState<Relationship | null>(null);
  const [rows, setRows] = useState<AnnotationRow[]>([]);
  const [filterText, setFilterText] = useState('');
  const [filter, setFilter] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [totalPages, setTotalPages] = useState(1);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);

  const toast = useToast();

  useEffect(() => {
    api
      .relationships()
      .then((list) => {
        setRelationships(list);
        setCurrent(list[0] ?? null);
      })
      .catch((error: { message?: string }) => toast.error(error.message ?? 'Could not load relationships.'));
  }, [toast]);

  const load = useCallback(async () => {
    if (!current) return;
    setLoading(true);
    try {
      const result = await api.annotationRows(current.key, { filter, page, pageSize });
      setRows(result.items);
      setTotal(result.total);
      setTotalPages(result.totalPages);
      if (result.page !== page) setPage(result.page);
    } catch (error) {
      const failure = error as { message?: string; detail?: string };
      toast.error(failure.message ?? 'Could not load rows.', failure.detail);
      setRows([]);
      setTotal(0);
      setTotalPages(1);
    } finally {
      setLoading(false);
    }
  }, [current, filter, page, pageSize, toast]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    const timer = setTimeout(() => {
      const trimmed = filterText.trim();
      setFilter((value) => {
        if (value === trimmed) return value;
        setPage(1);
        return trimmed;
      });
    }, 300);
    return () => clearTimeout(timer);
  }, [filterText]);

  /** Replaces one row in place so adding a tag does not reload and reshuffle the whole page. */
  function replaceRow(id: string, tags: Tag[]) {
    setRows((current) => current.map((row) => (row.id === id ? { ...row, tags } : row)));
  }

  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
        <FormControl size="small" sx={{ minWidth: 240 }}>
          <InputLabel id="rel-label">Relationship</InputLabel>
          <Select
            labelId="rel-label"
            label="Relationship"
            value={current?.key ?? ''}
            onChange={(event) => {
              setCurrent(relationships.find((item) => item.key === event.target.value) ?? null);
              setPage(1);
              setFilterText('');
              setFilter('');
            }}
          >
            {relationships.map((item) => (
              <MenuItem key={item.key} value={item.key}>
                {item.label}
              </MenuItem>
            ))}
          </Select>
        </FormControl>

        {current?.big && (
          <FormControl size="small" sx={{ minWidth: 110 }}>
            <InputLabel id="rel-size-label">Rows</InputLabel>
            <Select
              labelId="rel-size-label"
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
        )}

        <Box sx={{ flexGrow: 1 }} />

        <Typography variant="body2" color="text.secondary">
          {loading ? 'Loading\u2026' : `${total} row${total === 1 ? '' : 's'}`}
        </Typography>
        <TextField
          size="small"
          label={`Find ${current?.owner.toLowerCase() ?? 'row'}`}
          value={filterText}
          onChange={(event) => setFilterText(event.target.value)}
          sx={{ minWidth: 240 }}
        />
      </Stack>

      <TableContainer component={Paper} variant="outlined">
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell sx={{ width: '28%' }}>{current?.owner ?? 'Row'}</TableCell>
              <TableCell>{current ? `${current.tag}s` : 'Tags'}</TableCell>
              <TableCell sx={{ width: 280 }}>Add</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.length === 0 && !loading && (
              <TableRow>
                <TableCell colSpan={3}>
                  <Typography variant="body2" color="text.secondary" sx={{ py: 3, textAlign: 'center' }}>
                    Nothing to show.
                  </Typography>
                </TableCell>
              </TableRow>
            )}
            {current &&
              rows.map((row) => (
                <AnnotationRowView
                  key={row.id}
                  relationship={current}
                  row={row}
                  onTags={(tags) => replaceRow(row.id, tags)}
                />
              ))}
          </TableBody>
        </Table>
      </TableContainer>

      {current?.big && (
        <Stack direction="row" sx={{ justifyContent: 'center' }}>
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
    </Stack>
  );
}

interface RowProps {
  relationship: Relationship;
  row: AnnotationRow;
  onTags: (tags: Tag[]) => void;
}

function AnnotationRowView({ relationship, row, onTags }: RowProps) {
  const [options, setOptions] = useState<Tag[]>([]);
  const [search, setSearch] = useState('');
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const loadedRef = useRef(false);
  const anchorRef = useRef<HTMLDivElement | null>(null);
  const toast = useToast();

  const loadOptions = useCallback(
    async (query: string) => {
      try {
        setOptions(await api.tagOptions(relationship.key, query));
      } catch (error) {
        toast.error((error as { message?: string }).message ?? 'Could not load options.');
      }
    },
    [relationship.key, toast],
  );

  // Clicking the box shows everything available straight away; typing narrows it from the server.
  function handleOpen() {
    setOpen(true);
    if (!loadedRef.current) {
      loadedRef.current = true;
      void loadOptions('');
    }
  }

  useEffect(() => {
    if (!open) return;
    const timer = setTimeout(() => void loadOptions(search.trim()), 250);
    return () => clearTimeout(timer);
  }, [search, open, loadOptions]);

  const typed = search.trim();
  const alreadyOn = new Set(row.tags.map((tag) => tag.id));
  const available = options.filter((option) => !alreadyOn.has(option.id));
  const exactExists = available.some((option) => option.name.toLowerCase() === typed.toLowerCase());

  // When nothing matches, the only choice offered is to create what was typed.
  const choices: (Tag | { create: string })[] =
    typed !== '' && !exactExists && available.length === 0 ? [{ create: typed }] : available;

  async function add(choice: Tag | { create: string }) {
    setBusy(true);
    try {
      const tag = 'create' in choice ? { newName: choice.create } : { tagId: choice.id };
      const added = await api.addTag(relationship.key, row.id, tag);
      onTags([...row.tags, added]);
      setSearch('');
      // A newly created tag should appear the next time the list opens.
      loadedRef.current = false;
      toast.success(`${relationship.tag} added.`);
    } catch (error) {
      const failure = error as { message?: string; detail?: string };
      toast.error(failure.message ?? 'Could not add the tag.', failure.detail);
    } finally {
      setBusy(false);
    }
  }

  async function remove(tag: Tag) {
    setBusy(true);
    try {
      await api.removeTag(relationship.key, row.id, tag.id);
      onTags(row.tags.filter((candidate) => candidate.id !== tag.id));
      toast.success(`${relationship.tag} removed.`);
    } catch (error) {
      const failure = error as { message?: string; detail?: string };
      toast.error(failure.message ?? 'Could not remove the tag.', failure.detail);
    } finally {
      setBusy(false);
    }
  }

  /** Song artists are positional, so dropping a chip renumbers the whole row 1..n on the server. */
  async function drop(toIndex: number) {
    const from = dragIndex;
    setDragIndex(null);
    if (from === null || from === toIndex) return;

    const reordered = [...row.tags];
    const [moved] = reordered.splice(from, 1);
    reordered.splice(toIndex, 0, moved);

    setBusy(true);
    try {
      const saved = await api.reorderArtists(
        row.id,
        reordered.map((tag) => tag.id),
      );
      onTags(saved);
      toast.success('Order saved.');
    } catch (error) {
      const failure = error as { message?: string; detail?: string };
      toast.error(failure.message ?? 'Could not save the order.', failure.detail);
    } finally {
      setBusy(false);
    }
  }

  return (
    <TableRow hover>
      <TableCell>
        <Typography variant="body2">{row.label}</Typography>
        {row.detail && (
          <Typography variant="caption" color="text.secondary">
            {row.detail}
          </Typography>
        )}
      </TableCell>

      <TableCell>
        <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
          {row.tags.length === 0 && (
            <Typography variant="caption" color="text.secondary">
              None yet
            </Typography>
          )}
          {row.tags.map((tag, index) => (
            <Chip
              key={tag.id}
              size="small"
              label={relationship.ordered ? `${index + 1}. ${tag.name}` : tag.name}
              onDelete={() => void remove(tag)}
              disabled={busy}
              draggable={relationship.ordered}
              onDragStart={() => setDragIndex(index)}
              onDragOver={(event) => relationship.ordered && event.preventDefault()}
              onDrop={() => void drop(index)}
              sx={{
                cursor: relationship.ordered ? 'grab' : 'default',
                opacity: dragIndex === index ? 0.4 : 1,
                // The delete cross only appears on hover, as the spec asks.
                '& .MuiChip-deleteIcon': { opacity: 0, transition: 'opacity 120ms' },
                '&:hover .MuiChip-deleteIcon': { opacity: 1 },
              }}
            />
          ))}
          {busy && <CircularProgress size={14} />}
        </Stack>
      </TableCell>

      <TableCell>
        <Box ref={anchorRef}>
          <TextField
            size="small"
            fullWidth
            placeholder={`Add ${relationship.tag.toLowerCase()}`}
            value={search}
            onFocus={handleOpen}
            onClick={handleOpen}
            onChange={(event) => setSearch(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Escape') setOpen(false);
              if (event.key === 'Enter' && choices.length > 0) void add(choices[0]);
            }}
          />
        </Box>
        <Popper
          open={open && anchorRef.current !== null}
          anchorEl={anchorRef.current}
          placement="bottom-start"
          style={{ zIndex: 1300, width: anchorRef.current?.clientWidth }}
        >
          <ClickAwayListener onClickAway={() => setOpen(false)}>
            <Paper elevation={6} sx={{ maxHeight: 260, overflowY: 'auto' }}>
              <List dense disablePadding>
                {choices.length === 0 && (
                  <ListItem>
                    <Typography variant="body2" color="text.secondary">
                      No {relationship.tag.toLowerCase()}s left to add
                    </Typography>
                  </ListItem>
                )}
                {choices.map((choice) => (
                  <ListItemButton
                    key={'create' in choice ? 'create' : choice.id}
                    // Mouse-down fires before the text box loses focus, so the click always lands.
                    onMouseDown={(event) => {
                      event.preventDefault();
                      void add(choice);
                    }}
                  >
                    {'create' in choice ? (
                      <Typography variant="body2" color="primary">
                        add &ldquo;{choice.create}&rdquo;
                      </Typography>
                    ) : (
                      <Typography variant="body2">{choice.name}</Typography>
                    )}
                  </ListItemButton>
                ))}
              </List>
            </Paper>
          </ClickAwayListener>
        </Popper>
      </TableCell>
    </TableRow>
  );
}
