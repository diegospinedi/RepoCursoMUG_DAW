import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';
import { buscarArticulos, CAMPO_TEXTO, type ArticuloListado, type PaginaArticulos } from '../api/cliente';
import { recortar, validarBusqueda } from '../catalogo/reglas';
import { esBuscableEnVivo, mostrarSugerencia } from '../catalogo/sugerencias';
import { Campo } from '../componentes/Campo';
import { CampoBusqueda } from '../componentes/CampoBusqueda';
import { useBusquedaEnVivo, type Decision } from '../componentes/useBusquedaEnVivo';
import { mostrarImporte } from '../configuracion/numeros';
import { MENSAJE_PRECIO_NEGATIVO } from './reglas';

export const MENSAJE_SIN_ACTIVOS = 'No hay artículos activos que coincidan con la búsqueda.';

/**
 * La búsqueda vigente: el texto ya recortado, la página y si su página 1 da sugerencias (la búsqueda en vivo; "Buscar"
 * y Enter no las dan).
 */
interface Consulta {
  texto: string;
  pagina: number;
  sugerir: boolean;
}

/**
 * D-07 de FEAT-013: con 2 caracteres o más, busca en vivo. Con el campo vacío (la vuelta a como al abrir, ver
 * `alDescartar`), con 1 carácter o con un texto que la Api rechazaría, no pide nada.
 */
const decidir = (clave: string): Decision => (esBuscableEnVivo(clave) ? 'buscar' : 'ninguno');

/**
 * Locales a propósito, y no en el hook: son la excepción de este buscador (Block 2 de FEAT-013), donde el `buscar` del
 * hook no pide por su cuenta. Solo programa la `consulta` y devuelve `PROGRAMADA`; el efecto que ya pedía los
 * resultados (con `vigente` y la cancelación del pedido anterior) la pide y llena la tabla y las sugerencias, así que el
 * hook no recibe nada que aplicar (`ignorar`).
 */
const PROGRAMADA = { tipo: 'programada' } as const;
const ignorar = () => {};

interface Props {
  /** Recibe el artículo elegido; solo se llama con precio de venta no negativo (FR-12). */
  alAgregar: (articulo: ArticuloListado) => void;
  /** Un error que no corresponde al texto buscado (por ejemplo, la Api caída): lo muestra la pantalla. */
  alError: (mensaje: string | null) => void;
}

/**
 * Buscador de artículos activos para las líneas del presupuesto (FR-09, D-15): busca mientras se escribe (FEAT-013:
 * tras una pausa, con 2 caracteres o más) o con Enter o el botón, pide `soloActivos=true`, lista Código, Descripción y
 * Precio de venta con "Agregar" por resultado y pagina de a 50. La página 1 de la búsqueda en vivo da también las
 * sugerencias; elegir una pasa por la misma regla que "Agregar" (D-09 de FEAT-013). Con el campo vacío vuelve a como al
 * abrir, sin resultados. Un artículo con precio de venta negativo no se entrega y muestra el mensaje de AC-16 junto al
 * buscador (FR-12). Una respuesta de una búsqueda o página anterior que llega tarde se descarta y su pedido se
 * cancela. Sin sesión va a `/ingreso` reemplazando la entrada.
 */
export function BuscadorArticulos({ alAgregar, alError }: Props) {
  const navegar = useNavigate();
  const [escrito, setEscrito] = useState('');
  /** El mensaje junto al buscador: el texto a buscar inválido o el artículo con precio negativo (AC-16). */
  const [errorBuscador, setErrorBuscador] = useState<string | undefined>(undefined);
  const [consulta, setConsulta] = useState<Consulta | null>(null);
  const [resultados, setResultados] = useState<PaginaArticulos | null>(null);
  const [sugerencias, setSugerencias] = useState<readonly ArticuloListado[]>([]);
  const [buscando, setBuscando] = useState(false);
  /** La consulta cuyos resultados se ven (null: ninguno): a ella se vuelve al descartar una búsqueda en vivo (D-07). */
  const aplicada = useRef<Consulta | null>(null);

  useEffect(() => {
    // Al descartar se vuelve a la consulta que ya se ve: el pedido en curso se cancela y no se pide otro.
    if (consulta === null || consulta === aplicada.current) {
      return;
    }
    let vigente = true;
    const control = new AbortController();
    async function buscar(actual: Consulta) {
      setBuscando(true);
      const resultado = await buscarArticulos(actual.texto, actual.pagina, true, control.signal);
      // Solo se cancela al dejar de estar vigente.
      if (!vigente || resultado.tipo === 'cancelado') {
        return;
      }
      setBuscando(false);
      switch (resultado.tipo) {
        case 'ok':
          aplicada.current = actual;
          setResultados(resultado.datos);
          setSugerencias(actual.sugerir && actual.pagina === 1 ? resultado.datos.articulos : []);
          break;
        case 'sinSesion':
          navegar('/ingreso', { replace: true });
          break;
        case 'campo':
          aplicada.current = null;
          setResultados(null);
          setSugerencias([]);
          if (resultado.campo === CAMPO_TEXTO) {
            setErrorBuscador(resultado.mensaje);
          } else {
            alError(resultado.mensaje);
          }
          break;
        default:
          aplicada.current = null;
          setResultados(null);
          setSugerencias([]);
          alError(resultado.mensaje);
          break;
      }
    }
    void buscar(consulta);
    return () => {
      vigente = false;
      control.abort();
    };
  }, [consulta, navegar, alError]);

  /**
   * Un texto nuevo: vuelve a la página 1 y limpia los mensajes de la búsqueda anterior. En vivo, la página 1 da
   * sugerencias; "Buscar" y Enter (`sugerir` falso) no, y la lista queda cerrada.
   */
  function buscarTexto(texto: string, sugerir: boolean) {
    alError(null);
    setErrorBuscador(undefined);
    if (!sugerir) {
      // Cierra ya la lista abierta, aunque el clic en "Buscar" no le saque el foco al campo.
      setSugerencias([]);
    }
    setConsulta({ texto, pagina: 1, sugerir });
    return Promise.resolve(PROGRAMADA);
  }

  /**
   * Lo que no se busca en vivo no deja sugerencias para elegir y cancela también el pedido en curso. Con 1 carácter o
   * un texto inválido vuelve a la consulta que se ve, y los resultados quedan como estaban; con el campo vacío, a como
   * al abrir, sin resultados (D-07).
   */
  function alDescartar() {
    setSugerencias([]);
    setBuscando(false);
    if (recortar(escrito) === '') {
      aplicada.current = null;
      setResultados(null);
      setErrorBuscador(undefined);
    }
    setConsulta(aplicada.current);
  }

  const { cancelar } = useBusquedaEnVivo(escrito, (texto) => buscarTexto(texto, true), ignorar, {
    decidir,
    alDescartar,
  });

  // `alDescartar` corre en un efecto del hook, después del dibujo: sin esto, un texto que ya no se busca en vivo
  // mostraría por un momento las sugerencias del anterior.
  const sugerenciasVisibles = esBuscableEnVivo(escrito) ? sugerencias : [];

  function alBuscar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const validacion = validarBusqueda(escrito);
    if ('error' in validacion) {
      alError(null);
      setErrorBuscador(validacion.error);
      return;
    }
    // FR-10: cancela la pausa pendiente y busca enseguida, sin sugerencias.
    cancelar();
    void buscarTexto(validacion.texto, false);
  }

  function irA(pagina: number) {
    alError(null);
    setConsulta((anterior) => (anterior === null ? anterior : { ...anterior, pagina }));
  }

  function agregar(articulo: ArticuloListado) {
    if (articulo.precioVenta < 0) {
      setErrorBuscador(MENSAJE_PRECIO_NEGATIVO);
      return;
    }
    setErrorBuscador(undefined);
    alAgregar(articulo);
  }

  const totalPaginas =
    resultados === null ? 1 : Math.max(1, Math.ceil(resultados.total / resultados.tamanoPagina));

  return (
    <>
      <form role="search" onSubmit={alBuscar} noValidate>
        <Campo etiqueta="Texto a buscar" error={errorBuscador}>
          {(atributos) => (
            <CampoBusqueda
              {...atributos}
              name={CAMPO_TEXTO}
              valor={escrito}
              alCambiar={setEscrito}
              sugerencias={sugerenciasVisibles}
              clave={(articulo) => articulo.codigo}
              mostrar={mostrarSugerencia}
              // D-09: por la misma regla que "Agregar", nunca directo a `alAgregar`.
              alElegir={agregar}
            />
          )}
        </Campo>
        <button type="submit">Buscar</button>
      </form>
      {buscando && resultados === null && <p className="cargando">Cargando…</p>}
      {resultados !== null && resultados.articulos.length === 0 && <p>{MENSAJE_SIN_ACTIVOS}</p>}
      {resultados !== null && resultados.articulos.length > 0 && (
        <div className="tabla-contenedor">
          <table className="tabla">
            <caption>Artículos activos</caption>
            <thead>
              <tr>
                <th scope="col">Código</th>
                <th scope="col">Descripción</th>
                <th scope="col" className="tabla__importe">
                  Precio de venta ($)
                </th>
                <th scope="col">Acción</th>
              </tr>
            </thead>
            <tbody>
              {resultados.articulos.map((articulo) => (
                <tr key={articulo.codigo}>
                  <td>{articulo.codigo}</td>
                  <td>{articulo.descripcion}</td>
                  <td className="tabla__importe">{mostrarImporte(articulo.precioVenta)}</td>
                  <td>
                    <button
                      type="button"
                      className="boton-secundario"
                      aria-label={`Agregar ${articulo.codigo} ${articulo.descripcion}`}
                      onClick={() => agregar(articulo)}
                    >
                      Agregar
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {resultados !== null && (resultados.total > 0 || resultados.pagina > 1) && (
        <nav className="paginacion" aria-label="Páginas de la búsqueda">
          <button
            type="button"
            className="boton-secundario"
            disabled={buscando || resultados.pagina <= 1}
            onClick={() => irA(resultados.pagina - 1)}
          >
            Anterior
          </button>
          <span>{`Página ${resultados.pagina} de ${totalPaginas}`}</span>
          <button
            type="button"
            className="boton-secundario"
            disabled={buscando || resultados.pagina >= totalPaginas}
            onClick={() => irA(resultados.pagina + 1)}
          >
            Siguiente
          </button>
        </nav>
      )}
    </>
  );
}
