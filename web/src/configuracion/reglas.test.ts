import { describe, expect, it } from 'vitest';
import {
  IMPORTE_MAXIMO_SEGURO,
  MENSAJE_ALICUOTA_FALTANTE,
  MENSAJE_ALICUOTA_FUERA_DE_RANGO,
  MENSAJE_CONDICION_FISCAL,
  MENSAJE_HASTA_DOS_DECIMALES,
  MENSAJE_MULTIPLO_FALTANTE,
  MENSAJE_MULTIPLO_MINIMO,
  MENSAJE_NO_ES_NUMERO,
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
};

const con = (cambios: Partial<TextosConfiguracion>) => validarConfiguracion({ ...INICIALES, ...cambios });

/** El mensaje de un campo, o `undefined` si el campo es válido. */
function errorDe(cambios: Partial<TextosConfiguracion>, campo: keyof TextosConfiguracion): string | undefined {
  const resultado = con(cambios);
  return resultado.tipo === 'errores' ? resultado.errores[campo] : undefined;
}

describe('reglas de la configuración', () => {
  it('reglas_MensajesIgualesALosDeLaApi', () => {
    // Los mismos textos que ValidacionConfiguracion.cs (Block 3), más los dos propios del navegador.
    expect(MENSAJE_ALICUOTA_FALTANTE).toBe('Ingresá la alícuota de IVA.');
    expect(MENSAJE_ALICUOTA_FUERA_DE_RANGO).toBe('La alícuota de IVA debe estar entre 0 y 100.');
    expect(MENSAJE_HASTA_DOS_DECIMALES).toBe('Ingresá hasta 2 decimales.');
    expect(MENSAJE_CONDICION_FISCAL).toBe('Elegí Responsable Inscripto o Monotributo.');
    expect(MENSAJE_MULTIPLO_FALTANTE).toBe('Ingresá el múltiplo de redondeo comercial.');
    expect(MENSAJE_MULTIPLO_MINIMO).toBe('El valor mínimo es 0,01.');
    expect(MENSAJE_TOPE_NO_POSITIVO).toBe('El tope debe ser mayor a 0.');
    expect(MENSAJE_VALOR_MENOR).toBe('Ingresá un valor menor.');
    expect(MENSAJE_NO_ES_NUMERO).toBe('Ingresá un número.');
  });

  it('reglas_ValoresIniciales_ValidosSinTope', () => {
    expect(validarConfiguracion(INICIALES)).toEqual({
      tipo: 'ok',
      datos: { alicuotaIva: 21, condicionFiscal: 'responsable_inscripto', multiploRedondeo: 0.01, topeIdentificacion: null },
    });
  });

  it('reglas_ConTopeYMonotributo_DevuelveLosNumeros', () => {
    expect(con({ alicuotaIva: '10,5', condicionFiscal: 'monotributo', multiploRedondeo: '10', topeIdentificacion: '250000.50' })).toEqual({
      tipo: 'ok',
      datos: { alicuotaIva: 10.5, condicionFiscal: 'monotributo', multiploRedondeo: 10, topeIdentificacion: 250000.5 },
    });
  });

  it('reglas_AlicuotaBordes_Validos', () => {
    expect(con({ alicuotaIva: '0' }).tipo).toBe('ok');
    expect(con({ alicuotaIva: '100' }).tipo).toBe('ok');
    expect(con({ alicuotaIva: '21,500' }).tipo).toBe('ok');
  });

  it('reglas_Alicuota_Rechazos', () => {
    expect(errorDe({ alicuotaIva: '' }, 'alicuotaIva')).toBe(MENSAJE_ALICUOTA_FALTANTE);
    expect(errorDe({ alicuotaIva: 'abc' }, 'alicuotaIva')).toBe(MENSAJE_NO_ES_NUMERO);
    expect(errorDe({ alicuotaIva: '101' }, 'alicuotaIva')).toBe(MENSAJE_ALICUOTA_FUERA_DE_RANGO);
    expect(errorDe({ alicuotaIva: '100,01' }, 'alicuotaIva')).toBe(MENSAJE_ALICUOTA_FUERA_DE_RANGO);
    expect(errorDe({ alicuotaIva: '21,555' }, 'alicuotaIva')).toBe(MENSAJE_HASTA_DOS_DECIMALES);
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

  it('reglas_VariosCamposInvalidos_UnErrorPorCampo', () => {
    expect(con({ alicuotaIva: '101', multiploRedondeo: '0,001', topeIdentificacion: '0' })).toEqual({
      tipo: 'errores',
      errores: {
        alicuotaIva: MENSAJE_ALICUOTA_FUERA_DE_RANGO,
        multiploRedondeo: MENSAJE_MULTIPLO_MINIMO,
        topeIdentificacion: MENSAJE_TOPE_NO_POSITIVO,
      },
    });
  });
});
