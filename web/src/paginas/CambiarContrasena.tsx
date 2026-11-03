import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import { CAMPO_ACTUAL, CAMPO_NUEVA, cambiarContrasena, consultarSesion } from '../api/cliente';
import { validarContrasena } from '../acceso/politicaContrasena';
import { CampoContrasena } from '../componentes/CampoContrasena';

/** Mensaje que el inicio muestra después de un cambio exitoso; viaja en el estado de la navegación. */
export const MENSAJE_CAMBIADA = 'La contraseña se cambió.';
export const MENSAJE_ACTUAL_REQUERIDA = 'Ingresá la contraseña actual.';

/**
 * Cambio de la contraseña con la sesión iniciada (FR-03). Consulta la sesión una sola vez al montarse y, sin sesión,
 * va a `/ingreso` reemplazando la entrada. Valida la nueva en el navegador con la misma política que la definición;
 * los errores del servidor van junto al campo que indica la Api. Al tener éxito vuelve al inicio con el mensaje en el
 * estado de la navegación. Las contraseñas viven solo en el estado de este componente; no hay consultas periódicas.
 */
export function CambiarContrasena() {
  const navegar = useNavigate();
  const [verificando, setVerificando] = useState(true);
  const [actual, setActual] = useState('');
  const [nueva, setNueva] = useState('');
  const [errorActual, setErrorActual] = useState<string | null>(null);
  const [errorNueva, setErrorNueva] = useState<string | null>(null);
  const [general, setGeneral] = useState<{ mensaje: string; conflicto: boolean } | null>(null);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    let vigente = true;
    async function verificar() {
      const sesion = await consultarSesion();
      if (!vigente) {
        return;
      }
      if (sesion.tipo === 'sinSesion') {
        // El cliente ya avisó al manejador de App; se navega igual para no depender de ese registro (ver Arranque).
        navegar('/ingreso', { replace: true });
        return;
      }
      if (sesion.tipo !== 'ok') {
        setGeneral({ mensaje: sesion.mensaje, conflicto: false });
      }
      setVerificando(false);
    }
    void verificar();
    return () => {
      vigente = false;
    };
  }, [navegar]);

  async function alEnviar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    setGeneral(null);
    const errorLocalActual = actual === '' ? MENSAJE_ACTUAL_REQUERIDA : null;
    const errorLocalNueva = validarContrasena(nueva);
    setErrorActual(errorLocalActual);
    setErrorNueva(errorLocalNueva);
    if (errorLocalActual !== null || errorLocalNueva !== null) {
      return;
    }

    setEnviando(true);
    const resultado = await cambiarContrasena(actual, nueva);
    setEnviando(false);
    switch (resultado.tipo) {
      case 'ok':
        navegar('/', { replace: true, state: { mensaje: MENSAJE_CAMBIADA } });
        break;
      case 'campo':
        // La Api indica el campo; cualquier otro (o ninguno conocido) va a la actual, como hace la Api.
        if (resultado.campo === CAMPO_NUEVA) {
          setErrorNueva(resultado.mensaje);
        } else {
          setErrorActual(resultado.mensaje);
        }
        break;
      case 'conflicto':
        setGeneral({ mensaje: resultado.mensaje, conflicto: true });
        break;
      case 'general':
      case 'bloqueado':
        setGeneral({ mensaje: resultado.mensaje, conflicto: false });
        break;
      case 'sinSesion':
        navegar('/ingreso', { replace: true });
        break;
    }
  }

  if (verificando) {
    return <p className="cargando">Cargando…</p>;
  }

  return (
    <section className="pantalla">
      <h1>Cambiar la contraseña</h1>
      {/* method="post": si el envío por JavaScript no llegara a correr, el navegador no pone las contraseñas en la URL. */}
      <form method="post" onSubmit={alEnviar} noValidate>
        <CampoContrasena
          etiqueta="Contraseña actual"
          nombre={CAMPO_ACTUAL}
          autocompletar="current-password"
          valor={actual}
          alCambiar={setActual}
          error={errorActual}
        />
        <CampoContrasena
          etiqueta="Contraseña nueva"
          nombre={CAMPO_NUEVA}
          autocompletar="new-password"
          valor={nueva}
          alCambiar={setNueva}
          error={errorNueva}
        />
        {general !== null && (
          <div className="mensaje-general">
            <p role="alert">{general.mensaje}</p>
            {general.conflicto && <Link to="/ingreso">Ir al ingreso</Link>}
          </div>
        )}
        <button type="submit" disabled={enviando}>
          Cambiar la contraseña
        </button>
      </form>
      <p>
        <Link to="/">Volver al inicio</Link>
      </p>
    </section>
  );
}
