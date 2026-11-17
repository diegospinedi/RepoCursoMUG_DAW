import type { ReactNode } from 'react';

/** Los íconos que puede llevar una opción del menú de Inicio (FEAT-012, D-03): uno por opción. */
export type NombreIcono =
  | 'presupuesto-nuevo'
  | 'buscar'
  | 'factura'
  | 'articulos'
  | 'articulo-nuevo'
  | 'configuracion'
  | 'llave';

/** El trazo de cada ícono, dibujado en una grilla de 24×24. */
function trazo(nombre: NombreIcono): ReactNode {
  switch (nombre) {
    case 'presupuesto-nuevo':
      // Una hoja con la esquina doblada y un signo más.
      return (
        <>
          <path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z" />
          <path d="M14 3v5h5" />
          <path d="M12 11v6M9 14h6" />
        </>
      );
    case 'buscar':
      // Una lupa.
      return (
        <>
          <circle cx="11" cy="11" r="7" />
          <path d="m20 20-4-4" />
        </>
      );
    case 'factura':
      // Un recibo con el borde inferior dentado y renglones.
      return (
        <>
          <path d="M6 3h12v18l-3-2-3 2-3-2-3 2z" />
          <path d="M9 8h6M9 12h6M9 16h3" />
        </>
      );
    case 'articulos':
      // Una caja.
      return (
        <>
          <path d="M21 8 12 3 3 8v8l9 5 9-5z" />
          <path d="m3 8 9 5 9-5M12 13v8" />
        </>
      );
    case 'articulo-nuevo':
      // Una etiqueta de precio con un signo más.
      return (
        <>
          <path d="M3 12V4a1 1 0 0 1 1-1h8l9 9-9 9z" />
          <circle cx="7.5" cy="7.5" r="1.5" />
          <path d="M14 12v6M11 15h6" />
        </>
      );
    case 'configuracion':
      // Tres controles deslizantes.
      return (
        <>
          <path d="M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12" />
          <circle cx="16" cy="6" r="2" />
          <circle cx="10" cy="12" r="2" />
          <circle cx="18" cy="18" r="2" />
        </>
      );
    case 'llave':
      // Una llave.
      return (
        <>
          <circle cx="8" cy="15" r="4" />
          <path d="m10.8 12.2 9.2-9.2M16 7l3 3M14 9l2 2" />
        </>
      );
  }
}

/**
 * El ícono de una opción del menú de Inicio (FEAT-012, D-03): un SVG en línea de 24×24 que se pinta con `currentColor`
 * (el primario, por la hoja de estilos). Es decorativo: `aria-hidden`, sin `<title>`, sin `<style>` ni atributo `style`.
 */
export function IconoMenu({ nombre }: { nombre: NombreIcono }) {
  return (
    <svg
      className="tarjeta-menu__icono"
      width="24"
      height="24"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {trazo(nombre)}
    </svg>
  );
}
