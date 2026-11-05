import { readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig, devices, type Project } from '@playwright/test';

import { archivosDePrueba } from './compilacion.mjs';

/**
 * Suite de punta a punta (NFR-03): los canales estables instalados de Chrome y Edge.
 *
 * Hay un proyecto por navegador y por archivo de tests/ (`chrome-*` y `msedge-*`, por ejemplo `chrome-acceso` o
 * `msedge-configuracion`). servidor.mjs compila la web y la Api y levanta una Api por proyecto, cada una con su base y
 * su marca en directorios temporales que se borran al terminar: así cada archivo empieza con una base nueva en cada
 * navegador.
 *
 * Necesita Chrome y Edge instalados en el sistema donde corre: en WSL (`npx playwright install --with-deps chrome
 * msedge`, en una terminal con sudo) o en Windows (clon fuera de WSL, Node 22, npm ci en web/ y e2e/). Las dos vías:
 * e2e/README.md.
 *
 * OPTICA_E2E_DIAGNOSTICO_CHROMIUM=1 agrega los proyectos `chromium-*` (el Chromium que descarga Playwright). Es solo un
 * diagnóstico para entornos sin Chrome ni Edge: no reemplaza a los proyectos `chrome-*` y `msedge-*` ni cuenta para
 * NFR-03.
 */
const puertoBase = Number(process.env.OPTICA_E2E_PUERTO ?? '5543');
if (!Number.isInteger(puertoBase) || puertoBase < 1024 || puertoBase > 65000) {
  throw new Error('OPTICA_E2E_PUERTO tiene que ser un número de puerto entre 1024 y 65000.');
}

const navegadores: Project[] = [
  { name: 'chrome', use: { ...devices['Desktop Chrome'], channel: 'chrome' } },
  {
    name: 'msedge',
    use: {
      ...devices['Desktop Edge'],
      channel: 'msedge',
      // Con un perfil nuevo, Edge inicia sesión solo (implicit sign-in) y consulta servicios de Microsoft
      // (graph.microsoft.com, substrate.office.com, edge.microsoft.com) con la cuenta del equipo; mientras tanto el
      // proceso del navegador deja de atender pedidos por 2 a 10 s y la pantalla queda en "Cargando…" (W-VER-03 de
      // FEAT-001a y FEAT-001b, D-30 de FEAT-002b). La suite solo habla con localhost: cualquier otro nombre no se
      // resuelve, así esos pedidos fallan en el acto y no sale ningún dato de la cuenta. Se agrega a los argumentos de
      // Playwright sin reemplazarlos (a diferencia de --disable-features).
      launchOptions: { args: ['--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE localhost'] },
    },
  },
];
if (process.env.OPTICA_E2E_DIAGNOSTICO_CHROMIUM === '1') {
  navegadores.push({ name: 'chromium', use: { ...devices['Desktop Chrome'] } });
}

/**
 * Cada archivo de pruebas deja su base en un estado que otro no espera (acceso.spec.ts la termina con otra contraseña
 * y bloqueada), así que cada uno tiene su propia Api y su propia base por navegador (D-05). La lista sale de tests/:
 * un archivo nuevo tiene su proyecto sin tocar nada acá, y uno con un nombre que no sirve hace fallar la configuración
 * en lugar de quedar sin correr.
 */
const directorioDePruebas = join(dirname(fileURLToPath(import.meta.url)), 'tests');
const archivos = archivosDePrueba(readdirSync(directorioDePruebas));

// Un proyecto por navegador y por archivo ("chrome-acceso", "msedge-configuracion"…), con su Api en puertos
// consecutivos. El nombre del navegador sigue siendo el prefijo del proyecto y el canal no cambia.
const proyectos: Project[] = navegadores
  .flatMap((navegador) => archivos.map((archivo) => ({ navegador, archivo })))
  .map(({ navegador, archivo }, indice) => ({
    ...navegador,
    name: `${navegador.name}-${archivo}`,
    testMatch: `${archivo}.spec.ts`,
    use: { ...navegador.use, baseURL: `https://localhost:${puertoBase + indice}` },
  }));
const instancias = proyectos.map((proyecto, indice) => `${proyecto.name}:${puertoBase + indice}`).join(',');
const ultimaUrl = `https://localhost:${puertoBase + proyectos.length - 1}`;

export default defineConfig({
  testDir: directorioDePruebas,
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
