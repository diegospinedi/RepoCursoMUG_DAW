import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test';

/**
 * Cantidad, descuento y cálculos de las líneas y el total (FEAT-003b: AC-01, AC-02, AC-03, AC-07, AC-08 y AC-09) de
 * punta a punta, contra la Api real que sirve la SPA compilada, en Chrome y Edge (NFR-01).
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva (D-05 de FEAT-001c). La
 * primera prueba que corre define la contraseña de esa base y las demás ingresan con ella (`ingresar` resuelve los dos
 * casos), así que las pruebas no dependen del orden. Cada prueba deja por la Api Responsable Inscripto, alícuota 21 y
 * múltiplo 0,01 y crea sus propios artículos por la Api con la sesión de la página, con códigos en el proveedor propios
 * ("PC-…"). Ninguna espera un número de presupuesto fijo: lo leen de la respuesta del alta. La base y todo lo grabado
 * viven en el directorio temporal de la corrida, que servidor.mjs borra al terminar (los presupuestos no se pueden
 * eliminar desde la Api). Ninguna prueba toca un presupuesto en Final: todo lo que se graba queda en Borrador.
 *
 * Los números se escriben como los escribiría la operadora (coma decimal) y los importes se leen como los muestra la
 * pantalla ("2.700,00"), con el símbolo en el encabezado de la columna (D-34 de FEAT-003a).
 *
 * Datos personales: el cliente es ficticio, con un DNI que no puede ser de nadie (99.999.999).
 */

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-presupuestos-calculos-1';

const TITULO_LINEAS = 'Líneas del presupuesto';
const TEXTO_TOTAL = 'Total (IVA incluido)';

/** Textos del PRD (AC-02, AC-03, AC-09), tal cual. */
const MENSAJE_CANTIDAD_INVALIDA = 'La cantidad debe ser un número entero mayor a 0';
const MENSAJE_CANTIDAD_MAXIMA = 'Usá hasta 9.999 unidades.';
const MENSAJE_DESCUENTO_INVALIDO = 'El descuento debe estar entre 0 y 100';

/** Lo que muestra la pantalla de alta en un importe que no se puede calcular (D-26). */
const SIN_IMPORTE = '—';

/** Configuración de partida de cada prueba, como la pide el POST de la Api. */
const PARTIDA = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: null,
};

/** Un cliente ficticio: ningún dato es de una persona real. */
const CLIENTE = {
  apellido: 'Ficticio',
  nombre: 'Cliente de Cálculos',
  dni: '99.999.999',
};

/** Lo que devuelve la Api de un artículo grabado. */
interface Articulo {
  codigo: number;
  codigoProveedor: string;
  descripcion: string;
  precioVenta: number;
  activo: boolean;
}

/** Una línea como la envía la pantalla (y como se graba por la Api para preparar AC-08). */
interface LineaAGrabar {
  codigoArticulo: number;
  cantidad: number;
  porcentajeDescuento: number;
}

/** Lo que devuelve la Api de un presupuesto grabado. */
interface PresupuestoGrabado {
  numero: number;
  estado: string;
  lineas: {
    codigoArticulo: number;
    descripcion: string;
    precioUnitario: number;
    cantidad: number;
    porcentajeDescuento: number;
    precioConDescuento: number;
    precioFinal: number;
  }[];
  total: number;
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

const tablaLineas = (page: Page) => page.getByRole('table', { name: TITULO_LINEAS });
const filasLineas = (page: Page) => tablaLineas(page).locator('tbody tr');
/** La celda del total: la de Precio Final en la fila "Total (IVA incluido)" del pie (D-10). */
const celdaTotal = (page: Page) =>
  tablaLineas(page)
    .locator('tfoot tr')
    .filter({ has: page.getByRole('rowheader', { name: TEXTO_TOTAL, exact: true }) })
    .getByRole('cell')
    .first();
const cantidad = (page: Page, n: number) => page.getByLabel(`Cantidad de la línea ${n}`, { exact: true });
const descuento = (page: Page, n: number) => page.getByLabel(`Porcentaje de descuento de la línea ${n}`, { exact: true });
const botonGrabar = (page: Page) => page.getByRole('button', { name: 'Grabar', exact: true });

const esAlta = (url: string) => new URL(url).pathname === '/api/presupuestos';
const esBusqueda = (url: string) => new URL(url).pathname === '/api/articulos';

/**
 * Las celdas de una fila de la tabla de líneas en la pantalla de alta: Precio con Descuento y Precio Final (columnas
 * 6 y 7; las anteriores son el código, la descripción y los tres campos).
 */
const precioConDescuento = (page: Page, n: number) => filasLineas(page).nth(n - 1).getByRole('cell').nth(5);
const precioFinal = (page: Page, n: number) => filasLineas(page).nth(n - 1).getByRole('cell').nth(6);

/**
 * Entra como la operadora: en la base nueva define la contraseña; después ingresa con ella. Termina en el inicio con
 * la sesión iniciada.
 */
async function ingresar(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/(definir-contrasena|ingreso)$/);
  await page.getByLabel('Contraseña', { exact: true }).fill(CONTRASENA);
  if (/\/definir-contrasena$/.test(page.url())) {
    await page.getByRole('button', { name: 'Definir contraseña' }).click();
  } else {
    await page.getByRole('button', { name: 'Ingresar' }).click();
  }
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
}

/** Ingresa y deja por la Api Responsable Inscripto, alícuota 21 y múltiplo 0,01. */
async function ingresarConConfiguracion(page: Page) {
  await ingresar(page);
  const respuesta = await page.request.post('/api/configuracion', { data: PARTIDA });
  expect(respuesta.status(), 'No se pudo fijar la configuración de partida').toBe(200);
}

/** Graba por la Api un artículo de costo `precio` y margen 0 y verifica que su precio de venta es `precio`. */
async function crearArticulo(request: APIRequestContext, codigoProveedor: string, precio: number): Promise<Articulo> {
  const respuesta = await request.post('/api/articulos', {
    data: { codigoProveedor, descripcion: `Artículo de cálculo ${codigoProveedor}`, precioCosto: precio, margenUtilidad: 0 },
  });
  expect(respuesta.status(), `No se pudo crear ${codigoProveedor}`).toBe(201);
  const creado: Articulo = await respuesta.json();
  expect(creado.activo).toBe(true);
  expect(creado.precioVenta).toBe(precio);
  return creado;
}

/** Graba un presupuesto por la Api con el cliente ficticio y esas líneas; devuelve lo que respondió. */
async function grabarPorApi(request: APIRequestContext, lineas: LineaAGrabar[]): Promise<PresupuestoGrabado> {
  const respuesta = await request.post('/api/presupuestos', { data: { cliente: CLIENTE, lineas } });
  expect(respuesta.status(), 'No se pudo grabar el presupuesto por la Api').toBe(201);
  return respuesta.json();
}

/** El presupuesto tal como está grabado, pedido con la sesión de la página. */
async function consultarPresupuesto(request: APIRequestContext, numero: number): Promise<PresupuestoGrabado> {
  const respuesta = await request.get(`/api/presupuestos/${numero}`);
  expect(respuesta.status(), `GET /api/presupuestos/${numero}`).toBe(200);
  return respuesta.json();
}

/** Abre el alta desde el inicio, como la operadora, y carga el cliente ficticio. */
async function abrirNuevoPresupuesto(page: Page) {
  await page.getByRole('link', { name: 'Nuevo presupuesto' }).click();
  await expect(page).toHaveURL(/\/presupuestos\/nuevo$/);
  await page.getByLabel('Apellido', { exact: true }).fill(CLIENTE.apellido);
  await page.getByLabel('Nombre', { exact: true }).fill(CLIENTE.nombre);
  await page.getByLabel('DNI', { exact: true }).fill(CLIENTE.dni);
}

/** Busca el artículo por su descripción y lo agrega como línea. */
async function agregar(page: Page, articulo: Articulo) {
  await page.getByLabel('Texto a buscar', { exact: true }).fill(articulo.descripcion);
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'GET' && esBusqueda(r.url())),
    page.getByRole('button', { name: 'Buscar', exact: true }).click(),
  ]);
  expect(respuesta.status()).toBe(200);
  await page.getByRole('button', { name: `Agregar ${articulo.codigo} ${articulo.descripcion}`, exact: true }).click();
}

/** Pulsa Grabar, espera el alta y el presupuesto grabado en pantalla; devuelve lo que respondió la Api. */
async function grabarDesdePantalla(page: Page): Promise<PresupuestoGrabado> {
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'POST' && esAlta(r.url())),
    botonGrabar(page).click(),
  ]);
  expect(respuesta.status(), 'El alta no respondió 201').toBe(201);
  const grabado: PresupuestoGrabado = await respuesta.json();
  await expect(page).toHaveURL(new RegExp(`/presupuestos/${grabado.numero}$`));
  await expect(page.getByRole('heading', { name: 'Presupuesto', exact: true })).toBeVisible();
  return grabado;
}

/** Abre el presupuesto grabado con su dirección, como al volver a consultarlo. */
async function abrirGrabado(page: Page, numero: number) {
  await page.goto(`/presupuestos/${numero}`);
  await expect(page.getByRole('heading', { name: 'Presupuesto', exact: true })).toBeVisible();
  await expect(filasLineas(page).first()).toBeVisible();
}

/**
 * Prepara una prueba de rechazo: graba por la Api un presupuesto testigo (así se sabe cuál es el último número), abre
 * el alta con el cliente y una línea del artículo, y empieza a contar los POST de alta. `nadaGrabado` verifica que la
 * pantalla no envió ningún alta, que sigue en el alta y que no existe un presupuesto después del testigo.
 */
async function prepararRechazo(page: Page, prueba: string) {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, `PC-${prueba}-1000`, 1000);
  const testigo = await grabarPorApi(page.request, [
    { codigoArticulo: articulo.codigo, cantidad: 1, porcentajeDescuento: 0 },
  ]);
  await abrirNuevoPresupuesto(page);
  await agregar(page, articulo);
  let altas = 0;
  page.on('request', (pedido) => {
    if (pedido.method() === 'POST' && esAlta(pedido.url())) {
      altas++;
    }
  });
  const nadaGrabado = async () => {
    expect(altas, 'La pantalla envió un alta').toBe(0);
    await expect(page).toHaveURL(/\/presupuestos\/nuevo$/);
    const siguiente = await page.request.get(`/api/presupuestos/${testigo.numero + 1}`);
    expect(siguiente.status(), 'Se grabó un presupuesto').toBe(404);
  };
  return { nadaGrabado };
}

/**
 * El campo quedó marcado inválido y su mensaje, vinculado por aria-describedby, es `mensaje`, anunciado sin
 * interrumpir mientras se escribe (aria-live="polite", D-26).
 */
async function rechazadoJuntoAlCampo(page: Page, campo: Locator, mensaje: string) {
  await expect(campo).toHaveAttribute('aria-invalid', 'true');
  await expect(campo).toHaveAttribute('aria-describedby', /.+/);
  const idMensaje = await campo.getAttribute('aria-describedby');
  const texto = page.locator(`[id="${idMensaje}"]`);
  await expect(texto).toHaveText(mensaje);
  await expect(page.locator('[aria-live="polite"]').filter({ has: texto })).toHaveCount(1);
}

/** El campo no está marcado inválido. */
async function sinError(campo: Locator) {
  await expect(campo).toHaveAttribute('aria-invalid', 'false');
}

test('AC-01: cantidad 3 y 10 % dan 900,00 y 2.700,00', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, 'PC-AC01-1000', 1000);

  await abrirNuevoPresupuesto(page);
  await agregar(page, articulo);
  // Al agregarla, Cantidad 1 y Descuento 0.
  await expect(cantidad(page, 1)).toHaveValue('1');
  await expect(descuento(page, 1)).toHaveValue('0');
  await expect(precioConDescuento(page, 1)).toHaveText('1.000,00');
  await expect(precioFinal(page, 1)).toHaveText('1.000,00');
  await expect(celdaTotal(page)).toHaveText('1.000,00');

  // AC-01 (FR-07): cada cambio recalcula la fila y el total en pantalla, sin grabar.
  await cantidad(page, 1).fill('3');
  await expect(precioConDescuento(page, 1)).toHaveText('1.000,00');
  await expect(precioFinal(page, 1)).toHaveText('3.000,00');
  await expect(celdaTotal(page)).toHaveText('3.000,00');
  await descuento(page, 1).fill('10');
  await expect(precioConDescuento(page, 1)).toHaveText('900,00');
  await expect(precioFinal(page, 1)).toHaveText('2.700,00');
  await expect(celdaTotal(page)).toHaveText('2.700,00');
  await sinError(cantidad(page, 1));
  await sinError(descuento(page, 1));

  // Se graba lo cargado, con los importes que calcula el servidor (ADR-016).
  const grabado = await grabarDesdePantalla(page);
  const guardado = await consultarPresupuesto(page.request, grabado.numero);
  expect(guardado.lineas).toEqual([
    {
      codigoArticulo: articulo.codigo,
      descripcion: articulo.descripcion,
      precioUnitario: 1000,
      cantidad: 3,
      porcentajeDescuento: 10,
      precioConDescuento: 900,
      precioFinal: 2700,
    },
  ]);
  expect(guardado.total).toBe(2700);
  await expect(filasLineas(page).first().getByRole('cell')).toHaveText([
    String(articulo.codigo),
    articulo.descripcion,
    '1.000,00',
    '3',
    '10',
    '900,00',
    '2.700,00',
  ]);
});

test('AC-02: cantidad 0, -1 y 2,5 rechazada junto al campo', async ({ page }) => {
  const { nadaGrabado } = await prepararRechazo(page, 'AC02');

  for (const invalida of ['0', '-1', '2,5']) {
    await cantidad(page, 1).fill(invalida);

    // AC-02: el mensaje del PRD junto al campo; la fila y el total no se calculan con un valor no aceptado.
    await rechazadoJuntoAlCampo(page, cantidad(page, 1), MENSAJE_CANTIDAD_INVALIDA);
    await expect(precioConDescuento(page, 1)).toHaveText(SIN_IMPORTE);
    await expect(precioFinal(page, 1)).toHaveText(SIN_IMPORTE);
    await expect(celdaTotal(page)).toHaveText(SIN_IMPORTE);
    await sinError(descuento(page, 1));

    // Grabar no envía y lleva el foco al campo inválido (D-26), que conserva lo escrito.
    await botonGrabar(page).click();
    await expect(cantidad(page, 1)).toBeFocused();
    await rechazadoJuntoAlCampo(page, cantidad(page, 1), MENSAJE_CANTIDAD_INVALIDA);
    await expect(cantidad(page, 1)).toHaveValue(invalida);
  }
  await nadaGrabado();

  // Corregida, el mensaje se va y la fila se vuelve a calcular.
  await cantidad(page, 1).fill('2');
  await sinError(cantidad(page, 1));
  await expect(precioFinal(page, 1)).toHaveText('2.000,00');
  await expect(celdaTotal(page)).toHaveText('2.000,00');
});

test('AC-09: cantidad 10.000 rechazada junto al campo', async ({ page }) => {
  const { nadaGrabado } = await prepararRechazo(page, 'AC09');

  // 9.999 es la máxima que se acepta (D-01).
  await cantidad(page, 1).fill('9999');
  await sinError(cantidad(page, 1));
  await expect(precioFinal(page, 1)).toHaveText('9.999.000,00');

  // AC-09: la operadora escribe 10.000, con punto de miles (D-29).
  await cantidad(page, 1).fill('10.000');
  await rechazadoJuntoAlCampo(page, cantidad(page, 1), MENSAJE_CANTIDAD_MAXIMA);
  await expect(precioFinal(page, 1)).toHaveText(SIN_IMPORTE);
  await expect(celdaTotal(page)).toHaveText(SIN_IMPORTE);

  await botonGrabar(page).click();
  await expect(cantidad(page, 1)).toBeFocused();
  await rechazadoJuntoAlCampo(page, cantidad(page, 1), MENSAJE_CANTIDAD_MAXIMA);
  await nadaGrabado();
});

test('AC-03: descuento -1 y 100,01 rechazado junto al campo', async ({ page }) => {
  const { nadaGrabado } = await prepararRechazo(page, 'AC03');

  for (const invalido of ['-1', '100,01']) {
    await descuento(page, 1).fill(invalido);

    // AC-03: el mensaje del PRD junto al campo; la fila y el total no se calculan.
    await rechazadoJuntoAlCampo(page, descuento(page, 1), MENSAJE_DESCUENTO_INVALIDO);
    await expect(precioConDescuento(page, 1)).toHaveText(SIN_IMPORTE);
    await expect(precioFinal(page, 1)).toHaveText(SIN_IMPORTE);
    await expect(celdaTotal(page)).toHaveText(SIN_IMPORTE);
    await sinError(cantidad(page, 1));

    await botonGrabar(page).click();
    await expect(descuento(page, 1)).toBeFocused();
    await rechazadoJuntoAlCampo(page, descuento(page, 1), MENSAJE_DESCUENTO_INVALIDO);
    await expect(descuento(page, 1)).toHaveValue(invalido);
  }
  await nadaGrabado();

  // Los extremos sí se aceptan: 100 deja la línea en 0,00 (AC-04).
  await descuento(page, 1).fill('100');
  await sinError(descuento(page, 1));
  await expect(precioConDescuento(page, 1)).toHaveText('0,00');
  await expect(celdaTotal(page)).toHaveText('0,00');
});

test('AC-07: total de dos líneas 2.710,02', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const barato = await crearArticulo(page.request, 'PC-AC07-667', 6.67);
  const caro = await crearArticulo(page.request, 'PC-AC07-1000', 1000);

  await abrirNuevoPresupuesto(page);
  // La única línea, calculada como en AC-06: 6,67 con 50 % da 3,34 (mitad hacia arriba) y por 3, 10,02.
  await agregar(page, barato);
  await cantidad(page, 1).fill('3');
  await descuento(page, 1).fill('50');
  await expect(precioConDescuento(page, 1)).toHaveText('3,34');
  await expect(precioFinal(page, 1)).toHaveText('10,02');
  await expect(celdaTotal(page)).toHaveText('10,02');

  // AC-07: se agrega una línea de Precio Final 2.700,00 y el total es la suma de los dos Precios Finales.
  await agregar(page, caro);
  await cantidad(page, 2).fill('3');
  await descuento(page, 2).fill('10');
  await expect(precioFinal(page, 2)).toHaveText('2.700,00');
  await expect(precioFinal(page, 1)).toHaveText('10,02');
  await expect(celdaTotal(page)).toHaveText('2.710,02');

  // Lo grabado coincide con lo que se vio (FR-06, NFR-02).
  const grabado = await grabarDesdePantalla(page);
  const guardado = await consultarPresupuesto(page.request, grabado.numero);
  expect(guardado.lineas.map(({ precioConDescuento: pcd, precioFinal: pf }) => [pcd, pf])).toEqual([
    [3.34, 10.02],
    [900, 2700],
  ]);
  expect(guardado.total).toBe(2710.02);
  expect(grabado.total).toBe(2710.02);
});

test('AC-08: el presupuesto grabado muestra el total con IVA incluido', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const barato = await crearArticulo(page.request, 'PC-AC08-667', 6.67);
  const caro = await crearArticulo(page.request, 'PC-AC08-1000', 1000);
  // Se graba por la Api, en Borrador, como lo grabaría la pantalla.
  const grabado = await grabarPorApi(page.request, [
    { codigoArticulo: barato.codigo, cantidad: 3, porcentajeDescuento: 50 },
    { codigoArticulo: caro.codigo, cantidad: 3, porcentajeDescuento: 10 },
  ]);
  expect(grabado.estado).toBe('borrador');
  expect(grabado.total).toBe(2710.02);

  await abrirGrabado(page, grabado.numero);

  // AC-08: las líneas con sus importes finales al consumidor (los precios de venta del catálogo, con IVA incluido)…
  await expect(filasLineas(page)).toHaveCount(2);
  await expect(filasLineas(page).nth(0).getByRole('cell')).toHaveText([
    String(barato.codigo),
    barato.descripcion,
    '6,67',
    '3',
    '50',
    '3,34',
    '10,02',
  ]);
  await expect(filasLineas(page).nth(1).getByRole('cell')).toHaveText([
    String(caro.codigo),
    caro.descripcion,
    '1.000,00',
    '3',
    '10',
    '900,00',
    '2.700,00',
  ]);
  // …y un solo total, el del servidor, con IVA incluido: ni neto ni IVA discriminados en ninguna parte.
  await expect(tablaLineas(page).locator('tfoot tr')).toHaveCount(1);
  await expect(celdaTotal(page)).toHaveText('2.710,02');
  await expect(page.getByText(/IVA/)).toHaveCount(1);
  await expect(page.getByText(/IVA/)).toHaveText(TEXTO_TOTAL);
  await expect(page.getByText(/neto|subtotal/i)).toHaveCount(0);
});
