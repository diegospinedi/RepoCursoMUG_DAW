import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { diferida } from '../pruebas/diferida';
import { mensajeJuntoAlCampo } from '../pruebas/formulario';
import { json, renderizarApp, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';

/** Datos ficticios (Ley 25.326): ningún dato real de un cliente. */
const APELLIDO_ESCRITO = 'González';

afterEach(() => {
  try {
    // R-02: los filtros viven solo en el estado del componente: ni en el almacenamiento ni en la URL.
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(decodeURIComponent(window.location.href)).not.toContain(APELLIDO_ESCRITO);
  } finally {
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
});

const RUTA = '/presupuestos';
const TITULO = 'Buscar presupuestos';
const CLAVE = 'POST /api/presupuestos/busqueda';
const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';
const MENSAJE_PEDIDO_RECHAZADO = 'No se pudo completar la operación. Recargá la página y reintentá.';
const MENSAJE_DESDE_POSTERIOR = 'La fecha Desde no puede ser posterior a Hasta.';
const MENSAJE_413 = 'La búsqueda es demasiado larga.';
const ENCABEZADOS = ['Número', 'Fecha', 'Apellido', 'Nombre', 'DNI', 'Estado', 'Total ($)'];

const FILA_155 = {
  numero: 155,
  fecha: '2026-03-10',
  apellido: 'González',
  nombre: 'María José',
  dni: '23456789',
  estado: 'final',
  total: 2710.02,
};
const FILA_154 = {
  numero: 154,
  fecha: '2026-03-09',
  apellido: 'Gómez',
  nombre: 'Ana',
  dni: '30111222',
  estado: 'borrador',
  total: 1850,
};

/** Las filas de los presupuestos `desde` a `hasta`, de mayor a menor, como las ordena la Api (FR-08). */
const filasDel = (desde: number, hasta: number) =>
  Array.from({ length: desde - hasta + 1 }, (_, i) => ({
    ...FILA_154,
    numero: desde - i,
  }));

const pagina = (presupuestos: unknown[], numero = 1, total = presupuestos.length) => ({
  presupuestos,
  pagina: numero,
  tamanoPagina: 50,
  total,
});

/** El presupuesto 155 completo, como lo devuelve `GET /api/presupuestos/155`, con datos ficticios. */
const PRESUPUESTO_155 = {
  numero: 155,
  fecha: '2026-03-10',
  estado: 'final',
  cliente: {
    apellido: 'González',
    nombre: 'María José',
    dni: '23456789',
    domicilio: null,
    email: null,
    telefono: null,
  },
  lineas: [
    {
      codigoArticulo: 1,
      descripcion: 'Armazón metálico',
      precioUnitario: 2710.02,
      cantidad: 1,
      porcentajeDescuento: 0,
      precioConDescuento: 2710.02,
      precioFinal: 2710.02,
      orden: 1,
    },
  ],
  total: 2710.02,
  version: 4,
  // El GET suma la facturación (FEAT-007, D-19) y la observación de la emisión pendiente (FEAT-008).
  factura: null,
  emisionPendiente: false,
  observacionEmision: null,
};

async function abrir() {
  renderizarApp(RUTA);
  await screen.findByRole('heading', { name: TITULO });
}

/**
 * El campo de la etiqueta. Desde FEAT-013 Apellido, Nombre y DNI son combobox con sugerencias, y la lista también se
 * nombra con la etiqueta (`aria-labelledby`): por eso se buscan por su rol; las fechas, por la etiqueta.
 */
const campo = (etiqueta: string) =>
  (screen.queryByRole('combobox', { name: etiqueta }) ?? screen.getByLabelText(etiqueta)) as HTMLInputElement;
const escribir = (etiqueta: string, valor: string) => fireEvent.change(campo(etiqueta), { target: { value: valor } });
const buscar = () => fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
const anterior = () => screen.getByRole('button', { name: 'Anterior' }) as HTMLButtonElement;
const siguiente = () => screen.getByRole('button', { name: 'Siguiente' }) as HTMLButtonElement;

/** El cuerpo JSON de cada búsqueda enviada, en orden. */
function cuerpos(servidor: ReturnType<typeof servidorFalso>): unknown[] {
  return servidor.pedidosA('POST', '/api/presupuestos/busqueda').map((p) => JSON.parse(String(p.init?.body)));
}

/** El texto de cada celda de cada fila de datos de la tabla. */
function filas(): string[][] {
  const tabla = screen.getByRole('table');
  const [, ...datos] = within(tabla).getAllByRole('row');
  return datos.map((fila) => within(fila).getAllByRole('cell').map((celda) => celda.textContent ?? ''));
}

/** Los mensajes de la pantalla con rol `alert` (generales y junto a los campos). */
function mensajes(): string[] {
  const pantalla = screen.getByRole('heading', { name: TITULO }).closest('section')!;
  return within(pantalla)
    .queryAllByRole('alert')
    .map((elemento) => elemento.textContent ?? '');
}

describe('BuscarPresupuestos', () => {
  it('BuscarPresupuestos_Paginacion_BotonesSecundarios', async () => {
    // FEAT-012, D-14, AC-13: Anterior y Siguiente son secundarios; Buscar sigue principal.
    servidorFalso({ [CLAVE]: json(200, pagina(filasDel(120, 71), 1, 120)) });
    await abrir();
    buscar();
    await screen.findByText('Página 1 de 3');

    expect(anterior().classList.contains('boton-secundario')).toBe(true);
    expect(siguiente().classList.contains('boton-secundario')).toBe(true);
    expect(screen.getByRole('button', { name: 'Buscar' }).classList.contains('boton-secundario')).toBe(false);
  });

  it('buscar sin filtros muestra la primera página y Siguiente', async () => {
    // AC-08: 120 presupuestos; la primera página trae del 120 al 71 y la tercera del 20 al 1.
    const servidor = servidorFalso({
      [CLAVE]: [
        json(200, pagina(filasDel(120, 71), 1, 120)),
        json(200, pagina(filasDel(70, 21), 2, 120)),
        json(200, pagina(filasDel(20, 1), 3, 120)),
        json(200, pagina(filasDel(70, 21), 2, 120)),
      ],
    });
    await abrir();

    // Al abrir no busca: la búsqueda empieza con Buscar.
    expect(servidor.pedidosA('POST', '/api/presupuestos/busqueda')).toHaveLength(0);
    expect(screen.queryByRole('table')).toBeNull();

    buscar();

    await screen.findByText('Página 1 de 3');
    expect(filas()).toHaveLength(50);
    expect(filas()[0]?.[0]).toBe('120');
    expect(filas()[49]?.[0]).toBe('71');
    expect(anterior().disabled).toBe(true);
    expect(siguiente().disabled).toBe(false);
    // D-23: sin filtros solo viaja la página; nunca una fecha "".
    expect(cuerpos(servidor)).toEqual([{ pagina: 1 }]);

    // Un texto escrito después de buscar no cambia la búsqueda que se pagina.
    escribir('Apellido', APELLIDO_ESCRITO);
    fireEvent.click(siguiente());
    await screen.findByText('Página 2 de 3');
    expect(filas()[0]?.[0]).toBe('70');

    fireEvent.click(siguiente());
    await screen.findByText('Página 3 de 3');
    expect(filas()).toHaveLength(20);
    expect(filas()[0]?.[0]).toBe('20');
    expect(filas()[19]?.[0]).toBe('1');
    expect(siguiente().disabled).toBe(true);
    expect(anterior().disabled).toBe(false);

    fireEvent.click(anterior());
    await screen.findByText('Página 2 de 3');
    expect(cuerpos(servidor)).toEqual([{ pagina: 1 }, { pagina: 2 }, { pagina: 3 }, { pagina: 2 }]);
    // Los filtros no van a la URL (R-02).
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
  });

  it('cada resultado muestra los siete datos', async () => {
    // AC-09 (FR-09): número, fecha, Apellido, Nombre, DNI, estado y total.
    servidorFalso({ [CLAVE]: json(200, pagina([FILA_155, FILA_154])) });
    await abrir();
    buscar();

    await waitFor(() => expect(filas()).toHaveLength(2));
    const encabezados = within(screen.getByRole('table'))
      .getAllByRole('columnheader')
      .map((celda) => celda.textContent);
    expect(encabezados).toEqual(ENCABEZADOS);
    expect(filas()).toEqual([
      ['155', '10/03/2026', 'González', 'María José', '23456789', 'Final', '2.710,02'],
      ['154', '09/03/2026', 'Gómez', 'Ana', '30111222', 'Borrador', '1.850,00'],
    ]);
    expect(screen.getByText('Página 1 de 1')).toBeTruthy();
  });

  it('el número abre el presupuesto', async () => {
    // AC-10 (FR-10): el número es un enlace a /presupuestos/:numero, que muestra sus datos y líneas.
    servidorFalso({
      [CLAVE]: json(200, pagina([FILA_155])),
      'GET /api/presupuestos/155': json(200, PRESUPUESTO_155),
    });
    await abrir();
    buscar();

    const enlace = await screen.findByRole('link', { name: 'Abrir el presupuesto 155' });
    expect(enlace.getAttribute('href')).toBe('/presupuestos/155');
    expect(enlace.textContent).toBe('155');

    fireEvent.click(enlace);

    expect(await screen.findByRole('heading', { name: 'Presupuesto' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/presupuestos/155');
    expect(await screen.findByText('Armazón metálico')).toBeTruthy();
  });

  it('desde posterior a hasta: rechazado sin llamar a la Api', async () => {
    // AC-07 (FR-06): no busca y avisa junto a Desde.
    const servidor = servidorFalso({ [CLAVE]: json(200, pagina([FILA_155])) });
    await abrir();

    escribir('Desde', '2026-03-20');
    escribir('Hasta', '2026-03-10');
    buscar();

    await waitFor(() => expect(mensajeJuntoAlCampo(campo('Desde'))).toBe(MENSAJE_DESDE_POSTERIOR));
    expect(campo('Desde').getAttribute('aria-invalid')).toBe('true');
    expect(campo('Hasta').getAttribute('aria-invalid')).toBe('false');
    expect(mensajes()).toEqual([MENSAJE_DESDE_POSTERIOR]);
    expect(servidor.pedidosA('POST', '/api/presupuestos/busqueda')).toHaveLength(0);

    // Con el rango corregido (límites iguales) busca y el aviso desaparece.
    escribir('Desde', '2026-03-10');
    buscar();

    await waitFor(() => expect(filas()).toHaveLength(1));
    expect(mensajeJuntoAlCampo(campo('Desde'))).toBeNull();
    expect(cuerpos(servidor)).toEqual([{ desde: '2026-03-10', hasta: '2026-03-10', pagina: 1 }]);
  });

  it('desde posterior a hasta después de una búsqueda: oculta la grilla anterior', async () => {
    // La grilla anterior no puede parecer el resultado del rango invertido ni paginarse con los filtros viejos.
    const servidor = servidorFalso({ [CLAVE]: json(200, pagina(filasDel(120, 71), 1, 120)) });
    await abrir();
    buscar();
    await screen.findByText('Página 1 de 3');

    escribir('Desde', '2026-03-20');
    escribir('Hasta', '2026-03-10');
    buscar();

    await waitFor(() => expect(mensajeJuntoAlCampo(campo('Desde'))).toBe(MENSAJE_DESDE_POSTERIOR));
    expect(screen.queryByRole('table')).toBeNull();
    expect(screen.queryByText('Página 1 de 3')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Siguiente' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Anterior' })).toBeNull();
    expect(servidor.pedidosA('POST', '/api/presupuestos/busqueda')).toHaveLength(1);
  });

  it('error 400 inválido de la Api junto al campo', async () => {
    // Cada error junto a su campo (todosLosErrores); el de la página, que no tiene campo, es un mensaje general fijo.
    const servidor = servidorFalso({
      [CLAVE]: [
        json(200, pagina([FILA_155])),
        json(400, {
          status: 400,
          errors: {
            dni: ['Usá hasta 20 caracteres.'],
            hasta: ['La fecha no es válida.'],
            apellido: ['Usá hasta 100 caracteres.'],
            pagina: ['Texto de la página que no se repite.'],
          },
        }),
      ],
    });
    await abrir();
    buscar();
    await waitFor(() => expect(filas()).toHaveLength(1));

    escribir('Apellido', APELLIDO_ESCRITO);
    escribir('DNI', '23.456');
    escribir('Hasta', '2026-03-10');
    buscar();

    await waitFor(() => expect(mensajeJuntoAlCampo(campo('DNI'))).toBe('Usá hasta 20 caracteres.'));
    expect(mensajeJuntoAlCampo(campo('Hasta'))).toBe('La fecha no es válida.');
    expect(mensajeJuntoAlCampo(campo('Apellido'))).toBe('Usá hasta 100 caracteres.');
    expect(mensajeJuntoAlCampo(campo('Nombre'))).toBeNull();
    expect(mensajeJuntoAlCampo(campo('Desde'))).toBeNull();
    expect(campo('DNI').getAttribute('aria-invalid')).toBe('true');
    // El texto del servidor para un campo que no está en pantalla no se muestra; sí el mensaje general fijo.
    expect(screen.queryByText('Texto de la página que no se repite.')).toBeNull();
    expect(screen.getByText(MENSAJE_PEDIDO_RECHAZADO)).toBeTruthy();
    // Cada texto aparece una sola vez: junto a su campo.
    expect(screen.getAllByText('Usá hasta 20 caracteres.')).toHaveLength(1);
    // La grilla anterior no queda como resultado de esta búsqueda.
    expect(screen.queryByRole('table')).toBeNull();
    expect(cuerpos(servidor)[1]).toEqual({ apellido: APELLIDO_ESCRITO, dni: '23.456', hasta: '2026-03-10', pagina: 1 });

    // Una búsqueda nueva limpia los errores anteriores.
    servidorFalso({ [CLAVE]: json(200, pagina([FILA_155])) });
    buscar();
    await waitFor(() => expect(filas()).toHaveLength(1));
    expect(mensajes()).toEqual([]);
  });

  it('error 400 sin errores por campo: mensaje general fijo', async () => {
    servidorFalso({ [CLAVE]: vacia(400) });
    await abrir();
    buscar();

    expect(await screen.findByText(MENSAJE_PEDIDO_RECHAZADO)).toBeTruthy();
    expect(screen.queryByRole('table')).toBeNull();
  });

  it.each([
    ['falla de red', sinRed],
    ['500', vacia(500)],
  ])('error de red: mensaje general (%s)', async (_caso, falla) => {
    const servidor = servidorFalso({ [CLAVE]: [json(200, pagina([FILA_155, FILA_154])), falla] });
    await abrir();
    buscar();
    await waitFor(() => expect(filas()).toHaveLength(2));

    buscar();

    expect(await screen.findByText(MENSAJE_SIN_CONEXION)).toBeTruthy();
    // La grilla anterior no se muestra como actual.
    expect(screen.queryByRole('table')).toBeNull();
    expect(screen.queryByText('Página 1 de 1')).toBeNull();
    // El mensaje está arriba de donde va la grilla, no junto a un campo.
    expect(mensajeJuntoAlCampo(campo('Apellido'))).toBeNull();
    expect(servidor.pedidosA('POST', '/api/presupuestos/busqueda')).toHaveLength(2);
  });

  it('413 rechazado: mensaje general fijo', async () => {
    servidorFalso({
      [CLAVE]: json(413, { status: 413, errors: { apellido: ['Texto del servidor que no se repite.'] } }),
    });
    await abrir();
    escribir('Apellido', APELLIDO_ESCRITO);
    buscar();

    expect(await screen.findByText(MENSAJE_413)).toBeTruthy();
    expect(mensajeJuntoAlCampo(campo('Apellido'))).toBeNull();
    expect(screen.queryByText('Texto del servidor que no se repite.')).toBeNull();
    expect(mensajes()).toEqual([MENSAJE_413]);
  });

  it('sin sesión: vuelve al ingreso', async () => {
    servidorFalso({ [CLAVE]: vacia(401) });
    await abrir();
    buscar();

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
  });

  it('error de un campo en un 401 con errors: junto al campo, sin mensaje general', async () => {
    // Fija el comportamiento existente de traducirError (cliente.ts): un 401 con `errors` de campo es un rechazo de
    // campo, no "sin sesión", y se muestra junto al campo, como en el resto de la app. La Api de búsqueda nunca
    // responde así (su 401 no trae cuerpo); la prueba solo asegura que la pantalla no lo trata distinto.
    servidorFalso({ [CLAVE]: json(401, { status: 401, errors: { nombre: ['Usá hasta 100 caracteres.'] } }) });
    await abrir();
    buscar();

    await waitFor(() => expect(mensajeJuntoAlCampo(campo('Nombre'))).toBe('Usá hasta 100 caracteres.'));
    expect(mensajes()).toEqual(['Usá hasta 100 caracteres.']);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
  });

  it('Enter en un campo busca', async () => {
    const servidor = servidorFalso({ [CLAVE]: json(200, pagina([FILA_155])) });
    await abrir();
    escribir('Nombre', '  maria ');

    fireEvent.submit(campo('Nombre').form!);

    await waitFor(() => expect(filas()).toHaveLength(1));
    expect(cuerpos(servidor)).toEqual([{ nombre: 'maria', pagina: 1 }]);
  });

  it('una búsqueda manual en curso: el doble clic no envía otra; Enter la reemplaza y la vieja no pisa la nueva', async () => {
    // D-08 de FEAT-013: la guarda `enCurso` se reemplaza por el número de pedido. El botón deshabilitado evita el doble
    // clic; un Enter mientras tanto envía otra búsqueda, y solo la respuesta de la última se muestra.
    const primera = diferida(pagina(filasDel(120, 71), 1, 120));
    const reemplazo = diferida(pagina([FILA_155]));
    const servidor = servidorFalso({ [CLAVE]: [primera.respuesta, reemplazo.respuesta] });
    await abrir();

    buscar();
    buscar();

    expect((screen.getByRole('button', { name: 'Buscar' }) as HTMLButtonElement).disabled).toBe(true);
    expect(servidor.pedidosA('POST', '/api/presupuestos/busqueda')).toHaveLength(1);

    fireEvent.submit(campo('Apellido').form!);
    expect(servidor.pedidosA('POST', '/api/presupuestos/busqueda')).toHaveLength(2);
    expect((screen.getByRole('button', { name: 'Buscar' }) as HTMLButtonElement).disabled).toBe(true);

    await reemplazo.liberar();
    await screen.findByText('Página 1 de 1');
    expect((screen.getByRole('button', { name: 'Buscar' }) as HTMLButtonElement).disabled).toBe(false);
    await primera.liberar();
    expect(filas().map((f) => f[0])).toEqual(['155']);

    // Una búsqueda que trae varias páginas, para la paginación.
    servidorFalso({ [CLAVE]: json(200, pagina(filasDel(120, 71), 1, 120)) });
    buscar();
    await screen.findByText('Página 1 de 3');

    // Mientras llega la página 2, Anterior y Siguiente quedan deshabilitados y un segundo clic no pide otra.
    const segunda = diferida(pagina(filasDel(70, 21), 2, 120));
    servidorFalso({ [CLAVE]: segunda.respuesta });
    fireEvent.click(siguiente());
    fireEvent.click(siguiente());
    expect(siguiente().disabled).toBe(true);
    expect(anterior().disabled).toBe(true);
    await segunda.liberar();
    await screen.findByText('Página 2 de 3');
  });

  it('sin coincidencias: aviso', async () => {
    servidorFalso({ [CLAVE]: json(200, pagina([], 1, 0)) });
    await abrir();
    escribir('DNI', '99');
    buscar();

    expect(await screen.findByText('No hay presupuestos que coincidan con la búsqueda.')).toBeTruthy();
    expect(screen.queryByRole('table')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Siguiente' })).toBeNull();
  });

  it('campos: fechas, largos y etiquetas', async () => {
    servidorFalso({});
    await abrir();

    expect(campo('Desde').type).toBe('date');
    expect(campo('Hasta').type).toBe('date');
    expect(campo('Apellido').maxLength).toBe(100);
    expect(campo('Nombre').maxLength).toBe(100);
    expect(campo('DNI').maxLength).toBe(20);
    // Sin autocompletado: el navegador no guarda en su historial los datos personales buscados.
    for (const etiqueta of ['Apellido', 'Nombre', 'DNI']) {
      expect(campo(etiqueta).getAttribute('autocomplete')).toBe('off');
    }
    expect(screen.getByRole('search')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Volver al inicio' }).getAttribute('href')).toBe('/');
  });
});

/** Avanza el reloj falso (solo `setTimeout`) y deja que React procese lo que eso dispare. */
const avanzar = (ms: number) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });

/** El texto de cada sugerencia visible. */
const sugerencias = () => screen.queryAllByRole('option').map((opcion) => opcion.textContent);

const pedidos = (servidor: ReturnType<typeof servidorFalso>) => servidor.pedidosA('POST', '/api/presupuestos/busqueda');

const botonBuscar = () => screen.getByRole('button', { name: 'Buscar' }) as HTMLButtonElement;

/** Abre la pantalla con el reloj real, pasa al reloj falso y pone el foco en el campo indicado. */
async function abrirEnVivo(etiqueta = 'Apellido') {
  await abrir();
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  const enfocado = campo(etiqueta);
  act(() => enfocado.focus());
  return enfocado;
}

const SUGERENCIA_155 = '155 · González, María José · 10/03/2026';
const SUGERENCIA_154 = '154 · Gómez, Ana · 09/03/2026';

describe('BuscarPresupuestos — búsqueda mientras se escribe (FEAT-013)', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('BuscarPresupuestos_EscribirApellido_SugerenciasYAbrir155', async () => {
    // AC-04: tras la pausa, una sola búsqueda llena la tabla y las sugerencias; elegir la del 155 lo abre.
    const servidor = servidorFalso({
      [CLAVE]: json(200, pagina([FILA_155, FILA_154])),
      'GET /api/presupuestos/155': json(200, PRESUPUESTO_155),
    });
    const apellido = await abrirEnVivo();

    fireEvent.change(apellido, { target: { value: 'p' } });
    fireEvent.change(apellido, { target: { value: 'pé' } });
    fireEvent.change(apellido, { target: { value: ' pér ' } });
    await avanzar(299);
    expect(pedidos(servidor)).toHaveLength(0);
    await avanzar(1);

    // Los filtros van en el cuerpo, recortados (ADR-019).
    expect(cuerpos(servidor)).toEqual([{ apellido: 'pér', pagina: 1 }]);
    expect(sugerencias()).toEqual([SUGERENCIA_155, SUGERENCIA_154]);
    expect(filas().map((f) => f[0])).toEqual(['155', '154']);
    expect(apellido.getAttribute('aria-expanded')).toBe('true');
    // Solo el campo con el foco abre la lista.
    expect(campo('Nombre').getAttribute('aria-expanded')).toBe('false');
    expect(within(apellido.closest('.campo-busqueda')!).getByRole('status').textContent).toBe('2 sugerencias');
    const opcion155 = screen.getByRole('option', { name: SUGERENCIA_155 });
    expect(opcion155.id.endsWith('-155')).toBe(true);

    fireEvent.click(opcion155);

    expect(screen.getByTestId('ubicacion').textContent).toBe('/presupuestos/155');
  });

  it('BuscarPresupuestos_RespuestaViejaNoPisaLaNueva', async () => {
    // AC-09: la respuesta de "gó" llega después que la de "gon": siguen las sugerencias y la tabla de "gon".
    const vieja = diferida(pagina([FILA_154]));
    const servidor = servidorFalso({ [CLAVE]: [vieja.respuesta, json(200, pagina([FILA_155]))] });
    const apellido = await abrirEnVivo();

    fireEvent.change(apellido, { target: { value: 'gó' } });
    await avanzar(300);
    expect(pedidos(servidor)).toHaveLength(1);
    fireEvent.change(apellido, { target: { value: 'gon' } });
    await avanzar(300);
    expect(cuerpos(servidor)).toEqual([
      { apellido: 'gó', pagina: 1 },
      { apellido: 'gon', pagina: 1 },
    ]);
    expect(sugerencias()).toEqual([SUGERENCIA_155]);

    await vieja.liberar();
    await avanzar(0);

    expect(sugerencias()).toEqual([SUGERENCIA_155]);
    expect(filas().map((f) => f[0])).toEqual(['155']);
  });

  it('BuscarPresupuestos_SugerenciaSinDni', async () => {
    // R-02 y D-15: ni las sugerencias ni el anuncio muestran el DNI, aunque se busque por DNI; nada en la consola,
    // en el almacenamiento ni en la URL.
    const consola = (['log', 'info', 'warn', 'error', 'debug', 'trace'] as const).map((metodo) =>
      vi.spyOn(console, metodo).mockImplementation(() => {}),
    );
    const servidor = servidorFalso({ [CLAVE]: json(200, pagina([FILA_155])) });
    const dni = await abrirEnVivo('DNI');

    fireEvent.change(dni, { target: { value: '2345' } });
    await avanzar(300);

    expect(cuerpos(servidor)).toEqual([{ dni: '2345', pagina: 1 }]);
    expect(sugerencias()).toEqual([SUGERENCIA_155]);
    for (const opcion of screen.getAllByRole('option')) {
      expect(opcion.textContent).not.toContain(FILA_155.dni);
      expect(opcion.id).not.toContain(FILA_155.dni);
    }
    expect(within(dni.closest('.campo-busqueda')!).getByRole('status').textContent).toBe('1 sugerencia');
    expect(dni.getAttribute('autocomplete')).toBe('off');
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
    expect(decodeURIComponent(window.location.href)).not.toContain('2345');
    for (const espia of consola) {
      expect(espia).not.toHaveBeenCalled();
    }
  });

  it('BuscarPresupuestos_RangoInvalidoEnVivo_SinPedido', async () => {
    // D-07: con Desde posterior a Hasta no pide ni muestra nada en vivo; el error aparece con "Buscar". Con el rango
    // corregido, lo escrito busca con las fechas.
    const servidor = servidorFalso({ [CLAVE]: json(200, pagina([FILA_155])) });
    const apellido = await abrirEnVivo();

    escribir('Desde', '2026-03-20');
    escribir('Hasta', '2026-03-10');
    fireEvent.change(apellido, { target: { value: 'pér' } });
    await avanzar(300);

    expect(pedidos(servidor)).toHaveLength(0);
    expect(mensajes()).toEqual([]);
    expect(sugerencias()).toEqual([]);

    buscar();
    expect(mensajeJuntoAlCampo(campo('Desde'))).toBe(MENSAJE_DESDE_POSTERIOR);
    expect(pedidos(servidor)).toHaveLength(0);

    escribir('Desde', '2026-03-01');
    fireEvent.change(apellido, { target: { value: 'pére' } });
    await avanzar(300);

    expect(cuerpos(servidor)).toEqual([{ apellido: 'pére', desde: '2026-03-01', hasta: '2026-03-10', pagina: 1 }]);
    expect(sugerencias()).toEqual([SUGERENCIA_155]);
  });

  it('BuscarPresupuestos_UnCaracter_SinPedidoNiSugerencias', async () => {
    // D-07: con 1 carácter no pide; las sugerencias anteriores no quedan para elegir y la tabla queda como estaba.
    const servidor = servidorFalso({ [CLAVE]: json(200, pagina([FILA_155, FILA_154])) });
    const apellido = await abrirEnVivo();

    fireEvent.change(apellido, { target: { value: 'gó' } });
    await avanzar(300);
    expect(sugerencias()).toHaveLength(2);

    fireEvent.change(apellido, { target: { value: ' g ' } });
    await avanzar(300);

    expect(pedidos(servidor)).toHaveLength(1);
    expect(sugerencias()).toEqual([]);
    expect(apellido.getAttribute('aria-expanded')).toBe('false');
    expect(filas().map((f) => f[0])).toEqual(['155', '154']);
  });

  it('BuscarPresupuestos_CampoVaciado_VuelveABuscarConLosOtrosFiltros', async () => {
    // D-07: el campo que queda vacío vuelve a buscar, con el resto de los filtros; sin ningún texto, sin sugerencias.
    const servidor = servidorFalso({
      [CLAVE]: [
        json(200, pagina([FILA_154])),
        json(200, pagina([FILA_155])),
        json(200, pagina([FILA_154, FILA_155])),
        json(200, pagina([FILA_155, FILA_154])),
      ],
    });
    const nombre = await abrirEnVivo('Nombre');

    fireEvent.change(nombre, { target: { value: 'an' } });
    await avanzar(300);
    const apellido = campo('Apellido');
    act(() => apellido.focus());
    fireEvent.change(apellido, { target: { value: 'gó' } });
    await avanzar(300);
    fireEvent.change(apellido, { target: { value: '' } });
    await avanzar(300);

    expect(cuerpos(servidor)).toEqual([
      { nombre: 'an', pagina: 1 },
      { apellido: 'gó', nombre: 'an', pagina: 1 },
      { nombre: 'an', pagina: 1 },
    ]);
    expect(filas().map((f) => f[0])).toEqual(['154', '155']);

    act(() => nombre.focus());
    fireEvent.change(nombre, { target: { value: '' } });
    await avanzar(300);

    expect(cuerpos(servidor)[3]).toEqual({ pagina: 1 });
    expect(filas().map((f) => f[0])).toEqual(['155', '154']);
    expect(sugerencias()).toEqual([]);
    expect(nombre.getAttribute('aria-expanded')).toBe('false');
  });

  it('BuscarPresupuestos_CambiarFechas_NoDispara', async () => {
    // D-07: Desde y Hasta aplican con "Buscar", no mientras se cambian.
    const servidor = servidorFalso({ [CLAVE]: json(200, pagina([FILA_155])) });
    const apellido = await abrirEnVivo();

    fireEvent.change(apellido, { target: { value: 'pé' } });
    await avanzar(300);
    expect(pedidos(servidor)).toHaveLength(1);

    escribir('Desde', '2026-03-01');
    escribir('Hasta', '2026-03-31');
    await avanzar(300);
    expect(pedidos(servidor)).toHaveLength(1);

    buscar();
    await avanzar(0);
    expect(cuerpos(servidor)[1]).toEqual({ apellido: 'pé', desde: '2026-03-01', hasta: '2026-03-31', pagina: 1 });
    // La respuesta de "Buscar" no abre sugerencias.
    expect(sugerencias()).toEqual([]);
  });

  it('BuscarPresupuestos_BuscarDeshabilitadoSoloEnElEnvioManual', async () => {
    // D-08: la búsqueda en vivo no deshabilita "Buscar"; el envío manual sí, hasta su respuesta, y reemplaza al pedido
    // en vivo (que ya no llega).
    const enVivo = diferida(pagina([FILA_154]));
    const manual = diferida(pagina([FILA_155]));
    const servidor = servidorFalso({ [CLAVE]: [enVivo.respuesta, manual.respuesta] });
    const apellido = await abrirEnVivo();

    fireEvent.change(apellido, { target: { value: 'gó' } });
    await avanzar(300);
    expect(pedidos(servidor)).toHaveLength(1);
    expect(botonBuscar().disabled).toBe(false);
    expect(screen.queryByText('Cargando…')).toBeNull();

    buscar();
    expect(pedidos(servidor)).toHaveLength(2);
    expect(botonBuscar().disabled).toBe(true);

    await enVivo.liberar();
    await avanzar(0);
    expect(botonBuscar().disabled).toBe(true);
    expect(screen.queryByRole('table')).toBeNull();

    await manual.liberar();
    await avanzar(0);
    expect(botonBuscar().disabled).toBe(false);
    expect(filas().map((f) => f[0])).toEqual(['155']);
    expect(sugerencias()).toEqual([]);
  });

  it('BuscarPresupuestos_400EnVivo_OcultaSugerenciasSinErrorDelCampo', async () => {
    // D-07: un 400 en vivo oculta las sugerencias y no muestra el error del campo; con "Buscar", sí.
    const rechazo = json(400, { status: 400, errors: { apellido: ['Usá hasta 100 caracteres.'] } });
    servidorFalso({ [CLAVE]: [json(200, pagina([FILA_155])), rechazo, rechazo] });
    const apellido = await abrirEnVivo();

    fireEvent.change(apellido, { target: { value: 'gó' } });
    await avanzar(300);
    expect(sugerencias()).toEqual([SUGERENCIA_155]);

    fireEvent.change(apellido, { target: { value: 'góm' } });
    await avanzar(300);

    expect(sugerencias()).toEqual([]);
    expect(mensajeJuntoAlCampo(apellido)).toBeNull();
    expect(mensajes()).toEqual([]);

    buscar();
    await avanzar(0);
    expect(mensajeJuntoAlCampo(apellido)).toBe('Usá hasta 100 caracteres.');
  });

  it('BuscarPresupuestos_ErrorDeBusquedaEnVivo_MensajeSinSugerencias', async () => {
    // AC-10: la búsqueda en vivo falla; se ocultan las sugerencias y la tabla, y se ve el mensaje de hoy.
    servidorFalso({ [CLAVE]: [json(200, pagina([FILA_155])), sinRed] });
    const apellido = await abrirEnVivo();

    fireEvent.change(apellido, { target: { value: 'gó' } });
    await avanzar(300);
    expect(sugerencias()).toEqual([SUGERENCIA_155]);

    fireEvent.change(apellido, { target: { value: 'góx' } });
    await avanzar(300);

    expect(sugerencias()).toEqual([]);
    expect(apellido.getAttribute('aria-expanded')).toBe('false');
    expect(mensajes()).toEqual([MENSAJE_SIN_CONEXION]);
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('BuscarPresupuestos_SinSesionEnVivo_VuelveAlIngreso', async () => {
    servidorFalso({ [CLAVE]: vacia(401) });
    const apellido = await abrirEnVivo();

    fireEvent.change(apellido, { target: { value: 'gó' } });
    await avanzar(300);

    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
  });

  it('BuscarPresupuestos_TextoInvalidoEnVivo_SinPedido', async () => {
    // D-07: un texto que la Api rechazaría (UTF-16 inválido) no se pide en vivo.
    const servidor = servidorFalso({ [CLAVE]: json(200, pagina([FILA_155])) });
    const apellido = await abrirEnVivo();

    fireEvent.change(apellido, { target: { value: `ab${String.fromCharCode(0xd83d)}` } });
    await avanzar(300);
    expect(pedidos(servidor)).toHaveLength(0);

    fireEvent.change(apellido, { target: { value: 'ab' } });
    await avanzar(300);
    expect(cuerpos(servidor)).toEqual([{ apellido: 'ab', pagina: 1 }]);
  });
});

describe('BuscarPresupuestos — sugerencias que ya no corresponden (FEAT-013, ronda 2)', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  /** Intenta elegir la primera sugerencia con el teclado. */
  const elegirConEnter = (campoEnfocado: HTMLInputElement) => {
    fireEvent.keyDown(campoEnfocado, { key: 'ArrowDown' });
    fireEvent.keyDown(campoEnfocado, { key: 'Enter' });
  };

  it('BuscarPresupuestos_UnCaracterOTextoInvalido_SinSugerenciasEnElMismoEvento', async () => {
    // H1: con 1 carácter o un texto inválido, las sugerencias anteriores no se ven ni se eligen, ni por un dibujo.
    servidorFalso({ [CLAVE]: json(200, pagina([FILA_155, FILA_154])) });
    const apellido = await abrirEnVivo();
    fireEvent.change(apellido, { target: { value: 'gó' } });
    await avanzar(300);
    expect(sugerencias()).toHaveLength(2);

    fireEvent.change(apellido, { target: { value: 'g' } });

    expect(sugerencias()).toEqual([]);
    expect(apellido.getAttribute('aria-expanded')).toBe('false');
    elegirConEnter(apellido);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);

    fireEvent.change(apellido, { target: { value: 'gó' } });
    await avanzar(300);
    expect(sugerencias()).toHaveLength(2);
    fireEvent.change(apellido, { target: { value: `gó${String.fromCharCode(0xd83d)}` } });

    expect(sugerencias()).toEqual([]);
    expect(apellido.getAttribute('aria-expanded')).toBe('false');
  });

  it('BuscarPresupuestos_CampoVaciado_SinSugerenciasEnElMismoEvento', async () => {
    // H1: el campo vaciado busca de nuevo, pero durante la pausa y el pedido no deja elegir las sugerencias viejas.
    const nuevo = diferida(pagina([FILA_154]));
    servidorFalso({ [CLAVE]: [json(200, pagina([FILA_155])), nuevo.respuesta] });
    const apellido = await abrirEnVivo();
    fireEvent.change(apellido, { target: { value: 'gó' } });
    await avanzar(300);
    expect(sugerencias()).toEqual([SUGERENCIA_155]);

    fireEvent.change(apellido, { target: { value: '' } });

    expect(sugerencias()).toEqual([]);
    expect(apellido.getAttribute('aria-expanded')).toBe('false');
    elegirConEnter(apellido);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);

    // Con el pedido en curso, tampoco.
    await avanzar(300);
    expect(sugerencias()).toEqual([]);
    elegirConEnter(apellido);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
    await nuevo.liberar();
    expect(filas().map((f) => f[0])).toEqual(['154']);
    expect(sugerencias()).toEqual([]);
  });

  it('BuscarPresupuestos_RangoInvalidoDuranteLaPausa_SinPedidoNiSugerencias', async () => {
    // Verificador H-1: las fechas pasan a un rango invertido mientras corre la pausa: no se pide y se vacían las
    // sugerencias.
    const servidor = servidorFalso({ [CLAVE]: json(200, pagina([FILA_155])) });
    const apellido = await abrirEnVivo();
    fireEvent.change(apellido, { target: { value: 'gó' } });
    await avanzar(300);
    expect(sugerencias()).toEqual([SUGERENCIA_155]);

    fireEvent.change(apellido, { target: { value: 'gón' } });
    escribir('Desde', '2026-03-20');
    escribir('Hasta', '2026-03-10');
    await avanzar(300);

    expect(pedidos(servidor)).toHaveLength(1);
    expect(sugerencias()).toEqual([]);
    expect(mensajes()).toEqual([]);
  });

  it('BuscarPresupuestos_CampoVaciadoConPedidoEnCurso_NoReponeSugerencias', async () => {
    // D-23: el pedido de "gó" sigue en curso al vaciar el campo; cuando llega, no repone sus sugerencias.
    const enCurso = diferida(pagina([FILA_155]));
    const vaciado = diferida(pagina([FILA_154]));
    const servidor = servidorFalso({ [CLAVE]: [enCurso.respuesta, vaciado.respuesta] });
    const apellido = await abrirEnVivo();
    fireEvent.change(apellido, { target: { value: 'gó' } });
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
    expect(filas().map((f) => f[0])).toEqual(['154']);
    expect(sugerencias()).toEqual([]);
  });

  it('BuscarPresupuestos_FechasInvalidas_SinSugerenciasEnElMismoEvento', async () => {
    // D-22: invertir solo las fechas vacía las sugerencias en ese evento, sin pedir, y la respuesta viva en curso no
    // las repone.
    const enCurso = diferida(pagina([FILA_155]));
    const servidor = servidorFalso({ [CLAVE]: [enCurso.respuesta, json(200, pagina([FILA_155, FILA_154]))] });
    const apellido = await abrirEnVivo();

    // Con un pedido en curso.
    fireEvent.change(apellido, { target: { value: 'gó' } });
    await avanzar(300);
    escribir('Desde', '2026-03-20');
    escribir('Hasta', '2026-03-10');
    await enCurso.liberar();
    await avanzar(0);
    expect(sugerencias()).toEqual([]);
    elegirConEnter(apellido);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);

    // Con las sugerencias a la vista.
    escribir('Desde', '2026-03-01');
    fireEvent.change(apellido, { target: { value: 'gón' } });
    await avanzar(300);
    expect(sugerencias()).toHaveLength(2);

    escribir('Desde', '2026-03-20');

    expect(sugerencias()).toEqual([]);
    expect(apellido.getAttribute('aria-expanded')).toBe('false');
    elegirConEnter(apellido);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
    await avanzar(300);
    expect(pedidos(servidor)).toHaveLength(2);
    expect(mensajes()).toEqual([]);
  });

  it('BuscarPresupuestos_DosEnviosManuales_BuscarSigueDeshabilitadoHastaElUltimo', async () => {
    // D-08: un Enter reemplaza al envío manual en curso; que el primero termine no vuelve a habilitar "Buscar".
    vi.useRealTimers();
    const primera = diferida(pagina([FILA_154]));
    const segunda = diferida(pagina([FILA_155]));
    servidorFalso({ [CLAVE]: [primera.respuesta, segunda.respuesta] });
    await abrir();

    buscar();
    fireEvent.submit(campo('Apellido').form!);
    await primera.liberar();
    await act(async () => {});

    expect(botonBuscar().disabled).toBe(true);
    await segunda.liberar();
    await screen.findByText('Página 1 de 1');
    expect(botonBuscar().disabled).toBe(false);
    expect(filas().map((f) => f[0])).toEqual(['155']);
  });
});
