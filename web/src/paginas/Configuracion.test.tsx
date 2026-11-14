import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
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
  const servidor = servidorFalso({ 'GET /api/configuracion': json(200, INICIAL), 'POST /api/configuracion': json(200, GRABADA_INICIAL) });
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
    const servidor = servidorFalso({ 'GET /api/configuracion': json(200, INICIAL) });
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
    servidorFalso({ 'GET /api/configuracion': json(200, INICIAL) });
    await abrir();

    expect(estados()).toEqual([AVISO_SIN_TOPE]);
  });

  it('Configuracion_ConTope_SinAviso', async () => {
    servidorFalso({
      'GET /api/configuracion': json(200, { ...INICIAL, topeIdentificacion: 250000.5, faltaTope: false }),
    });
    await abrir();

    expect(campo(ETIQUETA_TOPE).value).toBe('250000,5');
    expect(estados()).toEqual([]);
  });

  it('Configuracion_GrabaTope_MuestraValorGuardado', async () => {
    const guardada = { ...GRABADA_INICIAL, topeIdentificacion: 250000, faltaTope: false };
    const servidor = servidorFalso({
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
    const servidor = servidorFalso({
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
    servidorFalso({
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
    servidorFalso({ 'GET /api/configuracion': json(200, { ...INICIAL, alicuotaIva: 10.5 }) });
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
    servidorFalso({ 'GET /api/configuracion': json(200, { ...INICIAL, alicuotasAdmitidas: [21, 27] }) });
    await abrir();

    expect(opcionesAlicuota().map((o) => o.texto)).toEqual(['21', '27']);
  });

  it('alicuota guardada 19: ninguna elegida y aviso, envío rechazado', async () => {
    const servidor = servidorFalso({
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
    const servidor = servidorFalso({
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
    const servidor = servidorFalso({
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
    servidorFalso({
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
    servidorFalso({ 'GET /api/configuracion': json(200, INICIAL), 'POST /api/configuracion': json(200, GRABADA_INICIAL) });
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
    servidorFalso({
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
    servidorFalso({
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
    servidorFalso({ 'GET /api/configuracion': json(200, INICIAL), 'POST /api/configuracion': vacia(413) });
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
    servidorFalso({ 'GET /api/configuracion': vacia(401) });
    renderizarApp(RUTA);

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
  });

  it('Configuracion_SesionVencidaAlGrabar_NavegaAlIngreso', async () => {
    servidorFalso({ 'GET /api/configuracion': json(200, INICIAL), 'POST /api/configuracion': vacia(401) });
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
    servidorFalso({ 'GET /api/configuracion': json(200, INICIAL), 'POST /api/configuracion': respuesta });
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
    servidorFalso({
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
    servidorFalso({
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
    servidorFalso({
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
    servidorFalso({
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
    servidorFalso({ 'GET /api/configuracion': vacia(500) });
    renderizarApp(RUTA);

    expect((await screen.findByText(MENSAJE_SIN_CONEXION)).getAttribute('role')).toBe('alert');
    expect(screen.getByRole('heading', { name: TITULO })).toBeTruthy();
    expect(screen.queryByRole('button', { name: BOTON })).toBeNull();
  });

  it('Configuracion_EnlaceVolverAlInicio', async () => {
    servidorFalso({ 'GET /api/configuracion': json(200, INICIAL) });
    await abrir();

    expect(screen.getByRole('link', { name: 'Volver al inicio' }).getAttribute('href')).toBe('/');
  });
});
