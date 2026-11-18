import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Campo } from './Campo';
import { ListaSugerencias, MAXIMO_SUGERENCIAS } from './ListaSugerencias';

afterEach(cleanup);

/** Datos ficticios (Ley 25.326): ningún cliente real. */
interface Cliente {
  numero: number;
  nombre: string;
}
const CLIENTES: Cliente[] = [
  { numero: 155, nombre: 'Pérez, Ana' },
  { numero: 156, nombre: 'Pérez, Juan' },
  { numero: 157, nombre: 'Peralta, Luis' },
];

interface PropsPrueba {
  sugerencias: readonly Cliente[];
  alElegir?: (cliente: Cliente) => void;
  alEnviar?: () => void;
}

/** La lista dentro de `Campo`, en un formulario de búsqueda, con el texto en el estado. */
function Prueba({ sugerencias, alElegir = () => {}, alEnviar = () => {} }: PropsPrueba) {
  const [texto, setTexto] = useState('');
  return (
    <form
      role="search"
      onSubmit={(evento) => {
        evento.preventDefault();
        alEnviar();
      }}
    >
      <Campo etiqueta="Apellido" error={undefined}>
        {(atributos) => (
          <ListaSugerencias
            {...atributos}
            valor={texto}
            alCambiar={setTexto}
            sugerencias={sugerencias}
            clave={(c) => c.numero}
            mostrar={(c) => c.nombre}
            alElegir={alElegir}
          />
        )}
      </Campo>
      <button type="submit">Buscar</button>
    </form>
  );
}

const campo = () => screen.getByRole('combobox', { name: 'Apellido' });
const opciones = () => screen.queryAllByRole('option');
const marcada = () => opciones().filter((o) => o.getAttribute('aria-selected') === 'true');
/** Teclea en el campo con el foco: abre la lista con las sugerencias que tenga. */
function escribir(texto: string) {
  campo().focus();
  fireEvent.change(campo(), { target: { value: texto } });
}
/** Aprieta una tecla en el campo; devuelve falso si el componente llamó a `preventDefault`. */
const tecla = (key: string) => fireEvent.keyDown(campo(), { key });

describe('ListaSugerencias (FEAT-013, D-10 a D-12)', () => {
  it('ListaSugerencias_TecladoFlechasEnterEscapeTab', () => {
    const alElegir = vi.fn();
    const alEnviar = vi.fn();
    render(<Prueba sugerencias={CLIENTES} alElegir={alElegir} alEnviar={alEnviar} />);
    expect(campo().getAttribute('type')).toBe('text');
    expect(campo().getAttribute('autocomplete')).toBe('off');

    // Escribir abre la lista, sin ninguna marcada.
    escribir('pe');
    expect(opciones().map((o) => o.textContent)).toEqual(['Pérez, Ana', 'Pérez, Juan', 'Peralta, Luis']);
    expect(marcada()).toEqual([]);

    // Las flechas recorren la lista, y dan la vuelta en los extremos.
    expect(tecla('ArrowDown')).toBe(false);
    expect(marcada().map((o) => o.textContent)).toEqual(['Pérez, Ana']);
    tecla('ArrowDown');
    expect(marcada().map((o) => o.textContent)).toEqual(['Pérez, Juan']);
    tecla('ArrowUp');
    expect(marcada().map((o) => o.textContent)).toEqual(['Pérez, Ana']);
    tecla('ArrowUp');
    expect(marcada().map((o) => o.textContent)).toEqual(['Peralta, Luis']);
    tecla('ArrowDown');
    expect(marcada().map((o) => o.textContent)).toEqual(['Pérez, Ana']);

    // Enter con una marcada la elige, sin enviar el formulario, y cierra la lista.
    expect(tecla('Enter')).toBe(false);
    expect(alElegir).toHaveBeenCalledExactlyOnceWith(CLIENTES[0]);
    expect(alEnviar).not.toHaveBeenCalled();
    expect(screen.queryByRole('listbox')).toBeNull();

    // Escape con la lista abierta la cierra (con preventDefault, así no vacía nada); con la lista cerrada no hace nada.
    escribir('per');
    expect(opciones()).toHaveLength(3);
    expect(tecla('Escape')).toBe(false);
    expect(screen.queryByRole('listbox')).toBeNull();
    expect((campo() as HTMLInputElement).value).toBe('per');
    expect(tecla('Escape')).toBe(true);

    // Con la lista cerrada, la flecha abajo la vuelve a abrir con la primera marcada; la flecha arriba, con la última.
    tecla('ArrowDown');
    expect(marcada().map((o) => o.textContent)).toEqual(['Pérez, Ana']);
    tecla('Escape');
    tecla('ArrowUp');
    expect(marcada().map((o) => o.textContent)).toEqual(['Peralta, Luis']);

    // Enter sin una marcada busca como hoy (envía el formulario) y cierra la lista.
    escribir('pere');
    expect(tecla('Enter')).toBe(true);
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(alElegir).toHaveBeenCalledTimes(1);

    // Tab cierra sin elegir; perder el foco también.
    escribir('perez');
    tecla('ArrowDown');
    expect(tecla('Tab')).toBe(true);
    expect(screen.queryByRole('listbox')).toBeNull();
    escribir('pérez');
    expect(opciones()).toHaveLength(3);
    act(() => campo().blur());
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(alElegir).toHaveBeenCalledTimes(1);

    // Otra tecla no hace nada especial.
    escribir('pérez,');
    expect(tecla('a')).toBe(true);
    expect(opciones()).toHaveLength(3);

    // Con el mouse: mousedown no saca el foco del campo (preventDefault) y el clic elige.
    const segunda = opciones()[1]!;
    expect(fireEvent.mouseDown(segunda)).toBe(false);
    fireEvent.click(segunda);
    expect(alElegir).toHaveBeenLastCalledWith(CLIENTES[1]);
    expect(screen.queryByRole('listbox')).toBeNull();
  });

  it('ListaSugerencias_SinSugerencias_LasFlechasNoAbren', () => {
    render(<Prueba sugerencias={[]} />);
    escribir('zz');
    expect(tecla('ArrowDown')).toBe(true);
    expect(tecla('ArrowUp')).toBe(true);
    expect(tecla('Enter')).toBe(true);
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(campo().getAttribute('aria-expanded')).toBe('false');
  });

  it('ListaSugerencias_AriaActivedescendantYAnuncioSoloCantidad', () => {
    const { rerender } = render(<Prueba sugerencias={CLIENTES} />);
    // La región está montada desde el principio, vacía.
    expect(screen.queryByRole('status')).not.toBeNull();
    const anuncio = screen.getByRole('status');
    expect(anuncio.getAttribute('aria-live')).toBe('polite');
    expect(anuncio.textContent).toBe('');
    expect(campo().getAttribute('role')).toBe('combobox');
    expect(campo().getAttribute('aria-autocomplete')).toBe('list');
    expect(campo().getAttribute('aria-expanded')).toBe('false');
    expect(campo().hasAttribute('aria-activedescendant')).toBe(false);

    escribir('pe');
    const lista = screen.getByRole('listbox');
    expect(campo().getAttribute('aria-expanded')).toBe('true');
    expect(campo().getAttribute('aria-controls')).toBe(lista.id);
    // La lista se nombra con la etiqueta del campo.
    const etiqueta = document.getElementById(lista.getAttribute('aria-labelledby') ?? '');
    expect(etiqueta?.textContent).toBe('Apellido');
    expect(screen.getByRole('listbox', { name: 'Apellido' })).toBe(lista);
    // R-02: el anuncio dice solo la cantidad, nunca un nombre.
    expect(anuncio.textContent).toBe('3 sugerencias');
    // Sin una opción marcada no hay aria-activedescendant.
    expect(campo().hasAttribute('aria-activedescendant')).toBe(false);

    tecla('ArrowDown');
    const primera = opciones()[0]!;
    expect(campo().getAttribute('aria-activedescendant')).toBe(primera.id);
    expect(primera.id).toContain('155');
    const idPrimera = primera.id;

    // Los ids son estables: las mismas sugerencias en otro arreglo conservan id y marca.
    rerender(<Prueba sugerencias={[...CLIENTES]} />);
    expect(opciones()[0]!.id).toBe(idPrimera);
    expect(campo().getAttribute('aria-activedescendant')).toBe(idPrimera);

    // Sugerencias nuevas: la marca vuelve a ninguna.
    rerender(<Prueba sugerencias={[CLIENTES[2]!, CLIENTES[0]!]} />);
    expect(marcada()).toEqual([]);
    expect(campo().hasAttribute('aria-activedescendant')).toBe(false);
    expect(anuncio.textContent).toBe('2 sugerencias');
    rerender(<Prueba sugerencias={[CLIENTES[2]!]} />);
    expect(anuncio.textContent).toBe('1 sugerencia');

    // Como mucho 8, y el anuncio cuenta las que se ven.
    expect(MAXIMO_SUGERENCIAS).toBe(8);
    const muchos = Array.from({ length: 12 }, (_, i) => ({ numero: 200 + i, nombre: `Cliente ${i}` }));
    rerender(<Prueba sugerencias={muchos} />);
    expect(opciones()).toHaveLength(8);
    expect(anuncio.textContent).toBe('8 sugerencias');

    // Otras 8 (NFR-03): la cantidad es la misma, pero el lector de pantalla tiene que volver a anunciar. La región se
    // vacía y se vuelve a escribir; con las mismas 8, no se toca.
    const observador = new MutationObserver(() => {});
    observador.observe(anuncio, { childList: true, characterData: true, subtree: true });
    rerender(<Prueba sugerencias={[...muchos]} />);
    expect(observador.takeRecords()).toEqual([]);
    const otros = muchos.map((c) => ({ ...c, numero: c.numero + 100 }));
    rerender(<Prueba sugerencias={otros} />);
    const registros = observador.takeRecords();
    observador.disconnect();
    expect(registros.length).toBeGreaterThan(0);
    expect(anuncio.textContent).toBe('8 sugerencias');
    rerender(<Prueba sugerencias={muchos} />);

    // Cerrada, el anuncio queda vacío y aria-activedescendant desaparece aunque haya una marcada.
    tecla('ArrowDown');
    tecla('Escape');
    expect(anuncio.textContent).toBe('');
    expect(campo().hasAttribute('aria-activedescendant')).toBe(false);

    // Sin el foco, el campo no se expande ni anuncia, aunque lleguen sugerencias.
    act(() => campo().blur());
    rerender(<Prueba sugerencias={CLIENTES} />);
    expect(campo().getAttribute('aria-expanded')).toBe('false');
    expect(anuncio.textContent).toBe('');
  });

  it('ListaSugerencias_ClaveConEspacios_IdValido', () => {
    // Un id con espacios rompería aria-activedescendant (es una lista de ids separados por espacios).
    render(
      <Campo etiqueta="Código" error={undefined}>
        {(atributos) => (
          <ListaSugerencias
            {...atributos}
            valor=""
            alCambiar={() => {}}
            sugerencias={['ABC 1', 'ABC/2']}
            clave={(c) => c}
            mostrar={(c) => c}
            alElegir={() => {}}
          />
        )}
      </Campo>,
    );
    const combo = screen.getByRole('combobox', { name: 'Código' });
    combo.focus();
    fireEvent.keyDown(combo, { key: 'ArrowDown' });
    const [primera, segunda] = screen.getAllByRole('option');
    expect(primera?.id).toMatch(/ABC_1$/);
    expect(segunda?.id).toMatch(/ABC_2$/);
    expect(primera?.id).not.toMatch(/\s/);
    expect(combo.getAttribute('aria-activedescendant')).toBe(primera?.id);
    expect(document.getElementById(combo.getAttribute('aria-activedescendant') ?? '')).toBe(primera);
  });

  it('ListaSugerencias_HtmlEnSugerenciaSeVeComoTexto', () => {
    const peligroso = '<img src=x onerror="alert(1)">Pérez';
    render(<Prueba sugerencias={[{ numero: 1, nombre: peligroso }]} />);
    escribir('pe');

    expect(opciones().map((o) => o.textContent)).toEqual([peligroso]);
    expect(document.querySelector('img')).toBeNull();
    expect(screen.getByRole('listbox').innerHTML).not.toContain('<img');
  });
});
