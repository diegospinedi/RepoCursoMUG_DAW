import { useId, useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import {
  buscarPresupuestos,
  CAMPO_APELLIDO,
  CAMPO_DESDE,
  CAMPO_DNI,
  CAMPO_HASTA,
  CAMPO_NOMBRE,
  MENSAJE_PEDIDO_RECHAZADO,
  type Cancelado,
  type ErrorDeCampo,
  type EstadoPresupuesto,
  type FiltroPresupuestos,
  type PaginaPresupuestos,
  type PresupuestoListado,
  type ResultadoBusquedaPresupuestos,
} from '../api/cliente';
import { esUtf16Valido } from '../catalogo/reglas';
import { Campo } from '../componentes/Campo';
import { CampoBusqueda } from '../componentes/CampoBusqueda';
import { MINIMO_CARACTERES, useBusquedaEnVivo, type Decision } from '../componentes/useBusquedaEnVivo';
import { mostrarImporte } from '../configuracion/numeros';
import { mostrarFecha, validarRangoFechas } from '../presupuestos/reglas';

export const MENSAJE_SIN_COINCIDENCIAS = 'No hay presupuestos que coincidan con la búsqueda.';

const TEXTO_DEL_ESTADO: Record<EstadoPresupuesto, string> = { borrador: 'Borrador', final: 'Final' };

type NombreFiltro = keyof FiltroPresupuestos;
type ErroresFiltro = Partial<Record<NombreFiltro, string>>;

/** Los filtros de texto, que buscan mientras se escribe (FEAT-013); las fechas aplican con "Buscar" (D-07). */
type FiltroTexto = 'apellido' | 'nombre' | 'dni';
type Textos = Record<FiltroTexto, string>;

/** Los filtros de texto en el orden de la pantalla, con su etiqueta y el largo de D-07 (el mismo que valida la Api). */
const FILTROS_TEXTO: readonly { campo: FiltroTexto; etiqueta: string; largoMaximo: number }[] = [
  { campo: 'apellido', etiqueta: 'Apellido', largoMaximo: 100 },
  { campo: 'nombre', etiqueta: 'Nombre', largoMaximo: 100 },
  { campo: 'dni', etiqueta: 'DNI', largoMaximo: 20 },
];

const CAMPOS_DEL_FILTRO: Record<string, NombreFiltro> = {
  [CAMPO_APELLIDO]: 'apellido',
  [CAMPO_NOMBRE]: 'nombre',
  [CAMPO_DNI]: 'dni',
  [CAMPO_DESDE]: 'desde',
  [CAMPO_HASTA]: 'hasta',
};

const FILTRO_VACIO: FiltroPresupuestos = { apellido: '', nombre: '', dni: '', desde: '', hasta: '' };

const CANCELADO: Cancelado = { tipo: 'cancelado' };

/**
 * Un pedido de la búsqueda: si salió de la búsqueda en vivo o de un envío manual, el filtro y la página que se envían, y
 * si su respuesta da sugerencias.
 */
interface Pedido {
  tipo: 'vivo' | 'manual';
  filtro: FiltroPresupuestos;
  pagina: number;
  sugerir: boolean;
}

/** La clave de la búsqueda en vivo: solo los textos, así cambiar las fechas no la cambia (D-07). */
interface ClaveEnVivo extends Textos {
  tipo: 'vivo';
}

/** La clave de un envío manual ("Buscar", Enter o la paginación): el filtro y la página que se piden. */
interface ClaveManual {
  tipo: 'manual';
  filtro: FiltroPresupuestos;
  pagina: number;
}

/** Los textos recortados como los envía el cliente: la clave de la búsqueda en vivo (sin las fechas, D-07). */
const textosDe = (filtro: FiltroPresupuestos): Textos => ({
  apellido: filtro.apellido.trim(),
  nombre: filtro.nombre.trim(),
  dni: filtro.dni.trim(),
});

/**
 * Si la Api aceptaría el filtro (D-07 de FEAT-013): el rango de fechas y cada texto, con UTF-16 válido y dentro de su
 * largo. Lo que no, no se pide en vivo; con "Buscar" se muestra el error de hoy.
 */
const esBuscableEnVivo = (filtro: FiltroPresupuestos) =>
  validarRangoFechas(filtro.desde, filtro.hasta) === undefined &&
  FILTROS_TEXTO.every(({ campo, largoMaximo }) => {
    const texto = filtro[campo].trim();
    return esUtf16Valido(texto) && texto.length <= largoMaximo;
  });

/**
 * Si un texto recién editado puede dejar sugerencias a la vista (H1): con {@link MINIMO_CARACTERES} o más y con un
 * filtro que la Api aceptaría. Si no, las anteriores se vacían en el mismo evento, antes de la pausa.
 */
const daSugerencias = (texto: string, filtro: FiltroPresupuestos) =>
  texto.trim().length >= MINIMO_CARACTERES && esBuscableEnVivo(filtro);

const claveEnVivo = (filtro: FiltroPresupuestos) =>
  JSON.stringify({ tipo: 'vivo', ...textosDe(filtro) } satisfies ClaveEnVivo);

const claveManual = (filtro: FiltroPresupuestos, pagina: number) =>
  JSON.stringify({ tipo: 'manual', filtro, pagina } satisfies ClaveManual);

/**
 * Lo que se ve de un presupuesto sugerido (FR-05 de FEAT-013): número, apellido y nombre, y fecha. Sin el DNI (D-15,
 * R-02). Es texto: la lista lo dibuja con JSX.
 */
const mostrarSugerencia = (fila: PresupuestoListado) =>
  `${fila.numero} · ${fila.apellido}, ${fila.nombre} · ${mostrarFecha(fila.fecha)}`;

/**
 * Los errores de un 400 que tienen un campo en la pantalla, y si quedó alguno sin campo (por ejemplo, `pagina`): ese
 * se informa con el mensaje general fijo, sin repetir el texto del servidor fuera de su campo.
 */
function repartirErrores(errores: readonly ErrorDeCampo[]): { porCampo: ErroresFiltro; sinCampo: boolean } {
  const porCampo: ErroresFiltro = {};
  let sinCampo = false;
  for (const { campo, mensaje } of errores) {
    const propio = Object.hasOwn(CAMPOS_DEL_FILTRO, campo) ? CAMPOS_DEL_FILTRO[campo] : undefined;
    if (propio === undefined) {
      sinCampo = true;
    } else {
      porCampo[propio] ??= mensaje;
    }
  }
  return { porCampo, sinCampo };
}

interface PropsCampoFiltro {
  etiqueta: string;
  nombre: 'desde' | 'hasta';
  valor: string;
  error: string | undefined;
  alCambiar: (valor: string) => void;
}

/**
 * Una fecha del filtro con su etiqueta y su mensaje de error al lado, vinculado por `aria-describedby`, como los datos
 * del cliente del presupuesto. Los textos usan `CampoBusqueda` (D-12 de FEAT-013).
 */
function CampoFiltro({ etiqueta, nombre, valor, error, alCambiar }: PropsCampoFiltro) {
  const id = useId();
  const idError = `${id}-error`;
  return (
    <div className="campo">
      <label htmlFor={id}>{etiqueta}</label>
      <input
        id={id}
        name={nombre}
        type="date"
        autoComplete="off"
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

/**
 * Búsqueda de presupuestos (FEAT-006, FR-01 a FR-10): filtra por fecha Desde y Hasta, Apellido, Nombre y DNI, combinados
 * con "Y", con el botón Buscar o con Enter (nunca al abrir). Muestra 50 resultados por página con Anterior y Siguiente;
 * el número abre el presupuesto. Desde posterior a Hasta se avisa junto a Desde sin llamar a la Api (AC-07).
 *
 * Mientras se escribe (FEAT-013, D-07): tras una pausa, cuando el texto editado tiene 2 caracteres o más o queda vacío,
 * una sola búsqueda con todos los filtros (fechas incluidas) llena la tabla y hasta 8 sugerencias (número, cliente y
 * fecha, sin el DNI); elegir una abre el presupuesto. Cambiar las fechas no busca. Con un filtro que la Api rechazaría
 * no se pide nada en vivo, y un 400 en vivo solo oculta las sugerencias: el error aparece con "Buscar".
 *
 * Los filtros y los resultados viven solo en el estado del componente (R-02): ni en la URL ni en el almacenamiento del
 * navegador; la Api los recibe en el cuerpo (ADR-019). Paginar usa los filtros de la última respuesta aceptada, no lo
 * escrito después. Cada pedido lleva un número (D-08): uno nuevo cancela el anterior, y solo la respuesta del último se
 * muestra. "Buscar" y la paginación quedan deshabilitados solo durante un envío manual, contra el doble clic. Sin sesión
 * va a `/ingreso` reemplazando la entrada.
 */
export function BuscarPresupuestos() {
  const navegar = useNavigate();
  const [escrito, setEscrito] = useState<FiltroPresupuestos>(FILTRO_VACIO);
  const [errores, setErrores] = useState<ErroresFiltro>({});
  const [general, setGeneral] = useState<string | null>(null);
  const [resultado, setResultado] = useState<PaginaPresupuestos | null>(null);
  const [sugerencias, setSugerencias] = useState<readonly PresupuestoListado[]>([]);
  const [aplicado, setAplicado] = useState<FiltroPresupuestos>(FILTRO_VACIO);
  const [buscando, setBuscando] = useState(false);

  /** Los textos que vio la última decisión, para saber qué campo se editó. */
  const vistos = useRef<Textos>(textosDe(FILTRO_VACIO));
  /** Si la búsqueda en vivo programada da sugerencias: no cuando el campo editado quedó vacío. */
  const sugerirEnVivo = useRef(false);
  /**
   * El último pedido enviado. Invariante: `buscar` lo asigna de forma sincrónica, antes de esperar la respuesta, y el
   * hook entrega a `alResultado` solo la respuesta del último `buscar` (un pedido nuevo invalida y cancela el
   * anterior). Por eso, cuando `alResultado` corre, este es el pedido de esa respuesta y nunca es `null`.
   */
  const ultimoPedido = useRef<Pedido | null>(null);
  /** El número del último envío manual: solo su fin vuelve a habilitar "Buscar". */
  const manuales = useRef(0);

  const cambiar = (campo: NombreFiltro) => (valor: string) => setEscrito((anterior) => ({ ...anterior, [campo]: valor }));

  /**
   * Vacía las sugerencias a la vista y hace que la respuesta viva en curso, si la hay, no las reponga (D-22, D-23): el
   * hook no cancela ese pedido durante la pausa, y lo que se pidió ya no corresponde a lo escrito. La tabla sí se
   * actualiza con esa respuesta.
   */
  function invalidarSugerencias() {
    setSugerencias([]);
    if (ultimoPedido.current !== null) {
      ultimoPedido.current.sugerir = false;
    }
  }

  /**
   * Un texto editado. H1: si ya no da sugerencias (vacío, 1 carácter o un filtro que la Api rechazaría), las anteriores
   * se invalidan en este mismo evento: ni durante la pausa ni durante el pedido se ven o se eligen.
   */
  const cambiarTexto = (campo: FiltroTexto) => (valor: string) => {
    if (!daSugerencias(valor, { ...escrito, [campo]: valor })) {
      invalidarSugerencias();
    }
    cambiar(campo)(valor);
  };

  /** Una fecha editada. No dispara la búsqueda (D-07), pero un rango invertido invalida las sugerencias (D-22). */
  const cambiarFecha = (campo: 'desde' | 'hasta') => (valor: string) => {
    const nuevo = { ...escrito, [campo]: valor };
    if (validarRangoFechas(nuevo.desde, nuevo.hasta) !== undefined) {
      invalidarSugerencias();
    }
    cambiar(campo)(valor);
  };

  /*
   * `decidir` y `buscar` leen `escrito` de su cierre. Es el del dibujo actual: el hook guarda las funciones de cada
   * dibujo en un efecto declarado antes del que decide, y la pausa termina después de dibujos posteriores, así que
   * `buscar` ve las fechas del momento en que se pide.
   */

  /** D-07: busca si el campo editado tiene 2 caracteres o más o quedó vacío, y la Api aceptaría el filtro. */
  function decidir(clave: string): Decision {
    const textos = JSON.parse(clave) as ClaveEnVivo;
    const anteriores = vistos.current;
    vistos.current = textos;
    const editados = FILTROS_TEXTO.map(({ campo }) => campo).filter((campo) => textos[campo] !== anteriores[campo]);
    if (editados.some((campo) => textos[campo] !== '' && textos[campo].length < MINIMO_CARACTERES)) {
      return 'ninguno';
    }
    if (!esBuscableEnVivo(escrito)) {
      return 'ninguno';
    }
    sugerirEnVivo.current = editados.some((campo) => textos[campo] !== '');
    return 'buscar';
  }

  function buscar(clave: string, senal: AbortSignal): Promise<ResultadoBusquedaPresupuestos | Cancelado> {
    const leida = JSON.parse(clave) as ClaveEnVivo | ClaveManual;
    let pedido: Pedido;
    if (leida.tipo === 'manual') {
      pedido = { tipo: 'manual', filtro: leida.filtro, pagina: leida.pagina, sugerir: false };
    } else {
      // Las fechas pudieron cambiar durante la pausa: un rango inválido no se pide en vivo.
      if (!esBuscableEnVivo(escrito)) {
        setSugerencias([]);
        return Promise.resolve(CANCELADO);
      }
      pedido = { tipo: 'vivo', filtro: escrito, pagina: 1, sugerir: sugerirEnVivo.current };
    }
    ultimoPedido.current = pedido;
    return buscarPresupuestos(pedido.filtro, pedido.pagina, senal);
  }

  function alResultado(respuesta: ResultadoBusquedaPresupuestos) {
    // Nunca `null` aquí: ver el invariante de `ultimoPedido`.
    const pedido = ultimoPedido.current!;
    switch (respuesta.tipo) {
      case 'ok':
        setErrores({});
        setGeneral(null);
        setAplicado(pedido.filtro);
        setResultado(respuesta.datos);
        setSugerencias(pedido.sugerir ? respuesta.datos.presupuestos : []);
        break;
      case 'sinSesion':
        // El cliente ya avisó al manejador de App; se navega igual para no depender de ese registro (ver Arranque).
        navegar('/ingreso', { replace: true });
        break;
      case 'campos':
      case 'campo': {
        setSugerencias([]);
        if (pedido.tipo === 'vivo') {
          // D-07: un 400 en vivo solo oculta las sugerencias; el error del campo aparece con "Buscar".
          break;
        }
        // Sin resultados que parezcan de esta búsqueda.
        setResultado(null);
        const { porCampo, sinCampo } = repartirErrores(respuesta.tipo === 'campos' ? respuesta.errores : [respuesta]);
        setErrores(porCampo);
        if (sinCampo) {
          setGeneral(MENSAJE_PEDIDO_RECHAZADO);
        }
        break;
      }
      default:
        // Red, 5xx, 413 o respuesta ilegible: la grilla anterior no se muestra como actual.
        setResultado(null);
        setSugerencias([]);
        setErrores({});
        setGeneral(respuesta.mensaje);
        break;
    }
  }

  const { enviarAhora, cancelar } = useBusquedaEnVivo(claveEnVivo(escrito), buscar, alResultado, {
    decidir,
    alDescartar: () => setSugerencias([]),
  });

  /** Un envío manual ("Buscar", Enter o la paginación): cancela la pausa y el pedido en curso y no da sugerencias. */
  function enviar(filtro: FiltroPresupuestos, pagina: number) {
    const propio = ++manuales.current;
    setBuscando(true);
    setGeneral(null);
    setSugerencias([]);
    void enviarAhora(claveManual(filtro, pagina)).then(() => {
      if (propio === manuales.current) {
        setBuscando(false);
      }
    });
  }

  function alBuscar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const errorDesde = validarRangoFechas(escrito.desde, escrito.hasta);
    if (errorDesde !== undefined) {
      // Como ante un 400: la grilla anterior no se muestra como resultado de este rango ni se pagina con lo viejo, y
      // una búsqueda en vivo pendiente ya no llega.
      cancelar();
      setSugerencias([]);
      setResultado(null);
      setGeneral(null);
      setErrores({ desde: errorDesde });
      return;
    }
    enviar(escrito, 1);
  }

  const irA = (pagina: number) => enviar(aplicado, pagina);

  const abrirPresupuesto = (fila: PresupuestoListado) => navegar(`/presupuestos/${fila.numero}`);

  const totalPaginas =
    resultado === null ? 1 : Math.max(1, Math.ceil(resultado.total / resultado.tamanoPagina));

  return (
    <section className="pantalla pantalla--ancha">
      <h1>Buscar presupuestos</h1>
      <form role="search" onSubmit={alBuscar} noValidate>
        <CampoFiltro etiqueta="Desde" nombre="desde" valor={escrito.desde} error={errores.desde} alCambiar={cambiarFecha('desde')} />
        <CampoFiltro etiqueta="Hasta" nombre="hasta" valor={escrito.hasta} error={errores.hasta} alCambiar={cambiarFecha('hasta')} />
        {FILTROS_TEXTO.map(({ campo, etiqueta, largoMaximo }) => (
          <Campo key={campo} etiqueta={etiqueta} error={errores[campo]}>
            {(atributos) => (
              // Sin autocompletado (lo pone CampoBusqueda): el navegador no guarda los datos personales buscados.
              <CampoBusqueda
                {...atributos}
                name={campo}
                maxLength={largoMaximo}
                valor={escrito[campo]}
                alCambiar={cambiarTexto(campo)}
                sugerencias={sugerencias}
                clave={(fila) => fila.numero}
                mostrar={mostrarSugerencia}
                alElegir={abrirPresupuesto}
              />
            )}
          </Campo>
        ))}
        <button type="submit" disabled={buscando}>
          Buscar
        </button>
      </form>
      {general !== null && (
        <p className="mensaje-general" role="alert">
          {general}
        </p>
      )}
      {buscando && resultado === null && <p className="cargando">Cargando…</p>}
      {resultado !== null && resultado.presupuestos.length === 0 && <p>{MENSAJE_SIN_COINCIDENCIAS}</p>}
      {resultado !== null && resultado.presupuestos.length > 0 && (
        <div className="tabla-contenedor">
          <table className="tabla">
            <caption>Presupuestos encontrados</caption>
            <thead>
              <tr>
                <th scope="col">Número</th>
                <th scope="col">Fecha</th>
                <th scope="col">Apellido</th>
                <th scope="col">Nombre</th>
                <th scope="col">DNI</th>
                <th scope="col">Estado</th>
                <th scope="col" className="tabla__importe">
                  Total ($)
                </th>
              </tr>
            </thead>
            <tbody>
              {resultado.presupuestos.map((fila) => (
                <tr key={fila.numero}>
                  <td>
                    <Link to={`/presupuestos/${fila.numero}`} aria-label={`Abrir el presupuesto ${fila.numero}`}>
                      {fila.numero}
                    </Link>
                  </td>
                  <td>{mostrarFecha(fila.fecha)}</td>
                  <td>{fila.apellido}</td>
                  <td>{fila.nombre}</td>
                  <td>{fila.dni}</td>
                  <td>{TEXTO_DEL_ESTADO[fila.estado]}</td>
                  <td className="tabla__importe">{mostrarImporte(fila.total)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {/* La página que se muestra es la que devolvió la Api, no la pedida: mientras llega la otra no cambia. */}
      {resultado !== null && (resultado.total > 0 || resultado.pagina > 1) && (
        <nav className="paginacion" aria-label="Páginas de los resultados">
          <button
            type="button"
            className="boton-secundario"
            disabled={buscando || resultado.pagina <= 1}
            onClick={() => irA(resultado.pagina - 1)}
          >
            Anterior
          </button>
          <span>{`Página ${resultado.pagina} de ${totalPaginas}`}</span>
          <button
            type="button"
            className="boton-secundario"
            disabled={buscando || resultado.pagina >= totalPaginas}
            onClick={() => irA(resultado.pagina + 1)}
          >
            Siguiente
          </button>
        </nav>
      )}
      <p>
        <Link to="/">Volver al inicio</Link>
      </p>
    </section>
  );
}
