import { cleanup, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { json, renderizarApp, servidorFalso, vacia } from './pruebas/servidorFalso';

afterEach(cleanup);

describe('App', () => {
  it.each([
    ['definir', false, 'Definí la contraseña de acceso'],
    ['ingreso', true, 'Ingresá al sistema'],
    ['inicio', true, 'Sesión iniciada'],
  ] as const)('App_PantallaDe%s_MuestraElLogo', async (_pantalla, contrasenaDefinida, titulo) => {
    servidorFalso({
      'GET /api/acceso/estado': json(200, { contrasenaDefinida }),
      'GET /api/sesion': vacia(titulo === 'Sesión iniciada' ? 204 : 401),
    });

    renderizarApp('/');

    expect(await screen.findByRole('heading', { name: titulo })).toBeTruthy();
    expect(screen.getByRole('img', { name: 'Logo de la óptica' }).getAttribute('src')).toBe('/api/marca/logo');
  });

  it('App_RutaCambiarContrasena_MuestraLaPantallaConElLogo', async () => {
    servidorFalso({ 'GET /api/sesion': vacia(204) });

    renderizarApp('/cambiar-contrasena');

    expect(await screen.findByRole('heading', { name: 'Cambiar la contraseña' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/cambiar-contrasena');
    expect(screen.getByRole('img', { name: 'Logo de la óptica' }).getAttribute('src')).toBe('/api/marca/logo');
  });
});
