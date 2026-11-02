# Test run FEAT-001a

Corrida después del Block 6/6 (Servir la SPA desde la Api y pruebas de punta a punta): suite
completa de la solución .NET, la SPA de `web/`, las pruebas unitarias de `e2e/` y la suite de punta
a punta en los proyectos `chrome` y `msedge`.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit 2.9.3 y coverlet; Vitest 5.0.5 con @vitest/coverage-v8; node:test (Node 22.23.2); Playwright 1.64.1 con Chrome 156.0.8078.79 y Edge 156.0.4314.58 |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm ci && npm run test:coverage) && (cd e2e && npm ci && npm run test:unidad && npm run test:harness)` |
| Total | 561 |
| Passed | 561 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 93.0% |
| Branch coverage | 91.0% |
| Function coverage | 89.8% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings; `npm run build` (`tsc --noEmit` estricto + `vite build`) — clean, 0 findings |

Desglose: .NET 468 (Domain 59, Application 27, Infrastructure 268, Api 114); web 62 (10 archivos);
e2e unidad 25 (`node:test`); e2e de punta a punta 6 (`chrome` 3, `msedge` 3).

Cobertura combinada de .NET y web por conteos: líneas 1707/1836 (.NET 1540/1668, web 167/168),
ramas 566/622 (.NET 474/528, web 92/94), funciones 247/275 (.NET 205/233, web 42/42). Las pruebas
unitarias de `e2e/` y la suite de Playwright no miden cobertura. En `web/` las ayudas de prueba
(`src/pruebas/**`) y `src/main.tsx` están excluidas de la cobertura.

## Failures
(none)

## Skips
(none)
