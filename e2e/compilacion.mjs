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

/**
 * Qué responde el simulador de ARCA según el archivo de pruebas (D-17): el rechazo y la falta de respuesta tienen cada
 * uno su archivo, y por lo tanto su Api; todos los demás autorizan.
 */
const RESPUESTA_DEL_SIMULADOR = {
  'facturacion-rechazo': 'rechazar',
  'facturacion-sin-respuesta': 'no_responder',
};

/** El archivo de pruebas de una instancia: su nombre sin el navegador ("chrome-facturacion-rechazo" → "facturacion-rechazo"). */
const archivoDeLaInstancia = (nombre) => nombre.slice(nombre.indexOf('-') + 1);

/**
 * Las variables `Arca__*` de la Api de una instancia (FEAT-007, D-17, R-10): siempre el entorno simulado, que no sale de
 * la máquina, con 2 s de tiempo máximo (así la prueba de "ARCA no respondió" no espera 30 s) y la respuesta del
 * simulador que corresponde a su archivo de pruebas. El CUIT y el certificado van vacíos (cuentan como ausentes): un
 * certificado que esté en el entorno de quien corre la suite nunca llega a una Api de prueba.
 */
export function variablesDeArca(nombre) {
  return {
    Arca__Entorno: 'Simulado',
    Arca__TiempoMaximoSegundos: '2',
    Arca__Simulado__Respuesta: RESPUESTA_DEL_SIMULADOR[archivoDeLaInstancia(nombre)] ?? 'autorizar',
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
 * El contador inicial del simulador (`arca-simulado.json` del directorio de datos, D-53) de una instancia, o `null` para
 * empezar sin archivo (numera desde 1). Solo `facturacion.spec.ts` arranca la Factura B del punto de venta 3 en 124: su
 * primera factura autorizada es la 0003-00000125 de AC-19 y AC-30.
 */
export function contadorSimulado(nombre) {
  return archivoDeLaInstancia(nombre) === 'facturacion' ? { contadores: { 'B-3': 124 } } : null;
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
