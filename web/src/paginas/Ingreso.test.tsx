import { screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import {
  comprobarQueLaContrasenaNoQuedoYLimpiar,
  escribirYEnviar as escribirYEnviarEn,
  mensajeJuntoAlCampo,
} from '../pruebas/formulario';
import {
  json,
  problema,
  problemaGeneral,
  renderizarApp,
  servidorFalso,
  sinRed,
  vacia,
} from '../pruebas/servidorFalso';

// En todas las pruebas: la contraseña escrita no queda en el almacenamiento del navegador ni en la URL.
afterEach(comprobarQueLaContrasenaNoQuedoYLimpiar);

const escribirYEnviar = (valor: string) => escribirYEnviarEn(valor, 'Ingresar');

describe('Ingreso', () => {
  it('Ingreso_ContrasenaIncorrecta_RechazadaConMensaje', async () => {
    const mensaje = 'La contraseña no es correcta. Volvé a ingresarla.';
    servidorFalso({ 'POST /api/acceso/ingresar': problema(401, 'contrasena', mensaje) });
    renderizarApp('/ingreso');

    const campo = await escribirYEnviar('incorrecta');

    await waitFor(() => expect(mensajeJuntoAlCampo(campo)).toBe(mensaje));
    expect(campo.getAttribute('aria-invalid')).toBe('true');
    // Un 401 con cuerpo no es "sin sesión": no se sale de la pantalla ni se pierde el campo.
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
    expect(campo.isConnected).toBe(true);
  });

  it('Cliente_ErrorDeRed_MuestraMensajeGeneral', async () => {
    servidorFalso({ 'POST /api/acceso/ingresar': sinRed });
    renderizarApp('/ingreso');

    const campo = await escribirYEnviar('ocho1234');

    const alerta = await screen.findByText('No se pudo conectar con el sistema. Reintentá.');
    expect(alerta.getAttribute('role')).toBe('alert');
    // Es un mensaje general, no del campo.
    expect(mensajeJuntoAlCampo(campo)).toBeNull();
    expect(screen.getByRole('button', { name: 'Ingresar' }).hasAttribute('disabled')).toBe(false);
  });

  it('Ingreso_Error500_MuestraMensajeGeneral', async () => {
    servidorFalso({ 'POST /api/acceso/ingresar': json(500, { title: 'Error' }) });
    renderizarApp('/ingreso');

    await escribirYEnviar('ocho1234');

    expect(await screen.findByText('No se pudo conectar con el sistema. Reintentá.')).toBeTruthy();
  });

  it('Ingreso_SinContrasenaDefinida409_MensajeParaDefinir', async () => {
    servidorFalso({ 'POST /api/acceso/ingresar': problemaGeneral(409, 'Definí la contraseña primero') });
    renderizarApp('/ingreso');

    await escribirYEnviar('ocho1234');

    expect(await screen.findByText('Todavía no hay una contraseña. Definí la contraseña primero.')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Definir la contraseña' }).getAttribute('href')).toBe(
      '/definir-contrasena',
    );
  });

  it('Ingreso_Bloqueado423_MuestraMensajeDeBloqueo', async () => {
    servidorFalso({
      'POST /api/acceso/ingresar': problemaGeneral(423, 'El acceso está bloqueado temporalmente. Volvé a intentar en unos minutos.'),
    });
    renderizarApp('/ingreso');

    const campo = await escribirYEnviar('ocho1234');

    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toBe('El acceso está bloqueado temporalmente. Volvé a intentar en unos minutos.');
    // Es un mensaje general: no se navega ni se vacía la pantalla.
    expect(mensajeJuntoAlCampo(campo)).toBeNull();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
    expect(campo.isConnected).toBe(true);
    expect(screen.getByRole('button', { name: 'Ingresar' }).hasAttribute('disabled')).toBe(false);
  });

  it('Ingreso_7Caracteres_NoSeEnvia', async () => {
    const servidor = servidorFalso({ 'POST /api/acceso/ingresar': vacia(204) });
    renderizarApp('/ingreso');

    const campo = await escribirYEnviar('1234567');

    await waitFor(() =>
      expect(mensajeJuntoAlCampo(campo)).toBe(
        'La contraseña debe tener al menos 8 caracteres. Ingresá una más larga.',
      ),
    );
    expect(servidor.pedidosA('POST', '/api/acceso/ingresar')).toHaveLength(0);
  });

  it('Ingreso_Correcta_EntraAlInicio', async () => {
    const servidor = servidorFalso({
      'POST /api/acceso/ingresar': vacia(204),
      'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }),
      'GET /api/sesion': vacia(204),
    });
    renderizarApp('/ingreso');
    expect((await screen.findByRole('button', { name: 'Ingresar' })).closest('form')?.getAttribute('method')).toBe(
      'post',
    );

    const campo = await escribirYEnviar('ocho1234');

    expect(await screen.findByRole('heading', { name: 'Sesión iniciada' })).toBeTruthy();
    expect(campo.getAttribute('autocomplete')).toBe('current-password');
    const [pedido] = servidor.pedidosA('POST', '/api/acceso/ingresar');
    expect(JSON.parse(String(pedido?.init?.body))).toEqual({ contrasena: 'ocho1234' });
    expect(screen.getByTestId('ubicacion').textContent).toBe('/');
  });
});
