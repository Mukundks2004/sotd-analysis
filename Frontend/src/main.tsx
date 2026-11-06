import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createTheme, ThemeProvider } from '@mui/material';
import { App } from './App';
import { ToastProvider } from './ui/Toast';
import './index.css';

const theme = createTheme({
  palette: { primary: { main: '#3949ab' }, secondary: { main: '#00897b' } },
  typography: { fontSize: 13 },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider theme={theme}>
      <ToastProvider>
        <App />
      </ToastProvider>
    </ThemeProvider>
  </StrictMode>,
);
