# Test run FEAT-001c

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de
punta a punta), sobre el commit `cd19ad8`, con los 6 bloques de la spec y la corrección de Block 5
(`caf4657`).

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit 2.9.3 y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright 1.64.1 con Chrome y Edge estables |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm run test:coverage) && (cd e2e && npm run test:unidad && npm run test:harness)` |
| Total | 1003 |
| Passed | 1003 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 94.5% |
| Branch coverage | 92.2% |
| Function coverage | 93.7% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors |

Desglose: Optica.Domain.Tests 122, Optica.Application.Tests 94, Optica.Infrastructure.Tests 323,
Optica.Api.Tests 217 (.NET 756); web 174; e2e unidad 49; e2e 24 (`chrome-acceso` 5,
`chrome-configuracion` 7, `msedge-acceso` 5, `msedge-configuracion` 7).

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet sin las migraciones generadas
(líneas 2429/2571, ramas 792/859, funciones 354/378). Cobertura de la web, por separado: líneas 99.26%,
ramas 97.23%, funciones 100%, sentencias 99.28%. Las pruebas de unidad de e2e y la e2e no miden
cobertura.

msedge: esta corrida pasó 12/12. Durante el Block 6, una corrida anterior tuvo 1 falla intermitente en
`[msedge-acceso] acceso.spec.ts › primera vez define contraseña e ingresa` (no apareció "Sesión iniciada"
en 5 s); no se repitió en las cuatro corridas siguientes. Es la intermitencia W-VER-03 aceptada en
FEAT-001a y FEAT-001b; este ticket no toca `acceso.spec.ts`.

## Failures
(none)

## Skips
(none)
