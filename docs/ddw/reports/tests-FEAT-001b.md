# Test run FEAT-001b

Corrida después del Block 3/7 (Contratos y persistencia del cambio de contraseña). El bloque solo
toca la solución .NET; `web/` y `e2e/` no cambiaron.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit 2.9.3 y coverlet (XPlat Code Coverage) |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage"` |
| Total | 523 |
| Passed | 523 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 94.5% |
| Branch coverage | 91.9% |
| Function coverage | 92.5% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings |

Desglose: Optica.Domain.Tests 76, Optica.Application.Tests 44, Optica.Infrastructure.Tests 282,
Optica.Api.Tests 121. Cobertura: unión de los cuatro informes de coverlet sin las migraciones
generadas (líneas 1662/1759, ramas 534/581, funciones 247/267).

## Failures
(none)

## Skips
(none)
