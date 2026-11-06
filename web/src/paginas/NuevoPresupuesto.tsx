import { useEffect, useId, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import {
  CAMPO_APELLIDO,
  CAMPO_DNI,
  CAMPO_DOMICILIO,
  CAMPO_EMAIL,
  CAMPO_LINEAS,
  CAMPO_NOMBRE,
  CAMPO_TELEFONO,
  consultarSesion,
  grabarPresupuesto,
  type ArticuloListado,
  type ErrorDeCampo,
} from '../api/cliente';
import { mostrarImporte } from '../configuracion/numeros';
import { BuscadorArticulos } from '../presupuestos/BuscadorArticulos';
import {
  LARGO_MAXIMO_APELLIDO,
  LARGO_MAXIMO_DNI_ESCRITO,
  LARGO_MAXIMO_DOMICILIO,
  LARGO_MAXIMO_EMAIL,
  LARGO_MAXIMO_NOMBRE,
  LARGO_MAXIMO_TELEFONO,
  mensajeConfirmacion,
  validarCliente,
  validarLineas,
  type ErroresCliente,
  type TextosCliente,
} from '../presupuestos/reglas';

const VACIOS: TextosCliente = { apellido: '', nombre: '', dni: '', domicilio: '', email: '', telefono: '' };

const CAMPOS_CLIENTE: readonly (keyof TextosCliente)[] = [
  CAMPO_APELLIDO,
  CAMPO_NOMBRE,
  CAMPO_DNI,
  CAMPO_DOMICILIO,
  CAMPO_EMAIL,
  CAMPO_TELEFONO,
];

const esCampoCliente = (campo: string): campo is keyof TextosCliente => CAMPOS_CLIENTE.some((c) => c === campo);

/** `lineas[i]` de los errores de la Api (D-20): el índice de la línea enviada, o `null` si el campo es otro. */
function indiceDeLinea(campo: string): number | null {
  const coincidencia = /^lineas\[(\d+)\]$/.exec(campo);
  return coincidencia === null ? null : Number(coincidencia[1]);
}

/**
 * Una línea cargada en pantalla: el artículo elegido y un identificador propio, porque el mismo artículo puede estar
 * en más de una línea (D-16). Cantidad 1, Descuento 0 y los dos precios iguales al unitario (FR-14).
 */
interface LineaCargada {
  id: number;
  codigo: number;
  descripcion: string;
  precioUnitario: number;
}

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

interface PropsLinea {
  linea: LineaCargada;
  numero: number;
  error: string | undefined;
  alQuitar: () => void;
}

/** Una fila de la tabla de líneas (FR-13): el Precio Unitario en un campo de solo lectura (FR-11). */
function FilaLinea({ linea, numero, error, alQuitar }: PropsLinea) {
  const idError = `${useId()}-error`;
  const importe = mostrarImporte(linea.precioUnitario);
  const describe = error !== undefined ? idError : undefined;
  return (
    <tr>
      <td>{linea.codigo}</td>
      <td>{linea.descripcion}</td>
      <td className="tabla__importe">
        <input
          type="text"
          readOnly
          value={importe}
          aria-label={`Precio unitario de la línea ${numero}`}
          aria-describedby={describe}
        />
      </td>
      <td>1</td>
      <td>0</td>
      <td className="tabla__importe">{importe}</td>
      <td className="tabla__importe">{importe}</td>
      <td>
        <button type="button" aria-label={`Quitar línea ${numero}`} aria-describedby={describe} onClick={alQuitar}>
          Quitar
        </button>
        {error !== undefined && (
          <p id={idError} className="campo__error" role="alert">
            {error}
          </p>
        )}
      </td>
    </tr>
  );
}

/**
 * Alta de un presupuesto (FR-01 a FR-14): los seis datos del cliente, el buscador de artículos activos
 * ({@link BuscadorArticulos}, FR-09) y la tabla de líneas. Verifica la sesión una sola vez al montarse; sin sesión va
 * a `/ingreso` reemplazando la entrada. Valida en el navegador con las reglas del servidor y no envía si hay errores;
 * los del servidor van junto al campo, a la línea o a la tabla que indica la Api (D-20). Al grabar navega al
 * presupuesto grabado con la confirmación en el estado de la navegación (D-24), que no lleva datos del cliente.
 *
 * Datos personales (D-17): viven solo en el estado de este componente; nada en la URL, en `localStorage`,
 * `sessionStorage` ni en la consola. No hay consultas periódicas.
 */
export function NuevoPresupuesto() {
  const navegar = useNavigate();
  const idErrorLineas = `${useId()}-lineas`;
  const [verificando, setVerificando] = useState(true);
  const [textos, setTextos] = useState<TextosCliente>(VACIOS);
  const [errores, setErrores] = useState<ErroresCliente>({});
  const [general, setGeneral] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [lineas, setLineas] = useState<LineaCargada[]>([]);
  const [errorLineas, setErrorLineas] = useState<string | undefined>(undefined);
  /** Los errores de `lineas[i]` de la Api, por el identificador de la línea que se envió en la posición i. */
  const [erroresPorLinea, setErroresPorLinea] = useState<Record<number, string>>({});
  const proximaLinea = useRef(1);
  const envioEnCurso = useRef(false);

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
        setGeneral(sesion.mensaje);
      }
      setVerificando(false);
    }
    void verificar();
    return () => {
      vigente = false;
    };
  }, [navegar]);

  function cambiar(campo: keyof TextosCliente, valor: string) {
    setTextos((anteriores) => ({ ...anteriores, [campo]: valor }));
  }

  /** FR-10: copia el código, la descripción y el precio de venta (el buscador ya descartó los negativos, FR-12). */
  function agregar(articulo: ArticuloListado) {
    setErrorLineas(undefined);
    const id = proximaLinea.current++;
    setLineas((anteriores) => [
      ...anteriores,
      { id, codigo: articulo.codigo, descripcion: articulo.descripcion, precioUnitario: articulo.precioVenta },
    ]);
  }

  /** D-21: ayuda de pantalla antes de grabar; el mensaje de esa línea se va con ella. */
  function quitar(id: number) {
    setErrorLineas(undefined);
    setLineas((anteriores) => anteriores.filter((linea) => linea.id !== id));
    setErroresPorLinea((anteriores) => {
      const restantes = { ...anteriores };
      delete restantes[id];
      return restantes;
    });
  }

  /** Reparte los errores de la Api: cada dato del cliente, cada línea, la tabla y, lo que no tiene lugar, en general. */
  function mostrarErrores(recibidos: ErrorDeCampo[], enviadas: LineaCargada[]) {
    const delCliente: ErroresCliente = {};
    const porLinea: Record<number, string> = {};
    const otros: string[] = [];
    let deLineas: string | undefined;
    for (const { campo, mensaje } of recibidos) {
      const indice = indiceDeLinea(campo);
      const enviada = indice === null ? undefined : enviadas[indice];
      if (esCampoCliente(campo)) {
        delCliente[campo] = mensaje;
      } else if (enviada !== undefined) {
        porLinea[enviada.id] = mensaje;
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

  async function grabar() {
    // Dos clics seguidos antes de que React vuelva a dibujar no envían dos altas (serían dos presupuestos).
    if (envioEnCurso.current) {
      return;
    }
    setGeneral(null);
    setErroresPorLinea({});
    const validacion = validarCliente(textos);
    const errorDeLineas = validarLineas(lineas.length);
    setErrores(validacion.tipo === 'errores' ? validacion.errores : {});
    setErrorLineas(errorDeLineas);
    if (validacion.tipo === 'errores' || errorDeLineas !== undefined) {
      return;
    }

    const enviadas = lineas;
    envioEnCurso.current = true;
    setEnviando(true);
    const resultado = await grabarPresupuesto(
      validacion.datos,
      enviadas.map((linea) => linea.codigo),
    );
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
        setGeneral(resultado.mensaje);
        break;
    }
  }

  if (verificando) {
    return <p className="cargando">Cargando…</p>;
  }

  return (
    <section className="pantalla pantalla--ancha">
      <h1>Nuevo presupuesto</h1>
      <fieldset>
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
        <table className="tabla" aria-describedby={errorLineas !== undefined ? idErrorLineas : undefined}>
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
            {lineas.map((linea, indice) => (
              <FilaLinea
                key={linea.id}
                linea={linea}
                numero={indice + 1}
                error={erroresPorLinea[linea.id]}
                alQuitar={() => quitar(linea.id)}
              />
            ))}
          </tbody>
        </table>
      </div>
      {errorLineas !== undefined && (
        <p id={idErrorLineas} className="campo__error" role="alert">
          {errorLineas}
        </p>
      )}

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
      <button type="button" disabled={enviando} onClick={() => void grabar()}>
        Grabar
      </button>
      <p>
        <Link to="/">Volver al inicio</Link>
      </p>
    </section>
  );
}
