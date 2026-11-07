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
