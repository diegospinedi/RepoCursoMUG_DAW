import { useEffect, useState, type ReactNode } from 'react';
import { consultarMarca, type Marca } from '../api/cliente';
import { ContextoMarca } from './contextoMarca';

const COLOR = /^#[0-9A-Fa-f]{6}$/;

/**
 * Pide la marca una sola vez y fija `--color-primario` y `--color-fondo` en la raíz del documento (por CSSOM, que la
 * política `default-src 'self'` permite). Un color con otro formato no se aplica y queda el de `estilos.css`; si la
 * marca no se puede obtener, quedan los colores por defecto.
 */
export function MarcaProvider({ children }: { children: ReactNode }) {
  const [marca, setMarca] = useState<Marca | null>(null);

  useEffect(() => {
    let vigente = true;
    void consultarMarca().then((resultado) => {
      if (!vigente || resultado.tipo !== 'ok') {
        return;
      }
      const raiz = document.documentElement.style;
      if (COLOR.test(resultado.datos.primario)) {
        raiz.setProperty('--color-primario', resultado.datos.primario);
      }
      if (COLOR.test(resultado.datos.fondo)) {
        raiz.setProperty('--color-fondo', resultado.datos.fondo);
      }
      setMarca(resultado.datos);
    });
    return () => {
      vigente = false;
    };
  }, []);

  return <ContextoMarca.Provider value={marca}>{children}</ContextoMarca.Provider>;
}
