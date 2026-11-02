import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { consultarEstado, consultarSesion, type Resultado } from '../api/cliente';
import { Inicio } from '../paginas/Inicio';

type Pantalla = { tipo: 'cargando' } | { tipo: 'inicio' } | { tipo: 'error'; mensaje: string };

/**
 * Decide la pantalla inicial: sin contraseña, la definición (FR-02); con contraseña, el inicio si hay sesión y el
 * ingreso si no (FR-01). Consulta una vez al montarse y otra solo si la persona pide reintentar.
 */
export function Arranque() {
  const navegar = useNavigate();
  const [pantalla, setPantalla] = useState<Pantalla>({ tipo: 'cargando' });
  const [intento, setIntento] = useState(0);

  useEffect(() => {
    let vigente = true;
    async function decidir() {
      const estado = await consultarEstado();
      if (!vigente) {
        return;
      }
      if (estado.tipo !== 'ok') {
        noDisponible(estado);
        return;
      }
      if (!estado.datos.contrasenaDefinida) {
        navegar('/definir-contrasena', { replace: true });
        return;
      }

      const sesion = await consultarSesion();
      if (!vigente) {
        return;
      }
      if (sesion.tipo === 'ok') {
        setPantalla({ tipo: 'inicio' });
      } else {
        noDisponible(sesion);
      }
    }
    /**
     * Sin sesión, al ingreso; cualquier otro error, mensaje general y reintento.
     *
     * Ante `sinSesion` el cliente ya avisó al manejador global de `App`, que hace la misma navegación. Se navega
     * igual acá para que el arranque no dependa de ese registro: los efectos de los hijos corren antes que los del
     * padre, así que este pedido sale ANTES de que `App` registre el manejador, y que esté registrado cuando llega la
     * respuesta depende solo de que sea asíncrona. Repetir la navegación no tiene costo: es un `replace` a la misma
     * ruta.
     */
    function noDisponible(resultado: Exclude<Resultado<unknown>, { tipo: 'ok' }>) {
      if (resultado.tipo === 'sinSesion') {
        navegar('/ingreso', { replace: true });
      } else {
        setPantalla({ tipo: 'error', mensaje: resultado.mensaje });
      }
    }
    void decidir();
    return () => {
      vigente = false;
    };
  }, [intento, navegar]);

  if (pantalla.tipo === 'inicio') {
    return <Inicio />;
  }
  if (pantalla.tipo === 'error') {
    return (
      <section className="pantalla">
        <p className="mensaje-general" role="alert">
          {pantalla.mensaje}
        </p>
        <button
          type="button"
          onClick={() => {
            setPantalla({ tipo: 'cargando' });
            setIntento((n) => n + 1);
          }}
        >
          Reintentar
        </button>
      </section>
    );
  }
  return <p className="cargando">Cargando…</p>;
}
