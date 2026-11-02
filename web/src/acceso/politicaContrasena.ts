/**
 * Validación de la contraseña en el navegador, antes de enviarla. Cuenta igual que el servidor (ADR-004): unidades
 * UTF-16 de la forma NFC, de 8 a 128, sin recortar espacios. El servidor vuelve a validar.
 */
export const MINIMO = 8;
export const MAXIMO = 128;

export const MENSAJE_MUY_CORTA = 'La contraseña debe tener al menos 8 caracteres. Ingresá una más larga.';
export const MENSAJE_MUY_LARGA = 'La contraseña puede tener hasta 128 caracteres. Ingresá una más corta.';

/** El mensaje a mostrar junto al campo, o `null` si la contraseña cumple la política. */
export function validarContrasena(valor: string): string | null {
  const largo = valor.normalize('NFC').length;
  if (largo < MINIMO) {
    return MENSAJE_MUY_CORTA;
  }
  if (largo > MAXIMO) {
    return MENSAJE_MUY_LARGA;
  }
  return null;
}
