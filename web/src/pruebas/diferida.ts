import { act } from '@testing-library/react';
import { json } from './servidorFalso';

/**
 * Una respuesta JSON del `fetch` falso que llega recién cuando la prueba llama a `liberar()` (D-18 de FEAT-013): sirve
 * para tener un pedido en curso, o para que una respuesta vieja llegue después de una nueva.
 */
export function diferida(cuerpo: unknown, estado = 200) {
  let liberar: () => void = () => {};
  const llegada = new Promise<void>((resolver) => {
    liberar = resolver;
  });
  return {
    respuesta: async () => {
      await llegada;
      return json(estado, cuerpo)();
    },
    liberar: () => act(async () => liberar()),
  };
}
