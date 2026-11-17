import { crc32, deflateRawSync } from 'node:zlib';

/**
 * Planillas .xlsx para la suite de punta a punta (FEAT-011, D-18): un libro con una sola hoja, armado con `node:zlib`
 * (`deflateRawSync` y `crc32`) y sin paquetes npm. Es el mínimo que abre el lector de la Api
 * (`LectorPlanillaCostosXlsx`, con `DocumentFormat.OpenXml`): `[Content_Types].xml`, las relaciones del paquete y del
 * libro, `xl/workbook.xml` y `xl/worksheets/sheet1.xml`. Los textos van como cadenas en línea (`t="inlineStr"`), así
 * no hace falta `sharedStrings.xml`, y los números como `<v>` sin tipo.
 *
 * No es un archivo de prueba de Playwright (no termina en `.spec.ts`), así que no tiene proyecto propio; lo prueba
 * `xlsx-minimo.test.mjs` dentro de `npm run test:unidad`. Usa solo sintaxis de TypeScript que Node puede quitar sin
 * compilar, para que esa prueba lo importe tal cual.
 */

/** Una celda de la planilla: texto, número o vacía (se omite). */
export type Celda = string | number | null;

/** Una entrada del ZIP: su nombre (ASCII) y su contenido sin comprimir. */
export interface EntradaZip {
  nombre: string;
  datos: Buffer;
}

const FIRMA_LOCAL = 0x04034b50;
const FIRMA_CENTRAL = 0x02014b50;
const FIRMA_EOCD = 0x06054b50;
/** Versión 2.0: la que alcanza para deflate. */
const VERSION = 20;
const DEFLATE = 8;
/** 1980-01-01 00:00 en formato MS-DOS: la fecha fija hace que el mismo contenido dé siempre los mismos bytes. */
const FECHA_DOS = (0 << 9) | (1 << 5) | 1;
const HORA_DOS = 0;

/**
 * Los espacios de nombres y tipos de relación de OOXML. Son nombres dentro del XML, nunca se piden; la revisión de
 * hosts externos de `ci.mjs` (FR-10 de FEAT-005) admite exactamente estas rutas en `RUTAS_OOXML` (D-27 de FEAT-011).
 */
const NS_TIPOS = 'http://schemas.openxmlformats.org/package/2006/content-types';
const NS_RELACIONES_PAQUETE = 'http://schemas.openxmlformats.org/package/2006/relationships';
const NS_PRINCIPAL = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';
const NS_RELACIONES = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships';
const TIPO_DOCUMENTO = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument';
const TIPO_HOJA = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet';

const PROLOGO = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n';

/**
 * Un ZIP con las entradas en ese orden, cada una comprimida con deflate, con su cabecera local, su registro en el
 * directorio central y el registro final (EOCD) sin comentario. Sin ZIP64: alcanza para las planillas de la suite.
 */
export function zipMinimo(entradas: EntradaZip[]): Buffer {
  const locales: Buffer[] = [];
  const centrales: Buffer[] = [];
  let desplazamiento = 0;
  for (const { nombre, datos } of entradas) {
    const nombreBytes = Buffer.from(nombre, 'utf8');
    const comprimidos = deflateRawSync(datos);
    const crc = crc32(datos);

    const local = Buffer.alloc(30);
    local.writeUInt32LE(FIRMA_LOCAL, 0);
    local.writeUInt16LE(VERSION, 4);
    local.writeUInt16LE(0, 6); // sin banderas: sin descriptor de datos, tamaños en la cabecera
    local.writeUInt16LE(DEFLATE, 8);
    local.writeUInt16LE(HORA_DOS, 10);
    local.writeUInt16LE(FECHA_DOS, 12);
    local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(comprimidos.length, 18);
    local.writeUInt32LE(datos.length, 22);
    local.writeUInt16LE(nombreBytes.length, 26);
    local.writeUInt16LE(0, 28);

    const central = Buffer.alloc(46);
    central.writeUInt32LE(FIRMA_CENTRAL, 0);
    central.writeUInt16LE(VERSION, 4);
    central.writeUInt16LE(VERSION, 6);
    central.writeUInt16LE(0, 8);
    central.writeUInt16LE(DEFLATE, 10);
    central.writeUInt16LE(HORA_DOS, 12);
    central.writeUInt16LE(FECHA_DOS, 14);
    central.writeUInt32LE(crc, 16);
    central.writeUInt32LE(comprimidos.length, 20);
    central.writeUInt32LE(datos.length, 24);
    central.writeUInt16LE(nombreBytes.length, 28);
    // extra, comentario, disco, atributos internos y externos en cero
    central.writeUInt32LE(desplazamiento, 42);

    locales.push(local, nombreBytes, comprimidos);
    centrales.push(central, nombreBytes);
    desplazamiento += local.length + nombreBytes.length + comprimidos.length;
  }

  const directorio = Buffer.concat(centrales);
  const eocd = Buffer.alloc(22);
  eocd.writeUInt32LE(FIRMA_EOCD, 0);
  eocd.writeUInt16LE(0, 4);
  eocd.writeUInt16LE(0, 6);
  eocd.writeUInt16LE(entradas.length, 8);
  eocd.writeUInt16LE(entradas.length, 10);
  eocd.writeUInt32LE(directorio.length, 12);
  eocd.writeUInt32LE(desplazamiento, 16);
  eocd.writeUInt16LE(0, 20);
  return Buffer.concat([...locales, directorio, eocd]);
}

/** El texto con `&`, `<`, `>` y `"` escapados para el XML. */
function escapar(texto: string): string {
  return texto.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

/** La letra de la columna (1 → A, 2 → B, 27 → AA). */
function columna(numero: number): string {
  let letras = '';
  for (let n = numero; n > 0; n = Math.floor((n - 1) / 26)) {
    letras = String.fromCharCode(65 + ((n - 1) % 26)) + letras;
  }
  return letras;
}

/** Una celda con su referencia; vacía si es null. */
function celdaXml(valor: Celda, referencia: string): string {
  if (valor === null) {
    return '';
  }
  if (typeof valor === 'number') {
    if (!Number.isFinite(valor)) {
      throw new Error(`La celda ${referencia} no es un número finito.`);
    }
    return `<c r="${referencia}"><v>${String(valor)}</v></c>`;
  }
  return `<c r="${referencia}" t="inlineStr"><is><t>${escapar(valor)}</t></is></c>`;
}

/** La hoja con las filas desde la 1, cada celda con su referencia (A1, B2…). */
function hojaXml(filas: Celda[][]): string {
  const filasXml = filas
    .map((celdas, i) => {
      const fila = i + 1;
      const contenido = celdas.map((valor, j) => celdaXml(valor, `${columna(j + 1)}${fila}`)).join('');
      return `<row r="${fila}">${contenido}</row>`;
    })
    .join('');
  return `${PROLOGO}<worksheet xmlns="${NS_PRINCIPAL}"><sheetData>${filasXml}</sheetData></worksheet>`;
}

/**
 * Un .xlsx con una hoja ("Hoja1") con `filas`: la primera es la fila 1 de la hoja. Un texto es una cadena en línea, un
 * número un valor numérico y `null` una celda que no se escribe.
 */
export function xlsxMinimo(filas: Celda[][]): Buffer {
  const tipos =
    `${PROLOGO}<Types xmlns="${NS_TIPOS}">` +
    '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>' +
    '<Default Extension="xml" ContentType="application/xml"/>' +
    '<Override PartName="/xl/workbook.xml" ' +
    'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>' +
    '<Override PartName="/xl/worksheets/sheet1.xml" ' +
    'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>' +
    '</Types>';
  const relacionesPaquete =
    `${PROLOGO}<Relationships xmlns="${NS_RELACIONES_PAQUETE}">` +
    `<Relationship Id="rId1" Type="${TIPO_DOCUMENTO}" Target="xl/workbook.xml"/>` +
    '</Relationships>';
  const libro =
    `${PROLOGO}<workbook xmlns="${NS_PRINCIPAL}" xmlns:r="${NS_RELACIONES}">` +
    '<sheets><sheet name="Hoja1" sheetId="1" r:id="rId1"/></sheets></workbook>';
  const relacionesLibro =
    `${PROLOGO}<Relationships xmlns="${NS_RELACIONES_PAQUETE}">` +
    `<Relationship Id="rId1" Type="${TIPO_HOJA}" Target="worksheets/sheet1.xml"/>` +
    '</Relationships>';

  return zipMinimo([
    { nombre: '[Content_Types].xml', datos: Buffer.from(tipos, 'utf8') },
    { nombre: '_rels/.rels', datos: Buffer.from(relacionesPaquete, 'utf8') },
    { nombre: 'xl/workbook.xml', datos: Buffer.from(libro, 'utf8') },
    { nombre: 'xl/_rels/workbook.xml.rels', datos: Buffer.from(relacionesLibro, 'utf8') },
    { nombre: 'xl/worksheets/sheet1.xml', datos: Buffer.from(hojaXml(filas), 'utf8') },
  ]);
}
