import { act, render } from '@testing-library/react';
import { useEffect } from 'react';
import { MemoryRouter, useLocation, useNavigate, type NavigateFunction } from 'react-router';
import { vi } from 'vitest';
import { App } from '../App';

/** Un pedido tal como lo recibió el `fetch` falso. */
export interface PedidoRegistrado {
  metodo: string;
  ruta: string;
  init: RequestInit | undefined;
}

type Respuesta = () => Response | Promise<Response>;

/** Respuesta JSON con el código indicado. */
export const json = (estado: number, cuerpo: unknown): Respuesta => () =>
  new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'Content-Type': 'application/json' } });

/** Respuesta sin cuerpo. */
export const vacia = (estado: number): Respuesta => () => new Response(null, { status: estado });

/** ValidationProblemDetails como los que arma la Api (`errors.{campo}`). */
export const problema = (estado: number, campo: string, mensaje: string): Respuesta => () =>
  new Response(
    JSON.stringify({ status: estado, title: 'One or more validation errors occurred.', errors: { [campo]: [mensaje] } }),
    { status: estado, headers: { 'Content-Type': 'application/problem+json' } },
  );

/** ProblemDetails sin `errors`, como el 409 de la Api. */
export const problemaGeneral = (estado: number, detalle: string): Respuesta => () =>
  new Response(JSON.stringify({ status: estado, title: 'Conflicto', detail: detalle }), {
    status: estado,
    headers: { 'Content-Type': 'application/problem+json' },
  });

/** Simula una falla de red de `fetch`. */
export const sinRed: Respuesta = () => Promise.reject(new TypeError('Failed to fetch'));

export const MARCA_EJEMPLO = { primario: '#5A5A5A', fondo: '#F5F5F5', esEjemplo: true };

/**
 * Reemplaza `fetch` por uno que responde según `"METODO /ruta"`. Cada clave puede tener una lista de respuestas que
 * se usan en orden (la última se repite). `GET /api/marca` responde la marca de ejemplo salvo que se indique otra.
 */
export function servidorFalso(rutas: Record<string, Respuesta | Respuesta[]>) {
  const llamadas: PedidoRegistrado[] = [];
  const tabla: Record<string, Respuesta[]> = { 'GET /api/marca': [json(200, MARCA_EJEMPLO)] };
  for (const [clave, valor] of Object.entries(rutas)) {
    tabla[clave] = Array.isArray(valor) ? [...valor] : [valor];
  }

  const fetchFalso = vi.fn(async (entrada: RequestInfo | URL, init?: RequestInit) => {
    const ruta = typeof entrada === 'string' ? entrada : entrada instanceof URL ? entrada.pathname : entrada.url;
    const metodo = (init?.method ?? 'GET').toUpperCase();
    llamadas.push({ metodo, ruta, init });
    const lista = tabla[`${metodo} ${ruta}`];
    if (!lista || lista.length === 0) {
      return new Response(null, { status: 404 });
    }
    const respuesta = lista.length > 1 ? lista.shift()! : lista[0]!;
    return respuesta();
  });
  vi.stubGlobal('fetch', fetchFalso);

  return {
    llamadas,
    /** Pedidos a una ruta (sin la marca). */
    pedidosA: (metodo: string, ruta: string) => llamadas.filter((l) => l.metodo === metodo && l.ruta === ruta),
  };
}

function Ubicacion() {
  const ubicacion = useLocation();
  // El estado de la navegación va en un atributo para que las pruebas comprueben que no lleva contraseñas.
  return (
    <output data-testid="ubicacion" data-estado={JSON.stringify(ubicacion.state ?? null)}>
      {ubicacion.pathname + ubicacion.search + ubicacion.hash}
    </output>
  );
}

/** Una entrada del historial: la ruta, o la ruta con el estado de la navegación. */
type Entrada = string | { pathname: string; state?: unknown };

let navegarEnPrueba: NavigateFunction | null = null;

/** Registra la navegación del router montado y la quita al desmontarse (`cleanup` entre pruebas). */
function Navegador() {
  const navegar = useNavigate();
  useEffect(() => {
    navegarEnPrueba = navegar;
    return () => {
      navegarEnPrueba = null;
    };
  }, [navegar]);
  return null;
}

/**
 * Monta la aplicación completa en un router en memoria, con una sonda que muestra la ruta actual. Con una lista de
 * rutas arma ese historial y arranca en la última; `volver()` equivale al botón Atrás del navegador.
 */
export function renderizarApp(ruta: Entrada | Entrada[] = '/') {
  const historial = Array.isArray(ruta) ? ruta : [ruta];
  const resultado = render(
    <MemoryRouter initialEntries={historial} initialIndex={historial.length - 1}>
      <App />
      <Ubicacion />
      <Navegador />
    </MemoryRouter>,
  );
  return {
    ...resultado,
    volver: async () => {
      const navegar = navegarEnPrueba;
      if (navegar === null) {
        throw new Error('volver(): no hay un router montado con renderizarApp.');
      }
      await act(async () => {
        await navegar(-1);
      });
    },
  };
}
