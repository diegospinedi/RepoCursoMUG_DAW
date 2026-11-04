import { describe, expect, it } from 'vitest';
import { leerDecimal, mostrarDecimal } from './numeros';

describe('leerDecimal', () => {
  it('leerDecimal_ComaYPunto_Validos', () => {
    expect(leerDecimal('10,5')).toEqual({ tipo: 'numero', valor: 10.5, decimales: 1 });
    expect(leerDecimal('10.5')).toEqual({ tipo: 'numero', valor: 10.5, decimales: 1 });
  });

  it('leerDecimal_Entero_Valido', () => {
    expect(leerDecimal('21')).toEqual({ tipo: 'numero', valor: 21, decimales: 0 });
    expect(leerDecimal('0')).toEqual({ tipo: 'numero', valor: 0, decimales: 0 });
  });

  it('leerDecimal_CerosALaDerecha_NoCuentanComoDecimales', () => {
    // Como en el dominio: 21,500 y 0,010 tienen 2 decimales o menos.
    expect(leerDecimal('21,500')).toEqual({ tipo: 'numero', valor: 21.5, decimales: 1 });
    expect(leerDecimal('0.010')).toEqual({ tipo: 'numero', valor: 0.01, decimales: 2 });
  });

  it('leerDecimal_CuentaLosDecimalesDelTexto', () => {
    expect(leerDecimal('0,001')).toEqual({ tipo: 'numero', valor: 0.001, decimales: 3 });
    expect(leerDecimal('50,005')).toEqual({ tipo: 'numero', valor: 50.005, decimales: 3 });
  });

  it('leerDecimal_Vacio_SinValor', () => {
    expect(leerDecimal('')).toEqual({ tipo: 'vacio' });
    expect(leerDecimal('   ')).toEqual({ tipo: 'vacio' });
  });

  it('leerDecimal_EspaciosAlrededor_SeIgnoran', () => {
    expect(leerDecimal(' 21 ')).toEqual({ tipo: 'numero', valor: 21, decimales: 0 });
  });

  it('leerDecimal_Texto_Invalido', () => {
    expect(leerDecimal('abc')).toEqual({ tipo: 'invalido' });
    expect(leerDecimal('1.000,5')).toEqual({ tipo: 'invalido' });
    expect(leerDecimal('+3')).toEqual({ tipo: 'invalido' });
  });

  it.each(['-3', '1,2,3', '1.000.000', '10,', ',5', '1e3', '0x10', '1 000', '12a', 'Infinity', 'NaN'])(
    'leerDecimal_%s_Invalido',
    (texto) => {
      expect(leerDecimal(texto)).toEqual({ tipo: 'invalido' });
    },
  );
});

describe('mostrarDecimal', () => {
  it('mostrarDecimal_ConDecimales_UsaComa', () => {
    expect(mostrarDecimal(0.01)).toBe('0,01');
    expect(mostrarDecimal(10.5)).toBe('10,5');
  });

  it('mostrarDecimal_Entero_SinDecimales', () => {
    expect(mostrarDecimal(21)).toBe('21');
    expect(mostrarDecimal(200000)).toBe('200000');
  });

  it('mostrarDecimal_SinValor_TextoVacio', () => {
    expect(mostrarDecimal(null)).toBe('');
  });
});
