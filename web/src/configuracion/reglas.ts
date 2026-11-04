import { CONDICIONES_FISCALES, type CondicionFiscal, type DatosConfiguracion } from '../api/cliente';
import { leerDecimal } from './numeros';

/**
 * Las reglas de la configuración de negocio (Block 1: `PoliticaConfiguracionNegocio`) con los mensajes de la Api
 * (Block 3: `ValidacionConfiguracion`), para avisar en el navegador antes de enviar. Es una comodidad: la validación
 * que vale es la del servidor (R-03). Por campo gana la primera regla incumplida, en el mismo orden que el dominio.
 */

export const MENSAJE_ALICUOTA_FALTANTE = 'Ingresá la alícuota de IVA.';
export const MENSAJE_ALICUOTA_FUERA_DE_RANGO = 'La alícuota de IVA debe estar entre 0 y 100.';
export const MENSAJE_HASTA_DOS_DECIMALES = 'Ingresá hasta 2 decimales.';
export const MENSAJE_CONDICION_FISCAL = 'Elegí Responsable Inscripto o Monotributo.';
export const MENSAJE_MULTIPLO_FALTANTE = 'Ingresá el múltiplo de redondeo comercial.';
export const MENSAJE_MULTIPLO_MINIMO = 'El valor mínimo es 0,01.';
export const MENSAJE_TOPE_NO_POSITIVO = 'El tope debe ser mayor a 0.';
export const MENSAJE_VALOR_MENOR = 'Ingresá un valor menor.';
/** Propio del navegador: la Api recibe números, nunca texto escrito. */
export const MENSAJE_NO_ES_NUMERO = 'Ingresá un número.';

const ALICUOTA_MINIMA = 0;
const ALICUOTA_MAXIMA = 100;
const MULTIPLO_MINIMO = 0.01;
const DECIMALES_MAXIMOS = 2;

/**
 * El mayor importe que un `number` de JavaScript guarda con los centavos exactos. Es menor que el límite del dominio
 * (`long.MaxValue / 100`): por encima de este valor el número que se enviaría ya no es el que se escribió.
 */
export const IMPORTE_MAXIMO_SEGURO = Number.MAX_SAFE_INTEGER / 100;

/** Lo que hay escrito en el formulario. */
export interface TextosConfiguracion {
  alicuotaIva: string;
  condicionFiscal: string;
  multiploRedondeo: string;
  topeIdentificacion: string;
}

export type ErroresConfiguracion = Partial<Record<keyof TextosConfiguracion, string>>;

export type ValidacionConfiguracion =
  | { tipo: 'ok'; datos: DatosConfiguracion }
  | { tipo: 'errores'; errores: ErroresConfiguracion };

type Campo<T> = { valor: T } | { error: string };

function alicuota(texto: string): Campo<number> {
  const lectura = leerDecimal(texto);
  if (lectura.tipo === 'vacio') {
    return { error: MENSAJE_ALICUOTA_FALTANTE };
  }
  if (lectura.tipo === 'invalido') {
    return { error: MENSAJE_NO_ES_NUMERO };
  }
  if (lectura.valor < ALICUOTA_MINIMA || lectura.valor > ALICUOTA_MAXIMA) {
    return { error: MENSAJE_ALICUOTA_FUERA_DE_RANGO };
  }
  return lectura.decimales > DECIMALES_MAXIMOS ? { error: MENSAJE_HASTA_DOS_DECIMALES } : { valor: lectura.valor };
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

/** Valida los cuatro campos: los datos a enviar, o un mensaje por cada campo inválido. */
export function validarConfiguracion(textos: TextosConfiguracion): ValidacionConfiguracion {
  const campos = {
    alicuotaIva: alicuota(textos.alicuotaIva),
    condicionFiscal: condicion(textos.condicionFiscal),
    multiploRedondeo: multiplo(textos.multiploRedondeo),
    topeIdentificacion: tope(textos.topeIdentificacion),
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
    'valor' in campos.topeIdentificacion
  ) {
    return {
      tipo: 'ok',
      datos: {
        alicuotaIva: campos.alicuotaIva.valor,
        condicionFiscal: campos.condicionFiscal.valor,
        multiploRedondeo: campos.multiploRedondeo.valor,
        topeIdentificacion: campos.topeIdentificacion.valor,
      },
    };
  }
  return { tipo: 'errores', errores };
}
