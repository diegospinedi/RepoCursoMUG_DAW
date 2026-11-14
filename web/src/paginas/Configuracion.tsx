import { useCallback, useEffect, useId, useState, type FormEvent, type ReactNode } from 'react';
import { Link, useNavigate } from 'react-router';
import {
  CAMPO_ALICUOTA,
  CAMPO_CONDICION,
  CAMPO_MULTIPLO,
  CAMPO_PUNTO_VENTA,
  CAMPO_TOPE,
  CONDICIONES_FISCALES,
  consultarConfiguracion,
  grabarConfiguracion,
  type CondicionFiscal,
  type ConfiguracionNegocio,
} from '../api/cliente';
import { mostrarDecimal, mostrarEntero } from '../configuracion/numeros';
import {
  MENSAJE_ALICUOTA_NO_ADMITIDA,
  MENSAJE_PUNTO_VENTA,
  validarConfiguracion,
  type ErroresConfiguracion,
  type TextosConfiguracion,
} from '../configuracion/reglas';

export const MENSAJE_FALTA_TOPE = 'Falta cargar el tope de identificación del receptor.';
/** D-06 de FEAT-007: la alícuota guardada antes de FR-26 no es una de la lista. */
export const MENSAJE_ALICUOTA_FUERA_DE_LISTA =
  'La alícuota de IVA guardada ya no es válida. Elegí una de la lista y grabá.';
/** La opción que se ve mientras no hay una alícuota elegida (D-06). */
const SIN_ALICUOTA = 'Elegí una alícuota';
export const MENSAJE_GRABADA = 'La configuración se grabó.';

/**
 * El mensaje después de grabar (D-06 y D-09 de FEAT-002c): empieza como el de FEAT-001c y sigue con cuántos artículos
 * cambiaron de precio de venta, en singular o en plural, o con que ninguno cambió.
 */
function mensajeGrabada(preciosCambiados: number): string {
  if (preciosCambiados === 0) {
    return `${MENSAJE_GRABADA} Ningún precio de venta cambió.`;
  }
  // D-27: la cantidad lleva punto de miles.
  const articulos = preciosCambiados === 1 ? '1 artículo' : `${mostrarEntero(preciosCambiados)} artículos`;
  return `${MENSAJE_GRABADA} Cambió el precio de venta de ${articulos}.`;
}

/** El texto que ve la operadora para cada condición fiscal del contrato de la Api. */
const ETIQUETAS_CONDICION: Record<CondicionFiscal, string> = {
  responsable_inscripto: 'Responsable Inscripto',
  monotributo: 'Monotributo',
};

/** Largo máximo de cada número escrito: alcanza para cualquier importe válido y limita el cuerpo enviado. */
const LARGO_MAXIMO = 32;

const CAMPOS: readonly (keyof TextosConfiguracion)[] = [
  CAMPO_ALICUOTA,
  CAMPO_CONDICION,
  CAMPO_MULTIPLO,
  CAMPO_TOPE,
  CAMPO_PUNTO_VENTA,
];

const esCampo = (campo: string): campo is keyof TextosConfiguracion => CAMPOS.some((c) => c === campo);

/**
 * Los mensajes fijos de la pantalla para los 400 de la Api en estos campos (Block 2 de FEAT-007): la alícuota que ARCA
 * no acepta (D-35) y el punto de venta fuera de rango (AC-31). Los demás campos muestran el mensaje de la Api.
 */
const MENSAJES_FIJOS: Partial<Record<keyof TextosConfiguracion, string>> = {
  [CAMPO_ALICUOTA]: MENSAJE_ALICUOTA_NO_ADMITIDA,
  [CAMPO_PUNTO_VENTA]: MENSAJE_PUNTO_VENTA,
};

/** La alícuota guardada está en la lista que devolvió la Api (D-06 de FEAT-007). */
const alicuotaEnLaLista = (configuracion: ConfiguracionNegocio) =>
  configuracion.alicuotasAdmitidas.includes(configuracion.alicuotaIva);

/** Los textos del formulario; una alícuota guardada fuera de la lista queda sin elegir (D-06). */
function textosDe(configuracion: ConfiguracionNegocio): TextosConfiguracion {
  return {
    alicuotaIva: alicuotaEnLaLista(configuracion) ? mostrarDecimal(configuracion.alicuotaIva) : '',
    condicionFiscal: configuracion.condicionFiscal,
    multiploRedondeo: mostrarDecimal(configuracion.multiploRedondeo),
    topeIdentificacion: mostrarDecimal(configuracion.topeIdentificacion),
    puntoVenta: configuracion.puntoVenta === null ? '' : String(configuracion.puntoVenta),
  };
}

interface PropsCampo {
  etiqueta: string;
  error: string | undefined;
  /** El control, con el id y los atributos de accesibilidad que recibe. */
  children: (atributos: { id: string; 'aria-invalid': boolean; 'aria-describedby': string | undefined }) => ReactNode;
}

/** Un campo con su etiqueta y su mensaje de error al lado, vinculado por `aria-describedby`. */
function Campo({ etiqueta, error, children }: PropsCampo) {
  const id = useId();
  const idError = `${id}-error`;
  return (
    <div className="campo">
      <label htmlFor={id}>{etiqueta}</label>
      {children({ id, 'aria-invalid': error !== undefined, 'aria-describedby': error !== undefined ? idError : undefined })}
      {error !== undefined && (
        <p id={idError} className="campo__error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

interface PropsNumero {
  etiqueta: string;
  nombre: keyof TextosConfiguracion;
  valor: string;
  error: string | undefined;
  alCambiar: (valor: string) => void;
  /** El teclado en pantalla: decimal por defecto, numérico para un entero. */
  modo?: 'decimal' | 'numeric';
}

function CampoNumero({ etiqueta, nombre, valor, error, alCambiar, modo = 'decimal' }: PropsNumero) {
  return (
    <Campo etiqueta={etiqueta} error={error}>
      {(atributos) => (
        <input
          {...atributos}
          name={nombre}
          type="text"
          inputMode={modo}
          autoComplete="off"
          maxLength={LARGO_MAXIMO}
          value={valor}
          onChange={(evento) => alCambiar(evento.target.value)}
        />
      )}
    </Campo>
  );
}

/**
 * Parámetros de negocio (FR-01 a FR-05, FR-08). Consulta la configuración una sola vez al montarse; sin sesión va a
 * `/ingreso` reemplazando la entrada. Valida en el navegador con las reglas del dominio y no envía si hay errores; los
 * del servidor van junto al campo que indica la Api, incluido el del precio fuera de rango, junto al múltiplo (D-01 de
 * FEAT-002c). Al grabar muestra lo que devolvió la Api (D-03) y cuántos precios de venta cambiaron (D-06). Los valores
 * viven solo en el estado de este componente; no hay consultas periódicas.
 *
 * FEAT-007: la alícuota se elige de la lista que devuelve la Api (FR-26, AC-28); si la guardada no está en ella, no
 * queda ninguna elegida, un aviso pide elegir una y no se envía hasta elegirla (D-06). El punto de venta (FR-28) se
 * valida antes de enviar (AC-31); vacío conserva el guardado (D-27).
 */
export function Configuracion() {
  const navegar = useNavigate();
  const [cargando, setCargando] = useState(true);
  const [textos, setTextos] = useState<TextosConfiguracion | null>(null);
  const [faltaTope, setFaltaTope] = useState(false);
  const [alicuotas, setAlicuotas] = useState<readonly number[]>([]);
  const [alicuotaFueraDeLista, setAlicuotaFueraDeLista] = useState(false);
  const [errores, setErrores] = useState<ErroresConfiguracion>({});
  const [general, setGeneral] = useState<string | null>(null);
  /** Cuántos precios cambiaron en la última grabación; `null` si no hay una grabación que informar. */
  const [preciosCambiados, setPreciosCambiados] = useState<number | null>(null);
  const [enviando, setEnviando] = useState(false);

  /** Muestra la configuración que devolvió la Api, al abrir o después de grabar. Solo usa setters: no cambia. */
  const mostrar = useCallback((configuracion: ConfiguracionNegocio) => {
    setTextos(textosDe(configuracion));
    setFaltaTope(configuracion.faltaTope);
    setAlicuotas(configuracion.alicuotasAdmitidas);
    setAlicuotaFueraDeLista(!alicuotaEnLaLista(configuracion));
  }, []);

  useEffect(() => {
    let vigente = true;
    async function consultar() {
      const resultado = await consultarConfiguracion();
      if (!vigente) {
        return;
      }
      if (resultado.tipo === 'sinSesion') {
        // El cliente ya avisó al manejador de App; se navega igual para no depender de ese registro (ver Arranque).
        navegar('/ingreso', { replace: true });
        return;
      }
      if (resultado.tipo === 'ok') {
        mostrar(resultado.datos);
      } else {
        setGeneral(resultado.mensaje);
      }
      setCargando(false);
    }
    void consultar();
    return () => {
      vigente = false;
    };
  }, [navegar, mostrar]);

  function cambiar(campo: keyof TextosConfiguracion, valor: string) {
    setTextos((anteriores) => (anteriores === null ? anteriores : { ...anteriores, [campo]: valor }));
  }

  async function alEnviar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    if (textos === null) {
      return;
    }
    setGeneral(null);
    setPreciosCambiados(null);
    const validacion = validarConfiguracion(textos, alicuotas);
    if (validacion.tipo === 'errores') {
      setErrores(validacion.errores);
      return;
    }
    setErrores({});

    setEnviando(true);
    const resultado = await grabarConfiguracion(validacion.datos);
    setEnviando(false);
    switch (resultado.tipo) {
      case 'ok':
        mostrar(resultado.datos);
        setPreciosCambiados(resultado.datos.preciosCambiados);
        break;
      case 'campos':
      case 'campo': {
        const recibidos = resultado.tipo === 'campos' ? resultado.errores : [resultado];
        const delFormulario: ErroresConfiguracion = {};
        const otros: string[] = [];
        for (const { campo, mensaje } of recibidos) {
          if (esCampo(campo)) {
            delFormulario[campo] = MENSAJES_FIJOS[campo] ?? mensaje;
          } else {
            otros.push(mensaje);
          }
        }
        setErrores(delFormulario);
        setGeneral(otros.length > 0 ? otros.join(' ') : null);
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

  if (cargando) {
    return <p className="cargando">Cargando…</p>;
  }

  return (
    <section className="pantalla">
      <h1>Configuración</h1>
      {faltaTope && (
        <p className="aviso" role="status">
          {MENSAJE_FALTA_TOPE}
        </p>
      )}
      {alicuotaFueraDeLista && (
        <p className="aviso" role="status">
          {MENSAJE_ALICUOTA_FUERA_DE_LISTA}
        </p>
      )}
      {textos === null ? (
        general !== null && (
          <p className="mensaje-general" role="alert">
            {general}
          </p>
        )
      ) : (
        <form method="post" onSubmit={alEnviar} noValidate>
          <Campo etiqueta="Alícuota de IVA (%)" error={errores.alicuotaIva}>
            {(atributos) => (
              <select
                {...atributos}
                name={CAMPO_ALICUOTA}
                value={textos.alicuotaIva}
                onChange={(evento) => cambiar(CAMPO_ALICUOTA, evento.target.value)}
              >
                {/* Sin alícuota elegida (D-06): esta opción evita que el navegador muestre elegida la primera. */}
                {textos.alicuotaIva === '' && (
                  <option value="" disabled>
                    {SIN_ALICUOTA}
                  </option>
                )}
                {alicuotas.map((alicuota) => {
                  const texto = mostrarDecimal(alicuota);
                  return (
                    <option key={texto} value={texto}>
                      {texto}
                    </option>
                  );
                })}
              </select>
            )}
          </Campo>
          <Campo etiqueta="Condición fiscal" error={errores.condicionFiscal}>
            {(atributos) => (
              <select
                {...atributos}
                name={CAMPO_CONDICION}
                value={textos.condicionFiscal}
                onChange={(evento) => cambiar(CAMPO_CONDICION, evento.target.value)}
              >
                {CONDICIONES_FISCALES.map((condicion) => (
                  <option key={condicion} value={condicion}>
                    {ETIQUETAS_CONDICION[condicion]}
                  </option>
                ))}
              </select>
            )}
          </Campo>
          <CampoNumero
            etiqueta="Múltiplo de redondeo comercial ($)"
            nombre={CAMPO_MULTIPLO}
            valor={textos.multiploRedondeo}
            error={errores.multiploRedondeo}
            alCambiar={(valor) => cambiar(CAMPO_MULTIPLO, valor)}
          />
          <CampoNumero
            etiqueta="Tope de identificación del receptor ($)"
            nombre={CAMPO_TOPE}
            valor={textos.topeIdentificacion}
            error={errores.topeIdentificacion}
            alCambiar={(valor) => cambiar(CAMPO_TOPE, valor)}
          />
          <CampoNumero
            etiqueta="Punto de venta"
            nombre={CAMPO_PUNTO_VENTA}
            valor={textos.puntoVenta}
            error={errores.puntoVenta}
            alCambiar={(valor) => cambiar(CAMPO_PUNTO_VENTA, valor)}
            modo="numeric"
          />
          {general !== null && (
            <p className="mensaje-general" role="alert">
              {general}
            </p>
          )}
          {preciosCambiados !== null && (
            <p className="mensaje-exito" role="status">
              {mensajeGrabada(preciosCambiados)}
            </p>
          )}
          <button type="submit" disabled={enviando}>
            Grabar
          </button>
        </form>
      )}
      <p>
        <Link to="/">Volver al inicio</Link>
      </p>
    </section>
  );
}
