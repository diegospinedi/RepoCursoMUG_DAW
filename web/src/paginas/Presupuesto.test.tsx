import { cleanup, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { json, renderizarApp, servidorFalso, vacia } from '../pruebas/servidorFalso';

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
};
const GRABADO = { numero: 155, fecha: '2026-03-10', estado: 'borrador', cliente: CLIENTE, lineas: [LINEA], total: 1850 };

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
    // Solo lectura (FEAT-003c trae la edición): ni campos ni botones de edición.
    expect(within(pantalla()).queryAllByRole('textbox')).toEqual([]);
    expect(within(pantalla()).queryAllByRole('button')).toEqual([]);
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

  it('Presupuesto_Enlaces_NuevoPresupuestoYVolverAlInicio', async () => {
    await abrir();

    expect(screen.getByRole('link', { name: 'Nuevo presupuesto' }).getAttribute('href')).toBe('/presupuestos/nuevo');
    expect(screen.getByRole('link', { name: 'Volver al inicio' }).getAttribute('href')).toBe('/');
  });
});
