import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

/**
 * Rechazo de ARCA al emitir la factura (FEAT-007: AC-20, FR-21 y FR-22) de punta a punta, en Chrome y Edge (AC-26).
 *
 * Este archivo tiene su propia Api por navegador, con ARCA simulada que rechaza toda solicitud
 * (`Arca__Simulado__Respuesta=rechazar`, D-17): código 10016 y una descripción fija (D-53). Sus comprobantes no tienen
 * validez fiscal (R-10). La configuración (tope y punto de venta) se carga por la Api antes de emitir; los artículos
 * usan códigos propios (`FR-…`) y el presupuesto se graba en Borrador y después pasa a Final, como en la app. La prueba
 * no depende del orden: la primera que corre define la contraseña.
 *
 * Datos personales: el cliente es ficticio, con un DNI que empieza con 00 y por eso no se emite (00.718.294, D-50).
 */

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-facturacion-rechazo-1';

/** El rechazo del simulador, como lo muestra la pantalla: "ARCA rechazó la factura: código – descripción". */
const MENSAJE_RECHAZO =
  'ARCA rechazó la factura: 10016 – Rechazo simulado: el número o la fecha del comprobante no se corresponde con el ' +
  'próximo a autorizar.';

const CONFIGURACION = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: 100000,
  puntoVenta: 3,
};

const CLIENTE = {
  apellido: 'Ficticio',
  nombre: 'Cliente del Rechazo',
  dni: '00718294',
  domicilio: null,
  email: 'cliente.rechazo@example.com',
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
    data: { codigoProveedor, descripcion: `Artículo del rechazo ${codigoProveedor}`, precioCosto: 1000, margenUtilidad: 0 },
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

/** Pulsa Facturar, elige "Productos" y pulsa Emitir; devuelve la respuesta de la Api. */
async function emitir(page: Page, numero: number) {
  await botonFacturar(page).click();
  const dialogo = page.getByRole('dialog', { name: 'Facturar' });
  await dialogo.getByRole('radio', { name: 'Productos', exact: true }).check();
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => new URL(r.url()).pathname === `/api/presupuestos/${numero}/factura`),
    dialogo.getByRole('button', { name: 'Emitir', exact: true }).click(),
  ]);
  return respuesta;
}

test('facturación rechazo: mensaje de ARCA y Facturar habilitado', async ({ page }) => {
  await ingresar(page);
  const numero = await grabarFinal(page.request, 'FR-1');
  await page.goto(`/presupuestos/${numero}`);
  await expect(dato(page, 'Número')).toHaveText(String(numero));

  const respuesta = await emitir(page, numero);
  expect(respuesta.status()).toBe(409);
  expect(await respuesta.json()).toMatchObject({ motivo: 'rechazada', codigo: 10016 });

  // AC-20: el código y la descripción de ARCA, sin factura y con Facturar habilitado.
  const alerta = page.getByRole('alert');
  await expect(alerta).toHaveText(MENSAJE_RECHAZO);
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(botonFacturar(page)).toBeEnabled();
  await expect(dato(page, 'Factura')).toHaveCount(0);
  const releido = (await (await page.request.get(`/api/presupuestos/${numero}`)).json()) as {
    factura: unknown;
    emisionPendiente: boolean;
  };
  expect(releido.factura).toBeNull();
  expect(releido.emisionPendiente).toBe(false);

  // FR-22: se puede volver a emitir; el simulador vuelve a rechazar.
  const otra = await emitir(page, numero);
  expect(otra.status()).toBe(409);
  await expect(page.getByRole('alert')).toHaveText(MENSAJE_RECHAZO);
  await expect(botonFacturar(page)).toBeEnabled();
});
