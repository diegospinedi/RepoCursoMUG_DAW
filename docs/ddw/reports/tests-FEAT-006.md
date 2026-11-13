# Test run FEAT-006

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de punta a
punta), sobre el commit `fa69eff`, con los 6 bloques de la spec (filtro y consulta de la búsqueda, caso de uso y
endpoint de la búsqueda, pantalla de búsqueda, generador del PDF, caso de uso y endpoint del PDF, botón Descargar PDF y
e2e) y la guía de instalación con ADR-020.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright con Chrome y Edge estables |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm run test:coverage && npm run build && npm run lint && npm run test:lint) && (cd e2e && npm run test:unidad && npm run test:harness) && dotnet format Optica.sln --verify-no-changes` |
| Total | 2907 |
| Passed | 2907 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 97.0% |
| Branch coverage | 93.7% |
| Function coverage | 96.7% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean, 0 findings; `dotnet build Optica.sln` — 0 warnings, 0 errors; `npm run lint` en `web/` (oxlint `--max-warnings 0`) — clean, 0 findings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors; `npm run test:lint` — 8/8 |

Desglose: Optica.Domain.Tests 343, Optica.Application.Tests 393, Optica.Infrastructure.Tests 527, Optica.Api.Tests 526
(.NET 1789); web 903; e2e unidad 71; e2e 144 (72 en Chrome y 72 en Edge, 22 proyectos, con `presupuestos-busqueda` y
`presupuestos-pdf` nuevos en los dos navegadores). Las 8 pruebas de `npm run test:lint` verifican la configuración del
lint y se informan en la fila Lint, fuera del total. La suite de la Api tardó 1 min 11 s y el harness e2e 2,6 min.

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet con la ruta de cada archivo resuelta completa y
sin las migraciones generadas: líneas 5092/5248, ramas 1773/1893, funciones 808/836. Web, por separado: líneas 99,19 %,
ramas 96,94 %, funciones 100 %, sentencias 99,23 %. Las pruebas de unidad de e2e y la e2e no miden cobertura.

Cobertura .NET de los archivos de producción del ticket (nuevos o modificados), con el mismo cálculo: líneas
1092/1102 (99,1 %), ramas 446/471 (94,7 %), funciones 160/160 (100 %). Por archivo, por debajo del 80 % de ramas:
`BuscarPresupuestos.cs` 5/8 (62,5 %; las ramas sin cubrir son los `?? ` de lectura de fechas que la validación previa
ya garantiza) y `ConfiguracionInfraestructura.cs` 2/4 (50 %, ramas de FEAT-001 que dependen del sistema operativo; la
línea del registro del generador está cubierta). `ResumenPresupuesto.cs` no aparece en los informes: solo tiene
propiedades automáticas y un constructor que coverlet no instrumenta por separado. Web del ticket: `BuscarPresupuestos.tsx`
100 %, `cliente.ts` 100 % de líneas y 99,57 % de ramas, `Presupuesto.tsx` 98,43 % de líneas y 94,56 % de ramas.

Rendimiento: AC-16 por la Api sobre 10.000 presupuestos sembrados, 20 búsquedas entre 6,6 y 68,4 ms (D-03, D-26);
NFR-01 en el navegador, unos 0,6 s de la pulsación de Buscar a la grilla.

## Failures
(none)

## Skips
(none)
