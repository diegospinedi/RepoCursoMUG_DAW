# Test run FEAT-003b

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de
punta a punta), sobre el commit `2fff802`, con los 5 bloques de la spec y la corrección D-29 (punto de miles en la
Cantidad, tras el lazo correctivo CODE→PLAN de `f3d2fcf`).

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright con Chrome y Edge estables |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm run test:coverage && npm run build) && (cd e2e && npm run test:unidad && npm run test:harness)` |
| Total | 2483 |
| Passed | 2483 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 95.9% |
| Branch coverage | 91.6% |
| Function coverage | 95.7% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean, 0 findings; `dotnet build Optica.sln` — 0 warnings, 0 errors; `npm run lint` en `web/` (oxlint `--max-warnings 0`) — clean, 0 findings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors; `npm run test:lint` — 8/8 |

Desglose: Optica.Domain.Tests 326, Optica.Application.Tests 324, Optica.Infrastructure.Tests 450,
Optica.Api.Tests 446 (.NET 1546); web 750; e2e unidad 71; e2e 116 (58 en Chrome y 58 en Edge, con
`presupuestos-calculos` nuevo en los dos navegadores).

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet sin las migraciones generadas
(líneas 6154/6414, ramas 1790/1955, funciones 831/868). Web, por separado: líneas 99.02%, ramas 97.23%,
funciones 100%, sentencias 99.06%. Las pruebas de unidad de e2e y la e2e no miden cobertura.

Intermitencias: ninguna en esta corrida. En el Block 5 el harness completo corrió 3 veces más 1 en el cierre del
bloque (116/116 cada una). En la revisión del Block 4 hubo una falla aislada de 1 prueba de 226 en `src/paginas`
(1 de 6 corridas, sin nombre registrado), que no se repitió en 8 corridas posteriores ni en esta.

## Failures
(none)

## Skips
(none)
