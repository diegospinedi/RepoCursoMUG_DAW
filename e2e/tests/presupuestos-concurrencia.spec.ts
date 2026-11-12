import { expect, test, type APIRequestContext, type Browser, type BrowserContext, type Page } from '@playwright/test';

/**
 * Dos sesiones que graban presupuestos a la vez (FEAT-003d: AC-01 en el navegador y, por correr en los proyectos
 * `chrome` y `msedge`, AC-02), de punta a punta contra la Api real que sirve la SPA compilada.
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva (D-05 de FEAT-001c). Cada
 * prueba abre dos contextos de navegador (`browser.newContext()`), uno por sesión: el primero ingresa (en la base nueva
 * define la contraseña, como `ingresar` en el resto de la suite) y recién después ingresa el segundo con la misma
 * contraseña, así cada contexto tiene su propia cookie. Las pruebas no dependen del orden: cada una deja por la Api la
 * configuración de partida (Responsable Inscripto, 21 y 0,01), crea sus artículos con códigos en el proveedor propios
 * ("PCC-…"), graba por la Api un presupuesto testigo para conocer el último número y compara los números de las dos
 * sesiones con ese testigo, sin esperar un número fijo.
 *
 * El encuentro forzado de las dos altas dentro de la base no existe en el navegador: la superposición real la prueban
 * las pruebas de integración y de la Api (Block 1 y Block 2 de la spec). Acá se prueba que la pantalla de cada sesión
 * responde bien cuando las dos pulsan "Grabar" a la vez.
 *
 * Datos personales: los clientes son ficticios, con DNI improbables (99.999.9xx), teléfonos de ceros y emails en
 * example.com. La base y todo lo grabado viven en el directorio temporal de la corrida, que servidor.mjs borra al
 * terminar.
 */

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-presupuestos-concurrencia-1';

const TITULO_LINEAS = 'Líneas del presupuesto';
const TEXTO_TOTAL = 'Total (IVA incluido)';

/** Textos de FEAT-003a (D-24 y la confirmación del alta), tal cual. */
const MENSAJE_DNI_INVALIDO = 'El DNI debe tener 7 u 8 dígitos';
const mensajeGrabado = (numero: number) => `Presupuesto N.º ${numero} grabado. Estado: Borrador.`;

/** Configuración de partida de cada prueba, como la pide el POST de la Api. */
const PARTIDA = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: null,
};

/** Datos del cliente como los escribe la operadora en la pantalla. */
interface Cliente {
  apellido: string;
  nombre: string;
  dni: string;
  domicilio: string;
  email: string;
  telefono: string;
}

/** Clientes ficticios, uno por sesión, con datos elegidos para que sea improbable que coincidan con los de alguien. */
const CLIENTE_A: Cliente = {
  apellido: 'Ficticio Uno',
  nombre: 'Cliente de Concurrencia',
  dni: '99.999.901',
  domicilio: 'Calle Inventada 101, Localidad de Prueba',
  email: 'cliente.uno@example.com',
  telefono: '00 0000-0001',
};
const CLIENTE_B: Cliente = {
  apellido: 'Ficticio Dos',
  nombre: 'Cliente de Concurrencia',
  dni: '99.999.902',
  domicilio: 'Calle Inventada 202, Localidad de Prueba',
  email: 'cliente.dos@example.com',
  telefono: '00 0000-0002',
};

/** El testigo que se graba por la Api: solo los obligatorios. */
const CLIENTE_TESTIGO = { apellido: 'Ficticio Testigo', nombre: 'Cliente de Concurrencia', dni: '99.999.900' };

/** El DNI como lo guarda y lo muestra la Api: solo los dígitos (FEAT-003a). */
const soloDigitos = (dni: string) => dni.replace(/\D/g, '');

/** Lo que devuelve la Api de un artículo grabado. */
interface Articulo {
  codigo: number;
  codigoProveedor: string;
  descripcion: string;
  precioVenta: number;
  activo: boolean;
}

/** Lo que devuelve la Api de un presupuesto grabado. */
interface PresupuestoGrabado {
  numero: number;
  fecha: string;
  estado: string;
  cliente: {
    apellido: string;
    nombre: string;
    dni: string;
    domicilio: string | null;
    email: string | null;
    telefono: string | null;
  };
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

/** Una sesión: su contexto, su página y las violaciones de CSP que vio su consola. */
interface Sesion {
  contexto: BrowserContext;
  page: Page;
  violacionesCsp: string[];
}

const campo = (page: Page, etiqueta: string) => page.getByLabel(etiqueta, { exact: true });
const tablaLineas = (page: Page) => page.getByRole('table', { name: TITULO_LINEAS });
const filasLineas = (page: Page) => tablaLineas(page).locator('tbody tr');
const celdaTotal = (page: Page) =>
  tablaLineas(page)
    .locator('tfoot tr')
    .filter({ has: page.getByRole('rowheader', { name: TEXTO_TOTAL, exact: true }) })
    .getByRole('cell')
    .first();
const botonGrabar = (page: Page) => page.getByRole('button', { name: 'Grabar', exact: true });
/**
 * Ninguna alerta en la pantalla: ni el mensaje de error general ni un mensaje junto a un campo. En el presupuesto
 * grabado el rol alert solo lo usan los errores, así que una pantalla confirmada no debe tener ninguno.
 */
async function sinAlertas(page: Page) {
  await expect(page.getByRole('alert')).toHaveCount(0);
}

const escaparRegex = (texto: string) => texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

const dato = (page: Page, etiqueta: string) =>
  page
    .locator('dl.datos > div')
    .filter({ has: page.locator('dt', { hasText: new RegExp(`^${escaparRegex(etiqueta)}$`) }) })
    .locator('dd');

const esAlta = (url: string) => new URL(url).pathname === '/api/presupuestos';
const esBusqueda = (url: string) => new URL(url).pathname === '/api/articulos';

/**
 * Abre una sesión en un contexto nuevo del navegador, con la dirección de la Api del proyecto. Registra en su consola
 * las violaciones de Content-Security-Policy para verificarlas al cerrar.
 */
async function abrirSesion(browser: Browser, baseURL: string | undefined): Promise<Sesion> {
  expect(baseURL, 'El proyecto no definió baseURL').toBeTruthy();
  const contexto = await browser.newContext({ baseURL, ignoreHTTPSErrors: true });
  const page = await contexto.newPage();
  const violacionesCsp: string[] = [];
  page.on('console', (mensaje) => {
    if (/Content Security Policy/i.test(mensaje.text())) {
      violacionesCsp.push(mensaje.text());
    }
  });
  return { contexto, page, violacionesCsp };
}

/**
 * Cierra los contextos abiertos y verifica que ninguna página violó Content-Security-Policy. La verificación es blanda:
 * si la prueba ya falló, una violación no tapa el error original.
 */
async function cerrarSesiones(sesiones: Sesion[]) {
  await Promise.allSettled(sesiones.map((s) => s.contexto.close()));
  for (const sesion of sesiones) {
    expect.soft(sesion.violacionesCsp, 'La SPA no debe violar Content-Security-Policy').toEqual([]);
  }
}

/**
 * Entra como la operadora: en la base nueva define la contraseña; después ingresa con ella. Termina en el inicio con
 * la sesión iniciada.
 */
async function ingresar(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/(definir-contrasena|ingreso)$/);
  await campo(page, 'Contraseña').fill(CONTRASENA);
  if (/\/definir-contrasena$/.test(page.url())) {
    await page.getByRole('button', { name: 'Definir contraseña' }).click();
  } else {
    await page.getByRole('button', { name: 'Ingresar' }).click();
  }
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
}

/** Deja por la Api Responsable Inscripto, alícuota 21 y múltiplo 0,01. */
async function fijarConfiguracion(request: APIRequestContext) {
  const respuesta = await request.post('/api/configuracion', { data: PARTIDA });
  expect(respuesta.status(), 'No se pudo fijar la configuración de partida').toBe(200);
}

/**
 * El código en el proveedor es único en la base: con `--repeat-each` la misma prueba corre varias veces sobre la base
 * del proyecto, así que cada repetición lleva su índice ("PCC-AC01-A-R0", "PCC-AC01-A-R1"…). La descripción sale del
 * código, así que la búsqueda de la pantalla también encuentra solo el de esta repetición.
 */
const codigoPropio = (base: string) => `${base}-R${test.info().repeatEachIndex}`;

/** Graba por la Api un artículo con ese costo y margen 0; devuelve lo que respondió. */
async function crearArticulo(request: APIRequestContext, codigoProveedor: string, precioCosto: number): Promise<Articulo> {
  const respuesta = await request.post('/api/articulos', {
    data: {
      codigoProveedor,
      descripcion: `Artículo de concurrencia ${codigoProveedor}`,
      precioCosto,
      margenUtilidad: 0,
    },
  });
  expect(respuesta.status(), `No se pudo crear ${codigoProveedor}`).toBe(201);
  const creado: Articulo = await respuesta.json();
  expect(creado.activo).toBe(true);
  return creado;
}

/** Graba por la Api un presupuesto testigo en Borrador y devuelve su número: el último grabado antes de la prueba. */
async function grabarTestigo(request: APIRequestContext, codigoArticulo: number): Promise<number> {
  const respuesta = await request.post('/api/presupuestos', {
    data: { cliente: CLIENTE_TESTIGO, lineas: [{ codigoArticulo, cantidad: 1, porcentajeDescuento: 0 }] },
  });
  expect(respuesta.status(), 'No se pudo grabar el presupuesto testigo por la Api').toBe(201);
  const grabado: PresupuestoGrabado = await respuesta.json();
  return grabado.numero;
}

/** El presupuesto tal como está grabado, pedido con la sesión de la página. */
async function consultarPresupuesto(request: APIRequestContext, numero: number): Promise<PresupuestoGrabado> {
  const respuesta = await request.get(`/api/presupuestos/${numero}`);
  expect(respuesta.status(), `GET /api/presupuestos/${numero}`).toBe(200);
  return respuesta.json();
}

/** Abre el alta desde el inicio, como la operadora. */
async function abrirNuevoPresupuesto(page: Page) {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await page.getByRole('link', { name: 'Nuevo presupuesto' }).click();
  await expect(page).toHaveURL(/\/presupuestos\/nuevo$/);
  await expect(page.getByRole('heading', { name: 'Nuevo presupuesto' })).toBeVisible();
}

/** Escribe los datos del cliente en la pantalla de alta. */
async function cargarCliente(page: Page, cliente: Cliente) {
  await campo(page, 'Apellido').fill(cliente.apellido);
  await campo(page, 'Nombre').fill(cliente.nombre);
  await campo(page, 'DNI').fill(cliente.dni);
  await campo(page, 'Domicilio').fill(cliente.domicilio);
  await campo(page, 'Email').fill(cliente.email);
  await campo(page, 'Nro. de Teléfono').fill(cliente.telefono);
}

/** Busca el artículo por su descripción en la pantalla y lo agrega como línea. */
async function agregar(page: Page, articulo: Articulo) {
  await campo(page, 'Texto a buscar').fill(articulo.descripcion);
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'GET' && esBusqueda(r.url())),
    page.getByRole('button', { name: 'Buscar', exact: true }).click(),
  ]);
  expect(respuesta.status()).toBe(200);
  await page.getByRole('button', { name: `Agregar ${articulo.codigo} ${articulo.descripcion}`, exact: true }).click();
  await expect(filasLineas(page)).toHaveCount(1);
}

/** Abre el alta, carga el cliente y una línea del artículo, sin grabar. */
async function prepararAlta(page: Page, cliente: Cliente, articulo: Articulo) {
  await abrirNuevoPresupuesto(page);
  await cargarCliente(page, cliente);
  await agregar(page, articulo);
}

/** Espera el POST de alta de la página; se crea antes del clic para no perder la respuesta. */
const esperarAlta = (page: Page) =>
  page.waitForResponse((r) => r.request().method() === 'POST' && esAlta(r.url()));

/** Cuenta los POST de alta que salen de la página desde ahora. */
function contarAltas(page: Page) {
  let altas = 0;
  page.on('request', (pedido) => {
    if (pedido.method() === 'POST' && esAlta(pedido.url())) {
      altas++;
    }
  });
  return () => altas;
}

/** Después del alta, la pantalla muestra el presupuesto grabado con la confirmación y sin error general. */
async function confirmadoEnPantalla(page: Page, numero: number) {
  await expect(page).toHaveURL(new RegExp(`/presupuestos/${numero}$`));
  await expect(page.getByRole('status').filter({ hasText: 'Presupuesto N.º' })).toHaveText(mensajeGrabado(numero));
  await expect(dato(page, 'Número')).toHaveText(String(numero));
  await sinAlertas(page);
}

/** Vuelve a abrir el presupuesto con su dirección y verifica los datos del cliente y la línea grabada. */
async function reabrirYVerificar(page: Page, numero: number, cliente: Cliente, articulo: Articulo, precio: string) {
  await page.goto(`/presupuestos/${numero}`);
  await expect(page.getByRole('heading', { name: 'Presupuesto', exact: true })).toBeVisible();
  await expect(dato(page, 'Número')).toHaveText(String(numero));
  await expect(dato(page, 'Estado')).toHaveText('Borrador');
  await expect(dato(page, 'Apellido')).toHaveText(cliente.apellido);
  await expect(dato(page, 'Nombre')).toHaveText(cliente.nombre);
  await expect(dato(page, 'DNI')).toHaveText(soloDigitos(cliente.dni));
  await expect(dato(page, 'Domicilio')).toHaveText(cliente.domicilio);
  await expect(dato(page, 'Email')).toHaveText(cliente.email);
  await expect(filasLineas(page)).toHaveCount(1);
  await expect(filasLineas(page).first().getByRole('cell')).toHaveText([
    String(articulo.codigo),
    articulo.descripcion,
    precio,
    '1',
    '0',
    precio,
    precio,
  ]);
  await expect(celdaTotal(page)).toHaveText(precio);
  await sinAlertas(page);
}

test('AC-01: dos sesiones graban a la vez con números consecutivos', async ({ browser, baseURL }) => {
  const sesiones: Sesion[] = [];
  try {
    // La primera sesión ingresa (en la base nueva define la contraseña) y recién después la segunda, con la misma.
    const a = await abrirSesion(browser, baseURL);
    sesiones.push(a);
    await ingresar(a.page);
    const b = await abrirSesion(browser, baseURL);
    sesiones.push(b);
    await ingresar(b.page);

    await fijarConfiguracion(a.page.request);
    const articuloA = await crearArticulo(a.page.request, codigoPropio('PCC-AC01-A'), 1000);
    const articuloB = await crearArticulo(b.page.request, codigoPropio('PCC-AC01-B'), 500);
    const testigo = await grabarTestigo(a.page.request, articuloA.codigo);

    await prepararAlta(a.page, CLIENTE_A, articuloA);
    await prepararAlta(b.page, CLIENTE_B, articuloB);

    // Las dos sesiones pulsan Grabar a la vez.
    const altaA = esperarAlta(a.page);
    const altaB = esperarAlta(b.page);
    await Promise.all([botonGrabar(a.page).click(), botonGrabar(b.page).click()]);
    const [respuestaA, respuestaB] = await Promise.all([altaA, altaB]);
    expect(respuestaA.status(), 'El alta de la sesión A no respondió 201').toBe(201);
    expect(respuestaB.status(), 'El alta de la sesión B no respondió 201').toBe(201);
    const grabadoA: PresupuestoGrabado = await respuestaA.json();
    const grabadoB: PresupuestoGrabado = await respuestaB.json();

    // AC-01: números distintos y consecutivos, los dos siguientes al último grabado.
    expect(grabadoA.numero).not.toBe(grabadoB.numero);
    expect([grabadoA.numero, grabadoB.numero].sort((x, y) => x - y)).toEqual([testigo + 1, testigo + 2]);

    // Cada pantalla muestra su confirmación y ninguna el error general.
    await confirmadoEnPantalla(a.page, grabadoA.numero);
    await confirmadoEnPantalla(b.page, grabadoB.numero);

    // Al reabrir, cada presupuesto tiene sus datos.
    await reabrirYVerificar(a.page, grabadoA.numero, CLIENTE_A, articuloA, '1.000,00');
    await reabrirYVerificar(b.page, grabadoB.numero, CLIENTE_B, articuloB, '500,00');
    const guardadoA = await consultarPresupuesto(a.page.request, grabadoA.numero);
    const guardadoB = await consultarPresupuesto(b.page.request, grabadoB.numero);
    expect(guardadoA.cliente.dni).toBe(soloDigitos(CLIENTE_A.dni));
    expect(guardadoA.lineas.map((l) => l.codigoArticulo)).toEqual([articuloA.codigo]);
    expect(guardadoB.cliente.dni).toBe(soloDigitos(CLIENTE_B.dni));
    expect(guardadoB.lineas.map((l) => l.codigoArticulo)).toEqual([articuloB.codigo]);
    expect((await a.page.request.get(`/api/presupuestos/${testigo + 3}`)).status()).toBe(404);
  } finally {
    await cerrarSesiones(sesiones);
  }
});

test('AC-01: un alta inválida en una sesión no saltea el número de la otra', async ({ browser, baseURL }) => {
  const sesiones: Sesion[] = [];
  try {
    const a = await abrirSesion(browser, baseURL);
    sesiones.push(a);
    await ingresar(a.page);
    const b = await abrirSesion(browser, baseURL);
    sesiones.push(b);
    await ingresar(b.page);

    await fijarConfiguracion(a.page.request);
    const articuloA = await crearArticulo(a.page.request, codigoPropio('PCC-INV-A'), 1000);
    const articuloB = await crearArticulo(b.page.request, codigoPropio('PCC-INV-B'), 500);
    const testigo = await grabarTestigo(a.page.request, articuloA.codigo);

    // La sesión A carga un DNI de 6 dígitos; la B, un cliente válido.
    const dniInvalido = '999999';
    await prepararAlta(a.page, { ...CLIENTE_A, dni: dniInvalido }, articuloA);
    await prepararAlta(b.page, CLIENTE_B, articuloB);
    const altasA = contarAltas(a.page);

    const altaB = esperarAlta(b.page);
    await Promise.all([botonGrabar(a.page).click(), botonGrabar(b.page).click()]);
    const respuestaB = await altaB;
    expect(respuestaB.status(), 'El alta de la sesión B no respondió 201').toBe(201);
    const grabadoB: PresupuestoGrabado = await respuestaB.json();

    // La pantalla de A muestra el mensaje junto al DNI, sigue en el alta y no envió nada.
    const dniA = campo(a.page, 'DNI');
    await expect(dniA).toHaveAttribute('aria-invalid', 'true');
    await expect(dniA).toHaveAttribute('aria-describedby', /.+/);
    const idMensaje = await dniA.getAttribute('aria-describedby');
    await expect(a.page.locator(`[id="${idMensaje}"]`)).toHaveText(MENSAJE_DNI_INVALIDO);
    await expect(dniA).toHaveValue(dniInvalido);
    await expect(a.page).toHaveURL(/\/presupuestos\/nuevo$/);
    expect(altasA(), 'La pantalla con el DNI inválido envió un alta').toBe(0);

    // La B recibe el número siguiente al último, sin error general.
    expect(grabadoB.numero).toBe(testigo + 1);
    await confirmadoEnPantalla(b.page, grabadoB.numero);
    expect((await b.page.request.get(`/api/presupuestos/${testigo + 2}`)).status()).toBe(404);

    // A corrige el DNI y graba: recibe el que sigue, sin hueco en la numeración.
    await dniA.fill(CLIENTE_A.dni);
    const altaA = esperarAlta(a.page);
    await botonGrabar(a.page).click();
    const respuestaA = await altaA;
    expect(respuestaA.status(), 'El alta corregida de la sesión A no respondió 201').toBe(201);
    const grabadoA: PresupuestoGrabado = await respuestaA.json();
    expect(grabadoA.numero).toBe(testigo + 2);
    await confirmadoEnPantalla(a.page, grabadoA.numero);

    await reabrirYVerificar(a.page, grabadoA.numero, CLIENTE_A, articuloA, '1.000,00');
    await reabrirYVerificar(b.page, grabadoB.numero, CLIENTE_B, articuloB, '500,00');
  } finally {
    await cerrarSesiones(sesiones);
  }
});
