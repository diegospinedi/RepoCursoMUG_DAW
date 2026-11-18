import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { diferida } from '../pruebas/diferida';
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

/**
 * El campo de la etiqueta. Desde FEAT-013 los textos son combobox con sugerencias, y la lista también se nombra con la
 * etiqueta (`aria-labelledby`): por eso se buscan por su rol; las fechas, por la etiqueta.
 */
const campo = (etiqueta: string) =>
  (screen.queryByRole('combobox', { name: etiqueta }) ?? screen.getByLabelText(etiqueta)) as HTMLInputElement;

const escribir = (etiqueta: string, valor: string) => fireEvent.change(campo(etiqueta), { target: { value: valor } });

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
      expect(campo(etiqueta).getAttribute('autocomplete')).toBe('off');
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

    const numero = campo('Número de comprobante');
    await waitFor(() => expect(mensajeJuntoAlCampo(numero)).toBe('Usá hasta 100 caracteres.'));
    expect(mensajeJuntoAlCampo(screen.getByLabelText('Desde'))).toBe(MENSAJE_DESDE_POSTERIOR);
    expect(screen.getByText(MENSAJE_PEDIDO_RECHAZADO)).toBeTruthy();
    expect(screen.queryByText('Página inválida.')).toBeNull();
  });

  it('Buscar: un 400 con un solo error en un campo lo muestra junto al campo', async () => {
    await abrir({ [CLAVE]: json(400, { status: 400, errors: { apellido: ['Usá hasta 100 caracteres.'] } }) });

    buscar();

    const apellido = campo('Apellido');
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

  it('Buscar: mientras busca, el doble clic no envía otro pedido; Enter lo reemplaza y la vieja no pisa la nueva', async () => {
    // D-08 de FEAT-013: la guarda `enCurso` se reemplaza por el número de pedido. El botón deshabilitado evita el doble
    // clic; un Enter mientras tanto envía otra búsqueda, y solo la respuesta de la última se muestra.
    const primera = diferida(pagina(filasDel(200, 151), 1, 120));
    const reemplazo = diferida(pagina([FILA_155]));
    const servidor = await abrir({ [CLAVE]: [primera.respuesta, reemplazo.respuesta] });

    buscar();
    buscar();
    expect(await screen.findByText('Cargando…')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Buscar' }).hasAttribute('disabled')).toBe(true);
    expect(servidor.pedidosA('POST', RUTA_BUSQUEDA)).toHaveLength(1);

    fireEvent.submit(screen.getByRole('search'));
    expect(servidor.pedidosA('POST', RUTA_BUSQUEDA)).toHaveLength(2);
    expect(screen.getByRole('button', { name: 'Buscar' }).hasAttribute('disabled')).toBe(true);

    await reemplazo.liberar();
    await screen.findByRole('table', { name: 'Facturas encontradas' });
    expect(screen.getByRole('button', { name: 'Buscar' }).hasAttribute('disabled')).toBe(false);
    await primera.liberar();
    expect(within(tabla()).getAllByRole('row')).toHaveLength(2);
    expect(screen.queryByText('Página 1 de 3')).toBeNull();
  });
});

/** Avanza el reloj falso (solo `setTimeout`) y deja que React procese lo que eso dispare. */
const avanzar = (ms: number) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });

/** El texto de cada sugerencia visible. */
const sugerencias = () => screen.queryAllByRole('option').map((opcion) => opcion.textContent);

/** Abre la pantalla con el reloj real, pasa al reloj falso y pone el foco en el campo indicado. */
async function abrirEnVivo(rutas: Parameters<typeof servidorFalso>[0], etiqueta = 'Apellido') {
  const servidor = await abrir(rutas);
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  const enfocado = campo(etiqueta);
  act(() => enfocado.focus());
  return { servidor, enfocado };
}

/** Otra factura ficticia del mismo cliente, emitida desde el presupuesto 154. */
const FILA_154 = { ...FILA_155, presupuesto: 154, comprobante: 'Factura B 0003-00000124', fecha: '2026-10-29' };

const SUGERENCIA_155 = `${COMPROBANTE} · Pérez, Ana · 30/10/2026`;
const SUGERENCIA_154 = 'Factura B 0003-00000124 · Pérez, Ana · 29/10/2026';

describe('BuscarFacturas — búsqueda mientras se escribe (FEAT-013)', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('BuscarFacturas_EscribirApellido_SugerenciasYAbrirLaFactura', async () => {
    // AC-05: tras la pausa, una sola búsqueda llena la tabla y las sugerencias (comprobante, cliente y fecha, sin el
    // DNI); elegir una abre la factura por el número del presupuesto.
    const { servidor, enfocado: apellido } = await abrirEnVivo({ [CLAVE]: json(200, pagina([FILA_155, FILA_154])) });

    fireEvent.change(apellido, { target: { value: 'p' } });
    fireEvent.change(apellido, { target: { value: ' pé ' } });
    await avanzar(299);
    expect(servidor.pedidosA('POST', RUTA_BUSQUEDA)).toHaveLength(0);
    await avanzar(1);

    expect(servidor.pedidosA('POST', RUTA_BUSQUEDA).map(cuerpoDel)).toEqual([{ apellido: 'pé', pagina: 1 }]);
    expect(sugerencias()).toEqual([SUGERENCIA_155, SUGERENCIA_154]);
    expect(within(tabla()).getAllByRole('row')).toHaveLength(3);
    expect(apellido.getAttribute('aria-expanded')).toBe('true');
    expect(campo('Nombre').getAttribute('aria-expanded')).toBe('false');
    expect(within(apellido.closest('.campo-busqueda')!).getByRole('status').textContent).toBe('2 sugerencias');
    // R-02 y D-15: ni el texto ni el id de la sugerencia llevan el DNI.
    for (const opcion of screen.getAllByRole('option')) {
      expect(opcion.textContent).not.toContain(DNI_ESCRITO);
      expect(opcion.id).not.toContain(DNI_ESCRITO);
    }
    // "Buscar" no queda deshabilitado por la búsqueda en vivo (D-08).
    expect(screen.getByRole('button', { name: 'Buscar' }).hasAttribute('disabled')).toBe(false);

    fireEvent.click(screen.getByRole('option', { name: SUGERENCIA_155 }));

    expect(screen.getByTestId('ubicacion').textContent).toBe('/facturas/155');
  });

  it('BuscarFacturas_ErrorDeBusqueda_SinSugerencias', async () => {
    // AC-10: la búsqueda en vivo falla; se ocultan las sugerencias y la tabla, y se ve el mensaje de hoy.
    const { enfocado: apellido } = await abrirEnVivo({ [CLAVE]: [json(200, pagina([FILA_155])), vacia(500)] });

    fireEvent.change(apellido, { target: { value: 'pé' } });
    await avanzar(300);
    expect(sugerencias()).toEqual([SUGERENCIA_155]);

    fireEvent.change(apellido, { target: { value: 'pér' } });
    await avanzar(300);

    expect(sugerencias()).toEqual([]);
    expect(apellido.getAttribute('aria-expanded')).toBe('false');
    expect(screen.getByText(MENSAJE_SIN_CONEXION)).toBeTruthy();
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('BuscarFacturas_NumeroEnVivo_FechasYRangoInvalidoNoPiden', async () => {
    // D-07: el número de comprobante también busca en vivo, combinado con las fechas; cambiar las fechas no dispara y
    // con el rango invertido no se pide nada en vivo: el error aparece con "Buscar".
    const { servidor, enfocado: numero } = await abrirEnVivo(
      { [CLAVE]: json(200, pagina([FILA_155])) },
      'Número de comprobante',
    );

    escribir('Desde', '2026-10-01');
    escribir('Hasta', '2026-10-31');
    await avanzar(300);
    expect(servidor.pedidosA('POST', RUTA_BUSQUEDA)).toHaveLength(0);

    fireEvent.change(numero, { target: { value: '125' } });
    await avanzar(300);
    expect(servidor.pedidosA('POST', RUTA_BUSQUEDA).map(cuerpoDel)).toEqual([
      { numero: '125', desde: '2026-10-01', hasta: '2026-10-31', pagina: 1 },
    ]);
    expect(sugerencias()).toEqual([SUGERENCIA_155]);

    escribir('Desde', '2026-11-20');
    fireEvent.change(numero, { target: { value: '1250' } });
    await avanzar(300);
    expect(servidor.pedidosA('POST', RUTA_BUSQUEDA)).toHaveLength(1);
    expect(sugerencias()).toEqual([]);
    expect(mensajeJuntoAlCampo(campo('Desde'))).toBeNull();

    buscar();
    expect(mensajeJuntoAlCampo(campo('Desde'))).toBe(MENSAJE_DESDE_POSTERIOR);
    expect(servidor.pedidosA('POST', RUTA_BUSQUEDA)).toHaveLength(1);
  });

  it('BuscarFacturas_400EnVivo_OcultaSugerenciasSinErrorDelCampo', async () => {
    // D-07: un 400 en vivo oculta las sugerencias sin mostrar el error del campo.
    const { enfocado: apellido } = await abrirEnVivo({
      [CLAVE]: [json(200, pagina([FILA_155])), json(400, { status: 400, errors: { apellido: ['Usá hasta 100 caracteres.'] } })],
    });

    fireEvent.change(apellido, { target: { value: 'pé' } });
    await avanzar(300);
    expect(sugerencias()).toEqual([SUGERENCIA_155]);
    fireEvent.change(apellido, { target: { value: 'pér' } });
    await avanzar(300);

    expect(sugerencias()).toEqual([]);
    expect(mensajeJuntoAlCampo(apellido)).toBeNull();
    expect(screen.queryByRole('alert')).toBeNull();
  });
});

describe('BuscarFacturas — casos en vivo de la ronda 2 (FEAT-013)', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  const pedidos = (servidor: ReturnType<typeof servidorFalso>) => servidor.pedidosA('POST', RUTA_BUSQUEDA);
  const cuerpos = (servidor: ReturnType<typeof servidorFalso>) => pedidos(servidor).map(cuerpoDel);
  const presupuestosDeLaTabla = () =>
    within(tabla())
      .getAllByRole('row')
      .slice(1)
      .map((fila) => within(fila).getAllByRole('cell').at(-1)?.textContent);
  const botonBuscar = () => screen.getByRole('button', { name: 'Buscar' });

  /** Intenta elegir la primera sugerencia con el teclado. */
  const elegirConEnter = (campoEnfocado: HTMLInputElement) => {
    fireEvent.keyDown(campoEnfocado, { key: 'ArrowDown' });
    fireEvent.keyDown(campoEnfocado, { key: 'Enter' });
  };

  it('BuscarFacturas_UnCaracter_SinPedidoNiSugerencias', async () => {
    // D-07 y H1: con 1 carácter no pide, las sugerencias anteriores no se ven ni se eligen, y la tabla queda.
    const { servidor, enfocado: apellido } = await abrirEnVivo({ [CLAVE]: json(200, pagina([FILA_155, FILA_154])) });
    fireEvent.change(apellido, { target: { value: 'pé' } });
    await avanzar(300);
    expect(sugerencias()).toHaveLength(2);

    fireEvent.change(apellido, { target: { value: ' p ' } });

    expect(sugerencias()).toEqual([]);
    expect(apellido.getAttribute('aria-expanded')).toBe('false');
    elegirConEnter(apellido);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
    await avanzar(300);
    expect(pedidos(servidor)).toHaveLength(1);
    expect(presupuestosDeLaTabla()).toEqual(['155', '154']);
  });

  it('BuscarFacturas_CampoVaciado_SinSugerenciasEnElMismoEvento', async () => {
    // H1: el campo vaciado busca de nuevo, pero durante la pausa y el pedido no deja elegir las sugerencias viejas.
    const nuevo = diferida(pagina([FILA_154]));
    const { enfocado: apellido } = await abrirEnVivo({ [CLAVE]: [json(200, pagina([FILA_155])), nuevo.respuesta] });
    fireEvent.change(apellido, { target: { value: 'pé' } });
    await avanzar(300);
    expect(sugerencias()).toEqual([SUGERENCIA_155]);

    fireEvent.change(apellido, { target: { value: '' } });

    expect(sugerencias()).toEqual([]);
    expect(apellido.getAttribute('aria-expanded')).toBe('false');
    elegirConEnter(apellido);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);

    await avanzar(300);
    expect(sugerencias()).toEqual([]);
    elegirConEnter(apellido);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
    await nuevo.liberar();
    expect(presupuestosDeLaTabla()).toEqual(['154']);
    expect(sugerencias()).toEqual([]);
  });

  it('BuscarFacturas_CampoVaciado_VuelveABuscarConLosOtrosFiltros', async () => {
    // D-07: el campo que queda vacío vuelve a buscar con el resto de los filtros; sin ningún texto, sin sugerencias.
    const { servidor, enfocado: nombre } = await abrirEnVivo(
      {
        [CLAVE]: [
          json(200, pagina([FILA_154])),
          json(200, pagina([FILA_155])),
          json(200, pagina([FILA_154, FILA_155])),
          json(200, pagina([FILA_155, FILA_154])),
        ],
      },
      'Nombre',
    );
    fireEvent.change(nombre, { target: { value: 'an' } });
    await avanzar(300);
    const apellido = campo('Apellido');
    act(() => apellido.focus());
    fireEvent.change(apellido, { target: { value: 'pé' } });
    await avanzar(300);
    fireEvent.change(apellido, { target: { value: '' } });
    await avanzar(300);

    expect(cuerpos(servidor)).toEqual([
      { nombre: 'an', pagina: 1 },
      { apellido: 'pé', nombre: 'an', pagina: 1 },
      { nombre: 'an', pagina: 1 },
    ]);
    expect(presupuestosDeLaTabla()).toEqual(['154', '155']);

    act(() => nombre.focus());
    fireEvent.change(nombre, { target: { value: '' } });
    await avanzar(300);

    expect(cuerpos(servidor)[3]).toEqual({ pagina: 1 });
    expect(presupuestosDeLaTabla()).toEqual(['155', '154']);
    expect(sugerencias()).toEqual([]);
  });

  it('BuscarFacturas_RespuestaViejaNoPisaLaNueva', async () => {
    // AC-09: la respuesta de "pé" llega después que la de "pér": siguen las sugerencias y la tabla de "pér".
    const vieja = diferida(pagina([FILA_154]));
    const { servidor, enfocado: apellido } = await abrirEnVivo({
      [CLAVE]: [vieja.respuesta, json(200, pagina([FILA_155]))],
    });
    fireEvent.change(apellido, { target: { value: 'pé' } });
    await avanzar(300);
    fireEvent.change(apellido, { target: { value: 'pér' } });
    await avanzar(300);
    expect(cuerpos(servidor)).toEqual([
      { apellido: 'pé', pagina: 1 },
      { apellido: 'pér', pagina: 1 },
    ]);

    await vieja.liberar();
    await avanzar(0);

    expect(sugerencias()).toEqual([SUGERENCIA_155]);
    expect(presupuestosDeLaTabla()).toEqual(['155']);
  });

  it('BuscarFacturas_BuscarDeshabilitadoSoloEnElEnvioManual', async () => {
    // D-08: la búsqueda en vivo no deshabilita "Buscar"; el envío manual sí, hasta su respuesta, y la reemplaza.
    const enVivo = diferida(pagina([FILA_154]));
    const manual = diferida(pagina([FILA_155]));
    const { servidor, enfocado: apellido } = await abrirEnVivo({ [CLAVE]: [enVivo.respuesta, manual.respuesta] });
    fireEvent.change(apellido, { target: { value: 'pé' } });
    await avanzar(300);
    expect(pedidos(servidor)).toHaveLength(1);
    expect(botonBuscar().hasAttribute('disabled')).toBe(false);
    expect(screen.queryByText('Cargando…')).toBeNull();

    buscar();
    expect(pedidos(servidor)).toHaveLength(2);
    expect(botonBuscar().hasAttribute('disabled')).toBe(true);
    await enVivo.liberar();
    await avanzar(0);
    expect(botonBuscar().hasAttribute('disabled')).toBe(true);
    expect(screen.queryByRole('table')).toBeNull();

    await manual.liberar();
    await avanzar(0);
    expect(botonBuscar().hasAttribute('disabled')).toBe(false);
    expect(presupuestosDeLaTabla()).toEqual(['155']);
    expect(sugerencias()).toEqual([]);
  });

  it('BuscarFacturas_SinSesionEnVivo_VuelveAlIngreso', async () => {
    const { enfocado: apellido } = await abrirEnVivo({ [CLAVE]: vacia(401) });
    fireEvent.change(apellido, { target: { value: 'pé' } });
    await avanzar(300);

    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
  });

  it('BuscarFacturas_RangoInvalidoDuranteLaPausa_SinPedidoNiSugerencias', async () => {
    // Verificador H-1: las fechas pasan a un rango invertido mientras corre la pausa: no se pide y se vacían las
    // sugerencias.
    const { servidor, enfocado: apellido } = await abrirEnVivo({ [CLAVE]: json(200, pagina([FILA_155])) });
    fireEvent.change(apellido, { target: { value: 'pé' } });
    await avanzar(300);
    expect(sugerencias()).toEqual([SUGERENCIA_155]);

    fireEvent.change(apellido, { target: { value: 'pér' } });
    escribir('Desde', '2026-11-20');
    escribir('Hasta', '2026-10-01');
    await avanzar(300);

    expect(pedidos(servidor)).toHaveLength(1);
    expect(sugerencias()).toEqual([]);
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('BuscarFacturas_CampoVaciadoConPedidoEnCurso_NoReponeSugerencias', async () => {
    // D-23: el pedido de "pé" sigue en curso al vaciar el campo; cuando llega, no repone sus sugerencias.
    const enCurso = diferida(pagina([FILA_155]));
    const vaciado = diferida(pagina([FILA_154]));
    const { servidor, enfocado: apellido } = await abrirEnVivo({ [CLAVE]: [enCurso.respuesta, vaciado.respuesta] });
    fireEvent.change(apellido, { target: { value: 'pé' } });
    await avanzar(300);
    expect(pedidos(servidor)).toHaveLength(1);

    fireEvent.change(apellido, { target: { value: '' } });
    await enCurso.liberar();
    await avanzar(0);

    expect(sugerencias()).toEqual([]);
    expect(apellido.getAttribute('aria-expanded')).toBe('false');
    elegirConEnter(apellido);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);

    await avanzar(300);
    expect(cuerpos(servidor)[1]).toEqual({ pagina: 1 });
    await vaciado.liberar();
    expect(presupuestosDeLaTabla()).toEqual(['154']);
    expect(sugerencias()).toEqual([]);
  });

  it('BuscarFacturas_FechasInvalidas_SinSugerenciasEnElMismoEvento', async () => {
    // D-22: invertir solo las fechas vacía las sugerencias en ese evento, sin pedir, y la respuesta viva en curso no
    // las repone.
    const enCurso = diferida(pagina([FILA_155]));
    const { servidor, enfocado: apellido } = await abrirEnVivo({
      [CLAVE]: [enCurso.respuesta, json(200, pagina([FILA_155, FILA_154]))],
    });

    // Con un pedido en curso.
    fireEvent.change(apellido, { target: { value: 'pé' } });
    await avanzar(300);
    escribir('Desde', '2026-11-20');
    escribir('Hasta', '2026-10-01');
    await enCurso.liberar();
    await avanzar(0);
    expect(sugerencias()).toEqual([]);
    elegirConEnter(apellido);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);

    // Con las sugerencias a la vista.
    escribir('Desde', '2026-09-01');
    fireEvent.change(apellido, { target: { value: 'pér' } });
    await avanzar(300);
    expect(sugerencias()).toHaveLength(2);

    escribir('Desde', '2026-11-20');

    expect(sugerencias()).toEqual([]);
    expect(apellido.getAttribute('aria-expanded')).toBe('false');
    elegirConEnter(apellido);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
    await avanzar(300);
    expect(pedidos(servidor)).toHaveLength(2);
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('BuscarFacturas_SugerenciaSinDni', async () => {
    // R-02 y D-15: ni las sugerencias ni el anuncio muestran el DNI, aunque se busque por DNI; nada en la consola, en
    // el almacenamiento (afterEach) ni en la URL.
    const consola = (['log', 'info', 'warn', 'error', 'debug', 'trace'] as const).map((metodo) =>
      vi.spyOn(console, metodo).mockImplementation(() => {}),
    );
    const { servidor, enfocado: dni } = await abrirEnVivo({ [CLAVE]: json(200, pagina([FILA_155])) }, 'DNI');

    fireEvent.change(dni, { target: { value: DNI_ESCRITO } });
    await avanzar(300);

    expect(cuerpos(servidor)).toEqual([{ dni: DNI_ESCRITO, pagina: 1 }]);
    expect(sugerencias()).toEqual([SUGERENCIA_155]);
    for (const opcion of screen.getAllByRole('option')) {
      expect(opcion.textContent).not.toContain(DNI_ESCRITO);
      expect(opcion.id).not.toContain(DNI_ESCRITO);
    }
    const anuncio = within(dni.closest('.campo-busqueda')!).getByRole('status').textContent;
    expect(anuncio).toBe('1 sugerencia');
    expect(anuncio).not.toContain(DNI_ESCRITO);
    expect(dni.getAttribute('autocomplete')).toBe('off');
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
    expect(decodeURIComponent(window.location.href)).not.toContain(DNI_ESCRITO);
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    for (const espia of consola) {
      expect(espia).not.toHaveBeenCalled();
    }
  });

  it('BuscarFacturas_DosEnviosManuales_BuscarSigueDeshabilitadoHastaElUltimo', async () => {
    // D-08: un Enter reemplaza al envío manual en curso; que el primero termine no vuelve a habilitar "Buscar".
    const primera = diferida(pagina([FILA_154]));
    const segunda = diferida(pagina([FILA_155]));
    await abrir({ [CLAVE]: [primera.respuesta, segunda.respuesta] });

    buscar();
    fireEvent.submit(screen.getByRole('search'));
    await primera.liberar();
    await act(async () => {});

    expect(botonBuscar().hasAttribute('disabled')).toBe(true);
    await segunda.liberar();
    await screen.findByRole('table', { name: 'Facturas encontradas' });
    expect(botonBuscar().hasAttribute('disabled')).toBe(false);
    expect(presupuestosDeLaTabla()).toEqual(['155']);
  });
});
