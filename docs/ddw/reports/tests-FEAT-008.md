# Test run FEAT-008

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de punta a
punta), sobre el commit `3d87ac2` más la corrección de formato descripta abajo, con los 8 bloques de la spec (dominio y
persistencia de pendientes, observaciones y descartes; puerto de ARCA y cliente WSFEv1; simulador; emisión con número
registrado; verificación y descarte; Api; pantallas; e2e).

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright con Chrome y Edge estables |
| Command | `dotnet build Optica.sln && dotnet test Optica.sln --no-build --collect:"XPlat Code Coverage" && (cd web && npm run lint && npm run test:lint && npm run test:coverage && npm run build) && (cd e2e && npm run test:unidad && npm run test:harness) && dotnet format Optica.sln --verify-no-changes` |
| Total | 3799 |
| Passed | 3799 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 96.9% |
| Branch coverage | 90.1% |
| Function coverage | 97.3% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean, 0 findings (después de corregir 3 avisos de espacios en 1 archivo, ver abajo); `dotnet build Optica.sln` — 0 warnings, 0 errors; `npm run lint` en `web/` (oxlint `--max-warnings 0`) — clean, 0 findings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors; `npm run test:lint` — 8/8 |

Desglose: Optica.Domain.Tests 447, Optica.Application.Tests 505, Optica.Infrastructure.Tests 834, Optica.Api.Tests 626
(.NET 2412); web 1119 (24 archivos); e2e unidad 98; e2e 170 (85 en Chrome y 85 en Edge, con los proyectos
`verificacion-*` nuevos en los dos navegadores). Las 8 pruebas de `npm run test:lint` verifican la configuración del
lint y se informan en la fila Lint, fuera del total. La suite de la Api tardó 3 min 57 s con el recolector de
cobertura y el harness e2e 9,6 min.

Formato: la primera corrida de `dotnet format --verify-no-changes` informó 3 avisos WHITESPACE en
`src/Optica.Infrastructure/Persistencia/EmisionPendienteRepository.cs:115-116` (patrón de propiedades partido en dos
líneas). Se corrigió con una edición de solo espacios y saltos de línea; la segunda corrida terminó sin avisos, la
compilación siguió sin advertencias y `Optica.Infrastructure.Tests` volvió a pasar 834/834.

Web: la primera corrida de `npm run test:coverage` se hizo en paralelo con la compilación .NET y mostró un resumen de
17 archivos y 947 pruebas, todas en verde; no se tomó como válida. Se repitió sola: 24 archivos, 1119/1119, igual que
`npx vitest run` y que la revisión del Block 7. Los números de este reporte son los de esa repetición.

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet con la ruta de cada archivo resuelta completa,
sin las migraciones ni los archivos generados (`*.g.cs`): líneas 12379/12780, ramas 2941/3265, funciones 1363/1401.
Web, por separado: líneas 98,82 %, ramas 96,69 %, funciones 100 %, sentencias 98,77 %. Las pruebas de unidad de e2e y
la e2e no miden cobertura.

Pendiente fuera de la suite: AC-13 (verificación contra ARCA homologación real) queda para la verificación manual del
usuario con el certificado de homologación (D-01).

## Failures
(none)

## Skips
(none)
