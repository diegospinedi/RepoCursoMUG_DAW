import { useId, type ReactNode } from 'react';

interface PropsCampo {
  etiqueta: string;
  error: string | undefined;
  /** El control, con el id y los atributos de accesibilidad que recibe. */
  children: (atributos: { id: string; 'aria-invalid': boolean; 'aria-describedby': string | undefined }) => ReactNode;
}

/** Un campo con su etiqueta y su mensaje de error al lado, vinculado por `aria-describedby`. */
export function Campo({ etiqueta, error, children }: PropsCampo) {
  const id = useId();
  const idError = `${id}-error`;
  return (
    <div className="campo">
      <label htmlFor={id}>{etiqueta}</label>
      {children({ id, 'aria-invalid': error !== undefined, 'aria-describedby': error !== undefined ? idError : undefined })}
      {error !== undefined && (
        <p id={idError} className="campo__error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
