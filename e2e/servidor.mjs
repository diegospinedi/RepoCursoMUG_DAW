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

import { spawn, spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { get } from 'node:https';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  comandosDeCompilacion,
  debeBorrarse,
  esperarApi,
  leerInstancias,
  leerPid,
  mensajeDeFallo,
  verificarModulosDeLaWeb,
} from './compilacion.mjs';

const PREFIJO = 'optica-e2e-';
const repositorio = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// Una instancia por proyecto de Playwright (navegador y archivo de pruebas); un nombre mal formado detiene todo acá.
const instancias = leerInstancias(process.env.OPTICA_E2E_INSTANCIAS);

limpiarCorridasAnteriores();
const raiz = mkdtempSync(join(tmpdir(), PREFIJO));
writeFileSync(join(raiz, 'pid'), String(process.pid));
const hijos = [];
let terminando = false;

process.on('SIGTERM', () => void terminar(0));
process.on('SIGINT', () => void terminar(0));

try {
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

  const hijo = spawn('dotnet', [join(api, 'Optica.Api.dll')], {
    cwd: api,
    stdio: ['ignore', 'inherit', 'inherit'],
    env: {
      ...process.env,
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

/** Detiene las Api, espera que liberen la base y borra el directorio temporal. */
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
  process.exit(codigo);
}

/** Borra los directorios de corridas anteriores cuyo proceso ya no existe (en Windows no se borran al terminar). */
function limpiarCorridasAnteriores() {
  for (const entrada of readdirSync(tmpdir(), { withFileTypes: true })) {
    if (!entrada.isDirectory() || !entrada.name.startsWith(PREFIJO)) {
      continue;
    }
    const directorio = join(tmpdir(), entrada.name);
    let pid = NaN;
    let edadMs = 0;
    try {
      edadMs = Date.now() - statSync(directorio).mtimeMs;
      pid = leerPid(readFileSync(join(directorio, 'pid'), 'utf8'));
    } catch {
      // Sin archivo pid: una corrida que no llegó a escribirlo (o que lo está por escribir).
    }
    if (debeBorrarse({ pid, vive, edadMs })) {
      borrar(directorio);
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
