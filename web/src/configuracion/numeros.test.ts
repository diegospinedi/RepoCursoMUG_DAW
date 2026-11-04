import { describe, expect, it } from 'vitest';
import { leerDecimal, mostrarDecimal, mostrarImporte } from './numeros';
import { IMPORTE_MAXIMO_SEGURO } from './reglas';

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

  it('leerDecimal_Negativos_Validos', () => {
    // El signo menos se lee: así las reglas del dominio informan el rango ("-1" no es "no es un número").
    expect(leerDecimal('-1')).toEqual({ tipo: 'numero', valor: -1, decimales: 0 });
    expect(leerDecimal('-0,5')).toEqual({ tipo: 'numero', valor: -0.5, decimales: 1 });
    expect(leerDecimal('-0.015')).toEqual({ tipo: 'numero', valor: -0.015, decimales: 3 });
  });

  it.each(['-', '--1', '- 1', '+1', '-+1', '1-', '-,5'])('leerDecimal_SignoMal_%s_Invalido', (texto) => {
    expect(leerDecimal(texto)).toEqual({ tipo: 'invalido' });
  });

  it.each(['1,2,3', '1.000.000', '10,', ',5', '1e3', '0x10', '1 000', '12a', 'Infinity', 'NaN'])(
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

describe('mostrarImporte', () => {
  it('mostrarImporte_MilesYDecimales', () => {
    // D-11: 2 decimales, coma decimal y punto de miles.
    expect(mostrarImporte(1850)).toBe('1.850,00');
    expect(mostrarImporte(-150)).toBe('-150,00');
    expect(mostrarImporte(1666.66)).toBe('1.666,66');
  });

  it.each([
    [0, '0,00'],
    [0.5, '0,50'],
    [999, '999,00'],
    [1000, '1.000,00'],
    [-1000.5, '-1.000,50'],
    [1234567.89, '1.234.567,89'],
    [0.1 + 0.2, '0,30'],
    [-0, '0,00'],
    [0.005, '0,01'],
    [-0.004, '0,00'],
    [1e-7, '0,00'],
  ])('mostrarImporte_%d_%s', (valor, texto) => {
    expect(mostrarImporte(valor)).toBe(texto);
  });

  it.each([
    // Mayores a 2^45/100 y a 2^45: ahí multiplicar por 100 en punto flotante ya redondea al centavo equivocado.
    [351843720888.33, '351.843.720.888,33'],
    [1234567890123.45, '1.234.567.890.123,45'],
    [12345678901234.56, '12.345.678.901.234,56'],
    [38779820885963.52, '38.779.820.885.963,52'],
    [-38779820885963.52, '-38.779.820.885.963,52'],
    [45035996273704.95, '45.035.996.273.704,95'],
    [70368744177663.99, '70.368.744.177.663,99'],
    [88888888888888.88, '88.888.888.888.888,88'],
  ])('mostrarImporte_ImporteGrande_%d_CentavosExactos', (valor, texto) => {
    expect(mostrarImporte(valor)).toBe(texto);
  });

  it('mostrarImporte_ImporteMaximoSeguro_ComoLoEnviaJson', () => {
    // Por encima de 2^46 dos centavos seguidos pueden ser el mismo `number`: 90071992547409,90 y ,91 lo son. Se
    // muestra lo que JSON.stringify envía a la Api para ese número, que es lo que la Api guarda.
    expect(JSON.stringify(IMPORTE_MAXIMO_SEGURO)).toBe('90071992547409.9');
    expect(mostrarImporte(IMPORTE_MAXIMO_SEGURO)).toBe('90.071.992.547.409,90');
    expect(mostrarImporte(-IMPORTE_MAXIMO_SEGURO)).toBe('-90.071.992.547.409,90');
  });

  it.each([
    [1e21, '1.000.000.000.000.000.000.000,00'],
    [-1e22, '-10.000.000.000.000.000.000.000,00'],
  ])('mostrarImporte_NotacionExponencial_%d', (valor, texto) => {
    // Fuera del rango de la pantalla, pero sin romper el formato.
    expect(mostrarImporte(valor)).toBe(texto);
  });
});
