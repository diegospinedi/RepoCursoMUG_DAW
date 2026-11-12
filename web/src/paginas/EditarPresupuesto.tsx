import { Link } from 'react-router';
import {
  MENSAJE_PRESUPUESTO_FINAL,
  modificarPresupuesto,
  type DatosCliente,
  type LineaAGrabar,
  type PresupuestoGrabado,
} from '../api/cliente';
import { FormularioPresupuesto } from '../presupuestos/FormularioPresupuesto';
import { usePresupuestoDeLaRuta } from '../presupuestos/usePresupuestoDeLaRuta';

/**
 * Modificación de un presupuesto en Borrador (FEAT-003c, FR-02, FR-03): carga el presupuesto una vez al abrir y
 * muestra el formulario del alta ({@link FormularioPresupuesto}) con los datos del cliente y las líneas grabadas, cada
 * una con su Precio Unitario grabado (D-02) y su `orden`. Grabar envía la versión cargada (D-01) y vuelve al
 * presupuesto con la confirmación del grabado en Borrador (D-07). Un presupuesto en Final muestra
 * "El presupuesto está en Final y no se puede modificar." sin formulario (FR-05); un 409 `cambiado` o `final` muestra el
 * texto propio de su motivo, nunca el del servidor, y lo escrito sigue en pantalla. La carga es la de
 * {@link usePresupuestoDeLaRuta}: un número de ruta inválido o un 404 muestran "No se encontró el presupuesto."; sin
 * sesión va a `/ingreso` reemplazando la entrada.
 *
 * Datos personales (D-17): lo cargado y lo escrito viven solo en el estado de la pantalla; nada en la URL, en
 * `localStorage`, `sessionStorage` ni en la consola. No hay consultas periódicas.
 */
export function EditarPresupuesto() {
  const { numero, carga } = usePresupuestoDeLaRuta();

  return (
    <section className="pantalla pantalla--ancha">
      <h1>Modificar presupuesto</h1>
      {carga.tipo === 'cargando' && <p className="cargando">Cargando…</p>}
      {carga.tipo === 'error' && (
        <p className="mensaje-general" role="alert">
          {carga.mensaje}
        </p>
      )}
      {carga.tipo === 'listo' && <Edicion presupuesto={carga.presupuesto} />}
      {numero !== null && (
        <p>
          <Link to={`/presupuestos/${numero}`}>Volver al presupuesto</Link>
        </p>
      )}
    </section>
  );
}

/** El presupuesto cargado: en Final, solo el aviso (FR-05); en Borrador, el formulario con lo grabado. */
function Edicion({ presupuesto }: { presupuesto: PresupuestoGrabado }) {
  if (presupuesto.estado === 'final') {
    return (
      <p className="aviso" role="status">
        {MENSAJE_PRESUPUESTO_FINAL}
      </p>
    );
  }
  const { numero, version } = presupuesto;
  // La versión es la que devolvió el GET; la operadora nunca la escribe.
  const grabar = (cliente: DatosCliente, lineas: LineaAGrabar[]) => modificarPresupuesto(numero, cliente, lineas, version);
  return (
    <>
      <p>{`Presupuesto N.º ${numero}`}</p>
      <FormularioPresupuesto clienteInicial={presupuesto.cliente} lineasIniciales={presupuesto.lineas} grabar={grabar} />
    </>
  );
}
