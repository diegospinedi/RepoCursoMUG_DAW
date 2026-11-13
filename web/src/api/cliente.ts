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

import { IMPORTE_MAXIMO_SEGURO, sumarCentesimos } from '../configuracion/numeros';

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

/** Los motivos de un 409: los de los artículos (D-19) y `final` del presupuesto (FEAT-003c, D-05). */
export type MotivoConflicto = 'inactivo' | 'cambiado' | 'final';

const MOTIVOS_CONFLICTO: readonly MotivoConflicto[] = ['inactivo', 'cambiado', 'final'];

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
}

/** Lo que devuelve `GET /api/configuracion`. */
export interface ConfiguracionNegocio extends DatosConfiguracion {
  faltaTope: boolean;
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

/** Cualquier resultado que no sea `ok`: sirve para un {@link Resultado} de cualquier tipo de datos. */
type Fallo = Exclude<Resultado<never>, { tipo: 'ok' }>;

interface Opciones {
  metodo: 'GET' | 'POST' | 'PUT';
  ruta: string;
  cuerpo?: unknown;
  mensaje409?: string;
  /**
   * Para los 409 con `motivo` (D-19): el texto propio de cada motivo que la operación admite. Con esta opción el 409
   * se lee: un motivo que no está acá, o un cuerpo ilegible, es una respuesta ilegible.
   */
  motivos409?: Partial<Record<MotivoConflicto, string>>;
  /** Mensaje general de un 404; sin él, el mensaje de pedido rechazado. */
  mensaje404?: string;
  /** Campo del formulario donde se muestra un 413 (cuerpo demasiado grande); por defecto, {@link CAMPO_CONTRASENA}. */
  campo413?: string;
  /** Mensaje general de un 413, para un pedido sin un campo al que atribuirlo; tiene prioridad sobre `campo413`. */
  mensaje413?: string;
}

/** Pedido cuya respuesta exitosa trae datos: `leer` valida la forma del JSON. */
async function pedir<T>(opciones: Opciones, leer: Lector<T>): Promise<Resultado<T>> {
  const respuesta = await enviar(opciones);
  if (respuesta.tipo !== 'respuesta') {
    return respuesta;
  }
  const datos = leer(await leerJson(respuesta.respuesta));
  return datos === undefined ? { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION } : { tipo: 'ok', datos };
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
  { mensaje409, motivos409, mensaje404, campo413 = CAMPO_CONTRASENA, mensaje413 }: Opciones,
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
      return { tipo: 'general', mensaje: MENSAJE_OCUPADO };
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
function conflictoConMotivo(cuerpo: unknown, motivos: Partial<Record<MotivoConflicto, string>>): Fallo {
  const motivo = esObjeto(cuerpo) ? cuerpo.motivo : undefined;
  if (esMotivoConflicto(motivo)) {
    const mensaje = motivos[motivo];
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

const leerConfiguracion: Lector<ConfiguracionNegocio> = (cuerpo) =>
  esObjeto(cuerpo) &&
  esNumero(cuerpo.alicuotaIva) &&
  esCondicion(cuerpo.condicionFiscal) &&
  esNumero(cuerpo.multiploRedondeo) &&
  (cuerpo.topeIdentificacion === null || esNumero(cuerpo.topeIdentificacion)) &&
  typeof cuerpo.faltaTope === 'boolean'
    ? {
        alicuotaIva: cuerpo.alicuotaIva,
        condicionFiscal: cuerpo.condicionFiscal,
        multiploRedondeo: cuerpo.multiploRedondeo,
        topeIdentificacion: cuerpo.topeIdentificacion,
        faltaTope: cuerpo.faltaTope,
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
 * Graba los cuatro parámetros juntos y devuelve lo que quedó guardado (D-03), con cuántos precios de venta cambiaron
 * al recalcular el catálogo (D-09 y D-23 de FEAT-002c). A diferencia de las pantallas de acceso,
 * un 400 devuelve TODOS los campos de `errors`, y un 413 es un mensaje general propio (no el de la contraseña): el
 * navegador limita cada número a 32 caracteres, así que solo un cuerpo alterado supera el límite de la Api, y no hay
 * un campo al que atribuirlo.
 */
export const grabarConfiguracion = (datos: DatosConfiguracion): Promise<ResultadoGrabacion> =>
  grabarConCampos(
    { metodo: 'POST', ruta: '/api/configuracion', cuerpo: datos, mensaje413: MENSAJE_CONFIGURACION_GRANDE },
    leerConfiguracionGrabada,
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

const FORMATO_FECHA = /^(\d{4})-(\d{2})-(\d{2})$/;

/** Una fecha `yyyy-MM-dd` que existe en el calendario ("2026-02-30" no). Se compara en UTC: sin zona horaria. */
function esFecha(valor: unknown): valor is string {
  if (typeof valor !== 'string') {
    return false;
  }
  const partes = FORMATO_FECHA.exec(valor);
  if (partes === null) {
    return false;
  }
  const [anio, mes, dia] = [Number(partes[1]), Number(partes[2]), Number(partes[3])];
  const fecha = new Date(Date.UTC(anio, mes - 1, dia));
  return fecha.getUTCFullYear() === anio && fecha.getUTCMonth() === mes - 1 && fecha.getUTCDate() === dia;
}

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

/**
 * Un presupuesto por su número. Un número que no es un entero positivo seguro no se consulta (no puede existir); ese
 * caso y un 404 son el mensaje general {@link MENSAJE_PRESUPUESTO_NO_ENCONTRADO}.
 */
export const consultarPresupuesto = (numero: number): Promise<Resultado<PresupuestoGrabado>> =>
  esEnteroEntre(numero, 1)
    ? pedir(
        { metodo: 'GET', ruta: `/api/presupuestos/${numero}`, mensaje404: MENSAJE_PRESUPUESTO_NO_ENCONTRADO },
        leerPresupuesto,
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
function cuerpoBusqueda(filtro: FiltroPresupuestos, pagina: number): Record<string, string | number> {
  const cuerpo: Record<string, string | number> = {};
  const campos: [string, string][] = [
    [CAMPO_APELLIDO, filtro.apellido],
    [CAMPO_NOMBRE, filtro.nombre],
    [CAMPO_DNI, filtro.dni],
    [CAMPO_DESDE, filtro.desde],
    [CAMPO_HASTA, filtro.hasta],
  ];
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
      cuerpo: cuerpoBusqueda(filtro, pagina),
      mensaje413: MENSAJE_BUSQUEDA_GRANDE,
    },
    leerPaginaPresupuestos,
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
