# Spec FEAT-005: Integración continua para los pull requests

| Field | Value |
|-------|-------|
| Ticket | FEAT-005 |
| PRD | docs/ddw/prd/prd-FEAT-005.md |
| Tier | FEATURE |
| Date | 2026-11-07 |
| Spec loops | 4 |
| Loops since last human decision | 0 |

## Summary
Un workflow de GitHub Actions (`.github/workflows/ci.yml`) corre en cada pull request, en un solo job sobre
`ubuntu-24.04`, los comandos que `AGENTS.md` declara: `npm ci` en `web/` y `e2e/`, `dotnet build Optica.sln`,
`dotnet test Optica.sln`, el lint de la web y su prueba, las pruebas de la web con cobertura, la compilación de la web,
las pruebas de unidad de e2e y la suite de punta a punta en Chrome y Edge. Sin secretos, con permiso de solo lectura,
con tiempo máximo de 45 minutos y cancelando la corrida anterior del mismo pull request. .NET pasa a tener archivos de
bloqueo de NuGet, que la corrida exige en modo bloqueado (decisión del usuario, D-04). Las reglas del workflow, del
bloqueo de NuGet y de los hosts de las pruebas se fijan con pruebas `node:test` sobre funciones de `e2e/ci.mjs`, que
corren dentro de `npm run test:unidad` (decisión del usuario, D-07), y los tres navegadores de Playwright reciben la
regla de resolución de nombres que hoy solo tiene Edge. El job usa una carpeta del usuario (`HOME`) fuera del código,
porque en GitHub Actions el checkout queda dentro del perfil y las pruebas que verifican que los logs no lleven la
carpeta del usuario la encontraban (Block 4, decisión del usuario D-20, lazo correctivo desde CODE tras la primera
corrida del PR #11). Decisiones: `docs/ddw/specs/decisions-FEAT-005.md`.

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 2 |
| FR-02 | Block 1, Block 2 |
| FR-03 | Block 2, Block 4 |
| FR-04 | Block 2 |
| FR-05 | Block 2 |
| FR-06 | Block 2 |
| FR-07 | Block 2 |
| FR-08 | Block 2 |
| FR-09 | Block 2 |
| FR-10 | Block 3 |
| NFR-01 | Block 2 — `timeout-minutes: 45` a nivel del job (regla `tiempo-maximo`); estimación con las corridas locales de FEAT-004: `dotnet test` ≈ 2 min, web ≈ 1 min, `test:harness` 3,6 min, más instalación y compilaciones ≈ 6 min: unos 13 min, margen mayor a 3 veces |
| NFR-02 | Block 2 — `concurrency` con `group` por número de pull request y `cancel-in-progress: true` (regla `cancelacion`) |

## Dependencies between blocks
Block 1 crea `e2e/ci.mjs`, `e2e/ci.test.mjs` y el script de `test:unidad`; Block 2 y Block 3 agregan funciones y
pruebas a esos archivos. Block 2 necesita los archivos de bloqueo del Block 1 (la corrida restaura en modo bloqueado).
Block 4 modifica el `ci.yml` del Block 2 y agrega una regla a `revisarWorkflow`. Orden: Block 1, Block 2, Block 3,
Block 4.

## Block 1 — Bloqueo de dependencias de .NET

**Files**
- `Directory.Build.props` (modified) — `RestorePackagesWithLockFile` en `true` para todos los proyectos y
  `RestoreLockedMode` en `true` cuando la variable `CI` vale `true` (GitHub Actions la define).
- `src/Optica.Api/packages.lock.json`, `src/Optica.Application/packages.lock.json`,
  `src/Optica.Domain/packages.lock.json`, `src/Optica.Infrastructure/packages.lock.json`,
  `tests/Optica.Api.Tests/packages.lock.json`, `tests/Optica.Application.Tests/packages.lock.json`,
  `tests/Optica.Domain.Tests/packages.lock.json`, `tests/Optica.Infrastructure.Tests/packages.lock.json` (new) —
  generados por `dotnet restore Optica.sln`, sin editarlos a mano.
- `e2e/ci.mjs` (new) — `proyectosSinLockfile(archivos)` y `revisarPropsNuGet(texto)`.
- `e2e/ci.test.mjs` (new) — pruebas `node:test` del bloque.
- `e2e/package.json` (modified) — `test:unidad` pasa a `node --test compilacion.test.mjs ci.test.mjs`.
- `e2e/README.md` (modified) — `test:unidad` corre también las pruebas de `ci.mjs` (reglas de la CI).

**Logic**
Implementa FR-02 para .NET. Con `RestoreLockedMode` en la corrida de CI, `dotnet build` y `dotnet test` (que
restauran solos) fallan si un `PackageReference` no coincide con su `packages.lock.json` (error NU1004). En la PC de
desarrollo no hay modo bloqueado: agregar o cambiar un paquete actualiza el archivo de bloqueo, que se commitea con el
cambio. `proyectosSinLockfile(archivos)` recibe las rutas versionadas (`git ls-files`) y devuelve cada `.csproj` sin un
`packages.lock.json` en su carpeta. `revisarPropsNuGet(texto)` devuelve los problemas del `Directory.Build.props`.

**Input validation**
- `proyectosSinLockfile(archivos)`: arreglo de strings con rutas relativas separadas por `/`; un arreglo vacío
  devuelve un arreglo vacío.
- `revisarPropsNuGet(texto)`: string; vacío → un problema `vacio`.

**Error handling**
- Un archivo de bloqueo desincronizado con su proyecto — en la corrida `dotnet build` falla con NU1004 y el pull
  request queda rechazado en ese paso.
- Un proyecto sin `packages.lock.json` — la prueba lo rechaza nombrando el `.csproj`.
- Un `Directory.Build.props` sin `RestorePackagesWithLockFile` o sin el modo bloqueado en CI — la prueba lo rechaza
  nombrando la propiedad.

**Required tests**
- [ ] `NuGet_CadaProyectoConLockfile` — los 8 `.csproj` versionados tienen su `packages.lock.json` y el
      `Directory.Build.props` real no tiene problemas; validates AC-02
- [ ] `NuGet_LockfileFaltante_Rechazado` — una lista con un `.csproj` sin su archivo de bloqueo queda rechazada con
      el nombre del proyecto; validates AC-02
- [ ] `NuGet_SinModoBloqueado_Rechazado` — un props sin `RestoreLockedMode` condicionado a `CI` queda rechazado, y
      uno vacío también; validates AC-02
- [ ] `NuGet_LockfileDesincronizado_Rechazado` — manual en CODE: con `CI=true`, cambiar la versión de un
      `PackageReference` sin restaurar hace fallar `dotnet build Optica.sln` con NU1004; se deshace y se anota en el
      informe; validates AC-02

**Completion criterion**
`dotnet build Optica.sln` y `dotnet test Optica.sln` pasan con `CI=true` y los 8 archivos de bloqueo, y
`npm run test:unidad` en `e2e/` pasa con las pruebas de `compilacion.test.mjs` y las 3 automáticas de este bloque.

## Block 2 — Workflow de CI y sus reglas

**Files**
- `.github/workflows/ci.yml` (new) — el workflow.
- `e2e/ci.mjs` (modified) — `COMANDOS_DEL_PROYECTO`, `seccionStack(texto)` y `revisarWorkflow(texto)`.
- `e2e/ci.test.mjs` (modified) — pruebas del bloque.

**Logic**
Implementa FR-01 a FR-09. El workflow:
- `on: pull_request` (tipos por defecto: abierto, sincronizado, reabierto); `permissions: contents: read`;
  `concurrency` con `group: ci-${{ github.event.pull_request.number }}` y `cancel-in-progress: true`.
- Un job `verificacion` en `ubuntu-24.04` con `timeout-minutes: 45` y `env` sin secretos (`DOTNET_NOLOGO`,
  `DOTNET_CLI_TELEMETRY_OPTOUT`). Pasos, cada uno con `name`, en este orden:
  1. `actions/checkout` con `persist-credentials: false`, `actions/setup-dotnet` (`dotnet-version: 10.0.x`) y
     `actions/setup-node` (`node-version: 22`), fijadas por SHA con la versión en un comentario (D-02).
  2. `npm ci` con `working-directory: web` y con `working-directory: e2e` (FR-02).
  3. `dotnet build Optica.sln` y `dotnet test Optica.sln` (FR-03; modo bloqueado del Block 1).
  4. Con `working-directory: web`: `npm run lint`, `npm run test:lint`, `npm run test:coverage`, `npm run build`
     (FR-04, FR-05).
  5. Con `working-directory: e2e`: `npm run test:unidad`; `npx --no-install playwright install-deps chrome msedge` (usa el Playwright fijado en
     `e2e/package-lock.json`, nunca uno bajado del registro); un paso que
     imprime `google-chrome --version` y `microsoft-edge --version`; `dotnet dev-certs https`; `npm run test:harness`
     (FR-06, D-06).
Un paso que termina con código distinto de 0 corta el job y GitHub marca el pull request con el nombre del paso
(FR-07).

`COMANDOS_DEL_PROYECTO` es la lista fija de pares (directorio, comando) de `AGENTS.md`: (`web`, `npm ci`),
(`e2e`, `npm ci`), (`.`, `dotnet build Optica.sln`), (`.`, `dotnet test Optica.sln`), (`web`, `npm run lint`),
(`web`, `npm run test:lint`), (`web`, `npm run test:coverage`), (`web`, `npm run build`), (`e2e`,
`npm run test:unidad`), (`e2e`, `npm run test:harness`). Una prueba verifica que cada comando figura en la sección
"Stack" de `AGENTS.md`, así un cambio en `AGENTS.md` sin actualizar la lista (o al revés) falla.

`revisarWorkflow(texto)` devuelve una lista de problemas `{ regla, detalle }` (vacía si cumple todo). Los pasos se
leen así: la línea `steps:` fija una indentación n; cada línea con n + 2 espacios y `- ` empieza un paso, que termina
en el siguiente `- ` de esa indentación o en una línea con indentación ≤ n; las claves del paso son las de la línea
`- ` y las de indentación n + 4; el contenido de un `run: |` (indentación mayor) no cuenta como clave. Reglas:
- `vacio`: texto vacío o solo espacios.
- `disparador`: `pull_request` es el único evento de `on:` (sin `push`, `schedule` ni otros: el PRD deja afuera
  correr en cada push o con un horario).
- `disparador-inseguro`: aparece `pull_request_target` o `workflow_run`.
- `permisos`: hay `permissions:` de primer nivel con `contents: read`, y no aparece `write` ni `write-all`.
- `secretos`: aparece `secrets.`, `github.token` o `GITHUB_TOKEN`.
- `credenciales`: un paso con `uses: actions/checkout@…` sin `persist-credentials: false` en su `with`.
- `acciones`: un `uses:` que no es `actions/checkout`, `actions/setup-dotnet` o `actions/setup-node` seguido de `@` y
  un SHA de 40 caracteres hexadecimales (rechaza etiquetas, otros dueños y acciones como `upload-artifact` o
  `cache`, así la corrida no publica artefactos con bases o resultados).
- `tiempo-maximo`: el job no tiene `timeout-minutes:` a su nivel (indentación de las claves del job) con un valor
  entre 1 y 45; un `timeout-minutes` de paso no cuenta.
- `cancelacion`: `concurrency:` sin `cancel-in-progress: true` o con un `group:` que no incluye
  `github.event.pull_request.number`.
- `instalacion`: una línea de `run` (sin contar comentarios `#`) con `npm` seguido de `i`, `install`, `add`, `it` o
  `install-test` como palabra completa, o con `npx` sin `--no-install`.
- `pasos-con-nombre`: un paso sin la clave `name:`.
- `comandos`: un par de `COMANDOS_DEL_PROYECTO` sin un paso cuyo `run` sea ese comando y cuyo `working-directory`
  sea ese directorio (sin `working-directory` para `.`).

**Input validation**
- `revisarWorkflow(texto)`: string; vacío o solo espacios → problema `vacio` y ninguna otra regla.
- `seccionStack(texto)`: string; devuelve el texto entre `## Stack` y el siguiente `## ` o `---`; sin `## Stack`
  lanza `Error` con el mensaje "AGENTS.md no tiene la sección Stack".

**Error handling**
- Un paso falla (compilación, lint, prueba) — el job termina en rojo con el nombre del paso y los siguientes no
  corren; GitHub marca el pull request como fallado.
- La corrida supera los 45 minutos — GitHub la cancela y queda como fallada.
- Un workflow con disparador inseguro, secretos, credenciales persistidas, permiso de escritura, acción no permitida,
  sin tiempo máximo, sin cancelación por pull request, con `npm install`, con un paso sin nombre, con un comando
  faltante o vacío — `revisarWorkflow` lo rechaza nombrando la regla.
- Un `AGENTS.md` sin sección "Stack" o sin uno de los comandos de la lista — la prueba lo rechaza nombrando el
  comando.

**Required tests**
- [ ] `CI_WorkflowActual_SinProblemas` — el `ci.yml` real no tiene problemas; validates AC-01, AC-03, AC-04, AC-05,
      AC-06, AC-08, AC-09
- [ ] `CI_ComandosEnAgents` — cada comando de `COMANDOS_DEL_PROYECTO` figura en la sección "Stack" del `AGENTS.md`
      real; un `AGENTS.md` sin "Stack" queda rechazado con el error de `seccionStack`; validates AC-03, AC-04, AC-05,
      AC-06
- [ ] `CI_SoloPush_Rechazado` — un workflow con `on: push`, con `on: [pull_request, push]` o con `schedule` queda
      rechazado por `disparador`; validates AC-01
- [ ] `CI_PullRequestTarget_Rechazado` — `pull_request_target` y `workflow_run` quedan rechazados por
      `disparador-inseguro`; validates AC-08, AC-09
- [ ] `CI_ComandoFaltante_Rechazado` — sin el paso de `npm run lint`, o con `npm run build` en `e2e`, queda rechazado
      por `comandos`; validates AC-03, AC-04, AC-05, AC-06
- [ ] `CI_PasosSinNombre_Rechazado` — un `- run:` sin `name`, un paso cuyo único `name` es el del job y un `name:`
      escrito dentro de un `run: |` quedan rechazados por `pasos-con-nombre`; validates AC-07, AC-11
- [ ] `CI_NpmInstall_Rechazado` — `npm install`, `npm i` al final de la línea y `npm add x` quedan rechazados por
      `instalacion`, y `npx playwright install-deps` sin `--no-install` también; `# npm ci, no npm install` en un
      comentario no; validates AC-02
- [ ] `CI_ReferenciaASecretos_Rechazada` — `${{ secrets.TOKEN }}`, `${{ github.token }}` y `GITHUB_TOKEN` quedan
      rechazados por `secretos`; validates AC-08
- [ ] `CI_CheckoutConCredenciales_Rechazado` — un checkout sin `persist-credentials: false` queda rechazado por
      `credenciales`; validates AC-08
- [ ] `CI_PermisoDeEscritura_Rechazado` — `contents: write` y `permissions: write-all` quedan rechazados por
      `permisos`; validates AC-09
- [ ] `CI_AccionNoPermitida_Rechazada` — `actions/checkout@v7`, `otro/checkout@<sha40>` y
      `actions/upload-artifact@<sha40>` quedan rechazados por `acciones`
- [ ] `CI_SinTiempoMaximo_Rechazado` — sin `timeout-minutes`, con 60, o solo a nivel de un paso queda rechazado por
      `tiempo-maximo`; NFR-01
- [ ] `CI_SinCancelacion_Rechazado` — sin `cancel-in-progress: true` o con `group: ci` constante queda rechazado por
      `cancelacion`; NFR-02
- [ ] `CI_WorkflowVacio_Rechazado` — un texto vacío queda rechazado solo por `vacio`

**Completion criterion**
`npm run test:unidad` pasa con las 14 pruebas de este bloque; los tres SHA de D-02 coinciden con
`git ls-remote https://github.com/actions/<repo> refs/tags/<versión>` (resultado anotado en el informe de pruebas); y la corrida del workflow en el pull request de este
ticket termina en verde (AC-11) después de una corrida en rojo provocada a propósito que nombra el paso que falló
(AC-07).

## Block 3 — Pruebas sin servicios externos

**Files**
- `e2e/playwright.config.ts` (modified) — la regla `--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE localhost` pasa
  a una constante que reciben los tres navegadores (`chrome`, `msedge` y el diagnóstico `chromium`, D-05); el
  comentario explica por qué en todos.
- `e2e/ci.mjs` (modified) — `hostsExternos(texto)` y `proyectosSinRegla(proyectos)`.
- `e2e/ci.test.mjs` (modified) — pruebas del bloque.
- `e2e/README.md` (modified) — la sección "Edge sin red externa" pasa a valer para todos los navegadores de la suite.

**Logic**
Implementa FR-10. `hostsExternos(texto)` busca URL `http://` y `https://` y devuelve los hosts que no son locales ni
reservados. Permitidos, comparados exactos después de quitar los corchetes de IPv6: `localhost`, `127.0.0.1`, `::1`; direcciones privadas `10.*`,
`192.168.*`, `172.16.*`–`172.31.*`; dominios terminados en `.example`; `example.com` y sus subdominios; `www.w3.org`
(espacio de nombres de SVG y XHTML, nunca se pide). Cualquier otra IP literal o nombre es externo. Es una revisión
heurística: ve las URL escritas en los archivos de prueba, no direcciones armadas en tiempo de ejecución; la regla de
resolución cubre los nombres en la suite de punta a punta (no las IP literales, que la revisión de texto sí rechaza).
La prueba recorre los archivos de prueba versionados: `tests/**/*.cs`, `web/src/**/*.test.ts(x)`, `web/src/pruebas/**`,
`web/vite.config.ts`, `web/lint.test.mjs`, `e2e/tests/**`, `e2e/*.mjs` y `e2e/playwright.config.ts`; en
`e2e/ci.test.mjs` las URL externas de ejemplo se arman por partes (`'https://' + host`) para no rechazarse a sí mismo.
`proyectosSinRegla(proyectos)` recibe la lista de proyectos de la configuración ya evaluada (importada con la
eliminación de tipos de Node 22) y devuelve los nombres de los que no tienen la regla en `launchOptions.args`.
`prefijosDeNavegador(proyectos)` devuelve el conjunto de prefijos (`chrome`, `msedge`, `chromium`) de los nombres.
La prueba importa la configuración dos veces con URL distintas (`playwright.config.ts?variante=normal` y
`?variante=diagnostico`, esta con `OPTICA_E2E_DIAGNOSTICO_CHROMIUM=1`, que se devuelve a su valor original al
terminar), porque la caché de módulos ESM devolvería la primera evaluación.

**Input validation**
- `hostsExternos(texto)`: string (vacío → arreglo vacío); devuelve hosts en minúsculas y sin repetir.
- `proyectosSinRegla(proyectos)`: arreglo de objetos con `name` y `use`; un proyecto sin `use.launchOptions.args`
  cuenta como sin regla.

**Error handling**
- Un archivo de prueba con una URL a un host externo — la prueba lo rechaza nombrando el archivo y el host.
- Un proyecto de Playwright sin la regla de resolución — la prueba lo rechaza nombrando el proyecto; con la regla, un
  pedido a un nombre que no sea `localhost` falla en el acto sin salir de la máquina.

**Required tests**
- [ ] `Pruebas_SinHostsExternos` — ningún archivo de prueba versionado tiene una URL a un host externo; si alguno la
      tuviera, queda rechazado con su nombre y el host; validates AC-10
- [ ] `Pruebas_HostExterno_Rechazado` — `hostsExternos` rechaza `servicios1.afip.gov.ar`, `1.2.3.4` y
      `localhost.evil.com` (armados por partes) y acepta `localhost:5543`, `[::1]:5001`, `192.168.1.20`,
      `api.optica.example` y `www.w3.org`; validates AC-10
- [ ] `E2E_NavegadorSinRegla_Rechazado` — todos los proyectos de la configuración real tienen la regla; los
      prefijos son exactamente `{chrome, msedge}` sin la variable y `{chrome, msedge, chromium}` con ella; una lista
      con un proyecto sin la regla, o sin proyectos `msedge`, queda rechazada; validates AC-06, AC-10

**Completion criterion**
`npm run test:unidad` pasa con las 3 pruebas de este bloque y `npm run test:harness` pasa en Chrome y Edge con la regla
en los dos navegadores (104/104).

## Block 4 — Carpeta del usuario fuera del código en la CI

**Files**
- `.github/workflows/ci.yml` (modified) — el `env` del job suma `HOME: /tmp/perfil-ci`, y un paso nuevo, el primero
  del job, crea esa carpeta (`mkdir -p "$HOME"`) antes del checkout.
- `e2e/ci.mjs` (modified) — `revisarWorkflow` suma la regla `perfil`.
- `e2e/ci.test.mjs` (modified) — pruebas del bloque; `WORKFLOW_VALIDO` sigue igual al `ci.yml` real.

**Logic**
Implementa FR-03 en el entorno de GitHub Actions (D-20). El checkout queda en `/home/runner/work/<repo>`, dentro del
perfil `/home/runner`; el log de arranque de ASP.NET ("Content root path") y los stack traces de la compilación Debug
llevan esa ruta, y 9 pruebas que verifican que los logs y errores no contengan la carpeta del usuario
(`Environment.SpecialFolder.UserProfile`, que en Linux sale de `HOME`) fallan. Con `HOME` en `/tmp/perfil-ci` el perfil
queda fuera del código, que es la condición que esas pruebas suponen. El valor es fijo y no una expresión: el contexto
`runner` no está disponible en el `env` de un job. El paso que crea la carpeta va antes del checkout, porque
`actions/checkout` escribe la configuración global de git en `$HOME`. La caché de NuGet y la de npm pasan a esa
carpeta; se crean en cada corrida y se descartan con el runner. La regla `perfil` de `revisarWorkflow`: el `env` del job
(indentación de las claves del job) tiene `HOME:` con un valor (sin comillas) que empieza con `/tmp/`, sin `${{` y sin
`..`; un `HOME` en el `env` de un paso no cuenta. La posible fuga de "Content root path" en producción queda para un FIX
aparte (D-20).

**Input validation**
- Regla `perfil` sobre el texto del workflow: valor de `HOME` como string; vacío, relativo, con `${{`, con `..` o fuera de
  `/tmp/` → problema `perfil`.

**Error handling**
- Un workflow sin `HOME` en el `env` del job — `revisarWorkflow` lo rechaza con la regla `perfil`.
- Un `HOME` dentro de `/home/`, relativo, con una expresión `${{ … }}` o con `..` — rechazado con la regla `perfil`.
- La carpeta no se puede crear o una prueba de rutas del usuario sigue fallando en la corrida — el paso falla con su
  nombre y el pull request queda en rojo (FR-07).

**Required tests**
- [ ] `CI_WorkflowActual_SinProblemas` — sigue pasando con el `ci.yml` que tiene `HOME`; validates AC-03, AC-11
- [ ] `CI_SinPerfilFueraDelCodigo_Rechazado` — un workflow sin `HOME` en el `env` del job, o con `HOME` solo en el `env`
      de un paso, queda rechazado por `perfil`; validates AC-03
- [ ] `CI_PerfilInvalido_Rechazado` — `HOME: /home/runner`, `HOME: perfil`, `HOME: ${{ github.workspace }}/p` y
      `HOME: /tmp/../home/runner` quedan rechazados por `perfil`; `HOME: /tmp/perfil-ci` y `HOME: "/tmp/x"` no; validates
      AC-03
- [ ] `CI_PasoDeLaCorridaConError_Rechazado` — manual en el pull request: la corrida con el `ci.yml` de este bloque pasa
      "Pruebas de .NET" (1415/1415) y, si una prueba fallara, el paso falla con su nombre; se anota en el informe de
      pruebas; validates AC-07, AC-11

**Completion criterion**
`npm run test:unidad` pasa con las pruebas de este bloque y la corrida del workflow en el pull request de FEAT-005 pasa
el paso "Pruebas de .NET".

## Final verification
`validate_spec.py --tier FEATURE` pasa sobre este documento; en local pasan `dotnet test Optica.sln` (también con
`CI=true`), las pruebas, el lint y la compilación de la web y `npm run test:unidad && npm run test:harness` en `e2e/`;
y en el pull request de FEAT-005 hay una corrida en rojo provocada a propósito (commit temporal revertido) y la corrida
final en verde.
