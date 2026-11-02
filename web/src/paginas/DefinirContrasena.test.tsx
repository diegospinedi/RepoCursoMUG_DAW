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
  vacia,
} from '../pruebas/servidorFalso';

// En todas las pruebas: la contraseña escrita no queda en el almacenamiento del navegador ni en la URL.
afterEach(comprobarQueLaContrasenaNoQuedoYLimpiar);

const MENSAJE_CORTA = 'La contraseña debe tener al menos 8 caracteres. Ingresá una más larga.';
const MENSAJE_LARGA = 'La contraseña puede tener hasta 128 caracteres. Ingresá una más corta.';

const escribirYEnviar = (valor: string) => escribirYEnviarEn(valor, 'Definir contraseña');

const conInicio = {
  'POST /api/acceso/definir': vacia(201),
  'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }),
  'GET /api/sesion': vacia(204),
};

describe('DefinirContrasena', () => {
  it('DefinirContrasena_7Caracteres_RechazadaConMensajeJuntoAlCampo', async () => {
    const servidor = servidorFalso({ 'POST /api/acceso/definir': vacia(201) });
    renderizarApp('/definir-contrasena');

    const campo = await escribirYEnviar('1234567');

    await waitFor(() => expect(mensajeJuntoAlCampo(campo)).toBe(MENSAJE_CORTA));
    expect(campo.getAttribute('aria-invalid')).toBe('true');
    expect(servidor.pedidosA('POST', '/api/acceso/definir')).toHaveLength(0);
  });

  it('DefinirContrasena_8UnidadesQueEnNfcSon7_Rechazada', async () => {
    // "n" + tilde combinante (2 unidades) se normaliza a "ñ" (1 unidad): 8 unidades crudas, 7 en NFC.
    const servidor = servidorFalso({ 'POST /api/acceso/definir': vacia(201) });
    renderizarApp('/definir-contrasena');

    const campo = await escribirYEnviar('abcdeñf');

    await waitFor(() => expect(mensajeJuntoAlCampo(campo)).toBe(MENSAJE_CORTA));
    expect(servidor.pedidosA('POST', '/api/acceso/definir')).toHaveLength(0);
  });

  it('DefinirContrasena_129Caracteres_RechazadaConMensajeJuntoAlCampo', async () => {
    const servidor = servidorFalso({ 'POST /api/acceso/definir': vacia(201) });
    renderizarApp('/definir-contrasena');

    const campo = await escribirYEnviar('a'.repeat(129));

    await waitFor(() => expect(mensajeJuntoAlCampo(campo)).toBe(MENSAJE_LARGA));
    expect(servidor.pedidosA('POST', '/api/acceso/definir')).toHaveLength(0);
  });

  it('DefinirContrasena_129UnidadesQueEnNfcSon128_Aceptada', async () => {
    // El tope se cuenta en NFC: 127 "a" + "n" con tilde combinante son 129 unidades crudas y 128 en NFC.
    const valor = 'a'.repeat(127) + 'ñ';
    expect(valor.length).toBe(129);
    const servidor = servidorFalso(conInicio);
    renderizarApp('/definir-contrasena');

    const campo = await escribirYEnviar(valor);

    expect(await screen.findByRole('heading', { name: 'Sesión iniciada' })).toBeTruthy();
    expect(mensajeJuntoAlCampo(campo)).toBeNull();
    expect(servidor.pedidosA('POST', '/api/acceso/definir')).toHaveLength(1);
  });

  it('DefinirContrasena_Error400_InvalidoMuestraMensajeDelServidor', async () => {
    const mensaje = 'La contraseña tiene caracteres no válidos. Escribila de nuevo.';
    servidorFalso({ 'POST /api/acceso/definir': problema(400, 'contrasena', mensaje) });
    renderizarApp('/definir-contrasena');

    const campo = await escribirYEnviar('contraseña válida');

    await waitFor(() => expect(mensajeJuntoAlCampo(campo)).toBe(mensaje));
    expect(campo.getAttribute('aria-invalid')).toBe('true');
    expect(screen.getByTestId('ubicacion').textContent).toBe('/definir-contrasena');
  });

  it('DefinirContrasena_Correcta_EnviaJsonYEntraAlInicio', async () => {
    const servidor = servidorFalso(conInicio);
    renderizarApp('/definir-contrasena');
    // Defensa en profundidad: si el envío por JavaScript fallara, el navegador no pone la contraseña en la URL.
    expect((await screen.findByRole('button', { name: 'Definir contraseña' })).closest('form')?.getAttribute('method')).toBe(
      'post',
    );

    const campo = await escribirYEnviar('ocho1234');

    expect(await screen.findByRole('heading', { name: 'Sesión iniciada' })).toBeTruthy();
    const [pedido] = servidor.pedidosA('POST', '/api/acceso/definir');
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(new Headers(pedido?.init?.headers).get('Content-Type')).toBe('application/json');
    expect(JSON.parse(String(pedido?.init?.body))).toEqual({ contrasena: 'ocho1234' });
    // La contraseña no queda en pantalla (el almacenamiento y la URL los comprueba el afterEach).
    expect(screen.getByTestId('ubicacion').textContent).toBe('/');
    expect(campo.isConnected).toBe(false);
    expect(campo.getAttribute('autocomplete')).toBe('new-password');
    expect(campo.getAttribute('type')).toBe('password');
  });

  it('DefinirContrasena_YaDefinida409_MensajeParaIngresar', async () => {
    servidorFalso({
      'POST /api/acceso/definir': problemaGeneral(409, 'Ya hay una contraseña definida. Ingresá con ella.'),
    });
    renderizarApp('/definir-contrasena');

    await escribirYEnviar('ocho1234');

    expect(await screen.findByText('Ya hay una contraseña definida. Ingresá con ella.')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Ir al ingreso' }).getAttribute('href')).toBe('/ingreso');
  });

  it('DefinirContrasena_Limitador429_MensajeDeEspera', async () => {
    servidorFalso({ 'POST /api/acceso/definir': vacia(429) });
    renderizarApp('/definir-contrasena');

    await escribirYEnviar('ocho1234');

    expect(
      await screen.findByText('El sistema está ocupado. Intentá de nuevo en un momento.'),
    ).toBeTruthy();
  });
});
