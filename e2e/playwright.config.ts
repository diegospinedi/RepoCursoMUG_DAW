import { defineConfig, devices, type Project } from '@playwright/test';

/**
 * Suite de punta a punta (NFR-03): los canales estables instalados de Chrome y Edge.
 *
 * servidor.mjs compila la web y la Api y levanta una Api por proyecto, cada una con su base y su marca en directorios
 * temporales que se borran al terminar: así cada navegador empieza con una base nueva. Necesita Chrome y Edge
 * instalados en el sistema donde corre: en WSL (`npx playwright install --with-deps chrome msedge`, en una terminal
 * con sudo) o en Windows (clon fuera de WSL, Node 22, npm ci en web/ y e2e/). Las dos vías: e2e/README.md.
 *
 * OPTICA_E2E_DIAGNOSTICO_CHROMIUM=1 agrega el proyecto `chromium` (el Chromium que descarga Playwright). Es solo un
 * diagnóstico para entornos sin Chrome ni Edge: no reemplaza a los proyectos `chrome` y `msedge` ni cuenta para NFR-03.
 */
const puertoBase = Number(process.env.OPTICA_E2E_PUERTO ?? '5543');
if (!Number.isInteger(puertoBase) || puertoBase < 1024 || puertoBase > 65000) {
  throw new Error('OPTICA_E2E_PUERTO tiene que ser un número de puerto entre 1024 y 65000.');
}

const navegadores: Project[] = [
  { name: 'chrome', use: { ...devices['Desktop Chrome'], channel: 'chrome' } },
  { name: 'msedge', use: { ...devices['Desktop Edge'], channel: 'msedge' } },
];
if (process.env.OPTICA_E2E_DIAGNOSTICO_CHROMIUM === '1') {
  navegadores.push({ name: 'chromium', use: { ...devices['Desktop Chrome'] } });
}

// Una Api por proyecto, en puertos consecutivos.
const proyectos: Project[] = navegadores.map((proyecto, indice) => ({
  ...proyecto,
  use: { ...proyecto.use, baseURL: `https://localhost:${puertoBase + indice}` },
}));
const instancias = proyectos.map((proyecto, indice) => `${proyecto.name}:${puertoBase + indice}`).join(',');
const ultimaUrl = `https://localhost:${puertoBase + proyectos.length - 1}`;

export default defineConfig({
  testDir: './tests',
  // Dentro de un proyecto las pruebas dependen del estado de su base: van en orden y de a una.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: [['list']],
  use: {
    // El certificado de la Api en la prueba es el de desarrollo de .NET, que el navegador no tiene por qué conocer.
    ignoreHTTPSErrors: true,
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
  projects: proyectos,
  webServer: {
    command: 'node servidor.mjs',
    // servidor.mjs levanta las Api de a una y espera a cada una: cuando responde la última, están todas.
    url: `${ultimaUrl}/api/acceso/estado`,
    ignoreHTTPSErrors: true,
    reuseExistingServer: false,
    timeout: 300_000,
    stdout: 'pipe',
    stderr: 'pipe',
    // Con una señal (y no con SIGKILL) servidor.mjs detiene las Api y borra los directorios temporales.
    gracefulShutdown: { signal: 'SIGTERM', timeout: 20_000 },
    env: { OPTICA_E2E_INSTANCIAS: instancias },
  },
});
