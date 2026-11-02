import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation, useNavigationType } from 'react-router';
import { afterEach, describe, expect, it } from 'vitest';
import { alPerderLaSesion } from '../api/cliente';
import { json, renderizarApp, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';
import { Arranque } from './Arranque';

afterEach(cleanup);

/** Marca la llegada a /ingreso con la ruta y el tipo de navegación (PUSH, REPLACE o POP). */
function DestinoIngreso() {
  const ubicacion = useLocation();
  const tipo = useNavigationType();
  return <output data-testid="destino-ingreso">{`${ubicacion.pathname} ${tipo}`}</output>;
}

describe('Arranque', () => {
  it('Arranque_SinContrasena_MuestraDefinir', async () => {
    const servidor = servidorFalso({ 'GET /api/acceso/estado': json(200, { contrasenaDefinida: false }) });

    renderizarApp('/');

    expect(await screen.findByRole('heading', { name: 'Definí la contraseña de acceso' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/definir-contrasena');
    expect(screen.queryByRole('heading', { name: 'Ingresá al sistema' })).toBeNull();
    // Sin contraseña no hay sesión que consultar.
    expect(servidor.pedidosA('GET', '/api/sesion')).toHaveLength(0);
  });

  it('Arranque_SinSesion_MuestraIngreso', async () => {
    servidorFalso({
      'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }),
      'GET /api/sesion': vacia(401),
    });

    renderizarApp('/');

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
    expect(screen.queryByText('Sesión iniciada')).toBeNull();
  });

  it('Arranque_ConSesion_MuestraInicio', async () => {
    servidorFalso({
      'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }),
      'GET /api/sesion': vacia(204),
    });

    renderizarApp('/');

    expect(await screen.findByRole('heading', { name: 'Sesión iniciada' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Salir' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/');
  });

  it('Arranque_RutaDesconocida_VuelveAlArranque', async () => {
    servidorFalso({ 'GET /api/acceso/estado': json(200, { contrasenaDefinida: false }) });

    renderizarApp('/presupuestos/123');

    expect(await screen.findByRole('heading', { name: 'Definí la contraseña de acceso' })).toBeTruthy();
  });

  it('Arranque_SinSesionSinManejadorGlobal_NavegaAIngresoConReplace', async () => {
    // Arranque solo, sin App: nadie registró el manejador global de "sin sesión". La navegación tiene que salir del
    // propio arranque, y reemplazando la entrada.
    alPerderLaSesion(null);
    servidorFalso({
      'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }),
      'GET /api/sesion': vacia(401),
    });

    render(
      <MemoryRouter initialEntries={['/previa', '/']} initialIndex={1}>
        <Routes>
          <Route path="/" element={<Arranque />} />
          <Route path="/ingreso" element={<DestinoIngreso />} />
        </Routes>
      </MemoryRouter>,
    );

    const destino = await screen.findByTestId('destino-ingreso');
    expect(destino.textContent).toBe('/ingreso REPLACE');
  });

  it('Arranque_SinRed_MensajeGeneralYReintento', async () => {
    const servidor = servidorFalso({
      'GET /api/acceso/estado': [sinRed, json(200, { contrasenaDefinida: true })],
      'GET /api/sesion': [json(500, { title: 'Error' }), vacia(204)],
    });

    renderizarApp('/');

    expect(await screen.findByText('No se pudo conectar con el sistema. Reintentá.')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));

    // Segundo intento: el estado responde, pero la sesión da 500.
    expect(await screen.findByText('No se pudo conectar con el sistema. Reintentá.')).toBeTruthy();
    await waitFor(() => expect(servidor.pedidosA('GET', '/api/sesion')).toHaveLength(1));
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByRole('heading', { name: 'Sesión iniciada' })).toBeTruthy();
  });
});
