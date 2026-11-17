// Pruebas unitarias de compilacion.mjs (sin navegador ni Api): `npm run test:unidad` en e2e/.
import assert from 'node:assert/strict';
import { lstatSync, mkdirSync, mkdtempSync, readdirSync, rmSync, symlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { after, describe, test } from 'node:test';

import {
  ARCA_POR_ARCHIVO,
  archivosDePrueba,
  baseDeOtroMontaje,
  carpetasDeBackup,
  comandosDeCompilacion,
  contadorSimulado,
  corridaViva,
  debeBorrarse,
  entornoDeLaApi,
  esDelUsuario,
  esperarApi,
  inicioEnStat,
  leerArchivoPid,
  leerInstancias,
  leerPid,
  mensajeDeCarpetasEnUso,
  mensajeDeFallo,
  montajesDeMountinfo,
  puntoDeMontajeDe,
  revisarArcaPorArchivo,
  revisarCarpetaDeBackup,
  tratamientoDeSinEscritura,
  variablesDeArca,
  verificarModulosDeLaWeb,
} from './compilacion.mjs';

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

describe('carpetas de backup de la suite (FEAT-010, Block 8, D-16)', () => {
  // Bases absolutas en la plataforma que corre la prueba (en Windows, '/dev/shm' no sería una ruta completa).
  const TEMPORAL = join(tmpdir(), 'base-temporal');
  const OTRO_MONTAJE = join(tmpdir(), 'base-otro-montaje');
  const BASES = [TEMPORAL, OTRO_MONTAJE];

  test('con otro punto de montaje: destino y sin escritura ahí; misma unidad e inexistente junto a los datos', () => {
    const carpetas = carpetasDeBackup({ puerto: 5543, temporal: TEMPORAL, otroMontaje: OTRO_MONTAJE });

    assert.deepEqual(carpetas, {
      mismoMontaje: join(TEMPORAL, 'optica-e2e-backup-5543'),
      otroMontaje: join(OTRO_MONTAJE, 'optica-e2e-backup-5543'),
      destino: join(OTRO_MONTAJE, 'optica-e2e-backup-5543', 'destino'),
      sinEscritura: join(OTRO_MONTAJE, 'optica-e2e-backup-5543', 'sin-escritura'),
      mismaUnidad: join(TEMPORAL, 'optica-e2e-backup-5543', 'misma-unidad'),
      inexistente: join(TEMPORAL, 'optica-e2e-backup-5543', 'no-existe'),
    });
  });

  test('sin otro punto de montaje: no hay destino ni carpeta sin escritura (el caso positivo se salta)', () => {
    const carpetas = carpetasDeBackup({ puerto: 5600, temporal: TEMPORAL, otroMontaje: null });

    assert.equal(carpetas.otroMontaje, null);
    assert.equal(carpetas.destino, null);
    assert.equal(carpetas.sinEscritura, null);
    assert.equal(carpetas.mismaUnidad, join(TEMPORAL, 'optica-e2e-backup-5600', 'misma-unidad'));
  });

  for (const puerto of [0, 65_536, 5543.5, Number.NaN, '5543']) {
    test(`carpetasDeBackup con el puerto ${String(puerto)} (${typeof puerto}): rechazado`, () => {
      assert.throws(
        () => carpetasDeBackup({ puerto, temporal: TEMPORAL, otroMontaje: OTRO_MONTAJE }),
        /puerto/,
      );
    });
  }

  test('carpetasDeBackup con una base relativa: rechazado', () => {
    assert.throws(() => carpetasDeBackup({ puerto: 5543, temporal: 'tmp', otroMontaje: null }), /absoluta/);
  });

  describe('baseDeOtroMontaje', () => {
    const dispositivos = (mapa) => (ruta) => (Object.hasOwn(mapa, ruta) ? mapa[ruta] : null);
    /** Sin /proc/self/mountinfo legible: decide el dispositivo. */
    const sinMountinfo = () => [];

    test('Linux con /dev/shm en otro dispositivo que la carpeta temporal: /dev/shm', () => {
      const base = baseDeOtroMontaje({
        plataforma: 'linux',
        temporal: '/tmp',
        dispositivo: dispositivos({ '/tmp': 2096, '/dev/shm': 65 }),
        montajes: sinMountinfo,
      });

      assert.equal(base, '/dev/shm');
    });

    test('Linux con /dev/shm en el mismo dispositivo: ninguna (no es otra unidad)', () => {
      const base = baseDeOtroMontaje({
        plataforma: 'linux',
        temporal: '/tmp',
        dispositivo: dispositivos({ '/tmp': 65, '/dev/shm': 65 }),
        montajes: sinMountinfo,
      });

      assert.equal(base, null);
    });

    test('Linux sin /dev/shm: ninguna', () => {
      assert.equal(
        baseDeOtroMontaje({
          plataforma: 'linux',
          temporal: '/tmp',
          dispositivo: dispositivos({ '/tmp': 2096 }),
          montajes: sinMountinfo,
        }),
        null,
      );
    });

    // Con mountinfo decide el punto de montaje de prefijo más largo, el mismo criterio de la Api (DriveInfo).
    test('mountinfo: /dev/shm y la carpeta temporal en puntos de montaje distintos: /dev/shm', () => {
      const base = baseDeOtroMontaje({
        plataforma: 'linux',
        temporal: '/tmp',
        dispositivo: dispositivos({ '/tmp': 2096, '/dev/shm': 65 }),
        montajes: () => ['/', '/dev', '/dev/shm'],
      });

      assert.equal(base, '/dev/shm');
    });

    test('mountinfo: dispositivos distintos en el mismo punto de montaje (subvolumen de btrfs): ninguna', () => {
      const base = baseDeOtroMontaje({
        plataforma: 'linux',
        temporal: '/tmp',
        dispositivo: dispositivos({ '/tmp': 40, '/dev/shm': 41 }),
        montajes: () => ['/'],
      });

      assert.equal(base, null, 'La Api los toma como la misma unidad: AC-01 fallaría.');
    });

    test('mountinfo: el mismo dispositivo en otro punto de montaje (bind): /dev/shm, como lo ve la Api', () => {
      const base = baseDeOtroMontaje({
        plataforma: 'linux',
        temporal: '/tmp',
        dispositivo: dispositivos({ '/tmp': 65, '/dev/shm': 65 }),
        montajes: () => ['/', '/dev/shm'],
      });

      assert.equal(base, '/dev/shm');
    });

    test('Windows: ninguna (no hay un segundo punto de montaje conocido)', () => {
      const nuncaSeConsulta = () => {
        throw new Error('En Windows no se consulta ningún dispositivo.');
      };

      assert.equal(baseDeOtroMontaje({ plataforma: 'win32', temporal: 'C:\\Temp', dispositivo: nuncaSeConsulta }), null);
    });
  });

  describe('revisarCarpetaDeBackup (el harness solo crea y borra sus carpetas temporales)', () => {
    test('la carpeta propia de la instancia, en cualquiera de las dos bases: aceptada', () => {
      const carpetas = carpetasDeBackup({ puerto: 5543, temporal: TEMPORAL, otroMontaje: OTRO_MONTAJE });

      assert.doesNotThrow(() => revisarCarpetaDeBackup(carpetas.mismoMontaje, BASES));
      assert.doesNotThrow(() => revisarCarpetaDeBackup(carpetas.otroMontaje, BASES));
    });

    const rechazadas = {
      'una carpeta de backup real fuera de las bases': join(tmpdir(), 'Usuarios', 'Prueba', 'Backups'),
      'la base misma': OTRO_MONTAJE,
      'una subcarpeta de la carpeta propia': join(OTRO_MONTAJE, 'optica-e2e-backup-5543', 'destino'),
      'un nombre sin el prefijo de la suite': join(OTRO_MONTAJE, 'Backups'),
      'el prefijo sin un puerto': join(OTRO_MONTAJE, 'optica-e2e-backup-x'),
      'el prefijo dentro de otra carpeta de la base': join(OTRO_MONTAJE, 'otra', 'optica-e2e-backup-5543'),
      'una ruta relativa': join('optica-e2e-backup-5543'),
      'una ruta con ".." que sale de la base': `${OTRO_MONTAJE}${sep}optica-e2e-backup-5543${sep}..${sep}..${sep}x`,
      'una ruta con ".." que vuelve a la base (sin normalizar)': `${OTRO_MONTAJE}${sep}x${sep}..${sep}optica-e2e-backup-5543`,
      'una ruta vacía': '',
    };

    for (const [caso, ruta] of Object.entries(rechazadas)) {
      test(`${caso}: rechazada, sin repetir la ruta en el mensaje`, () => {
        assert.throws(
          () => revisarCarpetaDeBackup(ruta, BASES),
          (error) => {
            assert.match(error.message, /temporal propia/);
            if (ruta !== '') {
              assert.ok(!error.message.includes(ruta), 'El mensaje no debe repetir la ruta rechazada.');
            }
            return true;
          },
        );
      });
    }

    test('sin bases (por ejemplo, null de baseDeOtroMontaje): las nulas se ignoran y no aceptan nada', () => {
      assert.throws(() => revisarCarpetaDeBackup(join(OTRO_MONTAJE, 'optica-e2e-backup-5543'), [null]), /temporal propia/);
    });
  });

  describe('tratamientoDeSinEscritura (antes de devolverle el permiso para borrarla)', () => {
    const info = ({ carpeta = false, enlace = false } = {}) => ({
      isDirectory: () => carpeta,
      isSymbolicLink: () => enlace,
    });

    test('una carpeta real: se le devuelve el permiso', () => {
      assert.equal(tratamientoDeSinEscritura(info({ carpeta: true })), 'carpeta');
    });

    test('un enlace simbólico: no se le cambian los permisos (se borra solo el enlace)', () => {
      assert.equal(tratamientoDeSinEscritura(info({ enlace: true })), 'enlace');
    });

    test('otra cosa (un archivo): ni permisos ni borrado propio', () => {
      assert.equal(tratamientoDeSinEscritura(info()), 'otro');
    });

    test('no existe: nada', () => {
      assert.equal(tratamientoDeSinEscritura(null), 'ninguno');
    });

    // Con el sistema de archivos real: lstat no sigue el enlace, así que un enlace a una carpeta sigue siendo enlace.
    test('un enlace simbólico real a una carpeta, leído con lstat: enlace', { skip: process.platform === 'win32' }, () => {
      const base = mkdtempSync(join(tmpdir(), 'prueba-enlace-'));
      try {
        const ajena = join(base, 'ajena');
        mkdirSync(ajena);
        const enlace = join(base, 'sin-escritura');
        symlinkSync(ajena, enlace);

        assert.equal(tratamientoDeSinEscritura(lstatSync(enlace)), 'enlace');
        assert.equal(tratamientoDeSinEscritura(lstatSync(ajena)), 'carpeta');
      } finally {
        rmSync(base, { recursive: true, force: true });
      }
    });
  });

  describe('esDelUsuario (la limpieza solo borra raíces propias)', () => {
    test('del mismo uid: sí', () => {
      assert.equal(esDelUsuario({ uid: 1000 }, 1000), true);
    });

    test('de otro uid (otro usuario de la PC): no', () => {
      assert.equal(esDelUsuario({ uid: 0 }, 1000), false);
    });

    test('sin getuid (Windows): no se puede comparar, sí', () => {
      assert.equal(esDelUsuario({ uid: 0 }, undefined), true);
    });
  });

  test('mensajeDeCarpetasEnUso: dice cómo resolverlo, sin ninguna ruta', () => {
    const mensaje = mensajeDeCarpetasEnUso(5548);

    assert.match(mensaje, /5548/);
    assert.match(mensaje, /si no hay otra corrida/i);
    assert.match(mensaje, /optica-e2e-backup-\*/);
    assert.ok(!mensaje.includes(tmpdir()), 'El mensaje no debe llevar la ruta de la carpeta temporal.');
  });
});

describe('corrida viva (pid y hora de inicio del proceso, FEAT-010 Block 8)', () => {
  const vivo = () => true;
  const muerto = () => false;

  test('leerArchivoPid: pid y hora de inicio', () => {
    assert.deepEqual(leerArchivoPid('1234 98765\n'), { pid: 1234, inicio: '98765' });
  });

  test('leerArchivoPid: un archivo de antes, solo con el pid: sin hora de inicio', () => {
    assert.deepEqual(leerArchivoPid('1234'), { pid: 1234, inicio: null });
  });

  test('leerArchivoPid: pid inválido: NaN', () => {
    assert.ok(Number.isNaN(leerArchivoPid('x 1').pid));
  });

  test('inicioEnStat: el campo 22 de /proc/<pid>/stat, aunque el nombre tenga espacios y paréntesis', () => {
    const campos = Array.from({ length: 30 }, (_, i) => String(i + 3));
    campos[22 - 3] = '424242';
    const stat = `5678 (node (e2e) x) ${campos.join(' ')}`;

    assert.equal(inicioEnStat(stat), '424242');
  });

  test('inicioEnStat: texto que no es un stat: null', () => {
    assert.equal(inicioEnStat('basura'), null);
  });

  test('corridaViva: el proceso no existe: muerta', () => {
    assert.equal(corridaViva({ pid: 1234, inicio: '1', vive: muerto, inicioDe: () => '1' }), false);
  });

  test('corridaViva: el pid existe con la misma hora de inicio: viva', () => {
    assert.equal(corridaViva({ pid: 1234, inicio: '500', vive: vivo, inicioDe: () => '500' }), true);
  });

  test('corridaViva: el pid lo reutilizó otro proceso (otra hora de inicio): muerta', () => {
    assert.equal(corridaViva({ pid: 1234, inicio: '500', vive: vivo, inicioDe: () => '777' }), false);
  });

  test('corridaViva: sin hora de inicio guardada o sin poder leerla (Windows): decide solo el pid', () => {
    assert.equal(corridaViva({ pid: 1234, inicio: null, vive: vivo, inicioDe: () => '777' }), true);
    assert.equal(corridaViva({ pid: 1234, inicio: '500', vive: vivo, inicioDe: () => null }), true);
  });

  test('debeBorrarse con un pid reutilizado: sí', () => {
    assert.equal(debeBorrarse({ pid: 1234, vive: vivo, edadMs: 0, inicio: '500', inicioDe: () => '777' }), true);
  });

  test('debeBorrarse con la misma corrida viva: no', () => {
    assert.equal(debeBorrarse({ pid: 1234, vive: vivo, edadMs: 0, inicio: '500', inicioDe: () => '500' }), false);
  });
});

describe('puntos de montaje (/proc/self/mountinfo), el criterio de la Api', () => {
  const MOUNTINFO = [
    '82 67 8:48 / / rw,relatime - ext4 /dev/sdd rw',
    '112 82 0:6 / /dev rw,nosuid shared:14 - devtmpfs none rw',
    '120 112 0:65 / /dev/shm rw,nosuid,nodev - tmpfs none rw',
    '130 82 0:70 / /mnt/con\\040espacio rw - tmpfs none rw',
  ].join('\n');

  test('montajesDeMountinfo: el quinto campo de cada línea, con los escapes octales decodificados', () => {
    assert.deepEqual(montajesDeMountinfo(MOUNTINFO), ['/', '/dev', '/dev/shm', '/mnt/con espacio']);
  });

  test('montajesDeMountinfo: líneas vacías o cortas se ignoran', () => {
    assert.deepEqual(montajesDeMountinfo('\n12 34\n'), []);
  });

  test('puntoDeMontajeDe: el de prefijo más largo, por segmentos', () => {
    const montajes = ['/', '/dev', '/dev/shm'];

    assert.equal(puntoDeMontajeDe('/dev/shm/optica-e2e-backup-5543', montajes), '/dev/shm');
    assert.equal(puntoDeMontajeDe('/tmp', montajes), '/');
    // /dev/shmx no está dentro de /dev/shm.
    assert.equal(puntoDeMontajeDe('/dev/shmx', montajes), '/dev');
  });

  test('puntoDeMontajeDe sin ningún montaje que la contenga: null', () => {
    assert.equal(puntoDeMontajeDe('/tmp', ['/dev']), null);
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

describe('leerInstancias (OPTICA_E2E_INSTANCIAS)', () => {
  test('un proyecto por navegador y por archivo: nombre y puerto de cada uno', () => {
    assert.deepEqual(leerInstancias('chrome-acceso:5543,chrome-configuracion:5544,msedge-acceso:5545'), [
      { nombre: 'chrome-acceso', puerto: 5543 },
      { nombre: 'chrome-configuracion', puerto: 5544 },
      { nombre: 'msedge-acceso', puerto: 5545 },
    ]);
  });

  // El nombre termina en una ruta del directorio temporal: nada de barras, puntos, mayúsculas ni guiones sueltos.
  for (const par of ['../datos:5543', 'Chrome:5543', 'chrome/acceso:5543', '-chrome:5543', 'chrome-:5543', 'chrome--acceso:5543', 'chrome:', 'chrome:55a3', ':5543']) {
    test(`instancias: nombre mal formado rechazado (${JSON.stringify(par)})`, () => {
      assert.throws(() => leerInstancias(`chrome-acceso:5543,${par}`), /OPTICA_E2E_INSTANCIAS mal formada/);
    });
  }

  // Dos instancias con el mismo nombre compartirían base y marca en el directorio temporal.
  test('instancias: nombre duplicado rechazado', () => {
    assert.throws(
      () => leerInstancias('chrome-acceso:5543,msedge-acceso:5544,chrome-acceso:5545'),
      /OPTICA_E2E_INSTANCIAS mal formada: "chrome-acceso:5545"/,
    );
  });

  for (const puerto of ['0', '65536', '99999']) {
    test(`instancias: puerto ${puerto} fuera de 1 a 65535 rechazado`, () => {
      assert.throws(() => leerInstancias(`chrome-acceso:${puerto}`), /OPTICA_E2E_INSTANCIAS mal formada/);
    });
  }

  test('instancias: puertos 1 y 65535 aceptados', () => {
    assert.deepEqual(leerInstancias('chrome-acceso:1,msedge-acceso:65535'), [
      { nombre: 'chrome-acceso', puerto: 1 },
      { nombre: 'msedge-acceso', puerto: 65535 },
    ]);
  });

  test('sin instancias: error que dice quién la define', () => {
    assert.throws(() => leerInstancias(''), /Falta OPTICA_E2E_INSTANCIAS/);
    assert.throws(() => leerInstancias(undefined), /Falta OPTICA_E2E_INSTANCIAS/);
  });
});

describe('archivosDePrueba (proyectos de playwright.config.ts)', () => {
  test('todos los *.spec.ts de tests/, ordenados y sin la extensión; ignora lo demás', () => {
    assert.deepEqual(
      archivosDePrueba(['configuracion.spec.ts', 'ayuda.ts', 'acceso.spec.ts', 'notas.md', 'presupuestos-borrador.spec.ts']),
      ['acceso', 'configuracion', 'presupuestos-borrador'],
    );
  });

  // Una prueba nueva no puede quedar sin correr en silencio: si su nombre no sirve para una instancia, la configuración
  // falla nombrando el archivo.
  for (const archivo of ['Presupuestos.spec.ts', 'presupuestos_borrador.spec.ts', 'presupuestos.v2.spec.ts']) {
    test(`archivo con nombre que no sirve para un proyecto rechazado (${archivo})`, () => {
      assert.throws(
        () => archivosDePrueba(['acceso.spec.ts', archivo]),
        (error) => error.message.includes(`tests/${archivo}`) && /minúsculas/.test(error.message),
      );
    });
  }

  test('sin ningún *.spec.ts: error', () => {
    assert.throws(() => archivosDePrueba(['ayuda.ts']), /No hay pruebas/);
  });
});

describe('esperarApi (servidor.mjs)', () => {
  /** Un reloj falso: cada espera avanza el tiempo sin dormir de verdad. */
  function reloj() {
    let ahora = 0;
    return { ahora: () => ahora, dormir: async (ms) => void (ahora += ms) };
  }

  test('responde al tercer intento: termina sin error', async () => {
    let intentos = 0;
    const { ahora, dormir } = reloj();

    await esperarApi({
      nombre: 'chrome-configuracion',
      puerto: 5544,
      responde: async () => ++intentos === 3,
      terminada: () => null,
      ahora,
      dormir,
    });

    assert.equal(intentos, 3);
  });

  test('servidor: una Api que no arranca da error', async () => {
    // El proceso de la Api terminó con código 1 antes de responder: no se sigue esperando hasta el plazo.
    let consultas = 0;
    const { ahora, dormir } = reloj();

    await assert.rejects(
      esperarApi({
        nombre: 'msedge-configuracion',
        puerto: 5546,
        responde: async () => (consultas++, false),
        terminada: () => (consultas >= 2 ? 1 : null),
        ahora,
        dormir,
      }),
      /La Api de msedge-configuracion terminó antes de responder \(1\)/,
    );
    assert.ok(ahora() < 120_000, 'Siguió esperando después de que la Api terminó.');
  });

  test('servidor: una Api que no responde en el plazo da error', async () => {
    const { ahora, dormir } = reloj();

    await assert.rejects(
      esperarApi({
        nombre: 'chrome-acceso',
        puerto: 5543,
        responde: async () => false,
        terminada: () => null,
        plazoMs: 5_000,
        ahora,
        dormir,
      }),
      /La Api de chrome-acceso no respondió en el puerto 5543/,
    );
  });
});

describe('ARCA de cada instancia (FEAT-007 D-17; FEAT-008 D-19, D-20, D-31)', () => {
  /** Las credenciales de ARCA van vacías (cuentan como ausentes): ninguna del entorno de quien corre llega a la Api. */
  const SIN_CREDENCIALES = { Arca__Cuit: '', Arca__Certificado__Ruta: '', Arca__Certificado__Clave: '' };
  const esperadas = (respuesta, consulta = 'responder', espera = '3') => ({
    Arca__Entorno: 'Simulado',
    Arca__TiempoMaximoSegundos: '2',
    Arca__EsperaVerificacionSegundos: espera,
    Arca__Simulado__Respuesta: respuesta,
    Arca__Simulado__Consulta: consulta,
    ...SIN_CREDENCIALES,
  });

  for (const navegador of ['chrome', 'msedge', 'chromium']) {
    test(`${navegador}: facturacion autoriza, facturacion-rechazo rechaza y facturacion-sin-respuesta no responde`, () => {
      assert.deepEqual(variablesDeArca(`${navegador}-facturacion`), esperadas('autorizar'));
      assert.deepEqual(variablesDeArca(`${navegador}-facturacion-rechazo`), esperadas('rechazar'));
      assert.deepEqual(variablesDeArca(`${navegador}-facturacion-sin-respuesta`), esperadas('no_responder'));
    });

    test(`${navegador}: cada archivo de verificación con su respuesta y su consulta del simulador (FEAT-008)`, () => {
      assert.deepEqual(variablesDeArca(`${navegador}-verificacion-recupera`), esperadas('autorizar_sin_responder'));
      // Las dos con más espera, para que los pedidos que tienen que llegar antes de que pase no dependan de la
      // velocidad del navegador.
      assert.deepEqual(
        variablesDeArca(`${navegador}-verificacion-emite`),
        esperadas('no_responder_una_vez', 'responder', '8'),
      );
      assert.deepEqual(
        variablesDeArca(`${navegador}-verificacion-sin-respuesta`),
        esperadas('no_responder', 'responder', '6'),
      );
      assert.deepEqual(
        variablesDeArca(`${navegador}-verificacion-consulta-sin-respuesta`),
        esperadas('no_responder', 'no_responder'),
      );
      assert.deepEqual(
        variablesDeArca(`${navegador}-verificacion-importe-distinto`),
        esperadas('no_responder_una_vez'),
      );
    });
  }

  test('los demás archivos: simulado, 2 s, 3 s de espera, autorizar y responder, sin credenciales', () => {
    for (const nombre of ['chrome-acceso', 'msedge-presupuestos-pdf', 'chrome-configuracion']) {
      assert.deepEqual(variablesDeArca(nombre), esperadas('autorizar'));
    }
  });

  test('el nombre del archivo, no un pedazo: "facturacion-rechazo-x" no rechaza', () => {
    assert.equal(variablesDeArca('chrome-facturacion-rechazo-x').Arca__Simulado__Respuesta, 'autorizar');
    assert.equal(variablesDeArca('chrome-presupuestos-facturacion-rechazo').Arca__Simulado__Respuesta, 'autorizar');
    assert.equal(variablesDeArca('chrome-verificacion-emite-x').Arca__Simulado__Respuesta, 'autorizar');
  });

  test('D-31: la espera de verificación alcanza el tiempo máximo más el margen (el menor entre 10 s y un tercio)', () => {
    for (const nombre of ['chrome-acceso', 'msedge-verificacion-emite', 'chrome-verificacion-consulta-sin-respuesta']) {
      const variables = variablesDeArca(nombre);
      const plazo = Number(variables.Arca__TiempoMaximoSegundos);
      const espera = Number(variables.Arca__EsperaVerificacionSegundos);
      assert.ok(Number.isInteger(espera) && espera >= 1 && espera <= 3600, `Espera fuera de rango en ${nombre}.`);
      assert.ok(espera >= plazo + Math.min(10, plazo / 3), `La Api de ${nombre} no arrancaría (D-31).`);
    }
  });

  test('el mapa de archivos de la suite es válido: revisarArcaPorArchivo no falla', () => {
    assert.doesNotThrow(() => revisarArcaPorArchivo(ARCA_POR_ARCHIVO));
    assert.deepEqual(Object.keys(ARCA_POR_ARCHIVO).sort(), [
      'facturacion',
      'facturacion-rechazo',
      'facturacion-sin-respuesta',
      'facturas-busqueda',
      'facturas-pdf',
      'verificacion-consulta-sin-respuesta',
      'verificacion-emite',
      'verificacion-importe-distinto',
      'verificacion-recupera',
      'verificacion-sin-respuesta',
    ]);
  });

  // Un valor que la Api no admite la haría no arrancar con un error de .NET que no nombra el archivo de pruebas; uno que
  // admitiera con otro sentido correría la prueba contra otro ARCA. El harness se detiene antes, nombrando el archivo.
  for (const [caso, entrada, esperado] of [
    ['respuesta desconocida', { respuesta: 'autorizar_tarde' }, /respuesta/],
    ['consulta desconocida', { consulta: 'responder_tarde' }, /consulta/],
    ['respuesta que no es texto', { respuesta: 1 }, /respuesta/],
    ['propiedad desconocida', { plazo: 2 }, /plazo/],
    // D-31 con 2 s de plazo: al menos 2 + 0,67 s, así que 2 no alcanza y la Api no arrancaría.
    ['espera menor que el plazo más el margen (D-31)', { espera: 2 }, /espera tiene que ser.*D-31/],
    ['espera que no es un entero', { espera: 3.5 }, /espera tiene que ser.*D-31/],
    ['espera como texto', { espera: '8' }, /espera tiene que ser.*D-31/],
    ['espera mayor que 3600', { espera: 3601 }, /espera tiene que ser.*D-31/],
    ['archivo del simulador con una propiedad que no es del archivo', { simulado: { dni: '00718294' } }, /simulado/],
    ['archivo del simulador que no es un objeto', { simulado: 'contadores' }, /simulado/],
    // FEAT-009: un contador que el simulador no acepta haría que la Api rechace el archivo en la primera emisión, con un
    // error que no nombra el archivo de pruebas; el harness se detiene antes.
    ['contador como texto', { simulado: { contadores: { 'B-3': '124' } } }, /contadores/],
    ['contador que no es un entero', { simulado: { contadores: { 'B-3': 124.5 } } }, /contadores/],
    ['contador negativo', { simulado: { contadores: { 'B-3': -1 } } }, /contadores/],
    ['contador mayor que el número máximo de comprobante', { simulado: { contadores: { 'B-3': 100_000_000 } } }, /contadores/],
    ['contador de una Factura A', { simulado: { contadores: { 'A-3': 124 } } }, /contadores/],
    ['contador con un punto de venta con cero a la izquierda', { simulado: { contadores: { 'B-03': 124 } } }, /contadores/],
    ['contador de un punto de venta mayor que 99999', { simulado: { contadores: { 'B-100000': 1 } } }, /contadores/],
    ['contadores que no son un objeto', { simulado: { contadores: [124] } }, /contadores/],
  ]) {
    test(`archivo con ${caso}: detiene el harness nombrando el archivo`, () => {
      const mapa = { 'verificacion-rota': entrada };

      assert.throws(
        () => revisarArcaPorArchivo(mapa),
        (error) => error.message.includes('tests/verificacion-rota.spec.ts') && esperado.test(error.message),
      );
      assert.throws(
        () => variablesDeArca('chrome-verificacion-rota', mapa),
        (error) => error.message.includes('tests/verificacion-rota.spec.ts') && esperado.test(error.message),
      );
    });
  }

  test('espera válida por archivo: la usa Arca__EsperaVerificacionSegundos; sin ella, 3', () => {
    const mapa = { 'verificacion-larga': { espera: 3600 }, 'verificacion-justa': { espera: 3 } };

    assert.doesNotThrow(() => revisarArcaPorArchivo(mapa));
    assert.equal(variablesDeArca('chrome-verificacion-larga', mapa).Arca__EsperaVerificacionSegundos, '3600');
    assert.equal(variablesDeArca('msedge-verificacion-justa', mapa).Arca__EsperaVerificacionSegundos, '3');
    assert.equal(variablesDeArca('chrome-acceso', mapa).Arca__EsperaVerificacionSegundos, '3');
  });

  test('los valores se comparan exactos: "Autorizar" en mayúscula detiene el harness', () => {
    assert.throws(
      () => revisarArcaPorArchivo({ facturacion: { respuesta: 'Autorizar' } }),
      /tests\/facturacion\.spec\.ts/,
    );
  });

  test('contador del simulador: facturacion y los de facturas arrancan la Factura B del punto de venta 3 en 124', () => {
    assert.deepEqual(contadorSimulado('chrome-facturacion'), { contadores: { 'B-3': 124 } });
    assert.deepEqual(contadorSimulado('msedge-facturacion'), { contadores: { 'B-3': 124 } });
    // FEAT-009, AC-08 y AC-23: la primera Factura B de esas bases es la 0003-00000125; en la búsqueda, la primera
    // Factura C es la 0003-00034561 de AC-19.
    for (const navegador of ['chrome', 'msedge']) {
      assert.deepEqual(contadorSimulado(`${navegador}-facturas-pdf`), { contadores: { 'B-3': 124 } });
      assert.deepEqual(contadorSimulado(`${navegador}-facturas-busqueda`), {
        contadores: { 'B-3': 124, 'C-3': 34560 },
      });
    }
    // Los demás archivos de FEAT-009 numeran desde 1.
    assert.equal(contadorSimulado('chrome-configuracion-emisor'), null);
    assert.equal(contadorSimulado('chrome-facturacion-rechazo'), null);
    assert.equal(contadorSimulado('msedge-facturacion-sin-respuesta'), null);
    assert.equal(contadorSimulado('chrome-acceso'), null);
    for (const archivo of ['recupera', 'emite', 'sin-respuesta', 'consulta-sin-respuesta', 'importe-distinto']) {
      assert.equal(contadorSimulado(`msedge-verificacion-${archivo}`), null);
    }
  });

  test('cada archivo del mapa es un archivo de tests/: una clave mal escrita no queda sin efecto', () => {
    const directorio = join(dirname(fileURLToPath(import.meta.url)), 'tests');
    const archivos = archivosDePrueba(readdirSync(directorio));

    assert.deepEqual(
      Object.keys(ARCA_POR_ARCHIVO).filter((archivo) => !archivos.includes(archivo)),
      [],
    );
  });

  test('contadores válidos de Factura B y C: el harness los acepta', () => {
    const mapa = { 'facturas-rota': { simulado: { contadores: { 'B-3': 0, 'C-99999': 99_999_999 } } } };

    assert.doesNotThrow(() => revisarArcaPorArchivo(mapa));
  });

  test('contador del simulador: el contenido sembrado sale del mapa de archivos', () => {
    const sembrado = { contadores: { 'B-3': 7 } };

    assert.deepEqual(contadorSimulado('chrome-verificacion-rota', { 'verificacion-rota': { simulado: sembrado } }), sembrado);
  });

  describe('entornoDeLaApi: ninguna clave de ARCA de quien corre la suite llega a la Api', () => {
    test('quita las claves de ARCA del entorno, en cualquier mayúscula y con __ o :, y suma las de la instancia', () => {
      const entorno = {
        PATH: '/usr/bin',
        HOME: '/home/prueba',
        ARCA__ENTORNO: 'Produccion',
        arca__certificado__clave: 'x',
        'Arca:Certificado:Ruta': 'C:\\cert.pfx',
        Arca__Simulado__Respuesta: 'rechazar',
        ASPNETCORE_ARCA__ENTORNO: 'Produccion',
        dotnet_arca__cuit: '20000000001',
        'DOTNET_Arca:TiempoMaximoSegundos': '120',
      };

      const resultado = entornoDeLaApi(entorno, 'chrome-facturacion');

      assert.deepEqual(resultado, { PATH: '/usr/bin', HOME: '/home/prueba', ...variablesDeArca('chrome-facturacion') });
      // Ninguna variante de la clave del certificado queda con un valor.
      assert.deepEqual(
        Object.keys(resultado).filter((clave) => /certificado/i.test(clave)),
        ['Arca__Certificado__Ruta', 'Arca__Certificado__Clave'],
      );
      assert.equal(resultado.Arca__Certificado__Clave, '');
      assert.equal(resultado.Arca__Entorno, 'Simulado');
    });

    test('las que solo se parecen a una de ARCA quedan', () => {
      const entorno = { ARCADIA: '1', MI_ARCA__X: '2', Arcade__X: '3', ARCA: '4', ASPNETCORE_ENVIRONMENT: 'Production' };

      const resultado = entornoDeLaApi(entorno, 'msedge-facturacion-rechazo');

      assert.deepEqual(resultado, { ...entorno, ...variablesDeArca('msedge-facturacion-rechazo') });
    });

    test('no modifica el entorno recibido', () => {
      const entorno = { ARCA__ENTORNO: 'Produccion' };

      entornoDeLaApi(entorno, 'chrome-acceso');

      assert.deepEqual(entorno, { ARCA__ENTORNO: 'Produccion' });
    });
  });
});
