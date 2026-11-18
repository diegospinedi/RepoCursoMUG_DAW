import { expect, test, type APIRequestContext, type Page, type Request } from '@playwright/test';

/**
 * Listado y búsqueda de facturas (FEAT-009: AC-17 a AC-24 y AC-26) de punta a punta, contra la Api real que sirve la
 * SPA compilada, en Chrome y Edge (AC-25).
 *
 * ARCA es el simulador (`Arca__Entorno=Simulado`): no sale de la máquina. servidor.mjs deja el contador de la Factura B
 * del punto de venta 3 en 124 y el de la Factura C en 34560 (`ARCA_POR_ARCHIVO` de compilacion.mjs): la primera
 * Factura B de esta base es la 0003-00000125 (AC-23) y la primera Factura C, la 0003-00034561 (AC-19).
 *
 * Las facturas se siembran por la Api, como en la app: cada presupuesto se graba en Borrador, pasa a Final y se factura
 * (AGENTS.md); ninguna fecha se modifica en la base. Todas las facturas tienen la fecha del día en que se emiten, así que
 * los filtros de fecha se prueban alrededor de ese día (el anterior, el mismo y el siguiente), como la búsqueda de
 * presupuestos; los rangos con las fechas de marzo de 2026 de AC-18 y AC-22 se prueban en .NET con facturas fechadas
 * (`Repositorio_ApellidoYDesde_SoloGonzalez20`, `Filtro_DesdePosteriorAHasta_Rechazado`).
 *
 * Las pruebas corren en orden porque dependen de lo grabado. La primera graba los presupuestos 1 a 154 en Borrador y
 * factura el 155 (la 0003-00000125 de AC-23 y AC-24); la segunda y la tercera facturan los clientes de los filtros; la
 * última factura Borradores de la primera hasta llegar a 120 facturas (AC-17) y recién entonces busca sin filtros.
 *
 * Datos personales: los clientes son ficticios, con DNI que empiezan con 00 (D-08) y emails en example.com.
 *
 * FEAT-013: Número de comprobante, Apellido, Nombre y DNI buscan también mientras se escribe (tras una pausa de 300 ms).
 * Estas pruebas siguen probando "Buscar" y Enter (AC-08 de FEAT-013): esperan la respuesta y no el pedido, porque un
 * pedido en vivo que el envío manual cancela no tiene respuesta, y la grilla se espera por su contenido. Las sugerencias
 * tienen su propio archivo, `busqueda-en-vivo.spec.ts`.
 */
test.describe.configure({ mode: 'serial' });

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-facturas-busqueda-1';

const TITULO_PANTALLA = 'Buscar facturas';
const TITULO_RESULTADOS = 'Facturas encontradas';
const MENSAJE_SIN_COINCIDENCIAS = 'No hay facturas que coincidan con la búsqueda.';
const MENSAJE_DESDE_POSTERIOR_A_HASTA = 'La fecha Desde no puede ser posterior a Hasta.';

/** Responsable Inscripto (Factura B), alícuota 21, punto de venta 3 y tope de $100.000: ninguna identifica al receptor. */
const CONFIGURACION = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: 100000,
  puntoVenta: 3,
};
/** Monotributo: la Factura C de AC-19. */
const MONOTRIBUTO = { ...CONFIGURACION, condicionFiscal: 'monotributo' };

interface Cliente {
  apellido: string;
  nombre: string;
  dni: string;
  email?: string;
}

/** El cliente de los Borradores que completan las 120 facturas de AC-17: no coincide con ninguna búsqueda. */
const PAGINADO: Cliente = { apellido: 'Ficticio Paginado', nombre: 'Cliente de Facturas', dni: '00.999.999' };
/** El cliente del presupuesto 155 (AC-23, AC-24). */
const ORIGEN: Cliente = { apellido: 'Ficticio Origen', nombre: 'Cliente del Presupuesto', dni: '00.555.111' };
/** AC-18 y AC-21: "González, María José" y "Gómez, Ana". */
const GONZALEZ: Cliente = {
  apellido: 'González',
  nombre: 'María José',
  dni: '00.111.222',
  email: 'maria.jose.facturas@example.com',
};
const GOMEZ: Cliente = { apellido: 'Gómez', nombre: 'Ana', dni: '00.111.333' };
/** AC-20: el DNI 00345678 (ficticio, D-08); ningún otro cliente de este archivo contiene "3456" ni "00345" antes de AC-26. */
const DOCUMENTO: Cliente = { apellido: 'Ficticio Documento', nombre: 'Cliente con DNI', dni: '00.345.678' };
/** El cliente de la Factura C 0003-00034561 de AC-19. */
const NUMERADO: Cliente = { apellido: 'Ficticio Numerado', nombre: 'Cliente de Factura C', dni: '00.777.888' };
/** AC-26: "Pérez, Ana" con DNI 00123456, facturada sin identificar al receptor. */
const PEREZ: Cliente = { apellido: 'Pérez', nombre: 'Ana', dni: '00.123.456' };

const PRIMERA_FACTURA_B = 'Factura B 0003-00000125';
const PRIMERA_FACTURA_C = 'Factura C 0003-00034561';

interface PresupuestoGrabado {
  numero: number;
  estado: string;
  version: number;
  total: number;
}

interface FacturaEmitida {
  comprobante: string;
  numero: number;
  /** El número del presupuesto facturado (no viene en la respuesta de la Api: lo agrega {@link facturar}). */
  presupuesto: number;
}

/** Los filtros de la pantalla; un campo ausente queda vacío. Fechas `yyyy-MM-dd`, como las recibe un campo de fecha. */
interface Filtros {
  apellido?: string;
  nombre?: string;
  dni?: string;
  numero?: string;
  desde?: string;
  hasta?: string;
}

/** Los Borradores que graba la primera prueba; la última los factura hasta completar 120 facturas. */
const borradores: PresupuestoGrabado[] = [];
/** Todas las facturas emitidas en esta base, en el orden en que se emitieron. */
const emitidas: FacturaEmitida[] = [];

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

/** Los filtros de texto son campos con sugerencias (FEAT-013): la etiqueta nombra también su lista, así que van por el rol. */
const FILTROS_DE_TEXTO = ['Número de comprobante', 'Apellido', 'Nombre', 'DNI'];
const campo = (page: Page, etiqueta: string) =>
  FILTROS_DE_TEXTO.includes(etiqueta)
    ? page.getByRole('combobox', { name: etiqueta, exact: true })
    : page.getByLabel(etiqueta, { exact: true });
const botonBuscar = (page: Page) => page.getByRole('button', { name: 'Buscar', exact: true });
const tablaResultados = (page: Page) => page.getByRole('table', { name: TITULO_RESULTADOS });
const filasResultados = (page: Page) => tablaResultados(page).locator('tbody tr');
const paginacion = (page: Page) => page.getByRole('navigation', { name: 'Páginas de los resultados' });
const escaparRegex = (texto: string) => texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const dato = (page: Page, etiqueta: string) =>
  page
    .locator('dl.datos > div')
    .filter({ has: page.locator('dt', { hasText: new RegExp(`^${escaparRegex(etiqueta)}$`) }) })
    .locator('dd');

const esBusqueda = (pedido: Request) =>
  pedido.method() === 'POST' && new URL(pedido.url()).pathname === '/api/facturacion/busqueda';

/** El DNI como lo guarda la Api: solo los dígitos. */
const soloDigitos = (dni: string) => dni.replace(/\D/g, '');

/** "Factura B 0003-00000125" de una Factura B del punto de venta 3. */
const facturaB = (numero: number) => `Factura B 0003-${String(numero).padStart(8, '0')}`;

/** `yyyy-MM-dd` como dd/mm/aaaa. */
function comoSeMuestra(fecha: string): string {
  const [anio, mes, dia] = fecha.split('-');
  return `${dia}/${mes}/${anio}`;
}

/** El día anterior o siguiente de una fecha `yyyy-MM-dd`, sin pasar por la zona horaria de la máquina. */
function otroDia(fecha: string, dias: number): string {
  const [anio, mes, dia] = fecha.split('-').map(Number);
  return new Date(Date.UTC(anio!, mes! - 1, dia! + dias)).toISOString().slice(0, 10);
}

/** Entra como la operadora (en la base nueva define la contraseña) y deja por la Api la configuración de partida. */
async function ingresarConConfiguracion(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/(definir-contrasena|ingreso)$/);
  await campo(page, 'Contraseña').fill(CONTRASENA);
  if (/\/definir-contrasena$/.test(page.url())) {
    await page.getByRole('button', { name: 'Definir contraseña' }).click();
  } else {
    await page.getByRole('button', { name: 'Ingresar' }).click();
  }
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await configurar(page.request, CONFIGURACION);
}

/** Graba la configuración por la Api, con la sesión de la página. */
async function configurar(request: APIRequestContext, datos: object) {
  const respuesta = await request.post('/api/configuracion', { data: datos });
  expect(respuesta.status(), 'No se pudo grabar la configuración').toBe(200);
}

/** Graba por la Api un artículo con ese costo y sin margen (el precio de venta es el costo); devuelve su código. */
async function crearArticulo(request: APIRequestContext, codigoProveedor: string, precioCosto: number) {
  const respuesta = await request.post('/api/articulos', {
    data: { codigoProveedor, descripcion: `Artículo de facturas ${codigoProveedor}`, precioCosto, margenUtilidad: 0 },
  });
  expect(respuesta.status(), `No se pudo crear ${codigoProveedor}`).toBe(201);
  return ((await respuesta.json()) as { codigo: number }).codigo;
}

/** Graba por la Api un presupuesto en Borrador con una línea de ese artículo. */
async function grabarBorrador(request: APIRequestContext, cliente: Cliente, codigoArticulo: number) {
  const respuesta = await request.post('/api/presupuestos', {
    data: { cliente, lineas: [{ codigoArticulo, cantidad: 1, porcentajeDescuento: 0 }] },
  });
  expect(respuesta.status(), 'No se pudo grabar el presupuesto por la Api').toBe(201);
  return (await respuesta.json()) as PresupuestoGrabado;
}

/** Pasa a Final por la Api y emite su factura (ARCA simulada autoriza); la agrega a {@link emitidas}. */
async function facturar(request: APIRequestContext, presupuesto: PresupuestoGrabado): Promise<FacturaEmitida> {
  const final = await request.put(`/api/presupuestos/${presupuesto.numero}/estado`, {
    data: { estado: 'final', version: presupuesto.version },
  });
  expect(final.status(), 'No se pudo pasar a Final por la Api').toBe(200);
  const respuesta = await request.post(`/api/presupuestos/${presupuesto.numero}/factura`, {
    data: { concepto: 'productos' },
  });
  expect(respuesta.status(), 'No se pudo emitir la factura por la Api').toBe(201);
  const { comprobante, numero } = (await respuesta.json()) as { comprobante: string; numero: number };
  const factura: FacturaEmitida = { comprobante, numero, presupuesto: presupuesto.numero };
  emitidas.push(factura);
  return factura;
}

/** Graba un presupuesto de ese cliente y lo factura. */
async function facturarCliente(request: APIRequestContext, cliente: Cliente, codigoArticulo: number) {
  return facturar(request, await grabarBorrador(request, cliente, codigoArticulo));
}

/** La fecha de una factura, según su detalle en la Api. */
async function fechaDeLaFactura(request: APIRequestContext, presupuesto: number): Promise<string> {
  const respuesta = await request.get(`/api/presupuestos/${presupuesto}/factura`);
  expect(respuesta.status()).toBe(200);
  return ((await respuesta.json()) as { fecha: string }).fecha;
}

/** Abre la búsqueda desde el inicio, como la operadora. */
async function abrirBusqueda(page: Page) {
  await page.goto('/');
  await page.getByRole('link', { name: 'Facturas', exact: true }).click();
  await expect(page.getByRole('heading', { name: TITULO_PANTALLA })).toBeVisible();
  await expect(page).toHaveURL(/\/facturas$/);
}

/** Carga los seis filtros (un filtro ausente queda vacío). */
async function cargarFiltros(page: Page, filtros: Filtros) {
  await campo(page, 'Desde').fill(filtros.desde ?? '');
  await campo(page, 'Hasta').fill(filtros.hasta ?? '');
  await campo(page, 'Número de comprobante').fill(filtros.numero ?? '');
  await campo(page, 'Apellido').fill(filtros.apellido ?? '');
  await campo(page, 'Nombre').fill(filtros.nombre ?? '');
  await campo(page, 'DNI').fill(filtros.dni ?? '');
}

/**
 * Carga los filtros, pulsa Buscar y espera la respuesta. Verifica que los filtros viajaron solo en el cuerpo (ADR-019,
 * R-01 del PRD): la URL del pedido no tiene consulta y la de la pantalla no cambia. Devuelve el pedido.
 */
async function buscar(page: Page, filtros: Filtros): Promise<Request> {
  await cargarFiltros(page, filtros);
  const urlAntes = page.url();
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => esBusqueda(r.request())),
    botonBuscar(page).click(),
  ]);
  const pedido = respuesta.request();
  expect(respuesta.status(), 'La búsqueda tiene que responder 200').toBe(200);
  expect(new URL(pedido.url()).search).toBe('');
  expect(page.url()).toBe(urlAntes);
  expect(new URL(page.url()).search).toBe('');
  await expect(botonBuscar(page)).toBeEnabled();
  return pedido;
}

/** Los comprobantes de las filas de la grilla, en el orden en que se muestran. */
async function comprobantesDeLaGrilla(page: Page): Promise<string[]> {
  const textos = await filasResultados(page).locator('td:first-child').allTextContents();
  return textos.map((texto) => texto.trim());
}

/** Busca y espera la grilla con exactamente esos comprobantes (o el mensaje sin coincidencias si la lista es vacía). */
async function buscarYEsperar(page: Page, filtros: Filtros, comprobantes: string[]) {
  const pedido = await buscar(page, filtros);
  if (comprobantes.length === 0) {
    await expect(page.getByText(MENSAJE_SIN_COINCIDENCIAS, { exact: true })).toBeVisible();
    await expect(tablaResultados(page)).toHaveCount(0);
    return pedido;
  }
  // Con espera: la grilla anterior puede seguir en pantalla hasta que React dibuja la respuesta.
  await expect.poll(() => comprobantesDeLaGrilla(page)).toEqual(comprobantes);
  return pedido;
}

test('la Factura B 0003-00000125 del presupuesto 155: resultado y detalle sin acciones de edición', async ({ page }) => {
  test.setTimeout(180_000);
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, 'FB-PAGINAS', 500);

  // Presupuestos 1 a 154 en Borrador (la última prueba factura los que hagan falta para AC-17); los números son
  // consecutivos desde el 1 porque la base es nueva.
  for (let numero = 1; numero <= 154; numero++) {
    const borrador = await grabarBorrador(page.request, PAGINADO, articulo);
    expect(borrador.numero, 'La base no empezó en el presupuesto 1').toBe(numero);
    borradores.push(borrador);
  }
  const origen = await grabarBorrador(page.request, ORIGEN, articulo);
  expect(origen.numero).toBe(155);
  expect((await facturar(page.request, origen)).comprobante).toBe(PRIMERA_FACTURA_B);
  const hoy = await fechaDeLaFactura(page.request, 155);

  // AC-23: el resultado muestra el tipo y 0003-00000125 (como un solo texto, D-15), la fecha, Apellido, Nombre, DNI,
  // total y el presupuesto 155. El número se busca con el guion, que se ignora (FR-20).
  await abrirBusqueda(page);
  await buscarYEsperar(page, { numero: '0003-00000125' }, [PRIMERA_FACTURA_B]);
  await expect(tablaResultados(page).locator('thead th')).toHaveText([
    'Comprobante',
    'Fecha',
    'Apellido',
    'Nombre',
    'DNI',
    'Total ($)',
    'Presupuesto',
  ]);
  await expect(filasResultados(page).first().locator('td')).toHaveText([
    PRIMERA_FACTURA_B,
    comoSeMuestra(hoy),
    ORIGEN.apellido,
    ORIGEN.nombre,
    soloDigitos(ORIGEN.dni),
    '500,00',
    '155',
  ]);

  // AC-24: abrir la factura desde la lista muestra sus datos y el presupuesto 155, sin acciones de edición ni de
  // eliminación (una factura con CAE no se modifica, RF-32).
  await page.getByRole('link', { name: `Abrir la ${PRIMERA_FACTURA_B}`, exact: true }).click();
  await expect(page).toHaveURL(/\/facturas\/155$/);
  await expect(page.getByRole('heading', { name: 'Factura', exact: true })).toBeVisible();
  await expect(dato(page, 'Comprobante')).toHaveText(PRIMERA_FACTURA_B);
  await expect(dato(page, 'Fecha')).toHaveText(comoSeMuestra(hoy));
  await expect(dato(page, 'Presupuesto')).toHaveText('155');
  await expect(dato(page, 'Presupuesto').getByRole('link')).toHaveAttribute('href', '/presupuestos/155');
  await expect(dato(page, 'Apellido')).toHaveText(ORIGEN.apellido);
  await expect(dato(page, 'Nombre')).toHaveText(ORIGEN.nombre);
  await expect(dato(page, 'DNI')).toHaveText(soloDigitos(ORIGEN.dni));
  await expect(dato(page, 'Receptor')).toHaveText('Consumidor Final');
  await expect(dato(page, 'Total ($)')).toHaveText('500,00');
  await expect(dato(page, 'CAE')).toHaveText(/^\d{14}$/);
  await expect(page.getByRole('table', { name: 'Líneas de la factura' }).locator('tbody tr')).toHaveCount(1);
  const acciones = /editar|modificar|eliminar|borrar|anular|grabar|guardar/i;
  await expect(page.getByRole('button', { name: acciones })).toHaveCount(0);
  await expect(page.getByRole('link', { name: acciones })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Descargar PDF', exact: true })).toBeVisible();
  await expect(page.getByRole('textbox')).toHaveCount(0);

  // El enlace al presupuesto lleva al presupuesto 155.
  await dato(page, 'Presupuesto').getByRole('link').click();
  await expect(page).toHaveURL(/\/presupuestos\/155$/);
  await expect(dato(page, 'Número')).toHaveText('155');
});

test('búsqueda por apellido, nombre, DNI, número y fechas', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, 'FB-FILTROS', 1000);
  const gonzalez = await facturarCliente(page.request, GONZALEZ, articulo);
  const gomez = await facturarCliente(page.request, GOMEZ, articulo);
  const documento = await facturarCliente(page.request, DOCUMENTO, articulo);
  // AC-19: la Factura C 0003-00034561, emitida en Monotributo; después se vuelve a Responsable Inscripto.
  await configurar(page.request, MONOTRIBUTO);
  const facturaC = await facturarCliente(page.request, NUMERADO, articulo);
  expect(facturaC.comprobante).toBe(PRIMERA_FACTURA_C);
  await configurar(page.request, CONFIGURACION);

  const hoy = await fechaDeLaFactura(page.request, gonzalez.presupuesto);
  // Las cuatro se emitieron el mismo día: los rangos de abajo valen para todas. Si la corrida cruzó la medianoche,
  // falla acá con este motivo y no con un resultado de búsqueda inesperado.
  for (const factura of [gomez, documento, facturaC]) {
    expect(await fechaDeLaFactura(page.request, factura.presupuesto), `Fecha de la ${factura.comprobante}`).toBe(hoy);
  }
  const ayer = otroDia(hoy, -1);
  const manana = otroDia(hoy, 1);
  await abrirBusqueda(page);

  // AC-18: Apellido "gonzalez" (sin acento ni mayúscula) con Desde: Desde es inclusive y Gómez no coincide.
  await buscarYEsperar(page, { apellido: 'gonzalez', desde: manana }, []);
  const pedido = await buscarYEsperar(page, { apellido: 'gonzalez', desde: hoy }, [gonzalez.comprobante]);
  // Los filtros viajan solo en el cuerpo, sin los vacíos.
  expect(JSON.parse(pedido.postData() ?? '{}')).toEqual({ apellido: 'gonzalez', desde: hoy, pagina: 1 });
  // Los dos límites inclusive, uno solo de los dos, y "GO" está en los dos Apellidos (de la más reciente a la más antigua).
  await buscarYEsperar(page, { apellido: 'GO', desde: hoy, hasta: hoy }, [gomez.comprobante, gonzalez.comprobante]);
  await buscarYEsperar(page, { apellido: 'GO', hasta: ayer }, []);
  await buscarYEsperar(page, { apellido: 'ómez', desde: ayer, hasta: manana }, [gomez.comprobante]);

  // AC-19: el número "34561" encuentra la 0003-00034561, también escrito con el punto de venta y el guion.
  await buscarYEsperar(page, { numero: '34561' }, [PRIMERA_FACTURA_C]);
  await buscarYEsperar(page, { numero: '0003-00034561' }, [PRIMERA_FACTURA_C]);
  await expect(filasResultados(page).first().locator('td').nth(2)).toHaveText(NUMERADO.apellido);

  // AC-20: parte del DNI 00345678, con o sin puntos.
  await buscarYEsperar(page, { dni: '3456' }, [documento.comprobante]);
  await buscarYEsperar(page, { dni: '00.345' }, [documento.comprobante]);

  // AC-21: el Nombre "maria" encuentra "María José" y no "Ana".
  await buscarYEsperar(page, { nombre: 'maria' }, [gonzalez.comprobante]);

  // Los filtros se combinan con "Y": Apellido de González con el DNI de Gómez no encuentra nada (FR-18).
  await buscarYEsperar(page, { apellido: 'gonzalez', dni: soloDigitos(GOMEZ.dni) }, []);

  // AC-22: Desde posterior a Hasta no busca y avisa junto a Desde.
  const pedidos: Request[] = [];
  const registrar = (pedidoNuevo: Request) => {
    if (esBusqueda(pedidoNuevo)) pedidos.push(pedidoNuevo);
  };
  page.on('request', registrar);
  await buscarYEsperar(page, { nombre: 'maria' }, [gonzalez.comprobante]);
  pedidos.length = 0;
  // El Apellido de la búsqueda final se carga ya, con el rango inválido (las fechas se cargan primero): vaciar Nombre y
  // escribir "gonzalez" no piden nada en vivo con ese rango (FEAT-013, D-22), y Buscar rechazado cancela la pausa.
  await cargarFiltros(page, { apellido: 'gonzalez', desde: manana, hasta: hoy });
  await botonBuscar(page).click();
  const desde = campo(page, 'Desde');
  await expect(desde).toHaveAttribute('aria-invalid', 'true');
  const idError = await desde.getAttribute('aria-describedby');
  expect(idError, 'El mensaje tiene que estar vinculado al campo Desde').toBeTruthy();
  await expect(page.locator(`[id="${idError}"]`)).toHaveText(MENSAJE_DESDE_POSTERIOR_A_HASTA);
  await expect(campo(page, 'Hasta')).not.toHaveAttribute('aria-invalid', 'true');
  // La grilla anterior no queda como si fuera el resultado.
  await expect(tablaResultados(page)).toHaveCount(0);
  expect(pedidos, 'AC-22 no envía la búsqueda, ni con Buscar ni en vivo').toHaveLength(0);
  // Con el rango corregido, Enter busca. Solo cambian las fechas, que no buscan en vivo (D-07 de FEAT-013): si el
  // rechazo hubiera enviado algo, aparecería acá como un pedido de más.
  await cargarFiltros(page, { apellido: 'gonzalez', desde: hoy, hasta: hoy });
  await Promise.all([
    page.waitForResponse((r) => esBusqueda(r.request())),
    campo(page, 'Apellido').press('Enter'),
  ]);
  await expect.poll(() => comprobantesDeLaGrilla(page)).toEqual([gonzalez.comprobante]);
  page.off('request', registrar);
  expect(pedidos, 'AC-22 no envía la búsqueda; Enter envía exactamente una').toHaveLength(1);
  expect(JSON.parse(pedidos[0]!.postData() ?? '{}')).toEqual({ apellido: 'gonzalez', desde: hoy, hasta: hoy, pagina: 1 });
});

test('una factura a Consumidor Final se encuentra por el DNI del cliente del presupuesto', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, 'FB-CONSUMIDOR', 1000);
  // $1.000 no supera el tope: la factura no identifica al receptor.
  const perez = await facturarCliente(page.request, PEREZ, articulo);
  const { presupuesto } = perez;
  const detalle = await page.request.get(`/api/presupuestos/${presupuesto}/factura`);
  expect(detalle.status()).toBe(200);
  expect(((await detalle.json()) as { comprobante: string; receptor: string })).toMatchObject({
    comprobante: perez.comprobante,
    receptor: 'Consumidor Final',
  });

  // AC-26: la búsqueda por DNI "00123456" la muestra con Apellido "Pérez", Nombre "Ana" y DNI 00123456 (FR-25).
  await abrirBusqueda(page);
  await buscarYEsperar(page, { dni: '00123456' }, [perez.comprobante]);
  const celdas = filasResultados(page).first().locator('td');
  await expect(celdas.nth(2)).toHaveText('Pérez');
  await expect(celdas.nth(3)).toHaveText('Ana');
  await expect(celdas.nth(4)).toHaveText('00123456');
  await expect(celdas.nth(6)).toHaveText(String(presupuesto));

  // El detalle dice "Consumidor Final" como receptor y muestra el cliente del presupuesto.
  await page.getByRole('link', { name: `Abrir la ${perez.comprobante}`, exact: true }).click();
  await expect(dato(page, 'Receptor')).toHaveText('Consumidor Final');
  await expect(dato(page, 'Apellido')).toHaveText('Pérez');
  await expect(dato(page, 'DNI')).toHaveText('00123456');
});

test('120 facturas: la primera página con las 50 más recientes y la tercera con las 20 más antiguas', async ({ page }) => {
  // Hasta 114 emisiones por la Api, una tras otra: más que el plazo por defecto de 30 s en una PC lenta.
  test.setTimeout(300_000);
  await ingresarConConfiguracion(page);
  expect(emitidas.length, 'Las pruebas anteriores no dejaron sus facturas').toBeGreaterThan(0);

  // Completa las 120 facturas con los Borradores de la primera prueba (AC-17).
  const faltan = 120 - emitidas.length;
  expect(faltan).toBeLessThanOrEqual(borradores.length);
  for (const borrador of borradores.slice(0, faltan)) {
    await facturar(page.request, borrador);
  }
  const contadas = await page.request.post('/api/facturacion/busqueda', { data: { pagina: 1 } });
  expect(((await contadas.json()) as { total: number }).total).toBe(120);

  // Todas son del mismo día: de la más reciente a la más antigua es por número, de mayor a menor (FR-22). Si la corrida
  // cruzó la medianoche, falla acá con este motivo y no con un orden inesperado.
  const primera = emitidas[0]!;
  const ultima = emitidas.at(-1)!;
  expect(
    await fechaDeLaFactura(page.request, ultima.presupuesto),
    `La ${primera.comprobante} y la ${ultima.comprobante} tienen que ser del mismo día (¿la corrida cruzó la medianoche?)`,
  ).toBe(await fechaDeLaFactura(page.request, primera.presupuesto));
  const ordenadas = [...emitidas].sort((a, b) => b.numero - a.numero).map((f) => f.comprobante);
  expect(ordenadas[0]).toBe(PRIMERA_FACTURA_C);
  expect(ordenadas.at(-1)).toBe(PRIMERA_FACTURA_B);
  expect(ordenadas.slice(100)).toEqual(Array.from({ length: 20 }, (_, i) => facturaB(144 - i)));

  await abrirBusqueda(page);
  await buscar(page, {});
  await expect(filasResultados(page)).toHaveCount(50);
  expect(await comprobantesDeLaGrilla(page)).toEqual(ordenadas.slice(0, 50));
  await expect(paginacion(page)).toContainText('Página 1 de 3');
  await expect(paginacion(page).getByRole('button', { name: 'Anterior' })).toBeDisabled();

  await paginacion(page).getByRole('button', { name: 'Siguiente' }).click();
  await expect(paginacion(page)).toContainText('Página 2 de 3');
  await expect.poll(() => comprobantesDeLaGrilla(page)).toEqual(ordenadas.slice(50, 100));

  await paginacion(page).getByRole('button', { name: 'Siguiente' }).click();
  await expect(paginacion(page)).toContainText('Página 3 de 3');
  await expect(filasResultados(page)).toHaveCount(20);
  expect(await comprobantesDeLaGrilla(page)).toEqual(ordenadas.slice(100));
  await expect(paginacion(page).getByRole('button', { name: 'Siguiente' })).toBeDisabled();
});
