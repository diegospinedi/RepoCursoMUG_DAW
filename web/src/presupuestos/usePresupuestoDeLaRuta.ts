import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { consultarPresupuesto, MENSAJE_PRESUPUESTO_NO_ENCONTRADO, type PresupuestoGrabado } from '../api/cliente';
import { numeroDeLaRuta } from './reglas';

export type CargaPresupuesto =
  | { tipo: 'cargando' }
  | { tipo: 'error'; mensaje: string }
  | { tipo: 'listo'; presupuesto: PresupuestoGrabado };

/** La carga de un número de ruta antes de consultarlo: inválido ya es "no encontrado"; si no, se está cargando. */
function cargaInicial(numero: number | null): CargaPresupuesto {
  return numero === null ? { tipo: 'error', mensaje: MENSAJE_PRESUPUESTO_NO_ENCONTRADO } : { tipo: 'cargando' };
}

export interface PresupuestoDeLaRuta {
  /** El número de la ruta (`:numero`), o `null` si no puede existir (no se consulta). */
  numero: number | null;
  carga: CargaPresupuesto;
  /** Muestra un presupuesto que devolvió otra operación (por ejemplo, el paso a Final), si es el de la ruta. */
  mostrar: (presupuesto: PresupuestoGrabado) => void;
  /** Vuelve a leer el presupuesto de la ruta; mientras tanto se sigue mostrando el anterior. */
  recargar: () => void;
  /** Si la ruta todavía muestra ese número: una respuesta de otro número no debe pisar la pantalla. */
  sigueVigente: (numero: number) => boolean;
}

/**
 * El presupuesto de la ruta `/presupuestos/:numero…` (FEAT-003a y FEAT-003c), compartido por la pantalla del
 * presupuesto y la de modificación: lo consulta una vez al abrir y cada vez que se pide {@link
 * PresupuestoDeLaRuta.recargar}; con otro número en la ruta la carga vuelve a empezar en el mismo render, y una
 * respuesta que llega para un número que ya no se muestra se descarta. Un número inválido o un 404 son
 * "No se encontró el presupuesto."; sin sesión va a `/ingreso` reemplazando la entrada. Los datos del cliente viven
 * solo en este estado (D-17); no hay consultas periódicas.
 */
export function usePresupuestoDeLaRuta(): PresupuestoDeLaRuta {
  const navegar = useNavigate();
  const numero = numeroDeLaRuta(useParams().numero);
  const [carga, setCarga] = useState<CargaPresupuesto>(() => cargaInicial(numero));
  const [recargas, setRecargas] = useState(0);
  const numeroVigente = useRef(numero);
  // Con otro número en la ruta, la carga vuelve a empezar en este mismo render (el efecto solo consulta la Api).
  const [numeroMostrado, setNumeroMostrado] = useState(numero);
  if (numero !== numeroMostrado) {
    setNumeroMostrado(numero);
    setCarga(cargaInicial(numero));
  }

  useEffect(() => {
    numeroVigente.current = numero;
    return () => {
      numeroVigente.current = null;
    };
  }, [numero]);

  useEffect(() => {
    if (numero === null) {
      return;
    }
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
  }, [numero, recargas, navegar]);

  const sigueVigente = useCallback((otro: number) => numeroVigente.current === otro, []);

  const mostrar = useCallback(
    (presupuesto: PresupuestoGrabado) => {
      if (sigueVigente(presupuesto.numero)) {
        setCarga({ tipo: 'listo', presupuesto });
      }
    },
    [sigueVigente],
  );

  const recargar = useCallback(() => setRecargas((anteriores) => anteriores + 1), []);

  return { numero, carga, mostrar, recargar, sigueVigente };
}
