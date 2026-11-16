import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

/**
 * Verificación con ARCA cuya nueva emisión queda sin respuesta (FEAT-008: AC-12) de punta a punta, en Chrome y Edge
 * (AC-14).
 *
 * Este archivo tiene su propia Api por navegador, con ARCA simulada que nunca responde las solicitudes de factura
 * (`Arca__Simulado__Respuesta=no_responder`) y sí responde la consulta por número (`Arca__Simulado__Consulta=responder`,
 * D-19), un tiempo máximo de 2 s y una espera de 6 s antes de poder verificar (`Arca__EsperaVerificacionSegundos=6`;
 * los demás archivos usan 3 s): así el pedido que comprueba la pendiente nueva tiene unos 4 s para llegar.
 * Al verificar, el número pedido no figura autorizado (el simulador no registró nada), la verificación emite con el
 * número de FR-01 y esa emisión tampoco tiene respuesta: la emisión queda pendiente con el número nuevo registrado y el
 * presupuesto sigue con "Verificar con ARCA". La pendiente nueva tiene su propio momento: un pedido de verificación
 * enseguida recibe la espera otra vez (el número no se ve desde la pantalla; lo prueban las pruebas de .NET).
 *
 * La consulta sin respuesta (AC-11) necesita otra configuración del simulador: está en
 * `verificacion-consulta-sin-respuesta.spec.ts`. Sus comprobantes no tienen validez fiscal (R-10). Datos personales: el
 * cliente es ficticio, con un DNI que empieza con 00 y un email en example.com.
 */

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-verificacion-sin-respuesta-1';

/** La espera de esta Api antes de poder verificar (`espera: 6` en `ARCA_POR_ARCHIVO`, compilacion.mjs). */
const ESPERA_MS = 6_000;
const MENSAJE_SIN_RESPUESTA = 'ARCA no respondió. La emisión quedó pendiente de verificación.';
const MENSAJE_SIN_RESPUESTA_EMISION =
  'ARCA no respondió a la nueva emisión. La emisión quedó pendiente de verificación.';
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
  nombre: 'Cliente sin Respuesta en la Verificación',
  dni: '00718294',
  domicilio: null,
  email: 'cliente.verificacion.sin.respuesta@example.com',
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
    data: {
      codigoProveedor,
      descripcion: `Artículo de verificación sin respuesta ${codigoProveedor}`,
      precioCosto: 1000,
      margenUtilidad: 0,
    },
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
  return (await respuesta.json()) as {
    factura: unknown;
    emisionPendiente: boolean;
    observacionEmision: unknown;
  };
}

test('AC-12: la nueva emisión de la verificación sin respuesta deja la emisión pendiente con el número nuevo', async ({
  page,
}) => {
  test.setTimeout(90_000);
  await ingresar(page);
  const numero = await grabarFinal(page.request, 'VS-1');
  const rutaEmision = `/api/presupuestos/${numero}/factura`;
  const rutaVerificacion = `${rutaEmision}/verificacion`;
  await page.goto(`/presupuestos/${numero}`);
  await expect(dato(page, 'Número')).toHaveText(String(numero));

  await botonFacturar(page).click();
  const dialogo = page.getByRole('dialog', { name: 'Facturar' });
  await dialogo.getByRole('radio', { name: 'Productos', exact: true }).check();
  const [emision] = await Promise.all([
    page.waitForResponse((r) => esRuta(r.url(), rutaEmision)),
    dialogo.getByRole('button', { name: 'Emitir', exact: true }).click(),
  ]);
  const desde = Date.now();
  expect(emision.status()).toBe(409);
  expect(((await emision.json()) as { motivo: string }).motivo).toBe('sin_respuesta');
  await expect(page.getByRole('alert')).toHaveText(MENSAJE_SIN_RESPUESTA);
  await expect(botonVerificar(page)).toBeVisible();

  let verificaciones = 0;
  page.on('request', (pedido) => {
    if (esRuta(pedido.url(), rutaVerificacion)) verificaciones++;
  });
  const falta = ESPERA_MS - (Date.now() - desde);
  if (falta > 0) {
    await page.waitForTimeout(falta);
  }

  // La consulta dice "no autorizado", la verificación emite con el número de FR-01 y ARCA no responde (2 s).
  const inicio = Date.now();
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => esRuta(r.url(), rutaVerificacion)),
    botonVerificar(page).click(),
  ]);
  const demora = Date.now() - inicio;
  // La pendiente nueva se grabó recién, antes de enviar: un pedido de verificación enseguida recibe la espera de nuevo
  // (con la pendiente vieja, ya pasada su espera, consultaría). Va antes de mirar la pantalla: tiene que llegar dentro de
  // los 6 s de espera, de los que la nueva emisión ya usó 2.
  const otraVez = await page.request.post(rutaVerificacion, { headers: { 'Content-Type': 'application/json' } });

  expect(respuesta.status()).toBe(409);
  expect(((await respuesta.json()) as { motivo: string }).motivo).toBe('sin_respuesta_emision');
  // El tiempo máximo de la nueva emisión es de 2 s; el margen cubre la resolución del reloj.
  expect(demora).toBeGreaterThanOrEqual(1_900);
  expect(demora).toBeLessThan(15_000);
  expect(otraVez.status()).toBe(409);
  expect(((await otraVez.json()) as { motivo: string }).motivo).toBe('espera');

  await expect(page.getByRole('alert')).toHaveText(MENSAJE_SIN_RESPUESTA_EMISION);
  await expect(botonVerificar(page)).toBeVisible();
  await expect(botonFacturar(page)).toHaveCount(0);
  await expect(page.getByText(AVISO_PENDIENTE, { exact: true })).toBeVisible();
  expect(verificaciones).toBe(1);

  // Al volver a abrirlo sigue pendiente, sin factura ni observación.
  await page.reload();
  await expect(dato(page, 'Número')).toHaveText(String(numero));
  await expect(botonVerificar(page)).toBeVisible();
  await expect(botonFacturar(page)).toHaveCount(0);
  await expect(dato(page, 'Factura')).toHaveCount(0);
  const releido = await leer(page.request, numero);
  expect(releido.emisionPendiente).toBe(true);
  expect(releido.factura).toBeNull();
  expect(releido.observacionEmision).toBeNull();
});
