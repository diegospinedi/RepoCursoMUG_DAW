import { expect, test, type Locator, type Page } from '@playwright/test';

/**
 * Alta de artículos con precio de venta calculado (FEAT-002a, AC-01 a AC-11) de punta a punta, contra la Api real que
 * sirve la SPA compilada.
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva (D-05 de FEAT-001c). Las
 * pruebas corren en orden porque dependen de lo grabado: primero, con la configuración inicial (Responsable Inscripto,
 * alícuota 21 y múltiplo 0,01), las altas y los rechazos (AC-01, AC-07, AC-08, AC-04, AC-11, AC-02, AC-03, AC-05 y
 * AC-06); después el múltiplo 50 desde la pantalla de configuración (AC-10), que vuelve a 0,01; y al final Monotributo
 * (AC-09). Los números se escriben como los escribiría la operadora, con coma decimal y sin separador de miles (D-11),
 * y los importes se leen como los muestra la pantalla ("1.850,00").
 */
test.describe.configure({ mode: 'serial' });

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-articulos-1';

const ETIQUETA_CODIGO = 'Código en el proveedor';
const ETIQUETA_DESCRIPCION = 'Descripción';
const ETIQUETA_COSTO = 'Precio de costo ($)';
const ETIQUETA_MARGEN = 'Margen de utilidad (%)';
const ETIQUETA_PRECIO_VENTA = 'Precio de venta ($)';
const ETIQUETA_CONDICION = 'Condición fiscal';
const ETIQUETA_MULTIPLO = 'Múltiplo de redondeo comercial ($)';

const MENSAJE_CODIGO_DUPLICADO = 'Ese código ya pertenece a otro artículo.';
const MENSAJE_CODIGO_FALTANTE = 'Completá el código en el proveedor.';
const MENSAJE_DESCRIPCION_FALTANTE = 'Completá la descripción.';
const MENSAJE_MARGEN_FUERA_DE_RANGO = 'El margen debe estar entre 0 y 1.000.';
const ADVERTENCIA_COSTO = 'Precio de costo negativo o cero';
const MENSAJE_CONFIGURACION_GRABADA = 'La configuración se grabó.';

const mensajeGrabado = (codigo: number) => `Artículo grabado con el código ${codigo}.`;

/** Lo que escribe la operadora en el alta. */
interface Escrito {
  codigoProveedor: string;
  descripcion: string;
  precioCosto: string;
  margenUtilidad: string;
}

let violacionesCsp: string[] = [];
/** Los POST a /api/articulos que mandó la página en la prueba en curso. */
let altasEnviadas: string[] = [];
/** Los códigos asignados en este proyecto, en el orden de las altas: cada uno distinto y mayor que el anterior. */
const codigosAsignados: number[] = [];

test.beforeEach(({ page }) => {
  violacionesCsp = [];
  altasEnviadas = [];
  page.on('console', (mensaje) => {
    if (/Content Security Policy/i.test(mensaje.text())) {
      violacionesCsp.push(mensaje.text());
    }
  });
  page.on('request', (pedido) => {
    if (pedido.method() === 'POST' && new URL(pedido.url()).pathname === '/api/articulos') {
      altasEnviadas.push(pedido.url());
    }
  });
});

test.afterEach(() => {
  expect(violacionesCsp, 'La SPA no debe violar Content-Security-Policy').toEqual([]);
});

const campoContrasena = (page: Page) => page.getByLabel('Contraseña', { exact: true });
const codigo = (page: Page) => page.getByLabel(ETIQUETA_CODIGO, { exact: true });
const descripcion = (page: Page) => page.getByLabel(ETIQUETA_DESCRIPCION, { exact: true });
const costo = (page: Page) => page.getByLabel(ETIQUETA_COSTO, { exact: true });
const margen = (page: Page) => page.getByLabel(ETIQUETA_MARGEN, { exact: true });
const precioVenta = (page: Page) => page.getByLabel(ETIQUETA_PRECIO_VENTA, { exact: true });
const condicion = (page: Page) => page.getByLabel(ETIQUETA_CONDICION, { exact: true });
const multiplo = (page: Page) => page.getByLabel(ETIQUETA_MULTIPLO, { exact: true });

const mensajeDeAlta = (page: Page) => page.getByRole('status').filter({ hasText: /^Artículo grabado con el código/ });
const advertencia = (page: Page) => page.getByRole('status').filter({ hasText: ADVERTENCIA_COSTO });

async function volverAlInicio(page: Page) {
  await page.getByRole('link', { name: 'Volver al inicio' }).click();
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
}

async function abrirNuevoArticulo(page: Page) {
  await page.getByRole('link', { name: 'Nuevo artículo' }).click();
  await expect(page).toHaveURL(/\/articulos\/nuevo$/);
  await expect(page.getByRole('heading', { name: 'Nuevo artículo' })).toBeVisible();
}

async function abrirConfiguracion(page: Page) {
  await page.getByRole('link', { name: 'Configuración' }).click();
  await expect(page).toHaveURL(/\/configuracion$/);
  await expect(page.getByRole('heading', { name: 'Configuración' })).toBeVisible();
}

/** Ingresa con la contraseña ya definida y abre el alta de artículos desde el inicio, como la operadora. */
async function ingresarYAbrir(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/ingreso$/);
  await campoContrasena(page).fill(CONTRASENA);
  await page.getByRole('button', { name: 'Ingresar' }).click();
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await abrirNuevoArticulo(page);
}

/** Vuelve al inicio y abre de nuevo el alta: la SPA monta la pantalla otra vez, con el formulario vacío. */
async function volverAAbrir(page: Page) {
  await volverAlInicio(page);
  await abrirNuevoArticulo(page);
}

async function escribir(page: Page, escrito: Escrito) {
  await codigo(page).fill(escrito.codigoProveedor);
  await descripcion(page).fill(escrito.descripcion);
  await costo(page).fill(escrito.precioCosto);
  await margen(page).fill(escrito.margenUtilidad);
}

/** Pulsa Grabar y espera la respuesta de la Api; devuelve el código HTTP y el cuerpo. */
async function grabar(page: Page) {
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/articulos'),
    page.getByRole('button', { name: 'Grabar' }).click(),
  ]);
  return { estado: respuesta.status(), cuerpo: await respuesta.json() };
}

/** El artículo grabado con ese código, pedido con la sesión de la página: 200 y su cuerpo, o el código de error. */
async function consultar(page: Page, codigoAsignado: number) {
  const respuesta = await page.request.get(`/api/articulos/${codigoAsignado}`);
  return { estado: respuesta.status(), cuerpo: respuesta.status() === 200 ? await respuesta.json() : null };
}

/**
 * Escribe el artículo, lo graba y comprueba lo que ve la operadora (el código asignado y el Precio de Venta, con la
 * advertencia si corresponde) y lo que quedó guardado. Devuelve el artículo guardado.
 */
async function altaGrabada(
  page: Page,
  escrito: Escrito,
  esperado: { precioVentaMostrado: string; precioVenta: number; advertencia: string | null },
) {
  await escribir(page, escrito);
  const { estado, cuerpo } = await grabar(page);
  expect(estado).toBe(201);

  const asignado: number = cuerpo.codigo;
  expect(Number.isInteger(asignado) && asignado > 0, 'El código asignado no es un entero positivo').toBe(true);
  for (const anterior of codigosAsignados) {
    expect(asignado, 'El código asignado no es autonumérico').toBeGreaterThan(anterior);
  }
  codigosAsignados.push(asignado);

  await expect(mensajeDeAlta(page)).toHaveText(mensajeGrabado(asignado));
  await expect(precioVenta(page)).toHaveValue(esperado.precioVentaMostrado);
  if (esperado.advertencia === null) {
    await expect(advertencia(page)).toHaveCount(0);
  } else {
    await expect(advertencia(page)).toHaveText(esperado.advertencia);
  }
  // El formulario queda listo para otra alta.
  for (const campo of [codigo(page), descripcion(page), costo(page), margen(page)]) {
    await expect(campo).toHaveValue('');
  }

  const guardado = await consultar(page, asignado);
  expect(guardado.estado).toBe(200);
  expect(guardado.cuerpo.codigo).toBe(asignado);
  expect(guardado.cuerpo.precioVenta).toBe(esperado.precioVenta);
  expect(guardado.cuerpo.advertencia).toBe(esperado.advertencia);
  return guardado.cuerpo;
}

/** El campo quedó marcado inválido y su mensaje, vinculado por aria-describedby, es `mensaje`. */
async function rechazadoJuntoAlCampo(page: Page, campo: Locator, mensaje: string) {
  await expect(campo).toHaveAttribute('aria-invalid', 'true');
  const idMensaje = await campo.getAttribute('aria-describedby');
  expect(idMensaje, 'El campo no apunta a su mensaje').toBeTruthy();
  await expect(page.locator(`[id="${idMensaje}"]`)).toHaveText(mensaje);
}

/** Después del último código asignado no hay otro artículo: el rechazo no grabó nada. */
async function nadaNuevoGrabado(page: Page) {
  const ultimo = codigosAsignados.at(-1);
  expect(ultimo, 'No hay un alta anterior con la que comparar').toBeDefined();
  expect((await consultar(page, (ultimo as number) + 1)).estado).toBe(404);
}

/**
 * Pulsa Grabar con un valor que la pantalla rechaza: el mensaje queda junto al campo, no hay mensaje de alta, no se
 * grabó nada y no se envió nada. `altasEnviadas` se mira al final, cuando la página ya tuvo tiempo de mandar un POST.
 */
async function rechazadoSinGrabar(page: Page, campo: Locator, mensaje: string) {
  await page.getByRole('button', { name: 'Grabar' }).click();
  await rechazadoJuntoAlCampo(page, campo, mensaje);
  await expect(mensajeDeAlta(page)).toHaveCount(0);
  await expect(precioVenta(page)).toHaveValue('');
  await nadaNuevoGrabado(page);
  expect(altasEnviadas, 'La pantalla envió un artículo que debía rechazar').toEqual([]);
}

/** Lo que tiene grabado la Api de configuración ahora. */
async function configuracionVigente(page: Page) {
  const respuesta = await page.request.get('/api/configuracion');
  expect(respuesta.status()).toBe(200);
  return respuesta.json();
}

/** Graba la configuración desde su pantalla, como la operadora, y vuelve a abrir el alta de artículos. */
async function grabarConfiguracion(page: Page, cambiar: () => Promise<void>) {
  await volverAlInicio(page);
  await abrirConfiguracion(page);
  await cambiar();
  const [respuesta] = await Promise.all([
    page.waitForResponse(
      (r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/configuracion',
    ),
    page.getByRole('button', { name: 'Grabar' }).click(),
  ]);
  expect(respuesta.status()).toBe(200);
  await expect(page.getByRole('status').filter({ hasText: MENSAJE_CONFIGURACION_GRABADA })).toBeVisible();
  await volverAlInicio(page);
  await abrirNuevoArticulo(page);
}

test('articulos: alta asigna código y precio', async ({ page }) => {
  // Base nueva: define la contraseña e ingresa (FEAT-001a), y abre el alta desde el inicio.
  await page.goto('/');
  await expect(page).toHaveURL(/\/definir-contrasena$/);
  await campoContrasena(page).fill(CONTRASENA);
  await page.getByRole('button', { name: 'Definir contraseña' }).click();
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await abrirNuevoArticulo(page);
  expect(await configuracionVigente(page)).toMatchObject({
    condicionFiscal: 'responsable_inscripto',
    alicuotaIva: 21,
    multiploRedondeo: 0.01,
  });

  // El Precio de Venta lo calcula el sistema: vacío hasta grabar y de solo lectura (FR-11).
  await expect(precioVenta(page)).toHaveValue('');
  await expect(precioVenta(page)).not.toBeEditable();

  const guardado = await altaGrabada(
    page,
    { codigoProveedor: 'ABC-1', descripcion: 'Armazón metálico', precioCosto: '800', margenUtilidad: '25' },
    { precioVentaMostrado: '1.000,00', precioVenta: 1000, advertencia: null },
  );
  expect(guardado).toMatchObject({
    codigoProveedor: 'ABC-1',
    descripcion: 'Armazón metálico',
    precioCosto: 800,
    margenUtilidad: 25,
  });
  await expect(precioVenta(page)).not.toBeEditable();
});

test('articulos: costo 1210 y margen 50 da 1.815,00', async ({ page }) => {
  await ingresarYAbrir(page);

  await altaGrabada(
    page,
    { codigoProveedor: 'AC-07', descripcion: 'Lente orgánico', precioCosto: '1210', margenUtilidad: '50' },
    { precioVentaMostrado: '1.815,00', precioVenta: 1815, advertencia: null },
  );
});

test('articulos: costo 1000 y margen 50 da 1.500,00', async ({ page }) => {
  await ingresarYAbrir(page);

  await altaGrabada(
    page,
    { codigoProveedor: 'AC-08', descripcion: 'Estuche rígido', precioCosto: '1000', margenUtilidad: '50' },
    { precioVentaMostrado: '1.500,00', precioVenta: 1500, advertencia: null },
  );
});

test('articulos: costo 1210 y margen 150 da 3.025,00', async ({ page }) => {
  await ingresarYAbrir(page);

  await altaGrabada(
    page,
    { codigoProveedor: 'AC-04', descripcion: 'Lente de contacto', precioCosto: '1210', margenUtilidad: '150' },
    { precioVentaMostrado: '3.025,00', precioVenta: 3025, advertencia: null },
  );
});

test('articulos: 1.666,656 redondea a 1.666,66', async ({ page }) => {
  await ingresarYAbrir(page);

  // 1388,88 × (100 + 20) / 100 = 1666,656 exacto: con múltiplo 0,01 sube a 1666,66, nunca baja a 1666,65 (FR-09).
  await altaGrabada(
    page,
    { codigoProveedor: 'AC-11', descripcion: 'Cordón para anteojos', precioCosto: '1388,88', margenUtilidad: '20' },
    { precioVentaMostrado: '1.666,66', precioVenta: 1666.66, advertencia: null },
  );
});

test('articulos: código duplicado rechazado junto al campo', async ({ page }) => {
  await ingresarYAbrir(page);

  // "ABC-1" ya lo grabó AC-01; " abc-1 " es el mismo código sin los espacios y sin distinguir mayúsculas (FR-02).
  await escribir(page, {
    codigoProveedor: ' abc-1 ',
    descripcion: 'Otro armazón',
    precioCosto: '900',
    margenUtilidad: '30',
  });
  const { estado } = await grabar(page);
  expect(estado).toBe(400);

  await rechazadoJuntoAlCampo(page, codigo(page), MENSAJE_CODIGO_DUPLICADO);
  await expect(mensajeDeAlta(page)).toHaveCount(0);
  await expect(precioVenta(page)).toHaveValue('');
  // Lo escrito queda para corregirlo.
  await expect(codigo(page)).toHaveValue(' abc-1 ');
  await nadaNuevoGrabado(page);
});

test('articulos: sin código o sin descripción rechazado', async ({ page }) => {
  await ingresarYAbrir(page);

  // Cada caso en una pantalla recién abierta, sin el error del anterior.
  await escribir(page, { codigoProveedor: '', descripcion: 'Sin código', precioCosto: '100', margenUtilidad: '10' });
  await rechazadoSinGrabar(page, codigo(page), MENSAJE_CODIGO_FALTANTE);
  await expect(descripcion(page)).not.toHaveAttribute('aria-invalid', 'true');

  await volverAAbrir(page);
  await expect(codigo(page)).not.toHaveAttribute('aria-invalid', 'true');
  await escribir(page, { codigoProveedor: 'AC-03', descripcion: '', precioCosto: '100', margenUtilidad: '10' });
  await rechazadoSinGrabar(page, descripcion(page), MENSAJE_DESCRIPCION_FALTANTE);
  await expect(codigo(page)).not.toHaveAttribute('aria-invalid', 'true');
});

test('articulos: margen -1 y 1000,01 rechazado', async ({ page }) => {
  await ingresarYAbrir(page);

  for (const valor of ['-1', '1000,01']) {
    // Cada valor en una pantalla recién abierta, sin el error del anterior: así 1000,01 se prueba por separado.
    await expect(margen(page)).not.toHaveAttribute('aria-invalid', 'true');
    await escribir(page, {
      codigoProveedor: 'AC-05',
      descripcion: 'Margen fuera de rango',
      precioCosto: '100',
      margenUtilidad: valor,
    });
    await rechazadoSinGrabar(page, margen(page), MENSAJE_MARGEN_FUERA_DE_RANGO);

    await volverAAbrir(page);
  }

  expect(altasEnviadas, 'La pantalla envió un margen que debía rechazar').toEqual([]);
});

test('articulos: costo -100 graba -150,00 con advertencia', async ({ page }) => {
  await ingresarYAbrir(page);

  await altaGrabada(
    page,
    { codigoProveedor: 'AC-06', descripcion: 'Bonificación', precioCosto: '-100', margenUtilidad: '50' },
    { precioVentaMostrado: '-150,00', precioVenta: -150, advertencia: ADVERTENCIA_COSTO },
  );
});

test('articulos: múltiplo 50 redondea a 1.850,00', async ({ page }) => {
  await ingresarYAbrir(page);

  await grabarConfiguracion(page, () => multiplo(page).fill('50'));
  expect((await configuracionVigente(page)).multiploRedondeo).toBe(50);

  // 1815,37 × (100 + 0) / 100 = 1815,37: con múltiplo 50 sube a 1850 (FR-09), leyendo la configuración vigente (FR-10).
  await altaGrabada(
    page,
    { codigoProveedor: 'AC-10', descripcion: 'Anteojo de sol', precioCosto: '1815,37', margenUtilidad: '0' },
    { precioVentaMostrado: '1.850,00', precioVenta: 1850, advertencia: null },
  );

  // Vuelve al múltiplo inicial para la prueba siguiente.
  await grabarConfiguracion(page, () => multiplo(page).fill('0,01'));
  expect((await configuracionVigente(page)).multiploRedondeo).toBe(0.01);
});

test('articulos: Monotributo da 1.815,00', async ({ page }) => {
  await ingresarYAbrir(page);

  await grabarConfiguracion(page, () => condicion(page).selectOption({ label: 'Monotributo' }));
  expect(await configuracionVigente(page)).toMatchObject({ condicionFiscal: 'monotributo', multiploRedondeo: 0.01 });

  await altaGrabada(
    page,
    { codigoProveedor: 'AC-09', descripcion: 'Lente monofocal', precioCosto: '1210', margenUtilidad: '50' },
    { precioVentaMostrado: '1.815,00', precioVenta: 1815, advertencia: null },
  );
});
