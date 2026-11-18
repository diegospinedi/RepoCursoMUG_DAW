import { useId, type ReactNode } from 'react';

/**
 * El id de la etiqueta es el del control más este sufijo: la lista de sugerencias se nombra con la etiqueta
 * (`aria-labelledby`, D-11 de FEAT-013) sin que el render-prop sume atributos que irían a parar al control.
 */
export const SUFIJO_ETIQUETA = '-etiqueta';

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
      <label htmlFor={id} id={`${id}${SUFIJO_ETIQUETA}`}>
        {etiqueta}
      </label>
      {children({ id, 'aria-invalid': error !== undefined, 'aria-describedby': error !== undefined ? idError : undefined })}
      {error !== undefined && (
        <p id={idError} className="campo__error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
