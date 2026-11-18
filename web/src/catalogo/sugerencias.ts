import type { ArticuloListado } from '../api/cliente';
import { MINIMO_CARACTERES } from '../componentes/useBusquedaEnVivo';
import { recortar, validarBusqueda } from './reglas';

/**
 * Lo que se ve de un artículo sugerido (FR-02 de FEAT-013): código, código en el proveedor y descripción. Es texto: la
 * lista lo dibuja con JSX, y un HTML en la descripción se ve como texto (R-04).
 */
export const mostrarSugerencia = (articulo: ArticuloListado) =>
  `${articulo.codigo} · ${articulo.codigoProveedor} · ${articulo.descripcion}`;

/**
 * Si un texto del buscador de artículos se busca mientras se escribe (AC-02 y D-07 de FEAT-013): con
 * {@link MINIMO_CARACTERES} o más después del recorte, y solo si la Api lo aceptaría (`validarBusqueda`). Lo demás no
 * se pide en vivo; un texto inválido muestra su error recién con "Buscar".
 */
export function esBuscableEnVivo(texto: string): boolean {
  return recortar(texto).length >= MINIMO_CARACTERES && !('error' in validarBusqueda(texto));
}
