import type { DatosCliente } from '../api/cliente';
import { esUtf16Valido, recortar } from '../catalogo/reglas';

/**
 * Las reglas del cliente del presupuesto (`PoliticaCliente` del dominio) con los textos de la Api
 * (`ValidacionPresupuesto`), para avisar en el navegador antes de enviar. Es una comodidad: la validación que vale es
 * la del servidor. Por campo gana la primera regla incumplida, en el mismo orden que el dominio: faltante, UTF-16
 * inválido, largo y formato. El recorte y el control de UTF-16 son los del catálogo (D-25: Presupuestos puede usar
 * Catálogo); los textos son propios aunque repitan los del catálogo (D-08).
 *
 * Datos personales (D-17): estas funciones no registran ni guardan nada; los mensajes son fijos y nunca repiten el
 * valor escrito.
 */

export const MENSAJE_APELLIDO_FALTANTE = 'Ingresá el apellido del cliente';
export const MENSAJE_NOMBRE_FALTANTE = 'Ingresá el nombre del cliente';
/** El texto de AC-02, tal cual lo fija el PRD. */
export const MENSAJE_DNI_FALTANTE = 'Ingresá el DNI del cliente';
export const MENSAJE_DNI_INVALIDO = 'El DNI debe tener 7 u 8 dígitos';
export const MENSAJE_EMAIL_INVALIDO = 'El email debe tener el formato usuario@dominio.extensión';
export const MENSAJE_SIN_LINEAS = 'Agregá al menos una línea al presupuesto';
/** El texto de AC-16, tal cual lo fija el PRD. */
export const MENSAJE_PRECIO_NEGATIVO = 'El precio unitario no puede ser negativo';
export const MENSAJE_CARACTERES_INVALIDOS = 'El texto tiene caracteres no válidos. Escribilo de nuevo.';
export const MENSAJE_APELLIDO_LARGO = 'Usá hasta 100 caracteres.';
export const MENSAJE_NOMBRE_LARGO = 'Usá hasta 100 caracteres.';
export const MENSAJE_DOMICILIO_LARGO = 'Usá hasta 200 caracteres.';
export const MENSAJE_EMAIL_LARGO = 'Usá hasta 254 caracteres.';
export const MENSAJE_TELEFONO_LARGO = 'Usá hasta 30 caracteres.';
/** Más de {@link LINEAS_MAXIMAS} líneas (D-16, D-31). */
export const MENSAJE_DEMASIADAS_LINEAS = 'Usá hasta 100 líneas.';

/** Largos de D-08, medidos en unidades UTF-16 sobre el texto recortado. */
export const LARGO_MAXIMO_APELLIDO = 100;
export const LARGO_MAXIMO_NOMBRE = 100;
export const LARGO_MAXIMO_DOMICILIO = 200;
export const LARGO_MAXIMO_EMAIL = 254;
export const LARGO_MAXIMO_TELEFONO = 30;
/**
 * Largo del campo DNI en pantalla: 8 dígitos con sus dos puntos ("23.456.789"). Es un límite del campo, no una regla:
 * la validez la decide {@link normalizarDni}.
 */
export const LARGO_MAXIMO_DNI_ESCRITO = 10;

/** Líneas que admite un presupuesto (D-16). */
export const LINEAS_MAXIMAS = 100;

const LARGO_EXTENSION_MINIMO = 2;

/** La confirmación del alta (D-24), que nombra el estado (AC-12). */
export const mensajeConfirmacion = (numero: number) => `Presupuesto N.º ${numero} grabado. Estado: Borrador.`;

/** Lo que hay escrito en el formulario. */
export interface TextosCliente {
  apellido: string;
  nombre: string;
  dni: string;
  domicilio: string;
  email: string;
  telefono: string;
}

export type ErroresCliente = Partial<Record<keyof TextosCliente, string>>;

export type ValidacionCliente = { tipo: 'ok'; datos: DatosCliente } | { tipo: 'errores'; errores: ErroresCliente };

/**
 * Los códigos UTF-16 para los que `char.IsWhiteSpace` de .NET es verdadero: un email con cualquiera de ellos es
 * inválido, como en `PoliticaCliente`. Se escriben por su número para que el archivo no tenga caracteres invisibles.
 */
const ESPACIOS: ReadonlySet<number> = new Set([
  0x0009, 0x000a, 0x000b, 0x000c, 0x000d, 0x0020, 0x0085, 0x00a0, 0x1680, 0x2000, 0x2001, 0x2002, 0x2003, 0x2004,
  0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200a, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000,
]);

const tieneEspacios = (texto: string) => [...texto].some((caracter) => ESPACIOS.has(caracter.charCodeAt(0)));

const DIGITOS_DNI = /^[0-9]{7,8}$/;
const LETRAS_ASCII = /^[A-Za-z]+$/;

/**
 * El DNI recortado y sin ningún "." si quedan 7 u 8 dígitos ASCII (FR-04, D-09); si no, `null`. "23.456.789" da
 * "23456789".
 */
export function normalizarDni(texto: string): string | null {
  const digitos = recortar(texto).replaceAll('.', '');
  return DIGITOS_DNI.test(digitos) ? digitos : null;
}

/**
 * D-10 y D-28: una parte local sin espacios ni "@", una sola "@" y un dominio con al menos un punto, sin espacios ni
 * partes vacías, cuya última parte tiene al menos 2 letras ASCII. Recibe el email ya recortado.
 */
export function tieneFormatoDeEmail(email: string): boolean {
  const arroba = email.indexOf('@');
  if (arroba <= 0 || email.indexOf('@', arroba + 1) >= 0 || tieneEspacios(email)) {
    return false;
  }
  const partes = email.slice(arroba + 1).split('.');
  if (partes.length < 2 || partes.some((parte) => parte === '')) {
    return false;
  }
  const extension = partes[partes.length - 1]!;
  return extension.length >= LARGO_EXTENSION_MINIMO && LETRAS_ASCII.test(extension);
}

type Campo = { valor: string | null } | { error: string };

/** UTF-16 inválido antes que el largo, como `PoliticaCliente`. */
function texto(recortado: string, largoMaximo: number, demasiadoLargo: string): Campo {
  if (!esUtf16Valido(recortado)) {
    return { error: MENSAJE_CARACTERES_INVALIDOS };
  }
  return recortado.length > largoMaximo ? { error: demasiadoLargo } : { valor: recortado };
}

function obligatorio(escrito: string, largoMaximo: number, faltante: string, demasiadoLargo: string): Campo {
  const recortado = recortar(escrito);
  return recortado === '' ? { error: faltante } : texto(recortado, largoMaximo, demasiadoLargo);
}

/** Vacío o solo espacios es ausente: se envía `null` (FR-03, D-11). */
function opcional(escrito: string, largoMaximo: number, demasiadoLargo: string): Campo {
  const recortado = recortar(escrito);
  return recortado === '' ? { valor: null } : texto(recortado, largoMaximo, demasiadoLargo);
}

function dni(escrito: string): Campo {
  const recortado = recortar(escrito);
  if (recortado === '') {
    return { error: MENSAJE_DNI_FALTANTE };
  }
  // Se envía como se escribió (recortado): el servidor quita los puntos y guarda la versión normalizada.
  return normalizarDni(recortado) === null ? { error: MENSAJE_DNI_INVALIDO } : { valor: recortado };
}

function email(escrito: string): Campo {
  const campo = opcional(escrito, LARGO_MAXIMO_EMAIL, MENSAJE_EMAIL_LARGO);
  if ('error' in campo || campo.valor === null) {
    return campo;
  }
  return tieneFormatoDeEmail(campo.valor) ? campo : { error: MENSAJE_EMAIL_INVALIDO };
}

/** Valida los seis datos: los datos a enviar (recortados, opcionales vacíos en `null`), o un mensaje por campo. */
export function validarCliente(textos: TextosCliente): ValidacionCliente {
  const campos: Record<keyof TextosCliente, Campo> = {
    apellido: obligatorio(textos.apellido, LARGO_MAXIMO_APELLIDO, MENSAJE_APELLIDO_FALTANTE, MENSAJE_APELLIDO_LARGO),
    nombre: obligatorio(textos.nombre, LARGO_MAXIMO_NOMBRE, MENSAJE_NOMBRE_FALTANTE, MENSAJE_NOMBRE_LARGO),
    dni: dni(textos.dni),
    domicilio: opcional(textos.domicilio, LARGO_MAXIMO_DOMICILIO, MENSAJE_DOMICILIO_LARGO),
    email: email(textos.email),
    telefono: opcional(textos.telefono, LARGO_MAXIMO_TELEFONO, MENSAJE_TELEFONO_LARGO),
  };
  const errores: ErroresCliente = {};
  for (const [nombre, campo] of Object.entries(campos) as [keyof TextosCliente, Campo][]) {
    if ('error' in campo) {
      errores[nombre] = campo.error;
    }
  }
  if (Object.keys(errores).length > 0) {
    return { tipo: 'errores', errores };
  }
  const valor = (campo: Campo) => ('valor' in campo ? campo.valor : null);
  return {
    tipo: 'ok',
    datos: {
      apellido: valor(campos.apellido)!,
      nombre: valor(campos.nombre)!,
      dni: valor(campos.dni)!,
      domicilio: valor(campos.domicilio),
      email: valor(campos.email),
      telefono: valor(campos.telefono),
    },
  };
}

/** El mensaje de las líneas antes de enviar: ninguna o más de {@link LINEAS_MAXIMAS} (FR-07, D-31). */
export function validarLineas(cantidad: number): string | undefined {
  if (cantidad === 0) {
    return MENSAJE_SIN_LINEAS;
  }
  return cantidad > LINEAS_MAXIMAS ? MENSAJE_DEMASIADAS_LINEAS : undefined;
}
