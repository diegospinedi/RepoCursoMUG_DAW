import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test';

/**
 * Recálculo del catálogo al grabar la configuración (FEAT-002c, AC-01 a AC-04) de punta a punta, contra la Api real que
 * sirve la SPA compilada.
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva (D-05 de FEAT-001c). Cada
 * prueba fija al empezar, por la Api, la configuración de partida: 21, Responsable Inscripto, 0,01 y sin tope (D-24 de
 * FEAT-002c); crea sus artículos por la Api con la sesión de la página, con códigos en el proveedor propios ("RC-…"), y
 * cambia la configuración desde la pantalla, como la operadora.
 *
 * La configuración de partida resuelve la configuración, pero no los artículos: cada prueba deja los suyos en la base.
 * Por eso la cantidad del mensaje de D-06 no se escribe fija: se compara con los artículos de toda la base cuyo precio
 * de venta cambió (D-09), leídos por la Api antes y después de grabar. Y por eso las pruebas van en orden y AC-04 es la
 * última: el artículo de costo `ImporteMaximo` que deja haría rechazar cualquier recálculo posterior con múltiplo 50
 * (D-24). Ese artículo solo queda en la base de este proyecto, que se borra al terminar la suite.
 *
 * Los números se escriben como los escribiría la operadora, con coma decimal, y los importes se leen de la Api.
 */
test.describe.configure({ mode: 'serial' });

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-config-recalculo-1';

const ETIQUETA_ALICUOTA = 'Alícuota de IVA (%)';
const ETIQUETA_MULTIPLO = 'Múltiplo de redondeo comercial ($)';

const MENSAJE_GRABADA = 'La configuración se grabó.';

/** Configuración de partida de cada prueba (D-24), como la pide el POST de la Api. */
const PARTIDA = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: null,
};

/**
 * `ReglasImporte.ImporteMaximo` escrito como texto: en un número de JavaScript perdería los centavos, por eso el cuerpo
 * del alta de AC-04 se manda como texto y la respuesta se lee con `respuesta.text()`.
 */
const IMPORTE_MAXIMO = '92233720368547758.07';

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

/** Un artículo del listado de la Api (sin costo ni margen). */
interface Listado {
  codigo: number;
  precioVenta: number;
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
const alicuota = (page: Page) => page.getByLabel(ETIQUETA_ALICUOTA, { exact: true });
const multiplo = (page: Page) => page.getByLabel(ETIQUETA_MULTIPLO, { exact: true });
const mensajeGrabada = (page: Page) => page.getByRole('status').filter({ hasText: MENSAJE_GRABADA });

const esConfiguracion = (url: string) => new URL(url).pathname === '/api/configuracion';

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

/** Ingresa y fija por la Api la configuración de partida (D-24); verifica que quedó vigente. */
async function ingresarConConfiguracionDePartida(page: Page) {
  await ingresar(page);
  const respuesta = await page.request.post('/api/configuracion', { data: PARTIDA });
  expect(respuesta.status(), 'No se pudo fijar la configuración de partida').toBe(200);
  expect(await configuracionVigente(page.request)).toMatchObject({
    alicuotaIva: 21,
    condicionFiscal: 'responsable_inscripto',
    multiploRedondeo: 0.01,
    topeIdentificacion: null,
  });
}

/** Lo que tiene grabado la Api de configuración ahora. */
async function configuracionVigente(request: APIRequestContext) {
  const respuesta = await request.get('/api/configuracion');
  expect(respuesta.status()).toBe(200);
  return respuesta.json();
}

/**
 * El cuerpo de un GET como texto, sin perder centavos de importes grandes. Exige 200 antes de leerlo: si no, dos
 * respuestas de error iguales pasarían por "sin cambios".
 */
async function textoLeido(request: APIRequestContext, ruta: string) {
  const respuesta = await request.get(ruta);
  expect(respuesta.status(), `GET ${ruta}`).toBe(200);
  return respuesta.text();
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

/** El precio de venta de cada artículo de la base, activos e inactivos, recorriendo todas las páginas del listado. */
async function preciosDeLaBase(request: APIRequestContext): Promise<Map<number, number>> {
  const precios = new Map<number, number>();
  for (let pagina = 1; ; pagina++) {
    const respuesta = await request.get(`/api/articulos?pagina=${pagina}`);
    expect(respuesta.status()).toBe(200);
    const { articulos, total }: { articulos: Listado[]; total: number } = await respuesta.json();
    for (const articulo of articulos) {
      precios.set(articulo.codigo, articulo.precioVenta);
    }
    if (articulos.length === 0 || precios.size >= total) {
      expect(precios.size, 'El listado no devolvió todos los artículos').toBe(total);
      return precios;
    }
  }
}

/** Cuántos artículos de `antes` tienen otro precio de venta en `despues`. */
function cambiados(antes: Map<number, number>, despues: Map<number, number>) {
  expect([...despues.keys()].sort()).toEqual([...antes.keys()].sort());
  return [...antes].filter(([codigo, precio]) => despues.get(codigo) !== precio).length;
}

/** El mensaje de D-06 para `cantidad` artículos, con punto de miles (D-27). */
function mensajeEsperado(cantidad: number) {
  if (cantidad === 0) {
    return `${MENSAJE_GRABADA} Ningún precio de venta cambió.`;
  }
  const articulos =
    cantidad === 1 ? '1 artículo' : `${String(cantidad).replace(/\B(?=(\d{3})+(?!\d))/g, '.')} artículos`;
  return `${MENSAJE_GRABADA} Cambió el precio de venta de ${articulos}.`;
}

/** Abre la configuración desde el inicio, como la operadora. */
async function abrirConfiguracion(page: Page) {
  await page.getByRole('link', { name: 'Configuración' }).click();
  await expect(page).toHaveURL(/\/configuracion$/);
  await expect(page.getByRole('heading', { name: 'Configuración' })).toBeVisible();
}

/** Pulsa Grabar en la configuración y espera el POST; devuelve el código HTTP y el cuerpo como texto. */
async function grabarConfiguracion(page: Page) {
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'POST' && esConfiguracion(r.url())),
    page.getByRole('button', { name: 'Grabar' }).click(),
  ]);
  return { estado: respuesta.status(), texto: await respuesta.text() };
}

/**
 * Graba desde la pantalla un cambio que dispara el recálculo y verifica el mensaje de D-06 contra los precios de toda
 * la base: la cantidad que informan la Api y la pantalla es la de los artículos cuyo precio de venta cambió (D-09).
 */
async function grabarConRecalculo(page: Page, cambiar: () => Promise<void>) {
  const antes = await preciosDeLaBase(page.request);
  await abrirConfiguracion(page);
  await cambiar();
  const { estado, texto } = await grabarConfiguracion(page);
  expect(estado, texto).toBe(200);

  const despues = await preciosDeLaBase(page.request);
  const cantidad = cambiados(antes, despues);
  expect(JSON.parse(texto).preciosCambiados).toBe(cantidad);
  await expect(mensajeGrabada(page)).toHaveText(mensajeEsperado(cantidad));
  return cantidad;
}

/** El campo quedó marcado inválido y su mensaje, vinculado por aria-describedby, es `mensaje`. */
async function rechazadoJuntoAlCampo(page: Page, campo: Locator, mensaje: string) {
  await expect(campo).toHaveAttribute('aria-invalid', 'true');
  const idMensaje = await campo.getAttribute('aria-describedby');
  expect(idMensaje, 'El campo no apunta a su mensaje').toBeTruthy();
  await expect(page.locator(`[id="${idMensaje}"]`)).toHaveText(mensaje);
}

test('alicuota 10,5 y luego costo 1.105 con margen 50 graba 1.657,50', async ({ page }) => {
  await ingresarConConfiguracionDePartida(page);
  // Un artículo ya grabado: la alícuota no entra en su precio de venta, así que el recálculo no lo cambia (D-22).
  const previo = await crear(page.request, {
    codigoProveedor: 'RC-AC01-PREVIO',
    descripcion: 'Lente orgánico monofocal',
    precioCosto: 1210,
    margenUtilidad: 50,
  });
  expect(previo.precioVenta).toBe(1815);

  const cantidad = await grabarConRecalculo(page, async () => {
    await alicuota(page).selectOption('10,5');
  });

  // D-22: la configuración queda en 10,5 con múltiplo 0,01, ningún precio cambia y ninguna versión se incrementa.
  expect(cantidad).toBe(0);
  await expect(mensajeGrabada(page)).toHaveText(`${MENSAJE_GRABADA} Ningún precio de venta cambió.`);
  expect(await configuracionVigente(page.request)).toMatchObject({
    alicuotaIva: 10.5,
    condicionFiscal: 'responsable_inscripto',
    multiploRedondeo: 0.01,
  });
  expect(await consultar(page.request, previo.codigo)).toEqual(previo);

  // Después del cambio, el alta lee la configuración nueva: 1105 × (100 + 50) / 100 = 1657,50 (AC-01).
  const nuevo = await crear(page.request, {
    codigoProveedor: 'RC-AC01',
    descripcion: 'Lente bifocal',
    precioCosto: 1105,
    margenUtilidad: 50,
  });
  expect(nuevo.precioVenta).toBe(1657.5);
  expect((await consultar(page.request, nuevo.codigo)).precioVenta).toBe(1657.5);
});

test('multiplo 50 pasa 1.815,00 a 1.850,00 sin editar el articulo', async ({ page }) => {
  await ingresarConConfiguracionDePartida(page);
  const articulo = await crear(page.request, {
    codigoProveedor: 'RC-AC02',
    descripcion: 'Lente multifocal',
    precioCosto: 1210,
    margenUtilidad: 50,
  });
  expect(articulo.precioVenta).toBe(1815);

  const cantidad = await grabarConRecalculo(page, () => multiplo(page).fill('50'));

  // AC-02: el precio guardado pasa a 1.850,00 sin que nadie edite el artículo; los demás datos no cambian.
  expect(cantidad).toBeGreaterThanOrEqual(1);
  expect((await configuracionVigente(page.request)).multiploRedondeo).toBe(50);
  const guardado = await consultar(page.request, articulo.codigo);
  expect(guardado).toMatchObject({
    codigo: articulo.codigo,
    codigoProveedor: 'RC-AC02',
    descripcion: 'Lente multifocal',
    precioCosto: 1210,
    margenUtilidad: 50,
    precioVenta: 1850,
    activo: true,
  });
  // D-04: el recálculo le incrementa la versión, así una edición abierta con el precio anterior recibe 409.
  expect(guardado.version).toBe(articulo.version + 1);
});

test('el recalculo cambia tambien el precio de un articulo inactivo', async ({ page }) => {
  await ingresarConConfiguracionDePartida(page);
  const articulo = await crear(page.request, {
    codigoProveedor: 'RC-AC03',
    descripcion: 'Anteojo de sol polarizado',
    precioCosto: 1210,
    margenUtilidad: 50,
  });
  expect(articulo.precioVenta).toBe(1815);
  await desactivar(page.request, articulo.codigo);
  const inactivo = await consultar(page.request, articulo.codigo);
  expect(inactivo).toMatchObject({ activo: false, precioVenta: 1815 });

  const cantidad = await grabarConRecalculo(page, () => multiplo(page).fill('50'));

  // AC-03: el inactivo también se recalcula, y sigue inactivo.
  expect(cantidad).toBeGreaterThanOrEqual(1);
  const guardado = await consultar(page.request, articulo.codigo);
  expect(guardado).toMatchObject({
    codigo: articulo.codigo,
    precioCosto: 1210,
    margenUtilidad: 50,
    precioVenta: 1850,
    activo: false,
  });
  expect(guardado.version).toBe(inactivo.version + 1);
});

// Última prueba del archivo (D-24): deja un artículo que haría rechazar cualquier recálculo posterior con múltiplo 50.
test('multiplo 50 rechazado con el codigo del articulo y nada cambia', async ({ page }) => {
  await ingresarConConfiguracionDePartida(page);
  // Costo ImporteMaximo y margen 0: con múltiplo 0,01 el precio de venta es el máximo; con 50 lo superaría.
  const alta = await page.request.post('/api/articulos', {
    headers: { 'Content-Type': 'application/json' },
    data:
      '{"codigoProveedor":"RC-AC04","descripcion":"Armazón de titanio",' +
      `"precioCosto":${IMPORTE_MAXIMO},"margenUtilidad":0}`,
  });
  const textoAlta = await alta.text();
  expect(alta.status(), textoAlta).toBe(201);
  expect(textoAlta).toContain(`"precioCosto":${IMPORTE_MAXIMO}`);
  expect(textoAlta).toContain(`"precioVenta":${IMPORTE_MAXIMO}`);
  const codigo: number = JSON.parse(textoAlta).codigo;
  // Inactivo, para verificar la marca de D-25 y el agregado de D-26 de punta a punta.
  await desactivar(page.request, codigo);

  const configuracionAntes = await textoLeido(page.request, '/api/configuracion');
  const articuloAntes = await textoLeido(page.request, `/api/articulos/${codigo}`);
  expect(articuloAntes).toContain('"activo":false');
  const preciosAntes = await preciosDeLaBase(page.request);

  await abrirConfiguracion(page);
  await multiplo(page).fill('50');
  const { estado, texto } = await grabarConfiguracion(page);
  expect(estado, texto).toBe(400);

  // D-01, D-25 y D-26: el error, junto al múltiplo, dice que no se grabó, nombra el código autonumérico del artículo
  // con la marca de inactivo y agrega que hay que reactivarlo para editarlo.
  await rechazadoJuntoAlCampo(
    page,
    multiplo(page),
    `No se grabó el cambio: el precio de venta de 1 artículo sería demasiado grande (código ${codigo}, inactivo). ` +
      'Revisá esos artículos o el múltiplo; un artículo inactivo hay que reactivarlo para editarlo.',
  );
  await expect(mensajeGrabada(page)).toHaveCount(0);
  // Lo escrito queda para corregirlo.
  await expect(multiplo(page)).toHaveValue('50');

  // AC-04: ni la configuración ni ningún precio de venta cambió; leídos como texto, sin perder centavos.
  expect(await textoLeido(page.request, '/api/configuracion')).toBe(configuracionAntes);
  expect(await textoLeido(page.request, `/api/articulos/${codigo}`)).toBe(articuloAntes);
  expect(await preciosDeLaBase(page.request)).toEqual(preciosAntes);
  expect((await configuracionVigente(page.request)).multiploRedondeo).toBe(0.01);
});
