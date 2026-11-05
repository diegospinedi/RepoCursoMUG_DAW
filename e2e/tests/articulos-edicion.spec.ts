import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test';

/**
 * Búsqueda, edición y baja de artículos (FEAT-002b, AC-01 a AC-06) de punta a punta, contra la Api real que sirve la
 * SPA compilada.
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva (D-05 de FEAT-001c). Las
 * pruebas no dependen del orden: la que corre primero define la contraseña de esa base y las demás ingresan con ella
 * (`ingresar` resuelve los dos casos); cada prueba crea sus propios artículos por la Api, con la sesión de la página y
 * con códigos en el proveedor propios ("ED-…"), y las que verifican un precio dejan antes Responsable Inscripto,
 * alícuota 21 y múltiplo 0,01. Solo AC-04 graba descripciones con "armazón": la búsqueda de esa prueba tiene que listar
 * todos los artículos que lo contienen y ninguno más, y los de las otras pruebas no lo contienen. Los números se
 * escriben como los escribiría la operadora, con coma decimal y sin separador de miles, y los importes se leen como
 * los muestra la pantalla ("1.936,00").
 */

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-articulos-edicion-1';

const ETIQUETA_CODIGO = 'Código en el proveedor';
const ETIQUETA_COSTO = 'Precio de costo ($)';
const ETIQUETA_MARGEN = 'Margen de utilidad (%)';
const ETIQUETA_PRECIO_VENTA = 'Precio de venta ($)';
const ETIQUETA_ALICUOTA = 'Alícuota de IVA (%)';
const ETIQUETA_CONDICION = 'Condición fiscal';
const ETIQUETA_MULTIPLO = 'Múltiplo de redondeo comercial ($)';
const ETIQUETA_BUSQUEDA = 'Texto a buscar';
const TITULO_TABLA = 'Artículos del catálogo';

const MENSAJE_CODIGO_DUPLICADO = 'Ese código ya pertenece a otro artículo.';
const MENSAJE_ARTICULO_GRABADO = 'Artículo grabado.';
const MENSAJE_CONFIGURACION_GRABADA = 'La configuración se grabó.';

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
const codigoProveedor = (page: Page) => page.getByLabel(ETIQUETA_CODIGO, { exact: true });
const costo = (page: Page) => page.getByLabel(ETIQUETA_COSTO, { exact: true });
const margen = (page: Page) => page.getByLabel(ETIQUETA_MARGEN, { exact: true });
const precioVenta = (page: Page) => page.getByLabel(ETIQUETA_PRECIO_VENTA, { exact: true });
const tabla = (page: Page) => page.getByRole('table', { name: TITULO_TABLA });
const mensajeGrabado = (page: Page) => page.getByRole('status').filter({ hasText: MENSAJE_ARTICULO_GRABADO });

const esListado = (url: string) => new URL(url).pathname === '/api/articulos';
const esDelArticulo = (url: string, codigo: number) => new URL(url).pathname === `/api/articulos/${codigo}`;
const esDelEstado = (url: string, codigo: number) => new URL(url).pathname === `/api/articulos/${codigo}/estado`;

/**
 * Entra como la operadora: en la base nueva (la primera prueba del proyecto) define la contraseña; después ingresa con
 * ella. Termina en el inicio con la sesión iniciada.
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
}

async function volverAlInicio(page: Page) {
  await page.getByRole('link', { name: 'Volver al inicio' }).click();
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
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

/** Lo que tiene grabado la Api de configuración ahora. */
async function configuracionVigente(request: APIRequestContext) {
  const respuesta = await request.get('/api/configuracion');
  expect(respuesta.status()).toBe(200);
  return respuesta.json();
}

/**
 * Deja Responsable Inscripto, alícuota 21 y múltiplo 0,01 desde la pantalla de configuración, como la operadora, y
 * vuelve al inicio. Así las pruebas que verifican un precio (AC-01, AC-02, AC-03 y AC-05) no dependen de la
 * configuración con la que arrancó la base ni de lo que dejó otra prueba.
 */
async function configurarAlicuota21YMultiplo001(page: Page) {
  await page.getByRole('link', { name: 'Configuración' }).click();
  await expect(page).toHaveURL(/\/configuracion$/);
  await page.getByLabel(ETIQUETA_ALICUOTA, { exact: true }).fill('21');
  await page.getByLabel(ETIQUETA_CONDICION, { exact: true }).selectOption('responsable_inscripto');
  await page.getByLabel(ETIQUETA_MULTIPLO, { exact: true }).fill('0,01');
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/configuracion'),
    page.getByRole('button', { name: 'Grabar' }).click(),
  ]);
  expect(respuesta.status()).toBe(200);
  await expect(page.getByRole('status').filter({ hasText: MENSAJE_CONFIGURACION_GRABADA })).toBeVisible();
  expect(await configuracionVigente(page.request)).toMatchObject({
    condicionFiscal: 'responsable_inscripto',
    alicuotaIva: 21,
    multiploRedondeo: 0.01,
  });
  await volverAlInicio(page);
}

/** Abre el listado desde el inicio y espera a que muestre la tabla. */
async function abrirListado(page: Page) {
  await page.getByRole('link', { name: 'Artículos', exact: true }).click();
  await expect(page).toHaveURL(/\/articulos$/);
  await expect(page.getByRole('heading', { name: 'Artículos', exact: true })).toBeVisible();
  await expect(tabla(page)).toBeVisible();
}

/** Busca `texto` con el botón Buscar y espera la respuesta de esa búsqueda. */
async function buscar(page: Page, texto: string) {
  await page.getByLabel(ETIQUETA_BUSQUEDA, { exact: true }).fill(texto);
  const [respuesta] = await Promise.all([
    page.waitForResponse(
      (r) =>
        r.request().method() === 'GET' && esListado(r.url()) && new URL(r.url()).searchParams.get('texto') === texto,
    ),
    page.getByRole('button', { name: 'Buscar' }).click(),
  ]);
  expect(respuesta.status()).toBe(200);
}

/** La fila del listado de ese artículo, ubicada por su enlace de edición. */
const fila = (page: Page, articulo: Grabado) =>
  tabla(page)
    .getByRole('row')
    .filter({ has: page.getByRole('link', { name: `Editar ${articulo.codigo} ${articulo.descripcion}`, exact: true }) });

const COLUMNA_CODIGO_PROVEEDOR = 'Código en el proveedor';
const COLUMNA_DESCRIPCION = 'Descripción';
const COLUMNA_PRECIO_VENTA = 'Precio de venta ($)';
const COLUMNA_ESTADO = 'Estado';

/**
 * La celda de la fila bajo el encabezado `columna`: el índice sale de la posición del encabezado en la tabla, no de un
 * número fijo, así un cambio en el orden de las columnas no hace leer otra.
 */
async function celda(page: Page, filaDelArticulo: Locator, columna: string) {
  const encabezados = tabla(page).getByRole('columnheader');
  await expect(tabla(page).getByRole('columnheader', { name: columna, exact: true })).toHaveCount(1);
  const nombres = await encabezados.allInnerTexts();
  const indice = nombres.findIndex((nombre) => nombre.trim() === columna);
  expect(indice, `No hay una columna "${columna}"`).toBeGreaterThanOrEqual(0);
  return filaDelArticulo.getByRole('cell').nth(indice);
}

/** Desde el listado, busca el artículo por su código en el proveedor y abre su edición con el enlace de la fila. */
async function abrirEdicion(page: Page, articulo: Grabado) {
  await abrirListado(page);
  await buscar(page, articulo.codigoProveedor);
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'GET' && esDelArticulo(r.url(), articulo.codigo)),
    page.getByRole('link', { name: `Editar ${articulo.codigo} ${articulo.descripcion}`, exact: true }).click(),
  ]);
  expect(respuesta.status()).toBe(200);
  await expect(page).toHaveURL(new RegExp(`/articulos/${articulo.codigo}$`));
  await expect(page.getByRole('heading', { name: 'Editar artículo' })).toBeVisible();
  await expect(page.getByText(`Código ${articulo.codigo}`, { exact: true })).toBeVisible();
}

/** Pulsa Grabar en la edición y espera el PUT de los datos; devuelve el código HTTP. */
async function grabarEdicion(page: Page, codigo: number) {
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'PUT' && esDelArticulo(r.url(), codigo)),
    page.getByRole('button', { name: 'Grabar' }).click(),
  ]);
  return respuesta.status();
}

/** Pulsa el botón de la fila (Desactivar o Reactivar) y espera el PUT del estado con 200. */
async function cambiarEstadoDesdeElListado(page: Page, articulo: Grabado, accion: 'Desactivar' | 'Reactivar') {
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'PUT' && esDelEstado(r.url(), articulo.codigo)),
    page.getByRole('button', { name: `${accion} ${articulo.codigo} ${articulo.descripcion}`, exact: true }).click(),
  ]);
  expect(respuesta.status()).toBe(200);
}

/** El campo quedó marcado inválido y su mensaje, vinculado por aria-describedby, es `mensaje`. */
async function rechazadoJuntoAlCampo(page: Page, campo: Locator, mensaje: string) {
  await expect(campo).toHaveAttribute('aria-invalid', 'true');
  const idMensaje = await campo.getAttribute('aria-describedby');
  expect(idMensaje, 'El campo no apunta a su mensaje').toBeTruthy();
  await expect(page.locator(`[id="${idMensaje}"]`)).toHaveText(mensaje);
}

/** Los datos que AC-05 exige conservar, sin el estado ni la versión. */
const datosConservados = (articulo: Grabado) => ({
  codigo: articulo.codigo,
  codigoProveedor: articulo.codigoProveedor,
  descripcion: articulo.descripcion,
  precioCosto: articulo.precioCosto,
  margenUtilidad: articulo.margenUtilidad,
  precioVenta: articulo.precioVenta,
});

test('edicion con codigo duplicado muestra el error rechazado junto al campo', async ({ page }) => {
  await ingresar(page);
  const existente = await crear(page.request, {
    codigoProveedor: 'ED-DUP-1',
    descripcion: 'Estuche rígido de cuero',
    precioCosto: 1000,
    margenUtilidad: 50,
  });
  const editado = await crear(page.request, {
    codigoProveedor: 'ED-DUP-2',
    descripcion: 'Estuche blando de tela',
    precioCosto: 500,
    margenUtilidad: 40,
  });

  await abrirEdicion(page, editado);
  // " ed-dup-1 " es el código del otro artículo sin los espacios y sin distinguir mayúsculas (FR-01, como en el alta).
  await codigoProveedor(page).fill(' ed-dup-1 ');
  expect(await grabarEdicion(page, editado.codigo)).toBe(400);

  await rechazadoJuntoAlCampo(page, codigoProveedor(page), MENSAJE_CODIGO_DUPLICADO);
  await expect(mensajeGrabado(page)).toHaveCount(0);
  // Lo escrito queda para corregirlo, y no se grabó nada en ninguno de los dos.
  await expect(codigoProveedor(page)).toHaveValue(' ed-dup-1 ');
  expect(await consultar(page.request, editado.codigo)).toEqual(editado);
  expect(await consultar(page.request, existente.codigo)).toEqual(existente);
});

test('al abrir un articulo el precio de venta es de solo lectura', async ({ page }) => {
  await ingresar(page);
  await configurarAlicuota21YMultiplo001(page);
  const articulo = await crear(page.request, {
    codigoProveedor: 'ED-AC01',
    descripcion: 'Lente orgánico monofocal',
    precioCosto: 1210,
    margenUtilidad: 50,
  });

  await abrirEdicion(page, articulo);
  // El precio grabado se muestra, pero la operadora no puede cambiarlo (AC-01).
  await expect(precioVenta(page)).toHaveValue('1.815,00');
  await expect(precioVenta(page)).not.toBeEditable();
  // Los datos que sí se editan lo son.
  for (const campo of [codigoProveedor(page), costo(page), margen(page)]) {
    await expect(campo).toBeEditable();
  }
});

test('cambiar el margen de 50 a 60 graba 1.936,00', async ({ page }) => {
  await ingresar(page);
  await configurarAlicuota21YMultiplo001(page);
  const articulo = await crear(page.request, {
    codigoProveedor: 'ED-AC02',
    descripcion: 'Lente bifocal',
    precioCosto: 1210,
    margenUtilidad: 50,
  });
  expect(articulo.precioVenta).toBe(1815);

  await abrirEdicion(page, articulo);
  await expect(margen(page)).toHaveValue('50');
  await margen(page).fill('60');
  expect(await grabarEdicion(page, articulo.codigo)).toBe(200);

  // 1210 × (100 + 60) / 100 = 1936; con alícuota 21 y múltiplo 0,01 queda 1.936,00 (AC-02).
  await expect(mensajeGrabado(page)).toHaveText(MENSAJE_ARTICULO_GRABADO);
  await expect(precioVenta(page)).toHaveValue('1.936,00');
  await expect(precioVenta(page)).not.toBeEditable();
  expect(await consultar(page.request, articulo.codigo)).toMatchObject({
    codigo: articulo.codigo,
    codigoProveedor: 'ED-AC02',
    precioCosto: 1210,
    margenUtilidad: 60,
    precioVenta: 1936,
    activo: true,
  });
});

test('cambiar el costo de 1.210 a 2.420 graba 3.630,00', async ({ page }) => {
  await ingresar(page);
  await configurarAlicuota21YMultiplo001(page);
  const articulo = await crear(page.request, {
    codigoProveedor: 'ED-AC03',
    descripcion: 'Lente multifocal',
    precioCosto: 1210,
    margenUtilidad: 50,
  });
  expect(articulo.precioVenta).toBe(1815);

  await abrirEdicion(page, articulo);
  await expect(costo(page)).toHaveValue('1210');
  await costo(page).fill('2420');
  expect(await grabarEdicion(page, articulo.codigo)).toBe(200);

  // 2420 × (100 + 50) / 100 = 3630; con alícuota 21 y múltiplo 0,01 queda 3.630,00, con 2 decimales (AC-03, NFR-02).
  await expect(mensajeGrabado(page)).toHaveText(MENSAJE_ARTICULO_GRABADO);
  await expect(precioVenta(page)).toHaveValue('3.630,00');
  expect(await consultar(page.request, articulo.codigo)).toMatchObject({
    codigo: articulo.codigo,
    precioCosto: 2420,
    margenUtilidad: 50,
    precioVenta: 3630,
    activo: true,
  });
});

test('buscar armazon lista los que contienen Armazón o armazon y ningún otro', async ({ page }) => {
  await ingresar(page);
  // Los únicos artículos de esta base con "armazón" en la descripción, con y sin tilde y con mayúsculas distintas.
  const coincidentes = [
    await crear(page.request, {
      codigoProveedor: 'ED-AC04-1',
      descripcion: 'Armazón metálico',
      precioCosto: 800,
      margenUtilidad: 25,
    }),
    await crear(page.request, {
      codigoProveedor: 'ED-AC04-2',
      descripcion: 'Estuche para armazon',
      precioCosto: 300,
      margenUtilidad: 30,
    }),
    await crear(page.request, {
      codigoProveedor: 'ED-AC04-3',
      descripcion: 'ARMAZÓN de acetato',
      precioCosto: 900,
      margenUtilidad: 20,
    }),
  ];
  const otro = await crear(page.request, {
    codigoProveedor: 'ED-AC04-4',
    descripcion: 'Cordón para anteojos',
    precioCosto: 100,
    margenUtilidad: 10,
  });
  await abrirListado(page);
  // Sin acento, como lo dicen las pruebas, y con acento, como lo dice AC-04: el resultado es el mismo.
  for (const texto of ['armazon', 'armazón']) {
    await buscar(page, texto);
    // Tantas filas como coincidentes, y cada coincidente una vez, por su nombre accesible: no queda lugar para otro.
    await expect(tabla(page).getByRole('link', { name: /^Editar / })).toHaveCount(coincidentes.length);
    for (const articulo of coincidentes) {
      await expect(
        tabla(page).getByRole('link', { name: `Editar ${articulo.codigo} ${articulo.descripcion}`, exact: true }),
      ).toHaveCount(1);
    }
    await expect(fila(page, otro)).toHaveCount(0);
  }
});

test('desactivar muestra inactivo y conserva los datos', async ({ page }) => {
  await ingresar(page);
  await configurarAlicuota21YMultiplo001(page);
  const articulo = await crear(page.request, {
    codigoProveedor: 'ED-AC05',
    descripcion: 'Anteojo de sol polarizado',
    precioCosto: 1210,
    margenUtilidad: 50,
  });

  await abrirListado(page);
  await buscar(page, articulo.codigoProveedor);
  const filaDelArticulo = fila(page, articulo);
  await expect(await celda(page, filaDelArticulo, COLUMNA_ESTADO)).toHaveText('Activo');
  await cambiarEstadoDesdeElListado(page, articulo, 'Desactivar');

  // AC-05: el listado lo muestra inactivo, con el botón para reactivarlo y los mismos datos.
  await expect(await celda(page, filaDelArticulo, COLUMNA_ESTADO)).toHaveText('Inactivo');
  await expect(
    page.getByRole('button', { name: `Reactivar ${articulo.codigo} ${articulo.descripcion}`, exact: true }),
  ).toBeVisible();
  await expect(await celda(page, filaDelArticulo, COLUMNA_CODIGO_PROVEEDOR)).toHaveText('ED-AC05');
  await expect(await celda(page, filaDelArticulo, COLUMNA_DESCRIPCION)).toHaveText('Anteojo de sol polarizado');
  await expect(await celda(page, filaDelArticulo, COLUMNA_PRECIO_VENTA)).toHaveText('1.815,00');

  const guardado = await consultar(page.request, articulo.codigo);
  expect(guardado.activo).toBe(false);
  expect(datosConservados(guardado)).toEqual(datosConservados(articulo));
});

test('reactivar muestra activo', async ({ page }) => {
  await ingresar(page);
  const articulo = await crear(page.request, {
    codigoProveedor: 'ED-AC06',
    descripcion: 'Lente de contacto blando',
    precioCosto: 1000,
    margenUtilidad: 50,
  });

  await abrirListado(page);
  await buscar(page, articulo.codigoProveedor);
  const filaDelArticulo = fila(page, articulo);
  await cambiarEstadoDesdeElListado(page, articulo, 'Desactivar');
  await expect(await celda(page, filaDelArticulo, COLUMNA_ESTADO)).toHaveText('Inactivo');
  expect((await consultar(page.request, articulo.codigo)).activo).toBe(false);

  await cambiarEstadoDesdeElListado(page, articulo, 'Reactivar');

  // AC-06: el listado lo muestra activo otra vez, con el botón para desactivarlo.
  await expect(await celda(page, filaDelArticulo, COLUMNA_ESTADO)).toHaveText('Activo');
  await expect(
    page.getByRole('button', { name: `Desactivar ${articulo.codigo} ${articulo.descripcion}`, exact: true }),
  ).toBeVisible();
  const guardado = await consultar(page.request, articulo.codigo);
  expect(guardado.activo).toBe(true);
  expect(datosConservados(guardado)).toEqual(datosConservados(articulo));
});
