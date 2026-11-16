import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { alPerderLaSesion } from '../api/cliente';
import { mensajeJuntoAlCampo } from '../pruebas/formulario';
import { json, renderizarApp, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';

afterEach(() => {
  try {
    // Los valores viven solo en el estado del componente (threat model, Information Disclosure).
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  } finally {
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
});

const RUTA = '/configuracion';
const TITULO = 'Configuración';
const BOTON = 'Grabar';
const ETIQUETA_ALICUOTA = 'Alícuota de IVA (%)';
const ETIQUETA_CONDICION = 'Condición fiscal';
const ETIQUETA_MULTIPLO = 'Múltiplo de redondeo comercial ($)';
const ETIQUETA_TOPE = 'Tope de identificación del receptor ($)';
const ETIQUETA_PUNTO_VENTA = 'Punto de venta';
const AVISO_SIN_TOPE = 'Falta cargar el tope de identificación del receptor.';
const GRABADA = 'La configuración se grabó.';
/** D-06: el mensaje de grabación empieza como el de FEAT-001c y sigue con la cantidad de precios cambiados. */
const GRABADA_SIN_CAMBIOS = 'La configuración se grabó. Ningún precio de venta cambió.';
const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';
const MENSAJE_CUERPO_GRANDE = 'Los valores enviados son demasiado largos. Revisalos y grabá de nuevo.';
/** D-35: el mensaje de la alícuota que no es una de las 6 de ARCA. */
const MENSAJE_ALICUOTA_NO_ADMITIDA = 'La alícuota de IVA debe ser 0, 2,5, 5, 10,5, 21 o 27.';
const MENSAJE_PUNTO_VENTA = 'El punto de venta debe ser un número entero de 1 a 99999.';
/** D-06: la alícuota guardada no es una de la lista. */
const AVISO_ALICUOTA_FUERA_DE_LISTA = 'La alícuota de IVA guardada ya no es válida. Elegí una de la lista y grabá.';
/** Las 6 alícuotas de ARCA (FR-26), como las muestra la lista: con coma decimal. */
const OPCIONES_ALICUOTA = ['0', '2,5', '5', '10,5', '21', '27'];

const INICIAL = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: null,
  faltaTope: true,
  puntoVenta: null,
  alicuotasAdmitidas: [0, 2.5, 5, 10.5, 21, 27],
};

/** La respuesta del POST: lo guardado más la cantidad de precios de venta que cambiaron (D-23). */
const GRABADA_INICIAL = { ...INICIAL, preciosCambiados: 0 };

/** Los datos del emisor nunca cargados: el 200 con los cinco en null (D-22). */
const EMISOR_SIN_CARGAR = {
  razonSocial: null,
  domicilioComercial: null,
  cuit: null,
  ingresosBrutos: null,
  inicioActividades: null,
};

/**
 * El servidor falso de esta pantalla: la sección de los datos del emisor (FEAT-009) consulta su ruta al abrir, y el
 * servidor falso responde 404 a lo que no está en su tabla. Por defecto, los datos sin cargar; una prueba puede
 * reemplazar la respuesta.
 */
const servidorConEmisor = (rutas: Parameters<typeof servidorFalso>[0]) =>
  servidorFalso({ 'GET /api/configuracion/emisor': json(200, EMISOR_SIN_CARGAR), ...rutas });

/** Abre la pantalla y espera el formulario con los valores consultados. */
async function abrir() {
  renderizarApp(RUTA);
  await screen.findByRole('heading', { name: TITULO });
  await screen.findByRole('button', { name: BOTON });
}

const campo = (etiqueta: string) => screen.getByLabelText(etiqueta) as HTMLInputElement;

/** Las opciones de la lista de alícuotas, con su valor, su texto y si están elegidas. */
const opcionesAlicuota = () =>
  (within(campo(ETIQUETA_ALICUOTA)).getAllByRole('option') as HTMLOptionElement[]).map((o) => ({
    valor: o.value,
    texto: o.textContent,
    elegida: o.selected,
  }));

function escribir(etiqueta: string, valor: string) {
  fireEvent.change(campo(etiqueta), { target: { value: valor } });
}

const grabar = () => fireEvent.click(screen.getByRole('button', { name: BOTON }));

/** Los mensajes con rol status de la pantalla, sin la sonda de ubicación de las pruebas. */
function estados(): string[] {
  const pantalla = screen.getByRole('heading', { name: TITULO }).closest('section')!;
  return within(pantalla)
    .queryAllByRole('status')
    .map((elemento) => elemento.textContent ?? '');
}

/** Si la pantalla muestra algún mensaje de grabación, con cualquier cantidad de precios cambiados. */
const hayMensajeDeGrabada = () => estados().some((texto) => texto.startsWith(GRABADA));

/** Graba con un valor inválido en un campo y comprueba que el error queda junto a ese campo sin enviar. */
async function rechazadoJuntoAlCampo(etiqueta: string, valor: string, mensaje: string) {
  const servidor = servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL), 'POST /api/configuracion': json(200, GRABADA_INICIAL) });
  await abrir();

  escribir(etiqueta, valor);
  grabar();

  await waitFor(() => expect(mensajeJuntoAlCampo(campo(etiqueta))).toBe(mensaje));
  expect(campo(etiqueta).getAttribute('aria-invalid')).toBe('true');
  expect(servidor.pedidosA('POST', '/api/configuracion')).toHaveLength(0);
  expect(campo(etiqueta).value).toBe(valor);
  expect(hayMensajeDeGrabada()).toBe(false);
}

describe('Configuracion', () => {
  it('Configuracion_PrimeraVez_MuestraValoresIniciales', async () => {
    const servidor = servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL) });
    await abrir();

    expect(campo(ETIQUETA_ALICUOTA).value).toBe('21');
    expect(campo(ETIQUETA_ALICUOTA).tagName).toBe('SELECT');
    expect(campo(ETIQUETA_CONDICION).value).toBe('responsable_inscripto');
    expect(campo(ETIQUETA_MULTIPLO).value).toBe('0,01');
    expect(campo(ETIQUETA_TOPE).value).toBe('');
    expect(campo(ETIQUETA_PUNTO_VENTA).value).toBe('');
    const opciones = within(campo(ETIQUETA_CONDICION)).getAllByRole('option');
    expect(opciones.map((o) => [o.getAttribute('value'), o.textContent])).toEqual([
      ['responsable_inscripto', 'Responsable Inscripto'],
      ['monotributo', 'Monotributo'],
    ]);
    for (const etiqueta of [ETIQUETA_MULTIPLO, ETIQUETA_TOPE, ETIQUETA_PUNTO_VENTA]) {
      expect(campo(etiqueta).getAttribute('maxlength')).toBe('32');
      expect(campo(etiqueta).getAttribute('aria-invalid')).toBe('false');
    }
    expect(screen.getByRole('button', { name: BOTON }).closest('form')?.getAttribute('method')).toBe('post');
    expect(servidor.pedidosA('GET', '/api/configuracion')).toHaveLength(1);
  });

  it('Configuracion_SinTope_MuestraAviso', async () => {
    servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL) });
    await abrir();

    expect(estados()).toEqual([AVISO_SIN_TOPE]);
  });

  it('Configuracion_ConTope_SinAviso', async () => {
    servidorConEmisor({
      'GET /api/configuracion': json(200, { ...INICIAL, topeIdentificacion: 250000.5, faltaTope: false }),
    });
    await abrir();

    expect(campo(ETIQUETA_TOPE).value).toBe('250000,5');
    expect(estados()).toEqual([]);
  });

  it('Configuracion_GrabaTope_MuestraValorGuardado', async () => {
    const guardada = { ...GRABADA_INICIAL, topeIdentificacion: 250000, faltaTope: false };
    const servidor = servidorConEmisor({
      'GET /api/configuracion': json(200, INICIAL),
      'POST /api/configuracion': json(200, guardada),
    });
    await abrir();

    escribir(ETIQUETA_TOPE, '250000,00');
    grabar();

    await waitFor(() => expect(estados()).toEqual([GRABADA_SIN_CAMBIOS]));
    // Muestra lo que devolvió la Api, no lo escrito; y el aviso ya no está.
    expect(campo(ETIQUETA_TOPE).value).toBe('250000');
    const [pedido] = servidor.pedidosA('POST', '/api/configuracion');
    expect(JSON.parse(String(pedido?.init?.body))).toEqual({
      alicuotaIva: 21,
      condicionFiscal: 'responsable_inscripto',
      multiploRedondeo: 0.01,
      topeIdentificacion: 250000,
      puntoVenta: null,
    });
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
  });

  it('Configuracion_GrabaMonotributo_MuestraMonotributo', async () => {
    const servidor = servidorConEmisor({
      'GET /api/configuracion': json(200, INICIAL),
      'POST /api/configuracion': json(200, { ...GRABADA_INICIAL, condicionFiscal: 'monotributo' }),
    });
    await abrir();

    fireEvent.change(campo(ETIQUETA_CONDICION), { target: { value: 'monotributo' } });
    grabar();

    await waitFor(() => expect(estados()).toContain(GRABADA_SIN_CAMBIOS));
    expect(campo(ETIQUETA_CONDICION).value).toBe('monotributo');
    expect(JSON.parse(String(servidor.pedidosA('POST', '/api/configuracion')[0]?.init?.body))).toHaveProperty(
      'condicionFiscal',
      'monotributo',
    );
    // Se grabó sin tope (D-02): el aviso sigue.
    expect(estados()).toContain(AVISO_SIN_TOPE);
  });

  it('Configuracion_RespuestaDistintaDeLoEscrito_MuestraLoDevuelto', async () => {
    servidorConEmisor({
      'GET /api/configuracion': json(200, INICIAL),
      'POST /api/configuracion': json(200, { ...GRABADA_INICIAL, alicuotaIva: 10.5, multiploRedondeo: 10 }),
    });
    await abrir();

    escribir(ETIQUETA_ALICUOTA, '27');
    escribir(ETIQUETA_MULTIPLO, '10.0');
    grabar();

    await waitFor(() => expect(campo(ETIQUETA_ALICUOTA).value).toBe('10,5'));
    expect(campo(ETIQUETA_MULTIPLO).value).toBe('10');
    expect(opcionesAlicuota().filter((o) => o.elegida).map((o) => o.texto)).toEqual(['10,5']);
  });

  // ---- FEAT-007: la alícuota de una lista (FR-26, D-06) y el punto de venta (FR-28) ----

  it('alicuota lista: muestra las seis y la guardada elegida', async () => {
    servidorConEmisor({ 'GET /api/configuracion': json(200, { ...INICIAL, alicuotaIva: 10.5 }) });
    await abrir();

    // AC-28: las 6 de ARCA con coma decimal, en el orden de la Api, y la guardada elegida; sin aviso.
    expect(opcionesAlicuota()).toEqual(
      OPCIONES_ALICUOTA.map((texto) => ({ valor: texto, texto, elegida: texto === '10,5' })),
    );
    expect(campo(ETIQUETA_ALICUOTA).value).toBe('10,5');
    expect(campo(ETIQUETA_ALICUOTA).getAttribute('aria-invalid')).toBe('false');
    expect(estados()).toEqual([AVISO_SIN_TOPE]);
  });

  it('alicuota lista: las opciones son las que devuelve la Api', async () => {
    servidorConEmisor({ 'GET /api/configuracion': json(200, { ...INICIAL, alicuotasAdmitidas: [21, 27] }) });
    await abrir();

    expect(opcionesAlicuota().map((o) => o.texto)).toEqual(['21', '27']);
  });

  it('alicuota guardada 19: ninguna elegida y aviso, envío rechazado', async () => {
    const servidor = servidorConEmisor({
      'GET /api/configuracion': json(200, { ...INICIAL, alicuotaIva: 19 }),
      'POST /api/configuracion': json(200, { ...GRABADA_INICIAL, alicuotaIva: 21 }),
    });
    await abrir();

    // D-06: ninguna de las 6 elegida y un aviso que pide elegir una.
    expect(opcionesAlicuota().filter((o) => o.valor !== '')).toEqual(
      OPCIONES_ALICUOTA.map((texto) => ({ valor: texto, texto, elegida: false })),
    );
    expect(campo(ETIQUETA_ALICUOTA).value).toBe('');
    expect(estados()).toEqual([AVISO_SIN_TOPE, AVISO_ALICUOTA_FUERA_DE_LISTA]);

    grabar();

    await waitFor(() => expect(mensajeJuntoAlCampo(campo(ETIQUETA_ALICUOTA))).toBe(MENSAJE_ALICUOTA_NO_ADMITIDA));
    expect(campo(ETIQUETA_ALICUOTA).getAttribute('aria-invalid')).toBe('true');
    expect(servidor.pedidosA('POST', '/api/configuracion')).toHaveLength(0);
    expect(hayMensajeDeGrabada()).toBe(false);

    // Elegida una de la lista, se envía; la respuesta la trae admitida y el aviso se va.
    escribir(ETIQUETA_ALICUOTA, '21');
    grabar();

    await waitFor(() => expect(estados()).toEqual([AVISO_SIN_TOPE, GRABADA_SIN_CAMBIOS]));
    expect(JSON.parse(String(servidor.pedidosA('POST', '/api/configuracion')[0]?.init?.body))).toHaveProperty(
      'alicuotaIva',
      21,
    );
    expect(campo(ETIQUETA_ALICUOTA).getAttribute('aria-invalid')).toBe('false');
    expect(mensajeJuntoAlCampo(campo(ETIQUETA_ALICUOTA))).toBeNull();
  });

  it('Configuracion_PuntoVentaGuardado_SeMuestraYSeEnvia', async () => {
    const servidor = servidorConEmisor({
      'GET /api/configuracion': json(200, { ...INICIAL, puntoVenta: 3 }),
      'POST /api/configuracion': json(200, { ...GRABADA_INICIAL, puntoVenta: 12 }),
    });
    await abrir();
    expect(campo(ETIQUETA_PUNTO_VENTA).value).toBe('3');
    expect(campo(ETIQUETA_PUNTO_VENTA).getAttribute('inputmode')).toBe('numeric');

    escribir(ETIQUETA_PUNTO_VENTA, '0012');
    grabar();

    await waitFor(() => expect(estados()).toContain(GRABADA_SIN_CAMBIOS));
    expect(JSON.parse(String(servidor.pedidosA('POST', '/api/configuracion')[0]?.init?.body))).toHaveProperty(
      'puntoVenta',
      12,
    );
    // Muestra lo que devolvió la Api.
    expect(campo(ETIQUETA_PUNTO_VENTA).value).toBe('12');
  });

  it('Configuracion_PuntoVentaVacio_EnviaNullYMuestraElGuardado', async () => {
    const servidor = servidorConEmisor({
      'GET /api/configuracion': json(200, { ...INICIAL, puntoVenta: 3 }),
      'POST /api/configuracion': json(200, { ...GRABADA_INICIAL, puntoVenta: 3 }),
    });
    await abrir();

    escribir(ETIQUETA_PUNTO_VENTA, '');
    grabar();

    // D-27: vacío conserva el guardado.
    await waitFor(() => expect(estados()).toContain(GRABADA_SIN_CAMBIOS));
    expect(JSON.parse(String(servidor.pedidosA('POST', '/api/configuracion')[0]?.init?.body))).toHaveProperty(
      'puntoVenta',
      null,
    );
    expect(campo(ETIQUETA_PUNTO_VENTA).value).toBe('3');
  });

  it.each(['0', '100000', '2,5'])('punto de venta 0, 100000 y 2,5: inválido sin enviar (%s)', async (valor) => {
    await rechazadoJuntoAlCampo(ETIQUETA_PUNTO_VENTA, valor, MENSAJE_PUNTO_VENTA);
  });

  it.each([
    ['puntoVenta', ETIQUETA_PUNTO_VENTA, MENSAJE_PUNTO_VENTA],
    ['alicuotaIva', ETIQUETA_ALICUOTA, MENSAJE_ALICUOTA_NO_ADMITIDA],
  ])('400 de la Api en %s: error junto al campo', async (nombre, etiqueta, mensaje) => {
    servidorConEmisor({
      'GET /api/configuracion': json(200, INICIAL),
      'POST /api/configuracion': json(400, { status: 400, errors: { [nombre]: ['Texto del servidor.'] } }),
    });
    await abrir();

    escribir(ETIQUETA_PUNTO_VENTA, '5');
    grabar();

    // El mensaje fijo de la pantalla, no el texto que mandó el servidor.
    await waitFor(() => expect(mensajeJuntoAlCampo(campo(etiqueta))).toBe(mensaje));
    expect(campo(etiqueta).getAttribute('aria-invalid')).toBe('true');
    expect(screen.queryByText('Texto del servidor.')).toBeNull();
    expect(campo(ETIQUETA_PUNTO_VENTA).value).toBe('5');
    expect(hayMensajeDeGrabada()).toBe(false);
  });

  it('Configuracion_Multiplo0coma001_RechazadoJuntoAlCampo', async () => {
    await rechazadoJuntoAlCampo(ETIQUETA_MULTIPLO, '0,001', 'El valor mínimo es 0,01.');
  });

  it('Configuracion_Tope0_RechazadoJuntoAlCampo', async () => {
    await rechazadoJuntoAlCampo(ETIQUETA_TOPE, '0', 'El tope debe ser mayor a 0.');
  });

  it('Configuracion_NoNumero_Invalido', async () => {
    await rechazadoJuntoAlCampo(ETIQUETA_MULTIPLO, 'abc', 'Ingresá un número.');
  });

  it('Configuracion_ImporteSobreElMaximoSeguro_RechazadoJuntoAlCampo', async () => {
    await rechazadoJuntoAlCampo(ETIQUETA_MULTIPLO, '90071992547410', 'Ingresá un valor menor.');
  });

  it('Configuracion_ErrorCorregido_SeQuitaAlGrabarDeNuevo', async () => {
    servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL), 'POST /api/configuracion': json(200, GRABADA_INICIAL) });
    await abrir();
    escribir(ETIQUETA_PUNTO_VENTA, '0');
    grabar();
    await waitFor(() => expect(campo(ETIQUETA_PUNTO_VENTA).getAttribute('aria-invalid')).toBe('true'));

    escribir(ETIQUETA_PUNTO_VENTA, '3');
    grabar();

    await waitFor(() => expect(estados()).toContain(GRABADA_SIN_CAMBIOS));
    expect(campo(ETIQUETA_PUNTO_VENTA).getAttribute('aria-invalid')).toBe('false');
    expect(mensajeJuntoAlCampo(campo(ETIQUETA_PUNTO_VENTA))).toBeNull();
  });

  it('Configuracion_Error400DelServidor_MuestraCadaCampo', async () => {
    servidorConEmisor({
      'GET /api/configuracion': json(200, INICIAL),
      'POST /api/configuracion': json(400, {
        status: 400,
        errors: {
          multiploRedondeo: ['El valor mínimo es 0,01.'],
          topeIdentificacion: ['El tope debe ser mayor a 0.'],
        },
      }),
    });
    await abrir();

    escribir(ETIQUETA_TOPE, '1000');
    grabar();

    await waitFor(() => expect(mensajeJuntoAlCampo(campo(ETIQUETA_MULTIPLO))).toBe('El valor mínimo es 0,01.'));
    expect(mensajeJuntoAlCampo(campo(ETIQUETA_TOPE))).toBe('El tope debe ser mayor a 0.');
    expect(campo(ETIQUETA_MULTIPLO).getAttribute('aria-invalid')).toBe('true');
    expect(campo(ETIQUETA_TOPE).getAttribute('aria-invalid')).toBe('true');
    expect(mensajeJuntoAlCampo(campo(ETIQUETA_ALICUOTA))).toBeNull();
    expect(campo(ETIQUETA_TOPE).value).toBe('1000');
    expect(hayMensajeDeGrabada()).toBe(false);
  });

  it('Configuracion_Error400EnLaCondicion_JuntoAlSelector', async () => {
    servidorConEmisor({
      'GET /api/configuracion': json(200, INICIAL),
      'POST /api/configuracion': json(400, {
        errors: { condicionFiscal: ['Elegí Responsable Inscripto o Monotributo.'], campoDesconocido: ['Otro error.'] },
      }),
    });
    await abrir();

    grabar();

    await waitFor(() =>
      expect(mensajeJuntoAlCampo(campo(ETIQUETA_CONDICION))).toBe('Elegí Responsable Inscripto o Monotributo.'),
    );
    expect(campo(ETIQUETA_CONDICION).getAttribute('aria-invalid')).toBe('true');
    // Un campo que la pantalla no tiene se muestra como mensaje general.
    expect(screen.getByText('Otro error.').getAttribute('role')).toBe('alert');
  });

  it('Configuracion_Error413_MensajeGeneralConservaValores', async () => {
    servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL), 'POST /api/configuracion': vacia(413) });
    await abrir();

    escribir(ETIQUETA_TOPE, '300000');
    grabar();

    expect((await screen.findByText(MENSAJE_CUERPO_GRANDE)).getAttribute('role')).toBe('alert');
    // Es un mensaje general, no el de un campo.
    for (const etiqueta of [ETIQUETA_ALICUOTA, ETIQUETA_CONDICION, ETIQUETA_MULTIPLO, ETIQUETA_TOPE, ETIQUETA_PUNTO_VENTA]) {
      expect(campo(etiqueta).getAttribute('aria-invalid')).toBe('false');
      expect(mensajeJuntoAlCampo(campo(etiqueta))).toBeNull();
    }
    expect(campo(ETIQUETA_TOPE).value).toBe('300000');
    expect(hayMensajeDeGrabada()).toBe(false);
  });

  it('Configuracion_SinSesion401_NavegaAlIngreso', async () => {
    servidorConEmisor({ 'GET /api/configuracion': vacia(401) });
    renderizarApp(RUTA);

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
  });

  it('Configuracion_SesionVencidaAlGrabar_NavegaAlIngreso', async () => {
    servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL), 'POST /api/configuracion': vacia(401) });
    await abrir();

    grabar();

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
  });

  it.each([
    ['sin conexión', sinRed, MENSAJE_SIN_CONEXION],
    ['500', vacia(500), MENSAJE_SIN_CONEXION],
    ['429', vacia(429), 'El sistema está ocupado. Intentá de nuevo en un momento.'],
    ['413', vacia(413), MENSAJE_CUERPO_GRANDE],
  ] as const)('Configuracion_SinConexion_ErrorGeneralConservaValores (%s)', async (_caso, respuesta, mensaje) => {
    servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL), 'POST /api/configuracion': respuesta });
    await abrir();

    escribir(ETIQUETA_ALICUOTA, '10,5');
    escribir(ETIQUETA_TOPE, '300000');
    grabar();

    expect((await screen.findByText(mensaje)).getAttribute('role')).toBe('alert');
    expect(campo(ETIQUETA_ALICUOTA).value).toBe('10,5');
    expect(campo(ETIQUETA_TOPE).value).toBe('300000');
    expect(hayMensajeDeGrabada()).toBe(false);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
  });

  // ---- FEAT-002c: cantidad de precios cambiados (D-06, D-09) y error de D-01 ----

  it.each([
    [0, 'La configuración se grabó. Ningún precio de venta cambió.'],
    [1, 'La configuración se grabó. Cambió el precio de venta de 1 artículo.'],
    [2, 'La configuración se grabó. Cambió el precio de venta de 2 artículos.'],
    // D-27: punto de miles en la cantidad.
    [10000, 'La configuración se grabó. Cambió el precio de venta de 10.000 artículos.'],
  ])('Configuracion_Grabada_MuestraCantidad (%i)', async (preciosCambiados, mensaje) => {
    servidorConEmisor({
      'GET /api/configuracion': json(200, INICIAL),
      'POST /api/configuracion': json(200, { ...INICIAL, multiploRedondeo: 50, preciosCambiados }),
    });
    await abrir();

    escribir(ETIQUETA_MULTIPLO, '50');
    grabar();

    // Un solo mensaje, que empieza como el de FEAT-001c (las e2e existentes lo buscan así), junto al aviso del tope.
    await waitFor(() => expect(estados()).toEqual([AVISO_SIN_TOPE, mensaje]));
    expect(campo(ETIQUETA_MULTIPLO).value).toBe('50');
  });

  it.each([
    ['ausente', undefined],
    ['negativo', -1],
    ['con decimales', 2.5],
    ['de texto', '2'],
  ])('Configuracion_PreciosCambiadosInvalido_RespuestaIlegible (%s)', async (_caso, preciosCambiados) => {
    servidorConEmisor({
      'GET /api/configuracion': json(200, INICIAL),
      'POST /api/configuracion': json(200, { ...INICIAL, multiploRedondeo: 50, preciosCambiados }),
    });
    await abrir();

    escribir(ETIQUETA_MULTIPLO, '50');
    grabar();

    // Respuesta ilegible: el mensaje general y lo escrito queda, sin mensaje de grabación.
    expect((await screen.findByText(MENSAJE_SIN_CONEXION)).getAttribute('role')).toBe('alert');
    expect(campo(ETIQUETA_MULTIPLO).value).toBe('50');
    expect(hayMensajeDeGrabada()).toBe(false);
  });

  it('Configuracion_Error500_MensajeGeneral', async () => {
    servidorConEmisor({
      'GET /api/configuracion': json(200, INICIAL),
      'POST /api/configuracion': [
        json(200, { ...INICIAL, multiploRedondeo: 10, preciosCambiados: 3 }),
        vacia(500),
      ],
    });
    await abrir();
    escribir(ETIQUETA_MULTIPLO, '10');
    grabar();
    await waitFor(() =>
      expect(estados()).toContain('La configuración se grabó. Cambió el precio de venta de 3 artículos.'),
    );

    escribir(ETIQUETA_MULTIPLO, '50');
    grabar();

    // El mensaje general de FEAT-001c; la cantidad de la grabación anterior ya no se muestra y la configuración
    // mostrada no cambia por la respuesta fallida.
    expect((await screen.findByText(MENSAJE_SIN_CONEXION)).getAttribute('role')).toBe('alert');
    expect(hayMensajeDeGrabada()).toBe(false);
    expect(campo(ETIQUETA_MULTIPLO).value).toBe('50');
    expect(campo(ETIQUETA_ALICUOTA).value).toBe('21');
    expect(estados()).toEqual([AVISO_SIN_TOPE]);
  });

  it('Configuracion_PrecioFueraDeRango_ErrorJuntoAlMultiplo', async () => {
    const error =
      'No se grabó el cambio: el precio de venta de 2 artículos sería demasiado grande (códigos 15 y 42, inactivo). ' +
      'Revisá esos artículos o el múltiplo; un artículo inactivo hay que reactivarlo para editarlo.';
    servidorConEmisor({
      'GET /api/configuracion': json(200, INICIAL),
      'POST /api/configuracion': json(400, { status: 400, errors: { multiploRedondeo: [error] } }),
    });
    await abrir();

    escribir(ETIQUETA_MULTIPLO, '50');
    grabar();

    // D-01: junto al múltiplo, como los demás errores del servidor.
    await waitFor(() => expect(mensajeJuntoAlCampo(campo(ETIQUETA_MULTIPLO))).toBe(error));
    expect(campo(ETIQUETA_MULTIPLO).getAttribute('aria-invalid')).toBe('true');
    expect(campo(ETIQUETA_MULTIPLO).value).toBe('50');
    expect(hayMensajeDeGrabada()).toBe(false);
  });

  it('Configuracion_ConsultaFallida_MensajeGeneralSinFormulario', async () => {
    servidorConEmisor({ 'GET /api/configuracion': vacia(500) });
    renderizarApp(RUTA);

    expect((await screen.findByText(MENSAJE_SIN_CONEXION)).getAttribute('role')).toBe('alert');
    expect(screen.getByRole('heading', { name: TITULO })).toBeTruthy();
    expect(screen.queryByRole('button', { name: BOTON })).toBeNull();
  });

  it('Configuracion_EnlaceVolverAlInicio', async () => {
    servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL) });
    await abrir();

    expect(screen.getByRole('link', { name: 'Volver al inicio' }).getAttribute('href')).toBe('/');
  });

  // ---- FEAT-009: datos del emisor (FR-01 a FR-03, AC-01 a AC-04) ----

  describe('datos del emisor', () => {
    const RUTA_EMISOR = '/api/configuracion/emisor';
    const BOTON_EMISOR = 'Grabar datos del emisor';
    const ETIQUETA_RAZON_SOCIAL = 'Razón social';
    const ETIQUETA_DOMICILIO = 'Domicilio comercial';
    const ETIQUETA_CUIT = 'CUIT';
    const ETIQUETA_INGRESOS_BRUTOS = 'Ingresos Brutos';
    const ETIQUETA_INICIO = 'Fecha de inicio de actividades';
    const ETIQUETAS_EMISOR = [
      ETIQUETA_RAZON_SOCIAL,
      ETIQUETA_DOMICILIO,
      ETIQUETA_CUIT,
      ETIQUETA_INGRESOS_BRUTOS,
      ETIQUETA_INICIO,
    ];
    const EMISOR_GRABADO = 'Los datos del emisor se grabaron.';
    const MENSAJE_RAZON_SOCIAL_FALTANTE = 'Ingresá la razón social.';
    const MENSAJE_CUIT_INVALIDO = 'El CUIT no es válido: tienen que ser 11 dígitos, con o sin guiones.';
    /** Datos ficticios (D-33): el CUIT 20-00000000-1 tiene dígito verificador válido y no es de nadie. */
    const ESCRITOS = {
      razonSocial: 'Óptica de Prueba S.A.',
      domicilioComercial: 'Calle Falsa 123, Ciudad',
      cuit: '20-00000000-1',
      ingresosBrutos: '901-000000-1',
      inicioActividades: '2020-03-01',
    };
    const GUARDADOS = { ...ESCRITOS, cuit: '20000000001' };

    const seccion = () => screen.getByRole('region', { name: 'Datos del emisor' });
    const campoEmisor = (etiqueta: string) => within(seccion()).getByLabelText(etiqueta) as HTMLInputElement;
    const escribirEmisor = (etiqueta: string, valor: string) =>
      fireEvent.change(campoEmisor(etiqueta), { target: { value: valor } });
    const grabarEmisor = () => fireEvent.click(within(seccion()).getByRole('button', { name: BOTON_EMISOR }));

    /** Abre la pantalla y espera la sección de los datos del emisor con su formulario. */
    async function abrirEmisor() {
      await abrir();
      await screen.findByRole('button', { name: BOTON_EMISOR });
    }

    /** Escribe los cinco datos ficticios, con los cambios indicados. */
    function escribirCinco(cambios: Partial<typeof ESCRITOS> = {}) {
      const datos = { ...ESCRITOS, ...cambios };
      escribirEmisor(ETIQUETA_RAZON_SOCIAL, datos.razonSocial);
      escribirEmisor(ETIQUETA_DOMICILIO, datos.domicilioComercial);
      escribirEmisor(ETIQUETA_CUIT, datos.cuit);
      escribirEmisor(ETIQUETA_INGRESOS_BRUTOS, datos.ingresosBrutos);
      escribirEmisor(ETIQUETA_INICIO, datos.inicioActividades);
    }

    /** Los valores que muestran los cinco campos, en el orden de la pantalla. */
    const valoresEmisor = () => ETIQUETAS_EMISOR.map((etiqueta) => campoEmisor(etiqueta).value);

    const mensajesDeLaSeccion = (rol: 'status' | 'alert') =>
      within(seccion())
        .queryAllByRole(rol)
        .map((elemento) => elemento.textContent ?? '');

    it('Emisor_GrabaCincoDatos_LosMuestraAlVolver', async () => {
      const servidor = servidorConEmisor({
        'GET /api/configuracion': json(200, INICIAL),
        [`GET ${RUTA_EMISOR}`]: [json(200, EMISOR_SIN_CARGAR), json(200, GUARDADOS)],
        [`POST ${RUTA_EMISOR}`]: json(200, GUARDADOS),
      });
      await abrirEmisor();
      // Sin cargar (D-22): los cinco vacíos, la fecha con su control de fecha.
      expect(valoresEmisor()).toEqual(['', '', '', '', '']);
      expect(campoEmisor(ETIQUETA_INICIO).getAttribute('type')).toBe('date');

      escribirCinco();
      grabarEmisor();

      await waitFor(() => expect(mensajesDeLaSeccion('status')).toEqual([EMISOR_GRABADO]));
      const pedidos = servidor.pedidosA('POST', RUTA_EMISOR);
      expect(pedidos).toHaveLength(1);
      expect(JSON.parse(String(pedidos[0]?.init?.body))).toEqual(GUARDADOS);
      // Muestra lo que devolvió la Api: el CUIT sin guiones (AC-03).
      expect(valoresEmisor()).toEqual(Object.values(GUARDADOS));
      // Grabar los datos del emisor no graba los parámetros de negocio.
      expect(servidor.pedidosA('POST', '/api/configuracion')).toHaveLength(0);

      // AC-01: al volver a abrir la configuración, los cinco datos guardados.
      cleanup();
      await abrirEmisor();
      expect(valoresEmisor()).toEqual(Object.values(GUARDADOS));
      expect(mensajesDeLaSeccion('status')).toEqual([]);
      expect(servidor.pedidosA('GET', RUTA_EMISOR)).toHaveLength(2);
    });

    it('Emisor_SinRazonSocial_ErrorJuntoAlCampo', async () => {
      const servidor = servidorConEmisor({
        'GET /api/configuracion': json(200, INICIAL),
        [`POST ${RUTA_EMISOR}`]: json(200, GUARDADOS),
      });
      await abrirEmisor();

      escribirCinco({ razonSocial: '   ' });
      grabarEmisor();

      // AC-02: no se envía y el mensaje queda junto al campo; los demás valores quedan escritos.
      await waitFor(() =>
        expect(mensajeJuntoAlCampo(campoEmisor(ETIQUETA_RAZON_SOCIAL))).toBe(MENSAJE_RAZON_SOCIAL_FALTANTE),
      );
      expect(campoEmisor(ETIQUETA_RAZON_SOCIAL).getAttribute('aria-invalid')).toBe('true');
      expect(servidor.pedidosA('POST', RUTA_EMISOR)).toHaveLength(0);
      expect(campoEmisor(ETIQUETA_CUIT).value).toBe(ESCRITOS.cuit);
      expect(campoEmisor(ETIQUETA_CUIT).getAttribute('aria-invalid')).toBe('false');
      expect(mensajesDeLaSeccion('status')).toEqual([]);

      // El mismo error informado por la Api (un cuerpo alterado): junto al campo, con su texto y los valores quedan.
      cleanup();
      servidorConEmisor({
        'GET /api/configuracion': json(200, INICIAL),
        [`POST ${RUTA_EMISOR}`]: json(400, { status: 400, errors: { razonSocial: [MENSAJE_RAZON_SOCIAL_FALTANTE] } }),
      });
      await abrirEmisor();
      escribirCinco();
      grabarEmisor();

      await waitFor(() =>
        expect(mensajeJuntoAlCampo(campoEmisor(ETIQUETA_RAZON_SOCIAL))).toBe(MENSAJE_RAZON_SOCIAL_FALTANTE),
      );
      expect(valoresEmisor()).toEqual(Object.values(ESCRITOS));
      expect(mensajesDeLaSeccion('status')).toEqual([]);
    });

    it.each(['20-12345678-5', '2012345678'])('Emisor_CuitInvalido_ErrorJuntoAlCampo (%s)', async (cuit) => {
      // AC-04: el CUIT del PRD solo en AC-03 y AC-04 (D-33).
      const servidor = servidorConEmisor({
        'GET /api/configuracion': json(200, INICIAL),
        [`POST ${RUTA_EMISOR}`]: json(400, { status: 400, errors: { cuit: [MENSAJE_CUIT_INVALIDO] } }),
      });
      await abrirEmisor();

      escribirCinco({ cuit });
      grabarEmisor();

      await waitFor(() => expect(mensajeJuntoAlCampo(campoEmisor(ETIQUETA_CUIT))).toBe(MENSAJE_CUIT_INVALIDO));
      expect(campoEmisor(ETIQUETA_CUIT).getAttribute('aria-invalid')).toBe('true');
      expect(servidor.pedidosA('POST', RUTA_EMISOR)).toHaveLength(0);
      expect(campoEmisor(ETIQUETA_CUIT).value).toBe(cuit);
      expect(mensajesDeLaSeccion('status')).toEqual([]);

      // Si la Api lo rechaza igual (validación del servidor), el error va junto al campo.
      escribirEmisor(ETIQUETA_CUIT, '20-00000000-1');
      grabarEmisor();
      await waitFor(() => expect(servidor.pedidosA('POST', RUTA_EMISOR)).toHaveLength(1));
      await waitFor(() => expect(mensajeJuntoAlCampo(campoEmisor(ETIQUETA_CUIT))).toBe(MENSAJE_CUIT_INVALIDO));
      expect(campoEmisor(ETIQUETA_CUIT).value).toBe('20-00000000-1');
    });

    it.each([
      ['500', vacia(500)],
      ['sin conexión', sinRed],
    ] as const)('Emisor_ErrorDelServidor_MensajeGeneral (%s)', async (_caso, respuesta) => {
      servidorConEmisor({
        'GET /api/configuracion': json(200, INICIAL),
        [`POST ${RUTA_EMISOR}`]: respuesta,
      });
      await abrirEmisor();

      escribirCinco();
      grabarEmisor();

      // Mensaje general sin datos; nada se da por grabado y lo escrito queda.
      await waitFor(() => expect(mensajesDeLaSeccion('alert')).toEqual([MENSAJE_SIN_CONEXION]));
      expect(mensajesDeLaSeccion('status')).toEqual([]);
      expect(valoresEmisor()).toEqual(Object.values(ESCRITOS));
      for (const etiqueta of ETIQUETAS_EMISOR) {
        expect(campoEmisor(etiqueta).getAttribute('aria-invalid')).toBe('false');
      }
      expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
    });

    it('Emisor_Cargados_SeMuestranAlAbrir', async () => {
      servidorConEmisor({
        'GET /api/configuracion': json(200, INICIAL),
        [`GET ${RUTA_EMISOR}`]: json(200, GUARDADOS),
      });
      await abrirEmisor();

      expect(screen.getByRole('heading', { name: 'Datos del emisor', level: 2 })).toBeTruthy();
      expect(valoresEmisor()).toEqual(Object.values(GUARDADOS));
      expect(within(seccion()).getByRole('button', { name: BOTON_EMISOR }).closest('form')?.getAttribute('method')).toBe(
        'post',
      );
    });

    it('Emisor_Error400VariosCampos_CadaUnoJuntoASuCampo', async () => {
      servidorConEmisor({
        'GET /api/configuracion': json(200, INICIAL),
        [`POST ${RUTA_EMISOR}`]: json(400, {
          status: 400,
          errors: {
            domicilioComercial: ['El domicilio comercial puede tener hasta 200 caracteres.'],
            inicioActividades: ['La fecha de inicio de actividades no puede ser posterior a hoy.'],
            campoDesconocido: ['Otro error.'],
          },
        }),
      });
      await abrirEmisor();

      escribirCinco();
      grabarEmisor();

      await waitFor(() =>
        expect(mensajeJuntoAlCampo(campoEmisor(ETIQUETA_DOMICILIO))).toBe(
          'El domicilio comercial puede tener hasta 200 caracteres.',
        ),
      );
      expect(mensajeJuntoAlCampo(campoEmisor(ETIQUETA_INICIO))).toBe(
        'La fecha de inicio de actividades no puede ser posterior a hoy.',
      );
      expect(mensajeJuntoAlCampo(campoEmisor(ETIQUETA_RAZON_SOCIAL))).toBeNull();
      // Un campo que la sección no tiene se muestra como mensaje general.
      expect(mensajesDeLaSeccion('alert')).toContain('Otro error.');
      expect(valoresEmisor()).toEqual(Object.values(ESCRITOS));
    });

    it('Emisor_FechaPosteriorAHoy_RechazadaSinEnviar', async () => {
      const servidor = servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL) });
      await abrirEmisor();

      escribirCinco({ inicioActividades: '2999-01-01' });
      grabarEmisor();

      await waitFor(() =>
        expect(mensajeJuntoAlCampo(campoEmisor(ETIQUETA_INICIO))).toBe(
          'La fecha de inicio de actividades no puede ser posterior a hoy.',
        ),
      );
      expect(servidor.pedidosA('POST', RUTA_EMISOR)).toHaveLength(0);
    });

    it('Emisor_ErrorCorregido_SeQuitaAlGrabarDeNuevo', async () => {
      servidorConEmisor({
        'GET /api/configuracion': json(200, INICIAL),
        [`POST ${RUTA_EMISOR}`]: [vacia(500), json(200, GUARDADOS)],
      });
      await abrirEmisor();
      escribirCinco({ razonSocial: '' });
      grabarEmisor();
      await waitFor(() => expect(campoEmisor(ETIQUETA_RAZON_SOCIAL).getAttribute('aria-invalid')).toBe('true'));

      escribirEmisor(ETIQUETA_RAZON_SOCIAL, ESCRITOS.razonSocial);
      grabarEmisor();
      await waitFor(() => expect(mensajesDeLaSeccion('alert')).toEqual([MENSAJE_SIN_CONEXION]));
      expect(campoEmisor(ETIQUETA_RAZON_SOCIAL).getAttribute('aria-invalid')).toBe('false');

      grabarEmisor();
      await waitFor(() => expect(mensajesDeLaSeccion('status')).toEqual([EMISOR_GRABADO]));
      expect(mensajesDeLaSeccion('alert')).toEqual([]);
    });

    it('Emisor_Error413_MensajeGeneral', async () => {
      servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL), [`POST ${RUTA_EMISOR}`]: vacia(413) });
      await abrirEmisor();

      escribirCinco();
      grabarEmisor();

      await waitFor(() =>
        expect(mensajesDeLaSeccion('alert')).toEqual([
          'Los datos del emisor enviados son demasiado largos. Revisalos y grabá de nuevo.',
        ]),
      );
      expect(valoresEmisor()).toEqual(Object.values(ESCRITOS));
    });

    it('Emisor_Error409_MensajeGeneral', async () => {
      // La Api no responde 409 en esta ruta; si llegara uno, es un mensaje general y nada se da por grabado.
      servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL), [`POST ${RUTA_EMISOR}`]: vacia(409) });
      await abrirEmisor();

      escribirCinco();
      grabarEmisor();

      await waitFor(() =>
        expect(mensajesDeLaSeccion('alert')).toEqual([
          'No se pudo completar la operación. Recargá la página y reintentá.',
        ]),
      );
      expect(mensajesDeLaSeccion('status')).toEqual([]);
      expect(valoresEmisor()).toEqual(Object.values(ESCRITOS));
    });

    it('Emisor_Error401ConCampo_JuntoAlCampo', async () => {
      // Un 401 con `errors` no es una sesión vencida: es el error de un campo, como en el resto de los formularios.
      servidorConEmisor({
        'GET /api/configuracion': json(200, INICIAL),
        [`POST ${RUTA_EMISOR}`]: json(401, { status: 401, errors: { cuit: [MENSAJE_CUIT_INVALIDO] } }),
      });
      await abrirEmisor();

      escribirCinco();
      grabarEmisor();

      await waitFor(() => expect(mensajeJuntoAlCampo(campoEmisor(ETIQUETA_CUIT))).toBe(MENSAJE_CUIT_INVALIDO));
      expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
    });

    it('Emisor_Consulta404_MensajeGeneralSinFormulario', async () => {
      // D-22: un 404 no se toma como "sin cargar".
      servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL), [`GET ${RUTA_EMISOR}`]: vacia(404) });
      await abrir();

      await waitFor(() =>
        expect(mensajesDeLaSeccion('alert')).toEqual([
          'No se pudo completar la operación. Recargá la página y reintentá.',
        ]),
      );
      expect(screen.queryByRole('button', { name: BOTON_EMISOR })).toBeNull();
      // Los parámetros de negocio siguen disponibles.
      expect(screen.getByRole('button', { name: BOTON })).toBeTruthy();
    });

    it('Emisor_ConsultaSinSesion_NavegaAlIngreso', async () => {
      servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL), [`GET ${RUTA_EMISOR}`]: vacia(401) });
      renderizarApp(RUTA);

      expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
      expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
    });

    it('Emisor_SesionVencidaAlGrabar_NavegaAlIngreso', async () => {
      servidorConEmisor({ 'GET /api/configuracion': json(200, INICIAL), [`POST ${RUTA_EMISOR}`]: vacia(401) });
      await abrirEmisor();

      escribirCinco();
      grabarEmisor();

      expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
      expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
    });

    it('Emisor_RespuestaDespuesDeSalir_NoNavega', async () => {
      // La operadora sale de la configuración antes de que llegue la consulta del emisor; la respuesta es un 401. La
      // sección ya desmontada no debe navegar: la app sigue donde la operadora fue. Se quita el manejador de sesión de
      // App para que la única navegación posible sea la de la sección.
      let responder: (respuesta: Response) => void = () => {};
      const pendiente = new Promise<Response>((resolver) => {
        responder = resolver;
      });
      servidorConEmisor({
        'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }),
        'GET /api/sesion': vacia(204),
        'GET /api/configuracion': json(200, INICIAL),
        [`GET ${RUTA_EMISOR}`]: () => pendiente,
      });
      await abrir();
      expect(screen.getByText('Cargando los datos del emisor…')).toBeTruthy();

      fireEvent.click(screen.getByRole('link', { name: 'Volver al inicio' }));
      await screen.findByRole('heading', { name: 'Sesión iniciada' });
      expect(screen.queryByRole('region', { name: 'Datos del emisor' })).toBeNull();
      alPerderLaSesion(null);

      responder(new Response(null, { status: 401 }));
      await pendiente;
      await new Promise((resolver) => setTimeout(resolver, 20));

      expect(screen.getByTestId('ubicacion').textContent).toBe('/');
      expect(screen.getByRole('heading', { name: 'Sesión iniciada' })).toBeTruthy();
    });

    it('Emisor_ConsultaFallidaDeLaConfiguracion_SeccionDisponible', async () => {
      servidorConEmisor({ 'GET /api/configuracion': vacia(500), [`GET ${RUTA_EMISOR}`]: json(200, GUARDADOS) });
      renderizarApp(RUTA);

      await screen.findByRole('button', { name: BOTON_EMISOR });
      expect(valoresEmisor()).toEqual(Object.values(GUARDADOS));
      expect(screen.queryByRole('button', { name: BOTON })).toBeNull();
    });
  });
});
