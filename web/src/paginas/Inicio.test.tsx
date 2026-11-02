import { act, cleanup, fireEvent, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { json, renderizarApp, servidorFalso, vacia } from '../pruebas/servidorFalso';

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
