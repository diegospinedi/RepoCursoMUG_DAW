import { useEffect, useId, useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import {
  CAMPO_APELLIDO,
  CAMPO_CANTIDAD,
  CAMPO_DESCUENTO,
  CAMPO_DNI,
  CAMPO_DOMICILIO,
  CAMPO_EMAIL,
  CAMPO_LINEAS,
  CAMPO_NOMBRE,
  CAMPO_ORDEN,
  CAMPO_TELEFONO,
  type ArticuloListado,
  type DatosCliente,
  type ErrorDeCampo,
  type LineaAGrabar,
  type LineaPresupuesto,
  type ResultadoAltaPresupuesto,
} from '../api/cliente';
import { mostrarDecimal, mostrarImporte } from '../configuracion/numeros';
import { BuscadorArticulos } from './BuscadorArticulos';
import {
  calcularLinea,
  calcularTotal,
  LARGO_MAXIMO_APELLIDO,
  LARGO_MAXIMO_DNI_ESCRITO,
  LARGO_MAXIMO_DOMICILIO,
  LARGO_MAXIMO_EMAIL,
  LARGO_MAXIMO_NOMBRE,
  LARGO_MAXIMO_TELEFONO,
  MENSAJE_IMPORTE_LINEA_EXCEDIDO,
  MENSAJE_TOTAL_EXCEDIDO,
  mensajeConfirmacion,
  validarCantidad,
  validarCliente,
  validarDescuento,
  validarLineas,
  type CampoLinea,
  type ErroresCliente,
  type PreciosLinea,
  type TextosCliente,
} from './reglas';

const VACIOS: TextosCliente = { apellido: '', nombre: '', dni: '', domicilio: '', email: '', telefono: '' };
const SIN_LINEAS: readonly LineaPresupuesto[] = [];

const CAMPOS_CLIENTE: readonly (keyof TextosCliente)[] = [
  CAMPO_APELLIDO,
  CAMPO_NOMBRE,
  CAMPO_DNI,
  CAMPO_DOMICILIO,
  CAMPO_EMAIL,
  CAMPO_TELEFONO,
];

const esCampoCliente = (campo: string): campo is keyof TextosCliente => CAMPOS_CLIENTE.some((c) => c === campo);

/** Dónde va un mensaje de la Api sobre una línea: la línea entera (el artículo), su cantidad o su descuento (D-10). */
type LugarEnLinea = 'linea' | 'cantidad' | 'descuento';

/** Los mensajes de la Api de una línea, uno por lugar (D-10). */
type ErroresLinea = Partial<Record<LugarEnLinea, string>>;

/** El `orden` de una línea grabada (FEAT-003c, D-03) identifica la línea entera: su mensaje va junto a Quitar. */
const LUGAR_DEL_SUBCAMPO: Readonly<Record<string, LugarEnLinea>> = {
  [CAMPO_CANTIDAD]: 'cantidad',
  [CAMPO_DESCUENTO]: 'descuento',
  [CAMPO_ORDEN]: 'linea',
};

const CAMPO_DE_LINEA = new RegExp(`^${CAMPO_LINEAS}\\[(\\d+)\\](?:\\.(\\w+))?$`);

/**
 * `lineas[i]`, `lineas[i].cantidad`, `lineas[i].porcentajeDescuento` y `lineas[i].orden` de los errores de la Api
 * (D-20, D-06, FEAT-003c D-03): el índice de la línea enviada y el lugar del mensaje, o `null` si el campo es otro
 * (incluido un subcampo desconocido).
 */
function lugarDeLinea(campo: string): { indice: number; lugar: LugarEnLinea } | null {
  const coincidencia = CAMPO_DE_LINEA.exec(campo);
  if (coincidencia === null) {
    return null;
  }
  const subcampo = coincidencia[2];
  const lugar = subcampo === undefined ? 'linea' : LUGAR_DEL_SUBCAMPO[subcampo];
  return lugar === undefined ? null : { indice: Number(coincidencia[1]), lugar };
}

/**
 * Una línea cargada en pantalla: el artículo elegido y un identificador propio, porque el mismo artículo puede estar
 * en más de una línea (D-16). La Cantidad y el Descuento se guardan como se escribieron; en una línea nueva empiezan
 * en 1 y 0 (FR-01). Una línea ya grabada (FEAT-003c) trae su `orden`, que se envía al grabar, y su Precio Unitario
 * grabado (D-02); no puede cambiar de artículo: para cambiarlo se quita y se agrega otra.
 */
interface LineaCargada {
  id: number;
  orden?: number;
  codigo: number;
  descripcion: string;
  precioUnitario: number;
  cantidad: string;
  descuento: string;
}

/**
 * Lo que se muestra de una línea, derivado de lo escrito: cada campo validado con las reglas del servidor y, si los dos
 * son válidos, los precios (FR-04, FR-05). `precios` es `null` si algún campo es inválido o si el Precio Final supera el
 * tope (`excedido`, D-04): `calcularLinea` devuelve `null` en los dos casos, por eso se valida antes (D-25).
 */
interface LineaCalculada {
  cantidad: CampoLinea;
  descuento: CampoLinea;
  precios: PreciosLinea | null;
  excedido: boolean;
}

function calcular(linea: LineaCargada): LineaCalculada {
  const cantidad = validarCantidad(linea.cantidad);
  const descuento = validarDescuento(linea.descuento);
  if (!('valor' in cantidad) || !('valor' in descuento)) {
    return { cantidad, descuento, precios: null, excedido: false };
  }
  const precios = calcularLinea(linea.precioUnitario, cantidad.valor, descuento.valor);
  return { cantidad, descuento, precios, excedido: precios === null };
}

/** Una línea cargada junto con lo que se calcula de ella. */
interface FilaCalculada {
  linea: LineaCargada;
  calculada: LineaCalculada;
}

const errorDe = (campo: CampoLinea) => ('error' in campo ? campo.error : undefined);

/** Lo que muestra una celda de importe que no se puede calcular. */
const SIN_IMPORTE = '—';

const importeOSinDato = (valor: number | null) => (valor === null ? SIN_IMPORTE : mostrarImporte(valor));

interface PropsCampo {
  etiqueta: string;
  nombre: keyof TextosCliente;
  valor: string;
  error: string | undefined;
  largoMaximo: number;
  tipo?: 'text' | 'email' | 'tel';
  alCambiar: (valor: string) => void;
}

/**
 * Un dato del cliente con su etiqueta y su mensaje de error al lado, vinculado por `aria-describedby`. Sin
 * autocompletado (D-17): el navegador no guarda en su historial de formularios los datos personales escritos.
 */
function CampoCliente({ etiqueta, nombre, valor, error, largoMaximo, tipo = 'text', alCambiar }: PropsCampo) {
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

interface PropsMensaje {
  id: string;
  /** El mensaje calculado en el navegador mientras se escribe: gana, y se anuncia sin interrumpir. */
  local: string | undefined;
  /** El mensaje que devolvió la Api (o la validación al grabar): se anuncia como alerta, como en el resto. */
  servidor: string | undefined;
  /** El mensaje local puede recibir el foco desde el código (`tabIndex=-1`), para llevar a él al grabar. */
  enfocable?: boolean;
}

/**
 * El mensaje de un lugar (un campo o la tabla), con un solo `id` para `aria-describedby`. La región `aria-live` está
 * siempre presente para que el lector de pantalla anuncie lo que aparece en ella al escribir.
 */
function Mensaje({ id, local, servidor, enfocable = false }: PropsMensaje) {
  return (
    <>
      <div aria-live="polite">
        {local !== undefined && (
          <p id={id} className="campo__error" tabIndex={enfocable ? -1 : undefined}>
            {local}
          </p>
        )}
      </div>
      {local === undefined && servidor !== undefined && (
        <p id={id} className="campo__error" role="alert">
          {servidor}
        </p>
      )}
    </>
  );
}

interface PropsCampoDeLinea {
  etiqueta: string;
  valor: string;
  local: string | undefined;
  servidor: string | undefined;
  modo: 'numeric' | 'decimal';
  alCambiar: (valor: string) => void;
}

/** La Cantidad o el Descuento de una fila, con su mensaje al lado vinculado por `aria-describedby` (D-10). */
function CampoDeLinea({ etiqueta, valor, local, servidor, modo, alCambiar }: PropsCampoDeLinea) {
  const idError = `${useId()}-error`;
  const conError = (local ?? servidor) !== undefined;
  return (
    <td>
      <input
        type="text"
        inputMode={modo}
        autoComplete="off"
        value={valor}
        aria-label={etiqueta}
        aria-invalid={conError}
        aria-describedby={conError ? idError : undefined}
        onChange={(evento) => alCambiar(evento.target.value)}
      />
      <Mensaje id={idError} local={local} servidor={servidor} />
    </td>
  );
}

interface PropsLinea {
  fila: FilaCalculada;
  numero: number;
  errores: ErroresLinea;
  alCambiarCantidad: (valor: string) => void;
  alCambiarDescuento: (valor: string) => void;
  alQuitar: () => void;
}

/**
 * Una fila de la tabla de líneas (FR-13): el Precio Unitario en un campo de solo lectura (FR-11), la Cantidad y el
 * Descuento editables y los dos precios recalculados al escribir (FR-07), o "—" mientras no se puedan calcular. Cada
 * campo muestra un solo mensaje: el calculado en el navegador y, si no hay, el que devolvió la Api. El Precio Final por
 * encima del tope va junto a la Cantidad, donde lo pone el servidor (D-04); el mensaje de la Api sobre la línea entera
 * (el artículo, `lineas[i]`, o su `orden`) va junto a Quitar.
 */
function FilaLinea({ fila, numero, errores, alCambiarCantidad, alCambiarDescuento, alQuitar }: PropsLinea) {
  const { linea, calculada } = fila;
  const idError = `${useId()}-error`;
  const errorLinea = errores.linea;
  const describe = errorLinea !== undefined ? idError : undefined;
  const localCantidad =
    errorDe(calculada.cantidad) ?? (calculada.excedido ? MENSAJE_IMPORTE_LINEA_EXCEDIDO : undefined);
  return (
    <tr>
      <td>{linea.codigo}</td>
      <td>{linea.descripcion}</td>
      <td className="tabla__importe">
        <input
          type="text"
          readOnly
          value={mostrarImporte(linea.precioUnitario)}
          aria-label={`Precio unitario de la línea ${numero}`}
          aria-describedby={describe}
        />
      </td>
      <CampoDeLinea
        etiqueta={`Cantidad de la línea ${numero}`}
        valor={linea.cantidad}
        local={localCantidad}
        servidor={errores.cantidad}
        modo="numeric"
        alCambiar={alCambiarCantidad}
      />
      <CampoDeLinea
        etiqueta={`Porcentaje de descuento de la línea ${numero}`}
        valor={linea.descuento}
        local={errorDe(calculada.descuento)}
        servidor={errores.descuento}
        modo="decimal"
        alCambiar={alCambiarDescuento}
      />
      <td className="tabla__importe">{importeOSinDato(calculada.precios?.precioConDescuento ?? null)}</td>
      <td className="tabla__importe">{importeOSinDato(calculada.precios?.precioFinal ?? null)}</td>
      <td>
        <button type="button" aria-label={`Quitar línea ${numero}`} aria-describedby={describe} onClick={alQuitar}>
          Quitar
        </button>
        {errorLinea !== undefined && (
          <p id={idError} className="campo__error" role="alert">
            {errorLinea}
          </p>
        )}
      </td>
    </tr>
  );
}

/**
 * El total (FR-06) de las líneas calculadas: `null` si alguna línea no se puede calcular; `excedido` si todas se
 * pueden pero la suma supera el tope de D-04.
 */
function totalDe(filas: readonly FilaCalculada[]): { total: number | null; excedido: boolean } {
  const finales: number[] = [];
  for (const { calculada } of filas) {
    if (calculada.precios === null) {
      return { total: null, excedido: false };
    }
    finales.push(calculada.precios.precioFinal);
  }
  const total = calcularTotal(finales);
  return { total, excedido: total === null };
}

/**
 * Las líneas a enviar con la cantidad y el descuento validados (D-03), y el `orden` de las ya grabadas (FEAT-003c), o
 * `null` si alguna no se puede grabar.
 */
function lineasAGrabar(filas: readonly FilaCalculada[]): LineaAGrabar[] | null {
  const aGrabar: LineaAGrabar[] = [];
  for (const { linea, calculada } of filas) {
    const { cantidad, descuento, precios } = calculada;
    if (!('valor' in cantidad) || !('valor' in descuento) || precios === null) {
      return null;
    }
    aGrabar.push({
      ...(linea.orden !== undefined ? { orden: linea.orden } : {}),
      codigoArticulo: linea.codigo,
      cantidad: cantidad.valor,
      porcentajeDescuento: descuento.valor,
    });
  }
  return aGrabar;
}

/** Los datos guardados del cliente como se escriben en el formulario: un opcional sin cargar es el campo vacío. */
function textosDelCliente(cliente: DatosCliente): TextosCliente {
  return {
    apellido: cliente.apellido,
    nombre: cliente.nombre,
    dni: cliente.dni,
    domicilio: cliente.domicilio ?? '',
    email: cliente.email ?? '',
    telefono: cliente.telefono ?? '',
  };
}

/** Una línea grabada como línea cargada: su `orden`, su Precio Unitario grabado (D-02) y lo escrito como texto. */
function lineaCargadaDe(linea: LineaPresupuesto, id: number): LineaCargada {
  return {
    id,
    orden: linea.orden,
    codigo: linea.codigoArticulo,
    descripcion: linea.descripcion,
    precioUnitario: linea.precioUnitario,
    cantidad: String(linea.cantidad),
    descuento: mostrarDecimal(linea.porcentajeDescuento),
  };
}

interface PropsFormulario {
  /** Los datos guardados del cliente al abrir (la modificación, FEAT-003c); por defecto, los seis vacíos (el alta). */
  clienteInicial?: DatosCliente;
  /** Las líneas grabadas al abrir (la modificación, FEAT-003c); por defecto, ninguna (el alta). */
  lineasIniciales?: readonly LineaPresupuesto[];
  /** Envía lo validado: el alta o la modificación, que agrega el número y la versión. */
  grabar: (cliente: DatosCliente, lineas: LineaAGrabar[]) => Promise<ResultadoAltaPresupuesto>;
  /** Un mensaje general al abrir (el alta: la sesión no se pudo verificar); se quita al grabar, como los demás. */
  mensajeInicial?: string | null;
}

/**
 * El formulario del presupuesto, del alta (FEAT-003a, FEAT-003b) y de la modificación (FEAT-003c): los seis datos del
 * cliente, el buscador de artículos activos ({@link BuscadorArticulos}, FR-09) y la tabla de líneas, con la Cantidad y
 * el Descuento de cada línea, sus precios y el total con IVA incluido recalculados al escribir; al grabar se envían la
 * cantidad y el descuento (y el `orden` de las líneas ya grabadas), y el servidor calcula los importes que se graban
 * (D-03). Valida en el navegador con las reglas del servidor y no envía si hay errores; los del servidor van junto al
 * campo, a la línea o a la tabla que indica la Api (D-20). Al grabar navega al presupuesto con la confirmación en el
 * estado de la navegación (D-24), que no lleva datos del cliente; un 409 muestra el texto que eligió el cliente de la
 * Api para su motivo (D-07), y lo escrito sigue en pantalla.
 *
 * Datos personales (D-17): viven solo en el estado de este componente; nada en la URL, en `localStorage`,
 * `sessionStorage` ni en la consola. No hay consultas periódicas.
 */
export function FormularioPresupuesto({
  clienteInicial,
  lineasIniciales = SIN_LINEAS,
  grabar,
  mensajeInicial = null,
}: PropsFormulario) {
  const navegar = useNavigate();
  const idErrorLineas = `${useId()}-lineas`;
  const [textos, setTextos] = useState<TextosCliente>(() =>
    clienteInicial === undefined ? VACIOS : textosDelCliente(clienteInicial),
  );
  const [errores, setErrores] = useState<ErroresCliente>({});
  const [general, setGeneral] = useState<string | null>(mensajeInicial);
  const [enviando, setEnviando] = useState(false);
  const [lineas, setLineas] = useState<LineaCargada[]>(() =>
    lineasIniciales.map((linea, indice) => lineaCargadaDe(linea, indice + 1)),
  );
  const [errorLineas, setErrorLineas] = useState<string | undefined>(undefined);
  /** Los errores de `lineas[i]` (y de su cantidad o descuento) de la Api, por el identificador de la línea enviada. */
  const [erroresPorLinea, setErroresPorLinea] = useState<Record<number, ErroresLinea>>({});
  const proximaLinea = useRef(lineasIniciales.length + 1);
  const envioEnCurso = useRef(false);
  const refCliente = useRef<HTMLFieldSetElement>(null);
  const refTabla = useRef<HTMLTableElement>(null);
  /** Cuántas veces Grabar no envió por errores en las líneas: cada aumento lleva el foco al primer error. */
  const [pedidosDeFoco, setPedidosDeFoco] = useState(0);
  // FR-07: la fila y el total se derivan de lo escrito en cada dibujo, así siempre están al día.
  const filas: FilaCalculada[] = lineas.map((linea) => ({ linea, calculada: calcular(linea) }));
  const resumen = totalDe(filas);
  // El total por encima del tope va donde pone el servidor el error `lineas`: debajo de la tabla (D-04).
  const localLineas = resumen.excedido ? MENSAJE_TOTAL_EXCEDIDO : undefined;

  /**
   * Grabar no envió por errores en las líneas, cuyos mensajes ya se anunciaron al escribir: el foco va al primer campo
   * inválido en orden de lectura (los datos del cliente, después Cantidad y Descuento de cada línea) o, si el único
   * error es el total excedido, al mensaje de debajo de la tabla. Corre después de dibujar, con los errores del cliente
   * ya marcados.
   */
  useEffect(() => {
    if (pedidosDeFoco === 0) {
      return;
    }
    const invalido = '[aria-invalid="true"]';
    const destino =
      refCliente.current?.querySelector<HTMLElement>(invalido) ??
      refTabla.current?.querySelector<HTMLElement>(invalido) ??
      document.getElementById(idErrorLineas);
    destino?.focus();
  }, [pedidosDeFoco, idErrorLineas]);

  function cambiar(campo: keyof TextosCliente, valor: string) {
    setTextos((anteriores) => ({ ...anteriores, [campo]: valor }));
  }

  /** FR-10: copia el código, la descripción y el precio de venta (el buscador ya descartó los negativos, FR-12). */
  function agregar(articulo: ArticuloListado) {
    setErrorLineas(undefined);
    const id = proximaLinea.current++;
    setLineas((anteriores) => [
      ...anteriores,
      {
        id,
        codigo: articulo.codigo,
        descripcion: articulo.descripcion,
        precioUnitario: articulo.precioVenta,
        cantidad: '1',
        descuento: '0',
      },
    ]);
  }

  /** Quita los mensajes de la Api de una línea (artículo, cantidad y descuento). */
  function olvidarMensajesDe(id: number) {
    setErroresPorLinea((anteriores) => {
      if (anteriores[id] === undefined) {
        return anteriores;
      }
      const restantes = { ...anteriores };
      delete restantes[id];
      return restantes;
    });
  }

  /**
   * FR-01 y FR-07: guarda lo escrito (la fila y el total se recalculan al dibujar) y quita los mensajes de la Api de esa
   * línea, que hablaban de los valores anteriores: así un mensaje viejo no tapa el que se calcula ahora.
   */
  function cambiarLinea(id: number, lugar: 'cantidad' | 'descuento', valor: string) {
    setLineas((anteriores) => anteriores.map((linea) => (linea.id === id ? { ...linea, [lugar]: valor } : linea)));
    olvidarMensajesDe(id);
  }

  /** D-21: ayuda de pantalla antes de grabar; el mensaje de esa línea se va con ella. */
  function quitar(id: number) {
    setErrorLineas(undefined);
    setLineas((anteriores) => anteriores.filter((linea) => linea.id !== id));
    olvidarMensajesDe(id);
  }

  /**
   * Reparte los errores de la Api: cada dato del cliente, cada campo de cada línea (D-10), la tabla y, lo que no tiene
   * lugar (por ejemplo, la versión de la modificación), en general.
   */
  function mostrarErrores(recibidos: ErrorDeCampo[], enviadas: LineaCargada[]) {
    const delCliente: ErroresCliente = {};
    const porLinea: Record<number, ErroresLinea> = {};
    const otros: string[] = [];
    let deLineas: string | undefined;
    for (const { campo, mensaje } of recibidos) {
      const deLinea = lugarDeLinea(campo);
      const enviada = deLinea === null ? undefined : enviadas[deLinea.indice];
      if (esCampoCliente(campo)) {
        delCliente[campo] = mensaje;
      } else if (enviada !== undefined && deLinea !== null) {
        porLinea[enviada.id] = { ...porLinea[enviada.id], [deLinea.lugar]: mensaje };
      } else if (campo === CAMPO_LINEAS) {
        deLineas = mensaje;
      } else {
        otros.push(mensaje);
      }
    }
    setErrores(delCliente);
    setErroresPorLinea(porLinea);
    setErrorLineas(deLineas);
    setGeneral(otros.length > 0 ? otros.join(' ') : null);
  }

  async function alGrabar() {
    // Dos clics seguidos antes de que React vuelva a dibujar no envían dos veces (en el alta serían dos presupuestos).
    if (envioEnCurso.current) {
      return;
    }
    setGeneral(null);
    setErroresPorLinea({});
    const validacion = validarCliente(textos);
    const errorDeLineas = validarLineas(lineas.length);
    setErrores(validacion.tipo === 'errores' ? validacion.errores : {});
    setErrorLineas(errorDeLineas);
    // Los mensajes de cantidad, descuento e importes ya están en pantalla: se derivan de lo escrito al dibujar.
    const aGrabar = lineasAGrabar(filas);
    const erroresEnLineas = aGrabar === null || resumen.excedido;
    if (erroresEnLineas) {
      // Sin esto, el lector de pantalla no recibe respuesta: esos mensajes ya estaban y no se vuelven a anunciar.
      setPedidosDeFoco((pedidos) => pedidos + 1);
    }
    if (validacion.tipo === 'errores' || errorDeLineas !== undefined || erroresEnLineas) {
      return;
    }

    const enviadas = lineas;
    envioEnCurso.current = true;
    setEnviando(true);
    const resultado = await grabar(validacion.datos, aGrabar);
    envioEnCurso.current = false;
    setEnviando(false);
    switch (resultado.tipo) {
      case 'ok':
        navegar(`/presupuestos/${resultado.datos.numero}`, {
          state: { mensaje: mensajeConfirmacion(resultado.datos.numero) },
        });
        break;
      case 'campos':
      case 'campo':
        mostrarErrores(resultado.tipo === 'campos' ? resultado.errores : [resultado], enviadas);
        break;
      case 'sinSesion':
        navegar('/ingreso', { replace: true });
        break;
      default:
        // Incluye el 409 de la modificación: el texto propio de su motivo (D-07), nunca el del servidor.
        setGeneral(resultado.mensaje);
        break;
    }
  }

  return (
    <>
      <fieldset ref={refCliente}>
        <legend>Cliente</legend>
        <CampoCliente
          etiqueta="Apellido"
          nombre={CAMPO_APELLIDO}
          valor={textos.apellido}
          error={errores.apellido}
          largoMaximo={LARGO_MAXIMO_APELLIDO}
          alCambiar={(valor) => cambiar(CAMPO_APELLIDO, valor)}
        />
        <CampoCliente
          etiqueta="Nombre"
          nombre={CAMPO_NOMBRE}
          valor={textos.nombre}
          error={errores.nombre}
          largoMaximo={LARGO_MAXIMO_NOMBRE}
          alCambiar={(valor) => cambiar(CAMPO_NOMBRE, valor)}
        />
        <CampoCliente
          etiqueta="DNI"
          nombre={CAMPO_DNI}
          valor={textos.dni}
          error={errores.dni}
          largoMaximo={LARGO_MAXIMO_DNI_ESCRITO}
          alCambiar={(valor) => cambiar(CAMPO_DNI, valor)}
        />
        <CampoCliente
          etiqueta="Domicilio"
          nombre={CAMPO_DOMICILIO}
          valor={textos.domicilio}
          error={errores.domicilio}
          largoMaximo={LARGO_MAXIMO_DOMICILIO}
          alCambiar={(valor) => cambiar(CAMPO_DOMICILIO, valor)}
        />
        <CampoCliente
          etiqueta="Email"
          nombre={CAMPO_EMAIL}
          valor={textos.email}
          error={errores.email}
          largoMaximo={LARGO_MAXIMO_EMAIL}
          tipo="email"
          alCambiar={(valor) => cambiar(CAMPO_EMAIL, valor)}
        />
        <CampoCliente
          etiqueta="Nro. de Teléfono"
          nombre={CAMPO_TELEFONO}
          valor={textos.telefono}
          error={errores.telefono}
          largoMaximo={LARGO_MAXIMO_TELEFONO}
          tipo="tel"
          alCambiar={(valor) => cambiar(CAMPO_TELEFONO, valor)}
        />
      </fieldset>

      <h2>Buscar artículos</h2>
      {/* setGeneral es estable: el efecto de búsqueda del buscador no se vuelve a disparar en cada dibujo. */}
      <BuscadorArticulos alAgregar={agregar} alError={setGeneral} />

      <h2>Líneas</h2>
      <div className="tabla-contenedor">
        <table
          ref={refTabla}
          className="tabla"
          aria-describedby={(localLineas ?? errorLineas) !== undefined ? idErrorLineas : undefined}
        >
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
              <th scope="col">Acción</th>
            </tr>
          </thead>
          <tbody>
            {filas.map((fila, indice) => (
              <FilaLinea
                key={fila.linea.id}
                fila={fila}
                numero={indice + 1}
                errores={erroresPorLinea[fila.linea.id] ?? {}}
                alCambiarCantidad={(valor) => cambiarLinea(fila.linea.id, 'cantidad', valor)}
                alCambiarDescuento={(valor) => cambiarLinea(fila.linea.id, 'descuento', valor)}
                alQuitar={() => quitar(fila.linea.id)}
              />
            ))}
          </tbody>
          <tfoot>
            {/* FR-08 y D-10: un solo total, con IVA incluido, sin discriminarlo. */}
            <tr>
              <th scope="row" colSpan={6}>
                Total (IVA incluido)
              </th>
              <td className="tabla__importe">{importeOSinDato(resumen.total)}</td>
              <td />
            </tr>
          </tfoot>
        </table>
      </div>
      <Mensaje id={idErrorLineas} local={localLineas} servidor={errorLineas} enfocable />

      {general !== null && (
        <p className="mensaje-general" role="alert">
          {general}
        </p>
      )}
      {/*
        Grabar está fuera de un <form> a propósito: el buscador ya es un form y los forms no se anidan. Además, así
        Enter en un dato del cliente no graba: un presupuesto grabado no se puede eliminar, y no tiene que crearse por
        error.
      */}
      <button type="button" disabled={enviando} onClick={() => void alGrabar()}>
        Grabar
      </button>
    </>
  );
}
