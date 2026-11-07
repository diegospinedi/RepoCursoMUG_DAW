# Test run FEAT-004

Corrida del cierre de CODE: la suite completa del proyecto (.NET, lint y prueba del lint de la web, web con cobertura,
compilación, pruebas de unidad de e2e y e2e de punta a punta), sobre el commit `0447b22`, con los 2 bloques de la spec.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; oxlint 1.92.0 y node:test (lint de la web); Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright con Chrome y Edge estables |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm run lint && npm run test:lint && npm run test:coverage && npm run build) && (cd e2e && npm run test:unidad && npm run test:harness)` |
| Total | 2175 |
| Passed | 2175 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 98.92% |
| Branch coverage | 96.87% |
| Function coverage | 100% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `npm run lint` en `web/` (oxlint, `--max-warnings 0`) — clean, 0 findings; `dotnet format Optica.sln --verify-no-changes` — clean; `npm run build` (tsc + vite) — clean, 0 errors |

Desglose: Optica.Domain.Tests 300, Optica.Application.Tests 254, Optica.Infrastructure.Tests 449,
Optica.Api.Tests 412 (.NET 1415); web 602 (igual que antes del ticket, AC-05); prueba del lint 8; e2e unidad 49;
e2e 104.

Cobertura de la tabla: web, que es donde cambió el código de este ticket (sentencias 98,97 %). .NET sin cambios en
este ticket: líneas 95,5 %, ramas 91,7 %, funciones 95,8 %. `Presupuesto.tsx` queda en 93,18 % de líneas y 94,87 % de
ramas por la rama de cambio de número sin prueba propia, aceptada por el usuario (D-07). La prueba del lint y la e2e no
miden cobertura.

`npm audit` en `web/`: 0 vulnerabilidades.

## Failures
(none)

## Skips
(none)
