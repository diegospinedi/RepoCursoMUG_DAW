/**
 * Cliente de la Api: `fetch` al mismo origen (sin CORS), con la cookie de sesión que maneja el navegador; el cliente
 * nunca la lee. Traduce cada respuesta a un {@link Resultado} que las pantallas muestran tal cual.
 *
 * Reglas de este archivo:
 * - La contraseña solo viaja en el cuerpo JSON de `definir`, `ingresar` y `cambiar`: nunca en la URL, en el
 *   almacenamiento del navegador ni en la consola.
 * - No hay consultas periódicas: cualquier pedido con cookie renueva la sesión en la Api, y una consulta automática la
 *   mantendría abierta sin que nadie use el sistema.
 * - Hay dos 401: con `errors` en el cuerpo es un rechazo del campo (contraseña incorrecta); vacío, o sin `errors`, es
 *   "sin sesión" y avisa al manejador registrado con {@link alPerderLaSesion}.
 */

import {
  IMPORTE_MAXIMO_SEGURO,
  LARGO_MAXIMO_RUTA_BACKUP,
  mostrarImporte,
  PUNTO_VENTA_MAXIMO,
  PUNTO_VENTA_MINIMO,
  sumarCentesimos,
} from '../configuracion/numeros';
import { esFecha } from './fechas';

export const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';
export const MENSAJE_OCUPADO = 'El sistema está ocupado. Intentá de nuevo en un momento.';
export const MENSAJE_PEDIDO_RECHAZADO = 'No se pudo completar la operación. Recargá la página y reintentá.';
export const MENSAJE_CUERPO_GRANDE = 'La contraseña es demasiado larga. Ingresá una de 128 caracteres o menos.';
export const MENSAJE_YA_DEFINIDA = 'Ya hay una contraseña definida. Ingresá con ella.';
export const MENSAJE_SIN_CONTRASENA = 'Todavía no hay una contraseña. Definí la contraseña primero.';
export const MENSAJE_BLOQUEADO = 'El acceso está bloqueado temporalmente. Volvé a intentar en unos minutos.';
export const MENSAJE_SIN_CREDENCIAL_AL_CAMBIAR = 'No hay una contraseña definida. Volvé a ingresar.';
export const MENSAJE_CONFIGURACION_GRANDE = 'Los valores enviados son demasiado largos. Revisalos y grabá de nuevo.';
export const MENSAJE_ARTICULO_GRANDE = 'Los datos enviados son demasiado largos. Revisalos y grabá de nuevo.';
export const MENSAJE_ARTICULO_NO_ENCONTRADO = 'No se encontró el artículo.';
/** Textos propios de los 409 de los artículos, según su `motivo` (D-19): nunca el `detail` del servidor. */
export const MENSAJE_ARTICULO_INACTIVO = 'Este artículo está inactivo. Reactivalo para editarlo.';
export const MENSAJE_ARTICULO_CAMBIADO = 'El artículo cambió mientras lo editabas. Volvé a abrirlo.';
export const MENSAJE_LISTADO_CAMBIADO = 'El artículo cambió. Volvé a cargar el listado.';
/**
 * Textos propios de los 409 del presupuesto según su `motivo` (FEAT-003c, D-07): nunca el `detail` del servidor. Viven
 * acá, como los de los artículos, para que este archivo no importe valores de las reglas (que ya lo importan a él).
 */
export const MENSAJE_PRESUPUESTO_CAMBIADO = 'El presupuesto cambió mientras lo editabas. Volvé a abrirlo.';
export const MENSAJE_PRESUPUESTO_FINAL = 'El presupuesto está en Final y no se puede modificar.';
/** El 409 `borrador` del PDF (FEAT-006, D-11): la pantalla quedó vieja y el presupuesto sigue en Borrador. */
export const MENSAJE_PRESUPUESTO_BORRADOR_PDF = 'El presupuesto está en Borrador: pasalo a Final para descargar el PDF.';

/** Campo de la contraseña en el cuerpo y en `errors` de la Api. */
export const CAMPO_CONTRASENA = 'contrasena';
/** Campos del cuerpo y de `errors` de `cambiar`. */
export const CAMPO_ACTUAL = 'contrasenaActual';
export const CAMPO_NUEVA = 'contrasenaNueva';
/** Campos del cuerpo y de `errors` de `/api/configuracion`. */
export const CAMPO_ALICUOTA = 'alicuotaIva';
export const CAMPO_CONDICION = 'condicionFiscal';
export const CAMPO_MULTIPLO = 'multiploRedondeo';
export const CAMPO_TOPE = 'topeIdentificacion';
/** El punto de venta (FEAT-007, FR-28). */
export const CAMPO_PUNTO_VENTA = 'puntoVenta';

/** Campos del cuerpo y de `errors` de `/api/articulos`. */
export const CAMPO_CODIGO_PROVEEDOR = 'codigoProveedor';
export const CAMPO_DESCRIPCION = 'descripcion';
export const CAMPO_PRECIO_COSTO = 'precioCosto';
export const CAMPO_MARGEN_UTILIDAD = 'margenUtilidad';
/** Campo del cuerpo y de `errors` de `PUT /api/articulos/{codigo}` con la versión leída (D-17). */
export const CAMPO_VERSION = 'version';
/** Parámetro de `GET /api/articulos` y campo de `errors` del texto buscado. */
export const CAMPO_TEXTO = 'texto';

/** Lo que se graba en `POST /api/articulos`. */
export interface DatosArticulo {
  codigoProveedor: string;
  descripcion: string;
  precioCosto: number;
  margenUtilidad: number;
}

/**
 * Lo que devuelven `POST /api/articulos` (201), `GET /api/articulos/{codigo}` y los dos `PUT` (200): el artículo
 * completo, con su estado y la versión que la edición envía de vuelta (D-08, D-17).
 */
export interface ArticuloGrabado extends DatosArticulo {
  codigo: number;
  precioVenta: number;
  /** "Precio de costo negativo o cero" (FR-05), o `null`. */
  advertencia: string | null;
  activo: boolean;
  /** Entero de 0 a 2^53 − 1 (D-17). */
  version: number;
}

/** Una fila del listado: sin costo ni margen (D-12). */
export interface ArticuloListado {
  codigo: number;
  codigoProveedor: string;
  descripcion: string;
  precioVenta: number;
  activo: boolean;
}

/** Una página de `GET /api/articulos` (D-01). */
export interface PaginaArticulos {
  articulos: ArticuloListado[];
  pagina: number;
  tamanoPagina: number;
  total: number;
}

/**
 * Los motivos de un 409: los de los artículos (D-19), `final` del presupuesto (FEAT-003c, D-05), `borrador` del PDF
 * del presupuesto (FEAT-006, D-11) y de la factura, los demás de la emisión de la factura (FEAT-007, D-18, D-31), los
 * de la verificación y el descarte de una emisión pendiente (FEAT-008, spec Block 6) y `sin_datos_emisor` del PDF de la
 * factura (FEAT-009, FR-04).
 */
export type MotivoConflicto =
  | 'inactivo'
  | 'cambiado'
  | 'final'
  | 'borrador'
  | 'facturado'
  | 'pendiente'
  | 'sin_tope'
  | 'sin_punto_venta'
  | 'alicuota_no_admitida'
  | 'rechazada'
  | 'sin_respuesta'
  | 'no_enviada'
  | 'pendiente_verificacion'
  | 'sin_pendiente'
  | 'espera'
  | 'importe_distinto'
  | 'sin_numero'
  | 'otro_entorno'
  | 'sin_respuesta_emision'
  | 'sin_observacion'
  | 'sin_datos_emisor';

const MOTIVOS_CONFLICTO: readonly MotivoConflicto[] = [
  'inactivo',
  'cambiado',
  'final',
  'borrador',
  'facturado',
  'pendiente',
  'sin_tope',
  'sin_punto_venta',
  'alicuota_no_admitida',
  'rechazada',
  'sin_respuesta',
  'no_enviada',
  'pendiente_verificacion',
  'sin_pendiente',
  'espera',
  'importe_distinto',
  'sin_numero',
  'otro_entorno',
  'sin_respuesta_emision',
  'sin_observacion',
  'sin_datos_emisor',
];

const esMotivoConflicto = (valor: unknown): valor is MotivoConflicto => MOTIVOS_CONFLICTO.some((m) => m === valor);

/** El resultado de grabar un artículo: como {@link ResultadoGrabacion}, un 400 trae todos los campos inválidos. */
export type ResultadoAlta = Resultado<ArticuloGrabado> | { tipo: 'campos'; errores: ErrorDeCampo[] };

/** Los textos de la condición fiscal en el contrato de la Api. */
export const CONDICIONES_FISCALES = ['responsable_inscripto', 'monotributo'] as const;
export type CondicionFiscal = (typeof CONDICIONES_FISCALES)[number];

/** Lo que se graba en `POST /api/configuracion`. */
export interface DatosConfiguracion {
  alicuotaIva: number;
  condicionFiscal: CondicionFiscal;
  multiploRedondeo: number;
  /** `null`: sin tope cargado. */
  topeIdentificacion: number | null;
  /**
   * Entero de 1 a 99999 (FR-28 de FEAT-007). En la respuesta, `null` mientras no se cargue; al grabar, `null` conserva
   * el guardado (D-27).
   */
  puntoVenta: number | null;
}

/** Lo que devuelve `GET /api/configuracion`. */
export interface ConfiguracionNegocio extends DatosConfiguracion {
  faltaTope: boolean;
  /**
   * Las alícuotas que acepta ARCA (FR-26), para la lista de la pantalla. La guardada puede no estar entre ellas si se
   * grabó antes de FR-26 (D-06).
   */
  alicuotasAdmitidas: number[];
}

/**
 * Lo que devuelve `POST /api/configuracion` (D-23 de FEAT-002c): lo guardado y cuántos artículos cambiaron de precio
 * de venta al recalcular el catálogo (D-09), un entero de 0 en adelante.
 */
export interface ConfiguracionGrabada extends ConfiguracionNegocio {
  preciosCambiados: number;
}

export interface ErrorDeCampo {
  campo: string;
  mensaje: string;
}

/** El resultado de grabar: además de los de {@link Resultado}, un 400 trae todos los campos inválidos juntos. */
export type ResultadoGrabacion = Resultado<ConfiguracionGrabada> | { tipo: 'campos'; errores: ErrorDeCampo[] };

export type Resultado<T> =
  | { tipo: 'ok'; datos: T }
  /** Error que se muestra junto al campo indicado. */
  | { tipo: 'campo'; campo: string; mensaje: string }
  /**
   * 409: la operación no corresponde al estado del acceso o del artículo. En los artículos lleva el `motivo` que
   * informó la Api (D-19) y el texto propio de la pantalla para ese motivo.
   */
  | { tipo: 'conflicto'; mensaje: string; motivo?: MotivoConflicto }
  /** Error que se muestra como mensaje general de la pantalla. */
  | { tipo: 'general'; mensaje: string }
  /** 423: el ingreso está bloqueado por intentos fallidos; se muestra como mensaje general. */
  | { tipo: 'bloqueado'; mensaje: string }
  /** 401 sin cuerpo: no hay sesión (o venció). */
  | { tipo: 'sinSesion' };

export interface EstadoAcceso {
  contrasenaDefinida: boolean;
}

export interface Marca {
  primario: string;
  fondo: string;
  esEjemplo: boolean;
}

let manejadorSinSesion: (() => void) | null = null;

/** Registra (o quita, con `null`) lo que se hace ante un 401 sin sesión: limpiar el estado e ir a `/ingreso`. */
export function alPerderLaSesion(manejador: (() => void) | null): void {
  manejadorSinSesion = manejador;
}

type Lector<T> = (cuerpo: unknown) => T | undefined;

/** El texto propio de un motivo de 409: fijo, o armado con el cuerpo del 409 (`undefined` si es ilegible). */
type TextoDelMotivo = string | ((cuerpo: Record<string, unknown>) => string | undefined);

/** Cualquier resultado que no sea `ok`: sirve para un {@link Resultado} de cualquier tipo de datos. */
type Fallo = Exclude<Resultado<never>, { tipo: 'ok' }>;

interface Opciones {
  metodo: 'GET' | 'POST' | 'PUT';
  ruta: string;
  cuerpo?: unknown;
  mensaje409?: string;
  /**
   * Para los 409 con `motivo` (D-19): el texto propio de cada motivo que la operación admite. Con esta opción el 409
   * se lee: un motivo que no está acá, o un cuerpo ilegible, es una respuesta ilegible. Un motivo cuyo texto depende
   * del cuerpo (el rechazo de ARCA, FEAT-007) lleva una función que lo arma, o `undefined` si el cuerpo es ilegible.
   */
  motivos409?: Partial<Record<MotivoConflicto, TextoDelMotivo>>;
  /** Mensaje general de un 404; sin él, el mensaje de pedido rechazado. */
  mensaje404?: string;
  /** Campo del formulario donde se muestra un 413 (cuerpo demasiado grande); por defecto, {@link CAMPO_CONTRASENA}. */
  campo413?: string;
  /** Mensaje general de un 413, para un pedido sin un campo al que atribuirlo; tiene prioridad sobre `campo413`. */
  mensaje413?: string;
  /** Mensaje general de un 429; sin él, {@link MENSAJE_OCUPADO}. */
  mensaje429?: string;
  /** Mensaje general de una respuesta exitosa ilegible en {@link pedir}; sin él, {@link MENSAJE_SIN_CONEXION}. */
  mensajeIlegible?: string;
}

/** Pedido cuya respuesta exitosa trae datos: `leer` valida la forma del JSON. */
async function pedir<T>(opciones: Opciones, leer: Lector<T>): Promise<Resultado<T>> {
  const respuesta = await enviar(opciones);
  if (respuesta.tipo !== 'respuesta') {
    return respuesta;
  }
  const datos = leer(await leerJson(respuesta.respuesta));
  return datos === undefined
    ? { tipo: 'general', mensaje: opciones.mensajeIlegible ?? MENSAJE_SIN_CONEXION }
    : { tipo: 'ok', datos };
}

/** Pedido cuya respuesta exitosa no trae datos (204, 201): el cuerpo no se lee. */
async function pedirSinDatos(opciones: Opciones): Promise<Resultado<void>> {
  const respuesta = await enviar(opciones);
  return respuesta.tipo === 'respuesta' ? { tipo: 'ok', datos: undefined } : respuesta;
}

/** Hace el pedido: devuelve la respuesta exitosa, o el fallo ya traducido. */
async function enviar(opciones: Opciones): Promise<{ tipo: 'respuesta'; respuesta: Response } | Fallo> {
  const respuesta = await hacerPedido(opciones);
  if (respuesta instanceof Response) {
    return respuesta.ok ? { tipo: 'respuesta', respuesta } : traducirError(respuesta, opciones);
  }
  return respuesta;
}

/** El `fetch` de un pedido: la respuesta tal cual (con cualquier código), o el fallo de red ya traducido. */
async function hacerPedido({ metodo, ruta, cuerpo }: Opciones): Promise<Response | Fallo> {
  const init: RequestInit = { method: metodo, credentials: 'same-origin', cache: 'no-store' };
  if (metodo === 'POST' || metodo === 'PUT') {
    // La Api exige Content-Type: application/json en todo POST y PUT, aunque no lleve cuerpo (R-04; D-11: si no, 415).
    init.headers = { 'Content-Type': 'application/json', Accept: 'application/json' };
    if (cuerpo !== undefined) {
      init.body = JSON.stringify(cuerpo);
    }
  }

  try {
    return await fetch(ruta, init);
  } catch {
    return { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION };
  }
}

async function traducirError(
  respuesta: Response,
  { mensaje409, motivos409, mensaje404, campo413 = CAMPO_CONTRASENA, mensaje413, mensaje429 }: Opciones,
): Promise<Fallo> {
  switch (respuesta.status) {
    case 400: {
      const error = primerError(await leerJson(respuesta));
      return error ? { tipo: 'campo', ...error } : { tipo: 'general', mensaje: MENSAJE_PEDIDO_RECHAZADO };
    }
    case 401: {
      const error = primerError(await leerJson(respuesta));
      if (error) {
        return { tipo: 'campo', ...error };
      }
      manejadorSinSesion?.();
      return { tipo: 'sinSesion' };
    }
    case 404:
      return { tipo: 'general', mensaje: mensaje404 ?? MENSAJE_PEDIDO_RECHAZADO };
    case 409:
      return motivos409 === undefined
        ? { tipo: 'conflicto', mensaje: mensaje409 ?? MENSAJE_PEDIDO_RECHAZADO }
        : conflictoConMotivo(await leerJson(respuesta), motivos409);
    case 413:
      return mensaje413 !== undefined
        ? { tipo: 'general', mensaje: mensaje413 }
        : { tipo: 'campo', campo: campo413, mensaje: MENSAJE_CUERPO_GRANDE };
    case 423:
      // El texto es propio, no el `detail` del servidor: la Api no informa hasta cuándo dura el bloqueo.
      return { tipo: 'bloqueado', mensaje: MENSAJE_BLOQUEADO };
    case 429:
      return { tipo: 'general', mensaje: mensaje429 ?? MENSAJE_OCUPADO };
    default:
      return {
        tipo: 'general',
        mensaje: respuesta.status >= 500 ? MENSAJE_SIN_CONEXION : MENSAJE_PEDIDO_RECHAZADO,
      };
  }
}

/**
 * Un 409 con `motivo` (D-19): el motivo, si es uno de los que la operación admite, con el texto propio de la pantalla.
 * El `detail` del servidor no se usa. Un motivo desconocido, ausente o un cuerpo ilegible es una respuesta ilegible:
 * el mismo mensaje general que una respuesta exitosa con una forma inesperada.
 */
function conflictoConMotivo(cuerpo: unknown, motivos: Partial<Record<MotivoConflicto, TextoDelMotivo>>): Fallo {
  if (esObjeto(cuerpo) && esMotivoConflicto(cuerpo.motivo)) {
    const motivo = cuerpo.motivo;
    const texto = motivos[motivo];
    const mensaje = typeof texto === 'function' ? texto(cuerpo) : texto;
    if (mensaje !== undefined) {
      return { tipo: 'conflicto', motivo, mensaje };
    }
  }
  return { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION };
}

async function leerJson(respuesta: Response): Promise<unknown> {
  try {
    return await respuesta.json();
  } catch {
    return undefined;
  }
}

/** El primer mensaje de `errors` de un ValidationProblemDetails, con su campo. */
function primerError(cuerpo: unknown): { campo: string; mensaje: string } | undefined {
  if (!esObjeto(cuerpo) || !esObjeto(cuerpo.errors)) {
    return undefined;
  }
  for (const [campo, mensajes] of Object.entries(cuerpo.errors)) {
    if (Array.isArray(mensajes) && typeof mensajes[0] === 'string') {
      return { campo, mensaje: mensajes[0] };
    }
  }
  return undefined;
}

/**
 * Todos los mensajes de `errors` de un ValidationProblemDetails, el primero de cada campo, en el orden del cuerpo. Lo
 * usan {@link grabarConfiguracion} y {@link grabarArticulo}: esas pantallas informan todos los campos inválidos juntos.
 */
function todosLosErrores(cuerpo: unknown): ErrorDeCampo[] {
  if (!esObjeto(cuerpo) || !esObjeto(cuerpo.errors)) {
    return [];
  }
  const errores: ErrorDeCampo[] = [];
  for (const [campo, mensajes] of Object.entries(cuerpo.errors)) {
    if (Array.isArray(mensajes) && typeof mensajes[0] === 'string') {
      errores.push({ campo, mensaje: mensajes[0] });
    }
  }
  return errores;
}

function esObjeto(valor: unknown): valor is Record<string, unknown> {
  return typeof valor === 'object' && valor !== null;
}

const leerEstado: Lector<EstadoAcceso> = (cuerpo) =>
  esObjeto(cuerpo) && typeof cuerpo.contrasenaDefinida === 'boolean'
    ? { contrasenaDefinida: cuerpo.contrasenaDefinida }
    : undefined;

const leerMarca: Lector<Marca> = (cuerpo) =>
  esObjeto(cuerpo) &&
  typeof cuerpo.primario === 'string' &&
  typeof cuerpo.fondo === 'string' &&
  typeof cuerpo.esEjemplo === 'boolean'
    ? { primario: cuerpo.primario, fondo: cuerpo.fondo, esEjemplo: cuerpo.esEjemplo }
    : undefined;

const esNumero = (valor: unknown): valor is number => typeof valor === 'number' && Number.isFinite(valor);

const esCondicion = (valor: unknown): valor is CondicionFiscal =>
  CONDICIONES_FISCALES.some((condicion) => condicion === valor);

/**
 * El punto de venta de la respuesta: `null` o un entero de {@link PUNTO_VENTA_MINIMO} a {@link PUNTO_VENTA_MAXIMO},
 * invariante también al leer (D-33).
 */
const esPuntoVenta = (valor: unknown): valor is number | null =>
  valor === null || (esEnteroEntre(valor, PUNTO_VENTA_MINIMO) && valor <= PUNTO_VENTA_MAXIMO);

/** La lista de alícuotas admitidas: un arreglo no vacío de números, sin repetidos (cada uno es una opción). */
const esListaDeAlicuotas = (valor: unknown): valor is number[] =>
  Array.isArray(valor) && valor.length > 0 && valor.every(esNumero) && new Set(valor).size === valor.length;

/**
 * La configuración vigente. Exige `puntoVenta` (null o entero de 1 a 99999) y `alicuotasAdmitidas` (FEAT-007): sin
 * ellos la respuesta es ilegible. La alícuota guardada no se compara con la lista (D-06): la pantalla la marca.
 */
const leerConfiguracion: Lector<ConfiguracionNegocio> = (cuerpo) =>
  esObjeto(cuerpo) &&
  esNumero(cuerpo.alicuotaIva) &&
  esCondicion(cuerpo.condicionFiscal) &&
  esNumero(cuerpo.multiploRedondeo) &&
  (cuerpo.topeIdentificacion === null || esNumero(cuerpo.topeIdentificacion)) &&
  typeof cuerpo.faltaTope === 'boolean' &&
  esPuntoVenta(cuerpo.puntoVenta) &&
  esListaDeAlicuotas(cuerpo.alicuotasAdmitidas)
    ? {
        alicuotaIva: cuerpo.alicuotaIva,
        condicionFiscal: cuerpo.condicionFiscal,
        multiploRedondeo: cuerpo.multiploRedondeo,
        topeIdentificacion: cuerpo.topeIdentificacion,
        faltaTope: cuerpo.faltaTope,
        puntoVenta: cuerpo.puntoVenta,
        alicuotasAdmitidas: [...cuerpo.alicuotasAdmitidas],
      }
    : undefined;

/**
 * Un importe dentro del límite del navegador (D-19): un número finito cuyo módulo no supera
 * {@link IMPORTE_MAXIMO_SEGURO}, el mayor entero seguro / 100; uno mayor hace ilegible la respuesta. Dentro del
 * límite los centavos son exactos solo hasta 2^46 (70.368.744.177.664); ver D-22 en
 * `docs/ddw/specs/decisions-FEAT-002a.md`.
 */
const esImporte = (valor: unknown): valor is number => esNumero(valor) && Math.abs(valor) <= IMPORTE_MAXIMO_SEGURO;

const leerArticulo: Lector<ArticuloGrabado> = (cuerpo) =>
  esObjeto(cuerpo) &&
  typeof cuerpo.codigo === 'number' &&
  Number.isSafeInteger(cuerpo.codigo) &&
  typeof cuerpo.codigoProveedor === 'string' &&
  typeof cuerpo.descripcion === 'string' &&
  esImporte(cuerpo.precioCosto) &&
  esNumero(cuerpo.margenUtilidad) &&
  esImporte(cuerpo.precioVenta) &&
  (cuerpo.advertencia === null || typeof cuerpo.advertencia === 'string') &&
  typeof cuerpo.activo === 'boolean' &&
  esVersion(cuerpo.version)
    ? {
        codigo: cuerpo.codigo,
        codigoProveedor: cuerpo.codigoProveedor,
        descripcion: cuerpo.descripcion,
        precioCosto: cuerpo.precioCosto,
        margenUtilidad: cuerpo.margenUtilidad,
        precioVenta: cuerpo.precioVenta,
        advertencia: cuerpo.advertencia,
        activo: cuerpo.activo,
        version: cuerpo.version,
      }
    : undefined;

/** Un entero seguro dentro del rango indicado: `Number.isSafeInteger` ya limita a 2^53 − 1. */
const esEnteroEntre = (valor: unknown, minimo: number): valor is number =>
  typeof valor === 'number' && Number.isSafeInteger(valor) && valor >= minimo;

/** La versión del artículo: entero de 0 a 9.007.199.254.740.991 (2^53 − 1, D-17). */
const esVersion = (valor: unknown): valor is number => esEnteroEntre(valor, 0);

/**
 * La respuesta de grabar la configuración (D-23): la configuración, como la lee {@link leerConfiguracion}, más
 * `preciosCambiados`, un entero seguro de 0 en adelante. Ausente o de otra forma, la respuesta es ilegible. El GET no
 * la usa: sigue con {@link leerConfiguracion}, sin exigir la cantidad.
 */
const leerConfiguracionGrabada: Lector<ConfiguracionGrabada> = (cuerpo) => {
  const configuracion = leerConfiguracion(cuerpo);
  return configuracion !== undefined && esObjeto(cuerpo) && esEnteroEntre(cuerpo.preciosCambiados, 0)
    ? { ...configuracion, preciosCambiados: cuerpo.preciosCambiados }
    : undefined;
};

/** Una fila del listado. Solo copia los cinco campos de D-12: si la respuesta trajera costo o margen, se descartan. */
const leerFila: Lector<ArticuloListado> = (cuerpo) =>
  esObjeto(cuerpo) &&
  typeof cuerpo.codigo === 'number' &&
  Number.isSafeInteger(cuerpo.codigo) &&
  typeof cuerpo.codigoProveedor === 'string' &&
  typeof cuerpo.descripcion === 'string' &&
  esImporte(cuerpo.precioVenta) &&
  typeof cuerpo.activo === 'boolean'
    ? {
        codigo: cuerpo.codigo,
        codigoProveedor: cuerpo.codigoProveedor,
        descripcion: cuerpo.descripcion,
        precioVenta: cuerpo.precioVenta,
        activo: cuerpo.activo,
      }
    : undefined;

/** Una página del listado: cualquier fila ilegible hace ilegible la página entera (sin importes falsos). */
const leerPagina: Lector<PaginaArticulos> = (cuerpo) => {
  if (
    !esObjeto(cuerpo) ||
    !Array.isArray(cuerpo.articulos) ||
    !esEnteroEntre(cuerpo.pagina, 1) ||
    !esEnteroEntre(cuerpo.tamanoPagina, 1) ||
    !esEnteroEntre(cuerpo.total, 0)
  ) {
    return undefined;
  }
  const articulos: ArticuloListado[] = [];
  for (const elemento of cuerpo.articulos as unknown[]) {
    const fila = leerFila(elemento);
    if (fila === undefined) {
      return undefined;
    }
    articulos.push(fila);
  }
  return { articulos, pagina: cuerpo.pagina, tamanoPagina: cuerpo.tamanoPagina, total: cuerpo.total };
};

/** Un código de artículo que puede existir: entero positivo seguro. */
export const esCodigoArticulo = (codigo: number) => Number.isSafeInteger(codigo) && codigo > 0;

export const consultarEstado = () => pedir({ metodo: 'GET', ruta: '/api/acceso/estado' }, leerEstado);

export const consultarSesion = () => pedirSinDatos({ metodo: 'GET', ruta: '/api/sesion' });

export const consultarMarca = () => pedir({ metodo: 'GET', ruta: '/api/marca' }, leerMarca);

export const definirContrasena = (contrasena: string) =>
  pedirSinDatos({
    metodo: 'POST',
    ruta: '/api/acceso/definir',
    cuerpo: { [CAMPO_CONTRASENA]: contrasena },
    mensaje409: MENSAJE_YA_DEFINIDA,
  });

export const ingresar = (contrasena: string) =>
  pedirSinDatos({
    metodo: 'POST',
    ruta: '/api/acceso/ingresar',
    cuerpo: { [CAMPO_CONTRASENA]: contrasena },
    mensaje409: MENSAJE_SIN_CONTRASENA,
  });

export const salir = () => pedirSinDatos({ metodo: 'POST', ruta: '/api/acceso/salir' });

/**
 * Cambia la contraseña con la sesión iniciada. Un 413 va al campo de la actual: la nueva ya se validó en el navegador
 * (hasta 128 caracteres), así que solo la actual puede hacer que el cuerpo supere el límite de la Api.
 */
export const cambiarContrasena = (actual: string, nueva: string) =>
  pedirSinDatos({
    metodo: 'POST',
    ruta: '/api/acceso/cambiar',
    cuerpo: { [CAMPO_ACTUAL]: actual, [CAMPO_NUEVA]: nueva },
    mensaje409: MENSAJE_SIN_CREDENCIAL_AL_CAMBIAR,
    campo413: CAMPO_ACTUAL,
  });

/** Los parámetros de negocio guardados; se consulta al abrir la pantalla, sin consultas periódicas. */
export const consultarConfiguracion = () => pedir({ metodo: 'GET', ruta: '/api/configuracion' }, leerConfiguracion);

/**
 * Graba los parámetros juntos (el punto de venta en `null` conserva el guardado, D-27 de FEAT-007) y devuelve lo que
 * quedó guardado (D-03), con cuántos precios de venta cambiaron al recalcular el catálogo (D-09 y D-23 de FEAT-002c).
 * A diferencia de las pantallas de acceso,
 * un 400 devuelve TODOS los campos de `errors`, y un 413 es un mensaje general propio (no el de la contraseña): el
 * navegador limita cada número a 32 caracteres, así que solo un cuerpo alterado supera el límite de la Api, y no hay
 * un campo al que atribuirlo.
 */
export const grabarConfiguracion = (datos: DatosConfiguracion): Promise<ResultadoGrabacion> =>
  grabarConCampos(
    { metodo: 'POST', ruta: '/api/configuracion', cuerpo: datos, mensaje413: MENSAJE_CONFIGURACION_GRANDE },
    leerConfiguracionGrabada,
  );

// ---- Datos del emisor (FEAT-009, ADR-028): GET y POST /api/configuracion/emisor ----

export const CAMPO_RAZON_SOCIAL = 'razonSocial';
export const CAMPO_DOMICILIO_COMERCIAL = 'domicilioComercial';
export const CAMPO_CUIT = 'cuit';
export const CAMPO_INGRESOS_BRUTOS = 'ingresosBrutos';
export const CAMPO_INICIO_ACTIVIDADES = 'inicioActividades';

/** Mensaje general de un 413 al grabar los datos del emisor: el navegador nunca envía un cuerpo de ese tamaño. */
export const MENSAJE_EMISOR_GRANDE = 'Los datos del emisor enviados son demasiado largos. Revisalos y grabá de nuevo.';

/**
 * Los cinco datos del emisor, como los graba y los devuelve la Api: el CUIT con 11 dígitos, sin guiones, y la fecha de
 * inicio de actividades como `yyyy-MM-dd`.
 */
export interface DatosEmisor {
  razonSocial: string;
  domicilioComercial: string;
  cuit: string;
  ingresosBrutos: string;
  inicioActividades: string;
}

/** El resultado de grabar los datos del emisor: un 400 trae todos los campos inválidos juntos. */
export type ResultadoGrabacionEmisor = Resultado<DatosEmisor> | { tipo: 'campos'; errores: ErrorDeCampo[] };

/** El CUIT en forma canónica: 11 dígitos ASCII, sin guiones (sin el dígito verificador, que valida `reglas.ts`). */
export const CUIT_CANONICO = /^\d{11}$/;

/** Los cinco datos cargados: textos, el CUIT en forma canónica y la fecha existente. Solo copia los cinco campos. */
const leerDatosEmisorCargados: Lector<DatosEmisor> = (cuerpo) =>
  esObjeto(cuerpo) &&
  typeof cuerpo.razonSocial === 'string' &&
  typeof cuerpo.domicilioComercial === 'string' &&
  typeof cuerpo.cuit === 'string' &&
  CUIT_CANONICO.test(cuerpo.cuit) &&
  typeof cuerpo.ingresosBrutos === 'string' &&
  esFecha(cuerpo.inicioActividades)
    ? {
        razonSocial: cuerpo.razonSocial,
        domicilioComercial: cuerpo.domicilioComercial,
        cuit: cuerpo.cuit,
        ingresosBrutos: cuerpo.ingresosBrutos,
        inicioActividades: cuerpo.inicioActividades,
      }
    : undefined;

/** Los cinco campos de los datos del emisor, en el orden del formulario y del contrato. */
export const CAMPOS_EMISOR = [
  CAMPO_RAZON_SOCIAL,
  CAMPO_DOMICILIO_COMERCIAL,
  CAMPO_CUIT,
  CAMPO_INGRESOS_BRUTOS,
  CAMPO_INICIO_ACTIVIDADES,
] as const;

/**
 * Los datos del emisor vigentes: los cinco cargados, o `null` si la Api devolvió los cinco en null (nunca se cargaron,
 * D-22). Una mezcla de null y textos, o cualquier otra forma, es una respuesta ilegible.
 */
const leerDatosEmisorVigentes: Lector<DatosEmisor | null> = (cuerpo) =>
  esObjeto(cuerpo) && CAMPOS_EMISOR.every((campo) => cuerpo[campo] === null) ? null : leerDatosEmisorCargados(cuerpo);

/**
 * Los datos del emisor guardados (FR-01); `null` mientras no se cargaron. "Sin cargar" es el 200 con los cinco en null
 * del contrato: un 404 no se toma como tal, es el mensaje general de pedido rechazado (D-22).
 */
export const consultarDatosEmisor = (): Promise<Resultado<DatosEmisor | null>> =>
  pedir({ metodo: 'GET', ruta: '/api/configuracion/emisor' }, leerDatosEmisorVigentes);

/**
 * Graba los cinco datos del emisor juntos (FR-02) y devuelve lo que quedó guardado (el CUIT sin guiones, AC-03). Como
 * {@link grabarConfiguracion}: un 400 devuelve TODOS los campos de `errors` y un 413 es un mensaje general propio.
 */
export const grabarDatosEmisor = (datos: DatosEmisor): Promise<ResultadoGrabacionEmisor> =>
  grabarConCampos(
    { metodo: 'POST', ruta: '/api/configuracion/emisor', cuerpo: datos, mensaje413: MENSAJE_EMISOR_GRANDE },
    leerDatosEmisorCargados,
  );

// ---- Ubicación del backup (FEAT-010): GET y POST /api/configuracion/backup ----

/** El único campo del cuerpo de la ubicación del backup. */
export const CAMPO_RUTA_BACKUP = 'ruta';

/** Mensaje general de un 413 al grabar la ubicación: el navegador limita la ruta y nunca envía un cuerpo así. */
export const MENSAJE_BACKUP_GRANDE = 'La ruta enviada es demasiado larga. Revisala y grabá de nuevo.';

/** El resultado de grabar la ubicación del backup: un 400 trae el error del campo `ruta`. */
export type ResultadoGrabacionBackup = Resultado<string> | { tipo: 'campos'; errores: ErrorDeCampo[] };

/** Una ruta grabada: un texto de 1 a {@link LARGO_MAXIMO_RUTA_BACKUP} caracteres. */
const esRutaBackup = (valor: unknown): valor is string =>
  typeof valor === 'string' && valor.length > 0 && valor.length <= LARGO_MAXIMO_RUTA_BACKUP;

/** La ubicación grabada (respuesta del POST): `{ ruta }` con una ruta válida; solo copia ese campo. */
const leerUbicacionBackupGrabada: Lector<string> = (cuerpo) =>
  esObjeto(cuerpo) && esRutaBackup(cuerpo.ruta) ? cuerpo.ruta : undefined;

/**
 * La ubicación vigente (respuesta del GET): la ruta, o `null` si nunca se eligió. `ruta` ausente o de otra forma es una
 * respuesta ilegible.
 */
const leerUbicacionBackupVigente: Lector<string | null> = (cuerpo) =>
  esObjeto(cuerpo) && cuerpo.ruta === null ? null : leerUbicacionBackupGrabada(cuerpo);

/**
 * La ubicación del backup grabada (FR-01); `null` mientras nunca se eligió. La ruta puede llevar el nombre de una
 * persona (D-12): quien la consulta la guarda solo en el estado de su componente.
 */
export const consultarUbicacionBackup = (): Promise<Resultado<string | null>> =>
  pedir({ metodo: 'GET', ruta: '/api/configuracion/backup' }, leerUbicacionBackupVigente);

/**
 * Graba la ubicación del backup (FR-01 a FR-03) y devuelve la ruta que quedó grabada (normalizada por la Api). Como
 * {@link grabarConfiguracion}: un 400 devuelve los errores de `errors` (el de `ruta`, con el texto de la Api) y un 413
 * es un mensaje general propio.
 */
export const grabarUbicacionBackup = (ruta: string): Promise<ResultadoGrabacionBackup> =>
  grabarConCampos(
    {
      metodo: 'POST',
      ruta: '/api/configuracion/backup',
      cuerpo: { [CAMPO_RUTA_BACKUP]: ruta },
      mensaje413: MENSAJE_BACKUP_GRANDE,
    },
    leerUbicacionBackupGrabada,
  );

/**
 * Graba un artículo y devuelve lo que quedó guardado, con su código y su precio de venta (FR-01, FR-11). Como
 * {@link grabarConfiguracion}: un 400 devuelve TODOS los campos de `errors` (incluido el código duplicado, D-07) y un
 * 413 es un mensaje general propio, porque los campos del navegador tienen largo máximo y solo un cuerpo alterado
 * supera el límite de la Api. La Api no responde 409 en el alta.
 */
export const grabarArticulo = (datos: DatosArticulo): Promise<ResultadoAlta> =>
  grabarConCampos(
    { metodo: 'POST', ruta: '/api/articulos', cuerpo: datos, mensaje413: MENSAJE_ARTICULO_GRANDE },
    leerArticulo,
  );

/** Tamaño máximo de la planilla: 10 MiB, el mismo límite que el archivo decodificado en la Api (D-14). */
export const TAMANO_MAXIMO_PLANILLA = 10 * 1024 * 1024;
/** Planilla de más de {@link TAMANO_MAXIMO_PLANILLA}, en el navegador o por el 413 de la Api (D-25). */
export const MENSAJE_PLANILLA_GRANDE = 'La planilla supera los 10 MB.';
/** El 429 del limitador de la planilla (D-25): otra carga está en curso y otra en fila. */
export const MENSAJE_PLANILLA_OCUPADA = 'Hay otra planilla en proceso. Esperá unos segundos y volvé a cargarla.';
/** El 409 `cambiado` de la planilla (D-25): texto propio de la pantalla, igual al `detail` del servidor. */
export const MENSAJE_PLANILLA_CAMBIADA =
  'Un artículo cambió mientras se cargaba la planilla. No se actualizó ningún costo. Volvé a cargarla.';
/** El navegador no pudo leer el archivo elegido (se borró o se movió después de elegirlo). */
export const MENSAJE_PLANILLA_ILEGIBLE = 'No se pudo leer el archivo. Elegilo de nuevo.';
/** Un 200 que no respeta el contrato (D-26): los costos ya se aplicaron, así que no es un fallo de conexión. */
export const MENSAJE_PLANILLA_RESULTADO_ILEGIBLE =
  'La planilla se procesó, pero no se pudo mostrar el resultado. Revisá los costos en el listado.';
/** Largo máximo del código en la respuesta, con el "…" incluido (D-10). */
const LARGO_MAXIMO_CODIGO_PLANILLA = 60;
/** La última fila posible: 50.000 filas de datos después del encabezado (D-14). */
const FILA_MAXIMA_PLANILLA = 50_001;

/** Las razones de una fila no aplicada (FR-13). */
export const RAZONES_NO_APLICADA = ['código inexistente', 'precio inválido', 'código repetido en la planilla'] as const;
export type RazonNoAplicada = (typeof RAZONES_NO_APLICADA)[number];
/** Las indicaciones de una fila aplicada para revisar (FR-14, FR-15). */
export const INDICACIONES_REVISAR = ['actualizado con precio negativo o cero', 'inactivo'] as const;
export type IndicacionRevisar = (typeof INDICACIONES_REVISAR)[number];

/** Una fila de la planilla que no se aplicó: el número de fila, el código acotado a 60 caracteres (D-10) y la razón. */
export interface FilaNoAplicada {
  fila: number;
  codigo: string;
  razon: RazonNoAplicada;
}

/** Una fila aplicada que conviene revisar: el costo que quedó y por qué (precio negativo o cero, artículo inactivo). */
export interface FilaParaRevisar {
  fila: number;
  codigo: string;
  precioCosto: number;
  indicaciones: IndicacionRevisar[];
}

/** La respuesta 200 de `POST /api/articulos/planilla-costos` (Block 4). */
export interface ResultadoPlanilla {
  filasLeidas: number;
  aplicadas: number;
  noAplicadas: FilaNoAplicada[];
  revisar: FilaParaRevisar[];
}

/**
 * Bytes por trozo al pasar a base64: múltiplo de 3, así cada trozo se codifica sin relleno intermedio y los textos se
 * concatenan. Chico para que `String.fromCharCode(...trozo)` no pase el límite de argumentos ni desborde la pila.
 */
const TROZO_BASE64 = 3 * 8192;

/** El base64 de los bytes, por trozos (un archivo de 10 MB en una sola llamada desborda la pila). */
function aBase64(bytes: Uint8Array): string {
  const partes: string[] = [];
  for (let inicio = 0; inicio < bytes.length; inicio += TROZO_BASE64) {
    partes.push(btoa(String.fromCharCode(...bytes.subarray(inicio, inicio + TROZO_BASE64))));
  }
  return partes.join('');
}

const esRazonNoAplicada = (valor: unknown): valor is RazonNoAplicada => RAZONES_NO_APLICADA.some((r) => r === valor);
const esIndicacion = (valor: unknown): valor is IndicacionRevisar => INDICACIONES_REVISAR.some((i) => i === valor);

/** Un número de fila de la planilla: entero de 1 a {@link FILA_MAXIMA_PLANILLA}. */
const esFilaPlanilla = (valor: unknown): valor is number =>
  esEnteroEntre(valor, 1) && valor <= FILA_MAXIMA_PLANILLA;

/** El código como lo acota la Api: texto de hasta {@link LARGO_MAXIMO_CODIGO_PLANILLA} caracteres (D-10). */
const esCodigoPlanilla = (valor: unknown): valor is string =>
  typeof valor === 'string' && valor.length <= LARGO_MAXIMO_CODIGO_PLANILLA;

/** Cada fila aparece una sola vez en su lista: la tabla la usa como clave. */
const filasUnicas = (filas: readonly { fila: number }[]) => new Set(filas.map(({ fila }) => fila)).size === filas.length;

const leerFilaNoAplicada: Lector<FilaNoAplicada> = (cuerpo) =>
  esObjeto(cuerpo) && esFilaPlanilla(cuerpo.fila) && esCodigoPlanilla(cuerpo.codigo) && esRazonNoAplicada(cuerpo.razon)
    ? { fila: cuerpo.fila, codigo: cuerpo.codigo, razon: cuerpo.razon }
    : undefined;

const leerFilaParaRevisar: Lector<FilaParaRevisar> = (cuerpo) => {
  if (!esObjeto(cuerpo)) {
    return undefined;
  }
  const indicaciones = leerLista(cuerpo.indicaciones, (valor) => (esIndicacion(valor) ? valor : undefined));
  return esFilaPlanilla(cuerpo.fila) &&
    esCodigoPlanilla(cuerpo.codigo) &&
    esImporte(cuerpo.precioCosto) &&
    indicaciones !== undefined
    ? { fila: cuerpo.fila, codigo: cuerpo.codigo, precioCosto: cuerpo.precioCosto, indicaciones }
    : undefined;
};

/**
 * La respuesta 200 con sus invariantes (D-26): cada fila leída es aplicada o no aplicada
 * (`aplicadas + noAplicadas.length === filasLeidas`), las para revisar son aplicadas (`revisar.length <= aplicadas`) y
 * ninguna fila se repite dentro de su lista. Una respuesta que no las cumple es ilegible.
 */
const leerResultadoPlanilla: Lector<ResultadoPlanilla> = (cuerpo) => {
  if (!esObjeto(cuerpo)) {
    return undefined;
  }
  const { filasLeidas, aplicadas } = cuerpo;
  const noAplicadas = leerLista(cuerpo.noAplicadas, leerFilaNoAplicada);
  const revisar = leerLista(cuerpo.revisar, leerFilaParaRevisar);
  return esEnteroEntre(filasLeidas, 0) &&
    esEnteroEntre(aplicadas, 0) &&
    aplicadas <= filasLeidas &&
    noAplicadas !== undefined &&
    revisar !== undefined &&
    aplicadas + noAplicadas.length === filasLeidas &&
    revisar.length <= aplicadas &&
    filasUnicas(noAplicadas) &&
    filasUnicas(revisar)
    ? { filasLeidas, aplicadas, noAplicadas, revisar }
    : undefined;
};

/**
 * Actualiza los costos del catálogo con la planilla .xlsx elegida (FEAT-011, D-06): un archivo de más de
 * {@link TAMANO_MAXIMO_PLANILLA} se rechaza sin leerlo ni enviarlo; si no, lo lee entero, lo pasa a base64 y lo envía
 * en el cuerpo JSON (`{ planilla }`), nunca en la URL ni en el almacenamiento del navegador. Un 400 trae el texto de
 * `errors.planilla` (no es .xlsx, formato, límites); 409 `cambiado`, 413 y 429 tienen texto propio (D-25).
 */
export async function actualizarCostosPorPlanilla(archivo: File): Promise<Resultado<ResultadoPlanilla>> {
  if (archivo.size > TAMANO_MAXIMO_PLANILLA) {
    return { tipo: 'general', mensaje: MENSAJE_PLANILLA_GRANDE };
  }
  let planilla: string;
  try {
    planilla = aBase64(new Uint8Array(await archivo.arrayBuffer()));
  } catch {
    return { tipo: 'general', mensaje: MENSAJE_PLANILLA_ILEGIBLE };
  }
  return pedir(
    {
      metodo: 'POST',
      ruta: '/api/articulos/planilla-costos',
      cuerpo: { planilla },
      motivos409: { cambiado: MENSAJE_PLANILLA_CAMBIADA },
      mensaje413: MENSAJE_PLANILLA_GRANDE,
      mensaje429: MENSAJE_PLANILLA_OCUPADA,
      mensajeIlegible: MENSAJE_PLANILLA_RESULTADO_ILEGIBLE,
    },
    leerResultadoPlanilla,
  );
}

/**
 * Un artículo por su código, con su estado y su versión. Un código que no es un entero positivo no se consulta (no
 * puede existir); ese caso y un 404 son el mensaje general {@link MENSAJE_ARTICULO_NO_ENCONTRADO}.
 */
export const consultarArticulo = (codigo: number): Promise<Resultado<ArticuloGrabado>> =>
  esCodigoArticulo(codigo)
    ? pedir(
        { metodo: 'GET', ruta: `/api/articulos/${codigo}`, mensaje404: MENSAJE_ARTICULO_NO_ENCONTRADO },
        leerArticulo,
      )
    : Promise.resolve({ tipo: 'general', mensaje: MENSAJE_ARTICULO_NO_ENCONTRADO });

/**
 * Una página del catálogo (D-01, FR-04). El texto viaja en la query string (D-12) y se omite si está vacío (lista
 * todo, D-06); la pantalla ya lo recortó y validó. Las filas no traen costo ni margen. Con `soloActivos` (la búsqueda
 * de las líneas del presupuesto, FR-09, D-15) agrega `soloActivos=true`; en falso no lo envía, así el listado del
 * catálogo sigue pidiendo la misma URL de siempre.
 */
export const buscarArticulos = (
  texto: string,
  pagina: number,
  soloActivos = false,
): Promise<Resultado<PaginaArticulos>> => {
  const parametros = new URLSearchParams();
  if (texto !== '') {
    parametros.set(CAMPO_TEXTO, texto);
  }
  parametros.set('pagina', String(pagina));
  if (soloActivos) {
    parametros.set(CAMPO_SOLO_ACTIVOS, 'true');
  }
  return pedir({ metodo: 'GET', ruta: `/api/articulos?${parametros.toString()}` }, leerPagina);
};

/** Parámetro de `GET /api/articulos` que deja solo los artículos activos (D-15). */
export const CAMPO_SOLO_ACTIVOS = 'soloActivos';

/** D-24: número de ruta inválido o 404. */
export const MENSAJE_PRESUPUESTO_NO_ENCONTRADO = 'No se encontró el presupuesto.';

/** Campos de `errors` de `POST /api/presupuestos` (D-20): los seis del cliente y `lineas`; `lineas[i]` es la línea i. */
export const CAMPO_APELLIDO = 'apellido';
export const CAMPO_NOMBRE = 'nombre';
export const CAMPO_DNI = 'dni';
export const CAMPO_DOMICILIO = 'domicilio';
export const CAMPO_EMAIL = 'email';
export const CAMPO_TELEFONO = 'telefono';
export const CAMPO_LINEAS = 'lineas';
/** Los campos de la línea a enviar (FEAT-003b, D-03): la cantidad y el descuento, además del código del artículo. */
export const CAMPO_CODIGO_ARTICULO = 'codigoArticulo';
export const CAMPO_CANTIDAD = 'cantidad';
export const CAMPO_DESCUENTO = 'porcentajeDescuento';
/** El campo de `errors` de la cantidad de la línea i (D-06): `lineas[i].cantidad`. */
export const campoCantidad = (indice: number) => `${CAMPO_LINEAS}[${indice}].${CAMPO_CANTIDAD}`;
/** El campo de `errors` del descuento de la línea i (D-06): `lineas[i].porcentajeDescuento`. */
export const campoDescuento = (indice: number) => `${CAMPO_LINEAS}[${indice}].${CAMPO_DESCUENTO}`;
/** El `orden` de la línea grabada en la modificación (FEAT-003c, D-03), y su campo de `errors`: `lineas[i].orden`. */
export const CAMPO_ORDEN = 'orden';
export const campoOrden = (indice: number) => `${CAMPO_LINEAS}[${indice}].${CAMPO_ORDEN}`;
/** El campo del cuerpo y de `errors` de `PUT /api/presupuestos/{numero}/estado` (FEAT-003c). */
export const CAMPO_ESTADO = 'estado';

/**
 * El cliente que se graba (y el que devuelve la Api). Datos personales (Ley 25.326, D-17): viajan solo en el cuerpo
 * JSON, nunca en la URL, en el almacenamiento del navegador ni en la consola. Los opcionales vacíos van como `null`.
 */
export interface DatosCliente {
  apellido: string;
  nombre: string;
  dni: string;
  domicilio: string | null;
  email: string | null;
  telefono: string | null;
}

/** Los dos estados del presupuesto en el contrato de la Api (D-19, D-32). */
export const ESTADOS_PRESUPUESTO = ['borrador', 'final'] as const;
export type EstadoPresupuesto = (typeof ESTADOS_PRESUPUESTO)[number];

/** Una línea grabada con las siete columnas de FR-13; los importes son números JSON (D-33). */
export interface LineaPresupuesto {
  codigoArticulo: number;
  descripcion: string;
  precioUnitario: number;
  cantidad: number;
  porcentajeDescuento: number;
  precioConDescuento: number;
  precioFinal: number;
  /** La posición de la línea + 1 (FEAT-003c, D-03): identifica la línea grabada en una modificación. */
  orden: number;
}

/**
 * Lo que devuelven `POST /api/presupuestos` (201), `GET /api/presupuestos/{numero}` (200) y los dos `PUT` de
 * FEAT-003c (200).
 */
export interface PresupuestoGrabado {
  numero: number;
  /** `yyyy-MM-dd`, la fecha local del día en que se grabó (D-06). */
  fecha: string;
  estado: EstadoPresupuesto;
  cliente: DatosCliente;
  lineas: LineaPresupuesto[];
  /** La suma de los Precios Finales, con IVA incluido (FR-06); la calcula el servidor y no se guarda (D-05). */
  total: number;
  /** Entero de 0 a 2^53 − 1 (FEAT-003c, D-01): la modificación y el paso a Final la envían de vuelta. */
  version: number;
}

/**
 * Una línea a grabar (D-03): el código del artículo, la Cantidad y el Porcentaje de Descuento. Nunca precios ni
 * descripción: los calcula y los copia el servidor (R-01). En una modificación, la línea ya grabada lleva su `orden`
 * y la nueva no lo lleva (FEAT-003c, D-03); el alta nunca lo envía.
 */
export interface LineaAGrabar {
  orden?: number;
  codigoArticulo: number;
  cantidad: number;
  porcentajeDescuento: number;
}

/** El resultado de grabar un presupuesto: como {@link ResultadoAlta}, un 400 trae todos los campos inválidos. */
export type ResultadoAltaPresupuesto = Resultado<PresupuestoGrabado> | { tipo: 'campos'; errores: ErrorDeCampo[] };

const esTextoONulo = (valor: unknown): valor is string | null => valor === null || typeof valor === 'string';

const esEstadoPresupuesto = (valor: unknown): valor is EstadoPresupuesto =>
  ESTADOS_PRESUPUESTO.some((estado) => estado === valor);

/** El cliente: solo los seis campos; un dato ausente o de otro tipo hace ilegible la respuesta. */
const leerCliente: Lector<DatosCliente> = (cuerpo) =>
  esObjeto(cuerpo) &&
  typeof cuerpo.apellido === 'string' &&
  typeof cuerpo.nombre === 'string' &&
  typeof cuerpo.dni === 'string' &&
  esTextoONulo(cuerpo.domicilio) &&
  esTextoONulo(cuerpo.email) &&
  esTextoONulo(cuerpo.telefono)
    ? {
        apellido: cuerpo.apellido,
        nombre: cuerpo.nombre,
        dni: cuerpo.dni,
        domicilio: cuerpo.domicilio,
        email: cuerpo.email,
        telefono: cuerpo.telefono,
      }
    : undefined;

/**
 * Una línea: las siete columnas, con los importes dentro de {@link IMPORTE_MAXIMO_SEGURO}, y su `orden`, un entero
 * de 1 en adelante (FEAT-003c); sin él, la línea es ilegible.
 */
const leerLinea: Lector<LineaPresupuesto> = (cuerpo) =>
  esObjeto(cuerpo) &&
  esEnteroEntre(cuerpo.codigoArticulo, 1) &&
  typeof cuerpo.descripcion === 'string' &&
  esImporte(cuerpo.precioUnitario) &&
  typeof cuerpo.cantidad === 'number' &&
  Number.isSafeInteger(cuerpo.cantidad) &&
  esNumero(cuerpo.porcentajeDescuento) &&
  esImporte(cuerpo.precioConDescuento) &&
  esImporte(cuerpo.precioFinal) &&
  esEnteroEntre(cuerpo.orden, 1)
    ? {
        codigoArticulo: cuerpo.codigoArticulo,
        descripcion: cuerpo.descripcion,
        precioUnitario: cuerpo.precioUnitario,
        cantidad: cuerpo.cantidad,
        porcentajeDescuento: cuerpo.porcentajeDescuento,
        precioConDescuento: cuerpo.precioConDescuento,
        precioFinal: cuerpo.precioFinal,
        orden: cuerpo.orden,
      }
    : undefined;

/**
 * Un presupuesto grabado: número entero positivo seguro, fecha `yyyy-MM-dd`, estado `borrador` o `final`, los datos
 * del cliente, las líneas y el total. Cualquier parte ilegible hace ilegible la respuesta entera: la pantalla no
 * muestra importes ni datos falsos. El total tiene que ser la suma de los Precios Finales recibidos, comparada en
 * centésimos con {@link sumarCentesimos} y nunca como suma de flotantes (D-08): 0,1 + 0,2 es 0,3. Los importes se leen
 * como números JSON, con o sin ceros finales (900 o 900.00; D-14b). La versión (FEAT-003c) es obligatoria: sin ella
 * la pantalla no podría modificar ni pasar a Final, así que su falta hace ilegible la respuesta.
 */
const leerPresupuesto: Lector<PresupuestoGrabado> = (cuerpo) => {
  if (
    !esObjeto(cuerpo) ||
    !esEnteroEntre(cuerpo.numero, 1) ||
    !esFecha(cuerpo.fecha) ||
    !esEstadoPresupuesto(cuerpo.estado) ||
    !Array.isArray(cuerpo.lineas) ||
    !esImporte(cuerpo.total) ||
    !esVersion(cuerpo.version)
  ) {
    return undefined;
  }
  const cliente = leerCliente(cuerpo.cliente);
  if (cliente === undefined) {
    return undefined;
  }
  const lineas: LineaPresupuesto[] = [];
  for (const elemento of cuerpo.lineas as unknown[]) {
    const linea = leerLinea(elemento);
    if (linea === undefined) {
      return undefined;
    }
    lineas.push(linea);
  }
  const total = cuerpo.total;
  if (sumarCentesimos([total]) !== sumarCentesimos(lineas.map((linea) => linea.precioFinal))) {
    return undefined;
  }
  return {
    numero: cuerpo.numero,
    fecha: cuerpo.fecha,
    estado: cuerpo.estado,
    cliente,
    lineas,
    total,
    version: cuerpo.version,
  };
};

/** Por línea, el código, la cantidad y el descuento (D-03); con `conOrden`, también el `orden` de la grabada. */
function lineasDelCuerpo(lineas: readonly LineaAGrabar[], conOrden: boolean) {
  return lineas.map((linea) => ({
    ...(conOrden && linea.orden !== undefined ? { [CAMPO_ORDEN]: linea.orden } : {}),
    [CAMPO_CODIGO_ARTICULO]: linea.codigoArticulo,
    [CAMPO_CANTIDAD]: linea.cantidad,
    [CAMPO_DESCUENTO]: linea.porcentajeDescuento,
  }));
}

/**
 * Graba un presupuesto nuevo en Borrador (FR-01, FR-08) con el cliente y, por cada línea en orden, el código del
 * artículo, la Cantidad y el Porcentaje de Descuento (D-03), y nada más: el servidor copia la descripción y el precio
 * del catálogo y calcula los importes, así el Precio Unitario no se puede modificar (FR-11) ni el pedido fija precios
 * (R-01). Un 400 devuelve TODOS los campos de `errors` (D-20), incluidos `lineas[i].cantidad` y
 * `lineas[i].porcentajeDescuento` (D-06), y un 413 es el mensaje general del catálogo.
 */
export const grabarPresupuesto = (
  cliente: DatosCliente,
  lineas: readonly LineaAGrabar[],
): Promise<ResultadoAltaPresupuesto> =>
  grabarConCampos(
    {
      metodo: 'POST',
      ruta: '/api/presupuestos',
      cuerpo: { cliente, lineas: lineasDelCuerpo(lineas, false) },
      mensaje413: MENSAJE_ARTICULO_GRANDE,
    },
    leerPresupuesto,
  );

/** La factura emitida desde un presupuesto, como la informa su GET (FEAT-007, D-19): solo el comprobante. */
export interface FacturaDelPresupuesto {
  /** "Factura B 0003-00000125": tipo, punto de venta de 4 o 5 dígitos y número de 8 (AC-19). */
  comprobante: string;
}

/** Los motivos de la observación de una emisión pendiente (FEAT-008, FR-08, FR-14, FR-15). */
export const MOTIVOS_OBSERVACION = ['importe_distinto', 'sin_numero', 'otro_entorno'] as const;
export type MotivoObservacion = (typeof MOTIVOS_OBSERVACION)[number];

/** Los entornos de ARCA como los informa la Api: en minúscula y sin tildes (D-33). */
export const ENTORNOS_ARCA = ['simulado', 'homologacion', 'produccion'] as const;
export type EntornoArca = (typeof ENTORNOS_ARCA)[number];

/** El nombre legible de cada entorno de ARCA, para los textos de la pantalla. */
const TEXTO_ENTORNO: Record<EntornoArca, string> = {
  simulado: 'Simulado',
  homologacion: 'Homologación',
  produccion: 'Producción',
};

/** Si un valor (por ejemplo, el `motivo` de un 409) es uno de los motivos de la observación. */
export const esMotivoObservacion = (valor: unknown): valor is MotivoObservacion =>
  MOTIVOS_OBSERVACION.some((motivo) => motivo === valor);

/** Los datos que comparten los tres motivos de la observación. */
interface DatosObservacion {
  /** El total que registró la venta. */
  importeRegistrado: number;
  /** El entorno de ARCA al que se envió la emisión. */
  entorno: EntornoArca;
}

/**
 * La observación vigente de una emisión pendiente, como la informa el GET del presupuesto (FEAT-008): por qué no se
 * recuperó ni se emitió y, con ella, que se puede descartar (FR-09). No lleva datos del cliente. Unión discriminada
 * por `motivo`: un importe distinto siempre tiene el comprobante ("Factura B 0003-00000125") y el total con que ARCA
 * lo tiene autorizado; en los otros dos motivos el comprobante puede faltar (sin número) y el importe de ARCA no se
 * conoce.
 */
export type ObservacionEmision =
  | (DatosObservacion & { motivo: 'importe_distinto'; comprobante: string; importeArca: number })
  | (DatosObservacion & { motivo: 'sin_numero' | 'otro_entorno'; comprobante: string | null; importeArca: number | null });

/**
 * Lo que devuelve `GET /api/presupuestos/{numero}`: el presupuesto grabado más su estado de facturación (FEAT-007,
 * FR-20, FR-24, D-19) y la observación de la emisión pendiente (FEAT-008). El alta y los PUT no llevan estos datos.
 */
export interface PresupuestoConsultado extends PresupuestoGrabado {
  /** La factura emitida desde el presupuesto, o `null`. */
  factura: FacturaDelPresupuesto | null;
  /** Si hay una emisión pendiente de verificación (FR-24): mientras la haya, no se factura; se verifica (FEAT-008). */
  emisionPendiente: boolean;
  /** La observación vigente de la emisión pendiente (FEAT-008), o `null`: con ella se ofrece descartar. */
  observacionEmision: ObservacionEmision | null;
}

/** El texto que pide revisar el punto de venta: el mismo comienzo para los tres motivos de la observación. */
const REVISAR_EN_ARCA = 'Revisá el punto de venta en ARCA:';

/** FR-08, AC-07: el comprobante y los dos importes, en pesos. */
const mensajeImporteDistinto = (comprobante: string, importeRegistrado: number, importeArca: number) =>
  `${REVISAR_EN_ARCA} la ${comprobante} figura autorizada por $ ${mostrarImporte(importeArca)} y esta venta ` +
  `registró $ ${mostrarImporte(importeRegistrado)}.`;

/** FR-14, AC-15. */
export const MENSAJE_EMISION_SIN_NUMERO = `${REVISAR_EN_ARCA} esta emisión no tiene número de comprobante registrado.`;

/** FR-15, AC-16: el entorno de la emisión, con su nombre legible. */
const mensajeOtroEntorno = (entorno: EntornoArca) =>
  `${REVISAR_EN_ARCA} esta emisión se envió al entorno ${TEXTO_ENTORNO[entorno]} y el sistema está configurado en ` +
  'otro entorno.';

/**
 * El texto de una observación (FR-08, FR-14, FR-15): el mismo que muestra la verificación que la dejó, para que el
 * presupuesto lo siga explicando al volver a abrirlo.
 */
export function mensajeObservacion(observacion: ObservacionEmision): string {
  switch (observacion.motivo) {
    case 'importe_distinto':
      return mensajeImporteDistinto(observacion.comprobante, observacion.importeRegistrado, observacion.importeArca);
    case 'sin_numero':
      return MENSAJE_EMISION_SIN_NUMERO;
    case 'otro_entorno':
      return mensajeOtroEntorno(observacion.entorno);
  }
}

/**
 * El comprobante de una factura: "Factura", la letra B o C, el punto de venta de 4 o 5 dígitos (1 a 99999, con ceros a
 * la izquierda) y el número de 8 (1 a 99999999). Así lo arma la Api (D-19); cualquier otro texto, incluidos un punto de
 * venta o un número todo ceros, hace ilegible la respuesta.
 */
const FORMATO_COMPROBANTE = /^Factura ([BC]) (?!0+-)(\d{4,5})-(?!0{8}$)(\d{8})$/;

const esComprobante = (valor: unknown): valor is string =>
  typeof valor === 'string' && FORMATO_COMPROBANTE.test(valor);

/** La factura del GET: `null` o un objeto con el comprobante; solo copia el comprobante. */
const leerFacturaDelPresupuesto: Lector<FacturaDelPresupuesto | null> = (cuerpo) => {
  if (cuerpo === null) {
    return null;
  }
  return esObjeto(cuerpo) && esComprobante(cuerpo.comprobante) ? { comprobante: cuerpo.comprobante } : undefined;
};

const esEntornoArca = (valor: unknown): valor is EntornoArca => ENTORNOS_ARCA.some((entorno) => entorno === valor);

/**
 * La observación del GET (FEAT-008): `null` o los cinco datos, y solo ellos. El motivo y el entorno son uno de los
 * previstos; el comprobante, `null` o con el formato de D-19; los importes, dentro del límite del navegador. Un importe
 * distinto exige el comprobante y el importe de ARCA (sin ellos no hay qué mostrar). Cualquier otra forma, ilegible.
 */
const leerObservacion: Lector<ObservacionEmision | null> = (cuerpo) => {
  if (cuerpo === null) {
    return null;
  }
  if (
    !esObjeto(cuerpo) ||
    !esMotivoObservacion(cuerpo.motivo) ||
    !(cuerpo.comprobante === null || esComprobante(cuerpo.comprobante)) ||
    !esImporte(cuerpo.importeRegistrado) ||
    !(cuerpo.importeArca === null || esImporte(cuerpo.importeArca)) ||
    !esEntornoArca(cuerpo.entorno)
  ) {
    return undefined;
  }
  const { comprobante, importeRegistrado, importeArca, entorno } = cuerpo;
  if (cuerpo.motivo === 'importe_distinto') {
    return comprobante === null || importeArca === null
      ? undefined
      : { motivo: 'importe_distinto', comprobante, importeRegistrado, importeArca, entorno };
  }
  return { motivo: cuerpo.motivo, comprobante, importeRegistrado, importeArca, entorno };
};

/**
 * El GET de un presupuesto: lo que lee {@link leerPresupuesto} más `factura`, `emisionPendiente` y
 * `observacionEmision` (FEAT-008), obligatorios. Sin ellos, o con otra forma, la respuesta es ilegible: la pantalla no
 * puede decidir si ofrece Facturar, Verificar o Descartar. Una observación sin emisión pendiente también es ilegible.
 */
const leerPresupuestoConsultado: Lector<PresupuestoConsultado> = (cuerpo) => {
  const presupuesto = leerPresupuesto(cuerpo);
  if (presupuesto === undefined || !esObjeto(cuerpo) || typeof cuerpo.emisionPendiente !== 'boolean') {
    return undefined;
  }
  const factura = leerFacturaDelPresupuesto(cuerpo.factura);
  const observacionEmision = leerObservacion(cuerpo.observacionEmision);
  if (
    factura === undefined ||
    observacionEmision === undefined ||
    (observacionEmision !== null && !cuerpo.emisionPendiente)
  ) {
    return undefined;
  }
  return { ...presupuesto, factura, emisionPendiente: cuerpo.emisionPendiente, observacionEmision };
};

/**
 * Un presupuesto por su número, con su factura y su emisión pendiente (FEAT-007). Un número que no es un entero
 * positivo seguro no se consulta (no puede existir); ese caso y un 404 son el mensaje general
 * {@link MENSAJE_PRESUPUESTO_NO_ENCONTRADO}.
 */
export const consultarPresupuesto = (numero: number): Promise<Resultado<PresupuestoConsultado>> =>
  esEnteroEntre(numero, 1)
    ? pedir(
        { metodo: 'GET', ruta: `/api/presupuestos/${numero}`, mensaje404: MENSAJE_PRESUPUESTO_NO_ENCONTRADO },
        leerPresupuestoConsultado,
      )
    : Promise.resolve({ tipo: 'general', mensaje: MENSAJE_PRESUPUESTO_NO_ENCONTRADO });

/** Los textos de los 409 del presupuesto según su `motivo` (D-07): nunca el `detail` del servidor. */
const MOTIVOS_409_PRESUPUESTO: Partial<Record<MotivoConflicto, string>> = {
  cambiado: MENSAJE_PRESUPUESTO_CAMBIADO,
  final: MENSAJE_PRESUPUESTO_FINAL,
};

/**
 * Modifica un presupuesto en Borrador (FEAT-003c, FR-02, FR-03) con la versión que leyó la pantalla (D-01) y devuelve
 * lo grabado, releído, con la versión nueva. Cada línea grabada va con su `orden` y cada línea nueva sin él (D-03);
 * nunca precios ni descripción. Como {@link grabarPresupuesto}: un 400 trae todos los campos (incluidos `version` y
 * `lineas[i].orden`) y un 413 es el mensaje general del catálogo. Un 409 trae su motivo, `cambiado` o `final`, con el
 * texto propio (D-07); un 404, "No se encontró el presupuesto.". Un número que no puede existir no se envía.
 *
 * Datos personales (D-17): viajan solo en el cuerpo JSON.
 */
export const modificarPresupuesto = (
  numero: number,
  cliente: DatosCliente,
  lineas: readonly LineaAGrabar[],
  version: number,
): Promise<ResultadoAltaPresupuesto> =>
  esEnteroEntre(numero, 1)
    ? grabarConCampos(
        {
          metodo: 'PUT',
          ruta: `/api/presupuestos/${numero}`,
          cuerpo: { [CAMPO_VERSION]: version, cliente, lineas: lineasDelCuerpo(lineas, true) },
          motivos409: MOTIVOS_409_PRESUPUESTO,
          mensaje404: MENSAJE_PRESUPUESTO_NO_ENCONTRADO,
          mensaje413: MENSAJE_ARTICULO_GRANDE,
        },
        leerPresupuesto,
      )
    : Promise.resolve({ tipo: 'general', mensaje: MENSAJE_PRESUPUESTO_NO_ENCONTRADO });

/**
 * Pasa un presupuesto en Borrador a Final (FEAT-003c, FR-04, FR-05) con la versión que muestra la pantalla (D-01) y
 * devuelve el presupuesto releído. Un 409 trae su motivo, `cambiado` o `final`, con el texto propio (D-07). El cuerpo
 * es fijo, así que un 400 o un 413 son el mensaje general de pedido rechazado; un 404, "No se encontró el
 * presupuesto.". Un número que no puede existir no se envía.
 */
export const pasarPresupuestoAFinal = (numero: number, version: number): Promise<Resultado<PresupuestoGrabado>> =>
  esEnteroEntre(numero, 1)
    ? pedir(
        {
          metodo: 'PUT',
          ruta: `/api/presupuestos/${numero}/estado`,
          cuerpo: { [CAMPO_ESTADO]: 'final', [CAMPO_VERSION]: version },
          motivos409: MOTIVOS_409_PRESUPUESTO,
          mensaje404: MENSAJE_PRESUPUESTO_NO_ENCONTRADO,
          mensaje413: MENSAJE_PEDIDO_RECHAZADO,
        },
        leerPresupuesto,
      ).then((resultado) =>
        // Un 400 del cuerpo fijo no tiene un campo en la pantalla.
        resultado.tipo === 'campo' ? { tipo: 'general', mensaje: MENSAJE_PEDIDO_RECHAZADO } : resultado,
      )
    : Promise.resolve({ tipo: 'general', mensaje: MENSAJE_PRESUPUESTO_NO_ENCONTRADO });

/** El tipo de contenido del PDF del presupuesto (con o sin parámetros, como `; charset`). */
const TIPO_PDF = /^application\/pdf\s*(;|$)/i;

/**
 * El PDF de un presupuesto en Final (FEAT-006, FR-11, FR-12) con `GET /api/presupuestos/{numero}/pdf`: los bytes tal
 * cual, como `Blob`, para que la pantalla los descargue con una URL de objeto (D-10). La respuesta exitosa se lee con
 * `blob()` y no con `leerJson`; los errores pasan por la misma traducción que los demás pedidos: un 401 sin cuerpo avisa
 * a {@link alPerderLaSesion}; un 409 `borrador` (D-11) es un conflicto con
 * {@link MENSAJE_PRESUPUESTO_BORRADOR_PDF}, y cualquier otro motivo, una respuesta ilegible; un 404, "No se encontró el
 * presupuesto.". Una falla de red, un 5xx, una respuesta exitosa que no es `application/pdf` o un cuerpo que no se
 * pudo leer son {@link MENSAJE_SIN_CONEXION}. Un número que no puede existir no se envía.
 */
export const descargarPdfPresupuesto = (numero: number): Promise<Resultado<Blob>> =>
  descargarPdf(
    numero,
    `/api/presupuestos/${numero}/pdf`,
    { borrador: MENSAJE_PRESUPUESTO_BORRADOR_PDF },
    MENSAJE_PRESUPUESTO_NO_ENCONTRADO,
  );

/**
 * Un PDF por `GET` (el del presupuesto, FEAT-006, y el de la factura, FEAT-009): los bytes como `Blob`. Los 409 se leen
 * con los motivos que admite la operación; un 404 y un número que no puede existir (no se envía) son
 * `mensajeNoEncontrado`. Una respuesta exitosa que no es `application/pdf`, o cuyo cuerpo no se pudo leer, es
 * {@link MENSAJE_SIN_CONEXION}.
 */
async function descargarPdf(
  numero: number,
  ruta: string,
  motivos409: Partial<Record<MotivoConflicto, string>>,
  mensajeNoEncontrado: string,
): Promise<Resultado<Blob>> {
  if (!esEnteroEntre(numero, 1)) {
    return { tipo: 'general', mensaje: mensajeNoEncontrado };
  }
  const respuesta = await enviar({ metodo: 'GET', ruta, motivos409, mensaje404: mensajeNoEncontrado });
  if (respuesta.tipo !== 'respuesta') {
    return respuesta;
  }
  if (!TIPO_PDF.test(respuesta.respuesta.headers.get('Content-Type') ?? '')) {
    return { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION };
  }
  try {
    return { tipo: 'ok', datos: await respuesta.respuesta.blob() };
  } catch {
    return { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION };
  }
}

/*
 * Emisión de la factura a consumidor final (FEAT-007, D-18).
 */

/** Los dos conceptos de la factura en el contrato de la Api (FR-08). */
export const CONCEPTOS_FACTURA = ['productos', 'productos_y_servicios'] as const;
export type ConceptoFactura = (typeof CONCEPTOS_FACTURA)[number];

/**
 * Textos propios de los 409 de la emisión según su `motivo` (D-61): nunca el `detail` del servidor. El del rechazo de
 * ARCA lo arma {@link mensajeRechazoArca} con el código y la descripción que informó ARCA (AC-20).
 */
export const MENSAJE_FACTURA_BORRADOR = 'El presupuesto está en Borrador: pasalo a Final para facturarlo.';
export const MENSAJE_FACTURA_FACTURADO = 'El presupuesto ya tiene una factura.';
export const MENSAJE_FACTURA_PENDIENTE = 'La emisión anterior está pendiente de verificación.';
export const MENSAJE_FACTURA_SIN_TOPE = 'Cargá el tope de identificación en la configuración para facturar.';
export const MENSAJE_FACTURA_SIN_PUNTO_VENTA = 'Cargá el punto de venta en la configuración para facturar.';
export const MENSAJE_FACTURA_ALICUOTA_NO_ADMITIDA = 'Elegí una alícuota de IVA válida en la configuración para facturar.';
export const MENSAJE_FACTURA_SIN_RESPUESTA = 'ARCA no respondió. La emisión quedó pendiente de verificación.';
export const MENSAJE_FACTURA_NO_ENVIADA = 'No se pudo enviar la factura a ARCA. Intentá de nuevo en unos minutos.';
export const MENSAJE_FACTURA_PENDIENTE_VERIFICACION = 'La factura quedó pendiente de verificación.';

/** La confirmación de una factura autorizada (FR-19, AC-18): "Se emitió la Factura B 0003-00000125.". */
export const mensajeFacturaEmitida = (comprobante: string) => `Se emitió la ${comprobante}.`;

/** Un rechazo de ARCA sin código (D-57): ni el "0" ni el texto fijo de la Api, que repetiría la frase. */
export const MENSAJE_FACTURA_RECHAZADA_SIN_MOTIVO = 'ARCA rechazó la factura sin informar el motivo.';

/**
 * El largo máximo de la descripción de un rechazo de ARCA en la respuesta: el mismo que
 * `RespuestasValidacion.DescripcionArcaMaxima` de la Api, que la recorta a ese largo.
 */
const DESCRIPCION_ARCA_MAXIMA = 500;

/**
 * El texto de un rechazo de ARCA (FR-21, AC-20): "ARCA rechazó la factura: código – descripción". El código 0 es el
 * que usa la Api cuando ARCA rechazó sin informar un código ni un motivo (D-57): entonces el texto es solo
 * {@link MENSAJE_FACTURA_RECHAZADA_SIN_MOTIVO}, porque "0" haría creer que ARCA informó ese código. La descripción la
 * recorta la Api y la pantalla la muestra como texto, nunca como HTML. Un código que no es un entero de 0 en adelante,
 * o una descripción vacía, que no es texto o de más de 500 caracteres, hacen ilegible la respuesta.
 */
function mensajeRechazoArca(cuerpo: Record<string, unknown>): string | undefined {
  return textoRechazoArca(cuerpo.codigo, cuerpo.descripcion);
}

/**
 * El texto de un rechazo de ARCA con su código y su descripción, como lo arma {@link mensajeRechazoArca}; `undefined`
 * si alguno es ilegible. Lo comparten la emisión (`codigo`, `descripcion`) y la verificación (`codigoArca`,
 * `descripcionArca`, FEAT-008). La descripción solo se muestra: no se guarda.
 */
function textoRechazoArca(codigo: unknown, descripcion: unknown): string | undefined {
  if (
    !esEnteroEntre(codigo, 0) ||
    typeof descripcion !== 'string' ||
    descripcion.length === 0 ||
    descripcion.length > DESCRIPCION_ARCA_MAXIMA
  ) {
    return undefined;
  }
  return codigo === 0 ? MENSAJE_FACTURA_RECHAZADA_SIN_MOTIVO : `ARCA rechazó la factura: ${codigo} – ${descripcion}`;
}

/** Los textos de los 409 de la emisión, uno por motivo (D-18, D-31, D-61). */
const MOTIVOS_409_FACTURA: Partial<Record<MotivoConflicto, TextoDelMotivo>> = {
  borrador: MENSAJE_FACTURA_BORRADOR,
  facturado: MENSAJE_FACTURA_FACTURADO,
  pendiente: MENSAJE_FACTURA_PENDIENTE,
  sin_tope: MENSAJE_FACTURA_SIN_TOPE,
  sin_punto_venta: MENSAJE_FACTURA_SIN_PUNTO_VENTA,
  alicuota_no_admitida: MENSAJE_FACTURA_ALICUOTA_NO_ADMITIDA,
  rechazada: mensajeRechazoArca,
  sin_respuesta: MENSAJE_FACTURA_SIN_RESPUESTA,
  no_enviada: MENSAJE_FACTURA_NO_ENVIADA,
  pendiente_verificacion: MENSAJE_FACTURA_PENDIENTE_VERIFICACION,
};

/** Lo que devuelve `POST /api/presupuestos/{numero}/factura` (201, D-18). */
export interface FacturaEmitida {
  /** "Factura B 0003-00000125". */
  comprobante: string;
  tipo: 'b' | 'c';
  numero: number;
  /** Los 14 dígitos del CAE. */
  cae: string;
  /** `yyyy-MM-dd`. */
  vencimientoCae: string;
  /** `yyyy-MM-dd`. */
  fecha: string;
  total: number;
  neto: number;
  iva: number;
}

const FORMATO_CAE = /^\d{14}$/;

/**
 * La respuesta 201 de la emisión: todos los datos, con el comprobante en el formato de D-19 y coherente con el tipo y
 * el número informados, el CAE de 14 dígitos, fechas `yyyy-MM-dd` que existen e importes dentro del límite del
 * navegador. Cualquier dato ilegible hace ilegible la respuesta.
 */
const leerFacturaEmitida: Lector<FacturaEmitida> = (cuerpo) => {
  if (
    !esObjeto(cuerpo) ||
    typeof cuerpo.comprobante !== 'string' ||
    (cuerpo.tipo !== 'b' && cuerpo.tipo !== 'c') ||
    !esEnteroEntre(cuerpo.numero, 1) ||
    typeof cuerpo.cae !== 'string' ||
    !FORMATO_CAE.test(cuerpo.cae) ||
    !esFecha(cuerpo.vencimientoCae) ||
    !esFecha(cuerpo.fecha) ||
    !esImporte(cuerpo.total) ||
    !esImporte(cuerpo.neto) ||
    !esImporte(cuerpo.iva)
  ) {
    return undefined;
  }
  const partes = FORMATO_COMPROBANTE.exec(cuerpo.comprobante);
  if (partes === null || partes[1] !== cuerpo.tipo.toUpperCase() || Number(partes[3]) !== cuerpo.numero) {
    return undefined;
  }
  return {
    comprobante: cuerpo.comprobante,
    tipo: cuerpo.tipo,
    numero: cuerpo.numero,
    cae: cuerpo.cae,
    vencimientoCae: cuerpo.vencimientoCae,
    fecha: cuerpo.fecha,
    total: cuerpo.total,
    neto: cuerpo.neto,
    iva: cuerpo.iva,
  };
};

/**
 * Emite la factura a consumidor final de un presupuesto en Final (FEAT-007, FR-01, D-18) con
 * `POST /api/presupuestos/{numero}/factura`. Envía solo el concepto: tipo, importes, receptor y fecha los arma el
 * servidor (R-06). Un 409 trae su motivo con el texto propio de {@link MOTIVOS_409_FACTURA}; un motivo ajeno o un cuerpo
 * ilegible es {@link MENSAJE_SIN_CONEXION}. El cuerpo es fijo, así que un 400, un 413 o un 415 son el mensaje general de
 * pedido rechazado; un 404, "No se encontró el presupuesto."; un 401 sin cuerpo avisa a {@link alPerderLaSesion}. Un
 * número que no puede existir no se envía. No hay reintentos: una emisión repetida podría autorizar dos comprobantes
 * (FR-23).
 */
export const emitirFactura = (numero: number, concepto: ConceptoFactura): Promise<Resultado<FacturaEmitida>> =>
  esEnteroEntre(numero, 1)
    ? pedir(
        {
          metodo: 'POST',
          ruta: `/api/presupuestos/${numero}/factura`,
          cuerpo: { concepto },
          motivos409: MOTIVOS_409_FACTURA,
          mensaje404: MENSAJE_PRESUPUESTO_NO_ENCONTRADO,
          mensaje413: MENSAJE_PEDIDO_RECHAZADO,
        },
        leerFacturaEmitida,
      ).then((resultado) =>
        // Un 400 del cuerpo fijo no tiene un campo en la pantalla.
        resultado.tipo === 'campo' ? { tipo: 'general', mensaje: MENSAJE_PEDIDO_RECHAZADO } : resultado,
      )
    : Promise.resolve({ tipo: 'general', mensaje: MENSAJE_PRESUPUESTO_NO_ENCONTRADO });

/*
 * Verificación y descarte de una emisión pendiente, y lista de pendientes (FEAT-008, ADR-025, ADR-026).
 */

/** La confirmación de una factura recuperada (FR-05, AC-05). */
export const mensajeFacturaRecuperada = (comprobante: string) =>
  `Se recuperó la ${comprobante} que ARCA ya había autorizado.`;

/** FR-16, AC-17: los segundos que faltan para poder verificar. */
export const mensajeEspera = (segundos: number) =>
  `Esperá ${segundos} ${segundos === 1 ? 'segundo' : 'segundos'} para verificar con ARCA.`;

/** Textos propios de los 409 de la verificación y del descarte según su `motivo`: nunca el `detail` del servidor. */
export const MENSAJE_VERIFICACION_SIN_RESPUESTA = 'ARCA no respondió. La emisión sigue pendiente de verificación.';
export const MENSAJE_VERIFICACION_SIN_RESPUESTA_EMISION =
  'ARCA no respondió a la nueva emisión. La emisión quedó pendiente de verificación.';
export const MENSAJE_VERIFICACION_NO_ENVIADA =
  'No se pudo completar la verificación con ARCA. Intentá de nuevo en unos minutos.';
export const MENSAJE_SIN_EMISION_PENDIENTE = 'El presupuesto no tiene una emisión pendiente de verificación.';
export const MENSAJE_DESCARTE_SIN_OBSERVACION =
  'La emisión pendiente no tiene una observación que permita descartarla. Verificala con ARCA.';

/** Los textos de los 409 de la verificación (spec FEAT-008, Block 6 y Block 7); los que llevan datos los leen del cuerpo. */
const MOTIVOS_409_VERIFICACION: Partial<Record<MotivoConflicto, TextoDelMotivo>> = {
  sin_pendiente: MENSAJE_SIN_EMISION_PENDIENTE,
  espera: (cuerpo) =>
    esEnteroEntre(cuerpo.segundosRestantes, 1) ? mensajeEspera(cuerpo.segundosRestantes) : undefined,
  importe_distinto: (cuerpo) =>
    esComprobante(cuerpo.comprobante) && esImporte(cuerpo.importeRegistrado) && esImporte(cuerpo.importeArca)
      ? mensajeImporteDistinto(cuerpo.comprobante, cuerpo.importeRegistrado, cuerpo.importeArca)
      : undefined,
  sin_numero: MENSAJE_EMISION_SIN_NUMERO,
  otro_entorno: (cuerpo) => (esEntornoArca(cuerpo.entorno) ? mensajeOtroEntorno(cuerpo.entorno) : undefined),
  sin_respuesta: MENSAJE_VERIFICACION_SIN_RESPUESTA,
  sin_respuesta_emision: MENSAJE_VERIFICACION_SIN_RESPUESTA_EMISION,
  rechazada: (cuerpo) => textoRechazoArca(cuerpo.codigoArca, cuerpo.descripcionArca),
  no_enviada: MENSAJE_VERIFICACION_NO_ENVIADA,
  facturado: MENSAJE_FACTURA_FACTURADO,
};

/** Los textos de los 409 del descarte. */
const MOTIVOS_409_DESCARTE: Partial<Record<MotivoConflicto, TextoDelMotivo>> = {
  sin_pendiente: MENSAJE_SIN_EMISION_PENDIENTE,
  sin_observacion: MENSAJE_DESCARTE_SIN_OBSERVACION,
};

/** Lo que devuelve la verificación con 200: si la factura se recuperó (FR-05) o se emitió (FR-06), y la factura. */
export interface VerificacionEmision {
  resultado: 'recuperada' | 'emitida';
  factura: FacturaEmitida;
}

const leerVerificacion: Lector<VerificacionEmision> = (cuerpo) => {
  if (!esObjeto(cuerpo) || (cuerpo.resultado !== 'recuperada' && cuerpo.resultado !== 'emitida')) {
    return undefined;
  }
  const factura = leerFacturaEmitida(cuerpo.factura);
  return factura === undefined ? undefined : { resultado: cuerpo.resultado, factura };
};

/** Un POST sin cuerpo a una ruta de la emisión pendiente: `Content-Type: application/json` igual (D-33). */
const opcionesEmisionPendiente = (
  numero: number,
  accion: 'verificacion' | 'descarte',
  motivos409: Partial<Record<MotivoConflicto, TextoDelMotivo>>,
): Opciones => ({
  metodo: 'POST',
  ruta: `/api/presupuestos/${numero}/factura/${accion}`,
  motivos409,
  mensaje404: MENSAJE_PRESUPUESTO_NO_ENCONTRADO,
  mensaje413: MENSAJE_PEDIDO_RECHAZADO,
});

/** Un 400 de un pedido sin cuerpo no tiene un campo en la pantalla. */
const sinCampo = <T>(resultado: Resultado<T>): Resultado<T> =>
  resultado.tipo === 'campo' ? { tipo: 'general', mensaje: MENSAJE_PEDIDO_RECHAZADO } : resultado;

/**
 * Verifica con ARCA la emisión pendiente de un presupuesto (FEAT-008, FR-04 a FR-08, FR-12 a FR-16) con
 * `POST /api/presupuestos/{numero}/factura/verificacion`, sin cuerpo. Con 200, la factura recuperada o emitida. Un 409
 * trae su motivo con el texto propio de {@link MOTIVOS_409_VERIFICACION}, armado con los datos del cuerpo cuando los
 * lleva; un motivo ajeno o datos ilegibles son {@link MENSAJE_SIN_CONEXION}. La descripción de un rechazo de ARCA solo
 * va en el texto: no se guarda. Un 404, "No se encontró el presupuesto."; un 401 sin cuerpo avisa a
 * {@link alPerderLaSesion}. Sin reintentos (FR-12). Un número que no puede existir no se envía.
 */
export const verificarEmision = (numero: number): Promise<Resultado<VerificacionEmision>> =>
  esEnteroEntre(numero, 1)
    ? pedir(opcionesEmisionPendiente(numero, 'verificacion', MOTIVOS_409_VERIFICACION), leerVerificacion).then(sinCampo)
    : Promise.resolve({ tipo: 'general', mensaje: MENSAJE_PRESUPUESTO_NO_ENCONTRADO });

// ---- Estado del backup (FEAT-010, FR-09): GET /api/backup/estado ----

/** Si hay una ubicación de backup elegida. */
export type UbicacionEstadoBackup = 'elegida' | 'sin_elegir';

/**
 * El estado del backup para el aviso al ingresar: si falta el de ayer, la fecha del último (`yyyy-MM-dd`, o `null` si
 * no hay ninguno), si hay una ubicación elegida y si esa ubicación se puede leer. Nunca trae la ruta (D-12).
 */
export interface EstadoBackup {
  faltaAyer: boolean;
  ultimo: string | null;
  ubicacion: UbicacionEstadoBackup;
  accesible: boolean;
}

/**
 * Los cuatro campos del contrato, cada uno con su tipo; solo copia esos campos. Sin ubicación elegida no puede ser
 * accesible: esa combinación, como cualquier otra forma, es una respuesta ilegible.
 */
const leerEstadoBackup: Lector<EstadoBackup> = (cuerpo) => {
  if (
    !esObjeto(cuerpo) ||
    typeof cuerpo.faltaAyer !== 'boolean' ||
    !(cuerpo.ultimo === null || esFecha(cuerpo.ultimo)) ||
    (cuerpo.ubicacion !== 'elegida' && cuerpo.ubicacion !== 'sin_elegir') ||
    typeof cuerpo.accesible !== 'boolean' ||
    (cuerpo.ubicacion === 'sin_elegir' && cuerpo.accesible)
  ) {
    return undefined;
  }
  return { faltaAyer: cuerpo.faltaAyer, ultimo: cuerpo.ultimo, ubicacion: cuerpo.ubicacion, accesible: cuerpo.accesible };
};

/**
 * El estado del backup (FEAT-010, FR-09) con `GET /api/backup/estado`. Lo pide la pantalla de inicio una vez al cargar.
 */
export const consultarEstadoBackup = (): Promise<Resultado<EstadoBackup>> =>
  pedir({ metodo: 'GET', ruta: '/api/backup/estado' }, leerEstadoBackup);

/**
 * Descarta la emisión pendiente de un presupuesto con una observación vigente (FEAT-008, FR-09, FR-11) con
 * `POST /api/presupuestos/{numero}/factura/descarte`, sin cuerpo: 204 sin datos. Un 409 `sin_pendiente` o
 * `sin_observacion` trae su texto propio; los demás errores, como {@link verificarEmision}.
 */
export const descartarEmision = (numero: number): Promise<Resultado<void>> =>
  esEnteroEntre(numero, 1)
    ? pedirSinDatos(opcionesEmisionPendiente(numero, 'descarte', MOTIVOS_409_DESCARTE)).then(sinCampo)
    : Promise.resolve({ tipo: 'general', mensaje: MENSAJE_PRESUPUESTO_NO_ENCONTRADO });

/**
 * La lista de pendientes: un arreglo de números de presupuesto (enteros positivos, sin repetidos); cualquier otra
 * forma, ilegible.
 */
const leerEmisionesPendientes: Lector<number[]> = (cuerpo) =>
  esObjeto(cuerpo) &&
  Array.isArray(cuerpo.presupuestos) &&
  (cuerpo.presupuestos as unknown[]).every((numero) => esEnteroEntre(numero, 1)) &&
  // Un presupuesto tiene una sola emisión pendiente: un número repetido hace ilegible la lista.
  new Set(cuerpo.presupuestos).size === cuerpo.presupuestos.length
    ? [...(cuerpo.presupuestos as number[])]
    : undefined;

/**
 * Los presupuestos con una emisión pendiente de verificación (FEAT-008, FR-03) con
 * `GET /api/facturacion/emisiones-pendientes`: solo sus números. Lo pide la pantalla de inicio una vez al cargar.
 */
export const listarEmisionesPendientes = (): Promise<Resultado<number[]>> =>
  pedir({ metodo: 'GET', ruta: '/api/facturacion/emisiones-pendientes' }, leerEmisionesPendientes);

/*
 * Búsqueda de presupuestos (FEAT-006, ADR-019).
 */

/** El 413 de la búsqueda: un mensaje general propio, no en un campo (la pantalla limita los largos). */
export const MENSAJE_BUSQUEDA_GRANDE = 'La búsqueda es demasiado larga.';
/** Campos del cuerpo y de `errors` de `POST /api/presupuestos/busqueda` que no comparte con el alta. */
export const CAMPO_DESDE = 'desde';
export const CAMPO_HASTA = 'hasta';
export const CAMPO_PAGINA = 'pagina';

/**
 * Lo escrito en el formulario de la búsqueda: textos tal cual y fechas `yyyy-MM-dd` o vacías. Datos personales
 * (Ley 25.326): viajan solo en el cuerpo JSON, nunca en la URL, en el almacenamiento del navegador ni en la consola.
 */
export interface FiltroPresupuestos {
  apellido: string;
  nombre: string;
  dni: string;
  desde: string;
  hasta: string;
}

/** Un resultado de la búsqueda: los siete datos de FR-09, y nada más. */
export interface PresupuestoListado {
  numero: number;
  /** `yyyy-MM-dd`. */
  fecha: string;
  apellido: string;
  nombre: string;
  dni: string;
  estado: EstadoPresupuesto;
  total: number;
}

/** Una página de la búsqueda; una página posterior a la última trae la lista vacía y el total. */
export interface PaginaPresupuestos {
  presupuestos: PresupuestoListado[];
  pagina: number;
  tamanoPagina: number;
  total: number;
}

/** El resultado de buscar: como {@link ResultadoAlta}, un 400 trae todos los campos inválidos. */
export type ResultadoBusquedaPresupuestos = Resultado<PaginaPresupuestos> | { tipo: 'campos'; errores: ErrorDeCampo[] };

/** Un resultado: solo copia los siete datos; si la respuesta trajera otros datos del cliente, se descartan. */
const leerPresupuestoListado: Lector<PresupuestoListado> = (cuerpo) =>
  esObjeto(cuerpo) &&
  esEnteroEntre(cuerpo.numero, 1) &&
  esFecha(cuerpo.fecha) &&
  typeof cuerpo.apellido === 'string' &&
  typeof cuerpo.nombre === 'string' &&
  typeof cuerpo.dni === 'string' &&
  esEstadoPresupuesto(cuerpo.estado) &&
  esImporte(cuerpo.total)
    ? {
        numero: cuerpo.numero,
        fecha: cuerpo.fecha,
        apellido: cuerpo.apellido,
        nombre: cuerpo.nombre,
        dni: cuerpo.dni,
        estado: cuerpo.estado,
        total: cuerpo.total,
      }
    : undefined;

/** Una página de la búsqueda: cualquier resultado ilegible hace ilegible la página entera. */
const leerPaginaPresupuestos: Lector<PaginaPresupuestos> = (cuerpo) => {
  if (
    !esObjeto(cuerpo) ||
    !Array.isArray(cuerpo.presupuestos) ||
    !esEnteroEntre(cuerpo.pagina, 1) ||
    !esEnteroEntre(cuerpo.tamanoPagina, 1) ||
    !esEnteroEntre(cuerpo.total, 0)
  ) {
    return undefined;
  }
  const presupuestos: PresupuestoListado[] = [];
  for (const elemento of cuerpo.presupuestos as unknown[]) {
    const fila = leerPresupuestoListado(elemento);
    if (fila === undefined) {
      return undefined;
    }
    presupuestos.push(fila);
  }
  return { presupuestos, pagina: cuerpo.pagina, tamanoPagina: cuerpo.tamanoPagina, total: cuerpo.total };
};

/**
 * El cuerpo de la búsqueda: cada filtro recortado y solo si quedó cargado, y la página. Un campo vacío se omite: la Api
 * responde 400 a una fecha `""` (D-23), y un texto vacío no es un filtro.
 */
function cuerpoBusqueda(campos: readonly [string, string][], pagina: number): Record<string, string | number> {
  const cuerpo: Record<string, string | number> = {};
  for (const [campo, valor] of campos) {
    const recortado = valor.trim();
    if (recortado !== '') {
      cuerpo[campo] = recortado;
    }
  }
  cuerpo[CAMPO_PAGINA] = pagina;
  return cuerpo;
}

/**
 * Busca presupuestos (FEAT-006, FR-01 a FR-09) con `POST /api/presupuestos/busqueda`: los filtros viajan en el cuerpo
 * JSON y nunca en la URL (ADR-019, R-02). Un 400 devuelve TODOS los campos de `errors`; un 413, el mensaje general
 * {@link MENSAJE_BUSQUEDA_GRANDE}; un 401 sin cuerpo avisa a {@link alPerderLaSesion}; una falla de red, un 5xx o una
 * respuesta con forma inesperada, {@link MENSAJE_SIN_CONEXION}.
 */
export const buscarPresupuestos = (
  filtro: FiltroPresupuestos,
  pagina: number,
): Promise<ResultadoBusquedaPresupuestos> =>
  grabarConCampos(
    {
      metodo: 'POST',
      ruta: '/api/presupuestos/busqueda',
      cuerpo: cuerpoBusqueda(
        [
          [CAMPO_APELLIDO, filtro.apellido],
          [CAMPO_NOMBRE, filtro.nombre],
          [CAMPO_DNI, filtro.dni],
          [CAMPO_DESDE, filtro.desde],
          [CAMPO_HASTA, filtro.hasta],
        ],
        pagina,
      ),
      mensaje413: MENSAJE_BUSQUEDA_GRANDE,
    },
    leerPaginaPresupuestos,
  );

/*
 * Facturas: búsqueda, detalle y PDF (FEAT-009, FR-04, FR-05, FR-16 a FR-24).
 */

/** El campo del número de comprobante en el cuerpo y en `errors` de `POST /api/facturacion/busqueda`. */
export const CAMPO_NUMERO_COMPROBANTE = 'numero';

/** El 404 del detalle y del PDF de la factura (D-41): el mismo texto si el presupuesto no existe o no tiene factura. */
export const MENSAJE_FACTURA_NO_ENCONTRADA = 'No se encontró la factura.';
/** FR-04, AC-05: el 409 `sin_datos_emisor` del PDF de la factura, con el texto propio de la pantalla. */
export const MENSAJE_FACTURA_SIN_DATOS_EMISOR =
  'Completá los datos del emisor en la configuración para generar el PDF de la factura.';

/**
 * Lo escrito en el formulario de la búsqueda de facturas: textos tal cual y fechas `yyyy-MM-dd` o vacías. Datos
 * personales (Ley 25.326): viajan solo en el cuerpo JSON, nunca en la URL, en el almacenamiento ni en la consola.
 */
export interface FiltroFacturas {
  apellido: string;
  nombre: string;
  dni: string;
  /** El número de comprobante, por coincidencia parcial (FR-20). */
  numero: string;
  desde: string;
  hasta: string;
}

/** Un resultado de la búsqueda de facturas: los datos de FR-23, con el comprobante como texto (D-15), y nada más. */
export interface FacturaListada {
  /** El número del presupuesto de origen: identifica la factura en la web (D-14, D-28). */
  presupuesto: number;
  /** "Factura B 0003-00000125". */
  comprobante: string;
  /** `yyyy-MM-dd`. */
  fecha: string;
  apellido: string;
  nombre: string;
  dni: string;
  total: number;
}

/** Una página de la búsqueda de facturas; una página posterior a la última trae la lista vacía y el total. */
export interface PaginaFacturas {
  facturas: FacturaListada[];
  pagina: number;
  tamanoPagina: number;
  total: number;
}

/** El resultado de buscar facturas: un 400 trae todos los campos inválidos. */
export type ResultadoBusquedaFacturas = Resultado<PaginaFacturas> | { tipo: 'campos'; errores: ErrorDeCampo[] };

/** Una línea del detalle de la factura: descripción, cantidad e importe (el precio final de la línea). */
export interface LineaFactura {
  descripcion: string;
  cantidad: number;
  importe: number;
}

/**
 * El detalle de la factura de un presupuesto (FR-24): el comprobante como texto (D-15), el presupuesto de origen, el
 * cliente del presupuesto (FR-25), el receptor como lo dice la factura ("Consumidor Final" o "DNI 00123456"), las
 * líneas, el total, el IVA contenido (`null` en la Factura C), el CAE con su vencimiento y si es simulada.
 */
export interface FacturaDetalle {
  comprobante: string;
  /** `yyyy-MM-dd`. */
  fecha: string;
  presupuesto: number;
  apellido: string;
  nombre: string;
  dni: string;
  receptor: string;
  lineas: LineaFactura[];
  total: number;
  ivaContenido: number | null;
  cae: string;
  /** `yyyy-MM-dd`. */
  vencimientoCae: string;
  simulada: boolean;
}

/** Un resultado: solo copia los siete datos de FR-23; si la respuesta trajera otros datos del cliente, se descartan. */
const leerFacturaListada: Lector<FacturaListada> = (cuerpo) =>
  esObjeto(cuerpo) &&
  esEnteroEntre(cuerpo.presupuesto, 1) &&
  esComprobante(cuerpo.comprobante) &&
  esFecha(cuerpo.fecha) &&
  typeof cuerpo.apellido === 'string' &&
  typeof cuerpo.nombre === 'string' &&
  typeof cuerpo.dni === 'string' &&
  esImporte(cuerpo.total)
    ? {
        presupuesto: cuerpo.presupuesto,
        comprobante: cuerpo.comprobante,
        fecha: cuerpo.fecha,
        apellido: cuerpo.apellido,
        nombre: cuerpo.nombre,
        dni: cuerpo.dni,
        total: cuerpo.total,
      }
    : undefined;

/** Una lista en la que cada elemento se lee con `leer`: cualquier elemento ilegible hace ilegible la lista. */
function leerLista<T>(valor: unknown, leer: Lector<T>): T[] | undefined {
  if (!Array.isArray(valor)) {
    return undefined;
  }
  const leidos: T[] = [];
  for (const elemento of valor as unknown[]) {
    const leido = leer(elemento);
    if (leido === undefined) {
      return undefined;
    }
    leidos.push(leido);
  }
  return leidos;
}

/** Una página de la búsqueda de facturas: cualquier resultado ilegible hace ilegible la página entera. */
const leerPaginaFacturas: Lector<PaginaFacturas> = (cuerpo) => {
  if (
    !esObjeto(cuerpo) ||
    !esEnteroEntre(cuerpo.pagina, 1) ||
    !esEnteroEntre(cuerpo.tamanoPagina, 1) ||
    !esEnteroEntre(cuerpo.total, 0)
  ) {
    return undefined;
  }
  const facturas = leerLista(cuerpo.facturas, leerFacturaListada);
  return facturas === undefined
    ? undefined
    : { facturas, pagina: cuerpo.pagina, tamanoPagina: cuerpo.tamanoPagina, total: cuerpo.total };
};

/** Una línea del detalle: solo sus tres datos; cantidad entera positiva. */
const leerLineaFactura: Lector<LineaFactura> = (cuerpo) =>
  esObjeto(cuerpo) &&
  typeof cuerpo.descripcion === 'string' &&
  esEnteroEntre(cuerpo.cantidad, 1) &&
  esImporte(cuerpo.importe)
    ? { descripcion: cuerpo.descripcion, cantidad: cuerpo.cantidad, importe: cuerpo.importe }
    : undefined;

/**
 * El detalle: los trece datos, y solo ellos. Una factura sin líneas, o con un IVA contenido que no corresponde a su
 * letra (importe en la B, `null` en la C), es ilegible.
 */
const leerFacturaDetalle: Lector<FacturaDetalle> = (cuerpo) => {
  if (
    !esObjeto(cuerpo) ||
    !esComprobante(cuerpo.comprobante) ||
    !esFecha(cuerpo.fecha) ||
    !esEnteroEntre(cuerpo.presupuesto, 1) ||
    typeof cuerpo.apellido !== 'string' ||
    typeof cuerpo.nombre !== 'string' ||
    typeof cuerpo.dni !== 'string' ||
    typeof cuerpo.receptor !== 'string' ||
    !esImporte(cuerpo.total) ||
    !(cuerpo.ivaContenido === null || esImporte(cuerpo.ivaContenido)) ||
    typeof cuerpo.cae !== 'string' ||
    !FORMATO_CAE.test(cuerpo.cae) ||
    !esFecha(cuerpo.vencimientoCae) ||
    typeof cuerpo.simulada !== 'boolean'
  ) {
    return undefined;
  }
  // FR-11, FR-12: la Factura B siempre lleva el IVA contenido y la C nunca, como lo cruza `leerFacturaEmitida`.
  // El comprobante ya cumple FORMATO_COMPROBANTE: si no es una B, es una C.
  const esFacturaB = cuerpo.comprobante.startsWith('Factura B ');
  if (esFacturaB !== (cuerpo.ivaContenido !== null)) {
    return undefined;
  }
  const lineas = leerLista(cuerpo.lineas, leerLineaFactura);
  if (lineas === undefined || lineas.length === 0) {
    return undefined;
  }
  return {
    comprobante: cuerpo.comprobante,
    fecha: cuerpo.fecha,
    presupuesto: cuerpo.presupuesto,
    apellido: cuerpo.apellido,
    nombre: cuerpo.nombre,
    dni: cuerpo.dni,
    receptor: cuerpo.receptor,
    lineas,
    total: cuerpo.total,
    ivaContenido: cuerpo.ivaContenido,
    cae: cuerpo.cae,
    vencimientoCae: cuerpo.vencimientoCae,
    simulada: cuerpo.simulada,
  };
};

/**
 * Busca facturas (FR-16 a FR-23) con `POST /api/facturacion/busqueda` (D-28): los filtros viajan en el cuerpo JSON y
 * nunca en la URL (ADR-019). Como {@link buscarPresupuestos}: un 400 devuelve todos los campos de `errors`; un 413,
 * {@link MENSAJE_BUSQUEDA_GRANDE}; un 401 sin cuerpo avisa a {@link alPerderLaSesion}; una falla de red, un 5xx o una
 * respuesta con forma inesperada, {@link MENSAJE_SIN_CONEXION}.
 */
export const buscarFacturas = (filtro: FiltroFacturas, pagina: number): Promise<ResultadoBusquedaFacturas> =>
  grabarConCampos(
    {
      metodo: 'POST',
      ruta: '/api/facturacion/busqueda',
      cuerpo: cuerpoBusqueda(
        [
          [CAMPO_APELLIDO, filtro.apellido],
          [CAMPO_NOMBRE, filtro.nombre],
          [CAMPO_DNI, filtro.dni],
          [CAMPO_NUMERO_COMPROBANTE, filtro.numero],
          [CAMPO_DESDE, filtro.desde],
          [CAMPO_HASTA, filtro.hasta],
        ],
        pagina,
      ),
      mensaje413: MENSAJE_BUSQUEDA_GRANDE,
    },
    leerPaginaFacturas,
  );

/**
 * La factura emitida desde un presupuesto (FR-24) con `GET /api/presupuestos/{numero}/factura` (D-14). Un 404, o un
 * número que no puede existir (no se envía), es {@link MENSAJE_FACTURA_NO_ENCONTRADA}.
 */
export const consultarFactura = (numero: number): Promise<Resultado<FacturaDetalle>> =>
  esEnteroEntre(numero, 1)
    ? pedir(
        { metodo: 'GET', ruta: `/api/presupuestos/${numero}/factura`, mensaje404: MENSAJE_FACTURA_NO_ENCONTRADA },
        leerFacturaDetalle,
      )
    : Promise.resolve({ tipo: 'general', mensaje: MENSAJE_FACTURA_NO_ENCONTRADA });

/**
 * El PDF de la factura de un presupuesto (FR-05) con `GET /api/presupuestos/{numero}/factura/pdf`, como `Blob`. El 409
 * `sin_datos_emisor` (FR-04) es un conflicto propio con {@link MENSAJE_FACTURA_SIN_DATOS_EMISOR}, nunca el `detail` del
 * servidor; cualquier otro motivo es una respuesta ilegible. Un 404 es {@link MENSAJE_FACTURA_NO_ENCONTRADA}.
 */
export const descargarPdfFactura = (numero: number): Promise<Resultado<Blob>> =>
  descargarPdf(
    numero,
    `/api/presupuestos/${numero}/factura/pdf`,
    { sin_datos_emisor: MENSAJE_FACTURA_SIN_DATOS_EMISOR },
    MENSAJE_FACTURA_NO_ENCONTRADA,
  );

/**
 * Modifica los datos de un artículo con la versión que leyó la pantalla (D-08, D-17) y devuelve lo grabado, con el
 * precio de venta recalculado y la versión nueva. Como {@link grabarArticulo}: un 400 trae todos los campos y un 413
 * es un mensaje general propio. Un 409 trae su motivo con el texto propio (D-19); un 404, "No se encontró el
 * artículo.". Un código que no puede existir no se envía.
 */
export const modificarArticulo = (codigo: number, datos: DatosArticulo, version: number): Promise<ResultadoAlta> =>
  esCodigoArticulo(codigo)
    ? grabarConCampos(
        {
          metodo: 'PUT',
          ruta: `/api/articulos/${codigo}`,
          cuerpo: { ...datos, version },
          motivos409: { inactivo: MENSAJE_ARTICULO_INACTIVO, cambiado: MENSAJE_ARTICULO_CAMBIADO },
          mensaje404: MENSAJE_ARTICULO_NO_ENCONTRADO,
          mensaje413: MENSAJE_ARTICULO_GRANDE,
        },
        leerArticulo,
      )
    : Promise.resolve({ tipo: 'general', mensaje: MENSAJE_ARTICULO_NO_ENCONTRADO });

/**
 * Desactiva o reactiva un artículo (FR-05, FR-06) y devuelve el artículo con su versión nueva (D-26): la pantalla la
 * usa en la edición siguiente. Un 409 solo puede ser `cambiado` (D-16), con el texto que elige quien llama
 * (`mensajeCambiado`; por defecto, el del listado). Un 400 o un 413 no los produce este cliente (el cuerpo es fijo),
 * así que son un mensaje general.
 */
export const cambiarEstadoArticulo = (
  codigo: number,
  activo: boolean,
  mensajeCambiado: string = MENSAJE_LISTADO_CAMBIADO,
): Promise<Resultado<ArticuloGrabado>> =>
  esCodigoArticulo(codigo)
    ? pedir(
        {
          metodo: 'PUT',
          ruta: `/api/articulos/${codigo}/estado`,
          cuerpo: { activo },
          motivos409: { cambiado: mensajeCambiado },
          mensaje404: MENSAJE_ARTICULO_NO_ENCONTRADO,
          mensaje413: MENSAJE_PEDIDO_RECHAZADO,
        },
        leerArticulo,
      ).then((resultado) =>
        // Un 400 del cuerpo fijo no tiene un campo en la pantalla.
        resultado.tipo === 'campo' ? { tipo: 'general', mensaje: MENSAJE_PEDIDO_RECHAZADO } : resultado,
      )
    : Promise.resolve({ tipo: 'general', mensaje: MENSAJE_ARTICULO_NO_ENCONTRADO });

/**
 * Un POST o PUT cuyo 400 trae todos los campos inválidos. El 413 es el mensaje general propio de la pantalla que
 * llega en `opciones.mensaje413`, por el mismo camino que cualquier otro pedido ({@link traducirError}).
 */
async function grabarConCampos<T>(
  opciones: Opciones & { mensaje413: string },
  leer: Lector<T>,
): Promise<Resultado<T> | { tipo: 'campos'; errores: ErrorDeCampo[] }> {
  const respuesta = await hacerPedido(opciones);
  if (!(respuesta instanceof Response)) {
    return respuesta;
  }
  if (respuesta.ok) {
    const guardado = leer(await leerJson(respuesta));
    return guardado === undefined ? { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION } : { tipo: 'ok', datos: guardado };
  }
  if (respuesta.status === 400) {
    const errores = todosLosErrores(await leerJson(respuesta));
    return errores.length > 0 ? { tipo: 'campos', errores } : { tipo: 'general', mensaje: MENSAJE_PEDIDO_RECHAZADO };
  }
  // El alta no responde 409; la modificación lo lee con `motivos409`.
  return traducirError(respuesta, opciones);
}
