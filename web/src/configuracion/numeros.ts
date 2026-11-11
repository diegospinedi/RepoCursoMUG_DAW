/**
 * Lectura y presentación de los números de la configuración con coma decimal, como los escribe la operadora.
 *
 * Se acepta un número con una coma o un punto decimal ("10,5" o "10.5"), sin separador de miles ni signo más:
 * "1.000,5" y "+3" no son números. Un "-" inicial pegado a los dígitos sí se lee ("-1", "-0,5"): así las reglas del
 * dominio informan el rango del campo en lugar de "no es un número" (AC-03). Los decimales se cuentan en el texto, no en el `number`: así 21,500 tiene 1
 * decimal y 0,001 tiene 3, sin los errores de redondeo del punto flotante.
 */

/**
 * El mayor entero seguro de JavaScript dividido por 100 (`Number.MAX_SAFE_INTEGER / 100`): el límite de los importes
 * en el navegador, menor que el del dominio (`long.MaxValue / 100`). No garantiza centavos exactos en todo el rango:
 * un `number` distingue cada centavo solo hasta 2^46 (70.368.744.177.664); por encima, dos centavos seguidos pueden
 * ser el mismo `number`. Ver D-22 en `docs/ddw/specs/decisions-FEAT-002a.md`.
 */
export const IMPORTE_MAXIMO_SEGURO = Number.MAX_SAFE_INTEGER / 100;

/**
 * {@link IMPORTE_MAXIMO_SEGURO} en centésimos exactos: 9.007.199.254.740.991, es decir 90.071.992.547.409,91, el tope
 * del contrato con la web de D-04 (`ValidacionPresupuesto.ImporteMaximoContrato`). Se compara en BigInt porque como
 * `number` decimal ,91 y ,90 son el mismo valor (D-23).
 */
export const IMPORTE_MAXIMO_SEGURO_CENTESIMOS = BigInt(Number.MAX_SAFE_INTEGER);

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

/**
 * Un importe con 2 decimales, coma decimal y punto de miles ("1.850,00", "-150,00"; D-11 de FEAT-002a), sin `Intl`:
 * así no depende del idioma del navegador ni de sus datos de localización. Un valor que redondea a cero se muestra
 * sin signo.
 */
export function mostrarImporte(valor: number): string {
  const centavos = centavosDe(Math.abs(valor));
  const conMiles = mostrarEntero(centavos / 100n);
  const fraccion = (centavos % 100n).toString().padStart(2, '0');
  return `${valor < 0 && centavos > 0n ? '-' : ''}${conMiles},${fraccion}`;
}

/**
 * Los centavos de un valor no negativo, redondeados a la mitad hacia arriba. Se leen del texto decimal del número
 * (`String`, la representación más corta que vuelve al mismo `number`, la misma que envía `JSON.stringify`), no de
 * `valor * 100`: desde 2^45 esa multiplicación en punto flotante ya cae en el centavo equivocado
 * (38779820885963,52 daba ,53). Exportada para el cálculo de la línea del presupuesto y la suma de importes
 * (ADR-016): este módulo no importa nada, así `presupuestos/reglas.ts` y `api/cliente.ts` la usan sin un ciclo.
 */
export function centavosDe(valor: number): bigint {
  const texto = String(valor);
  if (/e/i.test(texto)) {
    // Notación exponencial: desde 1e21 el número es entero (BigInt lo lee exacto); debajo de 1e-6, redondea a cero.
    return valor >= 1 ? BigInt(valor) * 100n : 0n;
  }
  const [entera = '0', decimales = ''] = texto.split('.');
  const relleno = decimales.padEnd(3, '0');
  const redondeo = Number(relleno[2]) >= 5 ? 1n : 0n;
  return BigInt(entera) * 100n + BigInt(relleno.slice(0, 2)) + redondeo;
}

/**
 * La suma exacta de los importes en centésimos (BigInt), sin los errores de sumar en punto flotante: 0,1 + 0,2 da
 * 30 centésimos, no 0,30000000000000004 (D-08 de FEAT-003b). Cada importe se lleva a centésimos con
 * {@link centavosDe}; uno negativo resta. Recibe importes finitos: `NaN` o `±Infinity` no tienen centésimos y lanzan
 * una excepción, así que quien no los descartó antes (como `calcularTotal`) tiene que hacerlo.
 */
export function sumarCentesimos(importes: readonly number[]): bigint {
  return importes.reduce((suma, importe) => suma + (importe < 0 ? -centavosDe(-importe) : centavosDe(importe)), 0n);
}

/**
 * Un entero con punto de miles ("10.000", "1.234.567"; D-27 de FEAT-002c), sin `Intl`, como la parte entera de
 * {@link mostrarImporte}: no depende del idioma del navegador. Para cantidades, no para identificadores.
 */
export function mostrarEntero(valor: number | bigint): string {
  return valor.toString().replace(/\B(?=(\d{3})+$)/g, '.');
}

/** El número con coma decimal ("0,01", "21"); sin valor, el texto vacío. */
export function mostrarDecimal(valor: number | null): string {
  return valor === null ? '' : String(valor).replace('.', ',');
}
