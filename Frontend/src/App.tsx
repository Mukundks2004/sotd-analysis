import { useEffect, useState } from 'react';
import { AppBar, Box, Container, CssBaseline, Tab, Tabs, Toolbar, Typography } from '@mui/material';
import { api } from './api';
import { CrudTab } from './features/CrudTab';
import { AnnotationTab } from './features/AnnotationTab';
import { GenreTab } from './features/GenreTab';
import { useToast } from './ui/Toast';

export function App() {
  const [tab, setTab] = useState(0);
  const [status, setStatus] = useState<string>('');
  const toast = useToast();

  // A single check up front, so a backend that is not running is obvious rather than showing empty tables.
  useEffect(() => {
    api
      .health()
      .then((health) => setStatus(`${health.songs} songs \u00b7 ${health.entries} entries`))
      .catch((error: { message?: string }) => toast.error(error.message ?? 'The API is not reachable.'));
  }, [toast]);

  return (
    <Box sx={{ minHeight: '100vh', bgcolor: 'background.default' }}>
      <CssBaseline />
      <AppBar position="static">
        <Toolbar>
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            SOTD Database
          </Typography>
          <Typography variant="body2">{status}</Typography>
        </Toolbar>
        <Tabs
          value={tab}
          onChange={(_, value) => setTab(value)}
          textColor="inherit"
          indicatorColor="secondary"
          sx={{ px: 2 }}
        >
          <Tab label="Tables" />
          <Tab label="Annotations" />
          <Tab label="Genre graph" />
        </Tabs>
      </AppBar>

      <Container maxWidth={false} sx={{ py: 3 }}>
        {/* Each tab keeps its own state while hidden so switching back does not refetch everything. */}
        <Box hidden={tab !== 0}>{tab === 0 && <CrudTab />}</Box>
        <Box hidden={tab !== 1}>{tab === 1 && <AnnotationTab />}</Box>
        <Box hidden={tab !== 2}>{tab === 2 && <GenreTab />}</Box>
      </Container>
    </Box>
  );
}
