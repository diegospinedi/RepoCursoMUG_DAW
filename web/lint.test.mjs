// Pruebas del análisis estático de la web (oxlint): `npm run test:lint` en web/.
// Corren el ejecutable JavaScript de oxlint con node y execFile, sin intérprete de comandos, para que anden igual en
// Linux y en Windows (donde node_modules/.bin/oxlint es un .cmd). Los rechazos se prueban sobre una carpeta temporal.
import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { after, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const WEB = dirname(fileURLToPath(import.meta.url));
const REPOSITORIO = resolve(WEB, '..');
const CONFIGURACION = join(WEB, '.oxlintrc.json');
const require = createRequire(import.meta.url);

const carpetasTemporales = [];
after(() => {
  for (const carpeta of carpetasTemporales) rmSync(carpeta, { recursive: true, force: true });
});

/** El script `lint` de package.json; la prueba falla en su aserción si todavía no existe. */
function scriptLint() {
  const paquete = JSON.parse(readFileSync(join(WEB, 'package.json'), 'utf8'));
  const script = paquete.scripts?.lint;
  assert.equal(typeof script, 'string', 'package.json no declara el script "lint".');
  return script;
}

/** Los argumentos que el script `lint` le pasa a oxlint (sin el nombre del programa). */
function argumentosDelScript() {
  const [programa, ...argumentos] = scriptLint().trim().split(/\s+/);
  assert.equal(programa, 'oxlint', 'El script "lint" no ejecuta oxlint.');
  return argumentos;
}

/** Ruta del ejecutable JavaScript del paquete oxlint, resuelta desde web/. */
function binarioDeOxlint() {
  let rutaPaquete;
  try {
    rutaPaquete = require.resolve('oxlint/package.json');
  } catch {
    assert.fail('El paquete oxlint no está instalado en web/.');
  }
  const { bin } = JSON.parse(readFileSync(rutaPaquete, 'utf8'));
  const relativo = typeof bin === 'string' ? bin : bin?.oxlint;
  assert.equal(typeof relativo, 'string', 'El paquete oxlint no declara su ejecutable.');
  return join(dirname(rutaPaquete), relativo);
}

/** Corre oxlint con los argumentos del script `lint` en `cwd`; devuelve código, salida y duración. */
function correrLint(cwd) {
  const argumentos = argumentosDelScript();
  const binario = binarioDeOxlint();
  const inicio = performance.now();
  return new Promise((resolver) => {
    execFile(
      process.execPath,
      [binario, ...argumentos],
      // timeout: límite de 60 s del modelo de amenazas (NFR-01); si oxlint se cuelga, el proceso se mata y el código
      // queda en -1, así la prueba falla en lugar de esperar para siempre.
      { cwd, shell: false, env: { ...process.env, NO_COLOR: '1', FORCE_COLOR: '0' }, maxBuffer: 16 * 1024 * 1024, timeout: 60_000 },
      (error, stdout, stderr) => {
        const codigo = error ? (typeof error.code === 'number' ? error.code : -1) : 0;
        resolver({ codigo, salida: `${stdout}${stderr}`, milisegundos: performance.now() - inicio });
      },
    );
  });
}

/** Arma una carpeta temporal con la configuración de la web y los archivos indicados ({ruta relativa: contenido}). */
function carpetaConArchivos(archivos, configuracion) {
  const carpeta = mkdtempSync(join(tmpdir(), 'optica-lint-'));
  carpetasTemporales.push(carpeta);
  if (configuracion === undefined) {
    assert.ok(existsSync(CONFIGURACION), 'No existe web/.oxlintrc.json.');
    copyFileSync(CONFIGURACION, join(carpeta, '.oxlintrc.json'));
  } else {
    writeFileSync(join(carpeta, '.oxlintrc.json'), JSON.stringify(configuracion));
  }
  for (const [ruta, contenido] of Object.entries(archivos)) {
    const destino = join(carpeta, ruta);
    mkdirSync(dirname(destino), { recursive: true });
    writeFileSync(destino, contenido);
  }
  return carpeta;
}

const VARIABLE_SIN_USO = 'export function f(): number {\n  const sinUso = 1;\n  return 2;\n}\n';

const HOOK_EN_CONDICION = [
  "import { useState } from 'react';",
  '',
  'export function Componente({ activo }: { activo: boolean }) {',
  '  if (activo) {',
  '    const [valor] = useState(0);',
  '    return <p>{valor}</p>;',
  '  }',
  '  return null;',
  '}',
  '',
].join('\n');

describe('oxlint en la web', () => {
  test(
    'Lint_CodigoActual_SinProblemas',
    { todo: 'Block 2 de FEAT-004 corrige los 7 problemas actuales' },
    async () => {
      const { codigo, salida } = await correrLint(WEB);
      assert.equal(codigo, 0, `oxlint informó problemas en web/src:\n${salida}`);
    },
  );

  test('Lint_RevisaAplicacionYPruebas', async () => {
    const carpeta = carpetaConArchivos({
      'src/x.ts': VARIABLE_SIN_USO,
      'src/x.test.ts': VARIABLE_SIN_USO,
    });
    const { codigo, salida } = await correrLint(carpeta);
    assert.equal(codigo, 1, salida);
    assert.match(salida, /src[\\/]x\.ts/, 'No informó el archivo de la aplicación.');
    assert.match(salida, /src[\\/]x\.test\.ts/, 'No informó el archivo de prueba.');
  });

  test('Lint_VariableSinUso_Rechazado', async () => {
    const carpeta = carpetaConArchivos({ 'src/x.ts': VARIABLE_SIN_USO });
    const { codigo, salida } = await correrLint(carpeta);
    assert.equal(codigo, 1, salida);
    assert.match(salida, /src[\\/]x\.ts:2:\d+/, 'No informó archivo y línea.');
    assert.match(salida, /no-unused-vars/, 'No informó la regla.');
  });

  test('Lint_HookEnCondicion_Rechazado', async () => {
    const carpeta = carpetaConArchivos({ 'src/Componente.tsx': HOOK_EN_CONDICION });
    const { codigo, salida } = await correrLint(carpeta);
    assert.equal(codigo, 1, salida);
    assert.match(salida, /src[\\/]Componente\.tsx:5:\d+/, 'No informó archivo y línea.');
    assert.match(salida, /rules-of-hooks/, 'No informó la regla.');
  });

  test('Lint_ScriptConMaxWarnings0_Rechazado', async () => {
    assert.match(scriptLint(), /--max-warnings[ =]0(\s|$)/, 'El script "lint" no lleva --max-warnings 0.');
    // Con la configuración de la web, pero una regla bajada a aviso: el aviso también rechaza.
    assert.ok(existsSync(CONFIGURACION), 'No existe web/.oxlintrc.json.');
    const configuracion = JSON.parse(readFileSync(CONFIGURACION, 'utf8'));
    configuracion.rules = { ...configuracion.rules, 'no-debugger': 'warn' };
    const carpeta = carpetaConArchivos({ 'src/x.ts': 'export function f(): void {\n  debugger;\n}\n' }, configuracion);
    const { codigo, salida } = await correrLint(carpeta);
    assert.match(salida, /no-debugger/, 'No informó el aviso.');
    assert.equal(codigo, 1, `Un aviso no fue rechazado:\n${salida}`);
  });

  test('Lint_DistYCoverage_Ignorados', async () => {
    const carpeta = carpetaConArchivos({
      'src/bien.ts': 'export const valor = 1;\n',
      'dist/x.js': 'const sinUso = 1;\ndebugger;\n',
      'coverage/x.js': 'const sinUso = 1;\ndebugger;\n',
    });
    const { codigo, salida } = await correrLint(carpeta);
    assert.equal(codigo, 0, salida);
    assert.doesNotMatch(salida, /dist[\\/]x\.js/, 'Informó un archivo de dist/.');
    assert.doesNotMatch(salida, /coverage[\\/]x\.js/, 'Informó un archivo de coverage/.');
  });

  test('Lint_TodoElCodigo_MenosDe60Segundos', async () => {
    const { codigo, salida, milisegundos } = await correrLint(WEB);
    assert.ok(codigo === 0 || codigo === 1, `oxlint no terminó su análisis (código ${codigo}):\n${salida}`);
    assert.ok(milisegundos < 60_000, `El análisis tardó ${Math.round(milisegundos)} ms.`);
  });

  test('Lint_AgentsDeclaraElComando', () => {
    const agents = readFileSync(join(REPOSITORIO, 'AGENTS.md'), 'utf8');
    const stack = agents.split(/^## /m).find((seccion) => seccion.startsWith('Stack'));
    assert.ok(stack, 'AGENTS.md no tiene la sección "Stack".');
    assert.match(stack, /npm run lint/, 'La sección "Stack" de AGENTS.md no declara npm run lint.');
  });
});
