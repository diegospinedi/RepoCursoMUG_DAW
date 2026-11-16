import { useEffect, useId, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';
import {
  CAMPO_CUIT,
  CAMPO_DOMICILIO_COMERCIAL,
  CAMPO_INGRESOS_BRUTOS,
  CAMPO_INICIO_ACTIVIDADES,
  CAMPO_RAZON_SOCIAL,
  CAMPOS_EMISOR,
  consultarDatosEmisor,
  grabarDatosEmisor,
} from '../api/cliente';
import { Campo } from '../componentes/Campo';
import { repartirErrores } from '../componentes/repartirErrores';
import {
  hoyLocal,
  validarDatosEmisor,
  type ErroresDatosEmisor,
  type TextosDatosEmisor,
} from '../configuracion/reglas';

export const MENSAJE_EMISOR_GRABADO = 'Los datos del emisor se grabaron.';

/** Los cinco datos sin cargar: los campos vacíos. */
const EMISOR_VACIO: TextosDatosEmisor = {
  razonSocial: '',
  domicilioComercial: '',
  cuit: '',
  ingresosBrutos: '',
  inicioActividades: '',
};

const esCampoEmisor = (campo: string): campo is keyof TextosDatosEmisor => CAMPOS_EMISOR.some((c) => c === campo);

interface PropsTextoEmisor {
  etiqueta: string;
  nombre: keyof TextosDatosEmisor;
  valor: string;
  error: string | undefined;
  alCambiar: (valor: string) => void;
  tipo?: 'text' | 'date';
  /** Para la fecha: la fecha máxima del selector (hoy). */
  maximo?: string;
}

function CampoTextoEmisor({ etiqueta, nombre, valor, error, alCambiar, tipo = 'text', maximo }: PropsTextoEmisor) {
  return (
    <Campo etiqueta={etiqueta} error={error}>
      {(atributos) => (
        <input
          {...atributos}
          name={nombre}
          type={tipo}
          max={maximo}
          autoComplete="off"
          value={valor}
          onChange={(evento) => alCambiar(evento.target.value)}
        />
      )}
    </Campo>
  );
}

/**
 * Sección "Datos del emisor" de la configuración (FEAT-009): carga, muestra y graba los cinco datos que lleva la factura,
 * con su propio botón, independiente de los parámetros de negocio. "Sin cargar" es el 200 con los cinco en null (los
 * campos vacíos); un 404 u otro fallo de la consulta es un mensaje general sin formulario (D-22). Valida en el navegador
 * con el espejo de las reglas del servidor y no envía si hay errores; los del servidor van junto a su campo y lo escrito
 * queda. Un 500 o un fallo de red es un mensaje general sin datos: nada se da por grabado. Sin sesión va a `/ingreso`.
 * Los datos viven solo en el estado del componente.
 */
export function SeccionDatosEmisor() {
  const navegar = useNavigate();
  const idTitulo = useId();
  const [cargando, setCargando] = useState(true);
  const [textos, setTextos] = useState<TextosDatosEmisor | null>(null);
  const [errores, setErrores] = useState<ErroresDatosEmisor>({});
  const [general, setGeneral] = useState<string | null>(null);
  const [grabado, setGrabado] = useState(false);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    let vigente = true;
    async function consultar() {
      const resultado = await consultarDatosEmisor();
      if (!vigente) {
        return;
      }
      if (resultado.tipo === 'sinSesion') {
        navegar('/ingreso', { replace: true });
        return;
      }
      if (resultado.tipo === 'ok') {
        setTextos(resultado.datos ?? EMISOR_VACIO);
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

  function cambiar(campo: keyof TextosDatosEmisor, valor: string) {
    setTextos((anteriores) => (anteriores === null ? anteriores : { ...anteriores, [campo]: valor }));
  }

  /** Recibe lo escrito desde el render, donde el formulario ya existe: no hay textos nulos que descartar. */
  async function alEnviar(evento: FormEvent<HTMLFormElement>, escritos: TextosDatosEmisor) {
    evento.preventDefault();
    setGeneral(null);
    setGrabado(false);
    const validacion = validarDatosEmisor(escritos, hoyLocal());
    if (validacion.tipo === 'errores') {
      setErrores(validacion.errores);
      return;
    }
    setErrores({});

    setEnviando(true);
    const resultado = await grabarDatosEmisor(validacion.datos);
    setEnviando(false);
    switch (resultado.tipo) {
      case 'ok':
        setTextos(resultado.datos);
        setGrabado(true);
        break;
      case 'campos':
      case 'campo': {
        const repartidos = repartirErrores(
          resultado.tipo === 'campos' ? resultado.errores : [resultado],
          esCampoEmisor,
        );
        setErrores(repartidos.campos);
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
      <h2 id={idTitulo}>Datos del emisor</h2>
      {cargando ? (
        <p className="cargando">Cargando los datos del emisor…</p>
      ) : textos === null ? (
        mensajeGeneral
      ) : (
        <form method="post" onSubmit={(evento) => void alEnviar(evento, textos)} noValidate>
          <CampoTextoEmisor
            etiqueta="Razón social"
            nombre={CAMPO_RAZON_SOCIAL}
            valor={textos.razonSocial}
            error={errores.razonSocial}
            alCambiar={(valor) => cambiar(CAMPO_RAZON_SOCIAL, valor)}
          />
          <CampoTextoEmisor
            etiqueta="Domicilio comercial"
            nombre={CAMPO_DOMICILIO_COMERCIAL}
            valor={textos.domicilioComercial}
            error={errores.domicilioComercial}
            alCambiar={(valor) => cambiar(CAMPO_DOMICILIO_COMERCIAL, valor)}
          />
          <CampoTextoEmisor
            etiqueta="CUIT"
            nombre={CAMPO_CUIT}
            valor={textos.cuit}
            error={errores.cuit}
            alCambiar={(valor) => cambiar(CAMPO_CUIT, valor)}
          />
          <CampoTextoEmisor
            etiqueta="Ingresos Brutos"
            nombre={CAMPO_INGRESOS_BRUTOS}
            valor={textos.ingresosBrutos}
            error={errores.ingresosBrutos}
            alCambiar={(valor) => cambiar(CAMPO_INGRESOS_BRUTOS, valor)}
          />
          <CampoTextoEmisor
            etiqueta="Fecha de inicio de actividades"
            nombre={CAMPO_INICIO_ACTIVIDADES}
            valor={textos.inicioActividades}
            error={errores.inicioActividades}
            alCambiar={(valor) => cambiar(CAMPO_INICIO_ACTIVIDADES, valor)}
            tipo="date"
            maximo={hoyLocal()}
          />
          {mensajeGeneral}
          {grabado && (
            <p className="mensaje-exito" role="status">
              {MENSAJE_EMISOR_GRABADO}
            </p>
          )}
          <button type="submit" disabled={enviando}>
            Grabar datos del emisor
          </button>
        </form>
      )}
    </section>
  );
}
