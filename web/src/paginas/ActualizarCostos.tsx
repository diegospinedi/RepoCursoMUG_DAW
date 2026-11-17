import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import { actualizarCostosPorPlanilla, consultarSesion, type ResultadoPlanilla } from '../api/cliente';
import { mostrarImporte } from '../configuracion/numeros';

/** La ayuda junto al campo: Excel quita los ceros a la izquierda de un código escrito como número. */
export const AYUDA_COLUMNA_A = 'La columna A conviene con formato Texto para no perder ceros a la izquierda.';

const TITULO = 'Actualizar costos';

/** Filas visibles por tabla (D-26): una planilla de 50.000 filas congelaría la pantalla. */
export const FILAS_VISIBLES = 500;

/** El aviso de una tabla recortada a {@link FILAS_VISIBLES} (D-26). */
const avisoDelTope = (total: number) =>
  `Se muestran ${FILAS_VISIBLES} de ${total} filas. Corregí la planilla y volvé a cargarla.`;

/** El aviso debajo de una tabla con más de {@link FILAS_VISIBLES} filas; nada si entran todas. */
function AvisoDelTope({ total }: { total: number }) {
  return total > FILAS_VISIBLES ? <p className="aviso">{avisoDelTope(total)}</p> : null;
}

/** El resumen de la carga (FR-12). */
const mensajeResultado = ({ aplicadas, filasLeidas }: ResultadoPlanilla) =>
  `Se actualizaron ${aplicadas} artículos de ${filasLeidas} filas.`;

/**
 * Actualización de los costos del catálogo con una planilla .xlsx (FEAT-011). Verifica la sesión al montarse; sin
 * sesión va a `/ingreso`. La operadora elige un archivo y lo envía: el botón queda deshabilitado sin archivo y
 * mientras se procesa ("Procesando…"). Al terminar muestra cuántos artículos se actualizaron, las filas no aplicadas
 * con su razón y las aplicadas para revisar, hasta {@link FILAS_VISIBLES} por tabla (D-26). Cualquier rechazo (planilla de más de 10 MB, no .xlsx, formato, límites,
 * conflicto, otra carga en curso, red) es el aviso de error, y la operadora puede reintentar.
 *
 * Los códigos vienen de la planilla de un proveedor: se muestran solo por JSX, como texto (R-08). El archivo y el
 * resultado viven solo en el estado de este componente, nunca en la URL ni en el almacenamiento del navegador.
 */
export function ActualizarCostos() {
  const navegar = useNavigate();
  const idTitulo = useId();
  const idCampo = useId();
  const idAyuda = useId();
  const idError = useId();
  const [verificando, setVerificando] = useState(true);
  const [archivo, setArchivo] = useState<File | null>(null);
  const [procesando, setProcesando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  /** El error es un rechazo del archivo (400 con `errors.planilla`): el campo queda marcado inválido. */
  const [errorDelArchivo, setErrorDelArchivo] = useState(false);
  const [resultado, setResultado] = useState<ResultadoPlanilla | null>(null);
  /**
   * Un pedido en curso: corta un segundo envío del mismo lote de React, antes de que `procesando` llegue a pintarse
   * (mismo patrón que BuscarFacturas).
   */
  const enCurso = useRef(false);
  /**
   * Si la pantalla sigue montada: la respuesta puede llegar después de que la operadora salió, y entonces no cambia
   * el estado ni navega. Se enciende en el efecto para que el doble montaje de StrictMode la vuelva a encender.
   */
  const montada = useRef(false);

  useEffect(() => {
    montada.current = true;
    return () => {
      montada.current = false;
    };
  }, []);

  useEffect(() => {
    let vigente = true;
    async function verificar() {
      const sesion = await consultarSesion();
      if (!vigente) {
        return;
      }
      if (sesion.tipo === 'sinSesion') {
        navegar('/ingreso', { replace: true });
        return;
      }
      if (sesion.tipo !== 'ok') {
        setError(sesion.mensaje);
      }
      setVerificando(false);
    }
    void verificar();
    return () => {
      vigente = false;
    };
  }, [navegar]);

  function elegir(elegido: File | null) {
    setArchivo(elegido);
    // Otro archivo: el resultado y el error de la carga anterior ya no corresponden.
    setResultado(null);
    setError(null);
    setErrorDelArchivo(false);
  }

  async function alEnviar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    if (archivo === null || enCurso.current) {
      return;
    }
    enCurso.current = true;
    setError(null);
    setErrorDelArchivo(false);
    setResultado(null);
    setProcesando(true);
    try {
      const respuesta = await actualizarCostosPorPlanilla(archivo);
      if (!montada.current) {
        return;
      }
      switch (respuesta.tipo) {
        case 'ok':
          setResultado(respuesta.datos);
          break;
        case 'sinSesion':
          navegar('/ingreso', { replace: true });
          break;
        case 'campo':
          // 400 con errors.planilla (no .xlsx, formato, límites): el error es del archivo elegido.
          setError(respuesta.mensaje);
          setErrorDelArchivo(true);
          break;
        default:
          // 409, 413, 429, red, resultado ilegible y archivo de más de 10 MB: el texto ya viene armado.
          setError(respuesta.mensaje);
          break;
      }
    } finally {
      enCurso.current = false;
      if (montada.current) {
        setProcesando(false);
      }
    }
  }

  if (verificando) {
    return <p className="cargando">Cargando…</p>;
  }

  return (
    <section className="pantalla pantalla--ancha">
      <h1 id={idTitulo}>{TITULO}</h1>
      <form method="post" aria-labelledby={idTitulo} aria-busy={procesando} onSubmit={alEnviar} noValidate>
        <div className="campo">
          <label htmlFor={idCampo}>Planilla de costos (.xlsx)</label>
          <input
            id={idCampo}
            name="planilla"
            type="file"
            accept=".xlsx"
            aria-invalid={errorDelArchivo}
            aria-describedby={errorDelArchivo ? `${idAyuda} ${idError}` : idAyuda}
            disabled={procesando}
            onChange={(evento) => elegir(evento.target.files?.[0] ?? null)}
          />
          <p id={idAyuda}>{AYUDA_COLUMNA_A}</p>
        </div>
        {error !== null && (
          <p id={idError} className="mensaje-general" role="alert">
            {error}
          </p>
        )}
        <button type="submit" disabled={archivo === null || procesando}>
          {procesando ? 'Procesando…' : TITULO}
        </button>
      </form>
      {resultado !== null && (
        <>
          <p className="mensaje-exito" role="status">
            {mensajeResultado(resultado)}
          </p>
          {resultado.noAplicadas.length > 0 && (
            <div className="tabla-contenedor">
              <table className="tabla">
                <caption>Filas no aplicadas</caption>
                <thead>
                  <tr>
                    <th scope="col">Fila</th>
                    <th scope="col">Código</th>
                    <th scope="col">Razón</th>
                  </tr>
                </thead>
                <tbody>
                  {resultado.noAplicadas.slice(0, FILAS_VISIBLES).map((fila) => (
                    <tr key={fila.fila}>
                      <td>{fila.fila}</td>
                      <td>{fila.codigo}</td>
                      <td>{fila.razon}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <AvisoDelTope total={resultado.noAplicadas.length} />
            </div>
          )}
          {resultado.revisar.length > 0 && (
            <div className="tabla-contenedor">
              <table className="tabla">
                <caption>Para revisar</caption>
                <thead>
                  <tr>
                    <th scope="col">Fila</th>
                    <th scope="col">Código</th>
                    <th scope="col" className="tabla__importe">
                      Precio de costo ($)
                    </th>
                    <th scope="col">Indicaciones</th>
                  </tr>
                </thead>
                <tbody>
                  {resultado.revisar.slice(0, FILAS_VISIBLES).map((fila) => (
                    <tr key={fila.fila}>
                      <td>{fila.fila}</td>
                      <td>{fila.codigo}</td>
                      <td className="tabla__importe">{mostrarImporte(fila.precioCosto)}</td>
                      <td>{fila.indicaciones.join(', ')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <AvisoDelTope total={resultado.revisar.length} />
            </div>
          )}
        </>
      )}
      <p>
        <Link to="/articulos">Volver a artículos</Link>
      </p>
    </section>
  );
}
