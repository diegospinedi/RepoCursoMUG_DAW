import { expect, test, type APIRequestContext, type Page, type Request } from '@playwright/test';

/**
 * Emisión de la factura a consumidor final (FEAT-007: AC-01, AC-02, AC-03, AC-13, AC-18, AC-19, AC-30 y AC-32) de
 * punta a punta, contra la Api real que sirve la SPA compilada, en Chrome y Edge (AC-26, D-03).
 *
 * ARCA es el simulador (D-17, `Arca__Entorno=Simulado`, `Arca__Simulado__Respuesta=autorizar`): no sale de la máquina
 * y sus comprobantes no tienen validez fiscal (R-10). servidor.mjs deja el contador de la Factura B del punto de venta 3
 * de esta Api en 124 (`arca-simulado.json`), así la primera factura autorizada de esta base es la 0003-00000125.
 *
 * Las pruebas corren en orden porque dependen de lo grabado: la configuración incompleta va primero (la base nueva no
 * tiene tope ni punto de venta, y el punto de venta no se puede borrar una vez cargado, D-27) y no consume ningún
 * número; después la emisión de la 0003-00000125 y, al final, los rechazos de un Borrador y de uno ya facturado. La
 * configuración se carga por la Api antes de emitir; los artículos usan códigos propios (`FA-…`) y los presupuestos se
 * graban como en la app: en Borrador y, para un Final, después pasan a Final (AGENTS.md).
 *
 * Datos personales: el cliente es ficticio, con un DNI que empieza con 00 y por eso no se emite (00.718.294, D-50), un
 * teléfono de ceros y un email en example.com.
 */
test.describe.configure({ mode: 'serial' });

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-facturacion-1';

const COMPROBANTE = 'Factura B 0003-00000125';
const EMITIDA = `Se emitió la ${COMPROBANTE}.`;
const MENSAJE_SIN_TOPE = 'Cargá el tope de identificación en la configuración para facturar.';
const MENSAJE_SIN_PUNTO_VENTA = 'Cargá el punto de venta en la configuración para facturar.';

/** La configuración de partida, sin tope ni punto de venta, como la pide el POST de la Api. */
const SIN_TOPE = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: null,
  puntoVenta: null,
};
/** Con tope y sin punto de venta (el `null` conserva el guardado, que todavía no hay). */
const SIN_PUNTO_VENTA = { ...SIN_TOPE, topeIdentificacion: 100000 };
/** Completa: tope de $100.000,00 y punto de venta 3 (AC-30). */
const COMPLETA = { ...SIN_TOPE, topeIdentificacion: 100000, puntoVenta: 3 };

/** Un cliente ficticio (D-50). */
const CLIENTE = {
  apellido: 'Ficticio',
  nombre: 'Cliente de Facturación',
  dni: '00.718.294',
  domicilio: 'Calle Inventada 123, Localidad de Prueba',
  email: 'cliente.factura@example.com',
  telefono: '00 0000-0000',
};

interface PresupuestoGrabado {
  numero: number;
  estado: string;
  version: number;
}

interface PresupuestoConsultado extends PresupuestoGrabado {
  total: number;
  lineas: unknown[];
  factura: { comprobante: string } | null;
  emisionPendiente: boolean;
}

const campo = (page: Page, etiqueta: string) => page.getByLabel(etiqueta, { exact: true });
const botonFacturar = (page: Page) => page.getByRole('button', { name: 'Facturar', exact: true });
const escaparRegex = (texto: string) => texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const dato = (page: Page, etiqueta: string) =>
  page
    .locator('dl.datos > div')
    .filter({ has: page.locator('dt', { hasText: new RegExp(`^${escaparRegex(etiqueta)}$`) }) })
    .locator('dd');
const esEmision = (url: string, numero: number) => new URL(url).pathname === `/api/presupuestos/${numero}/factura`;

/** Entra como la operadora: en la base nueva define la contraseña; después ingresa con ella. */
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

/** Graba la configuración por la Api, con la sesión de la página. */
async function configurar(request: APIRequestContext, datos: object) {
  const respuesta = await request.post('/api/configuracion', { data: datos });
  expect(respuesta.status(), 'No se pudo grabar la configuración').toBe(200);
}

/**
 * Graba por la Api tres artículos de $1.000,00 (costo 1000, margen 0) y un presupuesto en Final con una línea de cada
 * uno: total $3.000,00 (AC-01). Se graba en Borrador y después pasa a Final, como en la app.
 */
async function grabarFinal(request: APIRequestContext, prefijo: string): Promise<PresupuestoGrabado> {
  const borrador = await grabarBorrador(request, prefijo);
  const respuesta = await request.put(`/api/presupuestos/${borrador.numero}/estado`, {
    data: { estado: 'final', version: borrador.version },
  });
  expect(respuesta.status(), 'No se pudo pasar a Final por la Api').toBe(200);
  const final = (await respuesta.json()) as PresupuestoGrabado;
  expect(final.estado).toBe('final');
  return final;
}

/** Graba por la Api tres artículos de $1.000,00 y un presupuesto en Borrador con una línea de cada uno. */
async function grabarBorrador(request: APIRequestContext, prefijo: string): Promise<PresupuestoGrabado> {
  const lineas = [];
  for (const sufijo of ['1', '2', '3']) {
    const articulo = await request.post('/api/articulos', {
      data: {
        codigoProveedor: `${prefijo}-${sufijo}`,
        descripcion: `Artículo de facturación ${prefijo}-${sufijo}`,
        precioCosto: 1000,
        margenUtilidad: 0,
      },
    });
    expect(articulo.status(), `No se pudo crear ${prefijo}-${sufijo}`).toBe(201);
    const { codigo } = (await articulo.json()) as { codigo: number };
    lineas.push({ codigoArticulo: codigo, cantidad: 1, porcentajeDescuento: 0 });
  }
  const respuesta = await request.post('/api/presupuestos', { data: { cliente: CLIENTE, lineas } });
  expect(respuesta.status(), 'No se pudo grabar el presupuesto por la Api').toBe(201);
  const grabado = (await respuesta.json()) as PresupuestoGrabado & { total: number };
  expect(grabado.estado).toBe('borrador');
  expect(grabado.total).toBe(3000);
  return grabado;
}

async function leer(request: APIRequestContext, numero: number): Promise<PresupuestoConsultado> {
  const respuesta = await request.get(`/api/presupuestos/${numero}`);
  expect(respuesta.status()).toBe(200);
  return (await respuesta.json()) as PresupuestoConsultado;
}

/** Abre el presupuesto como la operadora y espera que se muestre. */
async function abrirPresupuesto(page: Page, numero: number) {
  await page.goto(`/presupuestos/${numero}`);
  await expect(dato(page, 'Número')).toHaveText(String(numero));
}

/** Pulsa Facturar, elige el concepto y pulsa Emitir; devuelve la respuesta de la Api a la emisión. */
async function emitirDesdeLaPantalla(page: Page, numero: number, concepto = 'Productos') {
  await botonFacturar(page).click();
  const dialogo = page.getByRole('dialog', { name: 'Facturar' });
  await expect(dialogo).toBeVisible();
  await dialogo.getByRole('radio', { name: concepto, exact: true }).check();
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => esEmision(r.url(), numero) && r.request().method() === 'POST'),
    dialogo.getByRole('button', { name: 'Emitir', exact: true }).click(),
  ]);
  return respuesta;
}

test('facturación: configuración incompleta frena la emisión', async ({ page }) => {
  // AC-13 y AC-32: sin tope, o sin punto de venta, no se envía nada a ARCA y la pantalla dice qué cargar, con el enlace
  // a la configuración. La base es nueva: no hay tope ni punto de venta.
  await ingresar(page);
  await configurar(page.request, SIN_TOPE);
  const final = await grabarFinal(page.request, 'FA-INC');
  await abrirPresupuesto(page, final.numero);
  await expect(botonFacturar(page)).toBeEnabled();

  const sinTope = await emitirDesdeLaPantalla(page, final.numero);
  expect(sinTope.status()).toBe(409);
  expect(((await sinTope.json()) as { motivo: string }).motivo).toBe('sin_tope');
  const alerta = page.getByRole('alert');
  await expect(alerta).toContainText(MENSAJE_SIN_TOPE);
  await expect(alerta.getByRole('link', { name: 'Ir a la configuración' })).toHaveAttribute('href', '/configuracion');
  await expect(botonFacturar(page)).toBeEnabled();

  await configurar(page.request, SIN_PUNTO_VENTA);
  const sinPuntoVenta = await emitirDesdeLaPantalla(page, final.numero);
  expect(sinPuntoVenta.status()).toBe(409);
  expect(((await sinPuntoVenta.json()) as { motivo: string }).motivo).toBe('sin_punto_venta');
  await expect(page.getByRole('alert')).toContainText(MENSAJE_SIN_PUNTO_VENTA);

  // Ninguna factura ni emisión pendiente; el enlace lleva a la configuración.
  const releido = await leer(page.request, final.numero);
  expect(releido.factura).toBeNull();
  expect(releido.emisionPendiente).toBe(false);
  await page.getByRole('alert').getByRole('link', { name: 'Ir a la configuración' }).click();
  await expect(page).toHaveURL(/\/configuracion$/);
});

test('facturación: emitir Factura B y verla en el presupuesto', async ({ page }) => {
  // AC-01, AC-18, AC-19 y AC-30: Final de 3 líneas y $3.000,00, punto de venta 3; ARCA (simulada) asigna el 125.
  await ingresar(page);
  await configurar(page.request, COMPLETA);
  const final = await grabarFinal(page.request, 'FA-EMI');
  await abrirPresupuesto(page, final.numero);
  await expect(dato(page, 'Estado')).toHaveText('Final');
  await expect(botonFacturar(page)).toBeEnabled();

  // FR-08: el diálogo pide el concepto, sin ninguno elegido, y sin concepto no se puede emitir.
  await botonFacturar(page).click();
  const dialogo = page.getByRole('dialog', { name: 'Facturar' });
  await expect(dialogo).toBeVisible();
  await expect(dialogo.getByRole('radio', { name: 'Productos', exact: true })).not.toBeChecked();
  await expect(dialogo.getByRole('radio', { name: 'Productos y servicios', exact: true })).not.toBeChecked();
  await expect(dialogo.getByRole('button', { name: 'Emitir', exact: true })).toBeDisabled();
  await dialogo.getByRole('button', { name: 'Cancelar', exact: true }).click();
  await expect(dialogo).toBeHidden();

  const pedidos: Request[] = [];
  page.on('request', (pedido) => {
    if (esEmision(pedido.url(), final.numero)) pedidos.push(pedido);
  });
  const respuesta = await emitirDesdeLaPantalla(page, final.numero, 'Productos');

  // La pantalla manda solo el concepto (R-06); la Api arma la Factura B de las 3 líneas por $3.000,00.
  expect(pedidos).toHaveLength(1);
  expect(pedidos[0]?.postDataJSON()).toEqual({ concepto: 'productos' });
  expect(respuesta.status()).toBe(201);
  const factura = (await respuesta.json()) as { comprobante: string; tipo: string; numero: number; total: number };
  expect(factura).toMatchObject({ comprobante: COMPROBANTE, tipo: 'b', numero: 125, total: 3000 });

  // AC-18: la confirmación con el tipo, el punto de venta y el número; FR-20: el presupuesto muestra la factura.
  await expect(page.getByRole('status').filter({ hasText: EMITIDA })).toHaveText(EMITIDA);
  await expect(dato(page, 'Factura')).toHaveText(COMPROBANTE);
  await expect(botonFacturar(page)).toBeDisabled();

  // AC-19: al volver a abrirlo, el comprobante sigue ahí y Facturar deshabilitado (AC-03).
  await page.reload();
  await expect(dato(page, 'Factura')).toHaveText(COMPROBANTE);
  await expect(botonFacturar(page)).toBeDisabled();
  const releido = await leer(page.request, final.numero);
  expect(releido.factura).toEqual({ comprobante: COMPROBANTE });
  expect(releido.emisionPendiente).toBe(false);
  expect(releido.lineas).toHaveLength(3);
});

test('facturación: Borrador y pedido directo a la Api rechazados', async ({ page }) => {
  // AC-02 y AC-03: en Borrador y con factura el botón está deshabilitado, y la Api rechaza el pedido directo.
  await ingresar(page);
  await configurar(page.request, COMPLETA);

  const borrador = await grabarBorrador(page.request, 'FA-BOR');
  await abrirPresupuesto(page, borrador.numero);
  await expect(dato(page, 'Estado')).toHaveText('Borrador');
  await expect(botonFacturar(page)).toBeDisabled();
  await expect(botonFacturar(page)).toHaveAttribute('disabled', '');
  const directoBorrador = await page.request.post(`/api/presupuestos/${borrador.numero}/factura`, {
    data: { concepto: 'productos' },
  });
  expect(directoBorrador.status()).toBe(409);
  expect(((await directoBorrador.json()) as { motivo: string }).motivo).toBe('borrador');
  expect((await leer(page.request, borrador.numero)).factura).toBeNull();

  // Uno ya facturado (por la Api): la pantalla lo muestra deshabilitado y un segundo pedido directo, también desde el
  // navegador con la sesión, recibe 409 `facturado` sin tocar la factura.
  const final = await grabarFinal(page.request, 'FA-FAC');
  const primera = await page.request.post(`/api/presupuestos/${final.numero}/factura`, {
    data: { concepto: 'productos_y_servicios' },
  });
  expect(primera.status()).toBe(201);
  const { comprobante } = (await primera.json()) as { comprobante: string };
  expect(comprobante).toMatch(/^Factura B 0003-\d{8}$/);

  await abrirPresupuesto(page, final.numero);
  await expect(dato(page, 'Factura')).toHaveText(comprobante);
  await expect(botonFacturar(page)).toBeDisabled();
  const desdeElNavegador = await page.evaluate(async (ruta) => {
    const respuesta = await fetch(ruta, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ concepto: 'productos' }),
    });
    return { estado: respuesta.status, cuerpo: (await respuesta.json()) as { motivo?: string } };
  }, `/api/presupuestos/${final.numero}/factura`);
  expect(desdeElNavegador.estado).toBe(409);
  expect(desdeElNavegador.cuerpo.motivo).toBe('facturado');
  expect((await leer(page.request, final.numero)).factura).toEqual({ comprobante });
});
