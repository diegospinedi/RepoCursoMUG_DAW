import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { diferida } from '../pruebas/diferida';
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

/**
 * El campo de búsqueda. Desde FEAT-013 es un combobox con sugerencias, y la lista también se nombra con la etiqueta
 * (`aria-labelledby`): por eso se busca por su rol y no solo por la etiqueta.
 */
const combobox = () => screen.getByRole('combobox', { name: ETIQUETA_BUSQUEDA }) as HTMLInputElement;

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

    const campo = combobox();
    const pedidosAntes = servidor.llamadas.length;
    fireEvent.change(campo, { target: { value: 'a' } });
    fireEvent.change(campo, { target: { value: '  armazón ' } });
    // No busca en cada tecla: ningún pedido mientras no pasa la pausa (FEAT-013) ni se aprieta Enter o el botón.
    expect(servidor.llamadas.length).toBe(pedidosAntes);

    // Enter en el campo envía el formulario de búsqueda enseguida, sin esperar la pausa.
    fireEvent.submit(campo.form!);
    expect(servidor.pedidosA('GET', '/api/articulos?texto=armaz%C3%B3n&pagina=1')).toHaveLength(1);

    await waitFor(() =>
      expect(filas()).toEqual([
        ['7', 'ABC-1', 'Armazón metálico', '1.815,00', 'Activo', 'Desactivar'],
        ['9', 'AC-9', 'armazon de acetato', '2.420,50', 'Activo', 'Desactivar'],
      ]),
    );
    expect(screen.queryByText('Lente orgánico')).toBeNull();
    // D-12: ni costo ni margen en el listado (la tabla; fuera de ella está el enlace a la planilla de FEAT-011).
    expect(within(screen.getByRole('table')).queryByText(/costo|margen/i)).toBeNull();
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
    fireEvent.change(combobox(), { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await screen.findByText('Página 1 de 3');
  });

  it('Articulos_BotonesSecundarios_DesactivarReactivarYPaginacion', async () => {
    // FEAT-012, D-14, AC-13: Desactivar, Reactivar, Anterior y Siguiente son secundarios; Buscar sigue principal.
    servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON, { ...LENTE, activo: false }], 1, 120)),
    });
    await abrir();
    await waitFor(() => expect(filas()).toHaveLength(2));

    const secundarios = [
      within(filaDe(7)).getByRole('button', { name: DESACTIVAR_7 }),
      within(filaDe(8)).getByRole('button', { name: 'Reactivar 8 Lente orgánico' }),
      screen.getByRole('button', { name: 'Anterior' }),
      screen.getByRole('button', { name: 'Siguiente' }),
    ];
    for (const boton of secundarios) {
      expect(boton.classList.contains('boton-secundario')).toBe(true);
    }
    expect(screen.getByRole('button', { name: 'Buscar' }).classList.contains('boton-secundario')).toBe(false);
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

  it('Articulos tiene el enlace a Actualizar costos', async () => {
    // FEAT-011: junto a "Nuevo artículo", y con el listado vacío también (la planilla no depende de lo buscado).
    servidorFalso({ ...conSesion, 'GET /api/articulos?pagina=1': json(200, pagina([])) });
    await abrir();
    await screen.findByText('No hay artículos que coincidan con la búsqueda.');

    const enlace = screen.getByRole('link', { name: 'Actualizar costos con planilla' });
    expect(enlace.getAttribute('href')).toBe('/articulos/actualizar-costos');
    // Junto a "Nuevo artículo": su párrafo es el hermano siguiente del párrafo del alta.
    const nuevo = screen.getByRole('link', { name: 'Nuevo artículo' });
    expect(nuevo.parentElement?.nextElementSibling).toBe(enlace.parentElement);

    fireEvent.click(enlace);

    expect(await screen.findByRole('heading', { name: 'Actualizar costos' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/articulos/actualizar-costos');
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

    const campo = combobox();
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

    fireEvent.change(combobox(), { target: { value: ` ${texto} ` } });
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
    const campo = combobox();

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
    const campo = combobox();

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

    fireEvent.change(combobox(), { target: { value: 'lente' } });
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

    fireEvent.change(combobox(), { target: { value: 'armazón' } });
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

/** Diez armazones: la tabla los muestra todos y las sugerencias, los primeros 8 (FR-01). */
const DIEZ_ARMAZONES = Array.from({ length: 10 }, (_, i) => ({
  codigo: 100 + i,
  codigoProveedor: `ARM-${i}`,
  descripcion: `Armazón ${i}`,
  precioVenta: 1000 + i,
  activo: true,
}));

/** Avanza el reloj falso (solo `setTimeout`) y deja que React procese lo que eso dispare. */
const avanzar = (ms: number) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });

/** El texto de cada sugerencia visible. */
const sugerencias = () => screen.queryAllByRole('option').map((opcion) => opcion.textContent);

/** Abre la pantalla con el reloj real, espera el listado inicial y recién entonces pasa al reloj falso. */
async function abrirEnVivo(filasIniciales: number) {
  await abrir();
  await waitFor(() => expect(filas()).toHaveLength(filasIniciales));
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  const campo = combobox();
  act(() => campo.focus());
  return campo;
}

describe('Articulos — búsqueda mientras se escribe (FEAT-013)', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('Articulos_EscribirArm_SugerenciasYTablaFiltrada', async () => {
    // AC-01: tras la pausa, una sola búsqueda llena la tabla y las primeras 8 filas son las sugerencias.
    const servidor = servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON, LENTE, ARMAZON_ACETATO], 1, 120)),
      'GET /api/articulos?pagina=2': json(200, pagina([LENTE], 2, 120)),
      'GET /api/articulos?texto=arm&pagina=1': json(200, pagina(DIEZ_ARMAZONES)),
    });
    await abrir();
    // Un texto nuevo vuelve a la página 1 aunque se esté en otra.
    fireEvent.click(await screen.findByRole('button', { name: 'Siguiente' }));
    await screen.findByText('Página 2 de 3');
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const campo = combobox();
    act(() => campo.focus());
    expect(campo.type).toBe('text');
    expect(campo.getAttribute('autocomplete')).toBe('off');

    fireEvent.change(campo, { target: { value: 'a' } });
    fireEvent.change(campo, { target: { value: 'ar' } });
    fireEvent.change(campo, { target: { value: 'arm' } });
    await avanzar(299);
    // No busca en cada tecla: espera la pausa.
    expect(servidor.pedidosA('GET', '/api/articulos?texto=arm&pagina=1')).toHaveLength(0);
    await avanzar(1);

    expect(servidor.pedidosA('GET', '/api/articulos?texto=arm&pagina=1')).toHaveLength(1);
    expect(servidor.pedidosA('GET', '/api/articulos?texto=ar&pagina=1')).toHaveLength(0);
    expect(campo.getAttribute('aria-expanded')).toBe('true');
    expect(sugerencias()).toEqual(DIEZ_ARMAZONES.slice(0, 8).map((a) => `${a.codigo} · ${a.codigoProveedor} · ${a.descripcion}`));
    expect(filas()).toHaveLength(10);
    expect(filas()[9]?.[2]).toBe('Armazón 9');
    expect(screen.getByText('Página 1 de 1')).toBeTruthy();
    expect(within(campo.closest('.campo-busqueda')!).getByRole('status').textContent).toBe('8 sugerencias');
  });

  it('Articulos_UnCaracter_SinSugerencias', async () => {
    // AC-02 y D-07: con 1 carácter no pide y la tabla queda como estaba; vacío vuelve a la lista inicial.
    const servidor = servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON, LENTE, ARMAZON_ACETATO])),
      'GET /api/articulos?texto=arm&pagina=1': json(200, pagina([ARMAZON, ARMAZON_ACETATO])),
    });
    const campo = await abrirEnVivo(3);

    fireEvent.change(campo, { target: { value: 'arm' } });
    await avanzar(300);
    expect(sugerencias()).toHaveLength(2);

    fireEvent.change(campo, { target: { value: ' a ' } });
    // Las sugerencias de "arm" no quedan para elegir.
    expect(sugerencias()).toEqual([]);
    expect(campo.getAttribute('aria-expanded')).toBe('false');
    await avanzar(300);
    expect(servidor.llamadas.filter((l) => l.ruta.includes('texto=a&'))).toEqual([]);
    expect(filas().map((f) => f[0])).toEqual(['7', '9']);
    expect(sugerencias()).toEqual([]);

    const pedidosIniciales = servidor.pedidosA('GET', '/api/articulos?pagina=1').length;
    fireEvent.change(campo, { target: { value: '' } });
    await avanzar(300);
    // La lista inicial, la misma de abrir la pantalla, sin sugerencias abiertas.
    expect(servidor.pedidosA('GET', '/api/articulos?pagina=1')).toHaveLength(pedidosIniciales + 1);
    expect(filas().map((f) => f[0])).toEqual(['7', '8', '9']);
    expect(sugerencias()).toEqual([]);
    expect(campo.getAttribute('aria-expanded')).toBe('false');
  });

  it('Articulos_ElegirSugerencia_AbreLaEdicion', async () => {
    // AC-03: elegir la sugerencia del artículo 7 abre su edición.
    servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([LENTE])),
      'GET /api/articulos?texto=arm&pagina=1': json(200, pagina([ARMAZON_ACETATO, ARMAZON])),
    });
    const campo = await abrirEnVivo(1);

    fireEvent.change(campo, { target: { value: 'arm' } });
    await avanzar(300);
    fireEvent.keyDown(campo, { key: 'ArrowDown' });
    fireEvent.keyDown(campo, { key: 'ArrowDown' });
    fireEvent.keyDown(campo, { key: 'Enter' });

    expect(screen.getByTestId('ubicacion').textContent).toBe('/articulos/7');
  });

  it('Articulos_Buscar_ComoAntes', async () => {
    // AC-08 y FR-10: "Buscar" y Enter sin una opción marcada buscan enseguida, cancelan la pausa y cierran la lista.
    const servidor = servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON, LENTE, ARMAZON_ACETATO])),
      'GET /api/articulos?texto=armaz%C3%B3n&pagina=1': json(200, pagina([ARMAZON, ARMAZON_ACETATO])),
      'GET /api/articulos?texto=lente&pagina=1': json(200, pagina([LENTE])),
    });
    const campo = await abrirEnVivo(3);

    fireEvent.change(campo, { target: { value: '  armazón ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    expect(servidor.pedidosA('GET', '/api/articulos?texto=armaz%C3%B3n&pagina=1')).toHaveLength(1);
    await avanzar(300);
    // La pausa quedó cancelada: un solo pedido.
    expect(servidor.pedidosA('GET', '/api/articulos?texto=armaz%C3%B3n&pagina=1')).toHaveLength(1);
    expect(filas().map((f) => f[0])).toEqual(['7', '9']);
    expect(campo.getAttribute('aria-expanded')).toBe('false');

    // Enter con la lista abierta y sin opción marcada: cierra la lista y busca como el botón.
    fireEvent.change(campo, { target: { value: 'lente' } });
    await avanzar(300);
    expect(campo.getAttribute('aria-expanded')).toBe('true');
    fireEvent.keyDown(campo, { key: 'Enter' });
    fireEvent.submit(campo.form!);
    await avanzar(300);
    expect(campo.getAttribute('aria-expanded')).toBe('false');
    expect(servidor.pedidosA('GET', '/api/articulos?texto=lente&pagina=1')).toHaveLength(2);
    expect(filas().map((f) => f[0])).toEqual(['8']);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
  });

  it('Articulos_BuscarConLaListaAbierta_LaCierraYLaRespuestaNoLaReabre', async () => {
    // FR-10: con sugerencias a la vista, "Buscar" cierra la lista (aunque el clic no saque el foco del campo) y su
    // respuesta no la vuelve a abrir.
    const servidor = servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON, LENTE, ARMAZON_ACETATO])),
      'GET /api/articulos?texto=arm&pagina=1': json(200, pagina([ARMAZON, ARMAZON_ACETATO])),
    });
    const campo = await abrirEnVivo(3);

    fireEvent.change(campo, { target: { value: 'arm' } });
    await avanzar(300);
    expect(campo.getAttribute('aria-expanded')).toBe('true');
    expect(sugerencias()).toHaveLength(2);

    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    expect(campo.getAttribute('aria-expanded')).toBe('false');
    await avanzar(300);

    expect(servidor.pedidosA('GET', '/api/articulos?texto=arm&pagina=1')).toHaveLength(2);
    expect(document.activeElement).toBe(campo);
    expect(campo.getAttribute('aria-expanded')).toBe('false');
    expect(sugerencias()).toEqual([]);
  });

  it('Articulos_UnCaracterMientrasCargaLaListaInicial_LaListaLlegaIgual', async () => {
    // D-07: sin una tabla mostrada todavía, descartar no cancela la lista inicial (es la que se vuelve a ver).
    const inicial = diferida(pagina([ARMAZON, LENTE]));
    servidorFalso({ ...conSesion, 'GET /api/articulos?pagina=1': inicial.respuesta });
    await abrir();
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });

    fireEvent.change(combobox(), { target: { value: 'a' } });
    await avanzar(300);
    await inicial.liberar();
    await avanzar(0);

    expect(filas().map((f) => f[0])).toEqual(['7', '8']);
  });

  it.each([
    ['1 carácter', 'a'],
    ['un texto inválido de 2 o más caracteres', `ab${String.fromCharCode(0xd83d)}`],
  ])('Articulos_RespuestaTardiaTrasDescartar_%s_TablaComoEstaba', async (_caso, descartado) => {
    // D-07: lo que no se busca en vivo cancela también el pedido en curso; su respuesta no cambia la tabla ni deja
    // sugerencias, y la paginación sigue con lo que muestra la tabla.
    const tardia = diferida(pagina([LENTE]));
    const servidor = servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([ARMAZON, LENTE, ARMAZON_ACETATO], 1, 120)),
      'GET /api/articulos?pagina=2': json(200, pagina([LENTE], 2, 120)),
      'GET /api/articulos?texto=ab&pagina=1': tardia.respuesta,
    });
    const campo = await abrirEnVivo(3);

    fireEvent.change(campo, { target: { value: 'ab' } });
    await avanzar(300);
    expect(servidor.pedidosA('GET', '/api/articulos?texto=ab&pagina=1')).toHaveLength(1);
    fireEvent.change(campo, { target: { value: descartado } });
    await avanzar(300);
    await tardia.liberar();
    await avanzar(0);

    expect(filas().map((f) => f[0])).toEqual(['7', '8', '9']);
    expect(sugerencias()).toEqual([]);
    expect(campo.getAttribute('aria-expanded')).toBe('false');
    // Sin un pedido pendiente: los botones de fila y la paginación siguen habilitados.
    expect((within(filaDe(7)).getByRole('button', { name: DESACTIVAR_7 }) as HTMLButtonElement).disabled).toBe(false);
    fireEvent.click(screen.getByRole('button', { name: 'Siguiente' }));
    await avanzar(0);
    expect(servidor.pedidosA('GET', '/api/articulos?pagina=2')).toHaveLength(1);
    expect(servidor.llamadas.filter((l) => l.ruta.startsWith('/api/articulos?texto=ab&pagina=2'))).toEqual([]);
  });

  it('Articulos_ErrorDeBusqueda_SinSugerencias', async () => {
    // AC-10: la búsqueda falla; se ocultan las sugerencias y se ve el mensaje de hoy.
    servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, pagina([LENTE])),
      'GET /api/articulos?texto=arm&pagina=1': json(200, pagina([ARMAZON, ARMAZON_ACETATO])),
      'GET /api/articulos?texto=armx&pagina=1': sinRed,
    });
    const campo = await abrirEnVivo(1);

    fireEvent.change(campo, { target: { value: 'arm' } });
    await avanzar(300);
    expect(sugerencias()).toHaveLength(2);

    fireEvent.change(campo, { target: { value: 'armx' } });
    await avanzar(300);

    expect(sugerencias()).toEqual([]);
    expect(campo.getAttribute('aria-expanded')).toBe('false');
    expect(mensajes()).toEqual([MENSAJE_SIN_CONEXION]);
    expect(screen.queryByRole('table')).toBeNull();
  });
});
