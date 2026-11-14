import { CONDICIONES_FISCALES, type CondicionFiscal, type DatosConfiguracion } from '../api/cliente';
import { IMPORTE_MAXIMO_SEGURO, leerDecimal, PUNTO_VENTA_MAXIMO, PUNTO_VENTA_MINIMO } from './numeros';

/**
 * Las reglas de la configuración de negocio (Block 1: `PoliticaConfiguracionNegocio`) con los mensajes de la Api
 * (Block 3: `ValidacionConfiguracion`), para avisar en el navegador antes de enviar. Es una comodidad: la validación
 * que vale es la del servidor (R-03). Por campo gana la primera regla incumplida, en el mismo orden que el dominio.
 *
 * Desde FEAT-007 la alícuota se elige de la lista que devuelve la Api (las 6 de ARCA, FR-26): sin opción elegida o con
 * cualquier otro valor, un único mensaje (D-35). El punto de venta es un entero de 1 a 99999 (FR-28); vacío conserva
 * el guardado (D-27).
 */

export const MENSAJE_ALICUOTA_NO_ADMITIDA = 'La alícuota de IVA debe ser 0, 2,5, 5, 10,5, 21 o 27.';
export const MENSAJE_PUNTO_VENTA = 'El punto de venta debe ser un número entero de 1 a 99999.';
export const MENSAJE_HASTA_DOS_DECIMALES = 'Ingresá hasta 2 decimales.';
export const MENSAJE_CONDICION_FISCAL = 'Elegí Responsable Inscripto o Monotributo.';
export const MENSAJE_MULTIPLO_FALTANTE = 'Ingresá el múltiplo de redondeo comercial.';
export const MENSAJE_MULTIPLO_MINIMO = 'El valor mínimo es 0,01.';
export const MENSAJE_TOPE_NO_POSITIVO = 'El tope debe ser mayor a 0.';
export const MENSAJE_VALOR_MENOR = 'Ingresá un valor menor.';
/** Propio del navegador: la Api recibe números, nunca texto escrito. */
export const MENSAJE_NO_ES_NUMERO = 'Ingresá un número.';

/** Solo dígitos: sin signo, sin decimales ni exponente ("2,5", "-1" y "1e3" no son un punto de venta). */
const SOLO_DIGITOS = /^\d+$/;
const MULTIPLO_MINIMO = 0.01;
const DECIMALES_MAXIMOS = 2;

/** Definido en `numeros.ts`, que no importa nada, para que `api/cliente.ts` lo use sin un ciclo de módulos. */
export { IMPORTE_MAXIMO_SEGURO };

/** Lo que hay escrito en el formulario. */
export interface TextosConfiguracion {
  alicuotaIva: string;
  condicionFiscal: string;
  multiploRedondeo: string;
  topeIdentificacion: string;
  /** Vacío: conserva el punto de venta guardado (D-27). */
  puntoVenta: string;
}

export type ErroresConfiguracion = Partial<Record<keyof TextosConfiguracion, string>>;

export type ValidacionConfiguracion =
  | { tipo: 'ok'; datos: DatosConfiguracion }
  | { tipo: 'errores'; errores: ErroresConfiguracion };

type Campo<T> = { valor: T } | { error: string };

/** Una de las alícuotas de la lista (D-35): sin elegir, fuera de la lista o ilegible, el mismo mensaje. */
function alicuota(texto: string, admitidas: readonly number[]): Campo<number> {
  const lectura = leerDecimal(texto);
  return lectura.tipo === 'numero' && admitidas.includes(lectura.valor)
    ? { valor: lectura.valor }
    : { error: MENSAJE_ALICUOTA_NO_ADMITIDA };
}

/** Un entero de 1 a 99999, o `null` si está vacío (conserva el guardado, D-27). */
function puntoVenta(texto: string): Campo<number | null> {
  const limpio = texto.trim();
  if (limpio === '') {
    return { valor: null };
  }
  const valor = Number(limpio);
  return SOLO_DIGITOS.test(limpio) && valor >= PUNTO_VENTA_MINIMO && valor <= PUNTO_VENTA_MAXIMO
    ? { valor }
    : { error: MENSAJE_PUNTO_VENTA };
}

function condicion(texto: string): Campo<CondicionFiscal> {
  const encontrada = CONDICIONES_FISCALES.find((c) => c === texto);
  return encontrada === undefined ? { error: MENSAJE_CONDICION_FISCAL } : { valor: encontrada };
}

function multiplo(texto: string): Campo<number> {
  const lectura = leerDecimal(texto);
  if (lectura.tipo === 'vacio') {
    return { error: MENSAJE_MULTIPLO_FALTANTE };
  }
  if (lectura.tipo === 'invalido') {
    return { error: MENSAJE_NO_ES_NUMERO };
  }
  // D-01: el mínimo antes que la escala, así 0,001 informa el mínimo.
  if (lectura.valor < MULTIPLO_MINIMO) {
    return { error: MENSAJE_MULTIPLO_MINIMO };
  }
  return importe(lectura.valor, lectura.decimales);
}

function tope(texto: string): Campo<number | null> {
  const lectura = leerDecimal(texto);
  // D-02: sin tope es válido; queda el aviso de FR-08.
  if (lectura.tipo === 'vacio') {
    return { valor: null };
  }
  if (lectura.tipo === 'invalido') {
    return { error: MENSAJE_NO_ES_NUMERO };
  }
  if (lectura.valor <= 0) {
    return { error: MENSAJE_TOPE_NO_POSITIVO };
  }
  return importe(lectura.valor, lectura.decimales);
}

/** La escala y el máximo de un importe, en el orden del dominio. */
function importe(valor: number, decimales: number): Campo<number> {
  if (decimales > DECIMALES_MAXIMOS) {
    return { error: MENSAJE_HASTA_DOS_DECIMALES };
  }
  return valor > IMPORTE_MAXIMO_SEGURO ? { error: MENSAJE_VALOR_MENOR } : { valor };
}

/**
 * Valida los cinco campos: los datos a enviar, o un mensaje por cada campo inválido. `alicuotasAdmitidas` es la lista
 * que devolvió la Api (`GET /api/configuracion`).
 */
export function validarConfiguracion(
  textos: TextosConfiguracion,
  alicuotasAdmitidas: readonly number[],
): ValidacionConfiguracion {
  const campos = {
    alicuotaIva: alicuota(textos.alicuotaIva, alicuotasAdmitidas),
    condicionFiscal: condicion(textos.condicionFiscal),
    multiploRedondeo: multiplo(textos.multiploRedondeo),
    topeIdentificacion: tope(textos.topeIdentificacion),
    puntoVenta: puntoVenta(textos.puntoVenta),
  };
  const errores: ErroresConfiguracion = {};
  for (const [nombre, campo] of Object.entries(campos) as [keyof TextosConfiguracion, Campo<unknown>][]) {
    if ('error' in campo) {
      errores[nombre] = campo.error;
    }
  }
  if (
    'valor' in campos.alicuotaIva &&
    'valor' in campos.condicionFiscal &&
    'valor' in campos.multiploRedondeo &&
    'valor' in campos.topeIdentificacion &&
    'valor' in campos.puntoVenta
  ) {
    return {
      tipo: 'ok',
      datos: {
        alicuotaIva: campos.alicuotaIva.valor,
        condicionFiscal: campos.condicionFiscal.valor,
        multiploRedondeo: campos.multiploRedondeo.valor,
        topeIdentificacion: campos.topeIdentificacion.valor,
        puntoVenta: campos.puntoVenta.valor,
      },
    };
  }
  return { tipo: 'errores', errores };
}
