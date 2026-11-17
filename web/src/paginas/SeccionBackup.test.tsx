import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import { alPerderLaSesion } from '../api/cliente';
import { mensajeJuntoAlCampo } from '../pruebas/formulario';
import { json, renderizarApp, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';

/** Las rutas escritas en la prueba en curso: ninguna puede quedar en el navegador ni en la consola. */
let rutasEscritas: string[] = [];
let consola: MockInstance[] = [];

/**
 * Todas las pruebas vigilan la consola (sin mostrarla): se espía acá, después de que `restoreMocks` restauró los espías
 * de la prueba anterior, y el `afterEach` revisa que ninguna ruta escrita haya llegado a ella.
 */
beforeEach(() => {
  consola = (['log', 'info', 'warn', 'error', 'debug', 'trace'] as const).map((metodo) =>
    vi.spyOn(console, metodo).mockImplementation(() => {}),
  );
});

afterEach(() => {
  try {
    // La ruta puede llevar el nombre de una persona (D-12): vive solo en el estado del componente.
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    const lugares = [
      decodeURIComponent(window.location.href),
      screen.queryByTestId('ubicacion')?.textContent ?? '',
      screen.queryByTestId('ubicacion')?.getAttribute('data-estado') ?? '',
      ...consola.flatMap((espia) => espia.mock.calls.map((argumentos) => argumentos.map(String).join(' '))),
    ];
    for (const ruta of rutasEscritas.map((r) => r.trim()).filter((r) => r !== '')) {
      expect(lugares.filter((lugar) => lugar.includes(ruta))).toEqual([]);
    }
  } finally {
    rutasEscritas = [];
    consola = [];
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
});

const PANTALLA = '/configuracion';
const RUTA_API = '/api/configuracion/backup';
const TITULO_SECCION = 'Backup';
const BOTON = 'Grabar ubicación del backup';
const ETIQUETA = 'Carpeta del backup';
const GRABADA = 'La ubicación del backup se grabó.';
const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';
/** Los textos de `ValidacionUbicacionBackup` de la Api (Block 2). */
const MENSAJE_FALTANTE = 'Ingresá la carpeta del backup.';
const MENSAJE_LARGA = 'La ruta de la carpeta del backup puede tener hasta 260 caracteres.';
const MENSAJE_MISMA_UNIDAD =
  'La carpeta debe estar en otra unidad que la base de datos (un pendrive o un disco externo) o en una carpeta ' +
  'sincronizada con la nube (OneDrive, Google Drive o Dropbox).';
const MENSAJE_INEXISTENTE =
  'La carpeta no existe: no se puede escribir en ella. Revisá que el pendrive o el disco estén conectados.';
const MENSAJE_SIN_ESCRITURA = 'No se puede escribir en la carpeta elegida. Revisá sus permisos o elegí otra.';

/** Rutas ficticias, sin nombres de personas. */
const PENDRIVE = 'E:\\Backups\\Optica';
const NUBE = 'C:\\Usuarios\\Local\\OneDrive\\Backups';

const CONFIGURACION = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: 250000,
  faltaTope: false,
  puntoVenta: 3,
  alicuotasAdmitidas: [0, 2.5, 5, 10.5, 21, 27],
};
const EMISOR_SIN_CARGAR = {
  razonSocial: null,
  domicilioComercial: null,
  cuit: null,
  ingresosBrutos: null,
  inicioActividades: null,
};

/** El servidor falso de la pantalla: las otras dos secciones responden; la del backup, sin elegir salvo otra cosa. */
const servidor = (rutas: Parameters<typeof servidorFalso>[0]) =>
  servidorFalso({
    'GET /api/configuracion': json(200, CONFIGURACION),
    'GET /api/configuracion/emisor': json(200, EMISOR_SIN_CARGAR),
    [`GET ${RUTA_API}`]: json(200, { ruta: null }),
    ...rutas,
  });

const errorDeRuta = (mensaje: string) => json(400, { status: 400, errors: { ruta: [mensaje] } });

const seccion = () => screen.getByRole('region', { name: TITULO_SECCION });
const campo = () => within(seccion()).getByLabelText(ETIQUETA) as HTMLInputElement;
const mensajes = (rol: 'status' | 'alert') =>
  within(seccion())
    .queryAllByRole(rol)
    .map((elemento) => elemento.textContent ?? '');

/** Abre la configuración y espera el formulario de la sección del backup. */
async function abrir() {
  renderizarApp(PANTALLA);
  await screen.findByRole('button', { name: BOTON });
}

function escribir(ruta: string) {
  rutasEscritas.push(ruta);
  fireEvent.change(campo(), { target: { value: ruta } });
}

const grabar = () => fireEvent.click(within(seccion()).getByRole('button', { name: BOTON }));

/** Escribe la ruta, graba y espera el error de la Api junto al campo, con lo escrito conservado. */
async function rechazadaPorLaApi(ruta: string, mensaje: string) {
  const falso = servidor({ [`POST ${RUTA_API}`]: errorDeRuta(mensaje) });
  await abrir();

  escribir(ruta);
  grabar();

  await waitFor(() => expect(mensajeJuntoAlCampo(campo())).toBe(mensaje));
  expect(falso.pedidosA('POST', RUTA_API)).toHaveLength(1);
  expect(campo().getAttribute('aria-invalid')).toBe('true');
  expect(campo().value).toBe(ruta);
  expect(mensajes('status')).toEqual([]);
  expect(mensajes('alert')).toEqual([mensaje]);
}

describe('SeccionBackup', () => {
  // AC-01 (pendrive) y AC-17 (carpeta de OneDrive en la misma unidad que la base).
  it.each([PENDRIVE, NUBE])('Backup_GrabaUbicacion_LaMuestraAlVolver (%s)', async (ruta) => {
    const falso = servidor({
      [`GET ${RUTA_API}`]: [json(200, { ruta: null }), json(200, { ruta })],
      [`POST ${RUTA_API}`]: json(200, { ruta }),
    });
    await abrir();
    // Nunca elegida: el campo vacío y ningún mensaje de estado en reposo.
    expect(screen.getByRole('heading', { name: TITULO_SECCION, level: 2 })).toBeTruthy();
    expect(campo().value).toBe('');
    expect(campo().getAttribute('name')).toBe('ruta');
    expect(mensajes('status')).toEqual([]);
    expect(mensajes('alert')).toEqual([]);

    escribir(`  ${ruta}  `);
    grabar();

    await waitFor(() => expect(mensajes('status')).toEqual([GRABADA]));
    const pedidos = falso.pedidosA('POST', RUTA_API);
    expect(pedidos).toHaveLength(1);
    expect(JSON.parse(String(pedidos[0]?.init?.body))).toEqual({ ruta });
    // Muestra la ruta que devolvió la Api.
    expect(campo().value).toBe(ruta);
    expect(campo().getAttribute('aria-invalid')).toBe('false');
    // Grabar la ubicación no graba las otras secciones.
    expect(falso.pedidosA('POST', '/api/configuracion')).toHaveLength(0);
    expect(falso.pedidosA('POST', '/api/configuracion/emisor')).toHaveLength(0);

    // Al volver a abrir la configuración, la ubicación grabada y sin mensajes.
    cleanup();
    await abrir();
    expect(campo().value).toBe(ruta);
    expect(mensajes('status')).toEqual([]);
    expect(falso.pedidosA('GET', RUTA_API)).toHaveLength(2);
  });

  it('Backup_MismaUnidad_ErrorJuntoAlCampo', async () => {
    // AC-02: la Api decide la unidad; su mensaje va junto al campo y lo escrito queda.
    await rechazadaPorLaApi('C:\\Backups', MENSAJE_MISMA_UNIDAD);
  });

  it.each([MENSAJE_INEXISTENTE, MENSAJE_SIN_ESCRITURA])('Backup_SinEscritura_ErrorJuntoAlCampo (%s)', async (mensaje) => {
    // AC-03: carpeta inexistente o sin permiso de escritura.
    await rechazadaPorLaApi('/media/pendrive/Backups', mensaje);
  });

  it.each([
    ['500', vacia(500)],
    ['sin conexión', sinRed],
  ] as const)('Backup_ErrorDelServidor_MensajeGeneral (%s)', async (_caso, respuesta) => {
    servidor({ [`POST ${RUTA_API}`]: respuesta });
    await abrir();

    escribir(PENDRIVE);
    grabar();

    // Mensaje general sin la ruta; nada se da por grabado y lo escrito queda.
    await waitFor(() => expect(mensajes('alert')).toEqual([MENSAJE_SIN_CONEXION]));
    expect(mensajes('alert').some((texto) => texto.includes(PENDRIVE))).toBe(false);
    expect(mensajes('status')).toEqual([]);
    expect(campo().value).toBe(PENDRIVE);
    expect(campo().getAttribute('aria-invalid')).toBe('false');
    expect(screen.getByTestId('ubicacion').textContent).toBe(PANTALLA);
  });

  it.each([
    ['vacía', '', MENSAJE_FALTANTE],
    ['solo espacios', '   ', MENSAJE_FALTANTE],
    ['de 261 caracteres', `E:\\${'a'.repeat(258)}`, MENSAJE_LARGA],
  ])('Backup_Ruta%s_RechazadaSinEnviar', async (_caso, ruta, mensaje) => {
    const falso = servidor({ [`POST ${RUTA_API}`]: json(200, { ruta: PENDRIVE }) });
    await abrir();

    escribir(ruta);
    grabar();

    await waitFor(() => expect(mensajeJuntoAlCampo(campo())).toBe(mensaje));
    expect(campo().getAttribute('aria-invalid')).toBe('true');
    expect(falso.pedidosA('POST', RUTA_API)).toHaveLength(0);
    expect(campo().value).toBe(ruta);
    expect(mensajes('status')).toEqual([]);
  });

  it('Backup_RutaDe260_SeEnvia', async () => {
    const ruta = `E:\\${'a'.repeat(257)}`;
    const falso = servidor({ [`POST ${RUTA_API}`]: json(200, { ruta }) });
    await abrir();

    escribir(ruta);
    grabar();

    await waitFor(() => expect(mensajes('status')).toEqual([GRABADA]));
    expect(falso.pedidosA('POST', RUTA_API)).toHaveLength(1);
  });

  it('Backup_ErrorCorregido_SeQuitaAlGrabarDeNuevo', async () => {
    servidor({ [`POST ${RUTA_API}`]: [errorDeRuta(MENSAJE_SIN_ESCRITURA), json(200, { ruta: PENDRIVE })] });
    await abrir();

    escribir('/media/pendrive/Backups');
    grabar();
    await waitFor(() => expect(mensajeJuntoAlCampo(campo())).toBe(MENSAJE_SIN_ESCRITURA));

    escribir(PENDRIVE);
    grabar();
    await waitFor(() => expect(mensajes('status')).toEqual([GRABADA]));
    expect(mensajeJuntoAlCampo(campo())).toBeNull();
    expect(mensajes('alert')).toEqual([]);
  });

  it('Backup_Error400DeOtroCampo_MensajeGeneral', async () => {
    servidor({ [`POST ${RUTA_API}`]: json(400, { status: 400, errors: { otro: ['Otro error.'] } }) });
    await abrir();

    escribir(PENDRIVE);
    grabar();

    await waitFor(() => expect(mensajes('alert')).toEqual(['Otro error.']));
    expect(mensajeJuntoAlCampo(campo())).toBeNull();
  });

  it('Backup_413_MensajeGeneral', async () => {
    servidor({ [`POST ${RUTA_API}`]: vacia(413) });
    await abrir();

    escribir(PENDRIVE);
    grabar();

    await waitFor(() =>
      expect(mensajes('alert')).toEqual(['La ruta enviada es demasiado larga. Revisala y grabá de nuevo.']),
    );
    expect(campo().value).toBe(PENDRIVE);
  });

  it('Backup_Conflicto409_MensajeGeneral', async () => {
    servidor({ [`POST ${RUTA_API}`]: vacia(409) });
    await abrir();

    escribir(PENDRIVE);
    grabar();

    await waitFor(() =>
      expect(mensajes('alert')).toEqual(['No se pudo completar la operación. Recargá la página y reintentá.']),
    );
    expect(mensajes('status')).toEqual([]);
    expect(campo().value).toBe(PENDRIVE);
  });

  it('Backup_RespuestaDespuesDeSalir_NoNavega', async () => {
    // La operadora sale de la pantalla antes de que llegue la consulta de la ubicación; la respuesta es un 401. La
    // sección ya desmontada no navega. Se quita el manejador de sesión de App para que solo la sección pueda navegar.
    let responder: (respuesta: Response) => void = () => {};
    const pendiente = new Promise<Response>((resolver) => {
      responder = resolver;
    });
    servidor({
      'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }),
      'GET /api/sesion': vacia(204),
      [`GET ${RUTA_API}`]: () => pendiente,
    });
    renderizarApp(PANTALLA);
    await screen.findByRole('button', { name: 'Grabar datos del emisor' });
    expect(screen.getByText('Cargando la ubicación del backup…')).toBeTruthy();

    fireEvent.click(screen.getByRole('link', { name: 'Volver al inicio' }));
    await screen.findByRole('heading', { name: 'Sesión iniciada' });
    expect(screen.queryByRole('region', { name: TITULO_SECCION })).toBeNull();
    alPerderLaSesion(null);

    responder(new Response(null, { status: 401 }));
    await pendiente;
    await new Promise((resolver) => setTimeout(resolver, 20));

    expect(screen.getByTestId('ubicacion').textContent).toBe('/');
  });

  it('Backup_GrabarDespuesDeSalir_NoNavega', async () => {
    // La operadora graba y sale de la pantalla antes de que llegue la respuesta, que es un 401. La sección ya desmontada
    // no navega ni cambia su estado. Se quita el manejador de sesión de App para que solo la sección pueda navegar.
    let responder: (respuesta: Response) => void = () => {};
    const pendiente = new Promise<Response>((resolver) => {
      responder = resolver;
    });
    const falso = servidor({
      'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }),
      'GET /api/sesion': vacia(204),
      [`POST ${RUTA_API}`]: () => pendiente,
    });
    await abrir();

    escribir(PENDRIVE);
    grabar();
    await waitFor(() => expect(falso.pedidosA('POST', RUTA_API)).toHaveLength(1));

    fireEvent.click(screen.getByRole('link', { name: 'Volver al inicio' }));
    await screen.findByRole('heading', { name: 'Sesión iniciada' });
    expect(screen.queryByRole('region', { name: TITULO_SECCION })).toBeNull();
    alPerderLaSesion(null);

    responder(new Response(null, { status: 401 }));
    await pendiente;
    await new Promise((resolver) => setTimeout(resolver, 20));

    expect(screen.getByTestId('ubicacion').textContent).toBe('/');
    expect(screen.getByRole('heading', { name: 'Sesión iniciada' })).toBeTruthy();
  });

  it('Backup_EditarDespuesDeGrabar_QuitaElMensajeDeGrabada', async () => {
    servidor({ [`POST ${RUTA_API}`]: json(200, { ruta: PENDRIVE }) });
    await abrir();

    escribir(PENDRIVE);
    grabar();
    await waitFor(() => expect(mensajes('status')).toEqual([GRABADA]));

    // Lo que se ve ya no es lo grabado: el mensaje de grabada no puede seguir afirmándolo.
    escribir('/media/pendrive/Backups');
    expect(mensajes('status')).toEqual([]);
    expect(campo().value).toBe('/media/pendrive/Backups');
  });

  it('Backup_GrabarSinSesion_NavegaAlIngreso', async () => {
    servidor({ [`POST ${RUTA_API}`]: vacia(401), 'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }) });
    await abrir();

    escribir(PENDRIVE);
    grabar();

    await waitFor(() => expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso'));
  });

  it('Backup_ConsultaSinSesion_NavegaAlIngreso', async () => {
    servidor({ [`GET ${RUTA_API}`]: vacia(401), 'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }) });
    renderizarApp(PANTALLA);

    await waitFor(() => expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso'));
  });

  it('Backup_ConsultaFallida_MensajeGeneralSinFormulario', async () => {
    servidor({ [`GET ${RUTA_API}`]: vacia(500) });
    renderizarApp(PANTALLA);

    await waitFor(() => expect(mensajes('alert')).toEqual([MENSAJE_SIN_CONEXION]));
    expect(within(seccion()).queryByRole('button', { name: BOTON })).toBeNull();
    expect(within(seccion()).queryByLabelText(ETIQUETA)).toBeNull();
    // Las otras secciones siguen funcionando.
    expect(screen.getByRole('button', { name: 'Grabar datos del emisor' })).toBeTruthy();
  });

  it('Backup_SinPalabraConfiguracion', async () => {
    // D-13: el enlace "Configuración" del inicio se busca por subcadena; la sección no repite la palabra.
    servidor({});
    await abrir();

    expect(seccion().textContent ?? '').not.toMatch(/configuraci[oó]n/i);
  });
});
