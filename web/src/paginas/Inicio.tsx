import { Fragment, useEffect, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router';
import { listarEmisionesPendientes, salir } from '../api/cliente';

/**
 * El separador antes del elemento `indice` de una lista en castellano de `total` elementos: nada antes del primero,
 * " y " antes del último y ", " antes de los demás ("155", "155 y 160", "155, 160 y 170").
 */
function separadorAntesDe(indice: number, total: number): string {
  if (indice === 0) {
    return '';
  }
  return indice === total - 1 ? ' y ' : ', ';
}

/**
 * El aviso de emisiones pendientes (FEAT-008, FR-03, AC-03): "Hay emisiones pendientes de verificación en los
 * presupuestos 155 y 160.", con un enlace a cada presupuesto. Con uno solo, "en el presupuesto 155.".
 */
function AvisoEmisionesPendientes({ presupuestos }: { presupuestos: readonly number[] }) {
  return (
    <p className="aviso" role="status">
      Hay emisiones pendientes de verificación en {presupuestos.length === 1 ? 'el presupuesto' : 'los presupuestos'}{' '}
      {presupuestos.map((numero, indice) => (
        <Fragment key={numero}>
          {separadorAntesDe(indice, presupuestos.length)}
          <Link to={`/presupuestos/${numero}`}>{numero}</Link>
        </Fragment>
      ))}
      .
    </p>
  );
}

/** El aviso que otra pantalla deja en el estado de la navegación (por ejemplo, después de cambiar la contraseña). */
function avisoDe(estado: unknown): string | null {
  if (typeof estado !== 'object' || estado === null || !('mensaje' in estado)) {
    return null;
  }
  return typeof estado.mensaje === 'string' ? estado.mensaje : null;
}

/**
 * Pantalla con la sesión iniciada y los enlaces a las demás. Al cargar pide una vez la lista de emisiones pendientes
 * (FEAT-008, FR-03) y, si hay, las avisa con un enlace a cada presupuesto; sin pendientes, o con cualquier falla al
 * pedirla, no muestra nada (AC-04). No consulta en forma periódica: cualquier pedido con cookie renovaría la sesión.
 */
export function Inicio() {
  const navegar = useNavigate();
  const aviso = avisoDe(useLocation().state);
  const [mensaje, setMensaje] = useState<string | null>(null);
  const [saliendo, setSaliendo] = useState(false);
  const [pendientes, setPendientes] = useState<readonly number[]>([]);

  useEffect(() => {
    let vigente = true;
    async function listar() {
      const resultado = await listarEmisionesPendientes();
      // Una falla no se muestra: el aviso es una ayuda, y la pantalla sigue sirviendo sin él.
      if (vigente && resultado.tipo === 'ok') {
        setPendientes(resultado.datos);
      }
    }
    void listar();
    return () => {
      vigente = false;
    };
  }, []);

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
      {pendientes.length > 0 && <AvisoEmisionesPendientes presupuestos={pendientes} />}
      <p>
        <Link to="/cambiar-contrasena">Cambiar la contraseña</Link>
      </p>
      <p>
        <Link to="/configuracion">Configuración</Link>
      </p>
      <p>
        <Link to="/articulos">Artículos</Link>
      </p>
      <p>
        <Link to="/articulos/nuevo">Nuevo artículo</Link>
      </p>
      <p>
        <Link to="/presupuestos/nuevo">Nuevo presupuesto</Link>
      </p>
      <p>
        <Link to="/presupuestos">Buscar presupuestos</Link>
      </p>
      <button type="button" onClick={alSalir} disabled={saliendo}>
        Salir
      </button>
    </section>
  );
}
