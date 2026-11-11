# Verification FEAT-005

Verificación del módulo completo (Blocks 1 a 4) contra `docs/ddw/prd/prd-FEAT-005.md` y
`docs/ddw/specs/spec-FEAT-005.md`, sobre `418a8c2` (código igual a `f73edaa`, el commit de la corrida en verde del PR
#11). Verificación cruzada hecha por un `ddw-module-verifier` que no escribió el código.

| Field | Value |
|---|---|
| Module | .github/workflows/ci.yml, e2e/ci.mjs, e2e/playwright.config.ts, Directory.Build.props |
| Line coverage | 99.71% |
| Branch coverage | 93.44% |
| Function coverage | 100% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `npm run lint` en `web/` (oxlint, `--max-warnings 0`) — clean, 0 findings, local y en la corrida 42390075541; `dotnet format Optica.sln --verify-no-changes` — clean |

Cobertura medida sobre `e2e/ci.mjs`, el código nuevo del ticket, con `node --test --experimental-test-coverage
ci.test.mjs`: líneas 99,71 % (sin cubrir: `ci.mjs:351` y `:353`, comillas dentro de un `on:` en forma de lista), ramas
93,44 %, funciones 100 %. El resto del ticket es configuración (`ci.yml`, `Directory.Build.props`, lockfiles,
`playwright.config.ts`), verificada por las reglas de `ci.mjs` y por las corridas reales.

## Acceptance criteria
- ✅ AC-01 — `on: pull_request` (ci.yml:7-8), `eventosDe` y regla `disparador` (ci.mjs:396, :436) → `CI_SoloPush_Rechazado`, `CI_WorkflowActual_SinProblemas`; dos corridas reales con evento pull_request (42385023126, 42390075541)
- ✅ AC-02 — `npm ci` (ci.yml:53-58), modo bloqueado de NuGet (Directory.Build.props:43-49), `proyectosSinLockfile`, `revisarPropsNuGet`, regla `instalacion` → `NuGet_CadaProyectoConLockfile`, `NuGet_LockfileFaltante_Rechazado`, `NuGet_SinModoBloqueado_Rechazado`, `CI_NpmInstall_Rechazado`; manual `NuGet_LockfileDesincronizado_Rechazado` (NU1004, tests-FEAT-005.md)
- ✅ AC-03 — `dotnet build`/`dotnet test` con `HOME` fuera del código (ci.yml:33, :60-63), `COMANDOS_DEL_PROYECTO`, regla `perfil` → `CI_WorkflowActual_SinProblemas`, `CI_ComandosEnAgents`, `CI_ComandoFaltante_Rechazado`, `CI_SinPerfilFueraDelCodigo_Rechazado`, `CI_PerfilInvalido_Rechazado`; corrida verde con .NET 1415/1415
- ✅ AC-04 — lint y prueba del lint (ci.yml:64-69) → `CI_ComandoFaltante_Rechazado` (sin `npm run lint`, o con el paso marcado como opcional); corrida verde: lint limpio, prueba del lint 8/8
- ✅ AC-05 — pruebas con cobertura y compilación de la web (ci.yml:70-75) → `CI_WorkflowActual_SinProblemas`, `CI_ComandoFaltante_Rechazado`; corrida verde: 602 pruebas
- ✅ AC-06 — e2e en Chrome y Edge (ci.yml:76-93, playwright.config.ts:37-45) → `E2E_NavegadorSinRegla_Rechazado`, `CI_ComandoFaltante_Rechazado`; corrida verde: 104 passed con Chrome 156.0.8078.79 y Edge 156.0.4314.58
- ✅ AC-07 — el job se corta en el paso con código de salida distinto de 0 y GitHub muestra su nombre; reglas `pasos-con-nombre` y `comandos` (ningún paso de la lista puede ser opcional) → `CI_PasosSinNombre_Rechazado`, `CI_ComandoFaltante_Rechazado`; manual `CI_PasoDeLaCorridaConError_Rechazado`: la corrida real 42385023126 se cortó en "Pruebas de .NET", salteó los pasos siguientes y marcó el check del PR (ver D-24 abajo)
- ✅ AC-08 — sin secretos y checkout sin credenciales (ci.yml:43), reglas `secretos`, `credenciales`, `disparador-inseguro`, `acciones` → `CI_ReferenciaASecretos_Rechazada`, `CI_CheckoutConCredenciales_Rechazado`, `CI_PullRequestTarget_Rechazado`, `CI_AccionNoPermitida_Rechazada`
- ✅ AC-09 — `permissions: contents: read` (ci.yml:11-12), regla `permisos` → `CI_PermisoDeEscritura_Rechazado`, `CI_PullRequestTarget_Rechazado`; el registro de la corrida confirma `Contents: read`
- ✅ AC-10 — regla de resolución en los tres navegadores (playwright.config.ts:37), `hostsExternos`, `proyectosSinRegla`, `navegadoresFaltantes` → `Pruebas_SinHostsExternos`, `Pruebas_HostExterno_Rechazado`, `E2E_NavegadorSinRegla_Rechazado`
- ✅ AC-11 — corrida 42390075541 sobre f73edaa en verde (18/18 pasos, 4m22s), check "Verificación" en SUCCESS en el PR #11

NFR-01: `timeout-minutes: 45` → `CI_SinTiempoMaximo_Rechazado`; la corrida real tardó 4m22s. NFR-02: `concurrency` con
`cancel-in-progress` → `CI_SinCancelacion_Rechazado` (verificado sobre el texto; ninguna corrida se canceló en la
práctica).

## Spec blocks
- ✅ Block 1 — 4/4 pruebas: `NuGet_CadaProyectoConLockfile`, `NuGet_LockfileFaltante_Rechazado`, `NuGet_SinModoBloqueado_Rechazado`, manual `NuGet_LockfileDesincronizado_Rechazado`
- ✅ Block 2 — 14/14 pruebas: `CI_WorkflowActual_SinProblemas`, `CI_ComandosEnAgents`, `CI_SoloPush_Rechazado`, `CI_PullRequestTarget_Rechazado`, `CI_ComandoFaltante_Rechazado`, `CI_PasosSinNombre_Rechazado`, `CI_NpmInstall_Rechazado`, `CI_ReferenciaASecretos_Rechazada`, `CI_CheckoutConCredenciales_Rechazado`, `CI_PermisoDeEscritura_Rechazado`, `CI_AccionNoPermitida_Rechazada`, `CI_SinTiempoMaximo_Rechazado`, `CI_SinCancelacion_Rechazado`, `CI_WorkflowVacio_Rechazado`; SHA de D-02 verificados con `git ls-remote`
- ✅ Block 3 — 3/3 pruebas: `Pruebas_SinHostsExternos`, `Pruebas_HostExterno_Rechazado`, `E2E_NavegadorSinRegla_Rechazado`; harness 104/104
- ✅ Block 4 — 4/4 pruebas: `CI_WorkflowActual_SinProblemas`, `CI_SinPerfilFueraDelCodigo_Rechazado`, `CI_PerfilInvalido_Rechazado`, manual `CI_PasoDeLaCorridaConError_Rechazado`

Evidencia TDD: registrada por bloque en `.ddw-work/review-block-1.md` a `review-block-4.md` (pruebas en rojo con sus
aserciones antes de implementar, en cada ronda). El verificador la reconstruyó además corriendo el `ci.test.mjs` de
cada bloque contra el commit anterior: todas las pruebas nuevas de cada bloque fallan sin su código.

## Tests
- ✅ Sad-path tests: cada función que recibe entrada tiene pruebas con entrada inválida o vacía — `CI_WorkflowVacio_Rechazado` (`revisarWorkflow` vacío), `NuGet_SinModoBloqueado_Rechazado` (props vacío), `CI_ComandosEnAgents` (`seccionStack` sin la sección Stack), `NuGet_LockfileFaltante_Rechazado` (arreglo vacío), `E2E_NavegadorSinRegla_Rechazado` (proyectos sin `use`/`args`, lista vacía), `Pruebas_HostExterno_Rechazado`
- ✅ Suite completa: 2200/2200 (tests-FEAT-005.md), corrida de la CI en verde

## Decisión registrada en esta verificación
D-24 — AC-07 y `CI_PasoDeLaCorridaConError_Rechazado` se dan por demostrados con la corrida real en rojo del PR #11
(42385023126) en lugar de la corrida "provocada a propósito" de D-11 y de la spec (Block 2, criterio de cierre, y
verificación final). La corrida real cumple todo lo que pide AC-07 y evita un commit temporal en el historial. Se le
propuso al usuario antes del push del 2026-11-07, que lo confirmó sin pedir la corrida provocada.

## Warnings
- W-VER-03 — `e2e/ci.test.mjs:60-66, :82`: las pruebas de NuGet y de hosts dependen de `git` y cuentan archivos sin versionar (`--others`); el 8 de proyectos está fijo. Fallan con fuentes bajados como .zip.
- W-VER-01 — `e2e/ci.mjs:613`: oxlint `no-control-regex` por `[^\x00-\x7f]`, intencional; `e2e/` no está en el alcance del lint del proyecto.
- `.github/workflows/ci.yml:36`: el comentario del paso "Crear la carpeta del perfil" dice que `actions/checkout` escribe la configuración global de git en `$HOME`; el registro de la corrida muestra que usa una carpeta temporal propia. El paso sigue siendo útil (las pruebas escriben en el perfil); el comentario es inexacto (ya sospechado en D-23).
- `docs/ddw/reports/tests-FEAT-005.md`: cita "corrida 42390071207/42390075541"; la 42390071207 es la de CodeQL, la de la CI es 42390075541.
- Sad paths sin prueba propia (todos se comportan bien a mano): entrada que no es texto en las funciones de texto, `on:` en lista con comillas (`ci.mjs:350-353`), `.csproj` en la raíz, `proyectosSinRegla([])`.
- `e2e/ci.test.mjs:831`: menciona "NFR-03", que es del PRD de FEAT-001, no de FEAT-005.
- Datos personales: el log "Content root path" puede llevar el nombre del usuario de Windows en producción (R-09, aceptado hasta el FIX de D-20, pendiente de abrir); los registros públicos de la CI solo muestran datos ficticios de prueba, uno con un dominio real (`ana@mail.com`, previo a este ticket); la configuración del repositorio frente a forks (D-19) sigue pendiente de decisión del usuario.

Result: PASSED
