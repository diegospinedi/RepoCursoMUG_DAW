import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test';

import { anchoDeLaPagina, contrastes, muestrear, quieta } from './medidas.ts';

/**
 * Estilo uniforme de las 15 pantallas (FEAT-012: AC-11 a AC-16) de punta a punta, contra la Api real que sirve la SPA
 * compilada, en Chrome y Edge (AC-10, NFR-02), con ventanas de 360 px y 1280 px (D-20).
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva: la primera prueba define la
 * contraseña y las siguientes ingresan con ella. Cada prueba siembra por la Api, con la sesión de la página, lo que
 * necesitan las pantallas de detalle: un artículo, un presupuesto en Final con su factura (ARCA simulada autoriza, así
 * que el comprobante no tiene validez fiscal) y un presupuesto en Borrador para la pantalla de modificación, que en
 * Final solo muestra un aviso. Los presupuestos se graban como en la app, en Borrador y después a Final (AGENTS.md).
 * Los datos son ficticios y únicos por puerto y por prueba (códigos `EU-<puerto>-<n>`, DNI que empiezan con 00, emails
 * en example.com).
 *
 * La marca es la del harness (servidor.mjs): primario #0903A0 sobre fondo #FFFFFF, la real de NFR-01. Todo se mide con
 * estilos computados (getComputedStyle y las cajas del navegador): la tarjeta de cada pantalla contra la de una opción
 * del menú (AC-11; Inicio no lleva tarjeta exterior, D-15), el alto de los botones (AC-13), el campo inválido (AC-12),
 * el desplazamiento horizontal (AC-14), el bloque de error (AC-15) y el contraste del texto (AC-16). No se guardan
 * capturas. Cualquier violación de la política de seguridad de contenido hace fallar la prueba (R-02).
 */
test.describe.configure({ mode: 'serial' });

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-estilo-uniforme-1';

const ANCHOS = [360, 1280] as const;

/** Colores computados de la marca del harness y de las variables de diseño (web/src/estilos.css). */
const PRIMARIO = 'rgb(9, 3, 160)';
const BLANCO = 'rgb(255, 255, 255)';
const COLOR_ERROR = 'rgb(176, 0, 32)';
const FONDO_ERROR = 'rgb(253, 236, 238)';

/** Responsable Inscripto (Factura B) con punto de venta 3: la factura sembrada sale de ARCA simulada. */
const CONFIGURACION = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: 100000,
  puntoVenta: 3,
};

/** Lo que cada prueba siembra por la Api para las pantallas de detalle. */
interface Sembrado {
  articulo: number;
  final: number;
  borrador: number;
}

/** Una de las 15 pantallas de FR-09: cómo se abre, su título y lo que hay que hacer para que muestre su contenido. */
interface Pantalla {
  nombre: string;
  ruta: (datos: Sembrado) => string;
  titulo: string;
  /** Inicio no lleva tarjeta exterior: la tarjeta es cada opción del menú (D-15). */
  menu?: boolean;
  /** Las búsquedas muestran su tabla al buscar. */
  buscar?: boolean;
}

const PANTALLAS: Pantalla[] = [
  { nombre: 'Definir la contraseña', ruta: () => '/definir-contrasena', titulo: 'Definí la contraseña de acceso' },
  { nombre: 'Ingreso', ruta: () => '/ingreso', titulo: 'Ingresá al sistema' },
  { nombre: 'Inicio', ruta: () => '/', titulo: 'Sesión iniciada', menu: true },
  { nombre: 'Cambiar la contraseña', ruta: () => '/cambiar-contrasena', titulo: 'Cambiar la contraseña' },
  { nombre: 'Configuración', ruta: () => '/configuracion', titulo: 'Configuración' },
  { nombre: 'Artículos', ruta: () => '/articulos', titulo: 'Artículos' },
  { nombre: 'Nuevo artículo', ruta: () => '/articulos/nuevo', titulo: 'Nuevo artículo' },
  { nombre: 'Editar artículo', ruta: ({ articulo }) => `/articulos/${articulo}`, titulo: 'Editar artículo' },
  { nombre: 'Actualizar costos', ruta: () => '/articulos/actualizar-costos', titulo: 'Actualizar costos' },
  { nombre: 'Buscar presupuestos', ruta: () => '/presupuestos', titulo: 'Buscar presupuestos', buscar: true },
  { nombre: 'Nuevo presupuesto', ruta: () => '/presupuestos/nuevo', titulo: 'Nuevo presupuesto' },
  { nombre: 'Presupuesto', ruta: ({ final }) => `/presupuestos/${final}`, titulo: 'Presupuesto' },
  {
    nombre: 'Modificar presupuesto',
    ruta: ({ borrador }) => `/presupuestos/${borrador}/editar`,
    titulo: 'Modificar presupuesto',
  },
  { nombre: 'Buscar facturas', ruta: () => '/facturas', titulo: 'Buscar facturas', buscar: true },
  { nombre: 'Factura', ruta: ({ final }) => `/facturas/${final}`, titulo: 'Factura' },
];

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

const titulo = (page: Page, texto: string) => page.getByRole('heading', { level: 1, name: texto, exact: true });

/** Entra como la operadora: en la base nueva define la contraseña; después ingresa con ella. */
async function ingresar(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/(definir-contrasena|ingreso)$/);
  await page.getByLabel('Contraseña', { exact: true }).fill(CONTRASENA);
  if (/\/definir-contrasena$/.test(page.url())) {
    await page.getByRole('button', { name: 'Definir contraseña' }).click();
  } else {
    await page.getByRole('button', { name: 'Ingresar' }).click();
  }
  await expect(titulo(page, 'Sesión iniciada')).toBeVisible();
}

/** El puerto de la Api de este proyecto: hace únicos los datos sembrados entre proyectos. */
const puertoDe = (page: Page) => new URL(page.url()).port;

/** Graba por la Api un presupuesto en Borrador con una línea del artículo; devuelve su número y versión. */
async function grabarBorrador(request: APIRequestContext, codigo: number, dni: string) {
  const alta = await request.post('/api/presupuestos', {
    data: {
      cliente: {
        apellido: 'Ficticio',
        nombre: 'Estilo Uniforme',
        dni,
        domicilio: 'Calle Inventada 123, Localidad de Prueba',
        email: 'estilo.uniforme@example.com',
        telefono: '00 0000-0000',
      },
      lineas: [{ codigoArticulo: codigo, cantidad: 2, porcentajeDescuento: 0 }],
    },
  });
  expect(alta.status(), 'No se pudo grabar el presupuesto por la Api').toBe(201);
  return (await alta.json()) as { numero: number; version: number };
}

/**
 * Siembra por la Api, con la sesión de la página: la configuración, un artículo, un presupuesto en Final con su factura
 * simulada y un presupuesto en Borrador. Los códigos y DNI llevan el puerto y el número de la prueba que siembra: así
 * no se repiten entre pruebas de la misma base ni entre proyectos.
 */
async function sembrar(page: Page, prueba: number): Promise<Sembrado> {
  const puerto = puertoDe(page);
  const request = page.request;
  const configuracion = await request.post('/api/configuracion', { data: CONFIGURACION });
  expect(configuracion.status(), 'No se pudo grabar la configuración').toBe(200);

  const codigoProveedor = `EU-${puerto}-${prueba}`;
  const alta = await request.post('/api/articulos', {
    data: { codigoProveedor, descripcion: `Armazón de prueba ${codigoProveedor}`, precioCosto: 1500, margenUtilidad: 20 },
  });
  expect(alta.status(), `No se pudo crear ${codigoProveedor}`).toBe(201);
  const articulo = ((await alta.json()) as { codigo: number }).codigo;

  // DNI ficticio: 00, el puerto y el número de la prueba (D-08 de FEAT-009).
  const dni = `00${puerto}${String(prueba).padStart(2, '0')}`;
  const paraFinal = await grabarBorrador(request, articulo, dni);
  const final = await request.put(`/api/presupuestos/${paraFinal.numero}/estado`, {
    data: { estado: 'final', version: paraFinal.version },
  });
  expect(final.status(), 'No se pudo pasar a Final por la Api').toBe(200);
  const factura = await request.post(`/api/presupuestos/${paraFinal.numero}/factura`, { data: { concepto: 'productos' } });
  expect(factura.status(), 'No se pudo emitir la factura simulada por la Api').toBe(201);

  const borrador = await grabarBorrador(request, articulo, dni);
  return { articulo, final: paraFinal.numero, borrador: borrador.numero };
}

/**
 * Abre la pantalla por su ruta y espera su contenido: el título y ningún "Cargando…". En las búsquedas, busca sin
 * filtros para que se dibuje la tabla. Deja el puntero fuera de la pantalla (el hover cambia sombras y fondos).
 */
async function abrir(page: Page, pantalla: Pantalla, datos: Sembrado) {
  await page.goto(pantalla.ruta(datos));
  await expect(titulo(page, pantalla.titulo), pantalla.nombre).toBeVisible();
  await expect(page.getByText(/^Cargando/)).toHaveCount(0);
  if (pantalla.buscar) {
    await page.getByRole('search').getByRole('button', { name: 'Buscar', exact: true }).click();
    await expect(page.locator('.tabla-contenedor table'), `${pantalla.nombre}: la tabla de resultados`).toBeVisible();
  }
  await page.mouse.move(0, 0);
  await quieta(page);
}

/** Recorre las 15 pantallas con cada ancho y llama a `medir` en cada una, ya cargada. */
async function recorrer(page: Page, datos: Sembrado, medir: (pantalla: Pantalla, ancho: number) => Promise<void>) {
  for (const ancho of ANCHOS) {
    await page.setViewportSize({ width: ancho, height: 800 });
    for (const pantalla of PANTALLAS) {
      await abrir(page, pantalla, datos);
      await medir(pantalla, ancho);
    }
  }
}

/** Lo que define la tarjeta de FR-09: fondo, borde, radio y sombra computados. */
const estiloDeTarjeta = (elemento: Locator) =>
  elemento.evaluate((nodo) => {
    const estilo = getComputedStyle(nodo);
    return {
      fondo: estilo.backgroundColor,
      borde: `${estilo.borderTopWidth} ${estilo.borderTopStyle} ${estilo.borderTopColor}`,
      radio: estilo.borderRadius,
      sombra: estilo.boxShadow,
    };
  });

/** La sección de la pantalla: el padre del `h1` (la estructura h1 → section.pantalla no cambia). */
const seccionDe = (page: Page, texto: string) => titulo(page, texto).locator('xpath=..');

/** El contraste de cada texto visible dentro del elemento (medidas.ts). */
const contrastesDe = async (raiz: Locator) => contrastes(await raiz.evaluate(muestrear, 'textos' as const));

/** Los botones visibles de la pantalla, principales o secundarios, con su alto, su radio y sus colores computados. */
const botonesDe = (page: Page) =>
  page.locator('main').evaluate((main) =>
    [...main.querySelectorAll('button')]
      .filter((boton) => boton.checkVisibility() && boton.closest('dialog') === null)
      .map((boton) => {
        const estilo = getComputedStyle(boton);
        return {
          texto: (boton.textContent ?? '').trim() || (boton.getAttribute('aria-label') ?? ''),
          secundario: boton.classList.contains('boton-secundario'),
          alto: boton.getBoundingClientRect().height,
          altoMinimo: estilo.minHeight,
          // Los renglones de la etiqueta, contados con las cajas de línea del texto (un renglón, una altura distinta):
          // con 360 px una etiqueta larga se parte y el botón crece.
          renglones: (() => {
            const rango = document.createRange();
            rango.selectNodeContents(boton);
            const altos = new Set([...rango.getClientRects()].filter(({ width }) => width > 0).map(({ top }) => Math.round(top)));
            return Math.max(1, altos.size);
          })(),
          renglon: parseFloat(estilo.lineHeight),
          // Lo que suman al alto el relleno y el borde, de arriba y de abajo.
          marco:
            parseFloat(estilo.paddingTop) +
            parseFloat(estilo.paddingBottom) +
            parseFloat(estilo.borderTopWidth) +
            parseFloat(estilo.borderBottomWidth),
          rellenoVertical: `${estilo.paddingTop} ${estilo.paddingBottom}`,
          radio: estilo.borderRadius,
          fondo: estilo.backgroundColor,
          color: estilo.color,
          borde: estilo.borderTopColor,
        };
      }),
  );

test('las 15 pantallas como tarjeta con 360 y 1280 px', async ({ page }) => {
  // 30 aperturas de pantalla (15 × 2 anchos) con siembra: más que los 30 s por defecto en una máquina cargada.
  test.setTimeout(180_000);
  await ingresar(page);
  const datos = await sembrar(page, 1);

  // La tarjeta de referencia: una opción del menú de Inicio, sin el puntero encima.
  await abrir(page, PANTALLAS[2], datos);
  const referencia = await estiloDeTarjeta(page.locator('li.tarjeta-menu').first());
  expect(referencia.fondo).toBe(BLANCO);
  expect(referencia.radio).toBe('8px');
  expect(referencia.sombra).not.toBe('none');

  const tablas: string[] = [];
  await recorrer(page, datos, async (pantalla, ancho) => {
    const donde = `${pantalla.nombre} a ${ancho} px`;
    const seccion = seccionDe(page, pantalla.titulo);
    if (pantalla.menu) {
      // D-15: Inicio sin tarjeta exterior; cada opción del menú es la tarjeta.
      const exterior = await estiloDeTarjeta(seccion);
      expect(exterior.fondo, `${donde}: sin fondo de tarjeta exterior`).toBe('rgba(0, 0, 0, 0)');
      expect(exterior.sombra, `${donde}: sin sombra de tarjeta exterior`).toBe('none');
      const opciones = page.locator('li.tarjeta-menu');
      await expect(opciones).toHaveCount(7);
      for (let i = 0; i < 7; i++) {
        expect(await estiloDeTarjeta(opciones.nth(i)), `${donde}: la tarjeta ${i + 1} del menú`).toEqual(referencia);
      }
    } else {
      // AC-11: el contenido de la pantalla, en una tarjeta igual a las del menú.
      await expect(seccion, `${donde}: la sección de la pantalla`).toHaveClass(/\bpantalla\b/);
      expect(await estiloDeTarjeta(seccion), `${donde}: la tarjeta de la pantalla`).toEqual(referencia);
    }

    // AC-14: la página no se desplaza a lo ancho; las tablas, si lo necesitan, se desplazan dentro de su contenedor.
    const { documento, ventana } = await anchoDeLaPagina(page);
    expect(documento, `${donde}: la página no debe tener desplazamiento horizontal`).toBeLessThanOrEqual(ventana);
    const contenedores = await page.locator('.tabla-contenedor').evaluateAll((elementos) =>
      elementos.map((elemento) => ({
        desborde: getComputedStyle(elemento).overflowX,
        derecha: elemento.getBoundingClientRect().right,
      })),
    );
    for (const contenedor of contenedores) {
      expect(contenedor.desborde, `${donde}: la tabla se desplaza dentro de su contenedor`).toBe('auto');
      expect(contenedor.derecha, `${donde}: el contenedor de la tabla entra en la ventana`).toBeLessThanOrEqual(ventana);
    }
    if (contenedores.length > 0 && ancho === 360) {
      tablas.push(pantalla.nombre);
    }
  });
  // Las pantallas con tablas se midieron con 360 px, con datos sembrados.
  expect(tablas).toEqual(expect.arrayContaining(['Artículos', 'Buscar presupuestos', 'Presupuesto', 'Buscar facturas']));
});

test('botones principales y secundarios con el mismo alto', async ({ page }) => {
  // 30 aperturas de pantalla (15 × 2 anchos) con siembra: más que los 30 s por defecto en una máquina cargada.
  test.setTimeout(180_000);
  await ingresar(page);
  const datos = await sembrar(page, 2);

  const vistos: {
    donde: string;
    texto: string;
    secundario: boolean;
    alto: number;
    altoMinimo: string;
    renglones: number;
    renglon: number;
    marco: number;
    rellenoVertical: string;
    radio: string;
  }[] = [];
  await recorrer(page, datos, async (pantalla, ancho) => {
    const donde = `${pantalla.nombre} a ${ancho} px`;
    for (const boton of await botonesDe(page)) {
      vistos.push({ donde, ...boton });
      // AC-13: los colores de cada tipo, iguales en todas las pantallas.
      if (boton.secundario) {
        expect([boton.fondo, boton.color, boton.borde], `${donde}: "${boton.texto}" secundario`).toEqual([
          BLANCO,
          PRIMARIO,
          PRIMARIO,
        ]);
      } else {
        expect([boton.fondo, boton.color, boton.borde], `${donde}: "${boton.texto}" principal`).toEqual([
          PRIMARIO,
          BLANCO,
          PRIMARIO,
        ]);
      }
    }
  });

  // Hay de los dos tipos, también en las filas de las tablas ("Desactivar" en Artículos, "Quitar" en el presupuesto).
  const principales = vistos.filter(({ secundario }) => !secundario);
  const secundarios = vistos.filter(({ secundario }) => secundario);
  expect(principales.length).toBeGreaterThan(0);
  expect(secundarios.map(({ texto }) => texto)).toEqual(
    expect.arrayContaining(['Salir', 'Desactivar', 'Quitar', 'Descargar PDF']),
  );
  // AC-13: el mismo alto mínimo, relleno vertical, alto y radio que el primer botón principal, en todas las pantallas y
  // con los dos anchos. Solo una etiqueta que con 360 px se parte en dos renglones hace crecer el botón.
  const referencia = principales[0];
  expect(referencia.renglones).toBe(1);
  const { radio, altoMinimo, rellenoVertical } = referencia;
  for (const boton of vistos) {
    expect(boton.altoMinimo, `${boton.donde}: alto mínimo de "${boton.texto}"`).toBe(altoMinimo);
    expect(boton.rellenoVertical, `${boton.donde}: relleno vertical de "${boton.texto}"`).toBe(rellenoVertical);
    expect(boton.radio, `${boton.donde}: radio de "${boton.texto}"`).toBe(radio);
    if (boton.renglones === 1) {
      expect(boton.alto, `${boton.donde}: alto de "${boton.texto}"`).toBeCloseTo(referencia.alto, 0);
    } else {
      // Acotado: crece exactamente los renglones de más. Con el alto mínimo (40px) por encima del alto natural de un
      // renglón (20 + 16 + 2 = 38px), el alto esperado es el mayor entre el mínimo y renglones × renglón + marco; con
      // dos renglones, 58px y no 40 + 20.
      const esperado = Math.max(parseFloat(altoMinimo), boton.renglones * boton.renglon + boton.marco);
      expect(esperado, `${boton.donde}: "${boton.texto}" en ${boton.renglones} renglones crece`).toBeGreaterThan(referencia.alto);
      expect(boton.alto, `${boton.donde}: "${boton.texto}" en ${boton.renglones} renglones`).toBeCloseTo(esperado, 0);
      expect(boton.alto).toBeLessThanOrEqual(referencia.alto + (boton.renglones - 1) * boton.renglon + 0.5);
    }
  }
  // Con 1280 px ninguna etiqueta se parte: todos tienen exactamente el mismo alto.
  for (const boton of vistos.filter(({ donde }) => donde.endsWith('1280 px'))) {
    expect(boton.renglones, `${boton.donde}: "${boton.texto}" en un renglón`).toBe(1);
  }
});

test('campo inválido con borde de error y foco del primario', async ({ page }) => {
  await ingresar(page);

  // Las pantallas con formulario que validan en el navegador al enviar vacío: el botón que envía y cuántos campos.
  const formularios = [
    { nombre: 'Definir la contraseña', ruta: '/definir-contrasena', titulo: 'Definí la contraseña de acceso', boton: 'Definir contraseña' },
    { nombre: 'Ingreso', ruta: '/ingreso', titulo: 'Ingresá al sistema', boton: 'Ingresar' },
    { nombre: 'Cambiar la contraseña', ruta: '/cambiar-contrasena', titulo: 'Cambiar la contraseña', boton: 'Cambiar la contraseña' },
    { nombre: 'Nuevo artículo', ruta: '/articulos/nuevo', titulo: 'Nuevo artículo', boton: 'Grabar' },
    { nombre: 'Nuevo presupuesto', ruta: '/presupuestos/nuevo', titulo: 'Nuevo presupuesto', boton: 'Grabar' },
  ];
  const medidos: { nombre: string; borde: string; sombra: string; contorno: string; mensaje: string }[] = [];
  for (const ancho of ANCHOS) {
    await page.setViewportSize({ width: ancho, height: 800 });
    for (const formulario of formularios) {
      const donde = `${formulario.nombre} a ${ancho} px`;
      await page.goto(formulario.ruta);
      await expect(titulo(page, formulario.titulo)).toBeVisible();
      await expect(page.getByText(/^Cargando/)).toHaveCount(0);
      await page.locator('main').getByRole('button', { name: formulario.boton, exact: true }).click();

      // AC-12: el primer campo con error, con el borde de error y su mensaje junto al campo.
      const campo = page.locator('main [aria-invalid="true"]').first();
      await expect(campo, `${donde}: un campo inválido al enviar vacío`).toBeVisible();
      const idMensaje = await campo.getAttribute('aria-describedby');
      expect(idMensaje, `${donde}: el campo nombra su mensaje`).toBeTruthy();
      const mensaje = page.locator(`[id="${idMensaje!.split(' ')[0]}"]`);
      await expect(mensaje, `${donde}: el mensaje junto al campo`).toBeVisible();
      await expect(mensaje).toHaveClass(/\bcampo__error\b/);

      // El mensaje va justo debajo del campo: empieza donde termina el campo (con el margen de la hoja, unos px) y
      // comparte su borde izquierdo.
      const cajaCampo = await campo.boundingBox();
      const cajaMensaje = await mensaje.boundingBox();
      expect(cajaCampo && cajaMensaje, `${donde}: el campo y el mensaje se dibujan`).toBeTruthy();
      const separacion = cajaMensaje!.y - (cajaCampo!.y + cajaCampo!.height);
      expect(separacion, `${donde}: el mensaje empieza debajo del campo`).toBeGreaterThanOrEqual(0);
      expect(separacion, `${donde}: el mensaje está pegado al campo`).toBeLessThanOrEqual(12);
      expect(cajaMensaje!.x, `${donde}: el mensaje alineado con el campo`).toBeCloseTo(cajaCampo!.x, 0);

      // Sin el foco: el borde de error (el foco no es lo que lo pinta).
      await page.locator('main h1').click();
      await quieta(page);
      const bordeSinFoco = await campo.evaluate((nodo) => getComputedStyle(nodo).borderTopColor);
      expect(bordeSinFoco, `${donde}: borde de error sin el foco`).toBe(COLOR_ERROR);

      // Con el foco: el borde sigue en el color de error y se suma el contorno del primario (2px en `.campo input`).
      await campo.focus();
      await quieta(page);
      const medido = await campo.evaluate((nodo) => {
        const estilo = getComputedStyle(nodo);
        return {
          borde: estilo.borderTopColor,
          sombra: estilo.boxShadow,
          contorno: `${estilo.outlineStyle} ${estilo.outlineWidth} ${estilo.outlineColor}`,
        };
      });
      const colorDelMensaje = await mensaje.evaluate((nodo) => getComputedStyle(nodo).color);
      expect(medido.borde, `${donde}: borde de error con el foco`).toBe(COLOR_ERROR);
      expect(medido.contorno, `${donde}: contorno del primario con el foco`).toBe(`solid 2px ${PRIMARIO}`);
      expect(colorDelMensaje, `${donde}: el mensaje en el color de error`).toBe(COLOR_ERROR);
      // El mensaje de error, legible contra el fondo de la tarjeta (NFR-01).
      for (const { texto, contraste } of await contrastesDe(mensaje)) {
        expect(contraste, `${donde}: contraste de "${texto}"`).toBeGreaterThanOrEqual(4.5);
      }
      medidos.push({ nombre: donde, ...medido, mensaje: colorDelMensaje });
    }
  }
  // Igual en todas las pantallas.
  for (const medido of medidos) {
    expect({ ...medido, nombre: '' }, medido.nombre).toEqual({ ...medidos[0], nombre: '' });
  }
});

test('mensaje de error con el mismo bloque', async ({ page }) => {
  await ingresar(page);

  // Pantallas que muestran un mensaje de error sin cambiar nada: un número que no existe y una definición rechazada.
  const inexistente = 999_999;
  const errores = [
    { nombre: 'Editar artículo', ruta: `/articulos/${inexistente}`, titulo: 'Editar artículo' },
    { nombre: 'Presupuesto', ruta: `/presupuestos/${inexistente}`, titulo: 'Presupuesto' },
    { nombre: 'Modificar presupuesto', ruta: `/presupuestos/${inexistente}/editar`, titulo: 'Modificar presupuesto' },
    { nombre: 'Factura', ruta: `/facturas/${inexistente}`, titulo: 'Factura' },
  ];
  const bloques: { donde: string; bloque: Record<string, string> }[] = [];
  const medir = async (donde: string, mensaje: Locator) => {
    await expect(mensaje, `${donde}: el mensaje de error`).toBeVisible();
    await quieta(page);
    const bloque = await mensaje.evaluate((nodo) => {
      const estilo = getComputedStyle(nodo);
      return {
        borde: `${estilo.borderLeftWidth} ${estilo.borderLeftStyle} ${estilo.borderLeftColor}`,
        fondo: estilo.backgroundColor,
        color: estilo.color,
        radio: estilo.borderRadius,
        relleno: estilo.padding,
        prefijo: getComputedStyle(nodo, '::before').content,
      };
    });
    // AC-15: borde lateral y fondo del color de error, y un prefijo de texto: no depende solo del color (FR-13).
    expect(bloque.borde, `${donde}: borde lateral de error`).toBe(`4px solid ${COLOR_ERROR}`);
    expect(bloque.fondo, `${donde}: fondo de error`).toBe(FONDO_ERROR);
    expect(bloque.color, `${donde}: texto de error`).toBe(COLOR_ERROR);
    expect(bloque.prefijo, `${donde}: prefijo ✕`).toContain('✕');
    for (const { texto, contraste } of await contrastesDe(mensaje)) {
      expect(contraste, `${donde}: contraste de "${texto}"`).toBeGreaterThanOrEqual(4.5);
    }
    bloques.push({ donde, bloque });
  };

  for (const ancho of ANCHOS) {
    await page.setViewportSize({ width: ancho, height: 800 });
    for (const { nombre, ruta, titulo: texto } of errores) {
      await page.goto(ruta);
      await expect(titulo(page, texto)).toBeVisible();
      await medir(`${nombre} a ${ancho} px`, page.locator('main .mensaje-general'));
    }
    // Definir la contraseña cuando ya está definida: la Api la rechaza (409) y el error lleva un enlace (div con p y a).
    await page.goto('/definir-contrasena');
    await page.getByLabel('Contraseña', { exact: true }).fill('e2e-estilo-uniforme-otra');
    await page.getByRole('button', { name: 'Definir contraseña' }).click();
    await medir(`Definir la contraseña a ${ancho} px`, page.locator('main div.mensaje-general'));
  }

  // El mismo bloque en todas las pantallas.
  for (const { donde, bloque } of bloques) {
    expect(bloque, donde).toEqual(bloques[0].bloque);
  }
});

test('contraste y CSP en las 15 pantallas con la marca real', async ({ page }) => {
  // 30 aperturas de pantalla (15 × 2 anchos) con siembra: más que los 30 s por defecto en una máquina cargada.
  test.setTimeout(180_000);
  await ingresar(page);
  const datos = await sembrar(page, 5);
  // La marca instalada en el harness es la real de NFR-01.
  const variables = await page.evaluate(() => {
    const estilo = getComputedStyle(document.documentElement);
    return [estilo.getPropertyValue('--color-primario').trim(), estilo.getPropertyValue('--color-fondo').trim()];
  });
  expect(variables.map((valor) => valor.toUpperCase())).toEqual(['#0903A0', '#FFFFFF']);

  const pantallasMedidas = new Set<string>();
  await recorrer(page, datos, async (pantalla, ancho) => {
    const donde = `${pantalla.nombre} a ${ancho} px`;
    // AC-16: textos, campos, botones, tablas y mensajes de la pantalla, 4,5:1 o más contra su fondo.
    const medidos = await contrastesDe(page.locator('main'));
    expect(medidos.length, `${donde}: no se midió ningún texto`).toBeGreaterThan(0);
    for (const { texto, contraste } of medidos) {
      expect(contraste, `${donde}: contraste de "${texto}"`).toBeGreaterThanOrEqual(4.5);
    }
    expect(violacionesCsp, `${donde}: ninguna violación de Content-Security-Policy`).toEqual([]);
    pantallasMedidas.add(pantalla.nombre);
  });
  expect(pantallasMedidas.size).toBe(15);
});
