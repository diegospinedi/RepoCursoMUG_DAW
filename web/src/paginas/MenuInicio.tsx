import { useId } from 'react';
import { Link } from 'react-router';
import { IconoMenu } from '../componentes/IconoMenu';
import { SECCIONES_MENU, type OpcionMenu, type SeccionMenu } from './seccionesMenu';

/**
 * Una tarjeta del menú (FEAT-012, D-06): el ícono decorativo, el enlace con solo el nombre y la descripción fuera del
 * enlace, asociada con `aria-describedby`. El `::after` del enlace cubre la tarjeta, así que toda ella es clicable.
 */
function TarjetaMenu({ opcion }: { opcion: OpcionMenu }) {
  const idDescripcion = useId();
  return (
    <li className="tarjeta-menu">
      <IconoMenu nombre={opcion.icono} />
      <Link className="tarjeta-menu__enlace" to={opcion.destino} aria-describedby={idDescripcion}>
        {opcion.nombre}
      </Link>
      <p id={idDescripcion} className="tarjeta-menu__descripcion">
        {opcion.descripcion}
      </p>
    </li>
  );
}

/** Una sección del menú: una región con su título y la lista de tarjetas. */
function SeccionDelMenu({ seccion }: { seccion: SeccionMenu }) {
  const idTitulo = useId();
  return (
    <section className="menu__seccion" aria-labelledby={idTitulo}>
      <h2 id={idTitulo}>{seccion.titulo}</h2>
      <ul className="menu__lista">
        {seccion.opciones.map((opcion) => (
          <TarjetaMenu key={opcion.destino} opcion={opcion} />
        ))}
      </ul>
    </section>
  );
}

/** El menú de Inicio (FEAT-012, FR-01): las secciones Ventas, Catálogo y Sistema, armadas desde `SECCIONES_MENU`. */
export function MenuInicio() {
  return (
    <nav className="menu" aria-label="Menú de Inicio">
      {SECCIONES_MENU.map((seccion) => (
        <SeccionDelMenu key={seccion.titulo} seccion={seccion} />
      ))}
    </nav>
  );
}
