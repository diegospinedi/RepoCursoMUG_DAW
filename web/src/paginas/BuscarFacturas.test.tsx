import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { mensajeJuntoAlCampo } from '../pruebas/formulario';
import { json, renderizarApp, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';

/** Datos ficticios (Ley 25.326): nombres genéricos y un DNI con 00, ningún dato real de un cliente. */
const APELLIDO_ESCRITO = 'Pérez';
const DNI_ESCRITO = '00123456';

afterEach(() => {
  try {
    // Datos personales: los filtros viven solo en el estado de la pantalla, ni en el almacenamiento ni en la URL.
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(decodeURIComponent(window.location.href)).not.toContain(APELLIDO_ESCRITO);
    expect(decodeURIComponent(window.location.href)).not.toContain(DNI_ESCRITO);
  } finally {
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
});

const RUTA = '/facturas';
const TITULO = 'Buscar facturas';
const RUTA_BUSQUEDA = '/api/facturacion/busqueda';
const CLAVE = `POST ${RUTA_BUSQUEDA}`;
const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';
const MENSAJE_PEDIDO_RECHAZADO = 'No se pudo completar la operación. Recargá la página y reintentá.';
const MENSAJE_DESDE_POSTERIOR = 'La fecha Desde no puede ser posterior a Hasta.';
const ENCABEZADOS = ['Comprobante', 'Fecha', 'Apellido', 'Nombre', 'DNI', 'Total ($)', 'Presupuesto'];
const COMPROBANTE = 'Factura B 0003-00000125';

/** La Factura B 0003-00000125 emitida desde el presupuesto 155 (AC-23), como la devuelve la Api. */
const FILA_155 = {
  presupuesto: 155,
  comprobante: COMPROBANTE,
  fecha: '2026-10-30',
  apellido: 'Pérez',
  nombre: 'Ana',
  dni: '00123456',
  total: 1815,
};

/** Las facturas de los presupuestos `desde` a `hasta`, de mayor a menor, con comprobantes distintos. */
const filasDel = (desde: number, hasta: number) =>
  Array.from({ length: desde - hasta + 1 }, (_, i) => ({
    ...FILA_155,
    presupuesto: desde - i,
    comprobante: `Factura B 0003-${String(desde - i).padStart(8, '0')}`,
  }));

const pagina = (facturas: unknown[], numero = 1, total = facturas.length) => ({
  facturas,
  pagina: numero,
  tamanoPagina: 50,
  total,
});

const cuerpoDel = (pedido: { init: RequestInit | undefined } | undefined) =>
  JSON.parse(String(pedido?.init?.body)) as Record<string, unknown>;

async function abrir(rutas: Parameters<typeof servidorFalso>[0]) {
  const servidor = servidorFalso({ 'GET /api/sesion': vacia(204), ...rutas });
  renderizarApp(RUTA);
  await screen.findByRole('heading', { name: TITULO });
  return servidor;
}

const escribir = (etiqueta: string, valor: string) =>
  fireEvent.change(screen.getByLabelText(etiqueta), { target: { value: valor } });

const buscar = () => fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));

const tabla = () => screen.getByRole('table', { name: 'Facturas encontradas' });

describe('BuscarFacturas', () => {
  it('Buscar_Resultado_ColumnasDeAC23', async () => {
    const servidor = await abrir({ [CLAVE]: json(200, pagina([FILA_155])) });
    // No busca al abrir (FR-16 por el botón, como presupuestos).
    expect(servidor.pedidosA('POST', RUTA_BUSQUEDA)).toHaveLength(0);

    buscar();

    await screen.findByRole('table', { name: 'Facturas encontradas' });
    const encabezados = within(tabla())
      .getAllByRole('columnheader')
      .map((celda) => celda.textContent);
    expect(encabezados).toEqual(ENCABEZADOS);
    const [, fila] = within(tabla()).getAllByRole('row');
    const celdas = within(fila!)
      .getAllByRole('cell')
      .map((celda) => celda.textContent);
    // AC-23: el tipo con el punto de venta y número como texto (D-15), la fecha, el cliente, el total y el presupuesto.
    expect(celdas).toEqual([COMPROBANTE, '30/10/2026', 'Pérez', 'Ana', '00123456', '1.815,00', '155']);
    // FR-24: la factura se abre desde la lista, por el número del presupuesto (D-14, D-28).
    const enlace = within(fila!).getByRole('link', { name: `Abrir la ${COMPROBANTE}` });
    expect(enlace.getAttribute('href')).toBe('/facturas/155');
    // Sin filtros: solo la página 1.
    expect(cuerpoDel(servidor.pedidosA('POST', RUTA_BUSQUEDA)[0])).toEqual({ pagina: 1 });
  });

  it('Buscar_DesdePosterior_ErrorSinPedido', async () => {
    const servidor = await abrir({ [CLAVE]: json(200, pagina([FILA_155])) });

    escribir('Desde', '2026-03-20');
    escribir('Hasta', '2026-03-10');
    buscar();

    // AC-22: no busca e indica junto a Desde que no puede ser posterior a Hasta (D-42: la web valida antes de pedir).
    const desde = screen.getByLabelText('Desde');
    await waitFor(() => expect(mensajeJuntoAlCampo(desde)).toBe(MENSAJE_DESDE_POSTERIOR));
    expect(desde.getAttribute('aria-invalid')).toBe('true');
    expect(servidor.pedidosA('POST', RUTA_BUSQUEDA)).toHaveLength(0);
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('Buscar_FiltrosFueraDeLaUrl', async () => {
    const consola = (['log', 'info', 'warn', 'error', 'debug', 'trace'] as const).map((metodo) =>
      vi.spyOn(console, metodo).mockImplementation(() => {}),
    );
    const servidor = await abrir({ [CLAVE]: json(200, pagina([FILA_155])) });

    escribir('Apellido', APELLIDO_ESCRITO);
    escribir('Nombre', 'Ana');
    escribir('DNI', DNI_ESCRITO);
    escribir('Número de comprobante', '125');
    escribir('Desde', '2026-10-01');
    escribir('Hasta', '2026-10-31');
    buscar();

    await screen.findByRole('table', { name: 'Facturas encontradas' });
    // Los seis filtros viajan solo en el cuerpo JSON (ADR-019): la ruta es fija y la URL de la pantalla no cambia.
    const [pedido] = servidor.pedidosA('POST', RUTA_BUSQUEDA);
    expect(pedido?.ruta).toBe(RUTA_BUSQUEDA);
    expect(cuerpoDel(pedido)).toEqual({
      apellido: APELLIDO_ESCRITO,
      nombre: 'Ana',
      dni: DNI_ESCRITO,
      numero: '125',
      desde: '2026-10-01',
      hasta: '2026-10-31',
      pagina: 1,
    });
    expect(servidor.llamadas.every((llamada) => !llamada.ruta.includes('?'))).toBe(true);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
    // Sin autocompletado: el navegador no guarda los datos personales buscados.
    for (const etiqueta of ['Apellido', 'Nombre', 'DNI', 'Número de comprobante']) {
      expect(screen.getByLabelText(etiqueta).getAttribute('autocomplete')).toBe('off');
    }
    // Nada en la consola.
    for (const espia of consola) {
      expect(espia).not.toHaveBeenCalled();
    }
  });

  it('BuscarFacturas_Paginacion_BotonesSecundarios', async () => {
    // FEAT-012, D-14, AC-13: Anterior y Siguiente son secundarios; Buscar sigue principal.
    await abrir({ [CLAVE]: json(200, pagina(filasDel(200, 151), 1, 120)) });
    buscar();
    await screen.findByText('Página 1 de 3');

    expect(screen.getByRole('button', { name: 'Anterior' }).classList.contains('boton-secundario')).toBe(true);
    expect(screen.getByRole('button', { name: 'Siguiente' }).classList.contains('boton-secundario')).toBe(true);
    expect(screen.getByRole('button', { name: 'Buscar' }).classList.contains('boton-secundario')).toBe(false);
  });

  it('Buscar: paginado de 50 que reutiliza el último filtro aplicado', async () => {
    const servidor = await abrir({
      [CLAVE]: [
        json(200, pagina(filasDel(200, 151), 1, 120)),
        json(200, pagina(filasDel(150, 101), 2, 120)),
        json(200, pagina(filasDel(100, 81), 3, 120)),
        json(200, pagina(filasDel(150, 101), 2, 120)),
      ],
    });

    escribir('Apellido', APELLIDO_ESCRITO);
    buscar();
    expect(await screen.findByText('Página 1 de 3')).toBeTruthy();
    expect(within(tabla()).getAllByRole('row')).toHaveLength(51);
    expect(screen.getByRole('button', { name: 'Anterior' }).hasAttribute('disabled')).toBe(true);

    // Lo escrito después no se usa al paginar: la página siguiente es del último filtro aplicado.
    escribir('Apellido', 'Gómez');
    fireEvent.click(screen.getByRole('button', { name: 'Siguiente' }));
    expect(await screen.findByText('Página 2 de 3')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Siguiente' }));
    expect(await screen.findByText('Página 3 de 3')).toBeTruthy();
    expect(within(tabla()).getAllByRole('row')).toHaveLength(21);
    expect(screen.getByRole('button', { name: 'Siguiente' }).hasAttribute('disabled')).toBe(true);
    fireEvent.click(screen.getByRole('button', { name: 'Anterior' }));
    expect(await screen.findByText('Página 2 de 3')).toBeTruthy();

    const cuerpos = servidor.pedidosA('POST', RUTA_BUSQUEDA).map(cuerpoDel);
    expect(cuerpos).toEqual([
      { apellido: APELLIDO_ESCRITO, pagina: 1 },
      { apellido: APELLIDO_ESCRITO, pagina: 2 },
      { apellido: APELLIDO_ESCRITO, pagina: 3 },
      { apellido: APELLIDO_ESCRITO, pagina: 2 },
    ]);
  });

  it('Buscar: sin coincidencias lo dice', async () => {
    await abrir({ [CLAVE]: json(200, pagina([], 1, 0)) });

    buscar();

    expect(await screen.findByText('No hay facturas que coincidan con la búsqueda.')).toBeTruthy();
    expect(screen.queryByRole('table')).toBeNull();
    expect(screen.queryByRole('navigation', { name: 'Páginas de los resultados' })).toBeNull();
  });

  it('Buscar: un 400 muestra cada error junto a su campo y el que no tiene campo como mensaje general', async () => {
    await abrir({
      [CLAVE]: json(400, {
        status: 400,
        errors: {
          numero: ['Usá hasta 100 caracteres.'],
          desde: ['La fecha Desde no puede ser posterior a Hasta.'],
          pagina: ['Página inválida.'],
        },
      }),
    });

    buscar();

    const numero = screen.getByLabelText('Número de comprobante');
    await waitFor(() => expect(mensajeJuntoAlCampo(numero)).toBe('Usá hasta 100 caracteres.'));
    expect(mensajeJuntoAlCampo(screen.getByLabelText('Desde'))).toBe(MENSAJE_DESDE_POSTERIOR);
    expect(screen.getByText(MENSAJE_PEDIDO_RECHAZADO)).toBeTruthy();
    expect(screen.queryByText('Página inválida.')).toBeNull();
  });

  it('Buscar: un 400 con un solo error en un campo lo muestra junto al campo', async () => {
    await abrir({ [CLAVE]: json(400, { status: 400, errors: { apellido: ['Usá hasta 100 caracteres.'] } }) });

    buscar();

    const apellido = screen.getByLabelText('Apellido');
    await waitFor(() => expect(mensajeJuntoAlCampo(apellido)).toBe('Usá hasta 100 caracteres.'));
    expect(screen.queryByText(MENSAJE_PEDIDO_RECHAZADO)).toBeNull();
  });

  it.each([
    ['500', vacia(500)],
    ['falla de red', sinRed],
    ['respuesta ilegible', json(200, { facturas: [{ ...FILA_155, comprobante: 'Factura A 0003-00000125' }] })],
  ])('Buscar: %s muestra el mensaje general y no deja la grilla anterior', async (_caso, respuesta) => {
    await abrir({ [CLAVE]: [json(200, pagina([FILA_155])), respuesta] });
    buscar();
    await screen.findByRole('table', { name: 'Facturas encontradas' });

    buscar();

    expect(await screen.findByText(MENSAJE_SIN_CONEXION)).toBeTruthy();
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('Buscar: sin sesión va al ingreso', async () => {
    await abrir({ [CLAVE]: vacia(401) });

    buscar();

    await waitFor(() => expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso'));
  });

  it('Buscar: mientras busca no envía otro pedido', async () => {
    let responder: (respuesta: Response) => void = () => {};
    const servidor = await abrir({
      [CLAVE]: () =>
        new Promise<Response>((resolver) => {
          responder = resolver;
        }),
    });

    buscar();
    expect(await screen.findByText('Cargando…')).toBeTruthy();
    fireEvent.submit(screen.getByRole('search'));
    expect(screen.getByRole('button', { name: 'Buscar' }).hasAttribute('disabled')).toBe(true);
    responder(json(200, pagina([FILA_155]))() as Response);

    await screen.findByRole('table', { name: 'Facturas encontradas' });
    expect(servidor.pedidosA('POST', RUTA_BUSQUEDA)).toHaveLength(1);
  });
});
