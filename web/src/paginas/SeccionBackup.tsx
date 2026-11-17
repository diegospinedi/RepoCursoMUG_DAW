import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';
import { CAMPO_RUTA_BACKUP, consultarUbicacionBackup, grabarUbicacionBackup } from '../api/cliente';
import { Campo } from '../componentes/Campo';
import { repartirErrores } from '../componentes/repartirErrores';
import { validarRutaBackup } from '../configuracion/reglas';

export const MENSAJE_BACKUP_GRABADO = 'La ubicación del backup se grabó.';

type CampoBackup = typeof CAMPO_RUTA_BACKUP;

const esCampoBackup = (campo: string): campo is CampoBackup => campo === CAMPO_RUTA_BACKUP;

/**
 * Sección "Backup" de la pantalla de parámetros (FEAT-010, FR-01 a FR-03): carga, muestra y graba la carpeta donde se
 * guardan las copias, con su propio botón. Nunca elegida es el campo vacío; un fallo de la consulta es un mensaje
 * general sin formulario. Valida en el navegador con {@link validarRutaBackup} (la ruta obligatoria y su largo); editar
 * el campo quita el mensaje de grabada. El error de la Api (misma unidad,
 * carpeta inexistente o sin escritura, etc.) va junto al campo y lo escrito queda. Un 500 o un fallo de red es un
 * mensaje general sin la ruta. Sin sesión va a `/ingreso`. En reposo no muestra mensajes de estado (D-13).
 *
 * La ruta puede llevar el nombre de una persona (D-12): vive solo en el estado del componente, nunca en el
 * almacenamiento del navegador, la URL ni la consola.
 */
export function SeccionBackup() {
  const navegar = useNavigate();
  const idTitulo = useId();
  const [cargando, setCargando] = useState(true);
  const [ruta, setRuta] = useState<string | null>(null);
  const [error, setError] = useState<string | undefined>(undefined);
  const [general, setGeneral] = useState<string | null>(null);
  const [grabada, setGrabada] = useState(false);
  const [enviando, setEnviando] = useState(false);
  /**
   * Si la sección sigue montada: la respuesta de grabar puede llegar después de que la operadora salió de la pantalla,
   * y entonces no cambia el estado ni navega. Se enciende en el efecto (no al crearla) para que el doble montaje de
   * StrictMode la vuelva a encender después de su limpieza.
   */
  const montada = useRef(false);

  useEffect(() => {
    montada.current = true;
    return () => {
      montada.current = false;
    };
  }, []);

  useEffect(() => {
    let vigente = true;
    async function consultar() {
      const resultado = await consultarUbicacionBackup();
      if (!vigente) {
        return;
      }
      if (resultado.tipo === 'sinSesion') {
        navegar('/ingreso', { replace: true });
        return;
      }
      if (resultado.tipo === 'ok') {
        setRuta(resultado.datos ?? '');
      } else {
        setGeneral(resultado.mensaje);
      }
      setCargando(false);
    }
    void consultar();
    return () => {
      vigente = false;
    };
  }, [navegar]);

  /** Recibe lo escrito desde el render, donde el formulario ya existe: no hay una ruta nula que descartar. */
  async function alEnviar(evento: FormEvent<HTMLFormElement>, escrita: string) {
    evento.preventDefault();
    setGeneral(null);
    setGrabada(false);
    const validacion = validarRutaBackup(escrita);
    if (validacion.tipo === 'error') {
      setError(validacion.mensaje);
      return;
    }
    setError(undefined);

    setEnviando(true);
    const resultado = await grabarUbicacionBackup(validacion.ruta);
    if (!montada.current) {
      return;
    }
    setEnviando(false);
    switch (resultado.tipo) {
      case 'ok':
        setRuta(resultado.datos);
        setGrabada(true);
        break;
      case 'campos':
      case 'campo': {
        const repartidos = repartirErrores(
          resultado.tipo === 'campos' ? resultado.errores : [resultado],
          esCampoBackup,
        );
        setError(repartidos.campos.ruta);
        setGeneral(repartidos.general);
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

  const mensajeGeneral = general !== null && (
    <p className="mensaje-general" role="alert">
      {general}
    </p>
  );

  return (
    <section aria-labelledby={idTitulo}>
      <h2 id={idTitulo}>Backup</h2>
      {cargando ? (
        <p className="cargando">Cargando la ubicación del backup…</p>
      ) : ruta === null ? (
        mensajeGeneral
      ) : (
        <form method="post" onSubmit={(evento) => void alEnviar(evento, ruta)} noValidate>
          <Campo etiqueta="Carpeta del backup" error={error}>
            {(atributos) => (
              <input
                {...atributos}
                name={CAMPO_RUTA_BACKUP}
                type="text"
                autoComplete="off"
                spellCheck={false}
                value={ruta}
                onChange={(evento) => {
                  setRuta(evento.target.value);
                  // Lo que se ve deja de ser lo grabado: el mensaje de grabada ya no corresponde.
                  setGrabada(false);
                }}
              />
            )}
          </Campo>
          {mensajeGeneral}
          {grabada && (
            <p className="mensaje-exito" role="status">
              {MENSAJE_BACKUP_GRABADO}
            </p>
          )}
          <button type="submit" disabled={enviando}>
            Grabar ubicación del backup
          </button>
        </form>
      )}
    </section>
  );
}
