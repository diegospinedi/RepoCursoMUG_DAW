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
  cuitCanonico,
  hoyLocal,
  MENSAJE_CUIT_FALTANTE,
  MENSAJE_CUIT_INVALIDO,
  MENSAJE_DOMICILIO_COMERCIAL_CARACTERES,
  MENSAJE_DOMICILIO_COMERCIAL_FALTANTE,
  MENSAJE_DOMICILIO_COMERCIAL_LARGO,
  MENSAJE_INGRESOS_BRUTOS_CARACTERES,
  MENSAJE_INGRESOS_BRUTOS_FALTANTE,
  MENSAJE_INGRESOS_BRUTOS_LARGO,
  MENSAJE_INICIO_ACTIVIDADES_FALTANTE,
  MENSAJE_INICIO_ACTIVIDADES_INVALIDA,
  MENSAJE_INICIO_ACTIVIDADES_POSTERIOR,
  MENSAJE_RAZON_SOCIAL_CARACTERES,
  MENSAJE_RAZON_SOCIAL_FALTANTE,
  MENSAJE_RAZON_SOCIAL_LARGA,
  validarDatosEmisor,
  type TextosDatosEmisor,
  LARGO_MAXIMO_RUTA_BACKUP,
  MENSAJE_RUTA_BACKUP_FALTANTE,
  MENSAJE_RUTA_BACKUP_LARGA,
  validarRutaBackup,
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

// ---- FEAT-009: los datos del emisor (FR-01 a FR-03), espejo de DatosEmisor, Cuit y ValidacionConfiguracion ----

describe('reglas de los datos del emisor', () => {
  /** Datos ficticios (D-33): el CUIT 20-00000000-1 tiene dígito verificador válido y no es de nadie. */
  const VALIDOS: TextosDatosEmisor = {
    razonSocial: 'Óptica de Prueba S.A.',
    domicilioComercial: 'Calle Falsa 123, Ciudad',
    cuit: '20-00000000-1',
    ingresosBrutos: '901-000000-1',
    inicioActividades: '2020-03-01',
  };
  const HOY = '2026-11-16';

  const emisor = (cambios: Partial<TextosDatosEmisor>) => validarDatosEmisor({ ...VALIDOS, ...cambios }, HOY);

  function errorEmisor(cambios: Partial<TextosDatosEmisor>, campo: keyof TextosDatosEmisor): string | undefined {
    const resultado = emisor(cambios);
    return resultado.tipo === 'errores' ? resultado.errores[campo] : undefined;
  }

  it('Reglas_Cuit_DigitoVerificador', () => {
    // AC-03: con guiones se admite y queda sin guiones (el CUIT del PRD solo en AC-03 y AC-04, D-33).
    expect(cuitCanonico('20-12345678-6')).toBe('20123456786');
    expect(cuitCanonico('20123456786')).toBe('20123456786');
    expect(cuitCanonico('20000000001')).toBe('20000000001');
    expect(cuitCanonico(' 20-00000000-1 ')).toBe('20000000001');
    // Guiones en cualquier posición, como Cuit.Interpretar.
    expect(cuitCanonico('2-0-0-0-0-0-0-0-0-0-1')).toBe('20000000001');
    // AC-04: el dígito verificador no corresponde, o no son 11 dígitos.
    expect(cuitCanonico('20-12345678-5')).toBeNull();
    expect(cuitCanonico('2012345678')).toBeNull();
    expect(cuitCanonico('201234567860')).toBeNull();
    // Verificador 10: ningún dígito lo representa (2·5 + 6·4 = 34, resto 1).
    for (let digito = 0; digito <= 9; digito++) {
      expect(cuitCanonico(`2600000000${digito}`)).toBeNull();
    }
    // Ningún otro carácter: espacios internos, puntos, letras, dígitos no ASCII.
    expect(cuitCanonico('20 00000000 1')).toBeNull();
    expect(cuitCanonico('20.00000000.1')).toBeNull();
    expect(cuitCanonico('2000000000a')).toBeNull();
    expect(cuitCanonico('٢٠٠٠٠٠٠٠٠٠١')).toBeNull();
    expect(cuitCanonico('')).toBeNull();
    expect(cuitCanonico('---')).toBeNull();

    // En la validación: inválido junto al campo con el texto de la Api; válido se envía sin guiones.
    expect(errorEmisor({ cuit: '20-12345678-5' }, 'cuit')).toBe(MENSAJE_CUIT_INVALIDO);
    expect(errorEmisor({ cuit: '2012345678' }, 'cuit')).toBe(MENSAJE_CUIT_INVALIDO);
    expect(emisor({ cuit: '20-12345678-6' })).toEqual({ tipo: 'ok', datos: { ...VALIDOS, cuit: '20123456786' } });
  });

  it('reglasEmisor_MensajesIgualesALosDeLaApi', () => {
    // Los mismos textos que ValidacionConfiguracion.cs (Block 2 de FEAT-009).
    expect(MENSAJE_RAZON_SOCIAL_FALTANTE).toBe('Ingresá la razón social.');
    expect(MENSAJE_RAZON_SOCIAL_CARACTERES).toBe(
      'Escribí la razón social en una sola línea, sin caracteres especiales.',
    );
    expect(MENSAJE_RAZON_SOCIAL_LARGA).toBe('La razón social puede tener hasta 120 caracteres.');
    expect(MENSAJE_DOMICILIO_COMERCIAL_FALTANTE).toBe('Ingresá el domicilio comercial.');
    expect(MENSAJE_DOMICILIO_COMERCIAL_CARACTERES).toBe(
      'Escribí el domicilio comercial en una sola línea, sin caracteres especiales.',
    );
    expect(MENSAJE_DOMICILIO_COMERCIAL_LARGO).toBe('El domicilio comercial puede tener hasta 200 caracteres.');
    expect(MENSAJE_CUIT_FALTANTE).toBe('Ingresá el CUIT.');
    expect(MENSAJE_CUIT_INVALIDO).toBe('El CUIT no es válido: tienen que ser 11 dígitos, con o sin guiones.');
    expect(MENSAJE_INGRESOS_BRUTOS_FALTANTE).toBe('Ingresá el número de Ingresos Brutos.');
    expect(MENSAJE_INGRESOS_BRUTOS_CARACTERES).toBe(
      'Escribí el número de Ingresos Brutos en una sola línea, sin caracteres especiales.',
    );
    expect(MENSAJE_INGRESOS_BRUTOS_LARGO).toBe('El número de Ingresos Brutos puede tener hasta 30 caracteres.');
    expect(MENSAJE_INICIO_ACTIVIDADES_FALTANTE).toBe('Ingresá la fecha de inicio de actividades.');
    expect(MENSAJE_INICIO_ACTIVIDADES_INVALIDA).toBe('La fecha de inicio de actividades no es válida.');
    expect(MENSAJE_INICIO_ACTIVIDADES_POSTERIOR).toBe('La fecha de inicio de actividades no puede ser posterior a hoy.');
  });

  it('reglasEmisor_Validos_DatosRecortados', () => {
    expect(validarDatosEmisor(VALIDOS, HOY)).toEqual({ tipo: 'ok', datos: { ...VALIDOS, cuit: '20000000001' } });
    // El recorte del servidor: espacios, la marca de orden de bytes y U+0085 al principio y al final; los internos quedan.
    expect(
      emisor({
        razonSocial: '﻿  Óptica  de Prueba \u0085',
        domicilioComercial: ' Calle Falsa 123\t',
        ingresosBrutos: ' 901 ',
        inicioActividades: ' 2020-03-01 ',
      }),
    ).toEqual({
      tipo: 'ok',
      datos: {
        razonSocial: 'Óptica  de Prueba',
        domicilioComercial: 'Calle Falsa 123',
        cuit: '20000000001',
        ingresosBrutos: '901',
        inicioActividades: '2020-03-01',
      },
    });
  });

  it('reglasEmisor_CincoFaltantes_UnErrorPorCampo', () => {
    expect(
      validarDatosEmisor(
        { razonSocial: '', domicilioComercial: '   ', cuit: ' ', ingresosBrutos: '﻿', inicioActividades: '' },
        HOY,
      ),
    ).toEqual({
      tipo: 'errores',
      errores: {
        razonSocial: MENSAJE_RAZON_SOCIAL_FALTANTE,
        domicilioComercial: MENSAJE_DOMICILIO_COMERCIAL_FALTANTE,
        cuit: MENSAJE_CUIT_FALTANTE,
        ingresosBrutos: MENSAJE_INGRESOS_BRUTOS_FALTANTE,
        inicioActividades: MENSAJE_INICIO_ACTIVIDADES_FALTANTE,
      },
    });
  });

  it('reglasEmisor_SinRazonSocial_SoloEseCampo', () => {
    expect(emisor({ razonSocial: '' })).toEqual({
      tipo: 'errores',
      errores: { razonSocial: MENSAJE_RAZON_SOCIAL_FALTANTE },
    });
  });

  it.each([
    ['razonSocial', 120, MENSAJE_RAZON_SOCIAL_LARGA],
    ['domicilioComercial', 200, MENSAJE_DOMICILIO_COMERCIAL_LARGO],
    ['ingresosBrutos', 30, MENSAJE_INGRESOS_BRUTOS_LARGO],
  ] as const)('reglasEmisor_Largo_%s', (campo, maximo, mensaje) => {
    expect(errorEmisor({ [campo]: 'a'.repeat(maximo) }, campo)).toBeUndefined();
    // Se mide recortado.
    expect(errorEmisor({ [campo]: ` ${'a'.repeat(maximo)} ` }, campo)).toBeUndefined();
    expect(errorEmisor({ [campo]: 'a'.repeat(maximo + 1) }, campo)).toBe(mensaje);
    // En unidades UTF-16, como string.Length: un emoji cuenta 2.
    expect(errorEmisor({ [campo]: `${'a'.repeat(maximo - 2)}😀` }, campo)).toBeUndefined();
    expect(errorEmisor({ [campo]: `${'a'.repeat(maximo - 1)}😀` }, campo)).toBe(mensaje);
  });

  it.each([
    ['razonSocial', MENSAJE_RAZON_SOCIAL_CARACTERES],
    ['domicilioComercial', MENSAJE_DOMICILIO_COMERCIAL_CARACTERES],
    ['ingresosBrutos', MENSAJE_INGRESOS_BRUTOS_CARACTERES],
  ] as const)('reglasEmisor_CaracteresInvalidos_%s', (campo, mensaje) => {
    for (const texto of ['Línea\nOtra', 'Con\u0000nulo', 'Con\u007Fborrar', 'Con\u009Fcontrol', 'Sola\uD800', 'Sola\uDC00x']) {
      expect(errorEmisor({ [campo]: texto }, campo), JSON.stringify(texto)).toBe(mensaje);
    }
    // Los caracteres de control ganan sobre el largo, como en el dominio.
    expect(errorEmisor({ [campo]: `${'a'.repeat(300)}\u0001` }, campo)).toBe(mensaje);
  });

  it('reglasEmisor_Fecha', () => {
    expect(errorEmisor({ inicioActividades: HOY }, 'inicioActividades')).toBeUndefined();
    expect(errorEmisor({ inicioActividades: '2026-11-17' }, 'inicioActividades')).toBe(
      MENSAJE_INICIO_ACTIVIDADES_POSTERIOR,
    );
    for (const texto of ['2026-02-30', '2026-13-01', '01/03/2020', '2020-3-1', 'mañana']) {
      expect(errorEmisor({ inicioActividades: texto }, 'inicioActividades'), texto).toBe(
        MENSAJE_INICIO_ACTIVIDADES_INVALIDA,
      );
    }
    // 2024 es bisiesto.
    expect(errorEmisor({ inicioActividades: '2024-02-29' }, 'inicioActividades')).toBeUndefined();
  });

  it('reglasEmisor_FechaAnioMenorA100_ComoElServidor', () => {
    // DateOnly.TryParseExact("yyyy-MM-dd") acepta los años 1 a 99 tal cual (no los lleva a 19xx) y rechaza el año 0.
    expect(errorEmisor({ inicioActividades: '0050-01-01' }, 'inicioActividades')).toBeUndefined();
    expect(errorEmisor({ inicioActividades: '0001-01-01' }, 'inicioActividades')).toBeUndefined();
    expect(errorEmisor({ inicioActividades: '0004-02-29' }, 'inicioActividades')).toBeUndefined();
    expect(errorEmisor({ inicioActividades: '0050-02-29' }, 'inicioActividades')).toBe(
      MENSAJE_INICIO_ACTIVIDADES_INVALIDA,
    );
    expect(errorEmisor({ inicioActividades: '0000-01-01' }, 'inicioActividades')).toBe(
      MENSAJE_INICIO_ACTIVIDADES_INVALIDA,
    );
  });

  it('hoyLocal_FormatoDeFecha', () => {
    expect(hoyLocal(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05');
    expect(hoyLocal(new Date(2026, 10, 16, 0, 0))).toBe('2026-11-16');
  });
});

describe('reglas de la ubicación del backup', () => {
  /** Rutas ficticias, sin nombres de personas. */
  const PENDRIVE = 'E:\\Backups\\Optica';

  it('reglasBackup_TextosYLargoComoLaApi', () => {
    // Los mismos textos que `ValidacionUbicacionBackup` y el largo de `UbicacionBackup.LargoMaximoRuta`.
    expect(MENSAJE_RUTA_BACKUP_FALTANTE).toBe('Ingresá la carpeta del backup.');
    expect(MENSAJE_RUTA_BACKUP_LARGA).toBe('La ruta de la carpeta del backup puede tener hasta 260 caracteres.');
    expect(LARGO_MAXIMO_RUTA_BACKUP).toBe(260);
  });

  it('reglasBackup_RutaValida_DevuelveLaRecortada', () => {
    expect(validarRutaBackup(PENDRIVE)).toEqual({ tipo: 'ok', ruta: PENDRIVE });
    expect(validarRutaBackup(`  ${PENDRIVE}\t`)).toEqual({ tipo: 'ok', ruta: PENDRIVE });
    // El recorte del servidor (`RecorteTexto`) también quita U+0085 y la marca de orden de bytes.
    expect(validarRutaBackup(`\uFEFF/media/pendrive/Backups\u0085`)).toEqual({ tipo: 'ok', ruta: '/media/pendrive/Backups' });
  });

  it.each(['', '   ', '\t\n', '\u0085'])('reglasBackup_Faltante_Error (%j)', (escrita) => {
    expect(validarRutaBackup(escrita)).toEqual({ tipo: 'error', mensaje: MENSAJE_RUTA_BACKUP_FALTANTE });
  });

  it('reglasBackup_Largo_HastaElMaximoRecortado', () => {
    const de260 = `E:\\${'a'.repeat(257)}`;
    expect(validarRutaBackup(de260)).toEqual({ tipo: 'ok', ruta: de260 });
    // Los espacios de los extremos no cuentan: se recortan antes de medir.
    expect(validarRutaBackup(`   ${de260}   `)).toEqual({ tipo: 'ok', ruta: de260 });
    expect(validarRutaBackup(`${de260}a`)).toEqual({ tipo: 'error', mensaje: MENSAJE_RUTA_BACKUP_LARGA });
  });
});
