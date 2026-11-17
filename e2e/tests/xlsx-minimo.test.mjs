// Prueba de unidad del generador de planillas .xlsx de la suite (FEAT-011, Block 6, D-18): `npm run test:unidad` en
// e2e/. Lee el ZIP que arma `xlsx-minimo.ts` con un lector propio, independiente del generador, y exige firmas, CRC y
// tamaños correctos en las cabeceras locales, el directorio central y el registro final (EOCD).
import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { crc32, inflateRawSync } from 'node:zlib';

import { xlsxMinimo, zipMinimo } from './xlsx-minimo.ts';

const FIRMA_LOCAL = 0x04034b50;
const FIRMA_CENTRAL = 0x02014b50;
const FIRMA_EOCD = 0x06054b50;
const LARGO_EOCD = 22;
const DEFLATE = 8;

/**
 * Recorre el ZIP como lo haría un lector estricto: el EOCD al final y sin comentario, cada entrada del directorio
 * central con su cabecera local, los datos descomprimidos con `inflateRawSync` y su CRC y tamaños comparados con lo
 * declarado en los dos lugares. Lanza con un mensaje que nombra la entrada y el campo si algo no coincide.
 * @param {Buffer} zip
 * @returns {Map<string, Buffer>} el contenido descomprimido de cada entrada, por nombre.
 */
function leerZipEstricto(zip) {
  assert.ok(zip.length >= LARGO_EOCD, 'El ZIP es más corto que el registro final.');
  const eocd = zip.length - LARGO_EOCD;
  assert.equal(zip.readUInt32LE(eocd), FIRMA_EOCD, 'Falta la firma del registro final (EOCD) al final del archivo.');
  const enEsteDisco = zip.readUInt16LE(eocd + 8);
  const total = zip.readUInt16LE(eocd + 10);
  const largoCentral = zip.readUInt32LE(eocd + 12);
  const inicioCentral = zip.readUInt32LE(eocd + 16);
  assert.equal(zip.readUInt16LE(eocd + 20), 0, 'El EOCD declara un comentario.');
  assert.equal(enEsteDisco, total, 'Las entradas del disco y las totales no coinciden.');
  assert.equal(inicioCentral + largoCentral, eocd, 'El directorio central no termina donde empieza el EOCD.');

  const entradas = new Map();
  let posicion = inicioCentral;
  for (let i = 0; i < total; i++) {
    assert.equal(zip.readUInt32LE(posicion), FIRMA_CENTRAL, `Entrada ${i}: falta la firma del directorio central.`);
    const metodo = zip.readUInt16LE(posicion + 10);
    const crc = zip.readUInt32LE(posicion + 16);
    const comprimido = zip.readUInt32LE(posicion + 20);
    const tamano = zip.readUInt32LE(posicion + 24);
    const largoNombre = zip.readUInt16LE(posicion + 28);
    const largoExtra = zip.readUInt16LE(posicion + 30);
    const largoComentario = zip.readUInt16LE(posicion + 32);
    const local = zip.readUInt32LE(posicion + 42);
    const nombre = zip.subarray(posicion + 46, posicion + 46 + largoNombre).toString('utf8');
    posicion += 46 + largoNombre + largoExtra + largoComentario;

    assert.equal(zip.readUInt32LE(local), FIRMA_LOCAL, `${nombre}: falta la firma de la cabecera local.`);
    assert.equal(zip.readUInt16LE(local + 8), metodo, `${nombre}: el método local no coincide con el central.`);
    assert.equal(zip.readUInt32LE(local + 14), crc, `${nombre}: el CRC local no coincide con el central.`);
    assert.equal(zip.readUInt32LE(local + 18), comprimido, `${nombre}: el tamaño comprimido local no coincide.`);
    assert.equal(zip.readUInt32LE(local + 22), tamano, `${nombre}: el tamaño local no coincide.`);
    const largoNombreLocal = zip.readUInt16LE(local + 26);
    const largoExtraLocal = zip.readUInt16LE(local + 28);
    assert.equal(
      zip.subarray(local + 30, local + 30 + largoNombreLocal).toString('utf8'),
      nombre,
      `${nombre}: el nombre local no coincide con el central.`,
    );
    const inicioDatos = local + 30 + largoNombreLocal + largoExtraLocal;
    assert.equal(metodo, DEFLATE, `${nombre}: no está comprimida con deflate.`);
    const datos = inflateRawSync(zip.subarray(inicioDatos, inicioDatos + comprimido));
    assert.equal(datos.length, tamano, `${nombre}: el tamaño descomprimido no es el declarado.`);
    assert.equal(crc32(datos), crc, `${nombre}: el CRC no corresponde a los datos.`);
    entradas.set(nombre, datos);
  }
  assert.equal(posicion, eocd, 'Sobran bytes entre el directorio central y el EOCD.');
  return entradas;
}

/** El offset del registro de la entrada `nombre` en el directorio central. */
function offsetCentral(zip, nombre) {
  const eocd = zip.length - LARGO_EOCD;
  const total = zip.readUInt16LE(eocd + 10);
  let posicion = zip.readUInt32LE(eocd + 16);
  for (let i = 0; i < total; i++) {
    const largoNombre = zip.readUInt16LE(posicion + 28);
    const actual = zip.subarray(posicion + 46, posicion + 46 + largoNombre).toString('utf8');
    if (actual === nombre) return posicion;
    posicion += 46 + largoNombre + zip.readUInt16LE(posicion + 30) + zip.readUInt16LE(posicion + 32);
  }
  assert.fail(`No está la entrada ${nombre} en el directorio central.`);
}

/** El offset del CRC de la entrada `nombre` en el directorio central. */
const offsetCrcCentral = (zip, nombre) => offsetCentral(zip, nombre) + 16;

const PARTES = [
  '[Content_Types].xml',
  '_rels/.rels',
  'xl/workbook.xml',
  'xl/_rels/workbook.xml.rels',
  'xl/worksheets/sheet1.xml',
];

describe('xlsx-minimo', () => {
  test('xlsx-minimo arma un zip válido', () => {
    const planilla = xlsxMinimo([
      ['Codigo en el proveedor', 'Precio de Costo Nuevo'],
      ['ABC-1 & <x>', 2420],
      ['SIN-PRECIO', null],
      ['NEG', -100.5],
    ]);

    assert.ok(Buffer.isBuffer(planilla), 'El generador tiene que devolver un Buffer.');
    assert.deepEqual([...planilla.subarray(0, 4)], [0x50, 0x4b, 0x03, 0x04], 'Un .xlsx empieza con la firma de ZIP.');
    const entradas = leerZipEstricto(planilla);
    assert.deepEqual([...entradas.keys()], PARTES, 'Las partes del paquete no son las esperadas.');

    const hoja = entradas.get('xl/worksheets/sheet1.xml').toString('utf8');
    assert.match(hoja, /<c r="A1" t="inlineStr"><is><t>Codigo en el proveedor<\/t><\/is><\/c>/);
    assert.match(
      hoja,
      /<c r="A2" t="inlineStr"><is><t>ABC-1 &amp; &lt;x&gt;<\/t><\/is><\/c><c r="B2"><v>2420<\/v><\/c>/,
    );
    assert.match(hoja, /<row r="3"><c r="A3" t="inlineStr"><is><t>SIN-PRECIO<\/t><\/is><\/c><\/row>/);
    assert.match(hoja, /<c r="B4"><v>-100.5<\/v><\/c>/);
    const tipos = entradas.get('[Content_Types].xml').toString('utf8');
    assert.match(tipos, /PartName="\/xl\/workbook\.xml" ContentType="[^"]+spreadsheetml\.sheet\.main\+xml"/);
    assert.match(tipos, /PartName="\/xl\/worksheets\/sheet1\.xml" ContentType="[^"]+spreadsheetml\.worksheet\+xml"/);
  });

  test('xlsx-minimo: un CRC alterado se detecta', () => {
    const planilla = xlsxMinimo([['Codigo en el proveedor', 'Precio de Costo Nuevo']]);
    const invertir = (zip, offset) => zip.writeUInt32LE((zip.readUInt32LE(offset) ^ 0xffffffff) >>> 0, offset);
    const central = offsetCrcCentral(planilla, 'xl/worksheets/sheet1.xml');
    // La cabecera local de la entrada: su offset está 26 bytes después del CRC central; el CRC local, 14 después.
    const local = planilla.readUInt32LE(central + 26) + 14;

    // Solo el directorio central: no coincide con la cabecera local.
    const soloCentral = Buffer.from(planilla);
    invertir(soloCentral, central);
    // Los dos, coherentes entre sí: no corresponde a los datos.
    const ambos = Buffer.from(planilla);
    invertir(ambos, central);
    invertir(ambos, local);

    assert.doesNotThrow(() => leerZipEstricto(planilla), 'La planilla sin alterar tiene que leerse.');
    assert.throws(() => leerZipEstricto(soloCentral), /CRC local no coincide/);
    assert.throws(() => leerZipEstricto(ambos), /CRC no corresponde a los datos/);
  });

  test('xlsx-minimo: las mismas filas dan los mismos bytes, con fecha y hora DOS fijas', () => {
    const filas = [
      ['Codigo en el proveedor', 'Precio de Costo Nuevo'],
      ['ABC-1', 2420],
    ];
    const primera = xlsxMinimo(filas);
    const segunda = xlsxMinimo(filas);
    assert.ok(primera.equals(segunda), 'Dos llamadas con las mismas filas tienen que dar el mismo buffer.');

    for (const nombre of PARTES) {
      const central = offsetCentral(primera, nombre);
      const local = primera.readUInt32LE(central + 42);
      assert.equal(primera.readUInt16LE(local + 10), 0x0000, `${nombre}: la hora local no es 00:00.`);
      assert.equal(primera.readUInt16LE(local + 12), 0x0021, `${nombre}: la fecha local no es 1980-01-01.`);
      assert.equal(primera.readUInt16LE(central + 12), 0x0000, `${nombre}: la hora central no es 00:00.`);
      assert.equal(primera.readUInt16LE(central + 14), 0x0021, `${nombre}: la fecha central no es 1980-01-01.`);
    }
  });

  test('xlsx-minimo: una entrada que no está se informa sin leer fuera del directorio', () => {
    const planilla = xlsxMinimo([['Codigo en el proveedor', 'Precio de Costo Nuevo']]);
    assert.throws(
      () => offsetCrcCentral(planilla, 'xl/sharedStrings.xml'),
      /No está la entrada xl\/sharedStrings\.xml/,
    );
  });

  test('zipMinimo: el CRC de cada entrada es el de sus datos', () => {
    const datos = Buffer.from('ñandú '.repeat(1000), 'utf8');
    const zip = zipMinimo([{ nombre: 'a.txt', datos }]);
    const entradas = leerZipEstricto(zip);
    assert.deepEqual(entradas.get('a.txt'), datos);
    assert.ok(zip.length < datos.length, 'Los datos repetidos tienen que quedar comprimidos.');
  });
});
