import { existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { expect, test, type Page, type TestInfo } from '@playwright/test';

import { baseDeOtroMontaje, carpetasDeBackup } from '../compilacion.mjs';

/**
 * Ubicación del backup (FEAT-010: AC-01, AC-02 y AC-03) de punta a punta, contra la Api real que sirve la SPA compilada,
 * en Chrome y Edge (AC-16).
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva. Las carpetas las crea y las
 * borra servidor.mjs (D-16), una raíz por instancia con el nombre `optica-e2e-backup-<puerto>`
 * (`carpetasDeBackup` en compilacion.mjs): en la carpeta temporal, junto a los datos de la Api, la carpeta de la misma
 * unidad; en otro punto de montaje (`/dev/shm` en Linux), el destino válido y una carpeta sin permiso de escritura (555).
 * Sin un segundo punto de montaje (Windows), el caso positivo de AC-01 y la carpeta sin escritura se saltan con su motivo:
 * los cubren las pruebas de .NET. Nunca se desactiva la regla de la otra unidad con configuración.
 *
 * Las pruebas van en orden: primero los rechazos, que no graban nada y dejan el campo vacío al volver a abrir, y al final
 * la ubicación que sí se graba. Una vez grabada, el programador de la Api puede hacer una copia en esa carpeta dentro de
 * los 5 minutos siguientes; servidor.mjs detiene la Api antes de borrarla.
 *
 * Datos personales: las rutas son carpetas temporales de la suite, sin el nombre de nadie.
 */
test.describe.configure({ mode: 'serial' });

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-configuracion-backup-1';

const RUTA_UBICACION = '/api/configuracion/backup';
const BOTON_GRABAR = 'Grabar ubicación del backup';
const ETIQUETA_CARPETA = 'Carpeta del backup';

const MENSAJE_GRABADA = 'La ubicación del backup se grabó.';
/** Las acciones de `ValidacionUbicacionBackup` (Application), tal como las muestra la pantalla junto al campo. */
const MENSAJE_MISMA_UNIDAD =
  'La carpeta debe estar en otra unidad que la base de datos (un pendrive o un disco externo) o en una carpeta ' +
  'sincronizada con la nube (OneDrive, Google Drive o Dropbox).';
const MENSAJE_INEXISTENTE =
  'La carpeta no existe: no se puede escribir en ella. Revisá que el pendrive o el disco estén conectados.';
const MENSAJE_SIN_ESCRITURA = 'No se puede escribir en la carpeta elegida. Revisá sus permisos o elegí otra.';

const MOTIVO_SIN_OTRO_MONTAJE =
  'Sin un segundo punto de montaje (/dev/shm en Linux) no hay una carpeta de otra unidad: lo cubren las pruebas de ' +
  '.NET (D-16).';

let violacionesCsp: string[] = [];

test.beforeEach(({ page }) => {
  violacionesCsp = [];
  page.on('console', (mensaje) => {
    if (/Content Security Policy/i.test(mensaje.text())) {
      violacionesCsp.push(mensaje.text());
    }
  });
});

test.afterEach(() => {
  expect(violacionesCsp, 'La SPA no debe violar Content-Security-Policy').toEqual([]);
});

/** Las carpetas que servidor.mjs creó para la Api de este proyecto (la de su puerto). */
function carpetasDeEsteProyecto(testInfo: TestInfo) {
  const baseURL = testInfo.project.use.baseURL;
  if (baseURL === undefined) {
    throw new Error('El proyecto no tiene baseURL: lo arma playwright.config.ts.');
  }
  return carpetasDeBackup({
    puerto: Number(new URL(baseURL).port),
    temporal: tmpdir(),
    otroMontaje: baseDeOtroMontaje(),
  });
}

/** La carpeta existe porque la creó servidor.mjs: si no, la prueba probaría otro rechazo. */
function laCreoElHarness(ruta: string) {
  expect(existsSync(ruta), 'servidor.mjs tiene que crear las carpetas de backup de la instancia (D-16)').toBe(true);
}

const campoCarpeta = (page: Page) => page.getByLabel(ETIQUETA_CARPETA, { exact: true });
const seccion = (page: Page) => page.getByRole('region', { name: 'Backup', exact: true });
const botonGrabar = (page: Page) => page.getByRole('button', { name: BOTON_GRABAR, exact: true });
const mensajeGrabada = (page: Page) => page.getByRole('status').filter({ hasText: MENSAJE_GRABADA });

/** Entra como la operadora (en la base nueva define la contraseña) y abre la configuración desde el inicio. */
async function ingresarYAbrir(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/(definir-contrasena|ingreso)$/);
  await page.getByLabel('Contraseña', { exact: true }).fill(CONTRASENA);
  if (/\/definir-contrasena$/.test(page.url())) {
    await page.getByRole('button', { name: 'Definir contraseña' }).click();
  } else {
    await page.getByRole('button', { name: 'Ingresar' }).click();
  }
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await abrirDesdeElInicio(page);
}

async function abrirDesdeElInicio(page: Page) {
  await page.getByRole('link', { name: 'Configuración' }).click();
  await expect(page).toHaveURL(/\/configuracion$/);
  // La sección termina de cargar cuando aparece su botón.
  await expect(seccion(page).getByRole('button', { name: BOTON_GRABAR, exact: true })).toBeVisible();
}

/** Vuelve al inicio y abre de nuevo la pantalla: la SPA la monta otra vez y vuelve a consultar la Api. */
async function volverAAbrir(page: Page) {
  await page.getByRole('link', { name: 'Volver al inicio' }).click();
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await abrirDesdeElInicio(page);
}

/** Escribe la carpeta, pulsa "Grabar ubicación del backup" y espera la respuesta de la Api; devuelve el código. */
async function grabar(page: Page, ruta: string): Promise<number> {
  await campoCarpeta(page).fill(ruta);
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname === RUTA_UBICACION),
    botonGrabar(page).click(),
  ]);
  return respuesta.status();
}

/** El campo quedó marcado inválido y su mensaje, vinculado por aria-describedby, es `mensaje`; no se grabó. */
async function rechazadaJuntoAlCampo(page: Page, mensaje: string) {
  const campo = campoCarpeta(page);
  await expect(campo).toHaveAttribute('aria-invalid', 'true');
  const idMensaje = await campo.getAttribute('aria-describedby');
  expect(idMensaje, 'El campo no apunta a su mensaje').toBeTruthy();
  await expect(page.locator(`[id="${idMensaje}"]`)).toHaveText(mensaje);
  await expect(mensajeGrabada(page)).toHaveCount(0);
}

/** La ubicación que tiene grabada la Api ahora, pedida con la sesión de la página. */
async function grabada(page: Page): Promise<string | null> {
  const respuesta = await page.request.get(RUTA_UBICACION);
  expect(respuesta.status()).toBe(200);
  return (await respuesta.json()).ruta;
}

/** Nada grabado: la Api no tiene ubicación y, al volver a abrir, el campo está vacío. */
async function sinUbicacionGrabada(page: Page) {
  expect(await grabada(page)).toBeNull();
  await volverAAbrir(page);
  await expect(campoCarpeta(page)).toHaveValue('');
}

test('AC-02: una carpeta de la misma unidad que la base, fuera de la nube, no se graba', async ({ page }, testInfo) => {
  const carpetas = carpetasDeEsteProyecto(testInfo);
  laCreoElHarness(carpetas.mismaUnidad);
  await ingresarYAbrir(page);
  await expect(campoCarpeta(page)).toHaveValue('');

  expect(await grabar(page, carpetas.mismaUnidad)).toBe(400);

  await rechazadaJuntoAlCampo(page, MENSAJE_MISMA_UNIDAD);
  // Lo escrito queda para corregirlo.
  await expect(campoCarpeta(page)).toHaveValue(carpetas.mismaUnidad);
  await sinUbicacionGrabada(page);
});

test('AC-03: una carpeta inexistente no se graba', async ({ page }, testInfo) => {
  const carpetas = carpetasDeEsteProyecto(testInfo);
  expect(existsSync(carpetas.inexistente), 'La carpeta inexistente no debe existir').toBe(false);
  await ingresarYAbrir(page);

  expect(await grabar(page, carpetas.inexistente)).toBe(400);

  await rechazadaJuntoAlCampo(page, MENSAJE_INEXISTENTE);
  await sinUbicacionGrabada(page);
});

test('AC-03: una carpeta sin permiso de escritura no se graba', async ({ page }, testInfo) => {
  const carpetas = carpetasDeEsteProyecto(testInfo);
  test.skip(process.platform === 'win32', 'Los permisos 555 no existen en Windows: lo cubren las pruebas de .NET.');
  test.skip(
    process.getuid?.() === 0,
    'Como root se puede escribir en una carpeta 555: la regla no se puede probar así (lo cubren las pruebas de .NET).',
  );
  test.skip(carpetas.sinEscritura === null, MOTIVO_SIN_OTRO_MONTAJE);
  const sinEscritura = carpetas.sinEscritura as string;
  laCreoElHarness(sinEscritura);
  await ingresarYAbrir(page);

  expect(await grabar(page, sinEscritura)).toBe(400);

  await rechazadaJuntoAlCampo(page, MENSAJE_SIN_ESCRITURA);
  await sinUbicacionGrabada(page);
});

test('AC-01: una carpeta de otra unidad se graba y se muestra al volver a abrir la configuración', async ({
  page,
}, testInfo) => {
  const carpetas = carpetasDeEsteProyecto(testInfo);
  test.skip(carpetas.destino === null, MOTIVO_SIN_OTRO_MONTAJE);
  const destino = carpetas.destino as string;
  laCreoElHarness(destino);
  await ingresarYAbrir(page);

  expect(await grabar(page, destino)).toBe(200);

  await expect(mensajeGrabada(page)).toHaveText(MENSAJE_GRABADA);
  await expect(campoCarpeta(page)).toHaveAttribute('aria-invalid', 'false');
  expect(await grabada(page)).toBe(destino);
  await volverAAbrir(page);
  await expect(campoCarpeta(page)).toHaveValue(destino);
  // También después de recargar la página (otra carga de la SPA, la misma sesión).
  await page.reload();
  await expect(campoCarpeta(page)).toHaveValue(destino);
});
