import { afterEach, describe, expect, it, vi } from 'vitest';
import { IMPORTE_MAXIMO_SEGURO } from '../configuracion/reglas';
import { json, problema, problemaGeneral, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';
import {
  alPerderLaSesion,
  cambiarContrasena,
  consultarArticulo,
  consultarConfiguracion,
  consultarEstado,
  consultarMarca,
  consultarSesion,
  definirContrasena,
  grabarArticulo,
  grabarConfiguracion,
  ingresar,
  MENSAJE_ARTICULO_GRANDE,
  MENSAJE_CONFIGURACION_GRANDE,
  MENSAJE_PEDIDO_RECHAZADO,
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

  it('Cliente_423EnIngresar_BloqueadoConSuMensaje', async () => {
    servidorFalso({ 'POST /api/acceso/ingresar': problemaGeneral(423, 'texto del servidor') });

    expect(await ingresar('ocho1234')).toEqual({
      tipo: 'bloqueado',
      mensaje: 'El acceso está bloqueado temporalmente. Volvé a intentar en unos minutos.',
    });
  });

  it('Cliente_Cambiar_EnviaLasDosEnElCuerpo', async () => {
    const servidor = servidorFalso({ 'POST /api/acceso/cambiar': vacia(204) });

    expect(await cambiarContrasena('anterior1', 'nueva12345')).toEqual({ tipo: 'ok', datos: undefined });
    const [pedido] = servidor.pedidosA('POST', '/api/acceso/cambiar');
    expect(JSON.parse(String(pedido?.init?.body))).toEqual({ contrasenaActual: 'anterior1', contrasenaNueva: 'nueva12345' });
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(new Headers(pedido?.init?.headers).get('Content-Type')).toBe('application/json');
  });

  it.each([
    [409, { tipo: 'conflicto', mensaje: 'No hay una contraseña definida. Volvé a ingresar.' }],
    [413, { tipo: 'campo', campo: 'contrasenaActual', mensaje: 'La contraseña es demasiado larga. Ingresá una de 128 caracteres o menos.' }],
    [415, { tipo: 'general', mensaje: 'No se pudo completar la operación. Recargá la página y reintentá.' }],
    [429, { tipo: 'general', mensaje: 'El sistema está ocupado. Intentá de nuevo en un momento.' }],
    [500, { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION }],
  ] as const)('Cliente_Cambiar%i_MensajeQueCorresponde', async (estado, esperado) => {
    servidorFalso({ 'POST /api/acceso/cambiar': vacia(estado) });

    expect(await cambiarContrasena('anterior1', 'nueva12345')).toEqual(esperado);
  });

  it('Cliente_Cambiar400_ErrorEnElCampoQueIndicaLaApi', async () => {
    servidorFalso({ 'POST /api/acceso/cambiar': problema(400, 'contrasenaNueva', 'No es válida.') });

    expect(await cambiarContrasena('anterior1', 'nueva12345')).toEqual({
      tipo: 'campo',
      campo: 'contrasenaNueva',
      mensaje: 'No es válida.',
    });
  });

  it('Cliente_ErrorDeRed_MensajeGeneral', async () => {
    servidorFalso({ 'GET /api/acceso/estado': sinRed });

    expect(await consultarEstado()).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });
});

describe('cliente de la Api: configuración', () => {
  const GUARDADA = {
    alicuotaIva: 21,
    condicionFiscal: 'responsable_inscripto',
    multiploRedondeo: 0.01,
    topeIdentificacion: null,
    faltaTope: true,
  };
  const DATOS = {
    alicuotaIva: 21,
    condicionFiscal: 'monotributo',
    multiploRedondeo: 10,
    topeIdentificacion: 250000,
  } as const;
  const RECHAZADO = { tipo: 'general', mensaje: MENSAJE_PEDIDO_RECHAZADO };

  it('consultarConfiguracion_200_DevuelveLosValores', async () => {
    const servidor = servidorFalso({ 'GET /api/configuracion': json(200, GUARDADA) });

    expect(await consultarConfiguracion()).toEqual({ tipo: 'ok', datos: GUARDADA });
    const [pedido] = servidor.pedidosA('GET', '/api/configuracion');
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(pedido?.init?.cache).toBe('no-store');
  });

  it.each([
    ['alícuota que no es número', { ...GUARDADA, alicuotaIva: '21' }],
    ['condición desconocida', { ...GUARDADA, condicionFiscal: 'otra' }],
    ['múltiplo ausente', { ...GUARDADA, multiploRedondeo: undefined }],
    ['tope de texto', { ...GUARDADA, topeIdentificacion: '1' }],
    ['faltaTope no booleano', { ...GUARDADA, faltaTope: 'si' }],
    ['cuerpo que no es objeto', 'texto'],
  ])('consultarConfiguracion_%s_ErrorGeneral', async (_caso, cuerpo) => {
    servidorFalso({ 'GET /api/configuracion': json(200, cuerpo) });

    expect(await consultarConfiguracion()).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });

  it('consultarConfiguracion_401_SinSesion', async () => {
    const manejador = vi.fn();
    alPerderLaSesion(manejador);
    servidorFalso({ 'GET /api/configuracion': vacia(401) });

    expect(await consultarConfiguracion()).toEqual({ tipo: 'sinSesion' });
    expect(manejador).toHaveBeenCalledOnce();
  });

  it('grabarConfiguracion_200_EnviaLosCuatroYDevuelveLoGuardado', async () => {
    const guardada = { ...DATOS, faltaTope: false };
    const servidor = servidorFalso({ 'POST /api/configuracion': json(200, guardada) });

    expect(await grabarConfiguracion(DATOS)).toEqual({ tipo: 'ok', datos: guardada });
    const [pedido] = servidor.pedidosA('POST', '/api/configuracion');
    expect(JSON.parse(String(pedido?.init?.body))).toEqual(DATOS);
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(new Headers(pedido?.init?.headers).get('Content-Type')).toBe('application/json');
  });

  it('grabarConfiguracion_SinTope_EnviaNull', async () => {
    const servidor = servidorFalso({ 'POST /api/configuracion': json(200, GUARDADA) });

    await grabarConfiguracion({ ...DATOS, topeIdentificacion: null });

    const [pedido] = servidor.pedidosA('POST', '/api/configuracion');
    expect(JSON.parse(String(pedido?.init?.body))).toHaveProperty('topeIdentificacion', null);
  });

  it('grabarConfiguracion_400_DevuelveTodosLosErrores', async () => {
    servidorFalso({
      'POST /api/configuracion': json(400, {
        status: 400,
        title: 'One or more validation errors occurred.',
        errors: {
          alicuotaIva: ['La alícuota de IVA debe estar entre 0 y 100.'],
          topeIdentificacion: ['El tope debe ser mayor a 0.'],
          otro: 'no es una lista',
          vacio: [],
        },
      }),
    });

    expect(await grabarConfiguracion(DATOS)).toEqual({
      tipo: 'campos',
      errores: [
        { campo: 'alicuotaIva', mensaje: 'La alícuota de IVA debe estar entre 0 y 100.' },
        { campo: 'topeIdentificacion', mensaje: 'El tope debe ser mayor a 0.' },
      ],
    });
  });

  it('grabarConfiguracion_400SinErroresLegibles_MensajeGeneral', async () => {
    servidorFalso({
      'POST /api/configuracion': [
        json(400, { title: 'Bad' }),
        json(400, { errors: { alicuotaIva: [3] } }),
        () => new Response('x', { status: 400 }),
      ],
    });

    expect(await grabarConfiguracion(DATOS)).toEqual(RECHAZADO);
    expect(await grabarConfiguracion(DATOS)).toEqual(RECHAZADO);
    expect(await grabarConfiguracion(DATOS)).toEqual(RECHAZADO);
  });

  it('grabarConfiguracion_413_MensajeGeneralPropio', async () => {
    servidorFalso({ 'POST /api/configuracion': vacia(413) });

    expect(MENSAJE_CONFIGURACION_GRANDE).toBe('Los valores enviados son demasiado largos. Revisalos y grabá de nuevo.');
    expect(await grabarConfiguracion(DATOS)).toEqual({ tipo: 'general', mensaje: MENSAJE_CONFIGURACION_GRANDE });
  });

  it.each([
    [429, { tipo: 'general', mensaje: 'El sistema está ocupado. Intentá de nuevo en un momento.' }],
    [415, RECHAZADO],
    [500, { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION }],
  ] as const)('grabarConfiguracion_%i_MensajeGeneral', async (estado, esperado) => {
    servidorFalso({ 'POST /api/configuracion': vacia(estado) });

    expect(await grabarConfiguracion(DATOS)).toEqual(esperado);
  });

  it('grabarConfiguracion_401_SinSesion', async () => {
    const manejador = vi.fn();
    alPerderLaSesion(manejador);
    servidorFalso({ 'POST /api/configuracion': vacia(401) });

    expect(await grabarConfiguracion(DATOS)).toEqual({ tipo: 'sinSesion' });
    expect(manejador).toHaveBeenCalledOnce();
  });

  it('grabarConfiguracion_SinRed_MensajeGeneral', async () => {
    servidorFalso({ 'POST /api/configuracion': sinRed });

    expect(await grabarConfiguracion(DATOS)).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });

  it('grabarConfiguracion_200ConCuerpoInesperado_ErrorGeneral', async () => {
    servidorFalso({ 'POST /api/configuracion': json(200, { otro: 1 }) });

    expect(await grabarConfiguracion(DATOS)).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });
});

describe('cliente de la Api: artículos', () => {
  const DATOS = { codigoProveedor: 'ABC-1', descripcion: 'Armazón metálico', precioCosto: 1210, margenUtilidad: 50 };
  const GRABADO = { codigo: 7, ...DATOS, precioVenta: 1815, advertencia: null };
  const RECHAZADO = { tipo: 'general', mensaje: MENSAJE_PEDIDO_RECHAZADO };

  it('grabarArticulo_201_EnviaLosCuatroYDevuelveElGrabado', async () => {
    const servidor = servidorFalso({ 'POST /api/articulos': json(201, GRABADO) });

    expect(await grabarArticulo(DATOS)).toEqual({ tipo: 'ok', datos: GRABADO });
    const [pedido] = servidor.pedidosA('POST', '/api/articulos');
    expect(JSON.parse(String(pedido?.init?.body))).toEqual(DATOS);
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(new Headers(pedido?.init?.headers).get('Content-Type')).toBe('application/json');
  });

  it('grabarArticulo_201ConAdvertencia_LaDevuelve', async () => {
    const conAdvertencia = { ...GRABADO, precioCosto: -100, precioVenta: -150, advertencia: 'Precio de costo negativo o cero' };
    servidorFalso({ 'POST /api/articulos': json(201, conAdvertencia) });

    expect(await grabarArticulo({ ...DATOS, precioCosto: -100 })).toEqual({ tipo: 'ok', datos: conAdvertencia });
  });

  it('grabarArticulo_400_DevuelveTodosLosErrores', async () => {
    servidorFalso({
      'POST /api/articulos': json(400, {
        status: 400,
        title: 'One or more validation errors occurred.',
        errors: {
          codigoProveedor: ['Ese código ya pertenece a otro artículo.'],
          margenUtilidad: ['El margen debe estar entre 0 y 1.000.'],
          otro: 'no es una lista',
          vacio: [],
        },
      }),
    });

    expect(await grabarArticulo(DATOS)).toEqual({
      tipo: 'campos',
      errores: [
        { campo: 'codigoProveedor', mensaje: 'Ese código ya pertenece a otro artículo.' },
        { campo: 'margenUtilidad', mensaje: 'El margen debe estar entre 0 y 1.000.' },
      ],
    });
  });

  it('grabarArticulo_400SinErroresLegibles_MensajeGeneral', async () => {
    servidorFalso({
      'POST /api/articulos': [
        json(400, { title: 'Bad' }),
        json(400, { errors: { descripcion: [3] } }),
        () => new Response('x', { status: 400 }),
      ],
    });

    expect(await grabarArticulo(DATOS)).toEqual(RECHAZADO);
    expect(await grabarArticulo(DATOS)).toEqual(RECHAZADO);
    expect(await grabarArticulo(DATOS)).toEqual(RECHAZADO);
  });

  it('grabarArticulo_413_MensajeGeneralPropio', async () => {
    servidorFalso({ 'POST /api/articulos': vacia(413) });

    expect(MENSAJE_ARTICULO_GRANDE).toBe('Los datos enviados son demasiado largos. Revisalos y grabá de nuevo.');
    expect(await grabarArticulo(DATOS)).toEqual({ tipo: 'general', mensaje: MENSAJE_ARTICULO_GRANDE });
  });

  it.each([
    [429, { tipo: 'general', mensaje: 'El sistema está ocupado. Intentá de nuevo en un momento.' }],
    [415, RECHAZADO],
    [500, { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION }],
  ] as const)('grabarArticulo_%i_MensajeGeneral', async (estado, esperado) => {
    servidorFalso({ 'POST /api/articulos': vacia(estado) });

    expect(await grabarArticulo(DATOS)).toEqual(esperado);
  });

  it('grabarArticulo_401_SinSesion', async () => {
    const manejador = vi.fn();
    alPerderLaSesion(manejador);
    servidorFalso({ 'POST /api/articulos': vacia(401) });

    expect(await grabarArticulo(DATOS)).toEqual({ tipo: 'sinSesion' });
    expect(manejador).toHaveBeenCalledOnce();
  });

  it('grabarArticulo_SinRed_MensajeGeneral', async () => {
    servidorFalso({ 'POST /api/articulos': sinRed });

    expect(await grabarArticulo(DATOS)).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });

  it.each([
    ['código que no es entero', { ...GRABADO, codigo: 7.5 }],
    ['código de texto', { ...GRABADO, codigo: '7' }],
    ['descripción ausente', { ...GRABADO, descripcion: undefined }],
    ['código en el proveedor que no es texto', { ...GRABADO, codigoProveedor: 1 }],
    ['precio de venta de texto', { ...GRABADO, precioVenta: '1815' }],
    ['costo ausente', { ...GRABADO, precioCosto: undefined }],
    ['margen ausente', { ...GRABADO, margenUtilidad: undefined }],
    ['advertencia que no es texto', { ...GRABADO, advertencia: 1 }],
    ['advertencia ausente', { ...GRABADO, advertencia: undefined }],
    ['cuerpo que no es objeto', 'texto'],
    // D-19: un importe fuera del rango que un `number` guarda con los centavos exactos es una respuesta ilegible.
    ['precio de venta sobre el máximo seguro', { ...GRABADO, precioVenta: 90071992547410 }],
    ['precio de venta bajo el mínimo seguro', { ...GRABADO, precioVenta: -90071992547410 }],
    ['costo sobre el máximo seguro', { ...GRABADO, precioCosto: 90071992547410 }],
    ['costo bajo el mínimo seguro', { ...GRABADO, precioCosto: -90071992547410 }],
  ])('grabarArticulo_201Con%s_ErrorGeneral', async (_caso, cuerpo) => {
    servidorFalso({ 'POST /api/articulos': json(201, cuerpo) });

    expect(await grabarArticulo(DATOS)).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });

  it('grabarArticulo_201ConImportesEnElMaximoSeguro_LosDevuelve', async () => {
    const enElLimite = {
      ...GRABADO,
      precioCosto: -IMPORTE_MAXIMO_SEGURO,
      precioVenta: IMPORTE_MAXIMO_SEGURO,
    };
    servidorFalso({ 'POST /api/articulos': json(201, enElLimite) });

    expect(await grabarArticulo(DATOS)).toEqual({ tipo: 'ok', datos: enElLimite });
  });

  it('consultarArticulo_PrecioDeVentaSobreElMaximoSeguro_ErrorGeneral', async () => {
    servidorFalso({ 'GET /api/articulos/7': json(200, { ...GRABADO, precioVenta: 1e20 }) });

    expect(await consultarArticulo(7)).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });

  it('consultarArticulo_200_DevuelveElArticulo', async () => {
    const servidor = servidorFalso({ 'GET /api/articulos/7': json(200, GRABADO) });

    expect(await consultarArticulo(7)).toEqual({ tipo: 'ok', datos: GRABADO });
    const [pedido] = servidor.pedidosA('GET', '/api/articulos/7');
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(pedido?.init?.cache).toBe('no-store');
  });

  it('consultarArticulo_404_MensajeGeneral', async () => {
    servidorFalso({ 'GET /api/articulos/8': vacia(404) });

    expect(await consultarArticulo(8)).toEqual(RECHAZADO);
  });

  it('consultarArticulo_401_SinSesion', async () => {
    servidorFalso({ 'GET /api/articulos/7': vacia(401) });

    expect(await consultarArticulo(7)).toEqual({ tipo: 'sinSesion' });
  });

  it.each([Number.NaN, 1.5, -1, Number.MAX_SAFE_INTEGER + 1])(
    'consultarArticulo_CodigoInvalido_%d_NoConsulta',
    async (codigo) => {
      const servidor = servidorFalso({});

      expect(await consultarArticulo(codigo)).toEqual(RECHAZADO);
      expect(servidor.llamadas).toEqual([]);
    },
  );
});
