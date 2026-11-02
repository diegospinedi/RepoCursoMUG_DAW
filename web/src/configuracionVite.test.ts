import { afterEach, describe, expect, it, vi } from 'vitest';

afterEach(() => {
  vi.unstubAllEnvs();
  vi.resetModules();
});

/** Lee `secure` del proxy de /api con `OPTICA_API_URL` fijada (o sin fijar, con `undefined`). */
async function proxySeguro(url: string | undefined): Promise<unknown> {
  vi.stubEnv('OPTICA_API_URL', url);
  vi.resetModules();
  // Ruta no literal a propósito: vite.config.ts corre en Node y tsc no la sigue desde src/ (no hay @types/node).
  const ruta: string = '../vite.config.ts';
  const { default: configuracion } = (await import(/* @vite-ignore */ ruta)) as { default: unknown };
  const proxy = (configuracion as { server?: { proxy?: Record<string, { secure?: boolean }> } }).server?.proxy;
  return proxy?.['/api']?.secure;
}

describe('configuración de Vite', () => {
  it.each([
    [undefined, false],
    ['https://localhost:5001', false],
    ['https://127.0.0.1:5001', false],
    ['https://api.optica.example', true],
    ['https://192.168.1.20:5001', true],
    ['https://localhost.example.com', true],
  ] as const)('Proxy_Api%s_VerificaTlsSoloFueraDeLocalhost', async (url, seguro) => {
    expect(await proxySeguro(url)).toBe(seguro);
  });
});
