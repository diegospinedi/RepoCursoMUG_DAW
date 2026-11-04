/**
 * Lectura y presentación de los números de la configuración con coma decimal, como los escribe la operadora.
 *
 * Se acepta un número con una coma o un punto decimal ("10,5" o "10.5"), sin separador de miles ni signo más:
 * "1.000,5" y "+3" no son números. Un "-" inicial pegado a los dígitos sí se lee ("-1", "-0,5"): así las reglas del
 * dominio informan el rango del campo en lugar de "no es un número" (AC-03). Los decimales se cuentan en el texto, no en el `number`: así 21,500 tiene 1
 * decimal y 0,001 tiene 3, sin los errores de redondeo del punto flotante.
 */

/** El resultado de leer un texto: un número con sus decimales significativos, sin valor (vacío) o inválido. */
export type LecturaDecimal = { tipo: 'numero'; valor: number; decimales: number } | { tipo: 'vacio' } | { tipo: 'invalido' };

const FORMATO = /^(-?\d+)(?:[.,](\d+))?$/;

export function leerDecimal(texto: string): LecturaDecimal {
  const limpio = texto.trim();
  if (limpio === '') {
    return { tipo: 'vacio' };
  }
  const partes = FORMATO.exec(limpio);
  if (partes === null) {
    return { tipo: 'invalido' };
  }
  const [, entera, fraccion = ''] = partes;
  // Los ceros a la derecha no son decimales: 0,010 tiene 2.
  const decimales = fraccion.replace(/0+$/, '').length;
  return { tipo: 'numero', valor: Number(fraccion === '' ? entera : `${entera}.${fraccion}`), decimales };
}

/** El número con coma decimal ("0,01", "21"); sin valor, el texto vacío. */
export function mostrarDecimal(valor: number | null): string {
  return valor === null ? '' : String(valor).replace('.', ',');
}
