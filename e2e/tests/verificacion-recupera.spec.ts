import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

/**
 * Verificación con ARCA de una emisión que ARCA autorizó sin responder (FEAT-008: AC-02, AC-03, AC-04 y AC-05) de punta
 * a punta, en Chrome y Edge (AC-14).
 *
 * Este archivo tiene su propia Api por navegador, con ARCA simulada que registra el comprobante como autorizado y no
 * responde (`Arca__Simulado__Respuesta=autorizar_sin_responder`, D-19), un tiempo máximo de 2 s y una espera de 3 s
 * antes de poder verificar (`Arca__EsperaVerificacionSegundos=3`, D-20, D-31) en lugar del minuto de la óptica. Sus
 * comprobantes no tienen validez fiscal (R-10). El simulador empieza sin archivo: el primer comprobante es el
 * 0003-00000001 y el segundo, el 0003-00000002.
 *
 * Las pruebas van en orden: la primera ingresa en la base nueva, sin emisiones pendientes (AC-04); la segunda deja dos
 * pendientes, comprueba el aviso al ingresar (AC-03) y "Verificar con ARCA" en lugar de Facturar (AC-02), y las verifica
 * después de la espera: cada una recupera el comprobante que ARCA ya tenía, sin emitir otro (AC-05). La configuración
 * se carga por la Api; los artículos usan códigos propios (`VR-…`) y los presupuestos se graban en Borrador y después
 * pasan a Final, como en la app.
 *
 * Datos personales: el cliente es ficticio, con un DNI que empieza con 00 (no se emite, D-50 de FEAT-007) y un email en
 * example.com.
 */
test.describe.configure({ mode: 'serial' });

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-verificacion-recupera-1';

/** La espera del harness antes de poder verificar (`Arca__EsperaVerificacionSegundos=3`). */
const ESPERA_MS = 3_000;
const RUTA_PENDIENTES = '/api/facturacion/emisiones-pendientes';
const MENSAJE_SIN_RESPUESTA = 'ARCA no respondió. La emisión quedó pendiente de verificación.';
const AVISO_PENDIENTE = 'La emisión de la factura de este presupuesto está pendiente de verificación.';

const CONFIGURACION = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: 100000,
  puntoVenta: 3,
};

const CLIENTE = {
  apellido: 'Ficticio',
  nombre: 'Cliente de Verificación',
  dni: '00718294',
  domicilio: null,
  email: 'cliente.verificacion@example.com',
  telefono: null,
};

const campo = (page: Page, etiqueta: string) => page.getByLabel(etiqueta, { exact: true });
const botonFacturar = (page: Page) => page.getByRole('button', { name: 'Facturar', exact: true });
const botonVerificar = (page: Page) => page.getByRole('button', { name: 'Verificar con ARCA', exact: true });
const avisoPendientes = (page: Page) => page.locator('p.aviso', { hasText: 'Hay emisiones pendientes' });
const escaparRegex = (texto: string) => texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const dato = (page: Page, etiqueta: string) =>
  page
    .locator('dl.datos > div')
    .filter({ has: page.locator('dt', { hasText: new RegExp(`^${escaparRegex(etiqueta)}$`) }) })
    .locator('dd');
const esRuta = (url: string, ruta: string) => new URL(url).pathname === ruta;

/**
 * Entra como la operadora desde la página actual (definir la contraseña en la base nueva o ingresar) y devuelve la
 * lista de emisiones pendientes que pidió Inicio al cargar: hay que esperarla antes de afirmar que el aviso no está.
 */
async function entrar(page: Page): Promise<number[]> {
  await expect(page).toHaveURL(/\/(definir-contrasena|ingreso)$/);
  await campo(page, 'Contraseña').fill(CONTRASENA);
  const lista = page.waitForResponse((r) => esRuta(r.url(), RUTA_PENDIENTES));
  if (/\/definir-contrasena$/.test(page.url())) {
    await page.getByRole('button', { name: 'Definir contraseña' }).click();
  } else {
    await page.getByRole('button', { name: 'Ingresar' }).click();
  }
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  const respuesta = await lista;
  expect(respuesta.status()).toBe(200);
  return ((await respuesta.json()) as { presupuestos: number[] }).presupuestos;
}

/** Ingresa en la base (la primera vez define la contraseña) y graba la configuración por la Api. */
async function ingresar(page: Page): Promise<number[]> {
  await page.goto('/');
  const pendientes = await entrar(page);
  const respuesta = await page.request.post('/api/configuracion', { data: CONFIGURACION });
  expect(respuesta.status(), 'No se pudo grabar la configuración').toBe(200);
  return pendientes;
}

/** Sale con el botón de Inicio y vuelve a ingresar: el aviso de FR-03 es el de "al iniciar sesión". */
async function salirEIngresar(page: Page): Promise<number[]> {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await page.getByRole('button', { name: 'Salir', exact: true }).click();
  await expect(page).toHaveURL(/\/ingreso$/);
  return entrar(page);
}

/** Un artículo y un presupuesto en Final con una línea, grabados por la Api como en la app. */
async function grabarFinal(request: APIRequestContext, codigoProveedor: string): Promise<number> {
  const articulo = await request.post('/api/articulos', {
    data: { codigoProveedor, descripcion: `Artículo de verificación ${codigoProveedor}`, precioCosto: 1000, margenUtilidad: 0 },
  });
  expect(articulo.status()).toBe(201);
  const { codigo } = (await articulo.json()) as { codigo: number };
  const alta = await request.post('/api/presupuestos', {
    data: { cliente: CLIENTE, lineas: [{ codigoArticulo: codigo, cantidad: 1, porcentajeDescuento: 0 }] },
  });
  expect(alta.status()).toBe(201);
  const { numero, version } = (await alta.json()) as { numero: number; version: number };
  const final = await request.put(`/api/presupuestos/${numero}/estado`, { data: { estado: 'final', version } });
  expect(final.status()).toBe(200);
  return numero;
}

async function leer(request: APIRequestContext, numero: number) {
  const respuesta = await request.get(`/api/presupuestos/${numero}`);
  expect(respuesta.status()).toBe(200);
  return (await respuesta.json()) as { factura: { comprobante: string } | null; emisionPendiente: boolean };
}

/** Emite desde la pantalla del presupuesto abierto; ARCA autoriza y no responde: queda pendiente (409 `sin_respuesta`). */
async function emitirSinRespuesta(page: Page, numero: number) {
  await botonFacturar(page).click();
  const dialogo = page.getByRole('dialog', { name: 'Facturar' });
  await dialogo.getByRole('radio', { name: 'Productos', exact: true }).check();
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => esRuta(r.url(), `/api/presupuestos/${numero}/factura`)),
    dialogo.getByRole('button', { name: 'Emitir', exact: true }).click(),
  ]);
  expect(respuesta.status()).toBe(409);
  expect(((await respuesta.json()) as { motivo: string }).motivo).toBe('sin_respuesta');
  await expect(page.getByRole('alert')).toHaveText(MENSAJE_SIN_RESPUESTA);
}

/**
 * Espera a que pase la espera de verificación de una pendiente cuya emisión respondió en `desde`: la pendiente se graba
 * antes de enviar a ARCA, así que a los 3 s de la respuesta ya pasaron más de 3 s desde que quedó pendiente.
 */
async function esperarLaVerificacion(page: Page, desde: number) {
  const falta = ESPERA_MS - (Date.now() - desde);
  if (falta > 0) {
    await page.waitForTimeout(falta);
  }
}

/** AC-02: "Verificar con ARCA" en lugar de Facturar, con el aviso de la emisión pendiente. */
async function esperarVerificarEnLugarDeFacturar(page: Page) {
  await expect(botonVerificar(page)).toBeVisible();
  await expect(botonVerificar(page)).toBeEnabled();
  await expect(botonFacturar(page)).toHaveCount(0);
  await expect(page.getByText(AVISO_PENDIENTE, { exact: true })).toBeVisible();
}

test('AC-04: sin emisiones pendientes, Inicio no muestra el aviso', async ({ page }) => {
  const pendientes = await ingresar(page);

  expect(pendientes).toEqual([]);
  await expect(avisoPendientes(page)).toHaveCount(0);
});

test('AC-02, AC-03, AC-05: aviso al ingresar, Verificar con ARCA y recuperación del comprobante ya autorizado', async ({
  page,
}) => {
  test.setTimeout(90_000);
  await ingresar(page);
  const primero = await grabarFinal(page.request, 'VR-1');
  const segundo = await grabarFinal(page.request, 'VR-2');
  expect(segundo).toBeGreaterThan(primero);

  // La primera desde la pantalla: queda pendiente y Facturar desaparece (AC-02).
  await page.goto(`/presupuestos/${primero}`);
  await expect(dato(page, 'Número')).toHaveText(String(primero));
  await emitirSinRespuesta(page, primero);
  const desde = Date.now();
  await esperarVerificarEnLugarDeFacturar(page);

  // La segunda con un pedido directo a la Api, con la sesión.
  const directa = await page.request.post(`/api/presupuestos/${segundo}/factura`, { data: { concepto: 'productos' } });
  expect(directa.status()).toBe(409);
  expect(((await directa.json()) as { motivo: string }).motivo).toBe('sin_respuesta');
  const desdeSegundo = Date.now();

  // AC-03: al iniciar sesión, el aviso nombra los dos presupuestos, cada uno con su enlace.
  expect(await salirEIngresar(page)).toEqual([primero, segundo]);
  await expect(avisoPendientes(page)).toHaveText(
    `Hay emisiones pendientes de verificación en los presupuestos ${primero} y ${segundo}.`,
  );
  await avisoPendientes(page).getByRole('link', { name: String(primero), exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/presupuestos/${primero}$`));
  await expect(dato(page, 'Número')).toHaveText(String(primero));
  await esperarVerificarEnLugarDeFacturar(page);

  // AC-05: pasada la espera, verificar recupera el 0003-00000001 que ARCA ya tenía con el mismo importe. Si hubiera
  // emitido otro, el número sería el 0003-00000003 (el simulador ya registró el 1 y el 2).
  await esperarLaVerificacion(page, desde);
  const rutaVerificacion = `/api/presupuestos/${primero}/factura/verificacion`;
  let verificaciones = 0;
  page.on('request', (pedido) => {
    if (esRuta(pedido.url(), rutaVerificacion)) verificaciones++;
  });
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => esRuta(r.url(), rutaVerificacion)),
    botonVerificar(page).click(),
  ]);
  expect(respuesta.status()).toBe(200);
  const cuerpo = (await respuesta.json()) as { resultado: string; factura: { comprobante: string } };
  expect(cuerpo.resultado).toBe('recuperada');
  expect(cuerpo.factura.comprobante).toBe('Factura B 0003-00000001');
  await expect(page.getByText('Se recuperó la Factura B 0003-00000001 que ARCA ya había autorizado.', { exact: true }))
    .toBeVisible();
  await expect(dato(page, 'Factura')).toHaveText('Factura B 0003-00000001');
  await expect(botonVerificar(page)).toHaveCount(0);
  await expect(page.getByText(AVISO_PENDIENTE, { exact: true })).toHaveCount(0);
  expect(verificaciones).toBe(1);

  // Al volver a abrirlo, la factura sigue y no hay pendiente.
  await page.reload();
  await expect(dato(page, 'Factura')).toHaveText('Factura B 0003-00000001');
  await expect(botonVerificar(page)).toHaveCount(0);
  const releido = await leer(page.request, primero);
  expect(releido.emisionPendiente).toBe(false);
  expect(releido.factura).toEqual({ comprobante: 'Factura B 0003-00000001' });

  // La segunda, verificada con un pedido directo: recupera el 0003-00000002 (tampoco emite otro).
  await esperarLaVerificacion(page, desdeSegundo);
  const segunda = await page.request.post(`/api/presupuestos/${segundo}/factura/verificacion`, {
    headers: { 'Content-Type': 'application/json' },
  });
  expect(segunda.status()).toBe(200);
  const cuerpoSegunda = (await segunda.json()) as { resultado: string; factura: { comprobante: string } };
  expect(cuerpoSegunda.resultado).toBe('recuperada');
  expect(cuerpoSegunda.factura.comprobante).toBe('Factura B 0003-00000002');

  // AC-04 otra vez: resueltas las dos, al ingresar no hay aviso.
  expect(await salirEIngresar(page)).toEqual([]);
  await expect(avisoPendientes(page)).toHaveCount(0);
});
