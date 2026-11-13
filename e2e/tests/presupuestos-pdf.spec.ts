import { readFile } from 'node:fs/promises';
import { expect, test, type APIRequestContext, type Page, type Request } from '@playwright/test';
import { getDocument, VerbosityLevel } from 'pdfjs-dist/legacy/build/pdf.mjs';

/**
 * Descarga del PDF del presupuesto (FEAT-006: AC-11 y AC-12 en la pantalla) de punta a punta, contra la Api real que
 * sirve la SPA compilada, en Chrome y Edge (AC-17). El contenido del PDF (textos, colores, logo, importes) se verifica
 * en .NET con PdfPig (D-02); acá se verifica que el navegador descarga un PDF válido con el número del presupuesto, que
 * la Content-Security-Policy de la SPA no bloquea la descarga (D-10) y que un Borrador no se descarga ni por la
 * pantalla ni con un pedido directo a la Api.
 *
 * El PDF descargado se lee en Node con el build `legacy` de pdfjs-dist: sin worker (en Node usa el suyo en el mismo
 * proceso), sin fuentes del sistema ni descarga de fuentes, CMaps o archivos wasm (no se le da ninguna URL), así que no
 * pide nada a ningún host.
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva. La primera prueba que corre
 * define la contraseña y las demás ingresan con ella, así que no dependen del orden. Cada prueba deja por la Api la
 * configuración de partida, crea sus artículos con códigos en el proveedor propios ("PP-…") y graba sus presupuestos
 * como la app: en Borrador y, para un Final, después pasa a Final (AGENTS.md).
 *
 * Datos personales: el cliente es ficticio, con un DNI improbable (99.999.999), un teléfono de ceros y un email en
 * example.com. El PDF descargado queda en el directorio temporal de Playwright, que se borra al cerrar el contexto.
 */

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-presupuestos-pdf-1';

const MENSAJE_BORRADOR_PDF = 'El presupuesto está en Borrador: pasalo a Final para descargar el PDF.';

/** Configuración de partida de cada prueba, como la pide el POST de la Api. */
const PARTIDA = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: null,
};

/** Un cliente ficticio, con datos elegidos para que sea improbable que coincidan con los de una persona real. */
const CLIENTE = {
  apellido: 'Ficticio',
  nombre: 'Cliente del PDF',
  dni: '99.999.999',
  domicilio: 'Calle Inventada 123, Localidad de Prueba',
  email: 'cliente.pdf@example.com',
  telefono: '00 0000-0000',
};

interface PresupuestoGrabado {
  numero: number;
  estado: string;
  version: number;
}

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

const campo = (page: Page, etiqueta: string) => page.getByLabel(etiqueta, { exact: true });
const botonPdf = (page: Page) => page.getByRole('button', { name: 'Descargar PDF', exact: true });
const escaparRegex = (texto: string) => texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const dato = (page: Page, etiqueta: string) =>
  page
    .locator('dl.datos > div')
    .filter({ has: page.locator('dt', { hasText: new RegExp(`^${escaparRegex(etiqueta)}$`) }) })
    .locator('dd');
const esPedidoDelPdf = (url: string, numero: number) => new URL(url).pathname === `/api/presupuestos/${numero}/pdf`;

/**
 * Entra como la operadora: en la base nueva define la contraseña; después ingresa con ella. Deja por la Api la
 * configuración de partida.
 */
async function ingresarConConfiguracion(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/(definir-contrasena|ingreso)$/);
  await campo(page, 'Contraseña').fill(CONTRASENA);
  if (/\/definir-contrasena$/.test(page.url())) {
    await page.getByRole('button', { name: 'Definir contraseña' }).click();
  } else {
    await page.getByRole('button', { name: 'Ingresar' }).click();
  }
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  const respuesta = await page.request.post('/api/configuracion', { data: PARTIDA });
  expect(respuesta.status(), 'No se pudo fijar la configuración de partida').toBe(200);
}

/** Graba por la Api un artículo y un presupuesto en Borrador con una línea de ese artículo. */
async function grabarBorrador(request: APIRequestContext, codigoProveedor: string): Promise<PresupuestoGrabado> {
  const articulo = await request.post('/api/articulos', {
    data: { codigoProveedor, descripcion: `Artículo del PDF ${codigoProveedor}`, precioCosto: 1000, margenUtilidad: 0 },
  });
  expect(articulo.status(), `No se pudo crear ${codigoProveedor}`).toBe(201);
  const { codigo } = (await articulo.json()) as { codigo: number };
  const respuesta = await request.post('/api/presupuestos', {
    data: { cliente: CLIENTE, lineas: [{ codigoArticulo: codigo, cantidad: 1, porcentajeDescuento: 0 }] },
  });
  expect(respuesta.status(), 'No se pudo grabar el presupuesto por la Api').toBe(201);
  const grabado = (await respuesta.json()) as PresupuestoGrabado;
  expect(grabado.estado).toBe('borrador');
  return grabado;
}

/** Pasa a Final por la Api, con la versión leída, como lo hace la pantalla al confirmar. */
async function pasarAFinalPorApi(request: APIRequestContext, presupuesto: PresupuestoGrabado) {
  const respuesta = await request.put(`/api/presupuestos/${presupuesto.numero}/estado`, {
    data: { estado: 'final', version: presupuesto.version },
  });
  expect(respuesta.status(), 'No se pudo pasar a Final por la Api').toBe(200);
  const final = (await respuesta.json()) as PresupuestoGrabado;
  expect(final.estado).toBe('final');
  return final;
}

/** Abre el presupuesto como la operadora y espera que se muestre. */
async function abrirPresupuesto(page: Page, numero: number) {
  await page.goto(`/presupuestos/${numero}`);
  await expect(dato(page, 'Número')).toHaveText(String(numero));
}

/**
 * El texto de todas las páginas de un PDF, leído con pdfjs-dist sin worker ni recursos externos: ninguna URL de
 * fuentes estándar, CMaps, ICC o wasm, sin fuentes del sistema y sin `fetch`. Falla si el archivo no es un PDF válido.
 */
async function textoDelPdf(bytes: Uint8Array): Promise<{ paginas: number; texto: string }> {
  const carga = getDocument({
    data: bytes,
    disableFontFace: true,
    useSystemFonts: false,
    useWorkerFetch: false,
    useWasm: false,
    stopAtErrors: true,
    verbosity: VerbosityLevel.ERRORS,
  });
  try {
    const documento = await carga.promise;
    const partes: string[] = [];
    for (let numero = 1; numero <= documento.numPages; numero++) {
      const pagina = await documento.getPage(numero);
      const contenido = await pagina.getTextContent();
      for (const item of contenido.items) {
        if ('str' in item) {
          partes.push(item.str, item.hasEOL ? '\n' : ' ');
        }
      }
    }
    return { paginas: documento.numPages, texto: partes.join('').replace(/[ \t]+/g, ' ') };
  } finally {
    // En pdfjs-dist 6 el documento se libera desde la tarea de carga.
    await carga.destroy();
  }
}

test('descarga el PDF de un Final con el número', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const final = await pasarAFinalPorApi(page.request, await grabarBorrador(page.request, 'PP-FINAL'));
  await abrirPresupuesto(page, final.numero);
  await expect(dato(page, 'Estado')).toHaveText('Final');

  // La SPA se sirve con su CSP (D-10): la descarga tiene que funcionar con esa política activa.
  const documento = await page.request.get(`/presupuestos/${final.numero}`);
  expect(documento.headers()['content-security-policy']).toContain("default-src 'self'");
  const violaciones: string[] = [];
  await page.exposeFunction('registrarViolacionCsp', (texto: string) => violaciones.push(texto));
  await page.evaluate(() => {
    document.addEventListener('securitypolicyviolation', (evento) => {
      void (window as unknown as { registrarViolacionCsp: (t: string) => void }).registrarViolacionCsp(
        `${evento.violatedDirective} ${evento.blockedURI}`,
      );
    });
  });

  // AC-11: el botón está habilitado en Final y descarga presupuesto-N.pdf.
  await expect(botonPdf(page)).toBeEnabled();
  const [descarga, respuesta] = await Promise.all([
    page.waitForEvent('download'),
    page.waitForResponse((r) => esPedidoDelPdf(r.url(), final.numero)),
    botonPdf(page).click(),
  ]);
  expect(respuesta.status()).toBe(200);
  expect(respuesta.headers()['content-type']).toMatch(/^application\/pdf/);
  expect(descarga.suggestedFilename()).toBe(`presupuesto-${final.numero}.pdf`);
  // El navegador la descarga desde la URL de objeto, no con un enlace directo a la Api (D-10).
  expect(descarga.url()).toMatch(/^blob:/);
  expect(await descarga.failure()).toBeNull();

  const bytes = new Uint8Array(await readFile(await descarga.path()));
  expect(Buffer.from(bytes.subarray(0, 5)).toString('latin1')).toBe('%PDF-');
  const { paginas, texto } = await textoDelPdf(bytes);
  expect(paginas).toBeGreaterThanOrEqual(1);
  expect(texto).toContain(`Presupuesto N.º ${final.numero}`);

  // Ni la descarga ni la URL de objeto violaron la CSP, y la pantalla sigue igual, con el botón habilitado.
  expect(violaciones).toEqual([]);
  await expect(page).toHaveURL(new RegExp(`/presupuestos/${final.numero}$`));
  await expect(botonPdf(page)).toBeEnabled();
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('Borrador: botón deshabilitado y la Api rechaza el pedido directo', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const borrador = await grabarBorrador(page.request, 'PP-BORRADOR');
  // Los pedidos del navegador al PDF de este presupuesto (los de `page.request` no pasan por la página).
  const pedidosDelPdf: string[] = [];
  const registrar = (pedido: Request) => {
    if (esPedidoDelPdf(pedido.url(), borrador.numero)) pedidosDelPdf.push(pedido.url());
  };
  page.on('request', registrar);

  // AC-12 en la pantalla: el botón está deshabilitado (atributo `disabled`) y no pide nada.
  await abrirPresupuesto(page, borrador.numero);
  await expect(dato(page, 'Estado')).toHaveText('Borrador');
  await expect(botonPdf(page)).toBeDisabled();
  await expect(botonPdf(page)).toHaveAttribute('disabled', '');
  await botonPdf(page).click({ force: true });

  // AC-12 con un pedido directo a la Api, con la sesión: 409 `borrador`, sin PDF.
  const directo = await page.request.get(`/api/presupuestos/${borrador.numero}/pdf`);
  expect(directo.status()).toBe(409);
  expect(directo.headers()['content-type']).not.toMatch(/application\/pdf/);
  const cuerpo = (await directo.json()) as { motivo?: string; title?: string };
  expect(cuerpo.motivo).toBe('borrador');
  expect(cuerpo.title).toBe('No se pudo generar el PDF del presupuesto.');
  expect((await directo.body()).subarray(0, 5).toString('latin1')).not.toBe('%PDF-');

  // Para entonces, el clic sobre el botón deshabilitado ya habría salido: el navegador no pidió el PDF. El listener se
  // quita antes del pedido desde el navegador, que sí se registraría.
  expect(pedidosDelPdf).toEqual([]);
  page.off('request', registrar);

  // El mismo pedido desde el navegador (como lo haría alguien que saltea la pantalla) también se rechaza.
  const desdeElNavegador = await page.evaluate(async (ruta) => {
    const respuesta = await fetch(ruta, { credentials: 'same-origin' });
    return { estado: respuesta.status, tipo: respuesta.headers.get('Content-Type') ?? '' };
  }, `/api/presupuestos/${borrador.numero}/pdf`);
  expect(desdeElNavegador.estado).toBe(409);
  expect(desdeElNavegador.tipo).not.toMatch(/application\/pdf/);

  // El presupuesto sigue en Borrador y la pantalla no muestra el mensaje de la descarga (no se pidió).
  await expect(page.getByText(MENSAJE_BORRADOR_PDF, { exact: true })).toHaveCount(0);
  const releido = await page.request.get(`/api/presupuestos/${borrador.numero}`);
  expect(((await releido.json()) as PresupuestoGrabado).estado).toBe('borrador');
});
