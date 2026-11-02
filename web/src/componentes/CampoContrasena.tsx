import { useId } from 'react';

interface Props {
  valor: string;
  alCambiar: (valor: string) => void;
  /** Mensaje de error a mostrar junto al campo, o `null`. */
  error: string | null;
  /** `new-password` al definirla, `current-password` al ingresar. */
  autocompletar: 'new-password' | 'current-password';
}

/** Campo de contraseña con su mensaje de error al lado, vinculado por `aria-describedby`. */
export function CampoContrasena({ valor, alCambiar, error, autocompletar }: Props) {
  const id = useId();
  const idError = `${id}-error`;
  return (
    <div className="campo">
      <label htmlFor={id}>Contraseña</label>
      <input
        id={id}
        name="contrasena"
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
