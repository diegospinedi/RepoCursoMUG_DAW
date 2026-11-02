import { describe, expect, it } from 'vitest';
import { MENSAJE_MUY_CORTA, validarContrasena } from './politicaContrasena';

describe('politicaContrasena', () => {
  it('Politica_SieteEspaciosYUnaLetra_SeAcepta', () => {
    // ADR-004: los espacios cuentan y no se recortan. 7 espacios + 1 letra = 8 caracteres.
    expect(validarContrasena('       a')).toBeNull();
  });

  it('Politica_SieteCaracteresConEspaciosALosLados_SeRechaza', () => {
    // Los espacios cuentan, pero no alcanzan: 7 caracteres en total no llegan al mínimo de 8.
    expect(validarContrasena(' abcde ')).toBe(MENSAJE_MUY_CORTA);
  });
});
