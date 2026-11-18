import { ListaSugerencias, type PropsListaSugerencias } from './ListaSugerencias';

/**
 * Un buscador con sugerencias que llegan de la Api (D-12 de FEAT-013): el campo de texto de {@link ListaSugerencias},
 * dentro de `Campo`, con los atributos de su render-prop. Las sugerencias, la búsqueda y lo que pasa al elegir son de
 * la pantalla (con `useBusquedaEnVivo`); este campo solo las muestra y deja elegirlas.
 */
export function CampoBusqueda<T>(props: PropsListaSugerencias<T>) {
  return <ListaSugerencias {...props} />;
}
