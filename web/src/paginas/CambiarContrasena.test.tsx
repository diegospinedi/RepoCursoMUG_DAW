import { fireEvent, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import {
  comprobarQueLaContrasenaNoQuedoYLimpiar,
  escribirEn,
  mensajeJuntoAlCampo,
  rastrosDeContrasenas,
  vigilarConsola,
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

// En todas las pruebas: ninguna contraseña escrita queda en el almacenamiento del navegador, la URL ni la navegación.
afterEach(comprobarQueLaContrasenaNoQuedoYLimpiar);

const RUTA = '/cambiar-contrasena';
const TITULO = 'Cambiar la contraseña';
const BOTON = 'Cambiar la contraseña';
const MENSAJE_CORTA = 'La contraseña debe tener al menos 8 caracteres. Ingresá una más larga.';
const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';

const conSesion = {
  'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }),
  'GET /api/sesion': vacia(204),
};

/** Abre la pantalla y espera a que muestre el formulario (después de consultar la sesión). */
async function abrir(ruta: string | string[] = RUTA) {
  const app = renderizarApp(ruta);
  await waitFor(() => expect(screen.queryByRole('heading', { name: TITULO })).not.toBeNull());
  await waitFor(() => expect(screen.queryByRole('button', { name: BOTON })).not.toBeNull());
  return app;
}

/** Escribe las dos contraseñas y pulsa el botón. Devuelve los dos campos. */
async function cambiar(actual: string, nueva: string) {
  const campoActual = await escribirEn('Contraseña actual', actual);
  const campoNueva = await escribirEn('Contraseña nueva', nueva);
  fireEvent.click(screen.getByRole('button', { name: BOTON }));
  return { campoActual, campoNueva };
}

describe('CambiarContrasena', () => {
  it('CambiarContrasena_Correcta_MuestraExitoEnInicio', async () => {
    const servidor = servidorFalso({ ...conSesion, 'POST /api/acceso/cambiar': vacia(204) });
    await abrir();
    expect(screen.getByRole('button', { name: BOTON }).closest('form')?.getAttribute('method')).toBe('post');

    const { campoActual, campoNueva } = await cambiar('anterior1', 'nueva12345');

    expect(await screen.findByRole('heading', { name: 'Sesión iniciada' })).toBeTruthy();
    expect(screen.getByText('La contraseña se cambió.')).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/');
    const [pedido] = servidor.pedidosA('POST', '/api/acceso/cambiar');
    expect(JSON.parse(String(pedido?.init?.body))).toEqual({
      contrasenaActual: 'anterior1',
      contrasenaNueva: 'nueva12345',
    });
    expect(pedido?.init?.credentials).toBe('same-origin');
    expect(new Headers(pedido?.init?.headers).get('Content-Type')).toBe('application/json');
    expect(campoActual.getAttribute('autocomplete')).toBe('current-password');
    expect(campoNueva.getAttribute('autocomplete')).toBe('new-password');
    expect(campoActual.getAttribute('name')).toBe('contrasenaActual');
    expect(campoNueva.getAttribute('name')).toBe('contrasenaNueva');
    expect(campoActual.getAttribute('type')).toBe('password');
    expect(campoNueva.getAttribute('type')).toBe('password');
    // La pantalla se desmontó: los campos con las contraseñas ya no existen.
    expect(campoActual.isConnected).toBe(false);
    expect(campoNueva.isConnected).toBe(false);
  });

  it('CambiarContrasena_ActualIncorrecta_RechazadaConMensajeJuntoAlCampo', async () => {
    const mensaje = 'La contraseña actual no es válida.';
    servidorFalso({ ...conSesion, 'POST /api/acceso/cambiar': problema(400, 'contrasenaActual', mensaje) });
    await abrir();

    const { campoActual, campoNueva } = await cambiar('incorrecta', 'nueva12345');

    await waitFor(() => expect(mensajeJuntoAlCampo(campoActual)).toBe(mensaje));
    expect(campoActual.getAttribute('aria-invalid')).toBe('true');
    expect(mensajeJuntoAlCampo(campoNueva)).toBeNull();
    expect(campoNueva.getAttribute('aria-invalid')).toBe('false');
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
    expect(screen.queryByText('La contraseña se cambió.')).toBeNull();
  });

  it('CambiarContrasena_NuevaRechazadaPorElServidor_MensajeJuntoAlCampoNueva', async () => {
    const mensaje = 'La contraseña tiene caracteres no válidos. Escribila de nuevo.';
    servidorFalso({ ...conSesion, 'POST /api/acceso/cambiar': problema(400, 'contrasenaNueva', mensaje) });
    await abrir();

    const { campoActual, campoNueva } = await cambiar('anterior1', 'nueva12345');

    await waitFor(() => expect(mensajeJuntoAlCampo(campoNueva)).toBe(mensaje));
    expect(mensajeJuntoAlCampo(campoActual)).toBeNull();
  });

  it('CambiarContrasena_Nueva7Caracteres_InvalidaSinEnviar', async () => {
    const servidor = servidorFalso({ ...conSesion, 'POST /api/acceso/cambiar': vacia(204) });
    await abrir();

    const { campoActual, campoNueva } = await cambiar('anterior1', '1234567');

    await waitFor(() => expect(mensajeJuntoAlCampo(campoNueva)).toBe(MENSAJE_CORTA));
    expect(campoNueva.getAttribute('aria-invalid')).toBe('true');
    expect(mensajeJuntoAlCampo(campoActual)).toBeNull();
    expect(servidor.pedidosA('POST', '/api/acceso/cambiar')).toHaveLength(0);
  });

  it('CambiarContrasena_ActualVacia_RequeridaSinEnviar', async () => {
    const servidor = servidorFalso({ ...conSesion, 'POST /api/acceso/cambiar': vacia(204) });
    await abrir();

    const { campoActual } = await cambiar('', 'nueva12345');

    await waitFor(() => expect(mensajeJuntoAlCampo(campoActual)).toBe('Ingresá la contraseña actual.'));
    expect(servidor.pedidosA('POST', '/api/acceso/cambiar')).toHaveLength(0);
  });

  it('CambiarContrasena_SinSesionAlAbrir_NoAutorizadoNavegaAIngreso', async () => {
    // Historial: una entrada previa distinta de /ingreso y después la pantalla de cambio, que se reemplaza.
    const servidor = servidorFalso({
      'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }),
      'GET /api/sesion': vacia(401),
      'POST /api/acceso/cambiar': vacia(204),
    });
    const { volver } = renderizarApp(['/definir-contrasena', RUTA]);

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
    // La pantalla consultó la sesión una sola vez, directamente (sin pasar por el arranque), y no envió nada.
    expect(servidor.pedidosA('GET', '/api/sesion')).toHaveLength(1);
    expect(servidor.pedidosA('GET', '/api/acceso/estado')).toHaveLength(0);
    expect(servidor.pedidosA('POST', '/api/acceso/cambiar')).toHaveLength(0);

    await volver();
    // Atrás no vuelve a la pantalla protegida: la navegación reemplazó su entrada.
    expect(screen.getByTestId('ubicacion').textContent).toBe('/definir-contrasena');
  });

  it('CambiarContrasena_401AlEnviar_NavegaAIngreso', async () => {
    servidorFalso({ ...conSesion, 'POST /api/acceso/cambiar': vacia(401) });
    await abrir();

    await cambiar('anterior1', 'nueva12345');

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
  });

  it('CambiarContrasena_409_ConflictoMuestraMensaje', async () => {
    servidorFalso({
      ...conSesion,
      'POST /api/acceso/cambiar': problemaGeneral(409, 'Definí la contraseña primero'),
    });
    await abrir();

    const { campoActual } = await cambiar('anterior1', 'nueva12345');

    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toBe('No hay una contraseña definida. Volvé a ingresar.');
    expect(screen.getByRole('link', { name: 'Ir al ingreso' }).getAttribute('href')).toBe('/ingreso');
    expect(mensajeJuntoAlCampo(campoActual)).toBeNull();
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
  });

  it('CambiarContrasena_ErrorDeRed_MuestraMensajeGeneral', async () => {
    servidorFalso({ ...conSesion, 'POST /api/acceso/cambiar': sinRed });
    await abrir();

    const { campoActual, campoNueva } = await cambiar('anterior1', 'nueva12345');

    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toBe(MENSAJE_SIN_CONEXION);
    expect(mensajeJuntoAlCampo(campoActual)).toBeNull();
    expect(mensajeJuntoAlCampo(campoNueva)).toBeNull();
    expect(screen.getByRole('button', { name: BOTON }).hasAttribute('disabled')).toBe(false);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
  });

  it('CambiarContrasena_ErrorAlConsultarLaSesion_MensajeGeneralYFormulario', async () => {
    servidorFalso({ 'GET /api/sesion': sinRed });
    await abrir();

    expect(screen.getByRole('alert').textContent).toBe(MENSAJE_SIN_CONEXION);
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
  });

  it('CambiarContrasena_SeDejaLaPantallaAntesDeLaRespuesta_NoHaceNada', async () => {
    // La respuesta de la sesión llega cuando la persona ya volvió atrás: la pantalla desmontada no la procesa.
    let responder: (respuesta: Response) => void = () => {};
    servidorFalso({
      'GET /api/sesion': () => new Promise<Response>((resolver) => (responder = resolver)),
    });
    const { volver } = renderizarApp(['/definir-contrasena', RUTA]);
    await waitFor(() => expect(screen.getByText('Cargando…')).toBeTruthy());

    await volver();
    responder(new Response(null, { status: 500 }));

    expect(await screen.findByRole('heading', { name: 'Definí la contraseña de acceso' })).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/definir-contrasena');
  });

  it('CambiarContrasena_Cancelar_VuelveAlInicio', async () => {
    servidorFalso(conSesion);
    await abrir();

    fireEvent.click(screen.getByRole('link', { name: 'Volver al inicio' }));

    expect(await screen.findByRole('heading', { name: 'Sesión iniciada' })).toBeTruthy();
    expect(screen.queryByText('La contraseña se cambió.')).toBeNull();
  });

  it('CambiarContrasena_NingunaContrasenaQuedaEnElNavegador', async () => {
    vigilarConsola();
    const servidor = servidorFalso({
      ...conSesion,
      'POST /api/acceso/cambiar': [problema(400, 'contrasenaActual', 'La contraseña actual no es válida.'), vacia(204)],
    });
    await abrir();

    // Primero un rechazo y después el cambio: ninguno de los dos caminos deja rastros.
    const { campoActual } = await cambiar('incorrecta-1', 'nueva-12345');
    await waitFor(() => expect(mensajeJuntoAlCampo(campoActual)).not.toBeNull());
    expect(rastrosDeContrasenas()).toEqual([]);
    await cambiar('anterior-1', 'nueva-12345');
    expect(await screen.findByText('La contraseña se cambió.')).toBeTruthy();

    expect(rastrosDeContrasenas()).toEqual([]);
    // Las contraseñas viajaron solo en el cuerpo, nunca en la ruta.
    for (const pedido of servidor.llamadas) {
      expect(pedido.ruta).not.toMatch(/incorrecta|anterior|nueva-/);
    }
    // Ningún campo de la pantalla siguiente conserva un valor.
    for (const campo of Array.from(document.querySelectorAll('input'))) {
      expect(campo.value).toBe('');
    }
  });
});
