import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Campo } from './Campo';
import { CampoConOpciones, type Opcion } from './CampoConOpciones';

afterEach(cleanup);

/** Las alícuotas de ARCA, con la etiqueta que se ve (coma decimal) y el código que se graba. */
const ALICUOTAS: Opcion[] = [
  { valor: '0', etiqueta: '0' },
  { valor: '2.5', etiqueta: '2,5' },
  { valor: '5', etiqueta: '5' },
  { valor: '10.5', etiqueta: '10,5' },
  { valor: '21', etiqueta: '21' },
  { valor: '27', etiqueta: '27' },
];
const CONDICIONES: Opcion[] = [
  { valor: 'responsable_inscripto', etiqueta: 'Responsable Inscripto' },
  { valor: 'monotributista', etiqueta: 'Monotributista' },
];

function Prueba({ opciones, alElegir }: { opciones: readonly Opcion[]; alElegir: (opcion: Opcion) => void }) {
  const [texto, setTexto] = useState('');
  return (
    <Campo etiqueta="Alícuota de IVA" error={undefined}>
      {(atributos) => (
        <CampoConOpciones
          {...atributos}
          texto={texto}
          alCambiar={setTexto}
          opciones={opciones}
          alElegir={(opcion) => {
            setTexto(opcion.etiqueta);
            alElegir(opcion);
          }}
        />
      )}
    </Campo>
  );
}

const campo = () => screen.getByRole('combobox', { name: 'Alícuota de IVA' }) as HTMLInputElement;
const textos = () => screen.queryAllByRole('option').map((o) => o.textContent);
function escribir(texto: string) {
  campo().focus();
  fireEvent.change(campo(), { target: { value: texto } });
}

describe('CampoConOpciones (FEAT-013, D-13)', () => {
  it('CampoConOpciones_FiltraPlegadoYFlechaAbreLista', () => {
    const alElegir = vi.fn();
    const { rerender } = render(<Prueba opciones={ALICUOTAS} alElegir={alElegir} />);

    // Con el campo vacío y el foco, flecha abajo abre la lista completa, sin mínimo de caracteres.
    campo().focus();
    expect(screen.queryByRole('listbox')).toBeNull();
    fireEvent.keyDown(campo(), { key: 'ArrowDown' });
    expect(textos()).toEqual(['0', '2,5', '5', '10,5', '21', '27']);

    // AC-11: "10" deja solo 10,5. Un carácter ya filtra.
    escribir('10');
    expect(textos()).toEqual(['10,5']);
    escribir('2');
    expect(textos()).toEqual(['2,5', '21', '27']);

    // Elegir con el teclado entrega la opción (la pantalla graba su código).
    escribir('10');
    fireEvent.keyDown(campo(), { key: 'ArrowDown' });
    fireEvent.keyDown(campo(), { key: 'Enter' });
    expect(alElegir).toHaveBeenCalledExactlyOnceWith(ALICUOTAS[3]);
    expect(campo().value).toBe('10,5');

    // Se compara plegado: sin mayúsculas ni acentos.
    rerender(<Prueba opciones={CONDICIONES} alElegir={alElegir} />);
    escribir('  RÉSPONSABLE ');
    expect(textos()).toEqual(['Responsable Inscripto']);
    escribir('monotribut');
    expect(textos()).toEqual(['Monotributista']);

    // Un texto que no coincide con ninguna deja la lista cerrada.
    escribir('exento');
    expect(textos()).toEqual([]);
    expect(campo().getAttribute('aria-expanded')).toBe('false');
  });
});
