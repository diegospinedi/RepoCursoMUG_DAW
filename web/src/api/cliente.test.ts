import { afterEach, describe, expect, it, vi } from 'vitest';
import { IMPORTE_MAXIMO_SEGURO } from '../configuracion/reglas';
import { json, problema, problemaGeneral, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';
import {
  alPerderLaSesion,
  buscarArticulos,
  CAMPO_TEXTO,
  cambiarContrasena,
  cambiarEstadoArticulo,
  consultarArticulo,
  consultarPresupuesto,
  grabarPresupuesto,
  CAMPO_LINEAS,
  campoCantidad,
  campoDescuento,
  MENSAJE_PRESUPUESTO_NO_ENCONTRADO,
  MENSAJE_ARTICULO_CAMBIADO,
  MENSAJE_ARTICULO_INACTIVO,
  MENSAJE_ARTICULO_NO_ENCONTRADO,
  MENSAJE_LISTADO_CAMBIADO,
  modificarArticulo,
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
  campoOrden,
  modificarPresupuesto,
  pasarPresupuestoAFinal,
  buscarPresupuestos,
  MENSAJE_BUSQUEDA_GRANDE,
  descargarPdfPresupuesto,
  MENSAJE_PRESUPUESTO_BORRADOR_PDF,
} from './cliente';
import { MENSAJE_PRESUPUESTO_CAMBIADO, MENSAJE_PRESUPUESTO_FINAL } from '../presupuestos/reglas';

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
  const ADMITIDAS = [0, 2.5, 5, 10.5, 21, 27];
  const GUARDADA = {
    alicuotaIva: 21,
    condicionFiscal: 'responsable_inscripto',
    multiploRedondeo: 0.01,
    topeIdentificacion: null,
    faltaTope: true,
    puntoVenta: null,
    alicuotasAdmitidas: ADMITIDAS,
  };
  const DATOS = {
    alicuotaIva: 21,
    condicionFiscal: 'monotributo',
    multiploRedondeo: 10,
    topeIdentificacion: 250000,
    puntoVenta: 3,
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
    ['punto de venta ausente', { ...GUARDADA, puntoVenta: undefined }],
    ['punto de venta 0', { ...GUARDADA, puntoVenta: 0 }],
    ['punto de venta 100000', { ...GUARDADA, puntoVenta: 100000 }],
    ['punto de venta con decimales', { ...GUARDADA, puntoVenta: 2.5 }],
    ['punto de venta de texto', { ...GUARDADA, puntoVenta: '3' }],
    ['alícuotas admitidas vacías', { ...GUARDADA, alicuotasAdmitidas: [] }],
    ['alícuotas admitidas que no son lista', { ...GUARDADA, alicuotasAdmitidas: '21' }],
    ['alícuota admitida de texto', { ...GUARDADA, alicuotasAdmitidas: [0, '21'] }],
    ['alícuotas admitidas repetidas', { ...GUARDADA, alicuotasAdmitidas: [0, 2.5, 21, 21] }],
  ])('consultarConfiguracion_%s_ErrorGeneral', async (_caso, cuerpo) => {
    servidorFalso({ 'GET /api/configuracion': json(200, cuerpo) });

    expect(await consultarConfiguracion()).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });

  it('leerConfiguracion: respuesta sin alicuotasAdmitidas es inválida', async () => {
    const { alicuotasAdmitidas: _sinLista, ...sinLista } = GUARDADA;
    servidorFalso({
      'GET /api/configuracion': json(200, sinLista),
      'POST /api/configuracion': json(200, { ...sinLista, preciosCambiados: 0 }),
    });

    expect(await consultarConfiguracion()).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
    expect(await grabarConfiguracion(DATOS)).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });

  it('consultarConfiguracion_PuntoVentaYAlicuotaFueraDeLaLista_SeLeen', async () => {
    // D-06: una alícuota guardada antes de FR-26 se lee igual; el punto de venta llega como entero.
    const guardada = { ...GUARDADA, alicuotaIva: 19, puntoVenta: 99999 };
    servidorFalso({ 'GET /api/configuracion': json(200, { ...guardada, otroCampo: 'x' }) });

    expect(await consultarConfiguracion()).toEqual({ tipo: 'ok', datos: guardada });
  });

  it('consultarConfiguracion_401_SinSesion', async () => {
    const manejador = vi.fn();
    alPerderLaSesion(manejador);
    servidorFalso({ 'GET /api/configuracion': vacia(401) });

    expect(await consultarConfiguracion()).toEqual({ tipo: 'sinSesion' });
    expect(manejador).toHaveBeenCalledOnce();
  });

  it('consultarConfiguracion_SinPreciosCambiados_SeLee', async () => {
    // D-23: el GET responde sin preciosCambiados y se lee; el mismo cuerpo como respuesta del POST es ilegible, porque
    // la grabación tiene su propio lector.
    servidorFalso({ 'GET /api/configuracion': json(200, GUARDADA), 'POST /api/configuracion': json(200, GUARDADA) });

    expect(await consultarConfiguracion()).toEqual({ tipo: 'ok', datos: GUARDADA });
    expect(await grabarConfiguracion(DATOS)).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });

  it.each([
    ['ausente', undefined],
    ['nulo', null],
    ['negativo', -1],
    ['con decimales', 1.5],
    ['de texto', '3'],
    ['mayor que el entero seguro', Number.MAX_SAFE_INTEGER + 1],
  ])('grabarConfiguracion_PreciosCambiados_%s_RespuestaIlegible', async (_caso, preciosCambiados) => {
    servidorFalso({ 'POST /api/configuracion': json(200, { ...GUARDADA, preciosCambiados }) });

    expect(await grabarConfiguracion(DATOS)).toEqual({ tipo: 'general', mensaje: MENSAJE_SIN_CONEXION });
  });

  it('grabarConfiguracion_200_EnviaLosCincoYDevuelveLoGuardado', async () => {
    // Lo guardado y cuántos precios de venta cambiaron (D-09, D-23).
    const guardada = { ...DATOS, faltaTope: false, alicuotasAdmitidas: ADMITIDAS, preciosCambiados: 2 };
    const servidor = servidorFalso({ 'POST /api/configuracion': json(200, guardada) });

    expect(await grabarConfiguracion(DATOS)).toEqual({ tipo: 'ok', datos: guardada });
    const [pedido] = servidor.pedidosA('POST', '/api/configuracion');
    expect(JSON.parse(String(pedido?.init?.body))).toEqual(DATOS);
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(new Headers(pedido?.init?.headers).get('Content-Type')).toBe('application/json');
  });

  it('grabarConfiguracion_SinPuntoVenta_EnviaNull', async () => {
    // D-27: null conserva el punto de venta guardado.
    const servidor = servidorFalso({ 'POST /api/configuracion': json(200, { ...GUARDADA, preciosCambiados: 0 }) });

    await grabarConfiguracion({ ...DATOS, puntoVenta: null });

    const [pedido] = servidor.pedidosA('POST', '/api/configuracion');
    expect(JSON.parse(String(pedido?.init?.body))).toHaveProperty('puntoVenta', null);
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
          alicuotaIva: ['La alícuota de IVA debe ser 0, 2,5, 5, 10,5, 21 o 27.'],
          topeIdentificacion: ['El tope debe ser mayor a 0.'],
          puntoVenta: ['El punto de venta debe ser un número entero de 1 a 99999.'],
          otro: 'no es una lista',
          vacio: [],
        },
      }),
    });

    expect(await grabarConfiguracion(DATOS)).toEqual({
      tipo: 'campos',
      errores: [
        { campo: 'alicuotaIva', mensaje: 'La alícuota de IVA debe ser 0, 2,5, 5, 10,5, 21 o 27.' },
        { campo: 'topeIdentificacion', mensaje: 'El tope debe ser mayor a 0.' },
        { campo: 'puntoVenta', mensaje: 'El punto de venta debe ser un número entero de 1 a 99999.' },
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
  const GRABADO = { codigo: 7, ...DATOS, precioVenta: 1815, advertencia: null, activo: true, version: 3 };
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
    ['activo ausente', { ...GRABADO, activo: undefined }],
    ['activo que no es booleano', { ...GRABADO, activo: 'true' }],
    ['versión ausente', { ...GRABADO, version: undefined }],
    ['versión de texto', { ...GRABADO, version: '3' }],
    ['versión que no es entera', { ...GRABADO, version: 3.5 }],
    ['versión negativa', { ...GRABADO, version: -1 }],
    ['versión sobre el máximo seguro', { ...GRABADO, version: Number.MAX_SAFE_INTEGER + 1 }],
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

    expect(await consultarArticulo(8)).toEqual({ tipo: 'general', mensaje: MENSAJE_ARTICULO_NO_ENCONTRADO });
    expect(MENSAJE_ARTICULO_NO_ENCONTRADO).toBe('No se encontró el artículo.');
  });

  it('grabarArticulo_201ConVersionEnElMaximoSeguro_LaDevuelve', async () => {
    const enElLimite = { ...GRABADO, activo: false, version: Number.MAX_SAFE_INTEGER };
    servidorFalso({ 'POST /api/articulos': json(201, enElLimite) });

    expect(await grabarArticulo(DATOS)).toEqual({ tipo: 'ok', datos: enElLimite });
  });

  it('consultarArticulo_401_SinSesion', async () => {
    servidorFalso({ 'GET /api/articulos/7': vacia(401) });

    expect(await consultarArticulo(7)).toEqual({ tipo: 'sinSesion' });
  });

  it.each([Number.NaN, 1.5, -1, Number.MAX_SAFE_INTEGER + 1])(
    'consultarArticulo_CodigoInvalido_%d_NoConsulta',
    async (codigo) => {
      const servidor = servidorFalso({});

      // Como modificarArticulo y cambiarEstadoArticulo: un código que no puede existir no se encuentra.
      expect(await consultarArticulo(codigo)).toEqual({ tipo: 'general', mensaje: MENSAJE_ARTICULO_NO_ENCONTRADO });
      expect(servidor.llamadas).toEqual([]);
    },
  );
});

describe('cliente de la Api: listado, modificación y cambio de estado de artículos', () => {
  const DATOS = { codigoProveedor: 'ABC-1', descripcion: 'Armazón metálico', precioCosto: 1210, margenUtilidad: 60 };
  const GRABADO = { codigo: 7, ...DATOS, precioVenta: 1936, advertencia: null, activo: true, version: 4 };
  const FILA = { codigo: 7, codigoProveedor: 'ABC-1', descripcion: 'Armazón metálico', precioVenta: 1815, activo: true };
  const PAGINA = { articulos: [FILA], pagina: 1, tamanoPagina: 50, total: 1 };
  const ILEGIBLE = { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION };
  const conflicto = (cuerpo: unknown) => () =>
    new Response(JSON.stringify(cuerpo), { status: 409, headers: { 'Content-Type': 'application/problem+json' } });
  const DEL_SERVIDOR = { status: 409, title: 'No se pudo grabar el artículo.', detail: 'texto del servidor' };

  it('modificarArticulo_EnviaJsonConPut', async () => {
    const servidor = servidorFalso({ 'PUT /api/articulos/7': json(200, GRABADO) });

    expect(await modificarArticulo(7, DATOS, 3)).toEqual({ tipo: 'ok', datos: GRABADO });
    const [pedido] = servidor.pedidosA('PUT', '/api/articulos/7');
    // Sin el Content-Type la Api respondería 415 (D-11).
    expect(new Headers(pedido?.init?.headers).get('Content-Type')).toBe('application/json');
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(pedido?.init?.cache).toBe('no-store');
    expect(JSON.parse(String(pedido?.init?.body))).toEqual({ ...DATOS, version: 3 });
  });

  it('modificarArticulo_409ConMotivoDesconocido_RespuestaIlegible', async () => {
    servidorFalso({
      'PUT /api/articulos/7': [
        conflicto({ ...DEL_SERVIDOR, motivo: 'otro' }),
        conflicto(DEL_SERVIDOR),
        conflicto({ ...DEL_SERVIDOR, motivo: 1 }),
        () => new Response('<html>', { status: 409 }),
        vacia(409),
      ],
    });

    for (let i = 0; i < 5; i++) {
      expect(await modificarArticulo(7, DATOS, 3)).toEqual(ILEGIBLE);
    }
  });

  it.each([
    ['inactivo', MENSAJE_ARTICULO_INACTIVO, 'Este artículo está inactivo. Reactivalo para editarlo.'],
    ['cambiado', MENSAJE_ARTICULO_CAMBIADO, 'El artículo cambió mientras lo editabas. Volvé a abrirlo.'],
  ])('modificarArticulo_409Con%s_ConflictoConSuMotivoYTextoPropio', async (motivo, constante, texto) => {
    servidorFalso({ 'PUT /api/articulos/7': conflicto({ ...DEL_SERVIDOR, motivo }) });

    expect(constante).toBe(texto);
    // Nunca el `detail` del servidor (D-19).
    expect(await modificarArticulo(7, DATOS, 3)).toEqual({ tipo: 'conflicto', motivo, mensaje: texto });
  });

  it('modificarArticulo_400_DevuelveTodosLosErrores', async () => {
    servidorFalso({
      'PUT /api/articulos/7': json(400, {
        errors: { descripcion: ['Completá la descripción.'], version: ['Volvé a abrir el artículo para editarlo.'] },
      }),
    });

    expect(await modificarArticulo(7, DATOS, 3)).toEqual({
      tipo: 'campos',
      errores: [
        { campo: 'descripcion', mensaje: 'Completá la descripción.' },
        { campo: 'version', mensaje: 'Volvé a abrir el artículo para editarlo.' },
      ],
    });
  });

  it.each([
    [404, { tipo: 'general', mensaje: MENSAJE_ARTICULO_NO_ENCONTRADO }],
    [413, { tipo: 'general', mensaje: MENSAJE_ARTICULO_GRANDE }],
    [415, { tipo: 'general', mensaje: MENSAJE_PEDIDO_RECHAZADO }],
    [500, ILEGIBLE],
  ] as const)('modificarArticulo_%i_MensajeGeneral', async (estado, esperado) => {
    servidorFalso({ 'PUT /api/articulos/7': vacia(estado) });

    expect(await modificarArticulo(7, DATOS, 3)).toEqual(esperado);
  });

  it('modificarArticulo_401_SinSesion', async () => {
    const manejador = vi.fn();
    alPerderLaSesion(manejador);
    servidorFalso({ 'PUT /api/articulos/7': vacia(401) });

    expect(await modificarArticulo(7, DATOS, 3)).toEqual({ tipo: 'sinSesion' });
    expect(manejador).toHaveBeenCalledOnce();
  });

  it('modificarArticulo_CodigoInvalido_NoEnvia', async () => {
    const servidor = servidorFalso({});

    expect(await modificarArticulo(0, DATOS, 3)).toEqual({ tipo: 'general', mensaje: MENSAJE_ARTICULO_NO_ENCONTRADO });
    expect(await cambiarEstadoArticulo(1.5, true)).toEqual({ tipo: 'general', mensaje: MENSAJE_ARTICULO_NO_ENCONTRADO });
    expect(servidor.llamadas).toEqual([]);
  });

  it('cambiarEstadoArticulo_EnviaJsonConPutYDevuelveLaVersionNueva', async () => {
    const inactivo = { ...GRABADO, activo: false, version: 5 };
    const servidor = servidorFalso({ 'PUT /api/articulos/7/estado': json(200, inactivo) });

    expect(await cambiarEstadoArticulo(7, false)).toEqual({ tipo: 'ok', datos: inactivo });
    const [pedido] = servidor.pedidosA('PUT', '/api/articulos/7/estado');
    expect(new Headers(pedido?.init?.headers).get('Content-Type')).toBe('application/json');
    expect(JSON.parse(String(pedido?.init?.body))).toEqual({ activo: false });
  });

  it('cambiarEstadoArticulo_409_ConflictoConTextoPropio', async () => {
    servidorFalso({
      'PUT /api/articulos/7/estado': [
        conflicto({ ...DEL_SERVIDOR, motivo: 'cambiado' }),
        // El cambio de estado no responde `inactivo` (D-16): fuera de lo previsto es ilegible.
        conflicto({ ...DEL_SERVIDOR, motivo: 'inactivo' }),
      ],
    });

    expect(MENSAJE_LISTADO_CAMBIADO).toBe('El artículo cambió. Volvé a cargar el listado.');
    expect(await cambiarEstadoArticulo(7, true)).toEqual({
      tipo: 'conflicto',
      motivo: 'cambiado',
      mensaje: MENSAJE_LISTADO_CAMBIADO,
    });
    expect(await cambiarEstadoArticulo(7, true)).toEqual(ILEGIBLE);
  });

  it('cambiarEstadoArticulo_409_TextoDelCambiadoLoEligeQuienLlama', async () => {
    servidorFalso({ 'PUT /api/articulos/7/estado': conflicto({ ...DEL_SERVIDOR, motivo: 'cambiado' }) });

    expect(await cambiarEstadoArticulo(7, true, MENSAJE_ARTICULO_CAMBIADO)).toEqual({
      tipo: 'conflicto',
      motivo: 'cambiado',
      mensaje: MENSAJE_ARTICULO_CAMBIADO,
    });
  });

  it('cambiarEstadoArticulo_400ConErrores_MensajeGeneral', async () => {
    // El cuerpo es fijo: un error en `activo` no tiene un campo en ninguna pantalla.
    servidorFalso({
      'PUT /api/articulos/7/estado': problema(400, 'activo', 'Indicá si el artículo queda activo o inactivo.'),
    });

    expect(await cambiarEstadoArticulo(7, true)).toEqual({ tipo: 'general', mensaje: MENSAJE_PEDIDO_RECHAZADO });
  });

  it.each([
    [404, { tipo: 'general', mensaje: MENSAJE_ARTICULO_NO_ENCONTRADO }],
    [413, { tipo: 'general', mensaje: MENSAJE_PEDIDO_RECHAZADO }],
    [400, { tipo: 'general', mensaje: MENSAJE_PEDIDO_RECHAZADO }],
  ] as const)('cambiarEstadoArticulo_%i_MensajeGeneral', async (estado, esperado) => {
    servidorFalso({ 'PUT /api/articulos/7/estado': vacia(estado) });

    expect(await cambiarEstadoArticulo(7, true)).toEqual(esperado);
  });

  it('cambiarEstadoArticulo_200Ilegible_ErrorGeneral', async () => {
    servidorFalso({ 'PUT /api/articulos/7/estado': json(200, { ...GRABADO, version: undefined }) });

    expect(await cambiarEstadoArticulo(7, true)).toEqual(ILEGIBLE);
  });

  it('buscarArticulos_ArmaLaConsultaYDevuelveLaPagina', async () => {
    const servidor = servidorFalso({
      'GET /api/articulos?pagina=1': json(200, PAGINA),
      'GET /api/articulos?texto=armaz%C3%B3n+50%25&pagina=2': json(200, { ...PAGINA, articulos: [], pagina: 2 }),
    });

    expect(await buscarArticulos('', 1)).toEqual({ tipo: 'ok', datos: PAGINA });
    expect(await buscarArticulos('armazón 50%', 2)).toEqual({
      tipo: 'ok',
      datos: { ...PAGINA, articulos: [], pagina: 2 },
    });
    const [pedido] = servidor.pedidosA('GET', '/api/articulos?pagina=1');
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(pedido?.init?.cache).toBe('no-store');
    expect(pedido?.init?.body).toBeUndefined();
  });

  it.each([
    ['sin artículos', { ...PAGINA, articulos: undefined }],
    ['artículos que no son lista', { ...PAGINA, articulos: {} }],
    ['fila que no es objeto', { ...PAGINA, articulos: [1] }],
    ['código no entero', { ...PAGINA, articulos: [{ ...FILA, codigo: 7.5 }] }],
    ['código en el proveedor que no es texto', { ...PAGINA, articulos: [{ ...FILA, codigoProveedor: 1 }] }],
    ['descripción ausente', { ...PAGINA, articulos: [{ ...FILA, descripcion: undefined }] }],
    ['precio sobre el máximo seguro', { ...PAGINA, articulos: [{ ...FILA, precioVenta: 1e20 }] }],
    ['activo no booleano', { ...PAGINA, articulos: [{ ...FILA, activo: 1 }] }],
    ['página cero', { ...PAGINA, pagina: 0 }],
    ['tamaño de página cero', { ...PAGINA, tamanoPagina: 0 }],
    ['total negativo', { ...PAGINA, total: -1 }],
    ['total de texto', { ...PAGINA, total: '1' }],
    ['cuerpo que no es objeto', null],
  ])('buscarArticulos_200Con%s_ErrorGeneral', async (_caso, cuerpo) => {
    servidorFalso({ 'GET /api/articulos?pagina=1': json(200, cuerpo) });

    expect(await buscarArticulos('', 1)).toEqual(ILEGIBLE);
  });

  it('buscarArticulos_ConCostoOMargen_LosDescarta', async () => {
    // D-12: el listado nunca guarda costo ni margen, aunque una respuesta los trajera.
    servidorFalso({
      'GET /api/articulos?pagina=1': json(200, { ...PAGINA, articulos: [{ ...FILA, precioCosto: 1210, margenUtilidad: 50 }] }),
    });

    expect(await buscarArticulos('', 1)).toEqual({ tipo: 'ok', datos: PAGINA });
  });

  it('buscarArticulos_400_ErrorEnElCampo', async () => {
    servidorFalso({ 'GET /api/articulos?pagina=1': problema(400, 'texto', 'Usá hasta 200 caracteres.') });

    expect(CAMPO_TEXTO).toBe('texto');
    expect(await buscarArticulos('', 1)).toEqual({ tipo: 'campo', campo: CAMPO_TEXTO, mensaje: 'Usá hasta 200 caracteres.' });
  });

  it('buscarArticulos_SoloActivos_ViajaEnLaUrlSoloSiEsVerdadero', async () => {
    // D-15: el listado del catálogo sigue pidiendo sin el parámetro; la búsqueda de las líneas lo envía en true.
    const servidor = servidorFalso({
      'GET /api/articulos?pagina=1': json(200, PAGINA),
      'GET /api/articulos?texto=armazon&pagina=1': json(200, PAGINA),
      'GET /api/articulos?texto=armazon&pagina=2&soloActivos=true': json(200, { ...PAGINA, pagina: 2 }),
      'GET /api/articulos?pagina=1&soloActivos=true': json(200, PAGINA),
    });

    expect(await buscarArticulos('', 1, false)).toEqual({ tipo: 'ok', datos: PAGINA });
    expect(await buscarArticulos('armazon', 1)).toEqual({ tipo: 'ok', datos: PAGINA });
    expect(await buscarArticulos('armazon', 2, true)).toEqual({ tipo: 'ok', datos: { ...PAGINA, pagina: 2 } });
    expect(await buscarArticulos('', 1, true)).toEqual({ tipo: 'ok', datos: PAGINA });
    expect(servidor.llamadas.map((l) => l.ruta)).toEqual([
      '/api/articulos?pagina=1',
      '/api/articulos?texto=armazon&pagina=1',
      '/api/articulos?texto=armazon&pagina=2&soloActivos=true',
      '/api/articulos?pagina=1&soloActivos=true',
    ]);
  });
});

describe('cliente de la Api: presupuestos', () => {
  /** Datos ficticios (D-17). */
  const CLIENTE = {
    apellido: 'Pérez',
    nombre: 'Ana',
    dni: '99.999.999',
    domicilio: 'Calle Falsa 123',
    email: 'ana@mail.com',
    telefono: '11 5555-0000',
  };
  const LINEA = {
    codigoArticulo: 7,
    descripcion: 'Armazón metálico',
    precioUnitario: 1850,
    cantidad: 1,
    porcentajeDescuento: 0,
    precioConDescuento: 1850,
    precioFinal: 1850,
    orden: 1,
  };
  const GRABADO = {
    numero: 155,
    fecha: '2026-03-10',
    estado: 'borrador',
    cliente: { ...CLIENTE, dni: '99999999' },
    lineas: [LINEA],
    total: 1850,
    version: 3,
  };
  /** Una línea a enviar con la cantidad y el descuento de una línea nueva. */
  const UNA = [{ codigoArticulo: 7, cantidad: 1, porcentajeDescuento: 0 }];
  const ILEGIBLE = { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION };
  const NO_ENCONTRADO = { tipo: 'general', mensaje: MENSAJE_PRESUPUESTO_NO_ENCONTRADO };

  it('grabarPresupuesto_envía cantidad y descuento', async () => {
    const servidor = servidorFalso({ 'POST /api/presupuestos': json(201, GRABADO) });
    const lineas = [
      { codigoArticulo: 7, cantidad: 3, porcentajeDescuento: 10 },
      { codigoArticulo: 7, cantidad: 1, porcentajeDescuento: 0 },
      { codigoArticulo: 9, cantidad: 9999, porcentajeDescuento: 12.34 },
    ];

    expect(await grabarPresupuesto(CLIENTE, lineas)).toEqual({ tipo: 'ok', datos: GRABADO });
    const [pedido] = servidor.pedidosA('POST', '/api/presupuestos');
    // D-03 y D-12 de FEAT-003a: por línea, el código, la cantidad y el descuento; nunca precios ni descripción (R-01).
    expect(JSON.parse(String(pedido?.init?.body))).toEqual({ cliente: CLIENTE, lineas });
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(new Headers(pedido?.init?.headers).get('Content-Type')).toBe('application/json');
  });

  it('grabarPresupuesto_NoEnviaCamposAjenosDeLaLinea', async () => {
    const servidor = servidorFalso({ 'POST /api/presupuestos': json(201, GRABADO) });
    const conPrecios = { codigoArticulo: 7, cantidad: 2, porcentajeDescuento: 5, precioFinal: 1, descripcion: 'x' };

    await grabarPresupuesto(CLIENTE, [conPrecios]);
    const [pedido] = servidor.pedidosA('POST', '/api/presupuestos');
    expect(JSON.parse(String(pedido?.init?.body)).lineas).toEqual([
      { codigoArticulo: 7, cantidad: 2, porcentajeDescuento: 5 },
    ]);
  });

  it('CamposDeLaLinea_ComoLosNombraLaApi', () => {
    // D-06: los errores de la cantidad y el descuento de la línea i.
    expect(CAMPO_LINEAS).toBe('lineas');
    expect(campoCantidad(0)).toBe('lineas[0].cantidad');
    expect(campoDescuento(12)).toBe('lineas[12].porcentajeDescuento');
  });

  it('grabarPresupuesto_400_ErroresDeCantidadYDescuento', async () => {
    servidorFalso({
      'POST /api/presupuestos': json(400, {
        errors: {
          'lineas[0].cantidad': ['La cantidad debe ser un número entero mayor a 0'],
          'lineas[0].porcentajeDescuento': ['El descuento debe estar entre 0 y 100'],
          lineas: ['El total del presupuesto es demasiado alto.'],
        },
      }),
    });

    expect(await grabarPresupuesto(CLIENTE, UNA)).toEqual({
      tipo: 'campos',
      errores: [
        { campo: 'lineas[0].cantidad', mensaje: 'La cantidad debe ser un número entero mayor a 0' },
        { campo: 'lineas[0].porcentajeDescuento', mensaje: 'El descuento debe estar entre 0 y 100' },
        { campo: 'lineas', mensaje: 'El total del presupuesto es demasiado alto.' },
      ],
    });
  });

  it('leerPresupuesto_ImportesSinCerosFinales_LosLeeIgual', async () => {
    // D-14b: la Api devuelve 900 y 10.5, no 900.00 y 10.50; la lectura no depende del formato del número.
    const linea = { ...LINEA, precioUnitario: 1000, cantidad: 3, porcentajeDescuento: 10, precioConDescuento: 900 };
    const texto = (decimales: boolean) =>
      JSON.stringify({ ...GRABADO, lineas: [{ ...linea, precioFinal: 2700 }], total: 2700 }).replace(
        /"precioFinal":2700|"total":2700|"precioConDescuento":900/g,
        (parte) => (decimales ? `${parte}.00` : parte),
      );
    const crudo = (cuerpo: string) => () =>
      new Response(cuerpo, { status: 200, headers: { 'Content-Type': 'application/json' } });
    expect(texto(true)).toContain('"total":2700.00');
    servidorFalso({ 'GET /api/presupuestos/155': [crudo(texto(false)), crudo(texto(true))] });

    const esperado = { tipo: 'ok', datos: { ...GRABADO, lineas: [{ ...linea, precioFinal: 2700 }], total: 2700 } };
    expect(await consultarPresupuesto(155)).toEqual(esperado);
    expect(await consultarPresupuesto(155)).toEqual(esperado);
  });

  it('leerPresupuesto_0,1 + 0,2 = 0,3 se lee bien', async () => {
    // D-08: la suma se compara en centésimos (BigInt); en punto flotante 0,1 + 0,2 no es 0,3.
    const cuerpo = {
      ...GRABADO,
      lineas: [
        { ...LINEA, precioUnitario: 0.1, precioConDescuento: 0.1, precioFinal: 0.1 },
        { ...LINEA, precioUnitario: 0.2, precioConDescuento: 0.2, precioFinal: 0.2 },
      ],
      total: 0.3,
    };
    servidorFalso({ 'GET /api/presupuestos/155': json(200, cuerpo) });

    expect(await consultarPresupuesto(155)).toEqual({ tipo: 'ok', datos: cuerpo });
  });

  it('leerPresupuesto_AC07_TotalDeDosLineas', async () => {
    const cuerpo = {
      ...GRABADO,
      lineas: [
        { ...LINEA, precioUnitario: 6.67, cantidad: 3, porcentajeDescuento: 50, precioConDescuento: 3.34, precioFinal: 10.02 },
        { ...LINEA, precioUnitario: 1000, cantidad: 3, porcentajeDescuento: 10, precioConDescuento: 900, precioFinal: 2700 },
      ],
      total: 2710.02,
    };
    servidorFalso({ 'POST /api/presupuestos': json(201, cuerpo) });

    expect(await grabarPresupuesto(CLIENTE, UNA)).toEqual({ tipo: 'ok', datos: cuerpo });
  });

  it.each([
    ['total ausente', { ...GRABADO, total: undefined }],
    ['total nulo', { ...GRABADO, total: null }],
    ['total de texto', { ...GRABADO, total: '1850' }],
    ['total distinto de la suma', { ...GRABADO, total: 1850.01 }],
    ['total que es solo una de las líneas', { ...GRABADO, lineas: [LINEA, LINEA], total: 1850 }],
    ['total sobre el máximo seguro', { ...GRABADO, total: 90071992547410 }],
    ['total distinto de cero sin líneas', { ...GRABADO, lineas: [], total: 1 }],
  ])('leerPresupuesto_total faltante o distinto: error de lectura — %s', async (_caso, cuerpo) => {
    servidorFalso({
      'GET /api/presupuestos/155': json(200, cuerpo),
      'POST /api/presupuestos': json(201, cuerpo),
    });

    expect(await consultarPresupuesto(155)).toEqual(ILEGIBLE);
    expect(await grabarPresupuesto(CLIENTE, UNA)).toEqual(ILEGIBLE);
  });

  it('grabarPresupuesto_201SinOpcionales_LosLeeComoNull', async () => {
    const sinOpcionales = {
      ...GRABADO,
      estado: 'final',
      cliente: { ...GRABADO.cliente, domicilio: null, email: null, telefono: null },
    };
    servidorFalso({ 'POST /api/presupuestos': json(201, sinOpcionales) });

    expect(await grabarPresupuesto({ ...CLIENTE, domicilio: null, email: null, telefono: null }, UNA)).toEqual({
      tipo: 'ok',
      datos: sinOpcionales,
    });
  });

  it('grabarPresupuesto_400_DevuelveTodosLosErrores', async () => {
    servidorFalso({
      'POST /api/presupuestos': json(400, {
        errors: {
          dni: ['Ingresá el DNI del cliente'],
          'lineas[1]': ['El precio unitario no puede ser negativo'],
        },
      }),
    });

    expect(await grabarPresupuesto(CLIENTE, [...UNA, { codigoArticulo: 8, cantidad: 1, porcentajeDescuento: 0 }])).toEqual({
      tipo: 'campos',
      errores: [
        { campo: 'dni', mensaje: 'Ingresá el DNI del cliente' },
        { campo: 'lineas[1]', mensaje: 'El precio unitario no puede ser negativo' },
      ],
    });
  });

  it('grabarPresupuesto_413_MensajeDelCatalogo', async () => {
    servidorFalso({ 'POST /api/presupuestos': vacia(413) });

    expect(await grabarPresupuesto(CLIENTE, UNA)).toEqual({ tipo: 'general', mensaje: MENSAJE_ARTICULO_GRANDE });
  });

  it('grabarPresupuesto_401_SinSesion', async () => {
    const manejador = vi.fn();
    alPerderLaSesion(manejador);
    servidorFalso({ 'POST /api/presupuestos': vacia(401) });

    expect(await grabarPresupuesto(CLIENTE, UNA)).toEqual({ tipo: 'sinSesion' });
    expect(manejador).toHaveBeenCalledOnce();
  });

  it('consultarPresupuesto_200_DevuelveElPresupuestoSinCamposAjenos', async () => {
    const servidor = servidorFalso({
      'GET /api/presupuestos/155': json(200, {
        ...GRABADO,
        otro: 1,
        cliente: { ...GRABADO.cliente, otro: 2 },
        lineas: [{ ...LINEA, otro: 3 }],
      }),
    });

    expect(await consultarPresupuesto(155)).toEqual({ tipo: 'ok', datos: GRABADO });
    const [pedido] = servidor.pedidosA('GET', '/api/presupuestos/155');
    expect(pedido?.init?.cache).toBe('no-store');
  });

  it('consultarPresupuesto_404_NoEncontrado', async () => {
    servidorFalso({ 'GET /api/presupuestos/9': vacia(404) });

    expect(MENSAJE_PRESUPUESTO_NO_ENCONTRADO).toBe('No se encontró el presupuesto.');
    expect(await consultarPresupuesto(9)).toEqual(NO_ENCONTRADO);
  });

  it.each([0, -1, 1.5, Number.NaN, Number.MAX_SAFE_INTEGER + 1])(
    'consultarPresupuesto_NumeroInvalido_%d_NoConsulta',
    async (numero) => {
      const servidor = servidorFalso({});

      expect(await consultarPresupuesto(numero)).toEqual(NO_ENCONTRADO);
      expect(servidor.llamadas).toEqual([]);
    },
  );

  it('leerPresupuesto_EstadoDesconocido_RespuestaIlegible', async () => {
    servidorFalso({
      'GET /api/presupuestos/155': json(200, { ...GRABADO, estado: 'anulado' }),
      'POST /api/presupuestos': json(201, { ...GRABADO, estado: 'Borrador' }),
    });

    expect(await consultarPresupuesto(155)).toEqual(ILEGIBLE);
    expect(await grabarPresupuesto(CLIENTE, UNA)).toEqual(ILEGIBLE);
  });

  it.each([
    ['cuerpo que no es objeto', 'texto'],
    ['número cero', { ...GRABADO, numero: 0 }],
    ['número de texto', { ...GRABADO, numero: '155' }],
    ['número no entero', { ...GRABADO, numero: 1.5 }],
    ['fecha con otro formato', { ...GRABADO, fecha: '10/03/2026' }],
    ['fecha con hora', { ...GRABADO, fecha: '2026-03-10T00:00:00' }],
    ['fecha inexistente', { ...GRABADO, fecha: '2026-02-30' }],
    ['fecha con mes 13', { ...GRABADO, fecha: '2026-13-01' }],
    ['fecha ausente', { ...GRABADO, fecha: undefined }],
    ['estado ausente', { ...GRABADO, estado: undefined }],
    ['cliente ausente', { ...GRABADO, cliente: undefined }],
    ['apellido ausente', { ...GRABADO, cliente: { ...GRABADO.cliente, apellido: undefined } }],
    ['nombre que no es texto', { ...GRABADO, cliente: { ...GRABADO.cliente, nombre: 1 } }],
    ['DNI nulo', { ...GRABADO, cliente: { ...GRABADO.cliente, dni: null } }],
    ['domicilio ausente', { ...GRABADO, cliente: { ...GRABADO.cliente, domicilio: undefined } }],
    ['email que no es texto', { ...GRABADO, cliente: { ...GRABADO.cliente, email: 1 } }],
    ['teléfono ausente', { ...GRABADO, cliente: { ...GRABADO.cliente, telefono: undefined } }],
    ['líneas que no son lista', { ...GRABADO, lineas: {} }],
    ['línea que no es objeto', { ...GRABADO, lineas: [1] }],
    ['código de artículo cero', { ...GRABADO, lineas: [{ ...LINEA, codigoArticulo: 0 }] }],
    ['descripción ausente', { ...GRABADO, lineas: [{ ...LINEA, descripcion: undefined }] }],
    ['precio unitario de texto', { ...GRABADO, lineas: [{ ...LINEA, precioUnitario: '1850' }] }],
    ['precio unitario sobre el máximo seguro', { ...GRABADO, lineas: [{ ...LINEA, precioUnitario: 90071992547410 }] }],
    ['cantidad no entera', { ...GRABADO, lineas: [{ ...LINEA, cantidad: 1.5 }] }],
    ['descuento ausente', { ...GRABADO, lineas: [{ ...LINEA, porcentajeDescuento: undefined }] }],
    ['precio con descuento bajo el mínimo seguro', { ...GRABADO, lineas: [{ ...LINEA, precioConDescuento: -90071992547410 }] }],
    ['precio final nulo', { ...GRABADO, lineas: [{ ...LINEA, precioFinal: null }] }],
  ])('leerPresupuesto_%s_RespuestaIlegible', async (_caso, cuerpo) => {
    servidorFalso({ 'GET /api/presupuestos/155': json(200, cuerpo) });

    expect(await consultarPresupuesto(155)).toEqual(ILEGIBLE);
  });

  it('leerPresupuesto_ImportesEnElMaximoSeguro_LosLee', async () => {
    const enElLimite = {
      ...GRABADO,
      numero: Number.MAX_SAFE_INTEGER,
      lineas: [{ ...LINEA, precioUnitario: IMPORTE_MAXIMO_SEGURO, precioFinal: -IMPORTE_MAXIMO_SEGURO }],
      total: -IMPORTE_MAXIMO_SEGURO,
    };
    servidorFalso({ 'GET /api/presupuestos/155': json(200, enElLimite) });

    expect(await consultarPresupuesto(155)).toEqual({ tipo: 'ok', datos: enElLimite });
  });
});

describe('cliente de la Api: modificación y paso a Final del presupuesto (FEAT-003c)', () => {
  /** Datos ficticios (D-17). */
  const CLIENTE = {
    apellido: 'Pérez',
    nombre: 'Ana',
    dni: '99999999',
    domicilio: 'Calle Falsa 123',
    email: 'ana@example.com',
    telefono: '11 5555-0000',
  };
  const LINEA = {
    codigoArticulo: 7,
    descripcion: 'Armazón metálico',
    precioUnitario: 1850,
    cantidad: 1,
    porcentajeDescuento: 0,
    precioConDescuento: 1850,
    precioFinal: 1850,
    orden: 1,
  };
  const GRABADO = {
    numero: 155,
    fecha: '2026-03-10',
    estado: 'borrador',
    cliente: CLIENTE,
    lineas: [LINEA],
    total: 1850,
    version: 3,
  };
  const ILEGIBLE = { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION };
  const NO_ENCONTRADO = { tipo: 'general', mensaje: MENSAJE_PRESUPUESTO_NO_ENCONTRADO };
  /** Un 409 de la Api con un `detail` que la pantalla nunca muestra (D-07). */
  const conflicto = (motivo: string) =>
    json(409, { status: 409, title: 'No se pudo grabar el presupuesto.', detail: 'Texto del servidor.', motivo });

  it.each([
    ['versión ausente', { ...GRABADO, version: undefined }],
    ['versión nula', { ...GRABADO, version: null }],
    ['versión de texto', { ...GRABADO, version: '3' }],
    ['versión negativa', { ...GRABADO, version: -1 }],
    ['versión no entera', { ...GRABADO, version: 3.5 }],
    ['versión sobre el máximo seguro', { ...GRABADO, version: Number.MAX_SAFE_INTEGER + 1 }],
  ])('leerPresupuesto_SinVersion_ErrorDeLectura (%s)', async (_caso, cuerpo) => {
    servidorFalso({
      'GET /api/presupuestos/155': json(200, cuerpo),
      'POST /api/presupuestos': json(201, cuerpo),
      'PUT /api/presupuestos/155': json(200, cuerpo),
      'PUT /api/presupuestos/155/estado': json(200, cuerpo),
    });

    expect(await consultarPresupuesto(155)).toEqual(ILEGIBLE);
    expect(await grabarPresupuesto(CLIENTE, [])).toEqual(ILEGIBLE);
    expect(await modificarPresupuesto(155, CLIENTE, [], 3)).toEqual(ILEGIBLE);
    expect(await pasarPresupuestoAFinal(155, 3)).toEqual(ILEGIBLE);
  });

  it.each([
    ['orden ausente', { ...LINEA, orden: undefined }],
    ['orden nulo', { ...LINEA, orden: null }],
    ['orden cero', { ...LINEA, orden: 0 }],
    ['orden de texto', { ...LINEA, orden: '1' }],
    ['orden no entero', { ...LINEA, orden: 1.5 }],
  ])('leerPresupuesto_LineaSinOrden_ErrorDeLectura (%s)', async (_caso, linea) => {
    const cuerpo = { ...GRABADO, lineas: [LINEA, { ...linea }], total: 3700 };
    servidorFalso({
      'GET /api/presupuestos/155': json(200, cuerpo),
      'PUT /api/presupuestos/155': json(200, cuerpo),
    });

    expect(await consultarPresupuesto(155)).toEqual(ILEGIBLE);
    expect(await modificarPresupuesto(155, CLIENTE, [], 3)).toEqual(ILEGIBLE);
  });

  it('leerPresupuesto_VersionYOrden_LosLee', async () => {
    const cuerpo = {
      ...GRABADO,
      lineas: [LINEA, { ...LINEA, orden: 2 }],
      total: 3700,
      version: Number.MAX_SAFE_INTEGER,
    };
    servidorFalso({ 'GET /api/presupuestos/155': json(200, cuerpo) });

    expect(await consultarPresupuesto(155)).toEqual({ tipo: 'ok', datos: cuerpo });
  });

  it('modificarPresupuesto_EnviaVersionClienteYOrdenSoloEnLasGrabadas', async () => {
    const servidor = servidorFalso({ 'PUT /api/presupuestos/155': json(200, { ...GRABADO, version: 4 }) });
    const lineas = [
      { orden: 2, codigoArticulo: 7, cantidad: 3, porcentajeDescuento: 10 },
      { codigoArticulo: 9, cantidad: 1, porcentajeDescuento: 0 },
    ];

    expect(await modificarPresupuesto(155, CLIENTE, lineas, 3)).toEqual({
      tipo: 'ok',
      datos: { ...GRABADO, version: 4 },
    });
    const [pedido] = servidor.pedidosA('PUT', '/api/presupuestos/155');
    const cuerpo = JSON.parse(String(pedido?.init?.body)) as { lineas: object[] };
    expect(cuerpo).toEqual({ version: 3, cliente: CLIENTE, lineas });
    // D-03: la línea nueva va sin `orden`, ni siquiera nulo.
    expect('orden' in cuerpo.lineas[1]!).toBe(false);
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(new Headers(pedido?.init?.headers).get('Content-Type')).toBe('application/json');
  });

  it('modificarPresupuesto_NoEnviaCamposAjenosDeLaLinea', async () => {
    const servidor = servidorFalso({ 'PUT /api/presupuestos/155': json(200, GRABADO) });
    const conPrecios = { orden: 1, codigoArticulo: 7, cantidad: 2, porcentajeDescuento: 5, precioUnitario: 1 };

    await modificarPresupuesto(155, CLIENTE, [conPrecios], 3);
    const [pedido] = servidor.pedidosA('PUT', '/api/presupuestos/155');
    expect(JSON.parse(String(pedido?.init?.body)).lineas).toEqual([
      { orden: 1, codigoArticulo: 7, cantidad: 2, porcentajeDescuento: 5 },
    ]);
  });

  it.each([
    ['cambiado', MENSAJE_PRESUPUESTO_CAMBIADO],
    ['final', MENSAJE_PRESUPUESTO_FINAL],
  ])('modificarPresupuesto_409%s_TextoPropio', async (motivo, mensaje) => {
    servidorFalso({ 'PUT /api/presupuestos/155': conflicto(motivo) });

    expect(await modificarPresupuesto(155, CLIENTE, [], 3)).toEqual({ tipo: 'conflicto', motivo, mensaje });
  });

  it.each(['inactivo', 'otro', undefined])('modificarPresupuesto_409MotivoAjeno_%s_Ilegible', async (motivo) => {
    servidorFalso({ 'PUT /api/presupuestos/155': conflicto(motivo as string) });

    expect(await modificarPresupuesto(155, CLIENTE, [], 3)).toEqual(ILEGIBLE);
  });

  it('modificarPresupuesto_400_DevuelveTodosLosErrores', async () => {
    servidorFalso({
      'PUT /api/presupuestos/155': json(400, {
        errors: {
          version: ['Volvé a abrir el presupuesto para editarlo.'],
          'lineas[0].orden': ['La línea no corresponde a este presupuesto.'],
        },
      }),
    });

    expect(await modificarPresupuesto(155, CLIENTE, [], 3)).toEqual({
      tipo: 'campos',
      errores: [
        { campo: 'version', mensaje: 'Volvé a abrir el presupuesto para editarlo.' },
        { campo: 'lineas[0].orden', mensaje: 'La línea no corresponde a este presupuesto.' },
      ],
    });
    expect(campoOrden(0)).toBe('lineas[0].orden');
  });

  it('modificarPresupuesto_404YNumeroInvalido_NoEncontrado', async () => {
    const servidor = servidorFalso({ 'PUT /api/presupuestos/9': vacia(404) });

    expect(await modificarPresupuesto(9, CLIENTE, [], 3)).toEqual(NO_ENCONTRADO);
    expect(await modificarPresupuesto(0, CLIENTE, [], 3)).toEqual(NO_ENCONTRADO);
    expect(servidor.llamadas.map((l) => l.ruta)).toEqual(['/api/presupuestos/9']);
  });

  it('modificarPresupuesto_413_MensajeGeneral', async () => {
    servidorFalso({ 'PUT /api/presupuestos/155': vacia(413) });

    expect(await modificarPresupuesto(155, CLIENTE, [], 3)).toEqual({ tipo: 'general', mensaje: MENSAJE_ARTICULO_GRANDE });
  });

  it('pasarPresupuestoAFinal_EnviaEstadoFinalYVersion', async () => {
    const final = { ...GRABADO, estado: 'final', version: 4 };
    const servidor = servidorFalso({ 'PUT /api/presupuestos/155/estado': json(200, final) });

    expect(await pasarPresupuestoAFinal(155, 3)).toEqual({ tipo: 'ok', datos: final });
    const [pedido] = servidor.pedidosA('PUT', '/api/presupuestos/155/estado');
    expect(JSON.parse(String(pedido?.init?.body))).toEqual({ estado: 'final', version: 3 });
  });

  it.each([
    ['cambiado', MENSAJE_PRESUPUESTO_CAMBIADO],
    ['final', MENSAJE_PRESUPUESTO_FINAL],
  ])('pasarPresupuestoAFinal_409%s_TextoPropio', async (motivo, mensaje) => {
    servidorFalso({ 'PUT /api/presupuestos/155/estado': conflicto(motivo) });

    expect(await pasarPresupuestoAFinal(155, 3)).toEqual({ tipo: 'conflicto', motivo, mensaje });
  });

  it.each([
    ['400', problema(400, 'estado', 'El presupuesto solo puede pasar a Final.'), MENSAJE_PEDIDO_RECHAZADO],
    ['404', vacia(404), MENSAJE_PRESUPUESTO_NO_ENCONTRADO],
    ['413', vacia(413), MENSAJE_PEDIDO_RECHAZADO],
  ])('pasarPresupuestoAFinal_%s_MensajeGeneral', async (_caso, respuesta, mensaje) => {
    servidorFalso({ 'PUT /api/presupuestos/155/estado': respuesta });

    expect(await pasarPresupuestoAFinal(155, 3)).toEqual({ tipo: 'general', mensaje });
  });

  it('pasarPresupuestoAFinal_NumeroInvalido_NoEnvia', async () => {
    const servidor = servidorFalso({});

    expect(await pasarPresupuestoAFinal(-1, 3)).toEqual(NO_ENCONTRADO);
    expect(servidor.llamadas).toEqual([]);
  });
});

describe('cliente de la Api: búsqueda de presupuestos (FEAT-006)', () => {
  const RUTA_BUSQUEDA = '/api/presupuestos/busqueda';
  const CLAVE = `POST ${RUTA_BUSQUEDA}`;
  const SIN_FILTROS = { apellido: '', nombre: '', dni: '', desde: '', hasta: '' };
  /** Datos ficticios (Ley 25.326): ningún dato real de un cliente. */
  const FILTRO = { apellido: '  González ', nombre: 'María', dni: '23.456', desde: '2026-03-10', hasta: '2026-03-20' };
  const FILA = {
    numero: 155,
    fecha: '2026-03-10',
    apellido: 'González',
    nombre: 'María José',
    dni: '23456789',
    estado: 'final',
    total: 2710.02,
  };
  const PAGINA = { presupuestos: [FILA], pagina: 1, tamanoPagina: 50, total: 1 };
  const ILEGIBLE = { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION };

  const cuerpoEnviado = (servidor: ReturnType<typeof servidorFalso>) => {
    const [pedido] = servidor.pedidosA('POST', RUTA_BUSQUEDA);
    return JSON.parse(String(pedido?.init?.body)) as unknown;
  };

  it('buscarPresupuestos envía el cuerpo y no pone filtros en la URL', async () => {
    const servidor = servidorFalso({ [CLAVE]: json(200, PAGINA) });

    expect(await buscarPresupuestos(FILTRO, 3)).toEqual({ tipo: 'ok', datos: PAGINA });

    // R-02, ADR-019: la ruta es fija; Apellido, Nombre y DNI viajan solo en el cuerpo JSON.
    expect(servidor.llamadas.map((l) => `${l.metodo} ${l.ruta}`)).toEqual([CLAVE]);
    const [pedido] = servidor.llamadas;
    expect(pedido?.ruta).not.toContain('?');
    for (const valor of ['González', 'Gonz', 'María', '23.456', '2026-03']) {
      expect(pedido?.ruta).not.toContain(valor);
      expect(pedido?.ruta).not.toContain(encodeURIComponent(valor));
    }
    expect(new Headers(pedido?.init?.headers).get('Content-Type')).toBe('application/json');
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(pedido?.init?.cache).toBe('no-store');
    // Los textos van recortados; la fecha en yyyy-MM-dd, como la da el campo de fecha.
    expect(cuerpoEnviado(servidor)).toEqual({
      apellido: 'González',
      nombre: 'María',
      dni: '23.456',
      desde: '2026-03-10',
      hasta: '2026-03-20',
      pagina: 3,
    });
  });

  it('buscarPresupuestos_SinFiltros_SoloLaPagina', async () => {
    // D-23: una fecha vacía nunca viaja como "" (el servidor responde 400); los textos vacíos o en blanco tampoco.
    const servidor = servidorFalso({ [CLAVE]: json(200, { ...PAGINA, presupuestos: [], total: 0 }) });

    await buscarPresupuestos({ ...SIN_FILTROS, apellido: '   ', dni: ' ' }, 1);

    expect(cuerpoEnviado(servidor)).toEqual({ pagina: 1 });
  });

  it('buscarPresupuestos_SoloHasta_NoEnviaDesde', async () => {
    const servidor = servidorFalso({ [CLAVE]: json(200, PAGINA) });

    await buscarPresupuestos({ ...SIN_FILTROS, hasta: '2026-03-10' }, 1);

    expect(cuerpoEnviado(servidor)).toEqual({ hasta: '2026-03-10', pagina: 1 });
  });

  it('buscarPresupuestos_200_CopiaSoloLosSieteDatos', async () => {
    servidorFalso({
      [CLAVE]: json(200, {
        ...PAGINA,
        presupuestos: [{ ...FILA, domicilio: 'Calle Falsa 123', email: 'ana@example.com' }],
        extra: 1,
      }),
    });

    expect(await buscarPresupuestos(SIN_FILTROS, 1)).toEqual({ tipo: 'ok', datos: PAGINA });
  });

  it('buscarPresupuestos_200_PaginaPosteriorALaUltimaVacia', async () => {
    const vacia3 = { presupuestos: [], pagina: 4, tamanoPagina: 50, total: 120 };
    servidorFalso({ [CLAVE]: json(200, vacia3) });

    expect(await buscarPresupuestos(SIN_FILTROS, 4)).toEqual({ tipo: 'ok', datos: vacia3 });
  });

  it.each([
    ['sin la lista', { pagina: 1, tamanoPagina: 50, total: 1 }],
    ['página 0', { ...PAGINA, pagina: 0 }],
    ['tamaño 0', { ...PAGINA, tamanoPagina: 0 }],
    ['total negativo', { ...PAGINA, total: -1 }],
    ['número 0', { ...PAGINA, presupuestos: [{ ...FILA, numero: 0 }] }],
    ['número como texto', { ...PAGINA, presupuestos: [{ ...FILA, numero: '155' }] }],
    ['fecha inexistente', { ...PAGINA, presupuestos: [{ ...FILA, fecha: '2026-02-30' }] }],
    ['fecha dd/mm/aaaa', { ...PAGINA, presupuestos: [{ ...FILA, fecha: '10/03/2026' }] }],
    ['estado desconocido', { ...PAGINA, presupuestos: [{ ...FILA, estado: 'anulado' }] }],
    ['total como texto', { ...PAGINA, presupuestos: [{ ...FILA, total: '2710.02' }] }],
    ['total fuera del límite', { ...PAGINA, presupuestos: [{ ...FILA, total: IMPORTE_MAXIMO_SEGURO * 2 }] }],
    ['apellido nulo', { ...PAGINA, presupuestos: [{ ...FILA, apellido: null }] }],
    ['nombre ausente', { ...PAGINA, presupuestos: [{ ...FILA, nombre: undefined }] }],
    ['dni como número', { ...PAGINA, presupuestos: [{ ...FILA, dni: 23456789 }] }],
    ['fila nula', { ...PAGINA, presupuestos: [null] }],
    ['cuerpo nulo', null],
  ])('buscarPresupuestos_200Ilegible_%s', async (_caso, cuerpo) => {
    servidorFalso({ [CLAVE]: json(200, cuerpo) });

    expect(await buscarPresupuestos(SIN_FILTROS, 1)).toEqual(ILEGIBLE);
  });

  it('buscarPresupuestos_400_DevuelveTodosLosErrores', async () => {
    servidorFalso({
      [CLAVE]: json(400, {
        status: 400,
        errors: {
          desde: ['La fecha Desde no puede ser posterior a Hasta.'],
          dni: ['Usá hasta 20 caracteres.'],
        },
      }),
    });

    expect(await buscarPresupuestos(FILTRO, 1)).toEqual({
      tipo: 'campos',
      errores: [
        { campo: 'desde', mensaje: 'La fecha Desde no puede ser posterior a Hasta.' },
        { campo: 'dni', mensaje: 'Usá hasta 20 caracteres.' },
      ],
    });
  });

  it('buscarPresupuestos_400SinErrores_MensajeGeneral', async () => {
    servidorFalso({ [CLAVE]: vacia(400) });

    expect(await buscarPresupuestos(FILTRO, 1)).toEqual({ tipo: 'general', mensaje: MENSAJE_PEDIDO_RECHAZADO });
  });

  it('buscarPresupuestos_413_MensajeGeneralPropio', async () => {
    servidorFalso({ [CLAVE]: problema(413, 'apellido', 'texto del servidor') });

    expect(MENSAJE_BUSQUEDA_GRANDE).toBe('La búsqueda es demasiado larga.');
    expect(await buscarPresupuestos(FILTRO, 1)).toEqual({ tipo: 'general', mensaje: MENSAJE_BUSQUEDA_GRANDE });
  });

  it('buscarPresupuestos_401_SinSesion', async () => {
    const manejador = vi.fn();
    alPerderLaSesion(manejador);
    servidorFalso({ [CLAVE]: vacia(401) });

    expect(await buscarPresupuestos(FILTRO, 1)).toEqual({ tipo: 'sinSesion' });
    expect(manejador).toHaveBeenCalledOnce();
    alPerderLaSesion(null);
  });

  it.each([
    ['falla de red', sinRed],
    ['500', vacia(500)],
  ])('buscarPresupuestos_%s_SinConexion', async (_caso, respuesta) => {
    servidorFalso({ [CLAVE]: respuesta });

    expect(await buscarPresupuestos(FILTRO, 1)).toEqual(ILEGIBLE);
  });
});

describe('cliente de la Api: PDF del presupuesto (FEAT-006)', () => {
  const RUTA_PDF = '/api/presupuestos/155/pdf';
  const CLAVE = `GET ${RUTA_PDF}`;
  const ILEGIBLE = { tipo: 'general', mensaje: MENSAJE_SIN_CONEXION };
  /** El comienzo de un PDF: basta para comprobar que el cliente entrega los bytes tal cual. */
  const BYTES_PDF = '%PDF-1.7\n';
  const pdf = (tipo = 'application/pdf') => () =>
    new Response(BYTES_PDF, {
      status: 200,
      headers: { 'Content-Type': tipo, 'Content-Disposition': 'attachment; filename=presupuesto-155.pdf' },
    });
  /** El 409 de la Api para un Borrador (D-11, D-16): el título y el detalle del servidor nunca se muestran. */
  const conflicto = (motivo: unknown) =>
    json(409, {
      status: 409,
      title: 'No se pudo generar el PDF del presupuesto.',
      detail: 'El presupuesto está en Borrador.',
      motivo,
    });

  it('descargarPdfPresupuesto_Final_DevuelveElPdfComoBlob', async () => {
    const servidor = servidorFalso({ [CLAVE]: pdf() });

    const resultado = await descargarPdfPresupuesto(155);

    expect(resultado.tipo).toBe('ok');
    const datos = (resultado as { tipo: 'ok'; datos: Blob }).datos;
    // El `Blob` de `Response` es el de Node, no el de jsdom: se compara por su tipo y su contenido, no con instanceof.
    expect(datos.type).toBe('application/pdf');
    expect(await datos.text()).toBe(BYTES_PDF);
    const pedidos = servidor.pedidosA('GET', RUTA_PDF);
    expect(pedidos).toHaveLength(1);
    // Mismo origen, con la cookie de la sesión y sin cuerpo (D-10).
    expect(pedidos[0]?.init?.credentials).toBe('same-origin');
    expect(pedidos[0]?.init?.body).toBeUndefined();
  });

  it('cliente: 409 con motivo borrador se lee como conflicto', async () => {
    servidorFalso({ [CLAVE]: conflicto('borrador') });

    expect(await descargarPdfPresupuesto(155)).toEqual({
      tipo: 'conflicto',
      motivo: 'borrador',
      mensaje: MENSAJE_PRESUPUESTO_BORRADOR_PDF,
    });
    expect(MENSAJE_PRESUPUESTO_BORRADOR_PDF).toBe(
      'El presupuesto está en Borrador: pasalo a Final para descargar el PDF.',
    );
  });

  it.each(['final', 'cambiado', 'otro', undefined])('descargarPdfPresupuesto_409MotivoAjeno_%s_Ilegible', async (m) => {
    servidorFalso({ [CLAVE]: conflicto(m) });

    expect(await descargarPdfPresupuesto(155)).toEqual(ILEGIBLE);
  });

  it('descargarPdfPresupuesto_401_SinSesionYAvisa', async () => {
    const manejador = vi.fn();
    alPerderLaSesion(manejador);
    servidorFalso({ [CLAVE]: vacia(401) });

    expect(await descargarPdfPresupuesto(155)).toEqual({ tipo: 'sinSesion' });
    expect(manejador).toHaveBeenCalledOnce();
  });

  it('descargarPdfPresupuesto_404_NoEncontrado', async () => {
    servidorFalso({ [CLAVE]: vacia(404) });

    expect(await descargarPdfPresupuesto(155)).toEqual({
      tipo: 'general',
      mensaje: MENSAJE_PRESUPUESTO_NO_ENCONTRADO,
    });
  });

  it.each([
    ['falla de red', sinRed],
    ['500', vacia(500)],
    // Una respuesta exitosa que no es un PDF no se entrega como archivo.
    ['200 que no es PDF', pdf('application/json')],
    ['200 sin tipo', () => new Response(BYTES_PDF, { status: 200, headers: { 'Content-Type': '' } })],
    // Sin el encabezado: un cuerpo de bytes no le agrega un Content-Type, así `get` devuelve null.
    ['200 sin encabezado de tipo', () => new Response(new TextEncoder().encode(BYTES_PDF), { status: 200 })],
  ])('descargarPdfPresupuesto_%s_SinConexion', async (_caso, respuesta) => {
    servidorFalso({ [CLAVE]: respuesta });

    expect(await descargarPdfPresupuesto(155)).toEqual(ILEGIBLE);
  });

  it('descargarPdfPresupuesto_CuerpoIlegible_SinConexion', async () => {
    servidorFalso({
      [CLAVE]: () => {
        const respuesta = new Response(BYTES_PDF, { status: 200, headers: { 'Content-Type': 'application/pdf' } });
        vi.spyOn(respuesta, 'blob').mockRejectedValue(new TypeError('Failed to fetch'));
        return respuesta;
      },
    });

    expect(await descargarPdfPresupuesto(155)).toEqual(ILEGIBLE);
  });

  it.each([0, -1, 1.5, Number.MAX_SAFE_INTEGER + 1, Number.NaN])(
    'descargarPdfPresupuesto_NumeroInvalido_%s_NoSeEnvia',
    async (numero) => {
      const servidor = servidorFalso({});

      expect(await descargarPdfPresupuesto(numero)).toEqual({
        tipo: 'general',
        mensaje: MENSAJE_PRESUPUESTO_NO_ENCONTRADO,
      });
      expect(servidor.llamadas.filter((l) => l.ruta.startsWith('/api/presupuestos'))).toEqual([]);
    },
  );
});
