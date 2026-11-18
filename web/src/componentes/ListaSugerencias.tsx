import { useId, useState, type KeyboardEvent, type ReactNode } from 'react';
import { SUFIJO_ETIQUETA } from './Campo';

/** Como mucho, las sugerencias que se muestran (FR-01). */
export const MAXIMO_SUGERENCIAS = 8;

/** Los atributos que da el render-prop de `Campo`. */
export interface AtributosCampo {
  id: string;
  'aria-invalid': boolean;
  'aria-describedby': string | undefined;
}

export interface PropsListaSugerencias<T> extends AtributosCampo {
  valor: string;
  alCambiar: (valor: string) => void;
  /** Las sugerencias, en orden; se muestran las primeras {@link MAXIMO_SUGERENCIAS}. */
  sugerencias: readonly T[];
  /**
   * Lo que identifica a una sugerencia (el código, el número): arma su id estable. Tiene que ser única entre las
   * sugerencias, también después de codificarla para el id (cada carácter fuera de letras, dígitos, `_`, `.` y `-` pasa
   * a `_`).
   */
  clave: (sugerencia: T) => string | number;
  /** Lo que se ve de una sugerencia. Se dibuja con JSX: un texto con HTML se ve como texto (R-04). */
  mostrar: (sugerencia: T) => ReactNode;
  alElegir: (sugerencia: T) => void;
  maxLength?: number;
  name?: string;
}

/** La opción marcada con el teclado, junto con las sugerencias que había al marcarla. */
interface Marca {
  indice: number;
  firma: string;
}

const SIN_MARCA: Marca = { indice: -1, firma: '' };

/** La clave apta para un id: sin espacios, que romperían `aria-activedescendant`. */
const codificar = (clave: string | number) => String(clave).replace(/[^\w.-]/g, '_');

const anunciar = (cantidad: number) => (cantidad === 1 ? '1 sugerencia' : `${cantidad} sugerencias`);

/**
 * La base de los campos con sugerencias (D-10 a D-12 de FEAT-013, ADR-032): un campo de texto con un combobox ARIA 1.2
 * y su lista navegable. Escribir abre la lista; las flechas la recorren (y la abren si está cerrada); Enter con una
 * opción marcada la elige, y sin ninguna deja que el formulario busque como hoy; Escape la cierra; Tab y la pérdida del
 * foco la cierran sin elegir; con el mouse se elige con un clic sin que el campo pierda el foco. Solo el campo que
 * tiene el foco se expande y anuncia. La región `aria-live` está siempre montada y dice solo la cantidad (R-02). Con
 * sugerencias nuevas, la marca vuelve a ninguna.
 */
export function ListaSugerencias<T>({
  id,
  'aria-invalid': invalido,
  'aria-describedby': descripcion,
  valor,
  alCambiar,
  sugerencias,
  clave,
  mostrar,
  alElegir,
  maxLength,
  name,
}: PropsListaSugerencias<T>) {
  const base = useId();
  const idLista = `${base}lista`;
  const [abierta, setAbierta] = useState(false);
  const [enfocado, setEnfocado] = useState(false);
  const [marca, setMarca] = useState<Marca>(SIN_MARCA);

  const visibles = sugerencias.slice(0, MAXIMO_SUGERENCIAS);
  const idDe = (sugerencia: T) => `${base}-${codificar(clave(sugerencia))}`;
  // Un id codificado no tiene espacios: el espacio separa sin ambigüedad.
  const firma = visibles.map(idDe).join(' ');
  const mostrada = abierta && enfocado && visibles.length > 0;

  const anuncio = mostrada ? anunciar(visibles.length) : '';
  // Una marca puesta sobre otras sugerencias ya no vale.
  const marcada = mostrada && marca.firma === firma ? marca.indice : -1;
  const opcionMarcada = visibles[marcada];

  function cerrar() {
    setAbierta(false);
    setMarca(SIN_MARCA);
  }

  function elegir(sugerencia: T) {
    cerrar();
    alElegir(sugerencia);
  }

  function marcar(indice: number) {
    setAbierta(true);
    setMarca({ indice, firma });
  }

  function alTeclear(evento: KeyboardEvent<HTMLInputElement>) {
    const cantidad = visibles.length;
    switch (evento.key) {
      case 'ArrowDown':
        if (cantidad > 0) {
          evento.preventDefault();
          marcar(marcada < 0 ? 0 : (marcada + 1) % cantidad);
        }
        break;
      case 'ArrowUp':
        if (cantidad > 0) {
          evento.preventDefault();
          marcar(marcada <= 0 ? cantidad - 1 : marcada - 1);
        }
        break;
      case 'Enter':
        if (opcionMarcada !== undefined) {
          evento.preventDefault();
          elegir(opcionMarcada);
        } else {
          cerrar();
        }
        break;
      case 'Escape':
        if (mostrada) {
          evento.preventDefault();
          cerrar();
        }
        break;
      case 'Tab':
        cerrar();
        break;
    }
  }

  return (
    <div className="campo-busqueda">
      <input
        id={id}
        type="text"
        role="combobox"
        autoComplete="off"
        aria-invalid={invalido}
        aria-describedby={descripcion}
        aria-autocomplete="list"
        aria-expanded={mostrada}
        aria-controls={idLista}
        aria-activedescendant={opcionMarcada !== undefined ? idDe(opcionMarcada) : undefined}
        maxLength={maxLength}
        name={name}
        value={valor}
        onChange={(evento) => {
          setAbierta(true);
          setMarca(SIN_MARCA);
          alCambiar(evento.target.value);
        }}
        onKeyDown={alTeclear}
        onFocus={() => setEnfocado(true)}
        onBlur={() => {
          setEnfocado(false);
          cerrar();
        }}
      />
      <ul id={idLista} role="listbox" aria-labelledby={`${id}${SUFIJO_ETIQUETA}`} className="sugerencias" hidden={!mostrada}>
        {visibles.map((sugerencia, indice) => (
          <li
            key={idDe(sugerencia)}
            id={idDe(sugerencia)}
            role="option"
            aria-selected={indice === marcada}
            className="sugerencia"
            // Solo para que el campo no pierda el foco (y la lista no se cierre) antes del clic.
            onMouseDown={(evento) => evento.preventDefault()}
            onClick={() => elegir(sugerencia)}
          >
            {mostrar(sugerencia)}
          </li>
        ))}
      </ul>
      <div role="status" aria-live="polite" className="campo-busqueda__anuncio">
        {/*
         * NFR-03: con otras sugerencias y la misma cantidad, el texto no cambiaría y el lector de pantalla no volvería a
         * decirlo. La clave es la firma: otras sugerencias reemplazan el nodo, y la región anuncia el nodo nuevo.
         */}
        <span key={firma}>{anuncio}</span>
      </div>
    </div>
  );
}
