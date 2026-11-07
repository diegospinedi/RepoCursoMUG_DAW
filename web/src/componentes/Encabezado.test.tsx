import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { useMarca } from '../marca/contextoMarca';
import { MarcaProvider } from '../marca/MarcaProvider';
import { json, servidorFalso } from '../pruebas/servidorFalso';
import { Encabezado } from './Encabezado';

function colores() {
  const estilo = document.documentElement.style;
  return {
    primario: estilo.getPropertyValue('--color-primario'),
    fondo: estilo.getPropertyValue('--color-fondo'),
  };
}

function EsEjemplo() {
  const marca = useMarca();
  return <output data-testid="es-ejemplo">{marca === null ? 'cargando' : String(marca.esEjemplo)}</output>;
}

afterEach(() => {
  cleanup();
  document.documentElement.style.removeProperty('--color-primario');
  document.documentElement.style.removeProperty('--color-fondo');
});

describe('Encabezado y marca', () => {
  it('Encabezado_MuestraLogoYAplicaColoresDeMarca', async () => {
    servidorFalso({ 'GET /api/marca': json(200, { primario: '#0903A0', fondo: '#FFFFFF', esEjemplo: false }) });

    render(
      <MarcaProvider>
        <Encabezado />
        <EsEjemplo />
      </MarcaProvider>,
    );

    const logo = screen.getByRole('img', { name: 'Logo de la óptica' });
    expect(logo.getAttribute('src')).toBe('/api/marca/logo');
    await waitFor(() => expect(colores()).toEqual({ primario: '#0903A0', fondo: '#FFFFFF' }));
    expect(screen.getByTestId('es-ejemplo').textContent).toBe('false');
  });

  it('Marca_ColorConFormatoInvalido_NoSeAplica', async () => {
    servidorFalso({
      'GET /api/marca': json(200, { primario: 'red;background:url(x)', fondo: '#FFFFFF', esEjemplo: false }),
    });

    render(
      <MarcaProvider>
        <EsEjemplo />
      </MarcaProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('es-ejemplo').textContent).toBe('false'));
    expect(colores()).toEqual({ primario: '', fondo: '#FFFFFF' });
  });

  it('Marca_FondoConFormatoInvalido_NoSeAplica', async () => {
    servidorFalso({
      'GET /api/marca': json(200, { primario: '#0903A0', fondo: 'white;color:red', esEjemplo: false }),
    });

    render(
      <MarcaProvider>
        <EsEjemplo />
      </MarcaProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('es-ejemplo').textContent).toBe('false'));
    expect(colores()).toEqual({ primario: '#0903A0', fondo: '' });
  });

  it('Marca_SinRed_QuedanLosColoresPorDefecto', async () => {
    // La falla de red la dispara la prueba, dentro de act: act espera a que se resuelva la cadena de promesas que
    // la sigue (cliente y MarcaProvider) y aplica lo que haya cambiado antes de que se compruebe nada.
    let fallarRed: () => void = () => undefined;
    const pedidoPendiente = new Promise<Response>((_, rechazar) => {
      fallarRed = () => rechazar(new TypeError('Failed to fetch'));
    });
    const servidor = servidorFalso({ 'GET /api/marca': () => pedidoPendiente });

    render(
      <MarcaProvider>
        <Encabezado />
        <EsEjemplo />
      </MarcaProvider>,
    );
    expect(servidor.pedidosA('GET', '/api/marca')).toHaveLength(1);

    await act(async () => {
      fallarRed();
      await pedidoPendiente.catch(() => undefined);
    });

    expect(screen.getByTestId('es-ejemplo').textContent).toBe('cargando');
    expect(colores()).toEqual({ primario: '', fondo: '' });
    expect(screen.getByRole('img', { name: 'Logo de la óptica' })).toBeTruthy();
  });
});
