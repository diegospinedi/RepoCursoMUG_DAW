import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router';
import {
  consultarPresupuesto,
  MENSAJE_PRESUPUESTO_NO_ENCONTRADO,
  type EstadoPresupuesto,
  type PresupuestoGrabado,
} from '../api/cliente';
import { mostrarDecimal, mostrarImporte } from '../configuracion/numeros';

/** Lo que se muestra de un dato opcional que no se cargó (FR-03). */
export const SIN_DATO = '—';

const TEXTO_DEL_ESTADO: Record<EstadoPresupuesto, string> = { borrador: 'Borrador', final: 'Final' };

/**
 * El número de la ruta, si es un entero positivo escrito sin ceros a la izquierda y dentro de los enteros seguros; si
 * no, `null` (no puede existir y no se consulta).
 */
function numeroDeLaRuta(texto: string | undefined): number | null {
  if (texto === undefined || !/^[1-9][0-9]*$/.test(texto)) {
    return null;
  }
  const numero = Number(texto);
  return Number.isSafeInteger(numero) ? numero : null;
}

/** `yyyy-MM-dd` como dd/mm/aaaa (FR-15), desde el texto: sin `Date`, así no depende de la zona horaria. */
function mostrarFecha(fecha: string): string {
  const [anio, mes, dia] = fecha.split('-');
  return `${dia}/${mes}/${anio}`;
}

/** La confirmación que deja el alta en el estado de la navegación (D-24); cualquier otra cosa no se muestra. */
function confirmacionDe(estado: unknown): string | null {
  if (typeof estado !== 'object' || estado === null || !('mensaje' in estado)) {
    return null;
  }
  return typeof estado.mensaje === 'string' ? estado.mensaje : null;
}

type Carga = { tipo: 'cargando' } | { tipo: 'error'; mensaje: string } | { tipo: 'listo'; presupuesto: PresupuestoGrabado };

/**
 * Un presupuesto grabado, solo lectura (la edición es de FEAT-003c): número, fecha dd/mm/aaaa, estado, los seis datos
 * del cliente y las líneas con las siete columnas de FR-13 e importes con 2 decimales (NFR-02). Un número de ruta
 * inválido o un 404 muestran "No se encontró el presupuesto." (D-24); sin sesión va a `/ingreso` reemplazando la
 * entrada. Los datos del cliente viven solo en el estado de este componente (D-17); no hay consultas periódicas.
 */
export function Presupuesto() {
  const navegar = useNavigate();
  const confirmacion = confirmacionDe(useLocation().state);
  const numero = numeroDeLaRuta(useParams().numero);
  const [carga, setCarga] = useState<Carga>(
    numero === null ? { tipo: 'error', mensaje: MENSAJE_PRESUPUESTO_NO_ENCONTRADO } : { tipo: 'cargando' },
  );

  useEffect(() => {
    if (numero === null) {
      setCarga({ tipo: 'error', mensaje: MENSAJE_PRESUPUESTO_NO_ENCONTRADO });
      return;
    }
    setCarga({ tipo: 'cargando' });
    let vigente = true;
    async function cargar(numeroValido: number) {
      const resultado = await consultarPresupuesto(numeroValido);
      if (!vigente) {
        return;
      }
      switch (resultado.tipo) {
        case 'ok':
          setCarga({ tipo: 'listo', presupuesto: resultado.datos });
          break;
        case 'sinSesion':
          // El cliente ya avisó al manejador de App; se navega igual para no depender de ese registro (ver Arranque).
          navegar('/ingreso', { replace: true });
          break;
        default:
          setCarga({ tipo: 'error', mensaje: resultado.mensaje });
          break;
      }
    }
    void cargar(numero);
    return () => {
      vigente = false;
    };
  }, [numero, navegar]);

  return (
    <section className="pantalla pantalla--ancha">
      <h1>Presupuesto</h1>
      {confirmacion !== null && carga.tipo === 'listo' && (
        <p className="mensaje-exito" role="status">
          {confirmacion}
        </p>
      )}
      {carga.tipo === 'cargando' && <p className="cargando">Cargando…</p>}
      {carga.tipo === 'error' && (
        <p className="mensaje-general" role="alert">
          {carga.mensaje}
        </p>
      )}
      {carga.tipo === 'listo' && <DetallePresupuesto presupuesto={carga.presupuesto} />}
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
        </table>
      </div>
    </>
  );
}
