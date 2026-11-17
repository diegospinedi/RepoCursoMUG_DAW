// Servidor de la suite e2e (lo arranca el webServer de playwright.config.ts).
//
// 1. Compila la web (npm run build en web/) y la Api (Release) en un directorio temporal.
// 2. Por cada proyecto de OPTICA_E2E_INSTANCIAS ("nombre:puerto,...", un proyecto por navegador y por archivo de
//    pruebas), levanta una Api con su base y su marca de prueba en directorios temporales propios, y espera a que
//    responda antes de levantar la siguiente. Si una Api termina o no responde, servidor.mjs termina con código 1.
// 3. Al recibir SIGTERM o SIGINT detiene las Api y borra el directorio temporal.
//
// Windows no tiene señales: Playwright termina el árbol de procesos sin aviso y el directorio queda. Por eso, al
// empezar, se borran los directorios de corridas anteriores cuyo proceso ya no existe (ver debeBorrarse en
// compilacion.mjs: uno sin pid se respeta mientras sea reciente, porque puede ser de otra corrida que recién empieza).
//
// Nada de esto toca datos reales: base, claves de Data Protection y marca son de prueba y viven en el directorio
// temporal. La marca de prueba es genérica (no es la de la óptica).
//
// FEAT-010 (D-16): cada instancia tiene además sus carpetas de backup (carpetasDeBackup en compilacion.mjs), una raíz
// `optica-e2e-backup-<puerto>` en la carpeta temporal y otra en otro punto de montaje (/dev/shm en Linux) si lo hay.
// Antes de crear o borrar una, revisarCarpetaDeBackup comprueba que sea una temporal propia. Una vez elegida la
// ubicación, el programador de la Api puede copiar ahí: por eso se borran después de detener las Api.

import { spawn, spawnSync } from 'node:child_process';
import {
  chmodSync,
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  unlinkSync,
  writeFileSync,
} from 'node:fs';
import { get } from 'node:https';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  baseDeOtroMontaje,
  carpetasDeBackup,
  comandosDeCompilacion,
  contadorSimulado,
  debeBorrarse,
  entornoDeLaApi,
  esDelUsuario,
  esperarApi,
  inicioEnStat,
  leerArchivoPid,
  leerInstancias,
  mensajeDeCarpetasEnUso,
  mensajeDeFallo,
  PREFIJO_BACKUP,
  revisarArcaPorArchivo,
  revisarCarpetaDeBackup,
  tratamientoDeSinEscritura,
  verificarModulosDeLaWeb,
} from './compilacion.mjs';

const PREFIJO = 'optica-e2e-';
const repositorio = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// Una instancia por proyecto de Playwright (navegador y archivo de pruebas); un nombre mal formado detiene todo acá.
const instancias = leerInstancias(process.env.OPTICA_E2E_INSTANCIAS);

// Las bases de las carpetas de backup (D-16): la carpeta temporal y, si hay, otro punto de montaje.
const temporal = tmpdir();
const otroMontaje = baseDeOtroMontaje({ temporal });
const basesDeBackup = [temporal, otroMontaje];
/** Las raíces de backup que creó esta corrida, para borrarlas al terminar. */
const raicesDeBackup = [];
/**
 * El contenido de los archivos pid de esta corrida: el pid y la hora de inicio del proceso (Linux), así un pid que otro
 * proceso reutilizó después no hace pasar por viva una corrida terminada (leerArchivoPid y corridaViva).
 */
const contenidoPid = `${process.pid} ${inicioDe(process.pid) ?? ''}`.trim();

limpiarCorridasAnteriores();
const raiz = mkdtempSync(join(tmpdir(), PREFIJO));
writeFileSync(join(raiz, 'pid'), contenidoPid);
const hijos = [];
let terminando = false;
if (otroMontaje === null) {
  console.log(
    '[e2e] Sin un segundo punto de montaje: el caso positivo de AC-01 de FEAT-010 y la carpeta sin escritura se saltan.',
  );
}

process.on('SIGTERM', () => void terminar(0));
process.on('SIGINT', () => void terminar(0));

try {
  // Un valor de ARCA simulada que la Api no admite detiene todo antes de compilar, nombrando el archivo de pruebas.
  revisarArcaPorArchivo();
  verificarModulosDeLaWeb(join(repositorio, 'web'));
  const api = join(raiz, 'api');
  for (const { comando, argumentos, opciones } of comandosDeCompilacion({ repositorio, api })) {
    ejecutar(comando, argumentos, opciones);
  }

  for (const instancia of instancias) {
    await levantar(instancia, api);
  }
  console.log(`[e2e] Listo: ${instancias.map((i) => `${i.nombre} en https://localhost:${i.puerto}`).join(', ')}.`);
} catch (error) {
  console.error(`[e2e] ${error instanceof Error ? error.message : String(error)}`);
  await terminar(1);
}

/**
 * Corre un comando de compilación y falla si no termina bien. Si usa o no el intérprete de comandos lo decide
 * comandosDeCompilacion: solo npm.cmd en Windows, con argumentos fijos; dotnet nunca.
 */
function ejecutar(comando, argumentos, { cwd, shell }) {
  const resultado = spawnSync(comando, argumentos, { cwd, shell, stdio: 'inherit' });
  const mensaje = mensajeDeFallo(comando, argumentos, resultado);
  if (mensaje !== null) {
    throw new Error(mensaje);
  }
}

/** Levanta una Api con base y marca propias, y espera a que responda. */
async function levantar({ nombre, puerto }, api) {
  const datos = join(raiz, nombre, 'datos');
  const marca = join(raiz, nombre, 'marca');
  mkdirSync(datos, { recursive: true });
  mkdirSync(marca, { recursive: true });
  writeFileSync(
    join(marca, 'branding.json'),
    JSON.stringify({ logo: 'logo.svg', colores: { primario: '#0903A0', fondo: '#FFFFFF' } }),
  );
  writeFileSync(
    join(marca, 'logo.svg'),
    '<svg xmlns="http://www.w3.org/2000/svg" width="200" height="60" viewBox="0 0 200 60">' +
      '<rect width="200" height="60" fill="#0903A0"/>' +
      '<text x="100" y="38" font-size="24" text-anchor="middle" fill="#FFFFFF">Prueba</text></svg>',
  );
  // El archivo inicial del simulador de ARCA, solo donde la prueba lo necesita (FEAT-007, AC-30). Sin archivo, el
  // simulador numera desde 1. Desde FEAT-008 el archivo también guarda los comprobantes autorizados.
  const contador = contadorSimulado(nombre);
  if (contador !== null) {
    writeFileSync(join(datos, 'arca-simulado.json'), JSON.stringify(contador));
  }
  crearCarpetasDeBackup(puerto);

  const hijo = spawn('dotnet', [join(api, 'Optica.Api.dll')], {
    cwd: api,
    stdio: ['ignore', 'inherit', 'inherit'],
    env: {
      // Siempre ARCA simulada, con la respuesta que corresponde al archivo de pruebas y sin credenciales: ninguna clave
      // de ARCA del entorno de quien corre la suite llega a la Api, en ninguna mayúscula ni prefijo (FEAT-007).
      ...entornoDeLaApi(process.env, nombre),
      ASPNETCORE_ENVIRONMENT: 'Production',
      DOTNET_ENVIRONMENT: 'Production',
      AllowedHosts: 'localhost',
      // Sin el SQL de cada comando en la salida de la suite.
      'Logging__LogLevel__Microsoft.EntityFrameworkCore': 'Warning',
      Kestrel__Endpoints__Https__Url: `https://localhost:${puerto}`,
      Optica__Datos__Directorio: datos,
      Optica__Marca__Directorio: marca,
      Optica__Web__Directorio: join(repositorio, 'web', 'dist'),
    },
  });
  hijos.push(hijo);
  hijo.on('exit', (codigo, senal) => {
    if (!terminando) {
      console.error(`[e2e] La Api de ${nombre} terminó sola (${codigo ?? senal}).`);
      void terminar(1);
    }
  });

  // Si la Api termina antes de responder (o no responde en el plazo), esto falla y servidor.mjs termina con código 1:
  // Playwright no corre ninguna prueba.
  await esperarApi({
    nombre,
    puerto,
    responde: () => responde(`https://localhost:${puerto}/api/acceso/estado`),
    terminada: () => hijo.exitCode ?? hijo.signalCode,
  });
}

/**
 * Crea las carpetas de backup de la instancia del puerto (FEAT-010, D-16; `carpetasDeBackup`): cada raíz con el pid de
 * esta corrida, la carpeta de la misma unidad y, con otro punto de montaje, el destino y la carpeta sin escritura (555).
 * La inexistente nunca se crea. Una raíz que ya existe a esta altura es de otra corrida viva (las de corridas muertas
 * ya las borró limpiarCorridasAnteriores): se detiene en lugar de compartirla.
 */
function crearCarpetasDeBackup(puerto) {
  const carpetas = carpetasDeBackup({ puerto, temporal, otroMontaje });
  for (const raizDeBackup of [carpetas.mismoMontaje, carpetas.otroMontaje]) {
    if (raizDeBackup === null) {
      continue;
    }
    revisarCarpetaDeBackup(raizDeBackup, basesDeBackup);
    if (existsSync(raizDeBackup)) {
      throw new Error(mensajeDeCarpetasEnUso(puerto));
    }
    // Solo el usuario que corre la suite: /dev/shm y /tmp son de todos.
    mkdirSync(raizDeBackup, { mode: 0o700 });
    raicesDeBackup.push(raizDeBackup);
    writeFileSync(join(raizDeBackup, 'pid'), contenidoPid);
  }
  mkdirSync(carpetas.mismaUnidad);
  if (carpetas.destino !== null && carpetas.sinEscritura !== null) {
    mkdirSync(carpetas.destino, { mode: 0o700 });
    mkdirSync(carpetas.sinEscritura);
    // Con chmod y no con el modo de mkdir, que pasa por la umask.
    chmodSync(carpetas.sinEscritura, 0o555);
  }
}

/**
 * Borra una raíz de backup de la suite, solo si es una temporal propia (revisarCarpetaDeBackup). Antes devuelve el
 * permiso de escritura a la carpeta sin escritura, por si alguna vez quedara algo adentro, pero solo si es una carpeta
 * real (lstat, tratamientoDeSinEscritura): si es un enlace, chmod cambiaría los permisos de lo apuntado, así que se
 * borra solo el enlace. rmSync tampoco sigue enlaces.
 */
function borrarCarpetaDeBackup(raizDeBackup) {
  try {
    revisarCarpetaDeBackup(raizDeBackup, basesDeBackup);
  } catch (error) {
    console.error(`[e2e] ${error instanceof Error ? error.message : String(error)}`);
    return;
  }
  const sinEscritura = join(raizDeBackup, 'sin-escritura');
  let info = null;
  try {
    info = lstatSync(sinEscritura);
  } catch {
    // Sin otro punto de montaje (o ya borrada) no existe.
  }
  try {
    switch (tratamientoDeSinEscritura(info)) {
      case 'carpeta':
        chmodSync(sinEscritura, 0o700);
        break;
      case 'enlace':
        unlinkSync(sinEscritura);
        break;
      default:
        break;
    }
  } catch (error) {
    console.error(`[e2e] No se pudo preparar el borrado de una carpeta de backup de la suite (${error?.code ?? 'error'}).`);
  }
  borrar(raizDeBackup);
}

/** La hora de inicio de un proceso (`/proc/<pid>/stat`, solo Linux), o null si no se puede leer. */
function inicioDe(pid) {
  if (process.platform !== 'linux') {
    return null;
  }
  try {
    return inicioEnStat(readFileSync(`/proc/${pid}/stat`, 'utf8'));
  } catch {
    return null;
  }
}

/** GET a la Api local sin verificar el certificado (es el de desarrollo de .NET). */
function responde(url) {
  return new Promise((listo) => {
    const pedido = get(url, { rejectUnauthorized: false, timeout: 2_000 }, (respuesta) => {
      respuesta.resume();
      listo(respuesta.statusCode === 200);
    });
    pedido.on('error', () => listo(false));
    pedido.on('timeout', () => pedido.destroy());
  });
}

/**
 * Detiene las Api, espera que liberen la base y borra el directorio temporal y las carpetas de backup (después de
 * detenerlas: el programador de una Api podría estar copiando en la suya).
 */
async function terminar(codigo) {
  if (terminando) {
    return;
  }
  terminando = true;
  const vivos = hijos.filter((hijo) => hijo.exitCode === null && hijo.signalCode === null);
  const salidas = vivos.map((hijo) => new Promise((listo) => hijo.once('exit', listo)));
  for (const hijo of vivos) {
    hijo.kill('SIGTERM');
  }
  await Promise.race([Promise.all(salidas), new Promise((listo) => setTimeout(listo, 15_000))]);
  for (const hijo of vivos) {
    if (hijo.exitCode === null && hijo.signalCode === null) {
      hijo.kill('SIGKILL');
    }
  }
  borrar(raiz);
  for (const raizDeBackup of raicesDeBackup) {
    borrarCarpetaDeBackup(raizDeBackup);
  }
  process.exit(codigo);
}

/**
 * Borra los directorios de corridas anteriores cuyo proceso ya no existe (en Windows no se borran al terminar): los de
 * la carpeta temporal y las raíces de backup del otro punto de montaje. Las raíces de backup pasan por
 * borrarCarpetaDeBackup, que revisa que sean temporales propias.
 */
function limpiarCorridasAnteriores() {
  limpiarEn(temporal, PREFIJO);
  if (otroMontaje !== null) {
    limpiarEn(otroMontaje, PREFIJO_BACKUP);
  }
}

function limpiarEn(base, prefijo) {
  for (const entrada of readdirSync(base, { withFileTypes: true })) {
    if (!entrada.isDirectory() || !entrada.name.startsWith(prefijo)) {
      continue;
    }
    const directorio = join(base, entrada.name);
    // /tmp y /dev/shm son de todos: las de otro usuario de la PC no se tocan (sin getuid, en Windows, no se compara).
    try {
      if (!esDelUsuario(lstatSync(directorio), process.getuid?.())) {
        continue;
      }
    } catch {
      continue;
    }
    let pid = NaN;
    let inicio = null;
    let edadMs = 0;
    try {
      edadMs = Date.now() - statSync(directorio).mtimeMs;
      ({ pid, inicio } = leerArchivoPid(readFileSync(join(directorio, 'pid'), 'utf8')));
    } catch {
      // Sin archivo pid: una corrida que no llegó a escribirlo (o que lo está por escribir).
    }
    if (debeBorrarse({ pid, vive, edadMs, inicio, inicioDe })) {
      if (entrada.name.startsWith(PREFIJO_BACKUP)) {
        borrarCarpetaDeBackup(directorio);
      } else {
        borrar(directorio);
      }
    }
  }
}

function vive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return error?.code === 'EPERM';
  }
}

function borrar(directorio) {
  try {
    rmSync(directorio, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
  } catch (error) {
    console.error(`[e2e] No se pudo borrar un directorio temporal de la suite (${error?.code ?? 'error'}).`);
  }
}
