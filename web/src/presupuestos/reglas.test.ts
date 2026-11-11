import { describe, expect, it } from 'vitest';
import {
  centavosDe,
  IMPORTE_MAXIMO_SEGURO,
  IMPORTE_MAXIMO_SEGURO_CENTESIMOS,
  mostrarEntero,
} from '../configuracion/numeros';
import { MENSAJE_HASTA_DOS_DECIMALES } from '../configuracion/reglas';
import {
  CANTIDAD_MAXIMA,
  calcularLinea,
  calcularTotal,
  MENSAJE_CANTIDAD_INVALIDA,
  MENSAJE_CANTIDAD_MAXIMA,
  MENSAJE_DESCUENTO_FUERA_DE_RANGO,
  MENSAJE_IMPORTE_LINEA_EXCEDIDO,
  MENSAJE_TOTAL_EXCEDIDO,
  validarCantidad,
  validarDescuento,
  LARGO_MAXIMO_APELLIDO,
  LARGO_MAXIMO_DOMICILIO,
  LARGO_MAXIMO_EMAIL,
  LARGO_MAXIMO_NOMBRE,
  LARGO_MAXIMO_TELEFONO,
  LINEAS_MAXIMAS,
  MENSAJE_APELLIDO_FALTANTE,
  MENSAJE_APELLIDO_LARGO,
  MENSAJE_CARACTERES_INVALIDOS,
  MENSAJE_DEMASIADAS_LINEAS,
  MENSAJE_DNI_FALTANTE,
  MENSAJE_DNI_INVALIDO,
  MENSAJE_DOMICILIO_LARGO,
  MENSAJE_EMAIL_INVALIDO,
  MENSAJE_EMAIL_LARGO,
  MENSAJE_NOMBRE_FALTANTE,
  MENSAJE_NOMBRE_LARGO,
  MENSAJE_PRECIO_NEGATIVO,
  MENSAJE_SIN_LINEAS,
  MENSAJE_TELEFONO_LARGO,
  mensajeConfirmacion,
  normalizarDni,
  tieneFormatoDeEmail,
  validarCliente,
  validarLineas,
  type TextosCliente,
} from './reglas';

// Los caracteres invisibles se arman por su código: así el archivo no tiene ninguno escrito tal cual.
const c = (...codigos: number[]) => String.fromCharCode(...codigos);
const NEL = c(0x0085);
const NBSP = c(0x00a0);
const SURROGATE_ALTO = c(0xd83d);

/** Datos ficticios. */
const VALIDOS: TextosCliente = {
  apellido: 'Pérez',
  nombre: 'Ana',
  dni: '23.456.789',
  domicilio: 'Calle Falsa 123',
  email: 'ana@mail.com',
  telefono: '11 5555-0000',
};

function errorDe(cambios: Partial<TextosCliente>, campo: keyof TextosCliente): string | undefined {
  const resultado = validarCliente({ ...VALIDOS, ...cambios });
  return resultado.tipo === 'ok' ? undefined : resultado.errores[campo];
}

describe('reglas del presupuesto', () => {
  it('Textos_IgualesALosDeValidacionPresupuesto', () => {
    expect(MENSAJE_APELLIDO_FALTANTE).toBe('Ingresá el apellido del cliente');
    expect(MENSAJE_NOMBRE_FALTANTE).toBe('Ingresá el nombre del cliente');
    expect(MENSAJE_DNI_FALTANTE).toBe('Ingresá el DNI del cliente');
    expect(MENSAJE_DNI_INVALIDO).toBe('El DNI debe tener 7 u 8 dígitos');
    expect(MENSAJE_EMAIL_INVALIDO).toBe('El email debe tener el formato usuario@dominio.extensión');
    expect(MENSAJE_SIN_LINEAS).toBe('Agregá al menos una línea al presupuesto');
    expect(MENSAJE_PRECIO_NEGATIVO).toBe('El precio unitario no puede ser negativo');
    expect(MENSAJE_CARACTERES_INVALIDOS).toBe('El texto tiene caracteres no válidos. Escribilo de nuevo.');
    expect(MENSAJE_APELLIDO_LARGO).toBe('Usá hasta 100 caracteres.');
    expect(MENSAJE_NOMBRE_LARGO).toBe('Usá hasta 100 caracteres.');
    expect(MENSAJE_DOMICILIO_LARGO).toBe('Usá hasta 200 caracteres.');
    expect(MENSAJE_EMAIL_LARGO).toBe('Usá hasta 254 caracteres.');
    expect(MENSAJE_TELEFONO_LARGO).toBe('Usá hasta 30 caracteres.');
    expect(MENSAJE_DEMASIADAS_LINEAS).toBe('Usá hasta 100 líneas.');
    expect(mensajeConfirmacion(155)).toBe('Presupuesto N.º 155 grabado. Estado: Borrador.');
    expect([LARGO_MAXIMO_APELLIDO, LARGO_MAXIMO_NOMBRE, LARGO_MAXIMO_DOMICILIO, LARGO_MAXIMO_EMAIL]).toEqual([
      100, 100, 200, 254,
    ]);
    expect(LARGO_MAXIMO_TELEFONO).toBe(30);
    expect(LINEAS_MAXIMAS).toBe(100);
  });

  it('Valido_DevuelveLosTextosRecortados', () => {
    expect(
      validarCliente({
        apellido: '  Pérez ',
        nombre: `${NBSP}Ana`,
        dni: ' 23.456.789 ',
        domicilio: ' Calle Falsa 123 ',
        email: ' ana@mail.com ',
        telefono: ` 11 5555-0000${NEL}`,
      }),
    ).toEqual({
      tipo: 'ok',
      datos: {
        apellido: 'Pérez',
        nombre: 'Ana',
        dni: '23.456.789',
        domicilio: 'Calle Falsa 123',
        email: 'ana@mail.com',
        telefono: '11 5555-0000',
      },
    });
  });

  it('SinOpcionales_LosEnviaComoNull', () => {
    // FR-03, D-11: vacíos o solo espacios equivalen a ausentes.
    expect(validarCliente({ ...VALIDOS, domicilio: '', email: '   ', telefono: NBSP })).toEqual({
      tipo: 'ok',
      datos: { apellido: 'Pérez', nombre: 'Ana', dni: '23.456.789', domicilio: null, email: null, telefono: null },
    });
  });

  it('Faltantes_UnMensajePorCampo', () => {
    expect(validarCliente({ ...VALIDOS, apellido: ' ', nombre: '', dni: '  ' })).toEqual({
      tipo: 'errores',
      errores: { apellido: MENSAJE_APELLIDO_FALTANTE, nombre: MENSAJE_NOMBRE_FALTANTE, dni: MENSAJE_DNI_FALTANTE },
    });
  });

  it.each(['123456', '123456789', '23A56789', '23 456 789', '23-456-789', c(0xff12, 0xff13, 0xff14, 0xff15, 0xff16, 0xff17, 0xff18, 0xff19), `2345678${SURROGATE_ALTO}`])(
    'Dni_%s_Invalido',
    (dni) => {
      expect(errorDe({ dni }, 'dni')).toBe(MENSAJE_DNI_INVALIDO);
      expect(normalizarDni(dni)).toBeNull();
    },
  );

  it.each([
    ['23.456.789', '23456789'],
    ['23456789', '23456789'],
    ['1.234.567', '1234567'],
    [' 01234567 ', '01234567'],
    ['2.3.4.5.6.7.8.9', '23456789'],
  ])('Dni_%s_Normaliza%s', (dni, esperado) => {
    expect(normalizarDni(dni)).toBe(esperado);
    expect(errorDe({ dni }, 'dni')).toBeUndefined();
  });

  it.each([
    'ana@',
    'ana@mail',
    'ana@.com',
    'ana@mail.',
    'ana@mail.c',
    'ana@mail.c1',
    'ana@mail.12',
    'ana@mail..com',
    '@mail.com',
    'ana@@mail.com',
    'ana@mi@mail.com',
    'a na@mail.com',
    `ana${NEL}x@mail.com`,
    `ana@mail.co${NBSP}m`,
    'ana@mail.рф',
  ])('Email_%s_Invalido', (email) => {
    expect(tieneFormatoDeEmail(email)).toBe(false);
    expect(errorDe({ email }, 'email')).toBe(MENSAJE_EMAIL_INVALIDO);
  });

  it.each(['ana@mail.com', 'ana@mail.com.ar', 'Ana.Perez+presupuesto@Mail.COM', 'a@b.ar'])('Email_%s_Valido', (email) => {
    expect(tieneFormatoDeEmail(email)).toBe(true);
    expect(errorDe({ email }, 'email')).toBeUndefined();
  });

  it.each([
    ['apellido', LARGO_MAXIMO_APELLIDO, MENSAJE_APELLIDO_LARGO],
    ['nombre', LARGO_MAXIMO_NOMBRE, MENSAJE_NOMBRE_LARGO],
    ['domicilio', LARGO_MAXIMO_DOMICILIO, MENSAJE_DOMICILIO_LARGO],
    ['telefono', LARGO_MAXIMO_TELEFONO, MENSAJE_TELEFONO_LARGO],
  ] as const)('Largo_%s_HastaElMaximoDespuesDelRecorte', (campo, maximo, mensaje) => {
    expect(errorDe({ [campo]: ` ${'a'.repeat(maximo)} ` }, campo)).toBeUndefined();
    expect(errorDe({ [campo]: 'a'.repeat(maximo + 1) }, campo)).toBe(mensaje);
  });

  it('Largo_Email_254AceptaY255Rechaza', () => {
    const conLargo = (largo: number) => `${'a'.repeat(largo - '@mail.com'.length)}@mail.com`;
    expect(errorDe({ email: conLargo(254) }, 'email')).toBeUndefined();
    // El largo antes que el formato, como PoliticaCliente.
    expect(errorDe({ email: conLargo(255) }, 'email')).toBe(MENSAJE_EMAIL_LARGO);
  });

  it.each(['apellido', 'nombre', 'domicilio', 'email', 'telefono'] as const)(
    'Utf16Invalido_%s_CaracteresInvalidosAntesQueElLargo',
    (campo) => {
      expect(errorDe({ [campo]: `${'a'.repeat(300)}${SURROGATE_ALTO}` }, campo)).toBe(MENSAJE_CARACTERES_INVALIDOS);
    },
  );

  it('Lineas_SinLineasOMasDe100', () => {
    expect(validarLineas(0)).toBe(MENSAJE_SIN_LINEAS);
    expect(validarLineas(1)).toBeUndefined();
    expect(validarLineas(100)).toBeUndefined();
    expect(validarLineas(101)).toBe(MENSAJE_DEMASIADAS_LINEAS);
  });
});

describe('reglas de la línea: cantidad, descuento y cálculo', () => {
  it('Textos_IgualesALosDeValidacionPresupuesto', () => {
    expect(MENSAJE_CANTIDAD_INVALIDA).toBe('La cantidad debe ser un número entero mayor a 0');
    expect(MENSAJE_CANTIDAD_MAXIMA).toBe('Usá hasta 9.999 unidades.');
    expect(MENSAJE_DESCUENTO_FUERA_DE_RANGO).toBe('El descuento debe estar entre 0 y 100');
    expect(MENSAJE_IMPORTE_LINEA_EXCEDIDO).toBe('El precio final de la línea es demasiado alto.');
    expect(MENSAJE_TOTAL_EXCEDIDO).toBe('El total del presupuesto es demasiado alto.');
    expect(CANTIDAD_MAXIMA).toBe(9999);
  });

  /**
   * La tabla de casos común con `tests/Optica.Domain.Tests/Presupuestos/CalculoLineaTests.cs` (ADR-016): un cambio de
   * la fórmula obliga a cambiar los dos lados y esta tabla. Columnas: caso, Precio Unitario, Porcentaje de Descuento,
   * Cantidad, Precio con Descuento y Precio Final esperados. AC-07 (la suma) está en `calcularTotal_suma de AC-07`.
   */
  const TABLA_COMUN: [string, number, number, number, number, number][] = [
    ['AC-01', 1000.0, 10, 3, 900.0, 2700.0],
    ['AC-04', 1000.0, 100, 1, 0.0, 0.0],
    ['AC-05', 1000.0, 0, 1, 1000.0, 1000.0],
    ['AC-06', 6.67, 50, 3, 3.34, 10.02],
    ['mitad hacia arriba 0,025', 0.05, 50, 1, 0.03, 0.03],
    ['mitad hacia arriba 0,505', 1.01, 50, 1, 0.51, 0.51],
    ['redondeo hacia abajo', 99.99, 33.33, 1, 66.66, 66.66],
    ['descuento con 2 decimales', 1000.0, 12.34, 1, 876.6, 876.6],
    ['cantidad máxima', 1000.0, 10, 9_999, 900.0, 8_999_100.0],
    // D-23: en JavaScript 90.071.992.547.409,91 y ,90 son el mismo `number` (por encima de 2^46 pesos no hay centavos
    // exactos), así que esta fila no prueba la paridad al centavo con el servidor, solo que el tope entra. Los bordes
    // exactos del tope, en centésimos, están en `calcularLinea_TopeExacto_*` y `calcularTotal_TopeExacto_*`.
    ['tope del contrato con la web', 90_071_992_547_409.91, 0, 1, 90_071_992_547_409.91, 90_071_992_547_409.91],
  ];

  it.each(TABLA_COMUN)(
    'calcularLinea_ejemplos de AC-01, AC-04, AC-05 y AC-06 — tabla común: %s',
    (_caso, precioUnitario, descuento, cantidad, precioConDescuento, precioFinal) => {
      expect(calcularLinea(precioUnitario, cantidad, descuento)).toEqual({ precioConDescuento, precioFinal });
    },
  );

  it('calcularLinea_RedondeaDespuesDelDescuento', () => {
    // AC-06: 6,67 × 0,5 = 3,335 → 3,34 y 3,34 × 3 = 10,02; sin redondear antes daría 10,005 → 10,01.
    expect(calcularLinea(6.67, 3, 50)?.precioFinal).toBe(10.02);
  });

  it('calcularTotal_suma de AC-07', () => {
    // 6,67 × 3 con 50 % → 10,02 y 1.000,00 × 3 con 10 % → 2.700,00; la suma de los Precios Finales es 2.710,02.
    const ac06 = calcularLinea(6.67, 3, 50);
    const ac01 = calcularLinea(1000, 3, 10);
    expect(ac06?.precioFinal).toBe(10.02);
    expect(ac01?.precioFinal).toBe(2700);
    expect(calcularTotal([ac06!.precioFinal, ac01!.precioFinal])).toBe(2710.02);
  });

  it('calcularTotal_SinErrorDeComaFlotante', () => {
    expect(calcularTotal([0.1, 0.2])).toBe(0.3);
    expect(calcularTotal([])).toBe(0);
  });

  it('calcularLinea_importe excedido: rechazado', () => {
    // 9.007.199.254.740,99 × 10 = 90.071.992.547.409,90: dentro del tope. Un centavo más de precio lo supera.
    expect(calcularLinea(9_007_199_254_740.99, 10, 0)?.precioFinal).toBe(90_071_992_547_409.9);
    expect(calcularLinea(9_007_199_254_741, 10, 0)).toBeNull();
    expect(calcularLinea(IMPORTE_MAXIMO_SEGURO, CANTIDAD_MAXIMA, 0)).toBeNull();
    // Con descuento entra: el tope es del Precio Final.
    expect(calcularLinea(9_007_199_254_741, 10, 50)?.precioFinal).toBe(45_035_996_273_705);
  });

  it('Tope_EnCentesimos_EsElMayorEnteroSeguro', () => {
    // D-04 y D-23: 90.071.992.547.409,91 = Number.MAX_SAFE_INTEGER centésimos, como ImporteMaximoContrato.
    expect(IMPORTE_MAXIMO_SEGURO_CENTESIMOS).toBe(9_007_199_254_740_991n);
  });

  it('calcularLinea_TopeExacto_EnElTopeAceptadoYUnCentesimoMasRechazado', () => {
    // Los bordes en centésimos, con cantidad > 1 y precios que un `number` representa exactos (debajo de 2^46 pesos):
    // 9.007.199.254.740.991 = 1.416.003.655.831 × 6.361 → Precio Final exactamente en el tope: se acepta.
    const enElTope = 1_416_003_655_831n;
    // Contra el literal, no contra la constante: así un tope mal definido falla en calcularLinea.
    expect(enElTope * 6_361n).toBe(9_007_199_254_740_991n);
    const aceptada = calcularLinea(Number(enElTope) / 100, 6_361, 0);
    expect(aceptada).not.toBeNull();
    expect(centavosDe(aceptada!.precioConDescuento)).toBe(enElTope);

    // 9.007.199.254.740.992 = 2^40 × 8.192 → un centésimo por encima del tope: se rechaza.
    const unoMas = 2n ** 40n;
    expect(unoMas * 8_192n).toBe(9_007_199_254_740_992n);
    expect(calcularLinea(Number(unoMas) / 100, 8_192, 0)).toBeNull();
  });

  it('calcularTotal_TopeExacto_EnElTopeAceptadoYUnCentesimoMasRechazado', () => {
    // 90.071.992.547.409 pesos (entero, exacto como `number`) + 0,91 = el tope; + 0,92, un centésimo más.
    expect(centavosDe(90_071_992_547_409) + centavosDe(0.91)).toBe(9_007_199_254_740_991n);
    expect(calcularTotal([90_071_992_547_409, 0.91])).not.toBeNull();
    expect(calcularTotal([90_071_992_547_409, 0.92])).toBeNull();
  });

  it.each([
    ['NaN', [Number.NaN]],
    ['Infinity', [Number.POSITIVE_INFINITY]],
    ['-Infinity', [10, Number.NEGATIVE_INFINITY]],
  ])('calcularTotal_EntradaNoFinita_%s_Null', (_caso, preciosFinales) => {
    // Como calcularLinea: null es entrada inválida o total por encima del tope, nunca una excepción.
    expect(calcularTotal(preciosFinales)).toBeNull();
  });

  it('MensajeCantidadMaxima_AtadoACantidadMaxima', () => {
    // Como en el servidor: el número del texto es CANTIDAD_MAXIMA con punto de miles (es-AR).
    expect(MENSAJE_CANTIDAD_MAXIMA).toBe(`Usá hasta ${mostrarEntero(CANTIDAD_MAXIMA)} unidades.`);
  });

  it('calcularTotal_TotalExcedido_Rechazado', () => {
    expect(calcularTotal([IMPORTE_MAXIMO_SEGURO])).toBe(IMPORTE_MAXIMO_SEGURO);
    expect(calcularTotal([IMPORTE_MAXIMO_SEGURO, 1])).toBeNull();
    expect(calcularTotal([45_035_996_273_705, 45_035_996_273_705])).toBeNull();
  });

  it.each([
    ['precio negativo', -150, 1, 0],
    ['cantidad 0', 1000, 0, 0],
    ['cantidad 10.000', 1000, 10_000, 0],
    ['cantidad no entera', 1000, 2.5, 0],
    ['descuento negativo', 1000, 1, -1],
    ['descuento 100,01', 1000, 1, 100.01],
    ['descuento con 3 decimales', 1000, 1, 10.555],
    ['precio no finito', Number.NaN, 1, 0],
  ])('calcularLinea_%s_SinCalculo', (_caso, precioUnitario, cantidad, descuento) => {
    expect(calcularLinea(precioUnitario, cantidad, descuento)).toBeNull();
  });

  it.each(['1', '3', '9999', ' 12 ', '007'])('validarCantidad_%s_valida', (texto) => {
    expect(validarCantidad(texto)).toEqual({ valor: Number(texto.trim()) });
  });

  it.each(['0', '-1', '2,5', '2.5', '', '   ', 'tres', '1e3', '+3', '٣'])(
    'validarCantidad_0_-1_2,5_vacía: inválida — "%s"',
    (texto) => {
      expect(validarCantidad(texto)).toEqual({ error: MENSAJE_CANTIDAD_INVALIDA });
    },
  );

  it.each(['10000', '99999', '10000000000', '000010000'])('validarCantidad_10000: rechazada — "%s"', (texto) => {
    expect(validarCantidad(texto)).toEqual({ error: MENSAJE_CANTIDAD_MAXIMA });
  });

  describe('validarCantidad_PuntoDeMiles', () => {
    // D-29: un entero con punto de miles bien formado (primer grupo de 1 a 3 dígitos, después grupos de exactamente 3).
    it.each([
      ['1.000', 1000],
      ['9.999', 9999],
      [' 1.000 ', 1000],
    ])('"%s" vale %d', (texto, valor) => {
      expect(validarCantidad(texto)).toEqual({ valor });
    });

    it.each(['10.000', '1.000.000', '99.999', `1${'.000'.repeat(40)}`])(
      '"%s" da el máximo',
      (texto) => {
        expect(validarCantidad(texto)).toEqual({ error: MENSAJE_CANTIDAD_MAXIMA });
      },
    );

    it.each([
      '1.00',
      '10.0000',
      '.100',
      '1..000',
      '1.000.',
      '1000.000',
      '1,000',
      '-3',
      '+1.000',
      '-1.000',
      '1 000',
      '1. 000',
      '0.000',
      // Con punto, el primer grupo no empieza con 0; sin punto, "007" sigue valiendo 7 (D-25).
      '0.001',
      '001.000',
      ' 001.000 ',
      '000.010.000',
      '١.٠٠٠',
    ])('"%s" es inválida', (texto) => {
      expect(validarCantidad(texto)).toEqual({ error: MENSAJE_CANTIDAD_INVALIDA });
    });

    it('un texto largo mal agrupado se resuelve sin demora', () => {
      // Lineal en el largo: la expresión del punto de miles no tiene retroceso exponencial. El texto es inválido, así
      // que se rechaza en la expresión y nunca llega a BigInt.
      const largo = `${'1.000'.repeat(20_000)}x`;
      const inicio = performance.now();
      expect(validarCantidad(largo)).toEqual({ error: MENSAJE_CANTIDAD_INVALIDA });
      expect(performance.now() - inicio).toBeLessThan(1_000);
    });
  });

  it.each([
    ['0', 0],
    ['10', 10],
    ['100', 100],
    ['12,34', 12.34],
    ['12.34', 12.34],
    ['50,50', 50.5],
    [' 7 ', 7],
    ['-0', 0],
  ])('validarDescuento_%s_valido', (texto, valor) => {
    expect(validarDescuento(texto)).toEqual({ valor });
  });

  it.each(['-1', '100,01', '', '  ', '-0,01', '1000', 'diez'])('validarDescuento_-1_100,01_vacío: inválido — "%s"', (texto) => {
    expect(validarDescuento(texto)).toEqual({ error: MENSAJE_DESCUENTO_FUERA_DE_RANGO });
  });

  it.each(['10,555', '0,001', '99,999'])('validarDescuento_10,555: inválido por la escala — "%s"', (texto) => {
    expect(validarDescuento(texto)).toEqual({ error: MENSAJE_HASTA_DOS_DECIMALES });
  });

  it('validarDescuento_RangoAntesQueLaEscala', () => {
    // D-02: 100,001 está fuera de rango y además tiene 3 decimales: gana el rango.
    expect(validarDescuento('100,001')).toEqual({ error: MENSAJE_DESCUENTO_FUERA_DE_RANGO });
    expect(validarDescuento('-0,001')).toEqual({ error: MENSAJE_DESCUENTO_FUERA_DE_RANGO });
  });
});
