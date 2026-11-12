import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

/**
 * Edición en Borrador y paso a Final irreversible (FEAT-003c: AC-01 a AC-10) de punta a punta, contra la Api real que
 * sirve la SPA compilada, en Chrome y Edge (NFR-01).
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva (D-05 de FEAT-001c). La
 * primera prueba que corre define la contraseña de esa base y las demás ingresan con ella (`ingresar` resuelve los dos
 * casos), así que las pruebas no dependen del orden. Cada prueba deja por la Api Responsable Inscripto, alícuota 21 y
 * múltiplo 0,01, crea sus propios artículos por la Api con la sesión de la página, con códigos en el proveedor propios
 * ("PE-…"), y graba sus presupuestos por la Api en Borrador. Un presupuesto en Final se prepara como en la app: se graba
 * en Borrador y después pasa a Final (AGENTS.md). Ninguna prueba espera un número de presupuesto fijo: lo leen de la
 * respuesta del alta. La base y todo lo grabado viven en el directorio temporal de la corrida, que servidor.mjs borra al
 * terminar.
 *
 * Los pedidos directos a la Api de AC-07 y AC-08 salen del navegador (`fetch` en la página, con la cookie de la
 * sesión), como los haría alguien que saltea la pantalla.
 *
 * Datos personales: el cliente es ficticio, con un DNI improbable (99.999.999), un teléfono de ceros y un email en
 * example.com.
 */

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-presupuestos-edicion-1';

const TITULO_LINEAS = 'Líneas del presupuesto';
const TEXTO_TOTAL = 'Total (IVA incluido)';

/** Textos de D-07 y D-16, tal cual. */
const TITULO_DIALOGO = 'Pasar a Final';
const TEXTO_CONFIRMAR_FINAL = 'Una vez en Final, el presupuesto no se puede modificar.';
const MENSAJE_PRESUPUESTO_FINAL = 'El presupuesto está en Final y no se puede modificar.';
const mensajeGrabado = (numero: number) => `Presupuesto N.º ${numero} grabado. Estado: Borrador.`;
const mensajePasadoAFinal = (numero: number) => `Presupuesto N.º ${numero} pasado a Final.`;

/** Configuración de partida de cada prueba, como la pide el POST de la Api. */
const PARTIDA = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: null,
};

/** Un cliente ficticio, con datos elegidos para que sea improbable que coincidan con los de una persona real. */
const CLIENTE = {
  apellido: 'Ficticio',
  nombre: 'Cliente de Edición',
  dni: '99.999.999',
  domicilio: 'Calle Inventada 123, Localidad de Prueba',
  email: 'cliente.edicion@example.com',
  telefono: '00 0000-0000',
};

/** El DNI como lo guarda y lo devuelve la Api: solo los dígitos (FEAT-003a). */
const DNI_GUARDADO = '99999999';

/** Lo que devuelve la Api de un artículo grabado. */
interface Articulo {
  codigo: number;
  codigoProveedor: string;
  descripcion: string;
  precioVenta: number;
  activo: boolean;
}

/** Una línea como la envía la pantalla; `orden` solo en una línea ya grabada (D-03). */
interface LineaAGrabar {
  codigoArticulo: number;
  cantidad: number;
  porcentajeDescuento: number;
  orden?: number;
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
    orden: number;
  }[];
  total: number;
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

const tablaLineas = (page: Page) => page.getByRole('table', { name: TITULO_LINEAS });
const filasLineas = (page: Page) => tablaLineas(page).locator('tbody tr');
const celdaTotal = (page: Page) =>
  tablaLineas(page)
    .locator('tfoot tr')
    .filter({ has: page.getByRole('rowheader', { name: TEXTO_TOTAL, exact: true }) })
    .getByRole('cell')
    .first();
const dato = (page: Page, etiqueta: string) =>
  page
    .locator('dl.datos > div')
    .filter({ has: page.locator('dt', { hasText: new RegExp(`^${etiqueta.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`) }) })
    .locator('dd');
const campo = (page: Page, etiqueta: string) => page.getByLabel(etiqueta, { exact: true });
const cantidad = (page: Page, n: number) => campo(page, `Cantidad de la línea ${n}`);
const precioUnitario = (page: Page, n: number) => campo(page, `Precio unitario de la línea ${n}`);
const botonGrabar = (page: Page) => page.getByRole('button', { name: 'Grabar', exact: true });
const enlaceModificar = (page: Page) => page.getByRole('link', { name: 'Modificar', exact: true });
/**
 * El botón de la pantalla que abre el diálogo. El del diálogo tiene el mismo nombre: este localizador se usa solo con
 * el diálogo cerrado (fuera del DOM), y dentro del diálogo se busca con `dialogo(page).getByRole(…)`.
 */
const botonPasarAFinal = (page: Page) => page.getByRole('button', { name: 'Pasar a Final', exact: true });
const dialogo = (page: Page) => page.getByRole('dialog', { name: TITULO_DIALOGO });

const ruta = (url: string) => new URL(url).pathname;
const esModificacion = (url: string, numero: number) => ruta(url) === `/api/presupuestos/${numero}`;
const esPasoDeEstado = (url: string, numero: number) => ruta(url) === `/api/presupuestos/${numero}/estado`;
const esBusqueda = (url: string) => ruta(url) === '/api/articulos';

/** `yyyy-MM-dd` como dd/mm/aaaa (FR-15 de FEAT-003a). */
function comoSeMuestra(fecha: string): string {
  const [anio, mes, dia] = fecha.split('-');
  return `${dia}/${mes}/${anio}`;
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

/** Ingresa y deja por la Api Responsable Inscripto, alícuota 21 y múltiplo 0,01. */
async function ingresarConConfiguracion(page: Page) {
  await ingresar(page);
  const respuesta = await page.request.post('/api/configuracion', { data: PARTIDA });
  expect(respuesta.status(), 'No se pudo fijar la configuración de partida').toBe(200);
}

/** Graba por la Api un artículo con ese costo y margen; devuelve lo que respondió. */
async function crearArticulo(
  request: APIRequestContext,
  codigoProveedor: string,
  precioCosto: number,
  margenUtilidad = 0,
): Promise<Articulo> {
  const respuesta = await request.post('/api/articulos', {
    data: { codigoProveedor, descripcion: `Artículo de edición ${codigoProveedor}`, precioCosto, margenUtilidad },
  });
  expect(respuesta.status(), `No se pudo crear ${codigoProveedor}`).toBe(201);
  const creado: Articulo = await respuesta.json();
  expect(creado.activo).toBe(true);
  return creado;
}

/** Graba un presupuesto en Borrador por la Api con el cliente ficticio y esas líneas; devuelve lo que respondió. */
async function grabarPorApi(request: APIRequestContext, lineas: LineaAGrabar[]): Promise<PresupuestoGrabado> {
  const respuesta = await request.post('/api/presupuestos', { data: { cliente: CLIENTE, lineas } });
  expect(respuesta.status(), 'No se pudo grabar el presupuesto por la Api').toBe(201);
  const grabado: PresupuestoGrabado = await respuesta.json();
  expect(grabado.estado).toBe('borrador');
  return grabado;
}

/** Pasa a Final por la Api, con la versión leída, como lo hace la pantalla al confirmar. */
async function pasarAFinalPorApi(request: APIRequestContext, presupuesto: PresupuestoGrabado) {
  const respuesta = await request.put(`/api/presupuestos/${presupuesto.numero}/estado`, {
    data: { estado: 'final', version: presupuesto.version },
  });
  expect(respuesta.status(), 'No se pudo pasar a Final por la Api').toBe(200);
  const final: PresupuestoGrabado = await respuesta.json();
  expect(final.estado).toBe('final');
  return final;
}

/** El presupuesto tal como está grabado, pedido con la sesión de la página. */
async function consultarPresupuesto(request: APIRequestContext, numero: number): Promise<PresupuestoGrabado> {
  const respuesta = await request.get(`/api/presupuestos/${numero}`);
  expect(respuesta.status(), `GET /api/presupuestos/${numero}`).toBe(200);
  return respuesta.json();
}

/** El presupuesto grabado como texto, para comparar que nada cambió, byte por byte. */
async function presupuestoComoTexto(request: APIRequestContext, numero: number): Promise<string> {
  const respuesta = await request.get(`/api/presupuestos/${numero}`);
  expect(respuesta.status(), `GET /api/presupuestos/${numero}`).toBe(200);
  return respuesta.text();
}

/**
 * Un pedido directo a la Api desde el navegador, con la cookie de la sesión: lo que haría alguien que saltea la
 * pantalla. Devuelve el código HTTP y el cuerpo.
 */
async function pedidoDesdeElNavegador(page: Page, metodo: string, destino: string, cuerpo: unknown) {
  return page.evaluate(
    async ({ metodo: m, destino: d, cuerpo: c }) => {
      const respuesta = await fetch(d, {
        method: m,
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(c),
      });
      return { estado: respuesta.status, texto: await respuesta.text() };
    },
    { metodo, destino, cuerpo },
  );
}

/** Abre el presupuesto grabado con su dirección, como al volver a consultarlo. */
async function abrirGrabado(page: Page, numero: number) {
  await page.goto(`/presupuestos/${numero}`);
  await expect(page.getByRole('heading', { name: 'Presupuesto', exact: true })).toBeVisible();
  await expect(dato(page, 'Número')).toHaveText(String(numero));
}

/** Desde el presupuesto, "Modificar" lleva a la pantalla de modificación con el formulario cargado. */
async function abrirModificacion(page: Page, numero: number) {
  await abrirGrabado(page, numero);
  await enlaceModificar(page).click();
  await expect(page).toHaveURL(new RegExp(`/presupuestos/${numero}/editar$`));
  await expect(page.getByRole('heading', { name: 'Modificar presupuesto', exact: true })).toBeVisible();
  await expect(page.getByText(`Presupuesto N.º ${numero}`, { exact: true })).toBeVisible();
  await expect(campo(page, 'Apellido')).toHaveValue(CLIENTE.apellido);
}

/**
 * Pulsa Grabar en la modificación, espera el PUT y vuelve al presupuesto con la confirmación del grabado en Borrador.
 * Devuelve lo que envió la pantalla y lo que respondió la Api.
 */
async function grabarModificacion(page: Page, numero: number) {
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'PUT' && esModificacion(r.url(), numero)),
    botonGrabar(page).click(),
  ]);
  expect(respuesta.status(), 'La modificación no respondió 200').toBe(200);
  const enviado = respuesta.request().postDataJSON() as { version: number; lineas: LineaAGrabar[] };
  const grabado: PresupuestoGrabado = await respuesta.json();
  await expect(page).toHaveURL(new RegExp(`/presupuestos/${numero}$`));
  await expect(page.getByRole('status').filter({ hasText: mensajeGrabado(numero) })).toBeVisible();
  return { enviado, grabado };
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
}

/** Cuenta los pedidos de paso de estado que envía la página para ese presupuesto. */
function contarPasosDeEstado(page: Page, numero: number) {
  const contador = { pedidos: 0 };
  page.on('request', (pedido) => {
    if (pedido.method() === 'PUT' && esPasoDeEstado(pedido.url(), numero)) {
      contador.pedidos++;
    }
  });
  return contador;
}

/** Las siete celdas de la fila de un artículo con Cantidad 1 y Descuento 0, como las muestra el presupuesto. */
const celdasSinDescuento = (articulo: Articulo, precio: string) => [
  String(articulo.codigo),
  articulo.descripcion,
  precio,
  '1',
  '0',
  precio,
  precio,
];

test('AC-01: modificar no cambia la fecha', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, 'PE-AC01', 1000);
  const grabado = await grabarPorApi(page.request, [
    { codigoArticulo: articulo.codigo, cantidad: 1, porcentajeDescuento: 0 },
  ]);

  await abrirModificacion(page, grabado.numero);
  // La fecha no es un dato que la operadora pueda escribir en la modificación.
  await expect(campo(page, 'Fecha')).toHaveCount(0);
  await expect(page.getByRole('textbox', { name: /fecha/i })).toHaveCount(0);
  await cantidad(page, 1).fill('2');
  const { grabado: modificado } = await grabarModificacion(page, grabado.numero);

  // AC-01 (FR-01, D-06): la modificación conserva la fecha y el número del primer grabado.
  expect(modificado.fecha).toBe(grabado.fecha);
  expect(modificado.numero).toBe(grabado.numero);
  expect(modificado.lineas[0].cantidad).toBe(2);
  await expect(dato(page, 'Fecha')).toHaveText(comoSeMuestra(grabado.fecha));

  // Tampoco por un pedido directo: una fecha en el cuerpo no se lee y la fecha queda igual.
  const otraFecha = grabado.fecha === '2000-01-01' ? '2000-01-02' : '2000-01-01';
  const respuesta = await page.request.put(`/api/presupuestos/${grabado.numero}`, {
    data: {
      version: modificado.version,
      fecha: otraFecha,
      cliente: CLIENTE,
      lineas: [{ codigoArticulo: articulo.codigo, cantidad: 3, porcentajeDescuento: 0, orden: 1 }],
    },
  });
  expect(respuesta.status()).toBe(200);
  const guardado = await consultarPresupuesto(page.request, grabado.numero);
  expect(guardado.lineas[0].cantidad).toBe(3);
  expect(guardado.fecha).toBe(grabado.fecha);
  await abrirGrabado(page, grabado.numero);
  await expect(dato(page, 'Fecha')).toHaveText(comoSeMuestra(grabado.fecha));
});

test('AC-02: modificar el domicilio graba y se ve al reabrir', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, 'PE-AC02', 1000);
  const grabado = await grabarPorApi(page.request, [
    { codigoArticulo: articulo.codigo, cantidad: 1, porcentajeDescuento: 0 },
  ]);

  await abrirModificacion(page, grabado.numero);
  // El formulario se abre con los datos grabados.
  await expect(campo(page, 'Nombre')).toHaveValue(CLIENTE.nombre);
  await expect(campo(page, 'DNI')).toHaveValue(DNI_GUARDADO);
  await expect(campo(page, 'Domicilio')).toHaveValue(CLIENTE.domicilio);
  await expect(campo(page, 'Email')).toHaveValue(CLIENTE.email);
  await expect(campo(page, 'Nro. de Teléfono')).toHaveValue(CLIENTE.telefono);
  await expect(filasLineas(page)).toHaveCount(1);

  const nuevoDomicilio = 'Avenida Ficticia 456, Localidad de Prueba';
  await campo(page, 'Domicilio').fill(nuevoDomicilio);
  const { enviado, grabado: modificado } = await grabarModificacion(page, grabado.numero);

  // AC-02: se grabó con la versión leída, con un mensaje de confirmación, y el presupuesto muestra el nuevo domicilio.
  expect(enviado.version).toBe(grabado.version);
  expect(modificado.cliente).toEqual({ ...CLIENTE, dni: DNI_GUARDADO, domicilio: nuevoDomicilio });
  expect(modificado.estado).toBe('borrador');
  await expect(dato(page, 'Domicilio')).toHaveText(nuevoDomicilio);
  await expect(dato(page, 'Estado')).toHaveText('Borrador');

  // Al volver a abrirlo.
  await abrirGrabado(page, grabado.numero);
  await expect(dato(page, 'Domicilio')).toHaveText(nuevoDomicilio);
  await expect(dato(page, 'Apellido')).toHaveText(CLIENTE.apellido);
  const guardado = await consultarPresupuesto(page.request, grabado.numero);
  expect(guardado.cliente.domicilio).toBe(nuevoDomicilio);
  expect(guardado.lineas).toEqual(grabado.lineas);
});

test('AC-03: agregar una línea y quitar otra', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const conservada = await crearArticulo(page.request, 'PE-AC03-A', 1000);
  const quitada = await crearArticulo(page.request, 'PE-AC03-B', 500);
  const agregada = await crearArticulo(page.request, 'PE-AC03-C', 250);
  const grabado = await grabarPorApi(page.request, [
    { codigoArticulo: conservada.codigo, cantidad: 2, porcentajeDescuento: 10 },
    { codigoArticulo: quitada.codigo, cantidad: 1, porcentajeDescuento: 0 },
  ]);

  await abrirModificacion(page, grabado.numero);
  await expect(filasLineas(page)).toHaveCount(2);
  await expect(filasLineas(page).nth(1)).toContainText(quitada.descripcion);
  await page.getByRole('button', { name: 'Quitar línea 2', exact: true }).click();
  await expect(filasLineas(page)).toHaveCount(1);
  await agregar(page, agregada);
  await expect(filasLineas(page)).toHaveCount(2);
  await expect(filasLineas(page).nth(1)).toContainText(agregada.descripcion);
  const { enviado } = await grabarModificacion(page, grabado.numero);

  // La línea conservada viaja con su orden; la agregada, sin orden (D-03).
  expect(enviado.lineas).toEqual([
    { codigoArticulo: conservada.codigo, cantidad: 2, porcentajeDescuento: 10, orden: 1 },
    { codigoArticulo: agregada.codigo, cantidad: 1, porcentajeDescuento: 0 },
  ]);

  // AC-03: al volver a abrirlo, la agregada está y la quitada no.
  await abrirGrabado(page, grabado.numero);
  await expect(filasLineas(page)).toHaveCount(2);
  await expect(filasLineas(page).nth(0).getByRole('cell')).toHaveText([
    String(conservada.codigo),
    conservada.descripcion,
    '1.000,00',
    '2',
    '10',
    '900,00',
    '1.800,00',
  ]);
  await expect(filasLineas(page).nth(1).getByRole('cell')).toHaveText(celdasSinDescuento(agregada, '250,00'));
  await expect(tablaLineas(page)).not.toContainText(quitada.descripcion);
  await expect(celdaTotal(page)).toHaveText('2.050,00');
  const guardado = await consultarPresupuesto(page.request, grabado.numero);
  expect(guardado.lineas.map((l) => [l.codigoArticulo, l.orden])).toEqual([
    [conservada.codigo, 1],
    [agregada.codigo, 2],
  ]);
});

test('AC-04 y AC-05: pasar a Final pide confirmación y graba Final', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, 'PE-AC04', 1000);
  const grabado = await grabarPorApi(page.request, [
    { codigoArticulo: articulo.codigo, cantidad: 1, porcentajeDescuento: 0 },
  ]);
  await abrirGrabado(page, grabado.numero);
  await expect(dato(page, 'Estado')).toHaveText('Borrador');
  const pasos = contarPasosDeEstado(page, grabado.numero);
  // Antes de abrir el diálogo, "Modificar" recibe el clic: la comprobación de abajo no falla por otra causa.
  await enlaceModificar(page).click({ trial: true, timeout: 1000 });

  await botonPasarAFinal(page).click();

  // AC-05: antes de grabar, un diálogo avisa que después no podrá modificarse, con "Pasar a Final" y "Cancelar".
  await expect(dialogo(page)).toBeVisible();
  await expect(dialogo(page).getByRole('heading', { name: TITULO_DIALOGO })).toBeVisible();
  await expect(dialogo(page)).toContainText(TEXTO_CONFIRMAR_FINAL);
  await expect(dialogo(page).getByRole('button')).toHaveText(['Pasar a Final', 'Cancelar']);
  await expect(dialogo(page).getByRole('button', { name: 'Cancelar', exact: true })).toBeFocused();
  expect(pasos.pedidos, 'Se envió el paso a Final antes de confirmar').toBe(0);
  expect((await consultarPresupuesto(page.request, grabado.numero)).estado).toBe('borrador');

  // El diálogo es modal en el navegador real (lo que jsdom no puede probar): el resto de la página queda inerte, así
  // que "Modificar" ya no recibe el clic (Playwright lo ve tapado por el diálogo y la prueba de clic falla).
  expect(await dialogo(page).evaluate((elemento) => elemento.matches(':modal'))).toBe(true);
  await expect(enlaceModificar(page).click({ trial: true, timeout: 1000 })).rejects.toThrow();
  await expect(page).toHaveURL(new RegExp(`/presupuestos/${grabado.numero}$`));
  await expect(dialogo(page)).toBeVisible();

  // AC-04: confirmar graba el estado Final, con la versión mostrada.
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'PUT' && esPasoDeEstado(r.url(), grabado.numero)),
    dialogo(page).getByRole('button', { name: 'Pasar a Final', exact: true }).click(),
  ]);
  expect(respuesta.status()).toBe(200);
  expect(respuesta.request().postDataJSON()).toEqual({ estado: 'final', version: grabado.version });
  expect(pasos.pedidos).toBe(1);
  await expect(dialogo(page)).toHaveCount(0);
  const aviso = page.getByRole('status').filter({ hasText: mensajePasadoAFinal(grabado.numero) });
  await expect(aviso).toBeVisible();
  await expect(aviso).toBeFocused();
  await expect(dato(page, 'Estado')).toHaveText('Final');
  await expect(enlaceModificar(page)).toHaveCount(0);
  await expect(botonPasarAFinal(page)).toHaveCount(0);

  const guardado = await consultarPresupuesto(page.request, grabado.numero);
  expect(guardado.estado).toBe('final');
  expect(guardado.fecha).toBe(grabado.fecha);
  expect(guardado.lineas).toEqual(grabado.lineas);
  await abrirGrabado(page, grabado.numero);
  await expect(dato(page, 'Estado')).toHaveText('Final');
});

test('AC-06: cancelar deja el presupuesto en Borrador', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, 'PE-AC06', 1000);
  const grabado = await grabarPorApi(page.request, [
    { codigoArticulo: articulo.codigo, cantidad: 1, porcentajeDescuento: 0 },
  ]);
  const antes = await presupuestoComoTexto(page.request, grabado.numero);
  await abrirGrabado(page, grabado.numero);
  const pasos = contarPasosDeEstado(page, grabado.numero);

  // AC-06 con "Cancelar": el diálogo se cierra, el foco vuelve al botón y no se envía nada.
  await botonPasarAFinal(page).click();
  await expect(dialogo(page)).toBeVisible();
  await dialogo(page).getByRole('button', { name: 'Cancelar', exact: true }).click();
  await expect(dialogo(page)).toHaveCount(0);
  await expect(botonPasarAFinal(page)).toBeFocused();
  await expect(dato(page, 'Estado')).toHaveText('Borrador');

  // Con Escape, lo mismo.
  await botonPasarAFinal(page).click();
  await expect(dialogo(page)).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(dialogo(page)).toHaveCount(0);
  await expect(botonPasarAFinal(page)).toBeFocused();

  // Sigue en Borrador, sin cambios, y se puede seguir modificando.
  expect(pasos.pedidos, 'El paso a Final cancelado envió un pedido').toBe(0);
  expect(await presupuestoComoTexto(page.request, grabado.numero)).toBe(antes);
  await abrirGrabado(page, grabado.numero);
  await expect(dato(page, 'Estado')).toHaveText('Borrador');
  await expect(enlaceModificar(page)).toBeVisible();
  await expect(botonPasarAFinal(page)).toBeVisible();
});

test('AC-07 y AC-08: un Final rechaza cambios por pantalla y por Api', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, 'PE-AC07', 1000);
  const otro = await crearArticulo(page.request, 'PE-AC07-OTRO', 500);
  const grabado = await grabarPorApi(page.request, [
    { codigoArticulo: articulo.codigo, cantidad: 1, porcentajeDescuento: 0 },
  ]);

  // La modificación se abre en Borrador y, antes de grabar, el presupuesto pasa a Final (como al confirmarlo en otra
  // pestaña): grabar se rechaza con el texto de la pantalla y lo escrito sigue ahí.
  await abrirModificacion(page, grabado.numero);
  const final = await pasarAFinalPorApi(page.request, grabado);
  const enFinal = await presupuestoComoTexto(page.request, grabado.numero);
  await campo(page, 'Domicilio').fill('Domicilio que no se graba 1');
  const [rechazo] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'PUT' && esModificacion(r.url(), grabado.numero)),
    botonGrabar(page).click(),
  ]);
  expect(rechazo.status()).toBe(409);
  expect((await rechazo.json()).motivo).toBe('final');
  await expect(page.getByRole('alert').filter({ hasText: MENSAJE_PRESUPUESTO_FINAL })).toBeVisible();
  await expect(campo(page, 'Domicilio')).toHaveValue('Domicilio que no se graba 1');
  await expect(page).toHaveURL(new RegExp(`/presupuestos/${grabado.numero}/editar$`));
  expect(await presupuestoComoTexto(page.request, grabado.numero)).toBe(enFinal);

  // AC-07 por pantalla: en Final no hay acciones de edición, y la modificación muestra el aviso sin formulario.
  await abrirGrabado(page, grabado.numero);
  await expect(dato(page, 'Estado')).toHaveText('Final');
  await expect(enlaceModificar(page)).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Pasar a Final' })).toHaveCount(0);
  // AC-08 por pantalla: nada ofrece volver a Borrador.
  await expect(page.getByRole('button', { name: /borrador/i })).toHaveCount(0);
  await expect(page.getByRole('link', { name: /borrador/i })).toHaveCount(0);
  await page.goto(`/presupuestos/${grabado.numero}/editar`);
  await expect(page.getByRole('heading', { name: 'Modificar presupuesto', exact: true })).toBeVisible();
  await expect(page.getByText(MENSAJE_PRESUPUESTO_FINAL, { exact: true })).toBeVisible();
  await expect(botonGrabar(page)).toHaveCount(0);
  await expect(campo(page, 'Domicilio')).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Quitar línea/ })).toHaveCount(0);

  // AC-07 por un pedido directo a la Api, desde el navegador con la sesión: datos, líneas agregadas o quitadas, con la
  // versión vigente o con una vieja; todos se rechazan con 409 `final`.
  const destino = `/api/presupuestos/${grabado.numero}`;
  const lineaGrabada = { codigoArticulo: articulo.codigo, cantidad: 1, porcentajeDescuento: 0, orden: 1 };
  const intentos = [
    { version: final.version, cliente: { ...CLIENTE, domicilio: 'Domicilio que no se graba 2' }, lineas: [lineaGrabada] },
    { version: final.version, cliente: CLIENTE, lineas: [{ ...lineaGrabada, cantidad: 5, porcentajeDescuento: 50 }] },
    {
      version: final.version,
      cliente: CLIENTE,
      lineas: [lineaGrabada, { codigoArticulo: otro.codigo, cantidad: 1, porcentajeDescuento: 0 }],
    },
    { version: final.version, cliente: CLIENTE, lineas: [{ codigoArticulo: otro.codigo, cantidad: 1, porcentajeDescuento: 0 }] },
    { version: grabado.version, cliente: { ...CLIENTE, apellido: 'Otro' }, lineas: [lineaGrabada] },
  ];
  for (const cuerpo of intentos) {
    const { estado, texto } = await pedidoDesdeElNavegador(page, 'PUT', destino, cuerpo);
    expect(estado, texto).toBe(409);
    expect(JSON.parse(texto).motivo).toBe('final');
  }

  // AC-08: volver a Borrador se rechaza, con la versión vigente.
  const aBorrador = await pedidoDesdeElNavegador(page, 'PUT', `${destino}/estado`, {
    estado: 'borrador',
    version: final.version,
  });
  expect(aBorrador.estado, aBorrador.texto).toBe(409);
  expect(JSON.parse(aBorrador.texto).motivo).toBe('final');

  // Nada cambió: ni un byte del presupuesto, y sigue en Final en la pantalla.
  expect(await presupuestoComoTexto(page.request, grabado.numero)).toBe(enFinal);
  await abrirGrabado(page, grabado.numero);
  await expect(dato(page, 'Estado')).toHaveText('Final');
  await expect(dato(page, 'Domicilio')).toHaveText(CLIENTE.domicilio);
  await expect(filasLineas(page)).toHaveCount(1);
  await expect(filasLineas(page).first().getByRole('cell')).toHaveText(celdasSinDescuento(articulo, '1.000,00'));
});

test('AC-09 y AC-10: las líneas conservan precio tras recalcular y desactivar', async ({ page }) => {
  await ingresarConConfiguracion(page);
  // Costo 1.210 y margen 50 con múltiplo 0,01: 1.815,00; con múltiplo 50 pasa a 1.850,00 (FEAT-002c, AC-02).
  const articulo = await crearArticulo(page.request, 'PE-AC09', 1210, 50);
  expect(articulo.precioVenta).toBe(1815);
  const grabado = await grabarPorApi(page.request, [
    { codigoArticulo: articulo.codigo, cantidad: 1, porcentajeDescuento: 0 },
  ]);
  expect(grabado.lineas[0].precioUnitario).toBe(1815);

  // El catálogo se recalcula al grabar la configuración desde su pantalla, como la operadora.
  await page.goto('/');
  await page.getByRole('link', { name: 'Configuración' }).click();
  await expect(page.getByRole('heading', { name: 'Configuración' })).toBeVisible();
  await campo(page, 'Múltiplo de redondeo comercial ($)').fill('50');
  const [configuracion] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'POST' && ruta(r.url()) === '/api/configuracion'),
    page.getByRole('button', { name: 'Grabar' }).click(),
  ]);
  expect(configuracion.status()).toBe(200);
  const recalculado = await page.request.get(`/api/articulos/${articulo.codigo}`);
  expect((await recalculado.json()).precioVenta).toBe(1850);

  // AC-09: la línea del presupuesto grabado conserva 1.815,00.
  await abrirGrabado(page, grabado.numero);
  await expect(filasLineas(page).first().getByRole('cell')).toHaveText(celdasSinDescuento(articulo, '1.815,00'));
  await expect(celdaTotal(page)).toHaveText('1.815,00');
  expect((await consultarPresupuesto(page.request, grabado.numero)).lineas).toEqual(grabado.lineas);

  // AC-10: se desactiva el artículo y, al volver a abrir el presupuesto en Borrador, la línea sigue igual.
  const desactivado = await page.request.put(`/api/articulos/${articulo.codigo}/estado`, { data: { activo: false } });
  expect(desactivado.status()).toBe(200);
  expect((await desactivado.json()).activo).toBe(false);
  await abrirGrabado(page, grabado.numero);
  await expect(dato(page, 'Estado')).toHaveText('Borrador');
  await expect(filasLineas(page)).toHaveCount(1);
  await expect(filasLineas(page).first().getByRole('cell')).toHaveText(celdasSinDescuento(articulo, '1.815,00'));
  expect((await consultarPresupuesto(page.request, grabado.numero)).lineas).toEqual(grabado.lineas);

  // En la modificación también se ve con su precio grabado y, al cambiarle la cantidad, lo conserva (D-02).
  await enlaceModificar(page).click();
  await expect(page.getByRole('heading', { name: 'Modificar presupuesto', exact: true })).toBeVisible();
  await expect(filasLineas(page).first()).toContainText(String(articulo.codigo));
  await expect(filasLineas(page).first()).toContainText(articulo.descripcion);
  await expect(precioUnitario(page, 1)).toHaveValue('1.815,00');
  await cantidad(page, 1).fill('2');
  const { grabado: modificado } = await grabarModificacion(page, grabado.numero);
  expect(modificado.lineas).toEqual([
    { ...grabado.lineas[0], cantidad: 2, precioFinal: 3630 },
  ]);
  await expect(filasLineas(page).first().getByRole('cell')).toHaveText([
    String(articulo.codigo),
    articulo.descripcion,
    '1.815,00',
    '2',
    '0',
    '1.815,00',
    '3.630,00',
  ]);
});
