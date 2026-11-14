import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { mensajeJuntoAlCampo } from '../pruebas/formulario';
import { json, renderizarApp, servidorFalso, vacia } from '../pruebas/servidorFalso';

afterEach(() => {
  try {
    // D-17 y FEAT-003c: lo escrito vive solo en la memoria de la pantalla, nunca en el almacenamiento del navegador.
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  } finally {
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
});

const RUTA = '/presupuestos/155/editar';
const TITULO = 'Modificar presupuesto';
const MENSAJE_CAMBIADO = 'El presupuesto cambió mientras lo editabas. Volvé a abrirlo.';
const MENSAJE_FINAL = 'El presupuesto está en Final y no se puede modificar.';
const NO_ENCONTRADO = 'No se encontró el presupuesto.';
const CONFIRMACION = 'Presupuesto N.º 155 grabado. Estado: Borrador.';
const TEXTO_DEL_SERVIDOR = 'Texto del servidor que la pantalla no muestra.';

/** Datos ficticios (D-17), tal como quedaron guardados. */
const CLIENTE = {
  apellido: 'Pérez',
  nombre: 'Ana',
  dni: '99999999',
  domicilio: 'Calle Falsa 123',
  email: 'ana@example.com',
  telefono: '11 5555-0000',
};
/** Las dos líneas grabadas: el Precio Unitario es el de cuando se grabaron (D-02), no el del catálogo de hoy. */
const ARMAZON_GRABADO = {
  codigoArticulo: 7,
  descripcion: 'Armazón metálico',
  precioUnitario: 1850,
  cantidad: 2,
  porcentajeDescuento: 10,
  precioConDescuento: 1665,
  precioFinal: 3330,
  orden: 1,
};
const LENTE_GRABADO = {
  codigoArticulo: 12,
  descripcion: 'Lente orgánico',
  precioUnitario: 1000,
  cantidad: 1,
  porcentajeDescuento: 0,
  precioConDescuento: 1000,
  precioFinal: 1000,
  orden: 2,
};
const GRABADO = {
  numero: 155,
  fecha: '2026-03-10',
  estado: 'borrador',
  cliente: CLIENTE,
  lineas: [ARMAZON_GRABADO, LENTE_GRABADO],
  total: 4330,
  version: 3,
  // El GET suma la facturación (FEAT-007, D-19); el PUT no la lleva y la pantalla ignora lo que sobra.
  factura: null,
  emisionPendiente: false,
};
/** El artículo 7 en el catálogo de hoy: recalculado a $2.000,00 (AC-09). */
const ARMAZON_HOY = { codigo: 7, codigoProveedor: 'ARM-7', descripcion: 'Armazón metálico', precioVenta: 2000, activo: true };
const BUSCAR_ARMAZON = 'GET /api/articulos?texto=armazon&pagina=1&soloActivos=true';

/** Un servidor con el presupuesto 155 en Borrador, la búsqueda de "armazon" y el PUT (por defecto, 200). */
function servidor(rutas: Parameters<typeof servidorFalso>[0] = {}) {
  return servidorFalso({
    'GET /api/presupuestos/155': json(200, GRABADO),
    [BUSCAR_ARMAZON]: json(200, { articulos: [ARMAZON_HOY], pagina: 1, tamanoPagina: 50, total: 1 }),
    'PUT /api/presupuestos/155': json(200, { ...GRABADO, version: 4 }),
    ...rutas,
  });
}

async function abrir() {
  renderizarApp(RUTA);
  await screen.findByRole('heading', { name: TITULO });
  await screen.findByRole('button', { name: 'Grabar' });
}

const campo = (etiqueta: string) => screen.getByLabelText(etiqueta) as HTMLInputElement;
const escribir = (etiqueta: string, valor: string) => fireEvent.change(campo(etiqueta), { target: { value: valor } });
const grabar = () => fireEvent.click(screen.getByRole('button', { name: 'Grabar' }));
const cuerpoLineas = () => screen.getByRole('table', { name: 'Líneas del presupuesto' }).querySelector('tbody')!;

/** El texto de cada celda de cada línea cargada (los campos se leen por su valor). */
function lineas(): string[][] {
  return within(cuerpoLineas())
    .getAllByRole('row')
    .map((fila) =>
      within(fila)
        .getAllByRole('cell')
        .map((celda) => celda.querySelector('input')?.value ?? celda.textContent ?? ''),
    );
}

/** El cuerpo del único PUT enviado. */
function cuerpoEnviado(registro: ReturnType<typeof servidorFalso>) {
  const pedidos = registro.pedidosA('PUT', '/api/presupuestos/155');
  expect(pedidos).toHaveLength(1);
  return JSON.parse(String(pedidos[0]?.init?.body)) as { version: number; cliente: unknown; lineas: object[] };
}

describe('EditarPresupuesto', () => {
  it('Editar_CargaDatosYLineasGrabadas', async () => {
    // AC-02 y AC-09: los datos guardados y las líneas con su Precio Unitario grabado, no el del catálogo de hoy.
    const registro = servidor();
    await abrir();

    expect(campo('Apellido').value).toBe('Pérez');
    expect(campo('Nombre').value).toBe('Ana');
    expect(campo('DNI').value).toBe('99999999');
    expect(campo('Domicilio').value).toBe('Calle Falsa 123');
    expect(campo('Email').value).toBe('ana@example.com');
    expect(campo('Nro. de Teléfono').value).toBe('11 5555-0000');
    expect(lineas()).toEqual([
      ['7', 'Armazón metálico', '1.850,00', '2', '10', '1.665,00', '3.330,00', 'Quitar'],
      ['12', 'Lente orgánico', '1.000,00', '1', '0', '1.000,00', '1.000,00', 'Quitar'],
    ]);
    expect(campo('Precio unitario de la línea 1').readOnly).toBe(true);
    expect(screen.getByText('4.330,00')).toBeTruthy();
    expect(registro.llamadas.filter((l) => l.metodo === 'PUT')).toEqual([]);
  });

  it('Editar_ModificarDomicilio_GrabaYConfirma', async () => {
    // AC-02: graba con la versión cargada, las líneas grabadas con su `orden`, y vuelve al presupuesto confirmando.
    const registro = servidor();
    await abrir();

    escribir('Domicilio', 'Av. Siempre Viva 742');
    grabar();

    expect(await screen.findByText(CONFIRMACION)).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/presupuestos/155');
    expect(cuerpoEnviado(registro)).toEqual({
      version: 3,
      cliente: { ...CLIENTE, domicilio: 'Av. Siempre Viva 742' },
      lineas: [
        { orden: 1, codigoArticulo: 7, cantidad: 2, porcentajeDescuento: 10 },
        { orden: 2, codigoArticulo: 12, cantidad: 1, porcentajeDescuento: 0 },
      ],
    });
  });

  it('Editar_AgregarYQuitarLinea_EnviaOrdenSoloEnLasGrabadas', async () => {
    // AC-03: la línea quitada no se envía, la grabada que queda va con su `orden` y la nueva va sin `orden`, con el
    // precio actual del catálogo (D-02).
    const registro = servidor();
    await abrir();

    fireEvent.click(screen.getByRole('button', { name: 'Quitar línea 1' }));
    fireEvent.change(campo('Texto a buscar'), { target: { value: 'armazon' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Agregar 7 Armazón metálico' }));

    expect(lineas()).toEqual([
      ['12', 'Lente orgánico', '1.000,00', '1', '0', '1.000,00', '1.000,00', 'Quitar'],
      ['7', 'Armazón metálico', '2.000,00', '1', '0', '2.000,00', '2.000,00', 'Quitar'],
    ]);
    grabar();

    await screen.findByText(CONFIRMACION);
    const cuerpo = cuerpoEnviado(registro);
    expect(cuerpo.lineas).toEqual([
      { orden: 2, codigoArticulo: 12, cantidad: 1, porcentajeDescuento: 0 },
      { codigoArticulo: 7, cantidad: 1, porcentajeDescuento: 0 },
    ]);
    expect('orden' in cuerpo.lineas[1]!).toBe(false);
  });

  it('Editar_PresupuestoFinal_MuestraAvisoSinFormulario', async () => {
    // AC-07 y AC-08: un Final no se puede modificar por la pantalla; ni formulario ni botón para grabar.
    const registro = servidor({ 'GET /api/presupuestos/155': json(200, { ...GRABADO, estado: 'final' }) });
    renderizarApp(RUTA);

    expect(await screen.findByText(MENSAJE_FINAL)).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Grabar' })).toBeNull();
    expect(screen.queryAllByRole('textbox')).toEqual([]);
    expect(screen.queryByText('Pérez')).toBeNull();
    expect(screen.getByRole('link', { name: 'Volver al presupuesto' }).getAttribute('href')).toBe('/presupuestos/155');
    expect(registro.llamadas.filter((l) => l.metodo === 'PUT')).toEqual([]);
  });

  it('Editar_409Cambiado_MuestraMensaje', async () => {
    // D-01 y D-07: el texto propio, nunca el del servidor; lo escrito queda en pantalla y solo en la memoria.
    const espias = (['log', 'info', 'warn', 'error', 'debug'] as const).map((metodo) => vi.spyOn(console, metodo));
    servidor({
      'PUT /api/presupuestos/155': json(409, { title: 'No se pudo grabar el presupuesto.', detail: TEXTO_DEL_SERVIDOR, motivo: 'cambiado' }),
    });
    await abrir();

    escribir('Domicilio', 'Av. Siempre Viva 742');
    grabar();

    const alerta = await screen.findByText(MENSAJE_CAMBIADO);
    expect(alerta.getAttribute('role')).toBe('alert');
    expect(screen.queryByText(TEXTO_DEL_SERVIDOR)).toBeNull();
    expect(campo('Domicilio').value).toBe('Av. Siempre Viva 742');
    const ubicacion = screen.getByTestId('ubicacion');
    expect(ubicacion.textContent).toBe(RUTA);
    expect(ubicacion.getAttribute('data-estado')).toBe('null');
    expect(window.location.href).not.toContain('Siempre');
    const escrito = espias.flatMap((espia) => espia.mock.calls.flat().map((argumento) => String(argumento)));
    expect(escrito.filter((texto) => texto.includes('Siempre Viva'))).toEqual([]);
  });

  it('Editar_409Final_MuestraMensaje', async () => {
    servidor({
      'PUT /api/presupuestos/155': json(409, { title: 'No se pudo grabar el presupuesto.', detail: TEXTO_DEL_SERVIDOR, motivo: 'final' }),
    });
    await abrir();

    escribir('Domicilio', 'Av. Siempre Viva 742');
    grabar();

    const alerta = await screen.findByText(MENSAJE_FINAL);
    expect(alerta.getAttribute('role')).toBe('alert');
    expect(screen.queryByText(TEXTO_DEL_SERVIDOR)).toBeNull();
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
  });

  it('Editar_DatoInvalido_MensajeJuntoAlCampo', async () => {
    // Las mismas reglas del alta: el mensaje va junto al campo y no se envía.
    const registro = servidor();
    await abrir();

    escribir('Apellido', '');
    escribir('Cantidad de la línea 1', '0');
    grabar();

    expect(mensajeJuntoAlCampo(campo('Apellido'))).toBe('Ingresá el apellido del cliente');
    expect(mensajeJuntoAlCampo(campo('Cantidad de la línea 1'))).toBe('La cantidad debe ser un número entero mayor a 0');
    expect(registro.llamadas.filter((l) => l.metodo === 'PUT')).toEqual([]);
  });

  it('Editar_400DelServidor_JuntoALaLineaYGeneral', async () => {
    // D-03: `lineas[i].orden` va junto a la línea; `version` no es un campo de la pantalla y va como mensaje general.
    servidor({
      'PUT /api/presupuestos/155': json(400, {
        errors: {
          'lineas[1].orden': ['La línea no corresponde a este presupuesto.'],
          version: ['Volvé a abrir el presupuesto para editarlo.'],
          dni: ['El DNI debe tener 7 u 8 dígitos'],
        },
      }),
    });
    await abrir();

    grabar();

    await screen.findByText('La línea no corresponde a este presupuesto.');
    expect(mensajeJuntoAlCampo(screen.getByRole('button', { name: 'Quitar línea 2' }))).toBe(
      'La línea no corresponde a este presupuesto.',
    );
    expect(mensajeJuntoAlCampo(campo('DNI'))).toBe('El DNI debe tener 7 u 8 dígitos');
    expect(screen.getByText('Volvé a abrir el presupuesto para editarlo.').getAttribute('role')).toBe('alert');
  });

  it('Editar_404_MensajeNoEncontrado', async () => {
    servidorFalso({ 'GET /api/presupuestos/155': vacia(404) });
    renderizarApp(RUTA);

    expect(await screen.findByText(NO_ENCONTRADO)).toBeTruthy();
    expect(screen.getByRole('heading', { name: TITULO })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Grabar' })).toBeNull();
  });

  it.each(['abc', '0', '007'])('Editar_NumeroInvalido_%s_NoConsulta', async (numero) => {
    const registro = servidorFalso({});
    renderizarApp(`/presupuestos/${numero}/editar`);

    expect(await screen.findByText(NO_ENCONTRADO)).toBeTruthy();
    expect(registro.llamadas.filter((l) => l.ruta.startsWith('/api/presupuestos'))).toEqual([]);
  });

  it('Editar_SinSesionAlAbrir_VuelveAlIngreso', async () => {
    servidorFalso({ 'GET /api/presupuestos/155': vacia(401) });
    renderizarApp(RUTA);

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    await waitFor(() => expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso'));
  });

  it('Editar_SinSesionAlGrabar_VuelveAlIngreso', async () => {
    servidor({ 'PUT /api/presupuestos/155': vacia(401) });
    await abrir();

    grabar();

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    await waitFor(() => expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso'));
  });

  it('Editar_Enlaces_VolverAlPresupuesto', async () => {
    servidor();
    await abrir();

    expect(screen.getByRole('link', { name: 'Volver al presupuesto' }).getAttribute('href')).toBe('/presupuestos/155');
  });
});
