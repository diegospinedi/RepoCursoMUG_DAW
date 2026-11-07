// Pruebas de las reglas de la integración continua (ci.mjs): `npm run test:unidad` en e2e/.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

import * as ci from './ci.mjs';

const REPOSITORIO = join(dirname(fileURLToPath(import.meta.url)), '..');

/** Se toman del módulo para que la prueba falle en su aserción, no al importar, mientras la función no exista. */
const proyectosSinLockfile = (archivos) => {
  assert.equal(typeof ci.proyectosSinLockfile, 'function', 'ci.mjs no exporta proyectosSinLockfile.');
  return ci.proyectosSinLockfile(archivos);
};
const revisarPropsNuGet = (texto) => {
  assert.equal(typeof ci.revisarPropsNuGet, 'function', 'ci.mjs no exporta revisarPropsNuGet.');
  return ci.revisarPropsNuGet(texto);
};
const revisarWorkflow = (texto) => {
  assert.equal(typeof ci.revisarWorkflow, 'function', 'ci.mjs no exporta revisarWorkflow.');
  return ci.revisarWorkflow(texto);
};
const seccionStack = (texto) => {
  assert.equal(typeof ci.seccionStack, 'function', 'ci.mjs no exporta seccionStack.');
  return ci.seccionStack(texto);
};
const comandosDelProyecto = () => {
  assert.ok(Array.isArray(ci.COMANDOS_DEL_PROYECTO), 'ci.mjs no exporta COMANDOS_DEL_PROYECTO.');
  return ci.COMANDOS_DEL_PROYECTO;
};
const hostsExternos = (texto) => {
  assert.equal(typeof ci.hostsExternos, 'function', 'ci.mjs no exporta hostsExternos.');
  return ci.hostsExternos(texto);
};
const proyectosSinRegla = (proyectos) => {
  assert.equal(typeof ci.proyectosSinRegla, 'function', 'ci.mjs no exporta proyectosSinRegla.');
  return ci.proyectosSinRegla(proyectos);
};
const prefijosDeNavegador = (proyectos) => {
  assert.equal(typeof ci.prefijosDeNavegador, 'function', 'ci.mjs no exporta prefijosDeNavegador.');
  return ci.prefijosDeNavegador(proyectos);
};
const navegadoresFaltantes = (proyectos, esperados) => {
  assert.equal(typeof ci.navegadoresFaltantes, 'function', 'ci.mjs no exporta navegadoresFaltantes.');
  return ci.navegadoresFaltantes(proyectos, esperados);
};
const reglaSinRedExterna = () => {
  assert.equal(typeof ci.REGLA_SIN_RED_EXTERNA, 'string', 'ci.mjs no exporta REGLA_SIN_RED_EXTERNA.');
  return ci.REGLA_SIN_RED_EXTERNA;
};

/**
 * Rutas versionadas, separadas por `/` en cualquier sistema operativo. Incluye las nuevas todavía sin agregar al
 * índice (pero no las ignoradas por .gitignore), así la prueba también vale antes del commit; en la CI el checkout
 * trae solo lo commiteado. Con `-z` git separa por `\0` y no pone entre comillas ni escapa las rutas con tildes.
 */
const archivosVersionados = () =>
  execFileSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], {
    cwd: REPOSITORIO,
    encoding: 'utf8',
  })
    .split('\0')
    .filter(Boolean);

const PROPS_COMPLETO = `<Project>
  <PropertyGroup>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
  <PropertyGroup Condition="'$(CI)' == 'true'">
    <RestoreLockedMode>true</RestoreLockedMode>
  </PropertyGroup>
</Project>`;

describe('bloqueo de dependencias de NuGet', () => {
  test('NuGet_CadaProyectoConLockfile', () => {
    const archivos = archivosVersionados();
    const proyectos = archivos.filter((ruta) => ruta.endsWith('.csproj'));

    assert.equal(proyectos.length, 8, `Se esperaban 8 proyectos versionados y hay ${proyectos.length}.`);
    assert.deepEqual(proyectosSinLockfile(archivos), [], 'Hay proyectos sin packages.lock.json versionado.');

    const props = readFileSync(join(REPOSITORIO, 'Directory.Build.props'), 'utf8');
    assert.deepEqual(revisarPropsNuGet(props), [], 'El Directory.Build.props real tiene problemas.');
  });

  test('NuGet_LockfileFaltante_Rechazado', () => {
    assert.deepEqual(proyectosSinLockfile([]), []);

    const archivos = [
      'src/Optica.Api/Optica.Api.csproj',
      'src/Optica.Api/packages.lock.json',
      'src/Optica.Domain/Optica.Domain.csproj',
      // El archivo de bloqueo de otra carpeta no cuenta para este proyecto.
      'tests/Optica.Domain.Tests/packages.lock.json',
      'src/Optica.Domain/Program.cs',
    ];

    assert.deepEqual(proyectosSinLockfile(archivos), ['src/Optica.Domain/Optica.Domain.csproj']);

    // Rutas con tildes y espacios, tal como las devuelve `git ls-files -z` (sin comillas ni escapes).
    assert.deepEqual(
      proyectosSinLockfile([
        'src/Óptica Facturación/Óptica.Facturación.csproj',
        'src/Óptica Facturación/packages.lock.json',
        'tests/Óptica.Pruebas/Óptica.Pruebas.csproj',
      ]),
      ['tests/Óptica.Pruebas/Óptica.Pruebas.csproj'],
    );
  });

  test('NuGet_SinModoBloqueado_Rechazado', () => {
    assert.deepEqual(revisarPropsNuGet(PROPS_COMPLETO), [], 'El props de referencia no debería tener problemas.');

    const sinModoBloqueado = PROPS_COMPLETO.replace(/\s*<PropertyGroup Condition="'\$\(CI\)' == 'true'">[\s\S]*?<\/PropertyGroup>/, '');
    const problemas = revisarPropsNuGet(sinModoBloqueado);
    assert.ok(
      problemas.some((p) => p.regla === 'modo-bloqueado' && p.detalle.includes('RestoreLockedMode')),
      `Sin RestoreLockedMode tiene que rechazarse nombrando la propiedad: ${JSON.stringify(problemas)}`,
    );

    // Modo bloqueado sin condicionar a CI: bloquearía también la PC de desarrollo.
    const sinCondicion = PROPS_COMPLETO.replace(`<PropertyGroup Condition="'$(CI)' == 'true'">`, '<PropertyGroup>');
    assert.ok(
      revisarPropsNuGet(sinCondicion).some((p) => p.regla === 'modo-bloqueado'),
      'RestoreLockedMode sin la condición de CI tiene que rechazarse.',
    );

    const sinLockfile = PROPS_COMPLETO.replace(/\s*<RestorePackagesWithLockFile>true<\/RestorePackagesWithLockFile>/, '');
    assert.ok(
      revisarPropsNuGet(sinLockfile).some(
        (p) => p.regla === 'archivo-de-bloqueo' && p.detalle.includes('RestorePackagesWithLockFile'),
      ),
      'Sin RestorePackagesWithLockFile tiene que rechazarse nombrando la propiedad.',
    );

    assert.deepEqual(
      revisarPropsNuGet('   ').map((p) => p.regla),
      ['vacio'],
      'Un props vacío se rechaza solo por vacío.',
    );

    const rechazado = (texto, regla, mensaje) => {
      const problemas = revisarPropsNuGet(texto);
      assert.ok(problemas.some((p) => p.regla === regla), `${mensaje}: ${JSON.stringify(problemas)}`);
    };
    const aceptado = (texto, mensaje) => assert.deepEqual(revisarPropsNuGet(texto), [], mensaje);
    const antesDelCierre = (agregado) => PROPS_COMPLETO.replace('</Project>', `${agregado}\n</Project>`);

    // En MSBuild gana la última asignación: un `false` posterior apaga lo anterior.
    rechazado(
      antesDelCierre('  <PropertyGroup>\n    <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>\n  </PropertyGroup>'),
      'archivo-de-bloqueo',
      'RestorePackagesWithLockFile pisado con false tiene que rechazarse',
    );
    rechazado(
      antesDelCierre('  <PropertyGroup>\n    <RestoreLockedMode>false</RestoreLockedMode>\n  </PropertyGroup>'),
      'modo-bloqueado',
      'RestoreLockedMode pisado con false tiene que rechazarse',
    );

    // Un PropertyGroup dentro de un Target o de un Choose/When no es la asignación global.
    const soloLockedMode = PROPS_COMPLETO.replace(
      /\s*<PropertyGroup Condition="'\$\(CI\)' == 'true'">[\s\S]*?<\/PropertyGroup>/,
      '',
    );
    const soloLockfile = PROPS_COMPLETO.replace(
      /\s*<PropertyGroup>\s*<RestorePackagesWithLockFile>true<\/RestorePackagesWithLockFile>\s*<\/PropertyGroup>/,
      '',
    );
    rechazado(
      soloLockfile.replace(
        '</Project>',
        '  <Target Name="Antes" BeforeTargets="Restore">\n    <PropertyGroup>\n      <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>\n    </PropertyGroup>\n  </Target>\n</Project>',
      ),
      'archivo-de-bloqueo',
      'RestorePackagesWithLockFile dentro de un Target tiene que rechazarse',
    );
    rechazado(
      soloLockedMode.replace(
        '</Project>',
        `  <Choose>\n    <When Condition="'$(CI)' == 'true'">\n      <PropertyGroup>\n        <RestoreLockedMode>true</RestoreLockedMode>\n      </PropertyGroup>\n    </When>\n  </Choose>\n</Project>`,
      ),
      'modo-bloqueado',
      'RestoreLockedMode dentro de un Choose/When tiene que rechazarse',
    );
    rechazado(
      antesDelCierre('  <Target Name="Apagar">\n    <PropertyGroup>\n      <RestoreLockedMode>false</RestoreLockedMode>\n    </PropertyGroup>\n  </Target>'),
      'modo-bloqueado',
      'RestoreLockedMode asignado dentro de un Target tiene que rechazarse aunque haya uno global',
    );

    // Una propiedad nombrada solo en un comentario no cuenta.
    rechazado(
      soloLockedMode.replace(
        '</Project>',
        `  <!-- <PropertyGroup Condition="'$(CI)' == 'true'"><RestoreLockedMode>true</RestoreLockedMode></PropertyGroup> -->\n</Project>`,
      ),
      'modo-bloqueado',
      'RestoreLockedMode solo en un comentario tiene que rechazarse',
    );
    // Un comentario que se arma al quitar otro de adentro, o uno sin cerrar, también esconde la propiedad.
    const grupoCI =
      `<PropertyGroup Condition="'$(CI)' == 'true'">` + '<RestoreLockedMode>true</RestoreLockedMode></PropertyGroup>';
    rechazado(
      soloLockedMode.replace('</Project>', `  <!<!---->-- ${grupoCI} -->\n</Project>`),
      'modo-bloqueado',
      'RestoreLockedMode en un comentario armado al quitar otro tiene que rechazarse',
    );
    rechazado(
      soloLockedMode.replace('</Project>', `  <!-- ${grupoCI}\n</Project>`),
      'modo-bloqueado',
      'RestoreLockedMode en un comentario sin cerrar tiene que rechazarse',
    );

    // La condición con otros espacios, puesta en el elemento, o con el atributo entre comillas simples, vale igual.
    aceptado(
      soloLockedMode.replace(
        '</Project>',
        `  <PropertyGroup>\n    <RestoreLockedMode Condition=" '$(CI)'=='true' ">true</RestoreLockedMode>\n  </PropertyGroup>\n</Project>`,
      ),
      'La condición de CI en el elemento y con otros espacios tiene que aceptarse.',
    );
    aceptado(
      PROPS_COMPLETO.replace(`Condition="'$(CI)' == 'true'"`, `Condition='"$(CI)" == "true"'`),
      'La condición de CI con el atributo entre comillas simples tiene que aceptarse.',
    );
  });
});

const SHA_CHECKOUT = '3d3c42e5aac5ba805825da76410c181273ba90b1';
const SHA_OTRO = '0123456789abcdef0123456789abcdef01234567';

/** Un workflow que cumple todas las reglas; cada prueba lo cambia en un solo punto. */
const WORKFLOW_VALIDO = `name: CI

on:
  pull_request:

permissions:
  contents: read

concurrency:
  group: ci-\${{ github.event.pull_request.number }}
  cancel-in-progress: true

jobs:
  verificacion:
    name: Verificación
    runs-on: ubuntu-24.04
    timeout-minutes: 45
    env:
      DOTNET_NOLOGO: "true"
      DOTNET_CLI_TELEMETRY_OPTOUT: "true"
    steps:
      - name: Descargar el código
        uses: actions/checkout@${SHA_CHECKOUT} # v7.0.1
        with:
          persist-credentials: false
      - name: Instalar .NET
        uses: actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73 # v6.1.0
        with:
          dotnet-version: 10.0.x
      - name: Instalar Node
        uses: actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1 # v7.1.0
        with:
          node-version: 22
      - name: Dependencias de la web
        working-directory: web
        run: npm ci
      - name: Dependencias de e2e
        working-directory: e2e
        run: npm ci
      - name: Compilar .NET
        run: dotnet build Optica.sln
      - name: Pruebas de .NET
        run: dotnet test Optica.sln
      - name: Lint de la web
        working-directory: web
        run: npm run lint
      - name: Prueba del lint
        working-directory: web
        run: npm run test:lint
      - name: Pruebas de la web con cobertura
        working-directory: web
        run: npm run test:coverage
      - name: Compilar la web
        working-directory: web
        run: npm run build
      - name: Pruebas de unidad de e2e
        working-directory: e2e
        run: npm run test:unidad
      - name: Dependencias de sistema de Chrome y Edge
        working-directory: e2e
        run: npx --no-install playwright install-deps chrome msedge
      - name: Versiones de los navegadores
        run: |
          google-chrome --version
          microsoft-edge --version
      - name: Certificado de desarrollo HTTPS
        run: dotnet dev-certs https
      - name: Pruebas de punta a punta (Chrome y Edge)
        working-directory: e2e
        run: npm run test:harness
`;

/** Reemplaza un fragmento que tiene que estar: un reemplazo que no encuentra nada dejaría la prueba sin probar. */
const cambiar = (texto, buscado, nuevo) => {
  assert.ok(texto.includes(buscado), `El workflow de referencia no tiene ${JSON.stringify(buscado)}.`);
  return texto.replace(buscado, nuevo);
};

const reglasDe = (texto) => revisarWorkflow(texto).map((p) => p.regla);

/** El de referencia no tiene problemas y la variante sí, por la regla indicada. */
const workflowRechazado = (texto, regla, mensaje) => {
  assert.deepEqual(revisarWorkflow(WORKFLOW_VALIDO), [], 'El workflow de referencia no debería tener problemas.');
  assert.notEqual(texto, WORKFLOW_VALIDO, `${mensaje}: la variante es igual a la referencia.`);
  const problemas = revisarWorkflow(texto);
  assert.ok(
    problemas.some((p) => p.regla === regla && typeof p.detalle === 'string' && p.detalle !== ''),
    `${mensaje}: se esperaba la regla ${regla} y hubo ${JSON.stringify(problemas)}`,
  );
};

const workflowAceptado = (texto, mensaje) => assert.deepEqual(revisarWorkflow(texto), [], mensaje);

describe('workflow de integración continua', () => {
  test('CI_WorkflowActual_SinProblemas', () => {
    const texto = readFileSync(join(REPOSITORIO, '.github', 'workflows', 'ci.yml'), 'utf8');
    assert.deepEqual(revisarWorkflow(texto), [], 'El .github/workflows/ci.yml real tiene problemas.');
    // Los saltos de línea de Windows (un clon con autocrlf) no cambian el resultado.
    assert.deepEqual(revisarWorkflow(texto.replace(/\r?\n/g, '\r\n')), [], 'Con CRLF el ci.yml real tiene problemas.');

    // La referencia de las pruebas es el ci.yml real sin sus líneas de comentario: si uno cambia, el otro también.
    const sinLineasDeComentario = texto
      .split(/\r?\n/)
      .filter((linea) => !/^\s*#/.test(linea))
      .join('\n');
    assert.equal(WORKFLOW_VALIDO, sinLineasDeComentario, 'WORKFLOW_VALIDO no coincide con el ci.yml real.');
  });

  test('CI_ComandosEnAgents', () => {
    const comandos = comandosDelProyecto();
    assert.deepEqual(
      comandos.map(({ directorio, comando }) => `${directorio} ${comando}`),
      [
        'web npm ci',
        'e2e npm ci',
        '. dotnet build Optica.sln',
        '. dotnet test Optica.sln',
        'web npm run lint',
        'web npm run test:lint',
        'web npm run test:coverage',
        'web npm run build',
        'e2e npm run test:unidad',
        'e2e npm run test:harness',
      ],
    );

    const faltantes = (agents) => {
      const stack = seccionStack(agents);
      return comandos.map(({ comando }) => comando).filter((comando) => !stack.includes(comando));
    };

    const agents = readFileSync(join(REPOSITORIO, 'AGENTS.md'), 'utf8');
    assert.deepEqual(faltantes(agents), [], 'Hay comandos de COMANDOS_DEL_PROYECTO que no figuran en Stack.');

    // Un comando que está en AGENTS.md pero fuera de la sección Stack no cuenta, y se nombra.
    const stackSinLint =
      '# Proyecto\n\n## Stack\n\n- Install: npm ci\n- Build: dotnet build Optica.sln\n---\n\n## Otra\n\nnpm run lint\n';
    assert.ok(faltantes(stackSinLint).includes('npm run lint'), 'npm run lint fuera de Stack tiene que faltar.');
    assert.ok(!faltantes(stackSinLint).includes('npm ci'), 'npm ci está en Stack.');

    // La sección termina en el siguiente título de nivel 2.
    assert.equal(seccionStack('## Stack\nuno\n## Testing\ndos\n').includes('dos'), false);

    assert.throws(() => seccionStack('# Proyecto\n\n## Testing\n\nnpm ci\n'), {
      name: 'Error',
      message: 'AGENTS.md no tiene la sección Stack',
    });
  });

  test('CI_SoloPush_Rechazado', () => {
    const disparador = 'on:\n  pull_request:\n';
    workflowRechazado(cambiar(WORKFLOW_VALIDO, disparador, 'on: push\n'), 'disparador', 'on: push');
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, disparador, 'on: [pull_request, push]\n'),
      'disparador',
      'on: [pull_request, push]',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, disparador, "on:\n  pull_request:\n  schedule:\n    - cron: '0 3 * * *'\n"),
      'disparador',
      'pull_request con schedule',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, disparador, 'on:\n  push:\n    branches: [master]\n'),
      'disparador',
      'push en bloque',
    );
    workflowRechazado(cambiar(WORKFLOW_VALIDO, disparador, ''), 'disparador', 'sin on:');

    // Un flujo de YAML partido en varias líneas.
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, disparador, 'on: [pull_request,\n  push]\n'),
      'disparador',
      'on: [pull_request, push] en dos líneas',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, disparador, 'on: {pull_request: {},\n  workflow_dispatch: {}}\n'),
      'disparador',
      'on: {pull_request, workflow_dispatch} en dos líneas',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, disparador, 'on: {pull_request: {types: [opened, reopened]}, push: {}}\n'),
      'disparador',
      'push después de un pull_request con valores anidados',
    );
    workflowRechazado(cambiar(WORKFLOW_VALIDO, disparador, 'on: [pull_request,\n'), 'disparador', 'flujo sin cerrar');

    workflowAceptado(
      cambiar(WORKFLOW_VALIDO, disparador, 'on: {pull_request: {types: [opened,\n  reopened]}}\n'),
      'Un flujo partido con solo pull_request vale.',
    );
    workflowAceptado(cambiar(WORKFLOW_VALIDO, disparador, 'on: pull_request\n'), 'on: pull_request en línea vale.');
    workflowAceptado(cambiar(WORKFLOW_VALIDO, disparador, 'on: [pull_request]\n'), 'on: [pull_request] vale.');
  });

  test('CI_PullRequestTarget_Rechazado', () => {
    const disparador = 'on:\n  pull_request:\n';
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, disparador, 'on:\n  pull_request_target:\n'),
      'disparador-inseguro',
      'pull_request_target',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, disparador, 'on:\n  workflow_run:\n    workflows: [CI]\n'),
      'disparador-inseguro',
      'workflow_run',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, disparador, 'on: [pull_request, pull_request_target]\n'),
      'disparador-inseguro',
      'pull_request_target junto a pull_request',
    );
  });

  test('CI_ComandoFaltante_Rechazado', () => {
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, '      - name: Lint de la web\n        working-directory: web\n        run: npm run lint\n', ''),
      'comandos',
      'sin npm run lint',
    );
    const enE2e = cambiar(
      WORKFLOW_VALIDO,
      '        working-directory: web\n        run: npm run build\n',
      '        working-directory: e2e\n        run: npm run build\n',
    );
    workflowRechazado(enE2e, 'comandos', 'npm run build en e2e');
    assert.ok(
      revisarWorkflow(enE2e).some((p) => p.regla === 'comandos' && p.detalle.includes('npm run build')),
      'El problema nombra el comando.',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, '      - name: Compilar .NET\n        run:', '      - name: Compilar .NET\n        working-directory: web\n        run:'),
      'comandos',
      'dotnet build en web en lugar de la raíz',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, 'run: npm run test:harness', 'run: npm run test:harness -- --project chrome-acceso'),
      'comandos',
      'test:harness con otros argumentos',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, '      - name: Pruebas de .NET\n        run: dotnet test Optica.sln\n', '      - name: Pruebas de .NET\n        run: ""\n'),
      'comandos',
      'run vacío en lugar de dotnet test',
    );

    // Un paso que puede fallar sin cortar el job, o que puede no correr, no cuenta.
    const lint = '      - name: Lint de la web\n        working-directory: web\n        run: npm run lint\n';
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, lint, `${lint}        continue-on-error: true\n`),
      'comandos',
      'npm run lint con continue-on-error',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, lint, `${lint}        if: \${{ false }}\n`),
      'comandos',
      'npm run lint con if',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, '    timeout-minutes: 45\n', '    timeout-minutes: 45\n    continue-on-error: true\n'),
      'comandos',
      'job con continue-on-error',
    );

    // defaults con working-directory cambia el directorio de los pasos sin que lo diga cada paso.
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, 'jobs:\n', 'defaults:\n  run:\n    working-directory: web\n\njobs:\n'),
      'comandos',
      'defaults con working-directory de primer nivel',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, '    timeout-minutes: 45\n', '    timeout-minutes: 45\n    defaults:\n      run:\n        working-directory: e2e\n'),
      'comandos',
      'defaults con working-directory a nivel del job',
    );
    workflowAceptado(
      cambiar(WORKFLOW_VALIDO, '    timeout-minutes: 45\n', '    timeout-minutes: 45\n    defaults:\n      run:\n        shell: bash\n'),
      'defaults sin working-directory vale.',
    );
    workflowAceptado(
      cambiar(WORKFLOW_VALIDO, lint, `${lint}        continue-on-error: false\n`),
      'continue-on-error: false no cambia nada.',
    );
  });

  test('CI_PasosSinNombre_Rechazado', () => {
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, '      - name: Compilar .NET\n        run: dotnet build Optica.sln\n', '      - run: dotnet build Optica.sln\n'),
      'pasos-con-nombre',
      'un - run: sin name',
    );

    // El último paso sin nombre, seguido del name del job: ese name no es del paso.
    const sinNombreDelJob = cambiar(WORKFLOW_VALIDO, '    name: Verificación\n', '');
    workflowRechazado(
      cambiar(
        sinNombreDelJob,
        '      - name: Pruebas de punta a punta (Chrome y Edge)\n        working-directory: e2e\n        run: npm run test:harness\n',
        '      - working-directory: e2e\n        run: npm run test:harness\n    name: Verificación\n',
      ),
      'pasos-con-nombre',
      'un paso cuyo único name es el del job',
    );

    workflowRechazado(
      cambiar(
        WORKFLOW_VALIDO,
        '      - name: Compilar .NET\n        run: dotnet build Optica.sln\n',
        '      - run: |\n          name: Compilar .NET\n          dotnet build Optica.sln\n',
      ),
      'pasos-con-nombre',
      'un name: escrito dentro de un run: |',
    );
  });

  test('CI_NpmInstall_Rechazado', () => {
    const web = '        working-directory: web\n        run: npm ci\n';
    const con = (comando) => cambiar(WORKFLOW_VALIDO, web, `        working-directory: web\n        run: ${comando}\n`);
    workflowRechazado(con('npm install'), 'instalacion', 'npm install');
    workflowRechazado(con('npm i'), 'instalacion', 'npm i al final de la línea');
    workflowRechazado(con('npm add x'), 'instalacion', 'npm add x');
    workflowRechazado(con('npm ci && npm install-test'), 'instalacion', 'npm install-test');
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, 'npx --no-install playwright', 'npx playwright'),
      'instalacion',
      'npx sin --no-install',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, '          microsoft-edge --version\n', '          microsoft-edge --version\n          npm install\n'),
      'instalacion',
      'npm install en un run: |',
    );

    // Opciones entre npm y el subcomando, con o sin valor.
    workflowRechazado(con('npm --prefix web install left-pad'), 'instalacion', 'npm --prefix web install');
    workflowRechazado(con('npm -w x install'), 'instalacion', 'npm -w x install');
    workflowRechazado(con('npm --silent i'), 'instalacion', 'npm --silent i');
    workflowRechazado(con('npm --prefix=web add x'), 'instalacion', 'npm --prefix=web add');

    // Abreviaturas y errores de tipeo que npm acepta como install, y los que instalan o actualizan por otra vía.
    for (const subcomando of ['in', 'ins', 'inst', 'insta', 'instal', 'isntall', 'exec x', 'x playwright', 'update', 'up']) {
      workflowRechazado(con(`npm ${subcomando}`), 'instalacion', `npm ${subcomando}`);
    }

    // Un run escalar plano partido en varias líneas: las siguientes son su continuación.
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, web, '        working-directory: web\n        run:\n          npm install\n'),
      'instalacion',
      'run: vacío con npm install en la línea siguiente',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, web, '        working-directory: web\n        run: npm ci &&\n          npm install x\n'),
      'instalacion',
      'npm install x en la continuación de un run plano',
    );

    // npm run y npm ci con opciones no instalan nada fuera del archivo de bloqueo.
    workflowAceptado(
      cambiar(WORKFLOW_VALIDO, '        run: dotnet dev-certs https\n', '        run: npm --version && npm run --silent env\n'),
      'npm --version y npm run no son instalaciones.',
    );

    workflowAceptado(
      cambiar(WORKFLOW_VALIDO, web, '        working-directory: web\n        run: npm ci # npm ci, no npm install\n'),
      'Un npm install nombrado en un comentario no cuenta.',
    );
  });

  test('CI_ReferenciaASecretos_Rechazada', () => {
    const env = '      DOTNET_NOLOGO: "true"\n';
    workflowRechazado(cambiar(WORKFLOW_VALIDO, env, `${env}      TOKEN: \${{ secrets.TOKEN }}\n`), 'secretos', 'secrets.TOKEN');
    workflowRechazado(cambiar(WORKFLOW_VALIDO, env, `${env}      TOKEN: \${{ github.token }}\n`), 'secretos', 'github.token');
    workflowRechazado(cambiar(WORKFLOW_VALIDO, env, `${env}      GITHUB_TOKEN: x\n`), 'secretos', 'GITHUB_TOKEN');
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, env, `${env}      TOKEN: \${{ github['token'] }}\n`),
      'secretos',
      "github['token']",
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, env, `${env}      TOKEN: \${{ github[ "token" ] }}\n`),
      'secretos',
      'github["token"]',
    );
  });

  test('CI_CheckoutConCredenciales_Rechazado', () => {
    const conCredenciales = `@${SHA_CHECKOUT} # v7.0.1\n        with:\n          persist-credentials: false\n`;
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, conCredenciales, `@${SHA_CHECKOUT} # v7.0.1\n`),
      'credenciales',
      'checkout sin with',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, conCredenciales, `@${SHA_CHECKOUT} # v7.0.1\n        with:\n          persist-credentials: true\n`),
      'credenciales',
      'checkout con persist-credentials: true',
    );
    // persist-credentials: false en otro paso no cuenta para el checkout.
    workflowRechazado(
      cambiar(
        cambiar(WORKFLOW_VALIDO, conCredenciales, `@${SHA_CHECKOUT} # v7.0.1\n`),
        '          node-version: 22\n',
        '          node-version: 22\n          persist-credentials: false\n',
      ),
      'credenciales',
      'persist-credentials en otro paso',
    );
  });

  test('CI_PermisoDeEscritura_Rechazado', () => {
    workflowRechazado(cambiar(WORKFLOW_VALIDO, 'contents: read', 'contents: write'), 'permisos', 'contents: write');
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, 'permissions:\n  contents: read\n', 'permissions: write-all\n'),
      'permisos',
      'permissions: write-all',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, '    timeout-minutes: 45\n', '    timeout-minutes: 45\n    permissions:\n      pull-requests: write\n'),
      'permisos',
      'permiso de escritura a nivel del job',
    );
    workflowRechazado(cambiar(WORKFLOW_VALIDO, 'permissions:\n  contents: read\n', ''), 'permisos', 'sin permissions');
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, 'permissions:\n  contents: read\n', 'permissions: read-all\n'),
      'permisos',
      'permissions: read-all de primer nivel',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, '    timeout-minutes: 45\n', '    timeout-minutes: 45\n    permissions: read-all\n'),
      'permisos',
      'permissions: read-all a nivel del job',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, 'permissions:\n  contents: read\n', 'permissions: { contents: read, issues: write }\n'),
      'permisos',
      'write en un flujo de permissions',
    );

    // "write" fuera de un bloque permissions (el nombre de un paso) no es un permiso.
    workflowAceptado(
      cambiar(WORKFLOW_VALIDO, '      - name: Compilar .NET\n', '      - name: Compilar .NET (sin --write)\n'),
      'Un paso llamado "sin --write" no pide permisos.',
    );
  });

  test('CI_AccionNoPermitida_Rechazada', () => {
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, `actions/checkout@${SHA_CHECKOUT} # v7.0.1`, 'actions/checkout@v7'),
      'acciones',
      'actions/checkout@v7',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, `actions/checkout@${SHA_CHECKOUT}`, `otro/checkout@${SHA_OTRO}`),
      'acciones',
      'otro/checkout con SHA',
    );
    workflowRechazado(
      cambiar(
        WORKFLOW_VALIDO,
        '      - name: Pruebas de punta a punta',
        `      - name: Subir resultados\n        uses: actions/upload-artifact@${SHA_OTRO}\n      - name: Pruebas de punta a punta`,
      ),
      'acciones',
      'actions/upload-artifact con SHA',
    );
  });

  test('CI_SinTiempoMaximo_Rechazado', () => {
    const tiempo = '    timeout-minutes: 45\n';
    workflowRechazado(cambiar(WORKFLOW_VALIDO, tiempo, ''), 'tiempo-maximo', 'sin timeout-minutes');
    workflowRechazado(cambiar(WORKFLOW_VALIDO, tiempo, '    timeout-minutes: 60\n'), 'tiempo-maximo', 'timeout-minutes: 60');
    workflowRechazado(
      cambiar(
        cambiar(WORKFLOW_VALIDO, tiempo, ''),
        '        run: npm run test:harness\n',
        '        run: npm run test:harness\n        timeout-minutes: 30\n',
      ),
      'tiempo-maximo',
      'timeout-minutes solo en un paso',
    );
  });

  test('CI_SinCancelacion_Rechazado', () => {
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, '  cancel-in-progress: true\n', ''),
      'cancelacion',
      'sin cancel-in-progress',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, 'cancel-in-progress: true', 'cancel-in-progress: false'),
      'cancelacion',
      'cancel-in-progress: false',
    );
    workflowRechazado(
      cambiar(WORKFLOW_VALIDO, 'group: ci-${{ github.event.pull_request.number }}', 'group: ci'),
      'cancelacion',
      'group: ci constante',
    );
    workflowRechazado(
      cambiar(
        WORKFLOW_VALIDO,
        'concurrency:\n  group: ci-${{ github.event.pull_request.number }}\n  cancel-in-progress: true\n',
        '',
      ),
      'cancelacion',
      'sin concurrency',
    );
  });

  test('CI_WorkflowVacio_Rechazado', () => {
    assert.deepEqual(reglasDe(''), ['vacio'], 'Un texto vacío se rechaza solo por vacío.');
    assert.deepEqual(reglasDe('  \n\t\n '), ['vacio'], 'Un texto con solo espacios se rechaza solo por vacío.');
  });
});

/** Archivos de prueba que revisa FR-10: los de .NET, los de la web y los de la suite de punta a punta. */
const ARCHIVOS_DE_PRUEBA = [
  /^tests\/.+\.cs$/,
  /^web\/src\/.+\.test\.tsx?$/,
  /^web\/src\/pruebas\//,
  /^web\/vite\.config\.ts$/,
  /^web\/lint\.test\.mjs$/,
  /^e2e\/tests\//,
  /^e2e\/[^/]+\.mjs$/,
  /^e2e\/playwright\.config\.ts$/,
];

const esArchivoDePrueba = (ruta) => ARCHIVOS_DE_PRUEBA.some((patron) => patron.test(ruta));

/** Cada host externo como `archivo: host`, así el rechazo nombra los dos. */
const hostsExternosPorArchivo = (rutas, leer) =>
  rutas.flatMap((ruta) => hostsExternos(leer(ruta)).map((host) => `${ruta}: ${host}`));

/** Una URL armada por partes: escrita entera en este archivo, la prueba la encontraría en sí mismo. */
const url = (esquema, resto) => `${esquema}://${resto}`;

/** Los navegadores que cuentan para NFR-03: la suite sin la variable del diagnóstico. */
const NAVEGADORES_DE_LA_SUITE = ['chrome', 'msedge'];

describe('pruebas sin servicios externos', () => {
  test('Pruebas_SinHostsExternos', () => {
    const rutas = archivosVersionados().filter(esArchivoDePrueba);
    for (const esperada of [
      'e2e/playwright.config.ts',
      'e2e/ci.test.mjs',
      'e2e/servidor.mjs',
      'e2e/tests/acceso.spec.ts',
      'web/vite.config.ts',
      'web/lint.test.mjs',
      'web/src/configuracionVite.test.ts',
      'web/src/pruebas/servidorFalso.tsx',
      'tests/Optica.Api.Tests/FabricaApi.cs',
    ]) {
      assert.ok(rutas.includes(esperada), `La revisión no recorre ${esperada}.`);
    }

    const leer = (ruta) => readFileSync(join(REPOSITORIO, ruta), 'utf8');
    assert.deepEqual(hostsExternosPorArchivo(rutas, leer), [], 'Hay archivos de prueba con URL a hosts externos.');

    // Si un archivo tuviera una, el rechazo nombra el archivo y el host.
    const falsos = {
      'web/src/uno.test.ts': `fetch('${url('https', 'servicios1.afip.gov.ar/wsfev1')}')`,
      'e2e/tests/dos.spec.ts': `await page.goto('${url('http', 'localhost:5543')}/')`,
    };
    assert.deepEqual(hostsExternosPorArchivo(Object.keys(falsos), (ruta) => falsos[ruta]), [
      'web/src/uno.test.ts: servicios1.afip.gov.ar',
    ]);
  });

  test('Pruebas_HostExterno_Rechazado', () => {
    assert.deepEqual(hostsExternos(''), [], 'Un texto vacío no tiene hosts.');

    const externos = [
      url('https', 'servicios1.afip.gov.ar/wsfev1/service.asmx'),
      url('http', '1.2.3.4:8080/x'),
      url('https', 'localhost.evil.com/'),
    ];
    assert.deepEqual(hostsExternos(externos.join('\n')), ['servicios1.afip.gov.ar', '1.2.3.4', 'localhost.evil.com']);

    const locales = [
      url('https', 'localhost:5543/api/acceso/estado'),
      url('https', '[::1]:5001'),
      url('https', '192.168.1.20:5001'),
      url('https', 'api.optica.example'),
      url('http', 'www.w3.org/2000/svg'),
      url('http', '127.0.0.1:5001'),
      url('https', '10.0.0.7'),
      url('https', '172.16.0.1'),
      url('https', '172.31.255.254'),
      url('https', 'example.com'),
      url('https', 'localhost.example.com'),
    ];
    assert.deepEqual(hostsExternos(locales.join(' ')), [], 'Los hosts locales y reservados no son externos.');

    // Fuera de los rangos privados, nombres que solo se parecen a uno permitido, y un IPv6 que no es ::1.
    for (const resto of [
      '172.15.0.1',
      '172.32.0.1',
      '127.0.0.2',
      '10.evil.com',
      'w3.org',
      'notexample.com',
      'example.com.evil.net',
      'optica.example.evil.net',
      '[::2]:5001',
    ]) {
      assert.equal(hostsExternos(`"${url('https', resto)}"`).length, 1, `${resto} tiene que ser externo.`);
    }

    // IPv4 que el parser de URL no lee como privadas: con cero inicial es octal (010 es 8) y un octeto pasa de 255.
    for (const resto of ['010.0.0.1/', '10.0.0.999', '192.168.01.20', '0172.16.0.1', '10.1', '0x0a.0.0.1']) {
      assert.deepEqual(hostsExternos(url('https', resto)), [resto.replace('/', '')], `${resto} tiene que ser externo.`);
    }

    // El usuario y la contraseña de la URL no son el host: el parser toma lo que sigue al último `@`.
    assert.deepEqual(hostsExternos(url('https', 'localhost:5001@evil.com/')), ['evil.com']);
    assert.deepEqual(hostsExternos(url('https', 'localhost@localhost@evil.com/')), ['evil.com']);
    assert.deepEqual(hostsExternos(url('https', 'a@b@evil.com')), ['evil.com']);
    // Un `@` en la ruta no es parte del usuario.
    assert.deepEqual(hostsExternos(url('https', 'localhost/a@evil.com')), []);

    // Un host con caracteres que no son ASCII (un punto ideográfico, letras de ancho completo) se informa tal cual.
    assert.deepEqual(hostsExternos(url('https', 'localhost。evil.com/')), ['localhost。evil.com']);
    assert.deepEqual(hostsExternos(url('https', 'Ｌｏｃａｌｈｏｓｔ')), [
      'ｌｏｃａｌｈｏｓｔ',
    ]);

    // De www.w3.org solo valen los espacios de nombres y la DTD de SVG 1.1 (que LogoMarca.cs rechaza sin pedirla).
    for (const ruta of ['/2000/svg', '/1999/xhtml', '/1999/xlink', '/Graphics/SVG/1.1/DTD/svg11.dtd']) {
      assert.deepEqual(hostsExternos(`"${url('http', `www.w3.org${ruta}`)}"`), [], `www.w3.org${ruta} vale.`);
    }
    const otrasDeW3 = ['www.w3.org', 'www.w3.org/', 'www.w3.org/TR/xml', 'www.w3.org/2000/svg/otra', 'www.w3.org?x'];
    for (const resto of otrasDeW3) {
      assert.deepEqual(hostsExternos(`'${url('https', resto)}'`), ['www.w3.org'], `${resto} tiene que ser externo.`);
    }

    // En minúsculas y sin repetir, aunque el esquema esté en mayúsculas.
    assert.deepEqual(
      hostsExternos(`${url('HTTPS', 'Servicios1.AFIP.gov.ar')} ${url('https', 'servicios1.afip.gov.ar/x')}`),
      ['servicios1.afip.gov.ar'],
    );

    // Una URL armada en tiempo de ejecución no tiene host escrito: la revisión es de texto.
    assert.deepEqual(hostsExternos("const base = 'https://' + host;"), []);
    assert.deepEqual(hostsExternos('`https://${host}/api`'), []);
  });

  test('E2E_NavegadorSinRegla_Rechazado', async () => {
    const configuracion = new URL('./playwright.config.ts', import.meta.url);
    const importar = async (variante) => {
      configuracion.search = `?variante=${variante}`;
      return (await import(configuracion.href)).default.projects;
    };

    const variableOriginal = process.env.OPTICA_E2E_DIAGNOSTICO_CHROMIUM;
    let normal;
    let diagnostico;
    try {
      delete process.env.OPTICA_E2E_DIAGNOSTICO_CHROMIUM;
      normal = await importar('normal');
      process.env.OPTICA_E2E_DIAGNOSTICO_CHROMIUM = '1';
      diagnostico = await importar('diagnostico');
    } finally {
      if (variableOriginal === undefined) delete process.env.OPTICA_E2E_DIAGNOSTICO_CHROMIUM;
      else process.env.OPTICA_E2E_DIAGNOSTICO_CHROMIUM = variableOriginal;
    }

    assert.ok(normal.length > 0, 'La configuración no tiene proyectos.');
    assert.deepEqual(proyectosSinRegla(normal), [], 'Hay proyectos sin la regla de resolución de nombres.');
    assert.deepEqual(proyectosSinRegla(diagnostico), [], 'Hay proyectos del diagnóstico sin la regla.');
    assert.deepEqual([...prefijosDeNavegador(normal)].sort(), ['chrome', 'msedge']);
    assert.deepEqual([...prefijosDeNavegador(diagnostico)].sort(), ['chrome', 'chromium', 'msedge']);

    assert.deepEqual(navegadoresFaltantes(normal, NAVEGADORES_DE_LA_SUITE), []);
    assert.deepEqual(navegadoresFaltantes(diagnostico, NAVEGADORES_DE_LA_SUITE), []);

    // Una lista con un proyecto sin la regla (sin args, sin launchOptions o con otra regla) lo nombra.
    const conRegla = (name) => ({ name, use: { launchOptions: { args: ['--otro', reglaSinRedExterna()] } } });
    assert.deepEqual(
      proyectosSinRegla([
        conRegla('chrome-acceso'),
        { name: 'msedge-acceso', use: {} },
        { name: 'chromium-acceso', use: { launchOptions: {} } },
        { name: 'chrome-articulos', use: { launchOptions: { args: ['--host-resolver-rules=MAP * 127.0.0.1'] } } },
        conRegla('msedge-articulos'),
      ]),
      ['msedge-acceso', 'chromium-acceso', 'chrome-articulos'],
    );

    // Sin proyectos msedge la lista queda rechazada nombrando el navegador que falta, aunque todos tengan la regla.
    const soloChrome = [conRegla('chrome-acceso'), conRegla('chrome-articulos')];
    assert.deepEqual(proyectosSinRegla(soloChrome), []);
    assert.deepEqual(navegadoresFaltantes(soloChrome, NAVEGADORES_DE_LA_SUITE), ['msedge']);
    assert.deepEqual(navegadoresFaltantes([], NAVEGADORES_DE_LA_SUITE), ['chrome', 'msedge']);
    // Un nombre que solo empieza igual (`msedgex-…`) no es un proyecto msedge.
    assert.deepEqual(navegadoresFaltantes([...soloChrome, conRegla('msedgex-acceso')], NAVEGADORES_DE_LA_SUITE), [
      'msedge',
    ]);
  });
});
