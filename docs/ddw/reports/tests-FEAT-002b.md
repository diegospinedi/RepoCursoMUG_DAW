# Test run FEAT-002b

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de
punta a punta), sobre el commit `7b08dde`, con los 6 bloques de la spec y la corrección del e2e de Edge (D-30).

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright 1.64.1 con Chrome y Edge estables |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm run test:coverage) && (cd e2e && npm run test:unidad && npm run test:harness)` |
| Total | 1658 |
| Passed | 1658 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 95.0% |
| Branch coverage | 91.5% |
| Function coverage | 94.6% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors |

Desglose: Optica.Domain.Tests 243, Optica.Application.Tests 170, Optica.Infrastructure.Tests 378,
Optica.Api.Tests 325 (.NET 1116); web 433; e2e unidad 49; e2e 60 (`chrome-acceso` 5, `chrome-articulos` 11,
`chrome-articulos-edicion` 7, `chrome-configuracion` 7, `msedge-acceso` 5, `msedge-articulos` 11,
`msedge-articulos-edicion` 7, `msedge-configuracion` 7).

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet sin las migraciones generadas
(líneas 3570/3757, ramas 1267/1385, funciones 565/597). Código nuevo o modificado de .NET en esta rama (20
archivos de `src/` sin migraciones): líneas 94.1%, ramas 87.4%, funciones 91.7%. Web, por separado: líneas
99.24%, ramas 96.86%, funciones 100%, sentencias 99.27%. Las pruebas de unidad de e2e y la e2e no miden
cobertura.

msedge: con `--host-resolver-rules` en los proyectos `msedge-*` (D-30) la suite pasó 60/60 y los proyectos de Edge
30/30 en tres corridas seguidas; la intermitencia de "Cargando…" (W-VER-03 de FEAT-001a y FEAT-001b) no apareció.

## Failures
(none)

## Skips
(none)
