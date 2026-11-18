import { useMemo } from 'react';
import { plegar } from '../catalogo/reglas';
import { ListaSugerencias, type AtributosCampo } from './ListaSugerencias';

/** Una opción fija: lo que se graba y lo que se ve. */
export interface Opcion {
  valor: string;
  etiqueta: string;
}

interface PropsCampoConOpciones extends AtributosCampo {
  texto: string;
  alCambiar: (texto: string) => void;
  opciones: readonly Opcion[];
  alElegir: (opcion: Opcion) => void;
  name?: string;
}

const clave = (opcion: Opcion) => opcion.valor;
const mostrar = (opcion: Opcion) => opcion.etiqueta;

/**
 * Un campo que reemplaza a un `select` (D-13 de FEAT-013): al escribir muestra solo las opciones cuya etiqueta contiene
 * lo escrito, comparado con {@link plegar} (sin mayúsculas ni acentos, FR-13). No tiene mínimo de caracteres: con el
 * campo vacío están todas, y la flecha abajo abre la lista completa. Qué se graba y qué pasa con un texto que no
 * coincide (FR-14) lo decide la pantalla.
 */
export function CampoConOpciones({ texto, alCambiar, opciones, alElegir, name, ...atributos }: PropsCampoConOpciones) {
  const filtradas = useMemo(() => {
    const buscado = plegar(texto);
    return opciones.filter((opcion) => plegar(opcion.etiqueta).includes(buscado));
  }, [opciones, texto]);

  return (
    <ListaSugerencias
      {...atributos}
      valor={texto}
      alCambiar={alCambiar}
      sugerencias={filtradas}
      clave={clave}
      mostrar={mostrar}
      alElegir={alElegir}
      name={name}
    />
  );
}
