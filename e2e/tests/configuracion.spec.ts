import { expect, test, type Locator, type Page } from '@playwright/test';

/**
 * Parámetros de negocio (FEAT-001c, AC-01 a AC-07) de punta a punta, contra la Api real que sirve la SPA compilada.
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva (D-05): acceso.spec.ts deja
 * su base con otra contraseña y bloqueada. Las pruebas corren en orden porque dependen de lo grabado: primero los
 * valores iniciales y el aviso (AC-06, AC-07), después los rechazos que no graban nada (AC-03, AC-04, AC-05) y al
 * final lo que sí se graba (AC-02, AC-01). Los valores se escriben como los escribiría la operadora, con coma decimal.
 *
 * FEAT-007: la alícuota se elige de la lista de las 6 de ARCA (AC-28), la 19 por la Api se rechaza (AC-27) y el punto
 * de venta fuera de rango no se envía ni se graba (AC-31); al final se graban una alícuota y un punto de venta.
 */
test.describe.configure({ mode: 'serial' });

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-config-1';

const ETIQUETA_ALICUOTA = 'Alícuota de IVA (%)';
const ETIQUETA_CONDICION = 'Condición fiscal';
const ETIQUETA_MULTIPLO = 'Múltiplo de redondeo comercial ($)';
const ETIQUETA_TOPE = 'Tope de identificación del receptor ($)';
const ETIQUETA_PUNTO_VENTA = 'Punto de venta';

const MENSAJE_FALTA_TOPE = 'Falta cargar el tope de identificación del receptor.';
const MENSAJE_GRABADA = 'La configuración se grabó.';
/** D-35: la alícuota que no es una de las 6 de ARCA. */
const MENSAJE_ALICUOTA_NO_ADMITIDA = 'La alícuota de IVA debe ser 0, 2,5, 5, 10,5, 21 o 27.';
const MENSAJE_PUNTO_VENTA = 'El punto de venta debe ser un número entero de 1 a 99999.';
/** Las 6 alícuotas de ARCA (FR-26), como las muestra la lista. */
const OPCIONES_ALICUOTA = ['0', '2,5', '5', '10,5', '21', '27'];
const MENSAJE_MULTIPLO_MINIMO = 'El valor mínimo es 0,01.';
const MENSAJE_TOPE_NO_POSITIVO = 'El tope debe ser mayor a 0.';

/** El tope que graba AC-01, escrito con coma decimal, y el número que devuelve la Api. */
const TOPE_ESCRITO = '250000,5';
const TOPE_GRABADO = 250000.5;

/** Valores iniciales de la migración (FR-07). */
const INICIALES = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: null,
  faltaTope: true,
  puntoVenta: null,
  alicuotasAdmitidas: [0, 2.5, 5, 10.5, 21, 27],
};

/** El cuerpo de un POST directo a la Api con los valores iniciales. */
const PEDIDO_INICIAL = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: null,
};

let violacionesCsp: string[] = [];
/** Los POST a /api/configuracion que mandó la página en la prueba en curso. */
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
    if (pedido.method() === 'POST' && new URL(pedido.url()).pathname === '/api/configuracion') {
      grabaciones.push(pedido.url());
    }
  });
});

test.afterEach(() => {
  expect(violacionesCsp, 'La SPA no debe violar Content-Security-Policy').toEqual([]);
});

function campoContrasena(page: Page) {
  return page.getByLabel('Contraseña', { exact: true });
}

const alicuota = (page: Page) => page.getByLabel(ETIQUETA_ALICUOTA, { exact: true });
const condicion = (page: Page) => page.getByLabel(ETIQUETA_CONDICION, { exact: true });
const multiplo = (page: Page) => page.getByLabel(ETIQUETA_MULTIPLO, { exact: true });
const tope = (page: Page) => page.getByLabel(ETIQUETA_TOPE, { exact: true });
const puntoVenta = (page: Page) => page.getByLabel(ETIQUETA_PUNTO_VENTA, { exact: true });
const avisoFaltaTope = (page: Page) => page.getByRole('status').filter({ hasText: MENSAJE_FALTA_TOPE });

/** Ingresa con la contraseña ya definida y abre la configuración desde el inicio, como la operadora. */
async function ingresarYAbrir(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/ingreso$/);
  await campoContrasena(page).fill(CONTRASENA);
  await page.getByRole('button', { name: 'Ingresar' }).click();
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await abrirDesdeElInicio(page);
}

async function abrirDesdeElInicio(page: Page) {
  await page.getByRole('link', { name: 'Configuración' }).click();
  await expect(page).toHaveURL(/\/configuracion$/);
  await expect(page.getByRole('heading', { name: 'Configuración' })).toBeVisible();
}

/** Vuelve al inicio y abre de nuevo la pantalla: la SPA la monta otra vez y vuelve a consultar la Api. */
async function volverAAbrir(page: Page) {
  await page.getByRole('link', { name: 'Volver al inicio' }).click();
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await abrirDesdeElInicio(page);
}

/** Pulsa Grabar y espera la respuesta de la Api; devuelve el código. */
async function grabar(page: Page): Promise<number> {
  const [respuesta] = await Promise.all([
    page.waitForResponse(
      (r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/configuracion',
    ),
    page.getByRole('button', { name: 'Grabar' }).click(),
  ]);
  return respuesta.status();
}

/** El campo quedó marcado inválido y su mensaje, vinculado por aria-describedby, es `mensaje`. */
async function rechazadoJuntoAlCampo(page: Page, campo: Locator, mensaje: string) {
  await expect(campo).toHaveAttribute('aria-invalid', 'true');
  const idMensaje = await campo.getAttribute('aria-describedby');
  expect(idMensaje, 'El campo no apunta a su mensaje').toBeTruthy();
  await expect(page.locator(`[id="${idMensaje}"]`)).toHaveText(mensaje);
}

/** Lo que tiene grabado la Api ahora, pedido con la sesión de la página. */
async function grabado(page: Page) {
  const respuesta = await page.request.get('/api/configuracion');
  expect(respuesta.status()).toBe(200);
  return respuesta.json();
}

/**
 * Pulsa Grabar con un valor que la pantalla rechaza: el mensaje queda junto al campo, la Api sigue con los valores
 * iniciales y no se envió nada. `grabaciones` se mira al final, cuando la página ya tuvo tiempo de mandar un POST.
 */
async function rechazadoSinGrabar(page: Page, campo: Locator, mensaje: string) {
  await page.getByRole('button', { name: 'Grabar' }).click();
  await rechazadoJuntoAlCampo(page, campo, mensaje);
  await expect(page.getByRole('status').filter({ hasText: MENSAJE_GRABADA })).toHaveCount(0);
  expect(await grabado(page)).toEqual(INICIALES);
  expect(grabaciones, 'La pantalla envió un valor que debía rechazar').toEqual([]);
}

/** La opción elegida en el selector de condición fiscal. */
const condicionElegida = (page: Page) => condicion(page).getByRole('option', { selected: true });
/** La opción elegida en la lista de alícuotas. */
const alicuotaElegida = (page: Page) => alicuota(page).getByRole('option', { selected: true });

/** POST directo a la Api con la sesión de la página: devuelve el código y el cuerpo. */
async function grabarPorLaApi(page: Page, cambios: Record<string, unknown>) {
  const respuesta = await page.request.post('/api/configuracion', { data: { ...PEDIDO_INICIAL, ...cambios } });
  return { estado: respuesta.status(), cuerpo: await respuesta.json() };
}

test('configuracion: primera vez muestra 21, Responsable Inscripto y 0,01', async ({ page }) => {
  // Base nueva: define la contraseña e ingresa (FEAT-001a), y abre la configuración por primera vez (AC-06).
  await page.goto('/');
  await expect(page).toHaveURL(/\/definir-contrasena$/);
  await campoContrasena(page).fill(CONTRASENA);
  await page.getByRole('button', { name: 'Definir contraseña' }).click();
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await abrirDesdeElInicio(page);

  await expect(alicuota(page)).toHaveValue('21');
  // AC-28: la lista con las 6 alícuotas de ARCA y la guardada elegida.
  await expect(alicuota(page).getByRole('option')).toHaveText(OPCIONES_ALICUOTA);
  await expect(alicuotaElegida(page)).toHaveText('21');
  await expect(condicion(page)).toHaveValue('responsable_inscripto');
  await expect(condicionElegida(page)).toHaveText('Responsable Inscripto');
  await expect(multiplo(page)).toHaveValue('0,01');
  await expect(tope(page)).toHaveValue('');
  await expect(puntoVenta(page)).toHaveValue('');
  expect(await grabado(page)).toEqual(INICIALES);
});

test('configuracion: sin tope muestra el aviso', async ({ page }) => {
  await ingresarYAbrir(page);

  await expect(tope(page)).toHaveValue('');
  await expect(avisoFaltaTope(page)).toBeVisible();
  expect((await grabado(page)).faltaTope).toBe(true);
});

test('configuración: alícuota 19 por la Api rechazada', async ({ page }) => {
  await ingresarYAbrir(page);

  // AC-27 por la Api: 400 en alicuotaIva con el mensaje de las 6 (D-35), y no se graba nada.
  const { estado, cuerpo } = await grabarPorLaApi(page, { alicuotaIva: 19 });
  expect(estado).toBe(400);
  expect(cuerpo.errors).toEqual({ alicuotaIva: [MENSAJE_ALICUOTA_NO_ADMITIDA] });
  expect(await grabado(page)).toEqual(INICIALES);

  // Desde la pantalla la 19 no se puede elegir: la lista solo tiene las 6.
  await volverAAbrir(page);
  await expect(alicuota(page).getByRole('option')).toHaveText(OPCIONES_ALICUOTA);
  await expect(alicuotaElegida(page)).toHaveText('21');
  expect(grabaciones, 'La pantalla no debía enviar nada').toEqual([]);
});

test('configuración: punto de venta 0, 100000 y 2,5 rechazado junto al campo', async ({ page }) => {
  await ingresarYAbrir(page);

  for (const valor of ['0', '100000', '2,5']) {
    // Cada valor en una pantalla recién abierta, sin el error del anterior.
    await expect(puntoVenta(page)).toHaveValue('');
    await expect(puntoVenta(page)).not.toHaveAttribute('aria-invalid', 'true');

    await puntoVenta(page).fill(valor);
    await expect(puntoVenta(page)).toHaveValue(valor);
    await rechazadoSinGrabar(page, puntoVenta(page), MENSAJE_PUNTO_VENTA);

    await volverAAbrir(page);
  }

  // AC-31 por la Api: el mismo mensaje en puntoVenta, y no se graba nada.
  for (const valor of [0, 100000, 2.5]) {
    const { estado, cuerpo } = await grabarPorLaApi(page, { puntoVenta: valor });
    expect(estado, String(valor)).toBe(400);
    expect(cuerpo.errors, String(valor)).toEqual({ puntoVenta: [MENSAJE_PUNTO_VENTA] });
  }
  expect(await grabado(page)).toEqual(INICIALES);
  expect(grabaciones, 'La pantalla envió un punto de venta que debía rechazar').toEqual([]);
});

test('configuracion: múltiplo 0,001 rechazado con el mínimo', async ({ page }) => {
  await ingresarYAbrir(page);

  await multiplo(page).fill('0,001');
  await rechazadoSinGrabar(page, multiplo(page), MENSAJE_MULTIPLO_MINIMO);

  await volverAAbrir(page);
  await expect(multiplo(page)).toHaveValue('0,01');
});

test('configuracion: tope 0 rechazado junto al campo', async ({ page }) => {
  await ingresarYAbrir(page);

  await tope(page).fill('0');
  await rechazadoSinGrabar(page, tope(page), MENSAJE_TOPE_NO_POSITIVO);

  await volverAAbrir(page);
  await expect(tope(page)).toHaveValue('');
  await expect(avisoFaltaTope(page)).toBeVisible();
});

test('configuracion: Monotributo queda vigente al volver a abrir', async ({ page }) => {
  await ingresarYAbrir(page);

  await condicion(page).selectOption({ label: 'Monotributo' });
  expect(await grabar(page)).toBe(200);
  await expect(page.getByRole('status').filter({ hasText: MENSAJE_GRABADA })).toBeVisible();

  await volverAAbrir(page);
  await expect(condicion(page)).toHaveValue('monotributo');
  await expect(condicionElegida(page)).toHaveText('Monotributo');
  expect((await grabado(page)).condicionFiscal).toBe('monotributo');
});

test('configuracion: el tope grabado se ve al volver a abrir y sin reiniciar', async ({ page }) => {
  await ingresarYAbrir(page);
  await expect(avisoFaltaTope(page)).toBeVisible();

  await tope(page).fill(TOPE_ESCRITO);
  expect(await grabar(page)).toBe(200);
  await expect(page.getByRole('status').filter({ hasText: MENSAJE_GRABADA })).toBeVisible();
  await expect(tope(page)).toHaveValue(TOPE_ESCRITO);
  await expect(avisoFaltaTope(page)).toHaveCount(0);

  // Al volver a abrir la pantalla la Api devuelve el valor grabado, sin aviso.
  await volverAAbrir(page);
  await expect(tope(page)).toHaveValue(TOPE_ESCRITO);
  await expect(avisoFaltaTope(page)).toHaveCount(0);

  // La siguiente consulta, desde la misma página y con la misma Api (que no se reinició), lee el valor nuevo (FR-06).
  const despues = await grabado(page);
  expect(despues.topeIdentificacion).toBe(TOPE_GRABADO);
  expect(despues.faltaTope).toBe(false);
  expect(despues.condicionFiscal).toBe('monotributo');

  // Y una carga nueva de la página, que vuelve a pedir la configuración, muestra lo mismo.
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Configuración' })).toBeVisible();
  await expect(tope(page)).toHaveValue(TOPE_ESCRITO);
  await expect(avisoFaltaTope(page)).toHaveCount(0);
});

test('configuración: elegir alícuota y punto de venta y grabar', async ({ page }) => {
  await ingresarYAbrir(page);

  // AC-28 y AC-31: se elige 10,5 de la lista y se carga el punto de venta 3.
  await alicuota(page).selectOption('10,5');
  await puntoVenta(page).fill('3');
  expect(await grabar(page)).toBe(200);
  await expect(page.getByRole('status').filter({ hasText: MENSAJE_GRABADA })).toBeVisible();
  await expect(alicuotaElegida(page)).toHaveText('10,5');
  await expect(puntoVenta(page)).toHaveValue('3');

  // Al volver a abrir, la guardada queda elegida y el punto de venta se ve.
  await volverAAbrir(page);
  await expect(alicuota(page)).toHaveValue('10,5');
  await expect(alicuotaElegida(page)).toHaveText('10,5');
  await expect(puntoVenta(page)).toHaveValue('3');
  expect(await grabado(page)).toMatchObject({ alicuotaIva: 10.5, puntoVenta: 3 });

  // Vacío conserva el guardado (D-27).
  await puntoVenta(page).fill('');
  expect(await grabar(page)).toBe(200);
  await expect(puntoVenta(page)).toHaveValue('3');
  expect((await grabado(page)).puntoVenta).toBe(3);
});
