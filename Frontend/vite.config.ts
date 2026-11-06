import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API and the downloaded files it serves both live on the backend, so proxy them in development and
// keep every fetch in the app a same-origin relative path.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5280',
      '/files': 'http://localhost:5280',
    },
  },
})
