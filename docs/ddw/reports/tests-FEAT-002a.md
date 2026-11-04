# Test run FEAT-002a

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de
punta a punta), sobre el commit `2fcbdba`, con los 6 bloques de la spec.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit 2.9.3 y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright 1.64.1 con Chrome y Edge estables |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm run test:coverage) && (cd e2e && npm run test:unidad && npm run test:harness)` |
| Total | 1380 |
| Passed | 1380 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 94.9% |
| Branch coverage | 92.5% |
| Function coverage | 94.2% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors |

Desglose: Optica.Domain.Tests 214, Optica.Application.Tests 135, Optica.Infrastructure.Tests 347,
Optica.Api.Tests 259 (.NET 955); web 330; e2e unidad 49; e2e 46 (`chrome-acceso` 5, `chrome-articulos` 11,
`chrome-configuracion` 7, `msedge-acceso` 5, `msedge-articulos` 11, `msedge-configuracion` 7).

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet sin las migraciones generadas
(líneas 2975/3136, ramas 1017/1099, funciones 455/483). Código nuevo o modificado de .NET en esta rama (17
archivos de `src/` sin migraciones): líneas 94.2%, ramas 90.6%, funciones 96.5%. Web, por separado: líneas
99.28%, ramas 97.26%, funciones 100%, sentencias 99.31%. Las pruebas de unidad de e2e y la e2e no miden
cobertura.

msedge: esta corrida pasó 23/23; la intermitencia conocida de `acceso.spec.ts` (W-VER-03 de FEAT-001a y
FEAT-001b) no apareció.

## Failures
(none)

## Skips
(none)
