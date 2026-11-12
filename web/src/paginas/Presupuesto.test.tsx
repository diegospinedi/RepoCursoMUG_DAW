import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { json, renderizarApp, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';

afterEach(() => {
  try {
    // D-17: nada del presupuesto queda en el almacenamiento del navegador.
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  } finally {
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
});

const TITULO = 'Presupuesto';
const NO_ENCONTRADO = 'No se encontró el presupuesto.';
const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';
const CONFIRMACION = 'Presupuesto N.º 155 grabado. Estado: Borrador.';

/** Datos ficticios (D-17). */
const CLIENTE = {
  apellido: 'Pérez',
  nombre: 'Ana',
  // Claramente ficticio, tal como queda guardado (sin puntos).
  dni: '99999999',
  domicilio: 'Calle Falsa 123',
  email: 'ana@mail.com',
  telefono: '11 5555-0000',
};
const LINEA = {
  codigoArticulo: 7,
  descripcion: 'Armazón metálico',
  precioUnitario: 1850,
  cantidad: 1,
  porcentajeDescuento: 0,
  precioConDescuento: 1850,
  precioFinal: 1850,
  orden: 1,
};
const GRABADO = {
  numero: 155,
  fecha: '2026-03-10',
  estado: 'borrador',
  cliente: CLIENTE,
  lineas: [LINEA],
  total: 1850,
  version: 3,
};

/** Abre el presupuesto 155 con la respuesta indicada (por defecto, el grabado) y espera que se muestre. */
async function abrir(cuerpo: unknown = GRABADO, estado?: unknown) {
  const registro = servidorFalso({ 'GET /api/presupuestos/155': json(200, cuerpo) });
  renderizarApp(estado === undefined ? '/presupuestos/155' : { pathname: '/presupuestos/155', state: estado });
  await screen.findByRole('heading', { name: TITULO });
  await screen.findByText('Número');
  return registro;
}

/** El valor que la pantalla muestra junto a la etiqueta indicada (`<dt>` y su `<dd>`). */
function dato(etiqueta: string): string | null {
  const termino = screen.getByText(etiqueta, { selector: 'dt' });
  return termino.nextElementSibling?.textContent ?? null;
}

const pantalla = () => screen.getByRole('heading', { name: TITULO }).closest('section')!;

const CONFIRMAR_FINAL = 'Una vez en Final, el presupuesto no se puede modificar.';
const PASADO_A_FINAL = 'Presupuesto N.º 155 pasado a Final.';
const MENSAJE_CAMBIADO = 'El presupuesto cambió mientras lo editabas. Volvé a abrirlo.';
const MENSAJE_FINAL = 'El presupuesto está en Final y no se puede modificar.';

/** Un 409 de la Api con un `detail` que la pantalla nunca muestra (D-07). */
const conflicto409 = (motivo: string) =>
  json(409, { title: 'No se pudo grabar el presupuesto.', detail: 'Texto del servidor.', motivo });

/** Pulsa "Pasar a Final" en la pantalla y confirma en el diálogo. */
async function confirmarFinal() {
  fireEvent.click(within(pantalla()).getByRole('button', { name: 'Pasar a Final' }));
  const dialogo = await screen.findByRole('dialog');
  fireEvent.click(within(dialogo).getByRole('button', { name: 'Pasar a Final' }));
}

describe('Presupuesto', () => {
  it.each(['abc', '0', '007', '-1', '1.5', '9007199254740992'])(
    'Presupuesto_NumeroInvalidoO404_NoEncontrado (ruta %s)',
    async (numero) => {
      const registro = servidorFalso({});
      renderizarApp(`/presupuestos/${numero}`);

      expect(await screen.findByText(NO_ENCONTRADO)).toBeTruthy();
      expect(registro.llamadas.filter((l) => l.ruta.startsWith('/api/presupuestos'))).toEqual([]);
      expect(screen.queryByText('Número')).toBeNull();
    },
  );

  it('Presupuesto_NumeroInvalidoO404_NoEncontrado (404)', async () => {
    const registro = servidorFalso({ 'GET /api/presupuestos/9': vacia(404) });
    renderizarApp('/presupuestos/9');

    expect(await screen.findByText(NO_ENCONTRADO)).toBeTruthy();
    expect(registro.pedidosA('GET', '/api/presupuestos/9')).toHaveLength(1);
    expect(screen.getByRole('heading', { name: TITULO })).toBeTruthy();
  });

  it('Presupuesto_MuestraNumero155', async () => {
    // AC-09: el número que devuelve la Api.
    await abrir();

    expect(dato('Número')).toBe('155');
  });

  it('Presupuesto_MuestraFecha10_03_2026', async () => {
    // AC-19: dd/mm/aaaa, sin pasar por la zona horaria del navegador.
    await abrir();

    expect(dato('Fecha')).toBe('10/03/2026');
  });

  // El nombre conserva el número con que lo rastrean la spec y el informe de FEAT-003a; el dato es ficticio.
  it('Presupuesto_MuestraDni23456789', async () => {
    // AC-05: el DNI tal como quedó guardado, sin puntos.
    await abrir();

    expect(dato('DNI')).toBe('99999999');
  });

  it('Presupuesto_MuestraElEstadoYLosSeisDatos', async () => {
    // AC-01 y AC-12.
    await abrir();

    expect(dato('Estado')).toBe('Borrador');
    expect(dato('Apellido')).toBe('Pérez');
    expect(dato('Nombre')).toBe('Ana');
    expect(dato('Domicilio')).toBe('Calle Falsa 123');
    expect(dato('Email')).toBe('ana@mail.com');
    expect(dato('Nro. de Teléfono')).toBe('11 5555-0000');
    // Solo lectura: ningún campo; la edición va por "Modificar" y el único botón es "Pasar a Final" (FEAT-003c).
    expect(within(pantalla()).queryAllByRole('textbox')).toEqual([]);
    expect(within(pantalla()).queryAllByRole('button').map((b) => b.textContent)).toEqual(['Pasar a Final']);
  });

  it('Presupuesto_SinOpcionales_LosMuestraVacios', async () => {
    await abrir({ ...GRABADO, estado: 'final', cliente: { ...CLIENTE, domicilio: null, email: null, telefono: null } });

    expect(dato('Estado')).toBe('Final');
    expect(dato('Domicilio')).toBe('—');
    expect(dato('Email')).toBe('—');
    expect(dato('Nro. de Teléfono')).toBe('—');
  });

  it('Presupuesto_Linea_MuestraLasSieteColumnas', async () => {
    // AC-17 y AC-18: las siete columnas de FR-13, con importes con 2 decimales (NFR-02).
    await abrir({ ...GRABADO, lineas: [LINEA, { ...LINEA, codigoArticulo: 8, descripcion: 'Lente', precioUnitario: 1234567.5, precioConDescuento: 1234567.5, precioFinal: 1234567.5 }], total: 1236417.5 });

    const tabla = screen.getByRole('table', { name: 'Líneas del presupuesto' });
    expect(within(tabla).getAllByRole('columnheader').map((c) => c.textContent)).toEqual([
      'Código de Artículo',
      'Descripción',
      'Precio Unitario ($)',
      'Cantidad',
      'Porcentaje de Descuento (%)',
      'Precio con Descuento ($)',
      'Precio Final ($)',
    ]);
    const filas = within(tabla.querySelector('tbody')!).getAllByRole('row');
    expect(filas.map((f) => within(f).getAllByRole('cell').map((c) => c.textContent))).toEqual([
      ['7', 'Armazón metálico', '1.850,00', '1', '0', '1.850,00', '1.850,00'],
      ['8', 'Lente', '1.234.567,50', '1', '0', '1.234.567,50', '1.234.567,50'],
    ]);
  });

  it('presupuesto grabado muestra el total sin discriminar IVA', async () => {
    // AC-08 y FR-08: cantidad, descuento, precios y total tal como los devolvió el servidor (ADR-016: no recalcula),
    // con IVA incluido y sin columna ni fila de IVA. AC-06 y AC-07: $10,02 + $2.700,00 = $2.710,02.
    await abrir({
      ...GRABADO,
      lineas: [
        { ...LINEA, codigoArticulo: 15, descripcion: 'Paño de limpieza', precioUnitario: 6.67, cantidad: 3, porcentajeDescuento: 50, precioConDescuento: 3.34, precioFinal: 10.02 },
        // Un Precio Final que no es la cuenta de la pantalla (900 × 3): se muestra el del servidor, sin recalcular.
        { ...LINEA, codigoArticulo: 12, descripcion: 'Lente orgánico', precioUnitario: 1000, cantidad: 3, porcentajeDescuento: 10.5, precioConDescuento: 900, precioFinal: 2700.01 },
      ],
      total: 2710.03,
    });

    const tabla = screen.getByRole('table', { name: 'Líneas del presupuesto' });
    const filas = within(tabla.querySelector('tbody')!).getAllByRole('row');
    expect(filas.map((f) => within(f).getAllByRole('cell').map((c) => c.textContent))).toEqual([
      ['15', 'Paño de limpieza', '6,67', '3', '50', '3,34', '10,02'],
      ['12', 'Lente orgánico', '1.000,00', '3', '10,5', '900,00', '2.700,01'],
    ]);
    const encabezado = within(tabla).getByRole('rowheader', { name: 'Total (IVA incluido)' });
    expect(encabezado.closest('tfoot')).not.toBeNull();
    expect(within(encabezado.closest('tr')!).getAllByRole('cell').map((c) => c.textContent)).toEqual(['2.710,03']);
    // Sin discriminar IVA: ningún encabezado ni fila de IVA aparte.
    const encabezados = [...within(tabla).getAllByRole('columnheader'), ...within(tabla).getAllByRole('rowheader')];
    expect(encabezados.map((c) => c.textContent).filter((t) => t !== 'Total (IVA incluido)' && /IVA/i.test(t ?? ''))).toEqual([]);
    expect(within(tabla).getAllByRole('rowheader')).toHaveLength(1);
  });

  it('Presupuesto_ConLaConfirmacionDelAlta_LaMuestra', async () => {
    await abrir(GRABADO, { mensaje: CONFIRMACION });

    expect(within(pantalla()).getByRole('status').textContent).toBe(CONFIRMACION);
  });

  it.each([
    ['sin estado', undefined],
    ['mensaje que no es texto', { mensaje: 1 }],
    ['estado que no es objeto', CONFIRMACION],
    ['sin mensaje', { otro: CONFIRMACION }],
  ])('Presupuesto_EstadoDeNavegacionAjeno_%s_SinConfirmacion', async (_caso, estado) => {
    await abrir(GRABADO, estado ?? null);

    expect(within(pantalla()).queryByRole('status')).toBeNull();
  });

  it('Presupuesto_RespuestaIlegible_MensajeGeneralSinDatos', async () => {
    servidorFalso({ 'GET /api/presupuestos/155': json(200, { ...GRABADO, fecha: '10/03/2026' }) });
    renderizarApp('/presupuestos/155');

    expect(await screen.findByText(MENSAJE_SIN_CONEXION)).toBeTruthy();
    expect(screen.queryByText('Pérez')).toBeNull();
  });

  it('Presupuesto_SinSesion_VuelveAlIngreso', async () => {
    servidorFalso({ 'GET /api/presupuestos/155': vacia(401) });
    renderizarApp('/presupuestos/155');

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    await waitFor(() => expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso'));
  });

  it('Presupuesto_Borrador_EnlaceModificar', async () => {
    await abrir();

    expect(screen.getByRole('link', { name: 'Modificar' }).getAttribute('href')).toBe('/presupuestos/155/editar');
  });

  it('Presupuesto_PasarAFinal_PideConfirmacion', async () => {
    // AC-05: antes de grabar, el diálogo con el texto de D-07 y los dos botones; el foco queda dentro del diálogo.
    const registro = await abrir();
    expect(screen.queryByRole('dialog')).toBeNull();

    fireEvent.click(within(pantalla()).getByRole('button', { name: 'Pasar a Final' }));

    // Un título breve visible nombra el diálogo y el texto de D-07 lo describe.
    const dialogo = await screen.findByRole('dialog', { name: 'Pasar a Final', description: CONFIRMAR_FINAL });
    expect(within(dialogo).getByRole('heading', { name: 'Pasar a Final' })).toBeTruthy();
    expect(within(dialogo).getByText(CONFIRMAR_FINAL)).toBeTruthy();
    expect(within(dialogo).getAllByRole('button').map((b) => b.textContent)).toEqual(['Pasar a Final', 'Cancelar']);
    expect(dialogo.contains(document.activeElement)).toBe(true);
    expect(registro.pedidosA('PUT', '/api/presupuestos/155/estado')).toEqual([]);
  });

  it('Presupuesto_ConfirmarFinal_GrabaFinal', async () => {
    // AC-04: confirma, graba Final con la versión mostrada y la pantalla queda en Final, sin acciones de edición.
    const registro = servidorFalso({
      'GET /api/presupuestos/155': json(200, GRABADO),
      'PUT /api/presupuestos/155/estado': json(200, { ...GRABADO, estado: 'final', version: 4 }),
    });
    renderizarApp('/presupuestos/155');
    await screen.findByText('Número');

    await confirmarFinal();

    const aviso = await screen.findByText(PASADO_A_FINAL);
    expect(aviso.getAttribute('role')).toBe('status');
    const pedidos = registro.pedidosA('PUT', '/api/presupuestos/155/estado');
    expect(pedidos).toHaveLength(1);
    expect(JSON.parse(String(pedidos[0]?.init?.body))).toEqual({ estado: 'final', version: 3 });
    expect(dato('Estado')).toBe('Final');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(within(pantalla()).queryAllByRole('button')).toEqual([]);
    expect(screen.queryByRole('link', { name: 'Modificar' })).toBeNull();
    // El botón que tenía el foco ya no existe: el foco va al aviso.
    expect(document.activeElement).toBe(aviso);
  });

  it.each(['Cancelar', 'Escape', 'cancel'])('Presupuesto_CancelarFinal_NoLlamaALaApi (%s)', async (forma) => {
    // AC-06: cerrar sin confirmar no llama a la Api, el presupuesto sigue en Borrador y el foco vuelve al botón.
    const registro = await abrir();
    const boton = within(pantalla()).getByRole('button', { name: 'Pasar a Final' });
    fireEvent.click(boton);
    const dialogo = await screen.findByRole('dialog');

    if (forma === 'Cancelar') {
      fireEvent.click(within(dialogo).getByRole('button', { name: 'Cancelar' }));
    } else if (forma === 'Escape') {
      fireEvent.keyDown(dialogo, { key: 'Escape' });
    } else {
      // El evento que dispara el navegador al pulsar Escape en un diálogo modal.
      fireEvent(dialogo, new Event('cancel', { cancelable: true }));
    }

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(document.activeElement).toBe(boton);
    expect(registro.llamadas.filter((l) => l.metodo === 'PUT')).toEqual([]);
    expect(dato('Estado')).toBe('Borrador');
  });

  it('Presupuesto_DialogoOtraTecla_NoLoCierra', async () => {
    await abrir();
    fireEvent.click(within(pantalla()).getByRole('button', { name: 'Pasar a Final' }));
    const dialogo = await screen.findByRole('dialog');

    fireEvent.keyDown(dialogo, { key: 'Enter' });

    expect(screen.getByRole('dialog')).toBe(dialogo);
  });

  it('Presupuesto_Final_SinAccionesDeEdicion', async () => {
    // AC-07: un Final no ofrece modificar ni pasar a Final. Control: el mismo presupuesto en Borrador sí las ofrece,
    // así la ausencia en Final depende del estado y no de que la pantalla no tenga acciones.
    await abrir();
    expect(screen.getByRole('link', { name: 'Modificar' })).toBeTruthy();
    expect(within(pantalla()).getByRole('button', { name: 'Pasar a Final' })).toBeTruthy();
    cleanup();

    await abrir({ ...GRABADO, estado: 'final' });

    expect(dato('Estado')).toBe('Final');
    expect(within(pantalla()).queryAllByRole('button')).toEqual([]);
    expect(screen.queryByRole('link', { name: 'Modificar' })).toBeNull();
  });

  it('Presupuesto_ConfirmarFinal_409final_MensajePropioYRecargaEnFinal', async () => {
    // D-07: el texto propio, nunca el del servidor. Otra sesión ya lo pasó a Final: la pantalla lo vuelve a leer y
    // queda en Final, sin acciones de edición.
    const registro = servidorFalso({
      'GET /api/presupuestos/155': [json(200, GRABADO), json(200, { ...GRABADO, estado: 'final', version: 4 })],
      'PUT /api/presupuestos/155/estado': conflicto409('final'),
    });
    renderizarApp('/presupuestos/155');
    await screen.findByText('Número');

    await confirmarFinal();

    const alerta = await screen.findByText(MENSAJE_FINAL);
    expect(alerta.getAttribute('role')).toBe('alert');
    expect(screen.queryByText('Texto del servidor.')).toBeNull();
    expect(screen.queryByRole('dialog')).toBeNull();
    await waitFor(() => expect(dato('Estado')).toBe('Final'));
    expect(registro.pedidosA('GET', '/api/presupuestos/155')).toHaveLength(2);
    expect(within(pantalla()).queryAllByRole('button')).toEqual([]);
    expect(screen.queryByRole('link', { name: 'Modificar' })).toBeNull();
    expect(screen.queryByText(PASADO_A_FINAL)).toBeNull();
    // El botón ya no está: el foco va al mensaje, que sigue en pantalla después de recargar.
    expect(document.activeElement).toBe(screen.getByText(MENSAJE_FINAL));
  });

  it('Presupuesto_ConfirmarFinal_409cambiado_MensajePropioYRecargaLaVersion', async () => {
    // D-01: otra sesión lo modificó; la pantalla relee el presupuesto y el siguiente paso a Final usa la versión nueva.
    const registro = servidorFalso({
      'GET /api/presupuestos/155': [json(200, GRABADO), json(200, { ...GRABADO, version: 5 })],
      'PUT /api/presupuestos/155/estado': [conflicto409('cambiado'), json(200, { ...GRABADO, estado: 'final', version: 6 })],
    });
    renderizarApp('/presupuestos/155');
    await screen.findByText('Número');

    await confirmarFinal();

    const alerta = await screen.findByText(MENSAJE_CAMBIADO);
    expect(alerta.getAttribute('role')).toBe('alert');
    expect(screen.queryByText('Texto del servidor.')).toBeNull();
    await waitFor(() => expect(registro.pedidosA('GET', '/api/presupuestos/155')).toHaveLength(2));
    expect(dato('Estado')).toBe('Borrador');

    await confirmarFinal();

    expect(await screen.findByText(PASADO_A_FINAL)).toBeTruthy();
    const cuerpos = registro.pedidosA('PUT', '/api/presupuestos/155/estado').map((p) => JSON.parse(String(p.init?.body)));
    expect(cuerpos).toEqual([
      { estado: 'final', version: 3 },
      { estado: 'final', version: 5 },
    ]);
  });

  it('Presupuesto_ConfirmarFinal_SinRed_MensajeYFocoAlBoton', async () => {
    servidorFalso({ 'GET /api/presupuestos/155': json(200, GRABADO), 'PUT /api/presupuestos/155/estado': sinRed });
    renderizarApp('/presupuestos/155');
    await screen.findByText('Número');
    const boton = within(pantalla()).getByRole('button', { name: 'Pasar a Final' });

    await confirmarFinal();

    expect(await screen.findByText(MENSAJE_SIN_CONEXION)).toBeTruthy();
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.activeElement).toBe(boton);
    expect(dato('Estado')).toBe('Borrador');
  });

  it('Presupuesto_ConfirmarFinal_RespuestaDeOtroNumero_NoPisaLaPantalla', async () => {
    // Mientras el pedido del 155 está en curso la persona abre el 156: la respuesta del 155 ya no corresponde.
    let responder: (respuesta: Response) => void = () => {};
    const pendiente = new Promise<Response>((resolver) => {
      responder = resolver;
    });
    servidorFalso({
      'GET /api/presupuestos/155': json(200, GRABADO),
      'GET /api/presupuestos/156': json(200, { ...GRABADO, numero: 156 }),
      'PUT /api/presupuestos/155/estado': () => pendiente,
    });
    const { volver } = renderizarApp(['/presupuestos/156', '/presupuestos/155']);
    await screen.findByText('Número');
    await confirmarFinal();

    await volver();
    await waitFor(() => expect(dato('Número')).toBe('156'));
    responder(new Response(JSON.stringify({ ...GRABADO, estado: 'final', version: 4 }), { status: 200 }));
    await pendiente;
    await new Promise((resolver) => setTimeout(resolver, 0));

    expect(dato('Número')).toBe('156');
    expect(dato('Estado')).toBe('Borrador');
    expect(screen.queryByText(PASADO_A_FINAL)).toBeNull();
    expect(within(pantalla()).getByRole('button', { name: 'Pasar a Final' })).toBeTruthy();
  });

  it.each(['aviso', 'diálogo'])(
    'Presupuesto_VolverAlMismoNumero_SinAvisoNiDialogoViejos (%s)',
    async (caso) => {
      // Atrás y adelante con la misma pantalla montada (155 → 156 → 155): lo del 155 anterior no reaparece.
      servidorFalso({
        'GET /api/presupuestos/155': json(200, GRABADO),
        'GET /api/presupuestos/156': json(200, { ...GRABADO, numero: 156 }),
        'PUT /api/presupuestos/155/estado': sinRed,
      });
      const { volver } = renderizarApp(['/presupuestos/155', '/presupuestos/156', '/presupuestos/155']);
      await screen.findByText('Número');
      if (caso === 'aviso') {
        await confirmarFinal();
        await screen.findByText(MENSAJE_SIN_CONEXION);
      } else {
        fireEvent.click(within(pantalla()).getByRole('button', { name: 'Pasar a Final' }));
        await screen.findByRole('dialog');
      }

      await volver();
      await waitFor(() => expect(dato('Número')).toBe('156'));
      await volver();
      await waitFor(() => expect(dato('Número')).toBe('155'));

      expect(screen.queryByText(MENSAJE_SIN_CONEXION)).toBeNull();
      expect(screen.queryByRole('dialog')).toBeNull();
    },
  );

  it('Presupuesto_ConfirmarFinal_MientrasEnvia_ElFocoQuedaEnElDialogo', async () => {
    // Mientras se envía, los botones del diálogo no se deshabilitan (el foco caería al body): quedan `aria-disabled`
    // y no hacen nada.
    let responder: (respuesta: Response) => void = () => {};
    const pendiente = new Promise<Response>((resolver) => {
      responder = resolver;
    });
    const registro = servidorFalso({
      'GET /api/presupuestos/155': json(200, GRABADO),
      'PUT /api/presupuestos/155/estado': () => pendiente,
    });
    renderizarApp('/presupuestos/155');
    await screen.findByText('Número');
    fireEvent.click(within(pantalla()).getByRole('button', { name: 'Pasar a Final' }));
    const dialogo = await screen.findByRole('dialog');
    const confirmar = within(dialogo).getByRole('button', { name: 'Pasar a Final' });
    confirmar.focus();

    fireEvent.click(confirmar);

    await waitFor(() => expect(confirmar.getAttribute('aria-disabled')).toBe('true'));
    const cancelar = within(dialogo).getByRole('button', { name: 'Cancelar' });
    expect(cancelar.getAttribute('aria-disabled')).toBe('true');
    expect(confirmar.hasAttribute('disabled')).toBe(false);
    expect(dialogo.contains(document.activeElement)).toBe(true);
    fireEvent.click(cancelar);
    fireEvent.click(confirmar);
    fireEvent.keyDown(dialogo, { key: 'Escape' });
    expect(screen.getByRole('dialog')).toBe(dialogo);
    expect(registro.pedidosA('PUT', '/api/presupuestos/155/estado')).toHaveLength(1);

    responder(new Response(JSON.stringify({ ...GRABADO, estado: 'final', version: 4 }), { status: 200 }));

    expect(await screen.findByText(PASADO_A_FINAL)).toBeTruthy();
  });

  it('Presupuesto_ConfirmarFinal_SinSesion_VuelveAlIngreso', async () => {
    servidorFalso({
      'GET /api/presupuestos/155': json(200, GRABADO),
      'PUT /api/presupuestos/155/estado': vacia(401),
    });
    renderizarApp('/presupuestos/155');
    await screen.findByText('Número');

    await confirmarFinal();

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    await waitFor(() => expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso'));
  });

  it('Presupuesto_DialogoNativo_AbreModalYCierra', async () => {
    // En el navegador el diálogo es modal (showModal) y se cierra con close(); jsdom no los implementa.
    const prototipo = HTMLDialogElement.prototype as unknown as Record<string, unknown>;
    const showModal = vi.fn(function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    });
    const close = vi.fn(function (this: HTMLDialogElement) {
      this.removeAttribute('open');
    });
    prototipo.showModal = showModal;
    prototipo.close = close;
    try {
      await abrir();
      fireEvent.click(within(pantalla()).getByRole('button', { name: 'Pasar a Final' }));
      const dialogo = await screen.findByRole('dialog');
      expect(showModal).toHaveBeenCalledOnce();
      expect(dialogo.hasAttribute('open')).toBe(true);

      fireEvent.click(within(dialogo).getByRole('button', { name: 'Cancelar' }));

      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
      expect(close).toHaveBeenCalled();
    } finally {
      delete prototipo.showModal;
      delete prototipo.close;
    }
  });

  it('Presupuesto_Enlaces_NuevoPresupuestoYVolverAlInicio', async () => {
    await abrir();

    expect(screen.getByRole('link', { name: 'Nuevo presupuesto' }).getAttribute('href')).toBe('/presupuestos/nuevo');
    expect(screen.getByRole('link', { name: 'Volver al inicio' }).getAttribute('href')).toBe('/');
  });
});
