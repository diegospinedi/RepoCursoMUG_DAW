import { useEffect, useId, useRef, useState, type KeyboardEvent, type SyntheticEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router';
import {
  descargarPdfPresupuesto,
  pasarPresupuestoAFinal,
  type EstadoPresupuesto,
  type PresupuestoGrabado,
} from '../api/cliente';
import { mostrarDecimal, mostrarImporte } from '../configuracion/numeros';
import { mensajePasadoAFinal, mostrarFecha, TEXTO_CONFIRMAR_FINAL } from '../presupuestos/reglas';
import { usePresupuestoDeLaRuta } from '../presupuestos/usePresupuestoDeLaRuta';

/** Lo que se muestra de un dato opcional que no se cargó (FR-03). */
export const SIN_DATO = '—';

const TEXTO_DEL_ESTADO: Record<EstadoPresupuesto, string> = { borrador: 'Borrador', final: 'Final' };

/**
 * Descarga el PDF recibido como `presupuesto-{numero}.pdf` (D-10): una URL de objeto, un enlace temporal con
 * `download`, su clic y la revocación de la URL. El nombre lleva solo el número, nunca datos del cliente (R-02).
 */
function guardarPdf(contenido: Blob, numero: number) {
  const url = URL.createObjectURL(contenido);
  const enlace = document.createElement('a');
  enlace.href = url;
  enlace.download = `presupuesto-${numero}.pdf`;
  enlace.hidden = true;
  document.body.append(enlace);
  try {
    enlace.click();
  } finally {
    enlace.remove();
    URL.revokeObjectURL(url);
  }
}

/** La confirmación que deja el alta en el estado de la navegación (D-24); cualquier otra cosa no se muestra. */
function confirmacionDe(estado: unknown): string | null {
  if (typeof estado !== 'object' || estado === null || !('mensaje' in estado)) {
    return null;
  }
  return typeof estado.mensaje === 'string' ? estado.mensaje : null;
}

/**
 * El resultado del paso a Final que se muestra arriba del presupuesto, con el número al que corresponde: con otro
 * número en la ruta no se muestra.
 */
interface Aviso {
  numero: number;
  tipo: 'exito' | 'error';
  texto: string;
}

/** Adónde vuelve el foco al cerrar el diálogo: al botón que lo abrió o, si ya no está (pasó a Final), al aviso. */
type DestinoDelFoco = 'boton' | 'aviso' | null;

/**
 * Un presupuesto grabado, en solo lectura: número, fecha dd/mm/aaaa, estado, los seis datos del cliente, las líneas
 * con las siete columnas de FR-13 y el total con IVA incluido (FEAT-003b FR-08), con los importes que devolvió el
 * servidor, sin recalcularlos (ADR-016), y 2 decimales (NFR-02). En Borrador ofrece "Modificar" (la pantalla de
 * modificación) y "Pasar a Final", que pide confirmación en un diálogo (FEAT-003c, FR-04): confirmar envía la versión
 * mostrada (D-01) y muestra "Presupuesto N.º {n} pasado a Final."; cancelar, Escape o cerrar no llaman a la Api. En
 * Final no hay acciones de edición (FR-05). "Descargar PDF" (FEAT-006, FR-11, FR-12) está habilitado solo en Final y
 * descarga `presupuesto-{numero}.pdf` (D-10). Un 409 muestra el texto propio de su motivo, nunca el del servidor (D-07),
 * y vuelve a leer el presupuesto para mostrar su estado y su versión reales. Una respuesta que llega cuando la ruta ya
 * muestra otro número se descarta. La carga es la de {@link usePresupuestoDeLaRuta}: un número de ruta inválido o un
 * 404 muestran "No se encontró el presupuesto." (D-24) y sin sesión va a `/ingreso`. Los datos del cliente viven solo
 * en el estado de la pantalla (D-17); no hay consultas periódicas.
 */
export function Presupuesto() {
  const navegar = useNavigate();
  const confirmacion = confirmacionDe(useLocation().state);
  const { numero, carga, mostrar, recargar, sigueVigente } = usePresupuestoDeLaRuta();
  const [avisoGuardado, setAviso] = useState<Aviso | null>(null);
  /** El número cuyo paso a Final se está confirmando en el diálogo, o `null`. */
  const [confirmandoNumero, setConfirmandoNumero] = useState<number | null>(null);
  // Con otro número en la ruta, el aviso y el diálogo del anterior se borran en este mismo render (como la carga en
  // `usePresupuestoDeLaRuta`): así no reaparecen al volver a ese número con atrás o adelante.
  const [numeroMostrado, setNumeroMostrado] = useState(numero);
  if (numero !== numeroMostrado) {
    setNumeroMostrado(numero);
    setAviso(null);
    setConfirmandoNumero(null);
  }
  const [enviando, setEnviando] = useState(false);
  const envioEnCurso = useRef(false);
  /**
   * El número cuyo PDF se está pidiendo, o `null`. Con otro número en la ruta el botón del presupuesto nuevo no queda
   * bloqueado: `descargando` se deriva del número mostrado, como `aviso`.
   */
  const [numeroDescargando, setNumeroDescargando] = useState<number | null>(null);
  const descargaEnCurso = useRef<number | null>(null);
  const descargando = numeroDescargando !== null && numeroDescargando === numero;
  const refBotonFinal = useRef<HTMLButtonElement>(null);
  const refAviso = useRef<HTMLParagraphElement>(null);
  const destinoDelFoco = useRef<DestinoDelFoco>(null);
  // Además, una respuesta que llega tarde no los muestra para otro número (ver `sigueVigente`).
  const aviso = avisoGuardado !== null && avisoGuardado.numero === numero ? avisoGuardado : null;
  const confirmando = confirmandoNumero !== null && confirmandoNumero === numero;

  /** Al cerrarse el diálogo (ya fuera del DOM, así el foco no queda atrapado en él), el foco vuelve adonde corresponde. */
  useEffect(() => {
    if (confirmando) {
      return;
    }
    const destino = destinoDelFoco.current;
    destinoDelFoco.current = null;
    if (destino === 'boton') {
      refBotonFinal.current?.focus();
    } else if (destino === 'aviso') {
      refAviso.current?.focus();
    }
  }, [confirmando]);

  function cancelarFinal() {
    destinoDelFoco.current = 'boton';
    setConfirmandoNumero(null);
  }

  async function confirmarFinal(presupuesto: PresupuestoGrabado) {
    // Dos clics seguidos antes de que React vuelva a dibujar no envían dos pedidos.
    if (envioEnCurso.current) {
      return;
    }
    envioEnCurso.current = true;
    setEnviando(true);
    const resultado = await pasarPresupuestoAFinal(presupuesto.numero, presupuesto.version);
    envioEnCurso.current = false;
    setEnviando(false);
    // Como el efecto de carga: si la ruta ya muestra otro número, esta respuesta no le corresponde a la pantalla.
    if (!sigueVigente(presupuesto.numero)) {
      // Tampoco reaparece el diálogo si la persona vuelve a ese número.
      setConfirmandoNumero(null);
      return;
    }
    switch (resultado.tipo) {
      case 'ok':
        mostrar(resultado.datos);
        setAviso({ numero: presupuesto.numero, tipo: 'exito', texto: mensajePasadoAFinal(resultado.datos.numero) });
        // En Final el botón ya no está: el foco va al aviso, que anuncia el resultado.
        destinoDelFoco.current = 'aviso';
        break;
      case 'sinSesion':
        navegar('/ingreso', { replace: true });
        return;
      case 'conflicto':
        // El texto propio de su motivo (D-07), nunca el del servidor. Otra sesión lo modificó o lo pasó a Final: se
        // vuelve a leer para mostrar el estado y la versión reales (si quedó en Final, sin acciones de edición). El
        // botón puede desaparecer: el foco va al mensaje.
        setAviso({ numero: presupuesto.numero, tipo: 'error', texto: resultado.mensaje });
        destinoDelFoco.current = 'aviso';
        recargar();
        break;
      default:
        setAviso({ numero: presupuesto.numero, tipo: 'error', texto: resultado.mensaje });
        destinoDelFoco.current = 'boton';
        break;
    }
    setConfirmandoNumero(null);
  }

  /**
   * Descarga el PDF (FEAT-006, FR-11): mientras se pide no se lanza otra descarga. Un 409 `borrador` (la pantalla quedó
   * vieja, D-11) muestra su texto propio y vuelve a leer el presupuesto; sin sesión va a `/ingreso`; cualquier otra
   * falla muestra el mensaje fijo del cliente y el botón vuelve a habilitarse.
   */
  async function descargarPdf(numeroDelPresupuesto: number) {
    // Mientras se descarga el botón sigue habilitado (`aria-disabled`, para no perder el foco): esta guarda frena los
    // clics siguientes del mismo presupuesto.
    if (descargaEnCurso.current === numeroDelPresupuesto) {
      return;
    }
    descargaEnCurso.current = numeroDelPresupuesto;
    setNumeroDescargando(numeroDelPresupuesto);
    setAviso(null);
    const resultado = await descargarPdfPresupuesto(numeroDelPresupuesto);
    // Si mientras tanto empezó la descarga de otro número, esa sigue en curso.
    if (descargaEnCurso.current === numeroDelPresupuesto) {
      descargaEnCurso.current = null;
    }
    setNumeroDescargando((actual) => (actual === numeroDelPresupuesto ? null : actual));
    // Si la ruta ya muestra otro número, la respuesta no le corresponde: ni se descarga ni se muestra.
    if (!sigueVigente(numeroDelPresupuesto)) {
      return;
    }
    switch (resultado.tipo) {
      case 'ok':
        guardarPdf(resultado.datos, numeroDelPresupuesto);
        break;
      case 'sinSesion':
        navegar('/ingreso', { replace: true });
        break;
      case 'conflicto':
        // El texto propio de su motivo, nunca el del servidor; el mensaje es una alerta y se anuncia solo.
        setAviso({ numero: numeroDelPresupuesto, tipo: 'error', texto: resultado.mensaje });
        recargar();
        break;
      default:
        setAviso({ numero: numeroDelPresupuesto, tipo: 'error', texto: resultado.mensaje });
        break;
    }
  }

  return (
    <section className="pantalla pantalla--ancha">
      <h1>Presupuesto</h1>
      {confirmacion !== null && carga.tipo === 'listo' && aviso === null && (
        <p className="mensaje-exito" role="status">
          {confirmacion}
        </p>
      )}
      {aviso !== null && (
        <p
          ref={refAviso}
          className={aviso.tipo === 'exito' ? 'mensaje-exito' : 'mensaje-general'}
          role={aviso.tipo === 'exito' ? 'status' : 'alert'}
          tabIndex={-1}
        >
          {aviso.texto}
        </p>
      )}
      {carga.tipo === 'cargando' && <p className="cargando">Cargando…</p>}
      {carga.tipo === 'error' && (
        <p className="mensaje-general" role="alert">
          {carga.mensaje}
        </p>
      )}
      {carga.tipo === 'listo' && <DetallePresupuesto presupuesto={carga.presupuesto} />}
      {carga.tipo === 'listo' && carga.presupuesto.estado === 'borrador' && (
        <div>
          <p>
            <Link to={`/presupuestos/${carga.presupuesto.numero}/editar`}>Modificar</Link>
          </p>
          <button
            ref={refBotonFinal}
            type="button"
            disabled={enviando}
            onClick={() => {
              setAviso(null);
              setConfirmandoNumero(carga.presupuesto.numero);
            }}
          >
            Pasar a Final
          </button>
          {confirmando && (
            <DialogoConfirmarFinal
              enviando={enviando}
              alConfirmar={() => void confirmarFinal(carga.presupuesto)}
              alCancelar={cancelarFinal}
            />
          )}
        </div>
      )}
      {carga.tipo === 'listo' && (
        <p>
          {/*
            FR-12, AC-12: en Borrador está deshabilitado (`disabled`); la Api tampoco genera el PDF de un Borrador (409).
            Mientras descarga no se usa `disabled`, que le quitaría el foco: queda `aria-disabled` y `aria-busy`, y la
            guarda de `descargarPdf` frena otro clic.
          */}
          <button
            type="button"
            disabled={carga.presupuesto.estado !== 'final'}
            aria-disabled={descargando || undefined}
            aria-busy={descargando || undefined}
            onClick={() => void descargarPdf(carga.presupuesto.numero)}
          >
            Descargar PDF
          </button>
        </p>
      )}
      <p>
        <Link to="/presupuestos/nuevo">Nuevo presupuesto</Link>
      </p>
      <p>
        <Link to="/">Volver al inicio</Link>
      </p>
    </section>
  );
}

function DetallePresupuesto({ presupuesto }: { presupuesto: PresupuestoGrabado }) {
  const { cliente } = presupuesto;
  const datos: [string, string][] = [
    ['Número', String(presupuesto.numero)],
    ['Fecha', mostrarFecha(presupuesto.fecha)],
    ['Estado', TEXTO_DEL_ESTADO[presupuesto.estado]],
    ['Apellido', cliente.apellido],
    ['Nombre', cliente.nombre],
    ['DNI', cliente.dni],
    ['Domicilio', cliente.domicilio ?? SIN_DATO],
    ['Email', cliente.email ?? SIN_DATO],
    ['Nro. de Teléfono', cliente.telefono ?? SIN_DATO],
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
          <caption>Líneas del presupuesto</caption>
          <thead>
            <tr>
              <th scope="col">Código de Artículo</th>
              <th scope="col">Descripción</th>
              <th scope="col" className="tabla__importe">
                Precio Unitario ($)
              </th>
              <th scope="col">Cantidad</th>
              <th scope="col">Porcentaje de Descuento (%)</th>
              <th scope="col" className="tabla__importe">
                Precio con Descuento ($)
              </th>
              <th scope="col" className="tabla__importe">
                Precio Final ($)
              </th>
            </tr>
          </thead>
          <tbody>
            {presupuesto.lineas.map((linea, indice) => (
              // El mismo artículo puede repetirse (D-16): la clave es la posición, que no cambia en solo lectura.
              <tr key={indice}>
                <td>{linea.codigoArticulo}</td>
                <td>{linea.descripcion}</td>
                <td className="tabla__importe">{mostrarImporte(linea.precioUnitario)}</td>
                <td>{linea.cantidad}</td>
                <td>{mostrarDecimal(linea.porcentajeDescuento)}</td>
                <td className="tabla__importe">{mostrarImporte(linea.precioConDescuento)}</td>
                <td className="tabla__importe">{mostrarImporte(linea.precioFinal)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            {/* FR-08: el total que devolvió el servidor (ADR-016, no se recalcula), con IVA incluido y sin discriminarlo. */}
            <tr>
              <th scope="row" colSpan={6}>
                Total (IVA incluido)
              </th>
              <td className="tabla__importe">{mostrarImporte(presupuesto.total)}</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </>
  );
}

interface PropsDialogo {
  enviando: boolean;
  alConfirmar: () => void;
  alCancelar: () => void;
}

/**
 * El diálogo propio de confirmación del paso a Final (FR-04, D-07): un `<dialog>` modal con el título visible
 * "Pasar a Final" (su nombre accesible), el texto "Una vez en Final, el presupuesto no se puede modificar." (su
 * descripción) y los botones "Pasar a Final" y "Cancelar". Al abrirse el foco va a "Cancelar" (la acción no se puede
 * deshacer, así que Enter por accidente no la confirma). Escape (el evento `cancel` del navegador o la tecla) y
 * "Cancelar" lo cierran sin llamar a la Api; mientras se envía no se cierra y el foco queda dentro.
 * Se monta solo mientras está abierto: quien lo usa devuelve el foco al cerrarlo.
 */
function DialogoConfirmarFinal({ enviando, alConfirmar, alCancelar }: PropsDialogo) {
  const idTitulo = useId();
  const idTexto = useId();
  const refDialogo = useRef<HTMLDialogElement>(null);
  const refCancelar = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const dialogo = refDialogo.current;
    if (dialogo === null) {
      return;
    }
    // El navegador lo abre modal (el resto de la página queda inerte); sin `showModal` (jsdom), se abre igual.
    if (typeof dialogo.showModal === 'function') {
      if (!dialogo.open) {
        dialogo.showModal();
      }
    } else {
      dialogo.setAttribute('open', '');
    }
    refCancelar.current?.focus();
    return () => {
      if (typeof dialogo.close === 'function') {
        dialogo.close();
      } else {
        dialogo.removeAttribute('open');
      }
    };
  }, []);

  function cancelar(evento: SyntheticEvent) {
    // El navegador cerraría el diálogo por su cuenta: lo cierra la pantalla, que también devuelve el foco.
    evento.preventDefault();
    if (!enviando) {
      alCancelar();
    }
  }

  function alPulsarTecla(evento: KeyboardEvent<HTMLDialogElement>) {
    if (evento.key === 'Escape') {
      cancelar(evento);
    }
  }

  return (
    <dialog
      ref={refDialogo}
      aria-labelledby={idTitulo}
      aria-describedby={idTexto}
      onCancel={cancelar}
      onKeyDown={alPulsarTecla}
    >
      <h2 id={idTitulo}>Pasar a Final</h2>
      <p id={idTexto}>{TEXTO_CONFIRMAR_FINAL}</p>
      {/*
        Mientras se envía los botones no se deshabilitan con `disabled`: el que tiene el foco lo perdería y el foco
        caería al body, fuera del diálogo. Quedan `aria-disabled` y no hacen nada.
      */}
      <button type="button" aria-disabled={enviando} onClick={() => !enviando && alConfirmar()}>
        Pasar a Final
      </button>
      <button ref={refCancelar} type="button" aria-disabled={enviando} onClick={() => !enviando && alCancelar()}>
        Cancelar
      </button>
    </dialog>
  );
}
