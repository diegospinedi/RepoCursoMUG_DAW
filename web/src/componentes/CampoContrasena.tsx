import { useId } from 'react';
import { CAMPO_CONTRASENA } from '../api/cliente';

interface Props {
  valor: string;
  alCambiar: (valor: string) => void;
  /** Mensaje de error a mostrar junto al campo, o `null`. */
  error: string | null;
  /** `new-password` al definirla o elegir la nueva, `current-password` al ingresar o confirmar la actual. */
  autocompletar: 'new-password' | 'current-password';
  /** Texto de la etiqueta; por defecto, "Contraseña". */
  etiqueta?: string;
  /** Atributo `name` del campo; por defecto, {@link CAMPO_CONTRASENA}. */
  nombre?: string;
}

/** Campo de contraseña con su mensaje de error al lado, vinculado por `aria-describedby`. */
export function CampoContrasena({
  valor,
  alCambiar,
  error,
  autocompletar,
  etiqueta = 'Contraseña',
  nombre = CAMPO_CONTRASENA,
}: Props) {
  const id = useId();
  const idError = `${id}-error`;
  return (
    <div className="campo">
      <label htmlFor={id}>{etiqueta}</label>
      <input
        id={id}
        name={nombre}
        type="password"
        autoComplete={autocompletar}
        spellCheck={false}
        autoCapitalize="off"
        value={valor}
        onChange={(evento) => alCambiar(evento.target.value)}
        aria-invalid={error !== null}
        aria-describedby={error !== null ? idError : undefined}
      />
      {error !== null && (
        <p id={idError} className="campo__error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
