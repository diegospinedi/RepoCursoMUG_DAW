import { cleanup, fireEvent, screen } from '@testing-library/react';
import { expect } from 'vitest';

/** La última contraseña escrita con {@link escribirYEnviar} en la prueba en curso. */
let ultimaContrasena: string | null = null;

/** El texto del elemento que el campo declara en `aria-describedby`: el mensaje que está junto al campo. */
export function mensajeJuntoAlCampo(campo: HTMLElement): string | null {
  const id = campo.getAttribute('aria-describedby');
  return id ? (document.getElementById(id)?.textContent ?? null) : null;
}

/** Escribe la contraseña en el campo y pulsa el botón indicado. Devuelve el campo. */
export async function escribirYEnviar(valor: string, boton: string): Promise<HTMLElement> {
  ultimaContrasena = valor;
  const campo = await screen.findByLabelText('Contraseña');
  fireEvent.change(campo, { target: { value: valor } });
  fireEvent.click(screen.getByRole('button', { name: boton }));
  return campo;
}

function contenido(almacen: Storage): string[] {
  const entradas: string[] = [];
  for (let i = 0; i < almacen.length; i++) {
    const clave = almacen.key(i) ?? '';
    entradas.push(`${clave}=${almacen.getItem(clave) ?? ''}`);
  }
  return entradas;
}

/**
 * Para el `afterEach` de las pruebas de los formularios de contraseña (definir e ingresar): comprueba que la última
 * contraseña escrita no quedó en `localStorage`, `sessionStorage` ni en la URL (la del router en memoria y la del
 * documento). Después desmonta y limpia el almacenamiento, aunque la comprobación falle.
 */
export function comprobarQueLaContrasenaNoQuedoYLimpiar(): void {
  const contrasena = ultimaContrasena;
  ultimaContrasena = null;
  try {
    if (contrasena === null || contrasena === '') {
      return;
    }
    const rastros = [
      ...contenido(localStorage).map((e) => `localStorage: ${e}`),
      ...contenido(sessionStorage).map((e) => `sessionStorage: ${e}`),
      `ruta: ${screen.queryByTestId('ubicacion')?.textContent ?? ''}`,
      `documento: ${decodeURIComponent(window.location.href)}`,
    ].filter((rastro) => rastro.includes(contrasena) || rastro.includes(encodeURIComponent(contrasena)));
    expect(rastros, 'La contraseña quedó guardada o en la URL').toEqual([]);
  } finally {
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
}
