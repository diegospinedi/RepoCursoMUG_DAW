import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import {
  buscarArticulos,
  CAMPO_TEXTO,
  cambiarEstadoArticulo,
  type ArticuloListado,
  type PaginaArticulos,
} from '../api/cliente';
import { validarBusqueda } from '../catalogo/reglas';
import { mostrarImporte } from '../configuracion/numeros';

export const MENSAJE_SIN_COINCIDENCIAS = 'No hay artículos que coincidan con la búsqueda.';

/** Lo que se pide a la Api: el texto ya recortado, la página y un contador para volver a pedir la misma. */
interface Consulta {
  texto: string;
  pagina: number;
  recarga: number;
}

/**
 * Listado del catálogo (FR-04 a FR-07, D-01, D-12): busca por código, código en el proveedor o descripción con Enter o
 * con el botón (nunca en cada tecla), muestra 50 por página con Anterior y Siguiente, y desactiva o reactiva cada
 * artículo refrescando la página actual. No muestra costo ni margen. Lo buscado y el listado viven solo en el estado
 * del componente; no hay consultas periódicas. Sin sesión va a `/ingreso` reemplazando la entrada.
 *
 * Una respuesta de una búsqueda o página anterior que llega tarde se descarta (`vigente`). Mientras hay un cambio de
 * estado en curso o una recarga, los botones de fila quedan deshabilitados, y una guarda síncrona impide que dos clics
 * seguidos envíen dos pedidos antes de que React vuelva a dibujar.
 */
export function Articulos() {
  const navegar = useNavigate();
  const idBusqueda = useId();
  const idErrorBusqueda = `${idBusqueda}-error`;
  const [escrito, setEscrito] = useState('');
  const [errorBusqueda, setErrorBusqueda] = useState<string | undefined>(undefined);
  const [consulta, setConsulta] = useState<Consulta>({ texto: '', pagina: 1, recarga: 0 });
  const [listado, setListado] = useState<PaginaArticulos | null>(null);
  const [cargando, setCargando] = useState(true);
  const [general, setGeneral] = useState<string | null>(null);
  const [cambiando, setCambiando] = useState(false);
  const cambioEnCurso = useRef(false);

  useEffect(() => {
    let vigente = true;
    async function buscar() {
      setCargando(true);
      const resultado = await buscarArticulos(consulta.texto, consulta.pagina);
      if (!vigente) {
        return;
      }
      setCargando(false);
      switch (resultado.tipo) {
        case 'ok':
          setListado(resultado.datos);
          break;
        case 'sinSesion':
          // El cliente ya avisó al manejador de App; se navega igual para no depender de ese registro (ver Arranque).
          navegar('/ingreso', { replace: true });
          break;
        case 'campo':
          // Sin resultados que parezcan de esta búsqueda.
          setListado(null);
          if (resultado.campo === CAMPO_TEXTO) {
            setErrorBusqueda(resultado.mensaje);
          } else {
            setGeneral(resultado.mensaje);
          }
          break;
        default:
          setListado(null);
          setGeneral(resultado.mensaje);
          break;
      }
    }
    void buscar();
    return () => {
      vigente = false;
    };
  }, [consulta, navegar]);

  function alBuscar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    setGeneral(null);
    const validacion = validarBusqueda(escrito);
    if ('error' in validacion) {
      setErrorBusqueda(validacion.error);
      return;
    }
    setErrorBusqueda(undefined);
    setConsulta((anterior) => ({ texto: validacion.texto, pagina: 1, recarga: anterior.recarga + 1 }));
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
        <div className="campo">
          <label htmlFor={idBusqueda}>Texto a buscar</label>
          <input
            id={idBusqueda}
            name={CAMPO_TEXTO}
            type="search"
            autoComplete="off"
            value={escrito}
            aria-invalid={errorBusqueda !== undefined}
            aria-describedby={errorBusqueda !== undefined ? idErrorBusqueda : undefined}
            onChange={(evento) => setEscrito(evento.target.value)}
          />
          {errorBusqueda !== undefined && (
            <p id={idErrorBusqueda} className="campo__error" role="alert">
              {errorBusqueda}
            </p>
          )}
        </div>
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
          <button type="button" disabled={cargando || listado.pagina <= 1} onClick={() => irA(listado.pagina - 1)}>
            Anterior
          </button>
          <span>{`Página ${listado.pagina} de ${totalPaginas}`}</span>
          <button
            type="button"
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
        <Link to="/">Volver al inicio</Link>
      </p>
    </section>
  );
}
