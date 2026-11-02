# Test run FEAT-001a

Corrida después del Block 4/6 (Marca instalada y marca de ejemplo), suite completa de la solución.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit 2.9.3 y coverlet (XPlat Code Coverage) |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage"` |
| Total | 445 |
| Passed | 445 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 91.9% |
| Branch coverage | 89.2% |
| Function coverage | 87.8% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings |

Desglose por proyecto de pruebas: Optica.Domain.Tests 59, Optica.Application.Tests 27,
Optica.Infrastructure.Tests 265, Optica.Api.Tests 94. La cobertura es la unión de los cuatro
informes de coverlet. En esta corrida (WSL) se ejecutaron también las pruebas condicionales de
enlaces simbólicos y de archivos con nombres no válidos en Windows; en Windows sin privilegios esas
pruebas quedan omitidas de forma visible.

## Failures
(none)

## Skips
(none)
