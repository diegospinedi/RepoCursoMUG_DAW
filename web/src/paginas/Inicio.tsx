import { Fragment, useEffect, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router';
import { consultarEstadoBackup, listarEmisionesPendientes, salir, type EstadoBackup } from '../api/cliente';
import { mostrarFecha } from '../presupuestos/reglas';
import { MenuInicio } from './MenuInicio';

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

/**
 * El texto del aviso de backup (FEAT-010, FR-09, D-26), o `null` si no falta el de ayer: la fecha del último backup en
 * dd/mm/aaaa o que no hay ninguno, y después si no hay una ubicación elegida o si la elegida no está accesible. Sin
 * enlaces ni la palabra "configuración" (D-13).
 */
function textoAvisoBackup(estado: EstadoBackup): string | null {
  if (!estado.faltaAyer) {
    return null;
  }
  const frases = [
    'Falta el backup de ayer.',
    estado.ultimo === null
      ? 'No hay ningún backup disponible.'
      : `El último backup es del ${mostrarFecha(estado.ultimo)}.`,
  ];
  if (estado.ubicacion === 'sin_elegir') {
    frases.push('No hay una ubicación de backup elegida.');
  } else if (!estado.accesible) {
    frases.push('La ubicación del backup no está accesible.');
  }
  return frases.join(' ');
}

/** El aviso que otra pantalla deja en el estado de la navegación (por ejemplo, después de cambiar la contraseña). */
function avisoDe(estado: unknown): string | null {
  if (typeof estado !== 'object' || estado === null || !('mensaje' in estado)) {
    return null;
  }
  return typeof estado.mensaje === 'string' ? estado.mensaje : null;
}

/**
 * Pantalla con la sesión iniciada: los avisos, el menú de tarjetas a las demás pantallas (FEAT-012) y la barra final
 * con "Salir" y su error, en ese orden (D-08). Al cargar pide una vez la lista de emisiones pendientes (FEAT-008, FR-03)
 * y, si hay, las avisa con un enlace a cada presupuesto; sin pendientes, o con cualquier falla al pedirla, no muestra
 * nada (AC-04). También pide una vez el estado del backup (FEAT-010, FR-09) y, si falta el de ayer, lo avisa; con
 * cualquier falla al pedirlo no muestra nada. No consulta en forma periódica: cualquier pedido con cookie renovaría la
 * sesión.
 */
export function Inicio() {
  const navegar = useNavigate();
  const aviso = avisoDe(useLocation().state);
  const [mensaje, setMensaje] = useState<string | null>(null);
  const [saliendo, setSaliendo] = useState(false);
  const [pendientes, setPendientes] = useState<readonly number[]>([]);
  const [avisoBackup, setAvisoBackup] = useState<string | null>(null);

  useEffect(() => {
    let vigente = true;
    async function listar() {
      const resultado = await listarEmisionesPendientes();
      // Una falla no se muestra: el aviso es una ayuda, y la pantalla sigue sirviendo sin él.
      if (vigente && resultado.tipo === 'ok') {
        setPendientes(resultado.datos);
      }
    }
    async function consultarBackup() {
      const resultado = await consultarEstadoBackup();
      // Como el de las emisiones: una falla no se muestra.
      if (vigente && resultado.tipo === 'ok') {
        setAvisoBackup(textoAvisoBackup(resultado.datos));
      }
    }
    void listar();
    void consultarBackup();
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
    <section className="pantalla pantalla--menu">
      <h1>Sesión iniciada</h1>
      {aviso !== null && (
        <p className="mensaje-exito" role="status">
          {aviso}
        </p>
      )}
      {avisoBackup !== null && (
        <p className="aviso" role="status">
          {avisoBackup}
        </p>
      )}
      {pendientes.length > 0 && <AvisoEmisionesPendientes presupuestos={pendientes} />}
      <MenuInicio />
      <div className="inicio__acciones">
        {mensaje !== null && (
          <p className="mensaje-general" role="alert">
            {mensaje}
          </p>
        )}
        <button type="button" className="boton-secundario" onClick={alSalir} disabled={saliendo}>
          Salir
        </button>
      </div>
    </section>
  );
}
