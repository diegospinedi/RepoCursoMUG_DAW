import { describe, expect, it } from 'vitest';
import {
  IMPORTE_MAXIMO_SEGURO,
  MENSAJE_HASTA_DOS_DECIMALES,
  MENSAJE_NO_ES_NUMERO,
  MENSAJE_VALOR_MENOR,
} from '../configuracion/reglas';
import {
  esUtf16Valido,
  LARGO_MAXIMO_BUSQUEDA,
  LARGO_MAXIMO_NUMERO,
  MENSAJE_BUSQUEDA_LARGA,
  validarBusqueda,
  MENSAJE_CARACTERES_INVALIDOS,
  MENSAJE_CODIGO_FALTANTE,
  MENSAJE_CODIGO_LARGO,
  MENSAJE_COSTO_FALTANTE,
  MENSAJE_DESCRIPCION_FALTANTE,
  MENSAJE_DESCRIPCION_LARGA,
  MENSAJE_MARGEN_FALTANTE,
  MENSAJE_MARGEN_FUERA_DE_RANGO,
  recortar,
  validarArticulo,
  type TextosArticulo,
} from './reglas';

// Los caracteres invisibles se arman por su código: así el archivo no tiene ninguno escrito tal cual.
const c = (...codigos: number[]) => String.fromCharCode(...codigos);
const NBSP = c(0x00a0);
const TAB = c(0x0009);
const BOM = c(0xfeff);
const NEL = c(0x0085);
const SURROGATE_ALTO = c(0xd83d);
const SURROGATE_BAJO = c(0xde00);
/** Un emoji: un carácter visible que ocupa 2 unidades UTF-16, como lo cuenta string.Length en .NET. */
const EMOJI = SURROGATE_ALTO + SURROGATE_BAJO;

/**
 * Los caracteres para los que `char.IsWhiteSpace` de .NET es verdadero (U+0009 a U+000D, U+0020, U+0085, U+00A0,
 * U+1680, U+2000 a U+200A, U+2028, U+2029, U+202F, U+205F, U+3000), más U+FEFF (D-17).
 */
const RECORTABLES_DE_NET = [
  0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x20, 0x85, 0xa0, 0x1680, 0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006,
  0x2007, 0x2008, 0x2009, 0x200a, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000, 0xfeff,
];

const VALIDOS: TextosArticulo = {
  codigoProveedor: 'ABC-1',
  descripcion: 'Armazón metálico',
  precioCosto: '1210',
  margenUtilidad: '50',
};

/**
 * El mensaje de un campo, o `undefined` si los datos se aceptan (`ok`). Los demás campos son válidos, así que un
 * rechazo sin mensaje en este campo es un error de la regla y se informa como tal.
 */
function errorDe(cambios: Partial<TextosArticulo>, campo: keyof TextosArticulo): string | undefined {
  const resultado = validarArticulo({ ...VALIDOS, ...cambios });
  if (resultado.tipo === 'ok') {
    return undefined;
  }
  return resultado.errores[campo] ?? `rechazado sin mensaje en ${campo}`;
}

describe('reglas del artículo', () => {
  it('reglas_MensajesIgualesALosDeLaApi', () => {
    // Los mismos textos que ValidacionArticulo.cs y AccionesNumericas.cs (Block 3).
    expect(MENSAJE_CODIGO_FALTANTE).toBe('Completá el código en el proveedor.');
    expect(MENSAJE_CODIGO_LARGO).toBe('Usá hasta 50 caracteres.');
    expect(MENSAJE_DESCRIPCION_FALTANTE).toBe('Completá la descripción.');
    expect(MENSAJE_DESCRIPCION_LARGA).toBe('Usá hasta 200 caracteres.');
    expect(MENSAJE_CARACTERES_INVALIDOS).toBe('El texto tiene caracteres no válidos. Escribilo de nuevo.');
    expect(MENSAJE_COSTO_FALTANTE).toBe('Ingresá el precio de costo.');
    expect(MENSAJE_MARGEN_FALTANTE).toBe('Ingresá el margen de utilidad.');
    expect(MENSAJE_MARGEN_FUERA_DE_RANGO).toBe('El margen debe estar entre 0 y 1.000.');
    expect(MENSAJE_HASTA_DOS_DECIMALES).toBe('Ingresá hasta 2 decimales.');
    expect(MENSAJE_VALOR_MENOR).toBe('Ingresá un valor menor.');
  });

  it('reglas_Validos_DevuelveLosDatosConLosTextosRecortados', () => {
    expect(
      validarArticulo({
        codigoProveedor: ` ${NBSP}ABC-1${TAB} `,
        descripcion: `${BOM}Armazón metálico${NEL}`,
        precioCosto: '1210,5',
        margenUtilidad: '50.25',
      }),
    ).toEqual({
      tipo: 'ok',
      datos: { codigoProveedor: 'ABC-1', descripcion: 'Armazón metálico', precioCosto: 1210.5, margenUtilidad: 50.25 },
    });
  });

  it('reglas_VariosCamposInvalidos_DevuelveTodosLosErrores', () => {
    expect(validarArticulo({ codigoProveedor: '', descripcion: '', precioCosto: '', margenUtilidad: '' })).toEqual({
      tipo: 'errores',
      errores: {
        codigoProveedor: MENSAJE_CODIGO_FALTANTE,
        descripcion: MENSAJE_DESCRIPCION_FALTANTE,
        precioCosto: MENSAJE_COSTO_FALTANTE,
        margenUtilidad: MENSAJE_MARGEN_FALTANTE,
      },
    });
  });
});

describe('recortar', () => {
  it.each(RECORTABLES_DE_NET.map((codigo) => [codigo.toString(16).padStart(4, '0')] as const))(
    'recortar_EspacioDeNet_U+%s_SeQuita',
    (hexa) => {
      const espacio = c(Number.parseInt(hexa, 16));
      expect(recortar(`${espacio}${espacio}ABC${espacio}`)).toBe('ABC');
    },
  );

  it('recortar_EspacioDuroTabuladorBomYNel_SeQuitan', () => {
    expect(recortar(`${NBSP}${TAB}${BOM}${NEL}ABC${NEL}${BOM}${TAB}${NBSP}`)).toBe('ABC');
  });

  it('recortar_Nel_DistintoDelTrimDeJavaScript', () => {
    // El motivo de un recorte propio (D-17): trim() no quita U+0085 y .NET sí.
    expect(`${NEL}ABC`.trim()).not.toBe('ABC');
    expect(recortar(`${NEL}ABC`)).toBe('ABC');
  });

  it.each([
    ['espacio de ancho cero', 0x200b],
    ['separador de vocales mongol', 0x180e],
    ['separador de archivo', 0x1c],
    ['unión de ancho cero', 0x200d],
  ])('recortar_%s_NoSeQuita', (_nombre, codigo) => {
    // No son espacios para char.IsWhiteSpace: el servidor los conserva y la pantalla también. Los espacios de
    // afuera sí se quitan, y el recorte se detiene en el carácter conservado.
    const caracter = c(codigo);
    expect(recortar(` ${NBSP}${caracter}ABC${caracter}${TAB} `)).toBe(`${caracter}ABC${caracter}`);
  });

  it('recortar_EspaciosDelMedio_SeConservan', () => {
    expect(recortar(` A${NBSP}B C `)).toBe(`A${NBSP}B C`);
  });

  it('recortar_SoloEspacios_TextoVacio', () => {
    expect(recortar(RECORTABLES_DE_NET.map((codigo) => c(codigo)).join(''))).toBe('');
    expect(recortar('')).toBe('');
  });
});

describe('esUtf16Valido', () => {
  it('esUtf16Valido_BienFormadosVerdaderoYSustitutosSueltosFalso', () => {
    // Una sola prueba con los dos lados: una función que siempre responde lo mismo no la pasa.
    const casos: [string, string, boolean][] = [
      ['texto común', 'ABC-1', true],
      ['vacío', '', true],
      ['par sustituto completo', `A${EMOJI}B`, true],
      ['sustituto alto suelto', `A${SURROGATE_ALTO}`, false],
      ['sustituto alto seguido de otro carácter', `${SURROGATE_ALTO}A`, false],
      ['sustituto bajo suelto', `${SURROGATE_BAJO}A`, false],
      ['par invertido', `${SURROGATE_BAJO}${SURROGATE_ALTO}`, false],
    ];
    expect(casos.map(([caso, texto]) => [caso, esUtf16Valido(texto)])).toEqual(
      casos.map(([caso, , esperado]) => [caso, esperado]),
    );
  });
});

describe.each([
  ['codigoProveedor', 50, MENSAJE_CODIGO_FALTANTE, MENSAJE_CODIGO_LARGO],
  ['descripcion', 200, MENSAJE_DESCRIPCION_FALTANTE, MENSAJE_DESCRIPCION_LARGA],
] as const)('reglas del texto %s', (campo, largo, faltante, demasiadoLargo) => {
  it.each(['', '   ', `${NBSP}${TAB}${BOM}${NEL}`])('reglas_SinTexto_Faltante (%j)', (texto) => {
    expect(errorDe({ [campo]: texto }, campo)).toBe(faltante);
  });

  it('reglas_SurrogateSuelto_CaracteresInvalidos', () => {
    expect(errorDe({ [campo]: `A${SURROGATE_ALTO}` }, campo)).toBe(MENSAJE_CARACTERES_INVALIDOS);
    expect(errorDe({ [campo]: `${SURROGATE_BAJO}A` }, campo)).toBe(MENSAJE_CARACTERES_INVALIDOS);
  });

  it('reglas_LargoMaximoTrasRecortar_Valido', () => {
    expect(errorDe({ [campo]: 'A'.repeat(largo) }, campo)).toBeUndefined();
    // D-02: el largo se mide después de recortar.
    expect(errorDe({ [campo]: `  ${NBSP}${'A'.repeat(largo)}${BOM} ` }, campo)).toBeUndefined();
  });

  it('reglas_UnoMasQueElMaximo_DemasiadoLargo', () => {
    expect(errorDe({ [campo]: 'A'.repeat(largo + 1) }, campo)).toBe(demasiadoLargo);
  });

  it('reglas_LargoEnUnidadesUtf16_ComoNet', () => {
    // Un emoji cuenta 2, como string.Length en .NET.
    expect(errorDe({ [campo]: EMOJI.repeat(largo / 2) }, campo)).toBeUndefined();
    expect(errorDe({ [campo]: `${EMOJI.repeat(largo / 2)}A` }, campo)).toBe(demasiadoLargo);
  });
});

describe('reglas del precio de costo', () => {
  it.each([
    ['', MENSAJE_COSTO_FALTANTE],
    ['abc', MENSAJE_NO_ES_NUMERO],
    ['1.000,5', MENSAJE_NO_ES_NUMERO],
    ['1210,555', MENSAJE_HASTA_DOS_DECIMALES],
    ['-0,001', MENSAJE_HASTA_DOS_DECIMALES],
  ])('reglas_Costo_%j_Rechazado', (texto, mensaje) => {
    expect(errorDe({ precioCosto: texto }, 'precioCosto')).toBe(mensaje);
  });

  it.each(['0', '-100', '1210', '1210,50', '0.01'])('reglas_Costo_%j_Valido', (texto) => {
    // FR-05: cero y negativos son válidos.
    expect(errorDe({ precioCosto: texto }, 'precioCosto')).toBeUndefined();
  });

  it('reglas_CostoFueraDeRangoEnLosDosSentidos_Rechazado', () => {
    expect(IMPORTE_MAXIMO_SEGURO).toBe(90071992547409.91);
    expect(errorDe({ precioCosto: '90071992547409,91' }, 'precioCosto')).toBeUndefined();
    expect(errorDe({ precioCosto: '-90071992547409,91' }, 'precioCosto')).toBeUndefined();
    expect(errorDe({ precioCosto: '90071992547410' }, 'precioCosto')).toBe(MENSAJE_VALOR_MENOR);
    expect(errorDe({ precioCosto: '-90071992547410' }, 'precioCosto')).toBe(MENSAJE_VALOR_MENOR);
  });

  it('reglas_CostoConMasDecimalesYFueraDeRango_InformaLaEscala', () => {
    // El orden del dominio: la escala antes que el rango.
    expect(errorDe({ precioCosto: '90071992547410,001' }, 'precioCosto')).toBe(MENSAJE_HASTA_DOS_DECIMALES);
  });
});

describe('reglas del margen de utilidad', () => {
  it('reglas_Margen1000coma01_Rechazado', () => {
    expect(errorDe({ margenUtilidad: '1000,01' }, 'margenUtilidad')).toBe(MENSAJE_MARGEN_FUERA_DE_RANGO);
  });

  it.each([
    ['', MENSAJE_MARGEN_FALTANTE],
    ['x', MENSAJE_NO_ES_NUMERO],
    ['-1', MENSAJE_MARGEN_FUERA_DE_RANGO],
    ['-0,01', MENSAJE_MARGEN_FUERA_DE_RANGO],
    // AC-05: el rango antes que la escala.
    ['1000,001', MENSAJE_MARGEN_FUERA_DE_RANGO],
    ['50,005', MENSAJE_HASTA_DOS_DECIMALES],
  ])('reglas_Margen_%j_Rechazado', (texto, mensaje) => {
    expect(errorDe({ margenUtilidad: texto }, 'margenUtilidad')).toBe(mensaje);
  });

  it.each(['0', '1000', '1000,00', '50,25', '150'])('reglas_Margen_%j_Valido', (texto) => {
    expect(errorDe({ margenUtilidad: texto }, 'margenUtilidad')).toBeUndefined();
  });
});

describe('reglas de la búsqueda', () => {
  it('reglas_Busqueda_LimitesPropios', () => {
    expect(LARGO_MAXIMO_BUSQUEDA).toBe(200);
    expect(MENSAJE_BUSQUEDA_LARGA).toBe('Usá hasta 200 caracteres.');
    expect(LARGO_MAXIMO_NUMERO).toBe(32);
  });

  it.each([
    ['vacío', '', { texto: '' }],
    ['recortado', `${NBSP} armazón${TAB}`, { texto: 'armazón' }],
    ['200 tras el recorte', ` ${'a'.repeat(200)} `, { texto: 'a'.repeat(200) }],
    ['201 tras el recorte', 'a'.repeat(201), { error: 'Usá hasta 200 caracteres.' }],
    ['UTF-16 inválido', `a${SURROGATE_ALTO}`, { error: MENSAJE_CARACTERES_INVALIDOS }],
  ])('reglas_ValidarBusqueda_%s', (_caso, escrito, esperado) => {
    expect(validarBusqueda(escrito)).toEqual(esperado);
  });
});
