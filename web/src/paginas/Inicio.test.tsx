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
      // FEAT-009: la sección de los datos del emisor, sin cargar (D-22).
      'GET /api/configuracion/emisor': json(200, {
        razonSocial: null,
        domicilioComercial: null,
        cuit: null,
        ingresosBrutos: null,
        inicioActividades: null,
      }),
      // FEAT-010: la sección del backup, con la ubicación nunca elegida.
      'GET /api/configuracion/backup': json(200, { ruta: null }),
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

  it('Inicio: enlace Facturas', async () => {
    // FEAT-009: la búsqueda de facturas no consulta al abrir, como la de presupuestos.
    const servidor = servidorFalso(conSesion);
    renderizarApp('/');
    const enlace = await screen.findByRole('link', { name: 'Facturas' });
    expect(enlace.getAttribute('href')).toBe('/facturas');

    fireEvent.click(enlace);

    expect(await screen.findByRole('heading', { name: 'Buscar facturas' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/facturas');
    expect(servidor.pedidosA('POST', '/api/facturacion/busqueda')).toHaveLength(0);
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

  describe('aviso de backup (FEAT-010)', () => {
    const RUTA_ESTADO = '/api/backup/estado';
    const INICIO_AVISO = 'Falta el backup de ayer.';

    /** Abre el inicio con el estado indicado y espera a que la pantalla haya procesado la respuesta. */
    async function abrirInicio(estado: Parameters<typeof servidorFalso>[0][string]) {
      const servidor = servidorFalso({ ...conSesion, [`GET ${RUTA_ESTADO}`]: estado });
      renderizarApp('/');
      await screen.findByRole('heading', { name: 'Sesión iniciada' });
      await waitFor(() => expect(servidor.pedidosA('GET', RUTA_ESTADO)).toHaveLength(1));
      // La respuesta se procesa después del pedido: un ciclo más para que un aviso que no debe estar tampoco llegue.
      await act(async () => {
        await new Promise((resolver) => setTimeout(resolver, 0));
      });
      return servidor;
    }

    /** El aviso de backup: el `status` de la pantalla que empieza con "Falta el backup de ayer.". */
    function avisoBackup() {
      const pantalla = screen.getByRole('heading', { name: 'Sesión iniciada' }).closest('section');
      return within(pantalla!)
        .queryAllByRole('status')
        .find((elemento) => (elemento.textContent ?? '').startsWith(INICIO_AVISO));
    }

    /**
     * Los `status` que hablan del backup: los de la pantalla, o los de todo el documento si la pantalla ya no está
     * montada. No mira el resto del texto (los enlaces y los títulos pueden nombrar el backup en el futuro).
     */
    function statusDeBackup() {
      const pantalla = screen.queryByRole('heading', { name: 'Sesión iniciada' })?.closest('section');
      const estados = pantalla ? within(pantalla).queryAllByRole('status') : screen.queryAllByRole('status');
      return estados.filter((elemento) => /backup/i.test(elemento.textContent ?? ''));
    }

    it.each([
      [
        'con la ubicación accesible',
        { faltaAyer: true, ultimo: '2026-10-29', ubicacion: 'elegida', accesible: true },
        'Falta el backup de ayer. El último backup es del 29/10/2026.',
      ],
      [
        // AC-18: el pendrive está desconectado; la fecha sale del historial.
        'con la ubicación no accesible',
        { faltaAyer: true, ultimo: '2026-10-29', ubicacion: 'elegida', accesible: false },
        'Falta el backup de ayer. El último backup es del 29/10/2026. La ubicación del backup no está accesible.',
      ],
    ])('Inicio_AvisoBackupConFecha (%s)', async (_caso, estado, texto) => {
      // AC-09: un solo pedido al cargar y el aviso con la fecha del último backup en dd/mm/aaaa.
      const servidor = await abrirInicio(json(200, estado));

      const aviso = avisoBackup();
      expect(aviso?.textContent).toBe(texto);
      // D-13: sin enlaces ni la palabra "configuración" (hay e2e que buscan el enlace "Configuración" por subcadena).
      expect(within(aviso!).queryAllByRole('link')).toHaveLength(0);
      expect(aviso?.textContent?.toLowerCase()).not.toContain('configuración');
      expect(servidor.pedidosA('GET', RUTA_ESTADO)).toHaveLength(1);
      const [pedido] = servidor.pedidosA('GET', RUTA_ESTADO);
      expect(pedido?.init?.credentials).toBe('same-origin');
      // El enlace a la configuración sigue siendo uno solo.
      expect(screen.getAllByRole('link', { name: /configuración/i })).toHaveLength(1);
    });

    it.each([
      [
        // Base nueva, sin ubicación elegida (D-26: el texto de "sin elegir", no el de "no accesible").
        'sin ubicación elegida',
        { faltaAyer: true, ultimo: null, ubicacion: 'sin_elegir', accesible: false },
        'Falta el backup de ayer. No hay ningún backup disponible. No hay una ubicación de backup elegida.',
      ],
      [
        // La ubicación ya se eligió y está accesible, pero todavía no se hizo ningún backup: solo la primera parte.
        'con la ubicación elegida y accesible',
        { faltaAyer: true, ultimo: null, ubicacion: 'elegida', accesible: true },
        'Falta el backup de ayer. No hay ningún backup disponible.',
      ],
    ])('Inicio_AvisoSinNingunBackup (%s)', async (_caso, estado, texto) => {
      // AC-10: no existe ningún backup y falta el de ayer.
      await abrirInicio(json(200, estado));

      const aviso = avisoBackup();
      expect(aviso?.textContent).toBe(texto);
      expect(within(aviso!).queryAllByRole('link')).toHaveLength(0);
      expect(aviso?.textContent?.toLowerCase()).not.toContain('configuración');
    });

    it.each([
      ['con la ubicación accesible', { faltaAyer: false, ultimo: '2026-10-30', ubicacion: 'elegida', accesible: true }],
      ['con la ubicación no accesible', { faltaAyer: false, ultimo: '2026-10-31', ubicacion: 'elegida', accesible: false }],
    ])('Inicio_ConElDeAyer_SinAviso (%s)', async (_caso, estado) => {
      // FR-09: el aviso es por la falta del backup de ayer; sin esa falta no se muestra nada.
      await abrirInicio(json(200, estado));

      const pantalla = screen.getByRole('heading', { name: 'Sesión iniciada' }).closest('section');
      expect(within(pantalla!).queryByRole('status')).toBeNull();
      expect(statusDeBackup()).toEqual([]);
    });

    it.each([
      ['404', vacia(404)],
      ['500', vacia(500)],
      ['falla de red', sinRed],
      ['respuesta ilegible', json(200, { faltaAyer: 'sí', ultimo: null, ubicacion: 'sin_elegir', accesible: false })],
    ])('Inicio_EstadoFalla_SinAviso (%s)', async (_caso, respuesta) => {
      const errores = vi.spyOn(console, 'error');
      const advertencias = vi.spyOn(console, 'warn');

      await abrirInicio(respuesta);

      // Si el estado falla no se muestra nada: ni el aviso, ni un error, ni nada en la consola.
      const pantalla = screen.getByRole('heading', { name: 'Sesión iniciada' }).closest('section');
      expect(within(pantalla!).queryByRole('status')).toBeNull();
      expect(within(pantalla!).queryByRole('alert')).toBeNull();
      expect(statusDeBackup()).toEqual([]);
      expect(screen.getByTestId('ubicacion').textContent).toBe('/');
      expect(errores).not.toHaveBeenCalled();
      expect(advertencias).not.toHaveBeenCalled();
    });

    it('Inicio_AvisoBackupYEmisionesPendientes_Juntos', async () => {
      // Los dos avisos son independientes: cada uno con su pedido y su status.
      servidorFalso({
        ...conSesion,
        [`GET ${RUTA_ESTADO}`]: json(200, { faltaAyer: true, ultimo: null, ubicacion: 'sin_elegir', accesible: false }),
        'GET /api/facturacion/emisiones-pendientes': json(200, { presupuestos: [155] }),
      });
      renderizarApp('/');

      expect(await screen.findByText(/^Falta el backup de ayer\./)).toBeTruthy();
      expect(await screen.findByRole('link', { name: '155' })).toBeTruthy();
      const pantalla = screen.getByRole('heading', { name: 'Sesión iniciada' }).closest('section');
      expect(within(pantalla!).getAllByRole('status')).toHaveLength(2);
    });

    it('Inicio_AvisoBackup_SalirAntesDeLaRespuesta_NoActualiza', async () => {
      // Guarda de desmontaje: la respuesta que llega después de dejar la pantalla no se usa.
      let responder: (respuesta: Response) => void = () => {};
      const errores = vi.spyOn(console, 'error');
      const servidor = servidorFalso({
        ...conSesion,
        [`GET ${RUTA_ESTADO}`]: () =>
          new Promise<Response>((resolver) => {
            responder = resolver;
          }),
      });
      const { unmount } = renderizarApp('/');
      await screen.findByRole('heading', { name: 'Sesión iniciada' });
      await waitFor(() => expect(servidor.pedidosA('GET', RUTA_ESTADO)).toHaveLength(1));

      unmount();
      await act(async () => {
        responder(
          new Response(JSON.stringify({ faltaAyer: true, ultimo: null, ubicacion: 'sin_elegir', accesible: false }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
        await new Promise((resolver) => setTimeout(resolver, 0));
      });

      expect(statusDeBackup()).toEqual([]);
      expect(errores).not.toHaveBeenCalled();
    });
  });

  describe('menú de tarjetas (FEAT-012)', () => {
    /** La pantalla de Inicio: la sección de la que el h1 es hijo directo. */
    function pantalla() {
      const titulo = screen.getByRole('heading', { level: 1, name: 'Sesión iniciada' });
      return titulo.parentElement!;
    }

    /** Verdadero si `a` está antes que `b` en el documento. */
    function antesDe(a: Node, b: Node) {
      return (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;
    }

    it('Inicio_AvisosAntesDelMenu', async () => {
      // AC-08: los avisos se dibujan antes de las tarjetas; el h1 sigue siendo hijo directo de la sección.
      servidorFalso({
        ...conSesion,
        'GET /api/backup/estado': json(200, { faltaAyer: true, ultimo: null, ubicacion: 'sin_elegir', accesible: false }),
        'GET /api/facturacion/emisiones-pendientes': json(200, { presupuestos: [155] }),
      });
      renderizarApp('/');
      await screen.findByText(/^Falta el backup de ayer\./);
      await screen.findByRole('link', { name: '155' });

      const seccion = pantalla();
      expect(seccion.tagName).toBe('SECTION');
      expect(seccion.classList.contains('pantalla')).toBe(true);
      expect(seccion.classList.contains('pantalla--menu')).toBe(true);
      const avisos = within(seccion).getAllByRole('status');
      expect(avisos).toHaveLength(2);
      const regiones = within(seccion).getAllByRole('region');
      expect(regiones.map((region) => region.querySelector('h2')?.textContent)).toEqual(['Ventas', 'Catálogo', 'Sistema']);
      for (const aviso of avisos) {
        expect(antesDe(aviso, regiones[0]!)).toBe(true);
      }
      // Las tarjetas no son avisos: ni role status/alert ni la clase aviso.
      for (const region of regiones) {
        expect(within(region).queryByRole('status')).toBeNull();
        expect(within(region).queryByRole('alert')).toBeNull();
        expect(region.querySelector('.aviso')).toBeNull();
      }
      // "Salir" va al final, después del menú.
      const botonSalir = within(seccion).getByRole('button', { name: 'Salir' });
      expect(antesDe(regiones[2]!, botonSalir)).toBe(true);
    });

    it('Inicio_SalirSigueCerrandoLaSesion', async () => {
      // AC-09: "Salir" es un botón secundario en la barra final y sigue cerrando la sesión.
      const servidor = servidorFalso({ ...conSesion, 'POST /api/acceso/salir': vacia(204) });
      renderizarApp('/');
      const botonSalir = await screen.findByRole('button', { name: 'Salir' });
      expect(botonSalir.tagName).toBe('BUTTON');
      expect(botonSalir.textContent).toBe('Salir');
      expect(botonSalir.classList.contains('boton-secundario')).toBe(true);
      expect(botonSalir.parentElement?.classList.contains('inicio__acciones')).toBe(true);
      expect(botonSalir.parentElement?.parentElement).toBe(pantalla());

      fireEvent.click(botonSalir);

      expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
      expect(servidor.pedidosA('POST', '/api/acceso/salir')).toHaveLength(1);
    });

    it('Inicio_SalirConError_AlertaJuntoAlBoton', async () => {
      // El error de "Salir" se muestra en la barra final, justo antes del botón que lo produjo.
      servidorFalso({ ...conSesion, 'POST /api/acceso/salir': vacia(415) });
      renderizarApp('/');
      const botonSalir = await screen.findByRole('button', { name: 'Salir' });

      fireEvent.click(botonSalir);

      const alerta = await within(pantalla()).findByRole('alert');
      expect(alerta.textContent).toBe('No se pudo completar la operación. Recargá la página y reintentá.');
      expect(alerta.parentElement).toBe(botonSalir.parentElement);
      expect(alerta.parentElement?.classList.contains('inicio__acciones')).toBe(true);
      expect(alerta.nextElementSibling).toBe(botonSalir);
    });

    it('Inicio_ConsultasFallidas_MenuSinAvisos', async () => {
      // Si fallan las dos consultas, el menú se dibuja entero y no aparece ningún aviso.
      const servidor = servidorFalso({
        ...conSesion,
        'GET /api/backup/estado': vacia(500),
        'GET /api/facturacion/emisiones-pendientes': sinRed,
      });
      renderizarApp('/');
      await screen.findByRole('heading', { name: 'Sesión iniciada' });
      await waitFor(() => expect(servidor.pedidosA('GET', '/api/backup/estado')).toHaveLength(1));
      await waitFor(() => expect(servidor.pedidosA('GET', '/api/facturacion/emisiones-pendientes')).toHaveLength(1));
      await act(async () => {
        await new Promise((resolver) => setTimeout(resolver, 0));
      });

      const seccion = pantalla();
      expect(within(seccion).getAllByRole('region')).toHaveLength(3);
      expect(seccion.querySelectorAll('li.tarjeta-menu')).toHaveLength(7);
      expect(within(seccion).queryByRole('status')).toBeNull();
      expect(within(seccion).queryByRole('alert')).toBeNull();
      expect(seccion.querySelector('[style], style')).toBeNull();
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
