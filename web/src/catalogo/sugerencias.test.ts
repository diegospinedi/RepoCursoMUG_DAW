import { describe, expect, it } from 'vitest';
import { esBuscableEnVivo, mostrarSugerencia } from './sugerencias';

describe('sugerencias de artículos (FEAT-013)', () => {
  it('mostrarSugerencia_CodigoProveedorYDescripcion', () => {
    // FR-02: código, código en el proveedor y descripción, como texto.
    expect(
      mostrarSugerencia({
        codigo: 7,
        codigoProveedor: 'ABC-1',
        descripcion: '<b>Armazón</b> metálico',
        precioVenta: 1815,
        activo: true,
      }),
    ).toBe('7 · ABC-1 · <b>Armazón</b> metálico');
  });

  it.each([
    ['vacío', '', false],
    ['solo espacios', '   ', false],
    ['1 carácter', 'a', false],
    ['1 carácter con espacios', '  a ', false],
    ['2 caracteres', 'ab', true],
    ['2 caracteres con espacios', ' ab ', true],
    ['200 caracteres', 'a'.repeat(200), true],
    ['201 caracteres', 'a'.repeat(201), false],
    ['UTF-16 inválido', `ab${String.fromCharCode(0xd83d)}`, false],
  ])('esBuscableEnVivo_MinimoDosYTextoValido (%s)', (_caso, texto, esperado) => {
    // AC-02 y D-07: con menos de 2 caracteres recortados, o con un texto que la Api rechazaría, no se busca en vivo.
    expect(esBuscableEnVivo(texto)).toBe(esperado);
  });
});
