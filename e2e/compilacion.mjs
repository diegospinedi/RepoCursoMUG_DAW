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
