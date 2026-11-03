# Test run FEAT-001b

Corrida después del Block 1/7 (Política de bloqueo y consulta de rechazos). El bloque solo toca la
solución .NET; `web/` y `e2e/` no cambiaron y se corren en el cierre de CODE.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit 2.9.3 y coverlet (XPlat Code Coverage) |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage"` |
| Total | 484 |
| Passed | 484 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 91.8% |
| Branch coverage | 89.3% |
| Function coverage | 88.3% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings |

Desglose: Optica.Domain.Tests 70, Optica.Application.Tests 27, Optica.Infrastructure.Tests 273,
Optica.Api.Tests 114. Cobertura: unión de los cuatro informes de coverlet por archivo y línea, sin
las migraciones generadas (líneas 1541/1678, ramas 509/570, funciones 227/257).

## Failures
(none)

## Skips
(none)
