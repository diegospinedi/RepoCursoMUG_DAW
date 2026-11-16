import type { ErrorDeCampo } from '../api/cliente';

/**
 * Reparte los errores de un 400 de la Api entre los campos de un formulario y el mensaje general: los de un campo que
 * el formulario tiene van junto a ese campo (con `mensajeDe`, el texto propio de la pantalla para ese campo; por
 * defecto, el de la Api); los de cualquier otro campo se juntan en el mensaje general, o `null` si no hay ninguno.
 */
export function repartirErrores<C extends string>(
  recibidos: readonly ErrorDeCampo[],
  esCampo: (campo: string) => campo is C,
  mensajeDe: (campo: C, mensaje: string) => string = (_campo, mensaje) => mensaje,
): { campos: Partial<Record<C, string>>; general: string | null } {
  const campos: Partial<Record<C, string>> = {};
  const otros: string[] = [];
  for (const { campo, mensaje } of recibidos) {
    if (esCampo(campo)) {
      campos[campo] = mensajeDe(campo, mensaje);
    } else {
      otros.push(mensaje);
    }
  }
  return { campos, general: otros.length > 0 ? otros.join(' ') : null };
}
