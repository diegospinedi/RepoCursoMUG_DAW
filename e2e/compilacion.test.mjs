// Pruebas unitarias de compilacion.mjs (sin navegador ni Api): `npm run test:unidad` en e2e/.
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';

import {
  archivosDePrueba,
  comandosDeCompilacion,
  contadorSimulado,
  debeBorrarse,
  entornoDeLaApi,
  esperarApi,
  leerInstancias,
  leerPid,
  mensajeDeFallo,
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

describe('ARCA de cada instancia (FEAT-007, D-17)', () => {
  /** Las credenciales de ARCA van vacías (cuentan como ausentes): ninguna del entorno de quien corre llega a la Api. */
  const SIN_CREDENCIALES = { Arca__Cuit: '', Arca__Certificado__Ruta: '', Arca__Certificado__Clave: '' };
  const esperadas = (respuesta) => ({
    Arca__Entorno: 'Simulado',
    Arca__TiempoMaximoSegundos: '2',
    Arca__Simulado__Respuesta: respuesta,
    ...SIN_CREDENCIALES,
  });

  for (const navegador of ['chrome', 'msedge', 'chromium']) {
    test(`${navegador}: facturacion autoriza, facturacion-rechazo rechaza y facturacion-sin-respuesta no responde`, () => {
      assert.deepEqual(variablesDeArca(`${navegador}-facturacion`), esperadas('autorizar'));
      assert.deepEqual(variablesDeArca(`${navegador}-facturacion-rechazo`), esperadas('rechazar'));
      assert.deepEqual(variablesDeArca(`${navegador}-facturacion-sin-respuesta`), esperadas('no_responder'));
    });
  }

  test('los demás archivos: simulado, 2 s y autorizar, sin credenciales', () => {
    for (const nombre of ['chrome-acceso', 'msedge-presupuestos-pdf', 'chrome-configuracion']) {
      assert.deepEqual(variablesDeArca(nombre), esperadas('autorizar'));
    }
  });

  test('el nombre del archivo, no un pedazo: "facturacion-rechazo-x" no rechaza', () => {
    assert.equal(variablesDeArca('chrome-facturacion-rechazo-x').Arca__Simulado__Respuesta, 'autorizar');
    assert.equal(variablesDeArca('chrome-presupuestos-facturacion-rechazo').Arca__Simulado__Respuesta, 'autorizar');
  });

  test('contador del simulador: solo facturacion arranca en 124 para la Factura B del punto de venta 3 (AC-30)', () => {
    assert.deepEqual(contadorSimulado('chrome-facturacion'), { contadores: { 'B-3': 124 } });
    assert.deepEqual(contadorSimulado('msedge-facturacion'), { contadores: { 'B-3': 124 } });
    assert.equal(contadorSimulado('chrome-facturacion-rechazo'), null);
    assert.equal(contadorSimulado('msedge-facturacion-sin-respuesta'), null);
    assert.equal(contadorSimulado('chrome-acceso'), null);
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
