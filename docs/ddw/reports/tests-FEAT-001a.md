# Test run FEAT-001a

Corrida después del Block 3/6 (Api de acceso, sesión y protección), suite completa de la solución.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit 2.9.3 y coverlet (XPlat Code Coverage) |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage"` |
| Total | 143 |
| Passed | 143 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 90.7% |
| Branch coverage | 87.3% |
| Function coverage | 83.6% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings |

Desglose por proyecto de pruebas: Optica.Domain.Tests 7, Optica.Application.Tests 20,
Optica.Infrastructure.Tests 34, Optica.Api.Tests 82. La cobertura es la unión de los cuatro
informes de coverlet (1154 líneas, 306 ramas, 171 métodos); Optica.Api, medida por sus propias
pruebas: 93.8% de líneas y 95.7% de ramas.

## Failures
(none)

## Skips
(none)
