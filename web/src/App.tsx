import { useEffect } from 'react';
import { Navigate, Route, Routes, useNavigate } from 'react-router';
import { alPerderLaSesion } from './api/cliente';
import { Encabezado } from './componentes/Encabezado';
import { MarcaProvider } from './marca/MarcaProvider';
import { CambiarContrasena } from './paginas/CambiarContrasena';
import { Configuracion } from './paginas/Configuracion';
import { DefinirContrasena } from './paginas/DefinirContrasena';
import { Ingreso } from './paginas/Ingreso';
import { NuevoArticulo } from './paginas/NuevoArticulo';
import { Arranque } from './rutas/Arranque';

/**
 * Rutas de la aplicación. Ante un 401 sin sesión en cualquier pedido, navega a `/ingreso` reemplazando la entrada del
 * historial: la pantalla protegida se desmonta y su estado se descarta (FR-04). Cualquier otra ruta vuelve al
 * arranque, que decide la pantalla.
 */
export function App() {
  const navegar = useNavigate();

  useEffect(() => {
    alPerderLaSesion(() => navegar('/ingreso', { replace: true }));
    return () => alPerderLaSesion(null);
  }, [navegar]);

  return (
    <MarcaProvider>
      <div className="marco">
        <Encabezado />
        <main>
          <Routes>
            <Route path="/" element={<Arranque />} />
            <Route path="/definir-contrasena" element={<DefinirContrasena />} />
            <Route path="/ingreso" element={<Ingreso />} />
            <Route path="/cambiar-contrasena" element={<CambiarContrasena />} />
            <Route path="/configuracion" element={<Configuracion />} />
            <Route path="/articulos/nuevo" element={<NuevoArticulo />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </main>
      </div>
    </MarcaProvider>
  );
}
