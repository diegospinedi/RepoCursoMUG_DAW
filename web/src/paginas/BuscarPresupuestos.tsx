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
  type ErrorDeCampo,
  type EstadoPresupuesto,
  type FiltroPresupuestos,
  type PaginaPresupuestos,
} from '../api/cliente';
import { mostrarImporte } from '../configuracion/numeros';
import { mostrarFecha, validarRangoFechas } from '../presupuestos/reglas';

export const MENSAJE_SIN_COINCIDENCIAS = 'No hay presupuestos que coincidan con la búsqueda.';

/** Largos de D-07: los mismos que valida la Api, medidos sobre el texto escrito. */
const LARGO_MAXIMO_APELLIDO = 100;
const LARGO_MAXIMO_NOMBRE = 100;
const LARGO_MAXIMO_DNI = 20;

const TEXTO_DEL_ESTADO: Record<EstadoPresupuesto, string> = { borrador: 'Borrador', final: 'Final' };

type CampoFiltro = keyof FiltroPresupuestos;
type ErroresFiltro = Partial<Record<CampoFiltro, string>>;

const CAMPOS_DEL_FILTRO: Record<string, CampoFiltro> = {
  [CAMPO_APELLIDO]: 'apellido',
  [CAMPO_NOMBRE]: 'nombre',
  [CAMPO_DNI]: 'dni',
  [CAMPO_DESDE]: 'desde',
  [CAMPO_HASTA]: 'hasta',
};

const FILTRO_VACIO: FiltroPresupuestos = { apellido: '', nombre: '', dni: '', desde: '', hasta: '' };

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

interface PropsCampo {
  etiqueta: string;
  nombre: CampoFiltro;
  valor: string;
  error: string | undefined;
  tipo: 'text' | 'date';
  largoMaximo?: number;
  alCambiar: (valor: string) => void;
}

/**
 * Un filtro con su etiqueta y su mensaje de error al lado, vinculado por `aria-describedby`, como los datos del cliente
 * del presupuesto. Sin autocompletado: el navegador no guarda en su historial los datos personales buscados.
 */
function CampoBusqueda({ etiqueta, nombre, valor, error, tipo, largoMaximo, alCambiar }: PropsCampo) {
  const id = useId();
  const idError = `${id}-error`;
  return (
    <div className="campo">
      <label htmlFor={id}>{etiqueta}</label>
      <input
        id={id}
        name={nombre}
        type={tipo}
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

/**
 * Búsqueda de presupuestos (FEAT-006, FR-01 a FR-10): filtra por fecha Desde y Hasta, Apellido, Nombre y DNI, combinados
 * con "Y", con el botón Buscar o con Enter (nunca en cada tecla ni al abrir). Muestra 50 resultados por página con
 * Anterior y Siguiente; el número abre el presupuesto. Desde posterior a Hasta se avisa junto a Desde sin llamar a la
 * Api (AC-07).
 *
 * Los filtros y los resultados viven solo en el estado del componente (R-02): ni en la URL ni en el almacenamiento del
 * navegador; la Api los recibe en el cuerpo (ADR-019). Paginar usa los filtros de la última búsqueda, no lo escrito
 * después. Mientras hay un pedido en curso no se envía otro: el botón y la paginación quedan deshabilitados, y una
 * guarda síncrona impide que dos envíos seguidos salgan antes de que React vuelva a dibujar. Sin sesión va a
 * `/ingreso` reemplazando la entrada.
 */
export function BuscarPresupuestos() {
  const navegar = useNavigate();
  const [escrito, setEscrito] = useState<FiltroPresupuestos>(FILTRO_VACIO);
  const [errores, setErrores] = useState<ErroresFiltro>({});
  const [general, setGeneral] = useState<string | null>(null);
  const [resultado, setResultado] = useState<PaginaPresupuestos | null>(null);
  const [aplicado, setAplicado] = useState<FiltroPresupuestos>(FILTRO_VACIO);
  const [buscando, setBuscando] = useState(false);
  const enCurso = useRef(false);

  const cambiar = (campo: CampoFiltro) => (valor: string) => setEscrito((anterior) => ({ ...anterior, [campo]: valor }));

  async function pedir(filtro: FiltroPresupuestos, pagina: number) {
    if (enCurso.current) {
      return;
    }
    enCurso.current = true;
    setBuscando(true);
    setGeneral(null);
    try {
      const respuesta = await buscarPresupuestos(filtro, pagina);
      switch (respuesta.tipo) {
        case 'ok':
          setErrores({});
          setAplicado(filtro);
          setResultado(respuesta.datos);
          break;
        case 'sinSesion':
          // El cliente ya avisó al manejador de App; se navega igual para no depender de ese registro (ver Arranque).
          navegar('/ingreso', { replace: true });
          break;
        case 'campos':
        case 'campo': {
          // Sin resultados que parezcan de esta búsqueda.
          setResultado(null);
          const { porCampo, sinCampo } = repartirErrores(
            respuesta.tipo === 'campos' ? respuesta.errores : [respuesta],
          );
          setErrores(porCampo);
          if (sinCampo) {
            setGeneral(MENSAJE_PEDIDO_RECHAZADO);
          }
          break;
        }
        default:
          // Red, 5xx, 413 o respuesta ilegible: la grilla anterior no se muestra como actual.
          setResultado(null);
          setErrores({});
          setGeneral(respuesta.mensaje);
          break;
      }
    } finally {
      enCurso.current = false;
      setBuscando(false);
    }
  }

  function alBuscar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const errorDesde = validarRangoFechas(escrito.desde, escrito.hasta);
    if (errorDesde !== undefined) {
      // Como ante un 400: la grilla anterior no se muestra como resultado de este rango ni se pagina con lo viejo.
      setResultado(null);
      setGeneral(null);
      setErrores({ desde: errorDesde });
      return;
    }
    void pedir(escrito, 1);
  }

  const irA = (pagina: number) => void pedir(aplicado, pagina);

  const totalPaginas =
    resultado === null ? 1 : Math.max(1, Math.ceil(resultado.total / resultado.tamanoPagina));

  return (
    <section className="pantalla pantalla--ancha">
      <h1>Buscar presupuestos</h1>
      <form role="search" onSubmit={alBuscar} noValidate>
        <CampoBusqueda
          etiqueta="Desde"
          nombre="desde"
          tipo="date"
          valor={escrito.desde}
          error={errores.desde}
          alCambiar={cambiar('desde')}
        />
        <CampoBusqueda
          etiqueta="Hasta"
          nombre="hasta"
          tipo="date"
          valor={escrito.hasta}
          error={errores.hasta}
          alCambiar={cambiar('hasta')}
        />
        <CampoBusqueda
          etiqueta="Apellido"
          nombre="apellido"
          tipo="text"
          largoMaximo={LARGO_MAXIMO_APELLIDO}
          valor={escrito.apellido}
          error={errores.apellido}
          alCambiar={cambiar('apellido')}
        />
        <CampoBusqueda
          etiqueta="Nombre"
          nombre="nombre"
          tipo="text"
          largoMaximo={LARGO_MAXIMO_NOMBRE}
          valor={escrito.nombre}
          error={errores.nombre}
          alCambiar={cambiar('nombre')}
        />
        <CampoBusqueda
          etiqueta="DNI"
          nombre="dni"
          tipo="text"
          largoMaximo={LARGO_MAXIMO_DNI}
          valor={escrito.dni}
          error={errores.dni}
          alCambiar={cambiar('dni')}
        />
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
          <button type="button" disabled={buscando || resultado.pagina <= 1} onClick={() => irA(resultado.pagina - 1)}>
            Anterior
          </button>
          <span>{`Página ${resultado.pagina} de ${totalPaginas}`}</span>
          <button
            type="button"
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
