import { readFile } from 'node:fs/promises';
import { expect, test, type APIRequestContext, type Download, type Page } from '@playwright/test';
import { getDocument, OPS, VerbosityLevel } from 'pdfjs-dist/legacy/build/pdf.mjs';

/**
 * PDF de la factura (FEAT-009: AC-05 a AC-15 y AC-27) de punta a punta, contra la Api real que sirve la SPA compilada,
 * en Chrome y Edge (AC-25). El PDF se descarga desde el detalle de la factura, como la operadora, y se lee en Node con
 * el build `legacy` de pdfjs-dist: sin worker, sin fuentes del sistema ni descarga de fuentes, CMaps o wasm (no se le da
 * ninguna URL), así que no pide nada a ningún host.
 *
 * ARCA es el simulador (`Arca__Entorno=Simulado`): no sale de la máquina y sus comprobantes no tienen validez fiscal,
 * por eso todos los PDF de este archivo llevan la leyenda de comprobante simulado (AC-15). servidor.mjs deja el contador
 * de la Factura B del punto de venta 3 en 124 (`ARCA_POR_ARCHIVO` de compilacion.mjs), así la primera factura de esta
 * base es la 0003-00000125 (AC-08). La fecha de emisión es la del día en que corre la prueba: la factura no se puede
 * fechar el 30/10/2026 del ejemplo (el simulador emite con la fecha del día y una factura con CAE no se modifica); la
 * fecha fija se prueba en .NET (`Pdf_ComprobanteYFecha_B000300000125`).
 *
 * AC-16 (sin leyenda en producción) no se prueba acá: el harness corre siempre en `Simulado` y nunca debe emitir contra
 * ARCA producción (AGENTS.md), así que no hay forma segura de tener una factura de producción en esta suite. Queda
 * cubierto en .NET (`Pdf_Produccion_SinLeyenda` y `ReglasComprobanteImpreso_CondicionYValidezFiscal`). Tampoco se
 * escanea el QR (D-16): se verifica que el PDF tiene la imagen cuadrada del QR junto al CAE; el contenido del QR se
 * prueba en .NET (`QrArca_*`) y el escaneo con un teléfono queda como verificación manual.
 *
 * Las pruebas corren en orden porque dependen de lo grabado: la primera pide el PDF antes de cargar los datos del
 * emisor (AC-05; una vez cargados no se pueden borrar) y las siguientes los cargan por la Api. Los artículos usan
 * códigos propios (`FP-…`) y los presupuestos se graban como en la app: en Borrador y después pasan a Final
 * (AGENTS.md); la factura se emite por la Api (la emisión desde la pantalla la prueba facturacion.spec.ts).
 *
 * Datos personales: los clientes son ficticios, con DNI que empiezan con 00 (D-08), teléfonos de ceros y emails en
 * example.com; el CUIT del emisor es el ficticio 20-00000000-1 (D-33). Los PDF descargados quedan en el directorio
 * temporal de Playwright, que se borra al cerrar el contexto.
 */
test.describe.configure({ mode: 'serial' });

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-facturas-pdf-1';

const LEYENDA_SIMULADO = 'COMPROBANTE SIMULADO — SIN VALIDEZ FISCAL';
const LEYENDA_LEY_27743 = 'Régimen de Transparencia Fiscal al Consumidor (Ley 27.743)';
const MENSAJE_SIN_DATOS_EMISOR = 'Completá los datos del emisor en la configuración para generar el PDF de la factura.';
const PRIMERA_FACTURA_B = 'Factura B 0003-00000125';

/** La configuración de partida: Responsable Inscripto (Factura B), alícuota 21, punto de venta 3 y tope $100.000. */
const CONFIGURACION = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: 100000,
  puntoVenta: 3,
};
/** Con tope de $1.000: una factura de más de $1.000 identifica al receptor con su DNI (FR-11 de FEAT-007). */
const CON_TOPE_BAJO = { ...CONFIGURACION, topeIdentificacion: 1000 };
/** Monotributo: Factura C (AC-14, AC-27). */
const MONOTRIBUTO = { ...CONFIGURACION, condicionFiscal: 'monotributo' };

/** Los datos ficticios de una óptica inventada (D-33: CUIT 20-00000000-1). */
const EMISOR = {
  razonSocial: 'Óptica de Prueba S.R.L.',
  domicilioComercial: 'Calle Inventada 123, Localidad de Prueba',
  cuit: '20-00000000-1',
  ingresosBrutos: '901-000000-0',
  inicioActividades: '2020-03-02',
};

interface Cliente {
  apellido: string;
  nombre: string;
  dni: string;
  domicilio: string;
  email: string;
  telefono: string;
}

/** AC-10: "Pérez, Ana" con el DNI ficticio 00123456 (D-08). */
const PEREZ: Cliente = {
  apellido: 'Pérez',
  nombre: 'Ana',
  dni: '00.123.456',
  domicilio: 'Calle Inventada 456, Localidad de Prueba',
  email: 'ana.perez.factura@example.com',
  telefono: '00 0000-0000',
};
/** El cliente de las facturas sin identificar al receptor (AC-09): su DNI no va en el PDF. */
const SIN_IDENTIFICAR: Cliente = {
  apellido: 'Ficticio',
  nombre: 'Cliente sin identificar',
  dni: '00.718.294',
  domicilio: 'Calle Inventada 789, Localidad de Prueba',
  email: 'cliente.sin.identificar@example.com',
  telefono: '00 0000-0000',
};

interface PresupuestoGrabado {
  numero: number;
  estado: string;
  version: number;
  total: number;
}

interface FacturaEmitida {
  comprobante: string;
  tipo: string;
  numero: number;
  total: number;
}

interface FacturaDetalle {
  comprobante: string;
  fecha: string;
  presupuesto: number;
  receptor: string;
  total: number;
  ivaContenido: number | null;
  cae: string;
  vencimientoCae: string;
  simulada: boolean;
}

/** El presupuesto de la Factura B 0003-00000125 (la primera prueba lo factura; las siguientes lo usan). */
let presupuestoDeLaPrimera = 0;

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
const esPedidoDelPdf = (url: string, numero: number) =>
  new URL(url).pathname === `/api/presupuestos/${numero}/factura/pdf`;

/** `yyyy-MM-dd` como dd/mm/aaaa. */
function comoSeMuestra(fecha: string): string {
  const [anio, mes, dia] = fecha.split('-');
  return `${dia}/${mes}/${anio}`;
}

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

/** Graba los datos del emisor por la Api (la pantalla la prueba configuracion-emisor.spec.ts). */
async function grabarEmisor(request: APIRequestContext) {
  const respuesta = await request.post('/api/configuracion/emisor', { data: EMISOR });
  expect(respuesta.status(), 'No se pudieron grabar los datos del emisor').toBe(200);
}

/** Graba por la Api un artículo con ese costo y sin margen (el precio de venta es el costo); devuelve su código. */
async function crearArticulo(request: APIRequestContext, codigoProveedor: string, precioCosto: number) {
  const respuesta = await request.post('/api/articulos', {
    data: { codigoProveedor, descripcion: `Artículo ${codigoProveedor}`, precioCosto, margenUtilidad: 0 },
  });
  expect(respuesta.status(), `No se pudo crear ${codigoProveedor}`).toBe(201);
  const articulo = (await respuesta.json()) as { codigo: number; precioVenta: number };
  expect(articulo.precioVenta).toBe(precioCosto);
  return articulo.codigo;
}

/** Graba por la Api un presupuesto en Borrador con esas líneas y lo pasa a Final, como en la app. */
async function grabarFinal(
  request: APIRequestContext,
  cliente: Cliente,
  lineas: { codigo: number; cantidad: number }[],
): Promise<PresupuestoGrabado> {
  const alta = await request.post('/api/presupuestos', {
    data: {
      cliente,
      lineas: lineas.map(({ codigo, cantidad }) => ({ codigoArticulo: codigo, cantidad, porcentajeDescuento: 0 })),
    },
  });
  expect(alta.status(), 'No se pudo grabar el presupuesto por la Api').toBe(201);
  const borrador = (await alta.json()) as PresupuestoGrabado;
  const final = await request.put(`/api/presupuestos/${borrador.numero}/estado`, {
    data: { estado: 'final', version: borrador.version },
  });
  expect(final.status(), 'No se pudo pasar a Final por la Api').toBe(200);
  return (await final.json()) as PresupuestoGrabado;
}

/** Emite la factura del presupuesto por la Api (ARCA simulada autoriza). */
async function emitir(request: APIRequestContext, numero: number): Promise<FacturaEmitida> {
  const respuesta = await request.post(`/api/presupuestos/${numero}/factura`, { data: { concepto: 'productos' } });
  expect(respuesta.status(), 'No se pudo emitir la factura por la Api').toBe(201);
  return (await respuesta.json()) as FacturaEmitida;
}

/** El detalle de la factura de un presupuesto, como lo devuelve la Api. */
async function detalle(request: APIRequestContext, numero: number): Promise<FacturaDetalle> {
  const respuesta = await request.get(`/api/presupuestos/${numero}/factura`);
  expect(respuesta.status()).toBe(200);
  return (await respuesta.json()) as FacturaDetalle;
}

/** Abre el detalle de la factura desde el presupuesto, por el enlace de la fila "Factura" (FR-24). */
async function abrirDesdeElPresupuesto(page: Page, numero: number, comprobante: string) {
  await page.goto(`/presupuestos/${numero}`);
  await expect(dato(page, 'Número')).toHaveText(String(numero));
  await dato(page, 'Factura').getByRole('link', { name: comprobante }).click();
  await expect(page).toHaveURL(new RegExp(`/facturas/${numero}$`));
  await expect(page.getByRole('heading', { name: 'Factura', exact: true })).toBeVisible();
  await expect(dato(page, 'Comprobante')).toHaveText(comprobante);
}

/** Abre el detalle de la factura de un presupuesto por su ruta. */
async function abrirDetalle(page: Page, numero: number, comprobante: string) {
  await page.goto(`/facturas/${numero}`);
  await expect(dato(page, 'Comprobante')).toHaveText(comprobante);
}

/**
 * Pulsa Descargar PDF en el detalle y devuelve la descarga y sus bytes. Verifica la respuesta de la Api, el nombre del
 * archivo (solo el comprobante, nunca datos del cliente) y que el navegador la descarga desde una URL de objeto.
 */
async function descargar(page: Page, numero: number, comprobante: string): Promise<{ descarga: Download; bytes: Uint8Array }> {
  const [descarga, respuesta] = await Promise.all([
    page.waitForEvent('download'),
    page.waitForResponse((r) => esPedidoDelPdf(r.url(), numero)),
    botonPdf(page).click(),
  ]);
  expect(respuesta.status()).toBe(200);
  expect(respuesta.headers()['content-type']).toMatch(/^application\/pdf/);
  expect(respuesta.headers()['content-disposition']).toContain(
    `filename=factura-${comprobante.replace('Factura ', '').replace(' ', '-')}.pdf`,
  );
  expect(descarga.suggestedFilename()).toBe(`factura-${comprobante.replace('Factura ', '').replace(' ', '-')}.pdf`);
  expect(descarga.url()).toMatch(/^blob:/);
  expect(await descarga.failure()).toBeNull();
  const bytes = new Uint8Array(await readFile(await descarga.path()));
  expect(Buffer.from(bytes.subarray(0, 5)).toString('latin1')).toBe('%PDF-');
  return { descarga, bytes };
}

interface PdfLeido {
  /** El texto de cada página, con los espacios repetidos colapsados. */
  paginas: string[];
  /** Todo el texto, página tras página. */
  texto: string;
  /** El ancho y el alto, en píxeles, de cada imagen dibujada. */
  imagenes: { ancho: number; alto: number }[];
  /** Los colores de relleno usados, en hexadecimal y minúsculas ("#0903a0"). */
  colores: Set<string>;
}

/** Un color de relleno como lo entrega pdfjs (hexadecimal, o tres componentes de 0 a 255), en "#rrggbb". */
function colorHex(argumentos: unknown[]): string | null {
  const [primero] = argumentos;
  if (typeof primero === 'string') {
    return primero.toLowerCase();
  }
  if (argumentos.length >= 3 && argumentos.slice(0, 3).every((c) => typeof c === 'number')) {
    return `#${(argumentos.slice(0, 3) as number[]).map((c) => Math.round(c).toString(16).padStart(2, '0')).join('')}`;
  }
  return null;
}

/**
 * Lee un PDF con pdfjs-dist sin worker ni recursos externos (ninguna URL de fuentes estándar, CMaps, ICC o wasm, sin
 * fuentes del sistema y sin `fetch`): el texto de cada página, las imágenes y los colores de relleno. Falla si el
 * archivo no es un PDF válido.
 */
async function leerPdf(bytes: Uint8Array): Promise<PdfLeido> {
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
    const paginas: string[] = [];
    const imagenes: { ancho: number; alto: number }[] = [];
    const colores = new Set<string>();
    for (let numero = 1; numero <= documento.numPages; numero++) {
      const pagina = await documento.getPage(numero);
      const contenido = await pagina.getTextContent();
      const partes: string[] = [];
      for (const item of contenido.items) {
        if ('str' in item) {
          partes.push(item.str, item.hasEOL ? '\n' : ' ');
        }
      }
      paginas.push(partes.join('').replace(/\s+/g, ' ').trim());
      const operaciones = await pagina.getOperatorList();
      operaciones.fnArray.forEach((operacion, indice) => {
        const argumentos = (operaciones.argsArray[indice] ?? []) as unknown[];
        if (operacion === OPS.paintImageXObject || operacion === OPS.paintInlineImageXObject) {
          const [, ancho, alto] = argumentos as [unknown, number, number];
          imagenes.push({ ancho, alto });
        } else if (operacion === OPS.setFillRGBColor) {
          const color = colorHex(argumentos);
          if (color !== null) colores.add(color);
        }
      });
    }
    return { paginas, texto: paginas.join('\n'), imagenes, colores };
  } finally {
    // En pdfjs-dist 6 el documento se libera desde la tarea de carga.
    await carga.destroy();
  }
}

/** Cada página lleva la leyenda de comprobante simulado (AC-15). */
function leyendaEnCadaPagina(pdf: PdfLeido) {
  expect(pdf.paginas.length).toBeGreaterThanOrEqual(1);
  pdf.paginas.forEach((texto, indice) => {
    expect(texto, `La página ${indice + 1} no tiene la leyenda de comprobante simulado`).toContain(LEYENDA_SIMULADO);
  });
}

/** Los cinco datos del emisor y la condición frente al IVA (AC-07, AC-27). */
function datosDelEmisor(pdf: PdfLeido, condicion: string) {
  expect(pdf.texto).toContain(`Razón social: ${EMISOR.razonSocial}`);
  expect(pdf.texto).toContain(`Domicilio comercial: ${EMISOR.domicilioComercial}`);
  expect(pdf.texto).toContain(`CUIT: ${EMISOR.cuit}`);
  expect(pdf.texto).toContain(`Ingresos Brutos: ${EMISOR.ingresosBrutos}`);
  expect(pdf.texto).toContain(`Inicio de actividades: ${comoSeMuestra(EMISOR.inicioActividades)}`);
  expect(pdf.texto).toContain(`Condición frente al IVA: ${condicion}`);
}

/** El CAE y su vencimiento en el texto, y la imagen cuadrada del QR (AC-12, D-16). */
function caeYQr(pdf: PdfLeido, factura: FacturaDetalle) {
  expect(factura.cae).toMatch(/^\d{14}$/);
  expect(pdf.texto).toContain(`CAE: ${factura.cae}`);
  expect(pdf.texto).toContain(`Vencimiento del CAE: ${comoSeMuestra(factura.vencimientoCae)}`);
  const cuadradas = pdf.imagenes.filter(({ ancho, alto }) => ancho > 0 && ancho === alto);
  expect(cuadradas.length, 'El PDF no tiene la imagen del QR').toBeGreaterThanOrEqual(1);
}

test('PDF de la factura: sin datos del emisor no se genera y se pide completarlos', async ({ page }) => {
  // La Factura B 0003-00000125 de AC-08, a "Pérez, Ana" con DNI (AC-10): $2.710,02 supera el tope de $1.000. Sus dos
  // líneas son las de AC-11: 3 unidades de $3,34 ($10,02) y una de $2.700,00.
  await ingresar(page);
  await configurar(page.request, CON_TOPE_BAJO);
  const chico = await crearArticulo(page.request, 'FP-UNIDAD', 3.34);
  const grande = await crearArticulo(page.request, 'FP-ARMAZON', 2700);
  const presupuesto = await grabarFinal(page.request, PEREZ, [
    { codigo: chico, cantidad: 3 },
    { codigo: grande, cantidad: 1 },
  ]);
  expect(presupuesto.total).toBe(2710.02);
  const factura = await emitir(page.request, presupuesto.numero);
  expect(factura).toMatchObject({ comprobante: PRIMERA_FACTURA_B, tipo: 'b', numero: 125, total: 2710.02 });
  presupuestoDeLaPrimera = presupuesto.numero;

  // La configuración no tiene datos del emisor: la Api los devuelve en null.
  const emisor = await page.request.get('/api/configuracion/emisor');
  expect(((await emisor.json()) as { razonSocial: string | null }).razonSocial).toBeNull();

  // AC-05 en la pantalla: Descargar PDF no descarga nada y avisa que se completen en la configuración, con el enlace.
  const descargas: Download[] = [];
  page.on('download', (descarga) => descargas.push(descarga));
  await abrirDesdeElPresupuesto(page, presupuesto.numero, PRIMERA_FACTURA_B);
  const [respuesta] = await Promise.all([
    page.waitForResponse((r) => esPedidoDelPdf(r.url(), presupuesto.numero)),
    botonPdf(page).click(),
  ]);
  expect(respuesta.status()).toBe(409);
  const alerta = page.getByRole('alert');
  await expect(alerta).toContainText(MENSAJE_SIN_DATOS_EMISOR);
  await expect(alerta.getByRole('link', { name: 'Ir a la configuración' })).toHaveAttribute('href', '/configuracion');

  // AC-05 con un pedido directo a la Api: 409 `sin_datos_emisor`, sin PDF.
  const directo = await page.request.get(`/api/presupuestos/${presupuesto.numero}/factura/pdf`);
  expect(directo.status()).toBe(409);
  expect(directo.headers()['content-type']).not.toMatch(/application\/pdf/);
  expect(((await directo.json()) as { motivo?: string }).motivo).toBe('sin_datos_emisor');

  // El enlace lleva a la configuración, donde está la sección de los datos del emisor; no se descargó nada.
  await alerta.getByRole('link', { name: 'Ir a la configuración' }).click();
  await expect(page).toHaveURL(/\/configuracion$/);
  await expect(page.getByRole('heading', { name: 'Datos del emisor' })).toBeVisible();
  expect(descargas).toEqual([]);
});

test('PDF de la Factura B 0003-00000125 a Pérez, Ana: emisor, comprobante, líneas, CAE y QR', async ({ page }) => {
  expect(presupuestoDeLaPrimera, 'La prueba anterior no emitió la 0003-00000125').toBeGreaterThan(0);
  await ingresar(page);
  await grabarEmisor(page.request);
  const factura = await detalle(page.request, presupuestoDeLaPrimera);
  expect(factura).toMatchObject({ comprobante: PRIMERA_FACTURA_B, receptor: 'DNI 00123456', simulada: true });

  // AC-06: Descargar PDF en la factura autorizada descarga el PDF en la PC.
  await abrirDesdeElPresupuesto(page, presupuestoDeLaPrimera, PRIMERA_FACTURA_B);
  await expect(page.getByText(LEYENDA_SIMULADO, { exact: true })).toBeVisible();
  const { bytes } = await descargar(page, presupuestoDeLaPrimera, PRIMERA_FACTURA_B);
  const pdf = await leerPdf(bytes);

  // AC-06: el color primario #0903A0 (título, datos del comprobante y el logo de prueba) sobre el fondo #FFFFFF. El
  // dibujo del logo se verifica en .NET (`Pdf_LogoYColores`).
  expect([...pdf.colores]).toContain('#0903a0');
  expect([...pdf.colores]).toContain('#ffffff');

  // AC-07: los cinco datos del emisor y "Responsable Inscripto".
  datosDelEmisor(pdf, 'Responsable Inscripto');

  // AC-08: la letra B, el punto de venta 0003, el número 00000125 y la fecha de emisión.
  expect(pdf.texto).toContain('Factura B');
  expect(pdf.texto).toContain('Punto de venta: 0003');
  expect(pdf.texto).toContain('Número: 00000125');
  expect(pdf.texto).toContain(`Fecha de emisión: ${comoSeMuestra(factura.fecha)}`);

  // AC-10: el receptor con DNI 00123456, Apellido "Pérez" y Nombre "Ana"; no dice Consumidor Final.
  expect(pdf.texto).toContain('DNI: 00123456');
  expect(pdf.texto).toContain('Apellido: Pérez');
  expect(pdf.texto).toContain('Nombre: Ana');
  expect(pdf.texto).not.toContain('Consumidor Final');

  // AC-11: las dos líneas con descripción, cantidad e importe, y el total de $2.710,02, con 2 decimales (NFR-02).
  expect(pdf.texto).toMatch(/Artículo FP-UNIDAD 3 10,02/);
  expect(pdf.texto).toMatch(/Artículo FP-ARMAZON 1 2\.700,00/);
  expect(pdf.texto).toContain('Total: $ 2.710,02');

  // AC-12: el CAE, su vencimiento y la imagen del QR.
  caeYQr(pdf, factura);

  // AC-15: la leyenda de comprobante simulado en cada página.
  leyendaEnCadaPagina(pdf);
});

test('PDF de una Factura B a Consumidor Final de $1.815,00: sin DNI y con la leyenda de la Ley 27.743', async ({ page }) => {
  await ingresar(page);
  await configurar(page.request, CONFIGURACION);
  const articulo = await crearArticulo(page.request, 'FP-LENTE', 1815);
  // $1.815,00 no supera el tope de $100.000: la factura no identifica al receptor (AC-09).
  const presupuesto = await grabarFinal(page.request, SIN_IDENTIFICAR, [{ codigo: articulo, cantidad: 1 }]);
  const emitida = await emitir(page.request, presupuesto.numero);
  expect(emitida).toMatchObject({ tipo: 'b', total: 1815 });
  const factura = await detalle(page.request, presupuesto.numero);
  expect(factura).toMatchObject({ receptor: 'Consumidor Final', ivaContenido: 315 });

  await abrirDetalle(page, presupuesto.numero, emitida.comprobante);
  await expect(dato(page, 'Receptor')).toHaveText('Consumidor Final');
  await expect(dato(page, 'IVA contenido ($)')).toHaveText('315,00');
  const pdf = await leerPdf((await descargar(page, presupuesto.numero, emitida.comprobante)).bytes);

  // AC-09: el receptor es "Consumidor Final", sin DNI (ni el del cliente del presupuesto).
  expect(pdf.texto).toContain('Receptor: Consumidor Final');
  expect(pdf.texto).not.toContain('DNI:');
  expect(pdf.texto).not.toContain('00718294');
  expect(pdf.texto).not.toContain(SIN_IDENTIFICAR.apellido);

  // AC-13: la leyenda del Régimen de Transparencia Fiscal al Consumidor con IVA contenido $315,00.
  expect(pdf.texto).toContain(LEYENDA_LEY_27743);
  expect(pdf.texto).toContain('IVA contenido: $ 315,00');
  expect(pdf.texto).toContain('Total: $ 1.815,00');
  caeYQr(pdf, factura);
  leyendaEnCadaPagina(pdf);
});

test('PDF de una factura de 70 líneas: la leyenda de comprobante simulado en cada página', async ({ page }) => {
  // AC-15 con más de una página: 70 líneas no entran en una (como `Pdf_SimuladoYHomologacion_LeyendaEnCadaPagina`).
  await ingresar(page);
  await configurar(page.request, CONFIGURACION);
  const articulo = await crearArticulo(page.request, 'FP-ESTUCHE', 10);
  const lineas = Array.from({ length: 70 }, () => ({ codigo: articulo, cantidad: 1 }));
  const presupuesto = await grabarFinal(page.request, SIN_IDENTIFICAR, lineas);
  const emitida = await emitir(page.request, presupuesto.numero);
  expect(emitida.total).toBe(700);

  await abrirDetalle(page, presupuesto.numero, emitida.comprobante);
  const pdf = await leerPdf((await descargar(page, presupuesto.numero, emitida.comprobante)).bytes);
  expect(pdf.paginas.length, 'La factura de prueba tenía que ocupar más de una página').toBeGreaterThanOrEqual(2);
  leyendaEnCadaPagina(pdf);
  expect(pdf.texto).toContain('Total: $ 700,00');
});

test('PDF de una Factura C: "Responsable Monotributo" y sin IVA contenido', async ({ page }) => {
  await ingresar(page);
  await configurar(page.request, MONOTRIBUTO);
  const articulo = await crearArticulo(page.request, 'FP-CORDON', 1815);
  const presupuesto = await grabarFinal(page.request, SIN_IDENTIFICAR, [{ codigo: articulo, cantidad: 1 }]);
  const emitida = await emitir(page.request, presupuesto.numero);
  // La primera Factura C del punto de venta 3 de esta base.
  expect(emitida).toMatchObject({ comprobante: 'Factura C 0003-00000001', tipo: 'c', total: 1815 });
  const factura = await detalle(page.request, presupuesto.numero);
  expect(factura.ivaContenido).toBeNull();

  await abrirDetalle(page, presupuesto.numero, emitida.comprobante);
  await expect(dato(page, 'IVA contenido ($)')).toHaveCount(0);
  const pdf = await leerPdf((await descargar(page, presupuesto.numero, emitida.comprobante)).bytes);

  // AC-27: la condición "Responsable Monotributo" con los datos del emisor.
  datosDelEmisor(pdf, 'Responsable Monotributo');
  expect(pdf.texto).not.toContain('Responsable Inscripto');
  expect(pdf.texto).toContain('Factura C');

  // AC-14: ningún importe de IVA contenido, ni la leyenda de la Ley 27.743.
  expect(pdf.texto).not.toMatch(/IVA contenido/i);
  expect(pdf.texto).not.toContain(LEYENDA_LEY_27743);
  expect(pdf.texto).toContain('Total: $ 1.815,00');
  caeYQr(pdf, factura);
  leyendaEnCadaPagina(pdf);
});
