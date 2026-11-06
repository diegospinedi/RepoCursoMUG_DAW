import { useEffect, useId, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';
import { buscarArticulos, CAMPO_TEXTO, type ArticuloListado, type PaginaArticulos } from '../api/cliente';
import { validarBusqueda } from '../catalogo/reglas';
import { mostrarImporte } from '../configuracion/numeros';
import { MENSAJE_PRECIO_NEGATIVO } from './reglas';

export const MENSAJE_SIN_ACTIVOS = 'No hay artículos activos que coincidan con la búsqueda.';

/** La búsqueda vigente: el texto ya recortado y la página. */
interface Consulta {
  texto: string;
  pagina: number;
}

interface Props {
  /** Recibe el artículo elegido; solo se llama con precio de venta no negativo (FR-12). */
  alAgregar: (articulo: ArticuloListado) => void;
  /** Un error que no corresponde al texto buscado (por ejemplo, la Api caída): lo muestra la pantalla. */
  alError: (mensaje: string | null) => void;
}

/**
 * Buscador de artículos activos para las líneas del presupuesto (FR-09, D-15): busca con Enter o con el botón (nunca
 * en cada tecla), pide `soloActivos=true`, lista Código, Descripción y Precio de venta con "Agregar" por resultado y
 * pagina de a 50. Un artículo con precio de venta negativo no se entrega y muestra el mensaje de AC-16 junto al
 * buscador (FR-12). Una respuesta de una búsqueda o página anterior que llega tarde se descarta. Sin sesión va a
 * `/ingreso` reemplazando la entrada.
 */
export function BuscadorArticulos({ alAgregar, alError }: Props) {
  const navegar = useNavigate();
  const idBusqueda = useId();
  const idErrorBusqueda = `${idBusqueda}-error`;
  const [escrito, setEscrito] = useState('');
  /** El mensaje junto al buscador: el texto a buscar inválido o el artículo con precio negativo (AC-16). */
  const [errorBuscador, setErrorBuscador] = useState<string | undefined>(undefined);
  const [consulta, setConsulta] = useState<Consulta | null>(null);
  const [resultados, setResultados] = useState<PaginaArticulos | null>(null);
  const [buscando, setBuscando] = useState(false);

  useEffect(() => {
    if (consulta === null) {
      return;
    }
    let vigente = true;
    async function buscar(actual: Consulta) {
      setBuscando(true);
      const resultado = await buscarArticulos(actual.texto, actual.pagina, true);
      if (!vigente) {
        return;
      }
      setBuscando(false);
      switch (resultado.tipo) {
        case 'ok':
          setResultados(resultado.datos);
          break;
        case 'sinSesion':
          navegar('/ingreso', { replace: true });
          break;
        case 'campo':
          setResultados(null);
          if (resultado.campo === CAMPO_TEXTO) {
            setErrorBuscador(resultado.mensaje);
          } else {
            alError(resultado.mensaje);
          }
          break;
        default:
          setResultados(null);
          alError(resultado.mensaje);
          break;
      }
    }
    void buscar(consulta);
    return () => {
      vigente = false;
    };
  }, [consulta, navegar, alError]);

  function alBuscar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    alError(null);
    const validacion = validarBusqueda(escrito);
    if ('error' in validacion) {
      setErrorBuscador(validacion.error);
      return;
    }
    setErrorBuscador(undefined);
    setConsulta({ texto: validacion.texto, pagina: 1 });
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
        <div className="campo">
          <label htmlFor={idBusqueda}>Texto a buscar</label>
          <input
            id={idBusqueda}
            name={CAMPO_TEXTO}
            type="search"
            autoComplete="off"
            value={escrito}
            aria-invalid={errorBuscador !== undefined}
            aria-describedby={errorBuscador !== undefined ? idErrorBusqueda : undefined}
            onChange={(evento) => setEscrito(evento.target.value)}
          />
          {errorBuscador !== undefined && (
            <p id={idErrorBusqueda} className="campo__error" role="alert">
              {errorBuscador}
            </p>
          )}
        </div>
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
          <button type="button" disabled={buscando || resultados.pagina <= 1} onClick={() => irA(resultados.pagina - 1)}>
            Anterior
          </button>
          <span>{`Página ${resultados.pagina} de ${totalPaginas}`}</span>
          <button
            type="button"
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
