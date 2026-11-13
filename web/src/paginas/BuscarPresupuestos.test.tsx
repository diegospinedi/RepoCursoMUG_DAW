import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
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
};

async function abrir() {
  renderizarApp(RUTA);
  await screen.findByRole('heading', { name: TITULO });
}

const campo = (etiqueta: string) => screen.getByLabelText(etiqueta) as HTMLInputElement;
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

/** Una respuesta que llega cuando la prueba la libera. */
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

describe('BuscarPresupuestos', () => {
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

  it('una búsqueda en curso: no envía otra ni pagina', async () => {
    const primera = diferida(pagina(filasDel(120, 71), 1, 120));
    const servidor = servidorFalso({ [CLAVE]: primera.respuesta });
    await abrir();

    buscar();
    buscar();
    fireEvent.submit(campo('Apellido').form!);

    expect((screen.getByRole('button', { name: 'Buscar' }) as HTMLButtonElement).disabled).toBe(true);
    expect(servidor.pedidosA('POST', '/api/presupuestos/busqueda')).toHaveLength(1);

    await primera.liberar();
    await screen.findByText('Página 1 de 3');
    expect((screen.getByRole('button', { name: 'Buscar' }) as HTMLButtonElement).disabled).toBe(false);

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
