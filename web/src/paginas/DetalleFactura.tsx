import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import {
  consultarFactura,
  descargarPdfFactura,
  MENSAJE_FACTURA_NO_ENCONTRADA,
  type FacturaDetalle,
} from '../api/cliente';
import { guardarPdf } from '../componentes/guardarPdf';
import { mostrarImporte } from '../configuracion/numeros';
import { mostrarFecha, numeroDeLaRuta } from '../presupuestos/reglas';

/** La leyenda de una factura que no se emitió en producción: su CAE no existe en ARCA (`AGENTS.md`, FR-14). */
export const LEYENDA_SIMULADA = 'COMPROBANTE SIMULADO — SIN VALIDEZ FISCAL';

type Carga = { tipo: 'cargando' } | { tipo: 'error'; mensaje: string } | { tipo: 'listo'; factura: FacturaDetalle };

/** El aviso de la descarga: el mensaje general, o el de los datos del emisor con el enlace a la configuración. */
type Aviso = { tipo: 'general'; mensaje: string } | { tipo: 'sinDatosEmisor'; mensaje: string };

/** La carga de un número de ruta antes de consultarlo: inválido ya es "no encontrada"; si no, se está cargando. */
const cargaInicial = (numero: number | null): Carga =>
  numero === null ? { tipo: 'error', mensaje: MENSAJE_FACTURA_NO_ENCONTRADA } : { tipo: 'cargando' };

/**
 * "Factura B 0003-00000125" como `factura-B-0003-00000125.pdf`, el mismo nombre que da la Api: solo el comprobante,
 * nunca datos del cliente.
 */
const nombreArchivo = (comprobante: string) => `factura-${comprobante.replace(/^Factura /, '').replace(' ', '-')}.pdf`;

/**
 * Detalle de una factura (FEAT-009, FR-24, AC-24): la de la ruta `/facturas/:numero`, donde `:numero` es el número del
 * presupuesto de origen (D-14, D-28). Muestra sus datos, el enlace al presupuesto y Descargar PDF (FR-05, AC-06); sin
 * los datos del emisor, el 409 `sin_datos_emisor` se avisa con un enlace a la configuración y no se descarga nada
 * (FR-04, AC-05). No tiene acciones de edición ni de eliminación: una factura con CAE no se modifica (RF-32).
 *
 * Los datos del cliente viven solo en el estado del componente: ni en la URL (más allá del número del presupuesto), ni
 * en el almacenamiento del navegador, ni en la consola; la URL de objeto del PDF se revoca al descargarlo. Una
 * respuesta que llega para un número que la ruta ya no muestra se descarta. Sin sesión va a `/ingreso`.
 */
export function DetalleFactura() {
  const navegar = useNavigate();
  const numero = numeroDeLaRuta(useParams().numero);
  const [carga, setCarga] = useState<Carga>(() => cargaInicial(numero));
  const [aviso, setAviso] = useState<Aviso | null>(null);
  // El número cuya descarga está en curso: la guarda y el estado del botón son de esa factura, no de la pantalla.
  const [numeroDescargando, setNumeroDescargando] = useState<number | null>(null);
  const descargaEnCurso = useRef<number | null>(null);
  const numeroVigente = useRef(numero);
  const descargando = numero !== null && numeroDescargando === numero;
  // Con otro número en la ruta, la pantalla vuelve a empezar en este mismo render (el efecto solo consulta la Api).
  const [numeroMostrado, setNumeroMostrado] = useState(numero);
  if (numero !== numeroMostrado) {
    setNumeroMostrado(numero);
    setCarga(cargaInicial(numero));
    setAviso(null);
  }

  // Al salir de la pantalla (o cambiar de número) ninguna respuesta pendiente le corresponde: un PDF que llega después no
  // se descarga y un 401 no navega desde otra pantalla.
  useEffect(() => {
    numeroVigente.current = numero;
    return () => {
      numeroVigente.current = null;
    };
  }, [numero]);

  useEffect(() => {
    if (numero === null) {
      return;
    }
    let vigente = true;
    async function cargar(numeroValido: number) {
      const resultado = await consultarFactura(numeroValido);
      if (!vigente) {
        return;
      }
      switch (resultado.tipo) {
        case 'ok':
          setCarga({ tipo: 'listo', factura: resultado.datos });
          break;
        case 'sinSesion':
          navegar('/ingreso', { replace: true });
          break;
        default:
          // 404, 5xx, red o respuesta ilegible: un mensaje general, sin datos de la factura.
          setCarga({ tipo: 'error', mensaje: resultado.mensaje });
          break;
      }
    }
    void cargar(numero);
    return () => {
      vigente = false;
    };
  }, [numero, navegar]);

  /**
   * Descarga el PDF: mientras se pide no se lanza otra descarga de la misma factura (el botón conserva el foco con
   * `aria-disabled`). La guarda es por número, como en el presupuesto: al pasar a otra factura su botón no queda
   * bloqueado por la descarga anterior.
   */
  async function descargarPdf(factura: FacturaDetalle) {
    const numeroDeLaFactura = factura.presupuesto;
    if (descargaEnCurso.current === numeroDeLaFactura) {
      return;
    }
    descargaEnCurso.current = numeroDeLaFactura;
    setNumeroDescargando(numeroDeLaFactura);
    setAviso(null);
    const resultado = await descargarPdfFactura(numeroDeLaFactura);
    // Si mientras tanto empezó la descarga de otro número, esa sigue en curso.
    if (descargaEnCurso.current === numeroDeLaFactura) {
      descargaEnCurso.current = null;
    }
    setNumeroDescargando((actual) => (actual === numeroDeLaFactura ? null : actual));
    // Si la ruta ya muestra otra factura, o se salió de la pantalla, la respuesta no le corresponde.
    if (numeroVigente.current !== numeroDeLaFactura) {
      return;
    }
    switch (resultado.tipo) {
      case 'ok':
        guardarPdf(resultado.datos, nombreArchivo(factura.comprobante));
        break;
      case 'sinSesion':
        navegar('/ingreso', { replace: true });
        break;
      case 'conflicto':
        // El único motivo que admite el cliente es `sin_datos_emisor` (FR-04): el texto propio, nunca el del servidor.
        setAviso({ tipo: 'sinDatosEmisor', mensaje: resultado.mensaje });
        break;
      default:
        setAviso({ tipo: 'general', mensaje: resultado.mensaje });
        break;
    }
  }

  return (
    <section className="pantalla pantalla--ancha">
      <h1>Factura</h1>
      {carga.tipo === 'cargando' && <p className="cargando">Cargando…</p>}
      {carga.tipo === 'error' && (
        <p className="mensaje-general" role="alert">
          {carga.mensaje}
        </p>
      )}
      {carga.tipo === 'listo' && (
        <>
          {carga.factura.simulada && <p className="aviso">{LEYENDA_SIMULADA}</p>}
          <DatosFactura factura={carga.factura} />
          {aviso !== null && (
            <p className="mensaje-general" role="alert">
              {aviso.mensaje}
              {aviso.tipo === 'sinDatosEmisor' && (
                <>
                  {' '}
                  <Link to="/configuracion">Ir a la configuración</Link>
                </>
              )}
            </p>
          )}
          <p>
            <button
              type="button"
              className="boton-secundario"
              aria-disabled={descargando || undefined}
              aria-busy={descargando || undefined}
              onClick={() => void descargarPdf(carga.factura)}
            >
              Descargar PDF
            </button>
          </p>
        </>
      )}
      <p>
        <Link to="/facturas">Buscar facturas</Link>
      </p>
      <p>
        <Link to="/">Volver al inicio</Link>
      </p>
    </section>
  );
}

function DatosFactura({ factura }: { factura: FacturaDetalle }) {
  const datos: [string, ReactNode][] = [
    ['Comprobante', factura.comprobante],
    ['Fecha', mostrarFecha(factura.fecha)],
    [
      'Presupuesto',
      <Link key="presupuesto" to={`/presupuestos/${factura.presupuesto}`} aria-label={`Presupuesto N.º ${factura.presupuesto}`}>
        {factura.presupuesto}
      </Link>,
    ],
    ['Apellido', factura.apellido],
    ['Nombre', factura.nombre],
    ['DNI', factura.dni],
    ['Receptor', factura.receptor],
    ['Total ($)', mostrarImporte(factura.total)],
    // FR-11, FR-12: el IVA contenido solo en la Factura B; la Api lo manda `null` en la C.
    ...(factura.ivaContenido === null
      ? []
      : [['IVA contenido ($)', mostrarImporte(factura.ivaContenido)] as [string, ReactNode]]),
    ['CAE', factura.cae],
    ['Vencimiento del CAE', mostrarFecha(factura.vencimientoCae)],
  ];
  return (
    <>
      <dl className="datos">
        {datos.map(([etiqueta, valor]) => (
          <div key={etiqueta}>
            <dt>{etiqueta}</dt>
            <dd>{valor}</dd>
          </div>
        ))}
      </dl>
      <div className="tabla-contenedor">
        <table className="tabla">
          <caption>Líneas de la factura</caption>
          <thead>
            <tr>
              <th scope="col">Descripción</th>
              <th scope="col">Cantidad</th>
              <th scope="col" className="tabla__importe">
                Importe ($)
              </th>
            </tr>
          </thead>
          <tbody>
            {factura.lineas.map((linea, indice) => (
              // Las líneas no tienen clave propia; su orden es el de la factura y no cambia.
              <tr key={indice}>
                <td>{linea.descripcion}</td>
                <td>{linea.cantidad}</td>
                <td className="tabla__importe">{mostrarImporte(linea.importe)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
