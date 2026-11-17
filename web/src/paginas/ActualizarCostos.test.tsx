import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { json, problema, renderizarApp, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';

afterEach(() => {
  try {
    // La planilla y el resultado (códigos y costos de los proveedores) viven solo en el estado del componente.
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  } finally {
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
});

const RUTA = '/articulos/actualizar-costos';
const RUTA_API = '/api/articulos/planilla-costos';
const CLAVE_API = `POST ${RUTA_API}`;
const TITULO = 'Actualizar costos';
const ETIQUETA = 'Planilla de costos (.xlsx)';
const BOTON = 'Actualizar costos';
const PROCESANDO = 'Procesando…';
const AYUDA = 'La columna A conviene con formato Texto para no perder ceros a la izquierda.';
const MENSAJE_GRANDE = 'La planilla supera los 10 MB.';
const MENSAJE_OCUPADA = 'Hay otra planilla en proceso. Esperá unos segundos y volvé a cargarla.';
const MENSAJE_CAMBIADA =
  'Un artículo cambió mientras se cargaba la planilla. No se actualizó ningún costo. Volvé a cargarla.';
const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';
/** Los textos de los rechazos 400 del controlador (Block 4, D-24). */
const MENSAJE_NO_ES_XLSX =
  'El archivo no es una planilla .xlsx. Guardala como Libro de Excel (.xlsx) y volvé a cargarla.';
const MENSAJE_FORMATO =
  'El formato de la planilla no es el esperado. Tiene que tener solo dos columnas, con los encabezados ' +
  '"Codigo en el proveedor" y "Precio de Costo Nuevo" en la fila 1.';
const MENSAJE_LIMITES =
  'La planilla supera las 50.000 filas o tiene celdas demasiado largas. Dividila y cargala por partes. ' +
  'Si tiene formato en celdas vacías, quitáselo.';

const conSesion = { 'GET /api/sesion': vacia(204) };

const RESULTADO = {
  filasLeidas: 6,
  aplicadas: 4,
  noAplicadas: [
    { fila: 3, codigo: 'XYZ-9', razon: 'código inexistente' },
    { fila: 5, codigo: 'L-8', razon: 'precio inválido' },
  ],
  revisar: [
    { fila: 2, codigo: 'ABC-1', precioCosto: -100, indicaciones: ['actualizado con precio negativo o cero'] },
    { fila: 4, codigo: 'AC-9', precioCosto: 2420.5, indicaciones: ['inactivo'] },
    {
      fila: 6,
      codigo: 'M-1',
      precioCosto: 0,
      indicaciones: ['actualizado con precio negativo o cero', 'inactivo'],
    },
  ],
};

const planilla = (nombre = 'costos.xlsx') => new File([new Uint8Array([0x50, 0x4b, 0x03, 0x04])], nombre);

const pantalla = () => screen.getByRole('heading', { name: TITULO, level: 1 }).closest('section')!;
const campo = () => screen.getByLabelText(ETIQUETA) as HTMLInputElement;
const boton = () => screen.getByRole('button', { name: /Actualizar costos|Procesando…/ }) as HTMLButtonElement;
/** Los mensajes de la pantalla (sin la sonda de la ubicación, que también tiene el rol `status`). */
const mensajes = (rol: 'status' | 'alert') =>
  within(pantalla())
    .queryAllByRole(rol)
    .map((elemento) => elemento.textContent ?? '');

async function abrir() {
  renderizarApp(RUTA);
  await screen.findByRole('heading', { name: TITULO, level: 1 });
}

function elegir(archivo: File) {
  fireEvent.change(campo(), { target: { files: [archivo] } });
}

const actualizar = () => fireEvent.click(boton());

/** Encabezados y celdas de la tabla con ese título (su `caption`). */
function tabla(nombre: string) {
  const elemento = screen.getByRole('table', { name: nombre });
  const [encabezado, ...datos] = within(elemento).getAllByRole('row');
  return {
    encabezados: within(encabezado!)
      .getAllByRole('columnheader')
      .map((celda) => celda.textContent ?? ''),
    filas: datos.map((fila) => within(fila).getAllByRole('cell').map((celda) => celda.textContent ?? '')),
  };
}

/** Elige la planilla, la envía y espera el aviso de error. */
async function rechazada(respuesta: Parameters<typeof servidorFalso>[0][string]) {
  const servidor = servidorFalso({ ...conSesion, [CLAVE_API]: respuesta });
  await abrir();
  elegir(planilla());
  actualizar();
  await waitFor(() => expect(mensajes('alert')).toHaveLength(1));
  return servidor;
}

describe('ActualizarCostos', () => {
  it('muestra el resultado con aplicadas, no aplicadas y para revisar', async () => {
    const servidor = servidorFalso({ ...conSesion, [CLAVE_API]: json(200, RESULTADO) });
    await abrir();
    // En reposo, sin resultado ni mensajes.
    expect(screen.queryByRole('table')).toBeNull();
    expect(mensajes('status')).toEqual([]);
    expect(mensajes('alert')).toEqual([]);

    elegir(planilla());
    actualizar();

    // AC-02, AC-08, AC-12 en pantalla.
    expect(await screen.findByText('Se actualizaron 4 artículos de 6 filas.')).toBeTruthy();
    expect(mensajes('status')).toEqual(['Se actualizaron 4 artículos de 6 filas.']);
    expect(tabla('Filas no aplicadas')).toEqual({
      encabezados: ['Fila', 'Código', 'Razón'],
      filas: [
        ['3', 'XYZ-9', 'código inexistente'],
        ['5', 'L-8', 'precio inválido'],
      ],
    });
    expect(tabla('Para revisar')).toEqual({
      encabezados: ['Fila', 'Código', 'Precio de costo ($)', 'Indicaciones'],
      filas: [
        ['2', 'ABC-1', '-100,00', 'actualizado con precio negativo o cero'],
        ['4', 'AC-9', '2.420,50', 'inactivo'],
        ['6', 'M-1', '0,00', 'actualizado con precio negativo o cero, inactivo'],
      ],
    });
    expect(servidor.pedidosA('POST', RUTA_API)).toHaveLength(1);
    expect(mensajes('alert')).toEqual([]);
    // Terminó: el botón vuelve a su texto y queda habilitado para otra carga.
    expect(boton().textContent).toBe(BOTON);
    expect(boton().disabled).toBe(false);
  });

  it('sin listas no muestra las tablas', async () => {
    servidorFalso({
      ...conSesion,
      [CLAVE_API]: json(200, { filasLeidas: 2, aplicadas: 2, noAplicadas: [], revisar: [] }),
    });
    await abrir();
    elegir(planilla());
    actualizar();

    expect(await screen.findByText('Se actualizaron 2 artículos de 2 filas.')).toBeTruthy();
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('sin archivo el botón queda deshabilitado (faltante)', async () => {
    const servidor = servidorFalso(conSesion);
    await abrir();

    expect(boton().disabled).toBe(true);
    actualizar();
    // Ni enviando el formulario de otra forma (Enter): sin archivo no hay pedido.
    fireEvent.submit(campo().form!);
    expect(servidor.pedidosA('POST', RUTA_API)).toEqual([]);
    expect(mensajes('alert')).toEqual([]);

    // Elegir y después quitar el archivo lo vuelve a deshabilitar.
    elegir(planilla());
    expect(boton().disabled).toBe(false);
    fireEvent.change(campo(), { target: { files: [] } });
    expect(boton().disabled).toBe(true);
  });

  it('el campo acepta solo .xlsx, tiene su etiqueta y la ayuda de la columna A', async () => {
    servidorFalso(conSesion);
    await abrir();

    expect(campo().type).toBe('file');
    expect(campo().accept).toBe('.xlsx');
    expect(campo().multiple).toBe(false);
    const ayuda = document.getElementById(campo().getAttribute('aria-describedby') ?? '');
    expect(ayuda?.textContent).toBe(AYUDA);
  });

  it('archivo de más de 10 MB rechazado sin enviar', async () => {
    const servidor = servidorFalso({ ...conSesion, [CLAVE_API]: json(200, RESULTADO) });
    await abrir();
    const grande = planilla();
    Object.defineProperty(grande, 'size', { value: 10 * 1024 * 1024 + 1 });

    elegir(grande);
    actualizar();

    await waitFor(() => expect(mensajes('alert')).toEqual([MENSAJE_GRANDE]));
    expect(servidor.pedidosA('POST', RUTA_API)).toEqual([]);
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('muestra el error de formato inválido de la Api', async () => {
    // AC-07 (no .xlsx), AC-04 y AC-05 (formato) y los límites: el texto de errors.planilla, tal cual.
    const servidor = servidorFalso({
      ...conSesion,
      [CLAVE_API]: [
        problema(400, 'planilla', MENSAJE_NO_ES_XLSX),
        problema(400, 'planilla', MENSAJE_FORMATO),
        problema(400, 'planilla', MENSAJE_LIMITES),
      ],
    });
    await abrir();
    elegir(planilla('costos.csv'));

    for (const mensaje of [MENSAJE_NO_ES_XLSX, MENSAJE_FORMATO, MENSAJE_LIMITES]) {
      actualizar();
      await waitFor(() => expect(mensajes('alert')).toEqual([mensaje]));
      expect(screen.queryByRole('table')).toBeNull();
      expect(mensajes('status')).toEqual([]);
    }
    expect(servidor.pedidosA('POST', RUTA_API)).toHaveLength(3);
  });

  it('muestra el error de conflicto 409', async () => {
    const servidor = servidorFalso({
      ...conSesion,
      [CLAVE_API]: [
        json(409, { status: 409, motivo: 'cambiado', detail: MENSAJE_CAMBIADA }),
        json(200, RESULTADO),
      ],
    });
    await abrir();
    elegir(planilla());
    actualizar();

    await waitFor(() => expect(mensajes('alert')).toEqual([MENSAJE_CAMBIADA]));
    expect(screen.queryByRole('table')).toBeNull();

    // La operadora puede reintentar con el mismo archivo: el aviso se va y aparece el resultado.
    expect(boton().disabled).toBe(false);
    actualizar();
    expect(await screen.findByText('Se actualizaron 4 artículos de 6 filas.')).toBeTruthy();
    expect(mensajes('alert')).toEqual([]);
    expect(servidor.pedidosA('POST', RUTA_API)).toHaveLength(2);
  });

  it('muestra el aviso de otra planilla en proceso (429)', async () => {
    await rechazada(vacia(429));

    expect(mensajes('alert')).toEqual([MENSAJE_OCUPADA]);
  });

  it('muestra el error de la Api por una planilla de más de 10 MB (413)', async () => {
    await rechazada(vacia(413));

    expect(mensajes('alert')).toEqual([MENSAJE_GRANDE]);
  });

  it('muestra el error de red', async () => {
    await rechazada(sinRed);

    expect(mensajes('alert')).toEqual([MENSAJE_SIN_CONEXION]);
  });

  it('elegir otro archivo quita el resultado y el error anteriores', async () => {
    servidorFalso({ ...conSesion, [CLAVE_API]: [json(200, RESULTADO), vacia(429)] });
    await abrir();
    elegir(planilla());
    actualizar();
    await screen.findByText('Se actualizaron 4 artículos de 6 filas.');

    elegir(planilla('otra.xlsx'));
    expect(screen.queryByRole('table')).toBeNull();
    expect(mensajes('status')).toEqual([]);

    actualizar();
    await waitFor(() => expect(mensajes('alert')).toEqual([MENSAJE_OCUPADA]));
    elegir(planilla('tercera.xlsx'));
    expect(mensajes('alert')).toEqual([]);
  });

  it('mientras procesa el botón dice Procesando… y queda deshabilitado', async () => {
    let liberar: () => void = () => {};
    const llegada = new Promise<void>((resolver) => {
      liberar = resolver;
    });
    const servidor = servidorFalso({
      ...conSesion,
      [CLAVE_API]: async () => {
        await llegada;
        return json(200, RESULTADO)();
      },
    });
    await abrir();
    elegir(planilla());
    actualizar();

    await waitFor(() => expect(boton().textContent).toBe(PROCESANDO));
    expect(boton().disabled).toBe(true);
    expect(campo().disabled).toBe(true);
    expect(screen.getByRole('form', { name: TITULO }).getAttribute('aria-busy')).toBe('true');
    // Un segundo clic, o enviar el formulario de otra forma, no envía otra vez.
    actualizar();
    fireEvent.submit(campo().form!);

    await act(async () => liberar());

    expect(await screen.findByText('Se actualizaron 4 artículos de 6 filas.')).toBeTruthy();
    expect(screen.getByRole('form', { name: TITULO }).getAttribute('aria-busy')).toBe('false');
    expect(servidor.pedidosA('POST', RUTA_API)).toHaveLength(1);
  });

  it('un código con HTML se muestra como texto', async () => {
    // R-08: todo por JSX; ni el código de una fila no aplicada ni el de una para revisar se interpretan.
    const malicioso = '<img src=x onerror="window.__inyectado=1">';
    const conScript = '<script>window.__inyectado=1</script>';
    servidorFalso({
      ...conSesion,
      [CLAVE_API]: json(200, {
        filasLeidas: 2,
        aplicadas: 1,
        noAplicadas: [{ fila: 2, codigo: malicioso, razon: 'código inexistente' }],
        revisar: [{ fila: 3, codigo: conScript, precioCosto: 10, indicaciones: ['inactivo'] }],
      }),
    });
    await abrir();
    elegir(planilla());
    actualizar();
    await screen.findByText('Se actualizaron 1 artículos de 2 filas.');

    expect(tabla('Filas no aplicadas').filas).toEqual([['2', malicioso, 'código inexistente']]);
    expect(tabla('Para revisar').filas).toEqual([['3', conScript, '10,00', 'inactivo']]);
    expect(pantalla().querySelector('img')).toBeNull();
    expect(pantalla().querySelector('script')).toBeNull();
    expect((window as unknown as { __inyectado?: number }).__inyectado).toBeUndefined();
  });

  it('sin sesión al abrir vuelve al ingreso', async () => {
    servidorFalso({ 'GET /api/sesion': vacia(401), 'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }) });
    renderizarApp(RUTA);

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
  });

  it('sin sesión al actualizar vuelve al ingreso', async () => {
    servidorFalso({
      ...conSesion,
      [CLAVE_API]: vacia(401),
      'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }),
    });
    await abrir();
    elegir(planilla());
    actualizar();

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
  });

  it('muestra solo las primeras 500 filas de cada tabla con el aviso (1.000 filas)', async () => {
    // D-26: una planilla de 50.000 filas congelaría la pantalla.
    const noAplicadas = Array.from({ length: 1000 }, (_, i) => ({
      fila: i + 2,
      codigo: `NA-${i + 2}`,
      razon: 'código inexistente',
    }));
    const revisar = Array.from({ length: 1000 }, (_, i) => ({
      fila: i + 1002,
      codigo: `RV-${i + 1002}`,
      precioCosto: 0,
      indicaciones: ['actualizado con precio negativo o cero'],
    }));
    servidorFalso({
      ...conSesion,
      [CLAVE_API]: json(200, { filasLeidas: 2000, aplicadas: 1000, noAplicadas, revisar }),
    });
    await abrir();
    elegir(planilla());
    actualizar();
    await screen.findByText('Se actualizaron 1000 artículos de 2000 filas.');

    const noAplicadasVisibles = tabla('Filas no aplicadas').filas;
    expect(noAplicadasVisibles).toHaveLength(500);
    expect(noAplicadasVisibles[0]).toEqual(['2', 'NA-2', 'código inexistente']);
    expect(noAplicadasVisibles[499]).toEqual(['501', 'NA-501', 'código inexistente']);
    const revisarVisibles = tabla('Para revisar').filas;
    expect(revisarVisibles).toHaveLength(500);
    expect(revisarVisibles[499]?.[1]).toBe('RV-1501');
    expect(screen.getAllByText('Se muestran 500 de 1000 filas. Corregí la planilla y volvé a cargarla.')).toHaveLength(2);
    // Pinta y recorre 1.000 filas de tabla: con la cobertura activa supera los 5 s por defecto en WSL.
  }, 30_000);

  it('con 500 filas o menos no muestra el aviso del tope', async () => {
    servidorFalso({ ...conSesion, [CLAVE_API]: json(200, RESULTADO) });
    await abrir();
    elegir(planilla());
    actualizar();
    await screen.findByText('Se actualizaron 4 artículos de 6 filas.');

    expect(screen.queryByText(/^Se muestran/)).toBeNull();
  });

  it('un resultado ilegible muestra que la planilla se procesó', async () => {
    await rechazada(json(200, { ...RESULTADO, filasLeidas: 99 }));

    expect(mensajes('alert')).toEqual([
      'La planilla se procesó, pero no se pudo mostrar el resultado. Revisá los costos en el listado.',
    ]);
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('dos envíos en el mismo instante hacen un solo pedido', async () => {
    const servidor = servidorFalso({ ...conSesion, [CLAVE_API]: json(200, RESULTADO) });
    await abrir();
    elegir(planilla());
    const formulario = campo().form!;

    // Los dos en el mismo lote de React: el estado "procesando" todavía no se volvió a pintar.
    act(() => {
      formulario.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      formulario.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });

    expect(await screen.findByText('Se actualizaron 4 artículos de 6 filas.')).toBeTruthy();
    expect(servidor.pedidosA('POST', RUTA_API)).toHaveLength(1);
    // Terminado el pedido, se puede volver a cargar.
    actualizar();
    await waitFor(() => expect(servidor.pedidosA('POST', RUTA_API)).toHaveLength(2));
  });

  it('el rechazo 400 marca el campo inválido y lo describe con el error', async () => {
    servidorFalso({
      ...conSesion,
      [CLAVE_API]: [problema(400, 'planilla', MENSAJE_FORMATO), vacia(429)],
    });
    await abrir();
    expect(campo().getAttribute('aria-invalid')).toBe('false');
    elegir(planilla());
    actualizar();

    await waitFor(() => expect(campo().getAttribute('aria-invalid')).toBe('true'));
    const descripciones = (campo().getAttribute('aria-describedby') ?? '')
      .split(' ')
      .map((id) => document.getElementById(id)?.textContent);
    expect(descripciones).toEqual([AYUDA, MENSAJE_FORMATO]);

    // Un rechazo que no es del archivo (otra planilla en proceso) no marca el campo.
    actualizar();
    await waitFor(() => expect(mensajes('alert')).toEqual([MENSAJE_OCUPADA]));
    expect(campo().getAttribute('aria-invalid')).toBe('false');
    expect(campo().getAttribute('aria-describedby')?.split(' ')).toHaveLength(1);
  });

  it('si no se puede verificar la sesión muestra el aviso y deja cargar', async () => {
    servidorFalso({ 'GET /api/sesion': sinRed, [CLAVE_API]: json(200, RESULTADO) });
    await abrir();

    expect(mensajes('alert')).toEqual([MENSAJE_SIN_CONEXION]);
    elegir(planilla());
    expect(mensajes('alert')).toEqual([]);
    actualizar();
    expect(await screen.findByText('Se actualizaron 4 artículos de 6 filas.')).toBeTruthy();
  });

  it('ActualizarCostos_Cargando_ConElAnchoDeLaPantalla', async () => {
    // FEAT-012 (W13 de la revisión): mientras verifica la sesión, "Cargando…" lleva `pantalla--ancha`, así el marco ya
    // tiene el ancho de la pantalla que viene y no salta al terminar.
    let liberar: () => void = () => {};
    const llegada = new Promise<void>((resolver) => {
      liberar = resolver;
    });
    servidorFalso({
      'GET /api/sesion': async () => {
        await llegada;
        return vacia(204)();
      },
    });
    renderizarApp(RUTA);

    const cargando = await screen.findByText('Cargando…');
    expect(cargando.classList.contains('cargando')).toBe(true);
    expect(cargando.classList.contains('pantalla--ancha')).toBe(true);
    await act(async () => liberar());
    expect(await screen.findByRole('heading', { level: 1 })).toBeTruthy();
  });

  it('salir antes de verificar la sesión no cambia nada', async () => {
    let liberar: () => void = () => {};
    const llegada = new Promise<void>((resolver) => {
      liberar = resolver;
    });
    servidorFalso({
      'GET /api/sesion': async () => {
        await llegada;
        return vacia(204)();
      },
    });
    renderizarApp(RUTA);
    expect(await screen.findByText('Cargando…')).toBeTruthy();

    cleanup();
    await act(async () => liberar());

    expect(screen.queryByRole('heading')).toBeNull();
  });

  it('salir mientras procesa no muestra el resultado en otra pantalla', async () => {
    let liberar: () => void = () => {};
    const llegada = new Promise<void>((resolver) => {
      liberar = resolver;
    });
    servidorFalso({
      ...conSesion,
      [CLAVE_API]: async () => {
        await llegada;
        return json(200, RESULTADO)();
      },
      'GET /api/articulos?pagina=1': json(200, { articulos: [], pagina: 1, tamanoPagina: 50, total: 0 }),
    });
    await abrir();
    elegir(planilla());
    actualizar();
    await waitFor(() => expect(boton().textContent).toBe(PROCESANDO));

    fireEvent.click(screen.getByRole('link', { name: 'Volver a artículos' }));
    expect(await screen.findByRole('heading', { name: 'Artículos' })).toBeTruthy();
    await act(async () => liberar());

    expect(screen.queryByText('Se actualizaron 4 artículos de 6 filas.')).toBeNull();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/articulos');
  });

  it('tiene el enlace para volver al listado de artículos', async () => {
    servidorFalso(conSesion);
    await abrir();

    expect(screen.getByRole('link', { name: 'Volver a artículos' }).getAttribute('href')).toBe('/articulos');
  });
});
