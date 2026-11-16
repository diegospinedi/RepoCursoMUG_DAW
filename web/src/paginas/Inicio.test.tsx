import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { json, renderizarApp, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';

afterEach(cleanup);

const conSesion = {
  'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }),
  'GET /api/sesion': vacia(204),
};

describe('Inicio', () => {
  it('Cliente_401_NoAutorizadoNavegaAIngreso', async () => {
    // La sesión venció mientras la pantalla estaba abierta: la siguiente acción recibe un 401 vacío.
    servidorFalso({ ...conSesion, 'POST /api/acceso/salir': vacia(401) });
    renderizarApp('/');
    fireEvent.click(await screen.findByRole('button', { name: 'Salir' }));

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
    expect(screen.queryByText('Sesión iniciada')).toBeNull();
  });

  it('Cliente_401_ReemplazaLaEntradaYAtrasNoVuelveALaPantallaProtegida', async () => {
    // Historial: una entrada previa distinta de /ingreso y después el inicio. El 401 reemplaza la entrada del inicio
    // por /ingreso, así que el historial queda [/definir-contrasena, /ingreso]. La entrada previa es distinta de
    // /ingreso a propósito: si fuera /ingreso, un Atrás que no hiciera nada también "llegaría" ahí.
    const servidor = servidorFalso({ ...conSesion, 'POST /api/acceso/salir': vacia(401) });
    const { volver } = renderizarApp(['/definir-contrasena', '/']);
    fireEvent.click(await screen.findByRole('button', { name: 'Salir' }));
    await screen.findByRole('heading', { name: 'Ingresá al sistema' });
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');

    await volver();

    // Atrás lleva a la primera entrada, no a la pantalla protegida (/) que el 401 reemplazó.
    expect(screen.getByTestId('ubicacion').textContent).toBe('/definir-contrasena');
    expect(screen.getByTestId('ubicacion').textContent).not.toBe('/');
    expect(screen.getByRole('heading', { name: 'Definí la contraseña de acceso' })).toBeTruthy();
    expect(screen.queryByText('Sesión iniciada')).toBeNull();
    expect(servidor.pedidosA('GET', '/api/sesion')).toHaveLength(1);
  });

  it('Salir_Correcto_EnviaJsonSinCuerpoYVaAIngreso', async () => {
    const servidor = servidorFalso({ ...conSesion, 'POST /api/acceso/salir': vacia(204) });
    renderizarApp('/');
    fireEvent.click(await screen.findByRole('button', { name: 'Salir' }));

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    const [pedido] = servidor.pedidosA('POST', '/api/acceso/salir');
    // La Api exige Content-Type: application/json en todo POST, aunque no lleve cuerpo.
    expect(new Headers(pedido?.init?.headers).get('Content-Type')).toBe('application/json');
    expect(pedido?.init?.body).toBeUndefined();
    expect(pedido?.init?.credentials).toBe('same-origin');
  });

  it('Salir_ErrorDelServidor_MensajeGeneralYSigueEnInicio', async () => {
    servidorFalso({ ...conSesion, 'POST /api/acceso/salir': vacia(415) });
    renderizarApp('/');
    fireEvent.click(await screen.findByRole('button', { name: 'Salir' }));

    expect(
      await screen.findByText('No se pudo completar la operación. Recargá la página y reintentá.'),
    ).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Sesión iniciada' })).toBeTruthy();
  });

  it('Inicio_EnlaceCambiarLaContrasena_LlevaALaPantallaDeCambio', async () => {
    servidorFalso(conSesion);
    renderizarApp('/');
    const enlace = await screen.findByRole('link', { name: 'Cambiar la contraseña' });
    expect(enlace.getAttribute('href')).toBe('/cambiar-contrasena');
    // Sin un cambio previo, el inicio no muestra el mensaje de éxito.
    expect(screen.queryByText('La contraseña se cambió.')).toBeNull();

    fireEvent.click(enlace);

    expect(await screen.findByRole('heading', { name: 'Cambiar la contraseña' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/cambiar-contrasena');
  });

  it('Inicio_EnlaceConfiguracion', async () => {
    servidorFalso({
      ...conSesion,
      'GET /api/configuracion': json(200, {
        alicuotaIva: 21,
        condicionFiscal: 'responsable_inscripto',
        multiploRedondeo: 0.01,
        topeIdentificacion: null,
        faltaTope: true,
        puntoVenta: null,
        alicuotasAdmitidas: [0, 2.5, 5, 10.5, 21, 27],
      }),
    });
    renderizarApp('/');
    const enlace = await screen.findByRole('link', { name: 'Configuración' });
    expect(enlace.getAttribute('href')).toBe('/configuracion');

    fireEvent.click(enlace);

    expect(await screen.findByRole('heading', { name: 'Configuración' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/configuracion');
  });

  it('Inicio_EnlaceNuevoArticulo', async () => {
    servidorFalso(conSesion);
    renderizarApp('/');
    const enlace = await screen.findByRole('link', { name: 'Nuevo artículo' });
    expect(enlace.getAttribute('href')).toBe('/articulos/nuevo');

    fireEvent.click(enlace);

    expect(await screen.findByRole('heading', { name: 'Nuevo artículo' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/articulos/nuevo');
  });

  it('Inicio_EnlaceNuevoPresupuesto', async () => {
    // D-21.
    servidorFalso(conSesion);
    renderizarApp('/');
    const enlace = await screen.findByRole('link', { name: 'Nuevo presupuesto' });
    expect(enlace.getAttribute('href')).toBe('/presupuestos/nuevo');

    fireEvent.click(enlace);

    expect(await screen.findByRole('heading', { name: 'Nuevo presupuesto' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/presupuestos/nuevo');
  });

  it('Inicio: enlace Buscar presupuestos', async () => {
    // FEAT-006: la búsqueda no consulta al abrir, así que no hace falta preparar la Api.
    const servidor = servidorFalso(conSesion);
    renderizarApp('/');
    const enlace = await screen.findByRole('link', { name: 'Buscar presupuestos' });
    expect(enlace.getAttribute('href')).toBe('/presupuestos');

    fireEvent.click(enlace);

    expect(await screen.findByRole('heading', { name: 'Buscar presupuestos' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/presupuestos');
    expect(servidor.pedidosA('POST', '/api/presupuestos/busqueda')).toHaveLength(0);
  });

  it('Inicio_EnlaceArticulos', async () => {
    servidorFalso({
      ...conSesion,
      'GET /api/articulos?pagina=1': json(200, { articulos: [], pagina: 1, tamanoPagina: 50, total: 0 }),
    });
    renderizarApp('/');
    const enlace = await screen.findByRole('link', { name: 'Artículos' });
    expect(enlace.getAttribute('href')).toBe('/articulos');

    fireEvent.click(enlace);

    expect(await screen.findByRole('heading', { name: 'Artículos' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/articulos');
  });

  it.each([
    ['sin mensaje', { otro: 'La contraseña se cambió.' }],
    ['mensaje que no es texto', { mensaje: 42 }],
    ['estado que no es objeto', 'La contraseña se cambió.'],
  ])('Inicio_EstadoDeNavegacionAjeno_%s_NoMuestraAviso', async (_caso, state) => {
    servidorFalso(conSesion);
    renderizarApp([{ pathname: '/', state }]);

    const pantalla = (await screen.findByRole('heading', { name: 'Sesión iniciada' })).closest('section');
    // Dentro de la pantalla: la sonda de ubicación de las pruebas también tiene el rol status.
    expect(within(pantalla!).queryByRole('status')).toBeNull();
  });

  describe('aviso de emisiones pendientes (FEAT-008)', () => {
    const RUTA_PENDIENTES = '/api/facturacion/emisiones-pendientes';
    const INICIO_AVISO = 'Hay emisiones pendientes de verificación';

    /** Abre el inicio con la lista indicada y espera a que la pantalla haya procesado la respuesta. */
    async function abrirInicio(lista: Parameters<typeof servidorFalso>[0][string]) {
      const servidor = servidorFalso({ ...conSesion, [`GET ${RUTA_PENDIENTES}`]: lista });
      renderizarApp('/');
      await screen.findByRole('heading', { name: 'Sesión iniciada' });
      await waitFor(() => expect(servidor.pedidosA('GET', RUTA_PENDIENTES)).toHaveLength(1));
      // La respuesta se procesa después del pedido: un ciclo más para que un aviso que no debe estar tampoco llegue.
      await act(async () => {
        await new Promise((resolver) => setTimeout(resolver, 0));
      });
      return servidor;
    }

    it('Inicio_ConPendientes_Aviso155y160', async () => {
      // AC-03: un solo pedido al cargar; el aviso con los números y un enlace a cada presupuesto.
      const servidor = await abrirInicio(json(200, { presupuestos: [155, 160] }));

      const aviso = await screen.findByText((_texto, elemento) =>
        elemento?.tagName === 'P' && (elemento.textContent ?? '').startsWith(INICIO_AVISO),
      );
      expect(aviso.textContent).toBe('Hay emisiones pendientes de verificación en los presupuestos 155 y 160.');
      expect(within(aviso).getByRole('link', { name: '155' }).getAttribute('href')).toBe('/presupuestos/155');
      expect(within(aviso).getByRole('link', { name: '160' }).getAttribute('href')).toBe('/presupuestos/160');
      expect(servidor.pedidosA('GET', RUTA_PENDIENTES)).toHaveLength(1);
      const [pedido] = servidor.pedidosA('GET', RUTA_PENDIENTES);
      expect(pedido?.init?.credentials).toBe('same-origin');
    });

    it.each([
      [[155], 'Hay emisiones pendientes de verificación en el presupuesto 155.'],
      [[155, 160, 170], 'Hay emisiones pendientes de verificación en los presupuestos 155, 160 y 170.'],
    ])('Inicio_ConPendientes_ListaEnCastellano (%j)', async (presupuestos, texto) => {
      await abrirInicio(json(200, { presupuestos }));

      const aviso = await screen.findByText((_texto, elemento) =>
        elemento?.tagName === 'P' && (elemento.textContent ?? '').startsWith(INICIO_AVISO),
      );
      expect(aviso.textContent).toBe(texto);
      expect(within(aviso).getAllByRole('link')).toHaveLength(presupuestos.length);
    });

    it('Inicio_ConPendientes_EnlaceAbreElPresupuesto', async () => {
      servidorFalso({
        ...conSesion,
        [`GET ${RUTA_PENDIENTES}`]: json(200, { presupuestos: [155, 160] }),
        'GET /api/presupuestos/160': vacia(404),
      });
      renderizarApp('/');

      fireEvent.click(await screen.findByRole('link', { name: '160' }));

      expect(await screen.findByRole('heading', { name: 'Presupuesto' })).toBeTruthy();
      expect(screen.getByTestId('ubicacion').textContent).toBe('/presupuestos/160');
    });

    it('Inicio_SinPendientes_SinAviso', async () => {
      // AC-04.
      await abrirInicio(json(200, { presupuestos: [] }));

      expect(screen.queryByText(INICIO_AVISO, { exact: false })).toBeNull();
      const pantalla = screen.getByRole('heading', { name: 'Sesión iniciada' }).closest('section');
      expect(within(pantalla!).queryByRole('status')).toBeNull();
    });

    it.each([
      ['404', vacia(404)],
      ['500', vacia(500)],
      ['falla de red', sinRed],
      ['respuesta ilegible', json(200, { presupuestos: ['155'] })],
    ])('Inicio_FallaAlListar_SinAvisoNiError (%s)', async (_caso, respuesta) => {
      await abrirInicio(respuesta);

      expect(screen.queryByText(INICIO_AVISO, { exact: false })).toBeNull();
      const pantalla = screen.getByRole('heading', { name: 'Sesión iniciada' }).closest('section');
      expect(within(pantalla!).queryByRole('alert')).toBeNull();
      expect(within(pantalla!).queryByRole('status')).toBeNull();
      expect(screen.getByTestId('ubicacion').textContent).toBe('/');
    });
  });

  describe('con reloj falso', () => {
    // El reloj falso se activa ANTES de renderizar: así captura cualquier setInterval o setTimeout que la pantalla
    // programe al montarse. No se usan findBy/waitFor, que esperan con esos mismos temporizadores.
    beforeEach(() => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'] });
    });
    afterEach(() => {
      vi.useRealTimers();
    });

    /** Avanza el reloj falso y deja que React procese lo que eso dispare. */
    const avanzar = (ms: number) =>
      act(async () => {
        await vi.advanceTimersByTimeAsync(ms);
      });

    it('Inicio_NoConsultaPeriodicamente', async () => {
      // Cualquier pedido con cookie renueva la sesión: una consulta periódica la mantendría viva sola.
      const servidor = servidorFalso(conSesion);
      renderizarApp('/');
      for (let i = 0; i < 50 && screen.queryByRole('heading', { name: 'Sesión iniciada' }) === null; i++) {
        await avanzar(0);
      }
      expect(screen.getByRole('heading', { name: 'Sesión iniciada' })).toBeTruthy();
      const pedidos = servidor.llamadas.length;

      // Más de una hora: alcanza para un intervalo de segundos o minutos y para un setTimeout que se reprograma.
      await avanzar(61 * 60 * 1000);

      expect(servidor.llamadas.slice(pedidos)).toEqual([]);
      expect(servidor.pedidosA('GET', '/api/sesion')).toHaveLength(1);
      expect(vi.getTimerCount()).toBe(0);
    });
  });
});
