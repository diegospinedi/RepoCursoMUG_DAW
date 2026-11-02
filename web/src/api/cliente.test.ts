import { afterEach, describe, expect, it, vi } from 'vitest';
import { json, problema, problemaGeneral, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';
import {
  alPerderLaSesion,
  consultarEstado,
  consultarMarca,
  consultarSesion,
  definirContrasena,
  ingresar,
  MENSAJE_SIN_CONEXION,
  salir,
} from './cliente';

afterEach(() => alPerderLaSesion(null));

describe('cliente de la Api', () => {
  it('Cliente_401Vacio_AvisaSinSesion', async () => {
    servidorFalso({ 'GET /api/sesion': vacia(401) });
    const manejador = vi.fn();
    alPerderLaSesion(manejador);

    expect(await consultarSesion()).toEqual({ tipo: 'sinSesion' });
    expect(manejador).toHaveBeenCalledTimes(1);
  });

  it('Cliente_401ConErrores_EsErrorDelCampoYNoSinSesion', async () => {
    servidorFalso({ 'POST /api/acceso/ingresar': problema(401, 'contrasena', 'No es correcta.') });
    const manejador = vi.fn();
    alPerderLaSesion(manejador);

    expect(await ingresar('ocho1234')).toEqual({ tipo: 'campo', campo: 'contrasena', mensaje: 'No es correcta.' });
    expect(manejador).not.toHaveBeenCalled();
  });

  it('Cliente_401ConCuerpoSinErrores_EsSinSesion', async () => {
    servidorFalso({ 'GET /api/sesion': json(401, { title: 'Unauthorized' }) });

    expect(await consultarSesion()).toEqual({ tipo: 'sinSesion' });
  });

  it('Cliente_401SinManejadorRegistrado_NoFalla', async () => {
    servidorFalso({ 'POST /api/acceso/salir': vacia(401) });

    expect(await salir()).toEqual({ tipo: 'sinSesion' });
  });

  it('Cliente_Estado_DevuelveLosDatos', async () => {
    servidorFalso({ 'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }) });

    expect(await consultarEstado()).toEqual({ tipo: 'ok', datos: { contrasenaDefinida: true } });
  });

  it('Cliente_EstadoConCuerpoInesperado_EsErrorGeneral', async () => {
    servidorFalso({ 'GET /api/acceso/estado': json(200, { otra: 1 }) });

    expect(await consultarEstado()).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });

  it('Cliente_EstadoConJsonRoto_EsErrorGeneral', async () => {
    servidorFalso({ 'GET /api/acceso/estado': () => new Response('<html>', { status: 200 }) });

    expect(await consultarEstado()).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });

  it('Cliente_Marca_ValidaLaForma', async () => {
    servidorFalso({ 'GET /api/marca': json(200, { primario: '#0903A0', fondo: '#FFFFFF', esEjemplo: false }) });
    expect(await consultarMarca()).toEqual({
      tipo: 'ok',
      datos: { primario: '#0903A0', fondo: '#FFFFFF', esEjemplo: false },
    });

    servidorFalso({ 'GET /api/marca': json(200, { primario: '#0903A0' }) });
    expect((await consultarMarca()).tipo).toBe('general');

    servidorFalso({ 'GET /api/marca': json(200, null) });
    expect((await consultarMarca()).tipo).toBe('general');
  });

  it('Cliente_Marca_EsEjemploNoBooleano_SeRechazaComoFormaInvalida', async () => {
    servidorFalso({ 'GET /api/marca': json(200, { primario: '#0903A0' }) });
    const formaInvalida = await consultarMarca();
    expect(formaInvalida).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });

    for (const esEjemplo of ['false', 1, null]) {
      servidorFalso({ 'GET /api/marca': json(200, { primario: '#0903A0', fondo: '#FFFFFF', esEjemplo }) });
      expect(await consultarMarca()).toEqual(formaInvalida);
    }
  });

  it('Cliente_PedidosGet_MismoOrigenSinCache', async () => {
    const servidor = servidorFalso({ 'GET /api/sesion': vacia(204) });

    expect(await consultarSesion()).toEqual({ tipo: 'ok', datos: undefined });
    const [pedido] = servidor.pedidosA('GET', '/api/sesion');
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(pedido?.init?.cache).toBe('no-store');
  });

  it.each([
    ['definir', definirContrasena, 409, 'Ya hay una contraseña definida. Ingresá con ella.'],
    ['ingresar', ingresar, 409, 'Todavía no hay una contraseña. Definí la contraseña primero.'],
  ] as const)('Cliente_409En%s_MensajePropio', async (ruta, operacion, estado, mensaje) => {
    servidorFalso({ [`POST /api/acceso/${ruta}`]: problemaGeneral(estado, 'texto del servidor') });

    expect(await operacion('ocho1234')).toEqual({ tipo: 'conflicto', mensaje });
  });

  it.each([
    [413, { tipo: 'campo', campo: 'contrasena', mensaje: 'La contraseña es demasiado larga. Ingresá una de 128 caracteres o menos.' }],
    [415, { tipo: 'general', mensaje: 'No se pudo completar la operación. Recargá la página y reintentá.' }],
    [429, { tipo: 'general', mensaje: 'El sistema está ocupado. Intentá de nuevo en un momento.' }],
    [500, { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION }],
    [503, { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION }],
    [403, { tipo: 'general', mensaje: 'No se pudo completar la operación. Recargá la página y reintentá.' }],
  ] as const)('Cliente_Codigo%i_MensajeQueCorresponde', async (estado, esperado) => {
    servidorFalso({ 'POST /api/acceso/definir': vacia(estado) });

    expect(await definirContrasena('ocho1234')).toEqual(esperado);
  });

  it('Cliente_400SinErroresLegibles_MensajeGeneral', async () => {
    servidorFalso({ 'POST /api/acceso/definir': [json(400, { title: 'Bad' }), () => new Response('x', { status: 400 })] });

    const general = { tipo: 'general', mensaje: 'No se pudo completar la operación. Recargá la página y reintentá.' };
    expect(await definirContrasena('ocho1234')).toEqual(general);
    expect(await definirContrasena('ocho1234')).toEqual(general);
  });

  it('Cliente_400ConErroresDeOtroFormato_MensajeGeneral', async () => {
    servidorFalso({ 'POST /api/acceso/definir': json(400, { errors: { contrasena: 'no es lista' } }) });

    expect(await definirContrasena('ocho1234')).toEqual({
      tipo: 'general',
      mensaje: 'No se pudo completar la operación. Recargá la página y reintentá.',
    });
  });

  it('Cliente_ErrorDeRed_MensajeGeneral', async () => {
    servidorFalso({ 'GET /api/acceso/estado': sinRed });

    expect(await consultarEstado()).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });
});
