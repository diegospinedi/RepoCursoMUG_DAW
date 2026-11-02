import { definirContrasena } from '../api/cliente';
import { FormularioContrasena } from '../componentes/FormularioContrasena';

/** Primera vez: define la contraseña de acceso (FR-02, FR-03). La Api inicia la sesión al definirla. */
export function DefinirContrasena() {
  return (
    <FormularioContrasena
      titulo="Definí la contraseña de acceso"
      boton="Definir contraseña"
      autocompletar="new-password"
      enviar={definirContrasena}
      enlaceConflicto={{ a: '/ingreso', texto: 'Ir al ingreso' }}
    />
  );
}
