# Test run FEAT-003c

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de
punta a punta), sobre el commit `558d5ab`, con los 5 bloques de la spec (dominio, persistencia y triggers,
Api, web y e2e).

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright con Chrome y Edge estables |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm run test:coverage && npm run build) && (cd e2e && npm run test:unidad && npm run test:harness)` |
| Total | 2675 |
| Passed | 2675 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 96.3% |
| Branch coverage | 91.5% |
| Function coverage | 95.8% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean, 0 findings; `dotnet build Optica.sln` — 0 warnings, 0 errors; `npm run lint` en `web/` (oxlint `--max-warnings 0`) — clean, 0 findings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors; `npm run test:lint` — 8/8 |

Desglose: Optica.Domain.Tests 334, Optica.Application.Tests 358, Optica.Infrastructure.Tests 474,
Optica.Api.Tests 483 (.NET 1649); web 825; e2e unidad 71; e2e 130 (65 en Chrome y 65 en Edge, con
`presupuestos-edicion` nuevo en los dos navegadores).

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet sin las migraciones generadas
(líneas 7080/7355, ramas 2006/2193, funciones 923/963). Web, por separado: líneas 99.11%, ramas 96.85%,
funciones 100%, sentencias 99.15%. Las pruebas de unidad de e2e y la e2e no miden cobertura.

Intermitencias: ninguna en esta corrida. Observación: `Optica.Api.Tests` tardó 16 min 31 s en esta corrida
(WSL sobre `/mnt/c`); no hubo fallas ni reintentos.

## Failures
(none)

## Skips
(none)
