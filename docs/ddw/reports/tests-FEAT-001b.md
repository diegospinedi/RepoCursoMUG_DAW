# Test run FEAT-001b

Corrida después del Block 4/7 (Cambio de contraseña: caso de uso y Api): solución .NET completa y la
suite de punta a punta. `web/` no cambió.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit 2.9.3 y coverlet; Playwright 1.64.1 con Chrome 156 y Edge 156 |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd e2e && npm run test:harness)` |
| Total | 581 |
| Passed | 581 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 94.7% |
| Branch coverage | 92.1% |
| Function coverage | 92.9% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings |

Desglose: Optica.Domain.Tests 76, Optica.Application.Tests 58, Optica.Infrastructure.Tests 282,
Optica.Api.Tests 159; e2e 6 (`chrome` 3, `msedge` 3). Cobertura de .NET: unión de los cuatro
informes de coverlet sin las migraciones generadas (líneas 1744/1841, ramas 560/608, funciones
262/282); la e2e no mide cobertura.

## Failures
(none)

## Skips
(none)
