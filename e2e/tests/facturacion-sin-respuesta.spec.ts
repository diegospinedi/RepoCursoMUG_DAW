import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

/**
 * ARCA sin respuesta al emitir la factura (FEAT-007: AC-21, AC-22, FR-23 y FR-24) de punta a punta, en Chrome y Edge
 * (AC-26).
 *
 * Este archivo tiene su propia Api por navegador, con ARCA simulada que no responde
 * (`Arca__Simulado__Respuesta=no_responder`, D-17) y un tiempo máximo de 2 s (`Arca__TiempoMaximoSegundos=2`) en lugar
 * de los 30 s de la óptica (NFR-01 se mide en .NET con reloj controlado). Sus comprobantes no tienen validez fiscal
 * (R-10). La configuración (tope y punto de venta) se carga por la Api antes de emitir; los artículos usan códigos
 * propios (`FS-…`) y el presupuesto se graba en Borrador y después pasa a Final, como en la app. La prueba no depende
 * del orden: la primera que corre define la contraseña.
 *
 * Datos personales: el cliente es ficticio, con un DNI que empieza con 00 y por eso no se emite (00.718.294, D-50).
 */

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-facturacion-sin-respuesta-1';

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
  nombre: 'Cliente sin Respuesta',
  dni: '00718294',
  domicilio: null,
  email: 'cliente.sin.respuesta@example.com',
  telefono: null,
};

const campo = (page: Page, etiqueta: string) => page.getByLabel(etiqueta, { exact: true });
const botonFacturar = (page: Page) => page.getByRole('button', { name: 'Facturar', exact: true });
const escaparRegex = (texto: string) => texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const dato = (page: Page, etiqueta: string) =>
  page
    .locator('dl.datos > div')
    .filter({ has: page.locator('dt', { hasText: new RegExp(`^${escaparRegex(etiqueta)}$`) }) })
    .locator('dd');

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
    data: { codigoProveedor, descripcion: `Artículo sin respuesta ${codigoProveedor}`, precioCosto: 1000, margenUtilidad: 0 },
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

test('facturación sin respuesta: mensaje y pendiente al volver a facturar', async ({ page }) => {
  await ingresar(page);
  const numero = await grabarFinal(page.request, 'FS-1');
  const rutaEmision = `/api/presupuestos/${numero}/factura`;
  await page.goto(`/presupuestos/${numero}`);
  await expect(dato(page, 'Número')).toHaveText(String(numero));

  // Los pedidos de emisión que hace la página: sin reintentos (FR-23).
  let emisiones = 0;
  page.on('request', (pedido) => {
    if (new URL(pedido.url()).pathname === rutaEmision) emisiones++;
  });

  await botonFacturar(page).click();
  const dialogo = page.getByRole('dialog', { name: 'Facturar' });
  await dialogo.getByRole('radio', { name: 'Productos', exact: true }).check();
  const inicio = Date.now();
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => new URL(r.url()).pathname === rutaEmision),
    dialogo.getByRole('button', { name: 'Emitir', exact: true }).click(),
  ]);
  const demora = Date.now() - inicio;

  // AC-21: la Api abandona la espera al tiempo máximo (2 s acá), no reintenta y la pantalla dice que ARCA no respondió.
  expect(respuesta.status()).toBe(409);
  expect(((await respuesta.json()) as { motivo: string }).motivo).toBe('sin_respuesta');
  expect(demora).toBeGreaterThanOrEqual(1_000);
  expect(demora).toBeLessThan(15_000);
  await expect(page.getByRole('alert')).toHaveText(MENSAJE_SIN_RESPUESTA);
  expect(emisiones).toBe(1);

  // FR-24: la emisión quedó pendiente; Facturar queda deshabilitado, también al volver a abrir el presupuesto.
  await expect(botonFacturar(page)).toBeDisabled();
  await expect(page.getByText(AVISO_PENDIENTE, { exact: true })).toBeVisible();
  await page.reload();
  await expect(dato(page, 'Número')).toHaveText(String(numero));
  await expect(botonFacturar(page)).toBeDisabled();
  await expect(page.getByText(AVISO_PENDIENTE, { exact: true })).toBeVisible();
  await expect(dato(page, 'Factura')).toHaveCount(0);

  // AC-22: un nuevo pedido, directo a la Api con la sesión, no envía otra solicitud: 409 `pendiente`. La Api lo rechaza
  // antes de llamar a ARCA (lo prueba Block 7 con un autorizador que cuenta las llamadas); acá no se mide el tiempo.
  const directo = await page.request.post(rutaEmision, { data: { concepto: 'productos' } });
  expect(directo.status()).toBe(409);
  expect(((await directo.json()) as { motivo: string }).motivo).toBe('pendiente');
  const releido = (await (await page.request.get(`/api/presupuestos/${numero}`)).json()) as {
    factura: unknown;
    emisionPendiente: boolean;
  };
  expect(releido.factura).toBeNull();
  expect(releido.emisionPendiente).toBe(true);
});
