import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { mensajeJuntoAlCampo } from '../pruebas/formulario';
import { json, renderizarApp, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';

afterEach(() => {
  try {
    // Costo, margen y versión viven solo en el estado del componente (D-12).
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  } finally {
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
});

const TITULO = 'Editar artículo';
const ETIQUETA_CODIGO = 'Código en el proveedor';
const ETIQUETA_DESCRIPCION = 'Descripción';
const ETIQUETA_COSTO = 'Precio de costo ($)';
const ETIQUETA_MARGEN = 'Margen de utilidad (%)';
const ETIQUETA_VENTA = 'Precio de venta ($)';
const ETIQUETAS = [ETIQUETA_CODIGO, ETIQUETA_DESCRIPCION, ETIQUETA_COSTO, ETIQUETA_MARGEN, ETIQUETA_VENTA];
const ETIQUETAS_EDITABLES = ETIQUETAS.slice(0, 4);
const NO_ENCONTRADO = 'No se encontró el artículo.';
const AVISO_INACTIVO = 'Este artículo está inactivo. Reactivalo para editarlo.';
const MENSAJE_CAMBIADO = 'El artículo cambió mientras lo editabas. Volvé a abrirlo.';
const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';
const GRABADO_OK = 'Artículo grabado.';

const ARTICULO = {
  codigo: 7,
  codigoProveedor: 'ABC-1',
  descripcion: 'Armazón metálico',
  precioCosto: 1210,
  margenUtilidad: 50,
  precioVenta: 1815,
  advertencia: null,
  activo: true,
  version: 3,
};

const RUTA = '/articulos/7';
const API = '/api/articulos/7';

/** Una respuesta que llega cuando la prueba la libera: para ordenar a mano respuestas que se cruzan. */
function diferida(cuerpo: unknown) {
  let liberar: () => void = () => {};
  const llegada = new Promise<void>((resolver) => {
    liberar = resolver;
  });
  return {
    respuesta: async () => {
      await llegada;
      return json(200, cuerpo)();
    },
    liberar: () => act(async () => liberar()),
  };
}

const conflicto = (motivo: string) =>
  json(409, { status: 409, title: 'No se pudo grabar el artículo.', detail: 'texto del servidor', motivo });

async function abrir(ruta = RUTA) {
  renderizarApp(ruta);
  await screen.findByRole('heading', { name: TITULO });
}

/** Espera el formulario cargado. */
async function abrirFormulario() {
  await abrir();
  await waitFor(() => expect(campo(ETIQUETA_CODIGO).value).toBe('ABC-1'));
}

const campo = (etiqueta: string) => screen.getByLabelText(etiqueta) as HTMLInputElement;

const escribir = (etiqueta: string, valor: string) => fireEvent.change(campo(etiqueta), { target: { value: valor } });

const grabar = () => fireEvent.click(screen.getByRole('button', { name: 'Grabar' }));

function pantalla() {
  return within(screen.getByRole('heading', { name: TITULO }).closest('section')!);
}

const estados = () => pantalla().queryAllByRole('status').map((e) => e.textContent ?? '');

describe('EditarArticulo', () => {
  it.each(['/articulos/abc', '/articulos/0', '/articulos/-1', '/articulos/1.5', '/articulos/007', '/articulos/9007199254740992'])(
    'EditarArticulo_CodigoInvalido_NoEncontrado (%s)',
    async (ruta) => {
      const servidor = servidorFalso({});
      await abrir(ruta);

      expect((await screen.findByText(NO_ENCONTRADO)).getAttribute('role')).toBe('alert');
      expect(screen.getByRole('link', { name: 'Volver al listado' }).getAttribute('href')).toBe('/articulos');
      expect(screen.queryByLabelText(ETIQUETA_CODIGO)).toBeNull();
      expect(servidor.llamadas.filter((l) => l.ruta.startsWith('/api/articulos'))).toEqual([]);
    },
  );

  it('EditarArticulo_404_NoEncontrado', async () => {
    servidorFalso({ [`GET ${API}`]: vacia(404) });
    await abrir();

    expect(await screen.findByText(NO_ENCONTRADO)).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Volver al listado' }).getAttribute('href')).toBe('/articulos');
    expect(screen.queryByLabelText(ETIQUETA_CODIGO)).toBeNull();
  });

  it('EditarArticulo_PrecioVentaSoloLectura', async () => {
    // AC-01.
    const servidor = servidorFalso({ [`GET ${API}`]: json(200, ARTICULO), [`PUT ${API}`]: json(200, ARTICULO) });
    await abrirFormulario();

    expect(campo(ETIQUETA_VENTA).readOnly).toBe(true);
    expect(campo(ETIQUETA_VENTA).value).toBe('1.815,00');
    for (const etiqueta of ETIQUETAS_EDITABLES) {
      expect(campo(etiqueta).readOnly).toBe(false);
    }
    expect(campo(ETIQUETA_DESCRIPCION).value).toBe('Armazón metálico');
    expect(campo(ETIQUETA_COSTO).value).toBe('1210');
    expect(campo(ETIQUETA_MARGEN).value).toBe('50');
    expect(pantalla().getByText('Código 7')).toBeTruthy();

    grabar();

    await waitFor(() => expect(servidor.pedidosA('PUT', API)).toHaveLength(1));
    const cuerpo = JSON.parse(String(servidor.pedidosA('PUT', API)[0]?.init?.body)) as object;
    expect(Object.keys(cuerpo)).not.toContain('precioVenta');
  });

  it('EditarArticulo_GrabarMargen_MuestraPrecioNuevo', async () => {
    // AC-02: costo $1.210, margen 50 % → 60 %: $1.936,00.
    const servidor = servidorFalso({
      [`GET ${API}`]: json(200, ARTICULO),
      [`PUT ${API}`]: json(200, { ...ARTICULO, margenUtilidad: 60, precioVenta: 1936, version: 4 }),
    });
    await abrirFormulario();

    escribir(ETIQUETA_MARGEN, '60');
    grabar();

    await waitFor(() => expect(campo(ETIQUETA_VENTA).value).toBe('1.936,00'));
    expect(estados()).toEqual([GRABADO_OK]);
    expect(JSON.parse(String(servidor.pedidosA('PUT', API)[0]?.init?.body))).toEqual({
      codigoProveedor: 'ABC-1',
      descripcion: 'Armazón metálico',
      precioCosto: 1210,
      margenUtilidad: 60,
      version: 3,
    });
    expect(campo(ETIQUETA_MARGEN).value).toBe('60');

    // La siguiente grabación envía la versión nueva, no la leída al abrir.
    escribir(ETIQUETA_DESCRIPCION, 'Armazón de titanio');
    expect(estados()).toEqual([]);
    grabar();
    await waitFor(() => expect(servidor.pedidosA('PUT', API)).toHaveLength(2));
    expect(JSON.parse(String(servidor.pedidosA('PUT', API)[1]?.init?.body))).toHaveProperty('version', 4);
  });

  it('EditarArticulo_GrabarCosto_MuestraPrecioNuevo', async () => {
    // AC-03: costo $1.210 → $2.420 con margen 50 %: $3.630,00.
    const servidor = servidorFalso({
      [`GET ${API}`]: json(200, ARTICULO),
      [`PUT ${API}`]: json(200, { ...ARTICULO, precioCosto: 2420, precioVenta: 3630, version: 4 }),
    });
    await abrirFormulario();

    escribir(ETIQUETA_COSTO, '2420');
    grabar();

    await waitFor(() => expect(campo(ETIQUETA_VENTA).value).toBe('3.630,00'));
    expect(estados()).toEqual([GRABADO_OK]);
    expect(JSON.parse(String(servidor.pedidosA('PUT', API)[0]?.init?.body))).toMatchObject({
      precioCosto: 2420,
      margenUtilidad: 50,
      version: 3,
    });
  });

  it('EditarArticulo_GrabarConAdvertencia_LaMuestra', async () => {
    servidorFalso({
      [`GET ${API}`]: json(200, { ...ARTICULO, precioCosto: 12.5, margenUtilidad: 0.25 }),
      [`PUT ${API}`]: json(200, {
        ...ARTICULO,
        precioCosto: -100,
        precioVenta: -150,
        advertencia: 'Precio de costo negativo o cero',
      }),
    });
    await abrir();
    // Los decimales se muestran con coma, como los escribe la operadora.
    await waitFor(() => expect(campo(ETIQUETA_COSTO).value).toBe('12,5'));
    expect(campo(ETIQUETA_MARGEN).value).toBe('0,25');

    escribir(ETIQUETA_COSTO, '-100');
    grabar();

    await waitFor(() => expect(estados()).toEqual([GRABADO_OK, 'Precio de costo negativo o cero']));
    expect(campo(ETIQUETA_VENTA).value).toBe('-150,00');
  });

  it('EditarArticulo_ErroresPorCampo_400', async () => {
    servidorFalso({
      [`GET ${API}`]: json(200, ARTICULO),
      [`PUT ${API}`]: json(400, {
        status: 400,
        errors: {
          codigoProveedor: ['Ese código ya pertenece a otro artículo.'],
          margenUtilidad: ['El margen debe estar entre 0 y 1.000.'],
          version: ['Volvé a abrir el artículo para editarlo.'],
        },
      }),
    });
    await abrirFormulario();

    escribir(ETIQUETA_CODIGO, 'xyz-2');
    grabar();

    await waitFor(() =>
      expect(mensajeJuntoAlCampo(campo(ETIQUETA_CODIGO))).toBe('Ese código ya pertenece a otro artículo.'),
    );
    expect(mensajeJuntoAlCampo(campo(ETIQUETA_MARGEN))).toBe('El margen debe estar entre 0 y 1.000.');
    expect(campo(ETIQUETA_CODIGO).getAttribute('aria-invalid')).toBe('true');
    expect(mensajeJuntoAlCampo(campo(ETIQUETA_DESCRIPCION))).toBeNull();
    // El error en `version` no es de un campo del formulario: mensaje general con el botón para volver a abrir.
    expect(screen.getByText('Volvé a abrir el artículo para editarlo.').getAttribute('role')).toBe('alert');
    expect(screen.getByRole('button', { name: 'Volver a abrir' })).toBeTruthy();
    expect(campo(ETIQUETA_CODIGO).value).toBe('xyz-2');
    expect(campo(ETIQUETA_VENTA).value).toBe('1.815,00');
    expect(estados()).toEqual([]);
  });

  it('EditarArticulo_ErroresEnElNavegador_NoEnvia', async () => {
    const servidor = servidorFalso({ [`GET ${API}`]: json(200, ARTICULO), [`PUT ${API}`]: json(200, ARTICULO) });
    await abrirFormulario();

    escribir(ETIQUETA_DESCRIPCION, '  ');
    escribir(ETIQUETA_MARGEN, '1000,01');
    grabar();

    await waitFor(() => expect(mensajeJuntoAlCampo(campo(ETIQUETA_DESCRIPCION))).toBe('Completá la descripción.'));
    expect(mensajeJuntoAlCampo(campo(ETIQUETA_MARGEN))).toBe('El margen debe estar entre 0 y 1.000.');
    expect(servidor.pedidosA('PUT', API)).toHaveLength(0);
  });

  it.each([
    ['cambiado', MENSAJE_CAMBIADO],
    ['inactivo', AVISO_INACTIVO],
  ])('EditarArticulo_Conflicto409_MuestraMensaje (%s)', async (motivo, mensaje) => {
    const servidor = servidorFalso({
      [`GET ${API}`]: [json(200, ARTICULO), json(200, { ...ARTICULO, descripcion: 'Otra descripción', version: 5 })],
      [`PUT ${API}`]: [conflicto(motivo), json(200, ARTICULO)],
    });
    await abrirFormulario();

    escribir(ETIQUETA_MARGEN, '60');
    grabar();

    const alerta = await screen.findByText(mensaje);
    expect(alerta.getAttribute('role')).toBe('alert');
    // Arriba del formulario, y nunca el texto del servidor (D-19).
    const formulario = campo(ETIQUETA_CODIGO).closest('form')!;
    expect(alerta.compareDocumentPosition(formulario) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.queryByText('texto del servidor')).toBeNull();
    expect(estados()).toEqual([]);

    fireEvent.click(screen.getByRole('button', { name: 'Volver a abrir' }));

    await waitFor(() => expect(campo(ETIQUETA_DESCRIPCION).value).toBe('Otra descripción'));
    expect(screen.queryByText(mensaje)).toBeNull();
    expect(campo(ETIQUETA_MARGEN).value).toBe('50');
    expect(servidor.pedidosA('GET', API)).toHaveLength(2);

    grabar();
    await waitFor(() => expect(servidor.pedidosA('PUT', API)).toHaveLength(2));
    expect(JSON.parse(String(servidor.pedidosA('PUT', API)[1]?.init?.body))).toHaveProperty('version', 5);
  });

  it('EditarArticulo_Conflicto409ConMotivoDesconocido_MensajeGeneral', async () => {
    servidorFalso({ [`GET ${API}`]: json(200, ARTICULO), [`PUT ${API}`]: conflicto('otro') });
    await abrirFormulario();

    grabar();

    expect((await screen.findByText(MENSAJE_SIN_CONEXION)).getAttribute('role')).toBe('alert');
    expect(screen.queryByRole('button', { name: 'Volver a abrir' })).toBeNull();
    expect(screen.queryByText('texto del servidor')).toBeNull();
  });

  it('EditarArticulo_Inactivo_SoloLecturaConReactivar', async () => {
    // D-02 y D-26: reactivar devuelve la versión nueva, y la edición siguiente la envía.
    const servidor = servidorFalso({
      [`GET ${API}`]: json(200, { ...ARTICULO, activo: false, version: 4 }),
      [`PUT ${API}/estado`]: json(200, { ...ARTICULO, activo: true, version: 5 }),
      [`PUT ${API}`]: json(200, { ...ARTICULO, margenUtilidad: 60, precioVenta: 1936, version: 6 }),
    });
    await abrirFormulario();

    expect(screen.getByText(AVISO_INACTIVO)).toBeTruthy();
    for (const etiqueta of ETIQUETAS) {
      expect(campo(etiqueta).readOnly).toBe(true);
    }
    expect(screen.queryByRole('button', { name: 'Grabar' })).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Reactivar' }));

    await waitFor(() => expect(screen.queryByText(AVISO_INACTIVO)).toBeNull());
    expect(JSON.parse(String(servidor.pedidosA('PUT', `${API}/estado`)[0]?.init?.body))).toEqual({ activo: true });
    for (const etiqueta of ETIQUETAS_EDITABLES) {
      expect(campo(etiqueta).readOnly).toBe(false);
    }
    expect(campo(ETIQUETA_VENTA).readOnly).toBe(true);
    expect(screen.queryByRole('button', { name: 'Reactivar' })).toBeNull();

    escribir(ETIQUETA_MARGEN, '60');
    grabar();

    await waitFor(() => expect(campo(ETIQUETA_VENTA).value).toBe('1.936,00'));
    expect(JSON.parse(String(servidor.pedidosA('PUT', API)[0]?.init?.body))).toHaveProperty('version', 5);
  });

  it('EditarArticulo_ReactivarFallido_SinRed_MensajeGeneral', async () => {
    servidorFalso({
      [`GET ${API}`]: json(200, { ...ARTICULO, activo: false }),
      [`PUT ${API}/estado`]: sinRed,
    });
    await abrirFormulario();

    fireEvent.click(screen.getByRole('button', { name: 'Reactivar' }));

    expect((await screen.findByText(MENSAJE_SIN_CONEXION)).getAttribute('role')).toBe('alert');
    expect(screen.getByText(AVISO_INACTIVO)).toBeTruthy();
    expect(campo(ETIQUETA_MARGEN).readOnly).toBe(true);
  });

  it('EditarArticulo_Reactivar409_MensajeDeLaEdicionConVolverAAbrir', async () => {
    const servidor = servidorFalso({
      [`GET ${API}`]: [json(200, { ...ARTICULO, activo: false }), json(200, { ...ARTICULO, version: 9 })],
      [`PUT ${API}/estado`]: conflicto('cambiado'),
    });
    await abrirFormulario();

    fireEvent.click(screen.getByRole('button', { name: 'Reactivar' }));

    // El texto de la edición, no el del listado.
    expect((await screen.findByText(MENSAJE_CAMBIADO)).getAttribute('role')).toBe('alert');
    expect(screen.queryByText('El artículo cambió. Volvé a cargar el listado.')).toBeNull();
    expect(screen.queryByText('texto del servidor')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Volver a abrir' }));

    await waitFor(() => expect(screen.queryByText(AVISO_INACTIVO)).toBeNull());
    expect(screen.queryByText(MENSAJE_CAMBIADO)).toBeNull();
    expect(servidor.pedidosA('GET', API)).toHaveLength(2);
  });

  it('EditarArticulo_VolverAAbrir_GrabarNoDisponibleDuranteLaReapertura', async () => {
    const reapertura = diferida({ ...ARTICULO, descripcion: 'Otra descripción', version: 5 });
    const servidor = servidorFalso({
      [`GET ${API}`]: [json(200, ARTICULO), reapertura.respuesta],
      [`PUT ${API}`]: conflicto('cambiado'),
    });
    await abrirFormulario();
    grabar();
    fireEvent.click(await screen.findByRole('button', { name: 'Volver a abrir' }));

    // Sin el formulario de la versión vieja mientras llega la nueva.
    expect(screen.queryByRole('button', { name: 'Grabar' })).toBeNull();
    expect(screen.queryByLabelText(ETIQUETA_CODIGO)).toBeNull();
    expect(screen.getByText('Cargando…')).toBeTruthy();

    await reapertura.liberar();

    await waitFor(() => expect(campo(ETIQUETA_DESCRIPCION).value).toBe('Otra descripción'));
    expect(screen.getByRole('button', { name: 'Grabar' })).toBeTruthy();
    expect(servidor.pedidosA('PUT', API)).toHaveLength(1);
  });

  it('EditarArticulo_OtroCodigo_GrabarNoDisponibleHastaCargarlo', async () => {
    const otro = diferida({ ...ARTICULO, codigo: 8, descripcion: 'Lente orgánico', version: 1 });
    servidorFalso({ [`GET ${API}`]: json(200, ARTICULO), 'GET /api/articulos/8': otro.respuesta });
    const { volver } = renderizarApp(['/articulos/8', RUTA]);
    await screen.findByRole('heading', { name: TITULO });
    await waitFor(() => expect(campo(ETIQUETA_CODIGO).value).toBe('ABC-1'));

    await volver();

    expect(screen.getByTestId('ubicacion').textContent).toBe('/articulos/8');
    expect(screen.queryByRole('button', { name: 'Grabar' })).toBeNull();
    expect(screen.queryByText('Armazón metálico')).toBeNull();
    expect(screen.queryByLabelText(ETIQUETA_DESCRIPCION)).toBeNull();

    await otro.liberar();

    await waitFor(() => expect(campo(ETIQUETA_DESCRIPCION).value).toBe('Lente orgánico'));
    expect(pantalla().getByText('Código 8')).toBeTruthy();
  });

  it('EditarArticulo_RespuestaViejaDeOtroCodigo_NoPisaLaNueva', async () => {
    const vieja = diferida(ARTICULO);
    servidorFalso({
      [`GET ${API}`]: vieja.respuesta,
      'GET /api/articulos/8': json(200, { ...ARTICULO, codigo: 8, descripcion: 'Lente orgánico', version: 1 }),
    });
    const { volver } = renderizarApp(['/articulos/8', RUTA]);
    await screen.findByRole('heading', { name: TITULO });

    await volver();
    await waitFor(() => expect(campo(ETIQUETA_DESCRIPCION).value).toBe('Lente orgánico'));

    await vieja.liberar();

    expect(campo(ETIQUETA_DESCRIPCION).value).toBe('Lente orgánico');
    expect(pantalla().getByText('Código 8')).toBeTruthy();
  });

  it.each([
    ['sin red', sinRed, MENSAJE_SIN_CONEXION],
    ['respuesta ilegible', json(200, { ...ARTICULO, version: undefined }), MENSAJE_SIN_CONEXION],
    ['500', vacia(500), MENSAJE_SIN_CONEXION],
  ] as const)('EditarArticulo_CargaFallida_%s_MensajeGeneralSinImportes', async (_caso, respuesta, mensaje) => {
    servidorFalso({ [`GET ${API}`]: respuesta });
    await abrir();

    expect((await screen.findByText(mensaje)).getAttribute('role')).toBe('alert');
    expect(screen.queryByLabelText(ETIQUETA_VENTA)).toBeNull();
    expect(screen.queryByText(/1\.815/)).toBeNull();
    expect(screen.getByRole('link', { name: 'Volver al listado' })).toBeTruthy();
  });

  it.each([
    ['sin red', sinRed, MENSAJE_SIN_CONEXION],
    ['413', vacia(413), 'Los datos enviados son demasiado largos. Revisalos y grabá de nuevo.'],
  ] as const)('EditarArticulo_GrabacionFallida_%s_ConservaLoEscrito', async (_caso, respuesta, mensaje) => {
    servidorFalso({ [`GET ${API}`]: json(200, ARTICULO), [`PUT ${API}`]: respuesta });
    await abrirFormulario();

    escribir(ETIQUETA_MARGEN, '60');
    grabar();

    expect((await screen.findByText(mensaje)).getAttribute('role')).toBe('alert');
    expect(campo(ETIQUETA_MARGEN).value).toBe('60');
    expect(campo(ETIQUETA_VENTA).value).toBe('1.815,00');
  });

  it('EditarArticulo_SinSesion401_VuelveAlIngreso', async () => {
    servidorFalso({ [`GET ${API}`]: vacia(401) });
    renderizarApp(RUTA);

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
  });

  it('EditarArticulo_SesionVencidaAlGrabar_VuelveAlIngreso', async () => {
    servidorFalso({ [`GET ${API}`]: json(200, ARTICULO), [`PUT ${API}`]: vacia(401) });
    await abrirFormulario();

    grabar();

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
  });

  it('EditarArticulo_SesionVencidaAlReactivar_VuelveAlIngreso', async () => {
    servidorFalso({ [`GET ${API}`]: json(200, { ...ARTICULO, activo: false }), [`PUT ${API}/estado`]: vacia(401) });
    await abrirFormulario();

    fireEvent.click(screen.getByRole('button', { name: 'Reactivar' }));

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
  });
});
