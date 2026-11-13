import { useEffect } from 'react';
import { Navigate, Route, Routes, useNavigate } from 'react-router';
import { alPerderLaSesion } from './api/cliente';
import { Encabezado } from './componentes/Encabezado';
import { MarcaProvider } from './marca/MarcaProvider';
import { Articulos } from './paginas/Articulos';
import { BuscarPresupuestos } from './paginas/BuscarPresupuestos';
import { CambiarContrasena } from './paginas/CambiarContrasena';
import { Configuracion } from './paginas/Configuracion';
import { DefinirContrasena } from './paginas/DefinirContrasena';
import { EditarArticulo } from './paginas/EditarArticulo';
import { EditarPresupuesto } from './paginas/EditarPresupuesto';
import { Ingreso } from './paginas/Ingreso';
import { NuevoArticulo } from './paginas/NuevoArticulo';
import { NuevoPresupuesto } from './paginas/NuevoPresupuesto';
import { Presupuesto } from './paginas/Presupuesto';
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
            <Route path="/articulos" element={<Articulos />} />
            {/* El alta antes que la edición: "nuevo" no es un código (react-router ya prefiere el segmento fijo). */}
            <Route path="/articulos/nuevo" element={<NuevoArticulo />} />
            <Route path="/articulos/:codigo" element={<EditarArticulo />} />
            {/* La búsqueda (FEAT-006): los filtros viven en la pantalla, nunca en la URL (R-02). */}
            <Route path="/presupuestos" element={<BuscarPresupuestos />} />
            {/* Como en artículos: "nuevo" es el alta, no el presupuesto con ese número. */}
            <Route path="/presupuestos/nuevo" element={<NuevoPresupuesto />} />
            <Route path="/presupuestos/:numero" element={<Presupuesto />} />
            <Route path="/presupuestos/:numero/editar" element={<EditarPresupuesto />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </main>
      </div>
    </MarcaProvider>
  );
}
