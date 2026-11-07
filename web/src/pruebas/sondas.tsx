import { useEffect } from 'react';
import { useLocation, useNavigate, type NavigateFunction } from 'react-router';

/** Muestra la ruta actual del router para que las pruebas la comprueben. */
export function Ubicacion() {
  const ubicacion = useLocation();
  // El estado de la navegación va en un atributo para que las pruebas comprueben que no lleva contraseñas.
  return (
    <output data-testid="ubicacion" data-estado={JSON.stringify(ubicacion.state ?? null)}>
      {ubicacion.pathname + ubicacion.search + ubicacion.hash}
    </output>
  );
}

/**
 * Entrega la navegación del router montado a `registrar` y la quita (`registrar(null)`) al desmontarse (`cleanup`
 * entre pruebas).
 */
export function Navegador({ registrar }: { registrar: (navegar: NavigateFunction | null) => void }) {
  const navegar = useNavigate();
  useEffect(() => {
    registrar(navegar);
    return () => {
      registrar(null);
    };
  }, [navegar, registrar]);
  return null;
}
