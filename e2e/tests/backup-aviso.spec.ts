import { expect, test, type Page } from '@playwright/test';

/**
 * Aviso de backup al ingresar y ninguna forma de restaurar en pantalla (FEAT-010: AC-10 y AC-15), de punta a punta,
 * contra la Api real que sirve la SPA compilada, en Chrome y Edge (AC-16).
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva: sin ningún backup y sin
 * ubicación elegida (ninguna prueba la elige), así que el programador de la Api no copia nada y falta el backup de ayer.
 * El aviso usa `role="status"`, sin enlaces ni la palabra "configuración" (D-13), y distingue "No hay una ubicación de
 * backup elegida." (D-26). AC-09 y AC-18 (fechas del historial) y AC-17 (nube) se prueban en .NET y en la web (D-16).
 *
 * AC-15 en pantalla: la restauración es solo el comando de consola `restaurar-backup` con la Api detenida (FR-14), así
 * que ninguna pantalla con sesión (el inicio y las del menú) tiene un texto, un botón o un enlace de restaurar, ni un
 * control que cargue o importe una copia, ni un campo de archivo. La lista de pantallas sale de los enlaces del inicio
 * y se compara con la esperada: una pantalla nueva en el menú hace fallar la prueba en lugar de quedar sin revisar.
 * También se recorre "Artículos → Actualizar costos con planilla" (FEAT-011, D-27), que no está en el menú: ahí se
 * admite un único campo de archivo, `accept=".xlsx"` con la etiqueta "Planilla de costos (.xlsx)", porque carga una
 * planilla de costos y no restaura backups; los textos y controles de restaurar se revisan igual. Las pantallas de
 * detalle (un presupuesto, una factura, editar un artículo) no se recorren porque esta base está vacía; las cubren las
 * pruebas de la Api (`ProteccionTests`), que recorren también todas las operaciones, y una búsqueda de texto en el
 * código de la web (que no es una prueba automática).
 */
test.describe.configure({ mode: 'serial' });

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-backup-aviso-1';

const AVISO_SIN_BACKUPS =
  'Falta el backup de ayer. No hay ningún backup disponible. No hay una ubicación de backup elegida.';

/**
 * Cualquier forma de ofrecer restaurar la base, en castellano o en inglés. "Restablecer" también: la app no tiene una
 * pantalla de restablecer la contraseña (es un comando de consola), así que no hay excepción que hacer.
 */
const RESTAURAR = /restaur|restore|restablec/i;

/** Un control que combine el backup o una copia con cargarla, importarla, subirla o volver a ella. */
const CARGAR_COPIA = /(backup|copia)[^]*(cargar|importar|subir|volver)|(cargar|importar|subir|volver)[^]*(backup|copia)/i;

/** Las pantallas del menú del inicio, en su orden: el enlace y el título con que se reconoce cada una. */
const PANTALLAS_DEL_MENU = [
  { enlace: 'Cambiar la contraseña', titulo: 'Cambiar la contraseña' },
  { enlace: 'Configuración', titulo: 'Configuración' },
  { enlace: 'Artículos', titulo: 'Artículos' },
  { enlace: 'Nuevo artículo', titulo: 'Nuevo artículo' },
  { enlace: 'Nuevo presupuesto', titulo: 'Nuevo presupuesto' },
  { enlace: 'Buscar presupuestos', titulo: 'Buscar presupuestos' },
  { enlace: 'Facturas', titulo: 'Buscar facturas' },
];

/** El único campo de archivo que admite una pantalla: su etiqueta y su `accept`. */
interface CampoDeArchivo {
  etiqueta: string;
  accept: string;
}

/**
 * Pantallas fuera del menú del inicio que tienen un campo de archivo, y el único que admiten (D-27 de FEAT-011): se
 * llega desde la pantalla `desde` del menú por su enlace. "Actualizar costos" carga una planilla .xlsx de costos, no
 * restaura backups.
 */
const PANTALLAS_CON_ARCHIVO: { desde: string; enlace: string; titulo: string; campo: CampoDeArchivo }[] = [
  {
    desde: 'Artículos',
    enlace: 'Actualizar costos con planilla',
    titulo: 'Actualizar costos',
    campo: { etiqueta: 'Planilla de costos (.xlsx)', accept: '.xlsx' },
  },
];

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

const tituloDelInicio = (page: Page) => page.getByRole('heading', { name: 'Sesión iniciada' });
const avisoBackup = (page: Page) => page.getByRole('status').filter({ hasText: 'Falta el backup de ayer.' });

/** Entra como la operadora (en la base nueva define la contraseña) y espera el inicio. */
async function ingresar(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/(definir-contrasena|ingreso)$/);
  await page.getByLabel('Contraseña', { exact: true }).fill(CONTRASENA);
  if (/\/definir-contrasena$/.test(page.url())) {
    await page.getByRole('button', { name: 'Definir contraseña' }).click();
  } else {
    await page.getByRole('button', { name: 'Ingresar' }).click();
  }
  await expect(tituloDelInicio(page)).toBeVisible();
}

/**
 * La pantalla terminó de cargar: su título está y no queda ningún texto "Cargando…" (cada pantalla y sección muestra
 * uno mientras espera a la Api, y lo muestra desde el primer render, así que con el título ya visible no falta ninguno).
 */
async function cargada(page: Page, titulo: string) {
  await expect(page.getByRole('heading', { level: 1, name: titulo, exact: true })).toBeVisible();
  await expect(page.getByText(/^Cargando/)).toHaveCount(0);
}

/**
 * Ningún texto ni control de la pantalla ofrece restaurar la base o cargar una copia. Sin `campo`, la pantalla no
 * tiene ningún campo de archivo; con `campo`, tiene exactamente uno, y es ese: su etiqueta y su `accept`.
 */
async function sinRestaurar(page: Page, pantalla: string, campo?: CampoDeArchivo) {
  await expect(page.locator('body'), `${pantalla}: ningún texto de restaurar`).not.toContainText(RESTAURAR);
  for (const nombre of [RESTAURAR, CARGAR_COPIA]) {
    for (const rol of ['button', 'link', 'menuitem'] as const) {
      await expect(page.getByRole(rol, { name: nombre }), `${pantalla}: ningún ${rol} que coincida con ${nombre}`).toHaveCount(0);
    }
  }
  // Restaurar desde el navegador obligaría a subir el archivo de la copia.
  const camposDeArchivo = page.locator('input[type="file"]');
  if (campo === undefined) {
    await expect(camposDeArchivo, `${pantalla}: ningún campo para subir un archivo`).toHaveCount(0);
    return;
  }
  await expect(camposDeArchivo, `${pantalla}: un único campo para subir un archivo`).toHaveCount(1);
  await expect(camposDeArchivo, `${pantalla}: el campo de archivo admite solo ${campo.accept}`).toHaveAttribute(
    'accept',
    campo.accept,
  );
  // El campo de archivo es el de la etiqueta esperada (y no otro con la misma forma).
  await expect(
    page.getByLabel(campo.etiqueta, { exact: true }).and(camposDeArchivo),
    `${pantalla}: el campo de archivo tiene la etiqueta "${campo.etiqueta}"`,
  ).toHaveCount(1);
}

test('AC-10: sin ningún backup, el inicio avisa al ingresar que no hay ninguno disponible', async ({ page }) => {
  await ingresar(page);

  await expect(avisoBackup(page)).toHaveText(AVISO_SIN_BACKUPS);
  // D-13: sin enlaces ni la palabra "configuración" (otras pruebas buscan el enlace "Configuración" por subcadena).
  await expect(avisoBackup(page).getByRole('link')).toHaveCount(0);
  await expect(avisoBackup(page)).not.toContainText(/configuraci/i);
  // Lo que la pantalla leyó: falta el de ayer, ninguno, sin ubicación. toEqual a propósito (y no toMatchObject): fija
  // también que el estado no traiga ningún otro campo, como la ruta de la ubicación (D-12).
  const estado = await page.request.get('/api/backup/estado');
  expect(estado.status()).toBe(200);
  expect(await estado.json()).toEqual({ faltaAyer: true, ultimo: null, ubicacion: 'sin_elegir', accesible: false });

  // Al volver a ingresar (otra sesión), el aviso sigue: nada cambió.
  await page.getByRole('button', { name: 'Salir' }).click();
  await expect(page).toHaveURL(/\/ingreso$/);
  await ingresar(page);
  await expect(avisoBackup(page)).toHaveText(AVISO_SIN_BACKUPS);
});

test('AC-15: ninguna pantalla con sesión ofrece restaurar la base', async ({ page }) => {
  await ingresar(page);
  await expect(avisoBackup(page)).toBeVisible();
  await sinRestaurar(page, 'Inicio');

  // El menú, leído de la pantalla: una pantalla nueva tiene que sumarse a la lista para que la prueba pase.
  const enlacesDelMenu = await page.getByRole('link').allInnerTexts();
  expect(
    enlacesDelMenu.map((texto) => texto.trim()),
    'El menú del inicio cambió: sumá la pantalla nueva a PANTALLAS_DEL_MENU para revisarla',
  ).toEqual(PANTALLAS_DEL_MENU.map(({ enlace }) => enlace));

  for (const { enlace, titulo } of PANTALLAS_DEL_MENU) {
    await page.getByRole('link', { name: enlace, exact: true }).click();
    await cargada(page, titulo);
    await sinRestaurar(page, titulo);
    await page.goto('/');
    await expect(tituloDelInicio(page)).toBeVisible();
  }

  // Las pantallas que no están en el menú del inicio y tienen un campo de archivo (D-27 de FEAT-011).
  for (const { desde, enlace, titulo, campo } of PANTALLAS_CON_ARCHIVO) {
    await page.getByRole('link', { name: desde, exact: true }).click();
    await cargada(page, desde);
    await page.getByRole('link', { name: enlace, exact: true }).click();
    await cargada(page, titulo);
    await sinRestaurar(page, titulo, campo);
    await page.goto('/');
    await expect(tituloDelInicio(page)).toBeVisible();
  }
});
