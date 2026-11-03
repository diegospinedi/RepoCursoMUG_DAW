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

export const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';
export const MENSAJE_OCUPADO = 'El sistema está ocupado. Intentá de nuevo en un momento.';
export const MENSAJE_PEDIDO_RECHAZADO = 'No se pudo completar la operación. Recargá la página y reintentá.';
export const MENSAJE_CUERPO_GRANDE = 'La contraseña es demasiado larga. Ingresá una de 128 caracteres o menos.';
export const MENSAJE_YA_DEFINIDA = 'Ya hay una contraseña definida. Ingresá con ella.';
export const MENSAJE_SIN_CONTRASENA = 'Todavía no hay una contraseña. Definí la contraseña primero.';
export const MENSAJE_BLOQUEADO = 'El acceso está bloqueado temporalmente. Volvé a intentar en unos minutos.';
export const MENSAJE_SIN_CREDENCIAL_AL_CAMBIAR = 'No hay una contraseña definida. Volvé a ingresar.';

/** Campo de la contraseña en el cuerpo y en `errors` de la Api. */
export const CAMPO_CONTRASENA = 'contrasena';
/** Campos del cuerpo y de `errors` de `cambiar`. */
export const CAMPO_ACTUAL = 'contrasenaActual';
export const CAMPO_NUEVA = 'contrasenaNueva';

export type Resultado<T> =
  | { tipo: 'ok'; datos: T }
  /** Error que se muestra junto al campo indicado. */
  | { tipo: 'campo'; campo: string; mensaje: string }
  /** 409: la operación no corresponde al estado del acceso. */
  | { tipo: 'conflicto'; mensaje: string }
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
  metodo: 'GET' | 'POST';
  ruta: string;
  cuerpo?: unknown;
  mensaje409?: string;
  /** Campo del formulario donde se muestra un 413 (cuerpo demasiado grande); por defecto, {@link CAMPO_CONTRASENA}. */
  campo413?: string;
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
async function enviar({ metodo, ruta, cuerpo, mensaje409, campo413 }: Opciones): Promise<
  { tipo: 'respuesta'; respuesta: Response } | Fallo
> {
  const init: RequestInit = { method: metodo, credentials: 'same-origin', cache: 'no-store' };
  if (metodo === 'POST') {
    // La Api exige Content-Type: application/json en todo POST, aunque no lleve cuerpo (R-04).
    init.headers = { 'Content-Type': 'application/json', Accept: 'application/json' };
    if (cuerpo !== undefined) {
      init.body = JSON.stringify(cuerpo);
    }
  }

  let respuesta: Response;
  try {
    respuesta = await fetch(ruta, init);
  } catch {
    return { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION };
  }

  return respuesta.ok ? { tipo: 'respuesta', respuesta } : traducirError(respuesta, mensaje409, campo413);
}

async function traducirError(
  respuesta: Response,
  mensaje409: string | undefined,
  campo413: string = CAMPO_CONTRASENA,
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
    case 409:
      return { tipo: 'conflicto', mensaje: mensaje409 ?? MENSAJE_PEDIDO_RECHAZADO };
    case 413:
      return { tipo: 'campo', campo: campo413, mensaje: MENSAJE_CUERPO_GRANDE };
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
