import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { mensajeJuntoAlCampo } from '../pruebas/formulario';
import { json, renderizarApp, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';

afterEach(() => {
  try {
    // El listado y lo buscado viven solo en el estado del componente (D-12).
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  } finally {
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
});

const RUTA = '/articulos';
const TITULO = 'Artículos';
const ETIQUETA_BUSQUEDA = 'Texto a buscar';
const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';
const MENSAJE_LISTADO_CAMBIADO = 'El artículo cambió. Volvé a cargar el listado.';
const ENCABEZADOS = ['Código', 'Código en el proveedor', 'Descripción', 'Precio de venta ($)', 'Estado', 'Acción'];

const ARMAZON = { codigo: 7, codigoProveedor: 'ABC-1', descripcion: 'Armazón metálico', precioVenta: 1815, activo: true };
const ARMAZON_ACETATO = {
  codigo: 9,
  codigoProveedor: 'AC-9',
  descripcion: 'armazon de acetato',
  precioVenta: 2420.5,
  activo: true,
};
const LENTE = { codigo: 8, codigoProveedor: 'L-8', descripcion: 'Lente orgánico', precioVenta: 1000, activo: true };

/** El cuerpo del artículo completo que devuelve el cambio de estado (con costo y margen, que el listado no usa). */
const completo = (fila: typeof ARMAZON, activo: boolean, version: number) => ({
  ...fila,
  precioCosto: 1210,
  margenUtilidad: 50,
  advertencia: null,
  activo,
  version,
});

const pagina = (articulos: unknown[], numero = 1, total = articulos.length) => ({
  articulos,
  pagina: numero,
  tamanoPagina: 50,
  total,
});

const conSesion = { 'GET /api/sesion': vacia(204) };

const DESACTIVAR_7 = 'Desactivar 7 Armazón metálico';

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

async function abrir() {
  renderizarApp(RUTA);
  await screen.findByRole('heading', { name: TITULO });
}

/** El texto de cada celda de cada fila de datos de la tabla. */
function filas(): string[][] {
  const tabla = screen.getByRole('table');
  const [, ...datos] = within(tabla).getAllByRole('row');
  return datos.map((fila) => within(fila).getAllByRole('cell').map((celda) => celda.textContent ?? ''));
}

function filaDe(codigo: number): HTMLElement {
  const fila = within(screen.getByRole('table'))
    .getAllByRole('row')
    .find((f) => within(f).queryAllByRole('cell')[0]?.textContent === String(codigo));
  if (fila === undefined) {
    throw new Error(`No hay una fila para el código ${codigo}.`);
  }
  return fila;
}

function mensajes(): string[] {
  const pantalla = screen.getByRole('heading', { name: TITULO }).closest('section')!;
  return within(pantalla)
    .queryAllByRole('alert')
    .map((elemento) => elemento.textContent ?? '');
}

describe('Articulos', () => {
  it('Articulos_Buscar_MuestraCoincidencias', async () => {
    // AC-04: "armazón" encuentra "Armazón" y "armazon" (lo pliega la Api) y ninguno que no lo contenga.
    const servidor = servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON, LENTE, ARMAZON_ACETATO])),
      'GET /api/articulos?texto=armaz%C3%B3n&pagina=1': json(200, pagina([ARMAZON, ARMAZON_ACETATO])),
    });
    await abrir();

    // Al abrir lista todo el catálogo (D-06).
    await waitFor(() => expect(filas()).toHaveLength(3));
    const encabezados = within(screen.getByRole('table'))
      .getAllByRole('columnheader')
      .map((celda) => celda.textContent);
    expect(encabezados).toEqual(ENCABEZADOS);

    expect(within(screen.getByRole('table')).getByText('Artículos del catálogo').tagName).toBe('CAPTION');

    const campo = screen.getByLabelText(ETIQUETA_BUSQUEDA) as HTMLInputElement;
    const pedidosAntes = servidor.llamadas.length;
    fireEvent.change(campo, { target: { value: 'a' } });
    fireEvent.change(campo, { target: { value: '  armazón ' } });
    // No busca en cada tecla: ningún pedido hasta Enter o el botón.
    expect(servidor.llamadas.length).toBe(pedidosAntes);

    // Enter en el campo envía el formulario de búsqueda.
    fireEvent.submit(campo.form!);

    await waitFor(() =>
      expect(filas()).toEqual([
        ['7', 'ABC-1', 'Armazón metálico', '1.815,00', 'Activo', 'Desactivar'],
        ['9', 'AC-9', 'armazon de acetato', '2.420,50', 'Activo', 'Desactivar'],
      ]),
    );
    expect(screen.queryByText('Lente orgánico')).toBeNull();
    // D-12: ni costo ni margen en el listado.
    expect(screen.queryByText(/costo|margen/i)).toBeNull();
    // Enlace al artículo y botón con el código y la descripción en su nombre accesible.
    expect(within(filaDe(7)).getByRole('link', { name: 'Editar 7 Armazón metálico' }).getAttribute('href')).toBe(
      '/articulos/7',
    );
    expect(within(filaDe(9)).getByRole('button', { name: 'Desactivar 9 armazon de acetato' })).toBeTruthy();

    // También con el botón.
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await waitFor(() => expect(servidor.pedidosA('GET', '/api/articulos?texto=armaz%C3%B3n&pagina=1')).toHaveLength(2));
    expect(campo.value).toBe('  armazón ');
  });

  it('Articulos_Paginas_AnteriorYSiguiente', async () => {
    // D-01: de a 50 por página, con Anterior y Siguiente.
    const servidor = servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON], 1, 120)),
      'GET /api/articulos?pagina=2': json(200, pagina([LENTE], 2, 120)),
      'GET /api/articulos?pagina=3': json(200, pagina([ARMAZON_ACETATO], 3, 120)),
    });
    await abrir();

    const anterior = () => screen.getByRole('button', { name: 'Anterior' }) as HTMLButtonElement;
    const siguiente = () => screen.getByRole('button', { name: 'Siguiente' }) as HTMLButtonElement;

    await screen.findByText('Página 1 de 3');
    expect(anterior().disabled).toBe(true);
    expect(siguiente().disabled).toBe(false);

    fireEvent.click(siguiente());
    await screen.findByText('Página 2 de 3');
    expect(filas()[0]?.[2]).toBe('Lente orgánico');
    expect(anterior().disabled).toBe(false);
    expect(siguiente().disabled).toBe(false);

    fireEvent.click(siguiente());
    await screen.findByText('Página 3 de 3');
    expect(siguiente().disabled).toBe(true);

    fireEvent.click(anterior());
    await screen.findByText('Página 2 de 3');
    expect(servidor.pedidosA('GET', '/api/articulos?pagina=2')).toHaveLength(2);

    // Una búsqueda nueva vuelve a la primera página.
    fireEvent.change(screen.getByLabelText(ETIQUETA_BUSQUEDA), { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await screen.findByText('Página 1 de 3');
  });

  it('Articulos_Desactivar_MuestraInactivo', async () => {
    // AC-05: queda inactivo en el listado y conserva sus datos.
    const servidor = servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': [
        json(200, pagina([ARMAZON, LENTE])),
        json(200, pagina([{ ...ARMAZON, activo: false }, LENTE])),
      ],
      'PUT /api/articulos/7/estado': json(200, completo(ARMAZON, false, 4)),
    });
    await abrir();
    await waitFor(() => expect(filas()).toHaveLength(2));

    fireEvent.click(within(filaDe(7)).getByRole('button', { name: DESACTIVAR_7 }));

    await waitFor(() => expect(filas()[0]).toEqual(['7', 'ABC-1', 'Armazón metálico', '1.815,00', 'Inactivo', 'Reactivar']));
    expect(filas()[1]?.[4]).toBe('Activo');
    const [pedido] = servidor.pedidosA('PUT', '/api/articulos/7/estado');
    expect(JSON.parse(String(pedido?.init?.body))).toEqual({ activo: false });
    expect(new Headers(pedido?.init?.headers).get('Content-Type')).toBe('application/json');
    // Refresca la página actual.
    expect(servidor.pedidosA('GET', '/api/articulos?pagina=1')).toHaveLength(2);
  });

  it('Articulos_Reactivar_MuestraActivo', async () => {
    // AC-06.
    const servidor = servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=2': [
        json(200, pagina([{ ...LENTE, activo: false }], 2, 60)),
        json(200, pagina([LENTE], 2, 60)),
      ],
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON], 1, 60)),
      'PUT /api/articulos/8/estado': json(200, completo(LENTE, true, 6)),
    });
    await abrir();
    fireEvent.click(await screen.findByRole('button', { name: 'Siguiente' }));
    await screen.findByText('Página 2 de 2');
    expect(filas()[0]?.[4]).toBe('Inactivo');

    fireEvent.click(within(filaDe(8)).getByRole('button', { name: 'Reactivar 8 Lente orgánico' }));

    await waitFor(() => expect(filas()[0]).toEqual(['8', 'L-8', 'Lente orgánico', '1.000,00', 'Activo', 'Desactivar']));
    expect(JSON.parse(String(servidor.pedidosA('PUT', '/api/articulos/8/estado')[0]?.init?.body))).toEqual({
      activo: true,
    });
    // Refresca la página en la que estaba, no la primera.
    expect(screen.getByText('Página 2 de 2')).toBeTruthy();
    expect(servidor.pedidosA('GET', '/api/articulos?pagina=1')).toHaveLength(1);
  });

  it('Articulos_RespuestaIlegible_MensajeGeneral', async () => {
    servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([{ ...ARMAZON, precioVenta: '1815' }])),
    });
    await abrir();

    await waitFor(() => expect(mensajes()).toEqual([MENSAJE_SIN_CONEXION]));
    // Sin importes falsos: no hay tabla.
    expect(screen.queryByRole('table')).toBeNull();
    expect(screen.queryByText(/1\.815/)).toBeNull();
  });

  it('Articulos_SinSesion401_VuelveAlIngreso', async () => {
    servidorFalso({ 'GET /api/articulos?pagina=1': vacia(401) });
    renderizarApp(RUTA);

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
  });

  it('Articulos_CambioDeEstado409_MensajeYRecarga', async () => {
    const servidor = servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': [
        json(200, pagina([ARMAZON])),
        json(200, pagina([{ ...ARMAZON, activo: false }])),
      ],
      'PUT /api/articulos/7/estado': json(409, {
        status: 409,
        title: 'No se pudo grabar el artículo.',
        detail: 'texto del servidor',
        motivo: 'cambiado',
      }),
    });
    await abrir();
    await waitFor(() => expect(filas()).toHaveLength(1));

    fireEvent.click(within(filaDe(7)).getByRole('button', { name: DESACTIVAR_7 }));

    await waitFor(() => expect(mensajes()).toEqual([MENSAJE_LISTADO_CAMBIADO]));
    await waitFor(() => expect(filas()[0]?.[4]).toBe('Inactivo'));
    expect(servidor.pedidosA('GET', '/api/articulos?pagina=1')).toHaveLength(2);
    expect(screen.queryByText('texto del servidor')).toBeNull();
  });

  it.each([
    ['sin red', sinRed, MENSAJE_SIN_CONEXION],
    ['404', vacia(404), 'No se encontró el artículo.'],
  ] as const)('Articulos_CambioDeEstadoFallido_%s_MensajeGeneral', async (_caso, respuesta, mensaje) => {
    servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON])),
      'PUT /api/articulos/7/estado': respuesta,
    });
    await abrir();
    await waitFor(() => expect(filas()).toHaveLength(1));

    fireEvent.click(within(filaDe(7)).getByRole('button', { name: DESACTIVAR_7 }));

    await waitFor(() => expect(mensajes()).toEqual([mensaje]));
    expect(filas()[0]?.[4]).toBe('Activo');
  });

  it('Articulos_CambioDeEstadoSinSesion_VuelveAlIngreso', async () => {
    servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON])),
      'PUT /api/articulos/7/estado': vacia(401),
    });
    await abrir();
    await waitFor(() => expect(filas()).toHaveLength(1));

    fireEvent.click(within(filaDe(7)).getByRole('button', { name: DESACTIVAR_7 }));

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
  });

  it('Articulos_SinCoincidencias_Aviso', async () => {
    servidorFalso({ ...conSesion, 'GET /api/articulos?pagina=1': json(200, pagina([])) });
    await abrir();

    expect(await screen.findByText('No hay artículos que coincidan con la búsqueda.')).toBeTruthy();
    expect(screen.queryByRole('table')).toBeNull();
    expect(screen.getByRole('link', { name: 'Nuevo artículo' }).getAttribute('href')).toBe('/articulos/nuevo');
    expect(screen.getByRole('link', { name: 'Volver al inicio' }).getAttribute('href')).toBe('/');
  });

  it.each([
    ['más de 200 caracteres', `  ${'a'.repeat(201)}  `, 'Usá hasta 200 caracteres.'],
    ['UTF-16 inválido', `armaz${String.fromCharCode(0xd83d)}`, 'El texto tiene caracteres no válidos. Escribilo de nuevo.'],
  ])('Articulos_BusquedaInvalida_%s_ErrorJuntoAlCampoSinBuscar', async (_caso, texto, mensaje) => {
    const servidor = servidorFalso({ ...conSesion, 'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON])) });
    await abrir();
    await waitFor(() => expect(filas()).toHaveLength(1));
    const pedidos = servidor.llamadas.length;

    const campo = screen.getByLabelText(ETIQUETA_BUSQUEDA);
    fireEvent.change(campo, { target: { value: texto } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));

    await waitFor(() => expect(mensajeJuntoAlCampo(campo)).toBe(mensaje));
    expect(campo.getAttribute('aria-invalid')).toBe('true');
    expect(servidor.llamadas.slice(pedidos)).toEqual([]);
  });

  it('Articulos_200CaracteresTrasElRecorte_Busca', async () => {
    const texto = 'a'.repeat(200);
    const servidor = servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON])),
      [`GET /api/articulos?texto=${texto}&pagina=1`]: json(200, pagina([])),
    });
    await abrir();

    fireEvent.change(screen.getByLabelText(ETIQUETA_BUSQUEDA), { target: { value: ` ${texto} ` } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));

    await screen.findByText('No hay artículos que coincidan con la búsqueda.');
    expect(servidor.pedidosA('GET', `/api/articulos?texto=${texto}&pagina=1`)).toHaveLength(1);
  });

  it('Articulos_Error400DeLaApi_JuntoAlCampoOGeneral', async () => {
    servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON])),
      'GET /api/articulos?texto=x&pagina=1': json(400, { errors: { texto: ['Usá hasta 200 caracteres.'] } }),
      'GET /api/articulos?texto=y&pagina=1': json(400, { errors: { pagina: ['Pedí una página entre 1 y 10.000.'] } }),
    });
    await abrir();
    const campo = screen.getByLabelText(ETIQUETA_BUSQUEDA);

    fireEvent.change(campo, { target: { value: 'x' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await waitFor(() => expect(mensajeJuntoAlCampo(campo)).toBe('Usá hasta 200 caracteres.'));

    fireEvent.change(campo, { target: { value: 'y' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await waitFor(() => expect(mensajes()).toEqual(['Pedí una página entre 1 y 10.000.']));
    expect(mensajeJuntoAlCampo(campo)).toBeNull();
  });

  it('Articulos_DobleClic_UnSoloPut', async () => {
    const put = diferida(completo(ARMAZON, false, 4));
    const servidor = servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': [json(200, pagina([ARMAZON, LENTE])), json(200, pagina([{ ...ARMAZON, activo: false }, LENTE]))],
      'PUT /api/articulos/7/estado': put.respuesta,
      'PUT /api/articulos/8/estado': json(200, completo(LENTE, false, 2)),
    });
    await abrir();
    await waitFor(() => expect(filas()).toHaveLength(2));

    const boton = within(filaDe(7)).getByRole('button', { name: DESACTIVAR_7 }) as HTMLButtonElement;
    // Los dos clics antes de que React vuelva a dibujar: el botón todavía está habilitado en el DOM, y solo la guarda
    // síncrona evita el segundo pedido.
    act(() => {
      boton.click();
      boton.click();
    });
    // Mientras hay un cambio en curso, ningún botón de fila admite otro.
    fireEvent.click(within(filaDe(8)).getByRole('button', { name: 'Desactivar 8 Lente orgánico' }));

    expect(servidor.pedidosA('PUT', '/api/articulos/7/estado')).toHaveLength(1);
    expect(servidor.pedidosA('PUT', '/api/articulos/8/estado')).toHaveLength(0);
    expect(boton.disabled).toBe(true);

    await put.liberar();
    await waitFor(() => expect(filas()[0]?.[4]).toBe('Inactivo'));
    expect(servidor.pedidosA('PUT', '/api/articulos/7/estado')).toHaveLength(1);
  });

  it('Articulos_CambioDeEstadoOk_ActualizaLaFilaAntesDeRecargar', async () => {
    const recarga = diferida(pagina([{ ...ARMAZON, activo: false }]));
    servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': [json(200, pagina([ARMAZON])), recarga.respuesta],
      'PUT /api/articulos/7/estado': json(200, completo(ARMAZON, false, 4)),
    });
    await abrir();
    await waitFor(() => expect(filas()).toHaveLength(1));

    fireEvent.click(within(filaDe(7)).getByRole('button', { name: DESACTIVAR_7 }));

    // La etiqueta sale del artículo que devolvió el PUT, sin esperar la recarga.
    await waitFor(() => expect(filas()[0]?.[4]).toBe('Inactivo'));
    const reactivar = within(filaDe(7)).getByRole('button', { name: 'Reactivar 7 Armazón metálico' }) as HTMLButtonElement;
    // Durante la recarga los botones de fila quedan deshabilitados.
    expect(reactivar.disabled).toBe(true);

    await recarga.liberar();
    await waitFor(() => expect(reactivar.disabled).toBe(false));
    expect(filas()[0]?.[4]).toBe('Inactivo');
  });

  it('Articulos_RespuestaViejaDeBusqueda_NoPisaLaNueva', async () => {
    const vieja = diferida(pagina([ARMAZON_ACETATO]));
    servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON])),
      'GET /api/articulos?texto=acetato&pagina=1': vieja.respuesta,
      'GET /api/articulos?texto=lente&pagina=1': json(200, pagina([LENTE])),
    });
    await abrir();
    await waitFor(() => expect(filas()).toHaveLength(1));
    const campo = screen.getByLabelText(ETIQUETA_BUSQUEDA);

    fireEvent.change(campo, { target: { value: 'acetato' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    fireEvent.change(campo, { target: { value: 'lente' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await waitFor(() => expect(filas()[0]?.[2]).toBe('Lente orgánico'));

    await vieja.liberar();

    expect(filas()).toEqual([['8', 'L-8', 'Lente orgánico', '1.000,00', 'Activo', 'Desactivar']]);
  });

  it('Articulos_RespuestaViejaDePagina_NoPisaLaBusquedaNueva', async () => {
    const vieja = diferida(pagina([ARMAZON_ACETATO], 2, 120));
    servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON], 1, 120)),
      'GET /api/articulos?pagina=2': vieja.respuesta,
      'GET /api/articulos?texto=lente&pagina=1': json(200, pagina([LENTE])),
    });
    await abrir();
    fireEvent.click(await screen.findByRole('button', { name: 'Siguiente' }));

    fireEvent.change(screen.getByLabelText(ETIQUETA_BUSQUEDA), { target: { value: 'lente' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await waitFor(() => expect(filas()[0]?.[2]).toBe('Lente orgánico'));

    await vieja.liberar();

    expect(filas()[0]?.[2]).toBe('Lente orgánico');
    expect(screen.getByText('Página 1 de 1')).toBeTruthy();
  });

  it('Articulos_PaginarConBusqueda_ConservaElTexto', async () => {
    const servidor = servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([LENTE], 1, 120)),
      'GET /api/articulos?texto=armaz%C3%B3n&pagina=1': json(200, pagina([ARMAZON], 1, 120)),
      'GET /api/articulos?texto=armaz%C3%B3n&pagina=2': json(200, pagina([ARMAZON_ACETATO], 2, 120)),
    });
    await abrir();
    await waitFor(() => expect(filas()).toHaveLength(1));

    fireEvent.change(screen.getByLabelText(ETIQUETA_BUSQUEDA), { target: { value: 'armazón' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await waitFor(() => expect(filas()[0]?.[2]).toBe('Armazón metálico'));

    fireEvent.click(screen.getByRole('button', { name: 'Siguiente' }));
    await waitFor(() => expect(filas()[0]?.[2]).toBe('armazon de acetato'));
    fireEvent.click(screen.getByRole('button', { name: 'Anterior' }));
    await waitFor(() => expect(filas()[0]?.[2]).toBe('Armazón metálico'));

    expect(servidor.pedidosA('GET', '/api/articulos?texto=armaz%C3%B3n&pagina=2')).toHaveLength(1);
    expect(servidor.pedidosA('GET', '/api/articulos?texto=armaz%C3%B3n&pagina=1')).toHaveLength(2);
    expect(servidor.pedidosA('GET', '/api/articulos?pagina=1')).toHaveLength(1);
  });
});
