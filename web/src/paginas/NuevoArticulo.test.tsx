import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mensajeJuntoAlCampo } from '../pruebas/formulario';
import { json, renderizarApp, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';

afterEach(() => {
  try {
    // Los valores viven solo en el estado del componente (threat model, Information Disclosure).
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  } finally {
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
});

const RUTA = '/articulos/nuevo';
const TITULO = 'Nuevo artículo';
const BOTON = 'Grabar';
const ETIQUETA_CODIGO = 'Código en el proveedor';
const ETIQUETA_DESCRIPCION = 'Descripción';
const ETIQUETA_COSTO = 'Precio de costo ($)';
const ETIQUETA_MARGEN = 'Margen de utilidad (%)';
const ETIQUETA_VENTA = 'Precio de venta ($)';
const ETIQUETAS_EDITABLES = [ETIQUETA_CODIGO, ETIQUETA_DESCRIPCION, ETIQUETA_COSTO, ETIQUETA_MARGEN];
const ADVERTENCIA = 'Precio de costo negativo o cero';
const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';
const MENSAJE_CUERPO_GRANDE = 'Los datos enviados son demasiado largos. Revisalos y grabá de nuevo.';

const GRABADO = {
  codigo: 7,
  codigoProveedor: 'ABC-1',
  descripcion: 'Armazón metálico',
  precioCosto: 1210,
  margenUtilidad: 50,
  precioVenta: 1815,
  advertencia: null,
  activo: true,
  version: 0,
};

const conSesion = { 'GET /api/sesion': vacia(204) };

/** Abre la pantalla y espera el formulario. */
async function abrir() {
  renderizarApp(RUTA);
  await screen.findByRole('heading', { name: TITULO });
  await screen.findByRole('button', { name: BOTON });
}

const campo = (etiqueta: string) => screen.getByLabelText(etiqueta) as HTMLInputElement;

function escribir(etiqueta: string, valor: string) {
  fireEvent.change(campo(etiqueta), { target: { value: valor } });
}

/** Completa los cuatro campos editables; los que no se indican quedan con un valor válido. */
function completar(valores: Partial<Record<'codigo' | 'descripcion' | 'costo' | 'margen', string>> = {}) {
  escribir(ETIQUETA_CODIGO, valores.codigo ?? 'ABC-1');
  escribir(ETIQUETA_DESCRIPCION, valores.descripcion ?? 'Armazón metálico');
  escribir(ETIQUETA_COSTO, valores.costo ?? '1210');
  escribir(ETIQUETA_MARGEN, valores.margen ?? '50');
}

const grabar = () => fireEvent.click(screen.getByRole('button', { name: BOTON }));

/** Los mensajes con rol status de la pantalla, sin la sonda de ubicación de las pruebas. */
function estados(): string[] {
  const pantalla = screen.getByRole('heading', { name: TITULO }).closest('section')!;
  return within(pantalla)
    .queryAllByRole('status')
    .map((elemento) => elemento.textContent ?? '');
}

/** Graba con valores inválidos y comprueba que cada error queda junto a su campo, sin enviar. */
async function rechazadoSinEnviar(
  valores: Parameters<typeof completar>[0],
  esperados: [etiqueta: string, mensaje: string][],
) {
  const servidor = servidorFalso({ ...conSesion, 'POST /api/articulos': json(201, GRABADO) });
  await abrir();

  completar(valores);
  grabar();

  for (const [etiqueta, mensaje] of esperados) {
    await waitFor(() => expect(mensajeJuntoAlCampo(campo(etiqueta))).toBe(mensaje));
    expect(campo(etiqueta).getAttribute('aria-invalid')).toBe('true');
  }
  expect(servidor.pedidosA('POST', '/api/articulos')).toHaveLength(0);
  expect(campo(ETIQUETA_VENTA).value).toBe('');
  expect(estados()).toEqual([]);
}

describe('NuevoArticulo', () => {
  it('NuevoArticulo_AlAbrir_CamposVaciosConSusLimites', async () => {
    const servidor = servidorFalso(conSesion);
    await abrir();

    for (const etiqueta of [...ETIQUETAS_EDITABLES, ETIQUETA_VENTA]) {
      expect(campo(etiqueta).value).toBe('');
    }
    expect(campo(ETIQUETA_CODIGO).getAttribute('maxlength')).toBe('50');
    expect(campo(ETIQUETA_DESCRIPCION).getAttribute('maxlength')).toBe('200');
    expect(campo(ETIQUETA_COSTO).getAttribute('maxlength')).toBe('32');
    expect(campo(ETIQUETA_MARGEN).getAttribute('maxlength')).toBe('32');
    for (const etiqueta of ETIQUETAS_EDITABLES) {
      expect(campo(etiqueta).getAttribute('aria-invalid')).toBe('false');
    }
    expect(screen.getByRole('button', { name: BOTON }).closest('form')?.getAttribute('method')).toBe('post');
    expect(servidor.pedidosA('GET', '/api/sesion')).toHaveLength(1);
    expect(screen.getByRole('link', { name: 'Volver al inicio' }).getAttribute('href')).toBe('/');
  });

  it('NuevoArticulo_Valido_MuestraCodigoYPrecio', async () => {
    const servidor = servidorFalso({
      ...conSesion,
      'POST /api/articulos': [json(201, GRABADO), json(201, { ...GRABADO, codigo: 8, precioVenta: 1500 })],
    });
    await abrir();

    completar({ codigo: ' ABC-1 ', costo: '1210', margen: '50' });
    grabar();

    await waitFor(() => expect(estados()).toEqual(['Artículo grabado con el código 7.']));
    expect(campo(ETIQUETA_VENTA).value).toBe('1.815,00');
    const [pedido] = servidor.pedidosA('POST', '/api/articulos');
    expect(JSON.parse(String(pedido?.init?.body))).toEqual({
      codigoProveedor: 'ABC-1',
      descripcion: 'Armazón metálico',
      precioCosto: 1210,
      margenUtilidad: 50,
    });
    // Listo para otra alta: los campos editables quedan vacíos.
    for (const etiqueta of ETIQUETAS_EDITABLES) {
      expect(campo(etiqueta).value).toBe('');
    }
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);

    completar({ codigo: 'XYZ-2', costo: '1000' });
    grabar();

    await waitFor(() => expect(estados()).toEqual(['Artículo grabado con el código 8.']));
    expect(campo(ETIQUETA_VENTA).value).toBe('1.500,00');
    expect(servidor.pedidosA('POST', '/api/articulos')).toHaveLength(2);
  });

  it('NuevoArticulo_PrecioDeVenta_SoloLectura', async () => {
    const servidor = servidorFalso({ ...conSesion, 'POST /api/articulos': json(201, GRABADO) });
    await abrir();

    // FR-11: la operadora no lo escribe; lo calcula el sistema.
    expect(campo(ETIQUETA_VENTA).readOnly).toBe(true);
    expect(campo(ETIQUETA_VENTA).value).toBe('');

    completar();
    grabar();

    await waitFor(() => expect(campo(ETIQUETA_VENTA).value).toBe('1.815,00'));
    expect(campo(ETIQUETA_VENTA).readOnly).toBe(true);
    const cuerpo = JSON.parse(String(servidor.pedidosA('POST', '/api/articulos')[0]?.init?.body)) as object;
    expect(Object.keys(cuerpo)).not.toContain('precioVenta');
  });

  it('NuevoArticulo_CodigoDuplicado_RechazadoJuntoAlCampo', async () => {
    servidorFalso({
      ...conSesion,
      'POST /api/articulos': json(400, {
        status: 400,
        errors: { codigoProveedor: ['Ese código ya pertenece a otro artículo.'] },
      }),
    });
    await abrir();

    completar({ codigo: 'abc-1' });
    grabar();

    await waitFor(() =>
      expect(mensajeJuntoAlCampo(campo(ETIQUETA_CODIGO))).toBe('Ese código ya pertenece a otro artículo.'),
    );
    expect(campo(ETIQUETA_CODIGO).getAttribute('aria-invalid')).toBe('true');
    expect(mensajeJuntoAlCampo(campo(ETIQUETA_DESCRIPCION))).toBeNull();
    // Conserva lo escrito para corregirlo.
    expect(campo(ETIQUETA_CODIGO).value).toBe('abc-1');
    expect(campo(ETIQUETA_COSTO).value).toBe('1210');
    expect(campo(ETIQUETA_VENTA).value).toBe('');
    expect(estados()).toEqual([]);
  });

  it('NuevoArticulo_Error400ConCampoDesconocido_MensajeGeneral', async () => {
    servidorFalso({
      ...conSesion,
      'POST /api/articulos': json(400, {
        errors: { margenUtilidad: ['El margen debe estar entre 0 y 1.000.'], otroCampo: ['Otro error.'] },
      }),
    });
    await abrir();

    completar();
    grabar();

    await waitFor(() =>
      expect(mensajeJuntoAlCampo(campo(ETIQUETA_MARGEN))).toBe('El margen debe estar entre 0 y 1.000.'),
    );
    expect(screen.getByText('Otro error.').getAttribute('role')).toBe('alert');
  });

  it('NuevoArticulo_SinCodigoOSinDescripcion_RechazadoSinEnviar', async () => {
    await rechazadoSinEnviar({ codigo: '', descripcion: '   ' }, [
      [ETIQUETA_CODIGO, 'Completá el código en el proveedor.'],
      [ETIQUETA_DESCRIPCION, 'Completá la descripción.'],
    ]);
  });

  it('NuevoArticulo_Margen1000coma01_RechazadoSinEnviar', async () => {
    await rechazadoSinEnviar({ margen: '1000,01' }, [[ETIQUETA_MARGEN, 'El margen debe estar entre 0 y 1.000.']]);
  });

  it('NuevoArticulo_NoNumero_Invalido', async () => {
    await rechazadoSinEnviar({ costo: 'abc' }, [[ETIQUETA_COSTO, 'Ingresá un número.']]);
  });

  it('NuevoArticulo_TextoConCaracteresInvalidos_RechazadoSinEnviar', async () => {
    await rechazadoSinEnviar({ descripcion: `Armazón${String.fromCharCode(0xd83d)}` }, [
      [ETIQUETA_DESCRIPCION, 'El texto tiene caracteres no válidos. Escribilo de nuevo.'],
    ]);
  });

  it('NuevoArticulo_CostoNegativo_MuestraAdvertencia', async () => {
    const servidor = servidorFalso({
      ...conSesion,
      'POST /api/articulos': json(201, {
        ...GRABADO,
        precioCosto: -100,
        precioVenta: -150,
        advertencia: ADVERTENCIA,
      }),
    });
    await abrir();

    completar({ costo: '-100' });
    grabar();

    await waitFor(() => expect(estados()).toEqual(['Artículo grabado con el código 7.', ADVERTENCIA]));
    expect(campo(ETIQUETA_VENTA).value).toBe('-150,00');
    expect(JSON.parse(String(servidor.pedidosA('POST', '/api/articulos')[0]?.init?.body))).toHaveProperty(
      'precioCosto',
      -100,
    );
  });

  it.each(ETIQUETAS_EDITABLES)(
    'NuevoArticulo_EscribirDespuesDeGrabar_LimpiaElResultado (%s)',
    async (etiqueta) => {
      // D-21: el resultado del alta anterior no queda al lado de los datos de la siguiente.
      servidorFalso({
        ...conSesion,
        'POST /api/articulos': json(201, { ...GRABADO, precioCosto: -100, precioVenta: -150, advertencia: ADVERTENCIA }),
      });
      await abrir();
      completar({ costo: '-100' });
      grabar();
      await waitFor(() => expect(estados()).toEqual(['Artículo grabado con el código 7.', ADVERTENCIA]));
      expect(campo(ETIQUETA_VENTA).value).toBe('-150,00');

      escribir(etiqueta, '1');

      expect(estados()).toEqual([]);
      expect(campo(ETIQUETA_VENTA).value).toBe('');
      expect(screen.queryByText('Artículo grabado con el código 7.')).toBeNull();
      expect(screen.queryByText(ADVERTENCIA)).toBeNull();
      expect(campo(etiqueta).value).toBe('1');
    },
  );

  it('NuevoArticulo_ErrorCorregido_SeQuitaAlGrabarDeNuevo', async () => {
    servidorFalso({ ...conSesion, 'POST /api/articulos': json(201, GRABADO) });
    await abrir();
    completar({ margen: '-1' });
    grabar();
    await waitFor(() => expect(campo(ETIQUETA_MARGEN).getAttribute('aria-invalid')).toBe('true'));

    escribir(ETIQUETA_MARGEN, '50');
    grabar();

    await waitFor(() => expect(estados()).toEqual(['Artículo grabado con el código 7.']));
    expect(campo(ETIQUETA_MARGEN).getAttribute('aria-invalid')).toBe('false');
    expect(mensajeJuntoAlCampo(campo(ETIQUETA_MARGEN))).toBeNull();
  });

  it('NuevoArticulo_SinSesion401_NavegaAlIngreso', async () => {
    servidorFalso({ 'GET /api/sesion': vacia(401) });
    renderizarApp(RUTA);

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
  });

  it('NuevoArticulo_SesionVencidaAlGrabar_NavegaAlIngreso', async () => {
    servidorFalso({ ...conSesion, 'POST /api/articulos': vacia(401) });
    await abrir();

    completar();
    grabar();

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
  });

  it('NuevoArticulo_VerificacionDeSesionFallida_MensajeGeneral', async () => {
    servidorFalso({ 'GET /api/sesion': vacia(500) });
    renderizarApp(RUTA);

    expect((await screen.findByText(MENSAJE_SIN_CONEXION)).getAttribute('role')).toBe('alert');
    expect(screen.getByRole('heading', { name: TITULO })).toBeTruthy();
  });

  it.each([
    ['sin conexión', sinRed, MENSAJE_SIN_CONEXION],
    ['413', vacia(413), MENSAJE_CUERPO_GRANDE],
    ['429', vacia(429), 'El sistema está ocupado. Intentá de nuevo en un momento.'],
    ['500', vacia(500), MENSAJE_SIN_CONEXION],
  ] as const)('NuevoArticulo_SinConexion_ErrorGeneralConservaValores (%s)', async (_caso, respuesta, mensaje) => {
    servidorFalso({ ...conSesion, 'POST /api/articulos': respuesta });
    await abrir();

    completar({ codigo: 'ABC-9', descripcion: 'Lente', costo: '1210,50', margen: '35' });
    grabar();

    expect((await screen.findByText(mensaje)).getAttribute('role')).toBe('alert');
    expect(campo(ETIQUETA_CODIGO).value).toBe('ABC-9');
    expect(campo(ETIQUETA_DESCRIPCION).value).toBe('Lente');
    expect(campo(ETIQUETA_COSTO).value).toBe('1210,50');
    expect(campo(ETIQUETA_MARGEN).value).toBe('35');
    for (const etiqueta of ETIQUETAS_EDITABLES) {
      expect(mensajeJuntoAlCampo(campo(etiqueta))).toBeNull();
    }
    expect(campo(ETIQUETA_VENTA).value).toBe('');
    expect(estados()).toEqual([]);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
  });

  describe('con reloj falso', () => {
    // Como en Inicio: el reloj falso se activa antes de renderizar y no se usan findBy/waitFor.
    beforeEach(() => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'] });
    });
    afterEach(() => {
      vi.useRealTimers();
    });

    const avanzar = (ms: number) =>
      act(async () => {
        await vi.advanceTimersByTimeAsync(ms);
      });

    it('NuevoArticulo_NoConsultaPeriodicamente', async () => {
      const servidor = servidorFalso(conSesion);
      renderizarApp(RUTA);
      for (let i = 0; i < 50 && screen.queryByRole('button', { name: BOTON }) === null; i++) {
        await avanzar(0);
      }
      expect(screen.getByRole('button', { name: BOTON })).toBeTruthy();
      const pedidos = servidor.llamadas.length;

      await avanzar(61 * 60 * 1000);

      expect(servidor.llamadas.slice(pedidos)).toEqual([]);
      expect(servidor.pedidosA('GET', '/api/sesion')).toHaveLength(1);
      expect(vi.getTimerCount()).toBe(0);
    });
  });
});
