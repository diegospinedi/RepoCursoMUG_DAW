import { useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router';
import { salir } from '../api/cliente';

/** El aviso que otra pantalla deja en el estado de la navegación (por ejemplo, después de cambiar la contraseña). */
function avisoDe(estado: unknown): string | null {
  if (typeof estado !== 'object' || estado === null || !('mensaje' in estado)) {
    return null;
  }
  return typeof estado.mensaje === 'string' ? estado.mensaje : null;
}

/**
 * Pantalla mínima con la sesión iniciada; las pantallas con datos llegan en los PRDs siguientes. No consulta en
 * forma periódica: cualquier pedido con cookie renovaría la sesión.
 */
export function Inicio() {
  const navegar = useNavigate();
  const aviso = avisoDe(useLocation().state);
  const [mensaje, setMensaje] = useState<string | null>(null);
  const [saliendo, setSaliendo] = useState(false);

  async function alSalir() {
    setMensaje(null);
    setSaliendo(true);
    const resultado = await salir();
    setSaliendo(false);
    if (resultado.tipo === 'ok') {
      navegar('/ingreso', { replace: true });
    } else if (resultado.tipo !== 'sinSesion') {
      // 'campo', 'conflicto' y 'bloqueado' no los produce salir; se muestran igual que un error general.
      setMensaje(resultado.mensaje);
    }
  }

  return (
    <section className="pantalla">
      <h1>Sesión iniciada</h1>
      {aviso !== null && (
        <p className="mensaje-exito" role="status">
          {aviso}
        </p>
      )}
      {mensaje !== null && (
        <p className="mensaje-general" role="alert">
          {mensaje}
        </p>
      )}
      <p>
        <Link to="/cambiar-contrasena">Cambiar la contraseña</Link>
      </p>
      <p>
        <Link to="/configuracion">Configuración</Link>
      </p>
      <button type="button" onClick={alSalir} disabled={saliendo}>
        Salir
      </button>
    </section>
  );
}
