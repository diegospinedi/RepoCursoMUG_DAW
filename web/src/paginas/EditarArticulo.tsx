import { useCallback, useEffect, useId, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import {
  CAMPO_CODIGO_PROVEEDOR,
  CAMPO_DESCRIPCION,
  CAMPO_MARGEN_UTILIDAD,
  CAMPO_PRECIO_COSTO,
  cambiarEstadoArticulo,
  consultarArticulo,
  CAMPO_VERSION,
  esCodigoArticulo,
  MENSAJE_ARTICULO_CAMBIADO,
  MENSAJE_ARTICULO_INACTIVO,
  MENSAJE_ARTICULO_NO_ENCONTRADO,
  modificarArticulo,
  type ArticuloGrabado,
} from '../api/cliente';
import {
  LARGO_MAXIMO_CODIGO,
  LARGO_MAXIMO_DESCRIPCION,
  LARGO_MAXIMO_NUMERO,
  validarArticulo,
  type ErroresArticulo,
  type TextosArticulo,
} from '../catalogo/reglas';
import { mostrarDecimal, mostrarImporte } from '../configuracion/numeros';

export const MENSAJE_ARTICULO_GRABADO = 'Artículo grabado.';

const CAMPOS: readonly (keyof TextosArticulo)[] = [
  CAMPO_CODIGO_PROVEEDOR,
  CAMPO_DESCRIPCION,
  CAMPO_PRECIO_COSTO,
  CAMPO_MARGEN_UTILIDAD,
];

const esCampo = (campo: string): campo is keyof TextosArticulo => CAMPOS.some((c) => c === campo);

/**
 * El código de la ruta, si es un entero positivo escrito sin ceros a la izquierda y dentro de los enteros seguros;
 * si no, `null` (no puede existir y no se consulta).
 */
function codigoDeLaRuta(texto: string | undefined): number | null {
  if (texto === undefined || !/^[1-9][0-9]*$/.test(texto)) {
    return null;
  }
  const codigo = Number(texto);
  return esCodigoArticulo(codigo) ? codigo : null;
}

/** Los valores guardados, como los escribe la operadora (coma decimal). */
function textosDe(articulo: ArticuloGrabado): TextosArticulo {
  return {
    codigoProveedor: articulo.codigoProveedor,
    descripcion: articulo.descripcion,
    precioCosto: mostrarDecimal(articulo.precioCosto),
    margenUtilidad: mostrarDecimal(articulo.margenUtilidad),
  };
}

interface PropsCampo {
  etiqueta: string;
  nombre: keyof TextosArticulo;
  valor: string;
  error: string | undefined;
  largoMaximo: number;
  numerico: boolean;
  soloLectura: boolean;
  alCambiar: (valor: string) => void;
}

/**
 * Un campo con su etiqueta y su mensaje de error al lado, como en el alta; de solo lectura si el artículo está inactivo.
 * Duplica a propósito el de `NuevoArticulo.tsx` (con la variante de solo lectura), igual que {@link CampoPrecioVenta}:
 * decisión aceptada en la revisión de Block 5 de FEAT-002b; extraerlos queda para otro ticket.
 */
function CampoArticulo({ etiqueta, nombre, valor, error, largoMaximo, numerico, soloLectura, alCambiar }: PropsCampo) {
  const id = useId();
  const idError = `${id}-error`;
  return (
    <div className="campo">
      <label htmlFor={id}>{etiqueta}</label>
      <input
        id={id}
        name={nombre}
        type="text"
        inputMode={numerico ? 'decimal' : undefined}
        autoComplete="off"
        maxLength={largoMaximo}
        readOnly={soloLectura}
        value={valor}
        aria-invalid={error !== undefined}
        aria-describedby={error !== undefined ? idError : undefined}
        onChange={(evento) => alCambiar(evento.target.value)}
      />
      {error !== undefined && (
        <p id={idError} className="campo__error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

/** El Precio de Venta: siempre de solo lectura, lo calcula el sistema (AC-01). */
function CampoPrecioVenta({ valor }: { valor: string }) {
  const id = useId();
  return (
    <div className="campo">
      <label htmlFor={id}>Precio de venta ($)</label>
      <input id={id} type="text" readOnly value={valor} />
    </div>
  );
}

type Carga = { tipo: 'cargando' } | { tipo: 'error'; mensaje: string } | { tipo: 'listo'; articulo: ArticuloGrabado };

/** La carga de un código de ruta antes de consultarlo: inválido ya es "no encontrado"; si no, se está cargando. */
function cargaInicial(codigo: number | null): Carga {
  return codigo === null ? { tipo: 'error', mensaje: MENSAJE_ARTICULO_NO_ENCONTRADO } : { tipo: 'cargando' };
}

/**
 * Edición de un artículo (FR-01 a FR-03, D-02, D-08). Carga el artículo una vez al abrir y otra solo si la operadora
 * pide volver a abrirlo. El Precio de Venta es de solo lectura; Grabar envía los cuatro datos con la versión leída y
 * muestra el precio nuevo. Un artículo inactivo se muestra en solo lectura con el botón Reactivar, que devuelve la
 * versión nueva (D-26). Un 409 muestra el texto propio de su motivo arriba del formulario (D-19), nunca el del
 * servidor. Los valores viven solo en el estado del componente; no hay consultas periódicas.
 */
export function EditarArticulo() {
  const navegar = useNavigate();
  const codigo = codigoDeLaRuta(useParams().codigo);
  const [carga, setCarga] = useState<Carga>(() => cargaInicial(codigo));
  // Al abrir otro código, el formulario con la versión anterior deja de estar disponible en este mismo render (el
  // efecto solo consulta la Api); "Volver a abrir" lo hace desde su propio manejador.
  const [codigoMostrado, setCodigoMostrado] = useState(codigo);
  if (codigo !== codigoMostrado) {
    setCodigoMostrado(codigo);
    setCarga(cargaInicial(codigo));
  }
  const [aperturas, setAperturas] = useState(0);
  const [textos, setTextos] = useState<TextosArticulo | null>(null);
  const [errores, setErrores] = useState<ErroresArticulo>({});
  const [general, setGeneral] = useState<string | null>(null);
  const [conflicto, setConflicto] = useState<string | null>(null);
  const [grabado, setGrabado] = useState(false);
  const [enviando, setEnviando] = useState(false);

  /** Muestra el artículo tal como quedó guardado, sin mensajes de una operación anterior. */
  const mostrar = useCallback((articulo: ArticuloGrabado) => {
    setCarga({ tipo: 'listo', articulo });
    setTextos(textosDe(articulo));
    setErrores({});
    setGeneral(null);
    setConflicto(null);
  }, []);

  useEffect(() => {
    if (codigo === null) {
      return;
    }
    let vigente = true;
    async function cargar(codigoValido: number) {
      const resultado = await consultarArticulo(codigoValido);
      if (!vigente) {
        return;
      }
      if (resultado.tipo === 'sinSesion') {
        // El cliente ya avisó al manejador de App; se navega igual para no depender de ese registro (ver Arranque).
        navegar('/ingreso', { replace: true });
        return;
      }
      if (resultado.tipo === 'ok') {
        mostrar(resultado.datos);
      } else {
        setCarga({ tipo: 'error', mensaje: resultado.mensaje });
      }
    }
    void cargar(codigo);
    return () => {
      vigente = false;
    };
  }, [codigo, aperturas, navegar, mostrar]);

  function volverAAbrir() {
    setGrabado(false);
    setConflicto(null);
    setGeneral(null);
    setCarga({ tipo: 'cargando' });
    setAperturas((anteriores) => anteriores + 1);
  }

  function cambiar(campo: keyof TextosArticulo, valor: string) {
    setTextos((anteriores) => (anteriores === null ? anteriores : { ...anteriores, [campo]: valor }));
    // Como en el alta (D-21 de FEAT-002a): al seguir editando se quita el resultado de la grabación anterior.
    setGrabado(false);
  }

  async function alEnviar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    if (carga.tipo !== 'listo' || textos === null || !carga.articulo.activo) {
      return;
    }
    setGeneral(null);
    setConflicto(null);
    setGrabado(false);
    const validacion = validarArticulo(textos);
    if (validacion.tipo === 'errores') {
      setErrores(validacion.errores);
      return;
    }
    setErrores({});

    setEnviando(true);
    const resultado = await modificarArticulo(carga.articulo.codigo, validacion.datos, carga.articulo.version);
    setEnviando(false);
    switch (resultado.tipo) {
      case 'ok':
        mostrar(resultado.datos);
        setGrabado(true);
        break;
      case 'campos':
      case 'campo': {
        const recibidos = resultado.tipo === 'campos' ? resultado.errores : [resultado];
        const delFormulario: ErroresArticulo = {};
        const otros: string[] = [];
        const deVersion: string[] = [];
        for (const { campo, mensaje } of recibidos) {
          if (esCampo(campo)) {
            delFormulario[campo] = mensaje;
          } else if (campo === CAMPO_VERSION) {
            deVersion.push(mensaje);
          } else {
            otros.push(mensaje);
          }
        }
        setErrores(delFormulario);
        setGeneral(otros.length > 0 ? otros.join(' ') : null);
        // La versión no es un campo del formulario: se corrige volviendo a abrir el artículo.
        setConflicto(deVersion.length > 0 ? deVersion.join(' ') : null);
        break;
      }
      case 'conflicto':
        setConflicto(resultado.mensaje);
        break;
      case 'general':
      case 'bloqueado':
        setGeneral(resultado.mensaje);
        break;
      case 'sinSesion':
        navegar('/ingreso', { replace: true });
        break;
    }
  }

  async function reactivar(articulo: ArticuloGrabado) {
    setGeneral(null);
    setEnviando(true);
    // En la edición, un 409 lleva el texto de la edición y se resuelve volviendo a abrir, no recargando el listado.
    const resultado = await cambiarEstadoArticulo(articulo.codigo, true, MENSAJE_ARTICULO_CAMBIADO);
    setEnviando(false);
    if (resultado.tipo === 'ok') {
      // D-26: la respuesta trae la versión nueva; la grabación siguiente la envía y no da 409 por este cambio.
      mostrar(resultado.datos);
    } else if (resultado.tipo === 'sinSesion') {
      navegar('/ingreso', { replace: true });
    } else if (resultado.tipo === 'conflicto') {
      setConflicto(resultado.mensaje);
    } else {
      setGeneral(resultado.mensaje);
    }
  }

  return (
    <section className="pantalla">
      <h1>Editar artículo</h1>
      {carga.tipo === 'cargando' && <p className="cargando">Cargando…</p>}
      {carga.tipo === 'error' && (
        <p className="mensaje-general" role="alert">
          {carga.mensaje}
        </p>
      )}
      {carga.tipo === 'listo' && textos !== null && (
        <>
          <p>{`Código ${carga.articulo.codigo}`}</p>
          {conflicto !== null && (
            <div>
              <p className="mensaje-general" role="alert">
                {conflicto}
              </p>
              <button type="button" onClick={volverAAbrir}>
                Volver a abrir
              </button>
            </div>
          )}
          {!carga.articulo.activo && (
            <div>
              <p className="aviso" role="status">
                {MENSAJE_ARTICULO_INACTIVO}
              </p>
              <button type="button" disabled={enviando} onClick={() => void reactivar(carga.articulo)}>
                Reactivar
              </button>
            </div>
          )}
          <form method="post" onSubmit={alEnviar} noValidate>
            <CampoArticulo
              etiqueta="Código en el proveedor"
              nombre={CAMPO_CODIGO_PROVEEDOR}
              valor={textos.codigoProveedor}
              error={errores.codigoProveedor}
              largoMaximo={LARGO_MAXIMO_CODIGO}
              numerico={false}
              soloLectura={!carga.articulo.activo}
              alCambiar={(valor) => cambiar(CAMPO_CODIGO_PROVEEDOR, valor)}
            />
            <CampoArticulo
              etiqueta="Descripción"
              nombre={CAMPO_DESCRIPCION}
              valor={textos.descripcion}
              error={errores.descripcion}
              largoMaximo={LARGO_MAXIMO_DESCRIPCION}
              numerico={false}
              soloLectura={!carga.articulo.activo}
              alCambiar={(valor) => cambiar(CAMPO_DESCRIPCION, valor)}
            />
            <CampoArticulo
              etiqueta="Precio de costo ($)"
              nombre={CAMPO_PRECIO_COSTO}
              valor={textos.precioCosto}
              error={errores.precioCosto}
              largoMaximo={LARGO_MAXIMO_NUMERO}
              numerico
              soloLectura={!carga.articulo.activo}
              alCambiar={(valor) => cambiar(CAMPO_PRECIO_COSTO, valor)}
            />
            <CampoArticulo
              etiqueta="Margen de utilidad (%)"
              nombre={CAMPO_MARGEN_UTILIDAD}
              valor={textos.margenUtilidad}
              error={errores.margenUtilidad}
              largoMaximo={LARGO_MAXIMO_NUMERO}
              numerico
              soloLectura={!carga.articulo.activo}
              alCambiar={(valor) => cambiar(CAMPO_MARGEN_UTILIDAD, valor)}
            />
            <CampoPrecioVenta valor={mostrarImporte(carga.articulo.precioVenta)} />
            {general !== null && (
              <p className="mensaje-general" role="alert">
                {general}
              </p>
            )}
            {grabado && (
              <p className="mensaje-exito" role="status">
                {MENSAJE_ARTICULO_GRABADO}
              </p>
            )}
            {grabado && carga.articulo.advertencia !== null && (
              <p className="aviso" role="status">
                {carga.articulo.advertencia}
              </p>
            )}
            {carga.articulo.activo && (
              <button type="submit" disabled={enviando}>
                Grabar
              </button>
            )}
          </form>
        </>
      )}
      <p>
        <Link to="/articulos">Volver al listado</Link>
      </p>
    </section>
  );
}
