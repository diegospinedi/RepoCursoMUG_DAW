// @vitest-environment node
// Sin DOM: la prueba solo lee el texto de la hoja, y en Node `import.meta.url` es la ruta `file:` de este archivo.
import { beforeAll, describe, expect, it } from 'vitest';

/**
 * La hoja global tal como está en el repositorio. Se lee con node:fs porque Vitest vacía los `.css` (también con
 * `?raw`); la ruta del módulo no es literal para que tsc no lo busque (no hay @types/node, como en configuracionVite).
 */
let estilos = '';
beforeAll(async () => {
  const modulo: string = 'node:fs/promises';
  const { readFile } = (await import(/* @vite-ignore */ modulo)) as {
    readFile: (ruta: URL, codificacion: 'utf8') => Promise<string>;
  };
  estilos = (await readFile(new URL('./estilos.css', import.meta.url), 'utf8')).replace(/\/\*[\s\S]*?\*\//g, '');
});

/** El índice de la llave que cierra la que abre en `inicio`. */
function cierre(texto: string, inicio: number): number {
  let profundidad = 0;
  for (let i = inicio; i < texto.length; i++) {
    if (texto[i] === '{') profundidad++;
    if (texto[i] === '}' && --profundidad === 0) return i;
  }
  throw new Error('Llaves sin cerrar en estilos.css.');
}

/** Separa una lista de selectores por las comas de primer nivel (no las de `:not(a, b)`), sin espacios sobrantes. */
function selectores(lista: string): string[] {
  const partes: string[] = [];
  let profundidad = 0;
  let actual = '';
  for (const caracter of lista) {
    if (caracter === '(') profundidad++;
    if (caracter === ')') profundidad--;
    if (caracter === ',' && profundidad === 0) {
      partes.push(actual);
      actual = '';
    } else {
      actual += caracter;
    }
  }
  partes.push(actual);
  return partes.map((parte) => parte.replace(/\s+/g, ' ').trim()).filter((parte) => parte !== '');
}

/** Las reglas `selectores { declaraciones }` de un texto sin @-reglas anidadas. */
function reglas(texto: string): { selectores: string[]; cuerpo: string }[] {
  return [...texto.matchAll(/([^{}]+)\{([^{}]*)\}/g)].map((regla) => ({
    selectores: selectores(regla[1] ?? ''),
    cuerpo: regla[2] ?? '',
  }));
}

/** La hoja separada en lo que está fuera de los @media y el cuerpo de cada @media, por su condición. */
function partes(): { fuera: string; medias: Map<string, string> } {
  const medias = new Map<string, string>();
  let fuera = '';
  let desde = 0;
  for (const encontrado of estilos.matchAll(/@media\s*([^{]+)\{/g)) {
    const inicio = encontrado.index;
    if (inicio < desde) continue;
    const abre = inicio + encontrado[0].length - 1;
    const fin = cierre(estilos, abre);
    fuera += estilos.slice(desde, inicio);
    medias.set((encontrado[1] ?? '').trim(), estilos.slice(abre + 1, fin));
    desde = fin + 1;
  }
  fuera += estilos.slice(desde);
  return { fuera, medias };
}

describe('estilos.css', () => {
  it('estilos_MovimientoReducido_SinTransiciones', () => {
    // FEAT-012 (FR-04, FAIL-1 de la revisión): cada selector que declara una transición aparece, tal cual, en el
    // @media (prefers-reduced-motion: reduce) con `transition: none`. Un selector distinto (por ejemplo `.campo input`
    // en lugar de `.campo input:not([type='file'], …)`) no alcanza: pierde por especificidad y la transición sigue.
    const { fuera, medias } = partes();
    const reducido = medias.get('(prefers-reduced-motion: reduce)');
    expect(reducido).toBeDefined();
    const sinTransicion = new Set(
      reglas(reducido ?? '')
        .filter((regla) => /transition:\s*none\s*;/.test(regla.cuerpo))
        .flatMap((regla) => regla.selectores),
    );

    const conTransicion = reglas(fuera)
      .filter((regla) => /(^|;|\s)transition:/.test(regla.cuerpo) && !/transition:\s*none/.test(regla.cuerpo))
      .flatMap((regla) => regla.selectores);
    // Control: la hoja tiene transiciones (la tarjeta del menú, los botones, los campos y las filas).
    expect(conTransicion.length).toBeGreaterThanOrEqual(4);

    expect(conTransicion.filter((selector) => !sinTransicion.has(selector))).toEqual([]);
  });

  it('estilos_Sugerencias_ListaMarcadaYAnuncioOculto', () => {
    // FEAT-013 (Block 1): la lista de sugerencias, la opción marcada con el teclado y el anuncio que solo oye el lector
    // de pantalla (fuera de la vista, pero no con display: none, que lo silenciaría).
    const definidas = reglas(partes().fuera);
    const cuerpo = (selector: string) =>
      definidas
        .filter((regla) => regla.selectores.includes(selector))
        .map((regla) => regla.cuerpo)
        .join(';');

    expect(cuerpo('.campo-busqueda')).toMatch(/position:\s*relative/);
    expect(cuerpo('.sugerencias')).toMatch(/position:\s*absolute/);
    expect(cuerpo('.sugerencia')).toMatch(/cursor:\s*pointer/);
    expect(cuerpo(".sugerencia[aria-selected='true']")).toMatch(/background-color:/);
    const anuncio = cuerpo('.campo-busqueda__anuncio');
    expect(anuncio).toMatch(/position:\s*absolute/);
    expect(anuncio).toMatch(/clip-path:\s*inset\(50%\)/);
    expect(anuncio).not.toMatch(/display:\s*none/);

    // Con colores forzados (alto contraste de Windows) el fondo de la marca desaparece: la opción marcada usa los
    // colores del sistema para la selección.
    const forzados = reglas(partes().medias.get('(forced-colors: active)') ?? '')
      .filter((regla) => regla.selectores.includes(".sugerencia[aria-selected='true']"))
      .map((regla) => regla.cuerpo)
      .join(';');
    expect(forzados).toMatch(/forced-color-adjust:\s*none/);
    expect(forzados).toMatch(/background:\s*Highlight\s*;/);
    expect(forzados).toMatch(/color:\s*HighlightText/);
  });
});
