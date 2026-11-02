import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// En desarrollo, Vite reenvía /api a la Api (sin CORS: el navegador ve un solo origen). La Api escucha en
// https://localhost:5001 con el certificado de desarrollo de .NET; OPTICA_API_URL permite cambiarlo.
const api = process.env.OPTICA_API_URL ?? 'https://localhost:5001';

// El certificado de desarrollo de .NET no siempre es de confianza para Node, así que con la Api en esta misma máquina
// (localhost o 127.0.0.1) el proxy no verifica TLS. Con cualquier otro host sí (secure: true): el proxy lleva la
// contraseña y la cookie de sesión, y sin verificar el certificado un intermediario en la red podría leerlas.
const apiLocal = ['localhost', '127.0.0.1'].includes(new URL(api).hostname);

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: api,
        secure: !apiLocal,
      },
    },
  },
  build: {
    // Sin mapas de fuente en lo que se publica.
    sourcemap: false,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    restoreMocks: true,
    include: ['src/**/*.test.{ts,tsx}'],
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      // Fuera de la medición: las pruebas mismas; src/pruebas/**, que son ayudas de prueba (fetch falso, montaje de la
      // App) y no se publican; y src/main.tsx, el punto de entrada, que no tiene lógica propia: solo monta la App
      // con BrowserRouter en el elemento raíz (la App se prueba montada con renderizarApp). Todo lo demás sí se mide.
      exclude: ['src/**/*.test.{ts,tsx}', 'src/pruebas/**', 'src/main.tsx'],
      reporter: ['text', 'html'],
      thresholds: {
        lines: 80,
        branches: 80,
        functions: 80,
        statements: 80,
      },
    },
  },
});
