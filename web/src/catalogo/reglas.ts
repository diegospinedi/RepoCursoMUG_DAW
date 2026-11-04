import type { DatosArticulo } from '../api/cliente';
import { leerDecimal } from '../configuracion/numeros';
import {
  IMPORTE_MAXIMO_SEGURO,
  MENSAJE_HASTA_DOS_DECIMALES,
  MENSAJE_NO_ES_NUMERO,
  MENSAJE_VALOR_MENOR,
} from '../configuracion/reglas';

/**
 * Las reglas del artículo (Block 1: `PoliticaArticulo`) con los mensajes de la Api (Block 3: `ValidacionArticulo` y
 * `AccionesNumericas`), para avisar en el navegador antes de enviar. Es una comodidad: la validación que vale es la
 * del servidor. Por campo gana la primera regla incumplida, en el mismo orden que el dominio.
 */

export const MENSAJE_CODIGO_FALTANTE = 'Completá el código en el proveedor.';
export const MENSAJE_CODIGO_LARGO = 'Usá hasta 50 caracteres.';
export const MENSAJE_DESCRIPCION_FALTANTE = 'Completá la descripción.';
export const MENSAJE_DESCRIPCION_LARGA = 'Usá hasta 200 caracteres.';
export const MENSAJE_CARACTERES_INVALIDOS = 'El texto tiene caracteres no válidos. Escribilo de nuevo.';
export const MENSAJE_COSTO_FALTANTE = 'Ingresá el precio de costo.';
export const MENSAJE_MARGEN_FALTANTE = 'Ingresá el margen de utilidad.';
export const MENSAJE_MARGEN_FUERA_DE_RANGO = 'El margen debe estar entre 0 y 1.000.';

export const LARGO_MAXIMO_CODIGO = 50;
export const LARGO_MAXIMO_DESCRIPCION = 200;
const MARGEN_MINIMO = 0;
const MARGEN_MAXIMO = 1000;
const DECIMALES_MAXIMOS = 2;

/**
 * Los códigos UTF-16 que se recortan (D-13, D-17): todos aquellos para los que `char.IsWhiteSpace` de .NET es
 * verdadero, más U+FEFF. Es un conjunto explícito, no `String.prototype.trim()`: `trim()` no quita U+0085 (NEL) y
 * .NET sí, y la clave del código tiene que salir igual en los dos lados. Se escriben por su número para que el
 * archivo no tenga caracteres invisibles.
 */
const RECORTABLES: ReadonlySet<number> = new Set([
  0x0009, 0x000a, 0x000b, 0x000c, 0x000d, 0x0020, 0x0085, 0x00a0, 0x1680, 0x2000, 0x2001, 0x2002, 0x2003, 0x2004,
  0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200a, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000, 0xfeff,
]);

const esRecortable = (texto: string, indice: number) => RECORTABLES.has(texto.charCodeAt(indice));

/** Quita del principio y del final los mismos caracteres que `NormalizacionCodigo.Recortar` del servidor. */
export function recortar(texto: string): string {
  let inicio = 0;
  let fin = texto.length;
  while (inicio < fin && esRecortable(texto, inicio)) {
    inicio++;
  }
  while (fin > inicio && esRecortable(texto, fin - 1)) {
    fin--;
  }
  return texto.slice(inicio, fin);
}

const esSustitutoAlto = (codigo: number) => codigo >= 0xd800 && codigo <= 0xdbff;
const esSustitutoBajo = (codigo: number) => codigo >= 0xdc00 && codigo <= 0xdfff;

/** Verdadero si el texto no tiene sustitutos sueltos (D-16): cada alto seguido de un bajo, ningún bajo solo. */
export function esUtf16Valido(texto: string): boolean {
  for (let i = 0; i < texto.length; i++) {
    const codigo = texto.charCodeAt(i);
    if (esSustitutoAlto(codigo)) {
      if (!esSustitutoBajo(texto.charCodeAt(i + 1))) {
        return false;
      }
      i++;
    } else if (esSustitutoBajo(codigo)) {
      return false;
    }
  }
  return true;
}

/** Lo que hay escrito en el formulario. */
export interface TextosArticulo {
  codigoProveedor: string;
  descripcion: string;
  precioCosto: string;
  margenUtilidad: string;
}

export type ErroresArticulo = Partial<Record<keyof TextosArticulo, string>>;

export type ValidacionArticulo = { tipo: 'ok'; datos: DatosArticulo } | { tipo: 'errores'; errores: ErroresArticulo };

type Campo<T> = { valor: T } | { error: string };

/**
 * Faltante, después UTF-16 inválido y por último el largo, medido en unidades UTF-16 (`length`, como `string.Length`
 * de .NET) sobre el texto recortado (D-02).
 */
function texto(escrito: string, largoMaximo: number, faltante: string, demasiadoLargo: string): Campo<string> {
  const recortado = recortar(escrito);
  if (recortado === '') {
    return { error: faltante };
  }
  if (!esUtf16Valido(recortado)) {
    return { error: MENSAJE_CARACTERES_INVALIDOS };
  }
  return recortado.length > largoMaximo ? { error: demasiadoLargo } : { valor: recortado };
}

function costo(escrito: string): Campo<number> {
  const lectura = leerDecimal(escrito);
  if (lectura.tipo === 'vacio') {
    return { error: MENSAJE_COSTO_FALTANTE };
  }
  if (lectura.tipo === 'invalido') {
    return { error: MENSAJE_NO_ES_NUMERO };
  }
  if (lectura.decimales > DECIMALES_MAXIMOS) {
    return { error: MENSAJE_HASTA_DOS_DECIMALES };
  }
  // FR-05: cero y negativos son válidos; el rango del tipo se controla en los dos sentidos.
  return Math.abs(lectura.valor) > IMPORTE_MAXIMO_SEGURO ? { error: MENSAJE_VALOR_MENOR } : { valor: lectura.valor };
}

function margen(escrito: string): Campo<number> {
  const lectura = leerDecimal(escrito);
  if (lectura.tipo === 'vacio') {
    return { error: MENSAJE_MARGEN_FALTANTE };
  }
  if (lectura.tipo === 'invalido') {
    return { error: MENSAJE_NO_ES_NUMERO };
  }
  // AC-05: el rango antes que la escala, así 1.000,01 informa el rango.
  if (lectura.valor < MARGEN_MINIMO || lectura.valor > MARGEN_MAXIMO) {
    return { error: MENSAJE_MARGEN_FUERA_DE_RANGO };
  }
  return lectura.decimales > DECIMALES_MAXIMOS ? { error: MENSAJE_HASTA_DOS_DECIMALES } : { valor: lectura.valor };
}

/** Valida los cuatro campos: los datos a enviar (con los textos recortados), o un mensaje por cada campo inválido. */
export function validarArticulo(textos: TextosArticulo): ValidacionArticulo {
  const campos = {
    codigoProveedor: texto(textos.codigoProveedor, LARGO_MAXIMO_CODIGO, MENSAJE_CODIGO_FALTANTE, MENSAJE_CODIGO_LARGO),
    descripcion: texto(
      textos.descripcion,
      LARGO_MAXIMO_DESCRIPCION,
      MENSAJE_DESCRIPCION_FALTANTE,
      MENSAJE_DESCRIPCION_LARGA,
    ),
    precioCosto: costo(textos.precioCosto),
    margenUtilidad: margen(textos.margenUtilidad),
  };
  if (
    'valor' in campos.codigoProveedor &&
    'valor' in campos.descripcion &&
    'valor' in campos.precioCosto &&
    'valor' in campos.margenUtilidad
  ) {
    return {
      tipo: 'ok',
      datos: {
        codigoProveedor: campos.codigoProveedor.valor,
        descripcion: campos.descripcion.valor,
        precioCosto: campos.precioCosto.valor,
        margenUtilidad: campos.margenUtilidad.valor,
      },
    };
  }
  const errores: ErroresArticulo = {};
  for (const [nombre, campo] of Object.entries(campos) as [keyof TextosArticulo, Campo<unknown>][]) {
    if ('error' in campo) {
      errores[nombre] = campo.error;
    }
  }
  return { tipo: 'errores', errores };
}
