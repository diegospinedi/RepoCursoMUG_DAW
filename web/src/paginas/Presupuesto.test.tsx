import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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
  // El GET suma la facturación (FEAT-007, D-19); los PUT no la llevan, y la pantalla ignora lo que sobra.
  factura: null,
  emisionPendiente: false,
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
    // Solo lectura: ningún campo; la edición va por "Modificar" y los botones son "Pasar a Final" (FEAT-003c) y
    // "Descargar PDF" (FEAT-006) y "Facturar" (FEAT-007), estos dos deshabilitados en Borrador.
    expect(within(pantalla()).queryAllByRole('textbox')).toEqual([]);
    expect(within(pantalla()).queryAllByRole('button').map((b) => b.textContent)).toEqual([
      'Pasar a Final',
      'Descargar PDF',
      'Facturar',
    ]);
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
    // En Final no hay acciones de edición: los botones son "Descargar PDF" (FEAT-006) y "Facturar" (FEAT-007).
    expect(within(pantalla()).queryAllByRole('button').map((b) => b.textContent)).toEqual(['Descargar PDF', 'Facturar']);
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
    // En Final no hay acciones de edición: los botones son "Descargar PDF" (FEAT-006) y "Facturar" (FEAT-007).
    expect(within(pantalla()).queryAllByRole('button').map((b) => b.textContent)).toEqual(['Descargar PDF', 'Facturar']);
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
    // En Final no hay acciones de edición: los botones son "Descargar PDF" (FEAT-006) y "Facturar" (FEAT-007).
    expect(within(pantalla()).queryAllByRole('button').map((b) => b.textContent)).toEqual(['Descargar PDF', 'Facturar']);
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

describe('Presupuesto: Descargar PDF (FEAT-006)', () => {
  const RUTA_PDF = '/api/presupuestos/155/pdf';
  const FINAL = { ...GRABADO, estado: 'final', version: 4 };
  const URL_DE_OBJETO = 'blob:prueba-pdf-155';
  const MENSAJE_BORRADOR = 'El presupuesto está en Borrador: pasalo a Final para descargar el PDF.';
  const BYTES_PDF = '%PDF-1.7\n';

  const pdf = () => () =>
    new Response(BYTES_PDF, {
      status: 200,
      headers: { 'Content-Type': 'application/pdf', 'Content-Disposition': 'attachment; filename=presupuesto-155.pdf' },
    });

  /** Lo que pasó con la URL de objeto y el enlace temporal, en orden (D-10). */
  let eventos: string[] = [];
  let enlaces: { descarga: string; href: string | null; enDocumento: boolean }[] = [];
  let blobs: Blob[] = [];

  // jsdom no implementa `URL.createObjectURL` ni `URL.revokeObjectURL`, ni la descarga de un enlace: se simulan.
  const urlEstatica = URL as unknown as Record<string, unknown>;
  beforeEach(() => {
    eventos = [];
    enlaces = [];
    blobs = [];
    urlEstatica.createObjectURL = vi.fn((blob: Blob) => {
      blobs.push(blob);
      eventos.push('crear');
      return URL_DE_OBJETO;
    });
    urlEstatica.revokeObjectURL = vi.fn((url: string) => {
      eventos.push(`revocar ${url}`);
    });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      eventos.push('clic');
      enlaces.push({ descarga: this.download, href: this.getAttribute('href'), enDocumento: this.isConnected });
    });
  });
  afterEach(() => {
    delete urlEstatica.createObjectURL;
    delete urlEstatica.revokeObjectURL;
  });

  const botonPdf = () => within(pantalla()).getByRole('button', { name: 'Descargar PDF' });

  /** Abre el presupuesto 155 con las respuestas indicadas y espera que se muestre. */
  async function abrirCon(rutas: Parameters<typeof servidorFalso>[0]) {
    const registro = servidorFalso(rutas);
    renderizarApp('/presupuestos/155');
    await screen.findByText('Número');
    return registro;
  }

  it('Descargar PDF habilitado en Final y descarga presupuesto-N.pdf', async () => {
    // AC-11 en pantalla: fetch, URL de objeto, enlace temporal con el nombre, clic y revocación (D-10).
    const registro = await abrirCon({ 'GET /api/presupuestos/155': json(200, FINAL), [`GET ${RUTA_PDF}`]: pdf() });
    expect(botonPdf().hasAttribute('disabled')).toBe(false);

    fireEvent.click(botonPdf());

    await waitFor(() => expect(eventos).toEqual(['crear', 'clic', `revocar ${URL_DE_OBJETO}`]));
    expect(registro.pedidosA('GET', RUTA_PDF)).toHaveLength(1);
    expect(await blobs[0]?.text()).toBe(BYTES_PDF);
    expect(enlaces).toEqual([{ descarga: 'presupuesto-155.pdf', href: URL_DE_OBJETO, enDocumento: true }]);
    // El enlace temporal no queda en la página, y el botón vuelve a habilitarse.
    expect(document.querySelector('a[download]')).toBeNull();
    await waitFor(() => expect(botonPdf().hasAttribute('disabled')).toBe(false));
    expect(within(pantalla()).queryByRole('alert')).toBeNull();
  });

  it('Descargar PDF deshabilitado en Borrador', async () => {
    // AC-12: en Borrador el botón está deshabilitado (atributo `disabled`) y no pide nada a la Api.
    const registro = await abrirCon({ 'GET /api/presupuestos/155': json(200, GRABADO), [`GET ${RUTA_PDF}`]: pdf() });

    expect(dato('Estado')).toBe('Borrador');
    expect(botonPdf().hasAttribute('disabled')).toBe(true);
    fireEvent.click(botonPdf());

    expect(registro.pedidosA('GET', RUTA_PDF)).toEqual([]);
    expect(eventos).toEqual([]);
  });

  it('Descargar PDF: mientras descarga no lanza otra', async () => {
    let responder: (respuesta: Response) => void = () => {};
    const pendiente = new Promise<Response>((resolver) => {
      responder = resolver;
    });
    const registro = await abrirCon({ 'GET /api/presupuestos/155': json(200, FINAL), [`GET ${RUTA_PDF}`]: () => pendiente });

    const boton = botonPdf();
    fireEvent.click(boton);
    // El botón sigue habilitado (solo `aria-disabled`, para no perder el foco): el segundo clic llega al manejador y
    // lo frena la guarda, sin otro pedido.
    await waitFor(() => expect(botonPdf().getAttribute('aria-disabled')).toBe('true'));
    expect(botonPdf().hasAttribute('disabled')).toBe(false);
    fireEvent.click(botonPdf());
    fireEvent.click(botonPdf());
    expect(registro.pedidosA('GET', RUTA_PDF)).toHaveLength(1);

    responder(pdf()());

    await waitFor(() => expect(eventos).toEqual(['crear', 'clic', `revocar ${URL_DE_OBJETO}`]));
    await waitFor(() => expect(botonPdf().hasAttribute('aria-disabled')).toBe(false));
    expect(registro.pedidosA('GET', RUTA_PDF)).toHaveLength(1);
  });

  it('Descargar PDF: el botón conserva el foco durante y después de la descarga', async () => {
    // WARN-1: con `disabled` nativo el navegador le quita el foco al botón mientras descarga; queda enfocable, con
    // `aria-disabled` y `aria-busy` mientras dura el pedido.
    let responder: (respuesta: Response) => void = () => {};
    const pendiente = new Promise<Response>((resolver) => {
      responder = resolver;
    });
    await abrirCon({ 'GET /api/presupuestos/155': json(200, FINAL), [`GET ${RUTA_PDF}`]: () => pendiente });
    const boton = botonPdf();
    boton.focus();
    expect(document.activeElement).toBe(boton);

    fireEvent.click(boton);

    await waitFor(() => expect(botonPdf().getAttribute('aria-busy')).toBe('true'));
    expect(botonPdf().getAttribute('aria-disabled')).toBe('true');
    expect(botonPdf().hasAttribute('disabled')).toBe(false);
    expect(document.activeElement).toBe(botonPdf());

    responder(pdf()());

    await waitFor(() => expect(eventos).toEqual(['crear', 'clic', `revocar ${URL_DE_OBJETO}`]));
    await waitFor(() => expect(botonPdf().hasAttribute('aria-busy')).toBe(false));
    expect(botonPdf().hasAttribute('aria-disabled')).toBe(false);
    expect(document.activeElement).toBe(botonPdf());
  });

  it('409 borrador: conflicto con mensaje propio y relectura', async () => {
    // La pantalla quedó vieja (decía Final) y la Api responde 409 `borrador` (D-11): el texto propio, nunca el del
    // servidor, y se vuelve a leer el presupuesto, que queda con el botón deshabilitado.
    const registro = await abrirCon({
      'GET /api/presupuestos/155': [json(200, FINAL), json(200, GRABADO)],
      [`GET ${RUTA_PDF}`]: json(409, {
        status: 409,
        title: 'No se pudo generar el PDF del presupuesto.',
        detail: 'El presupuesto está en Borrador.',
        motivo: 'borrador',
      }),
    });

    fireEvent.click(botonPdf());

    const alerta = await screen.findByText(MENSAJE_BORRADOR);
    expect(alerta.getAttribute('role')).toBe('alert');
    expect(screen.queryByText('El presupuesto está en Borrador.')).toBeNull();
    expect(screen.queryByText('No se pudo generar el PDF del presupuesto.')).toBeNull();
    await waitFor(() => expect(dato('Estado')).toBe('Borrador'));
    expect(registro.pedidosA('GET', '/api/presupuestos/155')).toHaveLength(2);
    expect(botonPdf().hasAttribute('disabled')).toBe(true);
    expect(eventos).toEqual([]);
    // El mensaje sigue en pantalla después de releer.
    expect(screen.getByText(MENSAJE_BORRADOR)).toBeTruthy();
  });

  it.each([
    ['falla de red', sinRed],
    ['500', vacia(500)],
  ])('error de red al descargar: mensaje fijo (%s)', async (_caso, respuesta) => {
    // Un mensaje fijo, sin archivo, y el botón vuelve a habilitarse para reintentar.
    const registro = await abrirCon({
      'GET /api/presupuestos/155': json(200, FINAL),
      [`GET ${RUTA_PDF}`]: [respuesta, pdf()],
    });

    fireEvent.click(botonPdf());

    const alerta = await screen.findByText(MENSAJE_SIN_CONEXION);
    expect(alerta.getAttribute('role')).toBe('alert');
    expect(eventos).toEqual([]);
    await waitFor(() => expect(botonPdf().hasAttribute('disabled')).toBe(false));
    expect(registro.pedidosA('GET', '/api/presupuestos/155')).toHaveLength(1);

    // Reintentar descarga y borra el mensaje anterior.
    fireEvent.click(botonPdf());

    await waitFor(() => expect(eventos).toEqual(['crear', 'clic', `revocar ${URL_DE_OBJETO}`]));
    expect(screen.queryByText(MENSAJE_SIN_CONEXION)).toBeNull();
  });

  it('401 al descargar: no autorizado, va a ingreso', async () => {
    await abrirCon({ 'GET /api/presupuestos/155': json(200, FINAL), [`GET ${RUTA_PDF}`]: vacia(401) });

    fireEvent.click(botonPdf());

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    await waitFor(() => expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso'));
    expect(eventos).toEqual([]);
  });

  /**
   * Abre el 155, pide su PDF (que queda pendiente) y vuelve al 156 con Atrás. Devuelve con qué responder el pedido del
   * 155 y el registro de pedidos.
   */
  async function pedirPdfYCambiarDeNumero() {
    let responder: (respuesta: Response) => void = () => {};
    const pendiente = new Promise<Response>((resolver) => {
      responder = resolver;
    });
    const registro = servidorFalso({
      'GET /api/presupuestos/155': json(200, FINAL),
      'GET /api/presupuestos/156': json(200, { ...FINAL, numero: 156 }),
      [`GET ${RUTA_PDF}`]: () => pendiente,
      'GET /api/presupuestos/156/pdf': pdf(),
    });
    const { volver } = renderizarApp(['/presupuestos/156', '/presupuestos/155']);
    await screen.findByText('Número');
    fireEvent.click(botonPdf());
    await waitFor(() => expect(botonPdf().getAttribute('aria-busy')).toBe('true'));

    await volver();
    await waitFor(() => expect(dato('Número')).toBe('156'));
    return { responder, registro };
  }

  it('Descargar PDF: un 500 tardío de otro número no pisa la pantalla', async () => {
    const { responder } = await pedirPdfYCambiarDeNumero();

    responder(new Response(null, { status: 500 }));

    await waitFor(() => expect(botonPdf().hasAttribute('aria-disabled')).toBe(false));
    expect(screen.queryByText(MENSAJE_SIN_CONEXION)).toBeNull();
    expect(eventos).toEqual([]);
  });

  it('Descargar PDF: un PDF tardío de otro número no se descarga', async () => {
    // WARN-2: aunque la respuesta sea un PDF válido, ya no le corresponde a la pantalla: ni URL de objeto ni clic.
    const { responder, registro } = await pedirPdfYCambiarDeNumero();

    const respuesta = pdf()();
    let leido = false;
    const blobOriginal = respuesta.blob.bind(respuesta);
    vi.spyOn(respuesta, 'blob').mockImplementation(async () => {
      const contenido = await blobOriginal();
      leido = true;
      return contenido;
    });
    responder(respuesta);

    // El cliente leyó el PDF; después se deja terminar a la pantalla lo que haría con él.
    await waitFor(() => expect(leido).toBe(true));
    await act(async () => {
      await new Promise((resolver) => setTimeout(resolver, 0));
    });
    expect(registro.pedidosA('GET', RUTA_PDF)).toHaveLength(1);
    expect(URL.createObjectURL).not.toHaveBeenCalled();
    expect(HTMLAnchorElement.prototype.click).not.toHaveBeenCalled();
    expect(eventos).toEqual([]);
    expect(dato('Número')).toBe('156');
  });

  it('Descargar PDF: al cambiar de número el botón del presupuesto nuevo no queda bloqueado', async () => {
    // INFO-3: la descarga pendiente del 155 no bloquea la del 156.
    const { registro } = await pedirPdfYCambiarDeNumero();

    expect(botonPdf().hasAttribute('aria-disabled')).toBe(false);
    expect(botonPdf().hasAttribute('aria-busy')).toBe(false);
    fireEvent.click(botonPdf());

    await waitFor(() => expect(eventos).toEqual(['crear', 'clic', `revocar ${URL_DE_OBJETO}`]));
    expect(registro.pedidosA('GET', '/api/presupuestos/156/pdf')).toHaveLength(1);
    expect(enlaces[0]?.descarga).toBe('presupuesto-156.pdf');
  });
});

describe('Presupuesto: Facturar (FEAT-007)', () => {
  const RUTA_FACTURA = '/api/presupuestos/155/factura';
  const CLAVE_FACTURA = `POST ${RUTA_FACTURA}`;
  const COMPROBANTE = 'Factura B 0003-00000125';
  const EMITIDA_MENSAJE = 'Se emitió la Factura B 0003-00000125.';
  const DESCRIPCION_RECHAZO =
    'Rechazo simulado: el número o la fecha del comprobante no se corresponde con el próximo a autorizar.';
  const SIN_RESPUESTA = 'ARCA no respondió. La emisión quedó pendiente de verificación.';
  const PENDIENTE = 'La emisión anterior está pendiente de verificación.';
  const PENDIENTE_VERIFICACION = 'La factura quedó pendiente de verificación.';
  const NO_ENVIADA = 'No se pudo enviar la factura a ARCA. Intentá de nuevo en unos minutos.';
  /** El aviso fijo junto a Facturar mientras hay una emisión pendiente (FR-24). */
  const AVISO_PENDIENTE = 'La emisión de la factura de este presupuesto está pendiente de verificación.';
  /** Un presupuesto en Final con 3 líneas y total $3.000,00 (AC-01), con un DNI ficticio que no se emite (D-50). */
  const FINAL = {
    ...GRABADO,
    estado: 'final',
    version: 4,
    cliente: { ...CLIENTE, dni: '00718294', email: 'cliente@example.com' },
    lineas: [
      { ...LINEA, precioUnitario: 1000, precioConDescuento: 1000, precioFinal: 1000 },
      { ...LINEA, precioUnitario: 1000, precioConDescuento: 1000, precioFinal: 1000, orden: 2 },
      { ...LINEA, precioUnitario: 1000, precioConDescuento: 1000, precioFinal: 1000, orden: 3 },
    ],
    total: 3000,
  };
  const FACTURADO = { ...FINAL, factura: { comprobante: COMPROBANTE } };
  const PENDIENTE_EN_GET = { ...FINAL, emisionPendiente: true };
  const EMITIDA = {
    comprobante: COMPROBANTE,
    tipo: 'b',
    numero: 125,
    cae: '12345678901234',
    vencimientoCae: '2026-11-09',
    fecha: '2026-10-30',
    total: 3000,
    neto: 2479.34,
    iva: 520.66,
  };
  /** El 409 de la emisión con un `detail` que la pantalla nunca muestra (D-61). */
  const conflictoFactura = (motivo: string, extra: Record<string, unknown> = {}) =>
    json(409, { status: 409, title: 'No se pudo emitir la factura.', detail: 'Texto del servidor.', motivo, ...extra });

  const botonFacturar = () => within(pantalla()).getByRole('button', { name: 'Facturar' });

  /** Abre el presupuesto 155 con las respuestas indicadas y espera que se muestre. */
  async function abrirCon(rutas: Parameters<typeof servidorFalso>[0]) {
    const registro = servidorFalso(rutas);
    renderizarApp('/presupuestos/155');
    await screen.findByText('Número');
    return registro;
  }

  /** Abre el diálogo con Facturar y lo devuelve. */
  async function abrirDialogo() {
    fireEvent.click(botonFacturar());
    return screen.findByRole('dialog', { name: 'Facturar' });
  }

  /** Abre el diálogo, elige el concepto y pulsa Emitir. */
  async function emitir(concepto: 'Productos' | 'Productos y servicios' = 'Productos') {
    const dialogo = await abrirDialogo();
    fireEvent.click(within(dialogo).getByRole('radio', { name: concepto }));
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Emitir' }));
    return dialogo;
  }

  it('Facturar: Final con concepto Productos emite y muestra el comprobante', async () => {
    // AC-01, AC-18, AC-19: la pantalla manda solo el concepto; con 201 confirma con el tipo, el punto de venta y el
    // número, y el presupuesto pasa a mostrar la factura, con Facturar deshabilitado (AC-03).
    const registro = await abrirCon({
      'GET /api/presupuestos/155': json(200, FINAL),
      [CLAVE_FACTURA]: json(201, EMITIDA),
    });
    expect(screen.queryByText('Factura', { selector: 'dt' })).toBeNull();
    expect(botonFacturar().hasAttribute('disabled')).toBe(false);

    await emitir('Productos');

    const aviso = await screen.findByText(EMITIDA_MENSAJE);
    expect(aviso.getAttribute('role')).toBe('status');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(dato('Factura')).toBe(COMPROBANTE);
    expect(botonFacturar().hasAttribute('disabled')).toBe(true);
    // El botón quedó deshabilitado: el foco va al aviso, que anuncia el resultado.
    await waitFor(() => expect(document.activeElement).toBe(aviso));
    const pedidos = registro.pedidosA('POST', RUTA_FACTURA);
    expect(pedidos).toHaveLength(1);
    // Solo el concepto (R-06): tipo, importes, receptor y fecha los arma el servidor.
    expect(JSON.parse(String(pedidos[0]?.init?.body))).toEqual({ concepto: 'productos' });
  });

  it('Facturar: Productos y servicios envía ese concepto', async () => {
    const registro = await abrirCon({
      'GET /api/presupuestos/155': json(200, FINAL),
      [CLAVE_FACTURA]: json(201, EMITIDA),
    });

    await emitir('Productos y servicios');

    await screen.findByText(EMITIDA_MENSAJE);
    const [pedido] = registro.pedidosA('POST', RUTA_FACTURA);
    expect(JSON.parse(String(pedido?.init?.body))).toEqual({ concepto: 'productos_y_servicios' });
  });

  it('Facturar: al abrir un presupuesto facturado muestra el comprobante (AC-19)', async () => {
    await abrirCon({ 'GET /api/presupuestos/155': json(200, FACTURADO) });

    expect(dato('Factura')).toBe(COMPROBANTE);
  });

  it.each([
    ['Borrador', GRABADO],
    ['con factura', FACTURADO],
    ['con emisión pendiente', PENDIENTE_EN_GET],
  ])('Facturar: deshabilitado en Borrador, con factura y con pendiente (%s)', async (_caso, cuerpo) => {
    // AC-02, AC-03, AC-22: `disabled`, y un clic no abre el diálogo ni llama a la Api. Control: en Final sin factura
    // ni pendiente está habilitado (el primer caso de este archivo).
    const registro = await abrirCon({ 'GET /api/presupuestos/155': json(200, cuerpo) });

    expect(botonFacturar().hasAttribute('disabled')).toBe(true);
    fireEvent.click(botonFacturar());
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(registro.pedidosA('POST', RUTA_FACTURA)).toEqual([]);
  });

  it('Facturar: con emisión pendiente avisa junto al botón', async () => {
    // El aviso es un estado (`role="status"`) y describe al botón deshabilitado: un lector de pantalla dice por qué.
    await abrirCon({ 'GET /api/presupuestos/155': json(200, PENDIENTE_EN_GET) });

    const aviso = screen.getByText(AVISO_PENDIENTE);
    expect(aviso.getAttribute('role')).toBe('status');
    expect(aviso.classList.contains('aviso')).toBe(true);
    expect(aviso.id).not.toBe('');
    expect(botonFacturar().getAttribute('aria-describedby')).toBe(aviso.id);
    expect(screen.getByRole('button', { name: 'Facturar', description: AVISO_PENDIENTE })).toBe(botonFacturar());
    cleanup();

    await abrirCon({ 'GET /api/presupuestos/155': json(200, FINAL) });
    expect(screen.queryByText(AVISO_PENDIENTE)).toBeNull();
    expect(botonFacturar().hasAttribute('aria-describedby')).toBe(false);
  });

  it('Facturar: sin concepto elegido, Emitir deshabilitado', async () => {
    // FR-08: el diálogo pide el concepto y no trae ninguno elegido al abrir, tampoco al volver a abrirlo.
    const registro = await abrirCon({ 'GET /api/presupuestos/155': json(200, FINAL) });

    let dialogo = await abrirDialogo();
    const productos = within(dialogo).getByRole('radio', { name: 'Productos' }) as HTMLInputElement;
    const servicios = within(dialogo).getByRole('radio', { name: 'Productos y servicios' }) as HTMLInputElement;
    expect(within(dialogo).getByRole('group', { name: 'Concepto' })).toBeTruthy();
    expect(productos.checked).toBe(false);
    expect(servicios.checked).toBe(false);
    const emitirBoton = within(dialogo).getByRole('button', { name: 'Emitir' });
    expect(emitirBoton.hasAttribute('disabled')).toBe(true);
    fireEvent.click(emitirBoton);
    expect(registro.pedidosA('POST', RUTA_FACTURA)).toEqual([]);

    fireEvent.click(servicios);
    expect(within(dialogo).getByRole('button', { name: 'Emitir' }).hasAttribute('disabled')).toBe(false);
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Cancelar' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    await waitFor(() => expect(document.activeElement).toBe(botonFacturar()));

    dialogo = await abrirDialogo();
    expect((within(dialogo).getByRole('radio', { name: 'Productos y servicios' }) as HTMLInputElement).checked).toBe(false);
    fireEvent.keyDown(dialogo, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(registro.pedidosA('POST', RUTA_FACTURA)).toEqual([]);
  });

  it('Facturar: mientras emite, un segundo clic no envía otra emisión', async () => {
    let responder: (respuesta: Response) => void = () => {};
    const pendiente = new Promise<Response>((resolver) => {
      responder = resolver;
    });
    const registro = await abrirCon({
      'GET /api/presupuestos/155': json(200, FINAL),
      [CLAVE_FACTURA]: () => pendiente,
    });

    const dialogo = await emitir();
    const emitirBoton = within(dialogo).getByRole('button', { name: 'Emitir' });
    await waitFor(() => expect(emitirBoton.getAttribute('aria-disabled')).toBe('true'));
    fireEvent.click(emitirBoton);
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Cancelar' }));
    fireEvent.keyDown(dialogo, { key: 'Escape' });
    expect(screen.getByRole('dialog')).toBe(dialogo);
    expect(registro.pedidosA('POST', RUTA_FACTURA)).toHaveLength(1);

    responder(new Response(JSON.stringify(EMITIDA), { status: 201 }));
    await screen.findByText(EMITIDA_MENSAJE);
    expect(registro.pedidosA('POST', RUTA_FACTURA)).toHaveLength(1);
  });

  it('Facturar: 409 rechazada muestra código y descripción y sigue habilitado', async () => {
    // AC-20, FR-22: el código y la descripción de ARCA, como texto; sin factura y con Facturar habilitado.
    const registro = await abrirCon({
      'GET /api/presupuestos/155': json(200, FINAL),
      [CLAVE_FACTURA]: conflictoFactura('rechazada', { codigo: 10016, descripcion: DESCRIPCION_RECHAZO }),
    });

    await emitir();

    const alerta = await screen.findByText(`ARCA rechazó la factura: 10016 – ${DESCRIPCION_RECHAZO}`);
    expect(alerta.getAttribute('role')).toBe('alert');
    expect(screen.queryByText('Texto del servidor.')).toBeNull();
    expect(screen.queryByRole('dialog')).toBeNull();
    await waitFor(() => expect(registro.pedidosA('GET', '/api/presupuestos/155')).toHaveLength(2));
    expect(botonFacturar().hasAttribute('disabled')).toBe(false);
    expect(screen.queryByText('Factura', { selector: 'dt' })).toBeNull();
  });

  it('Facturar: 409 rechazada con una descripción con marcas la muestra como texto', async () => {
    // La descripción viene de ARCA: se muestra como texto, nunca como HTML.
    await abrirCon({
      'GET /api/presupuestos/155': json(200, FINAL),
      [CLAVE_FACTURA]: conflictoFactura('rechazada', { codigo: 10016, descripcion: '<img src=x onerror=alert(1)>' }),
    });

    await emitir();

    const alerta = await screen.findByText('ARCA rechazó la factura: 10016 – <img src=x onerror=alert(1)>');
    expect(alerta.querySelector('img')).toBeNull();
  });

  it('Facturar: 409 rechazada con código 0 dice que ARCA no informó el motivo', async () => {
    // D-57: sin código de ARCA, ni "0" ni la frase fija de la Api repetida.
    await abrirCon({
      'GET /api/presupuestos/155': json(200, FINAL),
      [CLAVE_FACTURA]: conflictoFactura('rechazada', {
        codigo: 0,
        descripcion: 'ARCA rechazó el comprobante sin informar el motivo.',
      }),
    });

    await emitir();

    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toBe('ARCA rechazó la factura sin informar el motivo.');
  });

  /** Abre un Final cuya emisión responde `respuesta` y, al releerlo, tiene la emisión pendiente; emite. */
  async function emitirYQuedarPendiente(respuesta: Parameters<typeof servidorFalso>[0][string]) {
    await abrirCon({
      'GET /api/presupuestos/155': [json(200, FINAL), json(200, PENDIENTE_EN_GET)],
      [CLAVE_FACTURA]: respuesta,
    });
    await emitir();
  }

  it('Facturar: 409 sin_respuesta muestra que ARCA no respondió', async () => {
    // AC-21: el mensaje fijo y, al releer, Facturar deshabilitado por la emisión pendiente; el foco en el mensaje.
    await emitirYQuedarPendiente(conflictoFactura('sin_respuesta'));

    const alerta = await screen.findByText(SIN_RESPUESTA);
    expect(alerta.getAttribute('role')).toBe('alert');
    await waitFor(() => expect(botonFacturar().hasAttribute('disabled')).toBe(true));
    expect(screen.getByText(AVISO_PENDIENTE)).toBeTruthy();
    await waitFor(() => expect(document.activeElement).toBe(alerta));
  });

  it('Facturar: 409 pendiente muestra que la emisión anterior está pendiente', async () => {
    // AC-22: otra sesión dejó una emisión pendiente; el mensaje fijo y Facturar deshabilitado al releer.
    await emitirYQuedarPendiente(conflictoFactura('pendiente'));

    const alerta = await screen.findByText(PENDIENTE);
    expect(alerta.getAttribute('role')).toBe('alert');
    await waitFor(() => expect(botonFacturar().hasAttribute('disabled')).toBe(true));
    await waitFor(() => expect(document.activeElement).toBe(alerta));
  });

  it('Facturar: 500 y emisión pendiente al releer, el foco va al mensaje', async () => {
    // Sin respuesta legible no se sabe si la factura se grabó: se relee y, como el botón puede quedar deshabilitado,
    // el foco va al mensaje y no al botón.
    await emitirYQuedarPendiente(vacia(500));

    const alerta = await screen.findByText(MENSAJE_SIN_CONEXION);
    expect(alerta.getAttribute('role')).toBe('alert');
    await waitFor(() => expect(botonFacturar().hasAttribute('disabled')).toBe(true));
    await waitFor(() => expect(document.activeElement).toBe(alerta));
  });

  it('Facturar: 409 pendiente_verificacion, mensaje de pendiente y botón deshabilitado', async () => {
    // D-31: ARCA autorizó y la factura no se grabó; la pendiente queda y la pantalla no ofrece emitir otra.
    const registro = await abrirCon({
      'GET /api/presupuestos/155': [json(200, FINAL), json(200, PENDIENTE_EN_GET)],
      [CLAVE_FACTURA]: conflictoFactura('pendiente_verificacion'),
    });

    await emitir();

    expect((await screen.findByText(PENDIENTE_VERIFICACION)).getAttribute('role')).toBe('alert');
    await waitFor(() => expect(botonFacturar().hasAttribute('disabled')).toBe(true));
    expect(registro.pedidosA('POST', RUTA_FACTURA)).toHaveLength(1);
  });

  it.each([
    ['sin_tope', 'Cargá el tope de identificación en la configuración para facturar.'],
    ['sin_punto_venta', 'Cargá el punto de venta en la configuración para facturar.'],
    ['alicuota_no_admitida', 'Elegí una alícuota de IVA válida en la configuración para facturar.'],
  ])('Facturar: 409 sin_tope, sin_punto_venta y alicuota_no_admitida, mensajes con enlace (%s)', async (motivo, mensaje) => {
    // AC-13, AC-32, AC-29: el mensaje fijo, con el enlace a la configuración, y Facturar sigue habilitado.
    await abrirCon({
      'GET /api/presupuestos/155': json(200, FINAL),
      [CLAVE_FACTURA]: conflictoFactura(motivo),
    });

    await emitir();

    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toContain(mensaje);
    const enlace = within(alerta).getByRole('link', { name: 'Ir a la configuración' });
    expect(enlace.getAttribute('href')).toBe('/configuracion');
    expect(botonFacturar().hasAttribute('disabled')).toBe(false);

    fireEvent.click(enlace);
    expect(screen.getByTestId('ubicacion').textContent).toBe('/configuracion');
  });

  it.each([
    ['409 no_enviada', conflictoFactura('no_enviada'), NO_ENVIADA],
    ['409 con un motivo desconocido', conflictoFactura('otro'), MENSAJE_SIN_CONEXION],
    ['201 ilegible', json(201, { ...EMITIDA, cae: 'no' }), MENSAJE_SIN_CONEXION],
    ['500', vacia(500), MENSAJE_SIN_CONEXION],
    ['falla de red', sinRed, MENSAJE_SIN_CONEXION],
  ])('Facturar: 409 no_enviada y respuesta ilegible, error general (%s)', async (_caso, respuesta, mensaje) => {
    await abrirCon({
      'GET /api/presupuestos/155': json(200, FINAL),
      [CLAVE_FACTURA]: respuesta,
    });

    await emitir();

    const alerta = await screen.findByText(mensaje);
    expect(alerta.getAttribute('role')).toBe('alert');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(screen.queryByText('Factura', { selector: 'dt' })).toBeNull();
    await waitFor(() => expect(botonFacturar().hasAttribute('disabled')).toBe(false));
    expect(within(alerta).queryByRole('link')).toBeNull();
    await waitFor(() => expect(document.activeElement).toBe(alerta));
  });

  it('Facturar: 401 vuelve al ingreso', async () => {
    servidorFalso({
      'GET /api/presupuestos/155': json(200, FINAL),
      [CLAVE_FACTURA]: vacia(401),
      'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }),
    });
    renderizarApp('/presupuestos/155');
    await screen.findByText('Número');

    await emitir();

    await waitFor(() => expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso'));
  });

  it('Facturar: una respuesta de otro número no pisa la pantalla', async () => {
    let responder: (respuesta: Response) => void = () => {};
    const pendiente = new Promise<Response>((resolver) => {
      responder = resolver;
    });
    servidorFalso({
      'GET /api/presupuestos/155': json(200, FINAL),
      'GET /api/presupuestos/156': json(200, { ...FINAL, numero: 156 }),
      [CLAVE_FACTURA]: () => pendiente,
    });
    const { volver } = renderizarApp(['/presupuestos/156', '/presupuestos/155']);
    await screen.findByText('Número');
    await emitir();

    await volver();
    await waitFor(() => expect(dato('Número')).toBe('156'));
    responder(new Response(JSON.stringify(EMITIDA), { status: 201 }));
    await act(async () => {
      await pendiente;
    });

    expect(screen.queryByText(EMITIDA_MENSAJE)).toBeNull();
    expect(screen.queryByText('Factura', { selector: 'dt' })).toBeNull();
    expect(screen.queryByRole('dialog')).toBeNull();
  });
});
