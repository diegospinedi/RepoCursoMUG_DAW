# Test run FEAT-009

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de punta a
punta), sobre el commit `5804b58`, con los 8 bloques de la spec (datos del emisor en el dominio y la base; su Api; su
pantalla; QR de ARCA y generador del PDF; detalle y PDF de la factura; búsqueda de facturas; pantallas de facturas;
e2e). Las etapas corrieron una detrás de otra, sin compilar en paralelo con la cobertura web.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright con Chrome y Edge estables |
| Command | `dotnet build Optica.sln && dotnet test Optica.sln --no-build --collect:"XPlat Code Coverage" && (cd web && npm run lint && npm run test:lint && npm run test:coverage && npm run build) && (cd e2e && npm run test:unidad && npm run test:harness) && dotnet format Optica.sln --verify-no-changes` |
| Total | 4129 |
| Passed | 4129 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 96.9% |
| Branch coverage | 89.6% |
| Function coverage | 97.4% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean, 0 findings; `dotnet build Optica.sln` — 0 warnings, 0 errors; `npm run lint` en `web/` (oxlint `--max-warnings 0`) — clean, 0 findings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors; `npm run test:lint` — 8/8 |

Desglose: Optica.Domain.Tests 479, Optica.Application.Tests 533, Optica.Infrastructure.Tests 888, Optica.Api.Tests 663
(.NET 2563); web 1260 (26 archivos); e2e unidad 108; e2e 198 (99 en Chrome y 99 en Edge, 44 proyectos, con
`configuracion-emisor`, `facturas-pdf` y `facturas-busqueda` nuevos en los dos navegadores). Las 8 pruebas de
`npm run test:lint` verifican la configuración del lint y se informan en la fila Lint, fuera del total. La suite de la
Api tardó 1 min 15 s y el harness e2e 5,9 min.

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet con la ruta de cada archivo resuelta completa,
sin las migraciones ni los archivos generados: líneas 14314/14772, ramas 3309/3694, funciones 1586/1628. Web, por
separado: sentencias 98,95 %, ramas 97,06 %, funciones 100 %, líneas 98,90 %. Las pruebas de unidad de e2e y la e2e no
miden cobertura.

Cobertura .NET de los 33 archivos de producción del ticket (nuevos o modificados, sin migraciones): líneas 2997/3096
(96,8 %), ramas 790/902 (87,6 %), funciones 491/502 (97,8 %). Por archivo, por debajo del 80 % de ramas:
`ConfiguracionController.cs` 27/34, `FacturacionController.cs` 32/46, `ValidacionConfiguracion.cs` 21/29 (traducción
de infracciones a textos), `BuscarFacturas.cs` 9/12, `DocumentoFactura.cs` 41/56 (guardas de construcción),
`PaginaFacturas.cs` 2/4 y `OpticaDbContext.cs` 3/4. Ninguno baja del 80 % de líneas salvo `FuncionesSqlite.cs` (12/16,
con 8/8 ramas).

Fuera de la suite: el escaneo del QR con un teléfono (AC-12, D-16) y la versión anterior de Chrome y Edge (D-02)
quedan para la verificación manual del usuario; AC-16 se cubre en .NET porque el harness siempre corre en Simulado
(D-45).

## Failures
(none)

## Skips
(none)
