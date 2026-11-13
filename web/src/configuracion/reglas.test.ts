import { describe, expect, it } from 'vitest';
import {
  IMPORTE_MAXIMO_SEGURO,
  MENSAJE_ALICUOTA_NO_ADMITIDA,
  MENSAJE_CONDICION_FISCAL,
  MENSAJE_HASTA_DOS_DECIMALES,
  MENSAJE_MULTIPLO_FALTANTE,
  MENSAJE_MULTIPLO_MINIMO,
  MENSAJE_NO_ES_NUMERO,
  MENSAJE_PUNTO_VENTA,
  MENSAJE_TOPE_NO_POSITIVO,
  MENSAJE_VALOR_MENOR,
  validarConfiguracion,
  type TextosConfiguracion,
} from './reglas';

const INICIALES: TextosConfiguracion = {
  alicuotaIva: '21',
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: '0,01',
  topeIdentificacion: '',
  puntoVenta: '',
};

/** Las 6 alícuotas que acepta ARCA, como las devuelve `GET /api/configuracion` (FR-26). */
const ADMITIDAS = [0, 2.5, 5, 10.5, 21, 27];

const con = (cambios: Partial<TextosConfiguracion>) => validarConfiguracion({ ...INICIALES, ...cambios }, ADMITIDAS);

/** El mensaje de un campo, o `undefined` si el campo es válido. */
function errorDe(cambios: Partial<TextosConfiguracion>, campo: keyof TextosConfiguracion): string | undefined {
  const resultado = con(cambios);
  return resultado.tipo === 'errores' ? resultado.errores[campo] : undefined;
}

describe('reglas de la configuración', () => {
  it('reglas_MensajesIgualesALosDeLaApi', () => {
    // Los mismos textos que ValidacionConfiguracion.cs (Block 3), más los dos propios del navegador.
    expect(MENSAJE_ALICUOTA_NO_ADMITIDA).toBe('La alícuota de IVA debe ser 0, 2,5, 5, 10,5, 21 o 27.');
    expect(MENSAJE_PUNTO_VENTA).toBe('El punto de venta debe ser un número entero de 1 a 99999.');
    expect(MENSAJE_HASTA_DOS_DECIMALES).toBe('Ingresá hasta 2 decimales.');
    expect(MENSAJE_CONDICION_FISCAL).toBe('Elegí Responsable Inscripto o Monotributo.');
    expect(MENSAJE_MULTIPLO_FALTANTE).toBe('Ingresá el múltiplo de redondeo comercial.');
    expect(MENSAJE_MULTIPLO_MINIMO).toBe('El valor mínimo es 0,01.');
    expect(MENSAJE_TOPE_NO_POSITIVO).toBe('El tope debe ser mayor a 0.');
    expect(MENSAJE_VALOR_MENOR).toBe('Ingresá un valor menor.');
    expect(MENSAJE_NO_ES_NUMERO).toBe('Ingresá un número.');
  });

  it('reglas_ValoresIniciales_ValidosSinTope', () => {
    expect(validarConfiguracion(INICIALES, ADMITIDAS)).toEqual({
      tipo: 'ok',
      datos: {
        alicuotaIva: 21,
        condicionFiscal: 'responsable_inscripto',
        multiploRedondeo: 0.01,
        topeIdentificacion: null,
        puntoVenta: null,
      },
    });
  });

  it('reglas_ConTopeYMonotributo_DevuelveLosNumeros', () => {
    expect(con({ alicuotaIva: '10,5', condicionFiscal: 'monotributo', multiploRedondeo: '10', topeIdentificacion: '250000.50' })).toEqual({
      tipo: 'ok',
      datos: {
        alicuotaIva: 10.5,
        condicionFiscal: 'monotributo',
        multiploRedondeo: 10,
        topeIdentificacion: 250000.5,
        puntoVenta: null,
      },
    });
  });

  it('reglas_AlicuotaDeLaLista_Valida', () => {
    // Las 6 de la lista, como las muestra la pantalla (coma decimal), y cada una vuelve como número (FR-26).
    for (const [texto, valor] of [
      ['0', 0],
      ['2,5', 2.5],
      ['5', 5],
      ['10,5', 10.5],
      ['21', 21],
      ['27', 27],
    ] as const) {
      const resultado = con({ alicuotaIva: texto });
      expect(resultado.tipo === 'ok' ? resultado.datos.alicuotaIva : undefined).toBe(valor);
    }
  });

  it('reglas_AlicuotaFueraDeLaLista_MensajeDeLasSeis', () => {
    // D-35: sin opción elegida, una alícuota fuera de la lista o un texto que no es número, un único mensaje.
    for (const texto of ['', '19', '100', '101', '-1', '21,555', 'abc']) {
      expect(errorDe({ alicuotaIva: texto }, 'alicuotaIva'), texto).toBe(MENSAJE_ALICUOTA_NO_ADMITIDA);
    }
  });

  it('reglas_AlicuotaSoloDeLaListaRecibida', () => {
    // La lista la da la Api: una alícuota que no está en la recibida no se envía.
    const resultado = validarConfiguracion({ ...INICIALES, alicuotaIva: '27' }, [21]);
    expect(resultado).toEqual({ tipo: 'errores', errores: { alicuotaIva: MENSAJE_ALICUOTA_NO_ADMITIDA } });
  });

  it('reglas_PuntoVenta_EnteroDe1A99999', () => {
    const puntoDe = (texto: string) => {
      const resultado = con({ puntoVenta: texto });
      return resultado.tipo === 'ok' ? resultado.datos.puntoVenta : undefined;
    };
    expect(puntoDe('1')).toBe(1);
    expect(puntoDe('3')).toBe(3);
    expect(puntoDe(' 0003 ')).toBe(3);
    expect(puntoDe('99999')).toBe(99999);
    // Vacío conserva el guardado (D-27): se envía null.
    expect(puntoDe('')).toBeNull();
    expect(puntoDe('   ')).toBeNull();
  });

  it('punto de venta 0, 100000 y 2,5: inválido sin enviar', () => {
    // AC-31, más los demás textos que no son un entero de 1 a 99999.
    for (const texto of ['0', '100000', '2,5', '2.5', '-1', '1e3', 'abc', '3,0']) {
      expect(con({ puntoVenta: texto }), texto).toEqual({
        tipo: 'errores',
        errores: { puntoVenta: MENSAJE_PUNTO_VENTA },
      });
    }
  });

  it('reglas_Condicion_SoloLasDosOpciones', () => {
    expect(errorDe({ condicionFiscal: 'Monotributo' }, 'condicionFiscal')).toBe(MENSAJE_CONDICION_FISCAL);
    expect(errorDe({ condicionFiscal: '' }, 'condicionFiscal')).toBe(MENSAJE_CONDICION_FISCAL);
    expect(errorDe({ condicionFiscal: 'otro' }, 'condicionFiscal')).toBe(MENSAJE_CONDICION_FISCAL);
  });

  it('reglas_Multiplo_Rechazos', () => {
    expect(errorDe({ multiploRedondeo: '' }, 'multiploRedondeo')).toBe(MENSAJE_MULTIPLO_FALTANTE);
    expect(errorDe({ multiploRedondeo: 'x' }, 'multiploRedondeo')).toBe(MENSAJE_NO_ES_NUMERO);
    // D-01: el mínimo antes que la escala.
    expect(errorDe({ multiploRedondeo: '0,001' }, 'multiploRedondeo')).toBe(MENSAJE_MULTIPLO_MINIMO);
    expect(errorDe({ multiploRedondeo: '0' }, 'multiploRedondeo')).toBe(MENSAJE_MULTIPLO_MINIMO);
    expect(errorDe({ multiploRedondeo: '0,015' }, 'multiploRedondeo')).toBe(MENSAJE_HASTA_DOS_DECIMALES);
    expect(errorDe({ multiploRedondeo: '0.010' }, 'multiploRedondeo')).toBeUndefined();
  });

  it('reglas_Tope_Rechazos', () => {
    expect(errorDe({ topeIdentificacion: 'abc' }, 'topeIdentificacion')).toBe(MENSAJE_NO_ES_NUMERO);
    expect(errorDe({ topeIdentificacion: '0' }, 'topeIdentificacion')).toBe(MENSAJE_TOPE_NO_POSITIVO);
    expect(errorDe({ topeIdentificacion: '50,005' }, 'topeIdentificacion')).toBe(MENSAJE_HASTA_DOS_DECIMALES);
    expect(errorDe({ topeIdentificacion: '0,01' }, 'topeIdentificacion')).toBeUndefined();
  });

  it('reglas_ImporteSobreElMaximoSeguro_Rechazado', () => {
    expect(IMPORTE_MAXIMO_SEGURO).toBe(Number.MAX_SAFE_INTEGER / 100);
    expect(errorDe({ multiploRedondeo: '90071992547410' }, 'multiploRedondeo')).toBe(MENSAJE_VALOR_MENOR);
    expect(errorDe({ topeIdentificacion: '90071992547410' }, 'topeIdentificacion')).toBe(MENSAJE_VALOR_MENOR);
    expect(errorDe({ topeIdentificacion: '99999999999999999999999999999999' }, 'topeIdentificacion')).toBe(
      MENSAJE_VALOR_MENOR,
    );
    // El máximo seguro mismo, con sus centavos, se acepta.
    expect(errorDe({ topeIdentificacion: '90071992547409,91' }, 'topeIdentificacion')).toBeUndefined();
  });

  it('reglas_Negativos_MensajeDeLaReglaDelCampo', () => {
    expect(errorDe({ alicuotaIva: '-1' }, 'alicuotaIva')).toBe(MENSAJE_ALICUOTA_NO_ADMITIDA);
    expect(errorDe({ multiploRedondeo: '-1' }, 'multiploRedondeo')).toBe(MENSAJE_MULTIPLO_MINIMO);
    expect(errorDe({ topeIdentificacion: '-1' }, 'topeIdentificacion')).toBe(MENSAJE_TOPE_NO_POSITIVO);
  });

  it('reglas_VariosCamposInvalidos_UnErrorPorCampo', () => {
    expect(con({ alicuotaIva: '101', multiploRedondeo: '0,001', topeIdentificacion: '0', puntoVenta: '0' })).toEqual({
      tipo: 'errores',
      errores: {
        alicuotaIva: MENSAJE_ALICUOTA_NO_ADMITIDA,
        multiploRedondeo: MENSAJE_MULTIPLO_MINIMO,
        topeIdentificacion: MENSAJE_TOPE_NO_POSITIVO,
        puntoVenta: MENSAJE_PUNTO_VENTA,
      },
    });
  });
});
