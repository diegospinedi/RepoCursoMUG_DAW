import { cleanup } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { json, renderizarApp, servidorFalso } from './servidorFalso';

afterEach(cleanup);

describe('renderizarApp (ayuda de prueba)', () => {
  it('Volver_SinRouterMontado_LanzaError', async () => {
    // Sin router no hay historial: un volver() que no hiciera nada dejaría pasar una prueba de Atrás sin probarla.
    servidorFalso({ 'GET /api/acceso/estado': json(200, { contrasenaDefinida: false }) });
    const { volver, unmount } = renderizarApp('/');
    unmount();

    await expect(volver()).rejects.toThrow('volver(): no hay un router montado con renderizarApp.');
  });
});
