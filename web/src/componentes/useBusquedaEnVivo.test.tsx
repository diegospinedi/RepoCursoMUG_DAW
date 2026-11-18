import { act, cleanup, fireEvent, render, renderHook, screen } from '@testing-library/react';
import { StrictMode, useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { buscarArticulos, MENSAJE_SIN_CONEXION, type ArticuloListado, type Cancelado } from '../api/cliente';
import { json, servidorFalso, sinRed } from '../pruebas/servidorFalso';
import { Campo } from './Campo';
import { CampoBusqueda } from './CampoBusqueda';
import {
  MINIMO_CARACTERES,
  PAUSA_BUSQUEDA_MS,
  useBusquedaEnVivo,
  type Decision,
  type OpcionesBusquedaEnVivo,
} from './useBusquedaEnVivo';

afterEach(cleanup);

/** Un resultado del `buscar` falso, con la forma de los del cliente. */
interface Dato {
  tipo: 'ok';
  datos: string;
}
const ok = (datos: string): Dato => ({ tipo: 'ok', datos });

/** Un pedido del `buscar` falso: el texto, la señal, y cómo hacerlo llegar con el resultado que elija la prueba. */
interface Pedido {
  texto: string;
  senal: AbortSignal;
  /** Un texto llega como `ok` con ese dato. */
  responder: (resultado: string | Cancelado) => Promise<void>;
}

/** Un `buscar` cuyas respuestas llegan cuando la prueba las libera, en el orden que quiera. */
function buscarFalso() {
  const pedidos: Pedido[] = [];
  const buscar = vi.fn(
    (texto: string, senal: AbortSignal) =>
      new Promise<Dato | Cancelado>((resolver) => {
        pedidos.push({
          texto,
          senal,
          responder: (resultado) =>
            act(async () => {
              resolver(typeof resultado === 'string' ? ok(resultado) : resultado);
            }),
        });
      }),
  );
  return { buscar, pedidos };
}

/** Avanza el reloj falso y deja que React procese lo que eso dispare. */
const avanzar = (ms: number) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });

function montar(
  buscar: (texto: string, senal: AbortSignal) => Promise<Dato | Cancelado>,
  conStrictMode = false,
  opciones?: OpcionesBusquedaEnVivo,
) {
  const alResultado = vi.fn<(resultado: Dato, texto: string) => void>();
  const hook = renderHook(({ texto }) => useBusquedaEnVivo(texto, buscar, alResultado, opciones), {
    initialProps: { texto: '' },
    wrapper: conStrictMode ? StrictMode : undefined,
  });
  const escribir = (texto: string) => hook.rerender({ texto });
  return { ...hook, alResultado, escribir };
}

describe('useBusquedaEnVivo (FEAT-013, D-06)', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('useBusquedaEnVivo_UnPedidoPorPausaYMinimoDosCaracteres', async () => {
    expect(PAUSA_BUSQUEDA_MS).toBe(300);
    expect(MINIMO_CARACTERES).toBe(2);
    const { buscar, pedidos } = buscarFalso();
    const { escribir, alResultado } = montar(buscar, true);

    // Montar no busca, ni con StrictMode.
    await avanzar(1000);
    expect(buscar).not.toHaveBeenCalled();

    // Un carácter (aunque tenga espacios alrededor) no pide, ni después de la pausa.
    escribir(' a ');
    await avanzar(1000);
    expect(buscar).not.toHaveBeenCalled();

    // Diez caracteres seguidos, sin pausa: un solo pedido, 300 ms después de la última tecla, con el texto completo.
    const palabra = 'armazones1';
    for (let i = 1; i <= palabra.length; i++) {
      escribir(palabra.slice(0, i));
      await avanzar(100);
    }
    expect(buscar).not.toHaveBeenCalled();
    await avanzar(PAUSA_BUSQUEDA_MS - 101);
    expect(buscar).not.toHaveBeenCalled();
    await avanzar(1);
    expect(buscar).toHaveBeenCalledTimes(1);
    expect(pedidos[0]?.texto).toBe(palabra);
    expect(pedidos[0]?.senal).toBeInstanceOf(AbortSignal);

    await pedidos[0]?.responder('resultado');
    expect(alResultado).toHaveBeenCalledExactlyOnceWith(ok('resultado'), palabra);

    // Volver a un carácter antes de que venza la pausa no pide nada.
    escribir('ab');
    await avanzar(100);
    escribir('a');
    await avanzar(1000);
    expect(buscar).toHaveBeenCalledTimes(1);
  });

  it('useBusquedaEnVivo_RespuestaViejaDescartada', async () => {
    const { buscar, pedidos } = buscarFalso();
    const { escribir, alResultado } = montar(buscar);

    escribir('ar');
    await avanzar(PAUSA_BUSQUEDA_MS);
    escribir('arm');
    await avanzar(PAUSA_BUSQUEDA_MS);
    expect(pedidos.map((p) => p.texto)).toEqual(['ar', 'arm']);
    // El pedido nuevo cancela el anterior.
    expect(pedidos[0]?.senal.aborted).toBe(true);
    expect(pedidos[1]?.senal.aborted).toBe(false);

    // La respuesta nueva llega primero y la vieja después (un `buscar` que no respetara la señal): solo vale la nueva.
    await pedidos[1]?.responder('de arm');
    await pedidos[0]?.responder('de ar');
    expect(alResultado.mock.calls).toEqual([[ok('de arm'), 'arm']]);

    // Un pedido cancelado nunca llega a la pantalla.
    escribir('arma');
    await avanzar(PAUSA_BUSQUEDA_MS);
    await pedidos[2]?.responder({ tipo: 'cancelado' });
    expect(alResultado).toHaveBeenCalledTimes(1);

    // Bajar a un carácter cancela el pedido en curso: su respuesta ya no corresponde a lo escrito.
    escribir('armaz');
    await avanzar(PAUSA_BUSQUEDA_MS);
    escribir('a');
    expect(pedidos[3]?.senal.aborted).toBe(true);
    await pedidos[3]?.responder('de armaz');
    expect(alResultado).toHaveBeenCalledTimes(1);
  });

  it('useBusquedaEnVivo_EnviarAhoraCancelaLaPausa', async () => {
    const { buscar, pedidos } = buscarFalso();
    const { escribir, alResultado, result } = montar(buscar);

    // Enter o "Buscar" en medio de la pausa: busca enseguida, y la pausa ya no pide.
    escribir('arm');
    await avanzar(100);
    let envio: Promise<void> | undefined;
    act(() => {
      envio = result.current.enviarAhora();
    });
    expect(pedidos.map((p) => p.texto)).toEqual(['arm']);
    await avanzar(1000);
    expect(buscar).toHaveBeenCalledTimes(1);
    await pedidos[0]?.responder('manual');
    await envio;
    expect(alResultado).toHaveBeenLastCalledWith(ok('manual'), 'arm');

    // Con un pedido en vivo en curso, el envío manual lo cancela y su respuesta tardía no pisa la del envío.
    escribir('arma');
    await avanzar(PAUSA_BUSQUEDA_MS);
    act(() => {
      void result.current.enviarAhora('arma');
    });
    expect(pedidos[1]?.senal.aborted).toBe(true);
    await pedidos[2]?.responder('del envío');
    await pedidos[1]?.responder('del vivo');
    expect(alResultado).toHaveBeenLastCalledWith(ok('del envío'), 'arma');
    expect(alResultado).toHaveBeenCalledTimes(2);

    // El envío manual no tiene mínimo: con el campo vacío también busca, como el botón de siempre.
    act(() => {
      void result.current.enviarAhora('');
    });
    expect(pedidos[3]?.texto).toBe('');

    // Cancelar deja todo quieto: ni la pausa ni el pedido en curso llegan.
    escribir('armaz');
    act(() => result.current.cancelar());
    expect(pedidos[3]?.senal.aborted).toBe(true);
    await avanzar(1000);
    expect(buscar).toHaveBeenCalledTimes(4);
  });

  it('useBusquedaEnVivo_TextoRecortado_EspaciosNoPidenOtraVez', async () => {
    const { buscar, pedidos } = buscarFalso();
    const { escribir, alResultado, result } = montar(buscar);

    // Lo que se busca es el texto recortado.
    escribir('  arm ');
    await avanzar(PAUSA_BUSQUEDA_MS);
    expect(pedidos.map((p) => p.texto)).toEqual(['arm']);
    await pedidos[0]?.responder('r');
    expect(alResultado).toHaveBeenLastCalledWith(ok('r'), 'arm');

    // Agregar o quitar solo espacios no programa otro pedido.
    escribir('arm');
    await avanzar(1000);
    escribir(' arm   ');
    await avanzar(1000);
    expect(buscar).toHaveBeenCalledTimes(1);

    // El envío manual también busca el recortado.
    act(() => {
      void result.current.enviarAhora(' arma ');
    });
    expect(pedidos[1]?.texto).toBe('arma');
  });

  it('useBusquedaEnVivo_Decidir_BuscarVaciarONinguno', async () => {
    const decisiones: Record<string, Decision> = { arm: 'buscar', ar: 'vaciar', a: 'ninguno' };
    const alDescartar = vi.fn();
    const { buscar, pedidos } = buscarFalso();
    const { escribir } = montar(buscar, false, { decidir: (clave) => decisiones[clave] ?? 'ninguno', alDescartar });

    // 'buscar': después de la pausa, busca la clave.
    escribir('arm');
    await avanzar(PAUSA_BUSQUEDA_MS - 1);
    expect(buscar).not.toHaveBeenCalled();
    await avanzar(1);
    expect(pedidos.map((p) => p.texto)).toEqual(['arm']);

    // 'vaciar': después de la pausa, busca con el texto vacío (la lista inicial), y cancela el pedido anterior.
    escribir('ar');
    await avanzar(PAUSA_BUSQUEDA_MS);
    expect(pedidos.map((p) => p.texto)).toEqual(['arm', '']);
    expect(pedidos[0]?.senal.aborted).toBe(true);

    // 'ninguno': cancela la pausa y el pedido en curso, y avisa para que la pantalla vacíe las sugerencias.
    escribir('arm');
    await avanzar(100);
    escribir('a');
    expect(alDescartar).toHaveBeenCalledTimes(1);
    expect(pedidos[1]?.senal.aborted).toBe(true);
    await avanzar(1000);
    expect(buscar).toHaveBeenCalledTimes(2);
  });

  it('useBusquedaEnVivo_PorDefecto_MenosDeDosCaracteresDescarta', async () => {
    const alDescartar = vi.fn();
    const { buscar, pedidos } = buscarFalso();
    const { escribir } = montar(buscar, false, { alDescartar });

    escribir('ar');
    await avanzar(PAUSA_BUSQUEDA_MS);
    expect(pedidos.map((p) => p.texto)).toEqual(['ar']);
    escribir('a');
    expect(alDescartar).toHaveBeenCalledTimes(1);
    expect(pedidos[0]?.senal.aborted).toBe(true);
    escribir('');
    expect(alDescartar).toHaveBeenCalledTimes(2);
    await avanzar(1000);
    expect(buscar).toHaveBeenCalledTimes(1);
  });

  it('useBusquedaEnVivo_ClaveCombinada_UnSoloNumeroDePedido', async () => {
    // Block 3: apellido y nombre comparten un solo número de pedido. La pantalla arma la clave con los dos campos y
    // decide por el campo editado.
    const { buscar, pedidos } = buscarFalso();
    const decidir = (clave: string): Decision => {
      const { apellido, nombre } = JSON.parse(clave) as { apellido: string; nombre: string };
      return apellido.length >= 2 || nombre.length >= 2 ? 'buscar' : 'ninguno';
    };
    const { escribir, alResultado } = montar(buscar, false, { decidir });
    const clave = (apellido: string, nombre: string) => JSON.stringify({ apellido, nombre });

    escribir(clave('pe', ''));
    await avanzar(PAUSA_BUSQUEDA_MS);
    escribir(clave('pe', 'an'));
    await avanzar(PAUSA_BUSQUEDA_MS);
    expect(pedidos.map((p) => p.texto)).toEqual([clave('pe', ''), clave('pe', 'an')]);
    expect(pedidos[0]?.senal.aborted).toBe(true);

    // La respuesta vieja del apellido llega después de la del nombre: no la pisa.
    await pedidos[1]?.responder('apellido y nombre');
    await pedidos[0]?.responder('solo apellido');
    expect(alResultado.mock.calls).toEqual([[ok('apellido y nombre'), clave('pe', 'an')]]);

    // La decisión es de la pantalla, por campo: con los dos campos cortos no pide, aunque la clave sea larga.
    escribir(clave('p', 'a'));
    await avanzar(1000);
    expect(buscar).toHaveBeenCalledTimes(2);
  });

  it('useBusquedaEnVivo_BuscarRechaza_SinRechazoSinManejar', async () => {
    const buscar = vi.fn((texto: string) =>
      texto === 'falla' ? Promise.reject(new Error('no debería rechazar')) : Promise.resolve(ok(texto)),
    );
    const { escribir, alResultado, result } = montar(buscar);

    // En vivo: el rechazo se descarta en silencio (sin rechazo sin manejar, sin consola).
    escribir('falla');
    await avanzar(PAUSA_BUSQUEDA_MS);
    expect(buscar).toHaveBeenCalledTimes(1);
    expect(alResultado).not.toHaveBeenCalled();

    // El envío manual termina igual, sin rechazar.
    await act(async () => {
      await expect(result.current.enviarAhora('falla')).resolves.toBeUndefined();
    });

    // Si la pantalla lanza al recibir el resultado, el envío tampoco rechaza.
    alResultado.mockImplementation(() => {
      throw new Error('falla de la pantalla');
    });
    await act(async () => {
      await expect(result.current.enviarAhora('arm')).resolves.toBeUndefined();
    });
    expect(alResultado).toHaveBeenCalledTimes(1);
  });

  it('useBusquedaEnVivo_Desmontar_LimpiaLaPausaYCancelaElPedido', async () => {
    const { buscar, pedidos } = buscarFalso();
    const enCurso = montar(buscar, true);
    enCurso.escribir('arm');
    await avanzar(PAUSA_BUSQUEDA_MS);
    enCurso.unmount();
    expect(pedidos[0]?.senal.aborted).toBe(true);
    await pedidos[0]?.responder('tarde');
    expect(enCurso.alResultado).not.toHaveBeenCalled();

    const enPausa = montar(buscar, true);
    enPausa.escribir('arm');
    await avanzar(100);
    enPausa.unmount();
    await avanzar(1000);
    expect(buscar).toHaveBeenCalledTimes(1);
  });
});

/** Datos ficticios del catálogo. */
const FILA = (codigo: number, descripcion: string): ArticuloListado => ({
  codigo,
  codigoProveedor: `P-${codigo}`,
  descripcion,
  precioVenta: 100,
  activo: true,
});
const pagina = (articulos: ArticuloListado[]) => ({ articulos, pagina: 1, tamanoPagina: 50, total: articulos.length });

/** Una pantalla mínima: el hook con el cliente real, sugerencias si la búsqueda anduvo y el mensaje de hoy si falló. */
function Buscador() {
  const [texto, setTexto] = useState('');
  const [sugerencias, setSugerencias] = useState<ArticuloListado[]>([]);
  const [error, setError] = useState<string | null>(null);
  useBusquedaEnVivo(
    texto,
    (buscado, senal) => buscarArticulos(buscado, 1, false, senal),
    (resultado) => {
      if (resultado.tipo === 'ok') {
        setSugerencias(resultado.datos.articulos);
        setError(null);
      } else {
        setSugerencias([]);
        setError(resultado.tipo === 'sinSesion' ? null : resultado.mensaje);
      }
    },
  );
  return (
    <form role="search">
      <Campo etiqueta="Texto a buscar" error={undefined}>
        {(atributos) => (
          <CampoBusqueda
            {...atributos}
            valor={texto}
            alCambiar={setTexto}
            sugerencias={sugerencias}
            clave={(a) => a.codigo}
            mostrar={(a) => a.descripcion}
            alElegir={() => {}}
          />
        )}
      </Campo>
      {error !== null && <p role="alert">{error}</p>}
    </form>
  );
}

describe('useBusquedaEnVivo con el cliente de la Api', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('useBusquedaEnVivo_ErrorDeRed_SinSugerencias', async () => {
    const servidor = servidorFalso({
      'GET /api/articulos?texto=arm&pagina=1': json(200, pagina([FILA(7, 'Armazón metal'), FILA(8, 'Armazón acetato')])),
      'GET /api/articulos?texto=armx&pagina=1': sinRed,
    });
    render(<Buscador />);
    const campo = screen.getByRole('combobox', { name: 'Texto a buscar' });
    campo.focus();

    fireEvent.change(campo, { target: { value: 'arm' } });
    await avanzar(PAUSA_BUSQUEDA_MS);
    expect(screen.queryAllByRole('option').map((o) => o.textContent)).toEqual(['Armazón metal', 'Armazón acetato']);

    // AC-10: la búsqueda falla por la red; las sugerencias se ocultan y se ve el mensaje de hoy.
    fireEvent.change(campo, { target: { value: 'armx' } });
    await avanzar(PAUSA_BUSQUEDA_MS);
    expect(servidor.pedidosA('GET', '/api/articulos?texto=armx&pagina=1')).toHaveLength(1);
    expect(screen.queryAllByRole('option')).toEqual([]);
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(campo.getAttribute('aria-expanded')).toBe('false');
    expect(screen.getByRole('alert').textContent).toBe(MENSAJE_SIN_CONEXION);
  });
});
