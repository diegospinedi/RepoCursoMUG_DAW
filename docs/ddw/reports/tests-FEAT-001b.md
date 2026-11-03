# Test run FEAT-001b

Corrida del re-cierre de CODE, después del loop correctivo de VERIFY (WARN-2:
`ConflictoAlConfirmarException` sin causa): la suite completa del proyecto (.NET, web, pruebas de unidad
de e2e y e2e de punta a punta), sobre el commit `04506ca`. Reemplaza la corrida del primer cierre (759/759
sobre `e5a035b`); la diferencia es la prueba de regresión
`ConflictoAlConfirmar_NoAdmiteCausa_NingunConstructorRecibeExcepcion`.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit 2.9.3 y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright 1.64.1 con Chrome 156 y Edge 156 |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm run test:coverage) && (cd e2e && npm run test:unidad && npm run test:harness)` |
| Total | 760 |
| Passed | 760 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 94.3% |
| Branch coverage | 92.2% |
| Function coverage | 93.9% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors |

Desglose: Optica.Domain.Tests 76, Optica.Application.Tests 65, Optica.Infrastructure.Tests 299,
Optica.Api.Tests 194 (.NET 634); web 91; e2e unidad 25; e2e 10 (`chrome` 5, `msedge` 5).

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet sin las migraciones generadas
(líneas 2003/2125, ramas 616/668, funciones 290/309). Cobertura de la web, por separado: líneas
99.56%, ramas 98.5%, funciones 100%, sentencias 99.57%. Las pruebas de unidad de e2e y la e2e no miden
cobertura.

msedge: esta corrida pasó 5/5. La intermitencia W-VER-03 quedó aceptada por el usuario para
FEAT-001b en VERIFY, ronda 1.

## Failures
(none)

## Skips
(none)
