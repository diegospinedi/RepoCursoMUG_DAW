import { describe, expect, it } from 'vitest';
import {
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
