# Test run FEAT-001b

Corrida después del Block 2/7 (Ingreso con bloqueo en la Api): solución .NET completa y la suite de
punta a punta, que usa `POST /api/acceso/ingresar`. `web/` no cambió.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit 2.9.3 y coverlet; Playwright 1.64.1 con Chrome 156 y Edge 156 |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd e2e && npm run test:harness)` |
| Total | 502 |
| Passed | 502 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 93.5% |
| Branch coverage | 91.2% |
| Function coverage | 91.2% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings |

Desglose: Optica.Domain.Tests 70, Optica.Application.Tests 33, Optica.Infrastructure.Tests 273,
Optica.Api.Tests 120; e2e 6 (`chrome` 3, `msedge` 3). Cobertura de .NET: unión de los cuatro
informes de coverlet sin las migraciones generadas (líneas 1594/1704, ramas 526/577, funciones
237/260); la e2e no mide cobertura.

## Failures
(none)

## Skips
(none)
