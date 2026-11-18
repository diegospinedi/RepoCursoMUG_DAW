import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import {
  buscarArticulos,
  CAMPO_TEXTO,
  cambiarEstadoArticulo,
  type ArticuloListado,
  type PaginaArticulos,
} from '../api/cliente';
import { validarBusqueda } from '../catalogo/reglas';
import { esBuscableEnVivo, mostrarSugerencia } from '../catalogo/sugerencias';
import { Campo } from '../componentes/Campo';
import { CampoBusqueda } from '../componentes/CampoBusqueda';
import { useBusquedaEnVivo, type Decision } from '../componentes/useBusquedaEnVivo';
import { mostrarImporte } from '../configuracion/numeros';

export const MENSAJE_SIN_COINCIDENCIAS = 'No hay artículos que coincidan con la búsqueda.';

/**
 * D-07 de FEAT-013: con el campo vacío vuelve a la lista inicial; con 1 carácter, o con un texto que la Api rechazaría,
 * no pide nada y deja la tabla como estaba; con 2 o más, busca.
 */
function decidir(clave: string): Decision {
  if (clave === '') {
    return 'vaciar';
  }
  return esBuscableEnVivo(clave) ? 'buscar' : 'ninguno';
}

/**
 * Locales a propósito, y no en el hook: son la excepción de esta pantalla (Block 2 de FEAT-013), donde el `buscar` del
 * hook no pide por su cuenta. Solo programa la `consulta` y devuelve `PROGRAMADA`; el efecto que ya pedía el listado
 * (con `vigente` y la cancelación del pedido anterior) la pide y llena la tabla y las sugerencias, así que el hook no
 * recibe nada que aplicar (`ignorar`).
 */
const PROGRAMADA = { tipo: 'programada' } as const;
const ignorar = () => {};

/**
 * Lo que se pide a la Api: el texto ya recortado, la página, un contador para volver a pedir la misma y si su página 1
 * da sugerencias (la búsqueda en vivo de un texto; "Buscar", Enter y la lista inicial no las dan).
 */
interface Consulta {
  texto: string;
  pagina: number;
  recarga: number;
  sugerir: boolean;
}

/**
 * Listado del catálogo (FR-04 a FR-07, D-01, D-12): busca por código, código en el proveedor o descripción mientras se
 * escribe (FEAT-013: tras una pausa, con 2 caracteres o más) o con Enter o el botón, muestra 50 por página con
 * Anterior y Siguiente, y desactiva o reactiva cada artículo refrescando la página actual. No muestra costo ni margen.
 * Lo buscado y el listado viven solo en el estado del componente; no hay consultas periódicas. Sin sesión va a
 * `/ingreso` reemplazando la entrada.
 *
 * La búsqueda en vivo alimenta la misma `consulta` que el botón (D-06 de FEAT-013): la página 1 de un texto llena la
 * tabla y sus primeras filas son las sugerencias; elegir una abre la edición del artículo (FR-03). Una respuesta de una
 * búsqueda o página anterior que llega tarde se descarta (`vigente`) y su pedido se cancela. Mientras hay un cambio de
 * estado en curso o una recarga, los botones de fila quedan deshabilitados, y una guarda síncrona impide que dos clics
 * seguidos envíen dos pedidos antes de que React vuelva a dibujar.
 */
export function Articulos() {
  const navegar = useNavigate();
  const [escrito, setEscrito] = useState('');
  const [errorBusqueda, setErrorBusqueda] = useState<string | undefined>(undefined);
  const [consulta, setConsulta] = useState<Consulta>({ texto: '', pagina: 1, recarga: 0, sugerir: false });
  const [listado, setListado] = useState<PaginaArticulos | null>(null);
  const [sugerencias, setSugerencias] = useState<readonly ArticuloListado[]>([]);
  const [cargando, setCargando] = useState(true);
  const [general, setGeneral] = useState<string | null>(null);
  const [cambiando, setCambiando] = useState(false);
  const cambioEnCurso = useRef(false);
  /** La consulta cuya respuesta muestra la tabla: a ella se vuelve cuando se descarta una búsqueda en vivo (D-07). */
  const aplicada = useRef<Consulta | null>(null);

  useEffect(() => {
    // Al descartar se vuelve a la consulta que ya muestra la tabla: el pedido en curso se cancela y no se pide otro.
    if (consulta === aplicada.current) {
      return;
    }
    let vigente = true;
    const control = new AbortController();
    async function buscar() {
      setCargando(true);
      const resultado = await buscarArticulos(consulta.texto, consulta.pagina, false, control.signal);
      // Solo se cancela al dejar de estar vigente.
      if (!vigente || resultado.tipo === 'cancelado') {
        return;
      }
      setCargando(false);
      switch (resultado.tipo) {
        case 'ok':
          aplicada.current = consulta;
          setListado(resultado.datos);
          // La página 1 de un texto buscado en vivo da también las sugerencias; las otras páginas, ninguna.
          setSugerencias(consulta.sugerir && consulta.pagina === 1 ? resultado.datos.articulos : []);
          break;
        case 'sinSesion':
          // El cliente ya avisó al manejador de App; se navega igual para no depender de ese registro (ver Arranque).
          navegar('/ingreso', { replace: true });
          break;
        case 'campo':
          // Sin resultados ni sugerencias que parezcan de esta búsqueda.
          setListado(null);
          setSugerencias([]);
          if (resultado.campo === CAMPO_TEXTO) {
            setErrorBusqueda(resultado.mensaje);
          } else {
            setGeneral(resultado.mensaje);
          }
          break;
        default:
          setListado(null);
          setSugerencias([]);
          setGeneral(resultado.mensaje);
          break;
      }
    }
    void buscar();
    return () => {
      vigente = false;
      control.abort();
    };
  }, [consulta, navegar]);

  /**
   * Un texto nuevo: vuelve a la página 1 y limpia los mensajes de la búsqueda anterior. En vivo, la página 1 de un
   * texto da sugerencias; "Buscar" y Enter (`sugerir` falso) no, y la lista queda cerrada.
   */
  function buscarTexto(texto: string, sugerir = texto !== '') {
    setGeneral(null);
    setErrorBusqueda(undefined);
    if (!sugerir) {
      // Cierra ya la lista abierta, aunque el clic en "Buscar" no le saque el foco al campo.
      setSugerencias([]);
    }
    setConsulta((anterior) => ({ texto, pagina: 1, recarga: anterior.recarga + 1, sugerir }));
    return Promise.resolve(PROGRAMADA);
  }

  /**
   * Lo que no se busca en vivo (1 carácter o un texto inválido) no deja sugerencias para elegir, y cancela también el
   * pedido en curso volviendo a la consulta que muestra la tabla: la tabla queda como estaba (D-07).
   */
  function alDescartar() {
    setSugerencias([]);
    const mostrada = aplicada.current;
    // Sin una tabla mostrada todavía, la lista inicial en curso sigue: es la que se va a ver.
    if (mostrada !== null) {
      setCargando(false);
      setConsulta(mostrada);
    }
  }

  const { cancelar } = useBusquedaEnVivo(escrito, (texto) => buscarTexto(texto), ignorar, { decidir, alDescartar });

  // `alDescartar` corre en un efecto del hook, después del dibujo, y el campo vacío ('vaciar') no lo llama: sin esto,
  // un texto que ya no se busca en vivo mostraría por un momento las sugerencias del anterior.
  const sugerenciasVisibles = esBuscableEnVivo(escrito) ? sugerencias : [];

  function alBuscar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const validacion = validarBusqueda(escrito);
    if ('error' in validacion) {
      setGeneral(null);
      setErrorBusqueda(validacion.error);
      return;
    }
    // FR-10: cancela la pausa pendiente y busca enseguida, sin sugerencias.
    cancelar();
    void buscarTexto(validacion.texto, false);
  }

  function irA(pagina: number) {
    setGeneral(null);
    // Conserva el texto de la búsqueda activa.
    setConsulta((anterior) => ({ ...anterior, pagina }));
  }

  /**
   * Pide de nuevo la página actual. `cargando` se marca en el mismo lote que la consulta, no recién en el efecto: así
   * no hay un dibujo intermedio con los botones de fila habilitados y la recarga pendiente.
   */
  const recargar = () => {
    setCargando(true);
    setConsulta((anterior) => ({ ...anterior, recarga: anterior.recarga + 1 }));
  };

  async function cambiarEstado(fila: ArticuloListado) {
    if (cambioEnCurso.current) {
      return;
    }
    cambioEnCurso.current = true;
    setGeneral(null);
    setCambiando(true);
    try {
      const resultado = await cambiarEstadoArticulo(fila.codigo, !fila.activo);
      switch (resultado.tipo) {
        case 'ok': {
          // La fila muestra el estado que devolvió la Api ya mismo; la recarga trae el resto de la página.
          const { codigo, activo } = resultado.datos;
          setListado((anterior) =>
            anterior === null
              ? anterior
              : { ...anterior, articulos: anterior.articulos.map((a) => (a.codigo === codigo ? { ...a, activo } : a)) },
          );
          recargar();
          break;
        }
        case 'sinSesion':
          navegar('/ingreso', { replace: true });
          break;
        case 'conflicto':
          // D-16: otra escritura cambió el artículo; se muestra el texto propio y se vuelve a pedir la página.
          setGeneral(resultado.mensaje);
          recargar();
          break;
        default:
          setGeneral(resultado.mensaje);
          break;
      }
    } finally {
      cambioEnCurso.current = false;
      setCambiando(false);
    }
  }

  const totalPaginas = listado === null ? 1 : Math.max(1, Math.ceil(listado.total / listado.tamanoPagina));

  return (
    <section className="pantalla pantalla--ancha">
      <h1>Artículos</h1>
      <form role="search" onSubmit={alBuscar} noValidate>
        <Campo etiqueta="Texto a buscar" error={errorBusqueda}>
          {(atributos) => (
            <CampoBusqueda
              {...atributos}
              name={CAMPO_TEXTO}
              valor={escrito}
              alCambiar={setEscrito}
              sugerencias={sugerenciasVisibles}
              clave={(articulo) => articulo.codigo}
              mostrar={mostrarSugerencia}
              alElegir={(articulo) => navegar(`/articulos/${articulo.codigo}`)}
            />
          )}
        </Campo>
        <button type="submit">Buscar</button>
      </form>
      {general !== null && (
        <p className="mensaje-general" role="alert">
          {general}
        </p>
      )}
      {cargando && listado === null && <p className="cargando">Cargando…</p>}
      {listado !== null && listado.articulos.length === 0 && <p>{MENSAJE_SIN_COINCIDENCIAS}</p>}
      {listado !== null && listado.articulos.length > 0 && (
        <div className="tabla-contenedor">
          <table className="tabla">
            <caption>Artículos del catálogo</caption>
            <thead>
              <tr>
                <th scope="col">Código</th>
                <th scope="col">Código en el proveedor</th>
                <th scope="col">Descripción</th>
                <th scope="col" className="tabla__importe">
                  Precio de venta ($)
                </th>
                <th scope="col">Estado</th>
                <th scope="col">Acción</th>
              </tr>
            </thead>
            <tbody>
              {listado.articulos.map((fila) => {
                const accion = fila.activo ? 'Desactivar' : 'Reactivar';
                return (
                  <tr key={fila.codigo}>
                    <td>
                      <Link to={`/articulos/${fila.codigo}`} aria-label={`Editar ${fila.codigo} ${fila.descripcion}`}>
                        {fila.codigo}
                      </Link>
                    </td>
                    <td>{fila.codigoProveedor}</td>
                    <td>{fila.descripcion}</td>
                    <td className="tabla__importe">{mostrarImporte(fila.precioVenta)}</td>
                    <td>{fila.activo ? 'Activo' : 'Inactivo'}</td>
                    <td>
                      <button
                        type="button"
                        className="boton-secundario"
                        aria-label={`${accion} ${fila.codigo} ${fila.descripcion}`}
                        disabled={cambiando || cargando}
                        onClick={() => void cambiarEstado(fila)}
                      >
                        {accion}
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {/* La página que se muestra es la que devolvió la Api, no la pedida: mientras llega la otra no cambia. */}
      {listado !== null && (listado.total > 0 || listado.pagina > 1) && (
        <nav className="paginacion" aria-label="Páginas del listado">
          <button
            type="button"
            className="boton-secundario"
            disabled={cargando || listado.pagina <= 1}
            onClick={() => irA(listado.pagina - 1)}
          >
            Anterior
          </button>
          <span>{`Página ${listado.pagina} de ${totalPaginas}`}</span>
          <button
            type="button"
            className="boton-secundario"
            disabled={cargando || listado.pagina >= totalPaginas}
            onClick={() => irA(listado.pagina + 1)}
          >
            Siguiente
          </button>
        </nav>
      )}
      <p>
        <Link to="/articulos/nuevo">Nuevo artículo</Link>
      </p>
      <p>
        <Link to="/articulos/actualizar-costos">Actualizar costos con planilla</Link>
      </p>
      <p>
        <Link to="/">Volver al inicio</Link>
      </p>
    </section>
  );
}
