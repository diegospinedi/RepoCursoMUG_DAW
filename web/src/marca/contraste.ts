const COLOR = /^#[0-9A-Fa-f]{6}$/;

/** La luminancia relativa WCAG 2.x de un color `#RRGGBB`; lanza `RangeError` (sin repetir el valor) con otro formato. */
function luminancia(color: string): number {
  if (!COLOR.test(color)) {
    throw new RangeError('El color tiene que tener el formato #RRGGBB.');
  }
  const [r, g, b] = [1, 3, 5].map((inicio) => {
    const canal = Number.parseInt(color.slice(inicio, inicio + 2), 16) / 255;
    return canal <= 0.04045 ? canal / 12.92 : ((canal + 0.055) / 1.055) ** 2.4;
  }) as [number, number, number];
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/**
 * La relación de contraste WCAG entre dos colores `#RRGGBB` (FEAT-012, NFR-01, D-10): de 1 (iguales) a 21 (negro sobre
 * blanco). El orden de los colores no importa. Con un color en otro formato lanza `RangeError`.
 *
 * La usan solo las pruebas de NFR-01 (`contraste.test.ts`), que verifican las paletas conocidas: la aplicación no la
 * llama en tiempo de ejecución, y por eso el bundle no la incluye.
 */
export function contraste(colorA: string, colorB: string): number {
  const a = luminancia(colorA);
  const b = luminancia(colorB);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}
