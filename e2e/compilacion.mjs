// Funciones de servidor.mjs separadas para probarlas sin compilar ni levantar nada (compilacion.test.mjs). Todas son
// puras salvo verificarModulosDeLaWeb, que lee web/node_modules del disco (solo lectura).

import { existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

/** Antigüedad a partir de la cual un directorio de la suite sin archivo pid se considera abandonado. */
export const ANTIGUEDAD_SIN_PID_MS = 10 * 60_000;

/**
 * Los comandos que compilan la web y la Api, en orden.
 *
 * Solo `npm.cmd` pasa por el intérprete de comandos, y solo en Windows: Node no ejecuta un .cmd de otra forma, y sus
 * argumentos son fijos (la carpeta va como directorio de trabajo, no como argumento). `dotnet` es un ejecutable y se
 * lanza directo: con el intérprete, Windows uniría los argumentos sin comillas, una ruta con espacios (la del
 * repositorio o la carpeta temporal del perfil) llegaría partida y `&`, `^` o `%` se interpretarían.
 */
export function comandosDeCompilacion({ repositorio, api, plataforma = process.platform }) {
  const windows = plataforma === 'win32';
  return [
    {
      comando: windows ? 'npm.cmd' : 'npm',
      argumentos: ['run', 'build'],
      opciones: { cwd: join(repositorio, 'web'), shell: windows },
    },
    {
      comando: 'dotnet',
      argumentos: [
        'build', join(repositorio, 'src', 'Optica.Api', 'Optica.Api.csproj'), '-c', 'Release', '-o', api, '-nologo', '-v', 'q',
      ],
      opciones: { cwd: repositorio, shell: false },
    },
  ];
}

/** Paquetes con binarios nativos de la compilación de la web: carpeta dentro de node_modules y prefijo del nombre. */
const PAQUETES_NATIVOS = [
  { carpeta: '@rolldown', prefijo: 'binding-' },
  { carpeta: '.', prefijo: 'lightningcss-' },
];

/**
 * Falla con un mensaje claro si `web/node_modules` no existe o si sus binarios nativos son de otra plataforma (por
 * ejemplo, instalados desde WSL y usados desde Windows, sobre el mismo árbol): sin esto, la compilación de la web
 * falla con un error de carga de módulo que no dice qué hacer.
 */
export function verificarModulosDeLaWeb(directorioWeb, plataforma = process.platform, arquitectura = process.arch) {
  const modulos = join(directorioWeb, 'node_modules');
  if (!existsSync(modulos)) {
    throw new Error('Falta web/node_modules: corré "npm ci" en web/ antes de la suite.');
  }

  const esta = `${plataforma}-${arquitectura}`;
  for (const { carpeta, prefijo } of PAQUETES_NATIVOS) {
    const directorio = join(modulos, carpeta);
    if (!existsSync(directorio)) {
      continue;
    }
    const binarios = readdirSync(directorio)
      .filter((nombre) => nombre.startsWith(prefijo))
      .map((nombre) => nombre.slice(prefijo.length));
    // lightningcss-<plataforma>-<arquitectura>[-libc]; los demás nombres con el prefijo no son binarios.
    const plataformas = binarios.filter((resto) => /^[a-z0-9]+-[a-z0-9]+/.test(resto));
    if (plataformas.length > 0 && !plataformas.some((resto) => resto === esta || resto.startsWith(`${esta}-`))) {
      const encontradas = [...new Set(plataformas.map((resto) => resto.split('-').slice(0, 2).join('-')))].join(', ');
      throw new Error(
        `web/node_modules se instaló en otra plataforma (encontrado: ${encontradas}; esta: ${esta}). ` +
          'Borrá web/node_modules y corré "npm ci" en web/ desde esta plataforma (en Windows, sobre una copia del ' +
          'repositorio fuera de WSL; ver e2e/README.md).',
      );
    }
  }
}

/**
 * Si un directorio de una corrida anterior se puede borrar. Con pid: solo si ese proceso ya no existe. Sin pid: solo
 * si tiene {@link ANTIGUEDAD_SIN_PID_MS} o más, porque puede ser de otra corrida que acaba de crearlo y todavía no
 * escribió su pid.
 */
export function debeBorrarse({ pid, vive, edadMs }) {
  // Un pid vacío, 0 o negativo no es un proceso: process.kill(0, 0) le pega al grupo del proceso actual y "vive".
  if (Number.isInteger(pid) && pid > 0) {
    return !vive(pid);
  }
  return edadMs >= ANTIGUEDAD_SIN_PID_MS;
}

/** El pid escrito en el archivo pid de una corrida: un entero mayor que 0, o NaN si no lo es (vacío, 0, negativo). */
export function leerPid(texto) {
  const pid = Number(texto);
  return Number.isInteger(pid) && pid > 0 ? pid : NaN;
}

/**
 * Nombre de una instancia: palabras en minúsculas unidas por un guion ("chrome", "msedge-configuracion"). Termina en
 * una ruta del directorio temporal, así que no admite barras, puntos, mayúsculas ni guiones sueltos.
 */
const NOMBRE_DE_INSTANCIA = /^[a-z]+(?:-[a-z]+)*$/;

/**
 * Las instancias de OPTICA_E2E_INSTANCIAS ("nombre:puerto,..."), que arma playwright.config.ts: una por proyecto, es
 * decir, por navegador y por archivo de pruebas.
 */
export function leerInstancias(texto) {
  const nombres = new Set();
  const instancias = (texto ?? '')
    .split(',')
    .filter(Boolean)
    .map((par) => {
      const [nombre, puerto, ...resto] = par.split(':');
      const numero = /^\d+$/.test(puerto ?? '') ? Number(puerto) : NaN;
      // Un nombre repetido compartiría base y marca con la otra instancia.
      if (
        !NOMBRE_DE_INSTANCIA.test(nombre ?? '') ||
        nombres.has(nombre) ||
        !(numero >= 1 && numero <= 65_535) ||
        resto.length > 0
      ) {
        throw new Error(`OPTICA_E2E_INSTANCIAS mal formada: "${par}".`);
      }
      nombres.add(nombre);
      return { nombre, puerto: numero };
    });
  if (instancias.length === 0) {
    throw new Error('Falta OPTICA_E2E_INSTANCIAS (la define playwright.config.ts).');
  }
  return instancias;
}

const SUFIJO_DE_PRUEBA = '.spec.ts';

/**
 * Los archivos de pruebas de tests/ (los `*.spec.ts` de `nombres`), sin la extensión y ordenados: playwright.config.ts
 * arma con ellos un proyecto por navegador y por archivo, así que una prueba nueva entra sola. Falla si un nombre no
 * sirve para una instancia (minúsculas unidas por guiones), en lugar de dejar ese archivo sin correr.
 */
export function archivosDePrueba(nombres) {
  const archivos = nombres
    .filter((nombre) => nombre.endsWith(SUFIJO_DE_PRUEBA))
    .map((nombre) => nombre.slice(0, -SUFIJO_DE_PRUEBA.length))
    .sort();
  for (const archivo of archivos) {
    if (!NOMBRE_DE_INSTANCIA.test(archivo)) {
      throw new Error(
        `tests/${archivo}${SUFIJO_DE_PRUEBA}: el nombre tiene que ser palabras en minúsculas unidas por guiones ` +
          '(por ejemplo, presupuestos-borrador.spec.ts), porque nombra su proyecto y su Api.',
      );
    }
  }
  if (archivos.length === 0) {
    throw new Error(`No hay pruebas: tests/ no tiene ningún archivo *${SUFIJO_DE_PRUEBA}.`);
  }
  return archivos;
}

/** Las respuestas del simulador que admite `Arca:Simulado:Respuesta` (OpcionesArca, FEAT-007 D-17, FEAT-008 D-19). */
const RESPUESTAS_DEL_SIMULADOR = ['autorizar', 'rechazar', 'no_responder', 'autorizar_sin_responder', 'no_responder_una_vez'];

/** Lo que admite `Arca:Simulado:Consulta` (OpcionesArca, FEAT-008 D-19). */
const CONSULTAS_DEL_SIMULADOR = ['responder', 'no_responder'];

/** El tiempo máximo de ARCA de todas las Api de la suite (`Arca__TiempoMaximoSegundos`). */
const TIEMPO_MAXIMO_SEGUNDOS = 2;

/** La espera de verificación sin `espera` en el mapa (`Arca__EsperaVerificacionSegundos`, D-20). */
const ESPERA_POR_DEFECTO_SEGUNDOS = 3;

/**
 * D-31: la Api no arranca con una espera menor que el tiempo máximo más el margen del envío (el menor entre 10 s y un
 * tercio del tiempo máximo). Con 2 s: 2 + 0,67, así que el entero mínimo es 3.
 */
const ESPERA_MINIMA_SEGUNDOS = Math.ceil(TIEMPO_MAXIMO_SEGUNDOS + Math.min(10, TIEMPO_MAXIMO_SEGUNDOS / 3));

/** Las propiedades del archivo del simulador (`arca-simulado.json`, D-13 y D-27 de FEAT-008). */
const PROPIEDADES_DEL_ARCHIVO_SIMULADO = ['contadores', 'comprobantes'];

/**
 * La clave de un contador del simulador, como la lee `SimuladorArca.EsClaveValida`: `B-<pv>` o `C-<pv>`, con el punto
 * de venta de 1 a 99999 sin ceros a la izquierda.
 */
const CLAVE_DE_CONTADOR = /^[BC]-[1-9]\d{0,4}$/;

/** El último número autorizado que admite un contador: de 0 a `NumeroComprobante.NumeroMaximo`. */
const NUMERO_MAXIMO_DE_COMPROBANTE = 99_999_999;

/**
 * Si `contadores` es lo que el simulador acepta: un objeto con claves de {@link CLAVE_DE_CONTADOR} y números enteros de
 * 0 a {@link NUMERO_MAXIMO_DE_COMPROBANTE}. Con otro valor la Api rechazaría el archivo recién en la primera emisión.
 */
const sonContadoresValidos = (contadores) =>
  esObjetoPlano(contadores) &&
  Object.entries(contadores).every(
    ([clave, valor]) =>
      CLAVE_DE_CONTADOR.test(clave) && Number.isInteger(valor) && valor >= 0 && valor <= NUMERO_MAXIMO_DE_COMPROBANTE,
  );

/**
 * ARCA simulada según el archivo de pruebas (FEAT-007 D-17; FEAT-008 D-19), por el nombre sin `.spec.ts`. Cada archivo
 * que no responde como los demás tiene su entrada, y por lo tanto su Api; los que no figuran autorizan y responden la
 * consulta. Propiedades de cada entrada:
 * - `respuesta`: `Arca__Simulado__Respuesta` (por defecto `autorizar`).
 * - `consulta`: `Arca__Simulado__Consulta` (por defecto `responder`).
 * - `espera`: `Arca__EsperaVerificacionSegundos`, entero (por defecto 3). Tiene que cumplir D-31: al menos el tiempo
 *   máximo más el margen del envío (3 con 2 s de tiempo máximo) y como mucho 3600.
 * - `simulado`: el contenido inicial de `arca-simulado.json` en el directorio de datos de la Api (por defecto, sin
 *   archivo: el simulador numera desde 1). `facturacion` lo usa con la Factura B del punto de venta 3 en 124: su
 *   primera factura autorizada es la 0003-00000125 de AC-19 y AC-30 de FEAT-007. FEAT-009: `facturas-pdf` también
 *   (la Factura B 0003-00000125 de AC-08), y `facturas-busqueda` además arranca la Factura C del punto de venta 3 en
 *   34560 (la 0003-00034561 de AC-19). `contadores` se revisa como lo lee el simulador: claves `B-<pv>` o `C-<pv>` y
 *   enteros de 0 a 99999999.
 * Un valor que no esté en estas listas detiene el harness ({@link revisarArcaPorArchivo}).
 */
export const ARCA_POR_ARCHIVO = Object.freeze({
  facturacion: { simulado: { contadores: { 'B-3': 124 } } },
  'facturacion-rechazo': { respuesta: 'rechazar' },
  'facturacion-sin-respuesta': { respuesta: 'no_responder' },
  // FEAT-009, AC-08: la primera Factura B de esta base es la 0003-00000125.
  'facturas-pdf': { simulado: { contadores: { 'B-3': 124 } } },
  // FEAT-009, AC-19 y AC-23: la primera Factura B es la 0003-00000125 y la primera Factura C, la 0003-00034561.
  'facturas-busqueda': { simulado: { contadores: { 'B-3': 124, 'C-3': 34560 } } },
  // FEAT-008, AC-02 a AC-05: ARCA autoriza y la respuesta no llega; la verificación recupera el CAE.
  'verificacion-recupera': { respuesta: 'autorizar_sin_responder' },
  // FEAT-008, AC-06 y AC-17: la primera emisión no llega a ARCA; la verificación emite.
  // 8 s de espera: el pedido de verificación de otra pestaña tiene que llegar antes de que pase (AC-17).
  'verificacion-emite': { respuesta: 'no_responder_una_vez', espera: 8 },
  // FEAT-008, AC-12: la emisión de la verificación tampoco tiene respuesta.
  // 6 s de espera: el pedido inmediato que comprueba la pendiente nueva tiene que llegar antes de que pase.
  'verificacion-sin-respuesta': { respuesta: 'no_responder', espera: 6 },
  // FEAT-008, AC-11: la consulta de la verificación no tiene respuesta.
  'verificacion-consulta-sin-respuesta': { respuesta: 'no_responder', consulta: 'no_responder' },
  // FEAT-008, AC-07 a AC-09: el número de la pendiente lo autoriza ARCA a otra venta, con otro importe.
  'verificacion-importe-distinto': { respuesta: 'no_responder_una_vez' },
});

const esObjetoPlano = (valor) => typeof valor === 'object' && valor !== null && !Array.isArray(valor);

/**
 * Falla si una entrada del mapa de ARCA por archivo tiene una propiedad desconocida o un valor que la Api no admite
 * (exactos: la Api los compara sin mayúsculas, el harness no). El mensaje nombra el archivo de pruebas y la propiedad:
 * sin esto, la Api no arrancaría con un error que no dice qué archivo corregir, o la prueba correría contra otro ARCA.
 */
function revisarEntrada(archivo, entrada) {
  const falla = (detalle) =>
    new Error(`tests/${archivo}.spec.ts: ARCA simulada mal configurada en compilacion.mjs (${detalle}).`);
  if (!esObjetoPlano(entrada)) {
    throw falla('la entrada tiene que ser un objeto');
  }
  for (const [propiedad, valor] of Object.entries(entrada)) {
    switch (propiedad) {
      case 'respuesta':
        if (!RESPUESTAS_DEL_SIMULADOR.includes(valor)) {
          throw falla(`respuesta tiene que ser ${RESPUESTAS_DEL_SIMULADOR.join(', ')}`);
        }
        break;
      case 'consulta':
        if (!CONSULTAS_DEL_SIMULADOR.includes(valor)) {
          throw falla(`consulta tiene que ser ${CONSULTAS_DEL_SIMULADOR.join(' o ')}`);
        }
        break;
      case 'espera':
        if (!Number.isInteger(valor) || valor < ESPERA_MINIMA_SEGUNDOS || valor > 3600) {
          throw falla(
            `espera tiene que ser un número entero de segundos de ${ESPERA_MINIMA_SEGUNDOS} a 3600: al menos el tiempo ` +
              `máximo de ${TIEMPO_MAXIMO_SEGUNDOS} s más el margen del envío, D-31`,
          );
        }
        break;
      case 'simulado':
        if (!esObjetoPlano(valor) || !Object.keys(valor).every((clave) => PROPIEDADES_DEL_ARCHIVO_SIMULADO.includes(clave))) {
          throw falla(`simulado tiene que ser un objeto con ${PROPIEDADES_DEL_ARCHIVO_SIMULADO.join(' o ')}`);
        }
        if (Object.hasOwn(valor, 'contadores') && !sonContadoresValidos(valor.contadores)) {
          throw falla(
            'simulado.contadores tiene que ser un objeto con claves B-<punto de venta> o C-<punto de venta> (de 1 a ' +
              `99999) y números enteros de 0 a ${NUMERO_MAXIMO_DE_COMPROBANTE}`,
          );
        }
        break;
      default:
        throw falla(`propiedad desconocida "${propiedad}"; se admiten respuesta, consulta, espera y simulado`);
    }
  }
}

/** Revisa todo el mapa de ARCA por archivo: servidor.mjs lo llama antes de compilar. */
export function revisarArcaPorArchivo(mapa = ARCA_POR_ARCHIVO) {
  for (const [archivo, entrada] of Object.entries(mapa)) {
    revisarEntrada(archivo, entrada);
  }
}

/** El archivo de pruebas de una instancia: su nombre sin el navegador ("chrome-facturacion-rechazo" → "facturacion-rechazo"). */
const archivoDeLaInstancia = (nombre) => nombre.slice(nombre.indexOf('-') + 1);

/** La entrada revisada del mapa para una instancia, o un objeto vacío si su archivo no tiene. */
function arcaDeLaInstancia(nombre, mapa) {
  const archivo = archivoDeLaInstancia(nombre);
  if (!Object.hasOwn(mapa, archivo)) {
    return {};
  }
  revisarEntrada(archivo, mapa[archivo]);
  return mapa[archivo];
}

/**
 * Las variables `Arca__*` de la Api de una instancia (FEAT-007 D-17 y R-10; FEAT-008 D-19, D-20 y D-31): siempre el
 * entorno simulado, que no sale de la máquina, con 2 s de tiempo máximo (así la prueba de "ARCA no respondió" no espera
 * 30 s), 3 s de espera antes de poder verificar una emisión pendiente (en lugar del minuto de la óptica; D-31 exige al
 * menos el tiempo máximo más el margen, 2 + 0,67 s) y la respuesta y la consulta del simulador que corresponden a su
 * archivo de pruebas ({@link ARCA_POR_ARCHIVO}). El CUIT y el certificado van vacíos (cuentan como ausentes): un
 * certificado que esté en el entorno de quien corre la suite nunca llega a una Api de prueba.
 */
export function variablesDeArca(nombre, mapa = ARCA_POR_ARCHIVO) {
  const arca = arcaDeLaInstancia(nombre, mapa);
  return {
    Arca__Entorno: 'Simulado',
    Arca__TiempoMaximoSegundos: String(TIEMPO_MAXIMO_SEGUNDOS),
    Arca__EsperaVerificacionSegundos: String(arca.espera ?? ESPERA_POR_DEFECTO_SEGUNDOS),
    Arca__Simulado__Respuesta: arca.respuesta ?? 'autorizar',
    Arca__Simulado__Consulta: arca.consulta ?? 'responder',
    Arca__Cuit: '',
    Arca__Certificado__Ruta: '',
    Arca__Certificado__Clave: '',
  };
}

/**
 * Las claves de ARCA tal como .NET las lee del entorno: `Arca__…` o `Arca:…`, también con los prefijos
 * `ASPNETCORE_` y `DOTNET_` que la Api suma a su configuración. Windows no distingue mayúsculas en el entorno y .NET
 * tampoco en las claves, así que la comparación es sin mayúsculas.
 */
const CLAVE_DE_ARCA = /^(arca|aspnetcore_arca|dotnet_arca)(__|:)/i;

/**
 * El entorno de la Api de una instancia: el de quien corre la suite sin ninguna clave de ARCA, más las de
 * {@link variablesDeArca}. Sin quitarlas, una `ARCA__ENTORNO=Produccion` o una clave de certificado del entorno de la
 * PC se sumaría a las de la instancia (en otra mayúscula o con otro prefijo no la pisa) y la Api de prueba podría
 * salir a ARCA. No modifica `entorno`.
 */
export function entornoDeLaApi(entorno, nombre) {
  const sinArca = Object.fromEntries(Object.entries(entorno).filter(([clave]) => !CLAVE_DE_ARCA.test(clave)));
  return { ...sinArca, ...variablesDeArca(nombre) };
}

/**
 * El contenido inicial del archivo del simulador (`arca-simulado.json` del directorio de datos, D-53 de FEAT-007) de una
 * instancia, según su archivo de pruebas ({@link ARCA_POR_ARCHIVO}), o `null` para empezar sin archivo (numera desde 1).
 * `facturacion.spec.ts` arranca la Factura B del punto de venta 3 en 124: su primera factura autorizada es la
 * 0003-00000125 de AC-19 y AC-30. Los de FEAT-009 (`facturas-pdf`, `facturas-busqueda`) tienen el suyo en el mapa.
 */
export function contadorSimulado(nombre, mapa = ARCA_POR_ARCHIVO) {
  return arcaDeLaInstancia(nombre, mapa).simulado ?? null;
}

/**
 * Espera a que la Api de una instancia responda. Falla si su proceso termina antes (`terminada` devuelve el código o
 * la señal; null mientras sigue vivo) o si no responde en `plazoMs`. El reloj y la espera se reciben para probarla sin
 * dormir.
 */
export async function esperarApi({
  nombre,
  puerto,
  responde,
  terminada,
  plazoMs = 120_000,
  intervaloMs = 500,
  ahora = Date.now,
  dormir = (ms) => new Promise((listo) => setTimeout(listo, ms)),
}) {
  const limite = ahora() + plazoMs;
  while (ahora() < limite) {
    const fin = terminada();
    if (fin !== null && fin !== undefined) {
      throw new Error(`La Api de ${nombre} terminó antes de responder (${fin}).`);
    }
    if (await responde()) {
      return;
    }
    await dormir(intervaloMs);
  }
  throw new Error(`La Api de ${nombre} no respondió en el puerto ${puerto}.`);
}

/**
 * El mensaje de error de un comando de compilación que no terminó bien, o null si terminó bien. `resultado` es el de
 * `spawnSync`: si el comando no se pudo lanzar, trae `error` y no tiene código de salida. Solo se usa el código del
 * error, no su texto.
 */
export function mensajeDeFallo(comando, argumentos, resultado) {
  if (resultado.error) {
    return resultado.error.code === 'ENOENT'
      ? `No se encontró el comando "${comando}": instalalo o agregalo al PATH.`
      : `No se pudo ejecutar "${comando} ${argumentos[0]}" (${resultado.error.code ?? 'error'}).`;
  }
  if (resultado.status === 0) {
    return null;
  }
  return `Falló "${comando} ${argumentos[0]}" (código ${resultado.status ?? resultado.signal}).`;
}
