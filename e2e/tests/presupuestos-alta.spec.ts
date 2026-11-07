import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test';

/**
 * Alta del presupuesto con cliente y líneas del catálogo (FEAT-003a, AC-01 a AC-19) de punta a punta, contra la Api
 * real que sirve la SPA compilada.
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva (D-05 de FEAT-001c). La
 * primera prueba define la contraseña de esa base y verifica AC-10 con la base sin presupuestos; las demás ingresan con
 * ella (`ingresar` resuelve los dos casos). Cada prueba deja por la Api Responsable Inscripto, alícuota 21 y múltiplo
 * 0,01 y crea sus artículos por la Api con la sesión de la página, con códigos en el proveedor propios ("PR-…"). Solo
 * la prueba de AC-13 graba descripciones con "Armazón" o "armazon": esa búsqueda tiene que listar todos los activos que
 * lo contienen y ningún inactivo.
 *
 * Ninguna prueba, salvo AC-10 y AC-09, espera un número fijo: lo leen de la respuesta del alta. AC-09 graba por la Api
 * hasta que el último número es 154 y graba el siguiente desde la pantalla (D-22). AC-19 compara con la fecha local del
 * día de la PC que corre la Api, que es la misma que corre esta suite (D-06, D-22).
 *
 * Los importes se leen como los muestra la pantalla ("1.850,00"), con el símbolo en el encabezado de la columna
 * ("Precio Unitario ($)", D-34): no se busca el texto "$1.850,00".
 *
 * Datos personales (D-17, D-36): todos los clientes son ficticios. "ana@mail.com" y el DNI "23.456.789" son los
 * ejemplos literales del PRD; los demás datos son inventados para esta suite.
 */

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-presupuestos-alta-1';

const ETIQUETA_APELLIDO = 'Apellido';
const ETIQUETA_NOMBRE = 'Nombre';
const ETIQUETA_DNI = 'DNI';
const ETIQUETA_DOMICILIO = 'Domicilio';
const ETIQUETA_EMAIL = 'Email';
const ETIQUETA_TELEFONO = 'Nro. de Teléfono';
const ETIQUETA_BUSQUEDA = 'Texto a buscar';
const TITULO_LINEAS = 'Líneas del presupuesto';
const TITULO_RESULTADOS = 'Artículos activos';

/** Textos del PRD (AC-02, AC-16) y de D-24, tal cual. */
const MENSAJE_DNI_FALTANTE = 'Ingresá el DNI del cliente';
const MENSAJE_APELLIDO_FALTANTE = 'Ingresá el apellido del cliente';
const MENSAJE_NOMBRE_FALTANTE = 'Ingresá el nombre del cliente';
const MENSAJE_DNI_INVALIDO = 'El DNI debe tener 7 u 8 dígitos';
const MENSAJE_EMAIL_INVALIDO = 'El email debe tener el formato usuario@dominio.extensión';
const MENSAJE_SIN_LINEAS = 'Agregá al menos una línea al presupuesto';
const MENSAJE_PRECIO_NEGATIVO = 'El precio unitario no puede ser negativo';
const confirmacion = (numero: number) => `Presupuesto N.º ${numero} grabado. Estado: Borrador.`;

/** Lo que se muestra de un dato opcional sin cargar (D-35). */
const SIN_DATO = '—';

/** Las siete columnas de FR-13, con el símbolo de los importes en el encabezado (D-34). */
const COLUMNAS_LINEA = [
  'Código de Artículo',
  'Descripción',
  'Precio Unitario ($)',
  'Cantidad',
  'Porcentaje de Descuento (%)',
  'Precio con Descuento ($)',
  'Precio Final ($)',
];

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

/** Un cliente ficticio completo: ningún dato es de una persona real. */
const CLIENTE_FICTICIO: Cliente = {
  apellido: 'Ficticio',
  nombre: 'Cliente de Prueba',
  dni: '30111222',
  domicilio: 'Calle Inventada 123, Localidad de Prueba',
  email: 'cliente.ficticio@example.com',
  telefono: '011 5555-0000',
};

/** Solo los obligatorios, para las pruebas que no miran los opcionales. */
const CLIENTE_MINIMO: Cliente = { ...CLIENTE_FICTICIO, domicilio: '', email: '', telefono: '' };

/** Lo que se graba por la Api para preparar cada prueba. */
interface DatosArticulo {
  codigoProveedor: string;
  descripcion: string;
  precioCosto: number;
  margenUtilidad: number;
}

/** Lo que devuelve la Api de un artículo grabado. */
interface Articulo extends DatosArticulo {
  codigo: number;
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
const apellido = (page: Page) => page.getByLabel(ETIQUETA_APELLIDO, { exact: true });
const nombre = (page: Page) => page.getByLabel(ETIQUETA_NOMBRE, { exact: true });
const dni = (page: Page) => page.getByLabel(ETIQUETA_DNI, { exact: true });
const domicilio = (page: Page) => page.getByLabel(ETIQUETA_DOMICILIO, { exact: true });
const email = (page: Page) => page.getByLabel(ETIQUETA_EMAIL, { exact: true });
const telefono = (page: Page) => page.getByLabel(ETIQUETA_TELEFONO, { exact: true });
const busqueda = (page: Page) => page.getByLabel(ETIQUETA_BUSQUEDA, { exact: true });
const tablaLineas = (page: Page) => page.getByRole('table', { name: TITULO_LINEAS });
const filasLineas = (page: Page) => tablaLineas(page).locator('tbody tr');
const tablaResultados = (page: Page) => page.getByRole('table', { name: TITULO_RESULTADOS });
const botonAgregar = (page: Page, articulo: Articulo) =>
  page.getByRole('button', { name: `Agregar ${articulo.codigo} ${articulo.descripcion}`, exact: true });
const mensajeConfirmacion = (page: Page) => page.getByRole('status').filter({ hasText: 'Presupuesto N.º' });

const esAlta = (url: string) => new URL(url).pathname === '/api/presupuestos';
const esBusqueda = (url: string) => new URL(url).pathname === '/api/articulos';

const escaparRegex = (texto: string) => texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/**
 * El valor que muestra el presupuesto grabado para `etiqueta` (el `dd` junto a su `dt`). Se busca por CSS porque `dt` y
 * `dd` no tienen un rol ARIA con nombre que permita llegar al valor desde su etiqueta con `getByRole`.
 */
const dato = (page: Page, etiqueta: string) =>
  page
    .locator('dl.datos > div')
    .filter({ has: page.locator('dt', { hasText: new RegExp(`^${escaparRegex(etiqueta)}$`) }) })
    .locator('dd');

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

/** Ingresa y deja por la Api Responsable Inscripto, alícuota 21 y múltiplo 0,01; verifica que quedó vigente. */
async function ingresarConConfiguracion(page: Page) {
  await ingresar(page);
  const respuesta = await page.request.post('/api/configuracion', { data: PARTIDA });
  expect(respuesta.status(), 'No se pudo fijar la configuración de partida').toBe(200);
  const vigente = await page.request.get('/api/configuracion');
  expect(vigente.status()).toBe(200);
  expect(await vigente.json()).toMatchObject({
    alicuotaIva: 21,
    condicionFiscal: 'responsable_inscripto',
    multiploRedondeo: 0.01,
  });
}

/** Graba un artículo por la Api con la sesión de la página y devuelve lo que quedó guardado. */
async function crearArticulo(request: APIRequestContext, datos: DatosArticulo): Promise<Articulo> {
  const respuesta = await request.post('/api/articulos', { data: datos });
  expect(respuesta.status(), `No se pudo crear ${datos.codigoProveedor}`).toBe(201);
  const creado: Articulo = await respuesta.json();
  expect(creado.activo).toBe(true);
  return creado;
}

/** Un artículo de precio de venta 1.850,00: costo 1.850 y margen 0. */
async function crearArticulo1850(request: APIRequestContext, prueba: string): Promise<Articulo> {
  const articulo = await crearArticulo(request, {
    codigoProveedor: `PR-${prueba}-1850`,
    descripcion: `Lente monofocal de prueba ${prueba}`,
    precioCosto: 1850,
    margenUtilidad: 0,
  });
  expect(articulo.precioVenta).toBe(1850);
  return articulo;
}

/** Desactiva el artículo por la Api (FEAT-002b). */
async function desactivar(request: APIRequestContext, codigo: number) {
  const respuesta = await request.put(`/api/articulos/${codigo}/estado`, { data: { activo: false } });
  expect(respuesta.status(), `No se pudo desactivar ${codigo}`).toBe(200);
  expect((await respuesta.json()).activo).toBe(false);
}

/** Graba un presupuesto por la Api, con un cliente ficticio y una línea; devuelve su número. */
async function grabarPorApi(request: APIRequestContext, codigoArticulo: number): Promise<number> {
  const { apellido: a, nombre: n, dni: d } = CLIENTE_MINIMO;
  const respuesta = await request.post('/api/presupuestos', {
    data: { cliente: { apellido: a, nombre: n, dni: d }, lineas: [{ codigoArticulo }] },
  });
  expect(respuesta.status(), 'No se pudo grabar el presupuesto por la Api').toBe(201);
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
  await page.getByRole('link', { name: 'Nuevo presupuesto' }).click();
  await expect(page).toHaveURL(/\/presupuestos\/nuevo$/);
  await expect(page.getByRole('heading', { name: 'Nuevo presupuesto' })).toBeVisible();
}

/** Escribe los datos del cliente; un dato vacío deja el campo vacío. */
async function cargarCliente(page: Page, cliente: Cliente) {
  await apellido(page).fill(cliente.apellido);
  await nombre(page).fill(cliente.nombre);
  await dni(page).fill(cliente.dni);
  await domicilio(page).fill(cliente.domicilio);
  await email(page).fill(cliente.email);
  await telefono(page).fill(cliente.telefono);
}

/** Busca con el botón Buscar y espera la respuesta; verifica que la pantalla pidió solo artículos activos (D-15). */
async function buscar(page: Page, texto: string) {
  await busqueda(page).fill(texto);
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'GET' && esBusqueda(r.url())),
    page.getByRole('button', { name: 'Buscar', exact: true }).click(),
  ]);
  expect(respuesta.status()).toBe(200);
  expect(new URL(respuesta.url()).searchParams.get('soloActivos')).toBe('true');
}

/** Busca el artículo por su descripción y lo agrega como línea. */
async function buscarYAgregar(page: Page, articulo: Articulo) {
  await buscar(page, articulo.descripcion);
  await botonAgregar(page, articulo).click();
}

/** Pulsa Grabar, espera el alta y el presupuesto grabado en pantalla; devuelve lo que respondió la Api. */
async function grabarDesdePantalla(page: Page): Promise<PresupuestoGrabado> {
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'POST' && esAlta(r.url())),
    page.getByRole('button', { name: 'Grabar', exact: true }).click(),
  ]);
  expect(respuesta.status(), 'El alta no respondió 201').toBe(201);
  const grabado: PresupuestoGrabado = await respuesta.json();
  await expect(page).toHaveURL(new RegExp(`/presupuestos/${grabado.numero}$`));
  await expect(dato(page, 'Número')).toHaveText(String(grabado.numero));
  return grabado;
}

/** Vuelve a abrir el presupuesto con su dirección, en una entrada nueva del historial (sin la confirmación). */
async function reabrir(page: Page, numero: number) {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await page.goto(`/presupuestos/${numero}`);
  await expect(page.getByRole('heading', { name: 'Presupuesto', exact: true })).toBeVisible();
  await expect(dato(page, 'Número')).toHaveText(String(numero));
}

/**
 * Cuenta los POST de alta que salen de la página desde ahora: las pruebas de rechazo verifican que la pantalla no
 * envió nada.
 */
function contarAltas(page: Page) {
  let altas = 0;
  page.on('request', (pedido) => {
    if (pedido.method() === 'POST' && esAlta(pedido.url())) {
      altas++;
    }
  });
  return () => altas;
}

/**
 * Prepara una prueba de rechazo: graba por la Api un presupuesto testigo (así se sabe cuál es el último número), abre
 * el alta y empieza a contar los POST. `nadaGrabado` verifica que la pantalla no envió ningún alta, que sigue en el
 * alta y que no existe un presupuesto después del testigo.
 */
async function prepararRechazo(page: Page, prueba: string) {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo1850(page.request, prueba);
  const testigo = await grabarPorApi(page.request, articulo.codigo);
  await abrirNuevoPresupuesto(page);
  const altas = contarAltas(page);
  const nadaGrabado = async () => {
    expect(altas(), 'La pantalla envió un alta').toBe(0);
    await expect(page).toHaveURL(/\/presupuestos\/nuevo$/);
    const siguiente = await page.request.get(`/api/presupuestos/${testigo + 1}`);
    expect(siguiente.status(), 'Se grabó un presupuesto').toBe(404);
  };
  return { articulo, nadaGrabado };
}

/** El campo quedó marcado inválido y su mensaje, vinculado por aria-describedby, es `mensaje`. */
async function rechazadoJuntoAlCampo(page: Page, campo: Locator, mensaje: string) {
  await expect(campo).toHaveAttribute('aria-invalid', 'true');
  await mensajeVinculado(page, campo, mensaje);
}

/** El mensaje vinculado al elemento por aria-describedby es `mensaje`. */
async function mensajeVinculado(page: Page, elemento: Locator, mensaje: string) {
  await expect(elemento).toHaveAttribute('aria-describedby', /.+/);
  const idMensaje = await elemento.getAttribute('aria-describedby');
  await expect(page.locator(`[id="${idMensaje}"]`)).toHaveText(mensaje);
}

/** El campo no está marcado inválido. */
async function sinError(campo: Locator) {
  await expect(campo).toHaveAttribute('aria-invalid', 'false');
}

/** La fecha local de hoy en esta PC, como `yyyy-MM-dd`. */
function hoyLocal(): string {
  const ahora = new Date();
  const mes = String(ahora.getMonth() + 1).padStart(2, '0');
  const dia = String(ahora.getDate()).padStart(2, '0');
  return `${ahora.getFullYear()}-${mes}-${dia}`;
}

/** `yyyy-MM-dd` como dd/mm/aaaa (FR-15). */
function comoSeMuestra(fecha: string): string {
  const [anio, mes, dia] = fecha.split('-');
  return `${dia}/${mes}/${anio}`;
}

/** La línea de un artículo de 1.850,00 como la muestra la tabla de líneas: Cantidad 1, Descuento 0 (FR-14). */
const celdasLinea1850 = (articulo: Articulo) => [
  String(articulo.codigo),
  articulo.descripcion,
  '1.850,00',
  '1',
  '0',
  '1.850,00',
  '1.850,00',
];

// Primera prueba del archivo: la base todavía no tiene presupuestos (D-22).
test('el primer presupuesto de la base recibe el numero 1', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo1850(page.request, 'AC10');
  // Ningún presupuesto todavía.
  expect((await page.request.get('/api/presupuestos/1')).status()).toBe(404);

  await abrirNuevoPresupuesto(page);
  await cargarCliente(page, CLIENTE_MINIMO);
  await buscarYAgregar(page, articulo);
  const grabado = await grabarDesdePantalla(page);

  // AC-10: con la base vacía el número es 1.
  expect(grabado.numero).toBe(1);
  await expect(dato(page, 'Número')).toHaveText('1');
  expect((await consultarPresupuesto(page.request, 1)).numero).toBe(1);
});

test('sin DNI rechazado con Ingresá el DNI del cliente', async ({ page }) => {
  const { articulo, nadaGrabado } = await prepararRechazo(page, 'AC02');
  await cargarCliente(page, { ...CLIENTE_FICTICIO, dni: '' });
  await buscarYAgregar(page, articulo);

  await page.getByRole('button', { name: 'Grabar', exact: true }).click();

  // AC-02: el mensaje del PRD, junto al DNI; Apellido y Nombre no se reclaman.
  await rechazadoJuntoAlCampo(page, dni(page), MENSAJE_DNI_FALTANTE);
  await sinError(apellido(page));
  await sinError(nombre(page));
  await nadaGrabado();
});

test('sin apellido o sin nombre rechazado junto al campo faltante', async ({ page }) => {
  const { articulo, nadaGrabado } = await prepararRechazo(page, 'AC03');
  await cargarCliente(page, { ...CLIENTE_FICTICIO, apellido: '' });
  await buscarYAgregar(page, articulo);

  // Sin apellido: el mensaje va junto al Apellido y solo ahí.
  await page.getByRole('button', { name: 'Grabar', exact: true }).click();
  await rechazadoJuntoAlCampo(page, apellido(page), MENSAJE_APELLIDO_FALTANTE);
  await sinError(nombre(page));
  await sinError(dni(page));

  // Sin nombre: el mensaje va junto al Nombre y el del Apellido se va.
  await apellido(page).fill(CLIENTE_FICTICIO.apellido);
  await nombre(page).fill('');
  await page.getByRole('button', { name: 'Grabar', exact: true }).click();
  await rechazadoJuntoAlCampo(page, nombre(page), MENSAJE_NOMBRE_FALTANTE);
  await sinError(apellido(page));
  await sinError(dni(page));

  // AC-03: ninguno de los dos intentos grabó.
  await nadaGrabado();
});

test('DNI 123456, 123456789 o 23A56789 rechazado', async ({ page }) => {
  const { articulo, nadaGrabado } = await prepararRechazo(page, 'AC06');
  await cargarCliente(page, CLIENTE_FICTICIO);
  await buscarYAgregar(page, articulo);

  // AC-06: cada uno, rechazado junto al DNI con el texto de D-24.
  for (const invalido of ['123456', '123456789', '23A56789']) {
    await dni(page).fill(invalido);
    await page.getByRole('button', { name: 'Grabar', exact: true }).click();
    await rechazadoJuntoAlCampo(page, dni(page), MENSAJE_DNI_INVALIDO);
    await expect(dni(page)).toHaveValue(invalido);
  }
  await nadaGrabado();
});

test('email ana@ o ana@mail rechazado', async ({ page }) => {
  const { articulo, nadaGrabado } = await prepararRechazo(page, 'AC07');
  await cargarCliente(page, CLIENTE_FICTICIO);
  await buscarYAgregar(page, articulo);

  // AC-07: cada uno, rechazado junto al Email con el texto de D-24.
  for (const invalido of ['ana@', 'ana@mail']) {
    await email(page).fill(invalido);
    await page.getByRole('button', { name: 'Grabar', exact: true }).click();
    await rechazadoJuntoAlCampo(page, email(page), MENSAJE_EMAIL_INVALIDO);
    await sinError(dni(page));
  }
  await nadaGrabado();
});

test('sin lineas rechazado', async ({ page }) => {
  const { nadaGrabado } = await prepararRechazo(page, 'AC11');
  await cargarCliente(page, CLIENTE_FICTICIO);
  await expect(filasLineas(page)).toHaveCount(0);

  await page.getByRole('button', { name: 'Grabar', exact: true }).click();

  // AC-11: el mensaje de D-24, vinculado a la tabla de líneas; los datos del cliente no se reclaman.
  await mensajeVinculado(page, tablaLineas(page), MENSAJE_SIN_LINEAS);
  await sinError(apellido(page));
  await sinError(dni(page));
  await nadaGrabado();
});

test('articulo de -150,00 rechazado: no se agrega a la linea', async ({ page }) => {
  await ingresarConConfiguracion(page);
  // Costo −100 y margen 50: precio de venta −150,00 (D-04 de FEAT-002a).
  const negativo = await crearArticulo(page.request, {
    codigoProveedor: 'PR-AC16-NEGATIVO',
    descripcion: 'Bonificación de prueba AC16',
    precioCosto: -100,
    margenUtilidad: 50,
  });
  expect(negativo.precioVenta).toBe(-150);

  await abrirNuevoPresupuesto(page);
  await buscar(page, negativo.descripcion);
  const fila = tablaResultados(page).locator('tbody tr').filter({ has: botonAgregar(page, negativo) });
  await expect(fila.getByRole('cell').nth(2)).toHaveText('-150,00');

  await botonAgregar(page, negativo).click();

  // AC-16: el texto del PRD junto al campo del buscador, y la línea no se agrega.
  await rechazadoJuntoAlCampo(page, busqueda(page), MENSAJE_PRECIO_NEGATIVO);
  await expect(filasLineas(page)).toHaveCount(0);
});

test('seis datos y una linea graban, confirman y se muestran al reabrir', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo1850(page.request, 'AC01');

  await abrirNuevoPresupuesto(page);
  await cargarCliente(page, CLIENTE_FICTICIO);
  await buscarYAgregar(page, articulo);
  const grabado = await grabarDesdePantalla(page);

  // AC-01: un mensaje confirma que se grabó.
  await expect(mensajeConfirmacion(page)).toHaveText(confirmacion(grabado.numero));

  // Al volver a abrirlo, los seis datos.
  await reabrir(page, grabado.numero);
  await expect(dato(page, 'Apellido')).toHaveText(CLIENTE_FICTICIO.apellido);
  await expect(dato(page, 'Nombre')).toHaveText(CLIENTE_FICTICIO.nombre);
  await expect(dato(page, 'DNI')).toHaveText(CLIENTE_FICTICIO.dni);
  await expect(dato(page, 'Domicilio')).toHaveText(CLIENTE_FICTICIO.domicilio);
  await expect(dato(page, 'Email')).toHaveText(CLIENTE_FICTICIO.email);
  await expect(dato(page, 'Nro. de Teléfono')).toHaveText(CLIENTE_FICTICIO.telefono);
  expect((await consultarPresupuesto(page.request, grabado.numero)).cliente).toEqual({
    apellido: CLIENTE_FICTICIO.apellido,
    nombre: CLIENTE_FICTICIO.nombre,
    dni: CLIENTE_FICTICIO.dni,
    domicilio: CLIENTE_FICTICIO.domicilio,
    email: CLIENTE_FICTICIO.email,
    telefono: CLIENTE_FICTICIO.telefono,
  });

  // AC-17: la línea muestra las siete columnas de FR-13.
  await expect(tablaLineas(page).getByRole('columnheader')).toHaveText(COLUMNAS_LINEA);
  await expect(filasLineas(page)).toHaveCount(1);
  await expect(filasLineas(page).first().getByRole('cell')).toHaveText(celdasLinea1850(articulo));
});

test('sin domicilio, email ni telefono graba', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo1850(page.request, 'AC04');

  await abrirNuevoPresupuesto(page);
  await cargarCliente(page, CLIENTE_MINIMO);
  await buscarYAgregar(page, articulo);
  const grabado = await grabarDesdePantalla(page);

  // AC-04: grabó sin reclamar los tres opcionales, que quedan vacíos.
  await expect(mensajeConfirmacion(page)).toHaveText(confirmacion(grabado.numero));
  expect(grabado.cliente).toMatchObject({ domicilio: null, email: null, telefono: null });
  await reabrir(page, grabado.numero);
  await expect(dato(page, 'Apellido')).toHaveText(CLIENTE_MINIMO.apellido);
  await expect(dato(page, 'Domicilio')).toHaveText(SIN_DATO);
  await expect(dato(page, 'Email')).toHaveText(SIN_DATO);
  await expect(dato(page, 'Nro. de Teléfono')).toHaveText(SIN_DATO);
});

test('DNI 23.456.789 se guarda y se muestra 23456789', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo1850(page.request, 'AC05');

  await abrirNuevoPresupuesto(page);
  // El DNI es el ejemplo del PRD, con nombres ficticios (D-36).
  await cargarCliente(page, { ...CLIENTE_MINIMO, dni: '23.456.789' });
  await buscarYAgregar(page, articulo);
  const grabado = await grabarDesdePantalla(page);

  // AC-05: se guarda sin puntos y así se muestra al volver a abrirlo.
  expect((await consultarPresupuesto(page.request, grabado.numero)).cliente.dni).toBe('23456789');
  await reabrir(page, grabado.numero);
  await expect(dato(page, 'DNI')).toHaveText('23456789');
});

test('email ana@mail.com graba', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo1850(page.request, 'AC08');

  await abrirNuevoPresupuesto(page);
  // El email es el ejemplo del PRD (D-36): solo es texto de entrada, nunca se le envía nada.
  await cargarCliente(page, { ...CLIENTE_MINIMO, email: 'ana@mail.com' });
  await buscarYAgregar(page, articulo);
  const grabado = await grabarDesdePantalla(page);

  // AC-08: grabado con ese email.
  await expect(mensajeConfirmacion(page)).toHaveText(confirmacion(grabado.numero));
  expect((await consultarPresupuesto(page.request, grabado.numero)).cliente.email).toBe('ana@mail.com');
  await reabrir(page, grabado.numero);
  await expect(dato(page, 'Email')).toHaveText('ana@mail.com');
});

test('despues del 154 el siguiente es el 155', async ({ page }) => {
  // Unas 140 altas por la Api, una tras otra, para llegar al 154: más que el plazo por defecto de 30 s en una PC lenta.
  test.setTimeout(120_000);
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo1850(page.request, 'AC09');

  // Graba por la Api hasta que el último número es 154 (D-22).
  let ultimo = await grabarPorApi(page.request, articulo.codigo);
  expect(ultimo, 'La base ya pasó el 154 antes de esta prueba').toBeLessThanOrEqual(154);
  while (ultimo < 154) {
    const siguiente = await grabarPorApi(page.request, articulo.codigo);
    expect(siguiente).toBe(ultimo + 1);
    ultimo = siguiente;
  }
  expect((await consultarPresupuesto(page.request, 154)).numero).toBe(154);
  expect((await page.request.get('/api/presupuestos/155')).status()).toBe(404);

  // AC-09: el siguiente, grabado desde la pantalla, es el 155.
  await abrirNuevoPresupuesto(page);
  await cargarCliente(page, CLIENTE_MINIMO);
  await buscarYAgregar(page, articulo);
  const grabado = await grabarDesdePantalla(page);
  expect(grabado.numero).toBe(155);
  await expect(dato(page, 'Número')).toHaveText('155');
  await expect(mensajeConfirmacion(page)).toHaveText(confirmacion(155));
});

test('queda en Borrador y el mensaje de confirmacion lo dice', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo1850(page.request, 'AC12');

  await abrirNuevoPresupuesto(page);
  await cargarCliente(page, CLIENTE_MINIMO);
  await buscarYAgregar(page, articulo);
  const grabado = await grabarDesdePantalla(page);

  // AC-12: el mensaje de confirmación nombra el estado, y el presupuesto queda en Borrador.
  await expect(mensajeConfirmacion(page)).toHaveText(confirmacion(grabado.numero));
  await expect(mensajeConfirmacion(page)).toContainText('Estado: Borrador.');
  await expect(dato(page, 'Estado')).toHaveText('Borrador');
  expect(grabado.estado).toBe('borrador');
  await reabrir(page, grabado.numero);
  await expect(dato(page, 'Estado')).toHaveText('Borrador');
  expect((await consultarPresupuesto(page.request, grabado.numero)).estado).toBe('borrador');
});

test('buscar armazon lista activos con y sin tilde y ningun inactivo', async ({ page }) => {
  await ingresarConConfiguracion(page);
  // Única prueba con "Armazón" o "armazon" en la descripción: la búsqueda tiene que listar estos activos y nada más.
  const conTilde = await crearArticulo(page.request, {
    codigoProveedor: 'PR-AC13-ACTIVO-TILDE',
    descripcion: 'Armazón metálico de prueba',
    precioCosto: 1000,
    margenUtilidad: 0,
  });
  const sinTilde = await crearArticulo(page.request, {
    codigoProveedor: 'PR-AC13-ACTIVO-SIN-TILDE',
    descripcion: 'Estuche para armazon de prueba',
    precioCosto: 500,
    margenUtilidad: 0,
  });
  const inactivoConTilde = await crearArticulo(page.request, {
    codigoProveedor: 'PR-AC13-INACTIVO-TILDE',
    descripcion: 'Armazón de acetato inactivo',
    precioCosto: 1000,
    margenUtilidad: 0,
  });
  const inactivoSinTilde = await crearArticulo(page.request, {
    codigoProveedor: 'PR-AC13-INACTIVO-SIN-TILDE',
    descripcion: 'armazon de titanio inactivo',
    precioCosto: 1000,
    margenUtilidad: 0,
  });
  await desactivar(page.request, inactivoConTilde.codigo);
  await desactivar(page.request, inactivoSinTilde.codigo);

  await abrirNuevoPresupuesto(page);
  await buscar(page, 'armazon');

  // AC-13: los dos activos, con y sin tilde, y ningún inactivo.
  // La primera columna de los resultados es el Código: se leen todos los listados para compararlos con los activos.
  const codigos = tablaResultados(page).locator('tbody tr td:first-child');
  await expect(codigos).toHaveCount(2);
  const listados = (await codigos.allTextContents()).map(Number).sort((x, y) => x - y);
  expect(listados).toEqual([conTilde.codigo, sinTilde.codigo].sort((x, y) => x - y));
  await expect(botonAgregar(page, conTilde)).toBeVisible();
  await expect(botonAgregar(page, sinTilde)).toBeVisible();
  await expect(botonAgregar(page, inactivoConTilde)).toHaveCount(0);
  await expect(botonAgregar(page, inactivoSinTilde)).toHaveCount(0);
});

test('buscar por codigo lista el articulo activo', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, {
    codigoProveedor: 'PR-AC14',
    descripcion: 'Líquido limpiador de prueba AC14',
    precioCosto: 300,
    margenUtilidad: 0,
  });

  await abrirNuevoPresupuesto(page);
  await buscar(page, String(articulo.codigo));

  // AC-14: el artículo activo aparece en el listado (otros códigos que contienen esos dígitos también pueden).
  const fila = tablaResultados(page).locator('tbody tr').filter({ has: botonAgregar(page, articulo) });
  await expect(fila).toHaveCount(1);
  await expect(fila.getByRole('cell').nth(0)).toHaveText(String(articulo.codigo));
  await expect(fila.getByRole('cell').nth(1)).toHaveText(articulo.descripcion);
});

test('articulo de 1.850,00 completa la linea con precio unitario de solo lectura', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo1850(page.request, 'AC15');

  await abrirNuevoPresupuesto(page);
  await buscarYAgregar(page, articulo);

  // AC-15: Código, Descripción y Precio Unitario 1.850,00 (en la columna en pesos, D-34).
  await expect(tablaLineas(page).getByRole('columnheader').nth(2)).toHaveText('Precio Unitario ($)');
  await expect(filasLineas(page)).toHaveCount(1);
  const celdas = filasLineas(page).first().getByRole('cell');
  await expect(celdas.nth(0)).toHaveText(String(articulo.codigo));
  await expect(celdas.nth(1)).toHaveText(articulo.descripcion);
  const precioUnitario = page.getByLabel('Precio unitario de la línea 1', { exact: true });
  await expect(precioUnitario).toHaveValue('1.850,00');

  // No se puede modificar: el campo es de solo lectura y escribir en él no cambia el valor.
  await expect(precioUnitario).toHaveAttribute('readonly', '');
  await expect(precioUnitario).not.toBeEditable();
  await precioUnitario.focus();
  await page.keyboard.press('End');
  await page.keyboard.type('9');
  await page.keyboard.press('Backspace');
  await expect(precioUnitario).toHaveValue('1.850,00');
});

test('la linea se graba con cantidad 1, descuento 0 y precios de 1.850,00', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo1850(page.request, 'AC18');

  await abrirNuevoPresupuesto(page);
  await cargarCliente(page, CLIENTE_MINIMO);
  await buscarYAgregar(page, articulo);
  // Antes de grabar, la línea ya muestra los valores de FR-14. El Precio Unitario está en un campo de solo lectura (su
  // celda no tiene texto propio) y la última celda es la del botón Quitar.
  const enPantalla = celdasLinea1850(articulo);
  enPantalla[2] = '';
  await expect(filasLineas(page).first().getByRole('cell')).toHaveText([...enPantalla, 'Quitar']);
  await expect(page.getByLabel('Precio unitario de la línea 1', { exact: true })).toHaveValue('1.850,00');
  const grabado = await grabarDesdePantalla(page);

  // AC-18: lo grabado (importes de la Api en pesos, D-33) y lo que muestra el presupuesto al volver a abrirlo.
  const guardado = await consultarPresupuesto(page.request, grabado.numero);
  expect(guardado.lineas).toEqual([
    {
      codigoArticulo: articulo.codigo,
      descripcion: articulo.descripcion,
      precioUnitario: 1850,
      cantidad: 1,
      porcentajeDescuento: 0,
      precioConDescuento: 1850,
      precioFinal: 1850,
    },
  ]);
  await reabrir(page, grabado.numero);
  await expect(filasLineas(page)).toHaveCount(1);
  await expect(filasLineas(page).first().getByRole('cell')).toHaveText(celdasLinea1850(articulo));
});

test('la fecha es la del dia y se muestra al reabrir', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo1850(page.request, 'AC19');

  await abrirNuevoPresupuesto(page);
  await cargarCliente(page, CLIENTE_MINIMO);
  await buscarYAgregar(page, articulo);
  // La Api corre en esta misma PC: su fecha local es la de este proceso (D-06, D-22). Se lee antes y después de grabar
  // por si la prueba cruza la medianoche.
  const antes = hoyLocal();
  const grabado = await grabarDesdePantalla(page);
  const despues = hoyLocal();

  // AC-19: la fecha guardada es la del día, y se muestra dd/mm/aaaa al volver a abrirlo.
  expect([antes, despues]).toContain(grabado.fecha);
  expect((await consultarPresupuesto(page.request, grabado.numero)).fecha).toBe(grabado.fecha);
  await expect(dato(page, 'Fecha')).toHaveText(comoSeMuestra(grabado.fecha));
  await reabrir(page, grabado.numero);
  await expect(dato(page, 'Fecha')).toHaveText(comoSeMuestra(grabado.fecha));
});
