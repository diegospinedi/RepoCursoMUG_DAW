import { useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import {
  buscarFacturas,
  CAMPO_APELLIDO,
  CAMPO_DESDE,
  CAMPO_DNI,
  CAMPO_HASTA,
  CAMPO_NOMBRE,
  CAMPO_NUMERO_COMPROBANTE,
  MENSAJE_PEDIDO_RECHAZADO,
  type ErrorDeCampo,
  type FiltroFacturas,
  type PaginaFacturas,
} from '../api/cliente';
import { Campo } from '../componentes/Campo';
import { mostrarImporte } from '../configuracion/numeros';
import { mostrarFecha, validarRangoFechas } from '../presupuestos/reglas';

export const MENSAJE_SIN_FACTURAS = 'No hay facturas que coincidan con la búsqueda.';

/** El largo máximo de cada texto de la búsqueda: el mismo que valida la Api (`BuscarFacturas.LargoMaximoTexto`). */
const LARGO_MAXIMO_TEXTO = 100;

type CampoFiltro = keyof FiltroFacturas;
type ErroresFiltro = Partial<Record<CampoFiltro, string>>;

const CAMPOS_DEL_FILTRO: Record<string, CampoFiltro> = {
  [CAMPO_APELLIDO]: 'apellido',
  [CAMPO_NOMBRE]: 'nombre',
  [CAMPO_DNI]: 'dni',
  [CAMPO_NUMERO_COMPROBANTE]: 'numero',
  [CAMPO_DESDE]: 'desde',
  [CAMPO_HASTA]: 'hasta',
};

const FILTRO_VACIO: FiltroFacturas = { apellido: '', nombre: '', dni: '', numero: '', desde: '', hasta: '' };

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

/** Los filtros en el orden de la pantalla, con su etiqueta y su tipo de campo. */
const FILTROS: readonly { campo: CampoFiltro; etiqueta: string; tipo: 'text' | 'date' }[] = [
  { campo: 'desde', etiqueta: 'Desde', tipo: 'date' },
  { campo: 'hasta', etiqueta: 'Hasta', tipo: 'date' },
  { campo: 'numero', etiqueta: 'Número de comprobante', tipo: 'text' },
  { campo: 'apellido', etiqueta: 'Apellido', tipo: 'text' },
  { campo: 'nombre', etiqueta: 'Nombre', tipo: 'text' },
  { campo: 'dni', etiqueta: 'DNI', tipo: 'text' },
];

/**
 * Búsqueda de facturas (FEAT-009, FR-16 a FR-23): filtra por Apellido, Nombre, DNI, número de comprobante y fecha
 * Desde y Hasta, combinados con "Y", con el botón Buscar o con Enter (nunca en cada tecla ni al abrir). Muestra 50
 * resultados por página con Anterior y Siguiente; cada resultado lleva el comprobante como texto (D-15) y lo abre por
 * el número del presupuesto (`/facturas/:numero`, D-14, D-28). Desde posterior a Hasta se avisa junto a Desde sin
 * llamar a la Api (AC-22, D-42).
 *
 * Los filtros y los resultados viven solo en el estado del componente: ni en la URL ni en el almacenamiento del
 * navegador; la Api los recibe en el cuerpo (ADR-019). Paginar usa los filtros de la última búsqueda, no lo escrito
 * después. Mientras hay un pedido en curso no se envía otro. Sin sesión va a `/ingreso` reemplazando la entrada.
 */
export function BuscarFacturas() {
  const navegar = useNavigate();
  const [escrito, setEscrito] = useState<FiltroFacturas>(FILTRO_VACIO);
  const [errores, setErrores] = useState<ErroresFiltro>({});
  const [general, setGeneral] = useState<string | null>(null);
  const [resultado, setResultado] = useState<PaginaFacturas | null>(null);
  const [aplicado, setAplicado] = useState<FiltroFacturas>(FILTRO_VACIO);
  const [buscando, setBuscando] = useState(false);
  const enCurso = useRef(false);

  const cambiar = (campo: CampoFiltro) => (valor: string) => setEscrito((anterior) => ({ ...anterior, [campo]: valor }));

  async function pedir(filtro: FiltroFacturas, pagina: number) {
    if (enCurso.current) {
      return;
    }
    enCurso.current = true;
    setBuscando(true);
    setGeneral(null);
    try {
      const respuesta = await buscarFacturas(filtro, pagina);
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
      // FR-21, AC-22: no se busca; la grilla anterior no se muestra como resultado de este rango.
      setResultado(null);
      setGeneral(null);
      setErrores({ desde: errorDesde });
      return;
    }
    void pedir(escrito, 1);
  }

  const irA = (pagina: number) => void pedir(aplicado, pagina);

  const totalPaginas = resultado === null ? 1 : Math.max(1, Math.ceil(resultado.total / resultado.tamanoPagina));

  return (
    <section className="pantalla pantalla--ancha">
      <h1>Buscar facturas</h1>
      <form role="search" onSubmit={alBuscar} noValidate>
        {FILTROS.map(({ campo, etiqueta, tipo }) => (
          <Campo key={campo} etiqueta={etiqueta} error={errores[campo]}>
            {(atributos) => (
              // Sin autocompletado: el navegador no guarda en su historial los datos personales buscados.
              <input
                {...atributos}
                name={campo}
                type={tipo}
                autoComplete="off"
                maxLength={tipo === 'text' ? LARGO_MAXIMO_TEXTO : undefined}
                value={escrito[campo]}
                onChange={(evento) => cambiar(campo)(evento.target.value)}
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
      {resultado !== null && resultado.facturas.length === 0 && <p>{MENSAJE_SIN_FACTURAS}</p>}
      {resultado !== null && resultado.facturas.length > 0 && (
        <div className="tabla-contenedor">
          <table className="tabla">
            <caption>Facturas encontradas</caption>
            <thead>
              <tr>
                {/* D-15: el comprobante como un solo texto, sin columna aparte para el punto de venta. */}
                <th scope="col">Comprobante</th>
                <th scope="col">Fecha</th>
                <th scope="col">Apellido</th>
                <th scope="col">Nombre</th>
                <th scope="col">DNI</th>
                <th scope="col" className="tabla__importe">
                  Total ($)
                </th>
                <th scope="col">Presupuesto</th>
              </tr>
            </thead>
            <tbody>
              {resultado.facturas.map((fila) => (
                <tr key={fila.presupuesto}>
                  <td>
                    <Link to={`/facturas/${fila.presupuesto}`} aria-label={`Abrir la ${fila.comprobante}`}>
                      {fila.comprobante}
                    </Link>
                  </td>
                  <td>{mostrarFecha(fila.fecha)}</td>
                  <td>{fila.apellido}</td>
                  <td>{fila.nombre}</td>
                  <td>{fila.dni}</td>
                  <td className="tabla__importe">{mostrarImporte(fila.total)}</td>
                  <td>{fila.presupuesto}</td>
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
