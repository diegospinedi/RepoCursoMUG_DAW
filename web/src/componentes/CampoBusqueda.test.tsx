import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Campo } from './Campo';
import { CampoBusqueda } from './CampoBusqueda';

afterEach(cleanup);

interface Articulo {
  codigo: number;
  descripcion: string;
}
const SUGERENCIAS: Articulo[] = [
  { codigo: 7, descripcion: 'Armazón metal' },
  { codigo: 8, descripcion: 'Armazón acetato' },
];

function Prueba({ error, alElegir }: { error: string | undefined; alElegir: (articulo: Articulo) => void }) {
  const [texto, setTexto] = useState('');
  return (
    <form role="search">
      <Campo etiqueta="Texto a buscar" error={error}>
        {(atributos) => (
          <CampoBusqueda
            {...atributos}
            valor={texto}
            alCambiar={setTexto}
            sugerencias={SUGERENCIAS}
            clave={(a) => a.codigo}
            mostrar={(a) => (
              <>
                <span>{a.codigo}</span> <span>{a.descripcion}</span>
              </>
            )}
            alElegir={alElegir}
            maxLength={200}
            name="texto"
          />
        )}
      </Campo>
    </form>
  );
}

describe('CampoBusqueda (FEAT-013, D-12)', () => {
  it('CampoBusqueda_DentroDeCampo_SumaSusAtributosYMuestraSugerencias', () => {
    const alElegir = vi.fn();
    render(<Prueba error="Usá hasta 200 caracteres." alElegir={alElegir} />);
    const campo = screen.getByRole('combobox', { name: 'Texto a buscar' }) as HTMLInputElement;

    // Los atributos del render-prop de Campo: id de la etiqueta, aria-invalid y aria-describedby con el error.
    expect(campo.getAttribute('aria-invalid')).toBe('true');
    expect(document.getElementById(campo.getAttribute('aria-describedby') ?? '')?.textContent).toBe(
      'Usá hasta 200 caracteres.',
    );
    expect(campo.type).toBe('text');
    expect(campo.maxLength).toBe(200);
    expect(campo.name).toBe('texto');
    expect(campo.closest('.campo-busqueda')).not.toBeNull();

    // Las sugerencias que llegan de afuera se muestran tal cual y se eligen.
    campo.focus();
    fireEvent.change(campo, { target: { value: 'arm' } });
    expect(campo.value).toBe('arm');
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['7 Armazón metal', '8 Armazón acetato']);
    fireEvent.click(screen.getAllByRole('option')[1]!);
    expect(alElegir).toHaveBeenCalledExactlyOnceWith(SUGERENCIAS[1]);
  });
});
