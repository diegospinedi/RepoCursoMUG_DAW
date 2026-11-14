import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

/**
 * Verificación con ARCA cuyo número figura autorizado con otro importe, descarte y cancelación (FEAT-008: AC-07, AC-08 y
 * AC-09) de punta a punta, en Chrome y Edge (AC-14).
 *
 * Este archivo tiene su propia Api por navegador, con ARCA simulada que no responde la primera solicitud de factura del
 * proceso y autoriza las siguientes (`Arca__Simulado__Respuesta=no_responder_una_vez`, D-19), un tiempo máximo de 2 s y
 * una espera de 3 s antes de poder verificar (`Arca__EsperaVerificacionSegundos=3`). El número con otro importe se
 * arma como pasaría con otra emisión en el mismo punto de venta: el presupuesto A ($ 1.000,00) queda pendiente con el
 * 0003-00000001, que el simulador no registró; después el presupuesto B ($ 2.000,00) se factura y ARCA le autoriza ese
 * mismo 0003-00000001. Al verificar A, ARCA tiene el número con $ 2.000,00: no se emite ni se recupera nada y se avisa
 * revisar el punto de venta (AC-07). Cancelar la confirmación del descarte deja todo igual (AC-09); confirmarlo vuelve
 * Facturar (AC-08), y A se factura con el número siguiente. La fecha y la hora del descarte quedan registradas en la
 * base (lo prueban las pruebas de .NET: la Api no las expone).
 *
 * El archivo del simulador no se siembra: no admite un comprobante con número mayor que su contador (D-27), y un
 * contador que ya incluye el número haría que la emisión de A pidiera el siguiente.
 *
 * Una sola prueba: la Api cuenta "la primera solicitud" una vez por proceso. Sus comprobantes no tienen validez fiscal
 * (R-10). Datos personales: los clientes son ficticios, con un DNI que empieza con 00 y emails en example.com.
 */

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-verificacion-importe-distinto-1';

const ESPERA_MS = 3_000;
const MENSAJE_SIN_RESPUESTA = 'ARCA no respondió. La emisión quedó pendiente de verificación.';
const AVISO_PENDIENTE = 'La emisión de la factura de este presupuesto está pendiente de verificación.';
const COMPROBANTE = 'Factura B 0003-00000001';
const MENSAJE_IMPORTE_DISTINTO =
  `Revisá el punto de venta en ARCA: la ${COMPROBANTE} figura autorizada por $ 2.000,00 y esta venta registró ` +
  '$ 1.000,00.';
const TEXTO_CONFIRMAR_DESCARTE = '¿Descartar la emisión pendiente? El presupuesto va a poder volver a facturarse.';

const CONFIGURACION = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: 100000,
  puntoVenta: 3,
};

const cliente = (nombre: string, email: string) => ({
  apellido: 'Ficticio',
  nombre,
  dni: '00718294',
  domicilio: null,
  email,
  telefono: null,
});

const campo = (page: Page, etiqueta: string) => page.getByLabel(etiqueta, { exact: true });
const botonFacturar = (page: Page) => page.getByRole('button', { name: 'Facturar', exact: true });
const botonVerificar = (page: Page) => page.getByRole('button', { name: 'Verificar con ARCA', exact: true });
const botonDescartar = (page: Page) => page.getByRole('button', { name: 'Descartar la emisión', exact: true });
const dialogoDescarte = (page: Page) => page.getByRole('dialog', { name: 'Descartar la emisión' });
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

/** Un artículo del costo indicado (margen 0) y un presupuesto en Final con una línea, grabados como en la app. */
async function grabarFinal(
  request: APIRequestContext,
  codigoProveedor: string,
  precioCosto: number,
  datosCliente: ReturnType<typeof cliente>,
): Promise<number> {
  const articulo = await request.post('/api/articulos', {
    data: { codigoProveedor, descripcion: `Artículo de importe distinto ${codigoProveedor}`, precioCosto, margenUtilidad: 0 },
  });
  expect(articulo.status()).toBe(201);
  const { codigo } = (await articulo.json()) as { codigo: number };
  const alta = await request.post('/api/presupuestos', {
    data: { cliente: datosCliente, lineas: [{ codigoArticulo: codigo, cantidad: 1, porcentajeDescuento: 0 }] },
  });
  expect(alta.status()).toBe(201);
  const { numero, version, total } = (await alta.json()) as { numero: number; version: number; total: number };
  expect(total).toBe(precioCosto);
  const final = await request.put(`/api/presupuestos/${numero}/estado`, { data: { estado: 'final', version } });
  expect(final.status()).toBe(200);
  return numero;
}

async function leer(request: APIRequestContext, numero: number) {
  const respuesta = await request.get(`/api/presupuestos/${numero}`);
  expect(respuesta.status()).toBe(200);
  return (await respuesta.json()) as {
    factura: { comprobante: string } | null;
    emisionPendiente: boolean;
    observacionEmision: { motivo: string; comprobante: string; importeRegistrado: number; importeArca: number } | null;
  };
}

/** Emite desde el diálogo de Facturar del presupuesto abierto y devuelve la respuesta de la Api. */
async function emitir(page: Page, numero: number) {
  await botonFacturar(page).click();
  const dialogo = page.getByRole('dialog', { name: 'Facturar' });
  await dialogo.getByRole('radio', { name: 'Productos', exact: true }).check();
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => esRuta(r.url(), `/api/presupuestos/${numero}/factura`)),
    dialogo.getByRole('button', { name: 'Emitir', exact: true }).click(),
  ]);
  return respuesta;
}

test('AC-07, AC-09 y AC-08: importe distinto, cancelación del descarte y descarte confirmado', async ({ page }) => {
  test.setTimeout(90_000);
  await ingresar(page);
  const ventaA = await grabarFinal(
    page.request,
    'VI-A',
    1000,
    cliente('Cliente A de Importe Distinto', 'cliente.a.importe@example.com'),
  );
  const ventaB = await grabarFinal(
    page.request,
    'VI-B',
    2000,
    cliente('Cliente B de Importe Distinto', 'cliente.b.importe@example.com'),
  );
  const rutaVerificacion = `/api/presupuestos/${ventaA}/factura/verificacion`;
  const rutaDescarte = `/api/presupuestos/${ventaA}/factura/descarte`;

  // A: la primera solicitud del proceso queda sin respuesta, pendiente con el 0003-00000001 de $ 1.000,00.
  await page.goto(`/presupuestos/${ventaA}`);
  await expect(dato(page, 'Número')).toHaveText(String(ventaA));
  const emisionA = await emitir(page, ventaA);
  const desde = Date.now();
  expect(emisionA.status()).toBe(409);
  expect(((await emisionA.json()) as { motivo: string }).motivo).toBe('sin_respuesta');
  await expect(page.getByRole('alert')).toHaveText(MENSAJE_SIN_RESPUESTA);
  await expect(botonVerificar(page)).toBeVisible();

  // B se factura con un pedido directo: ARCA le autoriza el mismo 0003-00000001, por $ 2.000,00.
  const emisionB = await page.request.post(`/api/presupuestos/${ventaB}/factura`, { data: { concepto: 'productos' } });
  expect(emisionB.status()).toBe(201);
  expect(((await emisionB.json()) as { comprobante: string }).comprobante).toBe(COMPROBANTE);

  let verificaciones = 0;
  let descartes = 0;
  page.on('request', (pedido) => {
    if (esRuta(pedido.url(), rutaVerificacion)) verificaciones++;
    if (esRuta(pedido.url(), rutaDescarte)) descartes++;
  });
  const falta = ESPERA_MS - (Date.now() - desde);
  if (falta > 0) {
    await page.waitForTimeout(falta);
  }

  // AC-07: ni se emite ni se recupera; el aviso nombra el comprobante y los dos importes.
  const [verificacion] = await Promise.all([
    page.waitForResponse((r) => esRuta(r.url(), rutaVerificacion)),
    botonVerificar(page).click(),
  ]);
  expect(verificacion.status()).toBe(409);
  expect(await verificacion.json()).toMatchObject({
    motivo: 'importe_distinto',
    comprobante: COMPROBANTE,
    importeRegistrado: 1000,
    importeArca: 2000,
  });
  await expect(page.getByRole('alert')).toHaveText(MENSAJE_IMPORTE_DISTINTO);
  await expect(botonVerificar(page)).toBeVisible();
  await expect(botonDescartar(page)).toBeVisible();
  await expect(botonFacturar(page)).toHaveCount(0);
  await expect(dato(page, 'Factura')).toHaveCount(0);
  const observada = await leer(page.request, ventaA);
  expect(observada.emisionPendiente).toBe(true);
  expect(observada.factura).toBeNull();
  expect(observada.observacionEmision).toMatchObject({
    motivo: 'importe_distinto',
    comprobante: COMPROBANTE,
    importeRegistrado: 1000,
    importeArca: 2000,
  });
  // B conserva su factura: la verificación de A no la tocó.
  expect((await leer(page.request, ventaB)).factura).toEqual({ comprobante: COMPROBANTE });

  // AC-09: cancelar la confirmación no llama a la Api y deja la emisión pendiente con "Verificar con ARCA".
  await botonDescartar(page).click();
  await expect(dialogoDescarte(page)).toBeVisible();
  await expect(dialogoDescarte(page)).toContainText(TEXTO_CONFIRMAR_DESCARTE);
  await dialogoDescarte(page).getByRole('button', { name: 'Cancelar', exact: true }).click();
  await expect(dialogoDescarte(page)).toHaveCount(0);
  expect(descartes).toBe(0);
  await expect(botonVerificar(page)).toBeVisible();
  await expect(botonDescartar(page)).toBeVisible();
  await expect(botonFacturar(page)).toHaveCount(0);
  expect((await leer(page.request, ventaA)).emisionPendiente).toBe(true);

  // Al volver a abrirlo, la observación sigue explicándolo y se puede descartar.
  await page.reload();
  await expect(dato(page, 'Número')).toHaveText(String(ventaA));
  await expect(page.getByText(MENSAJE_IMPORTE_DISTINTO, { exact: true })).toBeVisible();
  await expect(page.getByText(AVISO_PENDIENTE, { exact: true })).toBeVisible();
  await expect(botonVerificar(page)).toBeVisible();
  await expect(botonFacturar(page)).toHaveCount(0);

  // AC-08: descartar y confirmar vuelve Facturar.
  await botonDescartar(page).click();
  const [descarte] = await Promise.all([
    page.waitForResponse((r) => esRuta(r.url(), rutaDescarte)),
    dialogoDescarte(page).getByRole('button', { name: 'Descartar', exact: true }).click(),
  ]);
  expect(descarte.status()).toBe(204);
  await expect(dialogoDescarte(page)).toHaveCount(0);
  await expect(page.getByText('Se descartó la emisión pendiente.', { exact: true })).toBeVisible();
  await expect(botonFacturar(page)).toBeVisible();
  await expect(botonFacturar(page)).toBeEnabled();
  await expect(botonVerificar(page)).toHaveCount(0);
  await expect(botonDescartar(page)).toHaveCount(0);
  await expect(page.getByText(AVISO_PENDIENTE, { exact: true })).toHaveCount(0);
  const descartada = await leer(page.request, ventaA);
  expect(descartada.emisionPendiente).toBe(false);
  expect(descartada.observacionEmision).toBeNull();
  expect(descartada.factura).toBeNull();
  expect(verificaciones).toBe(1);
  expect(descartes).toBe(1);

  // El presupuesto vuelve a facturarse: ARCA le autoriza el número siguiente.
  await page.reload();
  await expect(botonFacturar(page)).toBeEnabled();
  const nueva = await emitir(page, ventaA);
  expect(nueva.status()).toBe(201);
  await expect(page.getByText('Se emitió la Factura B 0003-00000002.', { exact: true })).toBeVisible();
  await expect(dato(page, 'Factura')).toHaveText('Factura B 0003-00000002');
});
