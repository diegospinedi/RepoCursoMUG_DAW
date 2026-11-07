import { createContext, useContext } from 'react';
import type { Marca } from '../api/cliente';

/** La marca que reparte `MarcaProvider`. */
export const ContextoMarca = createContext<Marca | null>(null);

/** La marca recibida, o `null` mientras se carga o si no se pudo obtener. */
export function useMarca(): Marca | null {
  return useContext(ContextoMarca);
}
