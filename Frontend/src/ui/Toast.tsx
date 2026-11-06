import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { Alert, Snackbar } from '@mui/material';

type Severity = 'success' | 'error';

interface Message {
  key: number;
  text: string;
  detail?: string;
  severity: Severity;
}

interface ToastApi {
  success: (text: string) => void;
  error: (text: string, detail?: string) => void;
}

const ToastContext = createContext<ToastApi | null>(null);

/**
 * Nothing in this app shows a change optimistically: every screen waits for the response and then reports
 * what actually happened here.
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState<Message | null>(null);

  const api = useMemo<ToastApi>(
    () => ({
      success: (text) => setMessage({ key: Date.now(), text, severity: 'success' }),
      error: (text, detail) => setMessage({ key: Date.now(), text, detail, severity: 'error' }),
    }),
    [],
  );

  return (
    <ToastContext.Provider value={api}>
      {children}
      <Snackbar
        key={message?.key}
        open={message !== null}
        autoHideDuration={message?.severity === 'error' ? 10000 : 4000}
        onClose={() => setMessage(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert
          severity={message?.severity ?? 'success'}
          variant="filled"
          onClose={() => setMessage(null)}
          sx={{ maxWidth: 560 }}
        >
          {message?.text}
          {message?.detail && (
            <div style={{ opacity: 0.85, fontSize: '0.8em', marginTop: 4, wordBreak: 'break-word' }}>
              {message.detail}
            </div>
          )}
        </Alert>
      </Snackbar>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastApi {
  const context = useContext(ToastContext);
  if (!context) throw new Error('useToast must be used inside a ToastProvider');
  return context;
}

/** Runs an API call and reports the outcome, returning whether it worked. */
export function useRun() {
  const toast = useToast();
  return useCallback(
    async (action: () => Promise<unknown>, successText: string): Promise<boolean> => {
      try {
        await action();
        toast.success(successText);
        return true;
      } catch (error) {
        const api = error as { message?: string; detail?: string };
        toast.error(api.message ?? 'Something went wrong.', api.detail);
        return false;
      }
    },
    [toast],
  );
}
