// @vitest-environment node
// Sin DOM: la prueba solo calcula, y en Node `import.meta.url` es la ruta `file:` del archivo, que lee estilos.css.
import { beforeAll, describe, expect, it } from 'vitest';
import { contraste } from './contraste';

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
  // Sin los comentarios: lo que se mide es lo que la hoja declara.
  estilos = (await readFile(new URL('../estilos.css', import.meta.url), 'utf8')).replace(/\/\*[\s\S]*?\*\//g, '');
});

/** El valor `#RRGGBB` de una variable de `:root` en `estilos.css`: los pares se miden con los colores reales de la hoja. */
function variable(nombre: string): string {
  const encontrado = new RegExp(`${nombre}:\\s*(#[0-9A-Fa-f]{6})\\s*;`).exec(estilos);
  if (encontrado?.[1] === undefined) {
    throw new Error(`estilos.css no define ${nombre} como #RRGGBB.`);
  }
  return encontrado[1].toUpperCase();
}

/** El porcentaje (sin el %) de una variable de mezcla de `:root`: el mismo que usa cada `color-mix` de la hoja. */
function porcentaje(nombre: string): number {
  const encontrado = new RegExp(`${nombre}:\\s*(\\d+(?:\\.\\d+)?)%\\s*;`).exec(estilos);
  if (encontrado?.[1] === undefined) {
    throw new Error(`estilos.css no define ${nombre} como un porcentaje.`);
  }
  return Number(encontrado[1]);
}

/**
 * `color-mix(in srgb, color P%, base)`: cada canal sRGB se interpola por separado (sin pasar a lineal), que es lo que
 * hace el navegador; el resultado se redondea al entero más cercano.
 */
function mezcla(color: string, porcentaje: number, base: string): string {
  const canales = [1, 3, 5].map((inicio) => {
    const a = Number.parseInt(color.slice(inicio, inicio + 2), 16);
    const b = Number.parseInt(base.slice(inicio, inicio + 2), 16);
    return Math.round((a * porcentaje + b * (100 - porcentaje)) / 100);
  });
  return `#${canales.map((canal) => canal.toString(16).padStart(2, '0').toUpperCase()).join('')}`;
}

/** Las dos marcas conocidas (NFR-01): la de ejemplo, que es la de `:root`, y la real (la del harness de las e2e). */
const MARCAS = [
  { nombre: 'de ejemplo', primario: '#5A5A5A', fondo: '#F5F5F5' },
  { nombre: 'real', primario: '#0903A0', fondo: '#FFFFFF' },
] as const;

describe('contraste', () => {
  it('contraste_PaletasConocidas_TextoYFoco', () => {
    // NFR-01: el texto de las tarjetas sobre blanco. #1F1F1F es --color-texto (web/src/estilos.css:5).
    expect(contraste('#1F1F1F', '#FFFFFF')).toBeGreaterThanOrEqual(4.5);
    // El primario de cada marca conocida sobre su fondo: el contorno del foco y el ícono (3:1, D-05).
    expect(contraste('#0903A0', '#FFFFFF')).toBeGreaterThanOrEqual(3);
    expect(contraste('#5A5A5A', '#F5F5F5')).toBeGreaterThanOrEqual(3);

    // Los colores tal como se ven (texto con opacidad o sobre fondos mezclados), compuestos a mano:
    // la descripción: #1F1F1F con opacidad 0,8 sobre la tarjeta blanca → 0,8·0x1F + 0,2·0xFF = 0x4C por canal.
    expect(contraste('#4C4C4C', '#FFFFFF')).toBeGreaterThanOrEqual(4.5);
    // El título de sección: #1F1F1F con opacidad 0,8 sobre el fondo de la marca de ejemplo (#F5F5F5)
    // → 0,8·0x1F + 0,2·0xF5 = 0x4A por canal.
    expect(contraste('#4A4A4A', '#F5F5F5')).toBeGreaterThanOrEqual(4.5);
    // El botón secundario con hover y el círculo del ícono, marca de ejemplo: texto #5A5A5A sobre
    // color-mix(#5A5A5A 10 %, blanco) → 0,1·0x5A + 0,9·0xFF ≈ 0xEE por canal.
    expect(contraste('#5A5A5A', '#EEEEEE')).toBeGreaterThanOrEqual(4.5);
    // Lo mismo con la marca del harness: #0903A0 sobre color-mix(#0903A0 10 %, blanco)
    // → (0,1·0x09 + 0,9·0xFF, 0,1·0x03 + 0,9·0xFF, 0,1·0xA0 + 0,9·0xFF) ≈ (0xE6, 0xE6, 0xF6).
    expect(contraste('#0903A0', '#E6E6F6')).toBeGreaterThanOrEqual(4.5);

    // Los extremos de la fórmula WCAG: negro sobre blanco es 21, un color contra sí mismo es 1, y el orden no importa.
    expect(contraste('#000000', '#ffffff')).toBeCloseTo(21, 5);
    expect(contraste('#5a5a5a', '#5A5A5A')).toBeCloseTo(1, 5);
    expect(contraste('#FFFFFF', '#0903A0')).toBeCloseTo(contraste('#0903A0', '#FFFFFF'), 10);
    // Un valor de referencia: #767676 sobre blanco es 4,54.
    expect(contraste('#767676', '#FFFFFF')).toBeCloseTo(4.54, 2);
  });

  describe('contraste_MensajesCamposBotonesYTablas_DosMarcas', () => {
    // FEAT-012, NFR-01, AC-16, D-20: los pares del estilo uniforme (Block 2), con los colores de `:root` y las mezclas
    // que hace estilos.css. Texto ≥ 4,5:1; contornos de foco y bordes de los campos ≥ 3:1 (WCAG 2.1 AA, 1.4.3 y 1.4.11).
    it.each(MARCAS)('marca $nombre (primario $primario)', ({ primario, fondo }) => {
      const texto = variable('--color-texto');
      const tarjeta = variable('--fondo-tarjeta');
      const error = variable('--color-error');
      const fondoError = variable('--fondo-error');
      const exito = variable('--color-exito');
      const fondoExito = variable('--fondo-exito');
      // Los porcentajes de color-mix salen de variables de :root (ningún color-mix de la hoja lleva un literal), así
      // los pares de abajo miden las mismas mezclas que dibuja el navegador.
      expect(estilos.match(/color-mix\([^;]*\d%/g)).toBeNull();
      const fondoAviso = mezcla(primario, porcentaje('--mezcla-aviso'), '#FFFFFF');
      const hoverPrimario = mezcla(primario, porcentaje('--mezcla-hover-primario'), '#000000');
      const hoverSecundario = mezcla(primario, porcentaje('--mezcla-hover-secundario'), '#FFFFFF');
      const encabezadoTabla = mezcla(primario, porcentaje('--mezcla-encabezado-tabla'), '#FFFFFF');
      const hoverFila = mezcla(primario, porcentaje('--mezcla-hover-fila'), '#FFFFFF');
      // El contenido de todas las pantallas va sobre la tarjeta blanca (FR-09).
      expect(tarjeta).toBe('#FFFFFF');

      // Mensajes (FR-13, D-17). Error: texto y "✕" en --color-error sobre --fondo-error.
      expect(contraste(error, fondoError)).toBeGreaterThanOrEqual(4.5);
      // Confirmación: texto y "✓" en --color-exito sobre --fondo-exito.
      expect(contraste(exito, fondoExito)).toBeGreaterThanOrEqual(4.5);
      // Aviso y estado: --color-texto sobre color-mix(primario --mezcla-aviso, blanco); la "ℹ" va en el primario.
      expect(contraste(texto, fondoAviso)).toBeGreaterThanOrEqual(4.5);
      expect(contraste(primario, fondoAviso)).toBeGreaterThanOrEqual(4.5);
      // Los enlaces dentro de un mensaje ("Ir a la configuración") van en el primario sobre el fondo del bloque.
      expect(contraste(primario, fondoError)).toBeGreaterThanOrEqual(4.5);
      expect(contraste(primario, fondoExito)).toBeGreaterThanOrEqual(4.5);
      // El error junto al campo (.campo__error): texto chico en --color-error sobre la tarjeta.
      expect(contraste(error, tarjeta)).toBeGreaterThanOrEqual(4.5);

      // Campos (FR-10, D-18). El borde del campo y el de error sobre la tarjeta (1.4.11).
      expect(contraste(variable('--borde-campo'), tarjeta)).toBeGreaterThanOrEqual(3);
      expect(contraste(error, tarjeta)).toBeGreaterThanOrEqual(3);
      // El contorno del foco, del primario, sobre la tarjeta.
      expect(contraste(primario, tarjeta)).toBeGreaterThanOrEqual(3);
      // El texto de un campo de solo lectura sobre su fondo gris.
      expect(contraste(texto, variable('--fondo-solo-lectura'))).toBeGreaterThanOrEqual(4.5);

      // Botones (FR-11). Principal: blanco sobre el primario y sobre su hover,
      // color-mix(primario --mezcla-hover-primario, negro).
      expect(contraste('#FFFFFF', primario)).toBeGreaterThanOrEqual(4.5);
      expect(contraste('#FFFFFF', hoverPrimario)).toBeGreaterThanOrEqual(4.5);
      // Secundario (y el botón del selector de archivo, y el círculo del ícono del menú): el primario sobre blanco y
      // sobre color-mix(primario --mezcla-hover-secundario, blanco).
      expect(contraste(primario, tarjeta)).toBeGreaterThanOrEqual(4.5);
      expect(contraste(primario, hoverSecundario)).toBeGreaterThanOrEqual(4.5);

      // Tablas (FR-12). Encabezado: --color-texto sobre color-mix(primario --mezcla-encabezado-tabla, blanco); fila con
      // el puntero encima: sobre color-mix(primario --mezcla-hover-fila, blanco).
      expect(contraste(texto, encabezadoTabla)).toBeGreaterThanOrEqual(4.5);
      expect(contraste(texto, hoverFila)).toBeGreaterThanOrEqual(4.5);

      // Títulos: el h1 y el h2 de las pantallas en el primario sobre la tarjeta, sin opacidad; el texto fuera de la
      // tarjeta, sobre el fondo de la marca.
      expect(contraste(primario, tarjeta)).toBeGreaterThanOrEqual(4.5);
      expect(contraste(texto, fondo)).toBeGreaterThanOrEqual(4.5);
    });
  });

  describe('control de las ayudas de la prueba (no es una prueba requerida)', () => {
    it('mezcla calcula color-mix en sRGB', () => {
      // Valores hechos a mano (ver contraste_PaletasConocidas_TextoYFoco): 0,1·0x5A + 0,9·0xFF = 238,5 → 239 (0xEF).
      expect(mezcla('#5A5A5A', 10, '#FFFFFF')).toBe('#EFEFEF');
      expect(mezcla('#0903A0', 10, '#FFFFFF')).toBe('#E6E6F6');
      expect(mezcla('#0903A0', 85, '#000000')).toBe('#080388');
    });
  });

  describe('contraste_ColorInvalido_Rechazado', () => {
    it.each(['#fff', 'red', '#GGGGGG', '', '#1F1F1F ', '1F1F1F0', '#1F1F1F1'])('%j', (invalido) => {
      expect(() => contraste(invalido, '#FFFFFF')).toThrow(RangeError);
      expect(() => contraste('#FFFFFF', invalido)).toThrow(RangeError);
      // El mensaje no repite el valor recibido.
      try {
        contraste(invalido, '#FFFFFF');
      } catch (error) {
        expect(error).toBeInstanceOf(RangeError);
        if (invalido !== '') {
          expect((error as RangeError).message).not.toContain(invalido);
        }
      }
    });
  });
});
