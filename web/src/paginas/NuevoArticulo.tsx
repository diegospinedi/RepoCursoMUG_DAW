import { useEffect, useId, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import {
  CAMPO_CODIGO_PROVEEDOR,
  CAMPO_DESCRIPCION,
  CAMPO_MARGEN_UTILIDAD,
  CAMPO_PRECIO_COSTO,
  consultarSesion,
  grabarArticulo,
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
import { mostrarImporte } from '../configuracion/numeros';

const VACIOS: TextosArticulo = { codigoProveedor: '', descripcion: '', precioCosto: '', margenUtilidad: '' };

const CAMPOS: readonly (keyof TextosArticulo)[] = [
  CAMPO_CODIGO_PROVEEDOR,
  CAMPO_DESCRIPCION,
  CAMPO_PRECIO_COSTO,
  CAMPO_MARGEN_UTILIDAD,
];

const esCampo = (campo: string): campo is keyof TextosArticulo => CAMPOS.some((c) => c === campo);

/** El mensaje que confirma el alta, con el código que asignó el sistema (FR-01). */
const mensajeGrabado = (codigo: number) => `Artículo grabado con el código ${codigo}.`;

interface PropsCampo {
  etiqueta: string;
  nombre: keyof TextosArticulo;
  valor: string;
  error: string | undefined;
  largoMaximo: number;
  numerico: boolean;
  alCambiar: (valor: string) => void;
}

/** Un campo editable con su etiqueta y su mensaje de error al lado, vinculado por `aria-describedby`. */
function CampoArticulo({ etiqueta, nombre, valor, error, largoMaximo, numerico, alCambiar }: PropsCampo) {
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

/** El Precio de Venta: lo calcula el sistema y la operadora no lo escribe (FR-11). */
function CampoPrecioVenta({ valor }: { valor: string }) {
  const id = useId();
  return (
    <div className="campo">
      <label htmlFor={id}>Precio de venta ($)</label>
      <input id={id} type="text" readOnly value={valor} />
    </div>
  );
}

/**
 * Alta de un artículo (FR-01 a FR-05, FR-11). Verifica la sesión una sola vez al montarse; sin sesión va a `/ingreso`
 * reemplazando la entrada. Valida en el navegador con las reglas del dominio y no envía si hay errores; los del
 * servidor van junto al campo que indica la Api. Al grabar muestra el código asignado, el Precio de Venta y, si la
 * hay, la advertencia; los campos quedan vacíos para otra alta. Los valores viven solo en el estado de este
 * componente; no hay consultas periódicas.
 */
export function NuevoArticulo() {
  const navegar = useNavigate();
  const [verificando, setVerificando] = useState(true);
  const [textos, setTextos] = useState<TextosArticulo>(VACIOS);
  const [errores, setErrores] = useState<ErroresArticulo>({});
  const [general, setGeneral] = useState<string | null>(null);
  const [grabado, setGrabado] = useState<ArticuloGrabado | null>(null);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    let vigente = true;
    async function verificar() {
      const sesion = await consultarSesion();
      if (!vigente) {
        return;
      }
      if (sesion.tipo === 'sinSesion') {
        // El cliente ya avisó al manejador de App; se navega igual para no depender de ese registro (ver Arranque).
        navegar('/ingreso', { replace: true });
        return;
      }
      if (sesion.tipo !== 'ok') {
        setGeneral(sesion.mensaje);
      }
      setVerificando(false);
    }
    void verificar();
    return () => {
      vigente = false;
    };
  }, [navegar]);

  function cambiar(campo: keyof TextosArticulo, valor: string) {
    setTextos((anteriores) => ({ ...anteriores, [campo]: valor }));
    // D-21: al empezar otra alta se quita el resultado de la anterior (mensaje, Precio de Venta y advertencia).
    setGrabado(null);
  }

  async function alEnviar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    setGeneral(null);
    setGrabado(null);
    const validacion = validarArticulo(textos);
    if (validacion.tipo === 'errores') {
      setErrores(validacion.errores);
      return;
    }
    setErrores({});

    setEnviando(true);
    const resultado = await grabarArticulo(validacion.datos);
    setEnviando(false);
    switch (resultado.tipo) {
      case 'ok':
        setGrabado(resultado.datos);
        setTextos(VACIOS);
        break;
      case 'campos':
      case 'campo': {
        const recibidos = resultado.tipo === 'campos' ? resultado.errores : [resultado];
        const delFormulario: ErroresArticulo = {};
        const otros: string[] = [];
        for (const { campo, mensaje } of recibidos) {
          if (esCampo(campo)) {
            delFormulario[campo] = mensaje;
          } else {
            otros.push(mensaje);
          }
        }
        setErrores(delFormulario);
        setGeneral(otros.length > 0 ? otros.join(' ') : null);
        break;
      }
      case 'conflicto':
      case 'general':
      case 'bloqueado':
        setGeneral(resultado.mensaje);
        break;
      case 'sinSesion':
        navegar('/ingreso', { replace: true });
        break;
    }
  }

  if (verificando) {
    return <p className="cargando">Cargando…</p>;
  }

  return (
    <section className="pantalla">
      <h1>Nuevo artículo</h1>
      <form method="post" onSubmit={alEnviar} noValidate>
        <CampoArticulo
          etiqueta="Código en el proveedor"
          nombre={CAMPO_CODIGO_PROVEEDOR}
          valor={textos.codigoProveedor}
          error={errores.codigoProveedor}
          largoMaximo={LARGO_MAXIMO_CODIGO}
          numerico={false}
          alCambiar={(valor) => cambiar(CAMPO_CODIGO_PROVEEDOR, valor)}
        />
        <CampoArticulo
          etiqueta="Descripción"
          nombre={CAMPO_DESCRIPCION}
          valor={textos.descripcion}
          error={errores.descripcion}
          largoMaximo={LARGO_MAXIMO_DESCRIPCION}
          numerico={false}
          alCambiar={(valor) => cambiar(CAMPO_DESCRIPCION, valor)}
        />
        <CampoArticulo
          etiqueta="Precio de costo ($)"
          nombre={CAMPO_PRECIO_COSTO}
          valor={textos.precioCosto}
          error={errores.precioCosto}
          largoMaximo={LARGO_MAXIMO_NUMERO}
          numerico
          alCambiar={(valor) => cambiar(CAMPO_PRECIO_COSTO, valor)}
        />
        <CampoArticulo
          etiqueta="Margen de utilidad (%)"
          nombre={CAMPO_MARGEN_UTILIDAD}
          valor={textos.margenUtilidad}
          error={errores.margenUtilidad}
          largoMaximo={LARGO_MAXIMO_NUMERO}
          numerico
          alCambiar={(valor) => cambiar(CAMPO_MARGEN_UTILIDAD, valor)}
        />
        <CampoPrecioVenta valor={grabado === null ? '' : mostrarImporte(grabado.precioVenta)} />
        {general !== null && (
          <p className="mensaje-general" role="alert">
            {general}
          </p>
        )}
        {grabado !== null && (
          <p className="mensaje-exito" role="status">
            {mensajeGrabado(grabado.codigo)}
          </p>
        )}
        {grabado?.advertencia != null && (
          <p className="aviso" role="status">
            {grabado.advertencia}
          </p>
        )}
        <button type="submit" disabled={enviando}>
          Grabar
        </button>
      </form>
      <p>
        <Link to="/">Volver al inicio</Link>
      </p>
    </section>
  );
}
