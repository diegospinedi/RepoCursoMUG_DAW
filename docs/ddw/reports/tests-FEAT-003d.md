# Test run FEAT-003d

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de
punta a punta), sobre el commit `9ce3c3c`, con los 3 bloques de la spec (tiempo de espera explícito y dos altas
simultáneas en integración, dos sesiones por la Api y volumen de 10.000 presupuestos, dos sesiones en el navegador).

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright con Chrome y Edge estables |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm run test:coverage && npm run build) && (cd e2e && npm run test:unidad && npm run test:harness)` |
| Total | 2701 |
| Passed | 2701 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 96.8% |
| Branch coverage | 93.3% |
| Function coverage | 96.3% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean, 0 findings; `dotnet build Optica.sln` — 0 warnings, 0 errors; `npm run lint` en `web/` (oxlint `--max-warnings 0`) — clean, 0 findings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors; `npm run test:lint` — 8/8 |

Desglose: Optica.Domain.Tests 334, Optica.Application.Tests 358, Optica.Infrastructure.Tests 489,
Optica.Api.Tests 490 (.NET 1671); web 825; e2e unidad 71; e2e 134 (67 en Chrome y 67 en Edge, 18 proyectos, con
`presupuestos-concurrencia` nuevo en los dos navegadores).

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet con la ruta de cada archivo resuelta completa
(sin contar dos veces un archivo que cada informe nombra con otra ruta relativa, corrección señalada en el verify de
FEAT-003c) y sin las migraciones generadas: líneas 4615/4766, ramas 1617/1733, funciones 726/754. Web, por separado:
líneas 99.11%, ramas 96.85%, funciones 100%, sentencias 99.15% (este ticket no toca la web). Las pruebas de unidad de
e2e y la e2e no miden cobertura. El único cambio de producción del ticket es `DefaultTimeout = 30` en
`FabricaConexion`.

Duración: las pruebas de volumen (`VolumenPresupuestosTests`, 5 pruebas con 10.000 presupuestos sembrados) suman entre
0,6 y 1 s cada una, unos 7 s en total; cada siembra tarda entre 237 y 310 ms. `Optica.Api.Tests` tardó 16 min 55 s en
esta corrida (WSL sobre `/mnt/c`, igual que en FEAT-003c: 16 min 31 s); el harness e2e, 2,8 min. El tope de 45 minutos
de la CI no cambia.

Intermitencias: ninguna en esta corrida. `presupuestos-concurrencia` pasó 10/10 en Edge y 10/10 en Chrome con
`--repeat-each=5` antes del commit del Block 3, y el harness completo pasó en 4 corridas (implementador, verificador y
correcciones) más esta.

## Failures
(none)

## Skips
(none)
