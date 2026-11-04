import { expect, test, type Page } from '@playwright/test';

/**
 * Recorrido de punta a punta contra la Api real, que sirve la SPA compilada. Cada proyecto (navegador) tiene su propia
 * Api con una base nueva y una marca de prueba en directorios temporales (ver servidor.mjs), así que las pruebas de un
 * proyecto corren en orden: primero sin contraseña, después con la contraseña definida, después el cambio de
 * contraseña y al final el bloqueo, que deja la base de ese proyecto bloqueada 5 minutos.
 */
test.describe.configure({ mode: 'serial' });

/** Exactamente 8 caracteres: el mínimo de NFR-01. */
const CONTRASENA = 'e2e-ocho';
/** La contraseña después del cambio (FEAT-001b, AC-04). Las dos son de prueba, no de nadie. */
const CONTRASENA_NUEVA = 'e2e-nueva-1';
const MENSAJE_CORTA = 'La contraseña debe tener al menos 8 caracteres. Ingresá una más larga.';
const MENSAJE_INCORRECTA = 'La contraseña no es correcta. Volvé a ingresarla.';
const MENSAJE_CAMBIADA = 'La contraseña se cambió.';
const MENSAJE_BLOQUEADO = 'El acceso está bloqueado temporalmente. Volvé a intentar en unos minutos.';
/** Colores de la marca de prueba que instala servidor.mjs. */
const PRIMARIO = '#0903A0';
const FONDO = '#FFFFFF';

/** Mensajes de la consola que indican que la política de contenido bloqueó algo de la SPA. */
let violacionesCsp: string[] = [];

test.beforeEach(({ page }) => {
  violacionesCsp = [];
  page.on('console', (mensaje) => {
    if (/Content Security Policy/i.test(mensaje.text())) {
      violacionesCsp.push(mensaje.text());
    }
  });
});

test.afterEach(() => {
  expect(violacionesCsp, 'La SPA no debe violar Content-Security-Policy').toEqual([]);
});

function campoContrasena(page: Page) {
  return page.getByLabel('Contraseña', { exact: true });
}

/**
 * Escribe la contraseña en el ingreso, pulsa Ingresar y devuelve el código con que respondió la Api. Esperar la
 * respuesta, y no solo el mensaje, hace falta porque dos rechazos seguidos muestran el mismo texto.
 */
async function ingresar(page: Page, contrasena: string): Promise<number> {
  await campoContrasena(page).fill(contrasena);
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'POST' && r.url().endsWith('/api/acceso/ingresar')),
    page.getByRole('button', { name: 'Ingresar' }).click(),
  ]);
  return respuesta.status();
}

async function contrasenaDefinida(page: Page): Promise<boolean> {
  const respuesta = await page.request.get('/api/acceso/estado');
  expect(respuesta.status()).toBe(200);
  return (await respuesta.json()).contrasenaDefinida;
}

test('7 caracteres rechazada con mensaje junto al campo', async ({ page }) => {
  await page.goto('/');

  await expect(page).toHaveURL(/\/definir-contrasena$/);
  await expect(page.getByRole('heading', { name: 'Definí la contraseña de acceso' })).toBeVisible();
  const campo = campoContrasena(page);
  await campo.fill('1234567');
  await page.getByRole('button', { name: 'Definir contraseña' }).click();

  // El mensaje queda junto al campo, vinculado por aria-describedby, y no se envió nada.
  await expect(campo).toHaveAttribute('aria-invalid', 'true');
  const idMensaje = await campo.getAttribute('aria-describedby');
  expect(idMensaje).toBeTruthy();
  await expect(page.locator(`[id="${idMensaje}"]`)).toHaveText(MENSAJE_CORTA);
  await expect(page).toHaveURL(/\/definir-contrasena$/);
  expect(await contrasenaDefinida(page)).toBe(false);
});

test('primera vez define contraseña e ingresa', async ({ page }) => {
  // La spec pide definir una de 8: el borde inferior de NFR-01, el primer largo aceptado después de los 7 rechazados.
  expect(CONTRASENA, 'La contraseña de la prueba tiene que tener exactamente 8 caracteres').toHaveLength(8);
  const documento = await page.goto('/');

  // La SPA llega desde la Api con la política de contenido (R-11), que además impide mostrarla dentro de un marco.
  expect(documento?.headers()['content-security-policy']).toBe(
    "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'",
  );
  expect(documento?.headers()['x-content-type-options']).toBe('nosniff');

  // Primera vez: pantalla de definición (AC-03).
  await expect(page).toHaveURL(/\/definir-contrasena$/);
  await campoContrasena(page).fill(CONTRASENA);
  await page.getByRole('button', { name: 'Definir contraseña' }).click();

  // Inicio con el logo y los colores de la marca instalada (AC-07).
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await expect(page).toHaveURL(/\/$/);
  const logo = page.getByRole('img', { name: 'Logo de la óptica' });
  await expect(logo).toBeVisible();
  await expect.poll(() => logo.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0)).toBe(true);
  await expect
    .poll(() =>
      page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--color-primario').trim()),
    )
    .toBe(PRIMARIO);
  expect(
    await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--color-fondo').trim()),
  ).toBe(FONDO);
  const marca = await (await page.request.get('/api/marca')).json();
  expect(marca).toEqual({ primario: PRIMARIO, fondo: FONDO, esEjemplo: false });

  // Sale…
  await page.getByRole('button', { name: 'Salir' }).click();
  await expect(page).toHaveURL(/\/ingreso$/);
  await expect(page.getByRole('heading', { name: 'Ingresá al sistema' })).toBeVisible();
  expect((await page.request.get('/api/sesion')).status()).toBe(401);

  // …una contraseña incorrecta se rechaza junto al campo…
  await campoContrasena(page).fill('otra-clave-1');
  await page.getByRole('button', { name: 'Ingresar' }).click();
  await expect(page.getByRole('alert')).toHaveText(MENSAJE_INCORRECTA);
  await expect(page).toHaveURL(/\/ingreso$/);

  // …y vuelve a ingresar con la correcta.
  await campoContrasena(page).fill(CONTRASENA);
  await page.getByRole('button', { name: 'Ingresar' }).click();
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  expect((await page.request.get('/api/sesion')).status()).toBe(204);
});

test('sin sesión una ruta protegida va a ingreso', async ({ page, context }) => {
  // Un navegador sin cookie de sesión que abre la pantalla protegida (AC-01).
  expect(await contrasenaDefinida(page)).toBe(true);
  await page.goto('/');
  await expect(page).toHaveURL(/\/ingreso$/);
  await expect(page.getByRole('heading', { name: 'Ingresá al sistema' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toHaveCount(0);

  // Con sesión, si la cookie desaparece (vencida o borrada), el siguiente pedido recibe 401 y la SPA vuelve al
  // ingreso sin dejar la pantalla protegida a la vista.
  await campoContrasena(page).fill(CONTRASENA);
  await page.getByRole('button', { name: 'Ingresar' }).click();
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  await context.clearCookies();
  await page.getByRole('button', { name: 'Salir' }).click();
  await expect(page).toHaveURL(/\/ingreso$/);
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toHaveCount(0);

  // Un enlace directo a una ruta de la SPA también llega a la Api y termina en el ingreso.
  await page.goto('/una-ruta-que-no-existe');
  await expect(page).toHaveURL(/\/ingreso$/);
});

test('cambia la contraseña e ingresa con la nueva', async ({ page }) => {
  // La contraseña ya la definió "primera vez define contraseña e ingresa" en esta misma base (AC-04).
  expect(await contrasenaDefinida(page)).toBe(true);
  await page.goto('/');
  await expect(page).toHaveURL(/\/ingreso$/);
  expect(await ingresar(page, CONTRASENA)).toBe(204);
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();

  // Desde el inicio, a la pantalla de cambio.
  await page.getByRole('link', { name: 'Cambiar la contraseña' }).click();
  await expect(page).toHaveURL(/\/cambiar-contrasena$/);
  await expect(page.getByRole('heading', { name: 'Cambiar la contraseña' })).toBeVisible();
  await page.getByLabel('Contraseña actual', { exact: true }).fill(CONTRASENA);
  await page.getByLabel('Contraseña nueva', { exact: true }).fill(CONTRASENA_NUEVA);
  const [cambio] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'POST' && r.url().endsWith('/api/acceso/cambiar')),
    page.getByRole('button', { name: 'Cambiar la contraseña' }).click(),
  ]);
  expect(cambio.status()).toBe(204);

  // Vuelve al inicio con el aviso, y ninguna contraseña quedó en la URL.
  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByRole('status')).toHaveText(MENSAJE_CAMBIADA);
  expect(page.url()).not.toContain(CONTRASENA);
  expect(page.url()).not.toContain(CONTRASENA_NUEVA);

  // Sale…
  await page.getByRole('button', { name: 'Salir' }).click();
  await expect(page).toHaveURL(/\/ingreso$/);

  // …la anterior ya no sirve…
  expect(await ingresar(page, CONTRASENA)).toBe(401);
  await expect(page.getByRole('alert')).toHaveText(MENSAJE_INCORRECTA);
  await expect(page).toHaveURL(/\/ingreso$/);

  // …y la nueva sí (y reinicia la cuenta de rechazos para la prueba siguiente).
  expect(await ingresar(page, CONTRASENA_NUEVA)).toBe(204);
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  expect((await page.request.get('/api/sesion')).status()).toBe(204);
});

// Última a propósito: deja la base de este proyecto bloqueada 5 minutos. El desbloqueo a los 5 minutos se prueba en la
// Api con el reloj controlado, no acá.
test('cinco intentos incorrectos bloquean el ingreso', async ({ page }) => {
  expect(await contrasenaDefinida(page)).toBe(true);
  await page.goto('/');
  await expect(page).toHaveURL(/\/ingreso$/);

  // Los cuatro primeros rechazos muestran el mensaje de contraseña incorrecta…
  for (let intento = 1; intento <= 4; intento++) {
    expect(await ingresar(page, `incorrecta-${intento}`), `intento ${intento}`).toBe(401);
    await expect(page.getByRole('alert')).toHaveText(MENSAJE_INCORRECTA);
  }

  // …el quinto ya avisa del bloqueo en ese mismo intento (AC-01)…
  expect(await ingresar(page, 'incorrecta-5')).toBe(423);
  await expect(page.getByRole('alert')).toHaveText(MENSAJE_BLOQUEADO);

  // …y durante el bloqueo tampoco entra con la correcta: sigue en el ingreso, con el mensaje y sin sesión.
  expect(await ingresar(page, CONTRASENA_NUEVA)).toBe(423);
  await expect(page.getByRole('alert')).toHaveText(MENSAJE_BLOQUEADO);
  await expect(page).toHaveURL(/\/ingreso$/);
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toHaveCount(0);
  expect((await page.request.get('/api/sesion')).status()).toBe(401);
});
