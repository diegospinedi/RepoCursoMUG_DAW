import { useState } from 'react';
import { useNavigate } from 'react-router';
import { salir } from '../api/cliente';

/**
 * Pantalla mínima con la sesión iniciada; las pantallas con datos llegan en los PRDs siguientes. No consulta en
 * forma periódica: cualquier pedido con cookie renovaría la sesión.
 */
export function Inicio() {
  const navegar = useNavigate();
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
      // 'campo' y 'conflicto' no los produce salir; se muestran igual que un error general.
      setMensaje(resultado.mensaje);
    }
  }

  return (
    <section className="pantalla">
      <h1>Sesión iniciada</h1>
      {mensaje !== null && (
        <p className="mensaje-general" role="alert">
          {mensaje}
        </p>
      )}
      <button type="button" onClick={alSalir} disabled={saliendo}>
        Salir
      </button>
    </section>
  );
}
