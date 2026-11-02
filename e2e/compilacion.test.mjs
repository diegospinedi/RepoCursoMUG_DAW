// Pruebas unitarias de compilacion.mjs (sin navegador ni Api): `npm run test:unidad` en e2e/.
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';

import * as compilacion from './compilacion.mjs';

const { comandosDeCompilacion, debeBorrarse, verificarModulosDeLaWeb } = compilacion;

/** Todavía no existen antes de esta ronda: se toman del módulo para que la prueba falle en su aserción, no al importar. */
const leerPid = (texto) => {
  assert.equal(typeof compilacion.leerPid, 'function', 'compilacion.mjs no exporta leerPid.');
  return compilacion.leerPid(texto);
};
const mensajeDeFallo = (...argumentos) => {
  assert.equal(typeof compilacion.mensajeDeFallo, 'function', 'compilacion.mjs no exporta mensajeDeFallo.');
  return compilacion.mensajeDeFallo(...argumentos);
};

/** Rutas como las de la PC de desarrollo en Windows: espacios, & y % (que cmd.exe interpretaría). */
const REPOSITORIO = 'C:\\Repos\\Curso & Prueba\\Modulo 6';
const API = 'C:\\Temp\\optica-e2e-%PERFIL%\\carpeta con espacios\\api';

describe('comandosDeCompilacion', () => {
  for (const plataforma of ['win32', 'linux', 'darwin']) {
    test(`${plataforma}: dotnet sin intérprete de comandos y con las rutas intactas`, () => {
      const dotnet = comandosDeCompilacion({ repositorio: REPOSITORIO, api: API, plataforma }).find(
        (c) => c.comando === 'dotnet',
      );

      assert.ok(dotnet, 'Falta el comando de dotnet.');
      assert.equal(dotnet.opciones.shell, false, 'dotnet no puede pasar por el intérprete de comandos.');
      // Cada ruta es un solo argumento, tal cual: sin comillas agregadas ni partida en los espacios.
      assert.ok(dotnet.argumentos.includes(API), 'La carpeta de salida no llega como un solo argumento.');
      assert.ok(
        dotnet.argumentos.some((a) => a.startsWith(REPOSITORIO) && a.endsWith('Optica.Api.csproj')),
        'El proyecto no llega como un solo argumento.',
      );
      assert.equal(dotnet.opciones.cwd, REPOSITORIO);
    });
  }

  test('win32: npm.cmd es el único con intérprete de comandos, y con argumentos fijos', () => {
    const comandos = comandosDeCompilacion({ repositorio: REPOSITORIO, api: API, plataforma: 'win32' });

    for (const comando of comandos.filter((c) => c.opciones.shell)) {
      assert.equal(comando.comando, 'npm.cmd');
      // Ningún argumento variable: nada que cmd.exe pueda partir o interpretar.
      for (const argumento of comando.argumentos) {
        assert.match(argumento, /^[a-z:]+$/);
      }
    }
    const npm = comandos.find((c) => c.comando === 'npm.cmd');
    assert.ok(npm, 'En Windows npm se invoca como npm.cmd.');
    assert.equal(npm.opciones.shell, true);
    // La carpeta va como directorio de trabajo, no como argumento.
    assert.equal(npm.opciones.cwd, join(REPOSITORIO, 'web'));
  });

  test('linux: nada pasa por el intérprete de comandos', () => {
    const comandos = comandosDeCompilacion({ repositorio: REPOSITORIO, api: API, plataforma: 'linux' });

    assert.deepEqual(
      comandos.map((c) => [c.comando, c.opciones.shell]),
      [
        ['npm', false],
        ['dotnet', false],
      ],
    );
  });
});

describe('verificarModulosDeLaWeb', () => {
  const base = mkdtempSync(join(tmpdir(), 'optica-e2e-unidad-'));
  after(() => rmSync(base, { recursive: true, force: true }));

  function webCon(...paquetes) {
    const web = mkdtempSync(join(base, 'web-'));
    for (const paquete of paquetes) {
      mkdirSync(join(web, 'node_modules', paquete), { recursive: true });
    }
    return web;
  }

  test('módulos de la misma plataforma: no falla', () => {
    const web = webCon('@rolldown/binding-linux-x64-gnu', 'lightningcss-linux-x64-gnu', 'lightningcss');

    assert.doesNotThrow(() => verificarModulosDeLaWeb(web, 'linux', 'x64'));
  });

  test('módulos instalados en Linux (WSL) usados desde Windows: error claro', () => {
    const web = webCon('@rolldown/binding-linux-x64-gnu', 'lightningcss-linux-x64-gnu');

    assert.throws(
      () => verificarModulosDeLaWeb(web, 'win32', 'x64'),
      (error) => /otra plataforma/.test(error.message) && /linux-x64/.test(error.message) && /npm ci/.test(error.message),
    );
  });

  test('módulos instalados en Windows usados desde Linux: error claro', () => {
    const web = webCon('@rolldown/binding-win32-x64-msvc');

    assert.throws(() => verificarModulosDeLaWeb(web, 'linux', 'x64'), /otra plataforma.*win32-x64/s);
  });

  test('sin node_modules: pide npm ci', () => {
    const web = mkdtempSync(join(base, 'vacia-'));

    assert.throws(() => verificarModulosDeLaWeb(web, 'linux', 'x64'), /npm ci/);
  });
});

describe('debeBorrarse (limpieza de corridas anteriores)', () => {
  const vivo = () => true;
  const muerto = () => false;
  const minuto = 60_000;

  test('con el proceso vivo, nunca', () => {
    assert.equal(debeBorrarse({ pid: 1234, vive: vivo, edadMs: 60 * minuto }), false);
  });

  test('con el proceso terminado, sí', () => {
    assert.equal(debeBorrarse({ pid: 1234, vive: muerto, edadMs: 1_000 }), true);
  });

  test('sin pid y reciente (otra corrida que todavía no lo escribió): no', () => {
    assert.equal(debeBorrarse({ pid: NaN, vive: muerto, edadMs: 9 * minuto }), false);
  });

  test('sin pid y con 10 minutos o más: sí', () => {
    assert.equal(debeBorrarse({ pid: NaN, vive: muerto, edadMs: 10 * minuto }), true);
  });

  // process.kill(0, 0) le pega al grupo del proceso actual y "vive": un pid 0 o negativo no es un proceso.
  for (const texto of ['', '0', '-5']) {
    const nombre = JSON.stringify(texto);

    test(`archivo pid ${nombre}: no es un proceso vivo, cuenta como sin pid`, () => {
      const nuncaSeConsulta = () => {
        throw new Error(`Se consultó si vive el pid ${nombre}.`);
      };

      assert.equal(debeBorrarse({ pid: Number(texto), vive: nuncaSeConsulta, edadMs: 10 * minuto }), true);
      assert.equal(debeBorrarse({ pid: Number(texto), vive: nuncaSeConsulta, edadMs: 9 * minuto }), false);
    });

    test(`leerPid(${nombre}): no es un pid`, () => {
      assert.ok(Number.isNaN(leerPid(texto)), `leerPid(${nombre}) devolvió ${leerPid(texto)}.`);
    });
  }

  test('leerPid de un pid válido, con salto de línea: el número', () => {
    assert.equal(leerPid('1234\n'), 1234);
  });
});

describe('mensajeDeFallo (comandos de compilación)', () => {
  test('terminó bien: sin mensaje', () => {
    assert.equal(mensajeDeFallo('dotnet', ['build'], { status: 0, signal: null }), null);
  });

  test('el comando no existe: lo nombra, no dice "código null"', () => {
    const error = Object.assign(new Error('spawnSync dotnet ENOENT'), { code: 'ENOENT' });

    const mensaje = mensajeDeFallo('dotnet', ['build'], { status: null, signal: null, error });

    assert.match(mensaje, /No se encontró el comando "dotnet"/);
    assert.doesNotMatch(mensaje, /null/);
  });

  test('no se pudo lanzar por otro motivo: nombra el comando y el código de error', () => {
    const error = Object.assign(new Error('spawnSync npm EACCES'), { code: 'EACCES' });

    const mensaje = mensajeDeFallo('npm', ['run', 'build'], { status: null, signal: null, error });

    assert.match(mensaje, /"npm run"/);
    assert.match(mensaje, /EACCES/);
    assert.doesNotMatch(mensaje, /null/);
  });

  test('terminó con error: el código de salida', () => {
    assert.equal(
      mensajeDeFallo('dotnet', ['build'], { status: 1, signal: null }),
      'Falló "dotnet build" (código 1).',
    );
  });

  test('terminó por una señal: la señal', () => {
    assert.equal(
      mensajeDeFallo('dotnet', ['build'], { status: null, signal: 'SIGKILL' }),
      'Falló "dotnet build" (código SIGKILL).',
    );
  });
});
