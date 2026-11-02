import { ingresar } from '../api/cliente';
import { FormularioContrasena } from '../componentes/FormularioContrasena';

/** Ingreso con la contraseña de acceso (FR-01). */
export function Ingreso() {
  return (
    <FormularioContrasena
      titulo="Ingresá al sistema"
      boton="Ingresar"
      autocompletar="current-password"
      enviar={ingresar}
      enlaceConflicto={{ a: '/definir-contrasena', texto: 'Definir la contraseña' }}
    />
  );
}
