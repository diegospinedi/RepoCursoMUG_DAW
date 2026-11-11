# Test run FEAT-005

Corrida del cierre de CODE: la suite completa del proyecto (.NET en modo CI, formato, lint y prueba del lint de la web,
web con cobertura, compilación, pruebas de unidad de e2e y e2e de punta a punta), sobre el commit `508fccb`, con los 4
bloques de la spec.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; oxlint y node:test (lint de la web); Vitest con cobertura v8 (web); node:test (e2e unidad, incluye `ci.test.mjs`); Playwright con Chrome y Edge estables |
| Command | `CI=true dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm run lint && npm run test:lint && npm run test:coverage && npm run build) && (cd e2e && npm run test:unidad && npm run test:harness)` |
| Total | 2200 |
| Passed | 2200 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 98.92% |
| Branch coverage | 96.87% |
| Function coverage | 100% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `npm run lint` en `web/` (oxlint, `--max-warnings 0`) — clean, 0 findings; `dotnet format Optica.sln --verify-no-changes` — clean; `npm run build` (tsc + vite) — clean, 0 errors |

Desglose: Optica.Domain.Tests 300, Optica.Application.Tests 254, Optica.Infrastructure.Tests 449,
Optica.Api.Tests 412 (.NET 1415, con `CI=true`: restauración en modo bloqueado con los 8 `packages.lock.json`);
web 602; prueba del lint 8; e2e unidad 71 (49 de `compilacion.test.mjs` y 22 de `ci.test.mjs`); e2e 104 (52 en
Chrome y 52 en Edge, con la regla de resolución en los dos).

Cobertura de la tabla: web (sentencias 98,97 %), sin cambios de código en este ticket. El código nuevo del ticket es
`e2e/ci.mjs`, cuyas reglas se ejercitan con las 22 pruebas de `ci.test.mjs`; node:test no mide cobertura en esta
corrida. .NET no tiene cambios de código en este ticket (solo `Directory.Build.props` y los archivos de bloqueo); la
suma directa de los cuatro informes de coverlet da 85,0 % de líneas y 71,7 % de ramas, una cifra no comparable con
las de tickets anteriores porque cada proyecto de pruebas también mide los ensamblados que no ejercita.

Verificaciones manuales de la spec:
- `NuGet_LockfileDesincronizado_Rechazado` (Block 1): con `CI=true`, cambiar
  `Microsoft.Extensions.Configuration.Abstractions` de 10.0.12 a 10.0.11 en `Optica.Infrastructure.csproj` sin
  restaurar hizo fallar `dotnet build Optica.sln` con NU1004; revertido (`git diff` vacío).
- SHA de D-02 (Block 2), con `git ls-remote https://github.com/actions/<repo> refs/tags/<versión>` el 2026-11-07:
  `actions/checkout` v7.0.1 → `3d3c42e5aac5ba805825da76410c181273ba90b1`, `actions/setup-dotnet` v6.1.0 →
  `0f4a354dd5547a28c70b7853dbd95d5b67886e73`, `actions/setup-node` v7.1.0 →
  `949feb2413d6458794dcd2491c4babbbce0c15c1`; coinciden los tres.
- Simulación del entorno de GitHub (Block 4): con `HOME` en la carpeta que contiene el repositorio fallan 10 de 50
  pruebas de `MarcaTests`, `RegistroTests` y `FabricaConexionTests`; con `HOME=/tmp/perfil-ci` pasan 50/50; con
  `HOME` nuevo, `CI=true`, build y test pasan 1415/1415.
- Corrida en el pull request #11 (AC-07, AC-11, `CI_PasoDeLaCorridaConError_Rechazado`): la primera corrida, sobre los
  Blocks 1 y 2, quedó en rojo en el paso "Pruebas de .NET" (9 de 1415, por el perfil dentro del código, D-20; corrida
  42385023126), con el pull request marcado como fallado por ese paso. La corrida sobre `f73edaa`, con los 4 bloques
  (corrida 42390071207/42390075541), quedó en verde en 4m22s: los 18 pasos en `success`, .NET 1415/1415, e2e 104
  passed con Google Chrome 156.0.8078.79 y Microsoft Edge 156.0.4314.58; CodeQL sin alertas abiertas.

`npm audit` en `web/`: 0 vulnerabilidades.

## Failures
(none)

## Skips
(none)
