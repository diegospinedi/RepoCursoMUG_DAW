import { expect, test, type APIRequestContext, type Locator, type Page, type Request } from '@playwright/test';

/**
 * Búsqueda con sugerencias mientras se escribe (FEAT-013: AC-01, AC-03 a AC-08, AC-11 a AC-14 y R-04) de punta a punta,
 * contra la Api real que sirve la SPA compilada, en Chrome y Edge (AC-14, NFR-04).
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva. Las pruebas van en orden
 * (`mode: 'serial'`) porque la primera necesita la base vacía: sus artículos de AC-03 son el 1 al 10, así el 7 es uno
 * de los suyos (si no, falla diciéndolo). Las demás siembran por la Api, con la sesión de la página, lo que buscan,
 * con una palabra propia en la descripción ("teclado", "medido", "cordón", "ñandú", "inyectado"), y no usan los datos
 * de otra. La de AC-04 graba Borradores hasta que el último número es 154 y falla si la base ya lo pasó. Los
 * presupuestos se graban como en la app, en Borrador y después a Final (AGENTS.md), y la factura sale de ARCA simulada
 * (`Arca__Entorno=Simulado`): no sale de la máquina ni tiene validez fiscal.
 *
 * Datos personales: los clientes son ficticios, con DNI que empiezan con 00 y sin email. Las sugerencias de
 * presupuestos y de facturas no muestran el DNI (R-02, D-15) y el anuncio dice solo la cantidad.
 *
 * Las esperas son por estado o por respuesta. La única medida de tiempo es la de AC-13: el tiempo desde la última tecla
 * hasta que se ven las sugerencias, medido en el navegador (`performance.now()`), sin esperas fijas. El volumen de
 * 10.000 artículos de AC-13 se mide en la Api (`Volumen_BuscarArmazon_P95MenorA700ms`, D-17).
 *
 * Cualquier violación de la política de seguridad de contenido, y cualquier error en la consola después de iniciar la
 * sesión, hace fallar la prueba (R-04).
 */
test.describe.configure({ mode: 'serial' });
/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-busqueda-en-vivo-1';

/** Responsable Inscripto (Factura B), alícuota 21, múltiplo 0,01, tope de $100.000 y punto de venta 3. */
const PARTIDA = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: 100000,
  puntoVenta: 3,
};

interface Cliente {
  apellido: string;
  nombre: string;
  dni: string;
}

/** El cliente de los presupuestos de relleno: no coincide con ninguna búsqueda de este archivo. */
const RELLENO: Cliente = { apellido: 'Ficticio Relleno', nombre: 'Cliente de Sugerencias', dni: '00.999.991' };
/** AC-04: "Pérez", el cliente del presupuesto 155. */
const PEREZ: Cliente = { apellido: 'Pérez', nombre: 'Ana Sugerida', dni: '00.123.457' };
/** AC-05: el cliente de la factura. */
const QUIROGA: Cliente = { apellido: 'Quiroga', nombre: 'Luis Sugerido', dni: '00.718.295' };
/** AC-06: el cliente del Borrador al que se le agrega una línea. */
const BORRADOR: Cliente = { apellido: 'Ficticio Borrador', nombre: 'Cliente de Agregado', dni: '00.999.992' };

const MENSAJE_CONDICION_DE_LA_LISTA = 'Elegí una condición fiscal de la lista.';

interface Articulo {
  codigo: number;
  codigoProveedor: string;
  descripcion: string;
  precioVenta: number;
}

interface PresupuestoGrabado {
  numero: number;
  estado: string;
  version: number;
  lineas: { codigoArticulo: number }[];
}

let violacionesCsp: string[] = [];
/** Los errores de la consola y de la página desde que se inició la sesión (los 401 previos al ingreso no cuentan). */
let erroresDeConsola: string[] = [];

test.beforeEach(({ page }) => {
  violacionesCsp = [];
  erroresDeConsola = [];
  page.on('console', (mensaje) => {
    if (/Content Security Policy/i.test(mensaje.text())) {
      violacionesCsp.push(mensaje.text());
    } else if (mensaje.type() === 'error') {
      erroresDeConsola.push(mensaje.text());
    }
  });
  page.on('pageerror', (error) => erroresDeConsola.push(error.message));
});

test.afterEach(() => {
  expect(violacionesCsp, 'La SPA no debe violar Content-Security-Policy').toEqual([]);
  expect(erroresDeConsola, 'Ningún error en la consola después de ingresar').toEqual([]);
});

const combobox = (page: Page, etiqueta: string) => page.getByRole('combobox', { name: etiqueta, exact: true });
/** La lista de sugerencias del campo: oculta (y fuera del árbol de accesibilidad) mientras está cerrada. */
const lista = (page: Page, etiqueta: string) => page.getByRole('listbox', { name: etiqueta, exact: true });
const opciones = (page: Page, etiqueta: string) => lista(page, etiqueta).getByRole('option');
/** La región que anuncia la cantidad de sugerencias de ese campo (D-11): el `status` que sigue a su combobox. */
const anuncio = (page: Page, etiqueta: string) =>
  combobox(page, etiqueta).locator('xpath=following-sibling::*[@role="status"]');
const botonBuscar = (page: Page) => page.getByRole('button', { name: 'Buscar', exact: true });
const escaparRegex = (texto: string) => texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const dato = (page: Page, etiqueta: string) =>
  page
    .locator('dl.datos > div')
    .filter({ has: page.locator('dt', { hasText: new RegExp(`^${escaparRegex(etiqueta)}$`) }) })
    .locator('dd');

/** Sin mayúsculas ni acentos (salvo la ñ), como compara la Api: para verificar qué coincide con lo escrito. */
const plegar = (texto: string) =>
  texto
    .normalize('NFD')
    .replace(/(?!̃)\p{M}/gu, '')
    .normalize('NFC')
    .toLowerCase();

const soloDigitos = (dni: string) => dni.replace(/\D/g, '');

/** El listado de artículos con un texto (la búsqueda en vivo o "Buscar"); el inicial no lleva `texto`. */
const busquedaDeArticulos = (url: string) => {
  const direccion = new URL(url);
  return direccion.pathname === '/api/articulos' ? direccion.searchParams.get('texto') : null;
};
const esBusquedaDePresupuestos = (pedido: Request) =>
  pedido.method() === 'POST' && new URL(pedido.url()).pathname === '/api/presupuestos/busqueda';
const esBusquedaDeFacturas = (pedido: Request) =>
  pedido.method() === 'POST' && new URL(pedido.url()).pathname === '/api/facturacion/busqueda';

/**
 * Entra como la operadora (en la base nueva define la contraseña), deja por la Api la configuración de partida y
 * empieza a contar los errores de la consola desde acá.
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
  const respuesta = await page.request.post('/api/configuracion', { data: PARTIDA });
  expect(respuesta.status(), 'No se pudo fijar la configuración de partida').toBe(200);
  erroresDeConsola = [];
}

/** Graba por la Api un artículo (con margen 0, el precio de venta es el costo); devuelve lo que respondió. */
async function crearArticulo(request: APIRequestContext, codigoProveedor: string, descripcion: string, precioCosto = 1000) {
  const respuesta = await request.post('/api/articulos', {
    data: { codigoProveedor, descripcion, precioCosto, margenUtilidad: 0 },
  });
  expect(respuesta.status(), `No se pudo crear ${codigoProveedor}`).toBe(201);
  return (await respuesta.json()) as Articulo;
}

/** Graba por la Api un presupuesto en Borrador con una línea de ese artículo. */
async function grabarBorrador(request: APIRequestContext, cliente: Cliente, codigoArticulo: number) {
  const respuesta = await request.post('/api/presupuestos', {
    data: { cliente, lineas: [{ codigoArticulo, cantidad: 1, porcentajeDescuento: 0 }] },
  });
  expect(respuesta.status(), 'No se pudo grabar el presupuesto por la Api').toBe(201);
  const grabado = (await respuesta.json()) as PresupuestoGrabado;
  expect(grabado.estado).toBe('borrador');
  return grabado;
}

/** Pasa el Borrador a Final y emite su factura por la Api (ARCA simulada autoriza); devuelve el comprobante. */
async function facturar(request: APIRequestContext, presupuesto: PresupuestoGrabado) {
  const final = await request.put(`/api/presupuestos/${presupuesto.numero}/estado`, {
    data: { estado: 'final', version: presupuesto.version },
  });
  expect(final.status(), 'No se pudo pasar a Final por la Api').toBe(200);
  const respuesta = await request.post(`/api/presupuestos/${presupuesto.numero}/factura`, {
    data: { concepto: 'productos' },
  });
  expect(respuesta.status(), 'No se pudo emitir la factura por la Api').toBe(201);
  return ((await respuesta.json()) as { comprobante: string }).comprobante;
}

/** Abre una pantalla desde el inicio, como la operadora, y espera su título. */
async function abrirDesdeElInicio(page: Page, enlace: string, titulo: string) {
  await page.goto('/');
  await page.getByRole('link', { name: enlace, exact: true }).click();
  await expect(page.getByRole('heading', { level: 1, name: titulo, exact: true })).toBeVisible();
  await expect(page.getByText(/^Cargando/)).toHaveCount(0);
}

/** Las sugerencias abiertas, con el campo expandido (D-11), y sus textos. */
async function sugerenciasVisibles(page: Page, etiqueta: string) {
  await expect(lista(page, etiqueta)).toBeVisible();
  await expect(combobox(page, etiqueta)).toHaveAttribute('aria-expanded', 'true');
  return (await opciones(page, etiqueta).allTextContents()).map((texto) => texto.trim());
}

/** La lista cerrada: oculta, el campo sin expandir y sin opción marcada, y el anuncio vacío. */
async function listaCerrada(page: Page, etiqueta: string) {
  await expect(lista(page, etiqueta)).toBeHidden();
  await expect(combobox(page, etiqueta)).toHaveAttribute('aria-expanded', 'false');
  await expect(combobox(page, etiqueta)).not.toHaveAttribute('aria-activedescendant', /.+/);
  await expect(anuncio(page, etiqueta)).toHaveText('');
}

/** Los textos de la columna `indice` de las filas de una tabla, sin espacios de más. */
async function columna(tabla: Locator, indice: number) {
  return (await tabla.locator('tbody tr').locator(`td:nth-child(${indice + 1})`).allTextContents()).map((t) => t.trim());
}

test('sugiere y filtra artículos y abre la edición', async ({ page }) => {
  await ingresar(page);
  // AC-03: en la base nueva, los diez "Armazón" son los artículos 1 a 10; el 7 es uno de ellos.
  const armazones: Articulo[] = [];
  for (let n = 1; n <= 10; n++) {
    const sufijo = String(n).padStart(2, '0');
    armazones.push(await crearArticulo(page.request, `SG-A${sufijo}`, `Armazón acetato sugerido ${sufijo}`));
  }
  expect(armazones.map((a) => a.codigo), 'La base no empezó en el artículo 1').toEqual(
    Array.from({ length: 10 }, (_, i) => i + 1),
  );
  const otros = [
    await crearArticulo(page.request, 'SG-L01', 'Lente orgánico sugerido'),
    await crearArticulo(page.request, 'SG-L02', 'Cristal mineral sugerido'),
  ];

  await abrirDesdeElInicio(page, 'Artículos', 'Artículos');
  const tabla = page.getByRole('table', { name: 'Artículos del catálogo' });
  // La lista inicial (sin texto) muestra todo el catálogo, también los que no contienen "arm".
  await expect(tabla).toContainText(otros[0]!.descripcion);

  // AC-01: "arm" y la pausa: hasta 8 sugerencias (código, código en el proveedor y descripción) que contienen "arm",
  // y la tabla filtrada con el mismo texto, sin apretar "Buscar".
  await combobox(page, 'Texto a buscar').pressSequentially('arm');
  const sugeridas = await sugerenciasVisibles(page, 'Texto a buscar');
  expect(sugeridas).toEqual(
    armazones.slice(0, 8).map((a) => `${a.codigo} · ${a.codigoProveedor} · ${a.descripcion}`),
  );
  await expect(tabla.locator('tbody tr')).toHaveCount(10);
  for (const descripcion of await columna(tabla, 2)) {
    expect(plegar(descripcion)).toContain('arm');
  }
  await expect(tabla).not.toContainText(otros[0]!.descripcion);
  await expect(tabla).not.toContainText(otros[1]!.descripcion);
  await expect(anuncio(page, 'Texto a buscar')).toHaveText('8 sugerencias');

  // AC-03: elegir la sugerencia del artículo 7 abre su edición.
  const siete = armazones[6]!;
  await opciones(page, 'Texto a buscar').filter({ hasText: siete.descripcion }).click();
  await expect(page).toHaveURL(new RegExp(`/articulos/${siete.codigo}$`));
  await expect(page.getByRole('heading', { name: 'Editar artículo', exact: true })).toBeVisible();
  await expect(page.getByLabel('Código en el proveedor', { exact: true })).toHaveValue(siete.codigoProveedor);
});

test('sugiere presupuestos y facturas y los abre', async ({ page }) => {
  test.setTimeout(180_000);
  await ingresar(page);
  const articulo = await crearArticulo(page.request, 'SG-PRESUPUESTOS', 'Artículo de presupuestos sugeridos', 500);

  // AC-04: Borradores de relleno hasta que el último número es 154; el 155 es de Pérez. Falla si la base ya lo pasó.
  let ultimo = 0;
  while (ultimo < 154) {
    ultimo = (await grabarBorrador(page.request, RELLENO, articulo.codigo)).numero;
  }
  expect(ultimo, 'La base ya tenía más de 154 presupuestos').toBe(154);
  const perez = await grabarBorrador(page.request, PEREZ, articulo.codigo);
  expect(perez.numero).toBe(155);
  // AC-05: un presupuesto de Quiroga en Final, facturado.
  const quiroga = await grabarBorrador(page.request, QUIROGA, articulo.codigo);
  const comprobante = await facturar(page.request, quiroga);

  // AC-04: "pér" en Apellido sugiere el 155 (número, apellido y nombre, y fecha; sin DNI), filtra la tabla y lo abre.
  await abrirDesdeElInicio(page, 'Buscar presupuestos', 'Buscar presupuestos');
  const busquedas: Request[] = [];
  page.on('request', (pedido) => {
    if (esBusquedaDePresupuestos(pedido)) busquedas.push(pedido);
  });
  await combobox(page, 'Apellido').pressSequentially('pér');
  const presupuestos = await sugerenciasVisibles(page, 'Apellido');
  expect(presupuestos).toHaveLength(1);
  expect(presupuestos[0]).toMatch(new RegExp(`^155 · Pérez, ${PEREZ.nombre} · \\d{2}/\\d{2}/\\d{4}$`));
  expect(presupuestos[0]).not.toContain(soloDigitos(PEREZ.dni));
  await expect(anuncio(page, 'Apellido')).toHaveText('1 sugerencia');
  const tablaPresupuestos = page.getByRole('table', { name: 'Presupuestos encontrados' });
  await expect.poll(() => columna(tablaPresupuestos, 0)).toEqual(['155']);
  // Los filtros viajan en el cuerpo y no en la URL (ADR-019, D-15).
  expect(busquedas.length).toBeGreaterThan(0);
  for (const pedido of busquedas) {
    expect(new URL(pedido.url()).search).toBe('');
  }
  expect(JSON.parse(busquedas.at(-1)!.postData() ?? '{}')).toMatchObject({ apellido: 'pér', pagina: 1 });
  expect(new URL(page.url()).search).toBe('');

  // AC-14 con teclado: flecha abajo marca la sugerencia y Enter la elige.
  await combobox(page, 'Apellido').press('ArrowDown');
  await expect(opciones(page, 'Apellido').first()).toHaveAttribute('aria-selected', 'true');
  await combobox(page, 'Apellido').press('Enter');
  await expect(page).toHaveURL(/\/presupuestos\/155$/);
  await expect(dato(page, 'Número')).toHaveText('155');
  await expect(dato(page, 'Apellido')).toHaveText(PEREZ.apellido);

  // AC-05: parte del apellido en Facturas sugiere la factura (comprobante, cliente y fecha; sin DNI) y la abre.
  await abrirDesdeElInicio(page, 'Facturas', 'Buscar facturas');
  await combobox(page, 'Apellido').pressSequentially('quir');
  const facturas = await sugerenciasVisibles(page, 'Apellido');
  expect(facturas).toHaveLength(1);
  expect(facturas[0]).toMatch(
    new RegExp(`^${escaparRegex(comprobante)} · Quiroga, ${QUIROGA.nombre} · \\d{2}/\\d{2}/\\d{4}$`),
  );
  expect(facturas[0]).not.toContain(soloDigitos(QUIROGA.dni));
  await expect(anuncio(page, 'Apellido')).toHaveText('1 sugerencia');
  const tablaFacturas = page.getByRole('table', { name: 'Facturas encontradas' });
  await expect.poll(() => columna(tablaFacturas, 0)).toEqual([comprobante]);

  await opciones(page, 'Apellido').first().click();
  await expect(page).toHaveURL(new RegExp(`/facturas/${quiroga.numero}$`));
  await expect(page.getByRole('heading', { name: 'Factura', exact: true })).toBeVisible();
  await expect(dato(page, 'Comprobante')).toHaveText(comprobante);
  await expect(dato(page, 'Apellido')).toHaveText(QUIROGA.apellido);
});

test('agrega la sugerencia al presupuesto', async ({ page }) => {
  await ingresar(page);
  const inicial = await crearArticulo(page.request, 'SG-AG-INICIAL', 'Paño de limpieza inicial', 300);
  const cordon = await crearArticulo(page.request, 'SG-AG-CORDON', 'Cordón sugerido para anteojos', 450);
  const borrador = await grabarBorrador(page.request, BORRADOR, inicial.codigo);

  // AC-06: dentro del Borrador, escribir en el buscador de artículos y elegir la sugerencia agrega la línea.
  await page.goto(`/presupuestos/${borrador.numero}`);
  await expect(dato(page, 'Número')).toHaveText(String(borrador.numero));
  await page.getByRole('link', { name: 'Modificar', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/presupuestos/${borrador.numero}/editar$`));
  await expect(page.getByRole('heading', { name: 'Modificar presupuesto', exact: true })).toBeVisible();
  const lineas = page.getByRole('table', { name: 'Líneas del presupuesto' }).locator('tbody tr');
  await expect(lineas).toHaveCount(1);

  await combobox(page, 'Texto a buscar').pressSequentially('cordón sug');
  expect(await sugerenciasVisibles(page, 'Texto a buscar')).toEqual([
    `${cordon.codigo} · ${cordon.codigoProveedor} · ${cordon.descripcion}`,
  ]);
  await opciones(page, 'Texto a buscar').first().click();
  await expect(lineas).toHaveCount(2);
  await expect(lineas.nth(1)).toContainText(cordon.descripcion);
  await expect(lineas.nth(1)).toContainText('450,00');
  await listaCerrada(page, 'Texto a buscar');

  // Grabada la modificación, el Borrador tiene las dos líneas.
  const [respuesta] = await Promise.all([
    page.waitForResponse(
      (r) => r.request().method() === 'PUT' && new URL(r.url()).pathname === `/api/presupuestos/${borrador.numero}`,
    ),
    page.getByRole('button', { name: 'Grabar', exact: true }).click(),
  ]);
  expect(respuesta.status()).toBe(200);
  const grabado = (await respuesta.json()) as PresupuestoGrabado;
  expect(grabado.estado).toBe('borrador');
  expect(grabado.lineas.map((l) => l.codigoArticulo)).toEqual([inicial.codigo, cordon.codigo]);
});

test('teclado y anuncio de sugerencias', async ({ page }) => {
  await ingresar(page);
  const estuches: Articulo[] = [];
  for (let n = 1; n <= 9; n++) {
    estuches.push(await crearArticulo(page.request, `SG-TK${n}`, `Estuche de teclado ${n}`));
  }
  await abrirDesdeElInicio(page, 'Artículos', 'Artículos');
  const campo = combobox(page, 'Texto a buscar');
  await expect(campo).toHaveAttribute('aria-autocomplete', 'list');
  await listaCerrada(page, 'Texto a buscar');

  // Abierta con 9 coincidencias: muestra 8, anuncia solo la cantidad (NFR-03, R-02) y no marca ninguna.
  await campo.pressSequentially('teclado');
  expect(await sugerenciasVisibles(page, 'Texto a buscar')).toHaveLength(8);
  await expect(anuncio(page, 'Texto a buscar')).toHaveText('8 sugerencias');
  await expect(campo).not.toHaveAttribute('aria-activedescendant', /.+/);
  const opcion = (indice: number) => opciones(page, 'Texto a buscar').nth(indice);

  // AC-07: flecha abajo marca la siguiente; flecha arriba, la anterior. El campo apunta a la marcada.
  await campo.press('ArrowDown');
  await expect(opcion(0)).toHaveAttribute('aria-selected', 'true');
  await expect(campo).toHaveAttribute('aria-activedescendant', (await opcion(0).getAttribute('id'))!);
  await campo.press('ArrowDown');
  await expect(opcion(1)).toHaveAttribute('aria-selected', 'true');
  await expect(opcion(0)).toHaveAttribute('aria-selected', 'false');
  await expect(campo).toHaveAttribute('aria-activedescendant', (await opcion(1).getAttribute('id'))!);
  await campo.press('ArrowUp');
  await expect(opcion(0)).toHaveAttribute('aria-selected', 'true');

  // AC-07: Escape cierra la lista sin vaciar el campo ni salir de la pantalla.
  await campo.press('Escape');
  await listaCerrada(page, 'Texto a buscar');
  await expect(campo).toHaveValue('teclado');
  await expect(campo).toBeFocused();
  await expect(page).toHaveURL(/\/articulos$/);

  // Tab cierra la lista sin elegir.
  await campo.press('ArrowDown');
  await expect(lista(page, 'Texto a buscar')).toBeVisible();
  await campo.press('Tab');
  await listaCerrada(page, 'Texto a buscar');
  await expect(page).toHaveURL(/\/articulos$/);

  // AC-07: con el foco de vuelta, flecha abajo abre y marca la primera; Enter elige la marcada y abre su edición.
  await campo.focus();
  await campo.press('ArrowDown');
  await expect(opcion(0)).toHaveAttribute('aria-selected', 'true');
  const primera = (await opcion(0).textContent())!.trim();
  const elegido = estuches.find((e) => primera === `${e.codigo} · ${e.codigoProveedor} · ${e.descripcion}`);
  expect(elegido, `La sugerencia "${primera}" no es uno de los estuches sembrados`).toBeDefined();
  await campo.press('Enter');
  await expect(page).toHaveURL(new RegExp(`/articulos/${elegido!.codigo}$`));
  await expect(page.getByRole('heading', { name: 'Editar artículo', exact: true })).toBeVisible();
});

test('un pedido por pausa y sugerencias en menos de un segundo', async ({ page }) => {
  await ingresar(page);
  for (let n = 1; n <= 3; n++) {
    await crearArticulo(page.request, `SG-MD${n}`, `Armazón medido ${n}`);
  }
  await abrirDesdeElInicio(page, 'Artículos', 'Artículos');
  const campo = combobox(page, 'Texto a buscar');

  // En el navegador: la hora de la última tecla y la de la primera vez que se ven sugerencias después de ella.
  await campo.evaluate((nodo) => {
    const listaDeSugerencias = document.getElementById(nodo.getAttribute('aria-controls') ?? '');
    if (listaDeSugerencias === null) throw new Error('El campo no apunta a su lista');
    const medida = { ultimaTecla: 0, sugerencias: 0 };
    Object.assign(window, { medidaDeSugerencias: medida });
    nodo.addEventListener('input', () => {
      medida.ultimaTecla = performance.now();
      medida.sugerencias = 0;
    });
    new MutationObserver(() => {
      if (medida.sugerencias === 0 && !listaDeSugerencias.hidden && listaDeSugerencias.querySelector('[role="option"]')) {
        medida.sugerencias = performance.now();
      }
    }).observe(listaDeSugerencias, { attributes: true, childList: true, subtree: true });
  });
  const textos: string[] = [];
  page.on('request', (pedido) => {
    const texto = busquedaDeArticulos(pedido.url());
    if (texto !== null) textos.push(texto);
  });

  // AC-13: "armazón" seguido, sin pausa entre teclas (30 ms; la pausa de la búsqueda es de 300 ms).
  await campo.pressSequentially('armazón', { delay: 30 });
  const sugeridas = await sugerenciasVisibles(page, 'Texto a buscar');
  expect(sugeridas.length).toBeGreaterThanOrEqual(3);
  for (const sugerida of sugeridas) {
    expect(plegar(sugerida)).toContain('armazon');
  }

  // NFR-02: un solo pedido, con el texto completo. Uno de un texto parcial habría salido antes de la última tecla.
  expect(textos).toEqual(['armazón']);
  // NFR-01: desde la última tecla hasta las sugerencias, menos de 1 segundo (300 ms de pausa más la Api).
  const medida = await page.evaluate(
    () => (window as unknown as { medidaDeSugerencias: { ultimaTecla: number; sugerencias: number } }).medidaDeSugerencias,
  );
  expect(medida.sugerencias, 'No se registró cuándo aparecieron las sugerencias').toBeGreaterThan(0);
  const demora = medida.sugerencias - medida.ultimaTecla;
  expect(demora, `Las sugerencias tardaron ${Math.round(demora)} ms`).toBeLessThan(1000);
  // Y no antes de la pausa: no se pidió en cada tecla.
  expect(demora).toBeGreaterThanOrEqual(300);
});

test('configuración filtra la alícuota y rechaza una condición inexistente', async ({ page }) => {
  await ingresar(page);
  const grabaciones: string[] = [];
  page.on('request', (pedido) => {
    if (pedido.method() === 'POST' && new URL(pedido.url()).pathname === '/api/configuracion') {
      grabaciones.push(pedido.url());
    }
  });
  await abrirDesdeElInicio(page, 'Configuración', 'Configuración');
  const alicuota = combobox(page, 'Alícuota de IVA (%)');
  const condicion = combobox(page, 'Condición fiscal');
  await expect(alicuota).toHaveValue('21');
  await expect(condicion).toHaveValue('Responsable Inscripto');

  // AC-11: "10" en la alícuota muestra solo la opción 10,5, que se elige con el teclado.
  await alicuota.fill('');
  expect(await sugerenciasVisibles(page, 'Alícuota de IVA (%)')).toEqual(['0', '2,5', '5', '10,5', '21', '27']);
  await alicuota.pressSequentially('10');
  expect(await sugerenciasVisibles(page, 'Alícuota de IVA (%)')).toEqual(['10,5']);
  await expect(anuncio(page, 'Alícuota de IVA (%)')).toHaveText('1 sugerencia');
  await alicuota.press('ArrowDown');
  await alicuota.press('Enter');
  await expect(alicuota).toHaveValue('10,5');
  await listaCerrada(page, 'Alícuota de IVA (%)');

  // FR-13: sin distinguir mayúsculas ni acentos.
  await condicion.fill('');
  await condicion.pressSequentially('MONÓ');
  expect(await sugerenciasVisibles(page, 'Condición fiscal')).toEqual(['Monotributo']);

  // AC-12: un texto que no coincide con ninguna opción no muestra opciones, no se graba y muestra el error junto al campo.
  await condicion.fill('Exento');
  await listaCerrada(page, 'Condición fiscal');
  await page.getByRole('button', { name: 'Grabar', exact: true }).click();
  await expect(condicion).toHaveAttribute('aria-invalid', 'true');
  const idMensaje = await condicion.getAttribute('aria-describedby');
  expect(idMensaje, 'El campo no apunta a su mensaje').toBeTruthy();
  await expect(page.locator(`[id="${idMensaje}"]`)).toHaveText(MENSAJE_CONDICION_DE_LA_LISTA);
  await expect(page.getByRole('status').filter({ hasText: 'La configuración se grabó.' })).toHaveCount(0);
  const vigente = await page.request.get('/api/configuracion');
  expect(await vigente.json()).toMatchObject({ alicuotaIva: 21, condicionFiscal: 'responsable_inscripto' });
  expect(grabaciones, 'La pantalla envió una condición que debía rechazar').toEqual([]);
});

test('Buscar sigue funcionando', async ({ page }) => {
  await ingresar(page);
  const gamuzas = [
    await crearArticulo(page.request, 'SG-BU1', 'Gamuza de ñandú 1'),
    await crearArticulo(page.request, 'SG-BU2', 'Gamuza de ñandú 2'),
  ];
  // Un texto de 1 carácter no se busca en vivo (AC-02, D-07): el pedido que llega es el de "Buscar" o Enter, como antes.
  const UN_CARACTER = { timeout: 5_000 };

  // AC-08 en Artículos: "Buscar" filtra la tabla como antes y no abre sugerencias.
  await abrirDesdeElInicio(page, 'Artículos', 'Artículos');
  await combobox(page, 'Texto a buscar').fill('ñ');
  const [articulos] = await Promise.all([
    page.waitForResponse((r) => busquedaDeArticulos(r.url()) === 'ñ', UN_CARACTER),
    botonBuscar(page).click(),
  ]);
  expect(articulos.status()).toBe(200);
  const tabla = page.getByRole('table', { name: 'Artículos del catálogo' });
  await expect(tabla).toContainText(gamuzas[0]!.descripcion);
  await expect(tabla).toContainText(gamuzas[1]!.descripcion);
  for (const descripcion of await columna(tabla, 2)) {
    expect(plegar(descripcion)).toContain('ñ');
  }
  await listaCerrada(page, 'Texto a buscar');

  // AC-08 en el presupuesto: "Buscar" llena la tabla de artículos activos y "Agregar" agrega la línea.
  await abrirDesdeElInicio(page, 'Nuevo presupuesto', 'Nuevo presupuesto');
  await combobox(page, 'Texto a buscar').fill('ñ');
  await Promise.all([
    page.waitForResponse((r) => busquedaDeArticulos(r.url()) === 'ñ', UN_CARACTER),
    botonBuscar(page).click(),
  ]);
  const activos = page.getByRole('table', { name: 'Artículos activos' });
  await expect(activos).toContainText(gamuzas[1]!.descripcion);
  await listaCerrada(page, 'Texto a buscar');
  await page.getByRole('button', { name: `Agregar ${gamuzas[1]!.codigo} ${gamuzas[1]!.descripcion}`, exact: true }).click();
  const lineas = page.getByRole('table', { name: 'Líneas del presupuesto' }).locator('tbody tr');
  await expect(lineas).toHaveCount(1);
  await expect(lineas.first()).toContainText(gamuzas[1]!.descripcion);

  // AC-08 en Buscar presupuestos: Enter sin una sugerencia marcada busca como hoy, con los filtros en el cuerpo.
  const articulo = await crearArticulo(page.request, 'SG-BU-PRESUPUESTO', 'Artículo del presupuesto buscado', 200);
  const buscado = await grabarBorrador(page.request, { ...RELLENO, apellido: 'Buscado Ficticio' }, articulo.codigo);
  await abrirDesdeElInicio(page, 'Buscar presupuestos', 'Buscar presupuestos');
  await combobox(page, 'Apellido').fill('b');
  const [pedido] = await Promise.all([
    page.waitForRequest(
      (r) => esBusquedaDePresupuestos(r) && JSON.parse(r.postData() ?? '{}').apellido === 'b',
      UN_CARACTER,
    ),
    combobox(page, 'Apellido').press('Enter'),
  ]);
  expect(new URL(pedido.url()).search).toBe('');
  await expect
    .poll(() => columna(page.getByRole('table', { name: 'Presupuestos encontrados' }), 0))
    .toContain(String(buscado.numero));
  await listaCerrada(page, 'Apellido');

  // AC-08 en Facturas: "Buscar" busca como hoy.
  await abrirDesdeElInicio(page, 'Facturas', 'Buscar facturas');
  await combobox(page, 'Apellido').fill('x');
  await Promise.all([
    page.waitForRequest(
      (r) => esBusquedaDeFacturas(r) && JSON.parse(r.postData() ?? '{}').apellido === 'x',
      UN_CARACTER,
    ),
    botonBuscar(page).click(),
  ]);
  await expect(page.getByText('No hay facturas que coincidan con la búsqueda.', { exact: true })).toBeVisible();
  await listaCerrada(page, 'Apellido');
});

test('sin violaciones de CSP ni error en la consola', async ({ page }) => {
  await ingresar(page);
  // R-04: una descripción con HTML se ve como texto: ni se carga la imagen ni corre el manejador.
  const inyectado = await crearArticulo(
    page.request,
    'SG-XSS',
    '<img src="sugerencia-inexistente.png" onerror="window.sugerenciaEjecutada = true">Paño inyectado',
  );

  await abrirDesdeElInicio(page, 'Artículos', 'Artículos');
  await combobox(page, 'Texto a buscar').pressSequentially('inyectado');
  expect(await sugerenciasVisibles(page, 'Texto a buscar')).toEqual([
    `${inyectado.codigo} · ${inyectado.codigoProveedor} · ${inyectado.descripcion}`,
  ]);
  await expect(lista(page, 'Texto a buscar').locator('img')).toHaveCount(0);
  expect(await page.evaluate(() => 'sugerenciaEjecutada' in window)).toBe(false);

  // El buscador del presupuesto, con la misma descripción.
  await abrirDesdeElInicio(page, 'Nuevo presupuesto', 'Nuevo presupuesto');
  await combobox(page, 'Texto a buscar').pressSequentially('inyectado');
  expect(await sugerenciasVisibles(page, 'Texto a buscar')).toHaveLength(1);
  await expect(lista(page, 'Texto a buscar').locator('img')).toHaveCount(0);

  // Presupuestos y facturas: escribir, esperar la respuesta en vivo y cerrar con Escape.
  for (const [enlace, titulo, esBusqueda] of [
    ['Buscar presupuestos', 'Buscar presupuestos', esBusquedaDePresupuestos],
    ['Facturas', 'Buscar facturas', esBusquedaDeFacturas],
  ] as const) {
    await abrirDesdeElInicio(page, enlace, titulo);
    await Promise.all([
      page.waitForResponse((r) => esBusqueda(r.request()) && JSON.parse(r.request().postData() ?? '{}').nombre === 'fi'),
      combobox(page, 'Nombre').pressSequentially('fi'),
    ]);
    await combobox(page, 'Nombre').press('Escape');
    await listaCerrada(page, 'Nombre');
  }

  // Configuración: el filtro local de la alícuota.
  await abrirDesdeElInicio(page, 'Configuración', 'Configuración');
  await combobox(page, 'Alícuota de IVA (%)').fill('2');
  expect(await sugerenciasVisibles(page, 'Alícuota de IVA (%)')).toEqual(['2,5', '21', '27']);
  await combobox(page, 'Alícuota de IVA (%)').press('Escape');

  expect(await page.evaluate(() => 'sugerenciaEjecutada' in window)).toBe(false);
  expect(violacionesCsp, 'Ninguna violación de la CSP en los cuatro buscadores ni en Configuración').toEqual([]);
  expect(erroresDeConsola, 'Ningún error en la consola en los cuatro buscadores ni en Configuración').toEqual([]);
});
