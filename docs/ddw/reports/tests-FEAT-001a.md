# Test run FEAT-001a

Corrida después del Block 5/6 (Interfaz web), suite completa: solución .NET y SPA de `web/`.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit 2.9.3 y coverlet; Vitest 5.0.5 con @vitest/coverage-v8 (Node 22.23.2) |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm ci && npm run test:coverage)` |
| Total | 507 |
| Passed | 507 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 92.6% |
| Branch coverage | 90.6% |
| Function coverage | 89.7% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings; `npm run build` (`tsc --noEmit` estricto + `vite build`) — clean, 0 findings |

Desglose: .NET 445 (Domain 59, Application 27, Infrastructure 265, Api 94) y web 62 (10 archivos).
Cobertura combinada por conteos: líneas 1604/1732 (.NET 1437/1564, web 167/168), ramas 531/586
(.NET 439/492, web 92/94), funciones 244/272 (.NET 202/230, web 42/42). En `web/` las ayudas de
prueba (`src/pruebas/**`) y `src/main.tsx` están excluidas de la cobertura.

## Failures
(none)

## Skips
(none)
