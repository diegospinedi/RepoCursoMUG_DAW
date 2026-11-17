import { expect, test, type APIRequestContext, type Page, type TestInfo } from '@playwright/test';

import { xlsxMinimo, type Celda } from './xlsx-minimo.ts';

/**
 * Actualización de costos con una planilla .xlsx (FEAT-011, AC-01, AC-02, AC-04, AC-07, AC-08, AC-11 y AC-12) de
 * punta a punta, contra la Api real que sirve la SPA compilada, en Chrome y Edge estables (NFR-02, D-02).
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva (D-05 de FEAT-001c). Cada
 * prueba fija al empezar, por la Api, la configuración de partida (21, Responsable Inscripto, múltiplo 0,01 y sin
 * tope) y crea sus artículos por la Api con la sesión de la página, con códigos en el proveedor propios: el prefijo
 * lleva el número de la prueba y el puerto de la Api ("PC1-5544-…"), así ninguna prueba toca los artículos de otra.
 * Las planillas las arma `xlsx-minimo.ts` en memoria (D-18) y se cargan con `setInputFiles` desde un buffer: no se
 * escribe ningún archivo en el disco. Después de cada carga, los artículos se leen por la Api.
 *
 * El precio de venta es el costo por (100 + margen) / 100, redondeado hacia arriba al múltiplo (AC-01: $2.420 con 50 %
 * da $3.630,00).
 */
test.describe.configure({ mode: 'serial' });

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-actualizar-costos-1';

const ETIQUETA_PLANILLA = 'Planilla de costos (.xlsx)';
const BOTON = 'Actualizar costos';
const ENLACE = 'Actualizar costos con planilla';

const ENCABEZADOS: Celda[] = ['Codigo en el proveedor', 'Precio de Costo Nuevo'];

/** Los textos de la Api (PlanillaCostosController, FR-05) y del dominio (PlanillaCostos, FR-13 a FR-15). */
const MENSAJE_FORMATO =
  'El formato de la planilla no es el esperado. Tiene que tener solo dos columnas, con los encabezados ' +
  '"Codigo en el proveedor" y "Precio de Costo Nuevo" en la fila 1.';
const MENSAJE_NO_ES_XLSX =
  'El archivo no es una planilla .xlsx. Guardala como Libro de Excel (.xlsx) y volvé a cargarla.';
const RAZON_INEXISTENTE = 'código inexistente';
const RAZON_REPETIDO = 'código repetido en la planilla';
const INDICACION_NO_POSITIVO = 'actualizado con precio negativo o cero';
const INDICACION_INACTIVO = 'inactivo';

/** Configuración de partida de cada prueba, como la pide el POST de la Api. */
const PARTIDA = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: null,
};

/** Lo que se graba por la Api para preparar cada prueba. */
interface Datos {
  codigoProveedor: string;
  descripcion: string;
  precioCosto: number;
  margenUtilidad: number;
}

/** Lo que devuelve la Api de un artículo grabado. */
interface Grabado extends Datos {
  codigo: number;
  precioVenta: number;
  activo: boolean;
  version: number;
}

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

const campoContrasena = (page: Page) => page.getByLabel('Contraseña', { exact: true });
const campoPlanilla = (page: Page) => page.getByLabel(ETIQUETA_PLANILLA, { exact: true });
const resumen = (page: Page) => page.getByRole('status').filter({ hasText: /^Se actualizaron / });
const aviso = (page: Page) => page.getByRole('alert');
const tabla = (page: Page, titulo: string) => page.getByRole('table', { name: titulo });

/** El prefijo de los códigos de la prueba `numero` en esta Api: "PC<n>-<puerto>-". */
function prefijo(numero: number, info: TestInfo) {
  const puerto = new URL(String(info.project.use.baseURL)).port;
  return `PC${numero}-${puerto}-`;
}

/**
 * Entra como la operadora: en la base nueva (la primera prueba del proyecto) define la contraseña; después ingresa con
 * ella. Fija por la Api la configuración de partida y termina en el inicio con la sesión iniciada.
 */
async function ingresar(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/(definir-contrasena|ingreso)$/);
  await campoContrasena(page).fill(CONTRASENA);
  if (/\/definir-contrasena$/.test(page.url())) {
    await page.getByRole('button', { name: 'Definir contraseña' }).click();
  } else {
    await page.getByRole('button', { name: 'Ingresar' }).click();
  }
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  const respuesta = await page.request.post('/api/configuracion', { data: PARTIDA });
  expect(respuesta.status(), 'No se pudo fijar la configuración de partida').toBe(200);
}

/** Graba un artículo por la Api con la sesión de la página y devuelve lo que quedó guardado. */
async function crear(request: APIRequestContext, datos: Datos): Promise<Grabado> {
  const respuesta = await request.post('/api/articulos', { data: datos });
  expect(respuesta.status(), `No se pudo crear ${datos.codigoProveedor}`).toBe(201);
  const creado: Grabado = await respuesta.json();
  expect(creado.activo).toBe(true);
  return creado;
}

/** El artículo tal como está grabado ahora, pedido con la sesión de la página. */
async function consultar(request: APIRequestContext, codigo: number): Promise<Grabado> {
  const respuesta = await request.get(`/api/articulos/${codigo}`);
  expect(respuesta.status()).toBe(200);
  return respuesta.json();
}

/** Desactiva el artículo por la Api (FEAT-002b). */
async function desactivar(request: APIRequestContext, codigo: number) {
  const respuesta = await request.put(`/api/articulos/${codigo}/estado`, { data: { activo: false } });
  expect(respuesta.status(), `No se pudo desactivar ${codigo}`).toBe(200);
  expect((await respuesta.json()).activo).toBe(false);
}

/** Abre "Actualizar costos" desde el inicio, pasando por Artículos, como la operadora. */
async function abrirActualizarCostos(page: Page) {
  await page.getByRole('link', { name: 'Artículos', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Artículos' })).toBeVisible();
  await page.getByRole('link', { name: ENLACE }).click();
  await expect(page).toHaveURL(/\/articulos\/actualizar-costos$/);
  await expect(page.getByRole('heading', { name: 'Actualizar costos' })).toBeVisible();
}

/**
 * Elige el archivo (desde un buffer, sin tocar el disco) y pulsa el botón; espera el POST de la planilla y devuelve su
 * código HTTP.
 */
async function cargar(page: Page, archivo: { name: string; mimeType: string; buffer: Buffer }) {
  await campoPlanilla(page).setInputFiles(archivo);
  const [respuesta] = await Promise.all([
    page.waitForResponse(
      (r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/articulos/planilla-costos',
    ),
    page.getByRole('button', { name: BOTON, exact: true }).click(),
  ]);
  return respuesta.status();
}

/** Carga una planilla .xlsx armada en memoria con `filas` (la primera es la de encabezados). */
const cargarXlsx = (page: Page, filas: Celda[][]) =>
  cargar(page, {
    name: 'costos.xlsx',
    mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    buffer: xlsxMinimo(filas),
  });

/**
 * Las filas de la tabla de la pantalla, como texto de cada celda. Antes de leerlas espera, con una aserción que
 * reintenta, a que la tabla tenga `cantidad` filas: `count()` no espera y podría leer la tabla a medio pintar.
 */
async function filasDe(page: Page, titulo: string, cantidad: number) {
  const filas = tabla(page, titulo).locator('tbody tr');
  await expect(filas, `La tabla "${titulo}" tiene que tener ${cantidad} filas`).toHaveCount(cantidad);
  const resultado: string[][] = [];
  for (let i = 0; i < cantidad; i++) {
    resultado.push((await filas.nth(i).locator('td').allInnerTexts()).map((texto) => texto.trim()));
  }
  return resultado;
}

/** Costo, venta, estado y versión: lo que una carga rechazada no puede cambiar. */
const estado = ({ precioCosto, precioVenta, activo, version }: Grabado) => ({
  precioCosto,
  precioVenta,
  activo,
  version,
});

test('actualiza costo y venta y lista la fila inexistente', async ({ page }, info) => {
  const p = prefijo(1, info);
  await ingresar(page);
  // AC-01: $1.210 con margen 50 %; la planilla trae $2.420.
  const abc = await crear(page.request, {
    codigoProveedor: `${p}ABC-1`,
    descripcion: 'Armazón de prueba AC-01',
    precioCosto: 1210,
    margenUtilidad: 50,
  });
  expect(abc.precioVenta).toBe(1815);
  const otro = await crear(page.request, {
    codigoProveedor: `${p}B`,
    descripcion: 'Lente de prueba AC-02',
    precioCosto: 100,
    margenUtilidad: 10,
  });
  const inexistente = `${p}NO-EXISTE`;

  await abrirActualizarCostos(page);
  const status = await cargarXlsx(page, [
    ENCABEZADOS,
    [`${p}ABC-1`, 2420],
    [`${p}B`, 200],
    [inexistente, 500],
  ]);

  expect(status).toBe(200);
  await expect(resumen(page)).toHaveText('Se actualizaron 2 artículos de 3 filas.');
  await expect(aviso(page)).toHaveCount(0);
  expect(await filasDe(page, 'Filas no aplicadas', 1)).toEqual([['4', inexistente, RAZON_INEXISTENTE]]);
  await expect(tabla(page, 'Para revisar')).toHaveCount(0);

  expect(await consultar(page.request, abc.codigo)).toMatchObject({
    precioCosto: 2420,
    precioVenta: 3630,
    margenUtilidad: 50,
    activo: true,
  });
  expect(await consultar(page.request, otro.codigo)).toMatchObject({ precioCosto: 200, precioVenta: 220 });
  const buscado = await page.request.get(`/api/articulos?texto=${encodeURIComponent(inexistente)}`);
  expect(buscado.status()).toBe(200);
  expect((await buscado.json()).total, 'La fila inexistente no crea un artículo').toBe(0);
});

test('planilla con tercera columna rechazada sin actualizar', async ({ page }, info) => {
  const p = prefijo(2, info);
  await ingresar(page);
  const articulo = await crear(page.request, {
    codigoProveedor: `${p}ABC-1`,
    descripcion: 'Armazón de prueba AC-04',
    precioCosto: 1210,
    margenUtilidad: 50,
  });

  await abrirActualizarCostos(page);
  const status = await cargarXlsx(page, [
    [...ENCABEZADOS, 'Notas'],
    [`${p}ABC-1`, 2420, 'sube'],
  ]);

  expect(status).toBe(400);
  await expect(aviso(page)).toHaveText(MENSAJE_FORMATO);
  await expect(campoPlanilla(page)).toHaveAttribute('aria-invalid', 'true');
  await expect(resumen(page)).toHaveCount(0);
  expect(estado(await consultar(page.request, articulo.codigo))).toEqual(estado(articulo));
});

test('archivo csv rechazado pide xlsx', async ({ page }, info) => {
  const p = prefijo(3, info);
  await ingresar(page);
  const articulo = await crear(page.request, {
    codigoProveedor: `${p}ABC-1`,
    descripcion: 'Armazón de prueba AC-07',
    precioCosto: 1210,
    margenUtilidad: 50,
  });

  await abrirActualizarCostos(page);
  const status = await cargar(page, {
    name: 'costos.csv',
    mimeType: 'text/csv',
    buffer: Buffer.from(`Codigo en el proveedor,Precio de Costo Nuevo\n${p}ABC-1,2420\n`, 'utf8'),
  });

  expect(status).toBe(400);
  await expect(aviso(page)).toHaveText(MENSAJE_NO_ES_XLSX);
  await expect(resumen(page)).toHaveCount(0);
  expect(estado(await consultar(page.request, articulo.codigo))).toEqual(estado(articulo));
});

test('lista precio negativo o cero, repetidos e inactivo', async ({ page }, info) => {
  const p = prefijo(4, info);
  await ingresar(page);
  const datos = (sufijo: string, precioCosto: number): Datos => ({
    codigoProveedor: `${p}${sufijo}`,
    descripcion: `Artículo de prueba ${sufijo}`,
    precioCosto,
    margenUtilidad: 50,
  });
  const negativo = await crear(page.request, datos('NEG', 100));
  const cero = await crear(page.request, datos('CERO', 100));
  const repetido = await crear(page.request, datos('ABC-1', 1210));
  const inactivo = await crear(page.request, datos('INACT', 100));
  await desactivar(page.request, inactivo.codigo);
  const repetidoAntes = await consultar(page.request, repetido.codigo);

  await abrirActualizarCostos(page);
  const status = await cargarXlsx(page, [
    ENCABEZADOS,
    [`${p}NEG`, -100],
    [`${p}CERO`, 0],
    // AC-11: el mismo código en mayúsculas y en minúsculas.
    [`${p}ABC-1`, 2420],
    [`${p}abc-1`, 2500],
    [`${p}INACT`, 300],
  ]);

  expect(status).toBe(200);
  await expect(resumen(page)).toHaveText('Se actualizaron 3 artículos de 5 filas.');
  expect(await filasDe(page, 'Filas no aplicadas', 2)).toEqual([
    ['4', `${p}ABC-1`, RAZON_REPETIDO],
    ['5', `${p}abc-1`, RAZON_REPETIDO],
  ]);
  expect(await filasDe(page, 'Para revisar', 3)).toEqual([
    ['2', `${p}NEG`, '-100,00', INDICACION_NO_POSITIVO],
    ['3', `${p}CERO`, '0,00', INDICACION_NO_POSITIVO],
    ['6', `${p}INACT`, '300,00', INDICACION_INACTIVO],
  ]);

  // AC-08: los dos se actualizan, con la venta recalculada.
  expect(await consultar(page.request, negativo.codigo)).toMatchObject({ precioCosto: -100, precioVenta: -150 });
  expect(await consultar(page.request, cero.codigo)).toMatchObject({ precioCosto: 0, precioVenta: 0 });
  // AC-11: el repetido queda como estaba.
  expect(estado(await consultar(page.request, repetido.codigo))).toEqual(estado(repetidoAntes));
  // AC-12: el inactivo se actualiza y sigue inactivo.
  expect(await consultar(page.request, inactivo.codigo)).toMatchObject({
    precioCosto: 300,
    precioVenta: 450,
    activo: false,
  });
});
