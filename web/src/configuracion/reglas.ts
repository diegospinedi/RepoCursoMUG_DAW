import {
  CONDICIONES_FISCALES,
  CUIT_CANONICO,
  type CondicionFiscal,
  type DatosConfiguracion,
  type DatosEmisor,
} from '../api/cliente';
import { esFecha } from '../api/fechas';
import {
  IMPORTE_MAXIMO_SEGURO,
  LARGO_MAXIMO_RUTA_BACKUP,
  leerDecimal,
  PUNTO_VENTA_MAXIMO,
  PUNTO_VENTA_MINIMO,
} from './numeros';

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

// ---- Datos del emisor (FEAT-009): espejo de DatosEmisor, Cuit y ValidacionConfiguracion del servidor ----

export const MENSAJE_RAZON_SOCIAL_FALTANTE = 'Ingresá la razón social.';
export const MENSAJE_RAZON_SOCIAL_CARACTERES = 'Escribí la razón social en una sola línea, sin caracteres especiales.';
export const MENSAJE_RAZON_SOCIAL_LARGA = 'La razón social puede tener hasta 120 caracteres.';
export const MENSAJE_DOMICILIO_COMERCIAL_FALTANTE = 'Ingresá el domicilio comercial.';
export const MENSAJE_DOMICILIO_COMERCIAL_CARACTERES =
  'Escribí el domicilio comercial en una sola línea, sin caracteres especiales.';
export const MENSAJE_DOMICILIO_COMERCIAL_LARGO = 'El domicilio comercial puede tener hasta 200 caracteres.';
export const MENSAJE_CUIT_FALTANTE = 'Ingresá el CUIT.';
export const MENSAJE_CUIT_INVALIDO = 'El CUIT no es válido: tienen que ser 11 dígitos, con o sin guiones.';
export const MENSAJE_INGRESOS_BRUTOS_FALTANTE = 'Ingresá el número de Ingresos Brutos.';
export const MENSAJE_INGRESOS_BRUTOS_CARACTERES =
  'Escribí el número de Ingresos Brutos en una sola línea, sin caracteres especiales.';
export const MENSAJE_INGRESOS_BRUTOS_LARGO = 'El número de Ingresos Brutos puede tener hasta 30 caracteres.';
export const MENSAJE_INICIO_ACTIVIDADES_FALTANTE = 'Ingresá la fecha de inicio de actividades.';
export const MENSAJE_INICIO_ACTIVIDADES_INVALIDA = 'La fecha de inicio de actividades no es válida.';
export const MENSAJE_INICIO_ACTIVIDADES_POSTERIOR = 'La fecha de inicio de actividades no puede ser posterior a hoy.';

/** Lo que hay escrito en la sección de los datos del emisor. */
export type TextosDatosEmisor = DatosEmisor;

export type ErroresDatosEmisor = Partial<Record<keyof TextosDatosEmisor, string>>;

export type ValidacionDatosEmisor =
  | { tipo: 'ok'; datos: DatosEmisor }
  | { tipo: 'errores'; errores: ErroresDatosEmisor };

/** Pesos del dígito verificador (módulo 11), de izquierda a derecha sobre los 10 primeros dígitos, como `Cuit.cs`. */
const PESOS_CUIT = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

/**
 * El recorte del servidor (`RecorteTexto`): quita del principio y del final los espacios de `char.IsWhiteSpace` y la
 * marca de orden de bytes. `\s` de JavaScript ya incluye esa marca; U+0085 es espacio en .NET y no en `\s`.
 */
const recortar = (texto: string) => texto.replace(/^[\s\u0085]+|[\s\u0085]+$/g, '');

/**
 * El CUIT en forma canónica (11 dígitos, sin guiones), o `null` si no es válido: como `Cuit.Interpretar`, recorta, admite
 * guiones en cualquier posición y rechaza cualquier otro carácter, un largo distinto de 11 y un dígito verificador que no
 * corresponde, incluido el 10, que ningún dígito representa (AC-03, AC-04).
 */
export function cuitCanonico(texto: string): string | null {
  const sinGuiones = recortar(texto).replaceAll('-', '');
  if (!CUIT_CANONICO.test(sinGuiones)) {
    return null;
  }
  const suma = PESOS_CUIT.reduce((acumulado, peso, i) => acumulado + Number(sinGuiones[i]) * peso, 0);
  const verificador = (11 - (suma % 11)) % 11;
  return verificador !== 10 && verificador === Number(sinGuiones[10]) ? sinGuiones : null;
}

/**
 * Sin caracteres de control (`char.IsControl`: U+0000 a U+001F y U+007F a U+009F, saltos de línea incluidos) y UTF-16
 * válido: cada sustituto alto seguido de uno bajo, ningún sustituto bajo suelto.
 */
function tieneCaracteresValidos(texto: string): boolean {
  for (let i = 0; i < texto.length; i++) {
    const codigo = texto.charCodeAt(i);
    const esControl = codigo <= 0x1f || (codigo >= 0x7f && codigo <= 0x9f);
    const esBajo = codigo >= 0xdc00 && codigo <= 0xdfff;
    if (esControl || esBajo) {
      return false;
    }
    if (codigo >= 0xd800 && codigo <= 0xdbff) {
      const siguiente = texto.charCodeAt(i + 1);
      if (!(siguiente >= 0xdc00 && siguiente <= 0xdfff)) {
        return false;
      }
      i++;
    }
  }
  return true;
}

interface MensajesTexto {
  faltante: string;
  caracteres: string;
  largo: string;
}

/** Un texto obligatorio: faltante, después caracteres inválidos y por último el largo, medido sobre el recortado. */
function textoObligatorio(texto: string, largoMaximo: number, mensajes: MensajesTexto): Campo<string> {
  const recortado = recortar(texto);
  if (recortado === '') {
    return { error: mensajes.faltante };
  }
  if (!tieneCaracteresValidos(recortado)) {
    return { error: mensajes.caracteres };
  }
  return recortado.length > largoMaximo ? { error: mensajes.largo } : { valor: recortado };
}

function cuit(texto: string): Campo<string> {
  if (recortar(texto) === '') {
    return { error: MENSAJE_CUIT_FALTANTE };
  }
  const canonico = cuitCanonico(texto);
  return canonico === null ? { error: MENSAJE_CUIT_INVALIDO } : { valor: canonico };
}

/** La fecha de inicio: obligatoria, existente y no posterior a `hoy` (las dos `yyyy-MM-dd`, se comparan como texto). */
function inicioActividades(texto: string, hoy: string): Campo<string> {
  const recortado = recortar(texto);
  if (recortado === '') {
    return { error: MENSAJE_INICIO_ACTIVIDADES_FALTANTE };
  }
  if (!esFecha(recortado)) {
    return { error: MENSAJE_INICIO_ACTIVIDADES_INVALIDA };
  }
  return recortado > hoy ? { error: MENSAJE_INICIO_ACTIVIDADES_POSTERIOR } : { valor: recortado };
}

/** La fecha local de hoy como `yyyy-MM-dd`, como la toma el servidor (`GetLocalNow`). */
export function hoyLocal(ahora: Date = new Date()): string {
  const dos = (numero: number) => String(numero).padStart(2, '0');
  return `${ahora.getFullYear()}-${dos(ahora.getMonth() + 1)}-${dos(ahora.getDate())}`;
}

/**
 * Valida los cinco datos del emisor (FR-02, FR-03) con las reglas del servidor: los datos a enviar (recortados y el CUIT
 * sin guiones, así el cuerpo nunca supera el límite de la Api), o un mensaje por cada campo inválido. `hoy` es la fecha
 * local como `yyyy-MM-dd` ({@link hoyLocal}). Es una comodidad: la validación que vale es la del servidor.
 */
export function validarDatosEmisor(textos: TextosDatosEmisor, hoy: string): ValidacionDatosEmisor {
  const campos = {
    razonSocial: textoObligatorio(textos.razonSocial, 120, {
      faltante: MENSAJE_RAZON_SOCIAL_FALTANTE,
      caracteres: MENSAJE_RAZON_SOCIAL_CARACTERES,
      largo: MENSAJE_RAZON_SOCIAL_LARGA,
    }),
    domicilioComercial: textoObligatorio(textos.domicilioComercial, 200, {
      faltante: MENSAJE_DOMICILIO_COMERCIAL_FALTANTE,
      caracteres: MENSAJE_DOMICILIO_COMERCIAL_CARACTERES,
      largo: MENSAJE_DOMICILIO_COMERCIAL_LARGO,
    }),
    cuit: cuit(textos.cuit),
    ingresosBrutos: textoObligatorio(textos.ingresosBrutos, 30, {
      faltante: MENSAJE_INGRESOS_BRUTOS_FALTANTE,
      caracteres: MENSAJE_INGRESOS_BRUTOS_CARACTERES,
      largo: MENSAJE_INGRESOS_BRUTOS_LARGO,
    }),
    inicioActividades: inicioActividades(textos.inicioActividades, hoy),
  };
  const errores: ErroresDatosEmisor = {};
  for (const [nombre, campo] of Object.entries(campos) as [keyof TextosDatosEmisor, Campo<string>][]) {
    if ('error' in campo) {
      errores[nombre] = campo.error;
    }
  }
  if (
    'valor' in campos.razonSocial &&
    'valor' in campos.domicilioComercial &&
    'valor' in campos.cuit &&
    'valor' in campos.ingresosBrutos &&
    'valor' in campos.inicioActividades
  ) {
    return {
      tipo: 'ok',
      datos: {
        razonSocial: campos.razonSocial.valor,
        domicilioComercial: campos.domicilioComercial.valor,
        cuit: campos.cuit.valor,
        ingresosBrutos: campos.ingresosBrutos.valor,
        inicioActividades: campos.inicioActividades.valor,
      },
    };
  }
  return { tipo: 'errores', errores };
}

// ---- Ubicación del backup (FEAT-010) ----

/** Definido en `numeros.ts`, que no importa nada, para que `api/cliente.ts` lo use sin un ciclo de módulos. */
export { LARGO_MAXIMO_RUTA_BACKUP };

/** Los mismos textos que `ValidacionUbicacionBackup` de la Api para las dos reglas que se validan en el navegador. */
export const MENSAJE_RUTA_BACKUP_FALTANTE = 'Ingresá la carpeta del backup.';
export const MENSAJE_RUTA_BACKUP_LARGA = 'La ruta de la carpeta del backup puede tener hasta 260 caracteres.';

export type ValidacionRutaBackup = { tipo: 'ok'; ruta: string } | { tipo: 'error'; mensaje: string };

/**
 * El espejo de las reglas de `UbicacionBackup` que no dependen del equipo: la ruta, recortada como en el servidor, es
 * obligatoria y tiene hasta {@link LARGO_MAXIMO_RUTA_BACKUP} caracteres. Lo demás (caracteres de control, ruta completa,
 * unidad, permisos, carpeta de los datos) lo decide la Api. Devuelve la ruta recortada a enviar o el mensaje del campo.
 * Es una comodidad: la validación que vale es la del servidor.
 */
export function validarRutaBackup(escrita: string): ValidacionRutaBackup {
  const ruta = recortar(escrita);
  if (ruta === '') {
    return { tipo: 'error', mensaje: MENSAJE_RUTA_BACKUP_FALTANTE };
  }
  if (ruta.length > LARGO_MAXIMO_RUTA_BACKUP) {
    return { tipo: 'error', mensaje: MENSAJE_RUTA_BACKUP_LARGA };
  }
  return { tipo: 'ok', ruta };
}
