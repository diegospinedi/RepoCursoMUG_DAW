import { cleanup, fireEvent, screen } from '@testing-library/react';
import { expect, vi, type MockInstance } from 'vitest';

/** Las contraseñas escritas con {@link escribirEn} o {@link escribirYEnviar} en la prueba en curso. */
let contrasenasEscritas: string[] = [];

/** Los métodos de `console` vigilados con {@link vigilarConsola} en la prueba en curso. */
let consolaVigilada: MockInstance[] = [];

const METODOS_CONSOLA = ['log', 'info', 'warn', 'error', 'debug', 'trace'] as const;

/** El texto del elemento que el campo declara en `aria-describedby`: el mensaje que está junto al campo. */
export function mensajeJuntoAlCampo(campo: HTMLElement): string | null {
  const id = campo.getAttribute('aria-describedby');
  return id ? (document.getElementById(id)?.textContent ?? null) : null;
}

/** Escribe una contraseña en el campo de la etiqueta indicada y la registra para la comprobación final. */
export async function escribirEn(etiqueta: string, valor: string): Promise<HTMLElement> {
  contrasenasEscritas.push(valor);
  const campo = await screen.findByLabelText(etiqueta);
  fireEvent.change(campo, { target: { value: valor } });
  return campo;
}

/** Escribe la contraseña en el campo "Contraseña" y pulsa el botón indicado. Devuelve el campo. */
export async function escribirYEnviar(valor: string, boton: string): Promise<HTMLElement> {
  const campo = await escribirEn('Contraseña', valor);
  fireEvent.click(screen.getByRole('button', { name: boton }));
  return campo;
}

/**
 * Empieza a registrar lo que se escribe en la consola (sin mostrarlo). Se llama dentro de la prueba, después de que
 * `restoreMocks` restauró los espías de la anterior; {@link comprobarQueLaContrasenaNoQuedoYLimpiar} revisa lo
 * registrado.
 */
export function vigilarConsola(): void {
  consolaVigilada = METODOS_CONSOLA.map((metodo) => vi.spyOn(console, metodo).mockImplementation(() => {}));
}

function contenido(almacen: Storage): string[] {
  const entradas: string[] = [];
  for (let i = 0; i < almacen.length; i++) {
    const clave = almacen.key(i) ?? '';
    entradas.push(`${clave}=${almacen.getItem(clave) ?? ''}`);
  }
  return entradas;
}

function textoDe(argumento: unknown): string {
  if (typeof argumento === 'string') {
    return argumento;
  }
  try {
    return JSON.stringify(argumento) ?? String(argumento);
  } catch {
    return String(argumento);
  }
}

/**
 * Los lugares del navegador donde quedó alguna de las contraseñas escritas: `localStorage`, `sessionStorage`, la URL
 * (la del router en memoria y la del documento), el estado de la navegación y, si se vigiló, la consola.
 */
export function rastrosDeContrasenas(): string[] {
  const contrasenas = contrasenasEscritas.filter((c) => c !== '');
  if (contrasenas.length === 0) {
    return [];
  }
  const ubicacion = screen.queryByTestId('ubicacion');
  const lugares = [
    ...contenido(localStorage).map((e) => `localStorage: ${e}`),
    ...contenido(sessionStorage).map((e) => `sessionStorage: ${e}`),
    `ruta: ${ubicacion?.textContent ?? ''}`,
    `estado de la navegación: ${ubicacion?.getAttribute('data-estado') ?? ''}`,
    `documento: ${decodeURIComponent(window.location.href)}`,
    ...consolaVigilada.flatMap((espia) =>
      espia.mock.calls.map((argumentos) => `consola: ${argumentos.map(textoDe).join(' ')}`),
    ),
  ];
  return lugares.filter((lugar) =>
    contrasenas.some((c) => lugar.includes(c) || lugar.includes(encodeURIComponent(c))),
  );
}

/**
 * Para el `afterEach` de las pruebas de los formularios de contraseña: comprueba que ninguna contraseña escrita quedó
 * en el navegador ({@link rastrosDeContrasenas}). Después desmonta y limpia el almacenamiento, aunque la comprobación
 * falle.
 */
export function comprobarQueLaContrasenaNoQuedoYLimpiar(): void {
  try {
    expect(rastrosDeContrasenas(), 'La contraseña quedó guardada, en la URL o en la consola').toEqual([]);
  } finally {
    contrasenasEscritas = [];
    consolaVigilada = [];
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
}
