import { expect, test, type APIRequestContext, type Page, type Response } from '@playwright/test';

/**
 * Verificación con ARCA de una emisión que ARCA no recibió (FEAT-008: AC-06 y AC-17) de punta a punta, en Chrome y
 * Edge (AC-14).
 *
 * Este archivo tiene su propia Api por navegador, con ARCA simulada que no responde la primera solicitud de factura del
 * proceso y autoriza las siguientes (`Arca__Simulado__Respuesta=no_responder_una_vez`, D-19), un tiempo máximo de 2 s y
 * una espera de 8 s antes de poder verificar (`Arca__EsperaVerificacionSegundos=8`, D-20, D-31; los demás archivos
 * usan 3 s). La primera emisión no
 * registra nada en el simulador: al verificar, el número pedido no figura autorizado y la verificación emite la factura
 * en esa misma acción, con el número que corresponde según FR-01 (el 0003-00000001, porque el simulador no autorizó
 * ninguno todavía). Que la consulta va antes del envío (AC-06) lo prueban las pruebas de .NET; acá se ve el resultado.
 *
 * AC-17: otra pestaña con la misma sesión abre el presupuesto antes de emitir. Mientras la primera emisión todavía
 * espera a ARCA, la pendiente ya está grabada: la otra pestaña vuelve a leer el presupuesto (con la web en caché) y pide
 * verificar. La Api responde los segundos que faltan sin consultar ni emitir, y la pendiente queda. El pedido tiene que
 * llegar dentro de los 8 s desde que la emisión quedó pendiente: la recarga y el clic suelen tardar menos de 1 s, pero
 * un navegador detenido varios segundos haría fallar la prueba (no la volvería verde por error: la Api consultaría y
 * emitiría, y el 409 `espera` no llegaría).
 *
 * Solo hay una prueba: la Api cuenta "la primera solicitud" una vez por proceso. Sus comprobantes no tienen validez
 * fiscal (R-10). Datos personales: el cliente es ficticio, con un DNI que empieza con 00 y un email en example.com.
 */

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-verificacion-emite-1';

/** La espera de esta Api antes de poder verificar (`espera: 8` en `ARCA_POR_ARCHIVO`, compilacion.mjs). */
const ESPERA_MS = 8_000;
const MENSAJE_SIN_RESPUESTA = 'ARCA no respondió. La emisión quedó pendiente de verificación.';
const AVISO_PENDIENTE = 'La emisión de la factura de este presupuesto está pendiente de verificación.';
const COMPROBANTE = 'Factura B 0003-00000001';

const CONFIGURACION = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: 100000,
  puntoVenta: 3,
};

const CLIENTE = {
  apellido: 'Ficticio',
  nombre: 'Cliente de Reemisión',
  dni: '00718294',
  domicilio: null,
  email: 'cliente.reemision@example.com',
  telefono: null,
};

const campo = (page: Page, etiqueta: string) => page.getByLabel(etiqueta, { exact: true });
const botonFacturar = (page: Page) => page.getByRole('button', { name: 'Facturar', exact: true });
const botonVerificar = (page: Page) => page.getByRole('button', { name: 'Verificar con ARCA', exact: true });
const escaparRegex = (texto: string) => texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const dato = (page: Page, etiqueta: string) =>
  page
    .locator('dl.datos > div')
    .filter({ has: page.locator('dt', { hasText: new RegExp(`^${escaparRegex(etiqueta)}$`) }) })
    .locator('dd');
const esRuta = (url: string, ruta: string) => new URL(url).pathname === ruta;

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
  const respuesta = await page.request.post('/api/configuracion', { data: CONFIGURACION });
  expect(respuesta.status(), 'No se pudo grabar la configuración').toBe(200);
}

/** Un artículo y un presupuesto en Final con una línea, grabados por la Api como en la app. */
async function grabarFinal(request: APIRequestContext, codigoProveedor: string): Promise<number> {
  const articulo = await request.post('/api/articulos', {
    data: { codigoProveedor, descripcion: `Artículo de reemisión ${codigoProveedor}`, precioCosto: 1000, margenUtilidad: 0 },
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

test('AC-17 y AC-06: antes de la espera informa los segundos; después, la verificación emite la factura', async ({
  page,
}) => {
  test.setTimeout(90_000);
  await ingresar(page);
  const numero = await grabarFinal(page.request, 'VE-1');
  const rutaEmision = `/api/presupuestos/${numero}/factura`;
  const rutaVerificacion = `${rutaEmision}/verificacion`;
  await page.goto(`/presupuestos/${numero}`);
  await expect(dato(page, 'Número')).toHaveText(String(numero));

  let emisiones = 0;
  let verificaciones = 0;
  page.on('request', (pedido) => {
    if (esRuta(pedido.url(), rutaEmision)) emisiones++;
    if (esRuta(pedido.url(), rutaVerificacion)) verificaciones++;
  });
  let emision: Promise<Response> | undefined;

  // La otra pestaña, con la misma sesión, se abre antes de emitir: la carga en frío no corre contra la espera.
  const otra = await page.context().newPage();
  try {
    await otra.goto(`/presupuestos/${numero}`);
    await expect(dato(otra, 'Número')).toHaveText(String(numero));
    await expect(botonFacturar(otra)).toBeEnabled();

    // La primera solicitud a ARCA no tiene respuesta: la emisión queda esperando los 2 s del tiempo máximo.
    await botonFacturar(page).click();
    const dialogo = page.getByRole('dialog', { name: 'Facturar' });
    await dialogo.getByRole('radio', { name: 'Productos', exact: true }).check();
    emision = page.waitForResponse((r) => esRuta(r.url(), rutaEmision));
    await dialogo.getByRole('button', { name: 'Emitir', exact: true }).click();

    // AC-17: con la pendiente ya grabada (antes de enviar a ARCA, FR-01), la otra pestaña vuelve a leer el presupuesto
    // (con la web ya en caché) y pide verificar.
    await expect.poll(async () => (await leer(page.request, numero)).emisionPendiente, { intervals: [50] }).toBe(true);
    await otra.reload();
    await expect(dato(otra, 'Número')).toHaveText(String(numero));
    // AC-02 en la otra pestaña: con la pendiente, "Verificar con ARCA" y no Facturar.
    await expect(botonVerificar(otra)).toBeVisible();
    await expect(botonFacturar(otra)).toHaveCount(0);
    const [espera] = await Promise.all([
      otra.waitForResponse((r) => esRuta(r.url(), rutaVerificacion)),
      botonVerificar(otra).click(),
    ]);
    expect(espera.status()).toBe(409);
    const cuerpo = (await espera.json()) as { motivo: string; segundosRestantes: number };
    expect(cuerpo.motivo).toBe('espera');
    expect(cuerpo.segundosRestantes).toBeGreaterThanOrEqual(1);
    expect(cuerpo.segundosRestantes).toBeLessThanOrEqual(ESPERA_MS / 1_000);
    const unidad = cuerpo.segundosRestantes === 1 ? 'segundo' : 'segundos';
    await expect(otra.getByRole('alert')).toHaveText(
      `Esperá ${cuerpo.segundosRestantes} ${unidad} para verificar con ARCA.`,
    );
    // Sin consultar ni emitir: la pendiente queda y el botón sigue.
    await expect(botonVerificar(otra)).toBeVisible();
    await expect(dato(otra, 'Factura')).toHaveCount(0);
  } finally {
    await otra.close();
  }

  // La emisión original termina sin respuesta, como si la verificación de la otra pestaña no hubiera existido.
  expect(emision, 'No se envió la emisión').toBeDefined();
  const respuestaEmision = await emision!;
  const desde = Date.now();
  expect(respuestaEmision.status()).toBe(409);
  expect(((await respuestaEmision.json()) as { motivo: string }).motivo).toBe('sin_respuesta');
  await expect(page.getByRole('alert')).toHaveText(MENSAJE_SIN_RESPUESTA);
  await expect(botonVerificar(page)).toBeVisible();
  await expect(botonFacturar(page)).toHaveCount(0);
  await expect(page.getByText(AVISO_PENDIENTE, { exact: true })).toBeVisible();
  const pendiente = await leer(page.request, numero);
  expect(pendiente.emisionPendiente).toBe(true);
  expect(pendiente.factura).toBeNull();

  // AC-06: pasada la espera, el número pedido no figura autorizado y la verificación emite en esa misma acción.
  const falta = ESPERA_MS - (Date.now() - desde);
  if (falta > 0) {
    await page.waitForTimeout(falta);
  }
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => esRuta(r.url(), rutaVerificacion)),
    botonVerificar(page).click(),
  ]);
  expect(respuesta.status()).toBe(200);
  const cuerpo = (await respuesta.json()) as { resultado: string; factura: { comprobante: string } };
  expect(cuerpo.resultado).toBe('emitida');
  expect(cuerpo.factura.comprobante).toBe(COMPROBANTE);
  await expect(page.getByText(`Se emitió la ${COMPROBANTE}.`, { exact: true })).toBeVisible();
  await expect(dato(page, 'Factura')).toHaveText(COMPROBANTE);
  await expect(botonVerificar(page)).toHaveCount(0);
  await expect(botonFacturar(page)).toBeDisabled();
  // Una sola emisión desde la página y una sola verificación: la página no reintenta (FR-12).
  expect(emisiones).toBe(1);
  expect(verificaciones).toBe(1);

  const releido = await leer(page.request, numero);
  expect(releido.emisionPendiente).toBe(false);
  expect(releido.factura).toEqual({ comprobante: COMPROBANTE });
});
