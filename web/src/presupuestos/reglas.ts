import type { DatosCliente } from '../api/cliente';
import { esUtf16Valido, recortar } from '../catalogo/reglas';
import { centavosDe, IMPORTE_MAXIMO_SEGURO_CENTESIMOS, leerDecimal, sumarCentesimos } from '../configuracion/numeros';
import { MENSAJE_HASTA_DOS_DECIMALES } from '../configuracion/reglas';

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

/*
 * Cantidad, descuento y cálculo de la línea (FEAT-003b). Los textos son los de `ValidacionPresupuesto`; las fórmulas,
 * las de `CalculoLinea` del dominio, en espejo y solo para mostrar (ADR-016): el servidor recalcula y graba lo suyo.
 */

/** El texto de AC-02, tal cual lo fija el PRD: 0, negativos, no enteros o vacío (D-01). */
export const MENSAJE_CANTIDAD_INVALIDA = 'La cantidad debe ser un número entero mayor a 0';
/** El texto de AC-09: más de {@link CANTIDAD_MAXIMA} unidades (D-01). */
export const MENSAJE_CANTIDAD_MAXIMA = 'Usá hasta 9.999 unidades.';
/** El texto de AC-03, tal cual lo fija el PRD: fuera de 0..100 o vacío (D-02). */
export const MENSAJE_DESCUENTO_FUERA_DE_RANGO = 'El descuento debe estar entre 0 y 100';
/** Límite técnico de D-04: el Precio Final de la línea supera el importe máximo del contrato. */
export const MENSAJE_IMPORTE_LINEA_EXCEDIDO = 'El precio final de la línea es demasiado alto.';
/** Límite técnico de D-04: el total del presupuesto supera el importe máximo del contrato. */
export const MENSAJE_TOTAL_EXCEDIDO = 'El total del presupuesto es demasiado alto.';

/** Unidades por línea (D-01, `CalculoLinea.CantidadMaxima`). */
export const CANTIDAD_MAXIMA = 9_999;
const CANTIDAD_MINIMA = 1;
const DESCUENTO_MINIMO = 0;
const DESCUENTO_MAXIMO = 100;
const DESCUENTO_DECIMALES_MAXIMOS = 2;
/** 100 % en centésimos de punto porcentual. */
const DESCUENTO_COMPLETO = 10_000n;

const SOLO_DIGITOS = /^[0-9]+$/;

/** Un campo de la línea: el valor a enviar o el mensaje a mostrar junto al campo. */
export type CampoLinea = { valor: number } | { error: string };

/**
 * La Cantidad escrita (FR-02): solo dígitos ASCII, sin signo, coma ni punto, que formen un entero de 1 a
 * {@link CANTIDAD_MAXIMA}. Se recortan los espacios de los extremos. Un entero de solo dígitos mayor al máximo, por
 * largo que sea, informa el máximo (como D-22 en el servidor).
 */
export function validarCantidad(texto: string): CampoLinea {
  const limpio = texto.trim();
  if (!SOLO_DIGITOS.test(limpio)) {
    return { error: MENSAJE_CANTIDAD_INVALIDA };
  }
  const cantidad = BigInt(limpio);
  if (cantidad < BigInt(CANTIDAD_MINIMA)) {
    return { error: MENSAJE_CANTIDAD_INVALIDA };
  }
  return cantidad > BigInt(CANTIDAD_MAXIMA) ? { error: MENSAJE_CANTIDAD_MAXIMA } : { valor: Number(cantidad) };
}

/**
 * El Porcentaje de Descuento escrito (FR-03, D-02), con coma o punto decimal: primero el rango (vacío, un texto que no
 * es un número o fuera de 0..100 dan el texto de AC-03, como el servidor ante un tipo JSON inválido, D-07) y después la
 * escala (más de 2 decimales).
 */
export function validarDescuento(texto: string): CampoLinea {
  const lectura = leerDecimal(texto);
  if (lectura.tipo !== 'numero' || lectura.valor < DESCUENTO_MINIMO || lectura.valor > DESCUENTO_MAXIMO) {
    return { error: MENSAJE_DESCUENTO_FUERA_DE_RANGO };
  }
  if (lectura.decimales > DESCUENTO_DECIMALES_MAXIMOS) {
    return { error: MENSAJE_HASTA_DOS_DECIMALES };
  }
  // "-0" es 0: así se envía y se compara como 0.
  return { valor: lectura.valor === 0 ? 0 : lectura.valor };
}

/** Los dos importes calculados de una línea. */
export interface PreciosLinea {
  precioConDescuento: number;
  precioFinal: number;
}

const importeDe = (centesimos: bigint) => Number(centesimos) / 100;

/** Un descuento de 0 a 100 con hasta 2 decimales, en centésimos; si no lo es, `null`. */
function descuentoEnCentesimos(descuento: number): bigint | null {
  if (!Number.isFinite(descuento) || descuento < DESCUENTO_MINIMO || descuento > DESCUENTO_MAXIMO) {
    return null;
  }
  const centesimos = centavosDe(descuento);
  return importeDe(centesimos) === descuento ? centesimos : null;
}

/**
 * El Precio con Descuento y el Precio Final de una línea (FR-04, FR-05, D-04), en centésimos con BigInt y redondeo
 * mitad hacia arriba, como `CalculoLinea`: Precio con Descuento = PU × (10000 − D) / 10000, redondeado; Precio Final
 * = Precio con Descuento ya redondeado × Cantidad. El precio pasa a centésimos con {@link centavosDe}, no con
 * `Math.round` de flotantes.
 *
 * Contrato de `null`: entrada inválida o importe excedido. Es decir, el Precio Final supera el tope de D-04
 * ({@link IMPORTE_MAXIMO_SEGURO_CENTESIMOS}, {@link MENSAJE_IMPORTE_LINEA_EXCEDIDO}) o los datos no se pueden calcular:
 * precio negativo o no finito (la línea ya se rechaza por AC-16 de FEAT-003a), cantidad fuera de
 * 1..{@link CANTIDAD_MAXIMA} o descuento fuera de 0..100 o con más de 2 decimales. La pantalla valida antes la
 * cantidad y el descuento con {@link validarCantidad} y {@link validarDescuento}, y solo entonces lee `null` como
 * importe excedido (D-25).
 *
 * Por encima de 2^46 pesos (70.368.744.177.664) un `number` no representa los centavos con exactitud y el resultado
 * puede diferir del servidor (D-23, ADR-016); por debajo, la paridad es exacta.
 */
export function calcularLinea(precioUnitario: number, cantidad: number, descuento: number): PreciosLinea | null {
  const descuentoCentesimos = descuentoEnCentesimos(descuento);
  if (
    !Number.isFinite(precioUnitario) ||
    precioUnitario < 0 ||
    !Number.isInteger(cantidad) ||
    cantidad < CANTIDAD_MINIMA ||
    cantidad > CANTIDAD_MAXIMA ||
    descuentoCentesimos === null
  ) {
    return null;
  }
  const precio = centavosDe(precioUnitario);
  // Mitad hacia arriba con enteros no negativos: sumar la mitad del divisor antes de la división entera.
  const conDescuento =
    (precio * (DESCUENTO_COMPLETO - descuentoCentesimos) + DESCUENTO_COMPLETO / 2n) / DESCUENTO_COMPLETO;
  const final = conDescuento * BigInt(cantidad);
  return final > IMPORTE_MAXIMO_SEGURO_CENTESIMOS
    ? null
    : { precioConDescuento: importeDe(conDescuento), precioFinal: importeDe(final) };
}

/**
 * El total del presupuesto (FR-06): la suma exacta de los Precios Finales ya redondeados, con
 * {@link sumarCentesimos}. Contrato de `null`, como {@link calcularLinea}: entrada inválida (algún importe `NaN` o
 * `±Infinity`) o total por encima del tope de D-04 ({@link IMPORTE_MAXIMO_SEGURO_CENTESIMOS},
 * {@link MENSAJE_TOTAL_EXCEDIDO}). Nunca lanza una excepción. Por encima de 2^46 pesos vale la limitación de D-23.
 */
export function calcularTotal(preciosFinales: readonly number[]): number | null {
  if (!preciosFinales.every(Number.isFinite)) {
    return null;
  }
  const total = sumarCentesimos(preciosFinales);
  return total > IMPORTE_MAXIMO_SEGURO_CENTESIMOS ? null : importeDe(total);
}
