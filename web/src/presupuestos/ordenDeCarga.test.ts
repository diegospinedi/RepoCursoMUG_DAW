import { beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Las reglas del presupuesto y el cliente de la Api se pueden cargar en cualquier orden: entre ellos no hay una
 * dependencia circular de valores (FEAT-003c). Si `api/cliente` importara valores de `presupuestos/reglas`, cargar
 * primero las reglas (reglas → configuracion/reglas → cliente → reglas) dejaría al cliente leyendo constantes todavía
 * sin inicializar: `ReferenceError: Cannot access … before initialization`.
 */
describe('orden de carga de los módulos', () => {
  beforeEach(() => {
    vi.resetModules();
  });

  it('OrdenDeCarga_ReglasAntesQueElCliente_NoFalla', async () => {
    const reglas = await import('./reglas');
    const cliente = await import('../api/cliente');

    expect(reglas.MENSAJE_PRESUPUESTO_CAMBIADO).toBe('El presupuesto cambió mientras lo editabas. Volvé a abrirlo.');
    expect(cliente.MENSAJE_PRESUPUESTO_CAMBIADO).toBe(reglas.MENSAJE_PRESUPUESTO_CAMBIADO);
    expect(cliente.MENSAJE_PRESUPUESTO_FINAL).toBe(reglas.MENSAJE_PRESUPUESTO_FINAL);
  });

  it('OrdenDeCarga_ClienteAntesQueLasReglas_NoFalla', async () => {
    const cliente = await import('../api/cliente');
    const reglas = await import('./reglas');

    expect(cliente.MENSAJE_PRESUPUESTO_FINAL).toBe('El presupuesto está en Final y no se puede modificar.');
    expect(reglas.MENSAJE_PRESUPUESTO_FINAL).toBe(cliente.MENSAJE_PRESUPUESTO_FINAL);
  });
});
