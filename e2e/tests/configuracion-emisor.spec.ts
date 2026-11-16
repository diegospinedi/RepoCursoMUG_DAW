import { expect, test, type Locator, type Page } from '@playwright/test';

/**
 * Datos del emisor (FEAT-009: AC-01 a AC-04) de punta a punta, contra la Api real que sirve la SPA compilada, en Chrome
 * y Edge (AC-25).
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva. Las pruebas corren en orden
 * porque dependen de lo grabado: primero la sección sin cargar, después los rechazos que no graban nada (AC-02, AC-04)
 * y al final lo que sí se graba (AC-03 y AC-01). La pantalla valida con el espejo de las reglas del servidor y no envía
 * un dato inválido; por eso cada rechazo se prueba también con un pedido directo a la Api, que es la que decide.
 *
 * Datos personales (D-33): el CUIT 20-12345678-6 del PRD tiene dígito verificador válido y podría ser de una persona
 * real; se usa solo donde AC-03 y AC-04 lo exigen, y la última prueba lo reemplaza por 20-00000000-1. La razón social,
 * el domicilio y el número de Ingresos Brutos son inventados.
 */
test.describe.configure({ mode: 'serial' });

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-configuracion-emisor-1';

const RUTA_EMISOR = '/api/configuracion/emisor';
const BOTON_GRABAR_EMISOR = 'Grabar datos del emisor';

const ETIQUETA_RAZON_SOCIAL = 'Razón social';
const ETIQUETA_DOMICILIO = 'Domicilio comercial';
const ETIQUETA_CUIT = 'CUIT';
const ETIQUETA_INGRESOS_BRUTOS = 'Ingresos Brutos';
const ETIQUETA_INICIO = 'Fecha de inicio de actividades';

const MENSAJE_GRABADOS = 'Los datos del emisor se grabaron.';
const MENSAJE_RAZON_SOCIAL_FALTANTE = 'Ingresá la razón social.';
const MENSAJE_CUIT_INVALIDO = 'El CUIT no es válido: tienen que ser 11 dígitos, con o sin guiones.';

/** AC-03 y AC-04: los CUIT del PRD (D-33: solo en estas pruebas). */
const CUIT_DEL_PRD = '20-12345678-6';
const CUIT_DEL_PRD_GUARDADO = '20123456786';
const CUIT_DIGITO_INVALIDO = '20-12345678-5';
const CUIT_CORTO = '2012345678';
/** El CUIT ficticio del resto de las pruebas (D-33): 20-00000000-1, con dígito verificador válido. */
const CUIT_FICTICIO = '20-00000000-1';
const CUIT_FICTICIO_GUARDADO = '20000000001';

/** Los datos ficticios de una óptica inventada, como los escribe la operadora. */
const EMISOR = {
  razonSocial: 'Óptica de Prueba S.R.L.',
  domicilioComercial: 'Calle Inventada 123, Localidad de Prueba',
  ingresosBrutos: '901-000000-0',
  inicioActividades: '2020-03-02',
};

/** Lo que devuelve la Api mientras los datos nunca se cargaron (D-22). */
const SIN_CARGAR = {
  razonSocial: null,
  domicilioComercial: null,
  cuit: null,
  ingresosBrutos: null,
  inicioActividades: null,
};

let violacionesCsp: string[] = [];
/** Los POST a los datos del emisor que mandó la página en la prueba en curso. */
let grabaciones: string[] = [];

test.beforeEach(({ page }) => {
  violacionesCsp = [];
  grabaciones = [];
  page.on('console', (mensaje) => {
    if (/Content Security Policy/i.test(mensaje.text())) {
      violacionesCsp.push(mensaje.text());
    }
  });
  page.on('request', (pedido) => {
    if (pedido.method() === 'POST' && new URL(pedido.url()).pathname === RUTA_EMISOR) {
      grabaciones.push(pedido.url());
    }
  });
});

test.afterEach(() => {
  expect(violacionesCsp, 'La SPA no debe violar Content-Security-Policy').toEqual([]);
});

const campo = (page: Page, etiqueta: string) => page.getByLabel(etiqueta, { exact: true });
const seccion = (page: Page) => page.getByRole('region', { name: 'Datos del emisor' });
const botonGrabarEmisor = (page: Page) => page.getByRole('button', { name: BOTON_GRABAR_EMISOR, exact: true });

/** Entra como la operadora (en la base nueva define la contraseña) y abre la configuración desde el inicio. */
async function ingresarYAbrir(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/(definir-contrasena|ingreso)$/);
  await campo(page, 'Contraseña').fill(CONTRASENA);
  if (/\/definir-contrasena$/.test(page.url())) {
    await page.getByRole('button', { name: 'Definir contraseña' }).click();
  } else {
    await page.getByRole('button', { name: 'Ingresar' }).click();
  }
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await abrirDesdeElInicio(page);
}

async function abrirDesdeElInicio(page: Page) {
  await page.getByRole('link', { name: 'Configuración' }).click();
  await expect(page).toHaveURL(/\/configuracion$/);
  await expect(page.getByRole('heading', { name: 'Configuración', exact: true })).toBeVisible();
  // La sección termina de cargar cuando aparece su botón.
  await expect(botonGrabarEmisor(page)).toBeVisible();
}

/** Vuelve al inicio y abre de nuevo la pantalla: la SPA la monta otra vez y vuelve a consultar la Api. */
async function volverAAbrir(page: Page) {
  await page.getByRole('link', { name: 'Volver al inicio' }).click();
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await abrirDesdeElInicio(page);
}

/** Escribe los cinco datos en la sección (un dato ausente queda vacío). */
async function cargar(page: Page, datos: Partial<typeof EMISOR & { cuit: string }>) {
  await campo(page, ETIQUETA_RAZON_SOCIAL).fill(datos.razonSocial ?? '');
  await campo(page, ETIQUETA_DOMICILIO).fill(datos.domicilioComercial ?? '');
  await campo(page, ETIQUETA_CUIT).fill(datos.cuit ?? '');
  await campo(page, ETIQUETA_INGRESOS_BRUTOS).fill(datos.ingresosBrutos ?? '');
  await campo(page, ETIQUETA_INICIO).fill(datos.inicioActividades ?? '');
}

/** Pulsa "Grabar datos del emisor" y espera la respuesta de la Api; devuelve el código. */
async function grabar(page: Page): Promise<number> {
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname === RUTA_EMISOR),
    botonGrabarEmisor(page).click(),
  ]);
  return respuesta.status();
}

/** El campo quedó marcado inválido y su mensaje, vinculado por aria-describedby, es `mensaje`. */
async function rechazadoJuntoAlCampo(page: Page, elCampo: Locator, mensaje: string) {
  await expect(elCampo).toHaveAttribute('aria-invalid', 'true');
  const idMensaje = await elCampo.getAttribute('aria-describedby');
  expect(idMensaje, 'El campo no apunta a su mensaje').toBeTruthy();
  await expect(page.locator(`[id="${idMensaje}"]`)).toHaveText(mensaje);
}

/** Los datos del emisor que tiene grabados la Api ahora, pedidos con la sesión de la página. */
async function grabados(page: Page) {
  const respuesta = await page.request.get(RUTA_EMISOR);
  expect(respuesta.status()).toBe(200);
  return respuesta.json();
}

/** POST directo a la Api con la sesión de la página: devuelve el código y el cuerpo. */
async function grabarPorLaApi(page: Page, datos: Record<string, unknown>) {
  const respuesta = await page.request.post(RUTA_EMISOR, { data: datos });
  return { estado: respuesta.status(), cuerpo: await respuesta.json() };
}

test('datos del emisor: sin cargar, la sección muestra los cinco campos vacíos', async ({ page }) => {
  await ingresarYAbrir(page);

  await expect(seccion(page)).toBeVisible();
  for (const etiqueta of [ETIQUETA_RAZON_SOCIAL, ETIQUETA_DOMICILIO, ETIQUETA_CUIT, ETIQUETA_INGRESOS_BRUTOS, ETIQUETA_INICIO]) {
    await expect(campo(page, etiqueta)).toHaveValue('');
  }
  expect(await grabados(page)).toEqual(SIN_CARGAR);
});

test('datos del emisor: sin razón social no se graban y se avisa junto al campo', async ({ page }) => {
  // AC-02: en la pantalla, el mensaje junto a Razón social y nada enviado.
  await ingresarYAbrir(page);
  await cargar(page, { ...EMISOR, razonSocial: '', cuit: CUIT_FICTICIO });
  await botonGrabarEmisor(page).click();
  await rechazadoJuntoAlCampo(page, campo(page, ETIQUETA_RAZON_SOCIAL), MENSAJE_RAZON_SOCIAL_FALTANTE);
  await expect(page.getByRole('status').filter({ hasText: MENSAJE_GRABADOS })).toHaveCount(0);
  // Lo escrito queda en la pantalla.
  await expect(campo(page, ETIQUETA_DOMICILIO)).toHaveValue(EMISOR.domicilioComercial);

  // AC-02 en la Api: sin razón social, o con solo espacios, 400 en razonSocial y no se graba nada.
  for (const razonSocial of [undefined, '   ']) {
    const { estado, cuerpo } = await grabarPorLaApi(page, { ...EMISOR, razonSocial, cuit: CUIT_FICTICIO });
    expect(estado, String(razonSocial)).toBe(400);
    expect(cuerpo.errors, String(razonSocial)).toEqual({ razonSocial: [MENSAJE_RAZON_SOCIAL_FALTANTE] });
  }
  expect(await grabados(page)).toEqual(SIN_CARGAR);
  expect(grabaciones, 'La pantalla envió datos que debía rechazar').toEqual([]);
});

test('datos del emisor: CUIT 20-12345678-5 y 2012345678 rechazados junto al campo', async ({ page }) => {
  await ingresarYAbrir(page);

  // AC-04 en la pantalla: cada CUIT en una pantalla recién abierta, con los otros cuatro datos válidos.
  for (const cuit of [CUIT_DIGITO_INVALIDO, CUIT_CORTO]) {
    await expect(campo(page, ETIQUETA_CUIT)).not.toHaveAttribute('aria-invalid', 'true');
    await cargar(page, { ...EMISOR, cuit });
    await botonGrabarEmisor(page).click();
    await rechazadoJuntoAlCampo(page, campo(page, ETIQUETA_CUIT), MENSAJE_CUIT_INVALIDO);
    await expect(campo(page, ETIQUETA_CUIT)).toHaveValue(cuit);
    await expect(campo(page, ETIQUETA_RAZON_SOCIAL)).not.toHaveAttribute('aria-invalid', 'true');
    await volverAAbrir(page);
  }

  // AC-04 en la Api: el mismo mensaje en cuit, y no se graba nada.
  for (const cuit of [CUIT_DIGITO_INVALIDO, CUIT_CORTO]) {
    const { estado, cuerpo } = await grabarPorLaApi(page, { ...EMISOR, cuit });
    expect(estado, cuit).toBe(400);
    expect(cuerpo.errors, cuit).toEqual({ cuit: [MENSAJE_CUIT_INVALIDO] });
  }
  expect(await grabados(page)).toEqual(SIN_CARGAR);
  expect(grabaciones, 'La pantalla envió un CUIT que debía rechazar').toEqual([]);
});

test('datos del emisor: CUIT 20-12345678-6 se guarda como 20123456786', async ({ page }) => {
  await ingresarYAbrir(page);

  // AC-03 en la pantalla: se escribe con guiones y queda guardado sin ellos.
  await cargar(page, { ...EMISOR, cuit: CUIT_DEL_PRD });
  expect(await grabar(page)).toBe(200);
  await expect(page.getByRole('status').filter({ hasText: MENSAJE_GRABADOS })).toBeVisible();
  expect((await grabados(page)).cuit).toBe(CUIT_DEL_PRD_GUARDADO);
  await expect(campo(page, ETIQUETA_CUIT)).toHaveValue(CUIT_DEL_PRD_GUARDADO);

  // AC-03 en la Api: la pantalla manda el CUIT ya sin guiones (D-36); la Api también los quita si llegan.
  const { estado, cuerpo } = await grabarPorLaApi(page, { ...EMISOR, cuit: CUIT_DEL_PRD });
  expect(estado).toBe(200);
  expect(cuerpo.cuit).toBe(CUIT_DEL_PRD_GUARDADO);
  expect((await grabados(page)).cuit).toBe(CUIT_DEL_PRD_GUARDADO);
});

test('datos del emisor: los cinco datos grabados se ven al volver a abrir la configuración', async ({ page }) => {
  await ingresarYAbrir(page);

  // AC-01: carga los cinco datos (con el CUIT ficticio, D-33) y graba.
  await cargar(page, { ...EMISOR, cuit: CUIT_FICTICIO });
  expect(await grabar(page)).toBe(200);
  await expect(page.getByRole('status').filter({ hasText: MENSAJE_GRABADOS })).toBeVisible();
  const esperados = { ...EMISOR, cuit: CUIT_FICTICIO_GUARDADO };
  expect(await grabados(page)).toEqual(esperados);

  // Al volver a abrir la configuración, y al recargar la página, se ven los cinco.
  for (const reabrir of [volverAAbrir, async (p: Page) => {
    await p.reload();
    await expect(botonGrabarEmisor(p)).toBeVisible();
  }]) {
    await reabrir(page);
    await expect(campo(page, ETIQUETA_RAZON_SOCIAL)).toHaveValue(EMISOR.razonSocial);
    await expect(campo(page, ETIQUETA_DOMICILIO)).toHaveValue(EMISOR.domicilioComercial);
    await expect(campo(page, ETIQUETA_CUIT)).toHaveValue(CUIT_FICTICIO_GUARDADO);
    await expect(campo(page, ETIQUETA_INGRESOS_BRUTOS)).toHaveValue(EMISOR.ingresosBrutos);
    await expect(campo(page, ETIQUETA_INICIO)).toHaveValue(EMISOR.inicioActividades);
  }

  // Grabar los datos del emisor no tocó los parámetros de negocio, que siguen con los valores iniciales.
  const parametros = await page.request.get('/api/configuracion');
  expect(await parametros.json()).toMatchObject({ alicuotaIva: 21, condicionFiscal: 'responsable_inscripto' });
});
