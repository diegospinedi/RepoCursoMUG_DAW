# Test run FEAT-003a

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de
punta a punta), sobre el commit `7f890d6`, con los 7 bloques de la spec (el Block 7 se agregó en el lazo correctivo de VERIFY,
D-37). Reemplaza la corrida del primer cierre de CODE sobre `e298f0e` (2167/2167).

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright con Chrome y Edge estables |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm run test:coverage && npm run build) && (cd e2e && npm run test:unidad && npm run test:harness)` |
| Total | 2170 |
| Passed | 2170 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 95.5% |
| Branch coverage | 91.7% |
| Function coverage | 95.8% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean, 0 findings; `dotnet build Optica.sln` — 0 warnings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors |

Desglose: Optica.Domain.Tests 300, Optica.Application.Tests 254, Optica.Infrastructure.Tests 449,
Optica.Api.Tests 412 (.NET 1415); web 602; e2e unidad 49; e2e 104 (los 5 archivos anteriores en Chrome y
Edge más `chrome-presupuestos-alta` 18 y `msedge-presupuestos-alta` 18).

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet sin las migraciones generadas
(líneas 4854/5082, ramas 1743/1900, funciones 791/826). Código nuevo o modificado de .NET en esta rama (19
archivos de `src/`, sin migraciones): líneas 96.1%, ramas 91.0%, funciones 98.9%. Web, por separado: líneas
99.1%, ramas 96.98%, funciones 100%, sentencias 99.13%. Las pruebas de unidad de e2e y la e2e no miden
cobertura.

Intermitencias: ninguna en esta corrida. En el Block 6 el archivo nuevo corrió solo 3 veces (36/36 cada una)
y el harness completo 2 veces (104/104); en el cierre del lazo correctivo, el harness completo otra vez 104/104.

## Failures
(none)

## Skips
(none)
