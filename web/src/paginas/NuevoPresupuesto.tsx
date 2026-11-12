import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { consultarSesion, grabarPresupuesto } from '../api/cliente';
import { FormularioPresupuesto } from '../presupuestos/FormularioPresupuesto';

/**
 * Alta de un presupuesto (FR-01 a FR-14 de FEAT-003a, FR-01 a FR-08 de FEAT-003b): el formulario del presupuesto
 * ({@link FormularioPresupuesto}) vacío, que graba con {@link grabarPresupuesto}. Verifica la sesión una sola vez al
 * montarse; sin sesión va a `/ingreso` reemplazando la entrada; con otro error lo muestra y deja el formulario.
 *
 * Datos personales (D-17): viven solo en el estado del formulario; nada en la URL, en `localStorage`,
 * `sessionStorage` ni en la consola. No hay consultas periódicas.
 */
export function NuevoPresupuesto() {
  const navegar = useNavigate();
  const [verificando, setVerificando] = useState(true);
  const [errorDeSesion, setErrorDeSesion] = useState<string | null>(null);

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
        setErrorDeSesion(sesion.mensaje);
      }
      setVerificando(false);
    }
    void verificar();
    return () => {
      vigente = false;
    };
  }, [navegar]);

  if (verificando) {
    return <p className="cargando">Cargando…</p>;
  }

  return (
    <section className="pantalla pantalla--ancha">
      <h1>Nuevo presupuesto</h1>
      <FormularioPresupuesto grabar={grabarPresupuesto} mensajeInicial={errorDeSesion} />
      <p>
        <Link to="/">Volver al inicio</Link>
      </p>
    </section>
  );
}
