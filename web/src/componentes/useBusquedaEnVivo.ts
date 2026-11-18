import { useCallback, useEffect, useRef } from 'react';
import type { Cancelado } from '../api/cliente';
import { recortar } from '../catalogo/reglas';

/** La pausa de escritura después de la cual se busca (FR-01, NFR-02). */
export const PAUSA_BUSQUEDA_MS = 300;

/** Los caracteres, recortados, que hacen falta para buscar en vivo con la decisión por defecto (FR-01, AC-02). */
export const MINIMO_CARACTERES = 2;

/** Cualquier resultado del cliente: todos llevan su `tipo`. */
interface ConTipo {
  tipo: string;
}

/**
 * Busca una clave con la señal del pedido. Devuelve el resultado del cliente, o `cancelado` si la señal se canceló.
 * **No debe rechazar:** el cliente traduce toda falla a un resultado. Si igual rechaza, el pedido se descarta en
 * silencio (sin rechazo sin manejar y sin nada en la consola) y la pantalla no recibe nada.
 */
export type BuscarEnVivo<R extends ConTipo> = (clave: string, senal: AbortSignal) => Promise<R | Cancelado>;

/**
 * Qué hacer con una clave nueva: `buscar` la busca después de la pausa; `vaciar` busca con `''` después de la pausa (la
 * lista inicial); `ninguno` cancela la pausa y el pedido en curso y llama a `alDescartar`.
 */
export type Decision = 'buscar' | 'vaciar' | 'ninguno';

export interface OpcionesBusquedaEnVivo {
  /** Por defecto: con {@link MINIMO_CARACTERES} o más, `buscar`; si no, `ninguno`. */
  decidir?: (clave: string) => Decision;
  /** Se llama con cada `ninguno`, para que la pantalla vacíe las sugerencias. */
  alDescartar?: () => void;
}

export interface BusquedaEnVivo {
  /**
   * El envío manual ("Buscar" o Enter, FR-10): cancela la pausa pendiente y el pedido en curso y busca enseguida, sin
   * decidir ni mínimo, la clave indicada (recortada) o, sin ella, la última. Termina cuando llega la respuesta; nunca
   * rechaza.
   */
  enviarAhora: (clave?: string) => Promise<void>;
  /** Cancela la pausa pendiente y el pedido en curso: ninguna de sus respuestas llega. */
  cancelar: () => void;
}

const decidirPorDefecto = (clave: string): Decision => (clave.length >= MINIMO_CARACTERES ? 'buscar' : 'ninguno');

/**
 * La búsqueda mientras se escribe (D-06 de FEAT-013, ADR-032).
 *
 * `clave` es lo que identifica la búsqueda: el texto del campo o, con varios campos que comparten un solo número de
 * pedido (Block 3: apellido, nombre y DNI, D-08), un filtro combinado serializado que arma la pantalla (por ejemplo con
 * `JSON.stringify`, con cada campo ya recortado). La clave se recorta; agregar o quitar solo espacios no pide otra vez.
 * Cada clave nueva pasa por `opciones.decidir`, donde la pantalla decide por campo (ver {@link Decision}).
 *
 * Cada pedido lleva un número creciente y su `AbortController`: uno nuevo cancela el anterior, y solo la respuesta del
 * último llega a `alResultado`, tal cual la devolvió el cliente (cada pantalla conserva su `switch`), con la clave que
 * se buscó. Un pedido cancelado nunca llega. Si `alResultado` lanza, el error se descarta. Montar el hook no busca. Al
 * desmontar limpia la pausa y cancela el pedido, también con el doble montaje de StrictMode.
 */
export function useBusquedaEnVivo<R extends ConTipo>(
  clave: string,
  buscar: BuscarEnVivo<R>,
  alResultado: (resultado: R, clave: string) => void,
  opciones?: OpcionesBusquedaEnVivo,
): BusquedaEnVivo {
  const recortada = recortar(clave);

  // Las funciones y la clave más recientes, para que la pausa y el envío manual no usen las de un render viejo.
  const ultimos = useRef({ recortada, buscar, alResultado, opciones });
  useEffect(() => {
    ultimos.current = { recortada, buscar, alResultado, opciones };
  });

  const numero = useRef(0);
  const controlador = useRef<AbortController | null>(null);
  const pausa = useRef<ReturnType<typeof setTimeout> | null>(null);

  const limpiarPausa = useCallback(() => {
    if (pausa.current !== null) {
      clearTimeout(pausa.current);
      pausa.current = null;
    }
  }, []);

  /** Invalida el pedido en curso: su número deja de ser el último y su señal se cancela. */
  const cancelarPedido = useCallback(() => {
    numero.current++;
    controlador.current?.abort();
    controlador.current = null;
  }, []);

  const pedir = useCallback(
    async (buscada: string) => {
      cancelarPedido();
      const propio = numero.current;
      const control = new AbortController();
      controlador.current = control;
      let resultado: R | Cancelado;
      try {
        resultado = await ultimos.current.buscar(buscada, control.signal);
      } catch {
        // `buscar` no debe rechazar (ver BuscarEnVivo): el pedido se descarta.
        resultado = { tipo: 'cancelado' };
      }
      if (propio !== numero.current) {
        return;
      }
      controlador.current = null;
      if (esCancelado(resultado)) {
        return;
      }
      try {
        ultimos.current.alResultado(resultado, buscada);
      } catch {
        // Un error de la pantalla no se convierte en un rechazo sin manejar del envío ni de la pausa.
      }
    },
    [cancelarPedido],
  );

  const cancelar = useCallback(() => {
    limpiarPausa();
    cancelarPedido();
  }, [limpiarPausa, cancelarPedido]);

  const enviarAhora = useCallback(
    (manual?: string) => {
      limpiarPausa();
      return pedir(manual === undefined ? ultimos.current.recortada : recortar(manual));
    },
    [limpiarPausa, pedir],
  );

  // La clave que ya se atendió: la inicial no se busca, y el segundo montaje de StrictMode no vuelve a programarla.
  const atendida = useRef(recortada);
  useEffect(() => {
    if (recortada === atendida.current) {
      return;
    }
    atendida.current = recortada;
    limpiarPausa();
    const { decidir = decidirPorDefecto, alDescartar } = ultimos.current.opciones ?? {};
    const decision = decidir(recortada);
    if (decision === 'ninguno') {
      cancelarPedido();
      alDescartar?.();
      return;
    }
    const buscada = decision === 'vaciar' ? '' : recortada;
    pausa.current = setTimeout(() => {
      pausa.current = null;
      void pedir(buscada);
    }, PAUSA_BUSQUEDA_MS);
  }, [recortada, limpiarPausa, cancelarPedido, pedir]);

  // Al desmontar: ni la pausa ni el pedido en curso llegan a una pantalla que ya no está.
  useEffect(() => cancelar, [cancelar]);

  return { enviarAhora, cancelar };
}

function esCancelado(resultado: ConTipo): resultado is Cancelado {
  return resultado.tipo === 'cancelado';
}
