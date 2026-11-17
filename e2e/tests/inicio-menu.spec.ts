import { expect, test, type Locator, type Page } from '@playwright/test';

import { aRgba, anchoDeLaPagina, contraste, contrastes, fondoEfectivo, muestrear, quieta } from './medidas.ts';

/**
 * Menú de Inicio en tarjetas (FEAT-012: AC-01, AC-02 y AC-04 a AC-07) de punta a punta, contra la Api real que sirve la
 * SPA compilada, en Chrome y Edge (AC-10, NFR-02).
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva: la primera prueba define la
 * contraseña y las siguientes ingresan con ella. No se graba ningún dato: el menú no depende de la base. La marca es la
 * del harness (servidor.mjs): primario #0903A0 sobre fondo #FFFFFF, la real de NFR-01, así que los colores computados
 * del ícono y del foco se comparan con ese primario, y el contraste se calcula desde los estilos computados.
 *
 * AC-03 (un enlace por nombre con su destino), AC-08 (avisos antes del menú) y AC-09 (Salir) los prueba Vitest
 * (`MenuInicio.test.tsx` e `Inicio.test.tsx`); el orden nuevo del menú también lo fija `backup-aviso.spec.ts` (D-11).
 * Cualquier violación de la política de seguridad de contenido hace fallar la prueba (R-02).
 */
test.describe.configure({ mode: 'serial' });

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-inicio-menu-1';

/** El primario de la marca del harness (#0903A0), como lo devuelve getComputedStyle. */
const PRIMARIO = 'rgb(9, 3, 160)';

/** Las secciones del menú y sus opciones, en el orden de FR-01: el nombre del enlace y su destino. */
const SECCIONES = [
  {
    titulo: 'Ventas',
    opciones: [
      { nombre: 'Nuevo presupuesto', destino: '/presupuestos/nuevo' },
      { nombre: 'Buscar presupuestos', destino: '/presupuestos' },
      { nombre: 'Facturas', destino: '/facturas' },
    ],
  },
  {
    titulo: 'Catálogo',
    opciones: [
      { nombre: 'Artículos', destino: '/articulos' },
      { nombre: 'Nuevo artículo', destino: '/articulos/nuevo' },
    ],
  },
  {
    titulo: 'Sistema',
    opciones: [
      { nombre: 'Configuración', destino: '/configuracion' },
      { nombre: 'Cambiar la contraseña', destino: '/cambiar-contrasena' },
    ],
  },
];
const OPCIONES = SECCIONES.flatMap(({ opciones }) => opciones);

let violacionesCsp: string[] = [];
let erroresDeConsola: string[] = [];

test.beforeEach(({ page }) => {
  violacionesCsp = [];
  erroresDeConsola = [];
  page.on('console', (mensaje) => {
    if (/Content Security Policy/i.test(mensaje.text())) {
      violacionesCsp.push(mensaje.text());
    } else if (mensaje.type() === 'error') {
      erroresDeConsola.push(mensaje.text());
    }
  });
  page.on('pageerror', (error) => erroresDeConsola.push(error.message));
});

test.afterEach(() => {
  expect(violacionesCsp, 'La SPA no debe violar Content-Security-Policy').toEqual([]);
});

const tituloDelInicio = (page: Page) => page.getByRole('heading', { name: 'Sesión iniciada' });
const menu = (page: Page) => page.getByRole('navigation', { name: 'Menú de Inicio' });
const tarjetas = (page: Page) => menu(page).locator('li.tarjeta-menu');
const tarjetaDe = (page: Page, nombre: string) =>
  tarjetas(page).filter({ has: page.getByRole('link', { name: nombre, exact: true }) });

/** Entra como la operadora (en la base nueva define la contraseña) y espera el inicio con el menú. */
async function ingresar(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/(definir-contrasena|ingreso)$/);
  await page.getByLabel('Contraseña', { exact: true }).fill(CONTRASENA);
  if (/\/definir-contrasena$/.test(page.url())) {
    await page.getByRole('button', { name: 'Definir contraseña' }).click();
  } else {
    await page.getByRole('button', { name: 'Ingresar' }).click();
  }
  await expect(tituloDelInicio(page)).toBeVisible();
  await expect(menu(page)).toBeVisible();
  // Sin el puntero sobre una tarjeta: el hover invierte el color del ícono.
  await page.mouse.move(0, 0);
  await quieta(page);
}

/** La caja de cada tarjeta del menú, en el orden del DOM. */
async function cajasDeLasTarjetas(page: Page) {
  const cajas = await tarjetas(page).evaluateAll((elementos) =>
    elementos.map((elemento) => {
      const { x, y, width, height } = elemento.getBoundingClientRect();
      return { x, y, width, height };
    }),
  );
  expect(cajas).toHaveLength(OPCIONES.length);
  return cajas;
}

/** El contorno computado de un elemento. */
const contornoDe = (elemento: Locator) =>
  elemento.evaluate((nodo) => {
    const estilo = getComputedStyle(nodo);
    return {
      estilo: estilo.outlineStyle,
      ancho: estilo.outlineWidth,
      color: estilo.outlineColor,
      separacion: estilo.outlineOffset,
    };
  });

test('muestra Ventas, Catálogo y Sistema con sus opciones', async ({ page }) => {
  await ingresar(page);

  // Las tres secciones, en orden, cada una como región con su título.
  const secciones = menu(page).locator('section.menu__seccion');
  await expect(secciones.getByRole('heading', { level: 2 })).toHaveText(SECCIONES.map(({ titulo }) => titulo));
  for (const { titulo, opciones } of SECCIONES) {
    const region = menu(page).getByRole('region', { name: titulo, exact: true });
    await expect(region, `La sección ${titulo}`).toBeVisible();
    // Las opciones de la sección, en su orden, con su destino (FR-01, FR-03).
    const enlaces = region.getByRole('link');
    await expect(enlaces).toHaveText(opciones.map(({ nombre }) => nombre));
    for (const [indice, { destino }] of opciones.entries()) {
      await expect(enlaces.nth(indice)).toHaveAttribute('href', destino);
    }
  }
  // Una tarjeta por opción y ningún enlace del menú fuera de ellas.
  await expect(tarjetas(page)).toHaveCount(OPCIONES.length);
  await expect(menu(page).getByRole('link')).toHaveCount(OPCIONES.length);
});

test('clic en la tarjeta fuera del texto abre Facturas', async ({ page }) => {
  await ingresar(page);

  const tarjeta = tarjetaDe(page, 'Facturas');
  const caja = await tarjeta.boundingBox();
  expect(caja, 'La tarjeta Facturas no se dibujó').not.toBeNull();
  // La esquina inferior derecha, dentro del borde redondeado (radio 8px): a 6px de cada lado.
  const punto = { x: caja!.width - 6, y: caja!.height - 6 };
  const absoluto = { x: caja!.x + punto.x, y: caja!.y + punto.y };
  // El punto está fuera del nombre, de la descripción y del ícono: el clic no cae sobre ningún texto.
  for (const parte of [
    tarjeta.getByRole('link', { name: 'Facturas', exact: true }),
    tarjeta.locator('.tarjeta-menu__descripcion'),
    tarjeta.locator('svg'),
  ]) {
    const otra = await parte.boundingBox();
    expect(otra).not.toBeNull();
    const adentro =
      absoluto.x >= otra!.x && absoluto.x <= otra!.x + otra!.width && absoluto.y >= otra!.y && absoluto.y <= otra!.y + otra!.height;
    expect(adentro, 'El punto del clic tiene que quedar fuera del texto y del ícono').toBe(false);
  }

  await tarjeta.click({ position: punto });
  await expect(page).toHaveURL(/\/facturas$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Buscar facturas' })).toBeVisible();
});

test('Tab recorre las tarjetas en orden con foco visible', async ({ page }) => {
  await ingresar(page);

  // Con la página recién cargada, el primer Tab llega a la primera tarjeta: no hay nada enfocable antes del menú.
  await page.goto('/');
  await expect(menu(page)).toBeVisible();
  await quieta(page);
  for (const { nombre } of OPCIONES) {
    await page.keyboard.press('Tab');
    const enlace = page.getByRole('link', { name: nombre, exact: true });
    await expect(enlace, `El foco tendría que estar en ${nombre}`).toBeFocused();
    // D-05: el contorno se dibuja una vez, en la tarjeta (3px del primario, separado 2px); el enlace no dibuja el suyo.
    expect(await contornoDe(tarjetaDe(page, nombre)), `El contorno de la tarjeta ${nombre}`).toEqual({
      estilo: 'solid',
      ancho: '3px',
      color: PRIMARIO,
      separacion: '2px',
    });
    expect((await contornoDe(enlace)).estilo, `El enlace ${nombre} no dibuja contorno propio`).toBe('none');
    // Las demás tarjetas no muestran el contorno.
    const conContorno = await tarjetas(page).evaluateAll(
      (elementos) => elementos.filter((elemento) => getComputedStyle(elemento).outlineStyle !== 'none').length,
    );
    expect(conContorno, 'Una sola tarjeta con el contorno del foco').toBe(1);
  }
  // Después del menú, Salir: separado de las tarjetas (FR-08).
  await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Salir' })).toBeFocused();
});

test('360 px una columna sin desplazamiento horizontal', async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 800 });
  await ingresar(page);

  const cajas = await cajasDeLasTarjetas(page);
  // Una sola columna: todas empiezan en el mismo x, tienen el mismo ancho y cada una va debajo de la anterior.
  for (const [indice, caja] of cajas.entries()) {
    expect(caja.x, `La tarjeta ${OPCIONES[indice].nombre} empieza en otra columna`).toBeCloseTo(cajas[0].x, 0);
    expect(caja.width).toBeCloseTo(cajas[0].width, 0);
    expect(caja.x + caja.width, 'La tarjeta no entra en la ventana').toBeLessThanOrEqual(360);
    if (indice > 0) {
      expect(caja.y, `La tarjeta ${OPCIONES[indice].nombre} no está debajo de la anterior`).toBeGreaterThanOrEqual(
        cajas[indice - 1].y + cajas[indice - 1].height,
      );
    }
  }
  const { documento, ventana } = await anchoDeLaPagina(page);
  expect(documento, 'La página no debe tener desplazamiento horizontal a 360 px').toBeLessThanOrEqual(ventana);
});

test('1280 px dos tarjetas o más en la misma fila', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await ingresar(page);

  const cajas = await cajasDeLasTarjetas(page);
  // Cuántas tarjetas comparten la fila de cada una: con 1280 px al menos una fila tiene dos o más.
  const porFila = new Map<number, number>();
  for (const { y } of cajas) {
    const fila = Math.round(y);
    porFila.set(fila, (porFila.get(fila) ?? 0) + 1);
  }
  expect(Math.max(...porFila.values()), 'Ninguna fila tiene dos tarjetas o más').toBeGreaterThanOrEqual(2);
  // Las de una misma fila no se superponen.
  const primeraFila = cajas.filter(({ y }) => Math.round(y) === Math.round(cajas[0].y));
  for (let i = 1; i < primeraFila.length; i++) {
    expect(primeraFila[i].x).toBeGreaterThanOrEqual(primeraFila[i - 1].x + primeraFila[i - 1].width);
  }
  const { documento, ventana } = await anchoDeLaPagina(page);
  expect(documento).toBeLessThanOrEqual(ventana);
});

test('íconos y foco con el primario de la marca', async ({ page }) => {
  await ingresar(page);

  // Un ícono por tarjeta, decorativo y dibujado en el primario (stroke="currentColor").
  const iconos = tarjetas(page).locator('svg');
  await expect(iconos).toHaveCount(OPCIONES.length);
  const colores = await iconos.evaluateAll((elementos) =>
    elementos.map((elemento) => {
      const estilo = getComputedStyle(elemento);
      return { color: estilo.color, trazo: estilo.stroke, oculto: elemento.getAttribute('aria-hidden') };
    }),
  );
  for (const [indice, color] of colores.entries()) {
    expect(color, `El ícono de ${OPCIONES[indice].nombre}`).toEqual({ color: PRIMARIO, trazo: PRIMARIO, oculto: 'true' });
  }

  // El foco de la tarjeta, en el primario: el primer Tab desde la página recién cargada.
  await page.goto('/');
  await expect(menu(page)).toBeVisible();
  await quieta(page);
  await page.keyboard.press('Tab');
  await quieta(page);
  const primera = tarjetaDe(page, OPCIONES[0].nombre);
  await expect(page.getByRole('link', { name: OPCIONES[0].nombre, exact: true })).toBeFocused();
  const contorno = await contornoDe(primera);
  expect(contorno.color).toBe(PRIMARIO);
  // El contorno (fuera de la tarjeta, separado 2px) contra el fondo efectivo de lo que la rodea: 3:1 o más (NFR-01).
  const [alrededor] = await primera.locator('xpath=..').evaluate(muestrear, 'fondo' as const);
  const contrasteDelFoco = contraste(aRgba(contorno.color), fondoEfectivo(alrededor.capas));
  expect(contrasteDelFoco, 'Contraste del contorno del foco').toBeGreaterThanOrEqual(3);

  // El texto de las tarjetas (títulos de sección, nombres y descripciones): 4,5:1 o más contra su fondo (NFR-01).
  const medidos = contrastes(await menu(page).evaluate(muestrear, 'textos' as const));
  expect(medidos.length, 'No se midió ningún texto del menú').toBeGreaterThanOrEqual(SECCIONES.length + 2 * OPCIONES.length);
  for (const { texto, contraste: relacion } of medidos) {
    expect(relacion, `Contraste de "${texto}"`).toBeGreaterThanOrEqual(4.5);
  }
});

test('sin violaciones de CSP ni error en la consola', async ({ page }) => {
  await ingresar(page);
  // Los errores de consola se cuentan desde el inicio con sesión: antes de ingresar, el arranque consulta la sesión y
  // el navegador anota su 401 esperado como "Failed to load resource" (no es del menú). Las violaciones de CSP se
  // cuentan desde el comienzo.
  erroresDeConsola = [];
  // El recorrido completo del menú: hover, foco con Tab y clic en una tarjeta, en los dos anchos.
  for (const ancho of [360, 1280]) {
    await page.setViewportSize({ width: ancho, height: 800 });
    await tarjetaDe(page, 'Artículos').hover();
    await page.mouse.move(0, 0);
    for (let i = 0; i < OPCIONES.length; i++) {
      await page.keyboard.press('Tab');
    }
    await tarjetaDe(page, 'Configuración').click({ position: { x: 6, y: 6 } });
    await expect(page.getByRole('heading', { level: 1, name: 'Configuración' })).toBeVisible();
    await expect(page.getByText(/^Cargando/)).toHaveCount(0);
    await page.goto('/');
    await expect(menu(page)).toBeVisible();
  }

  expect(violacionesCsp, 'Ninguna violación de Content-Security-Policy').toEqual([]);
  expect(erroresDeConsola, 'Ningún error en la consola').toEqual([]);
});
