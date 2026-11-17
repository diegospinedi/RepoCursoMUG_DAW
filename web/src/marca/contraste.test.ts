import { describe, expect, it } from 'vitest';
import { contraste } from './contraste';

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
